// P1.4 dotnet tests: the pure inventory, trade and loot rules (all-or-nothing operations, vendors priced in an item,
// deterministic loot).
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Gameplay.Inventory;
using NUnit.Framework;

namespace GameCore.Rules.Gameplay.Tests.Narrative
{
    [TestFixture]
    public sealed class InventoryRulesTests
    {
        private static readonly ItemModel Coin = new ItemModel(1, "Old Coin", 10, 5, 1, new[] { "currency" }, 0);
        private static readonly ItemModel Lantern = new ItemModel(2, "Lantern", 1, 800, 12, new[] { "tool" }, 0);
        private static readonly ItemModel Key = new ItemModel(3, "Gate Key", 1, 50, 3, null, 0);
        private static readonly ItemModel Clapper = new ItemModel(4, "Bell Clapper", 1, 2000, 0, null, 0);

        private static ItemTable Items() => new ItemTable().Add(Coin).Add(Lantern).Add(Key).Add(Clapper);

        private static VendorModel Odd(bool unlimited = false) =>
            new VendorModel(90, "Odd", new[] { new VendorEntry(Key.Key, 1, 3, 1, unlimited), new VendorEntry(Lantern.Key, 1, 20, 0, false) }, Coin.Key, 4);

        [Test]
        public void TryAdd_TopsUpStacksThenFillsEmptySlots()
        {
            var slots = new InventorySlots(3);
            Assert.That(InventoryRules.TryAdd(slots, Coin, 7, 0, Items()).Accepted, Is.True);
            InventoryOutcome more = InventoryRules.TryAdd(slots, Coin, 8, 0, Items());
            Assert.That(more.Accepted, Is.True);
            Assert.That(more.Total, Is.EqualTo(15));
            Assert.That(slots.Items, Is.EqualTo(new[] { Coin.Key, Coin.Key, 0 }));
            Assert.That(slots.Counts, Is.EqualTo(new[] { 10, 5, 0 }));
        }

        [Test]
        public void TryAdd_Full_IsAllOrNothing()
        {
            var slots = new InventorySlots(2);
            InventoryRules.TryAdd(slots, Lantern, 1, 0, Items());
            InventoryOutcome full = InventoryRules.TryAdd(slots, Coin, 11, 0, Items());
            Assert.That(full.Refusal, Is.EqualTo(InventoryRefusal.Full));
            Assert.That(InventoryRules.CountOf(slots, Coin.Key), Is.EqualTo(0), "nothing was added");
            Assert.That(slots.Items, Is.EqualTo(new[] { Lantern.Key, 0 }));
        }

        [Test]
        public void TryAdd_Overweight_IsRefused()
        {
            var slots = new InventorySlots(4);
            InventoryRules.TryAdd(slots, Lantern, 1, 0, Items());
            Assert.That(InventoryRules.TryAdd(slots, Clapper, 1, 2500, Items()).Refusal, Is.EqualTo(InventoryRefusal.Overweight));
            Assert.That(InventoryRules.TotalWeight(slots, Items()), Is.EqualTo(800));
            Assert.That(InventoryRules.TryAdd(slots, Clapper, 1, 2800, Items()).Accepted, Is.True);
        }

        [Test]
        public void TryRemove_EmptiesStacksFromTheBack()
        {
            var slots = new InventorySlots(3);
            InventoryRules.TryAdd(slots, Coin, 15, 0, Items());
            InventoryOutcome removed = InventoryRules.TryRemove(slots, Coin.Key, 6);
            Assert.That(removed.Accepted, Is.True);
            Assert.That(removed.Total, Is.EqualTo(9));
            Assert.That(slots.Items, Is.EqualTo(new[] { Coin.Key, 0, 0 }), "the second stack emptied first");
            Assert.That(slots.Counts[0], Is.EqualTo(9));
        }

        [Test]
        public void TryRemove_NotHeldAndBadCount_ChangeNothing()
        {
            var slots = new InventorySlots(2);
            InventoryRules.TryAdd(slots, Coin, 2, 0, Items());
            Assert.That(InventoryRules.TryRemove(slots, Coin.Key, 3).Refusal, Is.EqualTo(InventoryRefusal.NotHeld));
            Assert.That(InventoryRules.TryRemove(slots, Coin.Key, 0).Refusal, Is.EqualTo(InventoryRefusal.BadCount));
            Assert.That(InventoryRules.TryAdd(slots, Coin, -1, 0, Items()).Refusal, Is.EqualTo(InventoryRefusal.BadCount));
            Assert.That(InventoryRules.CountOf(slots, Coin.Key), Is.EqualTo(2));
        }

