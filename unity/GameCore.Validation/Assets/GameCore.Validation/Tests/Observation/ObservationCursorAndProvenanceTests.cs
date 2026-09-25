// GameCore.Observation.Tests — GC-016: committed-event cursors, delayed-consumer delivery and reconstructable
// provenance (O-17, O-25, P-026, P-045).
//
// The event observations run against the world's own committed-event store, which the family's message plane feeds
// one committed step at a time. Where a test needs a retention window it fully controls — an expired cursor and the
// continuation after resynchronization — it builds a bounded `CommittedEventStore` and `StepPublicationStore` over
// the same world incarnation and publishes events of the world's own committed-event shape; the resynchronization
// itself is always read from the real host's observation.
//
// The provenance observations run against the real `DerivationResult` the family's pipeline published and the real
// plan it published it with, and every expectation is read back out of `ProvenanceStore`.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition.Diagnostics;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using NUnit.Framework;

namespace GameCore.Observation.Tests
{
    /// <summary>Event cursors, delayed-consumer delivery and provenance reconstruction in real worlds.</summary>
    [TestFixture]
    public sealed class ObservationCursorAndProvenanceTests
    {
        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// A cursor outside retention is an explicit expiry, and resynchronization names the newest retained image
        /// together with the event cursor at that boundary (O-17, P-045).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void EventCursor_ReportsExpiryAndResynchronizesFromTheNewestBoundary(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                world.CommitOneStep();
                world.CommitOneStep();

                WorldObservation observation = world.Host.Observation;
                Assert.That(observation.Events, Is.Not.Null, "a family world declares a committed-event plane");

                var zero = new EventCursor(world.World, EventSequence.Zero);
                CommittedEventPage page = observation.Read(zero, 8);
                Assert.That(page.Outcome, Is.EqualTo(CursorOutcome.Ok), "the zero cursor is always answerable");
                Assert.That(page.Events.Count, Is.GreaterThan(0),
                    "two settled commands must have committed at least one event each (P-044)");
                Assert.That(page.Events.Count, Is.EqualTo(observation.Events!.Count));
                Assert.That(observation.EventPageCount, Is.EqualTo(1));
                Assert.That(observation.NoEventPlaneRefusalCount, Is.EqualTo(0));

