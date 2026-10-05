// GameCore.Gameplay.Inventory.Editor - inventory authoring operations (P1.4, catalog row 8; Studio 03 s4/s5).
//
//   inventory.grantStarting  set the starting count of an item in an inventory (0 removes it)
//   inventory.placeItem      place an item in a region (a WorldItemDefinition on the content set)
//   inventory.setStock       set a vendor's stock line (count or unlimited, buy and sell prices)
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Inventory.Editor
{
    /// <summary>The inventory.* authoring operations.</summary>
    public static class InventoryTools
    {
        [AuthorOperation("inventory.grantStarting", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InventoryValidator), Requires = NarrativeKinds.Inventory,
            Doc = "Sets how many of an item an inventory starts with (0 removes the line).")]
        public static void GrantStarting(
            InventoryDefinition inventory,
            [AuthorArg(Category = NarrativeKinds.Item, Doc = "The item.")] ItemDefinition item,
            [AuthorArg(Min = 0, Doc = "Starting count.")] int count)
        {
            if (inventory == null || item == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ItemUnknown + ": an inventory and an item are required");
            }

            if (count < 0 || (count > 0 && Math.Ceiling(count / (double)Math.Max(1, item.MaxStack)) > inventory.SlotCount))
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.InventoryFull + ": " + count + " " + item.DisplayName + " do not fit " + inventory.name);
            }

            Undo.RecordObject(inventory, "inventory.grantStarting");
            inventory.SetStarting(item, count);
            EditorUtility.SetDirty(inventory);
        }

        [AuthorOperation("inventory.placeItem", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InventoryValidator), Requires = NarrativeKinds.ContentSet,
            Doc = "Places an item in a region as a world item the player can pick up once (action ref inventory.pickup).")]
        public static WorldItemDefinition PlaceItem(
            GameplayContentSet contentSet,
            [AuthorArg(Category = NarrativeKinds.Item, Doc = "The item.")] ItemDefinition item,
            [AuthorArg(Category = "world.region", Doc = "The region.")] RegionDefinition region,
            [AuthorArg(Unit = "m", Doc = "Position in the region.")] Vector3 position,
            [AuthorArg(Required = false, Min = 1, Doc = "How many.")] int count = 1,
            [AuthorArg(Required = false, Doc = "Placed scene entity that shows it (optional).")] string entityId = "",
            [AuthorArg(Required = false, Doc = "Asset name; defaults to the item name.")] string name = "")
        {
            if (item == null || region == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.WorldItemMissingRegion + ": an item and a region are required");
            }

            if (contentSet == null || contentSet.World == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.ContentSetMissingWorld + ": a content set of a world is required");
            }

            bool inWorld = false;
            for (int i = 0; i < contentSet.World.Regions.Count; i++)
            {
                inWorld |= contentSet.World.Regions[i] == region;
            }

            if (!inWorld)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.WorldItemMissingRegion + ": region " + region.name + " is not in world " + contentSet.World.name);
            }

            WorldItemDefinition placed = NarrativeAuthoring.CreateAsset<WorldItemDefinition>(contentSet,
                string.IsNullOrEmpty(name) ? item.name + "_" + region.name : name, "WorldItems", string.Empty, "inventory.placeItem");
            placed.Configure(item, count, region, position, entityId ?? string.Empty);
            EditorUtility.SetDirty(placed);
            return placed;
        }

        [AuthorOperation("inventory.setStock", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(InventoryValidator), Requires = NarrativeKinds.Vendor,
            Doc = "Sets a vendor's stock line: count (or unlimited), the price the player pays and the price the vendor pays.")]
        public static void SetStock(
            VendorDefinition vendor,
            [AuthorArg(Category = NarrativeKinds.Item, Doc = "The item.")] ItemDefinition item,
            [AuthorArg(Min = 0, Doc = "Units in stock.")] int count,
            [AuthorArg(Required = false, Min = -1, Doc = "Buy price (-1 = the item's price).")] int buyPrice = -1,
            [AuthorArg(Required = false, Min = -1, Doc = "Sell price (-1 = half the item's price, 0 = not bought).")] int sellPrice = -1,
            [AuthorArg(Required = false, Doc = "Never runs out.")] bool unlimited = false)
        {
            if (vendor == null || item == null)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.VendorMissingStock + ": a vendor and an item are required");
            }

            if (count < 0 || buyPrice < -1 || sellPrice < -1)
            {
                throw new ArgumentException(NarrativeDiagnosticCodes.VendorOutOfStock + ": counts and prices are not negative");
            }

            Undo.RecordObject(vendor, "inventory.setStock");
            vendor.SetStock(item, count, buyPrice, sellPrice, unlimited);
            EditorUtility.SetDirty(vendor);
            NarrativeAuthoring.ThrowIfInvalid(vendor);
        }
    }

    /// <summary>Validation of items, inventories, vendors, loot tables and world items.</summary>
    [AuthorValidator("inventory.validator", Codes = new[]
    {
        NarrativeDiagnosticCodes.ItemBadStack,
        NarrativeDiagnosticCodes.InventoryBadSlots,
        NarrativeDiagnosticCodes.VendorMissingStock,
        NarrativeDiagnosticCodes.WorldItemMissingRegion,
        NarrativeDiagnosticCodes.LootTableEmpty,
        NarrativeDiagnosticCodes.ItemUnknown,
    })]
    public static class InventoryValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject definition) => LogicValidator.Validate(definition);
    }

    /// <summary>The inventory plugin's catalog registrations (P1.3's catalog contribution seam).</summary>
    public sealed class InventoryCatalogContributor : IGameplayCatalogContributor
    {
        public GameplayCatalogContribution Contribution =>
            new GameplayCatalogContribution("com.gamecore.gameplay.inventory", NarrativeCatalogNames.Inventory.Schemas, NarrativeCatalogNames.Inventory.Entries);
    }
}
