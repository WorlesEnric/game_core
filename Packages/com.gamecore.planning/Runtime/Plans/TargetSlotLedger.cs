// GameCore.Planning — stable target slots, generations and handle validation (GC-008).
//
// Normative sources: 00 P-004 (a target is an assembly recipient represented by one primary ECS entity; stable
// 128-bit identities, never reused) and P-005 (`TargetHandle` = world + slot + generation; every dereference
// validates world, generation, liveness and expected category; destroy/recreate invalidates handles even when a
// stable identity is restored; unsigned 64-bit counters overflow by rejecting further allocation, never by
// wrapping).
//
// This ledger is the engine-free half of the target registry: it owns slot allocation, generations and handle
// validation, and the Unity registry maps a slot to an `Entity`. Splitting it this way means the stale-handle
// rules are testable without Unity, while the entity mapping stays where the ECS backend is.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>One slot of the ledger: allocated identity plus the generation a handle must carry (P-005).</summary>
    public readonly struct TargetSlotEntry
    {
        public readonly uint Slot;
        public readonly bool IsAllocated;

        /// <summary>The slot's live identity; default when the slot is free or exhausted.</summary>
        public readonly TargetId Target;

        /// <summary>Generation a live handle must carry; a retired slot's generation has already advanced.</summary>
        public readonly ulong Generation;

        /// <summary>True when the generation counter is exhausted, so this slot can never be allocated again.</summary>
        public readonly bool IsExhausted;

        /// <summary>Times this slot was allocated; the third allocation of a slot carries generation 3.</summary>
        public readonly uint Allocations;

        public TargetSlotEntry(uint slot, bool isAllocated, TargetId target, ulong generation, bool isExhausted, uint allocations)
        {
            Slot = slot;
            IsAllocated = isAllocated;
            Target = target;
            Generation = generation;
            IsExhausted = isExhausted;
            Allocations = allocations;
        }

        public override string ToString() =>
            "#" + Slot.ToString(CultureInfo.InvariantCulture)
            + (IsAllocated ? ":" + Target.ToString() : ":free")
            + "@" + Generation.ToString(CultureInfo.InvariantCulture)
            + (IsExhausted ? ":exhausted" : string.Empty);
    }

    /// <summary>
    /// Slot/generation ledger of one world's targets. Generation semantics match P-005 exactly: a fresh slot starts
    /// at generation 1, retiring a slot advances its generation so every handle taken before the retire is stale,
    /// and a generation that would overflow refuses the allocation instead of wrapping.
    /// </summary>
    public sealed class TargetSlotLedger
    {
        private readonly TargetSlotEntry[] slots;
        private readonly Dictionary<Id128, uint> slotOfTarget = new Dictionary<Id128, uint>();
        private uint liveCount;

        public TargetSlotLedger(WorldId world, uint capacity)
        {
            if (world.Session.IsDefault)
            {
                throw new ArgumentException("A target ledger belongs to a real world incarnation (P-004).", nameof(world));
            }

            if (capacity == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A ledger with no slots cannot allocate a target.");
            }

            World = world;
            slots = new TargetSlotEntry[capacity];
            for (uint i = 0; i < capacity; i++)
            {
                slots[i] = new TargetSlotEntry(i, false, default(TargetId), 0UL, false, 0U);
            }
        }

        public WorldId World { get; }

        public uint Capacity => (uint)slots.Length;

        public int LiveCount => (int)liveCount;

        public int FreeCount => slots.Length - (int)liveCount;

        /// <summary>Handles refused because the named target is not live or already retired (P-005).</summary>
        public int StaleRejectionCount { get; private set; }

        /// <summary>Allocations refused because every slot is allocated or generation-exhausted (P-005).</summary>
        public int ExhaustedRejectionCount { get; private set; }

        /// <summary>True when at least one slot can never be reused because its generation counter ended.</summary>
        public bool HasExhaustedSlot { get; private set; }

        public IReadOnlyList<TargetSlotEntry> Entries => slots;

        /// <summary>
        /// Whether a slot's generation counter can no longer advance, so the slot must never be reused (P-005:
        /// overflow rejects further allocation and requires world recreation, never wraparound). A never-allocated
        /// slot is not exhausted: its first allocation takes generation 1 regardless of the stored counter.
        /// </summary>
        public static bool IsExhaustedGeneration(ulong generation, uint allocations) =>
            allocations != 0U && generation == ulong.MaxValue;

        /// <summary>Allocates a slot for one target; a live duplicate target is a conflict, never a second slot.</summary>
        public bool TryAllocate(TargetId target, out TargetHandle handle, out DiagnosticCode code)
        {
            if (target.IsDefault)
            {
                code = DiagnosticCode.StaleHandle;
                handle = default(TargetHandle);
                return false;
            }

            if (slotOfTarget.TryGetValue(target.Value, out uint existing))
            {
                // One world rejects duplicate live stable identities in a category (P-004).
                code = DiagnosticCode.OwnershipConflict;
                handle = default(TargetHandle);
                return false;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                TargetSlotEntry slot = slots[i];
                if (slot.IsAllocated || slot.IsExhausted)
                {
                    continue;
                }

                ulong generation = slot.Generation;
                if (IsExhaustedGeneration(generation, slot.Allocations))
                {
                    // The counter cannot advance, so this slot must never be reused (P-005).
                    slots[i] = new TargetSlotEntry(slot.Slot, false, default(TargetId), ulong.MaxValue, true, slot.Allocations);
                    HasExhaustedSlot = true;
                    continue;
                }

                generation = slot.Allocations == 0U ? 1UL : generation + 1UL;
                slots[i] = new TargetSlotEntry(slot.Slot, true, target, generation, false, slot.Allocations + 1U);
                slotOfTarget.Add(target.Value, slot.Slot);
                liveCount++;
                handle = new TargetHandle(World, slot.Slot, generation);
                code = DiagnosticCode.None;
                return true;
            }

            ExhaustedRejectionCount++;
            code = DiagnosticCode.BudgetExceeded;
            handle = default(TargetHandle);
            return false;
        }

        /// <summary>
        /// Validates and resolves one handle: world incarnation, slot range, liveness, generation and category. A
        /// failure is `StaleHandle` and never a substituted target (P-005).
        /// </summary>
        public bool TryResolve(TargetHandle handle, out TargetId target, out DiagnosticCode code)
        {
            target = default(TargetId);

            if (!handle.World.Session.Equals(World.Session))
            {
                StaleRejectionCount++;
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            if (handle.Slot >= slots.Length)
            {
                StaleRejectionCount++;
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            TargetSlotEntry slot = slots[handle.Slot];
            if (!slot.IsAllocated || slot.Generation != handle.Generation)
            {
                StaleRejectionCount++;
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            target = slot.Target;
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Resolves a live target's current handle; a target that was never allocated reports false.</summary>
        public bool TryGetHandle(TargetId target, out TargetHandle handle)
        {
            if (slotOfTarget.TryGetValue(target.Value, out uint slot))
            {
                handle = new TargetHandle(World, slot, slots[slot].Generation);
                return true;
            }

            handle = default(TargetHandle);
            return false;
        }

        public bool IsLive(TargetId target) => slotOfTarget.ContainsKey(target.Value);

        /// <summary>The slot's current generation; a handle taken before a retire no longer matches it (P-005).</summary>
        public ulong GenerationOf(uint slot)
        {
            if (slot >= slots.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            return slots[slot].Generation;
        }

        /// <summary>
        /// Retires one target's slot and advances its generation, which invalidates every handle taken before the
        /// retire (P-005). The resolved identity is returned so the caller can retract exactly that target's state.
        /// </summary>
        public bool Retire(TargetHandle handle, out TargetId target, out DiagnosticCode code)
        {
            if (!TryResolve(handle, out target, out code))
            {
                return false;
            }

            uint slot = handle.Slot;
            TargetSlotEntry entry = slots[slot];
            slotOfTarget.Remove(entry.Target.Value);
            liveCount--;

            // The next allocation of this slot takes generation+1, so the handle just retired can never resolve.
            slots[slot] = new TargetSlotEntry(slot, false, default(TargetId), entry.Generation, false, entry.Allocations);
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Retires by identity; false when the target is not live in this world.</summary>
        public bool RetireTarget(TargetId target, out TargetHandle handle, out DiagnosticCode code)
        {
            if (!TryGetHandle(target, out handle))
            {
                code = DiagnosticCode.StaleHandle;
                return false;
            }

            return Retire(handle, out _, out code);
        }

        /// <summary>Canonical handles of every live target, ordered by slot; diagnostics and tests only.</summary>
        public IReadOnlyList<TargetHandle> Handles()
        {
            var handles = new List<TargetHandle>(LiveCount);
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsAllocated)
                {
                    handles.Add(new TargetHandle(World, slots[i].Slot, slots[i].Generation));
                }
            }

            return handles;
        }
    }
}
