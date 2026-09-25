// GameCore.Derivation — publishing derivation provenance through the shared explanation contract (GC-006).
//
// P-026: `Explain(TargetId, CapabilityId, SnapshotToken)` returns matching and rejected rules, source scope path,
// descriptor evidence, exclusions/boundaries, mode gate, stratum, candidates, composition decisions, support ids,
// resulting recipe hash and state disposition. Records may be interned/compacted but stay reconstructable for the
// retained epoch, and diagnostics retain no unbounded string tree per entity.
//
// `IExplanationReader` (00 s5, GC-004's contract) is the shared shape, so this adapter lets a later wave publish
// derivation provenance without inventing a second explanation interface. Records are projected on demand and
// paged: an explanation of a target with many rules never materialises as one unbounded object.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>
    /// Read-only explanation view over one derivation result. The token identifies the epoch the records belong
    /// to, so a caller cannot read staged provenance as published composition (05 s5).
    /// </summary>
    public sealed class DerivationExplainReader : IExplanationReader
    {
        private readonly DerivationResult result;
        private readonly ExplanationSource source;

        public DerivationExplainReader(DerivationResult result, ExplanationSource source)
        {
            this.result = result ?? throw new ArgumentNullException(nameof(result));
            this.source = source;
        }

        /// <summary>A reader over a published derivation (the only source a gameplay caller may use).</summary>
        public static DerivationExplainReader Published(DerivationResult result) =>
            new DerivationExplainReader(result, ExplanationSource.PublishedComposition);

        /// <summary>A reader over staged plan diagnostics: labelled distinctly, never world observation (05 s5).</summary>
        public static DerivationExplainReader Staged(DerivationResult result) =>
            new DerivationExplainReader(result, ExplanationSource.StagedPlan);

        /// <summary>Bounded page of matching and rejected records for the requested (target, capability) pair.</summary>
        public ExplanationPage Explain(
            TargetId target,
            CapabilityId capability,
            SnapshotToken token,
            ExplanationPageRequest page)
        {
            if (page.MaxRecords == 0U)
            {
                throw new ArgumentOutOfRangeException(nameof(page), "An explanation page request is bounded and positive (05 s5).");
            }

            DerivationExplanation? explanation = result.ExplanationOf(target, capability);
            if (explanation == null)
            {
                return new ExplanationPage(
                    target,
                    capability,
                    token,
                    source,
                    page,
                    null,
                    null,
                    null,
                    result.Snapshot.Mode,
                    -1,
                    ContentHash.Empty,
                    null,
                    0UL,
                    0UL);
            }

            List<ExplanationRecord> matching = new List<ExplanationRecord>();
            List<ExplanationRecord> rejected = new List<ExplanationRecord>();
            for (int i = 0; i < explanation.Decisions.Count; i++)
            {
                CandidateDecision decision = explanation.Decisions[i];
                ExplanationRecord record = new ExplanationRecord(
                    decision.RecordKey,
                    decision.Rule,
                    decision.Provider,
                    decision.Diagnostic,
                    decision.EvidenceKeys);
                if (decision.Matched)
                {
                    matching.Add(record);
                }
                else
                {
                    rejected.Add(record);
                }
            }

            // The page cursor indexes the concatenation (matching then rejected), which is exactly what the
            // shared page shape's NextOffset walks, so a caller can page without out-of-band knowledge.
            List<ExplanationRecord> matchingPage = new List<ExplanationRecord>();
            List<ExplanationRecord> rejectedPage = new List<ExplanationRecord>();
            int start = (int)page.Offset;
            int budget = (int)page.MaxRecords;
            int totalMatching = matching.Count;
            int totalRejected = rejected.Count;
            int total = totalMatching + totalRejected;
            for (int index = start; index < total && matchingPage.Count + rejectedPage.Count < budget; index++)
            {
                if (index < matching.Count)
                {
                    matchingPage.Add(matching[index]);
                }
                else
                {
                    rejectedPage.Add(rejected[index - matching.Count]);
                }
            }

            return new ExplanationPage(
                target,
                capability,
                token,
                source,
                page,
                matchingPage,
                rejectedPage,
                explanation.ScopePath,
                explanation.Mode,
                explanation.Stratum,
                explanation.RecipeHash,
                null,
                (ulong)totalMatching,
                (ulong)totalRejected);
        }
    }
}
