// GameCore.Unity.Runtime — publishing derivation provenance into the shared diagnostics store (GC-016).
//
// P-026 is a *runtime* requirement: `Explain(TargetId, CapabilityId, SnapshotToken)` must answer for the published
// epoch of a real world, and O-25's probe says "every effective capability has complete provenance and
// losing/excluded candidate reasons". The pure diagnostics store cannot read a derivation by itself (it is engine-
// free and knows only contracts), so this adapter is the one place where a real `DerivationResult` is projected into
// `ProvenanceStore`:
//
//   * one compact record per candidate decision, carrying the rule, the provider, the output slot, the stratum, the
//     mode gate, the provider depth, the recipe/slot hashes and *keys* into an interned evidence table;
//   * one interned evidence entry per distinct exclusion, capability boundary, missing input, scope-path member,
//     descriptor key, support id and mode-gate decision, keyed by a canonical hash so two runs agree;
//   * the winners of every pair are recorded as winners even when the engine reported no decision for them, so a
//     capability that is effective always explains at least one support (P-017).
//
// Nothing is inferred and nothing is invented: every value comes from the derivation result, the token or the state
// dispositions the caller passes from the real plan. The projection is a pure function of its input, so the retained
// provenance is reconstructable from the same run and comparable across runs (TEST-008's oracle discipline).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition.Diagnostics;
using GameCore.Contracts;
using GameCore.Derivation;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>What one provenance publication recorded, for a gate or a probe to assert on.</summary>
    public sealed class ProvenancePublicationReport
    {
        public ProvenancePublicationReport(
            ProvenancePublishReport publish,
            int explanationCount,
            int pairCount,
            int winnerCount,
            int loserCount,
            int excludedCount,
            int boundaryCount,
            int modeDeniedCount,
            int inputMissingCount,
            int predicateRejectedCount,
            int selectorMismatchCount)
        {
            Publish = publish;
            ExplanationCount = explanationCount;
            PairCount = pairCount;
            WinnerCount = winnerCount;
            LoserCount = loserCount;
            ExcludedCount = excludedCount;
            BoundaryCount = boundaryCount;
            ModeDeniedCount = modeDeniedCount;
            InputMissingCount = inputMissingCount;
            PredicateRejectedCount = predicateRejectedCount;
            SelectorMismatchCount = selectorMismatchCount;
        }

        public ProvenancePublishReport Publish { get; }

        public int ExplanationCount { get; }

        public int PairCount { get; }

        public int WinnerCount { get; }

        public int LoserCount { get; }

        public int ExcludedCount { get; }

        public int BoundaryCount { get; }

        public int ModeDeniedCount { get; }

        public int InputMissingCount { get; }

        public int PredicateRejectedCount { get; }

        public int SelectorMismatchCount { get; }

        public bool Published => Publish.Succeeded;

        public override string ToString() =>
            "Provenance(" + Publish.ToString() + ", pairs=" + PairCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", winners=" + WinnerCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ", losers=" + LoserCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Projects one accepted derivation into retained, reconstructable provenance.</summary>
    public static class DerivationProvenancePublisher
    {
        /// <summary>
        /// Records the provenance of one published derivation at <paramref name="token"/>. The dispositions of the
        /// same publication are attached when the caller has them (a plan carries them), so a page can report state
        /// dispositions next to the winning rules (P-026, P-032).
        /// </summary>
        public static ProvenancePublicationReport Publish(
            DerivationResult derivation,
            SnapshotToken token,
            ProvenanceStore store,
            IReadOnlyList<StateDisposition>? dispositions)
        {
            if (derivation == null)
            {
                throw new ArgumentNullException(nameof(derivation));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var records = new List<CapabilityProvenance>();
            var entries = new Dictionary<Id128, ProvenanceEntry>();
            var pairs = new HashSet<PairIdentity>();
            int winners = 0;
            int losers = 0;
            int excluded = 0;
            int boundaries = 0;
            int modeDenied = 0;
            int inputMissing = 0;
            int predicateRejected = 0;
            int selectorMismatch = 0;

            for (int e = 0; e < derivation.Explanations.Count; e++)
            {
                DerivationExplanation explanation = derivation.Explanations[e];
                var pair = new PairIdentity(explanation.Target, explanation.Capability);
                pairs.Add(pair);

                var winnerKeys = new HashSet<ContributionKey>();
                for (int w = 0; w < explanation.Winners.Count; w++)
                {
                    winnerKeys.Add(explanation.Winners[w].Key);
                }

                var decided = new HashSet<ContributionKey>();
                for (int d = 0; d < explanation.Decisions.Count; d++)
                {
                    CandidateDecision decision = explanation.Decisions[d];
                    ContributionKey key = new ContributionKey(
                        decision.Provider, decision.Rule, decision.Target, decision.Capability, decision.OutputSlot);
                    decided.Add(key);

                    var evidenceKeys = new List<Id128>();
                    CollectDecisionEvidence(explanation, decision, entries, evidenceKeys);
                    ProvenanceKind kind = KindOf(decision, winnerKeys.Contains(key));
                    switch (kind)
                    {
                        case ProvenanceKind.Winner:
                            winners++;
                            break;
                        case ProvenanceKind.Loser:
                            losers++;
                            break;
                        case ProvenanceKind.Excluded:
                            excluded++;
                            break;
                        case ProvenanceKind.BoundaryBlocked:
                            boundaries++;
                            break;
                        case ProvenanceKind.ModeDenied:
                            modeDenied++;
                            break;
                        case ProvenanceKind.InputMissing:
                            inputMissing++;
                            break;
                        case ProvenanceKind.PredicateRejected:
                            predicateRejected++;
                            break;
                        default:
                            selectorMismatch++;
                            break;
                    }

                    records.Add(new CapabilityProvenance(
                        decision.RecordKey,
                        decision.Target,
                        decision.Capability,
                        kind,
                        decision.Diagnostic,
                        decision.Rule,
                        decision.Provider,
                        decision.OutputSlot,
                        decision.Stratum,
                        explanation.Mode,
                        decision.ProviderDepth,
                        explanation.RecipeHash,
                        explanation.SlotHash,
                        evidenceKeys));
                }

                for (int w = 0; w < explanation.Winners.Count; w++)
                {
                    CapabilityContribution winner = explanation.Winners[w];
                    if (decided.Contains(winner.Key))
                    {
                        continue;
                    }

                    // A winner the engine recorded no decision for still explains itself: the support is the record.
                    var evidenceKeys = new List<Id128>();
                    evidenceKeys.Add(Intern(
                        entries,
                        ProvenanceEvidenceKind.Support,
                        winner.Key.Rule.Value,
                        winner.Key.Provider.Value,
                        default(ScopeId),
                        winner.Key.Capability,
                        (int)winner.Key.OutputSlot,
                        "support"));

                    records.Add(new CapabilityProvenance(
                        SyntheticRecordKey(winner.Key),
                        winner.Key.Target,
                        winner.Key.Capability,
                        ProvenanceKind.Winner,
                        DiagnosticCode.None,
                        winner.Key.Rule,
                        winner.Key.Provider,
                        winner.Key.OutputSlot,
                        explanation.Stratum,
                        explanation.Mode,
                        -1,
                        explanation.RecipeHash,
                        explanation.SlotHash,
                        evidenceKeys));
                    winners++;
                }
            }

            var entryList = new List<ProvenanceEntry>(entries.Count);
            foreach (KeyValuePair<Id128, ProvenanceEntry> entry in entries)
            {
                // Dictionary order is never an input: the store sorts records and resolves keys by identity, but a
                // stable entry order keeps the published payload byte-identical across runs (TEST-022).
                entryList.Add(entry.Value);
            }

            entryList.Sort(CompareEntries);
            ProvenancePublishReport publish = store.PublishEpoch(token, records, entryList, dispositions);
            return new ProvenancePublicationReport(
                publish,
                derivation.Explanations.Count,
                pairs.Count,
                winners,
                losers,
                excluded,
                boundaries,
                modeDenied,
                inputMissing,
                predicateRejected,
                selectorMismatch);
        }

        private static ProvenanceKind KindOf(CandidateDecision decision, bool isWinner)
        {
            if (isWinner)
            {
                return ProvenanceKind.Winner;
            }

            switch (decision.Status)
            {
                case CandidateStatus.Shadowed:
                    return ProvenanceKind.Loser;
                case CandidateStatus.Emitted:
                    // Eligible and emitting, yet not part of the effective value: a composed-but-shadowed candidate.
                    return ProvenanceKind.Loser;
                case CandidateStatus.Excluded:
                    return ProvenanceKind.Excluded;
                case CandidateStatus.BlockedByBoundary:
                    return ProvenanceKind.BoundaryBlocked;
                case CandidateStatus.ModeDenied:
                    return ProvenanceKind.ModeDenied;
                case CandidateStatus.InputMissing:
                    return ProvenanceKind.InputMissing;
                case CandidateStatus.PredicateRejected:
                case CandidateStatus.PredicateUnregistered:
                    return ProvenanceKind.PredicateRejected;
                default:
                    return ProvenanceKind.SelectorMismatch;
            }
        }

        private static void CollectDecisionEvidence(
            DerivationExplanation explanation,
            CandidateDecision decision,
            Dictionary<Id128, ProvenanceEntry> entries,
            List<Id128> keys)
        {
            for (int i = 0; i < decision.Exclusions.Count; i++)
            {
                ExclusionEvidence exclusion = decision.Exclusions[i];
                keys.Add(Intern(
                    entries,
                    ProvenanceEvidenceKind.Exclusion,
                    exclusion.Rule.TargetId,
                    exclusion.Rule.AtScope.Value,
                    exclusion.SourceScope,
                    decision.Capability,
                    exclusion.FromScope ? 1 : 0,
                    "exclusion"));
            }

            for (int i = 0; i < decision.Boundaries.Count; i++)
            {
                BoundaryEvidence boundary = decision.Boundaries[i];
                keys.Add(Intern(
                    entries,
                    ProvenanceEvidenceKind.CapabilityBoundary,
                    boundary.BoundaryScope.Value,
                    boundary.Capability.Value,
                    boundary.BoundaryScope,
                    boundary.Capability,
                    boundary.AllContracts ? 1 : 0,
                    "boundary"));
            }

            for (int i = 0; i < decision.MissingInputs.Count; i++)
            {
                CapabilityId missing = decision.MissingInputs[i];
                keys.Add(Intern(
                    entries,
                    ProvenanceEvidenceKind.MissingInput,
                    missing.Value,
                    Id128.Zero,
                    default(ScopeId),
                    missing,
                    0,
                    "missing-input"));
            }

            for (int i = 0; i < decision.EvidenceKeys.Count; i++)
            {
                Id128 evidenceKey = decision.EvidenceKeys[i];
                keys.Add(Intern(
                    entries,
                    ProvenanceEvidenceKind.Descriptor,
                    evidenceKey,
                    Id128.Zero,
                    default(ScopeId),
                    decision.Capability,
                    0,
                    "descriptor"));
            }

            for (int i = 0; i < explanation.ScopePath.Count; i++)
            {
                ScopeId scope = explanation.ScopePath[i];
                keys.Add(Intern(
                    entries,
                    ProvenanceEvidenceKind.Scope,
                    scope.Value,
                    Id128.Zero,
                    scope,
                    decision.Capability,
                    i,
                    "scope-path"));
            }

            keys.Add(Intern(
                entries,
                ProvenanceEvidenceKind.ModeGate,
                decision.Provider.Value,
                decision.Rule.Value,
                decision.ProviderScope,
                decision.Capability,
                (int)decision.ModeGate,
                "mode-gate"));
        }

        /// <summary>
        /// Interns one evidence fact under a canonical key derived from its typed fields, so the same exclusion in
        /// two hundred targets costs one entry and repeated publications of the same world produce the same keys.
        /// </summary>
        private static Id128 Intern(
            Dictionary<Id128, ProvenanceEntry> entries,
            ProvenanceEvidenceKind kind,
            Id128 primary,
            Id128 secondary,
            ScopeId scope,
            CapabilityId capability,
            int number,
            string label)
        {
            var bytes = new List<byte>(64);
            AppendUInt32(bytes, (uint)kind);
            AppendId128(bytes, primary);
            AppendId128(bytes, secondary);
            AppendId128(bytes, scope.Value);
            AppendId128(bytes, capability.Value);
            AppendUInt32(bytes, unchecked((uint)number));
            Id128 key = Id128Codec.ReadBigEndian(ContentHash.Compute(bytes.ToArray()).ToArray(), 0);
            if (!entries.ContainsKey(key))
            {
                entries.Add(key, new ProvenanceEntry(
                    key, kind, primary, secondary, scope, capability, number, label));
            }

            return key;
        }

        /// <summary>Stable identity for a support record the engine did not report as a decision.</summary>
        private static Id128 SyntheticRecordKey(ContributionKey key)
        {
            var bytes = new List<byte>(96);
            AppendUInt32(bytes, 0x53555050U);
            AppendId128(bytes, key.Provider.Value);
            AppendId128(bytes, key.Rule.Value);
            AppendId128(bytes, key.Target.Value);
            AppendId128(bytes, key.Capability.Value);
            AppendUInt32(bytes, key.OutputSlot);
            return Id128Codec.ReadBigEndian(ContentHash.Compute(bytes.ToArray()).ToArray(), 0);
        }

        private static int CompareEntries(ProvenanceEntry left, ProvenanceEntry right) =>
            left.Key.CompareTo(right.Key);

        private static void AppendUInt32(List<byte> destination, uint value)
        {
            destination.Add((byte)(value >> 24));
            destination.Add((byte)(value >> 16));
            destination.Add((byte)(value >> 8));
            destination.Add((byte)value);
        }

        private static void AppendId128(List<byte> destination, Id128 value)
        {
            for (int shift = 56; shift >= 0; shift -= 8)
            {
                destination.Add((byte)(value.High >> shift));
            }

            for (int shift = 56; shift >= 0; shift -= 8)
            {
                destination.Add((byte)(value.Low >> shift));
            }
        }

        /// <summary>Pair identity used only to count distinct explained pairs (never ordering, never identity).</summary>
        private readonly struct PairIdentity : IEquatable<PairIdentity>
        {
            public PairIdentity(TargetId target, CapabilityId capability)
            {
                Target = target;
                Capability = capability;
            }

            public TargetId Target { get; }

            public CapabilityId Capability { get; }

            public bool Equals(PairIdentity other) =>
                Target.Equals(other.Target) && Capability.Equals(other.Capability);

            public override bool Equals(object? obj) => obj is PairIdentity other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Target.GetHashCode() * 397) ^ Capability.GetHashCode();
                }
            }
        }
    }
}
