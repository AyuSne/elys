using System.Collections.Generic;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 体块回退用的材质与方块工厂。像素素材关掉（或缺失）时，地块、作物与角色都退回这些体块，
    /// 领域状态不变。运行时创建的材质会被记录下来，重建表现层时一起销毁，避免反复切换泄漏。
    /// </summary>
    public static class DemoMaterials
    {
        /// <summary>URP 的 Unlit：体块不参与光照，颜色就是看到的颜色。</summary>
        public const string BlockShaderName = "Universal Render Pipeline/Unlit";

        static readonly Color DrySoil = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color WetSoil = new Color(0.24f, 0.17f, 0.12f);
        static readonly List<Material> Created = new List<Material>();

        public static Color SoilColor(float water)
        {
            return Color.Lerp(DrySoil, WetSoil, Mathf.Clamp01(water));
        }

        public static Material Create(string name, Color color)
        {
            var shader = Shader.Find(BlockShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            var material = new Material(shader) { name = name };
            SetColor(material, color);
            Created.Add(material);
            return material;
        }

        /// <summary>销毁所有运行时创建的材质；重建表现层前调用。</summary>
        public static void DestroyCreated()
        {
            foreach (var material in Created)
            {
                if (material == null)
                {
                    continue;
                }

                DestroyObject(material);
            }

            Created.Clear();
        }

        public static GameObject Block(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            return Primitive(PrimitiveType.Cube, name, parent, position, scale, color);
        }

        public static GameObject Cylinder(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            return Primitive(PrimitiveType.Cylinder, name, parent, position, scale, color);
        }

        static GameObject Primitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.position = position;
            primitive.transform.localScale = scale;
            primitive.GetComponent<Renderer>().sharedMaterial = Create(name, color);

            var collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                DestroyObject(collider);
            }

            return primitive;
        }

        public static void SetColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }
        }

        /// <summary>运行时对象销毁：Play Mode 用延迟销毁，编辑器里立即销毁。</summary>
        public static void DestroyObject(Object target)
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
