// GameCore.Gameplay.Inventory - typed inventory commands and presentation over committed slots (P1.4).
//
//   InventoryCommands   grant/consume/drop/transfer/buy/sell/pickup as typed commands with fresh request ids
//   InventoryPresenter  pushes an InventoryViewModel of one inventory to an IInventoryView when its slots change
//   WorldItemBinder     which world items are still lying in the world (item.taken), told to an IWorldItemPresence
//                       (P1.3/P1.5 hide the pickup), and the pickup itself through the action ref inventory.pickup
//   VendorBinder        the committed stock of every vendor, and Buy/Sell helpers for the trade UI
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using Seams = GameCore.Gameplay.Contracts.Narrative;

namespace GameCore.Gameplay.Inventory
{
    /// <summary>Typed inventory commands addressed to the player's inventory (or a named one).</summary>
    public sealed class InventoryCommands
    {
        private readonly NarrativeRuntime runtime;
        private int serial;

        public InventoryCommands(NarrativeRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Submitted { get; private set; }

        public int Refused { get; private set; }

        /// <summary>A fresh request id (deterministic per world and call order; never 0).</summary>
        public int NextRequestId()
        {
            serial++;
            return NarrativeKeys.NameKey("gameplay.inventory-request." + runtime.Index.WorldId + "." + serial);
        }

        public CommandAdmissionReceipt Grant(string itemRef, int count, int requestId = 0, string inventoryRef = "") =>
            Submit(inventoryRef, InventoryIds.GrantRoute, InventoryIds.GrantCommand, NarrativeCommands.Grant(Key(itemRef), count, Fresh(requestId)));

        public CommandAdmissionReceipt Consume(string itemRef, int count, int requestId = 0, string inventoryRef = "") =>
            Submit(inventoryRef, InventoryIds.ConsumeRoute, InventoryIds.ConsumeCommand, NarrativeCommands.Consume(Key(itemRef), count, Fresh(requestId)));

        public CommandAdmissionReceipt Drop(string itemRef, int count, string inventoryRef = "") =>
            Submit(inventoryRef, InventoryIds.DropRoute, InventoryIds.DropCommand, NarrativeCommands.Drop(Key(itemRef), count));

        public CommandAdmissionReceipt Transfer(string destinationRef, string itemRef, int count, string inventoryRef = "") =>
            Submit(inventoryRef, InventoryIds.TransferRoute, InventoryIds.TransferCommand, NarrativeCommands.Transfer(Key(destinationRef), Key(itemRef), count));

        public CommandAdmissionReceipt Buy(string vendorRef, string itemRef, int count, int requestId = 0) =>
            Submit(string.Empty, InventoryIds.BuyRoute, InventoryIds.BuyCommand, NarrativeCommands.Buy(Key(vendorRef), Key(itemRef), count, Fresh(requestId)));

        public CommandAdmissionReceipt Sell(string vendorRef, string itemRef, int count, int requestId = 0) =>
            Submit(string.Empty, InventoryIds.SellRoute, InventoryIds.SellCommand, NarrativeCommands.Sell(Key(vendorRef), Key(itemRef), count, Fresh(requestId)));

        public CommandAdmissionReceipt Pickup(string worldItemRef, int requestId = 0) =>
            Submit(string.Empty, InventoryIds.PickupRoute, InventoryIds.PickupCommand, NarrativeCommands.Pickup(Key(worldItemRef), Fresh(requestId)));

        /// <summary>The key of a content reference (authoring id or name), or 0.</summary>
        public int Key(string reference) => runtime.Models.TryResolve(reference ?? string.Empty, out int key) ? key : 0;

        private int Fresh(int requestId) => requestId != 0 ? requestId : NextRequestId();

        private CommandAdmissionReceipt Submit(string inventoryRef, RouteId route, SchemaRef schema, FrozenPayload payload)
        {
            int inventory = string.IsNullOrEmpty(inventoryRef) ? 0 : Key(inventoryRef);
            runtime.Index.TryInventoryTarget(inventory, out TargetId target);
            CommandAdmissionReceipt receipt = runtime.Submitter.Submit(route, target, schema, payload);
            if (receipt.Admitted)
            {
                Submitted++;
            }
            else
            {
                Refused++;
            }

            return receipt;
        }
    }

