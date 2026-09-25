// GameCore.Composition.Tests.Diagnostics — the explanation paging surface over retained provenance (GC-016).
//
// `IExplanationReader.Explain` and `IStagedPlanDiagnostics.ReadStaged` are the frozen W0 seam names (05 s5). These
// cases pin that the production implementation answers both, labels them distinctly, pages deterministically, and
// reports an expired epoch or an unknown staged operation instead of an empty success.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Composition.Diagnostics;
using NUnit.Framework;

namespace GameCore.Composition.Tests.Diagnostics
{
    [TestFixture]
    public sealed class ProvenanceExplanationReaderTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303136455850UL, 1UL));

        private static readonly TargetId Target = new TargetId(new Id128(0x4743303136544754UL, 1UL));

        private static readonly CapabilityId Capability = new CapabilityId(new Id128(0x4743303136435058UL, 1UL));

        private static readonly RuleId Rule = new RuleId(new Id128(0x4743303136524C58UL, 1UL));

        private static readonly RuleId OtherRule = new RuleId(new Id128(0x4743303136524C58UL, 2UL));

        private static readonly ProviderInstallationId Provider = new ProviderInstallationId(new Id128(0x4743303136505258UL, 1UL));

        private static SnapshotToken Token(ulong step) =>
            new SnapshotToken(World, AssemblyEpoch.First, new LogicalStepId(step));

        private static ProvenanceEntry Evidence(ulong ordinal, ProvenanceEvidenceKind kind) =>
            new ProvenanceEntry(
                new Id128(0x4743303136455658UL, ordinal),
                kind,
                Target.Value,
                Id128.Zero,
                default(ScopeId),
                Capability,
                (int)ordinal,
                "evidence " + ordinal);

        private static CapabilityProvenance Record(
            ProvenanceKind kind, ulong ordinal, RuleId rule, Id128 evidenceKey) =>
            new CapabilityProvenance(
                new Id128(0x4743303136524558UL, ordinal),
                Target,
                Capability,
                kind,
                kind == ProvenanceKind.Winner ? DiagnosticCode.None : DiagnosticCode.Ineligible,
                rule,
                Provider,
                0U,
                2,
                PropagationMode.Conservative,
                1,
                new ContentHash(new byte[ContentHash.SizeInBytes]),
                new ContentHash(new byte[ContentHash.SizeInBytes]),
                new[] { evidenceKey });

        private static ProvenanceStore SeededStore()
        {
            var store = new ProvenanceStore(World, 4, 16, 16, 2);
            ProvenanceEntry support = Evidence(1UL, ProvenanceEvidenceKind.Support);
            ProvenanceEntry gate = Evidence(2UL, ProvenanceEvidenceKind.ModeGate);
            ProvenanceEntry exclusion = Evidence(3UL, ProvenanceEvidenceKind.Exclusion);
            store.PublishEpoch(
                Token(1UL),
                new[]
                {
                    Record(ProvenanceKind.Winner, 1UL, Rule, support.Key),
                    Record(ProvenanceKind.ModeDenied, 2UL, OtherRule, gate.Key),
                    Record(ProvenanceKind.Excluded, 3UL, OtherRule, exclusion.Key),
                },
                new[] { support, gate, exclusion },
                null);
            return store;
        }

        [Test]
        public void ExplainAnswersTheFrozenSeamWithWinnersLosersAndKeys()
        {
            ProvenanceStore store = SeededStore();
            var reader = new ProvenanceExplanationReader(store);

            ExplanationPage page = reader.Explain(Target, Capability, Token(1UL), ExplanationPageRequest.FirstPage(16U));
            Assert.That(page.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
            Assert.That(page.Token, Is.EqualTo(Token(1UL)));
            Assert.That(page.Target, Is.EqualTo(Target));
            Assert.That(page.Capability, Is.EqualTo(Capability));
            Assert.That(page.Matching.Count, Is.EqualTo(1), "The winner is the matching record (P-026).");
            Assert.That(page.Rejected.Count, Is.EqualTo(2), "Losers and exclusions are rejected records with their codes.");
            Assert.That(page.Matching[0].Rule.Value, Is.EqualTo(Rule.Value));
            Assert.That(page.Matching[0].Rejection, Is.EqualTo(DiagnosticCode.None));
            Assert.That(page.Matching[0].Provider.Value, Is.EqualTo(Provider.Value));
            Assert.That(page.Matching[0].EvidenceKeys.Count, Is.EqualTo(1));
            Assert.That(page.Rejected[0].Rejection, Is.EqualTo(DiagnosticCode.Ineligible));
            Assert.That(page.TotalMatching, Is.EqualTo(1UL));
            Assert.That(page.TotalRejected, Is.EqualTo(2UL));
            Assert.That(page.HasMore, Is.False);
            Assert.That(page.Stratum, Is.EqualTo(2), "Stratum travels with the explanation (P-021).");
            Assert.That(page.Mode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(page.NextOffset, Is.EqualTo(3U));
            Assert.That(reader.PageCount, Is.EqualTo(1));
            Assert.That(reader.LastCode, Is.EqualTo(DiagnosticCode.None));

            ExplanationPage firstPage = reader.Explain(Target, Capability, Token(1UL), ExplanationPageRequest.FirstPage(2U));
            Assert.That(firstPage.Matching.Count, Is.EqualTo(1));
            Assert.That(firstPage.Rejected.Count, Is.EqualTo(1));
            Assert.That(firstPage.HasMore, Is.True);
            Assert.That(firstPage.NextOffset, Is.EqualTo(2U));

            ExplanationPage secondPage = reader.Explain(
                Target, Capability, Token(1UL), new ExplanationPageRequest(firstPage.NextOffset, 2U));
            Assert.That(secondPage.Matching.Count, Is.Zero);
            Assert.That(secondPage.Rejected.Count, Is.EqualTo(1));
            Assert.That(secondPage.HasMore, Is.False);
            Assert.That(secondPage.TotalMatching, Is.EqualTo(1UL), "Totals describe the pair, not the window.");
        }

        [Test]
        public void AnExpiredEpochOrUnknownStagedOperationIsReportedAndCounted()
        {
            ProvenanceStore store = SeededStore();
            var reader = new ProvenanceExplanationReader(store);

            ExplanationPage expired = reader.Explain(Target, Capability, Token(7UL), ExplanationPageRequest.FirstPage(4U));
            Assert.That(expired.Matching.Count, Is.Zero);
            Assert.That(expired.Rejected.Count, Is.Zero);
            Assert.That(expired.TotalMatching, Is.Zero);
            Assert.That(expired.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
            Assert.That(reader.ExpiredPageCount, Is.EqualTo(1));
            Assert.That(reader.LastCode, Is.EqualTo(DiagnosticCode.CursorExpired));

            var unknown = new OperationId(World, new Id128(0x4743303136495850UL, 1UL), 1UL);
            ExplanationPage staged = reader.ReadStaged(unknown, Target, Capability, ExplanationPageRequest.FirstPage(4U));
            Assert.That(staged.Source, Is.EqualTo(ExplanationSource.StagedPlan));
            Assert.That(staged.Matching.Count, Is.Zero);
            Assert.That(reader.UnknownStagedOperationCount, Is.EqualTo(1));
            Assert.That(reader.LastCode, Is.EqualTo(DiagnosticCode.CursorExpired));

            ExplanationPage invalid = reader.Explain(Target, Capability, Token(1UL), new ExplanationPageRequest(0U, 0U));
            Assert.That(invalid.Matching.Count, Is.Zero);
            Assert.That(invalid.Rejected.Count, Is.Zero);
            Assert.That(reader.InvalidPageRequestCount, Is.EqualTo(1));
            Assert.That(reader.LastCode, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(reader.PageCount, Is.EqualTo(1), "Only the first lookup produced a retained page.");
            Assert.That(reader.Store, Is.SameAs(store));
            Assert.Throws<ArgumentNullException>(() => new ProvenanceExplanationReader(null!));
        }

        [Test]
        public void StagedPagesAreLabeledAndMatchTheirReconstruction()
        {
            var store = new ProvenanceStore(World, 4, 16, 16, 2);
            ProvenanceEntry gate = Evidence(1UL, ProvenanceEvidenceKind.ModeGate);
            var operation = new OperationId(World, new Id128(0x4743303136495853UL, 1UL), 1UL);
            store.PublishStaged(
                operation,
                new[] { Record(ProvenanceKind.ModeDenied, 1UL, Rule, gate.Key) },
                new[] { gate },
                null);

            var reader = new ProvenanceExplanationReader(store);
            ExplanationPage page = reader.ReadStaged(
                operation, Target, Capability, ExplanationPageRequest.FirstPage(4U));

            Assert.That(page.Source, Is.EqualTo(ExplanationSource.StagedPlan),
                "A staged plan is labelled distinctly from published observation (00 s9).");
            Assert.That(page.Token, Is.EqualTo(default(SnapshotToken)));
            Assert.That(page.Matching.Count, Is.Zero, "A mode-denied candidate is not effective support.");
            Assert.That(page.Rejected.Count, Is.EqualTo(1));
            Assert.That(page.Rejected[0].Rejection, Is.EqualTo(DiagnosticCode.Ineligible));
            Assert.That(page.TotalRejected, Is.EqualTo(1UL));
            Assert.That(reader.PageCount, Is.EqualTo(1));
            Assert.That(reader.UnknownStagedOperationCount, Is.Zero);

            Assert.That(
                reader.TryReconstructStaged(operation, Target, Capability, 0U, 4U, out ProvenanceReconstruction? reconstruction, out DiagnosticCode code),
                Is.True);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(reconstruction!.Source, Is.EqualTo(ExplanationSource.StagedPlan));
            Assert.That(reconstruction.ModeDenied.Count, Is.EqualTo(1));
            Assert.That(reconstruction.EvidenceOf(gate.Key)!.Kind, Is.EqualTo(ProvenanceEvidenceKind.ModeGate));

            Assert.That(
                reader.TryReconstruct(Token(1UL), Target, Capability, 0U, 4U, out ProvenanceReconstruction? none, out DiagnosticCode missing),
                Is.False);
            Assert.That(none, Is.Null);
            Assert.That(missing, Is.EqualTo(DiagnosticCode.CursorExpired));
        }

        [Test]
        public void ExplanationRecordsCarryTheStoreCanonicalOrder()
        {
            ProvenanceStore store = SeededStore();
            var reader = new ProvenanceExplanationReader(store);

            ExplanationPage page = reader.Explain(Target, Capability, Token(1UL), ExplanationPageRequest.FirstPage(16U));
            Assert.That(reader.ToString(), Does.Contain("pages=1"));

            var keys = new List<Id128>();
            for (int i = 0; i < page.Matching.Count; i++)
            {
                keys.Add(page.Matching[i].RecordKey);
            }

            for (int i = 0; i < page.Rejected.Count; i++)
            {
                keys.Add(page.Rejected[i].RecordKey);
            }

            Assert.That(keys.Count, Is.EqualTo(3));
            Assert.That(keys[0], Is.EqualTo(new Id128(0x4743303136524558UL, 1UL)),
                "Winners come first and records keep the store's canonical order.");
        }
    }
}
