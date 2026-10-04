// GameCore.Gameplay.Entities - owner-state access through the published slot storage (P-032, P-034).
//
// Gameplay state lives in each target's TargetSlotState buffer: one int32 row per (owner, slot), which is exactly what
// checkpoints capture and restore. Systems write only their own owner's rows; presentation only reads.
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Gameplay.Entities
{
    /// <summary>Reads and writes owned int32 slots on target entities.</summary>
    public static class SlotState
    {
        public static bool TryRead(EntityManager entityManager, Entity entity, OwnerId owner, SlotId slot, out int value)
        {
            value = 0;
            if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasBuffer<TargetSlotState>(entity))
            {
                return false;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity, true);
            if (!AssemblyStorage.TryFindSlot(slots, owner, slot, out int row))
            {
                return false;
            }

            value = slots[row].Value;
            return true;
        }

        public static int ReadOrDefault(EntityManager entityManager, Entity entity, OwnerId owner, SlotId slot, int fallback) =>
            TryRead(entityManager, entity, owner, slot, out int value) ? value : fallback;

        /// <summary>Writes one owned slot at <see cref="GameplaySlots.SchemaVersion"/>, appending the row when missing.</summary>
        public static void Write(EntityManager entityManager, Entity entity, OwnerId owner, SlotId slot, int value)
        {
            if (entity == Entity.Null || !entityManager.Exists(entity))
            {
                return;
            }

            DynamicBuffer<TargetSlotState> slots = entityManager.HasBuffer<TargetSlotState>(entity)
                ? entityManager.GetBuffer<TargetSlotState>(entity)
                : entityManager.AddBuffer<TargetSlotState>(entity);

            if (AssemblyStorage.TryFindSlot(slots, owner, slot, out int row))
            {
                TargetSlotState existing = slots[row];
                existing.SchemaVersion = GameplaySlots.SchemaVersion;
                existing.Value = value;
                existing.Active = 1;
                slots[row] = existing;
                return;
            }

            slots.Add(new TargetSlotState
            {
                Slot = slot,
                Owner = owner,
                SchemaVersion = GameplaySlots.SchemaVersion,
                Value = value,
                Active = 1,
            });
        }
    }

    /// <summary>Committed slot reads of one world's targets through its registry (presentation, queries).</summary>
    public sealed class WorldSlotReader : ICommittedSlotReader
    {
        private readonly global::Unity.Entities.World world;
        private readonly TargetRegistry registry;

        public WorldSlotReader(global::Unity.Entities.World world, TargetRegistry registry)
        {
            this.world = world;
            this.registry = registry;
        }

        public bool TryRead(TargetId target, OwnerId owner, SlotId slot, out int value)
        {
            value = 0;
            if (world == null || !world.IsCreated || !registry.TryResolveTarget(target, out TargetHandle _, out Entity entity))
            {
                return false;
            }

            return SlotState.TryRead(world.EntityManager, entity, owner, slot, out value);
        }

        public int ReadOrDefault(TargetId target, OwnerId owner, SlotId slot, int fallback) =>
            TryRead(target, owner, slot, out int value) ? value : fallback;
    }
}
