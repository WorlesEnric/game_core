// GameCore.Derivation — the derivation delta between two published results (GC-006).
//
// 05 s4 derivation delta: added/removed/changed contribution keys plus effective slot changes, supports and
// explanation references. P-017 makes the distinction explicit: retracting one of two contributions that support
// the same derived structure must preserve the surviving support, so the delta reports the surviving and the lost
// support identities rather than only a component count.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Builds the canonical delta between a previous published derivation and a new one.</summary>
    public static class DerivationDeltaBuilder
    {
        /// <summary>
        /// Diffs two accepted derivations. <paramref name="contributions"/> is optional and only used to keep the
        /// result deterministic; identity comparison comes from the assemblies themselves.
        /// </summary>
        public static DerivationDelta Build(
            DerivationResult previous,
            IReadOnlyList<TargetAssembly> next,
            IReadOnlyList<CapabilityContribution>? contributions)
        {
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            Dictionary<ContributionKey, ContentHash> before = IndexContributions(previous.Contributions);
            Dictionary<ContributionKey, ContentHash> after = IndexContributions(next);

            List<ContributionKey> added = new List<ContributionKey>();
            List<ContributionKey> removed = new List<ContributionKey>();
            List<ContributionKey> changed = new List<ContributionKey>();

            foreach (KeyValuePair<ContributionKey, ContentHash> pair in after)
            {
                if (!before.TryGetValue(pair.Key, out ContentHash oldHash))
                {
                    added.Add(pair.Key);
                }
                else if (!oldHash.Equals(pair.Value))
                {
                    // The contribution identity survives a payload reconfiguration; only its value revision
                    // changes, which is exactly a "changed" entry rather than remove + add (P-017).
                    changed.Add(pair.Key);
                }
            }

            foreach (KeyValuePair<ContributionKey, ContentHash> pair in before)
            {
                if (!after.ContainsKey(pair.Key))
                {
                    removed.Add(pair.Key);
                }
            }

            added.Sort(CompareKeys);
            removed.Sort(CompareKeys);
            changed.Sort(CompareKeys);

            Dictionary<SlotIdentity, EffectiveSlot> oldSlots = IndexSlots(previous.Assemblies);
            Dictionary<SlotIdentity, EffectiveSlot> newSlots = IndexSlots(next);

            List<EffectiveSlotChange> slotChanges = new List<EffectiveSlotChange>();
            HashSet<Id128> affectedTargets = new HashSet<Id128>();
            HashSet<Id128> affectedRecipes = new HashSet<Id128>();

            foreach (KeyValuePair<SlotIdentity, EffectiveSlot> pair in newSlots)
            {
                if (!oldSlots.TryGetValue(pair.Key, out EffectiveSlot? oldSlot))
                {
                    slotChanges.Add(new EffectiveSlotChange(
                        pair.Key.Target,
                        pair.Key.Capability,
                        pair.Key.Slot,
                        true,
                        false,
                        false,
                        ContentHash.Empty,
                        pair.Value.Hash,
                        KeysOf(pair.Value.Support),
                        Array.Empty<ContributionKey>()));
                    affectedTargets.Add(pair.Key.Target.Value);
                    affectedRecipes.Add(pair.Key.Target.Value);
                    continue;
                }

                List<ContributionKey> lost = MissingSupport(oldSlot.Support, pair.Value.Support);
                List<ContributionKey> gained = MissingSupport(pair.Value.Support, oldSlot.Support);
                bool valueChanged = !oldSlot.Hash.Equals(pair.Value.Hash);
                if (!valueChanged && lost.Count == 0 && gained.Count == 0)
                {
                    continue;
                }

                // A shared component with one of two contributors removed keeps the surviving support and is a
                // "changed" slot, never a removed-and-recreated one (TEST-005).
                slotChanges.Add(new EffectiveSlotChange(
                    pair.Key.Target,
                    pair.Key.Capability,
                    pair.Key.Slot,
                    false,
                    false,
                    valueChanged,
                    oldSlot.Hash,
                    pair.Value.Hash,
                    KeysOf(pair.Value.Support),
                    lost));
                affectedTargets.Add(pair.Key.Target.Value);
                if (valueChanged)
                {
                    affectedRecipes.Add(pair.Key.Target.Value);
                }
            }

            foreach (KeyValuePair<SlotIdentity, EffectiveSlot> pair in oldSlots)
            {
                if (newSlots.ContainsKey(pair.Key))
                {
                    continue;
                }

                slotChanges.Add(new EffectiveSlotChange(
                    pair.Key.Target,
                    pair.Key.Capability,
                    pair.Key.Slot,
                    false,
                    true,
                    true,
                    pair.Value.Hash,
                    ContentHash.Empty,
                    Array.Empty<ContributionKey>(),
                    KeysOf(pair.Value.Support)));
                affectedTargets.Add(pair.Key.Target.Value);
                affectedRecipes.Add(pair.Key.Target.Value);
            }

            // A target whose contribution set changed is affected even when its slot hashes did not move.
            foreach (ContributionKey key in added)
            {
                affectedTargets.Add(key.Target.Value);
            }
            foreach (ContributionKey key in removed)
            {
                affectedTargets.Add(key.Target.Value);
            }

            slotChanges.Sort(CompareSlotChanges);
            return new DerivationDelta(
                added,
                removed,
                changed,
                slotChanges,
                ToTargetList(affectedTargets),
                ToTargetList(affectedRecipes));
        }

        private static Dictionary<ContributionKey, ContentHash> IndexContributions(
            IReadOnlyList<CapabilityContribution> contributions)
        {
            Dictionary<ContributionKey, ContentHash> index = new Dictionary<ContributionKey, ContentHash>();
            for (int i = 0; i < contributions.Count; i++)
            {
                CapabilityContribution contribution = contributions[i];
                if (contribution.Disposition != ContributionDisposition.Active)
                {
                    continue;
                }

                index[contribution.Key] = contribution.PayloadHash;
            }

            return index;
        }

        private static Dictionary<ContributionKey, ContentHash> IndexContributions(
            IReadOnlyList<TargetAssembly> assemblies)
        {
            Dictionary<ContributionKey, ContentHash> index = new Dictionary<ContributionKey, ContentHash>();
            for (int a = 0; a < assemblies.Count; a++)
            {
                IReadOnlyList<CapabilityContribution> contributions = assemblies[a].Contributions;
                for (int i = 0; i < contributions.Count; i++)
                {
                    CapabilityContribution contribution = contributions[i];
                    if (contribution.Disposition != ContributionDisposition.Active)
                    {
                        continue;
                    }

                    index[contribution.Key] = contribution.PayloadHash;
                }
            }

            return index;
        }

        private static Dictionary<SlotIdentity, EffectiveSlot> IndexSlots(IReadOnlyList<TargetAssembly> assemblies)
        {
            Dictionary<SlotIdentity, EffectiveSlot> index = new Dictionary<SlotIdentity, EffectiveSlot>();
            for (int a = 0; a < assemblies.Count; a++)
            {
                IReadOnlyList<EffectiveSlot> slots = assemblies[a].Slots;
                for (int s = 0; s < slots.Count; s++)
                {
                    index[new SlotIdentity(slots[s].Target, slots[s].Capability, slots[s].Slot)] = slots[s];
                }
            }

            return index;
        }

        private static List<ContributionKey> MissingSupport(
            IReadOnlyList<CapabilityContribution> from,
            IReadOnlyList<CapabilityContribution> present)
        {
            List<ContributionKey> missing = new List<ContributionKey>();
            for (int i = 0; i < from.Count; i++)
            {
                bool found = false;
                for (int j = 0; j < present.Count; j++)
                {
                    if (present[j].Key.Equals(from[i].Key))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    missing.Add(from[i].Key);
                }
            }

            missing.Sort(CompareKeys);
            return missing;
        }

        private static List<ContributionKey> KeysOf(IReadOnlyList<CapabilityContribution> contributions)
        {
            List<ContributionKey> keys = new List<ContributionKey>(contributions.Count);
            for (int i = 0; i < contributions.Count; i++)
            {
                keys.Add(contributions[i].Key);
            }

            keys.Sort(CompareKeys);
            return keys;
        }

        private static List<TargetId> ToTargetList(HashSet<Id128> targets)
        {
            List<Id128> sorted = new List<Id128>(targets);
            sorted.Sort(Id128Codec.CompareBigEndian);
            List<TargetId> result = new List<TargetId>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
            {
                result.Add(new TargetId(sorted[i]));
            }

            return result;
        }

        private static int CompareKeys(ContributionKey left, ContributionKey right)
        {
            int provider = left.Provider.Value.CompareTo(right.Provider.Value);
            if (provider != 0)
            {
                return provider;
            }

            int rule = left.Rule.Value.CompareTo(right.Rule.Value);
            if (rule != 0)
            {
                return rule;
            }

            int target = left.Target.Value.CompareTo(right.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            return capability != 0 ? capability : left.OutputSlot.CompareTo(right.OutputSlot);
        }

        private static int CompareSlotChanges(EffectiveSlotChange left, EffectiveSlotChange right)
        {
            int target = left.Target.Value.CompareTo(right.Target.Value);
            if (target != 0)
            {
                return target;
            }

            int capability = left.Capability.Value.CompareTo(right.Capability.Value);
            return capability != 0 ? capability : left.Slot.CompareTo(right.Slot);
        }

        /// <summary>Canonical identity of one effective slot: target, capability and slot index.</summary>
        private readonly struct SlotIdentity : IEquatable<SlotIdentity>
        {
            public readonly TargetId Target;
            public readonly CapabilityId Capability;
            public readonly uint Slot;

            public SlotIdentity(TargetId target, CapabilityId capability, uint slot)
            {
                Target = target;
                Capability = capability;
                Slot = slot;
            }

            public bool Equals(SlotIdentity other) =>
                Target.Equals(other.Target) && Capability.Equals(other.Capability) && Slot == other.Slot;

            public override bool Equals(object? obj) => obj is SlotIdentity other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + Target.GetHashCode();
                    hash = (hash * 31) + Capability.GetHashCode();
                    hash = (hash * 31) + Slot.GetHashCode();
                    return hash;
                }
            }
        }
    }
}
