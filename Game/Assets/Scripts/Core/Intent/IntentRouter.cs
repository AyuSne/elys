using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ElysFarm.Core
{
    /// <summary>
    /// 把玩家的一句话分到三个出口：农事指令、追问、闲聊。
    /// 这是唯一允许把「说的话」变成「做的事」的入口（见规格）：
    /// 模型只提供意图草稿，地块、品种与工步还要在这里按白名单重新解析并通过动作校验。
    /// 「怎么说」另走表达层（<see cref="ExpressionPrompt"/>），失败不编答案。
    /// 模型不可用、超时或输出不可用时，退到 <see cref="LocalIntentParser"/>，绝不阻塞调用方。
    /// </summary>
    public sealed class IntentRouter
    {
        /// <summary>对话 / 追问 / 闲聊的硬超时（秒）。</summary>
        public const float ChatDeadlineSeconds = ChatRequest.DefaultTimeoutSeconds;

        /// <summary>农事指令（规划级）的硬超时（秒）。</summary>
        public const float PlanningDeadlineSeconds = 15f;

        /// <summary>一次记忆检索取向量的硬超时（秒）；超时退回关键词相似度。</summary>
        public const float EmbeddingDeadlineSeconds = 2f;

        /// <summary>闲聊的表达层给不出可用台词时只说这一句故障台词，不生成假回答。</summary>
        public const string ChatFailureLine = "我这会儿没听清，你再说一遍？";

        /// <summary>表达层最多尝试的调用次数：空响应重试一次。</summary>
        const int ExpressionAttempts = 2;

        /// <summary>剩余预算低于这个值就不再发表达层请求，直接降级。</summary>
        const float MinCallSeconds = 0.5f;

        readonly IModelClient client;

        /// <summary>client 为 null 时直接走本地解析（离线演示与测试）。</summary>
        public IntentRouter(IModelClient client = null)
        {
            this.client = client;
        }

        /// <summary>是否配置了模型；HUD 用它显示在线 / 离线。</summary>
        public bool HasModel => client != null;

        /// <summary>最近一次模型调用的摘要；没有调用时为离线。</summary>
        public ModelCallInfo LastCall { get; private set; } = ModelCallInfo.None;

        /// <summary>
        /// 解析并处理一句话。无论模型是否可用都会返回结果；
        /// 未显式指定 deadline 时，农事指令用规划级 15 秒，追问与闲聊用对话级 6 秒，
        /// 取记忆向量与闲聊的表达层调用都占用这一次对话的预算。
        /// <paramref name="dialogue"/> 给表达层提供口吻与最近几轮；它由演示层构造，路由器只读。
        /// 超时、网络错误与非法 JSON 都退到本地解析，调用方不会抛出异常。
        /// </summary>
        public async Task<IntentResult> RouteAsync(
            string text,
            FarmWorld world,
            Npc npc,
            float? deadlineSeconds = null,
            float? embeddingDeadlineSeconds = null,
            DialogueContext dialogue = null)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (npc == null)
            {
                throw new ArgumentNullException(nameof(npc));
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return Finish(IntentResult.Refuse("你想让我做什么？", false, ModelCallInfo.None));
            }

            // 品种白名单先于模型生效：模型可能把「种月亮」改写成一句合法农活，
            // 但白名单外的品种必须由本地规则明确拒绝，不能绕过。
            if (LocalIntentParser.TryFindUnknownCrop(text, world, npc, out var unknownCrop))
            {
                return Finish(IntentResult.Refuse(
                    ActionValidator.UnknownSpeciesReason(unknownCrop),
                    false,
                    new ModelCallInfo(null, 0d, 0, 0, ModelCallOutcome.Guard)));
            }

            var deadline = deadlineSeconds
                ?? (LocalIntentParser.IsFarmRequest(text, world, npc) ? PlanningDeadlineSeconds : ChatDeadlineSeconds);

            if (client == null || deadline <= 0f)
            {
                var offlineRoute = new RouteContext(
                    text,
                    world,
                    npc,
                    world.RecallForAnswer(npc.Id, text),
                    dialogue,
                    clock: null,
                    deadline: 0f);
                return Finish(await ResolveAsync(
                    offlineRoute,
                    LocalIntentParser.Parse(text, world, npc),
                    usedFallback: true,
                    ModelCallInfo.None));
            }

            // 追问与闲聊都要先检索记忆：向量可用时用余弦，断网或超时自动退回关键词。
            // 取向量也占用这一次对话的硬超时预算，不能变成 6 秒之外再加 2 秒。
            var routeClock = Stopwatch.StartNew();
            var cueEmbedding = await MemoryContext.TryEmbedCueAsync(
                client,
                text,
                world,
                npc,
                Math.Min(deadline, embeddingDeadlineSeconds ?? EmbeddingDeadlineSeconds));
            var remaining = Math.Max(0.01f, deadline - (float)routeClock.Elapsed.TotalSeconds);
            var memories = world.RecallForAnswer(npc.Id, text, cueEmbedding);
            var route = new RouteContext(text, world, npc, memories, dialogue, routeClock, deadline);

            ChatRequest request;
            try
            {
                request = IntentPrompt.Build(world, npc, text, memories);
            }
            catch (Exception ex)
            {
                return Finish(await ResolveFallback(
                    route,
                    FailedCall(0d, ModelCallOutcome.Error, ex.Message)));
            }

            var attempt = await CallAsync(request, remaining);
            if (!attempt.Succeeded)
            {
                return Finish(await ResolveFallback(route, attempt.Call));
            }

            if (!ModelIntentParser.TryParse(attempt.Content, out var draft, out _))
            {
                return Finish(await ResolveFallback(route, Unusable(attempt.Call)));
            }

            return Finish(await ResolveAsync(route, draft, usedFallback: false, attempt.Call));
        }

        IntentResult Finish(IntentResult result)
        {
            LastCall = result.Call;
            return result;
        }

        /// <summary>本地解析接管一次：分类没拿到可用结果时用它，闲聊因此直接走降级。</summary>
        Task<IntentResult> ResolveFallback(RouteContext route, ModelCallInfo call)
        {
            return ResolveAsync(
                route,
                LocalIntentParser.Parse(route.Text, route.World, route.Npc),
                usedFallback: true,
                call);
        }

        /// <summary>
        /// 把一份草稿落到三个出口。出口的最终裁决权在本地：农活请求不能被模型当成闲聊吞掉，
        /// 纯闲聊也不能被模型凭空变成一条农活；追问与闲聊之间同样以本地规则为准。
        /// 闲聊与追问在此之后各走一次表达层调用。
        /// </summary>
        async Task<IntentResult> ResolveAsync(
            RouteContext route,
            IntentDraft draft,
            bool usedFallback,
            ModelCallInfo call)
        {
            if (draft == null)
            {
                return IntentResult.Refuse("我没听明白。", usedFallback, call);
            }

            // 出口以玩家原话为准，模型只负责理解细节（地块、品种、措辞）。
            // 不是农活请求时，追问还是闲聊也由本地规则定（带问句标记的是追问）：
            // 否则模型把「你在干什么」判成闲聊，回答既没有事实注入，又会把话写回记忆。
            var local = LocalIntentParser.Parse(route.Text, route.World, route.Npc);
            if (local.Outlet != draft.Outlet)
            {
                // 出口可以改，importance 仍是分类调用给的分数：闲聊被误判成农活时也要留得住。
                local.SetImportance(draft.Importance);
                draft = local;
                call = Corrected(call);
            }

            if (draft.Outlet == IntentOutlet.Chat)
            {
                return await ChatAsync(route, draft, call);
            }

            if (draft.Outlet == IntentOutlet.Query)
            {
                return await QueryAsync(route, call);
            }

            return ResolveCommand(route, draft, usedFallback, call);
        }

        /// <summary>
        /// 闲聊的表达层：分类之后单独一次调用，只负责说法。
        /// 台词由模型给；模型不可用、空响应重试后仍失败、或返回 JSON 时只说故障台词，
        /// 并把结果标成本地降级，HUD 据此标出「本地模板」。
        /// 分类调用没拿到可用结果时照样发这一次：出口已由本地规则定为闲聊，回话仍可以问模型。
        /// importance 仍用分类调用给出的分数写回记忆，地块、时钟与背包不动。
        /// </summary>
        async Task<IntentResult> ChatAsync(
            RouteContext route,
            IntentDraft draft,
            ModelCallInfo classification)
        {
            route.World.RememberChat(
                route.Npc.Id,
                route.Text,
                draft.Importance > 0 ? draft.Importance : MemoryContext.PlayerChatImportance);

            return await ExpressAsync(
                route,
                IntentOutlet.Chat,
                classification,
                () => ExpressionPrompt.BuildChat(
                    route.World,
                    route.Npc,
                    route.Text,
                    route.Memories,
                    route.Dialogue),
                accepts: null,
                localReply: ChatFailureLine);
        }

        /// <summary>
        /// 追问的表达层：事实（地块、工步、体力、命中的记忆）先由本地算出来，模型只换一种说法。
        /// 台词改掉事实、空响应或返回 JSON 时重试一次，仍不成立就回退事实串并标成本地降级；
        /// 追问只读，不写记忆、不产生工单、不改地块与背包。
        /// </summary>
        async Task<IntentResult> QueryAsync(RouteContext route, ModelCallInfo classification)
        {
            var facts = QueryFacts.Build(route.World, route.Npc, route.Text, route.Memories);
            return await ExpressAsync(
                route,
                IntentOutlet.Query,
                classification,
                () => ExpressionPrompt.BuildQuery(
                    route.World,
                    route.Npc,
                    route.Text,
                    facts,
                    route.Dialogue),
                accepts: facts.Accepts,
                localReply: facts.Reply);
        }

        /// <summary>
        /// 表达层的公共驱动：拿剩下的预算换一句台词，逐次重试直到可接受或用完次数。
        /// 模型不可用、没预算、空响应与 JSON 冒充台词都退到 <paramref name="localReply"/>，
        /// 结果标成本地降级；<paramref name="accepts"/> 为 null 表示模型说了就收（闲聊）。
        /// 分类调用纠正过出口时，ADR-0004 要求结果仍标 Correction 并保留那次调用的模型名、耗时与 token。
        /// </summary>
        async Task<IntentResult> ExpressAsync(
            RouteContext route,
            IntentOutlet outlet,
            ModelCallInfo classification,
            Func<ChatRequest> build,
            Func<string, bool> accepts,
            string localReply)
        {
            var reply = localReply;
            var call = classification;
            var localTemplate = true;

            var budget = route.Remaining;
            if (client != null && budget > MinCallSeconds)
            {
                var clock = Stopwatch.StartNew();
                for (var attempt = 0; attempt < ExpressionAttempts; attempt++)
                {
                    var left = budget - (float)clock.Elapsed.TotalSeconds;
                    if (left <= MinCallSeconds)
                    {
                        break;
                    }

                    var expression = await CallAsync(build(), left);
                    call = expression.Call;

                    var spoken = expression.Succeeded ? ExpressionPrompt.CleanReply(expression.Content) : null;
                    if (spoken != null && (accepts == null || accepts(spoken)))
                    {
                        reply = spoken;
                        localTemplate = false;
                        break;
                    }

                    if (expression.Succeeded)
                    {
                        call = Unusable(expression.Call);
                    }
                }
            }

            if (classification.Outcome == ModelCallOutcome.Correction)
            {
                // ADR-0004：本地出口纠正过模型的分类时，结果始终标 Correction 并保留那次分类调用的
                // 模型名、耗时与 token；表达层成功与否只决定台词来自模型还是本地模板，不能覆盖这个标记。
                call = new ModelCallInfo(
                    classification.Model,
                    classification.LatencySeconds,
                    classification.PromptTokens,
                    classification.CompletionTokens,
                    ModelCallOutcome.Correction,
                    localTemplate
                        ? "分类被本地出口纠正；表达层没给出可用台词，台词来自本地模板"
                        : "分类被本地出口纠正；台词由表达层生成");
            }
            else if (localTemplate && call.Succeeded)
            {
                // 分类成功了，但表达层没赶上剩余预算：整体按超时展示，别让 HUD 以为这句是模型说的。
                call = new ModelCallInfo(
                    call.Model,
                    call.LatencySeconds,
                    call.PromptTokens,
                    call.CompletionTokens,
                    ModelCallOutcome.Timeout,
                    "表达层没有剩余预算");
            }

            return IntentResult.ReadOnly(outlet, reply, localTemplate, call);
        }

        /// <summary>农事指令：白名单、地块与品种都要在本地再解析一次，然后进入动作校验。</summary>
        static IntentResult ResolveCommand(
            RouteContext route,
            IntentDraft draft,
            bool usedFallback,
            ModelCallInfo call)
        {
            if (draft.IsRefusal)
            {
                return IntentResult.Refuse(draft.Reply, usedFallback, call);
            }

            if (!draft.Goal.HasValue)
            {
                return IntentResult.Refuse("你想让我做什么？", usedFallback, call);
            }

            if (!ActionValidator.TryGetPlan(draft.Goal.Value, out var steps))
            {
                return IntentResult.Refuse("这个活我还不会干。", usedFallback, call);
            }

            if (!TryResolveCommandField(
                    route.World,
                    route.Npc,
                    draft.FieldToken,
                    steps[0],
                    out var field,
                    out var fieldReason))
            {
                return IntentResult.Refuse(fieldReason, usedFallback, call);
            }

            CropSpecies species = null;
            if (draft.Goal.Value == FarmGoal.Plant)
            {
                if (!ActionValidator.TryResolveSpecies(
                        draft.SpeciesToken,
                        route.Npc.PreferredSpeciesId,
                        out species,
                        out var speciesReason))
                {
                    return IntentResult.Refuse(speciesReason, usedFallback, call);
                }
            }

            if (!route.World.TryAssignWorkOrder(
                    route.Npc.Id,
                    field.Id,
                    species?.Id,
                    steps,
                    out var order,
                    out var reason))
            {
                return IntentResult.Refuse(reason, usedFallback, call);
            }

            // 玩家的话也要记住：跨日之后再问「上次你让我浇的是哪块」得答得出来。
            route.World.Remember(
                route.Npc.Id,
                MemoryContext.PlayerRequestMemory(
                    route.World,
                    draft.Goal.Value,
                    field,
                    species,
                    draft.FieldToken,
                    GoalVerb(draft.Goal.Value)),
                MemoryContext.PlayerRequestImportance);

            var reply = ReplyOr(
                draft.Reply,
                $"好，我这就去{field.Id}{ActionName(draft.Goal.Value, species)}。");

            return IntentResult.AcceptedCommand(reply, order, usedFallback, call);
        }

        /// <summary>
        /// 一次路由的固定输入：玩家原话、世界、说话对象、检索到的记忆、表达层上下文与预算。
        /// 这些值在整次路由里不变，打包成一个类型，免得每一步都传七八个位置参数。
        /// </summary>
        sealed class RouteContext
        {
            public RouteContext(
                string text,
                FarmWorld world,
                Npc npc,
                IReadOnlyList<MemoryHit> memories,
                DialogueContext dialogue,
                Stopwatch clock,
                float deadline)
            {
                Text = text;
                World = world;
                Npc = npc;
                Memories = memories;
                Dialogue = dialogue;
                Clock = clock;
                Deadline = deadline;
            }

            public string Text { get; }

            public FarmWorld World { get; }

            public Npc Npc { get; }

            /// <summary>本次回答检索命中的记忆；离线与在线都先检索，记录一致。</summary>
            public IReadOnlyList<MemoryHit> Memories { get; }

            /// <summary>表达层的口吻与最近几轮；离线时为 null。</summary>
            public DialogueContext Dialogue { get; }

            /// <summary>本次对话的总预算从此刻开始算；离线时为 null。</summary>
            public Stopwatch Clock { get; }

            public float Deadline { get; }

            /// <summary>还剩多少秒预算；没有预算时（离线）为 0。</summary>
            public float Remaining => Clock == null
                ? 0f
                : Math.Max(0f, Deadline - (float)Clock.Elapsed.TotalSeconds);
        }

        /// <summary>一次模型调用的结果：失败时 <see cref="Response"/> 为 null，<see cref="Call"/> 带原因与耗时。</summary>
        readonly struct ModelAttempt
        {
            public ModelAttempt(ChatResponse response, ModelCallInfo call)
            {
                Response = response;
                Call = call;
            }

            public ChatResponse Response { get; }

            public ModelCallInfo Call { get; }

            public bool Succeeded => Response != null;

            public string Content => Response?.Content;
        }

        /// <summary>
        /// 发一次对话补全并等它到预算上限。超时、断网与同步抛错都折算成一条失败摘要，
        /// 由调用方决定降级；不向上抛异常。
        /// </summary>
        async Task<ModelAttempt> CallAsync(ChatRequest request, float budgetSeconds)
        {
            var budget = Math.Max(0.01f, budgetSeconds);
            using var timeout = new CancellationTokenSource();
            var startedAt = Stopwatch.StartNew();
            Task<ChatResponse> callTask;
            try
            {
                request.TimeoutSeconds = Math.Max(1, (int)Math.Ceiling(budget));
                callTask = client.CompleteAsync(request, timeout.Token);
            }
            catch (Exception ex)
            {
                startedAt.Stop();
                return FailedAttempt(startedAt.Elapsed.TotalSeconds, ModelCallOutcome.Error, ex.Message);
            }

            var deadlineTask = Task.Delay(TimeSpan.FromSeconds(budget), timeout.Token);
            var winner = await Task.WhenAny(callTask, deadlineTask);
            if (winner != callTask)
            {
                Observe(callTask);
                return FailedAttempt(budget, ModelCallOutcome.Timeout, $"等待 {budget:0.#} 秒未返回");
            }

            timeout.Cancel();

            ChatResponse response;
            try
            {
                response = await callTask;
            }
            catch (Exception ex)
            {
                startedAt.Stop();
                return FailedAttempt(startedAt.Elapsed.TotalSeconds, ModelCallOutcome.Error, ex.Message);
            }

            var responseModel = string.IsNullOrWhiteSpace(response.Model) ? client.ModelName : response.Model;
            return new ModelAttempt(
                response,
                new ModelCallInfo(
                    responseModel,
                    response.LatencySeconds,
                    response.PromptTokens,
                    response.CompletionTokens,
                    ModelCallOutcome.Ok));
        }

        ModelAttempt FailedAttempt(double latencySeconds, ModelCallOutcome outcome, string detail)
        {
            return new ModelAttempt(null, FailedCall(latencySeconds, outcome, detail));
        }

        ModelCallInfo FailedCall(double latencySeconds, ModelCallOutcome outcome, string detail)
        {
            return new ModelCallInfo(client?.ModelName, latencySeconds, 0, 0, outcome, detail);
        }

        /// <summary>模型给了内容但这一层用不了（空内容、非法 JSON、JSON 冒充台词）。</summary>
        static ModelCallInfo Unusable(ModelCallInfo call)
        {
            return new ModelCallInfo(
                call.Model,
                call.LatencySeconds,
                call.PromptTokens,
                call.CompletionTokens,
                ModelCallOutcome.Unusable,
                call.Detail);
        }

        /// <summary>
        /// 没点名地块时让效用打分挑：取该目标工步里分数最高且通过校验的地块。
        /// 没有有效候选时才退回最近地块，由派单校验给出具体拒绝理由。
        /// </summary>
        static bool TryResolveCommandField(
            FarmWorld world,
            Npc npc,
            string token,
            WorkStep firstStep,
            out Field field,
            out string reason)
        {
            if (FieldLocator.IsImplicit(token))
            {
                foreach (var candidate in world.RankCandidates(npc.Id))
                {
                    if (candidate.IsValid && candidate.Step == firstStep)
                    {
                        field = world.GetField(candidate.FieldId);
                        reason = null;
                        return true;
                    }
                }
            }

            return FieldLocator.TryResolve(world, npc, token, out field, out reason);
        }

        static string ActionName(FarmGoal goal, CropSpecies species)
        {
            return goal == FarmGoal.Plant
                ? $"种{species?.DisplayName ?? "地"}"
                : GoalVerb(goal);
        }

        static string GoalVerb(FarmGoal goal)
        {
            switch (goal)
            {
                case FarmGoal.Plant:
                    return "种";
                case FarmGoal.Water:
                    return "浇水";
                case FarmGoal.Fertilize:
                    return "施肥";
                case FarmGoal.Weed:
                    return "除草";
                case FarmGoal.Harvest:
                    return "收获";
                default:
                    return "干活";
            }
        }

        static string ReplyOr(string reply, string fallback)
        {
            return string.IsNullOrWhiteSpace(reply) ? fallback : reply.Trim();
        }

        /// <summary>模型的分类被本地出口规则纠正：保留这次调用的模型名、耗时与 token，只换结果类别。</summary>
        static ModelCallInfo Corrected(ModelCallInfo call)
        {
            return new ModelCallInfo(
                call.Model,
                call.LatencySeconds,
                call.PromptTokens,
                call.CompletionTokens,
                ModelCallOutcome.Correction,
                call.Detail);
        }

        static void Observe(Task task)
        {
            task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
