// GameCore.Unity.Runtime — W1 integration seam: the serialized control lane of one world (GC-004) and the owned
// Unity world that executes it (GC-005).
//
// The two modules are deliberately separate. The control lane admits an immutable proposal and publishes an
// atomic revision/epoch step without touching ECS storage; the world host owns lifecycle, the logical step, the
// guarded dispatch and the published step image, and it executes only when it has admitted demand. This bridge
// is the one place that joins them for the W1 gate:
//
//   * a world that cannot execute (faulted, created, stopping, disposed) is refused **before** admission, so the
//     control lane never gains a ledger row for work the world cannot run (P-031, P-051);
//   * an admitted and published operation becomes exactly one unit of host demand, which the guarded driver
//     consumes as one logical step (P-036, P-037);
//   * the world owns the logical step, so the control lane's retention window follows the committed step
//     (`SyncStepFromWorld`) and never drives it (P-006);
//   * a world fault is reported against the operation that triggered the execution that faulted, and the
//     already-published operation result is **not** restated as a failure: publication is not rolled back by a
//     later execution fault (P-031). There is no false success and no false rollback claim.
//
// `CompositionHost` and `UnityWorldHost` stay the authorities for their own state; this type owns no durable
// state of its own beyond the counters it reports. The real live-publication path (mapping a plan to component
// and binding diffs) is GC-008's; what lives here is only the admission/execution handoff W1 can honestly prove.
//
// P-006 asks for ONE publication series, so this bridge is also where the lane's publication is *joined* to the
// world's: when a joined [AssemblyPublisher] is supplied, the bridge hands it the composition publication to adopt
// (and the publisher publishes the assembly, asserting the equality); without one, the bridge asks the world to
// adopt the composition publication itself. Either way the world's published epoch is the composition epoch the
// operation reported, and a world that cannot advance to that value refuses the admission instead of diverging.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;

namespace GameCore.Unity.Runtime.Integration
{
    /// <summary>How one admission-and-execution attempt ended.</summary>
    public enum BridgeOutcome
    {
        /// <summary>Admitted, published and handed to the world as one unit of demand.</summary>
        Executed = 0,

        /// <summary>The control lane refused the edit; nothing was staged and no demand was created.</summary>
        AdmissionRejected = 1,

        /// <summary>The boundary published no step-bearing result owned by this world incarnation.</summary>
        PublicationRejected = 2,

        /// <summary>The world cannot execute, so nothing was admitted onto its lane (P-031).</summary>
        WorldRefused = 3,

        /// <summary>
        /// The lane recognised a retransmission of an operation it already admitted: it returned the original row,
        /// nothing new was staged and no demand was created (P-050).
        /// </summary>
        Retransmission = 4,
    }

    /// <summary>
    /// Observation record of one <see cref="WorldCompositionBridge.SubmitAndExecute"/> attempt: the control-lane
    /// half (admission and publication) plus the handoff of the result into the world's demand. Written only by
    /// the bridge; every value is read from the real modules, never from a managed model of them.
    /// </summary>
    public sealed class WorldAdmissionReport
    {
        public OperationId Operation { get; set; }

        public BridgeOutcome Outcome { get; set; }

        /// <summary>The world's own refusal code when <see cref="Outcome"/> is <see cref="BridgeOutcome.WorldRefused"/>.</summary>
        public DiagnosticCode RefusalCode { get; set; }

        public string RefusalDetail { get; set; } = string.Empty;

        /// <summary>Published composition revision the edit was checked against (P-027, P-028).</summary>
        public CompositionRevision ExpectedRevision { get; set; }

        /// <summary>Live ledger rows on the lane after this attempt.</summary>
        public int LaneRowCount { get; set; }

        public AdmissionKind Admission { get; set; }

        public DiagnosticCode AdmissionCode { get; set; }

        /// <summary>True when the edit produced an immutable proposal waiting for the publication boundary.</summary>
        public bool Staged { get; set; }

        public Outcome PublicationOutcome { get; set; }

        public DiagnosticCode PublicationCode { get; set; }

        public SnapshotToken? PublishedToken { get; set; }

        /// <summary>True when exactly one unit of command demand was handed to the world.</summary>
        public bool CommandSubmitted { get; set; }

        /// <summary>The world's pending demand after the handoff.</summary>
        public ulong DemandAfter { get; set; }

        /// <summary>True when the admitted and published result became one unit of the world's demand.</summary>
        public bool Executed => Outcome == BridgeOutcome.Executed;

        public bool RefusedByWorld => Outcome == BridgeOutcome.WorldRefused;

        /// <summary>The lane returned an already-admitted attempt; nothing was staged and no demand was created.</summary>
        public bool Retransmitted => Outcome == BridgeOutcome.Retransmission;
    }

