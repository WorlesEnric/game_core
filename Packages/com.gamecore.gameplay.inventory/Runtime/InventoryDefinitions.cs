// GameCore.Gameplay.Inventory - items and economy authoring and its conversion (P1.4, catalog row 8).
//
//   ItemDefinition       stack size, weight, price, tags, use actions, icon
//   InventoryDefinition  slots, weight limit, starting currency and items, owner (the player's has isPlayer)
//   VendorDefinition     stock (count or unlimited), buy/sell prices, payment in currency or in an item
//   LootTableDefinition  weighted entries rolled deterministically from a seed
//   WorldItemDefinition  an item lying in a region; picked up once (item.taken) through the action ref inventory.pickup
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
    /// <summary>A starting stack.</summary>
    [Serializable]
    public sealed class ItemStackEntry
    {
        [AuthorRef(Category = NarrativeKinds.Item, Doc = "The item.")]
        public ItemDefinition? item;

        [AuthorField(Min = 1, Doc = "How many.")]
        public int count = 1;
    }

    /// <summary>One line of a vendor's stock.</summary>
    [Serializable]
    public sealed class VendorStockEntry
    {
        [AuthorRef(Category = NarrativeKinds.Item, Doc = "The item.")]
        public ItemDefinition? item;

        [AuthorField(Min = 0, Doc = "Units in stock (ignored when unlimited).")]
        public int stock = 1;

        [AuthorField(Min = -1, Doc = "Price the player pays (-1 = the item's price).")]
        public int buyPrice = -1;

        [AuthorField(Min = -1, Doc = "Price the vendor pays (-1 = half the item's price; 0 = not bought).")]
        public int sellPrice = -1;

        [AuthorField(Doc = "Never runs out.")]
        public bool unlimited;
    }

    /// <summary>One weighted loot entry.</summary>
    [Serializable]
    public sealed class LootEntryDefinition
    {
        [AuthorRef(Category = NarrativeKinds.Item, Doc = "The item.")]
        public ItemDefinition? item;

        [AuthorField(Min = 1, Doc = "Relative weight.")]
        public int weight = 1;

        [AuthorField(Min = 1, Doc = "Least dropped.")]
        public int min = 1;

        [AuthorField(Min = 1, Doc = "Most dropped.")]
        public int max = 1;
    }

    /// <summary>The inventory package's converter.</summary>
    public sealed class InventoryContentConverter : INarrativeContentConverter
    {
        public bool CanConvert(ScriptableObject asset) =>
            asset is ItemDefinition || asset is InventoryDefinition || asset is VendorDefinition || asset is LootTableDefinition || asset is WorldItemDefinition;

        public void Convert(ScriptableObject asset, NarrativeConversion conversion)
        {
            switch (asset)
            {
                case ItemDefinition item:
                    ConvertItem(item, conversion);
                    return;
                case InventoryDefinition inventory:
                    ConvertInventory(inventory, conversion);
                    return;
                case VendorDefinition vendor:
                    ConvertVendor(vendor, conversion);
                    return;
                case LootTableDefinition table:
                    ConvertLoot(table, conversion);
                    return;
                case WorldItemDefinition worldItem:
                    ConvertWorldItem(worldItem, conversion);
                    return;
            }
        }

        private static int ItemKey(ItemDefinition? item, ScriptableObject owner, NarrativeConversion conversion, string what)
        {
            int key = NarrativeRefs.KeyOf(item);
            if (key == 0)
            {
                conversion.Problem(owner, NarrativeDiagnosticCodes.ItemUnknown + ": " + what + " names no item");
                return 0;
            }

            conversion.Convert(item!);
            return key;
        }

        private static void ConvertItem(ItemDefinition item, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(item);
            if (key == 0 || item.MaxStack < 1)
            {
                conversion.Problem(item, NarrativeDiagnosticCodes.ItemBadStack + ": item " + item.name + " needs an authoring id and a stack of 1 or more");
                return;
            }

            ActionSetModel? use = conversion.ActionSet(item.UseActions);
            conversion.Models.AddItem(new ItemModel(key, item.DisplayName, item.MaxStack, item.Weight, item.Price, item.Tags, use != null ? use.Key : 0), item.AuthoringId);
            conversion.Models.Alias(item.name, key);
            conversion.Models.Alias(item.DisplayName, key);
        }

        private static void ConvertInventory(InventoryDefinition inventory, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(inventory);
            if (key == 0 || inventory.SlotCount < 1 || inventory.SlotCount > 64)
            {
                conversion.Problem(inventory, NarrativeDiagnosticCodes.InventoryBadSlots + ": inventory " + inventory.name + " needs 1..64 slots");
                return;
            }

            var starting = new List<ItemStack>();
            for (int i = 0; i < inventory.Starting.Count; i++)
            {
                int item = ItemKey(inventory.Starting[i].item, inventory, conversion, "starting stack " + i);
                if (item != 0)
                {
                    starting.Add(new ItemStack(item, Math.Max(1, inventory.Starting[i].count)));
                }
            }

            conversion.Models.AddInventory(new InventoryModel(key, inventory.DefinitionName, inventory.SlotCount, inventory.MaxWeight, inventory.StartingCurrency,
                starting, NarrativeRefs.EntityKey(inventory.OwnerEntityId), inventory.IsPlayer), inventory.AuthoringId);
            conversion.Models.Alias(inventory.DefinitionName, key);
        }

        private static void ConvertVendor(VendorDefinition vendor, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(vendor);
            if (key == 0)
            {
                conversion.Problem(vendor, NarrativeDiagnosticCodes.ContentKeyCollision + ": vendor " + vendor.name + " has no authoring id");
                return;
            }

            if (vendor.Stock.Count == 0)
            {
                conversion.Problem(vendor, NarrativeDiagnosticCodes.VendorMissingStock + ": vendor " + vendor.name + " sells nothing");
            }

            var entries = new List<VendorEntry>();
            for (int i = 0; i < vendor.Stock.Count; i++)
            {
                VendorStockEntry line = vendor.Stock[i];
                int item = ItemKey(line.item, vendor, conversion, "stock line " + i);
                if (item == 0)
                {
                    continue;
                }

                int basePrice = line.item != null ? line.item.Price : 0;
                int buy = line.buyPrice >= 0 ? line.buyPrice : basePrice;
                int sell = line.sellPrice >= 0 ? line.sellPrice : basePrice / 2;
                entries.Add(new VendorEntry(item, Math.Max(0, line.stock), buy, sell, line.unlimited));
            }

            int payment = vendor.PaymentItem != null ? ItemKey(vendor.PaymentItem, vendor, conversion, "payment") : 0;
            if (vendor.StockSlots < entries.Count)
            {
                conversion.Problem(vendor, NarrativeDiagnosticCodes.InventoryBadSlots + ": vendor " + vendor.name + " has fewer stock slots than stock lines");
            }

            conversion.Models.AddVendor(new VendorModel(key, vendor.DisplayName, entries, payment, vendor.StockSlots), vendor.AuthoringId);
            conversion.Models.Alias(vendor.name, key);
            conversion.Models.Alias(vendor.DisplayName, key);
        }

        private static void ConvertLoot(LootTableDefinition table, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(table);
            if (key == 0 || table.Entries.Count == 0)
            {
                conversion.Problem(table, NarrativeDiagnosticCodes.LootTableEmpty + ": loot table " + table.name + " has no entries");
                return;
            }

            var entries = new List<LootEntry>();
            for (int i = 0; i < table.Entries.Count; i++)
            {
                LootEntryDefinition entry = table.Entries[i];
                int item = ItemKey(entry.item, table, conversion, "loot entry " + i);
                if (item != 0)
                {
                    entries.Add(new LootEntry(item, Math.Max(1, entry.weight), Math.Max(1, entry.min), Math.Max(entry.min, entry.max)));
                }
            }

            conversion.Models.AddLootTable(new LootTableModel(key, table.DefinitionName, table.Rolls, entries), table.AuthoringId);
            conversion.Models.Alias(table.DefinitionName, key);
        }

        private static void ConvertWorldItem(WorldItemDefinition worldItem, NarrativeConversion conversion)
        {
            int key = NarrativeRefs.KeyOf(worldItem);
            int item = ItemKey(worldItem.Item, worldItem, conversion, "world item " + worldItem.name);
            if (key == 0 || item == 0)
            {
                return;
            }

            if (worldItem.Region == null || !AuthoringIds.IsValid(worldItem.Region.AuthoringId))
            {
                conversion.Problem(worldItem, NarrativeDiagnosticCodes.WorldItemMissingRegion + ": world item " + worldItem.name + " lies in no region");
                return;
            }

            Vector3 at = worldItem.Position;
            conversion.Models.AddWorldItem(new WorldItemModel(key, worldItem.AuthoringId, worldItem.DefinitionName, item, worldItem.Count,
                AuthoringIds.StableKey(worldItem.Region.AuthoringId), worldItem.Region.AuthoringId,
                Mathf.RoundToInt(at.x * 1000f), Mathf.RoundToInt(at.y * 1000f), Mathf.RoundToInt(at.z * 1000f), worldItem.EntityId));
            conversion.Models.Alias(worldItem.DefinitionName, key);
            if (AuthoringIds.IsValid(worldItem.EntityId))
            {
                conversion.Models.Alias(worldItem.EntityId, key);
            }
        }
    }
}
