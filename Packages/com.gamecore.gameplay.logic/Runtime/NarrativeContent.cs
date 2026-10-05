// GameCore.Gameplay.Logic - narrative content to pure models (P1.4).
//
// NarrativeConversion turns definition assets into the models of GameCore.Rules.Gameplay (NarrativeModelSet). Each
// narrative package contributes one INarrativeContentConverter for its own definition kinds; the logic converter
// handles facts (any IFactDefinition), condition sets, action sets and rules, and offers the shared resolution every
// converter uses: the key of a referenced asset, memoised condition/action set models (nested action sets flattened),
// inline condition/action lists with derived keys. The same conversion runs in the bake (to validate and report) and
// at boot (from the content manifest).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Logic
{
    /// <summary>Converts the definitions of one narrative package into models.</summary>
    public interface INarrativeContentConverter
    {
        /// <summary>True for the definition kinds this converter owns.</summary>
        bool CanConvert(ScriptableObject asset);

        /// <summary>Adds the asset's model(s) to <paramref name="conversion"/>.Models; problems go to Problem.</summary>
        void Convert(ScriptableObject asset, NarrativeConversion conversion);
    }

    /// <summary>Resolution of asset references to keys and labels.</summary>
    public static class NarrativeRefs
    {
        /// <summary>The key of a referenced asset: a fact's name key, an authored object's stable key, else 0.</summary>
        public static int KeyOf(UnityEngine.Object? asset)
        {
            if (asset == null)
            {
                return 0;
            }

            if (asset is IFactDefinition fact)
            {
                return NarrativeKeys.IsValidFactName(fact.FactName) ? NarrativeKeys.FactKey(fact.FactName) : 0;
            }

            if (asset is IAuthoredObject authored && AuthoringIds.IsValid(authored.AuthoringId))
            {
                return AuthoringIds.StableKey(authored.AuthoringId);
            }

            return 0;
        }

        /// <summary>The key of an entity authoring id, or 0.</summary>
        public static int EntityKey(string? authoringId) =>
            AuthoringIds.IsValid(authoringId) ? AuthoringIds.StableKey(authoringId!) : 0;

        /// <summary>A readable label of a referenced asset (<c>fact bell_rung</c>, <c>Old Coin</c>).</summary>
        public static string LabelOf(UnityEngine.Object? asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }

            if (asset is IFactDefinition fact)
            {
                return "fact " + fact.FactName;
            }

            if (asset is IDefinitionAsset definition)
            {
                return definition.DefinitionName;
            }

            return asset.name;
        }

        /// <summary>The authoring id of a referenced asset, or empty.</summary>
        public static string AuthoringIdOf(UnityEngine.Object? asset) =>
            asset is IAuthoredObject authored && AuthoringIds.IsValid(authored.AuthoringId) ? authored.AuthoringId : string.Empty;

        /// <summary>The kind of a narrative definition asset, or empty.</summary>
        public static string KindOf(UnityEngine.Object? asset) => asset is INarrativeDefinition definition ? definition.NarrativeKind : string.Empty;
    }

    /// <summary>One conversion run: models, memo tables, problems.</summary>
    public sealed class NarrativeConversion
    {
        private readonly List<INarrativeContentConverter> converters;
        private readonly Dictionary<int, ConditionSetModel> conditionMemo = new Dictionary<int, ConditionSetModel>();
        private readonly Dictionary<int, ActionSetModel> rawActions = new Dictionary<int, ActionSetModel>();
        private readonly Dictionary<int, ActionSetModel> flatActions = new Dictionary<int, ActionSetModel>();
        private readonly HashSet<int> inProgress = new HashSet<int>();
        private readonly HashSet<int> converted = new HashSet<int>();
        private readonly RawActionLookup rawLookup;

        public NarrativeConversion(string worldId, IEnumerable<INarrativeContentConverter> packageConverters)
        {
            WorldId = worldId ?? string.Empty;
            Models = new NarrativeModelSet();
            converters = new List<INarrativeContentConverter> { new LogicContentConverter() };
            if (packageConverters != null)
            {
                foreach (INarrativeContentConverter converter in packageConverters)
                {
                    if (converter != null && !(converter is LogicContentConverter))
                    {
                        converters.Add(converter);
                    }
                }
            }

            rawLookup = new RawActionLookup(rawActions);
        }

        public string WorldId { get; }

        public NarrativeModelSet Models { get; }

        public void Problem(UnityEngine.Object? subject, string problem)
        {
            Models.Problem((subject != null ? subject.name + ": " : string.Empty) + problem);
        }

        /// <summary>Converts every asset (facts first, then in the given order); assets no converter owns are reported.</summary>
        public void ConvertAll(IReadOnlyList<ScriptableObject> assets)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < assets.Count; i++)
                {
                    ScriptableObject asset = assets[i];
                    if (asset == null)
                    {
                        continue;
                    }

                    bool isFact = asset is IFactDefinition;
                    if ((pass == 0) != isFact)
                    {
                        continue;
                    }

                    Convert(asset);
                }
            }
        }

        /// <summary>Converts one asset once (later calls are no-ops).</summary>
        public void Convert(ScriptableObject asset)
        {
            if (asset == null || !converted.Add(asset.GetInstanceID()))
            {
                return;
            }

            for (int i = 0; i < converters.Count; i++)
            {
                if (converters[i].CanConvert(asset))
                {
                    converters[i].Convert(asset, this);
                    return;
                }
            }

            Problem(asset, NarrativeDiagnosticCodes.ContentUnknownKind + ": no narrative package converts " + asset.GetType().Name);
        }

        /// <summary>The model of a referenced ConditionSetDefinition (converted once and registered), or null.</summary>
        public ConditionSetModel? ConditionSet(ScriptableObject? asset)
        {
            if (asset == null)
            {
                return null;
            }

            if (!(asset is ConditionSetDefinition definition))
            {
                Problem(asset, NarrativeDiagnosticCodes.ConditionInvalid + ": " + asset.name + " is not a condition set");
                return null;
            }

            int key = NarrativeRefs.KeyOf(definition);
            if (conditionMemo.TryGetValue(key, out ConditionSetModel memo))
            {
                return memo;
            }

            if (!inProgress.Add(key))
            {
                Problem(asset, NarrativeDiagnosticCodes.ConditionCycle + ": condition set " + asset.name + " nests itself");
                return null;
            }

            var conditions = new List<ConditionModel>();
            for (int i = 0; i < definition.Conditions.Count; i++)
            {
                conditions.Add(Condition(definition.Conditions[i], definition));
            }

            var model = new ConditionSetModel(key, definition.DefinitionName, definition.Mode, conditions);
            inProgress.Remove(key);
            conditionMemo[key] = model;
            Models.AddConditionSet(model, definition.AuthoringId);
            Models.Alias(definition.DefinitionName, key);
            converted.Add(definition.GetInstanceID());
            return model;
        }

        /// <summary>An inline condition list as a model whose key derives from its owner and slot (null when empty).</summary>
        public ConditionSetModel? Inline(string ownerAuthoringId, string slot, IReadOnlyList<ConditionEntry>? entries, ConditionMode mode, UnityEngine.Object? owner)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            var conditions = new List<ConditionModel>();
            for (int i = 0; i < entries.Count; i++)
            {
                conditions.Add(Condition(entries[i], owner));
            }

            int key = NarrativeKeys.NameKey("gameplay.inline-conditions." + ownerAuthoringId + "." + slot);
            var model = new ConditionSetModel(key, (owner != null ? owner.name : ownerAuthoringId) + "." + slot, mode, conditions);
            if (!conditionMemo.ContainsKey(key))
            {
                conditionMemo[key] = model;
                Models.AddConditionSet(model, ownerAuthoringId + "." + slot);
            }

            return model;
        }

        /// <summary>The flattened model of a referenced ActionSetDefinition (converted once and registered), or null.</summary>
        public ActionSetModel? ActionSet(ScriptableObject? asset)
        {
            if (asset == null)
            {
                return null;
            }

            if (!(asset is ActionSetDefinition definition))
            {
                Problem(asset, NarrativeDiagnosticCodes.ActionInvalid + ": " + asset.name + " is not an action set");
                return null;
            }

            int key = NarrativeRefs.KeyOf(definition);
            if (flatActions.TryGetValue(key, out ActionSetModel memo))
            {
                return memo;
            }

            ActionSetModel raw = Raw(definition, key);
            var problems = new List<string>();
            ActionSetModel flat = ActionRules.Flatten(raw, rawLookup, problems);
            for (int i = 0; i < problems.Count; i++)
            {
                Problem(asset, NarrativeDiagnosticCodes.ActionInvalid + ": " + problems[i]);
            }

            flatActions[key] = flat;
            Models.AddActionSet(flat, definition.AuthoringId);
            Models.Alias(definition.DefinitionName, key);
            converted.Add(definition.GetInstanceID());
            return flat;
        }

        /// <summary>An inline action list as a flattened model whose key derives from its owner and slot (null when empty).</summary>
        public ActionSetModel? InlineActions(string ownerAuthoringId, string slot, IReadOnlyList<ActionEntry>? entries, UnityEngine.Object? owner)
        {
            if (entries == null || entries.Count == 0)
            {
                return null;
            }

            int key = NarrativeKeys.NameKey("gameplay.inline-actions." + ownerAuthoringId + "." + slot);
            if (flatActions.TryGetValue(key, out ActionSetModel memo))
            {
                return memo;
            }

            var actions = new List<ActionModel>();
            for (int i = 0; i < entries.Count; i++)
            {
                actions.Add(Action(entries[i], owner));
            }

            var raw = new ActionSetModel(key, (owner != null ? owner.name : ownerAuthoringId) + "." + slot, actions);
            rawActions[key] = raw;
            var problems = new List<string>();
            ActionSetModel flat = ActionRules.Flatten(raw, rawLookup, problems);
            for (int i = 0; i < problems.Count; i++)
            {
                Problem(owner, NarrativeDiagnosticCodes.ActionInvalid + ": " + problems[i]);
            }

            flatActions[key] = flat;
            Models.AddActionSet(flat, ownerAuthoringId + "." + slot);
            return flat;
        }

        /// <summary>One condition entry as a model.</summary>
        public ConditionModel Condition(ConditionEntry entry, UnityEngine.Object? owner)
        {
            if (entry == null)
            {
                Problem(owner, NarrativeDiagnosticCodes.ConditionInvalid + ": an empty condition entry");
                return new ConditionModel(ConditionKind.Always, 0, 0, CompareOp.Equal, 0, "invalid");
            }

            int key = NarrativeRefs.KeyOf(entry.subject);
            string label = NarrativeRefs.LabelOf(entry.subject);
            switch (entry.kind)
            {
                case ConditionKind.Fact:
                    Require(entry.subject is IFactDefinition, owner, "a fact condition needs a fact");
                    return new ConditionModel(ConditionKind.Fact, key, 0, entry.op, entry.value, label);
                case ConditionKind.ItemCount:
                    Require(key != 0, owner, "an item condition needs an item");
                    return new ConditionModel(ConditionKind.ItemCount, key, NarrativeRefs.KeyOf(entry.inventory), entry.op, entry.value, "count of " + label);
                case ConditionKind.Currency:
                    return new ConditionModel(ConditionKind.Currency, 0, NarrativeRefs.KeyOf(entry.inventory), entry.op, entry.value, "currency");
                case ConditionKind.QuestStatus:
                case ConditionKind.QuestStage:
                case ConditionKind.QuestBranch:
                    Require(key != 0, owner, "a quest condition needs a quest");
                    return new ConditionModel(entry.kind, key, 0, entry.op, entry.value, label + " " + entry.kind.ToString().Substring(5).ToLowerInvariant());
                case ConditionKind.ObjectiveDone:
                    Require(key != 0, owner, "an objective condition needs a quest");
                    return new ConditionModel(ConditionKind.ObjectiveDone, key, entry.index, entry.op, entry.value,
                        label + " objective " + entry.index.ToString(CultureInfo.InvariantCulture));
                case ConditionKind.Region:
                    Require(key != 0, owner, "a region condition needs a region");
                    return new ConditionModel(ConditionKind.Region, 0, NarrativeRefs.EntityKey(entry.entityId),
                        entry.op == CompareOp.NotEqual ? CompareOp.NotEqual : CompareOp.Equal, key, "region");
                case ConditionKind.TimeMs:
                    return new ConditionModel(ConditionKind.TimeMs, 0, 0, entry.op, entry.value, "time ms");
                case ConditionKind.NodeVisited:
                    Require(key != 0, owner, "a visited condition needs a graph");
                    return new ConditionModel(ConditionKind.NodeVisited, key, entry.index, entry.op, entry.value,
                        label + " node " + entry.index.ToString(CultureInfo.InvariantCulture) + " visited");
                case ConditionKind.Slot:
                    return new ConditionModel(ConditionKind.Slot, NarrativeRefs.EntityKey(entry.entityId), entry.index, entry.op, entry.value,
                        "slot " + NarrativeSlotRefs.NameOf(entry.index));
                case ConditionKind.RuleFired:
                    Require(key != 0, owner, "a rule condition needs a rule");
                    return new ConditionModel(ConditionKind.RuleFired, key, 0, entry.op, entry.value, label + " fired");
                case ConditionKind.ConditionSet:
                    ConditionSet(entry.subject);
                    return new ConditionModel(ConditionKind.ConditionSet, key, 0, CompareOp.Equal, entry.value == 0 ? 0 : 1, label);
                default:
                    return new ConditionModel(ConditionKind.Always, 0, 0, CompareOp.Equal, 1, "always");
            }
        }

        /// <summary>One action entry as a model (RunActionSet converts the nested set first).</summary>
        public ActionModel Action(ActionEntry entry, UnityEngine.Object? owner)
        {
            if (entry == null)
            {
                Problem(owner, NarrativeDiagnosticCodes.ActionInvalid + ": an empty action entry");
                return new ActionModel(ActionKind.ShowMessage, 0, 0, 0, string.Empty, string.Empty, "invalid");
            }

            int key = NarrativeRefs.KeyOf(entry.target);
            string label = NarrativeRefs.LabelOf(entry.target);
            string reference = entry.entityId ?? string.Empty;
            switch (entry.kind)
            {
                case ActionKind.SetFact:
                case ActionKind.AddFact:
                    Require(entry.target is IFactDefinition, owner, "a fact action needs a fact");
                    break;
                case ActionKind.Grant:
                case ActionKind.Consume:
                    Require(key != 0, owner, "an item action needs an item");
                    Require(entry.value > 0, owner, "an item action needs a positive count");
                    return new ActionModel(entry.kind, key, NarrativeRefs.KeyOf(entry.inventory), entry.value, reference, entry.text, label);
                case ActionKind.StartQuest:
                case ActionKind.AdvanceQuest:
                case ActionKind.CompleteQuest:
                case ActionKind.FailQuest:
                    Require(key != 0, owner, "a quest action needs a quest");
                    break;
                case ActionKind.Travel:
                    Require(entry.target is RegionDefinition, owner, "travel needs a region");
                    reference = NarrativeRefs.AuthoringIdOf(entry.target);
                    break;
                case ActionKind.StartDialogue:
                    Require(key != 0, owner, "startDialogue needs a dialogue graph");
                    break;
                case ActionKind.RunActionSet:
                    Require(entry.target is ActionSetDefinition, owner, "runActionSet needs an action set");
                    if (entry.target is ActionSetDefinition nested)
                    {
                        Raw(nested, key);
                    }

                    break;
                case ActionKind.Pickup:
                    reference = entry.target != null ? NarrativeRefs.AuthoringIdOf(entry.target) : reference;
                    break;
                case ActionKind.PlayAudio:
                case ActionKind.ShowMessage:
                    Require(!string.IsNullOrEmpty(entry.text), owner, entry.kind + " needs text");
                    break;
                case ActionKind.RestoreStamina:
                    // P1.7b declaration (player.restoreStamina{amount}); the port is P1.7a's.
                    if (entry.value <= 0)
                    {
                        Problem(owner, AuthoringHardeningCodes.RestoreStaminaInvalid + ": restoreStamina needs a positive amount");
                    }

                    return new ActionModel(ActionKind.RestoreStamina, 0, 0, entry.value, string.Empty, entry.text, "stamina");
                case ActionKind.Buy:
                    // P1.7b declaration (inv.buy{vendor, item, count} for the actor); the port is P1.7a's.
                    if (key == 0 || NarrativeRefs.KeyOf(entry.vendor) == 0 || entry.value <= 0)
                    {
                        Problem(owner, AuthoringHardeningCodes.BuyActionInvalid + ": buy needs a vendor, an item and a positive count");
                    }

                    return new ActionModel(ActionKind.Buy, key, NarrativeRefs.KeyOf(entry.vendor), entry.value, reference, entry.text, label);
            }

            return new ActionModel(entry.kind, key, NarrativeRefs.KeyOf(entry.inventory), entry.value, reference, entry.text, label);
        }

        private ActionSetModel Raw(ActionSetDefinition definition, int key)
        {
            if (rawActions.TryGetValue(key, out ActionSetModel existing))
            {
                return existing;
            }

            if (!inProgress.Add(key))
            {
                Problem(definition, NarrativeDiagnosticCodes.ActionInvalid + ": action set " + definition.name + " runs itself");
                return new ActionSetModel(key, definition.DefinitionName, Array.Empty<ActionModel>());
            }

            var actions = new List<ActionModel>();
            for (int i = 0; i < definition.Actions.Count; i++)
            {
                actions.Add(Action(definition.Actions[i], definition));
            }

            inProgress.Remove(key);
            var raw = new ActionSetModel(key, definition.DefinitionName, actions);
            rawActions[key] = raw;
            return raw;
        }

        private void Require(bool condition, UnityEngine.Object? owner, string problem)
        {
            if (!condition)
            {
                Problem(owner, NarrativeDiagnosticCodes.ContentMissingReference + ": " + problem);
            }
        }

        private sealed class RawActionLookup : IActionSetLookup
        {
            private readonly Dictionary<int, ActionSetModel> raw;

            public RawActionLookup(Dictionary<int, ActionSetModel> raw)
            {
                this.raw = raw;
            }

            public bool TryGet(int key, out ActionSetModel? set)
            {
                if (raw.TryGetValue(key, out ActionSetModel found))
                {
                    set = found;
                    return true;
                }

                set = null;
                return false;
            }
        }
    }

    /// <summary>The slots a Slot condition can read (index = the condition's slot ref).</summary>
    public static class NarrativeSlotRefs
    {
        public const int Alive = 0;
        public const int Variant = 1;
        public const int Visible = 2;
        public const int Region = 3;
        public const int Visits = 4;

        public static string NameOf(int slotRef)
        {
            switch (slotRef)
            {
                case Alive: return "entity.alive";
                case Variant: return "entity.variant";
                case Visible: return "entity.visible";
                case Region: return "world.region";
                case Visits: return "world.visits";
                default: return "unknown." + slotRef.ToString(CultureInfo.InvariantCulture);
            }
        }

        public static bool TryResolve(int slotRef, out OwnerId owner, out SlotId slot)
        {
            switch (slotRef)
            {
                case Alive: owner = GameplaySlots.EntityOwner; slot = GameplaySlots.Alive; return true;
                case Variant: owner = GameplaySlots.EntityOwner; slot = GameplaySlots.Variant; return true;
                case Visible: owner = GameplaySlots.EntityOwner; slot = GameplaySlots.Visible; return true;
                case Region: owner = GameplaySlots.WorldOwner; slot = GameplaySlots.Region; return true;
                case Visits: owner = GameplaySlots.WorldOwner; slot = GameplaySlots.Visits; return true;
                default: owner = default(OwnerId); slot = default(SlotId); return false;
            }
        }
    }

    /// <summary>The logic package's converter: facts, condition sets, action sets and rules.</summary>
    public sealed class LogicContentConverter : INarrativeContentConverter
    {
        public bool CanConvert(ScriptableObject asset) =>
            asset is IFactDefinition || asset is ConditionSetDefinition || asset is ActionSetDefinition || asset is RuleDefinition;

        public void Convert(ScriptableObject asset, NarrativeConversion conversion)
        {
            switch (asset)
            {
                case IFactDefinition fact:
                    if (!NarrativeKeys.IsValidFactName(fact.FactName))
                    {
                        conversion.Problem(asset, NarrativeDiagnosticCodes.FactInvalidName + ": '" + fact.FactName + "' is not a fact name");
                        return;
                    }

                    if (conversion.Models.TryGetFactByName(fact.FactName, out FactModel? _))
                    {
                        conversion.Problem(asset, NarrativeDiagnosticCodes.FactDuplicate + ": fact " + fact.FactName + " is declared twice");
                        return;
                    }

                    var model = new FactModel(NarrativeKeys.FactKey(fact.FactName), fact.FactName, fact.InitialValue, fact.Persistent);
                    conversion.Models.AddFact(model);
                    conversion.Models.Alias(fact.AuthoringId, model.Key);
                    conversion.Models.Alias("narrative.fact." + fact.FactName, model.Key);
                    return;
                case ConditionSetDefinition conditions:
                    conversion.ConditionSet(conditions);
                    return;
                case ActionSetDefinition actions:
                    conversion.ActionSet(actions);
                    return;
                case RuleDefinition rule:
                    ConvertRule(rule, conversion);
                    return;
            }
        }

        private static void ConvertRule(RuleDefinition rule, NarrativeConversion conversion)
        {
            int filterKey = NarrativeRefs.KeyOf(rule.TriggerSubject);
            if (filterKey == 0)
            {
                filterKey = NarrativeRefs.EntityKey(rule.TriggerEntityId);
            }

            var trigger = new TriggerModel(
                rule.Trigger,
                filterKey,
                rule.MatchAnyValue ? TriggerModel.AnyValue : rule.TriggerValue,
                NarrativeRefs.LabelOf(rule.TriggerSubject));
            ConditionSetModel? conditions = rule.Conditions.Count > 0
                ? conversion.Inline(rule.AuthoringId, "conditions", rule.Conditions, ConditionMode.All, rule)
                : conversion.ConditionSet(rule.ConditionSet);
            ActionSetModel? actions = rule.Actions.Count > 0
                ? conversion.InlineActions(rule.AuthoringId, "actions", rule.Actions, rule)
                : conversion.ActionSet(rule.ActionSet);
            if (rule.Trigger == TriggerKind.Manual && actions == null)
            {
                conversion.Problem(rule, NarrativeDiagnosticCodes.RuleMissingTrigger + ": a manual rule without actions does nothing");
            }

            var model = new RuleModel(
                NarrativeRefs.KeyOf(rule),
                rule.DefinitionName,
                trigger,
                conditions,
                actions,
                rule.Once,
                rule.CooldownMs,
                rule.MaxFires,
                rule.Priority);
            conversion.Models.AddRule(model, rule.AuthoringId);
            conversion.Models.Alias(rule.DefinitionName, model.Key);
        }
    }

    /// <summary>Builds the models of a baked content manifest (boot) or of a content set (bake).</summary>
    public static class NarrativeContent
    {
        /// <summary>The models of a content manifest with the given package converters; frozen.</summary>
        public static NarrativeModelSet Build(GameplayContentManifest manifest, IEnumerable<INarrativeContentConverter> converters)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            var assets = new List<ScriptableObject>();
            for (int i = 0; i < manifest.Entries.Count; i++)
            {
                if (manifest.Entries[i].asset != null)
                {
                    assets.Add(manifest.Entries[i].asset!);
                }
            }

            var conversion = new NarrativeConversion(manifest.WorldId, converters);
            conversion.ConvertAll(assets);
            conversion.Models.Freeze();
            return conversion.Models;
        }

        /// <summary>Converts loose assets (the bake, Studio tools, EditMode tests); not frozen.</summary>
        public static NarrativeConversion Convert(string worldId, IReadOnlyList<ScriptableObject> assets, IEnumerable<INarrativeContentConverter> converters)
        {
            var conversion = new NarrativeConversion(worldId, converters);
            conversion.ConvertAll(assets);
            return conversion;
        }
    }
}