    /// <summary>
    /// Observation record of what the lane and the world report **after** an operation ran, so a post-write fault
    /// is reported next to the operation whose execution triggered it. Written only by the bridge.
    /// </summary>
    public sealed class WorldExecutionReport
    {
        public OperationId Operation { get; set; }

        /// <summary>How the lane's status read resolved for this operation (found, unknown or expired).</summary>
        public OperationReadOutcome StatusOutcome { get; set; }

        /// <summary>True when the lane holds a terminal result for this operation.</summary>
        public bool HasResult { get; set; }

        /// <summary>The operation's own terminal outcome; it is the publication result and is never restated.</summary>
        public Outcome OperationOutcome { get; set; }

        public DiagnosticCode OperationCode { get; set; }

        public SnapshotToken? OperationToken { get; set; }

        public WorldLifecycleState WorldLifecycle { get; set; }

        /// <summary>True once the world latched a fault; it stays true after teardown (P-031).</summary>
        public bool WorldFaulted { get; set; }

        public DiagnosticCode WorldFaultCode { get; set; }

        public string WorldFaultDetail { get; set; } = string.Empty;

        public int WorldFaultCount { get; set; }

        /// <summary>The last committed logical step; a failed step never advances it (P-044).</summary>
        public LogicalStepId LastCommittedStep { get; set; }

        public SnapshotToken? LastCommittedToken { get; set; }

        public int CommittedStepCount { get; set; }

        public int PublishedImageCount { get; set; }

        /// <summary>True when the operation published and the world faulted during its execution.</summary>
        public bool FaultedAfterPublication => WorldFaulted && HasResult && OperationOutcome == Outcome.Published;

        /// <summary>True when the world faults and the lane has no result: the fault belongs to no admitted edit.</summary>
        public bool FaultedWithoutPublishedOperation => WorldFaulted && (!HasResult || OperationOutcome != Outcome.Published);
    }

    /// <summary>
    /// Joins one world's control lane to one owned Unity world. Both authorities are passed in; the bridge never
    /// creates a second host, a second lane or a second world for the same session (P-002, P-004).
    /// </summary>
    public sealed class WorldCompositionBridge
    {
        public WorldCompositionBridge(UnityWorldHost world, CompositionHost composition)
            : this(world, composition, null)
        {
        }

        /// <summary>
        /// Joins one lane to one world, optionally through the world's assembly publisher (P-006, P-030). A joined
        /// lane must already agree with the world it is joined to, which for a world that has published only its
        /// initial assembly means the seed `CompositionLaneSeed.InitialAssembly`; a lane that disagrees is refused
        /// rather than published on top of a series it does not share.
        /// </summary>
        public WorldCompositionBridge(
            UnityWorldHost world,
            CompositionHost composition,
            AssemblyPublisher? publisher)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Composition = composition ?? throw new ArgumentNullException(nameof(composition));
            Publisher = publisher;

            if (!composition.World.Session.Equals(world.World.Session))
            {
                // A lane and a world joined by the bridge must be the same world incarnation (P-004).
                throw new ArgumentException(
                    "The control lane belongs to another world incarnation than the host (P-004).",
                    nameof(composition));
            }

            if (publisher != null && !ReferenceEquals(publisher.World, world))
            {
                throw new ArgumentException(
                    "The assembly publisher belongs to another world incarnation than the host (P-004).",
                    nameof(publisher));
            }

            if (!AssemblyPublisher.MatchesPublishedAssembly(
                    composition.Committed.Revision,
                    composition.Committed.Epoch,
                    Publisher != null ? Publisher.PublishedRevision : world.PublishedCompositionRevision,
                    world.CurrentEpoch))
            {
                // P-006: the lane and the world publish one series. A lane that was not seeded from the world would
                // otherwise report an epoch the world never publishes, which is the split this join removes.
                throw new ArgumentException(
                    "The control lane publishes revision "
                    + composition.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + composition.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + " but its world publishes revision "
                    + (Publisher != null ? Publisher.PublishedRevision : world.PublishedCompositionRevision)
                        .Value.ToString(CultureInfo.InvariantCulture)
                    + "/epoch " + world.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                    + "; a joined lane must be seeded from the world it joins (P-006).",
                    nameof(composition));
            }
        }

        public UnityWorldHost World { get; }

        public CompositionHost Composition { get; }

        /// <summary>The world's assembly publisher, when this bridge is the live publication path (P-030).</summary>
        public AssemblyPublisher? Publisher { get; }

        /// <summary>Admissions whose composition publication the world could not advance to (P-006).</summary>
        public int PublicationJoinRefusalCount { get; private set; }

        public DiagnosticCode LastPublicationJoinCode { get; private set; }

        public string LastPublicationJoinDetail { get; private set; } = string.Empty;

