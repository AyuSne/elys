namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层的自主开关（#29）：开局待命，玩家还没碰过开关时第一条被接受的农事指令
    /// 自动恢复自主一次，之后只听玩家的；剧本回放永不打开。纯逻辑，不读领域世界，也不依赖场景。
    /// </summary>
    public sealed class AutonomySwitch
    {
        bool autoResumeUsed;

        /// <summary>当前是否让 NPC 自己找活干。</summary>
        public bool Enabled { get; private set; }

        /// <summary>
        /// 玩家按了一下开关：打开或暂停自主。按过开关就代表玩家接管了这件事，
        /// 开局那次自动恢复随之作废——「之后由玩家说了算」。
        /// </summary>
        public void Toggle()
        {
            autoResumeUsed = true;
            Enabled = !Enabled;
        }

        /// <summary>
        /// 一条被接受的农事指令。玩家还没碰过开关时，开局第一次替玩家恢复自主（保留原有演示节奏）；
        /// 碰过开关之后不再自动打开，要玩家再点一次开关才恢复。
        /// </summary>
        public void OnAcceptedOrder()
        {
            if (autoResumeUsed)
            {
                return;
            }

            autoResumeUsed = true;
            Enabled = true;
        }

        /// <summary>
        /// 剧本回放：自主永不打开，连开局那次自动恢复也一并作废。
        /// 回放里接单仍会发生，但证据不能因为「其余 NPC 自己找活」而改变。
        /// </summary>
        public void DisableForReplay()
        {
            Enabled = false;
            autoResumeUsed = true;
        }

        /// <summary>
        /// 压测姿态：程序化打开自主。已经有人接管开关，开局那次自动恢复一并作废。
        /// </summary>
        public void Enable()
        {
            Enabled = true;
            autoResumeUsed = true;
        }
    }
}
