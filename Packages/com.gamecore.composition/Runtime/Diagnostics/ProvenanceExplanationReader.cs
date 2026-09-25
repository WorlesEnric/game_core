// GameCore.Composition.Diagnostics — the shared explanation paging surface over retained provenance (GC-016).
//
// `IExplanationReader` and `IStagedPlanDiagnostics` are the frozen W0 seam names for this lookup (05 s5), and this
// reader is their production implementation for GC-016's stored provenance:
//
//   * `Explain` answers for a published token and `ReadStaged` for a still-unpublished operation. They are separate
//     methods with separate labels, so staged diagnostics can never be mistaken for world observation (00 s9);
//   * the page carries the winners as matching records and everything else (losers, exclusions, boundaries,
//     mode-gate denials, missing inputs, predicate rejections, selector mismatches, ineligibility) as rejected ones,
//     each with its stable code and its interned evidence keys;
//   * `TryReconstruct` is the same lookup with an explicit outcome, for a caller that must distinguish "this epoch
//     is no longer retained" from "this pair has no records";
//   * an expired or unknown lookup is a counted, empty page rather than an exception, because a diagnostics surface
//     is read by operators and must not take a caller down (P-051: queries are pure).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Composition.Diagnostics
{
    /// <summary>
    /// Bounded, read-only explanation view over one world's retained provenance. It never mutates the store, the
    /// world or a plan, and it never mixes a published epoch with a staged operation.
    /// </summary>
    public sealed class ProvenanceExplanationReader : IExplanationReader, IStagedPlanDiagnostics
    {
        private readonly ProvenanceStore store;

        public ProvenanceExplanationReader(ProvenanceStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public ProvenanceStore Store => store;

        /// <summary>Pages answered for a retained epoch or staged operation.</summary>
        public int PageCount { get; private set; }

        /// <summary>Lookups whose epoch or operation is outside retention: reported, never an empty success.</summary>
        public int ExpiredPageCount { get; private set; }

        /// <summary>Staged lookups for an operation the store never recorded.</summary>
        public int UnknownStagedOperationCount { get; private set; }

        /// <summary>Requests with a zero record bound, which is answered as an empty page and counted.</summary>
        public int InvalidPageRequestCount { get; private set; }

        /// <summary>Outcome of the most recent lookup: `None` only when it produced a retained page.</summary>
        public DiagnosticCode LastCode { get; private set; }

        /// <summary>Published-epoch page: matching winners, rejected losers/exclusions, interned evidence keys (P-026).</summary>
        public ExplanationPage Explain(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            ExplanationPageRequest page)
        {
            if (page.MaxRecords == 0U)
            {
                InvalidPageRequestCount++;
                LastCode = DiagnosticCode.UnsupportedVersion;
                return EmptyPage(target, capability, token, ExplanationSource.PublishedComposition, page);
            }

            if (!store.TryReconstruct(token, target, capability, page.Offset, page.MaxRecords, out ProvenanceReconstruction? reconstruction, out DiagnosticCode code) ||
                reconstruction == null)
            {
                ExpiredPageCount++;
                LastCode = code;
                return EmptyPage(target, capability, token, ExplanationSource.PublishedComposition, page);
            }

            PageCount++;
            LastCode = DiagnosticCode.None;
            return BuildPage(reconstruction, token, page);
        }

        /// <summary>Staged-plan page: the same shape, labelled `StagedPlan`, for an operation that has not published.</summary>
        public ExplanationPage ReadStaged(
            OperationId operation,
            TargetId target,
            CapabilityId capability,
            ExplanationPageRequest page)
        {
            if (page.MaxRecords == 0U)
            {
                InvalidPageRequestCount++;
                LastCode = DiagnosticCode.UnsupportedVersion;
                return EmptyPage(target, capability, default(SnapshotToken), ExplanationSource.StagedPlan, page);
            }

            if (!store.TryReconstructStaged(operation, target, capability, page.Offset, page.MaxRecords, out ProvenanceReconstruction? reconstruction, out DiagnosticCode code) ||
                reconstruction == null)
            {
                UnknownStagedOperationCount++;
                LastCode = code;
                return EmptyPage(target, capability, default(SnapshotToken), ExplanationSource.StagedPlan, page);
            }

            PageCount++;
            LastCode = DiagnosticCode.None;
            return BuildPage(reconstruction, default(SnapshotToken), page);
        }

        /// <summary>The full reconstruction of one published pair, with an explicit outcome instead of an empty page.</summary>
        public bool TryReconstruct(
            SnapshotToken token,
            TargetId target,
            CapabilityId capability,
            uint offset,
            uint maxRecords,
            out ProvenanceReconstruction? reconstruction,
            out DiagnosticCode code) =>
            store.TryReconstruct(token, target, capability, offset, maxRecords, out reconstruction, out code);

        /// <summary>The full reconstruction of one staged pair, with an explicit outcome.</summary>
        public bool TryReconstructStaged(
            OperationId operation,
            TargetId target,
            CapabilityId capability,
            uint offset,
            uint maxRecords,
            out ProvenanceReconstruction? reconstruction,
            out DiagnosticCode code) =>
            store.TryReconstructStaged(operation, target, capability, offset, maxRecords, out reconstruction, out code);

        private static ExplanationPage BuildPage(
            ProvenanceReconstruction reconstruction, SnapshotToken token, ExplanationPageRequest page)
        {
            var matching = new List<ExplanationRecord>();
            var rejected = new List<ExplanationRecord>();
            IReadOnlyList<CapabilityProvenance> records = reconstruction.Page;
            for (int i = 0; i < records.Count; i++)
            {
                CapabilityProvenance record = records[i];
                var explanationRecord = new ExplanationRecord(
                    record.RecordKey, record.Rule, record.Provider, record.Code, record.EvidenceKeys);
                if (record.Kind == ProvenanceKind.Winner)
                {
                    matching.Add(explanationRecord);
                }
                else
                {
                    rejected.Add(explanationRecord);
                }
            }

            ulong totalMatching = 0UL;
            for (int i = 0; i < reconstruction.Records.Count; i++)
            {
                if (reconstruction.Records[i].Kind == ProvenanceKind.Winner)
                {
                    totalMatching++;
                }
            }

            ulong totalRejected = (ulong)reconstruction.Records.Count - totalMatching;
            return new ExplanationPage(
                reconstruction.Target,
                reconstruction.Capability,
                token,
                reconstruction.Source,
                page,
                matching,
                rejected,
                null,
                reconstruction.Winners.Count > 0 ? reconstruction.Winners[0].Mode : FirstMode(reconstruction),
                reconstruction.Winners.Count > 0 ? reconstruction.Winners[0].Stratum : FirstStratum(reconstruction),
                reconstruction.Winners.Count > 0 ? reconstruction.Winners[0].RecipeHash : ContentHash.Empty,
                reconstruction.StateDispositions,
                totalMatching,
                totalRejected);
        }

        private static ExplanationPage EmptyPage(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            ExplanationSource source,
            ExplanationPageRequest page) =>
            new ExplanationPage(
                target,
                capability,
                token,
                source,
                page,
                null,
                null,
                null,
                PropagationMode.Automatic,
                -1,
                ContentHash.Empty,
                null,
                0UL,
                0UL);

        private static PropagationMode FirstMode(ProvenanceReconstruction reconstruction) =>
            reconstruction.Records.Count > 0 ? reconstruction.Records[0].Mode : PropagationMode.Automatic;

        private static int FirstStratum(ProvenanceReconstruction reconstruction) =>
            reconstruction.Records.Count > 0 ? reconstruction.Records[0].Stratum : -1;

        public override string ToString() =>
            "ProvenanceExplanationReader(pages=" + PageCount.ToString(CultureInfo.InvariantCulture)
            + ", expired=" + ExpiredPageCount.ToString(CultureInfo.InvariantCulture)
            + ", unknownStaged=" + UnknownStagedOperationCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
