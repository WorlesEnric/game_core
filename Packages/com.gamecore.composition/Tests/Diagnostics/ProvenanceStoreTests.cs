// GameCore.Composition.Tests.Diagnostics — reconstructable compact provenance and its retention (GC-016).
//
// P-026 allows provenance "records to be interned/compacted but remain reconstructable for the retained epoch".
// These cases pin the compact form's three obligations: a retained record set reconstructs winners, losers and
// exclusions completely; the reconstruction and its digest do not depend on how a producer enumerated its records;
// and retention is bounded and explicit, with a publish refused rather than truncated.
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
    public sealed class ProvenanceStoreTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303136505256UL, 1UL));

        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x4743303136505256UL, 2UL));

        private static readonly TargetId Target = new TargetId(new Id128(0x4743303136544152UL, 1UL));

        private static readonly TargetId SecondTarget = new TargetId(new Id128(0x4743303136544152UL, 2UL));

        private static readonly CapabilityId Capability = new CapabilityId(new Id128(0x4743303136434150UL, 1UL));

        private static readonly RuleId Rule = new RuleId(new Id128(0x474330313652554CUL, 1UL));

        private static readonly RuleId OtherRule = new RuleId(new Id128(0x474330313652554CUL, 2UL));

        private static readonly ProviderInstallationId Provider = new ProviderInstallationId(new Id128(0x474330313650524FUL, 1UL));

        private static readonly ContentHash Recipe = new ContentHash(Filled(RecipeSalt));

        private static readonly ContentHash Slot = new ContentHash(Filled(SlotSalt));

        private const byte RecipeSalt = 0x11;

        private const byte SlotSalt = 0x22;

        private static SnapshotToken Token(ulong step) =>
            new SnapshotToken(World, AssemblyEpoch.First, new LogicalStepId(step));

        private static ProvenanceEntry Evidence(
            ulong ordinal, ProvenanceEvidenceKind kind, TargetId target, CapabilityId capability) =>
            new ProvenanceEntry(
                new Id128(0x4743303136455649UL, ordinal),
                kind,
                target.Value,
                Id128.Zero,
                default(ScopeId),
                capability,
                (int)ordinal,
                "evidence " + ordinal);

        private static CapabilityProvenance Record(
            ProvenanceKind kind,
            Id128 recordKey,
            RuleId rule,
            TargetId target,
            Id128 evidenceKey) =>
            new CapabilityProvenance(
                recordKey,
                target,
                Capability,
                kind,
                kind == ProvenanceKind.Winner ? DiagnosticCode.None : DiagnosticCode.Ineligible,
                rule,
                Provider,
                0U,
                1,
                PropagationMode.Automatic,
                0,
                Recipe,
                Slot,
                new[] { evidenceKey });

        private static ProvenanceStore NewStore(
            int epochRetention = 4, int maxRecords = 16, int maxEntries = 16, int stagedRetention = 2) =>
            new ProvenanceStore(World, epochRetention, maxRecords, maxEntries, stagedRetention);

        [Test]
        public void ACompactRecordSetReconstructsWinnersLosersAndExclusions()
        {
            ProvenanceStore store = NewStore();
            ProvenanceEntry support = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            ProvenanceEntry modeGate = Evidence(2UL, ProvenanceEvidenceKind.ModeGate, Target, Capability);
            ProvenanceEntry exclusion = Evidence(3UL, ProvenanceEvidenceKind.Exclusion, Target, Capability);
            var records = new List<CapabilityProvenance>
            {
                Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, support.Key),
                Record(ProvenanceKind.Loser, new Id128(0x474330313652454BUL, 2UL), OtherRule, Target, modeGate.Key),
                Record(ProvenanceKind.Excluded, new Id128(0x474330313652454BUL, 3UL), OtherRule, Target, exclusion.Key),
            };

            ProvenancePublishReport publish = store.PublishEpoch(
                Token(1UL), records, new[] { support, modeGate, exclusion }, null);
            Assert.That(publish.Outcome, Is.EqualTo(ProvenancePublishOutcome.Published));
            Assert.That(publish.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(publish.RecordCount, Is.EqualTo(3));
            Assert.That(publish.EntryCount, Is.EqualTo(3));
            Assert.That(store.EpochCount, Is.EqualTo(1));
            Assert.That(store.PublishedEpochCount, Is.EqualTo(1));
            Assert.That(store.PairCount(Token(1UL)), Is.EqualTo(1));

            Assert.That(store.TryReconstruct(Token(1UL), Target, Capability, 0U, 16U, out ProvenanceReconstruction? full, out DiagnosticCode code), Is.True);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(full!.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
            Assert.That(full.IsStaged, Is.False);
            Assert.That(full.Records.Count, Is.EqualTo(3));
            Assert.That(full.Winners.Count, Is.EqualTo(1));
            Assert.That(full.Losers.Count, Is.EqualTo(1));
            Assert.That(full.Excluded.Count, Is.EqualTo(1));
            Assert.That(full.HasSupport, Is.True);
            Assert.That(full.Winners[0].Rule.Value, Is.EqualTo(Rule.Value));
            Assert.That(full.Winners[0].RecipeHash, Is.EqualTo(Recipe));
            Assert.That(full.Evidence.Count, Is.EqualTo(3), "Every page record's evidence resolves.");
            Assert.That(full.EvidenceOf(exclusion.Key)!.Kind, Is.EqualTo(ProvenanceEvidenceKind.Exclusion));
            Assert.That(full.EvidenceOf(modeGate.Key)!.Kind, Is.EqualTo(ProvenanceEvidenceKind.ModeGate));
            Assert.That(full.Digest.IsEmpty, Is.False);
            Assert.That(full.Digest, Is.EqualTo(store.DigestOf(Token(1UL), Target, Capability)));

            // Paging is bounded, walkable, and digest-stable: the digest covers the whole pair, never one window.
            Assert.That(store.TryReconstruct(Token(1UL), Target, Capability, 0U, 1U, out ProvenanceReconstruction? page, out DiagnosticCode pageCode), Is.True);
            Assert.That(pageCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(page!.Page.Count, Is.EqualTo(1));
            Assert.That(page.HasMore, Is.True);
            Assert.That(page.NextOffset, Is.EqualTo(1U));
            Assert.That(page.Digest, Is.EqualTo(full.Digest));
            Assert.That(page.TotalRecords, Is.EqualTo(3UL));

            Assert.That(store.TryReconstruct(Token(1UL), Target, Capability, 2U, 16U, out ProvenanceReconstruction? tail, out _), Is.True);
            Assert.That(tail!.Page.Count, Is.EqualTo(1));
            Assert.That(tail.HasMore, Is.False);
            Assert.That(tail.NextOffset, Is.EqualTo(3U));

            Assert.That(store.TryReconstruct(Token(1UL), SecondTarget, Capability, 0U, 16U, out ProvenanceReconstruction? missing, out DiagnosticCode missingCode), Is.True,
                "A retained epoch answers for a pair it has no records for.");
            Assert.That(missing!.Records.Count, Is.Zero);
            Assert.That(missing.HasSupport, Is.False);
            Assert.That(missingCode, Is.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void EnumerationOrderCannotChangeTheRetainedFormOrItsDigest()
        {
            ProvenanceStore store = NewStore();
            ProvenanceEntry support = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            ProvenanceEntry other = Evidence(2UL, ProvenanceEvidenceKind.Descriptor, Target, Capability);
            CapabilityProvenance winner = Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, support.Key);
            CapabilityProvenance loser = Record(ProvenanceKind.Loser, new Id128(0x474330313652454BUL, 2UL), OtherRule, Target, other.Key);

            store.PublishEpoch(Token(1UL), new[] { winner, loser }, new[] { support, other }, null);
            store.PublishEpoch(Token(2UL), new[] { loser, winner }, new[] { other, support }, null);

            Assert.That(store.DigestOf(Token(1UL), Target, Capability), Is.EqualTo(store.DigestOf(Token(2UL), Target, Capability)),
                "The digest is a function of the record set, not of the producer's enumeration order (P-008).");
            Assert.That(store.TryGetEpoch(Token(1UL), out ProvenanceEpoch? first), Is.True);
            Assert.That(store.TryGetEpoch(Token(2UL), out ProvenanceEpoch? second), Is.True);
            Assert.That(first!.Records[0].Kind, Is.EqualTo(ProvenanceKind.Winner), "Records are canonically ordered.");
            Assert.That(second!.Records[0].Kind, Is.EqualTo(ProvenanceKind.Winner));
            Assert.That(first.Digest, Is.EqualTo(second.Digest));
            Assert.That(first.RecordCount, Is.EqualTo(second.RecordCount));
            Assert.That(first.EntryCount, Is.EqualTo(second.EntryCount));
        }

        [Test]
        public void TwoWinnersChangeTheDigestAndTheRecordSet()
        {
            ProvenanceStore store = NewStore();
            ProvenanceEntry one = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            ProvenanceEntry two = Evidence(2UL, ProvenanceEvidenceKind.Support, Target, Capability);
            CapabilityProvenance first = Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, one.Key);

            store.PublishEpoch(Token(1UL), new[] { first }, new[] { one }, null);
            ContentHash single = store.DigestOf(Token(1UL), Target, Capability);

            store.PublishEpoch(
                Token(2UL),
                new[]
                {
                    first,
                    Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 2UL), OtherRule, Target, two.Key),
                },
                new[] { one, two },
                null);

            Assert.That(store.DigestOf(Token(2UL), Target, Capability), Is.Not.EqualTo(single),
                "Shared support is a set, so adding a winner changes the explanation (P-017).");
            Assert.That(store.TryReconstruct(Token(2UL), Target, Capability, 0U, 16U, out ProvenanceReconstruction? shared, out _), Is.True);
            Assert.That(shared!.Winners.Count, Is.EqualTo(2));
        }

        [Test]
        public void RetentionDropsOldEpochsAndReportsTheExpiry()
        {
            ProvenanceStore store = NewStore(epochRetention: 1);
            ProvenanceEntry entry = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            CapabilityProvenance record = Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, entry.Key);

            store.PublishEpoch(Token(1UL), new[] { record }, new[] { entry }, null);
            store.PublishEpoch(Token(2UL), new[] { record }, new[] { entry }, null);

            Assert.That(store.EpochCount, Is.EqualTo(1));
            Assert.That(store.DroppedEpochCount, Is.EqualTo(1));
            Assert.That(store.TryGetEpoch(Token(1UL), out ProvenanceEpoch? dropped), Is.False);
            Assert.That(dropped, Is.Null, "A dropped epoch releases its records and its interned evidence.");
            Assert.That(store.TryReconstruct(Token(1UL), Target, Capability, 0U, 16U, out ProvenanceReconstruction? gone, out DiagnosticCode code), Is.False);
            Assert.That(gone, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.CursorExpired), "Expiry is explicit, never an empty success (P-026).");
            Assert.That(store.ExpiredReconstructionCount, Is.EqualTo(1));
            Assert.That(store.TryReconstruct(Token(2UL), Target, Capability, 0U, 16U, out ProvenanceReconstruction? retained, out _), Is.True);
            Assert.That(retained!.Winners.Count, Is.EqualTo(1));
        }

        [Test]
        public void RepublishingOneTokenReplacesItInPlace()
        {
            ProvenanceStore store = NewStore();
            ProvenanceEntry entry = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            CapabilityProvenance record = Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, entry.Key);

            Assert.That(store.PublishEpoch(Token(1UL), new[] { record }, new[] { entry }, null).Outcome,
                Is.EqualTo(ProvenancePublishOutcome.Published));
            Assert.That(store.PublishEpoch(Token(1UL), new[] { record }, new[] { entry }, null).Outcome,
                Is.EqualTo(ProvenancePublishOutcome.Replaced));
            Assert.That(store.EpochCount, Is.EqualTo(1));
            Assert.That(store.PublishedEpochCount, Is.EqualTo(1), "A replacement is not a second publication.");
        }

        [Test]
        public void ADifferentWorldIncarnationIsRefusedWithoutMutation()
        {
            ProvenanceStore store = NewStore();
            ProvenancePublishReport refused = store.PublishEpoch(
                new SnapshotToken(OtherWorld, AssemblyEpoch.First, LogicalStepId.First), null, null, null);

            Assert.That(refused.Outcome, Is.EqualTo(ProvenancePublishOutcome.Refused));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(refused.Succeeded, Is.False);
            Assert.That(store.EpochCount, Is.Zero);
            Assert.That(store.RefusedPublishCount, Is.EqualTo(1));
        }

        [Test]
        public void UnresolvableEvidenceIsRefusedBeforeAnyMutation()
        {
            ProvenanceStore store = NewStore();
            ProvenanceEntry known = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            CapabilityProvenance dangling = Record(
                ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target,
                new Id128(0x47433031364D4953UL, 9UL));

            ProvenancePublishReport refused = store.PublishEpoch(Token(1UL), new[] { dangling }, new[] { known }, null);

            Assert.That(refused.Outcome, Is.EqualTo(ProvenancePublishOutcome.Refused));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.MissingDependency),
                "A record whose evidence cannot be resolved is never retained (P-026).");
            Assert.That(refused.RecordCount, Is.Zero);
            Assert.That(store.EpochCount, Is.Zero, "A refused publish changes nothing.");
            Assert.That(store.RefusedPublishCount, Is.EqualTo(1));
        }

        [Test]
        public void DeclaredBoundsRefuseInsteadOfTruncating()
        {
            ProvenanceStore store = NewStore(maxRecords: 2, maxEntries: 2);
            ProvenanceEntry one = Evidence(1UL, ProvenanceEvidenceKind.Support, Target, Capability);
            ProvenanceEntry two = Evidence(2UL, ProvenanceEvidenceKind.Support, Target, Capability);
            var records = new List<CapabilityProvenance>
            {
                Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 1UL), Rule, Target, one.Key),
                Record(ProvenanceKind.Winner, new Id128(0x474330313652454BUL, 2UL), OtherRule, Target, two.Key),
                Record(ProvenanceKind.Loser, new Id128(0x474330313652454BUL, 3UL), OtherRule, Target, one.Key),
            };

            ProvenancePublishReport tooManyRecords = store.PublishEpoch(Token(1UL), records, new[] { one, two }, null);
            Assert.That(tooManyRecords.Outcome, Is.EqualTo(ProvenancePublishOutcome.Refused));
            Assert.That(tooManyRecords.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(store.EpochCount, Is.Zero, "A truncated provenance set would misreport what the world decided.");

            ProvenancePublishReport tooManyEntries = store.PublishEpoch(
                Token(2UL),
                new[] { records[0] },
                new[] { one, two, Evidence(3UL, ProvenanceEvidenceKind.Descriptor, Target, Capability) },
                null);
            Assert.That(tooManyEntries.Outcome, Is.EqualTo(ProvenancePublishOutcome.Refused));
            Assert.That(tooManyEntries.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(store.EpochCount, Is.Zero);
            Assert.That(store.RefusedPublishCount, Is.EqualTo(2));
        }

        [Test]
        public void StagedProvenanceIsLabeledSeparatelyAndReleasable()
        {
            ProvenanceStore store = NewStore(stagedRetention: 1);
            ProvenanceEntry entry = Evidence(1UL, ProvenanceEvidenceKind.ModeGate, Target, Capability);
            CapabilityProvenance record = Record(ProvenanceKind.ModeDenied, new Id128(0x474330313652454BUL, 1UL), Rule, Target, entry.Key);
            OperationId operation = new OperationId(World, new Id128(0x4743303136495353UL, 1UL), 1UL);

            Assert.That(store.PublishStaged(operation, new[] { record }, new[] { entry }, null).Outcome,
                Is.EqualTo(ProvenancePublishOutcome.Published));
            Assert.That(store.StagedCount, Is.EqualTo(1));
            Assert.That(store.TryGetStaged(operation, out ProvenanceEpoch? staged), Is.True);
            Assert.That(staged!.IsStaged, Is.True);
            Assert.That(staged.Token, Is.EqualTo(default(SnapshotToken)));

            Assert.That(store.TryReconstructStaged(operation, Target, Capability, 0U, 16U, out ProvenanceReconstruction? reconstruction, out DiagnosticCode code), Is.True);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(reconstruction!.IsStaged, Is.True, "A staged plan is never reported as world observation (05 s5).");
            Assert.That(reconstruction.Source, Is.EqualTo(ExplanationSource.StagedPlan));
            Assert.That(reconstruction.Operation, Is.EqualTo(operation));
            Assert.That(reconstruction.ModeDenied.Count, Is.EqualTo(1));
            Assert.That(reconstruction.HasSupport, Is.False, "A denied candidate is not effective support.");
            Assert.That(store.DigestOfStaged(operation, Target, Capability), Is.EqualTo(reconstruction.Digest));
            Assert.That(store.PairCount(Token(1UL)), Is.Zero, "A staged operation is not a published epoch.");

            Assert.That(store.DropStaged(operation), Is.True);
            Assert.That(store.DropStaged(operation), Is.False);
            Assert.That(store.TryReconstructStaged(operation, Target, Capability, 0U, 16U, out ProvenanceReconstruction? dropped, out DiagnosticCode droppedCode), Is.False);
            Assert.That(dropped, Is.Null);
            Assert.That(droppedCode, Is.EqualTo(DiagnosticCode.CursorExpired));

            OperationId second = new OperationId(World, new Id128(0x4743303136495353UL, 1UL), 2UL);
            store.PublishStaged(operation, new[] { record }, new[] { entry }, null);
            store.PublishStaged(second, new[] { record }, new[] { entry }, null);
            Assert.That(store.StagedCount, Is.EqualTo(1), "Staged retention is bounded.");
            Assert.That(store.DroppedStagedCount, Is.EqualTo(1));

            ProvenancePublishReport foreign = store.PublishStaged(
                new OperationId(OtherWorld, new Id128(0x4743303136495353UL, 1UL), 1UL), null, null, null);
            Assert.That(foreign.Outcome, Is.EqualTo(ProvenancePublishOutcome.Refused));
            Assert.That(foreign.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
        }

        [Test]
        public void InvalidationAndZeroRecordPublishesAreRecordedExplicitly()
        {
            ProvenanceStore store = NewStore();
            Assert.That(store.PublishEpoch(Token(1UL), null, null, null).RecordCount, Is.Zero);
            Assert.That(store.EpochCount, Is.EqualTo(1), "An epoch with no provenance is recorded as such, not guessed at.");
            Assert.That(store.TryReconstruct(Token(1UL), Target, Capability, 0U, 16U, out ProvenanceReconstruction? empty, out DiagnosticCode code), Is.True);
            Assert.That(empty!.Records.Count, Is.Zero);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(empty.Digest, Is.EqualTo(ProvenanceDigest.Of(new List<CapabilityProvenance>())));
            Assert.That(store.Targets(Token(1UL)), Is.Empty);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStore(World, 0, 1, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStore(World, 1, 0, 1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStore(World, 1, 1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProvenanceStore(World, 1, 1, 1, 0));
        }

        private static byte[] Filled(byte value)
        {
            var bytes = new byte[ContentHash.SizeInBytes];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = value;
            }

            return bytes;
        }
    }
}
