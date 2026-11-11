using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ElysFarm.Core
{
    /// <summary>
    /// 模型或本地解析器给出的一份「草稿」：出口、目标、地块与品种的原始指称，以及回应。
    /// 它还不是可执行的工单——地块、品种、动作白名单都要在 <see cref="IntentRouter"/> 里再解析与校验。
    /// </summary>
    internal sealed class IntentDraft
    {
        public IntentOutlet Outlet { get; private set; }

        public FarmGoal? Goal { get; private set; }

        /// <summary>地块的原始指称，例如 east / A1 / null（未指定，交给效用打分挑地）。</summary>
        public string FieldToken { get; private set; }

        /// <summary>品种的原始指称，例如 radish / null（用 NPC 当前打算种的品种）。</summary>
        public string SpeciesToken { get; private set; }

        public string Reply { get; private set; }

        public bool IsRefusal { get; private set; }

        public static IntentDraft Command(FarmGoal goal, string fieldToken, string speciesToken, string reply)
        {
            return new IntentDraft
            {
                Outlet = IntentOutlet.FarmCommand,
                Goal = goal,
                FieldToken = fieldToken,
                SpeciesToken = speciesToken,
                Reply = reply
            };
        }

        public static IntentDraft Read(IntentOutlet outlet, string reply)
        {
            return new IntentDraft { Outlet = outlet, Reply = reply };
        }

        public static IntentDraft Refuse(string reason)
        {
            return new IntentDraft
            {
                Outlet = IntentOutlet.FarmCommand,
                IsRefusal = true,
                Reply = reason
            };
        }
    }

    /// <summary>解析模型返回的 JSON 意图。格式错误返回 false（调用方降级），语义非法返回一句拒绝。</summary>
    internal static class ModelIntentParser
    {
        public static bool TryParse(string content, out IntentDraft draft, out string error)
        {
            draft = null;
            error = null;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "模型返回了空内容";
                return false;
            }

            JObject json;
            try
            {
                json = JObject.Parse(content);
            }
            catch (Exception)
            {
                error = "模型返回的不是合法 JSON";
                return false;
            }

            var outlet = (string)json["outlet"];
            switch (outlet)
            {
                case "farm_command":
                case "farm":
                    return TryParseCommand(json, out draft, out error);

                case "query":
                case "chit_chat":
                case "chat":
                    draft = IntentDraft.Read(
                        outlet == "query" ? IntentOutlet.Query : IntentOutlet.Chat,
                        (string)json["reply"]);
                    return true;

                default:
                    error = "模型返回了未知出口";
                    return false;
            }
        }

        static bool TryParseCommand(JObject json, out IntentDraft draft, out string error)
        {
            error = null;
            var goalText = (string)json["goal"];
            var field = (string)json["field"];
            var species = (string)json["species"];
            var reply = (string)json["reply"];

            if (string.IsNullOrWhiteSpace(goalText) || goalText == "none")
            {
                draft = IntentDraft.Refuse("你想让我做什么？");
                return true;
            }

            if (!ActionValidator.TryParseGoal(goalText, out var goal))
            {
                draft = IntentDraft.Refuse($"「{goalText}」这个活我还不会干。");
                return true;
            }

            draft = IntentDraft.Command(goal, NormalizeNone(field), NormalizeNone(species), reply);
            return true;
        }

        static string NormalizeNone(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return value == "none" || value == "null" ? null : value.Trim();
        }
    }
}
