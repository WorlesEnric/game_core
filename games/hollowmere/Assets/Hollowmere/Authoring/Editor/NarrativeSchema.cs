// Hollowmere - the JSON shape of narrative entries (conditions, actions, rule triggers, quest objectives and rewards)
// for the Studio `create` / `set` operations of P3.1.
//
// P1.4 stored what an entry is about in one reference (`subject` / `target` / `triggerSubject`). P1.7b splits it into
// one typed reference per kind (`fact`, `item`, `quest`, `region`, ...; `triggerFact`, `triggerItem`, ...). The shape is
// chosen from the compiled types, so the same authoring code writes either schema. In the typed schema every typed
// field is written (the chosen one with the path, the others null) because a serialized list that grows copies the
// previous element into the new slot.
#nullable enable
using System;
using System.Reflection;
using GameCore.Gameplay.Logic;
using Newtonsoft.Json.Linq;

namespace Hollowmere.Authoring
{
    /// <summary>Writes narrative entries in the schema the compiled packages declare.</summary>
    public static class NarrativeSchema
    {
        /// <summary>True when the packages declare the P1.7b typed references.</summary>
        public static bool Typed => typeof(ActionEntry).GetField("fact", BindingFlags.Public | BindingFlags.Instance) != null;

        private static readonly string[] ConditionFields = { "fact", "item", "quest", "region", "graph", "rule", "conditionSet" };
        private static readonly string[] ActionFields = { "fact", "item", "quest", "region", "graph", "actionSet", "worldItem", "vendor" };
        private static readonly string[] TriggerFields = { "triggerFact", "triggerItem", "triggerVendor", "triggerQuest", "triggerGraph", "triggerRegion", "triggerRule", "triggerWorldItem" };
        private static readonly string[] ObjectiveFields = { "graph", "item", "region", "fact" };
        private static readonly string[] RewardFields = { "item", "fact" };

        public static string ConditionField(string kind)
        {
            switch (kind)
            {
                case "Fact": return "fact";
                case "ItemCount": return "item";
                case "QuestStatus":
                case "QuestStage":
                case "QuestBranch":
                case "ObjectiveDone": return "quest";
                case "Region": return "region";
                case "NodeVisited": return "graph";
                case "RuleFired": return "rule";
                case "ConditionSet": return "conditionSet";
                default: return string.Empty;
            }
        }

        public static string ActionField(string kind)
        {
            switch (kind)
            {
                case "SetFact":
                case "AddFact": return "fact";
                case "Grant":
                case "Consume":
                case "Buy": return "item";
                case "StartQuest":
                case "AdvanceQuest":
                case "CompleteQuest":
                case "FailQuest": return "quest";
                case "Travel": return "region";
                case "StartDialogue": return "graph";
                case "RunActionSet": return "actionSet";
                case "Pickup": return "worldItem";
                default: return string.Empty;
            }
        }

        public static string TriggerField(string trigger)
        {
            switch (trigger)
            {
                case "FactSet": return "triggerFact";
                case "ItemGranted":
                case "ItemConsumed": return "triggerItem";
                case "TradeDone": return "triggerVendor";
                case "QuestStarted":
                case "StageEntered":
                case "QuestCompleted":
                case "QuestFailed": return "triggerQuest";
                case "DialogueEnded":
                case "ChoiceMade": return "triggerGraph";
                case "RegionEntered": return "triggerRegion";
                case "RuleFired": return "triggerRule";
                case "ItemPickedUp": return "triggerWorldItem";
                default: return string.Empty;
            }
        }

        public static string ObjectiveField(string kind)
        {
            switch (kind)
            {
                case "Talk": return "graph";
                case "Collect": return "item";
                case "Reach": return "region";
                case "Fact": return "fact";
                default: return string.Empty;
            }
        }

        /// <summary>Writes the subject of an entry into <paramref name="entry"/> (legacy field or typed fields).</summary>
        public static void Subject(JObject entry, string legacyField, string[] typedFields, string typedField, string? path)
        {
            if (!Typed)
            {
                entry[legacyField] = StudioAuthor.Str(path);
                return;
            }

            foreach (string field in typedFields)
            {
                entry[field] = JValue.CreateNull();
            }

            if (typedField.Length > 0)
            {
                entry[typedField] = StudioAuthor.Str(path);
            }
            else if (path != null)
            {
                throw new InvalidOperationException("P3.1: an entry of this kind takes no subject (" + path + ")");
            }
        }

        public static void Condition(JObject entry, string kind, string? path) => Subject(entry, "subject", ConditionFields, ConditionField(kind), path);

        /// <summary>An action subject; <paramref name="vendor"/> is the vendor of a Buy (typed schema only).</summary>
        public static void Action(JObject entry, string kind, string? path, string? vendor = null)
        {
            Subject(entry, "target", ActionFields, ActionField(kind), path);
            if (Typed && vendor != null)
            {
                entry["vendor"] = vendor;
            }
        }

        public static void Trigger(JObject entry, string trigger, string? path) => Subject(entry, "triggerSubject", TriggerFields, TriggerField(trigger), path);

        public static void Objective(JObject entry, string kind, string? path) => Subject(entry, "target", ObjectiveFields, ObjectiveField(kind), path);

        public static void Reward(JObject entry, string kind, string? path) => Subject(entry, "target", RewardFields, kind == "Item" ? "item" : "fact", path);
    }
}
