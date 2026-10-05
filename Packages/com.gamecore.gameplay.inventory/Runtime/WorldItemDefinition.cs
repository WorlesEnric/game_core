// GameCore.Gameplay.Inventory - WorldItemDefinition (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Inventory
{
    /// <summary>An item lying in the world.</summary>
    [Authorable(NarrativeKinds.WorldItem, DisplayName = "World Item", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An item placed in a region; the interaction action ref inventory.pickup moves it into the player's inventory once.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/World Item", fileName = "WorldItem")]
    public sealed class WorldItemDefinition : NarrativeDefinitionAsset
    {
        [AuthorRef(Category = NarrativeKinds.Item, Doc = "The item.")]
        [SerializeField] private ItemDefinition? item;

        [AuthorField(Min = 1, Doc = "How many.")]
        [SerializeField] private int count = 1;

        [AuthorRef(Category = "world.region", Doc = "The region it lies in.")]
        [SerializeField] private RegionDefinition? region;

        [AuthorField(Unit = "m", Doc = "Position in the region.")]
        [SerializeField] private Vector3 position;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Placed scene entity that shows it, by authoring id (optional; P1.3's interactable).")]
        [SerializeField] private string entityId = string.Empty;

        public override string NarrativeKind => NarrativeKinds.WorldItem;

        public ItemDefinition? Item => item;

        public int Count => count;

        public RegionDefinition? Region => region;

        public Vector3 Position => position;

        public string EntityId => entityId;

        public void Configure(ItemDefinition? what, int howMany, RegionDefinition? where, Vector3 at, string entity)
        {
            item = what;
            count = Math.Max(1, howMany);
            region = where;
            position = at;
            entityId = entity ?? string.Empty;
        }
    }
}
