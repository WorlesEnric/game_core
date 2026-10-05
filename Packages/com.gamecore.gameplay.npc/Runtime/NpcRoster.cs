// GameCore.Gameplay.Npc - NpcDefinition, BehaviourDefinition, ScheduleDefinition and NpcRoster (P1.3, catalog row 4).
//
// An NPC is an authored entity (AuthoredEntity + EntityDefinition, placed in a region scene and baked like any other)
// whose entity definition an NpcDefinition names. The roster lists the NpcDefinitions of a game; at boot every placed
// entity whose definition one of them names becomes an NPC. The definitions hold presentation and behaviour data in
// authoring units (m, m/s, s) and convert once to the integer rule types (mm, mm/s, ms).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Npc;
using UnityEngine;

namespace GameCore.Gameplay.Npc
{
    /// <summary>The NPC kinds of a game and the NPC simulation settings.</summary>
    [Authorable("npc.roster", DisplayName = "NPC Roster", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Every NPC definition of a game, the logical step length and the update stride of NPCs in unloaded regions.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/NPC Roster", fileName = "NpcRoster")]
    public sealed class NpcRoster : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "npc.definition", Doc = "NPC definitions; each names a distinct entity definition.")]
        [SerializeField] private List<NpcDefinition> npcs = new List<NpcDefinition>();

        [AuthorField(Unit = "ms", Min = 5, Max = 100, Doc = "Logical duration of one step (NPC movement and world time).")]
        [SerializeField] private int stepMilliseconds = 20;

        [AuthorField(Min = 1, Max = 64, Doc = "NPCs in unloaded regions update every N-th step (with N steps of time).")]
        [SerializeField] private int unloadedStride = 8;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public IReadOnlyList<NpcDefinition> Npcs => npcs;

        public int StepMilliseconds => stepMilliseconds;

        public int UnloadedStride => unloadedStride;

        public NpcDefinition? FindByEntityDefinition(string entityDefinitionId)
        {
            for (int i = 0; i < npcs.Count; i++)
            {
                if (npcs[i] != null && npcs[i].Entity != null && string.Equals(npcs[i].Entity!.AuthoringId, entityDefinitionId, StringComparison.Ordinal))
                {
                    return npcs[i];
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

        public void Add(NpcDefinition definition)
        {
            if (definition != null && !npcs.Contains(definition))
            {
                npcs.Add(definition);
            }
        }

        public void Configure(int step, int stride)
        {
            stepMilliseconds = step;
            unloadedStride = stride;
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
