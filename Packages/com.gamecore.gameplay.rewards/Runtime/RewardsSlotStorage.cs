// GameCore.Gameplay.Rewards — reading and writing the outbox slot's live row (GC-024).
//
// The outbox slot's value is real live state: one `TargetSlotState` row per `(OwnerId, SlotId)` inside the target
// entity's buffer, carrying `SchemaVersion`, `Value` and the `Active` bit that distinguishes retained state from a
// slot with an active writer (Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyComponents.cs:130-144,
// P-032). The row is the same one `GameCore.Unity.Runtime.Integration.LiveTargetSeeder.TrySeedSlot`
// (LiveTargetSeeder.cs:111 and :167) creates and replaces, and the same one
// `AssemblyPublisher.ReadSlotStates(TargetId)` returns (AssemblyPublisher.cs:1049).
//
// WHY THIS FILE TOUCHES THE ENTITY MANAGER DIRECTLY. `UnityWorldHost` deliberately exposes no target surface: the
// `TargetId -> Entity` map lives in `TargetRegistry`, which is owned by whoever constructs it and is reachable in
// production only through `AssemblyPublisher.Registry` (ctor-injected). Neither the seeder nor the publisher is
// available to `RewardsInstallation.Mount(UnityWorldHost, WorldTimeDriver, RewardCatalog, ...)`, whose signature is
// fixed, so this module reaches the target entity the way the shipped checkpoint reader does when it has only a
// host: enumerate the entities carrying `TargetIdentity` and match the stable target id
// (Packages/com.gamecore.unity.runtime/Runtime/Persistence/CheckpointRestoreExecutor.cs:487-495). The row shape
// written below is field-for-field the seeder's, so the two cannot disagree about what a slot row is (P-054).
//
// Every method returns a value instead of throwing and reports through `detail` why it could not do what its name
// says: a read this module cannot perform returns false with the reason (P-052).
#nullable enable
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Runtime;
using Unity.Collections;
using Unity.Entities;

