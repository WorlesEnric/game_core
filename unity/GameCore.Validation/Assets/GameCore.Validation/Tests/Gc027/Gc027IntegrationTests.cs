#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Recovery;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameCore.Gc027.Tests
{
    /// <summary>
    /// GC-027 checkpoint-and-durable-delivery recovery, EditMode half
    /// (docs/game-core/09-implementation-guide.md, GC-027 — "Prove checkpoint and durable-delivery recovery under
    /// faults").
    ///
    /// The scenario (`Gc027Scenario` over `Gc027NarrativeHost` / `Gc027CardsHost`) is shared with the standalone
    /// player probe (`-probeRecovery`), and it runs only real modules: the family's own world created from its
    /// generated registration, GC-004's `CompositionHost` and its control lane, GC-006's derivation, GC-008's planner
    /// and publisher, GC-009's schedule compiler and temporal driver, the engine-free delivery seam
    /// (`DurableOutbox`, `DurableDeliveryAdapter`, its journals and its boundary hook), the world-side
    /// `WorldDeliveryOwner`, the Unity persistence half (`UnityCommittedBoundaryReader`, `CheckpointCapture`, the
    /// generated `CheckpointCatalog` serializers through `Gc018CheckpointCodecs`, `CheckpointRestorePlanner`,
    /// `CheckpointRestoreExecutor`), GC-027's own `WorldRecovery` composition and this change set's engine-free store
    /// and policy — and the real fault latch of the world it faults (GC-017's `AssemblyFaultInjection` through its
    /// mechanism, so every reach is compiled out of a release build).
    ///
    /// Each case runs the whole sequence once per family over the committed generated catalog and asserts its own named
    /// observation against that run. The run is cached, so a case never pays for a run another case already made;
    /// nothing is weakened by that, because a failed run fails every case that reads it, and the recorded observation
    /// order and count are asserted before any single observation is read.
    /// </summary>
    [TestFixture]
    [Timeout(1800000)]
    public sealed class Gc027IntegrationTests
    {
        private static readonly Dictionary<string, Gc027ScenarioResult> Runs = new Dictionary<string, Gc027ScenarioResult>();

        [TearDown]
        public void TearDown()
        {
            // A scenario disposes every world it created; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ the eight injection points

        /// <summary>
        /// The source world at a committed boundary: the copy produced a verified document, the header records the
        /// world's one open obligation, and an idle command-driven world committed no step (O-20, P-036, P-053).
        /// </summary>
        [Test]
        public void TheSourceWorldCapturesAndPublishesAVerifiedCheckpoint()
        {
            AssertObservation("gc027-source-world-captures-and-publishes-a-verified-checkpoint");
        }

        /// <summary>
        /// Injection point 1 of 8: a fault while the boundary is being copied produces no checkpoint at all, leaves the
        /// previously stored document intact and leaves the captured world running (O-20, P-049).
        /// </summary>
        [Test]
        public void CaptureCopyFaultProducesNoCheckpoint()
        {
            AssertObservation("gc027-capture-copy-fault-produces-no-checkpoint");
        }

        /// <summary>
        /// Injection point 2 of 8: a fault during file publication leaves the previously verified document as the
        /// stored one, readable, with no partial artifact and a running world (06 s7, O-20).
        /// </summary>
        [Test]
        public void PublicationFaultKeepsThePreviousDocument()
        {
            AssertObservation("gc027-publication-fault-keeps-the-previous-document");
        }

        /// <summary>
        /// Injection point 3 of 8: a fault while the destination's references are being repaired means the destination
        /// was never built, so no incomplete world can become the running one — TEST-016's checkpoint half of its last
        /// row (P-049).
        /// </summary>
        [Test]
        public void ReferenceRepairFaultNeverBuildsADestination()
        {
            AssertObservation("gc027-reference-repair-fault-never-builds-a-destination");
        }

        /// <summary>
        /// Injection point 4 of 8: a fault after the staging world was written to destroys it instead of publishing
        /// it: no epoch, revision or session becomes reachable, and the source stays faulted (P-031, P-049).
        /// </summary>
        [Test]
        public void PostwriteApplyFaultNeverExposesADestination()
        {
            AssertObservation("gc027-postwrite-apply-fault-never-exposes-a-destination");
        }

        /// <summary>
        /// Injection point 5 of 8: a fault as the validated world is about to be published leaves the registry exactly
        /// as it was, so no observer sees a mixture of old and new assembly (P-030, P-049).
        /// </summary>
        [Test]
        public void RecoveryPublicationFaultKeepsTheRegistryUnchanged()
        {
            AssertObservation("gc027-recovery-publication-fault-keeps-the-registry-unchanged");
        }

        /// <summary>
        /// Injection point 6 of 8: an obligation is made durable before it is handed over, so a fault at the append
        /// boundary refuses the commit rather than delivering something no journal holds (P-045).
        /// </summary>
        [Test]
        public void OutboxAppendFaultRefusesBeforeDelivery()
        {
            AssertObservation("gc027-outbox-append-fault-refuses-before-delivery");
        }

        /// <summary>
        /// Injection point 7 of 8: a fault after the destination was asked and before the attempt was recorded is the
        /// acknowledgement-loss window; a redelivery reuses the idempotency key, so the destination effect is applied
        /// exactly once (P-045).
        /// </summary>
        [Test]
        public void OutboxDeliveryFaultRedeliversWithOneDestinationEffect()
        {
            AssertObservation("gc027-outbox-delivery-fault-redelivers-with-one-destination-effect");
        }

        /// <summary>
        /// Injection point 8 of 8: a fault before the acknowledgement leaves the obligation redeliverable; a fault
        /// after it leaves the obligation settled with no destination effect lost or duplicated (P-045).
        /// </summary>
        [Test]
        public void OutboxAcknowledgementFaultRecordsOrRedeliversOnce()
        {
            AssertObservation("gc027-outbox-acknowledgement-fault-records-or-redelivers-once");
        }

        /// <summary>
        /// The restart point: a new session is built from the verified bytes alone, the previous session is not
        /// contacted, and the reinstated obligation is owned rather than delivered (P-049, P-053).
        /// </summary>
        [Test]
        public void RestartFromTheStoreRecoversWithoutInProcessState()
        {
            AssertObservation("gc027-restart-from-the-store-recovers-without-in-process-state");
        }

        /// <summary>
        /// The restart's refusal half: an absent document and an incompatible catalog each produce no world at all, so
        /// an incomplete destination is never exposed (P-049, P-054).
        /// </summary>
        [Test]
        public void RestartWithoutADocumentExposesNothing()
        {
            AssertObservation("gc027-restart-without-a-document-or-incompatible-content-exposes-nothing");
        }

        /// <summary>
        /// P-049's host-configured bounded retry: a retryable transient failure is retried under the configured bound
        /// with a new session and a new operation id, and the same failure is final once the bound is exhausted
        /// (P-049, P-050).
        /// </summary>
        [Test]
        public void TransientFailureIsRetriedUnderTheHostBound()
        {
            AssertObservation("gc027-transient-failure-is-retried-under-the-host-bound");
        }

        // ------------------------------------------------------------------ the recovered world

        /// <summary>
        /// The clean recovery: a new session is published with the checkpoint's state, the old faulted world is
        /// stopped and never resumed, and O-22's result is one new world (O-22, P-049).
        /// </summary>
        [Test]
        public void RecoveryPublishesANewSessionWithTheCapturedState()
        {
            AssertObservation("gc027-recovery-publishes-a-new-session-with-the-captured-state");
        }

        /// <summary>
        /// The recovered world carries the same stable target identities in a different native index block, and the
        /// retired session is no longer registered (P-004, P-005).
        /// </summary>
        [Test]
        public void RestoredWorldUsesDifferentNativeHandles()
        {
            AssertObservation("gc027-restored-world-uses-different-native-handles");
        }

        /// <summary>
        /// Active and dormant authoritative state both survive into the new session, and a dormant row stays dormant
        /// rather than being reactivated (P-032, P-053).
        /// </summary>
        [Test]
        public void ActiveAndDormantStateSurviveTheRecovery()
        {
            AssertObservation("gc027-active-and-dormant-state-survive-the-recovery");
        }

        /// <summary>
        /// The delivery obligation and its cursor are owned by the new session, the executor proved the reinstated
        /// rows before exposure, and the recovery delivered nothing: no hidden external replay (P-045, P-049, P-053).
        /// </summary>
        [Test]
        public void OutboxRowsAndDeliveryCursorSurviveTheRecovery()
        {
            AssertObservation("gc027-outbox-rows-and-delivery-cursor-survive-the-recovery");
        }

        /// <summary>
        /// Every world the run created is stopped and disposed, so no session outlives the run (P-035, P-048).
        /// </summary>
        [Test]
        public void TeardownDisposesEveryWorld()
        {
            AssertObservation("gc027-teardown-disposes-every-world");
        }

        // ------------------------------------------------------------------ the published table

        /// <summary>
        /// The observation table, its length, its qualification and the two digest literals the probe and the harness
        /// both assert are computed from the table, not read from a run: a renamed, reordered, added or dropped
        /// observation changes the literal this test expects, so the qualification cannot silently shrink (P-008).
        /// </summary>
        [Test]
        public void TheRecoveryObservationTableIsExactlyThePublishedSequence()
        {
            // The full table is what the runner can record for a genre declaring every capability; each family then
            // records the subset its own declarations allow, and its digest pins that subset (P-008, P-045, P-054).
            Assert.That(Gc027Scenario.ObservationNames.Length, Is.EqualTo(21),
                "the recovery runner's full observation table is twenty-one names");

            IGc027Family narrative = Gc013NarrativeHost.RecoveryFamily();
            IGc027Family cards = Gc013CardsHost.RecoveryFamily();
            IGc027Family traversal = Gc020TraversalHost.RecoveryFamily();

            string[] narrativeNames = Gc027Scenario.ExpectedNames(narrative);
            string[] cardNames = Gc027Scenario.ExpectedNames(cards);
            string[] traversalNames = Gc027Scenario.ExpectedNames(traversal);

            Assert.That(narrativeNames.Length, Is.EqualTo(17),
                "a genre with a delivery obligation and no engine domain records seventeen observations");
            Assert.That(cardNames.Length, Is.EqualTo(17));
            Assert.That(traversalNames.Length, Is.EqualTo(17),
                "the traversal course declares no delivery obligation and an engine domain, so its table swaps four "
                + "observations for four others and stays seventeen long");
            Assert.That(narrativeNames[0],
                Is.EqualTo(Gc013NarrativeHost.Label + "/" + Gc027Scenario.ObservationNames[0]));
            Assert.That(traversalNames, Does.Contain(Gc020TraversalHost.Label
                + "/gc027-recovered-engine-physics-is-reseeded-not-continued"));
            Assert.That(traversalNames, Does.Not.Contain(Gc020TraversalHost.Label
                + "/gc027-outbox-append-fault-refuses-before-delivery"));

            var narrativeResult = new Gc027ScenarioResult(Gc013NarrativeHost.Label, PassingSteps(narrativeNames));
            var cardsResult = new Gc027ScenarioResult(Gc013CardsHost.Label, PassingSteps(cardNames));
            var traversalResult = new Gc027ScenarioResult(Gc020TraversalHost.Label, PassingSteps(traversalNames));

            AssertDigest(ProbeRecovery.NarrativeDigest, narrativeResult.Digest, "narrative");
            AssertDigest(ProbeRecovery.CardsDigest, cardsResult.Digest, "cards");
            AssertDigest(ProbeRecovery.TraversalDigest, traversalResult.Digest, "traversal");
            Assert.That(narrativeResult.Digest, Is.Not.EqualTo(cardsResult.Digest),
                "the two families must not share one literal, or a family could report the other's run");
            Assert.That(traversalResult.Digest, Is.Not.EqualTo(narrativeResult.Digest));
            Assert.That(traversalResult.Digest, Is.Not.EqualTo(cardsResult.Digest));
        }

        /// <summary>
        /// The traversal course runs the whole recovery sequence too, and two of its observations are the ones the
        /// review named explicitly: the postwrite-apply fault on a fixed-step world, and the restart from the store
        /// alone. Its engine-physics observations are the physical-observation limitation stated as an observation.
        /// </summary>
        [UnityTest]
        public IEnumerator TheTraversalCourseCoversThePostwriteAndRestartFaultPoints()
        {
            // Unity's local PhysicsScene creation is a runtime API; the Editor refuses it in Edit Mode.
            // Enter Play Mode for the real engine-physics observations, then return to Edit Mode.
            yield return new EnterPlayMode();
            try
            {
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-postwrite-apply-fault-never-exposes-a-destination");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-recovery-publication-fault-keeps-the-registry-unchanged");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-restart-from-the-store-recovers-without-in-process-state");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-restart-without-a-document-or-incompatible-content-exposes-nothing");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-recovered-engine-physics-is-reseeded-not-continued");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-source-authoritative-state-survives-the-recovery");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-recovered-world-refuses-an-old-session-observation");
                AssertFamilyObservation(Gc020TraversalHost.Label, "gc027-recovered-world-steps-its-engine-once-per-admitted-step");
            }
            finally
            {
                UnityWorldRegistry.ResetAll();
            }

            yield return new ExitPlayMode();
        }

        /// <summary>
        /// The engine-free policy half, asserted where the suite can see it: the eight declared injection points are
        /// the ones the runner covers, every point names a permitted observable result and a data-loss class, and the
        /// retry table agrees with the failure classification (P-049, TEST-016).
        /// </summary>
        [Test]
        public void TheDeclaredFaultPointsCoverTheEightTaskBoundaries()
        {
            Assert.That(RecoveryFaultPoints.Count, Is.EqualTo(8),
                "the recovery fault table declares exactly the eight injection points GC-027 names");
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.CaptureCopy, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.CheckpointPublication, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.RestoreReferenceRepair, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.RestorePostwriteApply, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.OutboxAppend, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.OutboxDelivery, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.OutboxAcknowledgement, out RecoveryFaultPoint _), Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.Restart, out RecoveryFaultPoint _), Is.True);

            for (int i = 0; i < RecoveryFaultPoints.All.Count; i++)
            {
                RecoveryFaultPoint point = RecoveryFaultPoints.All[i];
                Assert.That(point.BoundaryNames.Count, Is.GreaterThan(0), point.Id + " names a boundary");
                Assert.That(point.Statement, Is.Not.Empty, point.Id + " states its permitted observable result");
                if (point.Mechanism == RecoveryInjectionMechanism.DeliveryHook)
                {
                    for (int n = 0; n < point.BoundaryNames.Count; n++)
                    {
                        Assert.That(DeliveryBoundaries.All, Does.Contain(point.BoundaryNames[n]),
                            point.Id + " names a declared delivery boundary");
                    }
                }
                else if (point.Mechanism == RecoveryInjectionMechanism.StoreRead)
                {
                    Assert.That(point.BoundaryNames, Is.EqualTo(new[] { "store-read" }),
                        "a restart is refused by the checkpoint store, not by a fault latch");
                }
                else
                {
                    // Every latch point names at least one `FaultBoundaryText` name. The postwrite-apply point names
                    // two, because the restore sequence reaches it twice and both reaches must leave the same
                    // observable result; every other latch point names exactly one.
                    Assert.That(point.BoundaryNames.Count, Is.GreaterThan(0), point.Id + " names a latch boundary");
                    for (int n = 0; n < point.BoundaryNames.Count; n++)
                    {
                        Assert.That(FaultBoundaryText.Names, Does.Contain(point.BoundaryNames[n]),
                            point.Id + " names a declared latch boundary");
                    }

                    if (!string.Equals(point.Id, RecoveryFaultPoints.RestorePostwriteApply, StringComparison.Ordinal))
                    {
                        Assert.That(point.BoundaryNames.Count, Is.EqualTo(1),
                            point.Id + " names exactly one latch boundary");
                    }
                }
            }
        }

        /// <summary>
        /// The engine-free store's versioned envelope, driven directly with no scenario and no world: a document
        /// round-trips with the same bytes and hash, a flipped document byte is refused as unusable rather than read
        /// as a partial document, another format major is refused as unsupported before anything is read, and the
        /// format's version gate and envelope length agree with what the store recorded (P-054, P-055).
        /// </summary>
        [Test]
        public void TheEngineFreeStoreIsVersionedAndRefusesCorruption()
        {
            var store = new MemoryCheckpointStore("memory://gc027/test");
            byte[] document = { 0x47, 0x43, 0x30, 0x32, 0x37 }; // "GC027", a small non-empty document.

            Assert.That(
                store.TryPublish(document, out StoredCheckpoint published, out DiagnosticCode publishCode, out string publishDetail),
                Is.True, "a non-empty document is published: " + publishCode + ": " + publishDetail);
            Assert.That(publishCode, Is.EqualTo(DiagnosticCode.None));

            Assert.That(
                store.TryRead(out byte[]? readBack, out StoredCheckpoint stored, out DiagnosticCode readCode, out string readDetail),
                Is.True, "the published envelope is read back: " + readCode + ": " + readDetail);
            Assert.That(readBack, Is.EqualTo(document), "the round trip returns the published bytes");
            Assert.That(stored.DocumentHash, Is.EqualTo(published.DocumentHash),
                "the round trip returns the published document's hash");
            Assert.That(stored.EnvelopeBytes, Is.EqualTo(CheckpointStoreFormat.EnvelopeBytesFor(document.Length)),
                "the stored value reports the format's envelope length for the document");

            byte[]? envelope = store.EnvelopeBytes();
            Assert.That(envelope, Is.Not.Null, "a published store hands back its envelope");

            // One flipped byte inside the document body no longer verifies against the envelope's document checksum,
            // so the blob is refused with a code and no document is returned (P-054).
            byte[] mutated = (byte[])envelope!.Clone();
            mutated[CheckpointStoreEnvelope.DocumentOffset] ^= 0xFF;
            Assert.That(store.TryOverwriteEnvelope(mutated, out string mutatedDetail), Is.True, mutatedDetail);
            Assert.That(
                store.TryRead(out byte[]? mutatedRead, out StoredCheckpoint _, out DiagnosticCode mutatedCode, out string _),
                Is.False, "a corrupted document is never read back");
            Assert.That(mutatedCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(mutatedRead, Is.Null, "a refused read returns no document");

            // A fresh envelope that declares another format major is a different layout: it is refused as an
            // unsupported version before any field is read (P-055).
            byte[] foreign = (byte[])envelope!.Clone();
            foreign[CheckpointStoreEnvelope.MajorOffset] = 2;
            Assert.That(store.TryOverwriteEnvelope(foreign, out string foreignDetail), Is.True, foreignDetail);
            Assert.That(
                store.TryRead(out byte[]? foreignRead, out StoredCheckpoint _, out DiagnosticCode foreignCode, out string _),
                Is.False, "another format major is never read back");
            Assert.That(foreignCode, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(foreignRead, Is.Null, "a refused read returns no document");

            Assert.That(CheckpointStoreFormat.IsSupported(1, 0), Is.True, "this build implements store format 1.0");
            Assert.That(CheckpointStoreFormat.IsSupported(2, 0), Is.False, "another major is another layout");
            Assert.That(CheckpointStoreFormat.IsSupported(1, 1), Is.False, "a newer minor has no escape hatch");
        }

        /// <summary>
        /// The declared table is the task's own order: the eight ids in the sequence the task names, the two
        /// checkpoint latch points, and a boundary list that is every point's boundary names in table order
        /// (P-049, TEST-016).
        /// </summary>
        [Test]
        public void TheRecoveryFaultTableCoversTheEightTaskBoundariesInOrder()
        {
            string[] expectedIds =
            {
                "capture-copy",
                "checkpoint-publication",
                "restore-reference-repair",
                "restore-postwrite-apply",
                "outbox-append",
                "outbox-delivery",
                "outbox-acknowledge",
                "restart",
            };

            Assert.That(RecoveryFaultPoints.Count, Is.EqualTo(expectedIds.Length),
                "the table declares the eight points GC-027 names");

            string[] actualIds = new string[RecoveryFaultPoints.All.Count];
            for (int i = 0; i < RecoveryFaultPoints.All.Count; i++)
            {
                actualIds[i] = RecoveryFaultPoints.All[i].Id;
            }

            Assert.That(actualIds, Is.EqualTo(expectedIds), "the declared ids in the task's order");

            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.CaptureCopy, out RecoveryFaultPoint captureCopy),
                Is.True);
            Assert.That(RecoveryFaultPoints.TryGet(RecoveryFaultPoints.CheckpointPublication, out RecoveryFaultPoint publication),
                Is.True);

            // The two checkpoint-* boundary names are the latch points': no other point claims one, and both are
            // armed on the owning world's latch rather than a delivery or store-read seam (P-049).
            Assert.That(captureCopy.Mechanism, Is.EqualTo(RecoveryInjectionMechanism.Latch), "capture-copy is a latch point");
            Assert.That(publication.Mechanism, Is.EqualTo(RecoveryInjectionMechanism.Latch), "checkpoint-publication is a latch point");
            Assert.That(captureCopy.BoundaryNames.Count, Is.EqualTo(1));
            Assert.That(publication.BoundaryNames.Count, Is.EqualTo(1));
            Assert.That(captureCopy.BoundaryNames[0], Is.EqualTo("checkpoint-capture-copy"));
            Assert.That(publication.BoundaryNames[0], Is.EqualTo("checkpoint-publication"));

            IReadOnlyList<string> boundaries = RecoveryFaultPoints.BoundaryNames();
            var expectedBoundaries = new List<string>();
            for (int i = 0; i < RecoveryFaultPoints.All.Count; i++)
            {
                for (int n = 0; n < RecoveryFaultPoints.All[i].BoundaryNames.Count; n++)
                {
                    expectedBoundaries.Add(RecoveryFaultPoints.All[i].BoundaryNames[n]);
                }
            }

            Assert.That(boundaries, Is.EqualTo(expectedBoundaries), "every point's boundary names, in table order");

            int checkpointNames = 0;
            for (int i = 0; i < boundaries.Count; i++)
            {
                if (boundaries[i].StartsWith("checkpoint-", StringComparison.Ordinal))
                {
                    checkpointNames++;
                }
            }

            Assert.That(checkpointNames, Is.EqualTo(2),
                "the two checkpoint-* boundary names are the two latch points'");
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertObservation(string bareName)
        {
            AssertFamilyObservation(Gc013NarrativeHost.Label, bareName);
            AssertFamilyObservation(Gc013CardsHost.Label, bareName);
        }

        /// <summary>The traversal adapter, built once per fixture so its run is shared like the other two (P-028).</summary>
        private static IGc027Family TraversalFamily()
        {
            IGc027Family? cached;
            if (Families.TryGetValue(Gc020TraversalHost.Label, out cached) && cached != null)
            {
                return cached;
            }

            IGc027Family built = Gc020TraversalHost.RecoveryFamily();
            Families[Gc020TraversalHost.Label] = built;
            return built;
        }

        private static readonly Dictionary<string, IGc027Family> Families = new Dictionary<string, IGc027Family>();

        private static void AssertFamilyObservation(string label, string bareName)
        {
            Gc027ScenarioResult run = RunOf(label);
            string[] expected = Gc027Scenario.ExpectedNames(FamilyOf(label));

            Assert.That(run.Steps.Count, Is.EqualTo(expected.Length),
                "the run records exactly the observations its own declarations allow: " + run.Describe());
            Assert.That(run.AllPassed, Is.True, run.Describe());
            Assert.That(Names(run), Is.EqualTo(expected), "observation order");

            Gc027Step step = Named(run, label + "/" + bareName);
            Assert.That(step.Passed, Is.True, step.ToString());
            Assert.That(step.Detail, Is.Not.Empty,
                "an observation carries the values it was computed from: " + step.Name);

            // Every injection point states its permitted observable result, and the two restart points state theirs;
            // the remaining observations are comparisons or cleanup, so requiring the clause everywhere would be
            // asking them to claim something they do not have.
            if (IsInjectionPoint(bareName))
            {
                Assert.That(step.Detail, Does.Contain("permittedResult="),
                    "an injection point states its permitted observable result: " + step.Name);
            }
        }

        /// <summary>
        /// The observations that defend one of the eight injection points (or one of the two restart points), i.e.
        /// the ones whose detail must name the permitted observable result the fault is allowed to have (P-049).
        /// </summary>
        private static readonly string[] InjectionPointObservations =
        {
            "gc027-capture-copy-fault-produces-no-checkpoint",
            "gc027-publication-fault-keeps-the-previous-document",
            "gc027-reference-repair-fault-never-builds-a-destination",
            "gc027-postwrite-apply-fault-never-exposes-a-destination",
            "gc027-recovery-publication-fault-keeps-the-registry-unchanged",
            "gc027-recovery-publishes-a-new-session-with-the-captured-state",
            "gc027-outbox-append-fault-refuses-before-delivery",
            "gc027-outbox-delivery-fault-redelivers-with-one-destination-effect",
            "gc027-outbox-acknowledgement-fault-records-or-redelivers-once",
            "gc027-restart-from-the-store-recovers-without-in-process-state",
            "gc027-restart-without-a-document-or-incompatible-content-exposes-nothing",
            "gc027-transient-failure-is-retried-under-the-host-bound",
        };

        private static bool IsInjectionPoint(string bareName)
        {
            for (int i = 0; i < InjectionPointObservations.Length; i++)
            {
                if (string.Equals(InjectionPointObservations[i], bareName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static Gc027ScenarioResult RunOf(string label)
        {
            Gc027ScenarioResult? cached;
            if (Runs.TryGetValue(label, out cached) && cached != null)
            {
                return cached;
            }

            Gc027ScenarioResult run = Gc027Scenario.Run(FamilyOf(label));
            Runs[label] = run;
            return run;
        }

        /// <summary>The adapter behind one label, so a case can ask what that family's table is (P-001).</summary>
        private static IGc027Family FamilyOf(string label)
        {
            if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
            {
                return Gc013NarrativeHost.RecoveryFamily();
            }

            return string.Equals(label, Gc013CardsHost.Label, StringComparison.Ordinal)
                ? Gc013CardsHost.RecoveryFamily()
                : TraversalFamily();
        }

        private static Gc027Step Named(Gc027ScenarioResult run, string qualifiedName)
        {
            for (int i = 0; i < run.Steps.Count; i++)
            {
                if (string.Equals(run.Steps[i].Name, qualifiedName, StringComparison.Ordinal))
                {
                    return run.Steps[i];
                }
            }

            throw new InvalidOperationException("the run recorded no observation named " + qualifiedName
                + ": " + run.Describe());
        }

        private static string[] Names(Gc027ScenarioResult run)
        {
            var names = new string[run.Steps.Count];
            for (int i = 0; i < run.Steps.Count; i++)
            {
                names[i] = run.Steps[i].Name;
            }

            return names;
        }

        private static IReadOnlyList<Gc027Step> PassingSteps(string[] names)
        {
            var steps = new List<Gc027Step>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new Gc027Step(names[i], true, "table check"));
            }

            return steps;
        }

        private static void AssertDigest(string expected, string actual, string label)
        {
            Assert.That(actual, Is.EqualTo(expected),
                "the " + label + " digest literal is the one the observation table produces; a renamed or "
                + "reordered observation changes it");
        }
    }
}
