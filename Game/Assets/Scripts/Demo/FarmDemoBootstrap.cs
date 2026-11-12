using System;
using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示场景的引导器：搭出地块、产出仓、休息点、农舍与三名 NPC，用真实时间推进
    /// <see cref="FarmWorld"/>，并让每名 NPC 按自己的人设决定间隔与体力节奏独立跑效用打分。
    /// 玩家输入经 <see cref="IntentRouter"/> 变成工单后，接单的 NPC 再按工单执行。
    /// 引导器不持有领域状态——只读 <see cref="FarmWorld"/> 的快照。
    /// 表现层用像素素材；素材缺失时自动退回体块，场景不会崩。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmDemoBootstrap : MonoBehaviour
    {
        public const string AheId = "ahe";
        public const string AyaId = "aya";
        public const string AtuId = "atu";

        public const string FieldA1 = "A1";
        public const string FieldA2 = "A2";
        public const string FieldA3 = "A3";

        /// <summary>默认指挥对象；HUD 可切到另外两名。</summary>
        public const string DefaultNpcId = AheId;

        const float WorkPause = 0.5f;
        const float IdlePause = 0.8f;
        const float RestRefresh = 0.3f;
        const float ArriveRadius = 0.05f;

        /// <summary>两名 NPC 之间多久试着搭一次话；没有听来的事就什么也不发生。</summary>
        const float ChatPause = 4f;

        /// <summary>搭话时两人停下手里的事，让观察者看清这句转述。</summary>
        const float ChatHold = 1.2f;

        static readonly Color AheColor = new Color(0.25f, 0.55f, 0.72f);
        static readonly Color AyaColor = new Color(0.88f, 0.55f, 0.25f);
        static readonly Color AtuColor = new Color(0.52f, 0.42f, 0.72f);

        /// <summary>休息点；体力见底的 NPC 会走到这里待着。</summary>
        public static FarmPosition RestSpot =>
            FarmLayout.ToFarmPosition(FarmLayout.TileCenter(FarmLayout.RestSpotTile));

        /// <summary>产出仓的标记点；NPC 走到这里就算到了产出仓。</summary>
        public static FarmPosition ProduceStoreSpot =>
            FarmLayout.ToFarmPosition(FarmLayout.TileCenter(FarmLayout.ProduceStoreTile));

        /// <summary>领域世界（唯一状态来源）。</summary>
        public FarmWorld World { get; private set; }

        /// <summary>HUD 正在观察、也是下一句话的指挥对象。</summary>
        public Npc SelectedNpc { get; private set; }

        /// <summary>最近一次工步的结果或跨日提示。</summary>
        public string LastMessage { get; private set; } = "第 1 天，三名 NPC 在产出仓旁待命。";

        /// <summary>玩家最近一次输入。</summary>
        public string LastPlayerText { get; private set; } = string.Empty;

        /// <summary>选中 NPC 对最近一次输入的回应（接单、拒绝或闲聊）。</summary>
        public string LastReply { get; private set; } = string.Empty;

        /// <summary>最近一次输入被理解成了什么，供 HUD 的「理解成」一行展示。</summary>
        public string LastUnderstood { get; private set; } = "还没有收到玩家的话";

        /// <summary>对话框里此刻说话的人；开局是默认指挥对象。</summary>
        public string SpeakerId { get; private set; } = DefaultNpcId;

        /// <summary>最近一次对话的出口；头像表情由它加上此刻的状态决定。</summary>
        public IntentOutlet LastOutlet { get; private set; } = IntentOutlet.Query;

        /// <summary>最近一次农事指令是否接单；只影响表现层，不改变领域状态。</summary>
        public bool LastAccepted { get; private set; }

        /// <summary>正在等模型回话；此时游戏循环照常推进。</summary>
        public bool IsThinking { get; private set; }

        /// <summary>意图出口：离线时为 null 客户端，全部走本地模板。</summary>
        public IntentRouter Router { get; private set; }

        /// <summary>是否配置了模型。</summary>
        public bool UsingModel => Router != null && Router.HasModel;

        /// <summary>最近一次模型调用摘要。</summary>
        public ModelCallInfo LastCall => Router != null ? Router.LastCall : ModelCallInfo.None;

        /// <summary>是否让 NPC 自己找活干；开局待命，等玩家第一句话或手动开启。</summary>
        public bool AutonomyEnabled { get; private set; }

        /// <summary>当前是否用素材（false 表示体块回退）。</summary>
        public bool UsingArt => PixelArt.UseArt;

        /// <summary>当前是否俯视机位。</summary>
        public bool IsOverheadCamera => cameraDirector == null || cameraDirector.Mode == CameraMode.Overhead;

        Transform visualRoot;
        Transform npcRoot;
        DemoCamera cameraDirector;
        readonly List<CropFieldView> fieldViews = new List<CropFieldView>();
        readonly List<NpcView> npcViews = new List<NpcView>();
        float nextChatAt;
        int chatSlot;

        sealed class NpcView
        {
            public Npc Npc;
            public NpcAvatar Avatar;
            public Color Color;
            public int Slot;
            public float WalkSpeed;
            public string PendingFieldId;
            public WorkStep PendingStep;
            public Vector2 WalkTarget;
            public bool Walking;
            public float NextActionAt;
            public string Intent = "在产出仓旁待命";
        }

        void Awake()
        {
            PixelArt.ApplyEnvironmentOverride();
            SetUpWorld();
            BuildScene();
        }

        void Update()
        {
            World.Tick(Time.deltaTime);

            foreach (var view in npcViews)
            {
                UpdateNpc(view);
                view.Avatar.SetWalking(view.Walking);
            }

            TryNpcConversation();

            foreach (var view in fieldViews)
            {
                view.Refresh();
            }
        }

        /// <summary>某名 NPC 此刻的意图文案，供 HUD 与观察者对照它的行为。</summary>
        public string IntentOf(string npcId)
        {
            var view = ViewOf(npcId);
            return view != null ? view.Intent : string.Empty;
        }

        /// <summary>
        /// 某名 NPC 此刻适合作头像表情的状态：休息与等模型优先于出口，
        /// 这样「刚接单但已经累到去休息」不会还挂着一张得意的脸。
        /// </summary>
        public FaceCondition FaceConditionOf(string npcId)
        {
            var view = ViewOf(npcId);
            if (view == null)
            {
                return FaceCondition.Idle;
            }

            if (view.Npc.IsResting)
            {
                return FaceCondition.Resting;
            }

            return IsThinking && SelectedNpc != null && SelectedNpc.Id == npcId
                ? FaceCondition.Thinking
                : FaceCondition.Idle;
        }

        /// <summary>切换 HUD 的观察与指挥对象；不影响任何 NPC 的自主行为。</summary>
        public void SelectNpc(string npcId)
        {
            SelectedNpc = World.GetNpc(npcId);
        }

        /// <summary>切换自主干活。关掉只影响之后的选择，正在走的路或工单会先干完。</summary>
        public void ToggleAutonomy()
        {
            AutonomyEnabled = !AutonomyEnabled;
            foreach (var view in npcViews)
            {
                view.NextActionAt = 0f;
                view.Intent = AutonomyEnabled ? "自己找活干" : "待命，等玩家开口";
            }
        }

        /// <summary>切回俯视 3×。</summary>
        public void UseOverheadCamera()
        {
            cameraDirector?.SetOverhead();
        }

        /// <summary>切到指定 NPC 的第三人称跟拍；未知 NPC 不改变机位。</summary>
        public void FollowCamera(string npcId)
        {
            var view = ViewOf(npcId);
            if (view == null || cameraDirector == null)
            {
                return;
            }

            cameraDirector.Follow(npcId, view.Avatar.transform);
        }

        /// <summary>当前是否正在跟拍某名 NPC。</summary>
        public bool IsFollowing(string npcId)
        {
            return cameraDirector != null && cameraDirector.Rig.IsFollowing(npcId);
        }

        /// <summary>正在跟拍的 NPC id；俯视时为 null。</summary>
        public string FollowedNpcId => cameraDirector != null ? cameraDirector.FollowNpcId : null;

        /// <summary>
        /// 在素材与体块之间切换。素材缺失时用体块是验收项，这里给观察者一个当场验证的开关：
        /// 重建的只是表现层，<see cref="FarmWorld"/> 的时钟、账本与 NPC 状态都不动。
        /// </summary>
        public void ToggleArt()
        {
            var following = cameraDirector != null ? cameraDirector.FollowNpcId : null;
            PixelArt.UseArt = !PixelArt.UseArt;
            BuildScene();

            if (!string.IsNullOrEmpty(following))
            {
                FollowCamera(following);
            }

            LastMessage = PixelArt.UseArt
                ? "切到像素素材"
                : "素材已关闭，退回体块（领域状态不变）";
        }

        /// <summary>
        /// 玩家提交一句话。真正的解析在 <see cref="IntentRouter"/> 里异步进行；
        /// 等待期间游戏循环不暂停，其余 NPC 继续自主干活。
        /// </summary>
        public void SubmitPlayerText(string text)
        {
            if (IsThinking || Router == null || string.IsNullOrWhiteSpace(text) || SelectedNpc == null)
            {
                return;
            }

            LastPlayerText = text.Trim();
            LastReply = string.Empty;
            LastUnderstood = "正在理解…";
            IsThinking = true;
            var target = SelectedNpc;
            SpeakerId = target.Id;
            var view = ViewOf(target.Id);
            if (view != null)
            {
                view.Intent = "正在听玩家说话";
            }

            SubmitAsync(LastPlayerText, target);
        }

        async void SubmitAsync(string text, Npc target)
        {
            try
            {
                var result = await Router.RouteAsync(text, World, target);
                if (this == null)
                {
                    return;
                }

                ApplyIntent(result, target);
            }
            catch (Exception ex)
            {
                if (this == null)
                {
                    return;
                }

                IsThinking = false;
                SpeakerId = target.Id;
                LastOutlet = IntentOutlet.Query;
                LastAccepted = false;
                LastReply = "我脑子卡了一下，先按本地规矩干活。";
                LastMessage = LastReply;
                var view = ViewOf(target.Id);
                if (view != null)
                {
                    view.Intent = "没听清";
                }

                Debug.LogWarning($"[Elys] 意图路由异常：{ex}");
            }
        }

        void ApplyIntent(IntentResult result, Npc target)
        {
            IsThinking = false;
            SpeakerId = target.Id;
            LastOutlet = result.Outlet;
            LastAccepted = result.Accepted;
            LastReply = result.Reply;

            if (result.Call.Outcome == ModelCallOutcome.Error && !string.IsNullOrWhiteSpace(result.Call.Detail))
            {
                Debug.LogWarning($"[Elys] 模型调用失败，已回退本地模板：{result.Call.Detail}");
            }

            var view = ViewOf(target.Id);

            if (result.Accepted)
            {
                LastUnderstood = FarmDemoText.Plan(result.FieldId, result.SpeciesId, result.PlannedSteps);
                if (view != null)
                {
                    view.Walking = false;
                    view.PendingFieldId = null;
                    view.NextActionAt = 0f;
                    view.Intent = "按工单开工";
                }

                AutonomyEnabled = true;
                foreach (var other in npcViews)
                {
                    other.NextActionAt = 0f;
                }

                LastMessage = $"{target.DisplayName}：{result.Reply}";
                return;
            }

            switch (result.Outlet)
            {
                case IntentOutlet.Query:
                    LastUnderstood = "追问：只读回答（不改状态）";
                    break;
                case IntentOutlet.Chat:
                    LastUnderstood = "闲聊：只留下记忆（提到别人会变成传闻）";
                    break;
                default:
                    LastUnderstood = "没有接单";
                    break;
            }

            LastMessage = $"{target.DisplayName}：{result.Reply}";
            if (view != null)
            {
                view.Intent = result.Outlet == IntentOutlet.FarmCommand ? "拒绝了这单活" : "陪着说了两句";
            }
        }

        void SetUpWorld()
        {
            World = new FarmWorld(new FarmClock());

            foreach (var footprint in FarmLayout.Fields)
            {
                World.AddField(
                    footprint.Id,
                    FarmLayout.ToFarmPosition(footprint.Center),
                    FarmStartup.FieldFertility);
            }

            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                World.AddNpc(
                    spawn.Id,
                    DisplayNameOf(spawn.Id),
                    FarmLayout.ToFarmPosition(spawn.World),
                    PersonaOf(spawn.Id));
            }

            World.SetProduceStore(ProduceStoreSpot);
            World.AddSeeds(CropCatalog.RadishId, 12);
            World.AddSeeds(CropCatalog.WheatId, 4);

            // 开局三块地各停在一个阶段：幼苗的萝卜、熟了的小麦、快熟的萝卜。
            // 种子库要先入账——播种照实数扣种，不能凭空长出三块地。
            FarmStartup.Seed(World);

            Router = new IntentRouter(DemoModelSettings.CreateClient());
            SelectedNpc = World.GetNpc(DefaultNpcId);
            World.DaySettled += day => LastMessage = $"第 {day} 天开始，作物结算完毕";
            AutonomyEnabled = false;
            LastMessage = "第 1 天，三名 NPC 在产出仓旁待命"
                + (UsingModel ? "（模型在线）" : "（本地模板）")
                + "。";

            foreach (var spawn in FarmLayout.NpcSpawns)
            {
                CreateNpcView(World.GetNpc(spawn.Id), ColorOf(spawn.Id));
            }
        }

        void BuildScene()
        {
            DemoMaterials.DestroyCreated();
            FarmDressing.DestroyTiles();

            if (visualRoot != null)
            {
                DemoMaterials.DestroyObject(visualRoot.gameObject);
            }

            visualRoot = new GameObject("Farm Visuals").transform;
            visualRoot.SetParent(transform, false);

            FarmDressing.Build(visualRoot);
            FarmProps.Build(visualRoot);

            fieldViews.Clear();
            foreach (var field in World.Fields)
            {
                var view = new CropFieldView(field);
                view.Build(visualRoot);
                fieldViews.Add(view);
            }

            npcRoot = new GameObject("NPCs").transform;
            npcRoot.SetParent(visualRoot, false);

            foreach (var view in npcViews)
            {
                view.Avatar = NpcAvatar.Create(view.Npc, npcRoot, view.Color);
                view.Avatar.SetFeet(FarmLayout.ToWorld(view.Npc.Position));
                view.Avatar.SetWalking(view.Walking);
            }

            SetUpCamera();
        }

        void SetUpCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
                camera = cameraObject.AddComponent<Camera>();
                if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
                {
                    cameraObject.AddComponent<AudioListener>();
                }
            }

            cameraDirector = GetComponent<DemoCamera>();
            if (cameraDirector == null)
            {
                cameraDirector = gameObject.AddComponent<DemoCamera>();
            }

            cameraDirector.Configure(camera);
        }

        void CreateNpcView(Npc npc, Color color)
        {
            npcViews.Add(new NpcView
            {
                Npc = npc,
                Color = color,
                Slot = npcViews.Count,
                WalkSpeed = WalkSpeedOf(npc.Id)
            });
        }

        /// <summary>演示用的展示名；领域只认 id，名字是表现层的事。</summary>
        public static string DisplayNameOf(string npcId)
        {
            switch (npcId)
            {
                case AheId:
                    return "阿禾";
                case AyaId:
                    return "阿芽";
                case AtuId:
                    return "阿土";
                default:
                    return npcId;
            }
        }

        static NpcPersona PersonaOf(string npcId)
        {
            switch (npcId)
            {
                case AyaId:
                    return NpcPersona.Aya;
                case AtuId:
                    return NpcPersona.Atu;
                default:
                    return NpcPersona.Ahe;
            }
        }

        static Color ColorOf(string npcId)
        {
            switch (npcId)
            {
                case AyaId:
                    return AyaColor;
                case AtuId:
                    return AtuColor;
                default:
                    return AheColor;
            }
        }

        /// <summary>行走速度是表现层参数，不进领域人设；只影响观感，不影响效用打分。</summary>
        static float WalkSpeedOf(string npcId)
        {
            switch (npcId)
            {
                case AheId:
                    return 2.6f;
                case AyaId:
                    return 3.4f;
                case AtuId:
                    return 1.6f;
                default:
                    return 2.6f;
            }
        }

        /// <summary>把相机摆到俯视 3× 的演示机位；场景生成器与运行时共用同一组数值。</summary>
        public static void ApplyCameraPose(Camera camera)
        {
            DemoCamera.ApplyOverheadTo(camera);
        }

        void UpdateNpc(NpcView view)
        {
            var npc = view.Npc;

            // 休息在走到休息点之前就结束时，不继续傻走到目的地，直接回去找活。
            if (view.Walking && string.IsNullOrEmpty(view.PendingFieldId) && !npc.IsResting)
            {
                view.Walking = false;
                view.Intent = "歇好了，接着干";
            }

            if (view.Walking)
            {
                Walk(view);
                return;
            }

            if (Time.time < view.NextActionAt)
            {
                return;
            }

            if (npc.IsResting)
            {
                var restTarget = RestTargetOf(view);
                if (Vector2.Distance(view.Avatar.Feet, restTarget) > 0.35f)
                {
                    BeginWalk(view, restTarget, $"去歇会儿（体力 {npc.Stamina:P0}）");
                }
                else
                {
                    view.Intent = $"歇着（体力 {npc.Stamina:P0}，还剩 {npc.RestSecondsRemaining:0} 秒）";
                    view.NextActionAt = Time.time + RestRefresh;
                }

                return;
            }

            var order = World.WorkOrderOf(npc.Id);
            if (order != null)
            {
                var next = order.NextStep;
                if (!next.HasValue)
                {
                    view.Intent = "工单刚干完";
                    return;
                }

                // 不在这里执行：走到地里后由 AdvanceWorkOrder 决定
                // 是执行下一步，还是先做浇水/除草/施肥这类维护。
                var canDoNext = World.GetField(order.FieldId).Can(next.Value, out var waitReason);
                BeginWalkToField(
                    view,
                    order.FieldId,
                    next.Value,
                    canDoNext ? null : $"等 {order.FieldId} · {FarmDemoText.StepName(next.Value)}（{waitReason}）");
                return;
            }

            if (!npc.CanDecide)
            {
                view.Intent = FarmWorld.ThinkingReason;
                view.NextActionAt = Time.time + 0.2f;
                return;
            }

            if (!AutonomyEnabled)
            {
                view.Intent = "待命，等玩家开口";
                return;
            }

            if (!World.TryDecide(npc.Id, out var candidate, out var reason))
            {
                view.Intent = $"没活干（{reason}）";
                view.NextActionAt = Time.time + IdlePause;
                return;
            }

            BeginWalkToField(view, candidate.FieldId, candidate.Step);
        }

        /// <summary>
        /// NPC 之间自己发起对话：轮到一个 NPC 时，找一名站得近、不在路上的同伴，
        /// 把听来的事讲一遍。演示层只负责挑人、播报与暂停，传闻本身由
        /// <see cref="FarmWorld.TryRetell"/> 认定、写进听者的记忆。
        /// </summary>
        void TryNpcConversation()
        {
            if (npcViews.Count < 2 || Time.time < nextChatAt)
            {
                return;
            }

            nextChatAt = Time.time + ChatPause;
            chatSlot = (chatSlot + 1) % npcViews.Count;
            var speaker = npcViews[chatSlot];
            if (speaker.Walking || speaker.Npc.IsResting)
            {
                return;
            }

            foreach (var listener in npcViews)
            {
                if (listener == speaker || listener.Walking)
                {
                    continue;
                }

                if (!World.TryRetell(speaker.Npc.Id, listener.Npc.Id, out var retold))
                {
                    continue;
                }

                var line = retold.Line;
                speaker.Intent = $"跟{listener.Npc.DisplayName}说：{line}";
                listener.Intent = $"听{speaker.Npc.DisplayName}说：{line}";
                SpeakerId = speaker.Npc.Id;
                LastOutlet = IntentOutlet.Chat;
                LastAccepted = false;
                speaker.NextActionAt = Time.time + ChatHold;
                listener.NextActionAt = Time.time + ChatHold;
                LastMessage = $"第 {World.Clock.Day} 天 · "
                    + $"{speaker.Npc.DisplayName}把听来的事讲给{listener.Npc.DisplayName}：{line}";
                return;
            }
        }

        void BeginWalkToField(NpcView view, string fieldId, WorkStep step, string intentHint = null)
        {
            view.PendingFieldId = fieldId;
            view.PendingStep = step;
            view.WalkTarget = FieldStandPoint(fieldId, view.Slot);
            view.Walking = true;
            view.Intent = intentHint ?? $"前往 {fieldId} · {FarmDemoText.StepName(step)}";
        }

        /// <summary>走到地块南侧、离地一格的位置；三个站位横向错开，避免三个人叠在一起。</summary>
        public static Vector2 FieldStandPoint(string fieldId, int slot)
        {
            if (!FarmLayout.TryGetField(fieldId, out var footprint))
            {
                return FarmLayout.OverheadCenter;
            }

            var center = FarmLayout.TileCenter(footprint.Tiles);
            return new Vector2(center.x + (slot - 1) * 0.8f, footprint.Tiles.y - 0.5f);
        }

        void BeginWalk(NpcView view, Vector2 target, string intent)
        {
            view.PendingFieldId = null;
            view.PendingStep = default;
            view.WalkTarget = target;
            view.Walking = true;
            view.Intent = intent;
        }

        Vector2 RestTargetOf(NpcView view)
        {
            var spot = FarmLayout.ToWorld(RestSpot);
            return new Vector2(spot.x + (view.Slot - 1) * 0.8f, spot.y);
        }

        void Walk(NpcView view)
        {
            var next = Vector2.MoveTowards(
                view.Avatar.Feet,
                view.WalkTarget,
                view.WalkSpeed * Time.deltaTime);
            view.Avatar.SetFeet(next);
            World.MoveNpc(view.Npc.Id, new FarmPosition(next.x, next.y));

            var direction = view.WalkTarget - next;
            if (direction.sqrMagnitude > 0.0004f)
            {
                view.Avatar.SetVelocity(direction);
            }

            if (Vector2.Distance(next, view.WalkTarget) > ArriveRadius)
            {
                return;
            }

            view.Walking = false;

            if (string.IsNullOrEmpty(view.PendingFieldId))
            {
                view.Intent = "歇着";
                view.NextActionAt = Time.time + RestRefresh;
                return;
            }

            FinishAction(view);
        }

        void FinishAction(NpcView view)
        {
            var npc = view.Npc;
            var order = World.WorkOrderOf(npc.Id);
            var executingOrder = order != null
                && order.FieldId == view.PendingFieldId
                && order.NextStep.HasValue
                && order.NextStep.Value == view.PendingStep;
            var result = executingOrder
                ? World.AdvanceWorkOrder(npc.Id)
                : World.ExecuteStep(npc.Id, view.PendingFieldId, view.PendingStep);

            if (result.Executed)
            {
                var executedStep = result.Step ?? view.PendingStep;
                var action = $"{FarmDemoText.StepName(executedStep)}了 {view.PendingFieldId}"
                    + (result.Yield > 0 ? $"，收得 {result.Yield} 个产出" : string.Empty);
                view.Intent = npc.IsResting ? "太累了，去歇会儿" : action;
                LastMessage = $"第 {World.Clock.Day} 天 · {npc.DisplayName}{action}";
                view.NextActionAt = Time.time + WorkPause;
            }
            else
            {
                view.Intent = npc.IsResting ? "太累了，去歇会儿" : $"没做成：{result.Reason}";
                LastMessage = $"{npc.DisplayName}想{FarmDemoText.StepName(view.PendingStep)}，但{result.Reason}";
                view.NextActionAt = Time.time + IdlePause;
            }

            view.PendingFieldId = null;
        }

        NpcView ViewOf(string npcId)
        {
            foreach (var view in npcViews)
            {
                if (view.Npc.Id == npcId)
                {
                    return view;
                }
            }

            return null;
        }
    }
}
