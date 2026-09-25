// GameCore.Unity.Runtime — ECS storage published by the assembly publisher (GC-008).
//
// Normative sources: 03 s1 (the runtime records a world owns: `TargetIdentity`, `ScopeMembership`, generated
// `CapabilityBinding` data, plugin-owned components, dormant state) 04 s5 (runtime state and derived data occupy
// distinct ECS components; generated binding components identify the contribution, its provider and schema
// version; the stable-ID index stores only TargetId-to-Entity lookup information and handle generation metadata)
// and 04 s6 (a spawned target is created pending, gets its stable ids and membership, then its complete derived
// bindings, and only then becomes active/visible).
//
// Three component roles, deliberately separate:
//   * `TargetIdentity` + `AssemblyStamp` — the stable identity and the epoch the target's rows were published in;
//   * `CapabilityBinding` — one derived row per (capability, output slot), with its support and active/dormant bit;
//   * `TargetSlotState` — owner state with its own schema version, separate from derived data (P-032).
// All three are blittable and contain no managed references, so they are safe in Burst jobs and IL2CPP.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using Unity.Entities;

namespace GameCore.Unity.Runtime
{
    /// <summary>
    /// Stable target identity plus the handle generation a resolver must carry (P-004, P-005). The generation is
    /// duplicated here so a resolved entity can be validated against a handle without touching the registry.
    /// </summary>
    public struct TargetIdentity : IComponentData
    {
        public TargetId Target;

        /// <summary>Handle generation of the target's current lifetime; it changes on despawn (P-005).</summary>
        public ulong Generation;
    }

    /// <summary>
    /// World stamp of one target: the assembly epoch its rows were published in and its slot in the registry. The
    /// stamp is the per-target half of the publication fence: a reader can compare it with the world's published
    /// epoch to detect a stale row without enumerating the binding table (03 s1 `ScopeMembership` note).
    /// </summary>
    public struct AssemblyStamp : IComponentData
    {
        public ulong AssemblyEpoch;

        public ulong CompositionRevision;

        /// <summary>Registry slot; the target-entity mapping is `<see cref="AssemblyStamp"/>.Slot` plus generation.</summary>
        public uint Slot;

        /// <summary>Zero while the target is pending a spawn publication; non-zero once it is visible (P-024).</summary>
        public uint Published;
    }

    /// <summary>
    /// One effective derived binding row (03 s1, 04 s5). Its identity is `(capability, output slot)` inside the
    /// owning target entity; `Active` is 0 for a row that is retained but excluded from active queries, which is
    /// the `PreserveDormant` shape of P-032.
    /// </summary>
    public struct CapabilityBinding : IBufferElementData
    {
        public CapabilityId Capability;

        public uint CapabilityVersion;

        public uint OutputSlot;

        public int Value;

        /// <summary>
        /// Provider installation that supports this row; removing its support removes only its own supports (P-033).
        /// The full set of supporters is the parallel `CapabilitySupportRow` buffer, and this field is the
        /// highest-ranked member of it, so a reader that wants "who owns this row" still has one answer.
        /// </summary>
        public ProviderInstallationId Provider;

        public ulong ProviderGeneration;

        public int Priority;

        public SchemaRef Schema;

        /// <summary>1 while the row participates in active queries; 0 when it is dormant (P-032).</summary>
        public byte Active;

        /// <summary>Number of contributions that support this row; 1 is the ordinary single-provider case (P-017).</summary>
        public int SupporterCount;

        public bool IsActive => Active != 0;

        /// <summary>True when more than one contribution supports this row (P-017).</summary>
        public bool IsMultiSupport => SupporterCount > 1;
    }

    /// <summary>
    /// One contribution that supports an effective binding row (P-017). This is the published form of
    /// `GameCore.Planning.CapabilitySupport`: the row keyed by `(capability, output slot)` carries the composed
    /// value of P-019, and one of these rows carries each supporter's own contribution value, so an `Additive` slot
    /// that a reducer folded from two providers is inspectable in a live world instead of collapsing to its
    /// top-ranked candidate. Removing one provider removes exactly its own support rows (P-033).
    /// </summary>
    public struct CapabilitySupportRow : IBufferElementData
    {
        public CapabilityId Capability;

        public uint OutputSlot;