        /// <summary>Operations this bridge admitted and handed to the world.</summary>
        public int SubmittedCount { get; private set; }

        /// <summary>Attempts the lane refused to admit (idempotency conflict, expired result or lane capacity).</summary>
        public int AdmissionRejectedCount { get; private set; }

        /// <summary>Attempts recognised as retransmissions of an already-admitted operation (P-050).</summary>
        public int RetransmissionCount { get; private set; }

        /// <summary>Attempts refused because the world could not execute them.</summary>
        public int RefusedCount { get; private set; }

        public DiagnosticCode LastRefusalCode { get; private set; }

        public string LastRefusalDetail { get; private set; } = string.Empty;

        /// <summary>Committed steps handed to the lane's retention window by <see cref="SyncStepFromWorld"/>.</summary>
        public LogicalStepId LastSyncedStep { get; private set; } = LogicalStepId.Zero;

        public int SyncCount { get; private set; }

        /// <summary>
        /// Admits one edit on the real control lane, publishes it at the boundary and hands the admitted result to
        /// the world as exactly one unit of command demand. A world that cannot execute refuses before admission.
        /// </summary>
        public WorldAdmissionReport SubmitAndExecute(CompositionEditPayload payload, OperationId operation)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            CompositionRevision expected = Composition.Committed.Revision;