namespace GameCore.Gameplay.Rewards
{
    /// <summary>Live-storage access to the reward outbox slot row of one target.</summary>
    internal static class RewardsSlotStorage
    {
        /// <summary>
        /// The target that currently carries this package's outbox row, found by reading the live rows: the entity
        /// scan matches `TargetIdentity.Target`, so the answer is the world's own mapping rather than a remembered
        /// value. Returns false with a reason when no live row exists (the slot was never seeded, or the target is
        /// not in this world), which is a value, not a silent zero.
        /// </summary>
        internal static bool TryFindSlotTarget(
            UnityWorldHost host,
            out TargetId target,
            out string detail)
        {
            target = default(TargetId);
            detail = string.Empty;
            EntityManager entityManager = host.EntityWorld.EntityManager;
            NativeArray<Entity> entities = entityManager.GetAllEntities(Allocator.Temp);
            try
            {
                for (int e = 0; e < entities.Length; e++)
                {
                    Entity entity = entities[e];
                    if (!entityManager.HasComponent<TargetIdentity>(entity)
                        || !entityManager.HasBuffer<TargetSlotState>(entity))
                    {
                        continue;
                    }

                    DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                    for (int s = 0; s < slots.Length; s++)
                    {
                        if (!slots[s].Slot.Equals(RewardsKeys.OutboxSlot)
                            || !slots[s].Owner.Equals(RewardsKeys.OutboxOwner))
                        {
                            continue;
                        }

                        target = entityManager.GetComponentData<TargetIdentity>(entity).Target;
                        detail = "the outbox row of target " + target.ToString() + " is live (schemaVersion="
                            + slots[s].SchemaVersion.ToString(CultureInfo.InvariantCulture) + ", value="
                            + slots[s].Value.ToString(CultureInfo.InvariantCulture) + ").";
                        return true;
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            detail = "no live target carries a row for slot " + RewardsKeys.OutboxSlot.ToString() + " owned by "
                + RewardsKeys.OutboxOwner.ToString()
                + "; the outbox slot has not been seeded in this world, so there is no live value to read (P-032).";
            return false;
        }

        /// <summary>
        /// Writes (or replaces) the outbox row of one target with an explicit schema version, value and active
        /// flag, exactly as `LiveTargetSeeder.TrySeedSlot` does. The row is added when it is absent, and a
        /// replacement rewrites only the three fields, so the rest of the row is left as the writer found it.
        /// </summary>
        internal static bool Write(
            UnityWorldHost host,
            TargetId target,
            uint schemaVersion,
            int value,
            bool active,
            out string detail)
        {
            if (!TryResolveEntity(host, target, out Entity entity, out detail))
            {
                return false;
            }

            EntityManager entityManager = host.EntityWorld.EntityManager;
            DynamicBuffer<TargetSlotState> slots = entityManager.HasBuffer<TargetSlotState>(entity)
                ? entityManager.GetBuffer<TargetSlotState>(entity)
                : entityManager.AddBuffer<TargetSlotState>(entity);

            byte flag = active ? (byte)1 : (byte)0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Slot.Equals(RewardsKeys.OutboxSlot)
                    && slots[i].Owner.Equals(RewardsKeys.OutboxOwner))
                {
                    TargetSlotState replaced = slots[i];
                    replaced.SchemaVersion = schemaVersion;
                    replaced.Value = value;
                    replaced.Active = flag;
                    slots[i] = replaced;
                    detail = DescribeRow(target, schemaVersion, value, active);
                    return true;
                }
            }

            slots.Add(new TargetSlotState
            {
                Slot = RewardsKeys.OutboxSlot,
                Owner = RewardsKeys.OutboxOwner,
                SchemaVersion = schemaVersion,
                Value = value,
                Active = flag,
            });
            detail = DescribeRow(target, schemaVersion, value, active);
            return true;
        }

        /// <summary>
        /// Reads the outbox row of one target: the value, its recorded schema version and whether it is dormant.
        /// A target that carries no such row, or no slot buffer at all, returns false with the reason instead of
        /// reporting a zero value that was never stored.
        /// </summary>
        internal static bool TryRead(
            UnityWorldHost host,
            TargetId target,
            out uint schemaVersion,
            out int value,
            out bool dormant,
            out string detail)
        {
            schemaVersion = 0U;
            value = 0;
            dormant = false;
            if (!TryResolveEntity(host, target, out Entity entity, out detail))
            {
                return false;
            }

            EntityManager entityManager = host.EntityWorld.EntityManager;
            if (!entityManager.HasBuffer<TargetSlotState>(entity))
            {
                detail = "target " + target.ToString()
                    + " carries no state-slot buffer, so the outbox row was never seeded there (P-032).";
                return false;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Slot.Equals(RewardsKeys.OutboxSlot)
                    || !slots[i].Owner.Equals(RewardsKeys.OutboxOwner))
                {
                    continue;
                }

                schemaVersion = slots[i].SchemaVersion;
                value = slots[i].Value;
                dormant = !slots[i].IsActive;
                detail = DescribeRow(target, schemaVersion, value, !dormant);
                return true;
            }

            detail = "target " + target.ToString() + " carries no row for slot "
                + RewardsKeys.OutboxSlot.ToString() + " owned by " + RewardsKeys.OutboxOwner.ToString()
                + "; the outbox slot is not seeded on this target (P-032).";
            return false;
        }

        /// <summary>The entity carrying one stable target identity, or false with the reason it is absent (P-005).</summary>
        private static bool TryResolveEntity(
            UnityWorldHost host,
            TargetId target,
            out Entity entity,
            out string detail)
        {
            entity = Entity.Null;
            detail = string.Empty;
            EntityManager entityManager = host.EntityWorld.EntityManager;
            NativeArray<Entity> entities = entityManager.GetAllEntities(Allocator.Temp);
            try
            {
                for (int e = 0; e < entities.Length; e++)
                {
                    Entity candidate = entities[e];
                    if (!entityManager.HasComponent<TargetIdentity>(candidate))
                    {
                        continue;
                    }

                    if (!entityManager.GetComponentData<TargetIdentity>(candidate).Target.Equals(target))
                    {
                        continue;
                    }

                    entity = candidate;
                    return true;
                }
            }
            finally
            {
                entities.Dispose();
            }

            detail = "target " + target.ToString()
                + " is not registered in this world; a stable identity that resolves to no live entity is a stale"
                + " reference, never an implicit creation (P-005).";
            return false;
        }

        /// <summary>One `detail` line naming the row this module just read or wrote.</summary>
        private static string DescribeRow(TargetId target, uint schemaVersion, int value, bool active) =>
            "outboxRow(target=" + target.ToString() + ", slot=" + RewardsKeys.OutboxSlot.ToString()
            + ", owner=" + RewardsKeys.OutboxOwner.ToString() + ", schemaVersion="
            + schemaVersion.ToString(CultureInfo.InvariantCulture) + ", value="
            + value.ToString(CultureInfo.InvariantCulture) + ", active=" + (active ? "1" : "0") + ")";
    }
}
