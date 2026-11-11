using System;
using System.Collections.Generic;
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
        /// <summary>默认硬超时：一句话等 6 秒还没结果就本地降级。</summary>
        public const float DefaultDeadlineSeconds = 6f;

        static readonly WorkStep[] PlantSteps =
        {
            WorkStep.Sow,
            WorkStep.Water,
            WorkStep.Fertilize,
            WorkStep.Weed,
            WorkStep.Harvest
        };

        static readonly WorkStep[] WaterSteps = { WorkStep.Water };
        static readonly WorkStep[] FertilizeSteps = { WorkStep.Fertilize };
        static readonly WorkStep[] WeedSteps = { WorkStep.Weed };
        static readonly WorkStep[] HarvestSteps = { WorkStep.Harvest };

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
        /// 超时、网络错误与非法 JSON 都会退到本地解析，调用方不会抛出异常。
        /// </summary>
        public async Task<IntentResult> RouteAsync(
            string text,
            FarmWorld world,
            Npc npc,
            float deadlineSeconds = DefaultDeadlineSeconds)
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
                    $"「{unknownCrop}」可种不出来，地里只能种萝卜和小麦。",
                    false,
                    new ModelCallInfo(null, 0d, 0, 0, "guard")));
            }

            if (client == null || deadlineSeconds <= 0f)
            {
                return Finish(LocalFallback(text, world, npc, ModelCallInfo.None));
            }

            using var timeout = new CancellationTokenSource();
            ChatRequest request;
            Task<ChatResponse> callTask;
            try
            {
                request = IntentPrompt.Build(world, npc, text);
                callTask = client.CompleteAsync(request, timeout.Token);
            }
            catch (Exception ex)
            {
                return Finish(LocalFallback(
                    text,
                    world,
                    npc,
                    new ModelCallInfo(null, 0d, 0, 0, "error", ex.Message)));
            }

            var deadlineTask = Task.Delay(TimeSpan.FromSeconds(deadlineSeconds), timeout.Token);
            var winner = await Task.WhenAny(callTask, deadlineTask);
            if (winner == callTask)
            {
                timeout.Cancel();
            }
            else
            {
                Observe(callTask);
                var timedOut = new ModelCallInfo(
                    null,
                    deadlineSeconds,
                    0,
                    0,
                    "timeout",
                    $"等待 {deadlineSeconds:0.#} 秒未返回");
                return Finish(LocalFallback(text, world, npc, timedOut));
            }

            ChatResponse response;
            try
            {
                response = await callTask;
            }
            catch (Exception ex)
            {
                return Finish(LocalFallback(
                    text,
                    world,
                    npc,
                    new ModelCallInfo(null, 0d, 0, 0, "error", ex.Message)));
            }

            var call = new ModelCallInfo(
                response.Model,
                response.LatencySeconds,
                response.PromptTokens,
                response.CompletionTokens,
                "ok");

            if (!ModelIntentParser.TryParse(response.Content, out var draft, out _))
            {
                var unusable = new ModelCallInfo(
                    response.Model,
                    response.LatencySeconds,
                    response.PromptTokens,
                    response.CompletionTokens,
                    "unusable");
                return Finish(LocalFallback(text, world, npc, unusable));
            }

            return Finish(ResolveDraft(draft, world, npc, false, call));
        }

        IntentResult Finish(IntentResult result)
        {
            LastCall = result.Call;
            return result;
        }

        IntentResult LocalFallback(string text, FarmWorld world, Npc npc, ModelCallInfo call)
        {
            var draft = LocalIntentParser.Parse(text, world, npc);
            return ResolveDraft(draft, world, npc, true, call);
        }

        static IntentResult ResolveDraft(
            IntentDraft draft,
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
                    return IntentResult.ReadOnly(
                        IntentOutlet.Query,
                        ReplyOr(draft.Reply, LocalIntentParser.QueryReply(world, npc)),
                        usedFallback,
                        call);

                case IntentOutlet.Chat:
                    return IntentResult.ReadOnly(
                        IntentOutlet.Chat,
                        ReplyOr(draft.Reply, LocalIntentParser.ChatReply(string.Empty)),
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

            if (!FieldLocator.TryResolve(world, npc, draft.FieldToken, out var field, out var fieldReason))
            {
                return IntentResult.Refuse(fieldReason, usedFallback, call);
            }

            CropSpecies species = null;
            if (draft.Goal.Value == FarmGoal.Plant)
            {
                if (!SpeciesLocator.TryResolve(draft.SpeciesToken, npc, out species, out var speciesReason))
                {
                    return IntentResult.Refuse(speciesReason, usedFallback, call);
                }
            }

            var steps = StepsFor(draft.Goal.Value);
            if (!world.TryAssignOrder(npc.Id, field.Id, species?.Id, steps, out var order, out var reason))
            {
                return IntentResult.Refuse(reason, usedFallback, call);
            }

            var reply = ReplyOr(
                draft.Reply,
                $"好，我这就去{field.Id}{ActionName(draft.Goal.Value, species)}。");

            return IntentResult.AcceptedCommand(
                reply,
                field.Id,
                species?.Id,
                steps,
                order,
                usedFallback,
                call);
        }

        static IReadOnlyList<WorkStep> StepsFor(FarmGoal goal)
        {
            switch (goal)
            {
                case FarmGoal.Plant:
                    return PlantSteps;
                case FarmGoal.Water:
                    return WaterSteps;
                case FarmGoal.Fertilize:
                    return FertilizeSteps;
                case FarmGoal.Weed:
                    return WeedSteps;
                case FarmGoal.Harvest:
                    return HarvestSteps;
                default:
                    throw new ArgumentOutOfRangeException(nameof(goal), goal, null);
            }
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
