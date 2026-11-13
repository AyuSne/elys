using System;
using System.Collections.Generic;

namespace ElysFarm.Core
{
    /// <summary>
    /// 表达层提示词里的一轮对话：玩家当时说的话与 NPC 的回话。
    /// 演示层的对话簿还带「理解成」这类界面注脚，进提示词的只有这两项。
    /// </summary>
    public readonly struct DialogueTurn
    {
        public DialogueTurn(string playerText, string reply)
        {
            PlayerText = playerText ?? string.Empty;
            Reply = reply ?? string.Empty;
        }

        /// <summary>玩家当时说的话。</summary>
        public string PlayerText { get; }

        /// <summary>NPC 当时的回话。</summary>
        public string Reply { get; }
    }

    /// <summary>
    /// 一次对话的表达层上下文：口吻与最近几轮。口吻与对话簿都住在演示层，
    /// 这里只是把两者装在一起交给 <see cref="IntentRouter"/>；路由器既不生成也不保存它们。
    /// </summary>
    public sealed class DialogueContext
    {
        /// <summary>表达层最多带上的轮数；对话簿也按这个数封顶，两处不能各写一个。</summary>
        public const int MaxRounds = 6;

        /// <summary>没有口吻也没有历史时的上下文：离线、剧本回放与不带上下文的测试用。</summary>
        public static readonly DialogueContext None = new DialogueContext(null);

        public DialogueContext(string tone, IReadOnlyList<DialogueTurn> history = null)
        {
            Tone = tone ?? string.Empty;
            History = Recent(history);
        }

        /// <summary>NPC 的口吻；空表示按平常口气说话。</summary>
        public string Tone { get; }

        /// <summary>最近几轮，最旧在前；最多 <see cref="MaxRounds"/> 轮。</summary>
        public IReadOnlyList<DialogueTurn> History { get; }

        static IReadOnlyList<DialogueTurn> Recent(IReadOnlyList<DialogueTurn> history)
        {
            if (history == null || history.Count == 0)
            {
                return Array.Empty<DialogueTurn>();
            }

            if (history.Count <= MaxRounds)
            {
                return history;
            }

            var recent = new DialogueTurn[MaxRounds];
            for (var i = 0; i < MaxRounds; i++)
            {
                recent[i] = history[history.Count - MaxRounds + i];
            }

            return recent;
        }
    }
}
