// GameCore.Gameplay.Inventory - InventoryDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>An inventory (the player's, a chest's, an NPC's).</summary>
    [Authorable(NarrativeKinds.Inventory, DisplayName = "Inventory", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An inventory: slots, weight limit, starting currency and items.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Inventory", fileName = "Inventory")]
    public sealed class InventoryDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Min = 1, Max = 64, Structural = true, Doc = "Slot count (the inventory's slot layout).")]
        [SerializeField] private int slotCount = 12;

        [AuthorField(Min = 0, Unit = "g", Doc = "Weight limit (0 = unlimited).")]
        [SerializeField] private int maxWeight;

        [AuthorField(Min = 0, Doc = "Starting currency.")]
        [SerializeField] private int startingCurrency;

        [AuthorField(Doc = "Starting items.")]
        [SerializeField] private List<ItemStackEntry> starting = new List<ItemStackEntry>();

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Owner entity, by authoring id (optional).")]
        [SerializeField] private string ownerEntityId = string.Empty;

        [AuthorField(Doc = "The player's inventory (grants, pickups and purchases go here).")]
        [SerializeField] private bool isPlayer;

        public override string NarrativeKind => NarrativeKinds.Inventory;

        public int SlotCount => slotCount;

        public int MaxWeight => maxWeight;

        public int StartingCurrency => startingCurrency;

        public IReadOnlyList<ItemStackEntry> Starting => starting;

        public string OwnerEntityId => ownerEntityId;

        public bool IsPlayer => isPlayer;

        public void Configure(int slots, int weightLimit, int currency, bool player, string owner)
        {
            slotCount = Math.Max(1, slots);
            maxWeight = Math.Max(0, weightLimit);
            startingCurrency = Math.Max(0, currency);
            isPlayer = player;
            ownerEntityId = owner ?? string.Empty;
        }

        /// <summary>Sets (replaces) the starting count of an item.</summary>
        public void SetStarting(ItemDefinition item, int count)
        {
            for (int i = 0; i < starting.Count; i++)
            {
                if (starting[i].item == item)
                {
                    if (count <= 0)
                    {
                        starting.RemoveAt(i);
                    }
                    else
                    {
                        starting[i].count = count;
                    }

                    return;
                }
            }

            if (count > 0)
            {
                starting.Add(new ItemStackEntry { item = item, count = count });
            }
        }
    }
}
