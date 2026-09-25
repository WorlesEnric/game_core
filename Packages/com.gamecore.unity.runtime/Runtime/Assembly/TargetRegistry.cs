// GameCore.Unity.Runtime — the stable target registry: `TargetId` <-> `Entity` with generations (GC-008).
//
// Normative sources: 00 P-004 (a target is an assembly recipient represented by one primary ECS entity; auxiliary
// entities are recipe-owned; one world rejects duplicate live stable identities in a category), P-005
// (`TargetHandle` = world + slot + generation; every dereference validates world, generation, liveness and expected
// category; destroy/recreate invalidates handles even when a stable identity is restored), P-024 (despawn destroys
// only recipe-owned entities) and 04 s5 (the stable-ID index stores only lookup information and handle generation
// metadata; raw `Entity.Index`/`Entity.Version` are world/session-local handles and are never a domain identity).
//
// The generation rules live in `GameCore.Planning.TargetSlotLedger` so they are testable without Unity; this type
// adds the one thing that needs the ECS backend — the slot-to-`Entity` mapping, replaced atomically on respawn.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using Unity.Entities;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Live target index of one world. Identities are stable; entities are not. A resolved handle is validated by
    /// the ledger (world, slot, generation, liveness) and only then mapped to the currently live entity, so a stale
    /// handle can never address a reused slot.
    /// </summary>
    public sealed class TargetRegistry
    {
        private readonly TargetSlotLedger ledger;
        private readonly Entity[] entities;
        private readonly Dictionary<Entity, uint> entitySlots = new Dictionary<Entity, uint>();

        public TargetRegistry(WorldId world, uint capacity)
        {
            ledger = new TargetSlotLedger(world, capacity);
            entities = new Entity[capacity];
            for (uint i = 0; i < capacity; i++)
            {
                entities[i] = Entity.Null;
            }
        }

        public WorldId World => ledger.World;

        public uint Capacity => ledger.Capacity;

        public int Count => ledger.LiveCount;

        /// <summary>Handles refused for naming a dead target or a retired generation (P-005).</summary>
        public int StaleRejectionCount => ledger.StaleRejectionCount;

        /// <summary>Allocations refused because the registry is full or a generation counter ended (P-005).</summary>
        public int ExhaustedRejectionCount => ledger.ExhaustedRejectionCount;

        public TargetSlotLedger Ledger => ledger;

        /// <summary>
        /// Allocates a slot and its entity for one stable identity. A duplicate live identity is an
        /// `OwnershipConflict`, never a second slot; a full/exhausted registry is `BudgetExceeded`, never a wrap.
        /// </summary>
        public bool TryAllocate(TargetId target, Entity entity, out TargetHandle handle, out DiagnosticCode code)
        {
            if (entity == Entity.Null)
            {
                code = DiagnosticCode.StaleHandle;
                handle = default(TargetHandle);
                return false;
            }

            if (!ledger.TryAllocate(target, out handle, out code))
            {
                return false;
            }

            entities[handle.Slot] = entity;
            entitySlots[entity] = handle.Slot;
            return true;
        }

        /// <summary>Resolutions refused because a live slot had no entity mapping (never silently substituted).</summary>
        public int MappingMissCount { get; private set; }

        public bool TryResolve(TargetHandle handle, out TargetId target, out Entity entity, out DiagnosticCode code)
        {
            entity = Entity.Null;
            if (!ledger.TryResolve(handle, out target, out code))
            {
                return false;
            }

            entity = entities[handle.Slot];
            if (entity == Entity.Null)
            {
                // A live slot without a live entity means the mapping was dropped; that is stale, not a fallback.
                MappingMissCount++;
                target = default(TargetId);
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            return true;
        }

        /// <summary>Resolves a stable identity to its current handle and entity; false when it is not live.</summary>
        public bool TryResolveTarget(TargetId target, out TargetHandle handle, out Entity entity)
        {
            if (ledger.TryGetHandle(target, out handle))
            {
                entity = entities[handle.Slot];
                if (entity != Entity.Null)
                {
                    return true;
                }
            }

            handle = default(TargetHandle);
            entity = Entity.Null;
            return false;
        }

        /// <summary>
        /// Retires one target: the slot's generation advances, the entity mapping is dropped and the resolved
        /// identity is returned so the caller destroys exactly that target's recipe-owned storage (P-005, P-024).
        /// </summary>
        public bool Retire(TargetHandle handle, out TargetId target, out Entity entity, out DiagnosticCode code)
        {
            entity = Entity.Null;
            if (!ledger.TryResolve(handle, out target, out code))
            {
                return false;
            }

            uint slot = handle.Slot;
            entity = entities[slot];
            entities[slot] = Entity.Null;
            if (entity != Entity.Null)
            {
                entitySlots.Remove(entity);
            }

            return ledger.Retire(handle, out _, out code);
        }

        /// <summary>Retires by identity; used by a despawn that names the target rather than a handle (O-12).</summary>
        public bool RetireTarget(TargetId target, out TargetHandle handle, out Entity entity, out DiagnosticCode code)
        {
            entity = Entity.Null;
            if (!TryResolveTarget(target, out handle, out entity))
            {
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            return Retire(handle, out _, out entity, out code);
        }

        /// <summary>Current handle of a live target; false when it is not live in this world.</summary>
        public bool TryGetHandle(TargetId target, out TargetHandle handle) => ledger.TryGetHandle(target, out handle);

        public bool IsLive(TargetId target) => ledger.IsLive(target);

        /// <summary>Live entity of a stable identity, or <see cref="Entity.Null"/>; no handle validation (index reads).</summary>
        public Entity EntityOf(TargetId target) =>
            ledger.TryGetHandle(target, out TargetHandle handle) ? entities[handle.Slot] : Entity.Null;

        /// <summary>Slot of a live entity; false when the entity is not a registered target (auxiliary entities included).</summary>
        public bool TryGetSlot(Entity entity, out uint slot) => entitySlots.TryGetValue(entity, out slot);

        /// <summary>Live handles in slot order; the publication fence enumerates exactly these (P-030).</summary>
        public IReadOnlyList<TargetHandle> Handles() => ledger.Handles();

        /// <summary>Live stable identities in slot order, for diagnostics and snapshot construction.</summary>
        public IReadOnlyList<TargetId> Targets()
        {
            IReadOnlyList<TargetHandle> handles = ledger.Handles();
            var targets = new List<TargetId>(handles.Count);
            for (int i = 0; i < handles.Count; i++)
            {
                if (ledger.TryResolve(handles[i], out TargetId target, out DiagnosticCode code) && code == DiagnosticCode.None)
                {
                    targets.Add(target);
                }
            }

            return targets;
        }

        private void StaleEntityMapping(uint slot) => _ = slot;
    }
}