        /// <summary>The supporter's provider installation (P-017).</summary>
        public ProviderInstallationId Provider;

        public ulong ProviderGeneration;

        /// <summary>The derivation rule that emitted this contribution; part of its `ContributionKey` (P-017).</summary>
        public RuleId Rule;

        /// <summary>The supporter's own contribution value, before the slot policy composed the effective value.</summary>
        public int Value;

        public int Priority;

        public override string ToString() =>
            Capability.ToString() + "#" + OutputSlot.ToString(CultureInfo.InvariantCulture)
            + "@" + Provider.ToString() + "/" + Rule.ToString()
            + "=" + Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// One owner state slot of one target, with its own schema version (P-032, P-034). Mutable gameplay state lives
    /// here and never in the derived binding buffer, so recomposing a binding cannot silently reset gameplay data.
    /// </summary>
    public struct TargetSlotState : IBufferElementData
    {
        public SlotId Slot;

        public OwnerId Owner;

        public uint SchemaVersion;

        public int Value;

        /// <summary>1 while an active owner writes this slot; 0 when it is dormant but retained (P-032).</summary>
        public byte Active;

        public bool IsActive => Active != 0;
    }

    /// <summary>Helpers for the published storage: the fixture, the publisher and tests share these shapes.</summary>
    public static class AssemblyStorage
    {
        /// <summary>Finds one binding row by identity; the buffer is small and canonically ordered (P-017).</summary>
        public static bool TryFindBinding(
            DynamicBuffer<CapabilityBinding> bindings,
            CapabilityId capability,
            uint outputSlot,
            out CapabilityBinding binding)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Capability.Equals(capability) && bindings[i].OutputSlot == outputSlot)
                {
                    binding = bindings[i];
                    return true;
                }
            }

            binding = default(CapabilityBinding);
            return false;
        }

        /// <summary>
        /// Collects the support rows of one binding row in canonical order (P-017). The buffer is small and already
        /// in canonical order, so this is a bounded scan and never a managed allocation beyond the returned list.
        /// </summary>
        public static void CollectSupports(
            DynamicBuffer<CapabilitySupportRow> supports,
            CapabilityId capability,
            uint outputSlot,
            List<CapabilitySupportRow> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            for (int i = 0; i < supports.Length; i++)
            {
                CapabilitySupportRow row = supports[i];
                if (row.Capability.Equals(capability) && row.OutputSlot == outputSlot)
                {
                    destination.Add(row);
                }
            }
        }

        /// <summary>
        /// True when the support buffer holds a support with this identity (P-017, P-033): the exact test a
        /// retraction uses to remove one provider's support without disturbing a co-supporter's.
        /// </summary>
        public static bool TryFindSupport(
            DynamicBuffer<CapabilitySupportRow> supports,
            CapabilityId capability,
            uint outputSlot,
            ProviderInstallationId provider,
            RuleId rule,
            out int index)
        {
            for (int i = 0; i < supports.Length; i++)
            {
                CapabilitySupportRow row = supports[i];
                if (row.Capability.Equals(capability) && row.OutputSlot == outputSlot
                    && row.Provider.Equals(provider) && row.Rule.Equals(rule))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /// <summary>Counts the support rows of one binding row (P-017).</summary>
        public static int CountSupports(
            DynamicBuffer<CapabilitySupportRow> supports,
            CapabilityId capability,
            uint outputSlot)
        {
            int count = 0;
            for (int i = 0; i < supports.Length; i++)
            {
                CapabilitySupportRow row = supports[i];
                if (row.Capability.Equals(capability) && row.OutputSlot == outputSlot)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Finds one state slot row by `(owner, slot)`; a miss means the target has no such state (P-032).</summary>
        public static bool TryFindSlot(
            DynamicBuffer<TargetSlotState> slots,
            OwnerId owner,
            SlotId slot,
            out int index)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Owner.Equals(owner) && slots[i].Slot.Equals(slot))
                {
                    index = i;
                    return true;
                }
            }

            index = -1;
            return false;
        }

        /// <summary>Counts active rows, so a dormant-only target is observable (P-032).</summary>
        public static int CountActive(DynamicBuffer<CapabilityBinding> bindings)
        {
            int count = 0;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].IsActive)
                {
                    count++;
                }
            }

            return count;
        }
    }
}
