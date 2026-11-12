using System;
using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块化演示场景的引导器：搭出地块、产出仓、休息点与三名 NPC 的方块替身，
    /// 用真实时间推进 <see cref="FarmWorld"/>，并让每名 NPC 按自己的人设决定间隔与体力节奏
    /// 独立跑效用打分。玩家输入经 <see cref="IntentRouter"/> 变成工单后，接单的 NPC 再按工单执行。
    /// 引导器不持有领域状态——只读 <see cref="FarmWorld"/> 的快照。
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

        const float FieldTop = 0.15f;
        const float NpcGroundY = 0.8f;
        const float WorkPause = 0.5f;
        const float IdlePause = 0.8f;
        const float RestRefresh = 0.3f;
        const float ArriveRadius = 0.05f;

        /// <summary>两名 NPC 之间多久试着搭一次话；没有听来的事就什么也不发生。</summary>
        const float ChatPause = 4f;

        /// <summary>搭话时两人停下手里的事，让观察者看清这句转述。</summary>
        const float ChatHold = 1.2f;

        static readonly Color DrySoil = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color WetSoil = new Color(0.24f, 0.17f, 0.12f);
        static readonly Color CropGreen = new Color(0.22f, 0.62f, 0.24f);
        static readonly Color CropMature = new Color(0.95f, 0.74f, 0.16f);
        static readonly Color WeedGreen = new Color(0.55f, 0.62f, 0.20f);
        static readonly Color AheColor = new Color(0.25f, 0.55f, 0.72f);
        static readonly Color AyaColor = new Color(0.88f, 0.55f, 0.25f);
        static readonly Color AtuColor = new Color(0.52f, 0.42f, 0.72f);

        /// <summary>休息点；体力见底的 NPC 会走到这里待着。</summary>
        public static readonly FarmPosition RestSpot = new FarmPosition(2.2f, -1.5f);

        /// <summary>领域世界（唯一状态来源）。</summary>
        public FarmWorld World { get; private set; }

        /// <summary>HUD 正在观察、也是下一句话的指挥对象。</summary>
        public Npc SelectedNpc { get; private set; }

        /// <summary>最近一次工步的结果或跨日提示。</summary>
        public string LastMessage { get; private set; } = "第 1 天，三名 NPC 在产出仓旁待命";

        /// <summary>玩家最近一次输入。</summary>
        public string LastPlayerText { get; private set; } = string.Empty;

        /// <summary>选中 NPC 对最近一次输入的回应（接单、拒绝或闲聊）。</summary>
        public string LastReply { get; private set; } = string.Empty;

        /// <summary>最近一次输入被理解成了什么，供 HUD 的「理解成」一行展示。</summary>
        public string LastUnderstood { get; private set; } = "还没有收到玩家的话";

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

        Transform npcRoot;
        readonly List<FieldView> fieldViews = new List<FieldView>();
        readonly List<NpcView> npcViews = new List<NpcView>();
        float nextChatAt;
        int chatSlot;

        sealed class FieldView
        {
            public Field Field;
            public Transform Crop;
            public Transform Weeds;
            public Material Soil;
            public Material CropMaterial;
        }

        sealed class NpcView
        {
            public Npc Npc;
            public Transform Body;
            public int Slot;
            public float WalkSpeed;
            public string PendingFieldId;
            public WorkStep PendingStep;
            public Vector3 WalkTarget;
            public bool Walking;
            public float NextActionAt;
            public string Intent = "在产出仓旁待命";
        }

        void Awake()
        {
            SetUpWorld();
            BuildBlockScene();
        }

        void Update()
        {
            World.Tick(Time.deltaTime);

            foreach (var view in npcViews)
            {
                UpdateNpc(view);
            }

            TryNpcConversation();
            UpdateViews();
        }

        /// <summary>某名 NPC 此刻的意图文案，供 HUD 与观察者对照它的行为。</summary>
        public string IntentOf(string npcId)
        {
            var view = ViewOf(npcId);
            return view != null ? view.Intent : string.Empty;
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
            World.AddField(FieldA1, new FarmPosition(-4f, 4f), 0.3f);
            World.AddField(FieldA2, new FarmPosition(0f, 4f), 0.3f);
            World.AddField(FieldA3, new FarmPosition(4f, 4f), 0.3f);

            World.AddNpc(AheId, "阿禾", new FarmPosition(-1.5f, -3f), NpcPersona.Ahe);
            World.AddNpc(AyaId, "阿芽", new FarmPosition(0f, -3f), NpcPersona.Aya);
            World.AddNpc(AtuId, "阿土", new FarmPosition(1.5f, -3f), NpcPersona.Atu);

            World.SetProduceStore(new FarmPosition(-3.4f, -1.8f));
            World.AddSeeds(CropCatalog.RadishId, 12);
            World.AddSeeds(CropCatalog.WheatId, 4);

            Router = new IntentRouter(DemoModelSettings.CreateClient());
            SelectedNpc = World.GetNpc(DefaultNpcId);
            World.DaySettled += day => LastMessage = $"第 {day} 天开始，作物结算完毕";
            AutonomyEnabled = false;
            LastMessage = "第 1 天，三名 NPC 在产出仓旁待命"
                + (UsingModel ? "（模型在线）" : "（本地模板）");
        }

        void BuildBlockScene()
        {
            CreateBlock("Ground", new Vector3(2f, -0.1f, 0f), new Vector3(38f, 0.2f, 28f), new Color(0.32f, 0.46f, 0.26f));

            foreach (var field in World.Fields)
            {
                CreateFieldView(field);
            }

            CreateBlock(
                "Produce Store",
                new Vector3(-3.4f, 0.8f, -1.8f),
                new Vector3(2.4f, 1.6f, 2.4f),
                new Color(0.58f, 0.38f, 0.22f));

            CreateBlock(
                "Rest Spot",
                new Vector3(RestSpot.X, 0.35f, RestSpot.Z),
                new Vector3(3.2f, 0.7f, 1.4f),
                new Color(0.82f, 0.70f, 0.30f));

            npcRoot = new GameObject("NPCs").transform;
            npcRoot.SetParent(transform, false);

            CreateNpc(World.GetNpc(AheId), AheColor);
            CreateNpc(World.GetNpc(AyaId), AyaColor);
            CreateNpc(World.GetNpc(AtuId), AtuColor);

            FrameCamera();
        }

        void CreateFieldView(Field field)
        {
            var fieldBase = CreateBlock(
                $"Field {field.Id}",
                new Vector3(field.Position.X, FieldTop * 0.5f, field.Position.Z),
                new Vector3(3f, FieldTop, 3f),
                DrySoil);

            var crop = CreateBlock($"Crop {field.Id}", Vector3.zero, Vector3.one, CropGreen);
            crop.SetActive(false);

            var view = new FieldView
            {
                Field = field,
                Crop = crop.transform,
                CropMaterial = crop.GetComponent<Renderer>().sharedMaterial,
                Soil = fieldBase.GetComponent<Renderer>().sharedMaterial
            };

            CreateWeeds(field, view);
            CreateFence(field);
            fieldViews.Add(view);
        }

        void CreateFence(Field field)
        {
            var postColor = new Color(0.72f, 0.62f, 0.46f);
            var corners = new[]
            {
                new Vector3(field.Position.X - 1.7f, 0.4f, field.Position.Z - 1.7f),
                new Vector3(field.Position.X + 1.7f, 0.4f, field.Position.Z - 1.7f),
                new Vector3(field.Position.X - 1.7f, 0.4f, field.Position.Z + 1.7f),
                new Vector3(field.Position.X + 1.7f, 0.4f, field.Position.Z + 1.7f)
            };

            foreach (var corner in corners)
            {
                CreateBlock("Fence Post", corner, new Vector3(0.16f, 0.8f, 0.16f), postColor);
            }
        }

        void CreateWeeds(Field field, FieldView view)
        {
            var weeds = new GameObject($"Weeds {field.Id}").transform;
            weeds.SetParent(transform, false);
            weeds.position = new Vector3(field.Position.X, FieldTop + 0.1f, field.Position.Z);

            var tuftMaterial = CreateMaterial("Weed", WeedGreen);
            var offsets = new[]
            {
                new Vector3(-1.05f, 0f, -1.05f),
                new Vector3(1.05f, 0f, -1.05f),
                new Vector3(-1.05f, 0f, 1.05f),
                new Vector3(1.05f, 0f, 1.05f)
            };

            foreach (var offset in offsets)
            {
                var tuft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tuft.name = "Weed Tuft";
                tuft.transform.SetParent(weeds, false);
                tuft.transform.localPosition = offset;
                tuft.transform.localScale = new Vector3(0.45f, 0.5f, 0.45f);
                tuft.GetComponent<Renderer>().sharedMaterial = tuftMaterial;
            }

            weeds.gameObject.SetActive(false);
            view.Weeds = weeds;
        }

        void CreateNpc(Npc npc, Color color)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = $"NPC {npc.DisplayName}";
            body.transform.SetParent(npcRoot, false);
            body.transform.position = new Vector3(npc.Position.X, NpcGroundY, npc.Position.Z);
            body.transform.localScale = new Vector3(0.65f, 0.8f, 0.65f);
            body.GetComponent<Renderer>().sharedMaterial = CreateMaterial($"Npc {npc.Id}", color);

            var nose = CreateBlock("Nose", Vector3.zero, new Vector3(0.22f, 0.22f, 0.4f), new Color(0.92f, 0.85f, 0.7f));
            nose.transform.SetParent(body.transform, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.5f);

            npcViews.Add(new NpcView
            {
                Npc = npc,
                Body = body.transform,
                Slot = npcViews.Count,
                WalkSpeed = WalkSpeedOf(npc.Id)
            });
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

        void FrameCamera()
        {
            ApplyCameraPose(Camera.main);

            var sun = FindFirstObjectByType<Light>();
            if (sun == null)
            {
                var lightObject = new GameObject("Sun");
                sun = lightObject.AddComponent<Light>();
                sun.type = LightType.Directional;
            }

            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.15f;
        }

        /// <summary>把相机摆到俯视 45° 的演示机位；场景生成器与运行时共用同一组数值。</summary>
        public static void ApplyCameraPose(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -16f), Quaternion.Euler(42f, 0f, 0f));
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.19f, 0.24f);
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
                if (Vector3.Distance(view.Body.position, restTarget) > 0.35f)
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
        /// <see cref="FarmWorld.TryShareNews"/> 认定、写进听者的记忆。
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

                if (!World.TryShareNews(speaker.Npc.Id, listener.Npc.Id, out var news))
                {
                    continue;
                }

                var line = $"{news.SubjectName}{news.Claim}";
                speaker.Intent = $"跟{listener.Npc.DisplayName}说：{line}";
                listener.Intent = $"听{speaker.Npc.DisplayName}说：{line}";
                speaker.NextActionAt = Time.time + ChatHold;
                listener.NextActionAt = Time.time + ChatHold;
                LastMessage = $"第 {World.Clock.Day} 天 · "
                    + $"{speaker.Npc.DisplayName}把听来的事讲给{listener.Npc.DisplayName}：{line}";
                return;
            }
        }

        void BeginWalkToField(NpcView view, string fieldId, WorkStep step, string intentHint = null)
        {
            var field = World.GetField(fieldId);
            var lateral = (view.Slot - 1) * 0.55f;
            var target = new Vector3(field.Position.X + lateral, NpcGroundY, field.Position.Z - 2.6f);

            view.PendingFieldId = fieldId;
            view.PendingStep = step;
            view.WalkTarget = target;
            view.Walking = true;
            view.Intent = intentHint ?? $"前往 {fieldId} · {FarmDemoText.StepName(step)}";
        }

        void BeginWalk(NpcView view, Vector3 target, string intent)
        {
            view.PendingFieldId = null;
            view.PendingStep = default;
            view.WalkTarget = target;
            view.Walking = true;
            view.Intent = intent;
        }

        Vector3 RestTargetOf(NpcView view)
        {
            var lateral = (view.Slot - 1) * 0.9f;
            return new Vector3(RestSpot.X + lateral, NpcGroundY, RestSpot.Z);
        }

        void Walk(NpcView view)
        {
            var position = view.Body.position;
            var next = Vector3.MoveTowards(position, view.WalkTarget, view.WalkSpeed * Time.deltaTime);
            next.y = NpcGroundY;
            view.Body.position = next;
            World.MoveNpc(view.Npc.Id, new FarmPosition(next.x, next.z));

            var direction = new Vector3(view.WalkTarget.x - next.x, 0f, view.WalkTarget.z - next.z);
            if (direction.sqrMagnitude > 0.0004f)
            {
                view.Body.rotation = Quaternion.LookRotation(direction);
            }

            var flatDistance = Vector2.Distance(
                new Vector2(next.x, next.z),
                new Vector2(view.WalkTarget.x, view.WalkTarget.z));
            if (flatDistance > ArriveRadius)
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

        void UpdateViews()
        {
            foreach (var view in fieldViews)
            {
                var field = view.Field;
                view.Soil.SetColor("_BaseColor", Color.Lerp(DrySoil, WetSoil, field.Water));

                var sown = field.IsSown;
                view.Crop.gameObject.SetActive(sown);
                if (sown)
                {
                    var height = 0.15f + field.Growth * 1.2f;
                    view.Crop.position = new Vector3(field.Position.X, FieldTop + height * 0.5f, field.Position.Z);
                    view.Crop.localScale = new Vector3(0.9f, height, 0.9f);
                    view.CropMaterial.SetColor("_BaseColor", Color.Lerp(CropGreen, CropMature, field.Growth));
                }

                view.Weeds.gameObject.SetActive(field.Weeds > 0.01f);
                if (field.Weeds > 0.01f)
                {
                    var spread = 0.25f + field.Weeds * 0.9f;
                    view.Weeds.localScale = new Vector3(spread, spread, spread);
                }
            }
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

        static GameObject CreateBlock(string name, Vector3 position, Vector3 scale, Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = CreateMaterial(name, color);
            return block;
        }

        static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = name };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }
    }
}
