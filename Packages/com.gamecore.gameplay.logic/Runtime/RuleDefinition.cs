// GameCore.Gameplay.Logic - RuleDefinition (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Logic
{
    /// <summary>When a trigger event matches, if the conditions hold, run the actions.</summary>
    [Authorable(NarrativeKinds.Rule, DisplayName = "Rule", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A reusable rule: trigger event and filter, conditions, actions, once/cooldown/limit.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Rule", fileName = "Rule")]
    public sealed class RuleDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "The event that triggers the rule.")]
        [SerializeField] private TriggerKind trigger = TriggerKind.FactSet;

        [AuthorRef(Category = "narrative.subject", Required = false, Doc = "Filter: the fact, item, quest, vendor, graph, region or rule the event must be about.")]
        [SerializeField] private ScriptableObject? triggerSubject;

        [AuthorField(Type = "authoringId", Doc = "Filter by entity authoring id (interactions) when no subject asset applies.")]
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

        public ScriptableObject? TriggerSubject => triggerSubject;

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
            triggerSubject = subject;
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
    }
}
