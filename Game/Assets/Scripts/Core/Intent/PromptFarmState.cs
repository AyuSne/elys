using System.Collections.Generic;
using System.Text;

namespace ElysFarm.Core
{
    /// <summary>
    /// 两份提示词共用的农场状态段：地块、NPC 状态与意图、检索到的记忆。
    /// 分类提示词与表达层提示词都从这里取，免得同一份状态在两边写成两种说法。
    /// </summary>
    internal static class PromptFarmState
    {
        /// <summary>地块一览：id、位置、状态与其中的作物。</summary>
        public static void AppendFields(StringBuilder user, FarmWorld world)
        {
            user.Append("地块：");
            for (var i = 0; i < world.Fields.Count; i++)
            {
                var field = world.Fields[i];
                if (i > 0)
                {
                    user.Append('；');
                }

                user.Append($"{field.Id}（x={field.Position.X:0.#}, z={field.Position.Z:0.#}，");
                user.Append(field.State);
                if (field.Crop != null)
                {
                    user.Append($"，{field.Crop.DisplayName}");
                }

                user.Append('）');
            }

            if (world.Fields.Count == 0)
            {
                user.Append("无");
            }

            user.Append('\n');
        }

        /// <summary>NPC 的状态与当前意图。</summary>
        public static void AppendNpc(StringBuilder user, FarmWorld world, Npc npc)
        {
            user.Append($"NPC：{npc.DisplayName}（{npc.Id}），体力 {npc.Stamina:P0}，心情 {npc.Mood:P0}，");
            user.Append($"饥饿 {npc.Hunger:P0}，当前位置 x={npc.Position.X:0.#}, z={npc.Position.Z:0.#}。\n");
            user.Append($"NPC 当前意图：{world.IntentOf(npc.Id)}。\n");
        }

        /// <summary>检索命中的记忆，按权重降序，最多三条。</summary>
        public static void AppendMemories(StringBuilder user, IReadOnlyList<MemoryHit> memories)
        {
            user.Append("检索到的记忆（按权重降序，最多三条）：");
            if (memories == null || memories.Count == 0)
            {
                user.Append("无。\n");
                return;
            }

            user.Append('\n');
            foreach (var memory in memories)
            {
                user.Append($"- 第 {memory.Day} 天：{OneLine(memory.Text)}（权重 {memory.Weight:0.00}）\n");
            }
        }

        /// <summary>记忆与台词里的换行会破坏提示词的行结构，渲染前压成一行。</summary>
        public static string OneLine(string text)
        {
            return (text ?? string.Empty)
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
        }
    }
}
