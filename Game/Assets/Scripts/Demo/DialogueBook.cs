using System;
using System.Collections.Generic;
using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 演示层每名 NPC 一份对话：玩家的一句话与 NPC 的回话记成一「轮」，
    /// 切换指挥对象时恢复对应那一份，只保留最近 <see cref="MaxRounds"/> 轮。
    /// 自己不是领域状态：不引用 <see cref="FarmWorld"/>，只供对话框与提示词使用。
    /// </summary>
    public sealed class DialogueBook
    {
        /// <summary>每名 NPC 保留的最大轮数；第 7 轮挤掉最旧的一轮。</summary>
        public const int MaxRounds = 6;

        readonly Dictionary<string, List<DialogueRound>> books =
            new Dictionary<string, List<DialogueRound>>(StringComparer.Ordinal);

        string currentNpcId;

        /// <summary>当前这个人最近的一轮；没聊过时为 null，对话框据此显示中性提示。</summary>
        public DialogueRound? CurrentRound
        {
            get
            {
                var rounds = RoundsOrNull(currentNpcId);
                return rounds != null && rounds.Count > 0 ? rounds[rounds.Count - 1] : (DialogueRound?)null;
            }
        }

        /// <summary>翻到某名 NPC 的对话；传 null 表示还没翻到任何人。不改变任何一轮的内容。</summary>
        public void SwitchTo(string npcId)
        {
            currentNpcId = npcId;
        }

        /// <summary>
        /// 记下一轮；必须传这一轮实际是说给谁的——异步回话可能晚于切换，
        /// 不能记到当时翻到的那个人名下。无名可记时抛异常。
        /// </summary>
        public void Record(string npcId, DialogueRound round)
        {
            if (string.IsNullOrEmpty(npcId))
            {
                throw new ArgumentException("记一轮对话必须知道是说给谁的。", nameof(npcId));
            }

            if (!books.TryGetValue(npcId, out var rounds))
            {
                rounds = new List<DialogueRound>(MaxRounds);
                books.Add(npcId, rounds);
            }

            rounds.Add(round);
            if (rounds.Count > MaxRounds)
            {
                rounds.RemoveAt(0);
            }
        }

        /// <summary>某名 NPC 保留的轮次，最旧在前；没聊过时为空。</summary>
        public IReadOnlyList<DialogueRound> RoundsOf(string npcId)
        {
            return RoundsOrNull(npcId) ?? (IReadOnlyList<DialogueRound>)Array.Empty<DialogueRound>();
        }

        List<DialogueRound> RoundsOrNull(string npcId)
        {
            return npcId != null && books.TryGetValue(npcId, out var rounds) ? rounds : null;
        }
    }

    /// <summary>一轮对话：玩家当时说的话、NPC 的回话，以及 HUD 用来恢复表情与提示的两个注脚。</summary>
    public readonly struct DialogueRound
    {
        public DialogueRound(
            string playerText,
            string reply,
            string understood,
            IntentOutlet outlet,
            bool accepted)
        {
            PlayerText = playerText;
            Reply = reply;
            Understood = understood;
            Outlet = outlet;
            Accepted = accepted;
        }

        public string PlayerText { get; }

        public string Reply { get; }

        /// <summary>「理解成…」一行；切换回来时连同这句话一起恢复。</summary>
        public string Understood { get; }

        public IntentOutlet Outlet { get; }

        public bool Accepted { get; }
    }
}
