// GameCore.Gameplay.Interaction - TriggerDefinition (P1.3, catalog row 5).
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
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
    public sealed class TriggerDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "entity.definition", Doc = "The entity definition placed for this trigger.")]
        [SerializeField] private EntityDefinition? entity;

        [AuthorField(Unit = "m", Min = 0.1, Max = 100, Doc = "Box size (x, y, z) centred on the placement, rotated by its yaw.")]
        [SerializeField] private Vector3 size = new Vector3(3f, 3f, 3f);

        [AuthorField(Min = 0, Max = 1000, Doc = "Maximum occupants (0 = unlimited).")]
        [SerializeField] private int maxOccupants;

        [AuthorRef(Category = "logic.condition", Required = false, Doc = "Condition ref; when it is false entries are ignored.")]
        [SerializeField] private string conditionRef = string.Empty;

        [AuthorRef(Category = "logic.action", Required = false, Doc = "Action ref run on a committed first entry.")]
        [SerializeField] private string actionRef = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public EntityDefinition? Entity => entity;

        public Vector3 Size => size;

        public int MaxOccupants => maxOccupants;

        public string ConditionRef => conditionRef;

        public string ActionRef => actionRef;

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
