// GameCore.Rules.Gameplay.Inventory - pure inventory, trade and loot rules (P1.4, catalog row 8).
//
// An inventory is K slots (K from its definition), each holding an item key (0 = empty) and a stack count, plus a
// currency. Every operation is all-or-nothing: it is planned on a copy and applied only when it fits (slots and the
// weight limit), so a refused grant, consume or trade never leaves a partial mutation. Vendors trade from their own
// stock inventory and may price in an item (Hollowmere's old coins) instead of currency.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Rules.Gameplay.Inventory
{
    /// <summary>Why an inventory operation was refused.</summary>
    public enum InventoryRefusal
    {
        None = 0,
        Full = 1,
        Overweight = 2,
        NotHeld = 3,
        CurrencyShort = 4,
        OutOfStock = 5,
        NotSold = 6,
        UnknownItem = 7,
        BadCount = 8,
        Taken = 9,
    }

    /// <summary>An item definition baked to a model.</summary>
    public sealed class ItemModel
    {
        public ItemModel(int key, string name, int maxStack, int weight, int price, IReadOnlyList<string>? tags, int useEffectKey)
        {
            Key = key;
            Name = name ?? string.Empty;
            MaxStack = maxStack < 1 ? 1 : maxStack;
            Weight = weight < 0 ? 0 : weight;
            Price = price < 0 ? 0 : price;
            Tags = tags ?? Array.Empty<string>();
            UseEffectKey = useEffectKey;
        }

        public int Key { get; }

        public string Name { get; }

        /// <summary>Most items one slot holds.</summary>
        public int MaxStack { get; }

        /// <summary>Grams per item.</summary>
        public int Weight { get; }

        /// <summary>Base price (currency units).</summary>
        public int Price { get; }

        public IReadOnlyList<string> Tags { get; }

        /// <summary>Action set run when one is consumed (0 = none).</summary>
        public int UseEffectKey { get; }

        public bool HasTag(string tag)
        {
            for (int i = 0; i < Tags.Count; i++)
            {
                if (string.Equals(Tags[i], tag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Resolves items by key.</summary>
    public interface IItemLookup
    {
        bool TryGet(int key, out ItemModel? item);
    }

    /// <summary>A dictionary-backed item lookup.</summary>
    public sealed class ItemTable : IItemLookup
    {
        private readonly Dictionary<int, ItemModel> items = new Dictionary<int, ItemModel>();

        public int Count => items.Count;

        public IEnumerable<ItemModel> Items => items.Values;

        public ItemTable Add(ItemModel item)
        {
            if (item != null)
            {
                items[item.Key] = item;
            }

            return this;
        }

        public bool TryGet(int key, out ItemModel? item)
        {
            if (items.TryGetValue(key, out ItemModel found))
            {
                item = found;
                return true;
            }

            item = null;
            return false;
        }
    }

    /// <summary>One starting stack of an inventory.</summary>
    public readonly struct ItemStack
    {
        public ItemStack(int itemKey, int count)
        {
            ItemKey = itemKey;
            Count = count;
        }

        public int ItemKey { get; }

        public int Count { get; }
    }

    /// <summary>An inventory definition baked to a model (the player's pack, a vendor's stock, a chest).</summary>
    public sealed class InventoryModel
    {
        public InventoryModel(
            int key,
            string name,
            int slotCount,
            int maxWeight,
            int startingCurrency,
            IReadOnlyList<ItemStack>? starting,
            int ownerEntityKey,
            bool isPlayer)
        {
            Key = key;
            Name = name ?? string.Empty;
            SlotCount = slotCount < 1 ? 1 : slotCount;
            MaxWeight = maxWeight < 0 ? 0 : maxWeight;
            StartingCurrency = startingCurrency < 0 ? 0 : startingCurrency;
            Starting = starting ?? Array.Empty<ItemStack>();
            OwnerEntityKey = ownerEntityKey;
            IsPlayer = isPlayer;
        }

        public int Key { get; }

        public string Name { get; }

        /// <summary>K: the inventory holds inv.item.0..K-1 and inv.count.0..K-1.</summary>
        public int SlotCount { get; }

        /// <summary>Grams; 0 = no limit.</summary>
        public int MaxWeight { get; }

        public int StartingCurrency { get; }

        public IReadOnlyList<ItemStack> Starting { get; }

        /// <summary>Stable key of the entity that carries it (0 = none).</summary>
        public int OwnerEntityKey { get; }

        /// <summary>True for the inventory "the actor" (key 0) resolves to.</summary>
        public bool IsPlayer { get; }
    }

    /// <summary>The mutable contents of one inventory: K slots and a currency.</summary>
    public sealed class InventorySlots
    {
        public InventorySlots(int slotCount)
        {
            int count = slotCount < 1 ? 1 : slotCount;
            Items = new int[count];
            Counts = new int[count];
        }

        public int[] Items { get; }

        public int[] Counts { get; }

        public int Currency { get; set; }

        public int SlotCount => Items.Length;

        public InventorySlots Clone()
        {
            var copy = new InventorySlots(Items.Length) { Currency = Currency };
            Array.Copy(Items, copy.Items, Items.Length);
            Array.Copy(Counts, copy.Counts, Counts.Length);
            return copy;
        }

        public void CopyFrom(InventorySlots other)
        {
            int n = Math.Min(Items.Length, other.Items.Length);
            Array.Copy(other.Items, Items, n);
            Array.Copy(other.Counts, Counts, n);
            Currency = other.Currency;
        }

        /// <summary>Slot indices whose item or count differ from <paramref name="before"/>.</summary>
        public List<int> ChangedSince(InventorySlots before)
        {
            var changed = new List<int>();
            for (int i = 0; i < Items.Length; i++)
            {
                if (i >= before.Items.Length || Items[i] != before.Items[i] || Counts[i] != before.Counts[i])
                {
                    changed.Add(i);
                }
            }

            return changed;
        }
    }

    /// <summary>The outcome of one inventory operation.</summary>
    public readonly struct InventoryOutcome
    {
        public InventoryOutcome(InventoryRefusal refusal, int amount, int total)
        {
            Refusal = refusal;
            Amount = amount;
            Total = total;
        }

        public bool Accepted => Refusal == InventoryRefusal.None;

        public InventoryRefusal Refusal { get; }

        /// <summary>Items added or removed (or the price of a trade).</summary>
        public int Amount { get; }

        /// <summary>The item's count in the inventory afterwards.</summary>
        public int Total { get; }

        public static InventoryOutcome Refused(InventoryRefusal refusal) => new InventoryOutcome(refusal, 0, 0);
    }

    /// <summary>Pure inventory operations.</summary>
    public static class InventoryRules
    {
        public static int CountOf(InventorySlots slots, int itemKey)
        {
            int total = 0;
            for (int i = 0; i < slots.Items.Length; i++)
            {
                if (slots.Items[i] == itemKey && itemKey != 0)
                {
                    total += slots.Counts[i];
                }
            }

            return total;
        }

        /// <summary>Total grams held (unknown items weigh nothing).</summary>
        public static int TotalWeight(InventorySlots slots, IItemLookup items)
        {
            long total = 0;
            for (int i = 0; i < slots.Items.Length; i++)
            {
                if (slots.Items[i] != 0 && items.TryGet(slots.Items[i], out ItemModel? item) && item != null)
                {
                    total += (long)item.Weight * slots.Counts[i];
                }
            }

            return total > int.MaxValue ? int.MaxValue : (int)total;
        }

        /// <summary>
        /// Adds <paramref name="count"/> items: tops up existing stacks first, then fills empty slots in order. All or
        /// nothing: Full when the stacks cannot hold them, Overweight when the weight limit would be passed.
        /// </summary>
        public static InventoryOutcome TryAdd(InventorySlots slots, ItemModel item, int count, int maxWeight, IItemLookup items)
        {
            if (count <= 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.BadCount);
            }

            if (maxWeight > 0 && (long)TotalWeight(slots, items) + (long)item.Weight * count > maxWeight)
            {
                return InventoryOutcome.Refused(InventoryRefusal.Overweight);
            }

            InventorySlots plan = slots.Clone();
            int remaining = count;
            for (int i = 0; i < plan.Items.Length && remaining > 0; i++)
            {
                if (plan.Items[i] == item.Key && plan.Counts[i] < item.MaxStack)
                {
                    int room = item.MaxStack - plan.Counts[i];
                    int put = room < remaining ? room : remaining;
                    plan.Counts[i] += put;
                    remaining -= put;
                }
            }

            for (int i = 0; i < plan.Items.Length && remaining > 0; i++)
            {
                if (plan.Items[i] == 0)
                {
                    int put = item.MaxStack < remaining ? item.MaxStack : remaining;
                    plan.Items[i] = item.Key;
                    plan.Counts[i] = put;
                    remaining -= put;
                }
            }

            if (remaining > 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.Full);
            }

            slots.CopyFrom(plan);
            return new InventoryOutcome(InventoryRefusal.None, count, CountOf(slots, item.Key));
        }

        /// <summary>Removes <paramref name="count"/> items, emptying stacks from the last slot backwards. All or nothing.</summary>
        public static InventoryOutcome TryRemove(InventorySlots slots, int itemKey, int count)
        {
            if (count <= 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.BadCount);
            }

            if (CountOf(slots, itemKey) < count)
            {
                return InventoryOutcome.Refused(InventoryRefusal.NotHeld);
            }

            int remaining = count;
            for (int i = slots.Items.Length - 1; i >= 0 && remaining > 0; i--)
            {
                if (slots.Items[i] != itemKey)
                {
                    continue;
                }

                int take = slots.Counts[i] < remaining ? slots.Counts[i] : remaining;
                slots.Counts[i] -= take;
                remaining -= take;
                if (slots.Counts[i] == 0)
                {
                    slots.Items[i] = 0;
                }
            }

            return new InventoryOutcome(InventoryRefusal.None, count, CountOf(slots, itemKey));
        }

        /// <summary>Moves items between two inventories, all or nothing.</summary>
        public static InventoryOutcome TryTransfer(
            InventorySlots from,
            InventorySlots to,
            ItemModel item,
            int count,
            int toMaxWeight,
            IItemLookup items)
        {
            InventorySlots source = from.Clone();
            InventoryOutcome removed = TryRemove(source, item.Key, count);
            if (!removed.Accepted)
            {
                return removed;
            }

            InventorySlots destination = to.Clone();
            InventoryOutcome added = TryAdd(destination, item, count, toMaxWeight, items);
            if (!added.Accepted)
            {
                return added;
            }

            from.CopyFrom(source);
            to.CopyFrom(destination);
            return new InventoryOutcome(InventoryRefusal.None, count, added.Total);
        }

        /// <summary>Fills an empty inventory with its definition's starting stacks (stacks that do not fit are dropped).</summary>
        public static InventorySlots Starting(InventoryModel model, IItemLookup items)
        {
            var slots = new InventorySlots(model.SlotCount) { Currency = model.StartingCurrency };
            for (int i = 0; i < model.Starting.Count; i++)
            {
                ItemStack stack = model.Starting[i];
                if (items.TryGet(stack.ItemKey, out ItemModel? item) && item != null && stack.Count > 0)
                {
                    TryAdd(slots, item, stack.Count, 0, items);
                }
            }

            return slots;
        }
    }

    /// <summary>One item a vendor trades.</summary>
    public readonly struct VendorEntry
    {
        public VendorEntry(int itemKey, int stock, int buyPrice, int sellPrice, bool unlimited)
        {
            ItemKey = itemKey;
            Stock = stock < 0 ? 0 : stock;
            BuyPrice = buyPrice < 0 ? 0 : buyPrice;
            SellPrice = sellPrice < 0 ? 0 : sellPrice;
            Unlimited = unlimited;
        }

        public int ItemKey { get; }

        /// <summary>Starting stock (the vendor's stock inventory is seeded with it).</summary>
        public int Stock { get; }

        /// <summary>What the player pays per item.</summary>
        public int BuyPrice { get; }

        /// <summary>What the vendor pays per item (0 = the vendor does not buy it).</summary>
        public int SellPrice { get; }

        /// <summary>Selling never depletes the stock.</summary>
        public bool Unlimited { get; }
    }

    /// <summary>A vendor (VendorDefinition baked to a model). Its stock lives in its own inventory target.</summary>
    public sealed class VendorModel
    {
        public VendorModel(int key, string name, IReadOnlyList<VendorEntry> entries, int paymentItemKey, int stockSlots)
        {
            Key = key;
            Name = name ?? string.Empty;
            Entries = entries ?? Array.Empty<VendorEntry>();
            PaymentItemKey = paymentItemKey;
            StockSlots = stockSlots < 1 ? 1 : stockSlots;
        }

        public int Key { get; }

        public string Name { get; }

        public IReadOnlyList<VendorEntry> Entries { get; }

        /// <summary>The item prices are paid in (0 = inv.currency).</summary>
        public int PaymentItemKey { get; }

        public int StockSlots { get; }

        public bool TryFind(int itemKey, out VendorEntry entry)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].ItemKey == itemKey)
                {
                    entry = Entries[i];
                    return true;
                }
            }

            entry = default(VendorEntry);
            return false;
        }

        /// <summary>The inventory model of the vendor's stock (seeded from the entries).</summary>
        public InventoryModel StockInventory()
        {
            var starting = new List<ItemStack>();
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Stock > 0)
                {
                    starting.Add(new ItemStack(Entries[i].ItemKey, Entries[i].Stock));
                }
            }

            return new InventoryModel(Key, Name, StockSlots, 0, 0, starting, 0, false);
        }
    }

    /// <summary>Pure trade operations: the buyer and the vendor's stock change together or not at all.</summary>
    public static class TradeRules
    {
        /// <summary>The buyer buys <paramref name="count"/> of an item from the vendor. Amount = total price.</summary>
        public static InventoryOutcome TryBuy(
            InventorySlots buyer,
            int buyerMaxWeight,
            InventorySlots stock,
            VendorModel vendor,
            ItemModel item,
            int count,
            IItemLookup items)
        {
            if (count <= 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.BadCount);
            }

            if (!vendor.TryFind(item.Key, out VendorEntry entry))
            {
                return InventoryOutcome.Refused(InventoryRefusal.NotSold);
            }

            if (!entry.Unlimited && InventoryRules.CountOf(stock, item.Key) < count)
            {
                return InventoryOutcome.Refused(InventoryRefusal.OutOfStock);
            }

            long priceLong = (long)entry.BuyPrice * count;
            if (priceLong > int.MaxValue)
            {
                return InventoryOutcome.Refused(InventoryRefusal.CurrencyShort);
            }

            int price = (int)priceLong;
            InventorySlots nextBuyer = buyer.Clone();
            InventorySlots nextStock = stock.Clone();
            if (price > 0)
            {
                if (vendor.PaymentItemKey != 0)
                {
                    if (!InventoryRules.TryRemove(nextBuyer, vendor.PaymentItemKey, price).Accepted)
                    {
                        return InventoryOutcome.Refused(InventoryRefusal.CurrencyShort);
                    }

                    if (items.TryGet(vendor.PaymentItemKey, out ItemModel? payment) && payment != null)
                    {
                        InventoryRules.TryAdd(nextStock, payment, price, 0, items);
                    }
                }
                else
                {
                    if (nextBuyer.Currency < price)
                    {
                        return InventoryOutcome.Refused(InventoryRefusal.CurrencyShort);
                    }

                    nextBuyer.Currency -= price;
                    nextStock.Currency += price;
                }
            }

            if (!entry.Unlimited && !InventoryRules.TryRemove(nextStock, item.Key, count).Accepted)
            {
                return InventoryOutcome.Refused(InventoryRefusal.OutOfStock);
            }

            InventoryOutcome added = InventoryRules.TryAdd(nextBuyer, item, count, buyerMaxWeight, items);
            if (!added.Accepted)
            {
                return added;
            }

            buyer.CopyFrom(nextBuyer);
            stock.CopyFrom(nextStock);
            return new InventoryOutcome(InventoryRefusal.None, price, added.Total);
        }

        /// <summary>The seller sells <paramref name="count"/> of an item to the vendor. Amount = total price received.</summary>
        public static InventoryOutcome TrySell(
            InventorySlots seller,
            int sellerMaxWeight,
            InventorySlots stock,
            VendorModel vendor,
            ItemModel item,
            int count,
            IItemLookup items)
        {
            if (count <= 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.BadCount);
            }

            if (!vendor.TryFind(item.Key, out VendorEntry entry) || entry.SellPrice <= 0)
            {
                return InventoryOutcome.Refused(InventoryRefusal.NotSold);
            }

            long priceLong = (long)entry.SellPrice * count;
            int price = priceLong > int.MaxValue ? int.MaxValue : (int)priceLong;
            InventorySlots nextSeller = seller.Clone();
            InventorySlots nextStock = stock.Clone();
            if (!InventoryRules.TryRemove(nextSeller, item.Key, count).Accepted)
            {
                return InventoryOutcome.Refused(InventoryRefusal.NotHeld);
            }

            if (vendor.PaymentItemKey != 0)
            {
                if (!items.TryGet(vendor.PaymentItemKey, out ItemModel? payment) || payment == null)
                {
                    return InventoryOutcome.Refused(InventoryRefusal.UnknownItem);
                }

                InventoryOutcome paid = InventoryRules.TryAdd(nextSeller, payment, price, sellerMaxWeight, items);
                if (!paid.Accepted)
                {
                    return paid;
                }
            }
            else
            {
                long currency = (long)nextSeller.Currency + price;
                nextSeller.Currency = currency > int.MaxValue ? int.MaxValue : (int)currency;
            }

            InventoryRules.TryAdd(nextStock, item, count, 0, items);
            seller.CopyFrom(nextSeller);
            stock.CopyFrom(nextStock);
            return new InventoryOutcome(InventoryRefusal.None, price, InventoryRules.CountOf(seller, item.Key));
        }
    }

    /// <summary>One weighted entry of a loot table.</summary>
    public readonly struct LootEntry
    {
        public LootEntry(int itemKey, int weight, int min, int max)
        {
            ItemKey = itemKey;
            Weight = weight < 0 ? 0 : weight;
            Min = min < 0 ? 0 : min;
            Max = max < min ? min : max;
        }

        public int ItemKey { get; }

        public int Weight { get; }

        public int Min { get; }

        public int Max { get; }
    }

    /// <summary>A loot table (LootTable baked to a model).</summary>
    public sealed class LootTableModel
    {
        public LootTableModel(int key, string name, int rolls, IReadOnlyList<LootEntry> entries)
        {
            Key = key;
            Name = name ?? string.Empty;
            Rolls = rolls < 1 ? 1 : rolls;
            Entries = entries ?? Array.Empty<LootEntry>();
        }

        public int Key { get; }

        public string Name { get; }

        public int Rolls { get; }

        public IReadOnlyList<LootEntry> Entries { get; }
    }

    /// <summary>Deterministic loot rolls (xorshift32 over an explicit seed; no shared random state).</summary>
    public static class LootRules
    {
        /// <summary>Rolls the table; equal seeds give equal stacks. Stacks of one item are merged, in first-roll order.</summary>
        public static List<ItemStack> Roll(LootTableModel table, uint seed)
        {
            var order = new List<int>();
            var counts = new Dictionary<int, int>();
            long totalWeight = 0;
            for (int i = 0; i < table.Entries.Count; i++)
            {
                totalWeight += table.Entries[i].Weight;
            }

            uint state = seed == 0U ? 0x9E3779B9U : seed;
            if (totalWeight > 0)
            {
                for (int roll = 0; roll < table.Rolls; roll++)
                {
                    state = Next(state);
                    long pick = (long)(state % (ulong)totalWeight);
                    LootEntry chosen = table.Entries[table.Entries.Count - 1];
                    for (int i = 0; i < table.Entries.Count; i++)
                    {
                        if (pick < table.Entries[i].Weight)
                        {
                            chosen = table.Entries[i];
                            break;
                        }

                        pick -= table.Entries[i].Weight;
                    }

                    state = Next(state);
                    int span = chosen.Max - chosen.Min + 1;
                    int amount = chosen.Min + (int)(state % (uint)span);
                    if (amount <= 0 || chosen.ItemKey == 0)
                    {
                        continue;
                    }

                    if (!counts.ContainsKey(chosen.ItemKey))
                    {
                        order.Add(chosen.ItemKey);
                        counts[chosen.ItemKey] = 0;
                    }

                    counts[chosen.ItemKey] += amount;
                }
            }

            var stacks = new List<ItemStack>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                stacks.Add(new ItemStack(order[i], counts[order[i]]));
            }

            return stacks;
        }

        private static uint Next(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }
    }
}