    /// <summary>The committed inventory as a view model.</summary>
    public static class InventoryViews
    {
        public static InventoryViewModel Read(NarrativeRuntime runtime, ICommittedSlotReader slots, int inventoryKey, int revision)
        {
            InventoryModel? model = InventoryDeclarations.ModelOf(runtime.Models, inventoryKey);
            if (model == null || !runtime.Index.TryInventoryTarget(inventoryKey, out TargetId target))
            {
                return new InventoryViewModel(inventoryKey, string.Empty, Array.Empty<InventorySlotView>(), 0, 0, 0, 0, revision);
            }

            InventorySlots held = InventoryDeclarations.ReadSlots(slots, target, model.SlotCount);
            var views = new List<InventorySlotView>();
            for (int k = 0; k < held.SlotCount; k++)
            {
                if (held.Items[k] == 0 || held.Counts[k] <= 0)
                {
                    continue;
                }

                runtime.Models.TryGet(held.Items[k], out ItemModel? item);
                views.Add(new InventorySlotView(k, held.Items[k], item != null ? item.Name : runtime.Models.NameOf(held.Items[k]), held.Counts[k],
                    item != null ? item.Weight : 0, item != null ? item.Price : 0, null, item != null ? item.Name : string.Empty));
            }

            return new InventoryViewModel(model.Key, model.Name, views, model.SlotCount, held.Currency,
                InventoryRules.TotalWeight(held, runtime.Models), model.MaxWeight, revision);
        }

        public static int Fingerprint(ICommittedSlotReader slots, TargetId target, int slotCount)
        {
            unchecked
            {
                int hash = 17 + slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Currency, 0);
                for (int k = 0; k < slotCount; k++)
                {
                    hash = (hash * 31) + slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Item(k), 0);
                    hash = (hash * 31) + slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Count(k), 0);
                }

