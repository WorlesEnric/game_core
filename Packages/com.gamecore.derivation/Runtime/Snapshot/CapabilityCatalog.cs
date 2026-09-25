// GameCore.Derivation — the capability contract table of one derivation input (GC-006).
//
// P-017: a catalog `CapabilityContract` declares output slots, schema, composition policy and reducer version.
// P-019: every output slot chooses exactly one policy, and mixed policies for the same contract/slot are catalog
// errors. This table is the derivation-side lookup for those declarations; it is immutable and canonical.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Immutable capability-contract table, keyed by capability identity (P-017, P-019).</summary>
    public sealed class CapabilityCatalog
    {
        private static readonly CapabilityCatalog EmptyCatalog = new CapabilityCatalog(null);

        private readonly Dictionary<Id128, CapabilityContract> byCapability;
        private readonly List<CapabilityContract> canonical;

        public CapabilityCatalog(IReadOnlyList<CapabilityContract>? contracts)
        {
            byCapability = new Dictionary<Id128, CapabilityContract>();
            canonical = new List<CapabilityContract>();
            if (contracts == null)
            {
                return;
            }

            for (int i = 0; i < contracts.Count; i++)
            {
                CapabilityContract contract = contracts[i];
                if (contract == null)
                {
                    throw new ArgumentException("A capability contract must not be null.", nameof(contracts));
                }

                if (contract.Capability.Capability.IsDefault)
                {
                    throw new ArgumentException("A capability contract requires a real capability identity (P-004).", nameof(contracts));
                }

                if (byCapability.ContainsKey(contract.Capability.Capability.Value))
                {
                    // One capability identity has one declaration; two policies for one slot would be ambiguous
                    // exactly as P-019 forbids.
                    throw new ArgumentException(
                        "A capability identity appears once in one catalog: " + contract.Capability.ToString(),
                        nameof(contracts));
                }

                byCapability.Add(contract.Capability.Capability.Value, contract);
                canonical.Add(contract);
            }

            canonical.Sort(CompareContracts);
        }

        public static CapabilityCatalog Empty => EmptyCatalog;

        /// <summary>Every contract, sorted by capability identity (then version) for stable enumeration.</summary>
        public IReadOnlyList<CapabilityContract> Contracts => canonical;

        public int Count => canonical.Count;

        /// <summary>Null means no such capability contract is declared (never a substituted default, P-009).</summary>
        public CapabilityContract? Find(CapabilityId capability) =>
            byCapability.TryGetValue(capability.Value, out CapabilityContract? found) ? found : null;

        /// <summary>
        /// The declared slot at one index: its schema and its single composition policy. False means the index is
        /// beyond the contract's declared slots, which a validated rule bound never reaches.
        /// </summary>
        public bool TryGetSlot(CapabilityContract contract, uint slot, out OutputSlotSchema? schema, out SlotCompositionPolicy? policy)
        {
            if (contract == null)
            {
                throw new ArgumentNullException(nameof(contract));
            }

            schema = null;
            policy = null;
            if (slot >= (uint)contract.OutputSlots.Count)
            {
                return false;
            }

            schema = contract.OutputSlots[(int)slot];
            IReadOnlyList<SlotCompositionPolicy> policies = contract.SlotPolicies;
            for (int i = 0; i < policies.Count; i++)
            {
                if (policies[i].Slot.Equals(schema.Slot))
                {
                    policy = policies[i];
                    return true;
                }
            }

            return false;
        }

        private static int CompareContracts(CapabilityContract left, CapabilityContract right) =>
            CanonicalDerivationOrder.CompareCapabilityRefs(left.Capability, right.Capability);
    }
}
