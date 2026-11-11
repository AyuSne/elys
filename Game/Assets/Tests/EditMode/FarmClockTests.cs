using ElysFarm.Core;
using NUnit.Framework;

namespace ElysFarm.Tests
{
    /// <summary>
    /// 覆盖 ALIGNMENT.md 里「时间功能正常」这条要求。
    /// 时间以游戏日为标尺：一个游戏日 5 分钟真实时间，可 1x / 4x / 16x 加速。
    /// </summary>
    public class FarmClockTests
    {
        [Test]
        public void 一天等于五分钟真实时间()
        {
            var clock = new FarmClock();

            clock.Tick(FarmClock.SecondsPerDay - 1f);
            Assert.AreEqual(1, clock.Day, "差一秒还不该跨日");

            clock.Tick(1f);
            Assert.AreEqual(2, clock.Day, "满 5 分钟应进入第二天");
        }

        [Test]
        public void 加速会按倍率推进()
        {
            var clock = new FarmClock { Speed = 16f };

            clock.Tick(FarmClock.SecondsPerDay / 16f);
            Assert.AreEqual(2, clock.Day, "16 倍速下 1/16 天应跨日");
        }

        [Test]
        public void 一次大步长可跨越多天()
        {
            var clock = new FarmClock();

            var advanced = clock.Tick(FarmClock.SecondsPerDay * 5f);

            Assert.AreEqual(5, advanced);
            Assert.AreEqual(6, clock.Day);
        }

        [Test]
        public void 跨日事件按次触发()
        {
            var clock = new FarmClock();
            var count = 0;
            clock.DayAdvanced += _ => count++;

            clock.Tick(FarmClock.SecondsPerDay * 3f);

            Assert.AreEqual(3, count);
        }

        [Test]
        public void 非正数时间被忽略()
        {
            var clock = new FarmClock();
            Assert.AreEqual(0, clock.Tick(0f));
            Assert.AreEqual(0, clock.Tick(-1f));
            Assert.AreEqual(1, clock.Day);
        }
    }
}
