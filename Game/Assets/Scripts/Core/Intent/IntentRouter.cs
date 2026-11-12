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

        /// <summary>一次批量补向量的条数上限；只补最近没向量的记忆，避免一次请求过大。</summary>
        const int EmbeddingBatchLimit = 10;

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
        /// 未显式指定 deadline 时，农事指令用规划级 15 秒，追问与闲聊用对话级 6 秒。
        /// 超时、网络错误与非法 JSON 都会退到本地解析，调用方不会抛出异常。
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
            if (LocalIntentParser.TryFindUnknownCrop(text, out var unknownCrop))
            {
                return Finish(IntentResult.Refuse(
                    ActionValidator.UnknownSpeciesReason(unknownCrop),
                    false,
                    new ModelCallInfo(null, 0d, 0, 0, ModelCallOutcome.Guard)));
            }

            var deadline = deadlineSeconds
                ?? (LocalIntentParser.LooksLikeFarmCommand(text) ? PlanningDeadlineSeconds : ChatDeadlineSeconds);

            if (client == null || deadline <= 0f)
            {
                var offlineMemories = world.RecallForAnswer(npc.Id, text);
                return Finish(LocalFallback(text, world, npc, offlineMemories, ModelCallInfo.None));
            }

            // 追问与闲聊都要先检索记忆：向量可用时用余弦，断网或超时自动退回关键词。
            var cueEmbedding = await TryEmbedCueAsync(
                text,
                world,
                npc,
                embeddingDeadlineSeconds ?? EmbeddingDeadlineSeconds);
            var memories = world.RecallForAnswer(npc.Id, text, cueEmbedding);

            using var timeout = new CancellationTokenSource();
            var startedAt = Stopwatch.StartNew();
            ChatRequest request;
            Task<ChatResponse> callTask;
            try
            {
                request = IntentPrompt.Build(world, npc, text, memories);
                request.TimeoutSeconds = Math.Max(1, (int)Math.Ceiling(deadline));
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

            var deadlineTask = Task.Delay(TimeSpan.FromSeconds(deadline), timeout.Token);
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
                    deadline,
                    0,
                    0,
                    ModelCallOutcome.Timeout,
                    $"等待 {deadline:0.#} 秒未返回");
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

            switch (draft.Outlet)
            {
                case IntentOutlet.Query:
                    // 本地追问必须重新生成：模型不可用时，draft 里的模板回复没有状态与记忆依据。
                    var localReply = LocalIntentParser.QueryReply(world, npc, playerText, memories);
                    var queryReply = usedFallback
                        ? localReply
                        : EnsureQueryBasis(ReplyOr(draft.Reply, localReply), world, npc, memories, localReply);
                    return IntentResult.ReadOnly(IntentOutlet.Query, queryReply, usedFallback, call);

                case IntentOutlet.Chat:
                    // 闲聊只留下记忆，不改变地块、时钟与背包；importance 后续由模型细化。
                    world.Remember(
                        npc.Id,
                        $"玩家说「{Shorten(playerText, PlayerChatMaxLength)}」",
                        PlayerChatImportance);
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
                PlayerRequestMemory(world, draft.Goal.Value, field, species, draft.FieldToken),
                PlayerRequestImportance);

            var reply = ReplyOr(
                draft.Reply,
                $"好，我这就去{field.Id}{ActionName(draft.Goal.Value, species)}。");

            return IntentResult.AcceptedCommand(reply, order, usedFallback, call);
        }

        /// <summary>
        /// 模型的追问回答如果既没提状态也没提记忆，补一句本地生成的依据。
        /// 这样「答得出」不依赖模型是否听话，也不会把空泛回答直接端给玩家。
        /// </summary>
        static string EnsureQueryBasis(
            string reply,
            FarmWorld world,
            Npc npc,
            IReadOnlyList<MemoryHit> memories,
            string localReply)
        {
            if (string.IsNullOrWhiteSpace(reply))
            {
                return localReply;
            }

            return HasQueryBasis(reply, world, npc, memories) ? reply : $"{reply.Trim()} {localReply}";
        }

        static bool HasQueryBasis(
            string reply,
            FarmWorld world,
            Npc npc,
            IReadOnlyList<MemoryHit> memories)
        {
            if (memories != null)
            {
                foreach (var memory in memories)
                {
                    if (reply.Contains($"第 {memory.Day} 天") || reply.Contains(memory.Text))
                    {
                        return true;
                    }
                }
            }

            foreach (var field in world.Fields)
            {
                if (reply.Contains(field.Id))
                {
                    return true;
                }
            }

            foreach (WorkStep step in Enum.GetValues(typeof(WorkStep)))
            {
                if (reply.Contains(WorkStepLabel.Of(step)))
                {
                    return true;
                }
            }

            return reply.Contains("体力") || reply.Contains("心情") || reply.Contains("饥饿");
        }

        /// <summary>
        /// 取检索线索的向量，并顺手补齐最近记忆缺的向量。任何失败（未配置、断网、超时、
        /// 维度不对）都返回 null，调用方按关键词检索，绝不阻塞对话。
        /// </summary>
        async Task<float[]> TryEmbedCueAsync(string cue, FarmWorld world, Npc npc, float deadline)
        {
            if (client == null || !client.SupportsEmbeddings || deadline <= 0f)
            {
                return null;
            }

            var pending = world.MemoriesNeedingEmbedding(npc.Id, EmbeddingBatchLimit);
            var inputs = new List<string>(pending.Count + 1) { cue };
            inputs.AddRange(pending);

            using var timeout = new CancellationTokenSource();
            try
            {
                var callTask = client.EmbedAsync(inputs, timeout.Token);
                var winner = await Task.WhenAny(callTask, Task.Delay(TimeSpan.FromSeconds(deadline), timeout.Token));
                if (winner != callTask)
                {
                    timeout.Cancel();
                    Observe(callTask);
                    return null;
                }

                timeout.Cancel();
                var vectors = await callTask;
                if (vectors == null || vectors.Count != inputs.Count)
                {
                    return null;
                }

                for (var i = 0; i < pending.Count; i++)
                {
                    var vector = UsableVector(vectors[i + 1]);
                    if (vector != null)
                    {
                        world.CacheMemoryEmbedding(npc.Id, pending[i], vector);
                    }
                }

                return UsableVector(vectors[0]);
            }
            catch
            {
                return null;
            }
        }

        static float[] UsableVector(float[] vector)
        {
            if (vector == null || vector.Length == 0)
            {
                return null;
            }

            foreach (var value in vector)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return null;
                }
            }

            return vector;
        }

        /// <summary>
        /// 玩家一次农事指令的记忆文本：用当时解析出的方位与地块，而不是原始句子。
        /// 玩家点名「西边」时即使农场只有一块地，也保留这个方位，跨日追问才对得上。
        /// </summary>
        static string PlayerRequestMemory(
            FarmWorld world,
            FarmGoal goal,
            Field field,
            CropSpecies species,
            string fieldToken)
        {
            var label = FieldLocator.TryDirectionLabel(fieldToken, out var direction)
                ? $"{direction}（{field.Id}）"
                : world.DescribeField(field.Id);
            switch (goal)
            {
                case FarmGoal.Plant:
                    return $"玩家让我把{label}种满{species?.DisplayName ?? "作物"}";
                case FarmGoal.Water:
                    return $"玩家让我给{label}浇水";
                case FarmGoal.Fertilize:
                    return $"玩家让我给{label}施肥";
                case FarmGoal.Weed:
                    return $"玩家让我给{label}除草";
                case FarmGoal.Harvest:
                    return $"玩家让我收{label}的作物";
                default:
                    return $"玩家让我去{label}干活";
            }
        }

        /// <summary>玩家农事指令的记忆重要性：高于日常维护，低于收获。</summary>
        const int PlayerRequestImportance = 7;

        /// <summary>玩家闲聊的记忆重要性：低于农事指令，避让待办事项。</summary>
        const int PlayerChatImportance = 4;

        /// <summary>闲聊记忆保留的最大字数；再长就截断，避免一条闲聊挤掉农事记忆。</summary>
        const int PlayerChatMaxLength = 40;

        static string Shorten(string text, int maxLength)
        {
            var value = (text ?? string.Empty).Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
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
            switch (goal)
            {
                case FarmGoal.Plant:
                    return $"种{species?.DisplayName ?? "地"}";
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
