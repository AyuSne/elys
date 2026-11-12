using UnityEngine;

namespace ElysFarm.Demo
{
    /// <summary>像素精灵对象的统一工厂：挂 SpriteRenderer、设排序；位置由调用方决定世界或局部。</summary>
    public static class SpriteObject
    {
        public static SpriteRenderer Create(string name, Transform parent, Sprite sprite, int sortingOrder)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            var renderer = gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = sortingOrder;
            return renderer;
        }
    }
}