                // A cursor beyond the last published sequence is an expiry, not an empty page.
                var beyond = new EventCursor(
                    world.World, new EventSequence(observation.Events.LastSequence.Value + 1UL));
                CommittedEventPage expired = observation.Read(beyond, 8);
                Assert.That(expired.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
                Assert.That(expired.Events.Count, Is.EqualTo(0));
                Assert.That(observation.EventGapReportCount, Is.EqualTo(1));

                SnapshotResynchronization resynchronization = observation.Resynchronize();
                Assert.That(resynchronization.Resynchronized, Is.True);
                Assert.That(resynchronization.Code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(resynchronization.Token.Equals(world.LatestBoundary()), Is.True,
                    "the resynchronizing reader restarts from the newest retained committed image (P-045)");
                Assert.That(resynchronization.Cursor.Equals(observation.LatestEventCursor()), Is.True);
                Assert.That(resynchronization.DroppedEvents, Is.EqualTo(observation.DroppedEventCount));
                Assert.That(resynchronization.RetainedImages, Is.EqualTo(observation.Snapshots.RetainedCount));
                Assert.That(observation.ResyncCount, Is.EqualTo(1));
                Assert.That(observation.ResyncUnavailableCount, Is.EqualTo(0));

                // The boundary token the resynchronization names is really leasable.
                CommittedBoundaryLeaseResult lease = observation.LeaseCommittedBoundary(
                    CommittedBoundaryRequest.At(resynchronization.Token, 8, 0U));
                Assert.That(lease.Outcome, Is.EqualTo(CommittedBoundaryOutcome.Leased), lease.CodeText);
                Assert.That(lease.Lease, Is.Not.Null);
                lease.Lease!.Dispose();

                // A cursor from another world incarnation is refused, never answered from this world's stream.
                var foreign = new EventCursor(
                    new WorldId(new Id128(0x4743303136464F52UL, 0x4549474E574F524CUL)), EventSequence.Zero);
                CommittedEventPage refused = observation.Read(foreign, 8);
                Assert.That(refused.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
                Assert.That(observation.NoEventPlaneRefusalCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// Delivery is at-least-once within retention and deduplicated per consumer: the same committed events are
        /// handed out once, a repeated poll suppresses them by `(world, sequence)`, an expired cursor demands a
        /// resynchronization, and the consumer continues from the boundary afterwards (P-045, TEST-014).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void DelayedConsumerDelivery_DeliversEachCommittedEventOnceAndRecoversAfterExpiry(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                world.CommitOneStep();
                world.CommitOneStep();

                WorldObservation observation = world.Host.Observation;
                var zero = new EventCursor(world.World, EventSequence.Zero);
                CommittedEventPage page = observation.Read(zero, 8);
                Assert.That(page.Outcome, Is.EqualTo(CursorOutcome.Ok));
                Assert.That(page.Events.Count, Is.GreaterThan(0),
                    "the settled commands must have committed real events (P-044)");

                var delivery = new DelayedConsumerDelivery(observation, zero, 8, 8);
                Assert.That(delivery.Cursor.Equals(zero), Is.True);

                DeliveryBatch delivered = delivery.Poll();
                Assert.That(delivered.Disposition, Is.EqualTo(DeliveryDisposition.Delivered));
                Assert.That(delivered.Delivered, Is.True);
                Assert.That(delivered.Events.Count, Is.EqualTo(page.Events.Count));
                Assert.That(delivered.DuplicatesSuppressed, Is.EqualTo(0));
                Assert.That(delivery.DeliveredCount, Is.EqualTo(page.Events.Count));

                // Every identity of that page is already this consumer's: a retry delivers nothing twice.
                DeliveryBatch repeated = delivery.PollFrom(zero);
                Assert.That(repeated.Disposition, Is.EqualTo(DeliveryDisposition.Duplicate));
                Assert.That(repeated.Events.Count, Is.EqualTo(0));
                Assert.That(repeated.DuplicatesSuppressed, Is.EqualTo(page.Events.Count));
                Assert.That(delivery.DuplicateCount, Is.EqualTo(page.Events.Count));
                Assert.That(delivery.DeliveredCount, Is.EqualTo(page.Events.Count));

                // Its own cursor is at the end of the stream: nothing new, and nothing is re-applied.
                DeliveryBatch idle = delivery.Poll();
                Assert.That(idle.Delivered, Is.False);
                Assert.That(idle.Events.Count, Is.EqualTo(0));

                var beyond = new EventCursor(
                    world.World, new EventSequence(observation.Events!.LastSequence.Value + 1UL));
                DeliveryBatch requiresResync = delivery.PollFrom(beyond);
                Assert.That(requiresResync.Disposition, Is.EqualTo(DeliveryDisposition.ResyncRequired));
                Assert.That(requiresResync.Events.Count, Is.EqualTo(0));
                Assert.That(delivery.ResyncRequiredCount, Is.EqualTo(1));

                SnapshotResynchronization resynchronization = delivery.Resynchronize();
                Assert.That(resynchronization.Resynchronized, Is.True);
                Assert.That(delivery.ResyncCount, Is.EqualTo(1));
                Assert.That(delivery.Cursor.Equals(resynchronization.Cursor), Is.True);
                Assert.That(delivery.Cursor.Equals(observation.LatestEventCursor()), Is.True);

                DeliveryBatch afterResync = delivery.Poll();
                Assert.That(afterResync.Delivered, Is.False);
                Assert.That(afterResync.Events.Count, Is.EqualTo(0),
                    "the boundary cursor is the newest committed event, so nothing remains above it");
                Assert.That(delivery.DeliveredWindow, Is.EqualTo(8));
                Assert.That(delivery.MaxEventsPerPage, Is.EqualTo(8));
                Assert.That(afterResync.NextCursor.Equals(delivery.Cursor), Is.True);

                AssertRetentionExpiryAndContinuation(world);
            }
        }

        /// <summary>
        /// Every effective capability of the published derivation reconstructs completely: winners with resolved
        /// evidence, paged records with a window-independent digest, and an explanation page that agrees with the
        /// reconstruction the store serves (O-25, P-026).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void DerivationProvenance_ReconstructsEveryEffectiveCapability(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                DerivedAssemblyReport report = world.LastPlannedReport
                    ?? throw new InvalidOperationException(
                        "the " + family + " harness published no planned assembly to derive provenance for");
                DerivationResult derivation = world.Pipeline.PreviousDerivation
                    ?? throw new InvalidOperationException(
                        "the " + family + " pipeline published no accepted derivation");
                Assert.That(derivation.Accepted, Is.True, "provenance is published for an accepted derivation");
                Assert.That(derivation.Explanations.Count, Is.GreaterThan(0));

                SnapshotToken token = world.LatestBoundary();
                var store = new ProvenanceStore(world.World, 8, 8192, 4096, 8);

                ProvenancePublicationReport published = DerivationProvenancePublisher.Publish(
                    derivation, token, store, report.Plan != null ? report.Plan.Dispositions : null);
                Assert.That(published.Published, Is.True, published.Publish.ToString());
                Assert.That(published.Publish.Outcome, Is.EqualTo(ProvenancePublishOutcome.Published));
                Assert.That(published.Publish.Code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(published.Publish.Token.Equals(token), Is.True);
                Assert.That(published.Publish.RecordCount, Is.GreaterThan(0));
                Assert.That(published.PairCount, Is.GreaterThan(0));
                Assert.That(published.WinnerCount, Is.GreaterThan(0));
                Assert.That(store.EpochCount, Is.EqualTo(1));
                Assert.That(store.DroppedEpochCount, Is.EqualTo(0));
                Assert.That(store.RefusedPublishCount, Is.EqualTo(0));
                Assert.That(store.PairCount(token), Is.EqualTo(published.PairCount));
                Assert.That(store.TryGetEpoch(token, out ProvenanceEpoch? epoch), Is.True);
                Assert.That(epoch, Is.Not.Null);
                Assert.That(epoch!.RecordCount, Is.EqualTo(published.Publish.RecordCount));

                // Every evaluated pair the derivation reports: an explanation with support reconstructs with
                // winners, and one without support reconstructs without any (P-017).
                var visited = new List<Pair>();
                int totalRecords = 0;
                int nonWinnerRecords = 0;
                int pairsWithRejections = 0;
                for (int e = 0; e < derivation.Explanations.Count; e++)
                {
                    DerivationExplanation explanation = derivation.Explanations[e];
                    Assert.That(
                        store.TryReconstruct(
                            token, explanation.Target, explanation.Capability, 0U, 4096U,
                            out ProvenanceReconstruction? whole, out DiagnosticCode wholeCode),
                        Is.True,
                        "target=" + explanation.Target + " capability=" + explanation.Capability
                        + " code=" + wholeCode);
                    Assert.That(wholeCode, Is.EqualTo(DiagnosticCode.None));
                    Assert.That(whole, Is.Not.Null);
                    Assert.That(whole!.TotalRecords, Is.EqualTo((ulong)whole.Records.Count));
                    Assert.That(whole.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
                    Assert.That(whole.Operation.IssuerSequence, Is.EqualTo(0UL),
                        "a published epoch names no staged operation (05 s5)");
                    Assert.That(whole.HasSupport, Is.EqualTo(explanation.Winners.Count > 0),
                        "a pair has effective support exactly when the derivation recorded a winner for it (P-017)");

                    int rejected = whole.Losers.Count + whole.Excluded.Count + whole.BoundaryBlocked.Count
                        + whole.ModeDenied.Count + whole.InputMissing.Count + whole.PredicateRejected.Count
                        + whole.SelectorMismatched.Count + whole.Ineligible.Count;
                    nonWinnerRecords += rejected;
                    if (rejected > 0)
                    {
                        pairsWithRejections++;
                    }

                    totalRecords += whole.Records.Count;
                    visited.Add(new Pair(explanation.Target, explanation.Capability, whole.Records.Count));
                }

                Assert.That(totalRecords, Is.EqualTo(published.Publish.RecordCount),
                    "every retained record belongs to exactly one (target, capability) pair");
                Assert.That(pairsWithRejections, Is.GreaterThanOrEqualTo(1),
                    "the fixtures mount a provider a scope boundary blocks (P-016) or a scope a sibling cannot "
                    + "reach, so at least one pair must report a losing, excluded or boundary-blocked candidate; "
                    + "observed non-winner records=" + nonWinnerRecords);

                // Every effective capability of every target assembly of the derivation.
                int effectivePairs = 0;
                for (int a = 0; a < derivation.Assemblies.Count; a++)
                {
                    TargetAssembly assembly = derivation.Assemblies[a];
                    for (int c = 0; c < assembly.EffectiveCapabilities.Count; c++)
                    {
                        CapabilityId capability = assembly.EffectiveCapabilities[c];
                        effectivePairs++;
                        Assert.That(
                            store.TryReconstruct(
                                token, assembly.Target, capability, 0U, 64U,
                                out ProvenanceReconstruction? pair, out DiagnosticCode code),
                            Is.True,
                            "target=" + assembly.Target + " capability=" + capability + " code=" + code);
                        Assert.That(code, Is.EqualTo(DiagnosticCode.None));
                        Assert.That(pair, Is.Not.Null);
                        Assert.That(pair!.Target.Equals(assembly.Target), Is.True);
                        Assert.That(pair.Capability.Equals(capability), Is.True);
                        Assert.That(pair.HasSupport, Is.True,
                            "an effective capability must explain at least one support (P-017)");
                        Assert.That(pair.Winners.Count, Is.GreaterThanOrEqualTo(1));
                        Assert.That(pair.Evidence.Count, Is.GreaterThan(0),
                            "every reconstructed winner resolves interned evidence (P-026)");

                        Assert.That(assembly.HasCapability(capability), Is.True);
                        List<string> support = SupportKeys(assembly, capability);
                        Assert.That(support.Count, Is.GreaterThanOrEqualTo(1));
                        for (int w = 0; w < pair.Winners.Count; w++)
                        {
                            CapabilityProvenance winner = pair.Winners[w];
                            Assert.That(winner.Kind, Is.EqualTo(ProvenanceKind.Winner));
                            Assert.That(winner.EvidenceKeys.Count, Is.GreaterThan(0));
                            Assert.That(winner.Target.Equals(assembly.Target), Is.True);
                            Assert.That(winner.Capability.Equals(capability), Is.True);
                            Assert.That(support.Contains(Key(winner.Rule, winner.Provider)), Is.True,
                                "winner " + winner.Rule + "@" + winner.Provider
                                + " must be a support contribution of the derivation's own effective slot");
                            Assert.That(pair.EvidenceOf(winner.EvidenceKeys[0]), Is.Not.Null,
                                "a winner's evidence key must resolve inside its own reconstruction");
                        }

                        AssertExplanationPageAgrees(store, pair, assembly.Target, capability, token);
                        AssertPagingWalksToTheEnd(store, pair, assembly.Target, capability, token);
                    }
                }

                Assert.That(effectivePairs, Is.GreaterThanOrEqualTo(1));
                Assert.That(effectivePairs, Is.LessThanOrEqualTo(store.PairCount(token)),
                    "an effective pair is a retained pair of the published epoch");
                Assert.That(store.ReconstructionCount, Is.GreaterThan(0));
                Assert.That(store.ExpiredReconstructionCount, Is.EqualTo(0));
                Assert.That(store.Targets(token).Count, Is.GreaterThan(0));
                Assert.That(store.StagedCount, Is.EqualTo(0));

                // A token the store does not retain is an explicit expiry, never an empty success (P-026).
                SnapshotToken absent = new SnapshotToken(
                    world.World, new AssemblyEpoch(world.Host.CurrentEpoch.Value + 1UL), world.Host.CurrentStep);
                Assert.That(store.TryGetEpoch(absent, out ProvenanceEpoch? _), Is.False);
                Assert.That(
                    store.TryReconstruct(
                        absent, derivation.Explanations[0].Target, derivation.Explanations[0].Capability, 0U, 8U,
                        out ProvenanceReconstruction? none, out DiagnosticCode absentCode),
                    Is.False);
                Assert.That(absentCode, Is.EqualTo(DiagnosticCode.CursorExpired));
                Assert.That(none, Is.Null);
                Assert.That(store.ExpiredReconstructionCount, Is.EqualTo(1));

                // A retained epoch with no records for a pair answers an empty page, not an error: the pair has no
                // provenance because the derivation never evaluated it (P-026).
                Assert.That(
                    store.TryReconstruct(
                        token, derivation.Explanations[0].Target, default(CapabilityId), 0U, 8U,
                        out ProvenanceReconstruction? emptyPair, out DiagnosticCode emptyCode),
                    Is.True);
                Assert.That(emptyCode, Is.EqualTo(DiagnosticCode.None));
                Assert.That(emptyPair, Is.Not.Null);
                Assert.That(emptyPair!.Records.Count, Is.EqualTo(0));
                Assert.That(emptyPair.HasSupport, Is.False);
            }
        }

        /// <summary>
        /// A bounded event store whose zero cursor has already fallen behind retention: the consumer is told to
        /// resynchronize instead of being handed a false continuation, and it delivers the next real event after
        /// resynchronizing (P-045, TEST-014).
        /// </summary>
        private static void AssertRetentionExpiryAndContinuation(ObservationFamilyWorld world)
        {
            var events = new CommittedEventStore(world.World, 2);
            events.Publish(new List<CommittedEvent> { world.CommittedEventOf(1UL, 1UL, Payload(0x11)) });
            events.Publish(new List<CommittedEvent> { world.CommittedEventOf(2UL, 2UL, Payload(0x12)) });
            events.Publish(new List<CommittedEvent> { world.CommittedEventOf(3UL, 3UL, Payload(0x13)) });
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events.FirstRetainedSequence.Value, Is.EqualTo(2UL));
            Assert.That(events.LastSequence.Value, Is.EqualTo(3UL));
            Assert.That(events.DroppedCount, Is.EqualTo(1));

            var snapshots = new StepPublicationStore(world.World, 2, 2);
            Assert.That(snapshots.Publish(world.StepCommitOf(1UL, 1)), Is.True);
            Assert.That(snapshots.Publish(world.StepCommitOf(2UL, 1)), Is.True);
            Assert.That(snapshots.Publish(world.StepCommitOf(3UL, 1)), Is.True);

            var observation = new WorldObservation(snapshots, events, null);
            Assert.That(observation.TryGetLatestBoundary(out SnapshotToken boundary), Is.True);
            Assert.That(boundary.Equals(snapshots.Last!.Token), Is.True);
            Assert.That(observation.Events!.World.Session.Equals(world.World.Session), Is.True);

            var zero = new EventCursor(world.World, EventSequence.Zero);
            CommittedEventPage expired = observation.Read(zero, 8);
            Assert.That(expired.Outcome, Is.EqualTo(CursorOutcome.CursorExpired),
                "the next event the zero cursor needs was already dropped (P-045)");
            Assert.That(observation.EventGapReportCount, Is.EqualTo(1));

            var delivery = new DelayedConsumerDelivery(observation, zero, 4, 8);
            DeliveryBatch first = delivery.Poll();
            Assert.That(first.Disposition, Is.EqualTo(DeliveryDisposition.ResyncRequired));
            Assert.That(delivery.ResyncRequiredCount, Is.EqualTo(1));

            SnapshotResynchronization resynchronization = delivery.Resynchronize();
            Assert.That(resynchronization.Resynchronized, Is.True);
            Assert.That(resynchronization.DroppedEvents, Is.EqualTo(1UL));
            Assert.That(resynchronization.Token.Equals(boundary), Is.True);
            Assert.That(delivery.Cursor.Equals(observation.LatestEventCursor()), Is.True);

            // One more real committed event, then the consumer continues from the boundary it resynchronized to.
            events.Publish(new List<CommittedEvent> { world.CommittedEventOf(4UL, 4UL, Payload(0x14)) });
            DeliveryBatch continued = delivery.Poll();
            Assert.That(continued.Disposition, Is.EqualTo(DeliveryDisposition.Delivered));
            Assert.That(continued.Events.Count, Is.EqualTo(1));
            Assert.That(continued.Events[0].Cursor.Sequence.Value, Is.EqualTo(4UL));
            Assert.That(delivery.DeliveredCount, Is.EqualTo(1));
            Assert.That(delivery.DuplicateCount, Is.EqualTo(0));
        }

        private static void AssertExplanationPageAgrees(
            ProvenanceStore store,
            ProvenanceReconstruction pair,
            TargetId target,
            CapabilityId capability,
            SnapshotToken token)
        {
            var reader = new ProvenanceExplanationReader(store);
            ExplanationPage page = reader.Explain(target, capability, token, ExplanationPageRequest.FirstPage(64U));
            Assert.That(reader.LastCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(reader.PageCount, Is.EqualTo(1));
            Assert.That(reader.ExpiredPageCount, Is.EqualTo(0));
            Assert.That(page.Source, Is.EqualTo(ExplanationSource.PublishedComposition),
                "a published epoch is never reported as a staged plan (05 s5)");
            Assert.That(page.Target.Equals(target), Is.True);
            Assert.That(page.Capability.Equals(capability), Is.True);
            Assert.That(page.Token.Equals(token), Is.True);
            Assert.That(page.Matching.Count, Is.EqualTo(pair.Winners.Count));
            Assert.That(page.Rejected.Count, Is.EqualTo(pair.Page.Count - pair.Winners.Count));
            Assert.That(page.TotalMatching, Is.EqualTo((ulong)pair.Winners.Count));
            Assert.That(page.TotalMatching + page.TotalRejected, Is.EqualTo((ulong)pair.Records.Count));
            Assert.That(page.TotalRejected, Is.EqualTo((ulong)pair.Records.Count - page.TotalMatching));
            Assert.That(page.StateDispositions.Count, Is.EqualTo(pair.StateDispositions.Count));
            Assert.That(page.Matching[0].Rule.Equals(pair.Winners[0].Rule), Is.True);
            Assert.That(page.Matching[0].Provider.Equals(pair.Winners[0].Provider), Is.True);
            Assert.That(page.RecipeHash.Equals(pair.Winners[0].RecipeHash), Is.True,
                "a matching page reports the winning rule's own recipe hash (P-026)");
        }

        private static void AssertPagingWalksToTheEnd(
            ProvenanceStore store,
            ProvenanceReconstruction pair,
            TargetId target,
            CapabilityId capability,
            SnapshotToken token)
        {
            ContentHash digest = store.DigestOf(token, target, capability);
            Assert.That(digest.IsEmpty, Is.False);

            uint offset = 0U;
            int walked = 0;
            int pages = 0;
            while (true)
            {
                Assert.That(
                    store.TryReconstruct(token, target, capability, offset, 1U, out ProvenanceReconstruction? one, out DiagnosticCode code),
                    Is.True);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(one, Is.Not.Null);
                Assert.That(one!.MaxRecords, Is.EqualTo(1U));
                Assert.That(one.Page.Count, Is.EqualTo(1), "a one-record window carries exactly one record");
                Assert.That(one.Records.Count, Is.EqualTo(pair.Records.Count),
                    "the retained record set does not depend on the page window");
                Assert.That(one.Digest.Equals(digest), Is.True,
                    "the digest covers the whole record set, not the page window");
                Assert.That(one.NextOffset, Is.EqualTo(offset + 1U));

                walked += one.Page.Count;
                pages++;
                offset = one.NextOffset;
                if (!one.HasMore)
                {
                    break;
                }

                Assert.That(pages, Is.LessThan(pair.Records.Count + 2), "the page walk must terminate");
            }

            Assert.That(walked, Is.EqualTo(pair.Records.Count));
            Assert.That(pages, Is.EqualTo(pair.Records.Count));
            Assert.That(
                store.TryReconstruct(token, target, capability, 0U, 4096U, out ProvenanceReconstruction? wide, out DiagnosticCode wideCode),
                Is.True);
            Assert.That(wideCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(wide!.Digest.Equals(digest), Is.True);
            Assert.That(wide.Page.Count, Is.EqualTo(wide.Records.Count));
            Assert.That(wide.HasMore, Is.False);
            Assert.That(store.DigestOf(token, target, capability).Equals(digest), Is.True);
        }

        private static List<string> SupportKeys(TargetAssembly assembly, CapabilityId capability)
        {
            var keys = new List<string>();
            for (int s = 0; s < assembly.Slots.Count; s++)
            {
                EffectiveSlot slot = assembly.Slots[s];
                if (!slot.Capability.Equals(capability))
                {
                    continue;
                }

                for (int c = 0; c < slot.Support.Count; c++)
                {
                    string key = Key(slot.Support[c].Rule, slot.Support[c].Provider);
                    if (!keys.Contains(key))
                    {
                        keys.Add(key);
                    }
                }
            }

            return keys;
        }

        private static string Key(RuleId rule, ProviderInstallationId provider)
            => rule.Value.ToString() + "@" + provider.Value.ToString();

        private static FrozenPayload Payload(byte value) => new FrozenPayload(new[] { value });

        /// <summary>One (target, capability) pair with the size of its retained record set.</summary>
        private readonly struct Pair
        {
            public Pair(TargetId target, CapabilityId capability, int records)
            {
                Target = target;
                Capability = capability;
                Records = records;
            }

            public TargetId Target { get; }

            public CapabilityId Capability { get; }

            public int Records { get; }
        }
    }
}
