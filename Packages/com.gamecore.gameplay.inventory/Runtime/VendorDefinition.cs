// GameCore.Gameplay.Inventory - VendorDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>A vendor.</summary>
    [Authorable(NarrativeKinds.Vendor, DisplayName = "Vendor", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A vendor: stock, prices, and payment in currency or in an item (old coins).")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Vendor", fileName = "Vendor")]
    public sealed class VendorDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "Display name.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Doc = "Stock lines.")]
        [SerializeField] private List<VendorStockEntry> stock = new List<VendorStockEntry>();

        [AuthorRef(Category = NarrativeKinds.Item, Required = false, Doc = "Payment item (empty = currency).")]
        [SerializeField] private ItemDefinition? paymentItem;

        [AuthorField(Min = 1, Max = 64, Structural = true, Doc = "Stock slots (the vendor's slot layout).")]
        [SerializeField] private int stockSlots = 8;

        [AuthorRef(Category = AuthorRefCategories.EntityInstance, Required = false, Doc = "Vendor entity, by authoring id (optional).")]
        [SerializeField] private string vendorEntityId = string.Empty;

        public override string NarrativeKind => NarrativeKinds.Vendor;

        public string DisplayName => displayName.Length > 0 ? displayName : name;

        public IReadOnlyList<VendorStockEntry> Stock => stock;

        public ItemDefinition? PaymentItem => paymentItem;

        public int StockSlots => stockSlots;

        public string VendorEntityId => vendorEntityId;

        public void Configure(string shownName, ItemDefinition? payment, int slots, string entityId)
        {
            displayName = shownName ?? string.Empty;
            paymentItem = payment;
            stockSlots = Math.Max(1, slots);
            vendorEntityId = entityId ?? string.Empty;
        }

        /// <summary>inventory.setPrice on a vendor: the buy price of an existing stock line; false when the vendor does not stock the item.</summary>
        public bool SetBuyPrice(ItemDefinition item, int buy)
        {
            for (int i = 0; i < stock.Count; i++)
            {
                if (stock[i].item == item)
                {
                    stock[i].buyPrice = buy;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Sets (replaces) the stock line of an item.</summary>
        public void SetStock(ItemDefinition item, int count, int buy, int sell, bool unlimited)
        {
            for (int i = 0; i < stock.Count; i++)
            {
                if (stock[i].item == item)
                {
                    stock[i].stock = count;
                    stock[i].buyPrice = buy;
                    stock[i].sellPrice = sell;
                    stock[i].unlimited = unlimited;
                    return;
                }
            }

            stock.Add(new VendorStockEntry { item = item, stock = count, buyPrice = buy, sellPrice = sell, unlimited = unlimited });
        }
    }
}
