using ElysFarm.Demo;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// #29 的自主开关：开局待命，第一条被接受的农事指令自动恢复自主一次，之后只听玩家的；
    /// 剧本回放永不打开自主。纯逻辑，不开场景；开关按钮与 HUD 的接线仍按 Play Mode 验收。
    /// </summary>
    public class AutonomySwitchTests
    {
        [Test]
        public void 开局待命_第一条被接受的农事指令自动恢复自主()
        {
            var autonomy = new AutonomySwitch();
            Assert.IsFalse(autonomy.Enabled, "开局三名 NPC 都应待命");

            autonomy.OnAcceptedOrder();

            Assert.IsTrue(autonomy.Enabled, "开局第一次接单会自动恢复自主（#29 保留现有行为）");
        }

        [Test]
        public void 手动暂停之后接单不再自动打开_再点开关才恢复()
        {
            var autonomy = new AutonomySwitch();
            autonomy.OnAcceptedOrder();
            autonomy.Toggle();

            Assert.IsFalse(autonomy.Enabled, "点一下开关回到待命");
            autonomy.OnAcceptedOrder();
            Assert.IsFalse(autonomy.Enabled, "开局那次自动恢复已经用掉；之后的接单不再替玩家打开");

            autonomy.Toggle();
            Assert.IsTrue(autonomy.Enabled, "再点一次开关才恢复自主");
        }

        [Test]
        public void 开局先按过开关_第一次接单也不再自动恢复()
        {
            var autonomy = new AutonomySwitch();
            autonomy.Toggle();
            autonomy.Toggle();

            Assert.IsFalse(autonomy.Enabled, "玩家已经按回待命");
            autonomy.OnAcceptedOrder();
            Assert.IsFalse(autonomy.Enabled, "玩家按过开关之后，接单不该再替他打开");
        }

        [Test]
        public void 剧本回放永不打开自主()
        {
            var autonomy = new AutonomySwitch();
            autonomy.DisableForReplay();
            autonomy.OnAcceptedOrder();

            Assert.IsFalse(autonomy.Enabled, "剧本回放的接单也不能打开自主，三条证据才逐字一致");
        }
    }
}
