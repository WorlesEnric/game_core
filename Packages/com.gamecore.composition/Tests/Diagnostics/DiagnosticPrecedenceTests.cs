// GameCore.Composition.Tests.Diagnostics — structured diagnostic payloads and their precedence (GC-016).
//
// P-052 requires a stable code, identity, phase, involved ids, counts/budgets and retry classification, and states
// that logging never changes precedence. These cases pin exactly that: identity and order are functions of typed
// fields, so rewording a diagnostic, formatting it or permuting a list cannot move it.
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
    public sealed class DiagnosticPrecedenceTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303136444941UL, 1UL));

        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x4743303136444941UL, 2UL));

        private static OperationId Operation(ulong sequence, WorldId? world = null) =>
            new OperationId(world ?? World, new Id128(0x4743303136444F50UL, 1UL), sequence);

        private static DiagnosticEnvelope Envelope(
            DiagnosticCode code,
            OperationPhase phase,
            ulong sequence,
            int involvedIds,
            string summary,
            RetryClassification retry = RetryClassification.NotRetryable)
        {
            var ids = new List<Id128>();
            for (int i = 0; i < involvedIds; i++)
            {
                ids.Add(new Id128(0x4743303136444944UL, (ulong)(i + 1)));
            }

            OperationId operation = Operation(sequence);
            DiagnosticEnvelope unkeyed = new DiagnosticEnvelope(
                DiagnosticKey.None, code, phase, operation, ContentHash.Empty, ids, null,
                involvedIds, 0L, retry, summary);
            return new DiagnosticEnvelope(
                DiagnosticKey.FromCanonical(unkeyed.CanonicalHash()), code, phase, operation, ContentHash.Empty,
                ids, null, involvedIds, 0L, retry, summary);
        }

        [Test]
        public void WordingNeverChangesIdentityOrPosition()
        {
            DiagnosticEnvelope first = Envelope(DiagnosticCode.StalePlan, OperationPhase.Validation, 7UL, 2, "revision moved");
            DiagnosticEnvelope reworded = Envelope(DiagnosticCode.StalePlan, OperationPhase.Validation, 7UL, 2, "the plan was checked against another revision");

            Assert.That(DiagnosticPrecedence.SamePosition(first, reworded), Is.True,
                "Only typed fields decide precedence, so wording cannot move a diagnostic (P-052).");
            Assert.That(DiagnosticPrecedence.Compare(first, reworded), Is.Zero);
            Assert.That(first.Key, Is.EqualTo(reworded.Key));
            Assert.That(first.CanonicalHash(), Is.EqualTo(reworded.CanonicalHash()));
            Assert.That(first.Format(), Is.Not.EqualTo(reworded.Format()), "The formatted text really does differ.");
            Assert.That(first.CodeText, Is.EqualTo("StalePlan"), "The stable literal is the code's own text.");
        }

        [Test]
        public void TypedFieldsDriveTheTotalOrder()
        {
            DiagnosticEnvelope planning = Envelope(DiagnosticCode.CapabilityConflict, OperationPhase.Planning, 1UL, 1, "a");
            DiagnosticEnvelope validationEarly = Envelope(DiagnosticCode.StalePlan, OperationPhase.Validation, 9UL, 1, "b");
            DiagnosticEnvelope validationLate = Envelope(DiagnosticCode.StalePlan, OperationPhase.Validation, 10UL, 1, "c");
            DiagnosticEnvelope validationSameOperationOtherCode =
                Envelope(DiagnosticCode.Ineligible, OperationPhase.Validation, 9UL, 1, "d");

            var forward = new List<DiagnosticEnvelope> { planning, validationLate, validationSameOperationOtherCode, validationEarly };
            var backward = new List<DiagnosticEnvelope> { validationEarly, validationSameOperationOtherCode, validationLate, planning };

            IReadOnlyList<DiagnosticEnvelope> orderedForward = DiagnosticPrecedence.Order(forward);
            IReadOnlyList<DiagnosticEnvelope> orderedBackward = DiagnosticPrecedence.Order(backward);

            Assert.That(Keys(orderedForward), Is.EqualTo(Keys(orderedBackward)),
                "Two permutations of the same diagnostics order identically.");
            Assert.That(orderedForward[0].Phase, Is.EqualTo(OperationPhase.Validation), "Phase orders first.");
            Assert.That(orderedForward[0].Code, Is.EqualTo(DiagnosticCode.Ineligible), "Code orders inside the phase.");
            Assert.That(orderedForward[3].Phase, Is.EqualTo(OperationPhase.Planning));
        }

        [Test]
        public void OrderIsStableWhenEverySummaryIsRewritten()
        {
            var original = new List<DiagnosticEnvelope>
            {
                Envelope(DiagnosticCode.BudgetExceeded, OperationPhase.Planning, 2UL, 3, "one"),
                Envelope(DiagnosticCode.Cycle, OperationPhase.Validation, 1UL, 1, "two"),
                Envelope(DiagnosticCode.MissingDependency, OperationPhase.Preparation, 4UL, 0, "three"),
            };
            var reworded = new List<DiagnosticEnvelope>
            {
                Envelope(DiagnosticCode.BudgetExceeded, OperationPhase.Planning, 2UL, 3, "alpha"),
                Envelope(DiagnosticCode.Cycle, OperationPhase.Validation, 1UL, 1, "beta"),
                Envelope(DiagnosticCode.MissingDependency, OperationPhase.Preparation, 4UL, 0, "gamma"),
            };

            Assert.That(Keys(DiagnosticPrecedence.Order(original)), Is.EqualTo(Keys(DiagnosticPrecedence.Order(reworded))));
            Assert.That(DiagnosticPrecedence.Order(null), Is.Empty);
            Assert.That(DiagnosticPrecedence.Order(new List<DiagnosticEnvelope>()), Is.Empty);
            Assert.Throws<ArgumentNullException>(() => DiagnosticPrecedence.Compare(null!, original[0]));
            Assert.Throws<ArgumentNullException>(() => DiagnosticPrecedence.Compare(original[0], null!));
        }

        [Test]
        public void DifferentOperationIdentitiesAndWorldsOrderDeterministically()
        {
            DiagnosticEnvelope first = Envelope(DiagnosticCode.ApplyFault, OperationPhase.Apply, 1UL, 0, "a");
            DiagnosticEnvelope second = Envelope(DiagnosticCode.ApplyFault, OperationPhase.Apply, 2UL, 0, "b");
            Assert.That(DiagnosticPrecedence.Compare(first, second), Is.LessThan(0));

            DiagnosticEnvelope otherWorld = new DiagnosticEnvelope(
                DiagnosticKey.None,
                DiagnosticCode.ApplyFault,
                OperationPhase.Apply,
                Operation(1UL, OtherWorld),
                ContentHash.Empty,
                null,
                null,
                0L,
                0L,
                RetryClassification.NotRetryable,
                "a");
            Assert.That(DiagnosticPrecedence.Compare(first, otherWorld), Is.Not.Zero,
                "A foreign world incarnation is never the same position.");
        }

        [Test]
        public void RegistryInternsBoundsAndOrdersItsPayloads()
        {
            var registry = new DiagnosticRegistry(3);
            Diagnostic first = Diagnostic.Create(DiagnosticCode.StaleHandle, OperationPhase.Validation, Operation(1UL), "first");
            Diagnostic sameFactDifferentWording =
                Diagnostic.Create(DiagnosticCode.StaleHandle, OperationPhase.Validation, Operation(1UL), "reworded");

            DiagnosticKey key = registry.Register(first);
            DiagnosticKey again = registry.Register(sameFactDifferentWording);

            Assert.That(again, Is.EqualTo(key), "The same typed fact interns to one key (P-052).");
            Assert.That(registry.RegisteredCount, Is.EqualTo(2));
            Assert.That(registry.InternedCount, Is.EqualTo(1));
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.TryGet(key, out DiagnosticEnvelope? stored), Is.True);
            Assert.That(stored!.Summary, Is.EqualTo("first"), "The first registration is the retained one.");

            for (ulong sequence = 2UL; sequence <= 5UL; sequence++)
            {
                registry.Register(Diagnostic.Create(
                    DiagnosticCode.StaleHandle, OperationPhase.Validation, Operation(sequence), "record " + sequence));
            }

            Assert.That(registry.Count, Is.EqualTo(3), "Retention is bounded.");
            Assert.That(registry.DroppedCount, Is.EqualTo(2), "Drops are counted, never silent.");
            Assert.That(registry.TryGet(key, out DiagnosticEnvelope? dropped), Is.False);
            Assert.That(dropped, Is.Null);
            Assert.That(registry.Entries().Count, Is.EqualTo(3));
            Assert.That(registry.OrderedEntries().Count, Is.EqualTo(3));

            IReadOnlyList<DiagnosticEnvelope> ordered = registry.OrderedEntries();
            for (int i = 1; i < ordered.Count; i++)
            {
                Assert.That(DiagnosticPrecedence.Compare(ordered[i - 1], ordered[i]), Is.LessThanOrEqualTo(0));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticRegistry(0));
        }

        [Test]
        public void FeedRecordsPublicationAndRejectionPayloadsUnderRetrievalKeys()
        {
            var registry = new DiagnosticRegistry(16);
            var feed = new CompositionDiagnosticFeed(registry, 8);
            OperationId publishedOperation = Operation(11UL);
            ContentHash planHash = new ContentHash(new byte[ContentHash.SizeInBytes]);
            var published = new CompositionPublishedEvent(
                publishedOperation,
                new CompositionRevision(3UL),
                new CompositionRevision(4UL),
                new AssemblyEpoch(3UL),
                new AssemblyEpoch(4UL),
                planHash,
                new AffectedCounts(2, 1, 3, 0, 1));

            PublicationDiagnostic publication = feed.RecordPublished(published);
            Assert.That(publication.Key.IsNone, Is.False);
            Assert.That(publication.Operation.Equals(publishedOperation), Is.True);
            Assert.That(publication.OldRevision.Value, Is.EqualTo(3UL));
            Assert.That(publication.NewRevision.Value, Is.EqualTo(4UL));
            Assert.That(publication.OldEpoch.Value, Is.EqualTo(3UL));
            Assert.That(publication.NewEpoch.Value, Is.EqualTo(4UL));
            Assert.That(publication.Counts.Targets, Is.EqualTo(2));
            Assert.That(publication.Counts.Stages, Is.EqualTo(1));
            Assert.That(feed.TryGetPublication(publication.Key, out PublicationDiagnostic? found), Is.True);
            Assert.That(found!.PlanHash, Is.EqualTo(planHash));
            Assert.That(feed.RecordPublished(published).Key, Is.EqualTo(publication.Key), "The same event interns to one record.");
            Assert.That(feed.PublicationCount, Is.EqualTo(2));
            Assert.That(feed.RecordCount, Is.EqualTo(1));

            OperationId rejectedOperation = Operation(12UL);
            var diagnostics = new List<Diagnostic>
            {
                Diagnostic.Create(DiagnosticCode.CapabilityConflict, OperationPhase.Planning, rejectedOperation, "two exclusive providers"),
            };
            var rejected = new CompositionRejectedEvent(rejectedOperation, planHash, diagnostics);
            RejectionDiagnostic rejection = feed.RecordRejected(rejected);

            Assert.That(rejection.Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(rejection.CodeText, Is.EqualTo("CapabilityConflict"));
            Assert.That(rejection.DiagnosticKeys.Count, Is.EqualTo(1));
            Assert.That(rejection.DiagnosticCodes[0], Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(registry.TryGet(rejection.DiagnosticKeys[0], out DiagnosticEnvelope? envelope), Is.True);
            Assert.That(envelope!.Phase, Is.EqualTo(OperationPhase.Planning));
            Assert.That(feed.TryGetRejection(rejection.Key, out RejectionDiagnostic? rejectedFound), Is.True);
            Assert.That(rejectedFound!.Operation.Equals(rejectedOperation), Is.True);
            Assert.That(feed.RejectionCount, Is.EqualTo(1));
            Assert.That(feed.Publications().Count, Is.EqualTo(1));
            Assert.That(feed.Rejections().Count, Is.EqualTo(1));

            ICompositionObserver observer = feed;
            observer.OnCompositionPublished(published);
            observer.OnCompositionRejected(rejected);
            Assert.That(feed.PublicationCount, Is.EqualTo(3));
            Assert.That(feed.RejectionCount, Is.EqualTo(2));
            Assert.That(feed.ToString(), Does.Contain("publications=3"));

            Assert.Throws<ArgumentNullException>(() => feed.RecordPublished(null!));
            Assert.Throws<ArgumentNullException>(() => feed.RecordRejected(null!));
            Assert.Throws<ArgumentNullException>(() => new CompositionDiagnosticFeed(null!, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompositionDiagnosticFeed(registry, 0));
        }

        [Test]
        public void FeedRetentionDropsTheOldestRecordAndCountsIt()
        {
            var feed = new CompositionDiagnosticFeed(new DiagnosticRegistry(8), 1);
            ContentHash planHash = new ContentHash(new byte[ContentHash.SizeInBytes]);

            for (ulong sequence = 1UL; sequence <= 3UL; sequence++)
            {
                OperationId operation = Operation(sequence);
                feed.RecordPublished(new CompositionPublishedEvent(
                    operation,
                    new CompositionRevision(sequence),
                    new CompositionRevision(sequence + 1UL),
                    new AssemblyEpoch(sequence),
                    new AssemblyEpoch(sequence + 1UL),
                    planHash,
                    new AffectedCounts(1, 0, 0, 0, 0)));
            }

            Assert.That(feed.PublicationCount, Is.EqualTo(3));
            Assert.That(feed.RecordCount, Is.EqualTo(1), "Records are bounded.");
            Assert.That(feed.DroppedRecordCount, Is.EqualTo(2));
            Assert.That(feed.Publications().Count, Is.EqualTo(1));
        }

        private static IReadOnlyList<DiagnosticKey> Keys(IReadOnlyList<DiagnosticEnvelope> envelopes)
        {
            var keys = new List<DiagnosticKey>(envelopes.Count);
            for (int i = 0; i < envelopes.Count; i++)
            {
                keys.Add(envelopes[i].Key);
            }

            return keys;
        }
    }
}