        [Test]
        public void TryTransfer_IsAllOrNothing()
        {
            var from = new InventorySlots(2);
            var to = new InventorySlots(1);
            InventoryRules.TryAdd(from, Coin, 4, 0, Items());
            InventoryRules.TryAdd(to, Lantern, 1, 0, Items());
            Assert.That(InventoryRules.TryTransfer(from, to, Coin, 2, 0, Items()).Refusal, Is.EqualTo(InventoryRefusal.Full));
            Assert.That(InventoryRules.CountOf(from, Coin.Key), Is.EqualTo(4), "the source kept its coins");

            var chest = new InventorySlots(2);
            InventoryOutcome moved = InventoryRules.TryTransfer(from, chest, Coin, 3, 0, Items());
            Assert.That(moved.Accepted, Is.True);
            Assert.That(InventoryRules.CountOf(from, Coin.Key), Is.EqualTo(1));
            Assert.That(InventoryRules.CountOf(chest, Coin.Key), Is.EqualTo(3));
        }

        [Test]
        public void Starting_SeedsTheDefinitionStacks()
        {
            var model = new InventoryModel(7, "Pack", 4, 0, 25, new[] { new ItemStack(Lantern.Key, 1), new ItemStack(Coin.Key, 12) }, 0, true);
            InventorySlots slots = InventoryRules.Starting(model, Items());
            Assert.That(slots.Currency, Is.EqualTo(25));
            Assert.That(InventoryRules.CountOf(slots, Coin.Key), Is.EqualTo(12));
            Assert.That(InventoryRules.CountOf(slots, Lantern.Key), Is.EqualTo(1));
            Assert.That(slots.SlotCount, Is.EqualTo(4));
        }

        [Test]
        public void TryBuy_PaysInTheVendorsItem_AndMovesStock()
        {
            var buyer = new InventorySlots(4);
            InventoryRules.TryAdd(buyer, Coin, 3, 0, Items());
            VendorModel odd = Odd();
            InventorySlots stock = InventoryRules.Starting(odd.StockInventory(), Items());
            InventoryOutcome bought = TradeRules.TryBuy(buyer, 0, stock, odd, Key, 1, Items());
            Assert.That(bought.Accepted, Is.True);
            Assert.That(bought.Amount, Is.EqualTo(3), "the price");
            Assert.That(InventoryRules.CountOf(buyer, Coin.Key), Is.EqualTo(0));
            Assert.That(InventoryRules.CountOf(buyer, Key.Key), Is.EqualTo(1));
            Assert.That(InventoryRules.CountOf(stock, Coin.Key), Is.EqualTo(3), "Odd holds the coins");
            Assert.That(InventoryRules.CountOf(stock, Key.Key), Is.EqualTo(0));
        }

        [Test]
        public void TryBuy_CurrencyShort_ChangesNothing()
        {
            var buyer = new InventorySlots(4);
            InventoryRules.TryAdd(buyer, Coin, 2, 0, Items());
            VendorModel odd = Odd();
            InventorySlots stock = InventoryRules.Starting(odd.StockInventory(), Items());
            Assert.That(TradeRules.TryBuy(buyer, 0, stock, odd, Key, 1, Items()).Refusal, Is.EqualTo(InventoryRefusal.CurrencyShort));
            Assert.That(InventoryRules.CountOf(buyer, Coin.Key), Is.EqualTo(2));
            Assert.That(InventoryRules.CountOf(stock, Key.Key), Is.EqualTo(1));
        }

