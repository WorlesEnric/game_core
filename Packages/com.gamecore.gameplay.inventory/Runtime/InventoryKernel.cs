// GameCore.Gameplay.Inventory - the inventory plugin's kernel half (P1.4, catalog row 8).
//
// Targets: one per inventory and per vendor (inv.item.<k>/inv.count.<k>, inv.currency, a request ring) and one per
// world item (item.taken, item.key, item.count). Routes, all addressed to an inventory (the actor's, or the vendor's for
// transfers into it):
//
//   inventory.grant     item, count, request id            -> ItemGranted | InventoryFull
//   inventory.consume   item, count, request id            -> [ActionDue... of the item's use actions], ItemConsumed
//   inventory.drop      item, count                        -> ItemDropped
//   inventory.transfer  destination inventory, item, count -> ItemGranted (destination), ItemTransferred
//   inventory.buy       vendor, item, count, request id    -> ItemGranted, TradeDone
//   inventory.sell      vendor, item, count, request id    -> ItemConsumed, TradeDone
//   inventory.pickup    world item, request id             -> ItemGranted, ItemPickedUp | InventoryFull
//
// Grants are idempotent by request id: an id already in the target's ring is rejected as IdempotencyConflict, which
// the narrative outbox reads as "already applied". A grant that does not fit commits InventoryFull without recording
// its id, so the outbox retries it later. The inventory stage runs after the world stage.
//
// P1.7a (A1): an obligation request id (negative, from the narrative outbox) is also claimed through the world's step
// tap - an obligation that is no longer open answers IdempotencyConflict even when the ring has forgotten it - and is
// settled in the step that applies it. Committed events go through the tap, so the ActionDue events of a consumed item's
// use actions become obligations in the same step.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Inventory
{
    /// <summary>Declarations of the inventory plugin.</summary>
    public static class InventoryDeclarations
    {
        public const string Stem = "inventory";

        public static readonly StageId Stage = NarrativePluginSpec.StageOf(Stem);

        public const int InventoryDomain = 0;

        public const int WorldItemDomain = 1;

        public const int ReasonNoSlot = 1;

        public const int ReasonWeight = 2;

        public static NarrativePluginSpec Spec(NarrativeModelSet models)
        {
            var spec = new NarrativePluginSpec(Stem, NarrativeCatalogNames.Inventory, InventoryIds.Owner)
                .Route(InventoryIds.GrantRoute, InventoryIds.GrantCommand, "grant", 3)
                .Route(InventoryIds.ConsumeRoute, InventoryIds.ConsumeCommand, "consume", 3)
                .Route(InventoryIds.DropRoute, InventoryIds.DropCommand, "drop", 2)
                .Route(InventoryIds.TransferRoute, InventoryIds.TransferCommand, "transfer", 3)
                .Route(InventoryIds.BuyRoute, InventoryIds.BuyCommand, "buy", 4)
                .Route(InventoryIds.SellRoute, InventoryIds.SellCommand, "sell", 4)
                .Route(InventoryIds.PickupRoute, InventoryIds.PickupCommand, "pickup", 2)
                .Slot(InventoryIds.Currency, InventoryDomain, "currency")
                .Slot(InventoryIds.ReqHead, InventoryDomain, "req-head");
            for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
            {
                spec.Slot(InventoryIds.Req(i), InventoryDomain, "req-" + i);
            }

            int slots = Math.Max(1, models.MaxInventorySlots);
            for (int k = 0; k < slots; k++)
            {
                spec.Slot(InventoryIds.Item(k), InventoryDomain, "item-" + k);
                spec.Slot(InventoryIds.Count(k), InventoryDomain, "count-" + k);
            }

            spec.Slot(InventoryIds.ItemTaken, WorldItemDomain, "taken")
                .Slot(InventoryIds.ItemKey, WorldItemDomain, "key")
                .Slot(InventoryIds.ItemCount, WorldItemDomain, "count");
            return spec.After(WorldDeclarations.Stage);
        }

        /// <summary>The inventory model of an inventory or vendor key (vendors as their stock inventory).</summary>
        public static InventoryModel? ModelOf(NarrativeModelSet models, int key)
        {
            int resolved = key == 0 && models.PlayerInventory != null ? models.PlayerInventory.Key : key;
            if (models.TryGetInventory(resolved, out InventoryModel? inventory) && inventory != null)
            {
                return inventory;
            }

            return models.TryGetVendor(resolved, out VendorModel? vendor) && vendor != null ? vendor.StockInventory() : null;
        }

        /// <summary>The committed slots of an inventory target.</summary>
        public static InventorySlots ReadSlots(ICommittedSlotReader slots, TargetId target, int slotCount)
        {
            var result = new InventorySlots(slotCount);
            for (int k = 0; k < slotCount; k++)
            {
                result.Items[k] = slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Item(k), 0);
                result.Counts[k] = slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Count(k), 0);
            }

            result.Currency = slots.ReadOrDefault(target, InventoryIds.Owner, InventoryIds.Currency, 0);
            return result;
        }
    }

    /// <summary>The inventory module of one world.</summary>
    public sealed class InventoryModule : INarrativeModule, INarrativeWorldAware
    {
        private readonly Dictionary<TargetId, int> inventoryKeys = new Dictionary<TargetId, int>();
        private NarrativeRuntime? runtime;
        private int slotCount = 1;

        public string Name => "inventory";

        public INarrativeContentConverter? Converter => new InventoryContentConverter();

        public NarrativeRuntime? Runtime => runtime;

        /// <summary>Typed inventory commands (tests, Studio, P1.5's UI).</summary>
        public InventoryCommands? Commands { get; private set; }

        public InventoryPresenter? Presenter { get; private set; }

        public WorldItemBinder? WorldItems { get; private set; }

        public VendorBinder? Vendors { get; private set; }

        /// <summary>The view the inventory presenter pushes to (P1.5 replaces it).</summary>
        public IInventoryView View { get; set; } = new NullInventoryView();

        public int Granted { get; private set; }

        public int Full { get; private set; }

        public int Trades { get; private set; }

        public int Pickups { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        public void Declare(NarrativeComposition composition)
        {
            NarrativePluginSpec spec = InventoryDeclarations.Spec(composition.Models);
            slotCount = Math.Max(1, composition.Models.MaxInventorySlots);
            composition.AddPlugin(spec, new ManagedSystemRegistration<InventoryCommandSystem>(spec.CommandSystem, spec.Stage, "GameplayInventoryCommandSystem"));
            composition.AddRecipe(spec.CreateRecipe("inventory"));
            composition.AddRecipe(spec.CreateRecipe("world-item"));
            foreach (InventoryModel inventory in composition.Models.Inventories)
            {
                composition.AddSeed(composition.Index.TargetOf(NarrativeTargetKind.Inventory, inventory.Key), spec.Recipe("inventory"), "inventory:" + inventory.Name);
            }

            foreach (VendorModel vendor in composition.Models.Vendors)
            {
                composition.AddSeed(composition.Index.TargetOf(NarrativeTargetKind.Vendor, vendor.Key), spec.Recipe("inventory"), "vendor:" + vendor.Name);
            }

            foreach (WorldItemModel item in composition.Models.WorldItems)
            {
                composition.AddSeed(composition.Index.TargetOf(NarrativeTargetKind.WorldItem, item.Key), spec.Recipe("world-item"), "world-item:" + item.Name);
            }
        }

        public void Attach(NarrativeRuntime attached, bool seedSlots)
        {
            runtime = attached ?? throw new ArgumentNullException(nameof(attached));
            foreach (InventoryModel inventory in attached.Models.Inventories)
            {
                SeedInventory(attached, attached.Index.TargetOf(NarrativeTargetKind.Inventory, inventory.Key), inventory, seedSlots);
            }

            foreach (VendorModel vendor in attached.Models.Vendors)
            {
                SeedInventory(attached, attached.Index.TargetOf(NarrativeTargetKind.Vendor, vendor.Key), vendor.StockInventory(), seedSlots);
            }

            if (seedSlots)
            {
                foreach (WorldItemModel item in attached.Models.WorldItems)
                {
                    TargetId target = attached.Index.TargetOf(NarrativeTargetKind.WorldItem, item.Key);
                    if (target.IsDefault)
                    {
                        continue;
                    }

                    attached.Seed(target, InventoryIds.Owner, InventoryIds.ItemTaken, 0);
                    attached.Seed(target, InventoryIds.Owner, InventoryIds.ItemKey, item.ItemKey);
                    attached.Seed(target, InventoryIds.Owner, InventoryIds.ItemCount, item.Count);
                }
            }

            attached.System<InventoryCommandSystem>().Module = this;
        }

        public void OnWorld(NarrativeWorld world)
        {
            NarrativeRuntime rt = world.Runtime;
            Commands = new InventoryCommands(rt);
            Presenter = new InventoryPresenter(rt, View, 0);
            rt.AddPresenter(Presenter);
            WorldItems = new WorldItemBinder(rt, world.Actions);
            rt.AddPresenter(WorldItems);
            Vendors = new VendorBinder(rt, Commands);
            rt.AddPresenter(Vendors);
        }

        private void SeedInventory(NarrativeRuntime rt, TargetId target, InventoryModel model, bool seed)
        {
            if (target.IsDefault)
            {
                return;
            }

            inventoryKeys[target] = model.Key;
            if (!seed)
            {
                return;
            }

            InventorySlots start = InventoryRules.Starting(model, rt.Models);
            for (int k = 0; k < slotCount; k++)
            {
                rt.Seed(target, InventoryIds.Owner, InventoryIds.Item(k), k < start.SlotCount ? start.Items[k] : 0);
                rt.Seed(target, InventoryIds.Owner, InventoryIds.Count(k), k < start.SlotCount ? start.Counts[k] : 0);
            }

            rt.Seed(target, InventoryIds.Owner, InventoryIds.Currency, model.StartingCurrency);
            rt.Seed(target, InventoryIds.Owner, InventoryIds.ReqHead, 0);
            for (int i = 0; i < NarrativeKeys.RequestRingSize; i++)
            {
                rt.Seed(target, InventoryIds.Owner, InventoryIds.Req(i), 0);
            }
        }

        // ---- kernel half --------------------------------------------------------------------------------------

        internal void Run(EntityManager entityManager)
        {
            NarrativeRuntime? rt = runtime;
            WorldMessagePlane? plane = rt != null ? rt.Host.Messages : null;
            if (rt == null || plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(InventoryIds.Owner);
            for (int i = 0; i < batch.Count; i++)
            {
                Execute(rt, plane, entityManager, batch[i]);
            }

            plane.ReleaseConsumed(InventoryIds.Owner);
        }

        private void Execute(NarrativeRuntime rt, WorldMessagePlane plane, EntityManager em, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<NarrativeCommand>(message.PayloadSchema, payload, out NarrativeCommand command, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                Malformed++;
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            TargetId target = message.Target;
            if (!inventoryKeys.TryGetValue(target, out int inventoryKey) || !NarrativeSlots.TryEntity(rt.Registry, em, target, out Entity _))
            {
                Refuse(plane, message, DiagnosticCode.StaleHandle);
                return;
            }

            InventoryModel? model = InventoryDeclarations.ModelOf(rt.Models, inventoryKey);
            if (model == null)
            {
                Refuse(plane, message, DiagnosticCode.MissingDependency);
                return;
            }

            var step = new InventoryStep(rt, em, target, model);
            RouteId route = message.Route;
            int requestIndex = route.Equals(InventoryIds.BuyRoute) || route.Equals(InventoryIds.SellRoute) ? 3
                : route.Equals(InventoryIds.PickupRoute) ? 1
                : route.Equals(InventoryIds.GrantRoute) || route.Equals(InventoryIds.ConsumeRoute) ? 2 : -1;
            int requestId = requestIndex >= 0 ? command[requestIndex] : 0;
            int[]? ring = RequestRing.IsTracked(requestId) ? NarrativeSlots.ReadRing(rt.Registry, em, target, InventoryIds.Owner, InventoryIds.Req) : null;
            if (NarrativeObligations.Admit(rt.Tap, ring, requestId) != DiagnosticCode.None)
            {
                Refuse(plane, message, DiagnosticCode.IdempotencyConflict);
                return;
            }

            DiagnosticCode refusal;
            if (route.Equals(InventoryIds.GrantRoute))
            {
                refusal = step.Grant(command[0], command[1], requestId, InventoryIds.SourceGrant);
            }
            else if (route.Equals(InventoryIds.ConsumeRoute))
            {
                refusal = step.Consume(command[0], command[1], requestId);
            }
            else if (route.Equals(InventoryIds.DropRoute))
            {
                refusal = step.Drop(command[0], command[1]);
            }
            else if (route.Equals(InventoryIds.TransferRoute))
            {
                refusal = step.Transfer(command[0], command[1], command[2]);
            }
            else if (route.Equals(InventoryIds.BuyRoute))
            {
                refusal = step.Trade(command[0], command[1], command[2], requestId, true);
            }
            else if (route.Equals(InventoryIds.SellRoute))
            {
                refusal = step.Trade(command[0], command[1], command[2], requestId, false);
            }
            else if (route.Equals(InventoryIds.PickupRoute))
            {
                refusal = step.Pickup(command[0], requestId);
            }
            else
            {
                refusal = DiagnosticCode.Ineligible;
            }

            if (refusal != DiagnosticCode.None)
            {
                Refuse(plane, message, refusal);
                return;
            }

            if (!step.Events.CommitAll(plane, message, rt.Tap))
            {
                Refuse(plane, message, DiagnosticCode.BudgetExceeded);
                return;
            }

            step.Apply();
            if (step.Applied)
            {
                NarrativeSlots.PushRing(rt.Registry, em, target, InventoryIds.Owner, InventoryIds.ReqHead, InventoryIds.Req, requestId);
                NarrativeObligations.Settle(rt.Tap, requestId);
            }

            Granted += step.GrantCount;
            Full += step.FullCount;
            Trades += step.TradeCount;
            Pickups += step.PickupCount;
        }

        private void Refuse(WorldMessagePlane plane, StepMessage message, DiagnosticCode code)
        {
            Refused++;
            plane.Reject(message, code, plane.ExecutingStep);
        }
    }

    /// <summary>One inventory command in the step; slot writes are applied after the events committed.</summary>
    internal sealed class InventoryStep
    {
        private readonly NarrativeRuntime rt;
        private readonly EntityManager em;
        private readonly TargetId target;
        private readonly InventoryModel model;
        private readonly InventorySlots before;
        private readonly InventorySlots slots;
        private readonly List<KeyValuePair<TargetId, InventoryPending>> others = new List<KeyValuePair<TargetId, InventoryPending>>();
        private readonly List<KeyValuePair<TargetId, int>> taken = new List<KeyValuePair<TargetId, int>>();

        public InventoryStep(NarrativeRuntime runtime, EntityManager entityManager, TargetId inventory, InventoryModel inventoryModel)
        {
            rt = runtime;
            em = entityManager;
            target = inventory;
            model = inventoryModel;
            before = ReadLive(inventory, inventoryModel.SlotCount);
            slots = before.Clone();
        }

        public StepEventBatch Events { get; } = new StepEventBatch();

        /// <summary>False when the command committed without applying (InventoryFull): its request id is not recorded.</summary>
        public bool Applied { get; private set; } = true;

        public int GrantCount { get; private set; }

        public int FullCount { get; private set; }

        public int TradeCount { get; private set; }

        public int PickupCount { get; private set; }

        public DiagnosticCode Grant(int itemKey, int count, int requestId, int source)
        {
            if (!rt.Models.TryGet(itemKey, out ItemModel? item) || item == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            InventoryOutcome outcome = InventoryRules.TryAdd(slots, item, count, model.MaxWeight, rt.Models);
            if (outcome.Refusal == InventoryRefusal.BadCount)
            {
                return DiagnosticCode.Ineligible;
            }

            if (!outcome.Accepted)
            {
                NotFit(itemKey, count, outcome.Refusal);
                return DiagnosticCode.None;
            }

            Events.Add(InventoryIds.ItemGrantedEvent, target, itemKey, outcome.Amount, outcome.Total, requestId, source, 0);
            GrantCount++;
            return DiagnosticCode.None;
        }

        public DiagnosticCode Consume(int itemKey, int count, int requestId)
        {
            if (!rt.Models.TryGet(itemKey, out ItemModel? item) || item == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            InventoryOutcome outcome = InventoryRules.TryRemove(slots, itemKey, count);
            if (!outcome.Accepted)
            {
                return DiagnosticCode.Ineligible;
            }

            if (item.UseEffectKey != 0 && rt.Models.TryGet(item.UseEffectKey, out ActionSetModel? use) && use != null)
            {
                NarrativeActions.AddDue(Events, rt.Index.HubTarget, use, rt.ActorKey, itemKey, LogicIds.SourceItemUse, false);
            }

            Events.Add(InventoryIds.ItemConsumedEvent, target, itemKey, outcome.Amount, outcome.Total, requestId, 0, 0);
            return DiagnosticCode.None;
        }

        public DiagnosticCode Drop(int itemKey, int count)
        {
            InventoryOutcome outcome = InventoryRules.TryRemove(slots, itemKey, count);
            if (!outcome.Accepted)
            {
                return DiagnosticCode.Ineligible;
            }

            Events.Add(InventoryIds.ItemDroppedEvent, target, itemKey, outcome.Amount, outcome.Total, 0, 0, 0);
            return DiagnosticCode.None;
        }

        public DiagnosticCode Transfer(int destinationKey, int itemKey, int count)
        {
            InventoryModel? destinationModel = InventoryDeclarations.ModelOf(rt.Models, destinationKey);
            if (destinationModel == null || !rt.Index.TryInventoryTarget(destinationKey, out TargetId destination) || destination.Equals(target)
                || !rt.Models.TryGet(itemKey, out ItemModel? item) || item == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            InventorySlots to = ReadLive(destination, destinationModel.SlotCount);
            InventorySlots toBefore = to.Clone();
            InventoryOutcome outcome = InventoryRules.TryTransfer(slots, to, item, count, destinationModel.MaxWeight, rt.Models);
            if (!outcome.Accepted)
            {
                return DiagnosticCode.Ineligible;
            }

            others.Add(new KeyValuePair<TargetId, InventoryPending>(destination, new InventoryPending(toBefore, to)));
            Events.Add(InventoryIds.ItemGrantedEvent, destination, itemKey, outcome.Amount, InventoryRules.CountOf(to, itemKey), 0, InventoryIds.SourceTransfer, 0);
            Events.Add(InventoryIds.ItemTransferredEvent, target, itemKey, outcome.Amount, destinationModel.Key, 0, 0, 0);
            return DiagnosticCode.None;
        }

        public DiagnosticCode Trade(int vendorKey, int itemKey, int count, int requestId, bool buy)
        {
            if (!rt.Models.TryGetVendor(vendorKey, out VendorModel? vendor) || vendor == null
                || !rt.Index.TryInventoryTarget(vendorKey, out TargetId vendorTarget)
                || !rt.Models.TryGet(itemKey, out ItemModel? item) || item == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            InventorySlots stock = ReadLive(vendorTarget, vendor.StockSlots);
            InventorySlots stockBefore = stock.Clone();
            InventoryOutcome outcome = buy
                ? TradeRules.TryBuy(slots, model.MaxWeight, stock, vendor, item, count, rt.Models)
                : TradeRules.TrySell(slots, model.MaxWeight, stock, vendor, item, count, rt.Models);
            if (!outcome.Accepted)
            {
                return outcome.Refusal == InventoryRefusal.Full || outcome.Refusal == InventoryRefusal.Overweight
                    ? DiagnosticCode.ResourceUnavailable
                    : DiagnosticCode.Ineligible;
            }

            others.Add(new KeyValuePair<TargetId, InventoryPending>(vendorTarget, new InventoryPending(stockBefore, stock)));
            if (buy)
            {
                Events.Add(InventoryIds.ItemGrantedEvent, target, itemKey, count, InventoryRules.CountOf(slots, itemKey), requestId, InventoryIds.SourceBuy, 0);
            }
            else
            {
                Events.Add(InventoryIds.ItemConsumedEvent, target, itemKey, count, InventoryRules.CountOf(slots, itemKey), requestId, 0, 0);
            }

            Events.Add(InventoryIds.TradeDoneEvent, target, vendorKey, itemKey, count, outcome.Amount, buy ? InventoryIds.TradeBuy : InventoryIds.TradeSell, requestId);
            TradeCount++;
            return DiagnosticCode.None;
        }

        public DiagnosticCode Pickup(int worldItemKey, int requestId)
        {
            TargetId worldItem = rt.Index.TargetOf(NarrativeTargetKind.WorldItem, worldItemKey);
            if (worldItem.IsDefault || !rt.Models.TryGetWorldItem(worldItemKey, out WorldItemModel? placed) || placed == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            if (NarrativeSlots.Read(rt.Registry, em, worldItem, InventoryIds.Owner, InventoryIds.ItemTaken, 0) != 0)
            {
                return DiagnosticCode.Ineligible;
            }

            int itemKey = NarrativeSlots.Read(rt.Registry, em, worldItem, InventoryIds.Owner, InventoryIds.ItemKey, placed.ItemKey);
            int count = NarrativeSlots.Read(rt.Registry, em, worldItem, InventoryIds.Owner, InventoryIds.ItemCount, placed.Count);
            if (!rt.Models.TryGet(itemKey, out ItemModel? item) || item == null)
            {
                return DiagnosticCode.MissingDependency;
            }

            InventoryOutcome outcome = InventoryRules.TryAdd(slots, item, count, model.MaxWeight, rt.Models);
            if (!outcome.Accepted)
            {
                NotFit(itemKey, count, outcome.Refusal);
                return DiagnosticCode.None;
            }

            taken.Add(new KeyValuePair<TargetId, int>(worldItem, 1));
            Events.Add(InventoryIds.ItemGrantedEvent, target, itemKey, outcome.Amount, outcome.Total, requestId, InventoryIds.SourcePickup, 0);
            Events.Add(InventoryIds.ItemPickedUpEvent, worldItem, worldItemKey, itemKey, count, model.Key, requestId, 0);
            PickupCount++;
            return DiagnosticCode.None;
        }

        public void Apply()
        {
            Write(target, before, slots);
            for (int i = 0; i < others.Count; i++)
            {
                Write(others[i].Key, others[i].Value.Before, others[i].Value.After);
            }

            for (int i = 0; i < taken.Count; i++)
            {
                NarrativeSlots.Write(rt.Registry, em, taken[i].Key, InventoryIds.Owner, InventoryIds.ItemTaken, taken[i].Value);
            }
        }

        private void NotFit(int itemKey, int count, InventoryRefusal refusal)
        {
            Applied = false;
            FullCount++;
            int reason = refusal == InventoryRefusal.Overweight ? InventoryDeclarations.ReasonWeight : InventoryDeclarations.ReasonNoSlot;
            Events.Add(InventoryIds.InventoryFullEvent, target, itemKey, count, reason, 0, 0, 0);
            slots.CopyFrom(before);
        }

        private InventorySlots ReadLive(TargetId inventory, int slotCount)
        {
            var result = new InventorySlots(slotCount);
            for (int k = 0; k < slotCount; k++)
            {
                result.Items[k] = NarrativeSlots.Read(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Item(k), 0);
                result.Counts[k] = NarrativeSlots.Read(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Count(k), 0);
            }

            result.Currency = NarrativeSlots.Read(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Currency, 0);
            return result;
        }

        private void Write(TargetId inventory, InventorySlots old, InventorySlots next)
        {
            List<int> changed = next.ChangedSince(old);
            for (int i = 0; i < changed.Count; i++)
            {
                int k = changed[i];
                NarrativeSlots.Write(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Item(k), next.Items[k]);
                NarrativeSlots.Write(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Count(k), next.Counts[k]);
            }

            if (next.Currency != old.Currency)
            {
                NarrativeSlots.Write(rt.Registry, em, inventory, InventoryIds.Owner, InventoryIds.Currency, next.Currency);
            }
        }

        private sealed class InventoryPending
        {
            public InventoryPending(InventorySlots before, InventorySlots after)
            {
                Before = before;
                After = after;
            }

            public InventorySlots Before { get; }

            public InventorySlots After { get; }
        }
    }

    /// <summary>The inventory command stage.</summary>
    [DisableAutoCreation]
    public partial class InventoryCommandSystem : SystemBase
    {
        public InventoryModule? Module { get; set; }

        protected override void OnUpdate()
        {
            InventoryModule? module = Module;
            if (module != null)
            {
                module.Run(EntityManager);
            }
        }
    }
}
