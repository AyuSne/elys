using ElysFarm.Core;

namespace ElysFarm.Demo
{
    /// <summary>
    /// 三名 NPC 的口吻：表达层提示词里的用词、句子长短、语气、口头禅与禁忌（见 GLOSSARY「口吻」）。
    /// 口吻只影响说法，不改决策参数——决定快慢、体力消耗与效用权重仍在 <see cref="NpcPersona"/>；
    /// 它住在演示层，不写进 <see cref="Npc"/>。
    /// </summary>
    public static class NpcTones
    {
        /// <summary>阿禾：稳、勤、话少。</summary>
        public const string Ahe =
            "说话简短，两三句说完就去干活；用词朴素，常说「行」「我这就去」「地里的活不等人」；" +
            "语气平，不寒暄、不打听别人的事、不撒娇；不许拿农活开玩笑，也不许敷衍。";

        /// <summary>阿芽：快、贫嘴、爱甩锅。</summary>
        public const string Aya =
            "说话快、爱贫嘴，先嬉皮笑脸一句再办正事；常用「诶」「得嘞」「你可真会挑时候」；" +
            "句子短、节奏跳，会自嘲，也会小小抱怨两句；不许说教，不许一本正经打官腔。";

        /// <summary>阿土：慢、懂多、爱打听。</summary>
        public const string Atu =
            "说话慢条斯理，爱打听也爱转述别人家的事；常用「诶我说」「你听说没」「可不是嘛」；" +
            "句子长一点，说完常顺带问一句；不许说重话，不许编别人没说过的话。";

        /// <summary>没匹配到人设时的中性口吻：仍然按两三句说，不装成任何一名 NPC。</summary>
        public const string Neutral = "平常自然的口吻，两三句说完。";

        /// <summary>按 NPC 的人设取口吻；不认识的人设给中性口吻。</summary>
        public static string Of(Npc npc)
        {
            return Of(npc?.Persona);
        }

        /// <summary>按人设取口吻；与三份预设人设对不上时给中性口吻。</summary>
        public static string Of(NpcPersona persona)
        {
            if (persona == null)
            {
                return Neutral;
            }

            if (persona.Id == NpcPersona.Ahe.Id)
            {
                return Ahe;
            }

            if (persona.Id == NpcPersona.Aya.Id)
            {
                return Aya;
            }

            if (persona.Id == NpcPersona.Atu.Id)
            {
                return Atu;
            }

            return Neutral;
        }
    }
}
