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
        /// <summary>每名 NPC 保留的最大轮数；第 7 轮挤掉最旧的一轮，与表达层的上下文上限是同一个数。</summary>
        public const int MaxRounds = DialogueContext.MaxRounds;

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

        /// <summary>
        /// 表达层要的最近几轮：去掉「理解成」这类界面注脚，只留一问一答，最旧在前。
        /// 返回的是另一份列表，调用方改不到对话簿里的那一份。
        /// </summary>
        public IReadOnlyList<DialogueTurn> HistoryOf(string npcId)
        {
            var rounds = RoundsOrNull(npcId);
            if (rounds == null || rounds.Count == 0)
            {
                return Array.Empty<DialogueTurn>();
            }

            var turns = new DialogueTurn[rounds.Count];
            for (var i = 0; i < rounds.Count; i++)
            {
                turns[i] = rounds[i].Turn;
            }

            return turns;
        }

        List<DialogueRound> RoundsOrNull(string npcId)
        {
            return npcId != null && books.TryGetValue(npcId, out var rounds) ? rounds : null;
        }
    }

    /// <summary>
    /// 一轮对话：玩家当时说的话、NPC 的回话，以及 HUD 用来恢复表情与提示的几个注脚。
    /// 进表达层提示词的只有 <see cref="Turn"/> 那一对问答。
    /// </summary>
    public readonly struct DialogueRound
    {
        public DialogueRound(
            DialogueTurn turn,
            string understood,
            IntentOutlet outlet,
            bool accepted,
            bool usedFallback = false)
        {
            Turn = turn;
            Understood = understood;
            Outlet = outlet;
            Accepted = accepted;
            UsedFallback = usedFallback;
        }

        /// <summary>玩家原话与 NPC 回话；表达层提示词直接取这一对。</summary>
        public DialogueTurn Turn { get; }

        /// <summary>玩家当时说的话。</summary>
        public string PlayerText => Turn.PlayerText;

        /// <summary>NPC 当时的回话。</summary>
        public string Reply => Turn.Reply;

        /// <summary>「理解成…」一行；切换回来时连同这句话一起恢复。</summary>
        public string Understood { get; }

        public IntentOutlet Outlet { get; }

        public bool Accepted { get; }

        /// <summary>这句话是本地模板说的（模型不可用或表达层没给出可用台词）；HUD 据此标出「本地模板」。</summary>
        public bool UsedFallback { get; }
    }
}