            if (World.Lifecycle != WorldLifecycleState.Running && World.Lifecycle != WorldLifecycleState.Paused)
            {
                // Refused before admission: no ledger row, no retention entry, no issuer sequence consumed.
                DiagnosticCode code = World.FaultCode == DiagnosticCode.None
                    ? DiagnosticCode.ApplyFault
                    : World.FaultCode;
                RefusedCount++;
                LastRefusalCode = code;
                LastRefusalDetail = "world " + World.World.Session.ToString() + " is " + World.Lifecycle
                    + " (" + DiagnosticCodeText.Of(code) + "); it accepts no further work (P-031). "
                    + World.FaultDetail;

                return new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.WorldRefused,
                    RefusalCode = code,
                    RefusalDetail = LastRefusalDetail,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                };
            }

            EditAdmission admission = Composition.SubmitEdit(payload, operation, expected);
            if (admission.Kind == AdmissionKind.Retransmission)
            {
                // The lane already admitted this exact attempt: it returns the original row and starts no new
                // work, so the bridge must not turn it into a second unit of execution demand (P-050).
                RetransmissionCount++;
                OperationLedgerEntry? original = Composition.Read(admission.Handle).Entry;

                return new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.Retransmission,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    Admission = admission.Kind,
                    AdmissionCode = admission.Code,
                    Staged = false,
                    PublicationOutcome = original != null ? original.Outcome : Outcome.Pending,
                    PublicationCode = original != null ? original.Code : DiagnosticCode.None,
                    PublishedToken = original != null ? original.PublishedSnapshot : null,
                };
            }

            if (!admission.Staged)
            {
                AdmissionRejectedCount++;
                return new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.AdmissionRejected,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    Admission = admission.Kind,
                    AdmissionCode = admission.Code,
                    Staged = false,
                    PublicationOutcome = Outcome.Rejected,
                    PublicationCode = admission.Code,
                };
            }

            IReadOnlyList<PublishedOperation> published = Composition.Drain();
            PublishedOperation? mine = null;
            for (int i = 0; i < published.Count; i++)
            {
                if (published[i].Operation.Equals(operation))
                {
                    mine = published[i];
                    break;
                }
            }

            bool tokenOwnedByWorld = mine != null
                && mine.Token != null
                && mine.Token.Value.World.Session.Equals(World.World.Session);
            if (mine == null || !tokenOwnedByWorld)
            {
                // A publication that produced no token, or a token naming another incarnation, is not demand this
                // world may execute: the handoff is refused instead of guessed.
                return new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.PublicationRejected,
                    RefusalCode = DiagnosticCode.StaleHandle,
                    RefusalDetail = "the publication boundary produced no token owned by this world incarnation",
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    Admission = admission.Kind,
                    AdmissionCode = admission.Code,
                    Staged = true,
                    PublicationOutcome = mine != null ? mine.Outcome : Outcome.Rejected,
                    PublicationCode = mine != null ? mine.Code : DiagnosticCode.StaleHandle,
                    PublishedToken = mine != null ? mine.Token : null,
                };
            }

            // P-006 has one publication series: the composition publication just published must become the world's
            // next assembly, so the epoch the operation reports is the epoch the world publishes. A world that cannot
            // advance to it refuses the handoff and gains no demand (the lane keeps its honest published result).
            CompositionPublicationJoin join = JoinPublishedComposition(mine!.Token!.Value);
            if (!join.Joined)
            {
                PublicationJoinRefusalCount++;
                LastPublicationJoinCode = join.Code;
                LastPublicationJoinDetail = join.Detail;

                return new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.PublicationRejected,
                    RefusalCode = join.Code,
                    RefusalDetail = join.Detail,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    Admission = admission.Kind,
                    AdmissionCode = admission.Code,
                    Staged = true,
                    PublicationOutcome = mine.Outcome,
                    PublicationCode = mine.Code,
                    PublishedToken = mine.Token,
                };
            }

            // The admitted and published result becomes demand; the guarded driver turns one admitted command into
            // exactly one logical step (P-036, P-037). The bridge does not run the step itself.
            World.NotifyCommandAdmitted(1U);
            SubmittedCount++;

            return new WorldAdmissionReport
            {
                Operation = operation,
                Outcome = BridgeOutcome.Executed,
                ExpectedRevision = expected,
                LaneRowCount = Composition.OperationLedger.RowCount,
                Admission = admission.Kind,
                AdmissionCode = admission.Code,
                Staged = true,
                PublicationOutcome = mine!.Outcome,
                PublicationCode = mine.Code,
                PublishedToken = mine.Token,
                CommandSubmitted = true,
                DemandAfter = World.PendingDemand,
            };
        }

        /// <summary>
        /// Joins one published composition operation to the world's assembly series (P-006). With an assembly
        /// publisher this is an adoption the publisher asserts at its next publication; without one (the W1 path) the
        /// world's epoch mirror advances to the composition publication itself, because the lane and the world
        /// publish the same series. Either way a publication that is not exactly the next assembly is refused.
        /// </summary>
        private CompositionPublicationJoin JoinPublishedComposition(SnapshotToken token)
        {
            AssemblyEpoch laneEpoch = token.AssemblyEpoch;

            if (Publisher != null)
            {
                if (!Publisher.TryAdoptLanePublication(Composition.Committed.Revision, laneEpoch, out _, out DiagnosticCode adoptCode))
                {
                    return new CompositionPublicationJoin(
                        false,
                        adoptCode,
                        "the assembly publisher refused to adopt composition publication revision "
                        + Composition.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/epoch " + laneEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; it must be exactly the next assembly of the world's series (P-006).");
                }

                return new CompositionPublicationJoin(true, DiagnosticCode.None, string.Empty);
            }

            if (!World.TryAdoptPublishedComposition(Composition.Committed.Revision, laneEpoch, out DiagnosticCode code, out string detail))
            {
                return new CompositionPublicationJoin(false, code, detail);
            }

            return new CompositionPublicationJoin(true, DiagnosticCode.None, string.Empty);
        }

        /// <summary>Outcome of joining one published composition operation to the world's assembly series.</summary>
        private readonly struct CompositionPublicationJoin
        {
            public CompositionPublicationJoin(bool joined, DiagnosticCode code, string detail)
            {
                Joined = joined;
                Code = code;
                Detail = detail ?? string.Empty;
            }

            public bool Joined { get; }

            public DiagnosticCode Code { get; }

            public string Detail { get; }
        }

        /// <summary>
        /// Reads what the lane and the world report for one operation now. Nothing is mutated: this is the honest
        /// view a caller needs to see a post-write world fault beside the operation that triggered it.
        /// </summary>
        public WorldExecutionReport Report(OperationId operation)
        {
            OperationResult? result = Composition.ResultOf(operation);
            OperationReadResult read = Composition.Read(
                new OperationStatusHandle(operation, Composition.Committed.Revision));

            PublishedStepImage? last = World.Publications.Last;

            return new WorldExecutionReport
            {
                Operation = operation,
                StatusOutcome = read.Outcome,
                HasResult = result != null,
                OperationOutcome = result != null ? result.Outcome : Outcome.Pending,
                OperationCode = result != null ? result.Code : read.Code,
                OperationToken = result != null ? result.PublishedSnapshot : null,
                WorldLifecycle = World.Lifecycle,
                WorldFaulted = World.FaultCount > 0,
                WorldFaultCode = World.FaultCode,
                WorldFaultDetail = World.FaultDetail,
                WorldFaultCount = World.FaultCount,
                LastCommittedStep = World.CurrentStep,
                LastCommittedToken = last != null ? (SnapshotToken?)last.Token : null,
                CommittedStepCount = World.Driver.CommittedStepCount,
                PublishedImageCount = World.Publications.PublishedCount,
            };
        }

        /// <summary>
        /// Lets the lane's retention window follow the step the world committed. The world owns the logical step;
        /// the lane only remembers it, so this can never advance the step the world is at (P-006, P-050).
        /// </summary>
        public int SyncStepFromWorld()
        {
            int expired = Composition.AdvanceSteps(World.CurrentStep);
            LastSyncedStep = World.CurrentStep;
            SyncCount++;
            return expired;
        }
    }
}
