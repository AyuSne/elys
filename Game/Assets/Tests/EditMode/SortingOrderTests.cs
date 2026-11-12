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

        /// <summary>#13：土路铺在草地上，耕地画在土路之上。</summary>
        [Test]
        public void 土路铺在草地之上耕地在土路之上()
        {
            Assert.Less(SortingOrder.Ground, SortingOrder.Road, "土路铺在草地上");
            Assert.Less(SortingOrder.Road, SortingOrder.Soil, "耕地画在土路之上");
        }

        /// <summary>
        /// #13：人和建筑、树按 Y 排。站在地标/装饰北侧的人先画、被挡住；
        /// 站到南侧就画在它前面。这是「遮挡关系正确」的可断言部分。
        /// </summary>
        [Test]
        public void 站在地标与装饰北侧的人被挡住()
        {
            foreach (var landmark in FarmLayout.Landmarks)
            {
                AssertOccludesAt(landmark.SortBase, landmark.Kind.ToString());
            }

            foreach (var decoration in FarmLayout.Decorations)
            {
                AssertOccludesAt(decoration.SortBase, decoration.Kind.ToString());
            }
        }

        static void AssertOccludesAt(float sortBase, string what)
        {
            var order = SortingOrder.At(sortBase);

            Assert.Greater(order, SortingOrder.Soil, $"{what} 要画在耕地之上");
            Assert.Less(
                SortingOrder.At(sortBase + 0.5f),
                order,
                $"站在 {what} 北侧（身后）的人应先画，被它挡住");
            Assert.Greater(
                SortingOrder.At(sortBase - 0.5f),
                order,
                $"站在 {what} 南侧（身前）的人应后画，盖住它");
        }
    }
}
