// GameCore.Validation.ProbeHost — the GC-027 checkpoint-and-durable-delivery recovery scenario.
//
// The task's own acceptance sentence (09 GC-027) is this file's specification:
//
//   "Compose `RecoverWorld` (O-22) over the existing capture/restore path, with P-049 host-configured bounded
//    retries. Inject faults at capture copy, file publication, restore reference repair, postwrite apply, outbox
//    append, delivery, ack and restart. For each: the permitted observable result, the failed old world never
//    resumes, no hidden external replay. Restore cards, narrative and traversal-compatible checkpoint data into a
//    NEW world with different native handles; verify active/dormant state, pending-command disposition and delivery
//    cursor intact; replayed external delivery does not duplicate the test destination effect; incompatible content
//    leaves the new world unexposed."
//
// ONE RUNNER, TWO FAMILY ADAPTERS. The world plumbing is `Gc027SourceWorld` (the real chain of modules GC-018's own
// scenario builds) and `Gc027RestoreBuilder` (the real O-21 rebuilder for a genre's own declarations). Every value a
// step reports is read from those modules: a step never records a claim it did not compute, and no helper returns a
// silent false — a method that cannot do what its name says reports the code and the detail it received (P-052).
//
// The eight injection points of `RecoveryFaultPoints` are covered in the task's order: capture copy, file
// publication, restore reference repair, postwrite apply, outbox append, delivery, acknowledgement, restart. For
// each one the scenario asserts the permitted observable result, that the old world never resumes and that no
// external effect was replayed; each recovery also writes a `RecoveryTranscript`, which is what the
// `artifacts/gc-027/` transcripts are produced from when the build host runs this.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Delivery;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Recovery;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-027 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc027Step
    {
        public Gc027Step(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one GC-027 run: the named observations plus one digest over them, computed with the same
    /// digest function every other scenario uses. A run that records a different set of observations, or a failing
    /// one, cannot report the expected digest (P-008).
    /// </summary>
    public sealed class Gc027ScenarioResult
    {
        public Gc027ScenarioResult(string label, IReadOnlyList<Gc027Step> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        public string Label { get; }

        public IReadOnlyList<Gc027Step> Steps { get; }

        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>Runs the GC-027 recovery sequence over one family's real worlds.</summary>
    public static class Gc027Scenario
    {
        /// <summary>
        /// The observation names one family's run records, in execution order. Both families record exactly these
        /// names, so a renamed or dropped observation fails the EditMode suite and the player probe instead of
        /// shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc027-source-world-captures-and-publishes-a-verified-checkpoint",
            "gc027-capture-copy-fault-produces-no-checkpoint",
            "gc027-publication-fault-keeps-the-previous-document",
            "gc027-reference-repair-fault-never-builds-a-destination",
            "gc027-postwrite-apply-fault-never-exposes-a-destination",
            "gc027-recovery-publication-fault-keeps-the-registry-unchanged",
            "gc027-recovery-publishes-a-new-session-with-the-captured-state",
            "gc027-restored-world-uses-different-native-handles",
            "gc027-active-and-dormant-state-survive-the-recovery",
            "gc027-outbox-rows-and-delivery-cursor-survive-the-recovery",
            "gc027-outbox-append-fault-refuses-before-delivery",
            "gc027-outbox-delivery-fault-redelivers-with-one-destination-effect",
            "gc027-outbox-acknowledgement-fault-records-or-redelivers-once",
            "gc027-restart-from-the-store-recovers-without-in-process-state",
            "gc027-restart-without-a-document-or-incompatible-content-exposes-nothing",
            "gc027-transient-failure-is-retried-under-the-host-bound",
            "gc027-teardown-disposes-every-world",
        };

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole recovery sequence for one family.</summary>
        public static Gc027ScenarioResult Run(IGc027Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            /// <summary>Sessions this run reserves; a distinct block per family so two families never collide.</summary>
            private const ulong SessionSaltXor = 0x4730323753455353UL;

            /// <summary>Transient read refusals the retry observation injects before the store answers (P-049).</summary>
            private const int TransientRefusals = 1;

            /// <summary>The host's configured attempt bound for the retry observation (P-049).</summary>
            private const int HostBoundedAttempts = 3;

            /// <summary>The host's configured bound for the exhaustion half: one attempt, so no retry is allowed.</summary>
            private const int HostExhaustedAttempts = 1;

            private const int ReservationCapacity = 16;

            private readonly IGc027Family family;
            private readonly List<Gc027Step> steps = new List<Gc027Step>();
            private readonly IdSequence sessionSequence;
            private readonly RecoveryTranscript transcript = new RecoveryTranscript(256);

            private Gc027SourceWorld? source;
            private MemoryCheckpointStore? store;
            private WorldRecoveryReport? recovery;
            private Gc027RestoreBuilder? builder;
            private byte[]? checkpointBytes;
            private ContentHash checkpointHash;
            private WorldId recoveredSession;
            private string lastFailure = string.Empty;
            private ulong operationIssuer;

            public Executor(IGc027Family family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt ^ SessionSaltXor);
            }

            public Gc027ScenarioResult Run()
            {
                bool built = BuildSource();
                CaptureCopyFault();
                PublicationFault();
                bool faulted = FaultTheSource();

                ReferenceRepairFault();
                PostwriteApplyFault();
                RecoveryPublicationFault();
                CleanRecovery();
                RestoredHandles();
                ActiveAndDormantState();
                OutboxAcrossTheRecovery();

                OutboxAppendFault();
                DeliveryFault();
                AcknowledgementFault();

                RestartFromTheStore();
                RestartWithoutADocument();
                TransientFailureIsRetried();
                TearDown();

                if (!built || !faulted)
                {
                    lastFailure = "the source world was not established: built=" + built + ", faulted=" + faulted;
                }

                return new Gc027ScenarioResult(family.Label, steps);
            }

            // ============================================================ 1. the source world and its checkpoint

            private bool BuildSource()
            {
                const string name = "gc027-source-world-captures-and-publishes-a-verified-checkpoint";
                try
                {
                    var world = new Gc027SourceWorld(family, sessionSequence);
                    source = world;
                    if (!world.TryBuild(out string buildDetail))
                    {
                        Add(name, false, "the source world could not be built: " + buildDetail);
                        return false;
                    }

                    if (!world.TryCommitUndeliveredObligation(out Id128 outboxId, out string obligationDetail))
                    {
                        Add(name, false, "the family's obligation could not be committed: " + obligationDetail);
                        return false;
                    }

                    store = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/checkpoint");
                    ulong idleSteps = world.PumpIdleFrames();
                    bool atBoundary = new UnityCommittedBoundaryReader(world.Host, world.Context).IsAtCommittedBoundary;
                    CheckpointPublicationResult publication = world.CaptureAndPublish(store, transcript);
                    if (!publication.Succeeded || publication.Capture == null)
                    {
                        Add(name, false, "the checkpoint could not be captured and published: " + publication.Detail);
                        return false;
                    }

                    checkpointBytes = publication.Capture.Document;
                    checkpointHash = publication.Capture.DocumentHash;

                    bool carriedOutbox = publication.Capture.Header.OutboxCount == 1U;
                    bool pass = atBoundary
                        && idleSteps == 0UL
                        && publication.Stored.DocumentBytes == checkpointBytes.Length
                        && publication.Stored.DocumentHash.Equals(checkpointHash)
                        && carriedOutbox
                        && store.Exists
                        && world.Host.Lifecycle == WorldLifecycleState.Running
                        && world.Delivery.Outbox.OpenCount == 1
                        && world.Delivery.Outbox.IsDurable;

                    Add(name, pass,
                        "session=" + world.World.Session.ToString()
                        + "; liveTargets=" + world.Targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; targetIndices=" + Join(world.TargetIndices)
                        + "; auxiliaryIndices=" + world.AuxiliaryIndexRange
                        + "; obligation=" + outboxId.ToString()
                        + "; headerOutboxCount=" + publication.Capture.Header.OutboxCount.ToString(CultureInfo.InvariantCulture)
                        + "; document=" + checkpointBytes.Length.ToString(CultureInfo.InvariantCulture) + "B/"
                        + checkpointHash.ToHex()
                        + "; store=" + store.Location
                        + "; envelope=" + publication.Stored.ToString()
                        + "; atCommittedBoundary=" + atBoundary
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; lifecycle=" + world.Host.Lifecycle
                        + "; permittedResult=the committed boundary was copied and a verified document is stored "
                        + "with the world's one delivery obligation inside it (O-20, P-053)"
                        + DescribeFailure());
                    return pass;
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                    return false;
                }
            }

            /// <summary>
            /// Injection point 1 (capture copy): the copy stage is interrupted before the reader copies the boundary,
            /// so no document and no artifact exist, and the world that was being captured keeps running (O-20, P-049).
            /// </summary>
            private void CaptureCopyFault()
            {
                const string name = "gc027-capture-copy-fault-produces-no-checkpoint";
                try
                {
                    if (source == null || store == null)
                    {
                        Add(name, false, "the source world or the store is missing");
                        return;
                    }

                    byte[]? before = store.EnvelopeBytes();
                    var faultStore = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/copy-fault");
                    source.Host.Faults.Arm(FaultBoundary.CheckpointCaptureCopy);
                    CheckpointPublicationResult publication = source.CaptureAndPublish(faultStore, transcript);
                    source.Host.Faults.Disarm(FaultBoundary.CheckpointCaptureCopy);

                    bool atPoint = publication.FaultPointId == RecoveryFaultPoints.CaptureCopy
                        && publication.Code == DiagnosticCode.ApplyFault;
                    bool noDocument = publication.Capture == null && !faultStore.Exists;
                    bool worldUntouched = source.Host.Lifecycle == WorldLifecycleState.Running
                        && source.Host.FaultCount == 0;
                    bool previousIntact = SameBytes(before, store.EnvelopeBytes());

                    Add(name, atPoint && noDocument && worldUntouched && previousIntact,
                        "faultPoint=" + DescribePoint(publication.FaultPointId)
                        + "; code=" + publication.Code
                        + "; captured=" + publication.Captured
                        + "; published=" + publication.Published
                        + "; faultStoreEmpty=" + (!faultStore.Exists)
                        + "; sourceLifecycle=" + source.Host.Lifecycle
                        + "; sourceFaultCount=" + source.Host.FaultCount.ToString(CultureInfo.InvariantCulture)
                        + "; storedDocumentIntact=" + previousIntact
                        + "; permittedResult=no checkpoint was produced and the world that was being captured keeps "
                        + "running (O-20, P-049)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    source?.Host.Faults.Disarm(FaultBoundary.CheckpointCaptureCopy);
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Injection point 2 (file publication): publication is interrupted before it writes, so the previously
            /// verified document is still the stored one and no partial artifact exists (06 s7, O-20).
            /// </summary>
            private void PublicationFault()
            {
                const string name = "gc027-publication-fault-keeps-the-previous-document";
                try
                {
                    if (source == null || store == null || checkpointBytes == null)
                    {
                        Add(name, false, "the source world, the store or the first document is missing");
                        return;
                    }

                    byte[]? before = store.EnvelopeBytes();
                    source.Host.Faults.Arm(FaultBoundary.CheckpointPublication);
                    CheckpointPublicationResult publication = source.CaptureAndPublish(store, transcript);
                    source.Host.Faults.Disarm(FaultBoundary.CheckpointPublication);

                    bool atPoint = publication.FaultPointId == RecoveryFaultPoints.CheckpointPublication
                        && publication.Code == DiagnosticCode.ApplyFault;
                    bool capturedButNotPublished = publication.Captured && !publication.Published;
                    bool previousIntact = SameBytes(before, store.EnvelopeBytes());
                    bool readable = store.TryRead(
                            out byte[]? document,
                            out StoredCheckpoint stored,
                            out DiagnosticCode code,
                            out string _)
                        && document != null
                        && stored.DocumentHash.Equals(checkpointHash)
                        && code == DiagnosticCode.None
                        && document.Length == checkpointBytes.Length;
                    bool worldUntouched = source.Host.Lifecycle == WorldLifecycleState.Running
                        && source.Host.FaultCount == 0;

                    Add(name, atPoint && capturedButNotPublished && previousIntact && readable && worldUntouched,
                        "faultPoint=" + DescribePoint(publication.FaultPointId)
                        + "; code=" + publication.Code
                        + "; capturedButNotPublished=" + capturedButNotPublished
                        + "; previousDocumentIntact=" + previousIntact
                        + "; previousDocumentReadable=" + readable
                        + "; previousDocumentHash=" + checkpointHash.ToHex()
                        + "; sourceLifecycle=" + source.Host.Lifecycle
                        + "; permittedResult=the previous document is still the stored, verified one and no "
                        + "partial artifact exists (06 s7, O-20)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    source?.Host.Faults.Disarm(FaultBoundary.CheckpointPublication);
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Faults the source world after its first live write through the family's own edit and the real
            /// publisher, so every recovery below recovers from a world that really faulted (P-031).
            /// </summary>
            private bool FaultTheSource()
            {
                if (source == null)
                {
                    return false;
                }

                bool faulted = source.TryFaultAfterFirstLiveWrite(out string detail);
                if (!faulted)
                {
                    lastFailure = detail;
                }

                transcript.Add(
                    faulted ? RecoveryPhase.SourceObserved : RecoveryPhase.Refused,
                    "the source world's postwrite fault: " + detail,
                    RecoveryDataLossClass.None);
                return faulted;
            }

            // ============================================================ 2. the recovery injection points

            /// <summary>
            /// Injection point 3 (restore reference repair): the destination's composition, recipes and reference
            /// tables are never rebuilt, so nothing is staged and the source is unchanged (P-049, TEST-016 row 10).
            /// </summary>
            private void ReferenceRepairFault()
            {
                const string name = "gc027-reference-repair-fault-never-builds-a-destination";
                try
                {
                    if (!Ready(name, out Gc027SourceWorld world, out MemoryCheckpointStore activeStore))
                    {
                        return;
                    }

                    int registryBefore = UnityWorldRegistry.Count;
                    var recoveryTranscript = new RecoveryTranscript(32);
                    world.Host.Faults.Arm(FaultBoundary.RestoreReferenceRepair);
                    WorldRecoveryReport report = Recover(
                        activeStore, recoveryTranscript, null, HostBoundedAttempts, out Gc027RestoreBuilder? unused);
                    world.Host.Faults.Disarm(FaultBoundary.RestoreReferenceRepair);

                    bool pass = report.Outcome == Outcome.Rejected
                        && report.Code == DiagnosticCode.ApplyFault
                        && !report.Recovered
                        && report.DestinationHost == null
                        && report.SourceNeverResumed
                        && report.SourceLifecycleAfter == WorldLifecycleState.Faulted
                        && report.Attempts.Count == 1
                        && report.Attempts.Records[0].FaultPointId == RecoveryFaultPoints.RestoreReferenceRepair
                        && unused != null
                        && unused.BuildCount == 0
                        && UnityWorldRegistry.Count == registryBefore
                        && recoveryTranscript.FaultCount == 1;

                    Add(name, pass,
                        "outcome=" + report.Outcome + "/" + report.Code
                        + "; attempts=" + report.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "; builderAttempts=" + (unused?.BuildCount ?? -1).ToString(CultureInfo.InvariantCulture)
                        + "; destination=" + (report.DestinationHost == null ? "never built" : "exposed")
                        + "; source=" + report.SourceLifecycleBefore.ToString() + "->"
                        + report.SourceLifecycleAfter.ToString()
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture) + "->"
                        + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; transcript=" + recoveryTranscript.Summary()
                        + "; permittedResult=the destination was never built, so no incomplete world can become the "
                        + "running one, and the source stays faulted (P-049)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    source?.Host.Faults.Disarm(FaultBoundary.RestoreReferenceRepair);
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Injection point 4 (postwrite apply during restore): the staging world has been written to and is
            /// destroyed instead of published, so no epoch, revision or session becomes reachable (P-031, P-049).
            /// </summary>
            private void PostwriteApplyFault()
            {
                const string name = "gc027-postwrite-apply-fault-never-exposes-a-destination";
                try
                {
                    if (!Ready(name, out Gc027SourceWorld world, out MemoryCheckpointStore activeStore))
                    {
                        return;
                    }

                    int registryBefore = UnityWorldRegistry.Count;
                    var recoveryTranscript = new RecoveryTranscript(32);
                    WorldRecoveryReport report = Recover(
                        activeStore, recoveryTranscript, "restore-apply", HostBoundedAttempts,
                        out Gc027RestoreBuilder? staged);
                    UnityWorldHost? staging = staged?.Staging;
                    bool stagingDestroyed = staging == null || staging.Lifecycle == WorldLifecycleState.Disposed;

                    bool pass = report.Outcome == Outcome.Rejected
                        && report.Code == DiagnosticCode.ApplyFault
                        && !report.Recovered
                        && report.DestinationHost == null
                        && report.SourceNeverResumed
                        && report.Attempts.Count == 1
                        && staged != null
                        && staged.BuildCount == 1
                        && staged.ArmedBoundaryWasSetOnStaging
                        && staging != null
                        && stagingDestroyed
                        && UnityWorldRegistry.Count == registryBefore
                        && recoveryTranscript.FaultCount == 1;

                    Add(name, pass,
                        "outcome=" + report.Outcome + "/" + report.Code
                        + "; stagingBuilt=" + (staging != null)
                        + "; stagingLifecycle=" + (staging == null ? "<none>" : staging.Lifecycle.ToString())
                        + "; stagingDisposed=" + stagingDestroyed
                        + "; armedOnStaging=" + (staged?.ArmedBoundaryWasSetOnStaging ?? false)
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture) + "->"
                        + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; source=" + report.SourceLifecycleAfter.ToString()
                        + "; permittedResult=the staged world was destroyed and never exposed, and the source is "
                        + "untouched (P-031, P-049)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Injection point 5 (publication of the recovered world): the validated world is destroyed and never
            /// exposed, so the registry still holds exactly the worlds it held before the attempt (P-030).
            /// </summary>
            private void RecoveryPublicationFault()
            {
                const string name = "gc027-recovery-publication-fault-keeps-the-registry-unchanged";
                try
                {
                    if (!Ready(name, out Gc027SourceWorld world, out MemoryCheckpointStore activeStore))
                    {
                        return;
                    }

                    _ = world;
                    int registryBefore = UnityWorldRegistry.Count;
                    var recoveryTranscript = new RecoveryTranscript(32);
                    WorldRecoveryReport report = Recover(
                        activeStore, recoveryTranscript, "recovery-publication", HostBoundedAttempts,
                        out Gc027RestoreBuilder? staged);
                    UnityWorldHost? staging = staged?.Staging;
                    bool stagingDestroyed = staging == null || staging.Lifecycle == WorldLifecycleState.Disposed;

                    bool pass = report.Outcome == Outcome.Rejected
                        && report.Code == DiagnosticCode.ApplyFault
                        && !report.Recovered
                        && report.DestinationHost == null
                        && report.SourceNeverResumed
                        && staged != null
                        && staged.BuildCount == 1
                        && staged.ArmedBoundaryWasSetOnStaging
                        && stagingDestroyed
                        && UnityWorldRegistry.Count == registryBefore;

                    Add(name, pass,
                        "outcome=" + report.Outcome + "/" + report.Code
                        + "; stagingBuilt=" + (staging != null)
                        + "; stagingLifecycle=" + (staging == null ? "<none>" : staging.Lifecycle.ToString())
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture) + "->"
                        + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; sourceFaultCount=" + report.SourceFaultCountAfter.ToString(CultureInfo.InvariantCulture)
                        + "; permittedResult=the validated world was destroyed and never exposed, so the outcome is: "
                        + "registry unchanged, source untouched (P-030, P-049)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>The clean recovery: a new session, published, with the source stopped and never resumed.</summary>
            private void CleanRecovery()
            {
                const string name = "gc027-recovery-publishes-a-new-session-with-the-captured-state";
                try
                {
                    if (!Ready(name, out Gc027SourceWorld world, out MemoryCheckpointStore activeStore))
                    {
                        return;
                    }

                    WorldId sourceSession = world.World;
                    var recoveryTranscript = new RecoveryTranscript(64);
                    recovery = Recover(
                        activeStore, recoveryTranscript, null, HostBoundedAttempts, out Gc027RestoreBuilder? restored);
                    builder = restored;

                    bool pass = recovery.Recovered
                        && recovery.Outcome == Outcome.Published
                        && recovery.DestinationIsFreshIncarnation
                        && recovery.SourceNeverResumed
                        && recovery.SourceLifecycleAfter == WorldLifecycleState.Disposed
                        && recovery.Restart != null
                        && recovery.Restart.DocumentPresent
                        && recovery.Restart.DocumentHash.Equals(checkpointHash)
                        && restored != null
                        && restored.RebuiltTargetCount > 0
                        && restored.RebuiltSlotCount > 0
                        && restored.Delivery != null
                        && recoveredSessionIsLive();

                    recoveredSession = recovery.Recovered && recovery.DestinationHost != null
                        ? recovery.DestinationHost.World
                        : default(WorldId);

                    transcript.Add(
                        recovery.Recovered ? RecoveryPhase.PublishNewWorld : RecoveryPhase.Refused,
                        "the clean recovery: " + OneLine(recovery.Describe()),
                        recovery.Recovered ? RecoveryDataLossClass.None : RecoveryDataLossClass.UncommittedSinceCheckpoint);

                    Add(name, pass,
                        "source=" + sourceSession.Session.ToString()
                        + "->" + recovery.SourceLifecycleBefore.ToString() + "/"
                        + recovery.SourceLifecycleAfter.ToString()
                        + "; destination=" + DescribeSession(recoveredSession)
                        + "; targets=" + (restored?.RebuiltTargetCount ?? 0).ToString(CultureInfo.InvariantCulture)
                        + "; slots=" + (restored?.RebuiltSlotCount ?? 0).ToString(CultureInfo.InvariantCulture)
                        + " (dormant=" + (restored?.RebuiltDormantSlotCount ?? 0).ToString(CultureInfo.InvariantCulture) + ")"
                        + "; attempts=" + recovery.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "; distinctIdentities=" + recovery.AttemptIdentitiesAreDistinct
                        + "; transcript=" + recoveryTranscript.Summary()
                        + "; permittedResult=a new session is published from the verified checkpoint with the "
                        + "captured state, and the old faulted world is stopped and never resumed (O-22, P-049)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>The recovered world's native handles are a different block from the source world's (P-005).</summary>
            private void RestoredHandles()
            {
                const string name = "gc027-restored-world-uses-different-native-handles";
                try
                {
                    if (source == null || recovery == null || !recovery.Recovered || builder == null)
                    {
                        Add(name, false, "no recovered world exists to compare native handles with");
                        return;
                    }

                    IReadOnlyList<int> sourceIndices = source.TargetIndices;
                    IReadOnlyList<int> restoredIndices = builder.RestoredTargetIndices;
                    var overlap = new List<int>();
                    for (int i = 0; i < sourceIndices.Count; i++)
                    {
                        for (int j = 0; j < restoredIndices.Count; j++)
                        {
                            if (sourceIndices[i] == restoredIndices[j])
                            {
                                overlap.Add(sourceIndices[i]);
                                break;
                            }
                        }
                    }

                    bool sameCount = sourceIndices.Count == restoredIndices.Count;
                    bool distinctBlocks = overlap.Count == 0;
                    bool sourceRetired = !UnityWorldRegistry.TryGet(recovery.Request.Source, out UnityWorldHost? _);
                    bool destinationLive = recoveredSessionIsLive();
                    bool fresh = recovery.DestinationIsFreshIncarnation;

                    Add(name, sameCount && distinctBlocks && sourceRetired && destinationLive && fresh,
                        "sourceIndices=" + Join(sourceIndices)
                        + " (auxiliary " + source.AuxiliaryIndexRange + ")"
                        + "; restoredIndices=" + Join(restoredIndices)
                        + "; overlappingIndices=" + Join(overlap)
                        + "; sourceStillRegistered=" + (!sourceRetired)
                        + "; recoveredRunning=" + destinationLive
                        + "; equalIdentities=the two worlds carry the same number of targets with the same stable ids "
                        + "while each occupies a different native handle block (P-004, P-005)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>Active and dormant authoritative state survive the recovery, including dormancy itself (P-032).</summary>
            private void ActiveAndDormantState()
            {
                const string name = "gc027-active-and-dormant-state-survive-the-recovery";
                try
                {
                    if (recovery == null || !recovery.Recovered || recovery.Restore == null)
                    {
                        Add(name, false, "no restore outcome exists to read the state disposition from");
                        return;
                    }

                    RestoreOutcome restore = recovery.Restore;
                    bool dormantSurvived = restore.DormantSlots > 0;
                    bool activeSurvived = restore.RestoredSlots > restore.DormantSlots;
                    bool targetsSurvived = restore.RestoredTargets > 0;

                    Add(name, dormantSurvived && activeSurvived && targetsSurvived,
                        "targets=" + restore.RestoredTargets.ToString(CultureInfo.InvariantCulture)
                        + "; slots=" + restore.RestoredSlots.ToString(CultureInfo.InvariantCulture)
                        + " (dormant=" + restore.DormantSlots.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; outboxRows=" + restore.RestoredOutboxRows.ToString(CultureInfo.InvariantCulture)
                        + "; restoreStage=" + restore.Stage.ToString()
                        + "; permittedResult=the active rows and the dormant row both survive into the new session, "
                        + "and a dormant row stays dormant rather than being reactivated (P-032, P-053)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>The delivery obligation and its cursor cross into the new session with nothing delivered (P-045).</summary>
            private void OutboxAcrossTheRecovery()
            {
                const string name = "gc027-outbox-rows-and-delivery-cursor-survive-the-recovery";
                try
                {
                    if (recovery == null || !recovery.Recovered || recovery.Outbox == null || recovery.Restore == null
                        || builder == null)
                    {
                        Add(name, false, "no recovered session with an outbox exists");
                        return;
                    }

                    OutboxConsistencyReport consistency = recovery.Outbox;
                    int reinstatedRows = recovery.Restore.RestoredOutboxRows;
                    bool carried = consistency.CarriedRows > 0
                        && consistency.CarriedOpenObligations == 1
                        && consistency.CarriedCursors == 1;
                    bool live = consistency.LiveOpenCount == 1 && consistency.LiveTrackedCount >= 1;
                    bool intact = consistency.Consistent && recovery.DeliveryStateIntact;
                    bool proved = builder.ProvedRowCount == consistency.CarriedRows
                        && builder.ReinstateAttemptCount == 1;
                    bool nothingDelivered = builder.Destination != null
                        && builder.Destination.AttemptCount == 0
                        && consistency.LiveTerminalCount == 0;

                    Add(name, carried && live && intact && proved && nothingDelivered && reinstatedRows > 0,
                        "carriedRows=" + consistency.CarriedRows.ToString(CultureInfo.InvariantCulture)
                        + " (open=" + consistency.CarriedOpenObligations.ToString(CultureInfo.InvariantCulture)
                        + ",cursors=" + consistency.CarriedCursors.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; reinstatedRows=" + reinstatedRows.ToString(CultureInfo.InvariantCulture)
                        + "; provedRows=" + builder.ProvedRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; liveOpen=" + consistency.LiveOpenCount.ToString(CultureInfo.InvariantCulture)
                        + "; terminal=" + consistency.LiveTerminalCount.ToString(CultureInfo.InvariantCulture)
                        + "; consistent=" + consistency.Consistent
                        + "; destinationAttempts=" + (builder.Destination?.AttemptCount ?? -1).ToString(CultureInfo.InvariantCulture)
                        + "; cursor=" + consistency.Describe().Split('\n')[0]
                        + "; permittedResult=the obligation and its delivery cursor are owned by the new session, the "
                        + "recovery delivered nothing, and the cursor is intact: no hidden external replay "
                        + "(P-045, P-049, P-053)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ============================================================ 3. the durable-delivery boundaries

            /// <summary>
            /// Injection point 6 (outbox append), through GC-021's own `IDeliveryStepHook` at `before-append`:
            /// nothing durable was written, so the commit is refused and nothing is delivered (P-045).
            /// </summary>
            private void OutboxAppendFault()
            {
                const string name = "gc027-outbox-append-fault-refuses-before-delivery";
                try
                {
                    WorldId session = SessionForDelivery(name);
                    if (session.Session.IsDefault)
                    {
                        return;
                    }

                    var destination = new Gc027RecordingDestination(
                        family.DeliveryDestinationId, family.DeliveryCommandSchema);
                    var hook = new Gc027DeliveryCrashHook(DeliveryBoundaries.BeforeAppend);
                    var outbox = new DurableOutbox(
                        family.Issuer, family.OutboxCapacity, family.OutboxTerminalRetention, OutboxDurability.Durable);
                    var journal = new MemoryDeliveryJournal("memory://gc027/" + family.Label + "/append-fault");
                    var adapter = new DurableDeliveryAdapter(outbox, journal, hook);
                    DeliveryKey key = DeliveryKey.Derive(
                        session, new EventSequence(71UL), family.DeliveryDestinationId, family.DeliveryCommandSchema);

                    bool crashed = false;
                    string crashDetail = string.Empty;
                    DiagnosticCode code = DiagnosticCode.None;
                    string detail = string.Empty;
                    try
                    {
                        adapter.TryCommit(
                            key, family.DeliveryPayloadSchema, family.DeliveryPayload(), new EventSequence(71UL),
                            LogicalStepId.Zero, AssemblyEpoch.First,
                            new OperationId(session, family.Issuer, 71UL), true,
                            out DeliveryObligation? committed, out code, out detail);
                        if (committed != null)
                        {
                            detail = "the commit was accepted although the append boundary crashed: "
                                + committed.Key.OutboxId.ToString();
                        }
                    }
                    catch (Gc027DeliveryCrashException crash)
                    {
                        crashed = true;
                        crashDetail = crash.Message;
                    }

                    bool nothingDurable = journal.FrameCount == 0;
                    bool nothingTracked = outbox.Count == 0 && outbox.OpenCount == 0;
                    bool destinationUntouched = destination.AttemptCount == 0;
                    bool hookReached = hook.CrashCount == 1
                        && hook.Reaches.Count == 1
                        && hook.Reaches[0] == DeliveryBoundaries.BeforeAppend;
                    bool admissionRefused = code == DiagnosticCode.None || outbox.Count == 0;

                    Add(name, crashed && hookReached && nothingDurable && nothingTracked && destinationUntouched
                        && admissionRefused,
                        "boundary=" + DeliveryBoundaries.BeforeAppend
                        + "; crashed=" + crashed
                        + "; journalFrames=" + journal.FrameCount.ToString(CultureInfo.InvariantCulture)
                        + "; trackedObligations=" + outbox.Count.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAttempts=" + destination.AttemptCount.ToString(CultureInfo.InvariantCulture)
                        + "; commitCode=" + code
                        + "; crashDetail=" + crashDetail
                        + "; permittedResult=an obligation is durable before it is handed over, so a fault before "
                        + "the append refuses the commit rather than delivering something no journal holds (P-045)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Injection point 7 (delivery), at `after-delivery`: the destination mutated and the attempt could not be
            /// recorded, so the obligation stays open and a redelivery carries the SAME idempotency key, which makes
            /// the destination mutate exactly once (P-045).
            /// </summary>
            private void DeliveryFault()
            {
                const string name = "gc027-outbox-delivery-fault-redelivers-with-one-destination-effect";
                try
                {
                    WorldId session = SessionForDelivery(name);
                    if (session.Session.IsDefault)
                    {
                        return;
                    }

                    var destination = new Gc027RecordingDestination(
                        family.DeliveryDestinationId, family.DeliveryCommandSchema);
                    var hook = new Gc027DeliveryCrashHook(DeliveryBoundaries.AfterDelivery);
                    var outbox = new DurableOutbox(
                        family.Issuer, family.OutboxCapacity, family.OutboxTerminalRetention, OutboxDurability.Durable);
                    var journal = new MemoryDeliveryJournal("memory://gc027/" + family.Label + "/delivery-fault");
                    var adapter = new DurableDeliveryAdapter(outbox, journal, hook);
                    DeliveryKey key = DeliveryKey.Derive(
                        session, new EventSequence(81UL), family.DeliveryDestinationId, family.DeliveryCommandSchema);

                    if (adapter.TryCommit(
                            key, family.DeliveryPayloadSchema, family.DeliveryPayload(), new EventSequence(81UL),
                            LogicalStepId.Zero, AssemblyEpoch.First,
                            new OperationId(session, family.Issuer, 81UL), true,
                            out DeliveryObligation? obligation, out DiagnosticCode commitCode, out string commitDetail)
                        != OutboxAdmission.Accepted
                        || obligation == null)
                    {
                        Add(name, false, "the obligation could not be committed before the delivery fault: "
                            + commitCode + ": " + commitDetail);
                        return;
                    }

                    bool crashed = false;
                    string crashDetail = string.Empty;
                    try
                    {
                        adapter.TryDeliver(obligation.Key.OutboxId, destination, out DiagnosticCode _, out string _);
                    }
                    catch (Gc027DeliveryCrashException crash)
                    {
                        crashed = true;
                        crashDetail = crash.Message;
                    }

                    bool openAfterCrash = outbox.TryGet(obligation.Key.OutboxId, out DeliveryObligation? open)
                        && open != null
                        && open.IsOpen;
                    int effectsAfterCrash = destination.AppliedCount;

                    // The redelivery: the same obligation, the same idempotency key, and a destination that recognises
                    // it instead of mutating a second time (P-045).
                    hook.CrashAt = string.Empty;
                    DeliveryOutcome redelivery = adapter.TryDeliver(
                        obligation.Key.OutboxId, destination, out DiagnosticCode redeliveryCode,
                        out string redeliveryDetail);

                    bool singleEffect = effectsAfterCrash == 1
                        && destination.AppliedCount == 1
                        && destination.EffectIsSingle;
                    bool recognised = destination.AlreadyAppliedCount == 1
                        && redelivery == DeliveryOutcome.Acknowledged;
                    bool settled = outbox.TryGet(obligation.Key.OutboxId, out DeliveryObligation? row)
                        && row != null
                        && row.State == OutboxDeliveryState.Acknowledged;

                    Add(name, crashed && openAfterCrash && singleEffect && recognised && settled
                        && hook.Reaches.Count == 2 && hook.CrashCount == 1,
                        "boundary=" + DeliveryBoundaries.AfterDelivery
                        + "; crashed=" + crashed
                        + "; openAfterCrash=" + openAfterCrash
                        + "; effectsAfterCrash=" + effectsAfterCrash.ToString(CultureInfo.InvariantCulture)
                        + "; effectsAfterRedelivery=" + destination.AppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; alreadyApplied=" + destination.AlreadyAppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; redelivery=" + redelivery.ToString() + "/" + redeliveryCode
                        + "; settledState=" + (settled ? OutboxDeliveryState.Acknowledged.ToString() : "<open>")
                        + "; crashDetail=" + crashDetail
                        + "; redeliveryDetail=" + redeliveryDetail
                        + "; permittedResult=a redelivery reuses the obligation's idempotency key, so the "
                        + "destination effect is applied exactly once (P-045)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Injection point 8 (acknowledgement): before the acknowledgement is persisted the obligation is
            /// redeliverable; after it the obligation is settled. Neither case loses or duplicates the destination
            /// effect (P-045).
            /// </summary>
            private void AcknowledgementFault()
            {
                const string name = "gc027-outbox-acknowledgement-fault-records-or-redelivers-once";
                try
                {
                    WorldId session = SessionForDelivery(name);
                    if (session.Session.IsDefault)
                    {
                        return;
                    }

                    var destination = new Gc027RecordingDestination(
                        family.DeliveryDestinationId, family.DeliveryCommandSchema);
                    var hook = new Gc027DeliveryCrashHook(DeliveryBoundaries.BeforeAcknowledge);
                    var outbox = new DurableOutbox(
                        family.Issuer, family.OutboxCapacity, family.OutboxTerminalRetention, OutboxDurability.Durable);
                    var journal = new MemoryDeliveryJournal("memory://gc027/" + family.Label + "/ack-fault");
                    var adapter = new DurableDeliveryAdapter(outbox, journal, hook);
                    DeliveryKey key = DeliveryKey.Derive(
                        session, new EventSequence(91UL), family.DeliveryDestinationId, family.DeliveryCommandSchema);

                    if (adapter.TryCommit(
                            key, family.DeliveryPayloadSchema, family.DeliveryPayload(), new EventSequence(91UL),
                            LogicalStepId.Zero, AssemblyEpoch.First,
                            new OperationId(session, family.Issuer, 91UL), true,
                            out DeliveryObligation? obligation, out DiagnosticCode commitCode, out string commitDetail)
                        != OutboxAdmission.Accepted
                        || obligation == null)
                    {
                        Add(name, false, "the obligation could not be committed: " + commitCode + ": " + commitDetail);
                        return;
                    }

                    bool crashed = false;
                    string crashDetail = string.Empty;
                    try
                    {
                        adapter.TryDeliver(obligation.Key.OutboxId, destination, out DiagnosticCode _, out string _);
                    }
                    catch (Gc027DeliveryCrashException crash)
                    {
                        crashed = true;
                        crashDetail = crash.Message;
                    }

                    bool stillRedeliverable = outbox.TryGet(obligation.Key.OutboxId, out DeliveryObligation? pending)
                        && pending != null
                        && !pending.IsTerminal;

                    hook.CrashAt = string.Empty;
                    DeliveryOutcome settledOutcome = adapter.TryDeliver(
                        obligation.Key.OutboxId, destination, out DiagnosticCode settledCode, out string settledDetail);

                    // The mirrored case: a fault after the acknowledgement leaves the obligation settled, so a later
                    // recovery must find the record and deliver nothing at all (P-045).
                    var afterDestination = new Gc027RecordingDestination(
                        family.DeliveryDestinationId, family.DeliveryCommandSchema);
                    var afterHook = new Gc027DeliveryCrashHook(DeliveryBoundaries.AfterAcknowledge);
                    var afterOutbox = new DurableOutbox(
                        family.Issuer, family.OutboxCapacity, family.OutboxTerminalRetention, OutboxDurability.Durable);
                    var afterAdapter = new DurableDeliveryAdapter(
                        afterOutbox,
                        new MemoryDeliveryJournal("memory://gc027/" + family.Label + "/ack-after"),
                        afterHook);
                    DeliveryKey afterKey = DeliveryKey.Derive(
                        session, new EventSequence(101UL), family.DeliveryDestinationId, family.DeliveryCommandSchema);
                    bool afterCommitted = afterAdapter.TryCommit(
                            afterKey, family.DeliveryPayloadSchema, family.DeliveryPayload(), new EventSequence(101UL),
                            LogicalStepId.Zero, AssemblyEpoch.First,
                            new OperationId(session, family.Issuer, 101UL), true,
                            out DeliveryObligation? afterObligation, out DiagnosticCode _, out string _)
                        == OutboxAdmission.Accepted
                        && afterObligation != null;
                    bool afterCrashed = false;
                    if (afterCommitted && afterObligation != null)
                    {
                        try
                        {
                            afterAdapter.TryAcknowledge(afterObligation.Key.OutboxId, out DiagnosticCode _, out string _);
                        }
                        catch (Gc027DeliveryCrashException)
                        {
                            afterCrashed = true;
                        }
                    }

                    bool afterSettled = afterCommitted
                        && afterObligation != null
                        && afterOutbox.TryGet(afterObligation.Key.OutboxId, out DeliveryObligation? settledRow)
                        && settledRow != null
                        && settledRow.State == OutboxDeliveryState.Acknowledged;
                    bool afterEffectNone = afterDestination.AttemptCount == 0;

                    Add(name, crashed && stillRedeliverable
                        && settledOutcome == DeliveryOutcome.Acknowledged
                        && destination.AppliedCount == 1
                        && destination.EffectIsSingle
                        && afterCrashed && afterSettled && afterEffectNone
                        && hook.Reaches.Count == 2,
                        "beforeAcknowledge=" + DeliveryBoundaries.BeforeAcknowledge
                        + "; crashed=" + crashed
                        + "; stillRedeliverable=" + stillRedeliverable
                        + "; settledOutcome=" + settledOutcome.ToString() + "/" + settledCode
                        + "; destinationEffects=" + destination.AppliedCount.ToString(CultureInfo.InvariantCulture)
                        + "; afterAcknowledge=" + DeliveryBoundaries.AfterAcknowledge
                        + "; afterCrashed=" + afterCrashed
                        + "; afterSettled=" + afterSettled
                        + "; afterCrashDestinationAttempts=" + afterDestination.AttemptCount.ToString(CultureInfo.InvariantCulture)
                        + "; crashDetail=" + crashDetail
                        + "; settledDetail=" + settledDetail
                        + "; permittedResult=an acknowledgement is persisted before it is applied, so a fault before "
                        + "it leaves the obligation redeliverable and a fault after it leaves it settled with exactly "
                        + "one destination effect (P-045)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ============================================================ 4. restart with no in-process state

            /// <summary>
            /// Injection point 9 (restart): a fresh session built from the stored bytes alone. The previous session is
            /// not contacted, and the reinstated obligation is owned rather than delivered (P-049, P-053).
            /// </summary>
            private void RestartFromTheStore()
            {
                const string name = "gc027-restart-from-the-store-recovers-without-in-process-state";
                try
                {
                    if (checkpointBytes == null || checkpointHash.IsEmpty || recovery == null)
                    {
                        Add(name, false, "no verified document exists to restart from");
                        return;
                    }

                    var restartStore = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/restart");
                    if (!restartStore.TryPublish(
                            checkpointBytes, out StoredCheckpoint _, out DiagnosticCode publishCode, out string publishDetail))
                    {
                        Add(name, false, "the restart store could not be prepared: " + publishCode + ": " + publishDetail);
                        return;
                    }

                    WorldId previousSession = recovery.Request.Source;
                    int registryBefore = UnityWorldRegistry.Count;
                    var restartTranscript = new RecoveryTranscript(64);
                    var restartBuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, null);
                    var context = new WorldRecoveryContext(
                        RestartRequest(restartStore, family.CatalogHash()),
                        family.CreateRegistration(Descriptor().Adaptation!),
                        () => new WorldId(sessionSequence.Next()),
                        NextOperation,
                        new RestoreReservationLedger(ReservationCapacity),
                        family.Codecs,
                        (WorldId _, OperationId _) => restartBuilder,
                        RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(HostBoundedAttempts, 0UL)),
                        restartTranscript,
                        (WorldId _) => restartBuilder.Outbox);

                    WorldRecoveryReport report = WorldRecovery.Restart(context, previousSession);
                    bool pass = report.Recovered
                        && report.DestinationIsFreshIncarnation
                        && report.Restart != null
                        && report.Restart.DocumentPresent
                        && report.Restart.DocumentHash.Equals(checkpointHash)
                        && report.Restart.ProducedNewSession
                        && report.Outbox != null
                        && report.Outbox.Consistent
                        && report.ObligationsOwed == 1
                        && restartBuilder.Destination != null
                        && restartBuilder.Destination.AttemptCount == 0
                        && UnityWorldRegistry.Count == registryBefore + 1;

                    Add(name, pass,
                        "previousSession=" + previousSession.Session.ToString()
                        + "; restartedSession=" + DescribeSession(report.DestinationHost?.World ?? default(WorldId))
                        + "; outcome=" + report.Outcome + "/" + report.Code
                        + "; document=" + (report.Restart?.DocumentHash.ToHex() ?? "<none>")
                        + "; owedObligations=" + report.ObligationsOwed.ToString(CultureInfo.InvariantCulture)
                        + "; destinationAttempts=" + (restartBuilder.Destination?.AttemptCount ?? -1).ToString(CultureInfo.InvariantCulture)
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture) + "->"
                        + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; transcript=" + restartTranscript.Summary()
                        + "; restartPoint=" + (report.Restart?.ToLine() ?? "<none>")
                        + "; permittedResult=a restart builds a new session from verified bytes only, contacts "
                        + "nothing from the previous process and delivers nothing (P-049, P-053)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The restart's refusal half: an absent document and an incompatible catalog each produce no world at
            /// all, which is the only permitted result when nothing verified can be read (P-049, P-054).
            /// </summary>
            private void RestartWithoutADocument()
            {
                const string name = "gc027-restart-without-a-document-or-incompatible-content-exposes-nothing";
                try
                {
                    int registryBefore = UnityWorldRegistry.Count;
                    WorldId previousSession = recovery?.Request.Source ?? default(WorldId);

                    // (a) nothing is stored at all.
                    var emptyStore = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/absent");
                    var absentBuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, null);
                    var absentContext = new WorldRecoveryContext(
                        RestartRequest(emptyStore, family.CatalogHash()),
                        family.CreateRegistration(Descriptor().Adaptation!),
                        () => new WorldId(sessionSequence.Next()),
                        NextOperation,
                        new RestoreReservationLedger(ReservationCapacity),
                        family.Codecs,
                        (WorldId _, OperationId _) => absentBuilder,
                        RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(HostExhaustedAttempts, 0UL)),
                        new RecoveryTranscript(32),
                        (WorldId _) => absentBuilder.Outbox);
                    WorldRecoveryReport absent = WorldRecovery.Restart(absentContext, previousSession);

                    // (b) a real document and a destination catalog that does not match it.
                    bool published = false;
                    var mismatchedBuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, null);
                    WorldRecoveryReport mismatched = absent;
                    if (checkpointBytes != null)
                    {
                        var mismatchedStore = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/mismatch");
                        published = mismatchedStore.TryPublish(
                            checkpointBytes, out StoredCheckpoint _, out DiagnosticCode _, out string _);
                        var mismatchedContext = new WorldRecoveryContext(
                            RestartRequest(mismatchedStore, DifferentFingerprint()),
                            family.CreateRegistration(Descriptor().Adaptation!),
                            () => new WorldId(sessionSequence.Next()),
                            NextOperation,
                            new RestoreReservationLedger(ReservationCapacity),
                            family.Codecs,
                            (WorldId _, OperationId _) => mismatchedBuilder,
                            RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(HostExhaustedAttempts, 0UL)),
                            new RecoveryTranscript(32),
                            (WorldId _) => mismatchedBuilder.Outbox);
                        mismatched = WorldRecovery.Restart(mismatchedContext, previousSession);
                    }

                    bool pass = published
                        && !absent.Recovered
                        && absent.DestinationHost == null
                        && absent.Code == DiagnosticCode.ResourceUnavailable
                        && absent.Restart != null
                        && !absent.Restart.DocumentPresent
                        && !mismatched.Recovered
                        && mismatched.DestinationHost == null
                        && mismatched.Code == DiagnosticCode.UnsupportedVersion
                        && absentBuilder.BuildCount == 0
                        && mismatchedBuilder.BuildCount == 0
                        && UnityWorldRegistry.Count == registryBefore;

                    Add(name, pass,
                        "absentDocument=" + absent.Outcome + "/" + absent.Code
                        + " (destination=" + DescribeExposure(absent) + ", builderAttempts="
                        + absentBuilder.BuildCount.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; incompatibleDocument=" + mismatched.Outcome + "/" + mismatched.Code
                        + " (destination=" + DescribeExposure(mismatched) + ", builderAttempts="
                        + mismatchedBuilder.BuildCount.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture) + "->"
                        + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; permittedResult=an absent or incompatible document produces no world at all, so an "
                        + "incomplete destination is never exposed (P-049, P-054)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ============================================================ 5. the host-configured bounded retry

            /// <summary>
            /// P-049's host-configured bounded retry against a real transient failure: the store refuses the first
            /// read, the first attempt is refused with the retry classification of that code, and the second attempt
            /// runs under a NEW session and a NEW operation id inside the host's bound. A second store with the bound
            /// set to one attempt proves the same failure is then final.
            /// </summary>
            private void TransientFailureIsRetried()
            {
                const string name = "gc027-transient-failure-is-retried-under-the-host-bound";
                try
                {
                    if (checkpointBytes == null || recovery == null)
                    {
                        Add(name, false, "no verified document exists to fail transiently against");
                        return;
                    }

                    WorldId previousSession = recovery.Request.Source;
                    var inner = new MemoryCheckpointStore("memory://gc027/" + family.Label + "/retry");
                    if (!inner.TryPublish(
                            checkpointBytes, out StoredCheckpoint _, out DiagnosticCode publishCode, out string publishDetail))
                    {
                        Add(name, false, "the retry store could not be prepared: " + publishCode + ": " + publishDetail);
                        return;
                    }

                    var flaky = new Gc027FlakyStore(inner, TransientRefusals);
                    var retriedBuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, null);
                    var retriedTranscript = new RecoveryTranscript(64);
                    var retriedContext = new WorldRecoveryContext(
                        RestartRequest(flaky, family.CatalogHash()),
                        family.CreateRegistration(Descriptor().Adaptation!),
                        () => new WorldId(sessionSequence.Next()),
                        NextOperation,
                        new RestoreReservationLedger(ReservationCapacity),
                        family.Codecs,
                        (WorldId _, OperationId _) => retriedBuilder,
                        RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(HostBoundedAttempts, 0UL)),
                        retriedTranscript,
                        (WorldId _) => retriedBuilder.Outbox);
                    WorldRecoveryReport retried = WorldRecovery.Restart(retriedContext, previousSession);

                    var secondFlaky = new Gc027FlakyStore(inner, TransientRefusals);
                    var boundedBuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, null);
                    var boundedContext = new WorldRecoveryContext(
                        RestartRequest(secondFlaky, family.CatalogHash()),
                        family.CreateRegistration(Descriptor().Adaptation!),
                        () => new WorldId(sessionSequence.Next()),
                        NextOperation,
                        new RestoreReservationLedger(ReservationCapacity),
                        family.Codecs,
                        (WorldId _, OperationId _) => boundedBuilder,
                        RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(HostExhaustedAttempts, 0UL)),
                        new RecoveryTranscript(32),
                        (WorldId _) => boundedBuilder.Outbox);
                    WorldRecoveryReport bounded = WorldRecovery.Restart(boundedContext, previousSession);

                    bool retriedOnce = retried.Recovered
                        && retried.Attempts.Count == 2
                        && retried.Attempts.RetryCount == 1
                        && retried.Attempts.Records[0].Code == DiagnosticCode.ResourceUnavailable
                        && retried.Attempts.Records[1].Outcome == Outcome.Published
                        && retried.AttemptIdentitiesAreDistinct
                        && flaky.TransientRefusalCount == TransientRefusals;
                    bool exhausted = !bounded.Recovered
                        && bounded.Attempts.Count == 1
                        && bounded.Attempts.RetryCount == 0
                        && bounded.Code == DiagnosticCode.ResourceUnavailable
                        && boundedBuilder.BuildCount == 0
                        && secondFlaky.TransientRefusalCount == TransientRefusals;

                    Add(name, retriedOnce && exhausted,
                        "hostBound=" + HostBoundedAttempts.ToString(CultureInfo.InvariantCulture)
                        + "; transientRefusals=" + flaky.TransientRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; attempts=" + retried.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + " (retries=" + retried.Attempts.RetryCount.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; firstAttempt=" + retried.Attempts.Records[0].ToLine()
                        + "; distinctIdentities=" + retried.AttemptIdentitiesAreDistinct
                        + "; classification=" + RecoveryFailureClassification.Describe(DiagnosticCode.ResourceUnavailable)
                        + "; boundedAttempts=" + HostExhaustedAttempts.ToString(CultureInfo.InvariantCulture)
                        + " attempts=" + bounded.Attempts.Count.ToString(CultureInfo.InvariantCulture)
                        + "/" + bounded.Code
                        + "; boundedBuilderAttempts=" + boundedBuilder.BuildCount.ToString(CultureInfo.InvariantCulture)
                        + "; transcript=" + retriedTranscript.Summary()
                        + "; permittedResult=a retryable transient failure is retried under the host's configured "
                        + "bound with a new session and a new operation id, and an exhausted bound makes it final "
                        + "(P-049, P-050)"
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            private void TearDown()
            {
                const string name = "gc027-teardown-disposes-every-world";
                try
                {
                    int before = UnityWorldRegistry.Count;
                    source?.TearDown();

                    // Every world this run owns is stopped and disposed here, including the sessions the clean
                    // recovery, the restart and the retry published: the registry returning to its baseline is what
                    // the EditMode suite asserts (P-035, P-048).
                    var live = new List<UnityWorldHost>(UnityWorldRegistry.Hosts);
                    for (int i = 0; i < live.Count; i++)
                    {
                        if (live[i].Lifecycle != WorldLifecycleState.Disposed)
                        {
                            live[i].Dispose();
                        }
                    }

                    int after = UnityWorldRegistry.Count;
                    Add(name, after == 0,
                        "registry=" + before.ToString(CultureInfo.InvariantCulture) + "->"
                        + after.ToString(CultureInfo.InvariantCulture)
                        + "; every world this run created is stopped and disposed, so no session outlives the run "
                        + "(P-035, P-048)"
                        + "; transcript=" + transcript.Summary()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ============================================================ helpers

            private bool Ready(string name, out Gc027SourceWorld world, out MemoryCheckpointStore activeStore)
            {
                world = source!;
                activeStore = store!;
                if (source == null || store == null || checkpointBytes == null)
                {
                    Add(name, false, "the source world, the store or the captured document is missing");
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Drives one O-22 recovery over the run's source world and store. The rebuilder is created per attempt
            /// because it owns the staging world it creates, which is also why a retry needs a new one (P-049).
            /// </summary>
            private WorldRecoveryReport Recover(
                MemoryCheckpointStore activeStore,
                RecoveryTranscript recoveryTranscript,
                string? armBoundaryName,
                int hostBoundedAttempts,
                out Gc027RestoreBuilder? createdBuilder)
            {
                Gc027SourceWorld world = source!;
                var rebuilder = new Gc027RestoreBuilder(family, Descriptor(), family.CatalogHash(), NextOperation, armBoundaryName);
                createdBuilder = rebuilder;
                PipelineDescriptorReport descriptor = Descriptor();
                var context = new WorldRecoveryContext(
                    new WorldRecoveryRequest(
                        world.World,
                        world.Request.Definition,
                        world.Request.TemporalModel,
                        world.Request.Mode,
                        family.CatalogHash(),
                        activeStore,
                        family.DirectMigrations,
                        family.AllocatedSchemas,
                        true),
                    family.CreateRegistration(descriptor.Adaptation!),
                    () => new WorldId(sessionSequence.Next()),
                    NextOperation,
                    new RestoreReservationLedger(ReservationCapacity),
                    family.Codecs,
                    (WorldId _, OperationId _) => rebuilder,
                    RecoveryRetryPolicy.FromHostSettings(new OperationExpirySettings(hostBoundedAttempts, 0UL)),
                    recoveryTranscript,
                    (WorldId _) => rebuilder.Outbox);
                return WorldRecovery.Recover(context);
            }

            /// <summary>
            /// The restart request names the source and definition the run already knows: the definition comes from
            /// the world create request, not from the ownership descriptor, which describes layouts and stages and
            /// carries no world definition at all.
            /// </summary>
            private WorldRecoveryRequest RestartRequest(ICheckpointStore activeStore, ContentHash fingerprint) =>
                new WorldRecoveryRequest(
                    recovery?.Request.Source ?? default(WorldId),
                    recovery?.Request.Definition ?? source!.Request.Definition,
                    TemporalModel.CommandDriven,
                    PropagationMode.Automatic,
                    fingerprint,
                    activeStore,
                    family.DirectMigrations,
                    family.AllocatedSchemas,
                    true);

            /// <summary>Reserves one operation identity of this run, strictly increasing per issuer (P-050).</summary>
            private OperationId NextOperation(WorldId world)
            {
                operationIssuer++;
                return new OperationId(world, family.Issuer, operationIssuer);
            }

            private PipelineDescriptorReport? descriptor;

            private PipelineDescriptorReport Descriptor()
            {
                if (descriptor == null)
                {
                    descriptor = family.CompilePipeline();
                }

                return descriptor;
            }

            /// <summary>A fingerprint that is deliberately not this family's, so incompatible content is refused (P-028).</summary>
            private ContentHash DifferentFingerprint()
            {
                byte[] bytes = family.CatalogHash().ToArray();
                bytes[0] ^= 0xFF;
                return new ContentHash(bytes);
            }

            /// <summary>The session a delivery-boundary observation runs against (P-004).</summary>
            private WorldId SessionForDelivery(string name)
            {
                if (!recoveredSession.Session.IsDefault)
                {
                    return recoveredSession;
                }

                if (recovery != null && recovery.Recovered && recovery.DestinationHost != null)
                {
                    recoveredSession = recovery.DestinationHost.World;
                    return recoveredSession;
                }

                Add(name, false, "no recovered session exists for this observation");
                return default(WorldId);
            }

            private bool recoveredSessionIsLive() =>
                !recoveredSession.Session.IsDefault
                && UnityWorldRegistry.TryGet(recoveredSession, out UnityWorldHost? host)
                && host != null
                && host.Lifecycle == WorldLifecycleState.Running;

            private void Add(string name, bool passed, string detail)
            {
                if (!passed)
                {
                    lastFailure = name + ": " + detail;
                }

                steps.Add(new Gc027Step(family.Label + "/" + name, passed, detail));
            }

            private static bool SameBytes(byte[]? left, byte[]? right)
            {
                if (left == null || right == null)
                {
                    return left == null && right == null;
                }

                if (left.Length != right.Length)
                {
                    return false;
                }

                for (int i = 0; i < left.Length; i++)
                {
                    if (left[i] != right[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            private static string DescribePoint(string faultPointId) =>
                faultPointId.Length == 0 ? "<none>" : faultPointId;

            private static string DescribeSession(WorldId session) =>
                session.Session.IsDefault ? "<none>" : session.Session.ToString();

            private static string DescribeExposure(WorldRecoveryReport report) =>
                report.DestinationHost == null ? "never exposed" : "exposed";

            private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

            private static string Join(IReadOnlyList<int> values)
            {
                if (values.Count == 0)
                {
                    return "<none>";
                }

                var text = new StringBuilder();
                for (int i = 0; i < values.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(',');
                    }

                    text.Append(values[i].ToString(CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }

            private string DescribeFailure() => lastFailure.Length == 0 ? string.Empty : "; lastFailure=" + lastFailure;

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
