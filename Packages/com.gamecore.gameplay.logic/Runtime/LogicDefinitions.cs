// GameCore.Gameplay.Logic - reusable rules authoring: ConditionSetDefinition, ActionSetDefinition, RuleDefinition, and
// the content set / content manifest pair the bake learns narrative definitions from (P1.4, catalog row 9).
//
// Conditions and actions name what they are about through ScriptableObject references (a fact, an item, a quest, a
// region, a graph, another set) with [AuthorRef] categories, or through an entity authoring id for placed scene
// entities, which assets cannot reference. The bake turns every reference into the int32 key the slots carry; nothing
// is resolved by string at runtime except through the baked tables.
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
    /// <summary>Shared identity plumbing of every narrative definition asset.</summary>
    public abstract class NarrativeDefinitionAsset : ScriptableObject, INarrativeDefinition
    {
        [SerializeField] private string authoringId = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public virtual string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public abstract string NarrativeKind { get; }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        /// <summary>Sets an explicit authoring id (content authoring scripts that keep ids stable across re-creation).</summary>
        public void SetAuthoringId(string id)
        {
            if (!AuthoringIds.IsValid(id))
            {
                throw new ArgumentException("GP-ID-002: '" + id + "' is not a canonical authoring id");
            }

            authoringId = id;
        }

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        protected virtual void Reset() => EnsureAuthoringId();

        protected virtual void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>One condition: what is read, how it compares, and against what.</summary>
    [Serializable]
    public sealed class ConditionEntry
    {
        [AuthorField(Doc = "What the condition reads.")]
        public ConditionKind kind = ConditionKind.Fact;

        [AuthorRef(Category = "narrative.subject", Required = false,
            Doc = "The fact, item, quest, region, graph, rule or condition set the condition is about.")]
        public ScriptableObject? subject;

        [AuthorRef(Category = "inventory.inventory", Required = false, Doc = "Inventory for item/currency conditions (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorField(Type = "authoringId", Doc = "Entity authoring id for region/slot conditions (empty = the actor or subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Objective index (objectiveDone), node index (nodeVisited) or slot ref (slot).")]
        public int index;

        [AuthorField(Doc = "Comparison operator.")]
        public CompareOp op = CompareOp.GreaterOrEqual;

        [AuthorField(Doc = "Value compared against (ignored for region conditions, which compare with the subject region).")]
        public int value = 1;

        public static ConditionEntry Of(ConditionKind kind, ScriptableObject? subject, CompareOp op, int value) =>
            new ConditionEntry { kind = kind, subject = subject, op = op, value = value };
    }

    /// <summary>One action.</summary>
    [Serializable]
    public sealed class ActionEntry
    {
        [AuthorField(Doc = "What the action does.")]
        public ActionKind kind = ActionKind.SetFact;

        [AuthorRef(Category = "narrative.subject", Required = false,
            Doc = "The fact, item, quest, graph, region, world item or action set the action is about.")]
        public ScriptableObject? target;

        [AuthorRef(Category = "inventory.inventory", Required = false, Doc = "Inventory for grant/consume (empty = the actor's).")]
        public ScriptableObject? inventory;

        [AuthorField(Type = "authoringId", Doc = "Entity authoring id for spawn/despawn/setInteractableState/startDialogue (empty = the subject).")]
        public string entityId = string.Empty;

        [AuthorField(Doc = "Fact value, item count, quest stage (-1 = next) or interactable state.")]
        public int value = 1;

        [AuthorField(Doc = "Cue id (playAudio) or message text (showMessage).")]
        public string text = string.Empty;

        public static ActionEntry Of(ActionKind kind, ScriptableObject? target, int value) =>
            new ActionEntry { kind = kind, target = target, value = value };

        public static ActionEntry Text(ActionKind kind, string text) => new ActionEntry { kind = kind, text = text ?? string.Empty };
    }

    /// <summary>A reusable list of conditions.</summary>
    [Authorable(NarrativeKinds.ConditionSet, DisplayName = "Condition Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Conditions over facts, items, quests, regions and time, combined with all or any.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Condition Set", fileName = "ConditionSet")]
    public sealed class ConditionSetDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "All conditions must hold, or any one.")]
        [SerializeField] private ConditionMode mode = ConditionMode.All;

        [AuthorField(Doc = "The conditions, in evaluation order.")]
        [SerializeField] private List<ConditionEntry> conditions = new List<ConditionEntry>();

        public override string NarrativeKind => NarrativeKinds.ConditionSet;

        public ConditionMode Mode => mode;

        public IReadOnlyList<ConditionEntry> Conditions => conditions;

        public void Configure(ConditionMode combine, IEnumerable<ConditionEntry> entries)
        {
            mode = combine;
            conditions = new List<ConditionEntry>(entries);
        }

        public void Add(ConditionEntry entry) => conditions.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }

    /// <summary>A reusable list of actions.</summary>
    [Authorable(NarrativeKinds.ActionSet, DisplayName = "Action Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Actions run in order: set facts, grant items, advance quests, change interactables, play cues, travel, show messages.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Action Set", fileName = "ActionSet")]
    public sealed class ActionSetDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "The actions, in order.")]
        [SerializeField] private List<ActionEntry> actions = new List<ActionEntry>();

        public override string NarrativeKind => NarrativeKinds.ActionSet;

        public IReadOnlyList<ActionEntry> Actions => actions;

        public void Configure(IEnumerable<ActionEntry> entries) => actions = new List<ActionEntry>(entries);

        public void Add(ActionEntry entry) => actions.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }

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

    /// <summary>The narrative content of one world: the definitions the bake learns (P1.4 compile hook).</summary>
    [Authorable(NarrativeKinds.ContentSet, DisplayName = "Gameplay Content Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Compile,
        Doc = "Lists the narrative definitions (facts, graphs, quests, items, rules...) of one world; the bake writes its content manifest.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Content Set", fileName = "ContentSet")]
    public sealed class GameplayContentSet : ScriptableObject
    {
        [AuthorRef(Category = "world.world", Doc = "The world this content belongs to.")]
        [SerializeField] private WorldDefinition? world;

        [AuthorRef(Category = "narrative.definition", Doc = "Every narrative definition of the world.")]
        [SerializeField] private List<ScriptableObject> definitions = new List<ScriptableObject>();

        public WorldDefinition? World => world;

        public IReadOnlyList<ScriptableObject> Definitions => definitions;

        public void Configure(WorldDefinition? owner, IEnumerable<ScriptableObject> list)
        {
            world = owner;
            definitions = new List<ScriptableObject>(list);
        }

        /// <summary>Adds a definition once; true when added.</summary>
        public bool Add(ScriptableObject definition)
        {
            if (definition == null || definitions.Contains(definition))
            {
                return false;
            }

            definitions.Add(definition);
            return true;
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

    /// <summary>
    /// The bake output the runtime boots narrative content from: every definition of a content set with its kind, key
    /// and content stamp, the fact table (one slot each), and a content hash over all of it. Written by the logic bake
    /// extension next to the content set; Entry.Verify recomputes it.
    /// </summary>
    public sealed class GameplayContentManifest : ScriptableObject
    {
        public const string Format = "gamecore.gameplay-content/1";

        [SerializeField] private string formatId = string.Empty;
        [SerializeField] private string worldId = string.Empty;
        [SerializeField] private string contentHash = string.Empty;
        [SerializeField] private List<ContentEntry> entries = new List<ContentEntry>();
        [SerializeField] private List<FactEntry> facts = new List<FactEntry>();

        public string FormatId => formatId;

        public string WorldId => worldId;

        public string ContentHash => contentHash;

        public IReadOnlyList<ContentEntry> Entries => entries;

        public IReadOnlyList<FactEntry> Facts => facts;

        public void Assign(string world, string hash, IEnumerable<ContentEntry> definitionEntries, IEnumerable<FactEntry> factEntries)
        {
            formatId = Format;
            worldId = world ?? string.Empty;
            contentHash = hash ?? string.Empty;
            entries = new List<ContentEntry>(definitionEntries);
            facts = new List<FactEntry>(factEntries);
        }

        /// <summary>The definitions of one kind, in manifest (authoring id) order.</summary>
        public List<ContentEntry> OfKind(string kind)
        {
            var list = new List<ContentEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].kind, kind, StringComparison.Ordinal))
                {
                    list.Add(entries[i]);
                }
            }

            return list;
        }

        public ContentEntry? FindByAuthoringId(string authoringId)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].authoringId, authoringId, StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }

        public ContentEntry? FindByName(string kind, string definitionName)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].kind, kind, StringComparison.Ordinal) && string.Equals(entries[i].name, definitionName, StringComparison.Ordinal))
                {
                    return entries[i];
                }
            }

            return null;
        }
    }
}