                return hash;
            }
        }
    }

    /// <summary>Pushes one inventory (default: the player's) to an IInventoryView when it changes.</summary>
    public sealed class InventoryPresenter : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private readonly int inventoryKey;
        private int fingerprint;
        private bool first = true;

        public InventoryPresenter(NarrativeRuntime runtime, IInventoryView view, int inventoryKey)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.inventoryKey = inventoryKey;
            View = view ?? new NullInventoryView();
        }

        public IInventoryView View { get; set; }

        public string BinderName => "inventory.presenter";

        public bool IsActive => true;

        public int Revision { get; private set; }

        public InventoryViewModel? Last { get; private set; }

        public int Present(ICommittedSlotReader slots)
        {
            InventoryModel? model = InventoryDeclarations.ModelOf(runtime.Models, inventoryKey);
            if (model == null || !runtime.Index.TryInventoryTarget(inventoryKey, out TargetId target))
            {
                return 0;
            }

            int next = InventoryViews.Fingerprint(slots, target, model.SlotCount);
            if (!first && next == fingerprint)
            {
                return 0;
            }

            first = false;
            fingerprint = next;
            Revision++;
            Last = InventoryViews.Read(runtime, slots, inventoryKey, Revision);
            View.Show(Last);
            return 1;
        }
    }

    /// <summary>Told when a world item appears or disappears (P1.3/P1.5 show or hide its pickup).</summary>
    public interface IWorldItemPresence
    {
        void SetPresent(string worldItemAuthoringId, string entityAuthoringId, bool present);
    }

    /// <summary>The default presence sink: records the last state of every world item.</summary>
    public sealed class NullWorldItemPresence : IWorldItemPresence
    {
        private readonly Dictionary<string, bool> present = new Dictionary<string, bool>(StringComparer.Ordinal);

        public int Calls { get; private set; }

        public bool IsPresent(string worldItemAuthoringId) => present.TryGetValue(worldItemAuthoringId, out bool value) && value;

        public void SetPresent(string worldItemAuthoringId, string entityAuthoringId, bool present)
        {
            this.present[worldItemAuthoringId] = present;
            Calls++;
        }
    }

    /// <summary>World item presence over item.taken, and the pickup through inventory.pickup.</summary>
    public sealed class WorldItemBinder : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private readonly Seams.IActionRunner actions;
        private readonly Dictionary<int, int> lastTaken = new Dictionary<int, int>();

        public WorldItemBinder(NarrativeRuntime runtime, Seams.IActionRunner actions)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.actions = actions ?? throw new ArgumentNullException(nameof(actions));
        }

        public IWorldItemPresence Presence { get; set; } = new NullWorldItemPresence();

        public string BinderName => "inventory.world-items";

        public bool IsActive => true;

        /// <summary>Picks up a world item (authoring id or name, or the placed entity's authoring id) into the player's inventory.</summary>
        public bool TryPickup(string worldItemRef)
        {
            int key = runtime.Models.TryResolve(worldItemRef ?? string.Empty, out int resolved) ? resolved : 0;
            var context = new EvaluationContext(0, key, worldItemRef ?? string.Empty, default(TargetId), default(TargetId));
            return actions.TryRun(NarrativeActionRunner.Pickup, context);
        }

        public bool IsTaken(string worldItemRef, ICommittedSlotReader slots)
        {
            if (!runtime.Models.TryResolve(worldItemRef ?? string.Empty, out int key))
            {
                return false;
            }

            TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.WorldItem, key);
            return !target.IsDefault && slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.ItemTaken, 0) != 0;
        }

        public int Present(ICommittedSlotReader slots)
        {
            int touched = 0;
            foreach (WorldItemModel item in runtime.Models.WorldItems)
            {
                TargetId target = runtime.Index.TargetOf(NarrativeTargetKind.WorldItem, item.Key);
                if (target.IsDefault)
                {
                    continue;
                }

                int taken = slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.ItemTaken, 0);
                if (lastTaken.TryGetValue(item.Key, out int earlier) && earlier == taken)
                {
                    continue;
                }

                lastTaken[item.Key] = taken;
                Presence.SetPresent(item.AuthoringId, item.EntityId, taken == 0);
                touched++;
            }

            return touched;
        }
    }

    /// <summary>One vendor's committed stock line.</summary>
    public sealed class VendorStockView
    {
        public VendorStockView(int itemKey, string itemName, int stock, int buyPrice, int sellPrice, bool unlimited)
        {
            ItemKey = itemKey;
            ItemName = itemName;
            Stock = stock;
            BuyPrice = buyPrice;
            SellPrice = sellPrice;
            Unlimited = unlimited;
        }

        public int ItemKey { get; }

        public string ItemName { get; }

        public int Stock { get; }

        public int BuyPrice { get; }

        public int SellPrice { get; }

        public bool Unlimited { get; }
    }

    /// <summary>The committed stock of every vendor, and the trade helpers.</summary>
    public sealed class VendorBinder : IPresentationBinder
    {
        private readonly NarrativeRuntime runtime;
        private readonly InventoryCommands commands;
        private readonly Dictionary<int, IReadOnlyList<VendorStockView>> stock = new Dictionary<int, IReadOnlyList<VendorStockView>>();

        public VendorBinder(NarrativeRuntime runtime, InventoryCommands commands)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public string BinderName => "inventory.vendors";

        public bool IsActive => true;

        public int Revision { get; private set; }

        public IReadOnlyList<VendorStockView> StockOf(string vendorRef) =>
            stock.TryGetValue(commands.Key(vendorRef), out IReadOnlyList<VendorStockView>? lines) && lines != null ? lines : Array.Empty<VendorStockView>();

        public CommandAdmissionReceipt Buy(string vendorRef, string itemRef, int count) => commands.Buy(vendorRef, itemRef, count);

        public CommandAdmissionReceipt Sell(string vendorRef, string itemRef, int count) => commands.Sell(vendorRef, itemRef, count);

        public int Present(ICommittedSlotReader slots)
        {
            int touched = 0;
            foreach (VendorModel vendor in runtime.Models.Vendors)
            {
                if (!runtime.Index.TryInventoryTarget(vendor.Key, out TargetId target))
                {
                    continue;
                }

                InventorySlots held = InventoryDeclarations.ReadSlots(slots, target, vendor.StockSlots);
                var lines = new List<VendorStockView>();
                bool changed = !stock.TryGetValue(vendor.Key, out IReadOnlyList<VendorStockView>? before) || before == null || before.Count != vendor.Entries.Count;
                for (int i = 0; i < vendor.Entries.Count; i++)
                {
                    VendorEntry entry = vendor.Entries[i];
                    int count = InventoryRules.CountOf(held, entry.ItemKey);
                    runtime.Models.TryGet(entry.ItemKey, out ItemModel? item);
                    lines.Add(new VendorStockView(entry.ItemKey, item != null ? item.Name : runtime.Models.NameOf(entry.ItemKey), count, entry.BuyPrice, entry.SellPrice, entry.Unlimited));
                    if (!changed && before![i].Stock != count)
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    stock[vendor.Key] = lines;
                    touched++;
                }
            }

            if (touched > 0)
            {
                Revision++;
            }

            return touched;
        }
    }
}
