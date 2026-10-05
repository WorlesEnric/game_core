// GameCore.Gameplay.World - PortalDefinition (P1.1; completed per 05 row 1 by P1.7b: arrival spawn points and a
// condition reference).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>One undirected portal connection between two regions.</summary>
    /// <remarks>
    /// The condition is a <c>logic.conditionSet</c> (the world package cannot name the logic types, so the field is a
    /// ScriptableObject with that category). "The ferry needs the ferryman's favour or the repaired punt" is a condition
    /// set in Any mode with two fact conditions. The world kernel's travel does not evaluate it yet (P1.7a); Studio's
    /// logic.whyNot and the validators read it through <see cref="IConditionGated"/>.
    /// </remarks>
    [Authorable("world.portal", DisplayName = "Portal", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An undirected connection between two regions (each region scene holds one RegionPortal end), the spawn point travellers arrive at on each side, and the condition travel needs.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Portal Definition", fileName = "Portal")]
    public sealed class PortalDefinition : ScriptableObject, IDefinitionAsset, IConditionGated
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Structural = true, Doc = "First region.")]
        [SerializeField] private RegionDefinition? regionA;

        [AuthorRef(Category = "world.region", Structural = true, Doc = "Second region.")]
        [SerializeField] private RegionDefinition? regionB;

        [AuthorField(Doc = "Spawn point of region A where travellers coming from B arrive (empty: the portal end's arrival pose).")]
        [SerializeField] private string spawnPointA = string.Empty;

        [AuthorField(Doc = "Spawn point of region B where travellers coming from A arrive (empty: the portal end's arrival pose).")]
        [SerializeField] private string spawnPointB = string.Empty;

        [AuthorRef(Category = "logic.conditionSet", Required = false, Doc = "Travel through the portal needs these conditions (empty: always open).")]
        [SerializeField] private ScriptableObject? condition;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public RegionDefinition? RegionA => regionA;

        public RegionDefinition? RegionB => regionB;

        public string SpawnPointA => spawnPointA;

        public string SpawnPointB => spawnPointB;

        public ScriptableObject? Condition => condition;

        /// <summary>The condition set's authoring id, or empty when travel is unconditional.</summary>
        public string ConditionRef => condition is IAuthoredObject authored ? authored.AuthoringId : string.Empty;

        /// <summary>The region on the other side of <paramref name="from"/>, or null when the portal does not touch it.</summary>
        public RegionDefinition? Other(RegionDefinition from)
        {
            if (from == regionA)
            {
                return regionB;
            }

            return from == regionB ? regionA : null;
        }

        /// <summary>The spawn point name travellers arriving in <paramref name="arrival"/> use (empty: the end's arrival pose).</summary>
        public string SpawnPointIn(RegionDefinition arrival)
        {
            if (arrival == regionA)
            {
                return spawnPointA;
            }

            return arrival == regionB ? spawnPointB : string.Empty;
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Connect(RegionDefinition a, RegionDefinition b)
        {
            regionA = a;
            regionB = b;
        }

        public void SetSpawnPoints(string inA, string inB)
        {
            spawnPointA = inA ?? string.Empty;
            spawnPointB = inB ?? string.Empty;
        }

        public void SetCondition(ScriptableObject? conditionSet) => condition = conditionSet;

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

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
