// GameCore.Gameplay.Interaction - InteractionRoster (P1.3, catalog row 5).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.Interaction
{
    /// <summary>The interactable and trigger kinds of a game and the interaction step length.</summary>
    [Authorable("interaction.roster", DisplayName = "Interaction Roster", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Every interactable and trigger definition of a game, each naming a distinct entity definition; the logical step length for cooldowns.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Interaction Roster", fileName = "InteractionRoster")]
    public sealed class InteractionRoster : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "interaction.interactable", Doc = "Interactable definitions.")]
        [SerializeField] private List<InteractableDefinition> interactables = new List<InteractableDefinition>();

        [AuthorRef(Category = "interaction.trigger", Doc = "Trigger definitions.")]
        [SerializeField] private List<TriggerDefinition> triggers = new List<TriggerDefinition>();

        [AuthorField(Unit = "ms", Min = 5, Max = 100, Doc = "Logical duration of one step (cooldowns).")]
        [SerializeField] private int stepMilliseconds = 20;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public IReadOnlyList<InteractableDefinition> Interactables => interactables;

        public IReadOnlyList<TriggerDefinition> Triggers => triggers;

        public int StepMilliseconds => stepMilliseconds;

        public InteractableDefinition? FindInteractable(string entityDefinitionId)
        {
            for (int i = 0; i < interactables.Count; i++)
            {
                EntityDefinition? entity = interactables[i] != null ? interactables[i].Entity : null;
                if (entity != null && string.Equals(entity.AuthoringId, entityDefinitionId, StringComparison.Ordinal))
                {
                    return interactables[i];
                }
            }

            return null;
        }

        public TriggerDefinition? FindTrigger(string entityDefinitionId)
        {
            for (int i = 0; i < triggers.Count; i++)
            {
                EntityDefinition? entity = triggers[i] != null ? triggers[i].Entity : null;
                if (entity != null && string.Equals(entity.AuthoringId, entityDefinitionId, StringComparison.Ordinal))
                {
                    return triggers[i];
                }
            }

            return null;
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Add(InteractableDefinition definition)
        {
            if (definition != null && !interactables.Contains(definition))
            {
                interactables.Add(definition);
            }
        }

        public void Add(TriggerDefinition definition)
        {
            if (definition != null && !triggers.Contains(definition))
            {
                triggers.Add(definition);
            }
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
