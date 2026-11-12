using System.Collections.Generic;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 把导入的 Quaternius 模型放进场景的统一入口：保留模型自带的 Z 轴校正旋转、
    /// 按包围盒把脚/根落在目标地面，并在材质不是 URP 时补一个 URP/Lit 克隆。
    /// 素材包里的模型 pivot 各不相同（有的在脚底、有的在中心），所以落位只能按包围盒算。
    /// </summary>
    public static class ModelPlacement
    {
        static readonly Dictionary<Material, Material> UrpClones = new Dictionary<Material, Material>();

        /// <summary>
        /// 实例化模型并落到 <paramref name="ground"/>。模型缺失时返回 null（调用方接体块）。
        /// </summary>
        public static GameObject Spawn(
            GameObject model,
            Transform parent,
            Vector3 ground,
            float scale = 1f,
            float yawDegrees = 0f)
        {
            if (model == null)
            {
                return null;
            }

            var instance = Object.Instantiate(model, parent);
            instance.name = model.name;
            instance.transform.localScale = model.transform.localScale * scale;
            instance.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f) * model.transform.rotation;
            instance.transform.position = ground;

            StripSceneObjects(instance);
            EnsureUrpMaterials(instance);
            SnapToGround(instance, ground);
            return instance;
        }

        /// <summary>
        /// FBX 自带 Blender 场景里的相机与灯，不删会盖住主相机画面。
        /// 编辑器导入设置已经关掉了，这里再兜底一次（老资产、手工导入都不会漏）。
        /// </summary>
        static void StripSceneObjects(GameObject instance)
        {
            foreach (var camera in instance.GetComponentsInChildren<Camera>(true))
            {
                Destroy(camera);
            }

            foreach (var light in instance.GetComponentsInChildren<Light>(true))
            {
                Destroy(light);
            }
        }

        /// <summary>按包围盒把模型水平居中到目标点、底部贴到目标点的 y。</summary>
        public static bool SnapToGround(GameObject instance, Vector3 ground)
        {
            if (!TryGetBounds(instance, out var bounds))
            {
                return false;
            }

            instance.transform.position += new Vector3(
                ground.x - bounds.center.x,
                ground.y - bounds.min.y,
                ground.z - bounds.center.z);
            return true;
        }

        /// <summary>模型所有渲染器的世界包围盒；没有渲染器时返回 false。</summary>
        public static bool TryGetBounds(GameObject instance, out Bounds bounds)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        /// <summary>
        /// 运行时兜底：导入的材质若仍是 Standard，就换成一个复制了颜色与贴图的 URP/Lit 材质。
        /// 编辑器里的批量转换见 <see cref="ElysFarm.EditorTools.QuaterniusMaterialPostprocessor"/>。
        /// </summary>
        public static void EnsureUrpMaterials(GameObject instance)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                var changed = false;

                for (var i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || UrpMaterialConverter.IsUrpCompatible(material))
                    {
                        continue;
                    }

                    if (!UrpClones.TryGetValue(material, out var clone))
                    {
                        clone = new Material(material);
                        if (!UrpMaterialConverter.Convert(clone))
                        {
                            Destroy(clone);
                            continue;
                        }

                        clone.name = material.name + " (URP)";
                        UrpClones[material] = clone;
                    }

                    materials[i] = clone;
                    changed = true;
                }

                if (changed)
                {
                    renderer.sharedMaterials = materials;
                }
            }
        }

        /// <summary>用材质属性块给整棵模型染色，不改共享材质；成熟小麦靠它变麦金色。</summary>
        public static void Tint(GameObject instance, Color color)
        {
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>销毁一个运行时对象；Play Mode 用延迟销毁，编辑器里立即销毁。</summary>
        public static void Destroy(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
