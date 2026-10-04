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
//   * SADR-011 (studio): a composition-only edit is NOT a command. An admitted and published edit changes the
//     assembly epoch and publishes a new consistent view without advancing the logical clock (04 s3: "A
//     composition-only publication changes AssemblyEpoch and publishes a new consistent view without advancing the
//     logical clock"), so the bridge hands the world no demand for it. Commands and wakes are the only step demand
//     (P-036, P-037). Until SADR-011 the bridge charged one logical step per edit; that was the W1 shortcut F5 names;
//   * SADR-011: the caller states the composition revision its edit was prepared against
//     (`SubmitAndExecute(payload, operation, expectedRevision)`); a stale expectation is a lane rejection with no
//     publication. The two-argument overload keeps its old meaning by passing the committed revision;
//   * SADR-011 validate-before-commit: when the bridge is joined to the world's derived-assembly pipeline it requires
//     the lane to consult a `DerivedAssemblyPreflightValidator` for that pipeline, so a composition the world would
//     refuse is refused while it is planned - a lane rejection with a structured `BridgeRefusal` - and the lane and
//     the world never diverge. The bridge then publishes the world's assembly for the lane's publication itself
//     (derived, or unchanged when derivation moved no target), so one call leaves both halves of the one P-006
//     series joined;
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
        /// <summary>
        /// Admitted and published, and the world's assembly joined to the publication. Since SADR-011 this hands the
        /// world no step demand: a composition edit changes the epoch, never the logical step.
        /// </summary>
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

    /// <summary>Which step of one bridge attempt refused it (SADR-011).</summary>
    public enum BridgeRefusalPhase
    {
        None = 0,

        /// <summary>The world cannot execute (faulted, stopping, disposed); nothing was admitted (P-031).</summary>
        WorldState = 1,

        /// <summary>
        /// The lane refused to admit the attempt before planning it: the expected revision is not the published one
        /// (stale), the operation identity conflicts, the lane is full, or unpublished proposals of another caller
        /// are pending. No ledger publication happened (P-028, P-050).
        /// </summary>
        Admission = 2,

        /// <summary>The lane's own planner or a non-world validator refused the proposal; the lane stayed published.</summary>
        CompositionPlan = 3,

        /// <summary>
        /// Validate-before-commit: the world's dry run (derive, translate, plan) refused the proposed composition, so
        /// the lane rejected it while planning. Lane and world both kept their published assembly.
        /// </summary>
        WorldPreflight = 4,

        /// <summary>The lane's publication boundary produced no token owned by this world, or the join was refused.</summary>
        Publication = 5,

        /// <summary>
        /// The world refused its assembly after the lane published. With validate-before-commit this is reachable only
        /// for what no dry run can see - a step in progress or a fault after the first live write (P-030, P-031) -
        /// and the report says so rather than claiming a rollback.
        /// </summary>
        WorldPublication = 6,
    }

    /// <summary>
    /// The structured refusal of one bridge attempt (SADR-011): the protocol code, the phase that refused and the
    /// witness that phase reported (the conflict, the plan state, the stale revision pair).
    /// </summary>
    public sealed class BridgeRefusal
    {
        public BridgeRefusal(DiagnosticCode code, BridgeRefusalPhase phase, string witness)
        {
            Code = code;
            Phase = phase;
            Witness = witness ?? string.Empty;
        }

        public DiagnosticCode Code { get; }

        public BridgeRefusalPhase Phase { get; }

        public string Witness { get; }

        /// <summary>True when the refusal left the lane and the world on one published series (every phase but one).</summary>
        public bool KeptOneSeries => Phase != BridgeRefusalPhase.WorldPublication;

        public override string ToString() =>
            "bridgeRefusal(" + DiagnosticCodeText.Of(Code) + ", " + Phase.ToString() + ": " + Witness + ")";
    }


    /// <summary>
    /// Observation record of one <see cref="WorldCompositionBridge.SubmitAndExecute(CompositionEditPayload, OperationId)"/> attempt: the control-lane
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

        /// <summary>
        /// True when the bridge handed the world a unit of command demand. Always false since SADR-011: a
        /// composition-only edit publishes an assembly epoch and is not a command, so it never charges a logical step
        /// (04 s3, P-036). Kept so existing readers observe the new semantics instead of failing to compile.
        /// </summary>
        public bool CommandSubmitted { get; set; }

        /// <summary>The world's pending demand after the attempt; an edit leaves it exactly as it found it.</summary>
        public ulong DemandAfter { get; set; }

        /// <summary>The world's committed logical step after the attempt; an edit never advances it (SADR-011).</summary>
        public LogicalStepId StepAfter { get; set; }

        /// <summary>Structured refusal: code, the phase that refused and its witness; null when nothing refused.</summary>
        public BridgeRefusal? Refusal { get; set; }

        /// <summary>
        /// The world's half of the publication when the bridge is joined to the derived-assembly pipeline: the
        /// derivation, plan and publication reports of the assembly published for this edit. Null otherwise.
        /// </summary>
        public DerivedAssemblyReport? Assembly { get; set; }

        /// <summary>The unchanged-assembly publication answering an edit whose derivation moved no target.</summary>
        public AssemblyPublicationReport? UnchangedAssembly { get; set; }

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

        /// <summary>
        /// SADR-011: joins one lane to one world through the world's publisher and its derived-assembly pipeline. The
        /// lane must consult a <see cref="DerivedAssemblyPreflightValidator"/> attached to <paramref name="pipeline"/>
        /// (alone or inside a <see cref="CompositionEditValidatorSet"/>): validate-before-commit is a construction
        /// invariant of this path, not an option a caller can forget.
        /// </summary>
        public WorldCompositionBridge(
            UnityWorldHost world,
            CompositionHost composition,
            AssemblyPublisher publisher,
            DerivedAssemblyPipeline pipeline)
            : this(world, composition, publisher)
        {
            if (publisher == null)
            {
                throw new ArgumentNullException(nameof(publisher));
            }

            if (pipeline == null)
            {
                throw new ArgumentNullException(nameof(pipeline));
            }

            if (!ReferenceEquals(pipeline.World, world)
                || !ReferenceEquals(pipeline.Lane, composition)
                || !ReferenceEquals(pipeline.Publisher, publisher))
            {
                throw new ArgumentException(
                    "The derived-assembly pipeline belongs to another world, lane or publisher (P-004).",
                    nameof(pipeline));
            }

            DerivedAssemblyPreflightValidator? preflight = DerivedAssemblyPreflightValidator.Of(composition.Validator);
            if (preflight == null || !ReferenceEquals(preflight.Pipeline, pipeline))
            {
                throw new ArgumentException(
                    "Validate-before-commit requires the lane to consult a DerivedAssemblyPreflightValidator attached "
                    + "to this pipeline; without it a world-side refusal would follow a lane publication (SADR-011).",
                    nameof(composition));
            }

            Pipeline = pipeline;
            Preflight = preflight;
        }

        public UnityWorldHost World { get; }

        public CompositionHost Composition { get; }

        /// <summary>The world's assembly publisher, when this bridge is the live publication path (P-030).</summary>
        public AssemblyPublisher? Publisher { get; }

        /// <summary>
        /// The world's derived-assembly pipeline when this bridge validates before committing and publishes the
        /// world's assembly itself (SADR-011); null on the W1 and adopt-only paths.
        /// </summary>
        public DerivedAssemblyPipeline? Pipeline { get; }

        /// <summary>The lane's world preflight this bridge relies on; non-null exactly when <see cref="Pipeline"/> is.</summary>
        public DerivedAssemblyPreflightValidator? Preflight { get; }

        /// <summary>Lane rejections that came from the world preflight: refusals that never split lane and world.</summary>
        public int PreflightRefusalCount { get; private set; }

        /// <summary>Attempts refused because the expected revision was not the published one (P-028).</summary>
        public int StaleExpectationCount { get; private set; }

        /// <summary>The structured refusal of the most recent refused attempt, or null before one.</summary>
        public BridgeRefusal? LastRefusal { get; private set; }

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
        /// Admits one edit on the real control lane against the currently published composition revision and
        /// publishes it at the boundary. Equivalent to the three-argument overload with
        /// <c>Composition.Committed.Revision</c> as the expectation, which is what this overload always meant.
        /// </summary>
        public WorldAdmissionReport SubmitAndExecute(CompositionEditPayload payload, OperationId operation)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            return SubmitAndExecute(payload, operation, Composition.Committed.Revision);
        }

        /// <summary>
        /// Admits one edit prepared against <paramref name="expectedRevision"/>, publishes it at the boundary and
        /// joins the world's assembly to that publication (SADR-011):
        /// <list type="bullet">
        /// <item>a world that cannot execute refuses before admission (no ledger row, P-031);</item>
        /// <item>a stale expectation is a lane rejection with code <see cref="DiagnosticCode.StalePlan"/> and no
        /// publication (P-028);</item>
        /// <item>with a joined pipeline the lane's world preflight has already derived and planned the proposal, so a
        /// world-side refusal is a lane-side rejection (<see cref="BridgeRefusalPhase.WorldPreflight"/>);</item>
        /// <item>a published edit changes the assembly epoch only: no command demand, no logical step.</item>
        /// </list>
        /// </summary>
        public WorldAdmissionReport SubmitAndExecute(
            CompositionEditPayload payload,
            OperationId operation,
            CompositionRevision expectedRevision)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            CompositionRevision expected = expectedRevision;

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

                return Refused(new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.WorldRefused,
                    RefusalCode = code,
                    RefusalDetail = LastRefusalDetail,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                }, new BridgeRefusal(code, BridgeRefusalPhase.WorldState, LastRefusalDetail));
            }

            if (Pipeline != null && Composition.OperationLedger.PendingInAdmissionOrder().Count != 0)
            {
                // The bridge publishes one edit and the world's assembly for exactly that publication. Proposals a
                // different caller staged on the lane would be drained with it and leave publications the world never
                // answered, which is the split this path exists to prevent (P-006).
                AdmissionRejectedCount++;
                string pendingDetail = "the lane holds "
                    + Composition.OperationLedger.PendingInAdmissionOrder().Count.ToString(CultureInfo.InvariantCulture)
                    + " unpublished proposal(s) submitted outside this bridge; the bridge publishes one edit and its "
                    + "assembly at a time (P-006, SADR-011)";
                return Refused(new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.AdmissionRejected,
                    RefusalCode = DiagnosticCode.TooLate,
                    RefusalDetail = pendingDetail,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    PublicationOutcome = Outcome.Rejected,
                    PublicationCode = DiagnosticCode.TooLate,
                }, new BridgeRefusal(DiagnosticCode.TooLate, BridgeRefusalPhase.Admission, pendingDetail));
            }

            int preflightRefusalsBefore = Preflight != null ? Preflight.Refusals : 0;
            CompositionRevision publishedBefore = Composition.Committed.Revision;
            EditAdmission admission = Composition.SubmitEdit(payload, operation, expected);
            if (admission.Kind == AdmissionKind.Retransmission)
            {
                // The lane already admitted this exact attempt: it returns the original row and starts no new
                // work, so the bridge must not turn it into a second publication or a second unit of demand (P-050).
                RetransmissionCount++;
                OperationLedgerEntry? original = Composition.Read(admission.Handle).Entry;

                return Finish(new WorldAdmissionReport
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
                });
            }

            if (!admission.Staged)
            {
                AdmissionRejectedCount++;
                var rejected = new WorldAdmissionReport
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

                if (admission.Code == DiagnosticCode.None)
                {
                    // A NoChange plan settles without a publication and without a refusal (P-006).
                    return Finish(rejected);
                }

                return Refused(rejected, ClassifyLaneRefusal(admission, expected, publishedBefore, preflightRefusalsBefore));
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
                // A publication that produced no token, or a token naming another incarnation, is not work this world
                // may adopt: the handoff is refused instead of guessed.
                const string noToken = "the publication boundary produced no token owned by this world incarnation";
                return Refused(new WorldAdmissionReport
                {
                    Operation = operation,
                    Outcome = BridgeOutcome.PublicationRejected,
                    RefusalCode = DiagnosticCode.StaleHandle,
                    RefusalDetail = noToken,
                    ExpectedRevision = expected,
                    LaneRowCount = Composition.OperationLedger.RowCount,
                    Admission = admission.Kind,
                    AdmissionCode = admission.Code,
                    Staged = true,
                    PublicationOutcome = mine != null ? mine.Outcome : Outcome.Rejected,
                    PublicationCode = mine != null ? mine.Code : DiagnosticCode.StaleHandle,
                    PublishedToken = mine != null ? mine.Token : null,
                }, new BridgeRefusal(
                    mine != null && mine.Code != DiagnosticCode.None ? mine.Code : DiagnosticCode.StaleHandle,
                    BridgeRefusalPhase.Publication,
                    noToken));
            }

            var report = new WorldAdmissionReport
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
            };

            // P-006 has one publication series: the composition publication just published must become the world's
            // next assembly, so the epoch the operation reports is the epoch the world publishes.
            if (Pipeline != null)
            {
                if (!PublishWorldAssembly(operation, report))
                {
                    return Refused(report, report.Refusal!);
                }
            }
            else
            {
                CompositionPublicationJoin join = JoinPublishedComposition(mine.Token!.Value);
                if (!join.Joined)
                {
                    PublicationJoinRefusalCount++;
                    LastPublicationJoinCode = join.Code;
                    LastPublicationJoinDetail = join.Detail;
                    report.Outcome = BridgeOutcome.PublicationRejected;
                    report.RefusalCode = join.Code;
                    report.RefusalDetail = join.Detail;
                    return Refused(report, new BridgeRefusal(join.Code, BridgeRefusalPhase.Publication, join.Detail));
                }
            }

            // SADR-011: the published edit is an assembly epoch, not a command. The world's logical step and its
            // pending demand are exactly what they were before the edit (04 s3, P-036, P-037).
            SubmittedCount++;
            return Finish(report);
        }

        /// <summary>
        /// The world's half of one lane publication on the pipeline path: the derived assembly, or the unchanged
        /// assembly when derivation moved no target, published at the lane's own revision/epoch (P-006). The lane's
        /// world preflight validated the same composition before it committed, so a refusal here is a P-030/P-031
        /// outcome (a step in progress, a postwrite fault), reported as such and never as a rollback.
        /// </summary>
        private bool PublishWorldAssembly(OperationId operation, WorldAdmissionReport report)
        {
            DerivedAssemblyPipeline pipeline = Pipeline!;
            AssemblyPublisher publisher = Publisher!;
            DerivedAssemblyReport assembly = pipeline.PublishDerived(operation);
            report.Assembly = assembly;

            if (assembly.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                    operation, Composition.Committed.Revision, Composition.Committed.Epoch);
                report.UnchangedAssembly = unchanged;
                if (!unchanged.Published)
                {
                    return WorldPublicationRefused(report, unchanged.Code,
                        "the unchanged assembly for composition publication "
                        + Composition.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + " was refused after the lane published: " + unchanged.Detail);
                }
            }
            else if (assembly.Outcome == DerivedAssemblyOutcome.Refused)
            {
                return WorldPublicationRefused(report, assembly.Code,
                    "the world refused the assembly after the lane published (the preflight accepted it): "
                    + assembly.Describe());
            }

            if (!AssemblyPublisher.MatchesPublishedAssembly(
                    Composition.Committed.Revision,
                    Composition.Committed.Epoch,
                    publisher.PublishedRevision,
                    World.CurrentEpoch))
            {
                return WorldPublicationRefused(report, DiagnosticCode.StalePlan,
                    "the lane publishes revision "
                    + Composition.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the world publishes revision "
                    + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + " after the join (P-006)");
            }

            return true;
        }

        private bool WorldPublicationRefused(WorldAdmissionReport report, DiagnosticCode code, string witness)
        {
            DiagnosticCode refusalCode = code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : code;
            PublicationJoinRefusalCount++;
            LastPublicationJoinCode = refusalCode;
            LastPublicationJoinDetail = witness;
            report.Outcome = BridgeOutcome.PublicationRejected;
            report.RefusalCode = refusalCode;
            report.RefusalDetail = witness;
            report.Refusal = new BridgeRefusal(refusalCode, BridgeRefusalPhase.WorldPublication, witness);
            return false;
        }

        /// <summary>
        /// Names the step that refused a lane admission: a stale expectation or a ledger refusal is
        /// <see cref="BridgeRefusalPhase.Admission"/>, a refusal by the world preflight during this call is
        /// <see cref="BridgeRefusalPhase.WorldPreflight"/>, and anything else the lane's planner or validators
        /// reported is <see cref="BridgeRefusalPhase.CompositionPlan"/>.
        /// </summary>
        private BridgeRefusal ClassifyLaneRefusal(
            EditAdmission admission,
            CompositionRevision expected,
            CompositionRevision publishedBefore,
            int preflightRefusalsBefore)
        {
            if (admission.Kind != AdmissionKind.Fresh)
            {
                return new BridgeRefusal(admission.Code, BridgeRefusalPhase.Admission,
                    "the lane refused to admit the operation (" + admission.Kind.ToString() + ")");
            }

            if (admission.Code == DiagnosticCode.StalePlan && !expected.Equals(publishedBefore))
            {
                StaleExpectationCount++;
                return new BridgeRefusal(DiagnosticCode.StalePlan, BridgeRefusalPhase.Admission,
                    "the edit was prepared against revision " + expected.Value.ToString(CultureInfo.InvariantCulture)
                    + " but the lane publishes revision " + publishedBefore.Value.ToString(CultureInfo.InvariantCulture)
                    + "; regenerate it against the published revision with a new operation id (P-028, P-050)");
            }

            if (Preflight != null && Preflight.Refusals > preflightRefusalsBefore)
            {
                PreflightRefusalCount++;
                return new BridgeRefusal(admission.Code, BridgeRefusalPhase.WorldPreflight, Preflight.LastRefusalDetail);
            }

            string witness = admission.Diagnostics.Count != 0
                ? admission.Diagnostics[0].ToString()
                : "the lane's planner refused the proposal";
            return new BridgeRefusal(admission.Code, BridgeRefusalPhase.CompositionPlan, witness);
        }

        private WorldAdmissionReport Refused(WorldAdmissionReport report, BridgeRefusal refusal)
        {
            report.Refusal = refusal;
            LastRefusal = refusal;
            return Finish(report);
        }

        private WorldAdmissionReport Finish(WorldAdmissionReport report)
        {
            report.CommandSubmitted = false;
            report.DemandAfter = World.PendingDemand;
            report.StepAfter = World.CurrentStep;
            return report;
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
