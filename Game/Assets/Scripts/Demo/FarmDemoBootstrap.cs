using System;
using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块化演示场景的引导器：搭出地块、产出仓与 NPC 的方块替身，
    /// 用真实时间推进 <see cref="FarmWorld"/>，并让 NPC 每个工步都重新跑一遍效用打分。
    /// 玩家输入经 <see cref="IntentRouter"/> 变成工单后，NPC 再按工单执行。
    /// 引导器不持有领域状态——只读 <see cref="FarmWorld"/> 的快照。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmDemoBootstrap : MonoBehaviour
    {
        public const string FieldId = "A1";
        public const string EastFieldId = "A2";
        public const string NpcId = "ahe";

        const float WalkSpeed = 2.6f;
        const float WorkPause = 0.5f;
        const float FieldTop = 0.15f;
        const float NpcGroundY = 0.8f;

        static readonly Color DrySoil = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color WetSoil = new Color(0.24f, 0.17f, 0.12f);
        static readonly Color CropGreen = new Color(0.22f, 0.62f, 0.24f);
        static readonly Color CropMature = new Color(0.95f, 0.74f, 0.16f);
        static readonly Color WeedGreen = new Color(0.55f, 0.62f, 0.20f);

        /// <summary>领域世界（唯一状态来源）。</summary>
        public FarmWorld World { get; private set; }

        /// <summary>演示地块。</summary>
        public Field Field { get; private set; }

        /// <summary>演示 NPC。</summary>
        public Npc Npc { get; private set; }

        /// <summary>NPC 此刻想干什么，供 HUD 展示。</summary>
        public string CurrentIntent { get; private set; } = "等天亮";

        /// <summary>最近一次工步的结果或跨日提示。</summary>
        public string LastMessage { get; private set; } = "第 1 天，NPC 在产出仓旁待命";

        /// <summary>玩家最近一次输入。</summary>
        public string LastPlayerText { get; private set; } = string.Empty;

        /// <summary>NPC 对最近一次输入的回应（接单、拒绝或闲聊）。</summary>
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

        Transform npcView;
        readonly List<FieldView> fieldViews = new List<FieldView>();

        string pendingFieldId;
        WorkStep pendingWorkStep;
        Vector3 walkTarget;
        bool walking;
        float nextDecisionAt;

        sealed class FieldView
        {
            public Field Field;
            public Transform Crop;
            public Transform Weeds;
            public Material Soil;
            public Material CropMaterial;
        }

        void Awake()
        {
            SetUpWorld();
            BuildBlockScene();
        }

        void Update()
        {
            World.Tick(Time.deltaTime);

            if (Time.time < nextDecisionAt)
            {
                UpdateViews();
                return;
            }

            if (walking)
            {
                Walk();
                UpdateViews();
                return;
            }

            var order = World.WorkOrderOf(NpcId);
            if (order != null)
            {
                var next = order.NextStep;
                if (!next.HasValue)
                {
                    CurrentIntent = "工单刚干完";
                    UpdateViews();
                    return;
                }

                // 不在这里执行：走到地里后由 AdvanceWorkOrder 决定
                // 是执行下一步，还是先做浇水/除草/施肥这类维护。
                var canDoNext = World.GetField(order.FieldId).Can(next.Value, out var waitReason);
                BeginAction(
                    order.FieldId,
                    next.Value,
                    canDoNext ? null : $"等 {order.FieldId} · {FarmDemoText.StepName(next.Value)}（{waitReason}）");
                UpdateViews();
                return;
            }

            if (!AutonomyEnabled)
            {
                CurrentIntent = "待命，等玩家开口";
                UpdateViews();
                return;
            }

            var best = World.NextStep(NpcId);
            if (!best.HasValue)
            {
                CurrentIntent = "等下一件该干的活";
                UpdateViews();
                return;
            }

            BeginAction(best.Value.FieldId, best.Value.Step);
            UpdateViews();
        }

        /// <summary>切换自主干活。关掉只影响之后的选择，正在走的路或工单会先干完。</summary>
        public void ToggleAutonomy()
        {
            AutonomyEnabled = !AutonomyEnabled;
            CurrentIntent = AutonomyEnabled ? "自己找活干" : "待命，等玩家开口";
        }

        void BeginAction(string fieldId, WorkStep step, string intentHint = null)
        {
            pendingFieldId = fieldId;
            pendingWorkStep = step;

            var field = World.GetField(fieldId);
            walkTarget = new Vector3(field.Position.X, NpcGroundY, field.Position.Z - 2.6f);
            walking = true;
            CurrentIntent = intentHint ?? $"前往 {fieldId} · {FarmDemoText.StepName(step)}";
        }

        /// <summary>
        /// 玩家提交一句话。真正的解析在 <see cref="IntentRouter"/> 里异步进行；
        /// 等待期间游戏循环不暂停，NPC 继续自主干活。
        /// </summary>
        public void SubmitPlayerText(string text)
        {
            if (IsThinking || Router == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            LastPlayerText = text.Trim();
            LastReply = string.Empty;
            LastUnderstood = "正在理解…";
            IsThinking = true;
            CurrentIntent = "正在听玩家说话";
            SubmitAsync(LastPlayerText);
        }

        async void SubmitAsync(string text)
        {
            try
            {
                var result = await Router.RouteAsync(text, World, Npc);
                if (this == null)
                {
                    return;
                }

                ApplyIntent(result);
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
                CurrentIntent = "没听清";
                Debug.LogWarning($"[Elys] 意图路由异常：{ex}");
            }
        }

        void ApplyIntent(IntentResult result)
        {
            IsThinking = false;
            LastReply = result.Reply;

            if (result.Call.Outcome == ModelCallOutcome.Error && !string.IsNullOrWhiteSpace(result.Call.Detail))
            {
                Debug.LogWarning($"[Elys] 模型调用失败，已回退本地模板：{result.Call.Detail}");
            }

            if (result.Accepted)
            {
                LastUnderstood = FarmDemoText.Plan(result.FieldId, result.SpeciesId, result.PlannedSteps);
                walking = false;
                pendingFieldId = null;
                nextDecisionAt = 0f;
                AutonomyEnabled = true;
                CurrentIntent = "按工单开工";
                LastMessage = $"{Npc.DisplayName}：{result.Reply}";
                return;
            }

            LastUnderstood = "没有接单";
            LastMessage = $"{Npc.DisplayName}：{result.Reply}";
            CurrentIntent = result.Outlet == IntentOutlet.FarmCommand ? "拒绝了这单活" : "陪着说了两句";
        }

        void SetUpWorld()
        {
            World = new FarmWorld(new FarmClock());
            Field = World.AddField(FieldId, new FarmPosition(-2f, 3f), 0.3f);
            World.AddField(EastFieldId, new FarmPosition(2.5f, 3f), 0.3f);
            Npc = World.AddNpc(NpcId, "阿禾", new FarmPosition(-1.5f, -3f));
            World.SetProduceStore(new FarmPosition(-2.4f, -3f));
            World.AddSeeds(CropCatalog.RadishId, 10);
            Router = new IntentRouter(DemoModelSettings.CreateClient());
            World.DaySettled += day => LastMessage = $"第 {day} 天开始，作物结算完毕";
            AutonomyEnabled = false;
            LastMessage = $"第 1 天，{Npc.DisplayName}在产出仓旁待命"
                + (UsingModel ? "（模型在线）" : "（本地模板）");
        }

        void BuildBlockScene()
        {
            CreateBlock("Ground", new Vector3(2f, -0.1f, 0f), new Vector3(34f, 0.2f, 28f), new Color(0.32f, 0.46f, 0.26f));

            foreach (var field in World.Fields)
            {
                CreateFieldView(field);
            }

            CreateBlock(
                "Produce Store",
                new Vector3(-4f, 0.8f, -3f),
                new Vector3(2.4f, 1.6f, 2.4f),
                new Color(0.58f, 0.38f, 0.22f));

            CreateNpc();

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

        void CreateNpc()
        {
            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = $"NPC {Npc.DisplayName}";
            npc.transform.position = new Vector3(Npc.Position.X, NpcGroundY, Npc.Position.Z);
            npc.transform.localScale = new Vector3(0.65f, 0.8f, 0.65f);
            npc.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Npc", new Color(0.25f, 0.55f, 0.72f));
            npcView = npc.transform;

            var nose = CreateBlock("Nose", Vector3.zero, new Vector3(0.22f, 0.22f, 0.4f), new Color(0.92f, 0.85f, 0.7f));
            nose.transform.SetParent(npcView, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.5f);
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

            camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -10f), Quaternion.Euler(42f, 0f, 0f));
            camera.fieldOfView = 45f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.13f, 0.19f, 0.24f);
        }

        void Walk()
        {
            var position = npcView.position;
            var next = Vector3.MoveTowards(position, walkTarget, WalkSpeed * Time.deltaTime);
            next.y = NpcGroundY;
            npcView.position = next;
            World.MoveNpc(NpcId, new FarmPosition(next.x, next.z));

            var direction = new Vector3(walkTarget.x - next.x, 0f, walkTarget.z - next.z);
            if (direction.sqrMagnitude > 0.0004f)
            {
                npcView.rotation = Quaternion.LookRotation(direction);
            }

            var flatDistance = Vector2.Distance(new Vector2(next.x, next.z), new Vector2(walkTarget.x, walkTarget.z));
            if (flatDistance > 0.05f)
            {
                return;
            }

            walking = false;

            var order = World.WorkOrderOf(NpcId);
            var executingOrder = order != null
                && order.FieldId == pendingFieldId
                && order.NextStep.HasValue
                && order.NextStep.Value == pendingWorkStep;
            var result = executingOrder
                ? World.AdvanceWorkOrder(NpcId)
                : World.ExecuteStep(NpcId, pendingFieldId, pendingWorkStep);

            if (result.Executed)
            {
                var executedStep = result.Step ?? pendingWorkStep;
                LastMessage = $"第 {World.Clock.Day} 天：{FarmDemoText.StepName(executedStep)}完成"
                    + (result.Yield > 0
                        ? $"，收得 {result.Yield} 个{CropCatalog.Radish.DisplayName}"
                        : string.Empty);
                CurrentIntent = LastMessage;
            }
            else
            {
                LastMessage = $"想{FarmDemoText.StepName(pendingWorkStep)}，但{result.Reason}";
                CurrentIntent = "换个活干";
            }

            nextDecisionAt = Time.time + WorkPause;
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
