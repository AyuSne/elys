using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ElysFarm.Core
{
    /// <summary>
    /// 记忆在对话里的编排：取检索线索的向量、把玩家的话写进记忆、给模型的空泛回答补依据。
    /// 这里不改变地块、时钟或背包；三个出口的分流仍由 <see cref="IntentRouter"/> 负责。
    /// </summary>
    internal static class MemoryContext
    {
        /// <summary>玩家农事指令的记忆重要性：高于日常维护，低于收获。</summary>
        public const int PlayerRequestImportance = 7;

        /// <summary>玩家闲聊的默认记忆重要性；模型给出 importance 时以模型为准。</summary>
        public const int PlayerChatImportance = 4;

        /// <summary>闲聊记忆保留的最大字数；再长就截断，避免一条闲聊挤掉农事记忆。</summary>
        public const int PlayerChatMaxLength = 40;

        /// <summary>
        /// 一次批量补向量的条数上限。只补最近没向量的记忆，避免一次请求过大；
        /// 上限内的记忆补齐后检索才能整段用余弦，否则整体退回关键词。
        /// </summary>
        const int EmbeddingBatchLimit = 64;

        /// <summary>
        /// 取检索线索的向量，并顺手补齐最近记忆缺的向量。任何失败（未配置、断网、超时、
        /// 维度不对）都返回 null，调用方按关键词检索，绝不阻塞对话。
        /// </summary>
        public static async Task<float[]> TryEmbedCueAsync(
            IModelClient client,
            string cue,
            FarmWorld world,
            Npc npc,
            float deadline)
        {
            if (client == null || !client.SupportsEmbeddings || deadline <= 0f)
            {
                return null;
            }

            var pending = world.MemoriesNeedingEmbedding(npc.Id, EmbeddingBatchLimit);
            var inputs = new List<string>(pending.Count + 1) { cue };
            inputs.AddRange(pending);

            using var timeout = new CancellationTokenSource();
            try
            {
                var callTask = client.EmbedAsync(inputs, timeout.Token);
                var winner = await Task.WhenAny(callTask, Task.Delay(TimeSpan.FromSeconds(deadline), timeout.Token));
                if (winner != callTask)
                {
                    timeout.Cancel();
                    Observe(callTask);
                    return null;
                }

                timeout.Cancel();
                var vectors = await callTask;
                if (vectors == null || vectors.Count != inputs.Count)
                {
                    return null;
                }

                for (var i = 0; i < pending.Count; i++)
                {
                    var vector = UsableVector(vectors[i + 1]);
                    if (vector != null)
                    {
                        world.CacheMemoryEmbedding(npc.Id, pending[i], vector);
                    }
                }

                return UsableVector(vectors[0]);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 玩家一次农事指令的记忆文本：用当时解析出的方位与地块，而不是原始句子。
        /// 玩家点名「西边」时即使农场只有一块地，也保留这个方位，跨日追问才对得上。
        /// </summary>
        public static string PlayerRequestMemory(
            FarmWorld world,
            FarmGoal goal,
            Field field,
            CropSpecies species,
            string fieldToken,
            string verb)
        {
            var label = FieldLocator.TryDirectionLabel(fieldToken, out var direction)
                ? $"{direction}（{field.Id}）"
                : world.DescribeField(field.Id);
            switch (goal)
            {
                case FarmGoal.Plant:
                    return $"玩家让我把{label}种满{species?.DisplayName ?? "作物"}";
                case FarmGoal.Harvest:
                    return $"玩家让我收{label}的作物";
                default:
                    return $"玩家让我给{label}{verb}";
            }
        }

        /// <summary>闲聊记忆只保留一行、定长，防止一条长话把提示词撑开。</summary>
        public static string Shorten(string text, int maxLength)
        {
            var value = (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "…";
        }

        /// <summary>玩家闲聊写进记忆的样子：一行、定长，与农事指令的「玩家让我…」区分开。</summary>
        public static string PlayerChatMemory(string text)
        {
            return $"玩家说「{Shorten(text, PlayerChatMaxLength)}」";
        }

        /// <summary>
        /// 模型的追问回答如果既没提状态也没提记忆，补一句本地生成的依据。
        /// 问「上次」时必须是记忆依据，只提当前地块不算答到了过去。
        /// </summary>
        public static string EnsureQueryBasis(
            string reply,
            FarmWorld world,
            Npc npc,
            IReadOnlyList<MemoryHit> memories,
            bool requireMemory,
            string localReply)
        {
            if (string.IsNullOrWhiteSpace(reply))
            {
                return localReply;
            }

            return HasQueryBasis(reply, world, npc, memories, requireMemory)
                ? reply
                : $"{reply.Trim()} {localReply}";
        }

        static bool HasQueryBasis(
            string reply,
            FarmWorld world,
            Npc npc,
            IReadOnlyList<MemoryHit> memories,
            bool requireMemory)
        {
            if (memories != null)
            {
                foreach (var memory in memories)
                {
                    if (reply.Contains($"第 {memory.Day} 天") || reply.Contains(memory.Text))
                    {
                        return true;
                    }
                }
            }

            if (requireMemory)
            {
                return false;
            }

            foreach (var field in world.Fields)
            {
                if (reply.Contains(field.Id))
                {
                    return true;
                }
            }

            foreach (WorkStep step in Enum.GetValues(typeof(WorkStep)))
            {
                if (reply.Contains(WorkStepLabel.Of(step)))
                {
                    return true;
                }
            }

            return reply.Contains("体力") || reply.Contains("心情") || reply.Contains("饥饿");
        }

        static float[] UsableVector(float[] vector)
        {
            if (vector == null || vector.Length == 0)
            {
                return null;
            }

            foreach (var value in vector)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return null;
                }
            }

            return vector;
        }

        static void Observe(Task task)
        {
            task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }
    }
}
