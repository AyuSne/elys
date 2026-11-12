using System.Collections.Generic;
using System.Text;

namespace ElysFarm.Core
{
    /// <summary>
    /// 意图解析的提示词。稳定的白名单与前缀放在最前面以命中缓存，
    /// 易变的农场快照与玩家原话放在最后（见技术调研「缓存」一节）。
    /// </summary>
    internal static class IntentPrompt
    {
        static readonly string SystemPrompt = BuildSystemPrompt();

        static string BuildSystemPrompt()
        {
            var speciesList = new StringBuilder();
            var speciesIds = new StringBuilder();
            foreach (var species in CropCatalog.All)
            {
                if (speciesList.Length > 0)
                {
                    speciesList.Append('、');
                    speciesIds.Append('|');
                }

                speciesList.Append($"{species.Id}={species.DisplayName}");
                speciesIds.Append(species.Id);
            }

            var goalIds = string.Join("|", ActionValidator.GoalTokens);

            return "你是「Elys 农场」的意图解析器。你只输出 json，不要解释。\n" +
                "动作白名单：plant=种满一块地、water=浇水、fertilize=施肥、weed=除草、harvest=收获。白名单之外的动作不允许执行。\n" +
                $"品种白名单：{speciesList}。白名单之外的品种种不出来。\n" +
                "地块指称：east=东边、west=西边、north=北边、south=南边、near=最近，也可以直接写地块 id。\n" +
                "输出 json 的格式：\n" +
                "{\"outlet\":\"farm_command|query|chat\",\"goal\":\"" + goalIds + "|none\"," +
                "\"field\":\"地块 id 或 east/west/north/south/near/none\"," +
                $"\"species\":\"{speciesIds}|none\",\"reply\":\"给玩家的一句话\"}}\n" +
                "规则：\n" +
                "1. 玩家要求农活 -> outlet 为 farm_command，并填 goal、field、species；没点名地块就用 near（由 NPC 的效用打分挑地），没点名品种就用 none。\n" +
                "2. 玩家追问状态或原因 -> outlet 为 query，reply 用 NPC 口吻简短回答。\n" +
                "3. 闲聊 -> outlet 为 chat，reply 简短自然。\n" +
                "4. 做不到的要求不要假装能做，在 reply 里用一句话说明理由。\n" +
                "5. 玩家追问时，reply 必须包含具体状态或记忆依据（地块 id、工步、体力、第几天），不能只说套话；引用记忆用「我记得…」。\n" +
                "6. 不要编造记忆里没有的地块或事件。\n" +
                "7. 只按 json 输出，不要输出 markdown 代码块。";
        }

        public static ChatRequest Build(
            FarmWorld world,
            Npc npc,
            string playerText,
            IReadOnlyList<MemoryHit> memories = null)
        {
            var user = new StringBuilder();
            user.Append("农场的真实状态：\n");
            user.Append("地块：");
            for (var i = 0; i < world.Fields.Count; i++)
            {
                var field = world.Fields[i];
                if (i > 0)
                {
                    user.Append("；");
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
            user.Append("可种品种：");
            var first = true;
            foreach (var species in CropCatalog.All)
            {
                if (!first)
                {
                    user.Append('、');
                }

                user.Append($"{species.DisplayName}（{species.Id}，{species.DaysToMature} 个游戏日成熟）");
                first = false;
            }

            user.Append('\n');
            user.Append($"NPC：{npc.DisplayName}（{npc.Id}），体力 {npc.Stamina:P0}，当前位置 x={npc.Position.X:0.#}, z={npc.Position.Z:0.#}。\n");
            user.Append($"NPC 当前意图：{world.IntentOf(npc.Id)}。\n");

            user.Append("检索到的记忆（按权重降序，最多三条）：");
            if (memories == null || memories.Count == 0)
            {
                user.Append("无。\n");
            }
            else
            {
                user.Append('\n');
                foreach (var memory in memories)
                {
                    user.Append($"- 第 {memory.Day} 天：{memory.Text}（权重 {memory.Weight:0.00}）\n");
                }
            }

            user.Append($"玩家说：{playerText}");

            return new ChatRequest(new[]
            {
                new ChatMessage("system", SystemPrompt),
                new ChatMessage("user", user.ToString())
            })
            {
                JsonOutput = true,
                Thinking = true,
                ReasoningEffort = "low",
                Temperature = 0f,
                MaxTokens = 400
            };
        }
    }
}
