using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块化演示场景的引导器：搭出地块、仓库与 NPC 的方块替身，
    /// 用真实时间推进 <see cref="FarmWorld"/>，并让 NPC 每个工步都重新跑一遍效用打分。
    /// 这里不调用任何模型，也不持有领域状态——只读 <see cref="FarmWorld"/> 的快照。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FarmDemoBootstrap : MonoBehaviour
    {
        public const string FieldId = "A1";
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

        public FarmWorld World { get; private set; }

        public Field Field { get; private set; }

        public Npc Npc { get; private set; }

        /// <summary>NPC 此刻想干什么，供 HUD 展示。</summary>
        public string CurrentIntent { get; private set; } = "等天亮";

        /// <summary>最近一次工步的结果或跨日提示。</summary>
        public string LastMessage { get; private set; } = "第 1 天，阿禾在仓库旁待命";

        Transform npcView;
        Transform cropView;
        Transform weedsView;
        Material fieldMaterial;
        Material cropMaterial;

        StepCandidate pendingStep;
        Vector3 walkTarget;
        bool walking;
        float nextDecisionAt;

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

            var best = FindBestStep();
            if (!best.HasValue)
            {
                CurrentIntent = "等下一件该干的活";
                UpdateViews();
                return;
            }

            pendingStep = best.Value;
            CurrentIntent = $"前往 {pendingStep.FieldId} · {FarmDemoText.StepName(pendingStep.Step)}";
            walkTarget = new Vector3(Field.X, NpcGroundY, Field.Z - 2.6f);
            walking = true;
            UpdateViews();
        }

        void SetUpWorld()
        {
            World = new FarmWorld(new FarmClock());
            Field = World.AddField(FieldId, 0f, 3f, 0.3f);
            Npc = World.AddNpc(NpcId, "阿禾", 0f, -3f);
            World.SetWarehouse(-3f, -3f);
            World.AddSeeds(CropCatalog.RadishId, 10);
            World.DaySettled += day => LastMessage = $"第 {day} 天开始，作物结算完毕";
        }

        void BuildBlockScene()
        {
            CreateBlock("Ground", new Vector3(0f, -0.1f, 0f), new Vector3(26f, 0.2f, 26f), new Color(0.32f, 0.46f, 0.26f));

            var fieldBase = CreateBlock(
                "Field A1",
                new Vector3(Field.X, FieldTop * 0.5f, Field.Z),
                new Vector3(3f, FieldTop, 3f),
                DrySoil);
            fieldMaterial = fieldBase.GetComponent<Renderer>().sharedMaterial;

            var crop = CreateBlock("Radish", Vector3.zero, Vector3.one, CropGreen);
            cropView = crop.transform;
            cropMaterial = crop.GetComponent<Renderer>().sharedMaterial;
            cropView.gameObject.SetActive(false);

            CreateFence();

            CreateBlock(
                "Warehouse",
                new Vector3(-3f, 0.8f, -3f),
                new Vector3(2.4f, 1.6f, 2.4f),
                new Color(0.58f, 0.38f, 0.22f));

            CreateWeeds();
            CreateNpc();

            FrameCamera();
        }

        void CreateFence()
        {
            var postColor = new Color(0.72f, 0.62f, 0.46f);
            var corners = new[]
            {
                new Vector3(-1.7f, 0.4f, Field.Z - 1.7f),
                new Vector3(1.7f, 0.4f, Field.Z - 1.7f),
                new Vector3(-1.7f, 0.4f, Field.Z + 1.7f),
                new Vector3(1.7f, 0.4f, Field.Z + 1.7f)
            };

            foreach (var corner in corners)
            {
                CreateBlock("Fence Post", corner, new Vector3(0.16f, 0.8f, 0.16f), postColor);
            }
        }

        void CreateWeeds()
        {
            weedsView = new GameObject("Weeds").transform;
            weedsView.SetParent(transform, false);
            weedsView.position = new Vector3(Field.X, FieldTop + 0.1f, Field.Z);

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
                tuft.transform.SetParent(weedsView, false);
                tuft.transform.localPosition = offset;
                tuft.transform.localScale = new Vector3(0.45f, 0.5f, 0.45f);
                tuft.GetComponent<Renderer>().sharedMaterial = tuftMaterial;
            }

            weedsView.gameObject.SetActive(false);
        }

        void CreateNpc()
        {
            var npc = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            npc.name = "NPC 阿禾";
            npc.transform.position = new Vector3(Npc.X, NpcGroundY, Npc.Z);
            npc.transform.localScale = new Vector3(0.65f, 0.8f, 0.65f);
            npc.GetComponent<Renderer>().sharedMaterial = CreateMaterial("Npc", new Color(0.25f, 0.55f, 0.72f));
            npcView = npc.transform;

            var nose = CreateBlock("Nose", Vector3.zero, new Vector3(0.22f, 0.22f, 0.4f), new Color(0.92f, 0.85f, 0.7f));
            nose.transform.SetParent(npcView, false);
            nose.transform.localPosition = new Vector3(0f, 0.45f, 0.5f);
        }

        void FrameCamera()
        {
            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 12f, -10f), Quaternion.Euler(42f, 0f, 0f));
                camera.fieldOfView = 45f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.13f, 0.19f, 0.24f);
            }

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

        void Walk()
        {
            var position = npcView.position;
            var next = Vector3.MoveTowards(position, walkTarget, WalkSpeed * Time.deltaTime);
            next.y = NpcGroundY;
            npcView.position = next;
            World.MoveNpc(NpcId, next.x, next.z);

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
            var result = World.ExecuteStep(NpcId, pendingStep.FieldId, pendingStep.Step);
            if (result.Executed)
            {
                LastMessage = $"第 {World.Clock.Day} 天：{FarmDemoText.StepName(pendingStep.Step)}完成"
                    + (result.Yield > 0 ? $"，收得 {result.Yield} 个萝卜" : string.Empty);
                CurrentIntent = LastMessage;
            }
            else
            {
                LastMessage = $"想{FarmDemoText.StepName(pendingStep.Step)}，但{result.Reason}";
                CurrentIntent = "换个活干";
            }

            nextDecisionAt = Time.time + WorkPause;
        }

        StepCandidate? FindBestStep()
        {
            IReadOnlyList<StepCandidate> candidates = World.RankCandidates(NpcId);
            foreach (var candidate in candidates)
            {
                if (candidate.IsValid)
                {
                    return candidate;
                }
            }

            return null;
        }

        void UpdateViews()
        {
            fieldMaterial.SetColor("_BaseColor", Color.Lerp(DrySoil, WetSoil, Field.Water));

            var sown = Field.IsSown;
            cropView.gameObject.SetActive(sown);
            if (sown)
            {
                var height = 0.15f + Field.Growth * 1.2f;
                cropView.position = new Vector3(Field.X, FieldTop + height * 0.5f, Field.Z);
                cropView.localScale = new Vector3(0.9f, height, 0.9f);
                cropMaterial.SetColor("_BaseColor", Color.Lerp(CropGreen, CropMature, Field.Growth));
            }

            weedsView.gameObject.SetActive(Field.Weeds > 0.01f);
            if (Field.Weeds > 0.01f)
            {
                var spread = 0.25f + Field.Weeds * 0.9f;
                weedsView.localScale = new Vector3(spread, spread, spread);
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
