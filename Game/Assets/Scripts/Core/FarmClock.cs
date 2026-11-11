using System;

namespace ElysFarm.Core
{
    /// <summary>
    /// 游戏时钟。一个游戏日等于 5 分钟真实时间，提供 1x / 4x / 16x 加速。
    /// 作物生长与 NPC 作息都以游戏日为标尺。
    /// </summary>
    public sealed class FarmClock
    {
        /// <summary>一个游戏日对应的真实秒数。</summary>
        public const float SecondsPerDay = 300f;

        /// <summary>加速倍率，只接受 <see cref="AllowedSpeeds"/> 中的值。</summary>
        public static readonly float[] AllowedSpeeds = { 1f, 4f, 16f };

        public FarmClock(float speed = 1f)
        {
            Speed = speed;
        }

        float speed = 1f;

        /// <summary>当前加速倍率。</summary>
        public float Speed
        {
            get => speed;
            set
            {
                if (Array.IndexOf(AllowedSpeeds, value) < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "加速倍率只接受 1x / 4x / 16x");
                }

                speed = value;
            }
        }

        /// <summary>当前游戏日，从 1 开始。</summary>
        public int Day { get; private set; } = 1;

        /// <summary>当前这一天已经过去的真实秒数。</summary>
        public float DaySeconds { get; private set; }

        /// <summary>当天进度 0..1。</summary>
        public float DayProgress => DaySeconds / SecondsPerDay;

        /// <summary>一个游戏日结算完毕时触发，参数为新的游戏日。</summary>
        public event Action<int> DayAdvanced;

        /// <summary>
        /// 推进时间。返回本次推进跨过了几个游戏日（通常为 0 或 1）。
        /// </summary>
        public int Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
            {
                return 0;
            }

            DaySeconds += deltaSeconds * Speed;

            var advanced = 0;
            while (DaySeconds >= SecondsPerDay)
            {
                DaySeconds -= SecondsPerDay;
                Day++;
                advanced++;
                DayAdvanced?.Invoke(Day);
            }

            return advanced;
        }
    }
}
