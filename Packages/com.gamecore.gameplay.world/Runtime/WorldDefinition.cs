// GameCore.Gameplay.World - WorldDefinition (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>The regions and portals of one world.</summary>
    [Authorable("world.definition", DisplayName = "World", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A world: its regions, portals, start region and streaming options.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/World Definition", fileName = "World")]
    public sealed class WorldDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Doc = "Regions of the world.")]
        [SerializeField] private List<RegionDefinition> regions = new List<RegionDefinition>();

        [AuthorRef(Category = "world.portal", Required = false, Doc = "Portal connections between regions.")]
        [SerializeField] private List<PortalDefinition> portals = new List<PortalDefinition>();

        [AuthorRef(Category = "world.region", Doc = "The region the game starts in.")]
        [SerializeField] private RegionDefinition? startRegion;

        [AuthorField(Doc = "Keep the neighbours of the focus region resident as well.")]
        [SerializeField] private bool preloadNeighbours;

        [AuthorField(Doc = "Authoring id of the placed entity the streamer follows (the traveller).")]
        [SerializeField] private string focusEntityId = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => contentStamp;

        public IReadOnlyList<RegionDefinition> Regions => regions;

        public IReadOnlyList<PortalDefinition> Portals => portals;

        public RegionDefinition? StartRegion => startRegion;

        public bool PreloadNeighbours => preloadNeighbours;

        public string FocusEntityId => focusEntityId;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void AddRegion(RegionDefinition region)
        {
            if (region != null && !regions.Contains(region))
            {
                regions.Add(region);
            }
        }

        public void AddPortal(PortalDefinition portal)
        {
            if (portal != null && !portals.Contains(portal))
            {
                portals.Add(portal);
            }
        }

        public void SetStartRegion(RegionDefinition? region) => startRegion = region;

        public void SetPreloadNeighbours(bool value) => preloadNeighbours = value;

        public void SetFocusEntity(string entityId) => focusEntityId = entityId ?? string.Empty;

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
