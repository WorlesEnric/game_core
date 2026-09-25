// GameCore.Derivation — the canonical catalog hash a derived-recipe cache keys on (GC-013, P-024).
//
// P-024 caches derived variants by "recipe revision, scope inheritance fingerprint, mode, and catalog hash". The
// integration layer owns the real catalog fingerprint (`ImmutableCatalog.Fingerprint`); a pure caller that only
// has a snapshot derives the same kind of hash here from the capability-contract table the snapshot carries. The
// text is canonical and complete over every field a contract declares to derivation: identity and version, the
// stratum, each output slot's schema and policy with its reducer key, and the cross-capability incompatibility
// set. Two catalogs with equal text are indistinguishable to the engine, which is the only property a cache key
// needs (P-008, P-028).
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Canonical hash of a capability-contract table (the P-024 catalog dimension).</summary>
    public static class CapabilityCatalogHash
    {
        /// <summary>Hash of one snapshot's contract table.</summary>
        public static ContentHash Compute(DerivationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return Compute(snapshot.Contracts);
        }

        /// <summary>Hash of one capability catalog, canonical over identity, stratum, slots, policies and incompatibilities.</summary>
        public static ContentHash Compute(CapabilityCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(Text(catalog)));
        }

        /// <summary>Canonical text of the contract table; the basis of <see cref="Compute(CapabilityCatalog)"/>.</summary>
        public static string Text(CapabilityCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            StringBuilder text = new StringBuilder();
            IReadOnlyList<CapabilityContract> contracts = catalog.Contracts;
            for (int c = 0; c < contracts.Count; c++)
            {
                CapabilityContract contract = contracts[c];
                text.Append("contract=").Append(contract.Capability.ToString())
                    .Append(";stratum=").Append(PayloadCodec.PriorityText(contract.Stratum)).Append('\n');

                IReadOnlyList<OutputSlotSchema> slots = contract.OutputSlots;
                for (int s = 0; s < slots.Count; s++)
                {
                    text.Append("  slot=").Append(slots[s].Slot.ToString())
                        .Append('/').Append(slots[s].Schema.ToString()).Append('\n');
                }

                IReadOnlyList<SlotCompositionPolicy> policies = contract.SlotPolicies;
                for (int p = 0; p < policies.Count; p++)
                {
                    text.Append("  policy=").Append(policies[p].Slot.ToString())
                        .Append('/').Append(policies[p].Policy.ToString())
                        .Append('/').Append(policies[p].Reducer.RegistrationKey.ToString())
                        .Append('@').Append(policies[p].Reducer.KeyVersion).Append('\n');
                }

                IReadOnlyList<CapabilityId> incompatible = contract.IncompatibleCapabilities;
                for (int i = 0; i < incompatible.Count; i++)
                {
                    text.Append("  incompatible=").Append(incompatible[i].ToString()).Append('\n');
                }
            }

            return text.ToString();
        }
    }
}
