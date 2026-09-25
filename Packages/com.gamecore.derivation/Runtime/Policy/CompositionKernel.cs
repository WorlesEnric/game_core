// GameCore.Derivation — canonical composition of one output slot (GC-006).
//
// P-019 defines exactly five policies and says every output slot chooses exactly one, that mixed policies for the
// same contract/slot are catalog errors, and that a reducer is pure, bounded, versioned and supplied by the
// contract's package. This file implements all five plus the P-018 override rules; the indexed engine and the
// reference oracle both call it, so composition semantics cannot drift between them.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>One rejected composition step: its stable code, its slot and the witnesses that caused it (P-028).</summary>
    public sealed class CompositionFailure
    {
        public CompositionFailure(
            DiagnosticCode code,
            TargetId target,
            CapabilityId capability,
            uint slot,
            IReadOnlyList<ContributionKey>? involved,
            IReadOnlyList<Id128>? witnessKeys,
            string summary)
        {
            Code = code;
            Target = target;
            Capability = capability;
            Slot = slot;
            Involved = ContractCollections.Freeze(involved);
            WitnessKeys = ContractCollections.Freeze(witnessKeys);
            Summary = summary ?? string.Empty;
        }

        public DiagnosticCode Code { get; }

        public TargetId Target { get; }

        public CapabilityId Capability { get; }

        public uint Slot { get; }

        /// <summary>Every candidate identity that took part, so conflicting provenance is reportable (P-019).</summary>
        public IReadOnlyList<ContributionKey> Involved { get; }

        /// <summary>Stable witness keys naming the exact members of a cycle, ambiguity or missing endpoint.</summary>
        public IReadOnlyList<Id128> WitnessKeys { get; }

        public string Summary { get; }

        public override string ToString() =>
            "CompositionFailure(" + Code.ToString() + ", " + Target.ToString() + "/" + Capability.ToString()
            + "#" + Slot + ", " + Summary + ")";
    }

    /// <summary>Outcome of resolving the P-018 selection override for one slot.</summary>
    public enum OverrideResolution
    {
        /// <summary>No override addresses this slot; rank decides the winner where the policy allows it.</summary>
        None = 0,

        /// <summary>Exactly one override addresses this slot and names an eligible candidate.</summary>
        Selected = 1,

        /// <summary>Two or more overrides address this slot with different providers: a versioned-data conflict.</summary>
        Ambiguous = 2,

        /// <summary>The override names a candidate the engine evaluated but that is not eligible (P-018).</summary>
        NotEligible = 3,
    }

    /// <summary>Composes the eligible candidates of one (target, capability, slot) group into one effective slot.</summary>
    public static class SlotComposer
    {
        /// <summary>
        /// Applies the declared policy. <paramref name="eligible"/> is already in P-018 precedence order;
        /// <paramref name="evaluated"/> holds every identity the engine evaluated for this (target, capability)
        /// pair, so an override naming an evaluated-but-ineligible candidate is reported rather than silently
        /// falling back to rank.
        /// </summary>
        public static bool TryCompose(
            DerivationSnapshot snapshot,
            IDerivationValueSource values,
            TargetId target,
            ScopeId targetScope,
            CapabilityRef capability,
            int stratum,
            uint slotIndex,
            OutputSlotSchema slotSchema,
            SlotCompositionPolicy slotPolicy,
            IReadOnlyList<RankedCandidate> eligible,
            IReadOnlyList<ProviderSelectionOverride> overrides,
            IReadOnlyList<ContributionKey> evaluated,
            out EffectiveSlot? composed,
            out CompositionFailure? failure)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (slotSchema == null)
            {
                throw new ArgumentNullException(nameof(slotSchema));
            }

            if (slotPolicy == null)
            {
                throw new ArgumentNullException(nameof(slotPolicy));
            }

            composed = null;
            failure = null;

            if (eligible.Count == 0)
            {
                return true;
            }

            ProviderInstallationId selected;
            List<ProviderSelectionOverride> ambiguous;
            List<ContributionKey> ineligibleMatches;
            OverrideResolution resolution = ResolveOverride(
                snapshot,
                target,
                targetScope,
                capability.Capability,
                overrides,
                eligible,
                evaluated,
                out selected,
                out ambiguous,
                out ineligibleMatches);

            if (resolution == OverrideResolution.Ambiguous)
            {
                failure = new CompositionFailure(
                    DiagnosticCode.CapabilityConflict,
                    target,
                    capability.Capability,
                    slotIndex,
                    KeysOf(eligible),
                    OverrideWitnessKeys(ambiguous),
                    "Two selection overrides name different providers for this slot (" + ambiguous.Count + ").");
                return false;
            }

            if (resolution == OverrideResolution.NotEligible)
            {
                List<Id128> witnesses = new List<Id128>
                {
                    EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("selectProvider", selected.ToString())),
                };

                for (int i = 0; i < ineligibleMatches.Count; i++)
                {
                    witnesses.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("ineligible", ineligibleMatches[i].ToString())));
                }

                witnesses.Sort(Id128Codec.CompareBigEndian);
                failure = new CompositionFailure(
                    DiagnosticCode.MissingDependency,
                    target,
                    capability.Capability,
                    slotIndex,
                    KeysOf(eligible),
                    witnesses,
                    "SelectProvider names a candidate that is not eligible; a missing selection rejects the plan (P-018).");
                return false;
            }

            bool hasSelection = resolution == OverrideResolution.Selected;
            List<FrozenPayload> composedValues = new List<FrozenPayload>();
            List<CapabilityContribution> support = new List<CapabilityContribution>();
            List<CapabilityContribution> shadowed = new List<CapabilityContribution>();

            switch (slotPolicy.Policy)
            {
                case CompositionPolicy.Additive:
                {
                    List<FrozenPayload> inputs = new List<FrozenPayload>(eligible.Count);
                    for (int i = 0; i < eligible.Count; i++)
                    {
                        inputs.Add(eligible[i].Contribution.Payload);
                        support.Add(eligible[i].Contribution);
                    }

                    if (!slotPolicy.Reducer.RegistrationKey.IsDefault)
                    {
                        FrozenPayload? reduced;
                        bool registered;
                        try
                        {
                            registered = values.TryReduce(slotPolicy.Reducer, inputs, out reduced);
                        }
                        catch (ReducerFailureException failureFromReducer)
                        {
                            // P-019: a reducer that validates overflow/range and finds an error rejects the whole
                            // proposal. No truncated or best-effort value is ever emitted.
                            failure = new CompositionFailure(
                                DiagnosticCode.CapabilityConflict,
                                target,
                                capability.Capability,
                                slotIndex,
                                KeysOf(eligible),
                                new[] { slotPolicy.Reducer.RegistrationKey },
                                "The declared reducer rejected its inputs: " + failureFromReducer.Message);
                            return false;
                        }

                        if (!registered || reduced == null)
                        {
                            failure = new CompositionFailure(
                                DiagnosticCode.MissingDependency,
                                target,
                                capability.Capability,
                                slotIndex,
                                KeysOf(eligible),
                                new[] { slotPolicy.Reducer.RegistrationKey },
                                "The declared reducer of this Additive slot is not registered (P-019, P-028).");
                            return false;
                        }

                        composedValues.Add(reduced);
                    }
                    else
                    {
                        composedValues.AddRange(CanonicalUnion(inputs));
                    }

                    break;
                }

                case CompositionPolicy.Replace:
                {
                    int winner = WinnerIndex(eligible, resolution, selected);
                    composedValues.Add(eligible[winner].Contribution.Payload);
                    support.Add(eligible[winner].Contribution);
                    AddShadowed(eligible, winner, shadowed);
                    break;
                }

                case CompositionPolicy.Ordered:
                {
                    List<RankedCandidate> ordered;
                    CompositionFailure? orderFailure;
                    if (!TopologicalOrder(
                            snapshot, target, capability.Capability, slotIndex, eligible, out ordered, out orderFailure))
                    {
                        failure = orderFailure;
                        return false;
                    }

                    for (int i = 0; i < ordered.Count; i++)
                    {
                        composedValues.Add(ordered[i].Contribution.Payload);
                        support.Add(ordered[i].Contribution);
                    }

                    break;
                }

                case CompositionPolicy.Exclusive:
                {
                    if (hasSelection)
                    {
                        int winner = WinnerIndex(eligible, resolution, selected);
                        composedValues.Add(eligible[winner].Contribution.Payload);
                        support.Add(eligible[winner].Contribution);
                        AddShadowed(eligible, winner, shadowed);
                        break;
                    }

                    if (eligible.Count > 1)
                    {
                        failure = new CompositionFailure(
                            DiagnosticCode.CapabilityConflict,
                            target,
                            capability.Capability,
                            slotIndex,
                            KeysOf(eligible),
                            KeysOf(eligible),
                            "Exclusive accepts at most one candidate and no selection override names one; rank does "
                            + "not hide the conflict (P-018, P-019).");
                        return false;
                    }

                    composedValues.Add(eligible[0].Contribution.Payload);
                    support.Add(eligible[0].Contribution);
                    break;
                }

                case CompositionPolicy.Incompatible:
                {
                    // The contract declared this slot incompatible: at most one active member, and no selection
                    // override can dissolve the conflict (P-018 allows SelectProvider for Replace and Exclusive).
                    if (eligible.Count > 1)
                    {
                        failure = new CompositionFailure(
                            DiagnosticCode.CapabilityConflict,
                            target,
                            capability.Capability,
                            slotIndex,
                            KeysOf(eligible),
                            KeysOf(eligible),
                            "Incompatible accepts at most one active member per incompatibility set; priority cannot "
                            + "destroy an incompatible capability (P-019).");
                        return false;
                    }

                    composedValues.Add(eligible[0].Contribution.Payload);
                    support.Add(eligible[0].Contribution);
                    break;
                }

                default:
                {
                    failure = new CompositionFailure(
                        DiagnosticCode.MissingDependency,
                        target,
                        capability.Capability,
                        slotIndex,
                        KeysOf(eligible),
                        null,
                        "Unknown composition policy " + slotPolicy.Policy.ToString() + " (P-019).");
                    return false;
                }
            }

            List<CapabilityContribution> supportFinal = new List<CapabilityContribution>(support.Count);
            for (int i = 0; i < support.Count; i++)
            {
                supportFinal.Add(support[i].Disposition == ContributionDisposition.Active
                    ? support[i]
                    : WithDisposition(support[i], ContributionDisposition.Active));
            }

            List<CapabilityContribution> shadowFinal = new List<CapabilityContribution>(shadowed.Count);
            for (int i = 0; i < shadowed.Count; i++)
            {
                shadowFinal.Add(shadowed[i].Disposition == ContributionDisposition.Shadowed
                    ? shadowed[i]
                    : WithDisposition(shadowed[i], ContributionDisposition.Shadowed));
            }

            composed = new EffectiveSlot(
                target,
                capability.Capability,
                capability.Version,
                stratum,
                slotIndex,
                slotSchema.Schema,
                slotPolicy.Policy,
                composedValues,
                supportFinal,
                shadowFinal,
                SlotHash.Compute(target, capability, slotIndex, slotSchema.Schema, slotPolicy.Policy, composedValues));

            return true;
        }

        /// <summary>
        /// Resolves the override that applies to one (target, capability) slot. Overrides are versioned
        /// composition data: a target-addressed override matches that target, and a scope-addressed override
        /// matches the addressed scope (exact) or, with <c>AppliesToSubtree</c>, that scope or any of its
        /// descendants. Two different providers for one slot are ambiguous, not last-writer-wins.
        /// </summary>
        public static OverrideResolution ResolveOverride(
            DerivationSnapshot snapshot,
            TargetId target,
            ScopeId targetScope,
            CapabilityId capability,
            IReadOnlyList<ProviderSelectionOverride> overrides,
            IReadOnlyList<RankedCandidate> eligible,
            IReadOnlyList<ContributionKey> evaluated,
            out ProviderInstallationId selected,
            out List<ProviderSelectionOverride> ambiguous,
            out List<ContributionKey> ineligibleMatches)
        {
            selected = default(ProviderInstallationId);
            ambiguous = new List<ProviderSelectionOverride>();
            ineligibleMatches = new List<ContributionKey>();
            if (overrides == null || overrides.Count == 0)
            {
                return OverrideResolution.None;
            }

            bool any = false;
            for (int i = 0; i < overrides.Count; i++)
            {
                ProviderSelectionOverride candidate = overrides[i];
                if (!candidate.Capability.Equals(capability))
                {
                    continue;
                }

                if (!OverrideAddresses(snapshot, candidate, target, targetScope))
                {
                    continue;
                }

                if (any && !selected.Equals(candidate.Provider))
                {
                    ambiguous.Add(candidate);
                    continue;
                }

                any = true;
                selected = candidate.Provider;
            }

            if (!any)
            {
                return OverrideResolution.None;
            }

            if (ambiguous.Count > 0)
            {
                return OverrideResolution.Ambiguous;
            }

            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].Key.Provider.Equals(selected))
                {
                    return OverrideResolution.Selected;
                }
            }

            for (int i = 0; i < evaluated.Count; i++)
            {
                if (evaluated[i].Provider.Equals(selected))
                {
                    ineligibleMatches.Add(evaluated[i]);
                }
            }

            return OverrideResolution.NotEligible;
        }

        /// <summary>True when one override addresses this target: by target identity, or by scope membership.</summary>
        public static bool OverrideAddresses(
            DerivationSnapshot snapshot,
            ProviderSelectionOverride item,
            TargetId target,
            ScopeId targetScope)
        {
            if (item.HasTarget && item.AtTarget.Equals(target))
            {
                return true;
            }

            if (!item.HasScope)
            {
                return false;
            }

            if (item.AtScope.Equals(targetScope))
            {
                return true;
            }

            return item.AppliesToSubtree && snapshot.IsSelfOrAncestor(item.AtScope, targetScope);
        }

        /// <summary>
        /// Cross-capability incompatibility of P-019: each contract may name capabilities that cannot be active
        /// together, and the declaration defines a set whose members are interchangeable. At most one member of
        /// each such set may be effective on one target; the conflict is reported with the two provenances and the
        /// whole proposal is rejected. The relation is read as set membership, so a declaration from either side
        /// forms the set: priority can never destroy an incompatible capability by ignoring a one-sided list.
        /// </summary>
        public static bool TryCheckIncompatibility(
            DerivationSnapshot snapshot,
            TargetId target,
            IReadOnlyList<CapabilityId> effectiveCapabilities,
            out CompositionFailure? failure)
        {
            failure = null;
            for (int i = 0; i < effectiveCapabilities.Count; i++)
            {
                CapabilityId left = effectiveCapabilities[i];
                CapabilityContract? contract = snapshot.Contracts.Find(left);
                if (contract == null)
                {
                    continue;
                }

                IReadOnlyList<CapabilityId> incompatible = contract.IncompatibleCapabilities;
                for (int j = 0; j < incompatible.Count; j++)
                {
                    CapabilityId other = incompatible[j];
                    if (other.Equals(left))
                    {
                        continue;
                    }

                    // The declared list is a set membership: one declaration from either side forms the set, so
                    // only the other member's presence has to be checked (P-019).
                    if (!Contains(effectiveCapabilities, other))
                    {
                        continue;
                    }

                    List<Id128> witnesses = new List<Id128>
                    {
                        EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("incompatible", left.ToString())),
                        EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("incompatible", other.ToString())),
                    };
                    witnesses.Sort(Id128Codec.CompareBigEndian);

                    failure = new CompositionFailure(
                        DiagnosticCode.CapabilityConflict,
                        target,
                        left,
                        0U,
                        null,
                        witnesses,
                        "Capabilities " + left.ToString() + " and " + other.ToString()
                        + " belong to one declared incompatibility set and both would be active (P-019).");
                    return false;
                }
            }

            return true;
        }

        private static bool Contains(IReadOnlyList<CapabilityId> capabilities, CapabilityId capability)
        {
            for (int i = 0; i < capabilities.Count; i++)
            {
                if (capabilities[i].Equals(capability))
                {
                    return true;
                }
            }

            return false;
        }

        private static CapabilityContribution WithDisposition(
            CapabilityContribution contribution,
            ContributionDisposition disposition) =>
            new CapabilityContribution(
                contribution.Key,
                contribution.Schema,
                contribution.Policy,
                contribution.Payload,
                contribution.PayloadHash,
                disposition);

        private static void AddShadowed(IReadOnlyList<RankedCandidate> eligible, int winner, List<CapabilityContribution> shadowed)
        {
            for (int i = 0; i < eligible.Count; i++)
            {
                if (i != winner)
                {
                    shadowed.Add(eligible[i].Contribution);
                }
            }
        }

        private static int WinnerIndex(
            IReadOnlyList<RankedCandidate> eligible,
            OverrideResolution resolution,
            ProviderInstallationId selected)
        {
            if (resolution != OverrideResolution.Selected)
            {
                return 0;
            }

            for (int i = 0; i < eligible.Count; i++)
            {
                if (eligible[i].Key.Provider.Equals(selected))
                {
                    return i;
                }
            }

            return 0;
        }

        private static List<FrozenPayload> CanonicalUnion(IReadOnlyList<FrozenPayload> inputs)
        {
            List<FrozenPayload> unique = new List<FrozenPayload>(inputs.Count);
            for (int i = 0; i < inputs.Count; i++)
            {
                bool duplicate = false;
                for (int j = 0; j < unique.Count; j++)
                {
                    if (PayloadEquals(unique[j], inputs[i]))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    unique.Add(inputs[i]);
                }
            }

            unique.Sort(ComparePayloads);
            return unique;
        }

        /// <summary>Byte equality of two payloads; the canonical set-union membership test (P-019).</summary>
        public static bool PayloadEquals(FrozenPayload left, FrozenPayload right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            IReadOnlyList<byte> a = left.Bytes;
            IReadOnlyList<byte> b = right.Bytes;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Byte order of two payloads; the canonical set-union output order (P-008).</summary>
        public static int ComparePayloads(FrozenPayload left, FrozenPayload right)
        {
            IReadOnlyList<byte> a = left.Bytes;
            IReadOnlyList<byte> b = right.Bytes;
            int shared = a.Count < b.Count ? a.Count : b.Count;
            for (int i = 0; i < shared; i++)
            {
                int compare = a[i].CompareTo(b[i]);
                if (compare != 0)
                {
                    return compare;
                }
            }

            return a.Count.CompareTo(b.Count);
        }

        private static bool TopologicalOrder(
            DerivationSnapshot snapshot,
            TargetId target,
            CapabilityId capability,
            uint slot,
            IReadOnlyList<RankedCandidate> eligible,
            out List<RankedCandidate> ordered,
            out CompositionFailure? failure)
        {
            ordered = new List<RankedCandidate>(eligible.Count);
            failure = null;

            Dictionary<Id128, int> bySelfKey = new Dictionary<Id128, int>();
            List<int> duplicateKeys = new List<int>();
            for (int i = 0; i < eligible.Count; i++)
            {
                DerivationRuleKeys? keys = KeysOfRule(snapshot, eligible[i].Key.Rule);
                if (keys == null || !keys.HasSelfKey)
                {
                    continue;
                }

                if (bySelfKey.ContainsKey(keys.SelfKey))
                {
                    duplicateKeys.Add(i);
                    continue;
                }

                bySelfKey.Add(keys.SelfKey, i);
            }

            if (duplicateKeys.Count > 0)
            {
                List<Id128> witnesses = new List<Id128>(duplicateKeys.Count);
                for (int i = 0; i < duplicateKeys.Count; i++)
                {
                    witnesses.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey(
                        "ambiguousKey", eligible[duplicateKeys[i]].Key.ToString())));
                }

                witnesses.Sort(Id128Codec.CompareBigEndian);
                failure = new CompositionFailure(
                    DiagnosticCode.AmbiguousOrder,
                    target,
                    capability,
                    slot,
                    KeysOf(eligible),
                    witnesses,
                    "Two ordered candidates declare the same ordering key (" + duplicateKeys.Count + ").");
                return false;
            }

            List<HashSet<int>> successors = new List<HashSet<int>>(eligible.Count);
            int[] indegree = new int[eligible.Count];
            for (int i = 0; i < eligible.Count; i++)
            {
                successors.Add(new HashSet<int>());
            }

            for (int i = 0; i < eligible.Count; i++)
            {
                DerivationRuleKeys? keys = KeysOfRule(snapshot, eligible[i].Key.Rule);
                if (keys == null)
                {
                    continue;
                }

                for (int e = 0; e < keys.Before.Count; e++)
                {
                    OrderKeyEdge edge = keys.Before[e];
                    int other;
                    if (bySelfKey.TryGetValue(edge.Key, out other))
                    {
                        if (other == i)
                        {
                            failure = SelfCycle(target, capability, slot, eligible, edge);
                            return false;
                        }

                        if (successors[i].Add(other))
                        {
                            indegree[other]++;
                        }

                        continue;
                    }

                    if (edge.Required)
                    {
                        failure = MissingEndpoint(target, capability, slot, eligible, edge);
                        return false;
                    }
                }

                for (int e = 0; e < keys.After.Count; e++)
                {
                    OrderKeyEdge edge = keys.After[e];
                    int other;
                    if (bySelfKey.TryGetValue(edge.Key, out other))
                    {
                        if (other == i)
                        {
                            failure = SelfCycle(target, capability, slot, eligible, edge);
                            return false;
                        }

                        if (successors[other].Add(i))
                        {
                            indegree[i]++;
                        }

                        continue;
                    }

                    if (edge.Required)
                    {
                        failure = MissingEndpoint(target, capability, slot, eligible, edge);
                        return false;
                    }
                }
            }

            // The ready set is ordered by P-018 precedence, which is the declared ready-set tie breaker.
            List<int> ready = new List<int>();
            for (int i = 0; i < indegree.Length; i++)
            {
                if (indegree[i] == 0)
                {
                    ready.Add(i);
                }
            }

            while (ready.Count > 0)
            {
                int pick = 0;
                for (int i = 1; i < ready.Count; i++)
                {
                    if (RankedCandidate.Compare(eligible[ready[i]], eligible[ready[pick]]) < 0)
                    {
                        pick = i;
                    }
                }

                int current = ready[pick];
                ready.RemoveAt(pick);
                ordered.Add(eligible[current]);

                foreach (int successor in successors[current])
                {
                    indegree[successor]--;
                    if (indegree[successor] == 0)
                    {
                        ready.Add(successor);
                    }
                }
            }

            if (ordered.Count != eligible.Count)
            {
                List<Id128> cycle = new List<Id128>();
                for (int i = 0; i < indegree.Length; i++)
                {
                    if (indegree[i] > 0)
                    {
                        cycle.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("cycle", eligible[i].Key.ToString())));
                    }
                }

                cycle.Sort(Id128Codec.CompareBigEndian);
                failure = new CompositionFailure(
                    DiagnosticCode.Cycle,
                    target,
                    capability,
                    slot,
                    KeysOf(eligible),
                    cycle,
                    "Declared before/after keys form a cycle of " + cycle.Count + " candidates (P-019).");
                return false;
            }

            return true;
        }

        private static CompositionFailure SelfCycle(
            TargetId target,
            CapabilityId capability,
            uint slot,
            IReadOnlyList<RankedCandidate> eligible,
            OrderKeyEdge edge) =>
            new CompositionFailure(
                DiagnosticCode.Cycle,
                target,
                capability,
                slot,
                KeysOf(eligible),
                new[] { EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("selfCycle", edge.Key.ToString())) },
                "A candidate declares an ordering key against itself; the smallest cycle witness is that key (P-019).");

        private static CompositionFailure MissingEndpoint(
            TargetId target,
            CapabilityId capability,
            uint slot,
            IReadOnlyList<RankedCandidate> eligible,
            OrderKeyEdge edge) =>
            new CompositionFailure(
                DiagnosticCode.MissingDependency,
                target,
                capability,
                slot,
                KeysOf(eligible),
                new[] { EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("missingOrderKey", edge.Key.ToString())) },
                "A required before/after key has no endpoint in this slot; an optional key would have dropped it (P-019).");

        private static DerivationRuleKeys? KeysOfRule(DerivationSnapshot snapshot, RuleId rule) =>
            snapshot.TryGetRuleKeys(rule, out DerivationRuleKeys? keys) ? keys : null;

        private static List<ContributionKey> KeysOf(IReadOnlyList<RankedCandidate> eligible)
        {
            List<ContributionKey> keys = new List<ContributionKey>(eligible.Count);
            for (int i = 0; i < eligible.Count; i++)
            {
                keys.Add(eligible[i].Key);
            }

            return keys;
        }

        private static IReadOnlyList<Id128> OverrideWitnessKeys(IReadOnlyList<ProviderSelectionOverride> ambiguous)
        {
            List<Id128> keys = new List<Id128>(ambiguous.Count);
            for (int i = 0; i < ambiguous.Count; i++)
            {
                keys.Add(EvidenceKeys.Derive(EvidenceKeys.EvidenceKey("override", ambiguous[i].ToString())));
            }

            keys.Sort(Id128Codec.CompareBigEndian);
            return keys.AsReadOnly();
        }
    }

    /// <summary>Canonical hash of one effective slot: identity plus composed values, never provider identity (P-024).</summary>
    public static class SlotHash
    {
        public static ContentHash Compute(
            TargetId target,
            CapabilityRef capability,
            uint slot,
            SchemaRef schema,
            CompositionPolicy policy,
            IReadOnlyList<FrozenPayload> values)
        {
            StringBuilder text = new StringBuilder();
            text.Append("slot=").Append(target.ToString()).Append('/').Append(capability.ToString())
                .Append('/').Append(slot).Append('/').Append(schema.ToString())
                .Append('/').Append(policy.ToString()).Append('\n');
            for (int i = 0; i < values.Count; i++)
            {
                text.Append("  value=");
                PayloadCodec.AppendCanonical(text, values[i]);
                text.Append('\n');
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(text.ToString()));
        }
    }
}
