using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #12 按 Y 排序：世界 y 越小越靠前，sortingOrder 越大、越晚绘制。
    /// 这是纯整数运算，不打开场景；16 位范围是 SpriteRenderer 的硬限制。
    /// </summary>
    public class SortingOrderTests
    {
        [Test]
        public void y越小排序值越大且不超出16位()
        {
            Assert.Greater(SortingOrder.At(0f), SortingOrder.At(3f), "y 更小的物体应画在前面");
            Assert.Greater(SortingOrder.At(-4f), SortingOrder.At(0f), "更靠南的 NPC 应盖住北边的作物");
            Assert.Less(SortingOrder.Ground, SortingOrder.Soil, "地面在最底，耕地在它上面");
            Assert.Less(SortingOrder.Soil, SortingOrder.At(10f), "耕地低于所有作物/角色");

            var largest = SortingOrder.At(-12f, 5);
            Assert.That(largest, Is.InRange(short.MinValue, short.MaxValue), "排序值必须落在 16 位范围内");
        }
    }
}
