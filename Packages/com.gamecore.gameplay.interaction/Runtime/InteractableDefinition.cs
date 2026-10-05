// GameCore.Gameplay.Interaction - InteractableDefinition (P1.3, catalog row 5).
//
// An interactable is an authored entity whose entity definition an InteractableDefinition names (through the
// InteractionRoster). The definition says what kind it is (door, gate, examinable, point, switch), the state it starts
// in, the prompt shown for each state, and two references resolved by other systems: the condition ref (evaluated
// through IConditionEvaluator: while Locked it must be True to unlock; otherwise False refuses the use) and the action
// ref (run through IActionRunner after a committed success). Empty references mean "always allowed" / "no action".
// P1.7b: both references are typed [AuthorRef]s (a logic.conditionSet, or a narrative.fact with a comparison; a
// logic.actionSet or a built-in action); the legacy strings stay readable until authoring.migrateRefs moves them.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>The prompt text for one state (state names as in InteractableStates: idle, closed, open, locked, ...).</summary>
    [Serializable]
    public sealed class StatePrompt
    {
        [AuthorField(Doc = "State name (idle, closed, open, locked, used, broken, off, on).")]
        public string state = string.Empty;

        [AuthorField(Doc = "Prompt text shown while focused in that state.")]
        public string text = string.Empty;

        public StatePrompt()
        {
        }

        public StatePrompt(string stateName, string promptText)
        {
            state = stateName;
            text = promptText;
        }
    }

    /// <summary>An interactable kind: door, gate, examinable, point or switch.</summary>
    [Authorable("interaction.interactable", DisplayName = "Interactable", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An interactable: its entity definition, kind, initial state, per-state prompts, condition and action references, uses, cooldown and range.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Interactable Definition", fileName = "Interactable")]
    public sealed class InteractableDefinition : ScriptableObject, IDefinitionAsset, IConditionGated
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Structural = true, Doc = "The entity definition placed for this interactable.")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Structural = true, Doc = "Door, Gate, Examinable, Point or Switch.")]
        [SerializeField] private InteractableKind kind = InteractableKind.Examinable;

        [AuthorField(Doc = "Initial state name (empty: the kind's default; doors/gates may start locked).")]
        [SerializeField] private string initialState = string.Empty;

        [AuthorField(Doc = "Prompt per state.")]
        [SerializeField] private List<StatePrompt> prompts = new List<StatePrompt>();

        [AuthorRef(Category = "logic.conditionSet", Required = false, Doc = "Condition set: unlocks a locked door when it holds; refuses a use when it fails.")]
        [SerializeField] private ScriptableObject? condition;

        [AuthorRef(Category = "narrative.fact", Required = false, Doc = "Fact condition (used when no condition set is given): the fact compared with conditionValue.")]
        [SerializeField] private ScriptableObject? conditionFact;

        [AuthorField(Doc = "Comparison of the fact condition (default: the fact is not 0).")]
        [SerializeField] private CompareOp conditionOp = CompareOp.NotEqual;

        [AuthorField(Doc = "Value the fact condition compares with.")]
        [SerializeField] private int conditionValue;

        // Legacy (P1.3): the condition as a string ref (a condition set id/name or narrative.fact.<name>[op N]). Read only
        // when no typed condition is set; authoring.migrateRefs moves it into the typed fields and clears it.
        [SerializeField, HideInInspector, AuthorField(Doc = "Legacy (P1.3/P1.4) string reference, read only while the typed field is empty; authoring.migrateRefs moves it into the typed field and clears it.")] private string conditionRef = string.Empty;

        [AuthorRef(Category = "logic.actionSet", Required = false, Doc = "Action set run after a committed success (through the outbox).")]
        [SerializeField] private ScriptableObject? actions;

        [AuthorField(Doc = "Built-in action used when no action set is given (inventory.pickup: pick up the subject world item).")]
        [SerializeField] private string builtInAction = string.Empty;

        // Legacy (P1.3): the action as a string ref; migrated like conditionRef.
        [SerializeField, HideInInspector, AuthorField(Doc = "Legacy (P1.3/P1.4) string reference, read only while the typed field is empty; authoring.migrateRefs moves it into the typed field and clears it.")] private string actionRef = string.Empty;

        [AuthorField(Min = 0, Max = 100000, Doc = "Maximum uses (0 = unlimited).")]
        [SerializeField] private int maxUses;

        [AuthorField(Unit = "s", Min = 0, Max = 3600, Doc = "Cooldown after a use.")]
        [SerializeField] private float cooldownSeconds = 0.5f;

        [AuthorField(Unit = "m", Min = 0, Max = 20, Doc = "Use range from the actor (0 = unlimited).")]
        [SerializeField] private float range = 3f;

        [AuthorField(Min = -100, Max = 100, Doc = "Focus priority against other candidates.")]
        [SerializeField] private int focusPriority;

        [AuthorField(Unit = "m", Min = 0, Max = 10, Doc = "Height of the focus point above the entity's origin.")]
        [SerializeField] private float focusHeight = 1f;

        [AuthorRef(Category = AuthorRefCategories.AudioClip, Required = false, Doc = "Feedback cue prefix: a bank clip id prefix (default interaction.<kind>).")]
        [SerializeField] private string cue = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public InteractableKind Kind => kind;

        public string InitialStateName => initialState;

        public IReadOnlyList<StatePrompt> Prompts => prompts;

        public ScriptableObject? Condition => condition;

        public ScriptableObject? ConditionFact => conditionFact;

        public CompareOp ConditionOp => conditionOp;

        public int ConditionValue => conditionValue;

        public ScriptableObject? Actions => actions;

        public string BuiltInAction => builtInAction;

        /// <summary>The legacy string references still stored (empty after authoring.migrateRefs).</summary>
        public string LegacyConditionRef => conditionRef;

        public string LegacyActionRef => actionRef;

        /// <summary>The effective condition reference the runtime evaluates (see <see cref="ConditionRefs.Effective"/>).</summary>
        public string ConditionRef => ConditionRefs.Effective(condition, conditionFact, conditionOp, conditionValue, conditionRef);

        /// <summary>The effective action reference: the action set's authoring id, else the built-in action, else the legacy string.</summary>
        public string ActionRef => ConditionRefs.EffectiveAction(actions, builtInAction, actionRef);

        public int MaxUses => maxUses;

        public float CooldownSeconds => cooldownSeconds;

        public float Range => range;

        public int FocusPriority => focusPriority;

        public float FocusHeight => focusHeight;

        public string Cue => string.IsNullOrEmpty(cue) ? "interaction." + kind.ToString().ToLowerInvariant() : cue;

        /// <summary>The initial state code: the named state when legal for the kind, else the kind's default.</summary>
        public int InitialState
        {
            get
            {
                if (InteractableStates.TryParse(initialState, out int state) && InteractionRules.IsLegal(kind, state))
                {
                    return state;
                }

                return InteractionRules.InitialState(kind, false);
            }
        }

        public InteractableProfile ToProfile() =>
            new InteractableProfile(kind, maxUses, GameplayUnits.ToMilliseconds(cooldownSeconds), GameplayUnits.ToMillimetres(range));

        /// <summary>The prompt for a state; falls back to "Use" / "Examine" / "Open" by kind.</summary>
        public string PromptFor(int state)
        {
            string stateName = InteractableStates.Name(state);
            for (int i = 0; i < prompts.Count; i++)
            {
                if (prompts[i] != null && string.Equals(prompts[i].state, stateName, StringComparison.OrdinalIgnoreCase))
                {
                    return prompts[i].text;
                }
            }

            switch (kind)
            {
                case InteractableKind.Door:
                case InteractableKind.Gate:
                    return state == InteractableStates.Open ? "Close" : (state == InteractableStates.Locked ? "Locked" : "Open");
                case InteractableKind.Examinable:
                    return "Examine";
                case InteractableKind.Switch:
                    return "Toggle";
                default:
                    return "Use";
            }
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(EntityDefinition? entityDefinition, InteractableKind value, float useRange, float cooldown, int uses)
        {
            entity = entityDefinition;
            kind = value;
            range = useRange;
            cooldownSeconds = cooldown;
            maxUses = uses;
        }

        /// <summary>interaction.setStates: the initial state and the per-state prompts.</summary>
        public void SetStates(string initial, IEnumerable<StatePrompt> statePrompts)
        {
            initialState = initial ?? string.Empty;
            prompts = new List<StatePrompt>(statePrompts);
        }

        /// <summary>
        /// Stores string references as they are (legacy form; the Editor tools resolve them to typed references first and
        /// call this only for strings that name nothing). Clears the typed condition and action.
        /// </summary>
        public void Link(string conditionText, string actionText)
        {
            condition = null;
            conditionFact = null;
            conditionOp = CompareOp.NotEqual;
            conditionValue = 0;
            conditionRef = conditionText ?? string.Empty;
            actions = null;
            builtInAction = string.Empty;
            actionRef = actionText ?? string.Empty;
        }

        /// <summary>The typed condition: a condition set, or a fact compared with a value (clears the legacy string).</summary>
        public void SetCondition(ScriptableObject? conditionSet, ScriptableObject? fact, CompareOp op, int value)
        {
            condition = conditionSet;
            conditionFact = fact;
            conditionOp = op;
            conditionValue = value;
            conditionRef = string.Empty;
        }

        /// <summary>The typed action: an action set, or a built-in action (clears the legacy string).</summary>
        public void SetActions(ScriptableObject? actionSet, string builtIn)
        {
            actions = actionSet;
            builtInAction = builtIn ?? string.Empty;
            actionRef = string.Empty;
        }

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }

    /// <summary>The effective condition/action reference strings of the typed interaction references (P1.7b).</summary>
    public static class ConditionRefs
    {
        /// <summary>The prefix of a fact condition shorthand (NarrativeConditionEvaluator.FactPrefix).</summary>
        public const string FactPrefix = "narrative.fact.";

        /// <summary>The built-in action that picks up the subject world item (NarrativeActionRunner.Pickup).</summary>
        public const string PickupAction = "inventory.pickup";

        /// <summary>Built-in actions the runtime knows.</summary>
        public static IReadOnlyList<string> BuiltInActions { get; } = Array.AsReadOnly(new[] { PickupAction });

        /// <summary>
        /// A condition set's authoring id; else the fact shorthand <c>narrative.fact.&lt;name&gt;</c> (fact != 0) or
        /// <c>narrative.fact.&lt;name&gt;&lt;op&gt;&lt;value&gt;</c>; else the legacy string.
        /// </summary>
        public static string Effective(ScriptableObject? conditionSet, ScriptableObject? fact, CompareOp op, int value, string legacy)
        {
            if (conditionSet != null && conditionSet is IAuthoredObject set && set.AuthoringId.Length > 0)
            {
                return set.AuthoringId;
            }

            if (fact != null && fact is GameCore.Gameplay.Contracts.Narrative.IFactDefinition definition && definition.FactName.Length > 0)
            {
                return FactShorthand(definition.FactName, op, value);
            }

            return legacy ?? string.Empty;
        }

        /// <summary>The fact shorthand of one comparison.</summary>
        public static string FactShorthand(string factName, CompareOp op, int value)
        {
            if (op == CompareOp.NotEqual && value == 0)
            {
                return FactPrefix + factName;
            }

            return FactPrefix + factName + Compare.Symbol(op) + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a fact shorthand body (<c>name</c>, <c>name!=0</c>, <c>name&gt;=2</c>) into its fact name, operator and
        /// value; false when the text is not one.
        /// </summary>
        public static bool TryParseFactShorthand(string reference, out string factName, out CompareOp op, out int value)
        {
            factName = string.Empty;
            op = CompareOp.NotEqual;
            value = 0;
            if (string.IsNullOrEmpty(reference) || !reference.StartsWith(FactPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            string text = reference.Substring(FactPrefix.Length);
            int split = text.IndexOfAny(new[] { '=', '!', '<', '>' });
            factName = split < 0 ? text : text.Substring(0, split);
            if (factName.Length == 0)
            {
                return false;
            }

            if (split < 0)
            {
                return true;
            }

            string rest = text.Substring(split);
            int symbols = 0;
            while (symbols < rest.Length && (rest[symbols] == '=' || rest[symbols] == '!' || rest[symbols] == '<' || rest[symbols] == '>'))
            {
                symbols++;
            }

            return Compare.TryParse(rest.Substring(0, symbols), out op)
                && int.TryParse(rest.Substring(symbols), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        /// <summary>An action set's authoring id; else the built-in action; else the legacy string.</summary>
        public static string EffectiveAction(ScriptableObject? actionSet, string builtIn, string legacy)
        {
            if (actionSet != null && actionSet is IAuthoredObject set && set.AuthoringId.Length > 0)
            {
                return set.AuthoringId;
            }

            return !string.IsNullOrEmpty(builtIn) ? builtIn : (legacy ?? string.Empty);
        }
    }
}
