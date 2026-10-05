// GameCore.Gameplay.Interaction - InteractableDefinition (P1.3, catalog row 5).
//
// An interactable is an authored entity whose entity definition an InteractableDefinition names (through the
// InteractionRoster). The definition says what kind it is (door, gate, examinable, point, switch), the state it starts
// in, the prompt shown for each state, and two references resolved by other systems: the condition ref (evaluated
// through IConditionEvaluator: while Locked it must be True to unlock; otherwise False refuses the use) and the action
// ref (run through IActionRunner after a committed success). Empty references mean "always allowed" / "no action".
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Interaction;
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
    public sealed class InteractableDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The entity definition placed for this interactable.")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Doc = "Door, Gate, Examinable, Point or Switch.")]
        [SerializeField] private InteractableKind kind = InteractableKind.Examinable;

        [AuthorField(Doc = "Initial state name (empty: the kind's default; doors/gates may start locked).")]
        [SerializeField] private string initialState = string.Empty;

        [AuthorField(Doc = "Prompt per state.")]
        [SerializeField] private List<StatePrompt> prompts = new List<StatePrompt>();

        [AuthorRef(Category = "logic.condition", Required = false, Doc = "Condition ref (e.g. narrative.fact.gate_open): unlocks a locked door when true; refuses a use when false.")]
        [SerializeField] private string conditionRef = string.Empty;

        [AuthorRef(Category = "logic.action", Required = false, Doc = "Action ref run after a committed successful use.")]
        [SerializeField] private string actionRef = string.Empty;

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

        [AuthorField(Doc = "Feedback cue prefix (default interaction.<kind>).")]
        [SerializeField] private string cue = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public InteractableKind Kind => kind;

        public string InitialStateName => initialState;

        public IReadOnlyList<StatePrompt> Prompts => prompts;

        public string ConditionRef => conditionRef;

        public string ActionRef => actionRef;

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

        /// <summary>interaction.linkCondition: the condition and action references.</summary>
        public void Link(string condition, string action)
        {
            conditionRef = condition ?? string.Empty;
            actionRef = action ?? string.Empty;
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
}
