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
        /// 取记忆向量也占用这次预算。超时、网络错误与非法 JSON 都退到本地解析，调用方不会抛出异常。
        /// </summary>
        public async Task<IntentResult> RouteAsync(
            string text,
            FarmWorld world,
            Npc npc,
            float? deadlineSeconds = null,
            float? embeddingDeadlineSeconds = null)
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
                var offlineMemories = world.RecallForAnswer(npc.Id, text);
                return Finish(LocalFallback(text, world, npc, offlineMemories, ModelCallInfo.None));
            }

            // 追问与闲聊都要先检索记忆：向量可用时用余弦，断网或超时自动退回关键词。
            // 取向量也占用这一次对话的硬超时预算，不能变成 6 秒之外再加 2 秒。
            var routeStartedAt = Stopwatch.StartNew();
            var cueEmbedding = await MemoryContext.TryEmbedCueAsync(
                client,
                text,
                world,
                npc,
                Math.Min(deadline, embeddingDeadlineSeconds ?? EmbeddingDeadlineSeconds));
            var remaining = Math.Max(0.01f, deadline - (float)routeStartedAt.Elapsed.TotalSeconds);
            var memories = world.RecallForAnswer(npc.Id, text, cueEmbedding);

            using var timeout = new CancellationTokenSource();
            var startedAt = Stopwatch.StartNew();
            ChatRequest request;
            Task<ChatResponse> callTask;
            try
            {
                request = IntentPrompt.Build(world, npc, text, memories);
                request.TimeoutSeconds = Math.Max(1, (int)Math.Ceiling(remaining));
                callTask = client.CompleteAsync(request, timeout.Token);
            }
            catch (Exception ex)
            {
                startedAt.Stop();
                return Finish(LocalFallback(
                    text,
                    world,
                    npc,
                    memories,
                    new ModelCallInfo(
                        client.ModelName,
                        startedAt.Elapsed.TotalSeconds,
                        0,
                        0,
                        ModelCallOutcome.Error,
                        ex.Message)));
            }

            var deadlineTask = Task.Delay(TimeSpan.FromSeconds(remaining), timeout.Token);
            var winner = await Task.WhenAny(callTask, deadlineTask);
            if (winner == callTask)
            {
                timeout.Cancel();
            }
            else
            {
                Observe(callTask);
                var timedOut = new ModelCallInfo(
                    client.ModelName,
                    remaining,
                    0,
                    0,
                    ModelCallOutcome.Timeout,
                    $"等待 {remaining:0.#} 秒未返回");
                return Finish(LocalFallback(text, world, npc, memories, timedOut));
            }

            ChatResponse response;
            try
            {
                response = await callTask;
            }
            catch (Exception ex)
            {
                startedAt.Stop();
                return Finish(LocalFallback(
                    text,
                    world,
                    npc,
                    memories,
                    new ModelCallInfo(
                        client.ModelName,
                        startedAt.Elapsed.TotalSeconds,
                        0,
                        0,
                        ModelCallOutcome.Error,
                        ex.Message)));
            }

            var responseModel = string.IsNullOrWhiteSpace(response.Model) ? client.ModelName : response.Model;
            var call = new ModelCallInfo(
                responseModel,
                response.LatencySeconds,
                response.PromptTokens,
                response.CompletionTokens,
                ModelCallOutcome.Ok);

            if (!ModelIntentParser.TryParse(response.Content, out var draft, out _))
            {
                var unusable = new ModelCallInfo(
                    responseModel,
                    response.LatencySeconds,
                    response.PromptTokens,
                    response.CompletionTokens,
                    ModelCallOutcome.Unusable);
                return Finish(LocalFallback(text, world, npc, memories, unusable));
            }

            return Finish(ResolveDraft(draft, text, memories, world, npc, false, call));
        }

        IntentResult Finish(IntentResult result)
        {
            LastCall = result.Call;
            return result;
        }

        IntentResult LocalFallback(
            string text,
            FarmWorld world,
            Npc npc,
            IReadOnlyList<MemoryHit> memories,
            ModelCallInfo call)
        {
            var draft = LocalIntentParser.Parse(text, world, npc);
            return ResolveDraft(draft, text, memories, world, npc, true, call);
        }

        static IntentResult ResolveDraft(
            IntentDraft draft,
            string playerText,
            IReadOnlyList<MemoryHit> memories,
            FarmWorld world,
            Npc npc,
            bool usedFallback,
            ModelCallInfo call)
        {
            if (draft == null)
            {
                return IntentResult.Refuse("我没听明白。", usedFallback, call);
            }

            // 出口以玩家原话为准，模型只负责理解细节（地块、品种、措辞）：
            // 农活请求不能被模型当成闲聊吞掉，纯闲聊也不能被模型凭空变成一条农活。
            var isRequest = LocalIntentParser.IsFarmRequest(playerText, world, npc);
            if (isRequest != (draft.Outlet == IntentOutlet.FarmCommand))
            {
                draft = LocalIntentParser.Parse(playerText, world, npc);
                call = Corrected(call);
            }

            switch (draft.Outlet)
            {
                case IntentOutlet.Query:
                    // 本地追问必须重新生成：模型不可用时，draft 里的模板回复没有状态与记忆依据。
                    var localReply = LocalIntentParser.QueryReply(world, npc, playerText, memories);
                    var queryReply = usedFallback
                        ? localReply
                        : MemoryContext.EnsureQueryBasis(
                            ReplyOr(draft.Reply, localReply),
                            world,
                            npc,
                            memories,
                            LocalIntentParser.IsMemoryQuestion(playerText),
                            localReply);
                    return IntentResult.ReadOnly(IntentOutlet.Query, queryReply, usedFallback, call);

                case IntentOutlet.Chat:
                    // 闲聊只留下记忆（提到别人时还留一条可转述的传闻），不改变地块、时钟与背包；
                    // 模型给了 importance 就用模型的。
                    world.RememberChat(
                        npc.Id,
                        playerText,
                        draft.Importance > 0 ? draft.Importance : MemoryContext.PlayerChatImportance);
                    return IntentResult.ReadOnly(
                        IntentOutlet.Chat,
                        ReplyOr(draft.Reply, LocalIntentParser.ChatReply(playerText ?? string.Empty)),
                        usedFallback,
                        call);
            }

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

            if (!TryResolveCommandField(world, npc, draft.FieldToken, steps[0], out var field, out var fieldReason))
            {
                return IntentResult.Refuse(fieldReason, usedFallback, call);
            }

            CropSpecies species = null;
            if (draft.Goal.Value == FarmGoal.Plant)
            {
                if (!ActionValidator.TryResolveSpecies(
                        draft.SpeciesToken,
                        npc.PreferredSpeciesId,
                        out species,
                        out var speciesReason))
                {
                    return IntentResult.Refuse(speciesReason, usedFallback, call);
                }
            }

            if (!world.TryAssignWorkOrder(npc.Id, field.Id, species?.Id, steps, out var order, out var reason))
            {
                return IntentResult.Refuse(reason, usedFallback, call);
            }

            // 玩家的话也要记住：跨日之后再问「上次你让我浇的是哪块」得答得出来。
            world.Remember(
                npc.Id,
                MemoryContext.PlayerRequestMemory(
                    world,
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
