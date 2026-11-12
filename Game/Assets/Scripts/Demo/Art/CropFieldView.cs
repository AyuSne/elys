using System.Collections.Generic;
using ElysFarm.Core;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 一块地的表现：土、围栏、九个作物位与杂草。作物按 <see cref="CropStage"/> 换模型；
    /// 模型缺失时同一套位置改放体块，状态照旧由 <see cref="Field"/> 决定。
    /// </summary>
    public sealed class CropFieldView
    {
        const float FieldTop = 0.15f;
        const float HalfWidth = 1.5f;
        const float CropSpread = 0.82f;
        const float CropYawPerUnit = 37f;
        const float WeedYawPerUnit = 53f;

        static readonly Vector3[] CropOffsets = BuildOffsets();

        readonly Field field;
        Transform root;
        Transform cropRoot;
        Transform weedsRoot;
        Material soilMaterial;
        CropStage stage = CropStage.None;

        public CropFieldView(Field field)
        {
            this.field = field;
        }

        public void Build(Transform parent)
        {
            root = new GameObject($"Field {field.Id}").transform;
            root.SetParent(parent, false);

            var soil = DemoMaterials.Block(
                $"Soil {field.Id}",
                root,
                new Vector3(field.Position.X, FieldTop * 0.5f, field.Position.Z),
                new Vector3(HalfWidth * 2f, FieldTop, HalfWidth * 2f),
                DemoMaterials.SoilColor(field.Water));
            soilMaterial = soil.GetComponent<Renderer>().sharedMaterial;

            BuildFence();

            cropRoot = new GameObject("Crops").transform;
            cropRoot.SetParent(root, false);

            BuildWeeds();
            Refresh();
        }

        /// <summary>每帧同步土色、作物阶段与杂草；只有阶段变化时才重建作物。</summary>
        public void Refresh()
        {
            DemoMaterials.SetColor(soilMaterial, DemoMaterials.SoilColor(field.Water));

            var next = CropStages.Of(field.State, field.Growth);
            if (next != stage)
            {
                stage = next;
                RebuildCrop();
            }

            if (weedsRoot != null)
            {
                var visible = field.Weeds > 0.01f && field.IsSown;
                weedsRoot.gameObject.SetActive(visible);
                if (visible)
                {
                    var spread = 0.7f + field.Weeds * 0.6f;
                    weedsRoot.localScale = new Vector3(spread, spread, spread);
                }
            }
        }

        void RebuildCrop()
        {
            ClearChildren(cropRoot);

            if (stage == CropStage.None || field.Crop == null)
            {
                return;
            }

            var speciesId = field.Crop.Id;
            var model = QuaterniusArt.LoadCrop(speciesId, stage);
            var look = CropStages.LookOf(speciesId, stage);

            foreach (var offset in CropOffsets)
            {
                var ground = new Vector3(
                    field.Position.X + offset.x,
                    FieldTop,
                    field.Position.Z + offset.z);

                if (model != null)
                {
                    for (var copy = 0; copy < look.Copies; copy++)
                    {
                        var clump = look.Copies == 1
                            ? Vector3.zero
                            : new Vector3((copy - 1) * 0.16f, 0f, (copy % 2) * 0.12f);
                        var crop = ModelPlacement.Spawn(
                            model,
                            cropRoot,
                            ground + clump,
                            look.Scale,
                            offset.z * CropYawPerUnit + copy * 40f);
                        if (crop != null && look.UseModelTint)
                        {
                            ModelPlacement.Tint(crop, look.Tint);
                        }
                    }

                    continue;
                }

                var height = 0.22f + CropStages.ScaleOf(stage) * 0.85f;
                DemoMaterials.Block(
                    $"Crop {field.Id}",
                    cropRoot,
                    new Vector3(ground.x, FieldTop + height * 0.5f, ground.z),
                    new Vector3(0.5f, height, 0.5f),
                    look.Tint);
            }
        }

        void BuildFence()
        {
            var fenceModel = QuaterniusArt.Load("Buildings/Fence");
            if (fenceModel != null)
            {
                var half = HalfWidth + 0.22f;
                ModelPlacement.Spawn(fenceModel, root, new Vector3(field.Position.X, 0f, field.Position.Z - half), 0.5f);
                ModelPlacement.Spawn(fenceModel, root, new Vector3(field.Position.X, 0f, field.Position.Z + half), 0.5f, 180f);
                ModelPlacement.Spawn(fenceModel, root, new Vector3(field.Position.X - half, 0f, field.Position.Z), 0.5f, 90f);
                ModelPlacement.Spawn(fenceModel, root, new Vector3(field.Position.X + half, 0f, field.Position.Z), 0.5f, 90f);
                return;
            }

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
                DemoMaterials.Block("Fence Post", root, corner, new Vector3(0.16f, 0.8f, 0.16f), postColor);
            }
        }

        void BuildWeeds()
        {
            weedsRoot = new GameObject($"Weeds {field.Id}").transform;
            weedsRoot.SetParent(root, false);
            weedsRoot.position = new Vector3(field.Position.X, FieldTop + 0.05f, field.Position.Z);

            var grass = QuaterniusArt.Load("Nature/Grass2");
            var tuftColor = new Color(0.55f, 0.62f, 0.20f);
            var offsets = new[]
            {
                new Vector3(-1.05f, 0f, -1.05f),
                new Vector3(1.05f, 0f, -1.05f),
                new Vector3(-1.05f, 0f, 1.05f),
                new Vector3(1.05f, 0f, 1.05f)
            };

            foreach (var offset in offsets)
            {
                if (grass != null)
                {
                    ModelPlacement.Spawn(
                        grass,
                        weedsRoot,
                        weedsRoot.position + offset,
                        0.85f,
                        offset.x * WeedYawPerUnit);
                    continue;
                }

                var tuft = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tuft.name = "Weed Tuft";
                tuft.transform.SetParent(weedsRoot, false);
                tuft.transform.localPosition = offset;
                tuft.transform.localScale = new Vector3(0.45f, 0.5f, 0.45f);
                tuft.GetComponent<Renderer>().sharedMaterial = DemoMaterials.Create("Weed", tuftColor);
            }

            weedsRoot.gameObject.SetActive(false);
        }

        static void ClearChildren(Transform parent)
        {
            var children = new List<GameObject>();
            foreach (Transform child in parent)
            {
                children.Add(child.gameObject);
            }

            foreach (var child in children)
            {
                ModelPlacement.Destroy(child);
            }
        }

        static Vector3[] BuildOffsets()
        {
            var offsets = new List<Vector3>();
            for (var x = -1; x <= 1; x++)
            {
                for (var z = -1; z <= 1; z++)
                {
                    offsets.Add(new Vector3(x * CropSpread, 0f, z * CropSpread));
                }
            }

            return offsets.ToArray();
        }
    }
}
