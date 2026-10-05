// GameCore.Gameplay.Logic - reusable rules authoring: ConditionSetDefinition, ActionSetDefinition, RuleDefinition, and
// the content set / content manifest pair the bake learns narrative definitions from (P1.4, catalog row 9).
//
// Conditions and actions name what they are about through typed ScriptableObject references (a fact, an item, a quest,
// a region, a graph, a rule, a set, a vendor, a world item), each an [AuthorRef] whose category is the target's
// [Authorable] type id, or through an entity authoring id ([AuthorRef] entity.instance) for placed scene entities, which
// assets cannot reference. The bake turns every reference into the int32 key the slots carry; nothing is resolved by
// string at runtime except through the baked tables.
//
// P1.7b: the P1.4 single `subject` / `target` reference (pseudo-category narrative.subject) is split into one typed
// field per kind. The old serialized field is kept as a hidden legacy field (FormerlySerializedAs), moved into the
// typed field when the asset loads (OnAfterDeserialize) and by authoring.migrateRefs; the `subject` / `target`
// accessors keep the P1.4 call sites working (they read the field the condition's or action's kind uses).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;
using UnityEngine.Serialization;

namespace GameCore.Gameplay.Logic
{
    /// <summary>The [Authorable] type id of a narrative subject (a narrative definition or a region), or empty.</summary>
    public static class NarrativeSubjects
    {
        public const string Region = "world.region";

        /// <summary>The kind of <paramref name="value"/>: its narrative kind, <c>world.region</c>, or empty.</summary>
        public static string KindOf(UnityEngine.Object? value)
        {
            if (ReferenceEquals(value, null))
            {
                return string.Empty;
            }

            if (value is INarrativeDefinition definition)
            {
                return definition.NarrativeKind;
            }

            return value is RegionDefinition ? Region : string.Empty;
        }
    }

    /// <summary>One condition: what is read, how it compares, and against what.</summary>
    [Serializable]
    public sealed class ConditionEntry : ISerializationCallbackReceiver
    {
        [AuthorField(Doc = "What the condition reads.")]
        public ConditionKind kind = ConditionKind.Fact;

