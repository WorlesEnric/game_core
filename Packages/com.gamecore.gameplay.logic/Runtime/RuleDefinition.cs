// GameCore.Gameplay.Logic - RuleDefinition (its own file: Unity resolves a ScriptableObject script by file name).
// P1.7b: the trigger filter is one typed [AuthorRef] per subject kind (the P1.4 narrative.subject field is a hidden
// legacy field, migrated on load and by authoring.migrateRefs; TriggerSubject reads the field the trigger kind uses).
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
    /// <summary>When a trigger event matches, if the conditions hold, run the actions.</summary>
    [Authorable(NarrativeKinds.Rule, DisplayName = "Rule", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A reusable rule: trigger event and filter, conditions, actions, once/cooldown/limit.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Rule", fileName = "Rule")]
    public sealed class RuleDefinition : NarrativeDefinitionAsset, ISerializationCallbackReceiver
    {
        [AuthorField(Doc = "The event that triggers the rule.")]
        [SerializeField] private TriggerKind trigger = TriggerKind.FactSet;

        [AuthorRef(Category = NarrativeKinds.Fact, Required = false, Doc = "Filter of factSet: the fact.")]
        [SerializeField] private ScriptableObject? triggerFact;

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "Filter of itemGranted / itemConsumed: the item.")]
        [SerializeField] private ScriptableObject? triggerItem;

        [AuthorRef(Category = NarrativeKinds.Vendor, Required = false, Doc = "Filter of tradeDone: the vendor.")]
        [SerializeField] private ScriptableObject? triggerVendor;

        [AuthorRef(Category = NarrativeKinds.Quest, Required = false, Doc = "Filter of questStarted / stageEntered / questCompleted / questFailed: the quest.")]
        [SerializeField] private ScriptableObject? triggerQuest;

        [AuthorRef(Category = NarrativeKinds.Graph, Required = false, Doc = "Filter of dialogueEnded / choiceMade: the dialogue graph.")]
        [SerializeField] private ScriptableObject? triggerGraph;

        [AuthorRef(Category = NarrativeSubjects.Region, Required = false, Doc = "Filter of regionEntered: the region.")]
        [SerializeField] private RegionDefinition? triggerRegion;

        [AuthorRef(Category = NarrativeKinds.Rule, Required = false, Doc = "Filter of ruleFired: the other rule.")]
        [SerializeField] private RuleDefinition? triggerRule;

        [AuthorRef(Category = NarrativeKinds.WorldItem, Required = false, Doc = "Filter of itemPickedUp: the world item.")]
        [SerializeField] private ScriptableObject? triggerWorldItem;

        // Legacy P1.4 storage (pseudo-category narrative.subject); see the file header.
        [SerializeField, HideInInspector, FormerlySerializedAs("triggerSubject")] private ScriptableObject? legacyTriggerSubject;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Filter by entity (authoring id) for interacted / regionEntered when no subject asset applies.")]
        [SerializeField] private string triggerEntityId = string.Empty;

        [AuthorField(Doc = "Match only this event value (see matchAnyValue).")]
        [SerializeField] private int triggerValue;

        [AuthorField(Doc = "Ignore the event value.")]
        [SerializeField] private bool matchAnyValue = true;

        [AuthorRef(Category = NarrativeKinds.ConditionSet, Required = false, Doc = "Shared conditions (used when no inline conditions are given).")]
        [SerializeField] private ConditionSetDefinition? conditionSet;

        [AuthorField(Doc = "Inline conditions (all must hold).")]
        [SerializeField] private List<ConditionEntry> conditions = new List<ConditionEntry>();

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "Shared actions (used when no inline actions are given).")]
        [SerializeField] private ActionSetDefinition? actionSet;

        [AuthorField(Doc = "Inline actions.")]
        [SerializeField] private List<ActionEntry> actions = new List<ActionEntry>();

        [AuthorField(Doc = "Fire at most once.")]
        [SerializeField] private bool once;

        [AuthorField(Unit = "ms", Min = 0, Doc = "Domain milliseconds after a fire during which the rule skips.")]
        [SerializeField] private int cooldownMs;

        [AuthorField(Min = 0, Doc = "Most fires (0 = unlimited).")]
        [SerializeField] private int maxFires;

        [AuthorField(Doc = "Rules of one event run in ascending priority.")]
        [SerializeField] private int priority;

        public override string NarrativeKind => NarrativeKinds.Rule;

        public TriggerKind Trigger => trigger;

        /// <summary>The trigger filter: the typed field the trigger kind uses (the legacy field until it is migrated).</summary>
        public ScriptableObject? TriggerSubject
        {
            get
            {
                ScriptableObject? typed = TypedFor(trigger);
                return typed != null ? typed : legacyTriggerSubject;
            }
        }

        /// <summary>The legacy filter still stored (null after migration).</summary>
        public ScriptableObject? LegacyTriggerSubject => legacyTriggerSubject;

        public string TriggerEntityId => triggerEntityId;

        public int TriggerValue => triggerValue;

        public bool MatchAnyValue => matchAnyValue;

        public ConditionSetDefinition? ConditionSet => conditionSet;

        public IReadOnlyList<ConditionEntry> Conditions => conditions;

        public ActionSetDefinition? ActionSet => actionSet;

        public IReadOnlyList<ActionEntry> Actions => actions;

        public bool Once => once;

        public int CooldownMs => cooldownMs;

        public int MaxFires => maxFires;

        public int Priority => priority;

        public void ConfigureTrigger(TriggerKind kind, ScriptableObject? subject, string entityId, bool anyValue, int value)
        {
            trigger = kind;
            AssignSubject(subject);
            triggerEntityId = entityId ?? string.Empty;
            matchAnyValue = anyValue;
            triggerValue = value;
        }

        public void ConfigureLimits(bool fireOnce, int cooldown, int fires, int order)
        {
            once = fireOnce;
            cooldownMs = Math.Max(0, cooldown);
            maxFires = Math.Max(0, fires);
            priority = order;
        }

        public void SetConditions(ConditionSetDefinition? shared, IEnumerable<ConditionEntry>? inline)
        {
            conditionSet = shared;
            conditions = inline != null ? new List<ConditionEntry>(inline) : new List<ConditionEntry>();
        }

        public void SetActions(ActionSetDefinition? shared, IEnumerable<ActionEntry>? inline)
        {
            actionSet = shared;
            actions = inline != null ? new List<ActionEntry>(inline) : new List<ActionEntry>();
        }

        /// <summary>Moves a classifiable legacy trigger filter into its typed field; true when something moved.</summary>
        public bool MigrateLegacy()
        {
            if (ReferenceEquals(legacyTriggerSubject, null) || NarrativeSubjects.KindOf(legacyTriggerSubject).Length == 0)
            {
                return false;
            }

            ScriptableObject moved = legacyTriggerSubject!;
            AssignSubject(moved);
            return true;
        }

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        void ISerializationCallbackReceiver.OnAfterDeserialize() => MigrateLegacy();

        private ScriptableObject? TypedFor(TriggerKind kind)
        {
            switch (kind)
            {
                case TriggerKind.FactSet: return triggerFact;
                case TriggerKind.ItemGranted:
                case TriggerKind.ItemConsumed: return triggerItem;
                case TriggerKind.TradeDone: return triggerVendor;
                case TriggerKind.QuestStarted:
                case TriggerKind.StageEntered:
                case TriggerKind.QuestCompleted:
                case TriggerKind.QuestFailed: return triggerQuest;
                case TriggerKind.DialogueEnded:
                case TriggerKind.ChoiceMade: return triggerGraph;
                case TriggerKind.RegionEntered: return triggerRegion;
                case TriggerKind.RuleFired: return triggerRule;
                case TriggerKind.ItemPickedUp: return triggerWorldItem;
                default: return null;
            }
        }

        private void AssignSubject(ScriptableObject? subject)
        {
            triggerFact = null;
            triggerItem = null;
            triggerVendor = null;
            triggerQuest = null;
            triggerGraph = null;
            triggerRegion = null;
            triggerRule = null;
            triggerWorldItem = null;
            legacyTriggerSubject = null;
            switch (NarrativeSubjects.KindOf(subject))
            {
                case NarrativeKinds.Fact: triggerFact = subject; break;
                case NarrativeKinds.Item: triggerItem = subject; break;
                case NarrativeKinds.Vendor: triggerVendor = subject; break;
                case NarrativeKinds.Quest: triggerQuest = subject; break;
                case NarrativeKinds.Graph: triggerGraph = subject; break;
                case NarrativeSubjects.Region: triggerRegion = (RegionDefinition)subject!; break;
                case NarrativeKinds.Rule: triggerRule = (RuleDefinition)subject!; break;
                case NarrativeKinds.WorldItem: triggerWorldItem = subject; break;
                default: legacyTriggerSubject = subject; break;
            }
        }
    }
}
