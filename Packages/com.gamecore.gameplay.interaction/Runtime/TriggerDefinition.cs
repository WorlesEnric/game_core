// GameCore.Gameplay.Interaction - TriggerDefinition (P1.3, catalog row 5).
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>
    /// A trigger volume kind: an entity whose placement is the centre of a box; actors entering or leaving it (judged
    /// from committed positions by the ProximityInteractor) commit TriggerEntered / TriggerExited and update the
    /// interact.occupants slot. The action ref runs on a committed first entry.
    /// </summary>
    [Authorable("interaction.trigger", DisplayName = "Trigger Volume", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A trigger volume: box size around the placed entity, occupant limit, condition and action references.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Trigger Definition", fileName = "Trigger")]
    public sealed class TriggerDefinition : ScriptableObject, IDefinitionAsset, IConditionGated
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Structural = true, Doc = "The entity definition placed for this trigger.")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Unit = "m", Min = 0.1, Max = 100, Doc = "Box size (x, y, z) centred on the placement, rotated by its yaw.")]
        [SerializeField] private Vector3 size = new Vector3(3f, 3f, 3f);

        [AuthorField(Min = 0, Max = 1000, Doc = "Maximum occupants (0 = unlimited).")]
        [SerializeField] private int maxOccupants;

        [AuthorRef(Category = "logic.conditionSet", Required = false, Doc = "Condition set: entries are ignored while it fails.")]
        [SerializeField] private ScriptableObject? condition;

        [AuthorRef(Category = "narrative.fact", Required = false, Doc = "Fact condition (used when no condition set is given): the fact compared with conditionValue.")]
        [SerializeField] private ScriptableObject? conditionFact;

        [AuthorField(Doc = "Comparison of the fact condition (default: the fact is not 0).")]
        [SerializeField] private CompareOp conditionOp = CompareOp.NotEqual;

        [AuthorField(Doc = "Value the fact condition compares with.")]
        [SerializeField] private int conditionValue;

        // Legacy (P1.3): the condition as a string ref (a condition set id/name or narrative.fact.<name>[op N]). Read only
        // when no typed condition is set; authoring.migrateRefs moves it into the typed fields and clears it.
        [SerializeField, HideInInspector] private string conditionRef = string.Empty;

        [AuthorRef(Category = "logic.actionSet", Required = false, Doc = "Action set run on a committed first entry (through the outbox).")]
        [SerializeField] private ScriptableObject? actions;

        [AuthorField(Doc = "Built-in action used when no action set is given (inventory.pickup: pick up the subject world item).")]
        [SerializeField] private string builtInAction = string.Empty;

        // Legacy (P1.3): the action as a string ref; migrated like conditionRef.
        [SerializeField, HideInInspector] private string actionRef = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public Vector3 Size => size;

        public int MaxOccupants => maxOccupants;

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

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(EntityDefinition? entityDefinition, Vector3 boxSize, int occupants)
        {
            entity = entityDefinition;
            size = boxSize;
            maxOccupants = occupants;
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
}
