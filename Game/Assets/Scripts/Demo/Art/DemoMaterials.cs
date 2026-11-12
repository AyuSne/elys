using System.Collections.Generic;
using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的材质与体块工厂。URP 里 <see cref="GameObject.CreatePrimitive"/> 的默认材质
    /// 属于内置管线（粉色），所以每个运行时创建的方块都必须显式赋 URP 材质。
    /// 运行时创建的材质会被记录下来，重建表现层（素材开关）时一起销毁，避免反复切换泄漏。
    /// </summary>
    public static class DemoMaterials
    {
        static readonly Color DrySoil = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color WetSoil = new Color(0.24f, 0.17f, 0.12f);
        static readonly List<Material> Created = new List<Material>();

        public static Color SoilColor(float water)
        {
            return Color.Lerp(DrySoil, WetSoil, Mathf.Clamp01(water));
        }

        public static Material Create(string name, Color color)
        {
            var shader = Shader.Find(UrpMaterialConverter.UrpLitShaderName);
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
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

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", 0.08f);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", 0f);
            }

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

                if (Application.isPlaying)
                {
                    Object.Destroy(material);
                }
                else
                {
                    Object.DestroyImmediate(material);
                }
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
    }
}