        [AuthorRef(Category = NarrativeKinds.Fact, Required = false, Doc = "The fact (fact conditions).")]
        public ScriptableObject? fact;

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "The item (item count conditions).")]
        public ScriptableObject? item;

        [AuthorRef(Category = NarrativeKinds.Quest, Required = false, Doc = "The quest (quest status, stage, branch and objective conditions).")]
        public ScriptableObject? quest;

        [AuthorRef(Category = NarrativeSubjects.Region, Required = false, Doc = "The region (region conditions).")]
        public RegionDefinition? region;

        [AuthorRef(Category = NarrativeKinds.Graph, Required = false, Doc = "The dialogue graph (node visited conditions).")]
        public ScriptableObject? graph;

        [AuthorRef(Category = NarrativeKinds.Rule, Required = false, Doc = "The rule (rule fired conditions).")]
        public RuleDefinition? rule;

        [AuthorRef(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "The nested condition set (condition set conditions).")]
        public ConditionSetDefinition? conditionSet;

        [AuthorRef(Category = NarrativeKinds.Inventory, Required = false, Doc = "Inventory for item/currency conditions (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Entity (authoring id) for region/slot conditions (empty = the actor or subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Objective index (objectiveDone), node index (nodeVisited) or slot ref (slot).")]
        public int index;

        [AuthorField(Doc = "Comparison operator.")]
        public CompareOp op = CompareOp.GreaterOrEqual;

        [AuthorField(Doc = "Value compared against (ignored for region conditions, which compare with the subject region).")]
        public int value = 1;

        // Legacy P1.4 storage (pseudo-category narrative.subject); see the file header.
        [SerializeField, HideInInspector, FormerlySerializedAs("subject")] private ScriptableObject? legacySubject;

        /// <summary>
        /// What the condition is about: the typed field the kind uses (the legacy field until it is migrated). Setting it
        /// stores the value in the typed field of its own kind and clears the others.
        /// </summary>
        public ScriptableObject? subject
        {
            get
            {
                ScriptableObject? typed = TypedFor(kind);
                return typed != null ? typed : legacySubject;
            }

            set => Assign(value);
        }

        /// <summary>The legacy reference still stored (null after migration).</summary>
        public ScriptableObject? LegacySubject => legacySubject;

        public static ConditionEntry Of(ConditionKind kind, ScriptableObject? subject, CompareOp op, int value) =>
            new ConditionEntry { kind = kind, subject = subject, op = op, value = value };

        /// <summary>Moves a classifiable legacy reference into its typed field; true when something moved.</summary>
        public bool MigrateLegacy()
        {
            if (ReferenceEquals(legacySubject, null) || NarrativeSubjects.KindOf(legacySubject).Length == 0)
            {
                return false;
            }

            ScriptableObject moved = legacySubject!;
            Assign(moved);
            return true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacy();

        private ScriptableObject? TypedFor(ConditionKind which)
        {
            switch (which)
            {
                case ConditionKind.Fact: return fact;
                case ConditionKind.ItemCount: return item;
                case ConditionKind.QuestStatus:
                case ConditionKind.QuestStage:
                case ConditionKind.QuestBranch:
                case ConditionKind.ObjectiveDone: return quest;
                case ConditionKind.Region: return region;
                case ConditionKind.NodeVisited: return graph;
                case ConditionKind.RuleFired: return rule;
                case ConditionKind.ConditionSet: return conditionSet;
                default: return null;
            }
        }

        private void Assign(ScriptableObject? next)
        {
            fact = null;
            item = null;
            quest = null;
            region = null;
            graph = null;
            rule = null;
            conditionSet = null;
            legacySubject = null;
            switch (NarrativeSubjects.KindOf(next))
            {
                case NarrativeKinds.Fact: fact = next; break;
                case NarrativeKinds.Item: item = next; break;
                case NarrativeKinds.Quest: quest = next; break;
                case NarrativeSubjects.Region: region = (RegionDefinition)next!; break;
                case NarrativeKinds.Graph: graph = next; break;
                case NarrativeKinds.Rule: rule = (RuleDefinition)next!; break;
                case NarrativeKinds.ConditionSet: conditionSet = (ConditionSetDefinition)next!; break;
                default: legacySubject = next; break;
            }
        }
    }

    /// <summary>One action.</summary>
    [Serializable]
    public sealed class ActionEntry : ISerializationCallbackReceiver
    {
        [AuthorField(Doc = "What the action does.")]
        public ActionKind kind = ActionKind.SetFact;

        [AuthorRef(Category = NarrativeKinds.Fact, Required = false, Doc = "The fact (setFact, addFact).")]
        public ScriptableObject? fact;

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "The item (grant, consume, buy).")]
        public ScriptableObject? item;

        [AuthorRef(Category = NarrativeKinds.Quest, Required = false, Doc = "The quest (startQuest, advanceQuest, completeQuest, failQuest).")]
        public ScriptableObject? quest;

        [AuthorRef(Category = NarrativeSubjects.Region, Required = false, Doc = "The region (travel).")]
        public RegionDefinition? region;

        [AuthorRef(Category = NarrativeKinds.Graph, Required = false, Doc = "The dialogue graph (startDialogue).")]
        public ScriptableObject? graph;

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "The nested action set (runActionSet).")]
        public ActionSetDefinition? actionSet;

        [AuthorRef(Category = NarrativeKinds.WorldItem, Required = false, Doc = "The world item (pickup; empty = the subject).")]
        public ScriptableObject? worldItem;

        [AuthorRef(Category = NarrativeKinds.Vendor, Required = false, Doc = "The vendor (buy).")]
        public ScriptableObject? vendor;

        [AuthorRef(Category = NarrativeKinds.Inventory, Required = false, Doc = "Inventory for grant/consume (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Entity (authoring id) for spawn/despawn/setInteractableState/startDialogue (empty = the subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Fact value, item count, quest stage (-1 = next), interactable state, buy count or stamina amount.")]
        public int value = 1;

        [AuthorField(Doc = "Cue id (playAudio) or message text (showMessage).")]
        public string text = string.Empty;

        // Legacy P1.4 storage (pseudo-category narrative.subject); see the file header.
        [SerializeField, HideInInspector, FormerlySerializedAs("target")] private ScriptableObject? legacyTarget;

        /// <summary>
        /// What the action is about: the typed field the kind uses (the legacy field until it is migrated). Setting it
        /// stores the value in the typed field of its own kind and clears the others (the vendor is kept).
        /// </summary>
        public ScriptableObject? target
        {
            get
            {
                ScriptableObject? typed = TypedFor(kind);
                return typed != null ? typed : legacyTarget;
            }

            set => Assign(value);
        }

        /// <summary>The legacy reference still stored (null after migration).</summary>
        public ScriptableObject? LegacyTarget => legacyTarget;

        public static ActionEntry Of(ActionKind kind, ScriptableObject? target, int value) =>
            new ActionEntry { kind = kind, target = target, value = value };

        public static ActionEntry Text(ActionKind kind, string text) => new ActionEntry { kind = kind, text = text ?? string.Empty };

        /// <summary>Buy <paramref name="count"/> of <paramref name="item"/> from <paramref name="vendor"/> for the actor (inv.buy).</summary>
        public static ActionEntry Buy(ScriptableObject vendor, ScriptableObject item, int count) =>
            new ActionEntry { kind = ActionKind.Buy, target = item, vendor = vendor, value = count };

        /// <summary>Restore <paramref name="amount"/> stamina units of the player (player.restoreStamina).</summary>
        public static ActionEntry RestoreStamina(int amount) => new ActionEntry { kind = ActionKind.RestoreStamina, value = amount };

        /// <summary>Moves a classifiable legacy reference into its typed field; true when something moved.</summary>
        public bool MigrateLegacy()
        {
            if (ReferenceEquals(legacyTarget, null) || NarrativeSubjects.KindOf(legacyTarget).Length == 0)
            {
                return false;
            }

            ScriptableObject moved = legacyTarget!;
            Assign(moved);
            return true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacy();

        private ScriptableObject? TypedFor(ActionKind which)
        {
            switch (which)
            {
                case ActionKind.SetFact:
                case ActionKind.AddFact: return fact;
                case ActionKind.Grant:
                case ActionKind.Consume:
                case ActionKind.Buy: return item;
                case ActionKind.StartQuest:
                case ActionKind.AdvanceQuest:
                case ActionKind.CompleteQuest:
                case ActionKind.FailQuest: return quest;
                case ActionKind.Travel: return region;
                case ActionKind.StartDialogue: return graph;
                case ActionKind.RunActionSet: return actionSet;
                case ActionKind.Pickup: return worldItem;
                default: return null;
            }
        }

        private void Assign(ScriptableObject? next)
        {
            fact = null;
            item = null;
            quest = null;
            region = null;
            graph = null;
            actionSet = null;
            worldItem = null;
            legacyTarget = null;
            switch (NarrativeSubjects.KindOf(next))
            {
                case NarrativeKinds.Fact: fact = next; break;
                case NarrativeKinds.Item: item = next; break;
                case NarrativeKinds.Quest: quest = next; break;
                case NarrativeSubjects.Region: region = (RegionDefinition)next!; break;
                case NarrativeKinds.Graph: graph = next; break;
                case NarrativeKinds.ActionSet: actionSet = (ActionSetDefinition)next!; break;
                case NarrativeKinds.WorldItem: worldItem = next; break;
                case NarrativeKinds.Vendor: vendor = next; break;
                default: legacyTarget = next; break;
            }
        }
    }

    /// <summary>One baked definition of a content manifest.</summary>
    [Serializable]
    public sealed class ContentEntry
    {
        public string kind = string.Empty;
        public string authoringId = string.Empty;
        public string name = string.Empty;
        public int key;
        public string contentStamp = string.Empty;
        public ScriptableObject? asset;
    }

    /// <summary>One baked fact of a content manifest.</summary>
    [Serializable]
    public sealed class FactEntry
    {
        public string name = string.Empty;
        public int key;
        public int initial;
        public bool persistent = true;
        public string authoringId = string.Empty;
    }
}
