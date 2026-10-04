// GameCore.Gameplay.World - PortalDefinition (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>One undirected portal connection between two regions.</summary>
    [Authorable("world.portal", DisplayName = "Portal", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An undirected connection between two regions; each region scene holds one RegionPortal end of it.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Portal Definition", fileName = "Portal")]
    public sealed class PortalDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Doc = "First region.")]
        [SerializeField] private RegionDefinition? regionA;

        [AuthorRef(Category = "world.region", Doc = "Second region.")]
        [SerializeField] private RegionDefinition? regionB;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public RegionDefinition? RegionA => regionA;

        public RegionDefinition? RegionB => regionB;

        /// <summary>The region on the other side of <paramref name="from"/>, or null when the portal does not touch it.</summary>
        public RegionDefinition? Other(RegionDefinition from)
        {
            if (from == regionA)
            {
                return regionB;
            }

            return from == regionB ? regionA : null;
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
