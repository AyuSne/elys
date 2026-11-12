using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的材质与体块工厂。URP 里 <see cref="GameObject.CreatePrimitive"/> 的默认材质
    /// 属于内置管线（粉色），所以每个运行时创建的方块都必须显式赋 URP 材质。
    /// </summary>
    public static class DemoMaterials
    {
        static readonly Color DrySoil = new Color(0.47f, 0.34f, 0.23f);
        static readonly Color WetSoil = new Color(0.24f, 0.17f, 0.12f);

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

            return material;
        }

        public static GameObject Block(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.position = position;
            block.transform.localScale = scale;
            block.GetComponent<Renderer>().sharedMaterial = Create(name, color);
            return block;
        }

        public static GameObject Cylinder(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.position = position;
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<Renderer>().sharedMaterial = Create(name, color);
            return cylinder;
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