        [Test]
        public void TryBuy_WithCurrency_AndStockLimits()
        {
            var vendor = new VendorModel(91, "Trader", new[] { new VendorEntry(Lantern.Key, 1, 10, 4, false), new VendorEntry(Coin.Key, 0, 1, 0, true) }, 0, 4);
            var buyer = new InventorySlots(4) { Currency = 30 };
            InventorySlots stock = InventoryRules.Starting(vendor.StockInventory(), Items());
            Assert.That(TradeRules.TryBuy(buyer, 0, stock, vendor, Lantern, 1, Items()).Accepted, Is.True);
            Assert.That(buyer.Currency, Is.EqualTo(20));
            Assert.That(stock.Currency, Is.EqualTo(10));
            Assert.That(TradeRules.TryBuy(buyer, 0, stock, vendor, Lantern, 1, Items()).Refusal, Is.EqualTo(InventoryRefusal.OutOfStock));
            Assert.That(TradeRules.TryBuy(buyer, 0, stock, vendor, Coin, 5, Items()).Accepted, Is.True, "unlimited stock never depletes");
            Assert.That(TradeRules.TryBuy(buyer, 0, stock, vendor, Clapper, 1, Items()).Refusal, Is.EqualTo(InventoryRefusal.NotSold));
        }

        [Test]
        public void TrySell_PaysTheSeller_OrRefusesWhatTheVendorDoesNotBuy()
        {
            VendorModel odd = Odd();
            var seller = new InventorySlots(4);
            InventoryRules.TryAdd(seller, Key, 1, 0, Items());
            InventoryRules.TryAdd(seller, Lantern, 1, 0, Items());
            InventorySlots stock = new InventorySlots(4);
            Assert.That(TradeRules.TrySell(seller, 0, stock, odd, Lantern, 1, Items()).Refusal, Is.EqualTo(InventoryRefusal.NotSold), "Odd pays nothing for lanterns");
            InventoryOutcome sold = TradeRules.TrySell(seller, 0, stock, odd, Key, 1, Items());
            Assert.That(sold.Accepted, Is.True);
            Assert.That(InventoryRules.CountOf(seller, Coin.Key), Is.EqualTo(1));
            Assert.That(InventoryRules.CountOf(stock, Key.Key), Is.EqualTo(1));
            Assert.That(TradeRules.TrySell(seller, 0, stock, odd, Key, 1, Items()).Refusal, Is.EqualTo(InventoryRefusal.NotHeld));
        }

        [Test]
        public void Loot_IsDeterministicPerSeed_AndMergesStacks()
        {
            var table = new LootTableModel(5, "marsh", 6, new[] { new LootEntry(Coin.Key, 3, 1, 2), new LootEntry(Clapper.Key, 1, 1, 1), new LootEntry(Lantern.Key, 0, 1, 1) });
            List<ItemStack> a = LootRules.Roll(table, 1234U);
            List<ItemStack> b = LootRules.Roll(table, 1234U);
            Assert.That(a.ConvertAll(s => s.ItemKey * 1000 + s.Count), Is.EqualTo(b.ConvertAll(s => s.ItemKey * 1000 + s.Count)));
            var keys = new HashSet<int>();
            int total = 0;
            foreach (ItemStack stack in a)
            {
                Assert.That(keys.Add(stack.ItemKey), Is.True, "one stack per item");
                Assert.That(stack.ItemKey, Is.Not.EqualTo(Lantern.Key), "weight 0 never drops");
                total += stack.Count;
            }

            Assert.That(total, Is.InRange(6, 12));
            Assert.That(LootRules.Roll(new LootTableModel(6, "empty", 3, new LootEntry[0]), 1U), Is.Empty);
        }

        [Test]
        public void Vendor_StockInventory_SeedsItsEntries()
        {
            InventoryModel stock = Odd().StockInventory();
            Assert.That(stock.Key, Is.EqualTo(90));
            Assert.That(stock.SlotCount, Is.EqualTo(4));
            Assert.That(stock.Starting, Has.Count.EqualTo(2));
            Assert.That(Odd().TryFind(Key.Key, out VendorEntry entry) && entry.BuyPrice == 3, Is.True);
            Assert.That(Odd().TryFind(Clapper.Key, out VendorEntry _), Is.False);
            Assert.That(Coin.HasTag("currency") && !Coin.HasTag("tool"), Is.True);
        }

        [Test]
        public void ChangedSince_ListsTheTouchedSlots()
        {
            var slots = new InventorySlots(3);
            InventoryRules.TryAdd(slots, Coin, 3, 0, Items());
            InventorySlots before = slots.Clone();
            InventoryRules.TryAdd(slots, Lantern, 1, 0, Items());
            Assert.That(slots.ChangedSince(before), Is.EqualTo(new[] { 1 }));
        }
    }
}
