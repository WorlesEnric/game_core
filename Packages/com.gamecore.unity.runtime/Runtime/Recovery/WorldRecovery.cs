// GameCore.Unity.Runtime.Recovery - O-22 RecoverWorld (GC-027).
//
// Normative sources: 00 O-22 ("Host; Faulted world, checkpoint or initial definition -> stopped old world plus new
// session. Preconditions: C; requires explicit source, reuse Restore/Create procedures; old storage never resumes,
// blocked old resources retained until safe. Failure: Failure leaves old Faulted and reports new attempt failure; no
// implicit effects replay; cancel only before new publication. Probe: mid-apply fault recovery yields new identity
// and last checkpoint state."), P-049 ("Pre-mutation failure leaves the old published world usable... Post-mutation/
// step failure halts simulation... Recovery creates a new `WorldId` from a verified checkpoint or initial catalog,
// validates/rebuilds composition and recipes, restores state, then reopens admission; old callbacks/handles never
// become valid. No hidden automatic replay of external side effects is allowed. Retryable transient resource
// failures use host-configured bounded attempts with new operation IDs; correctness failures require changed
// input/catalog."), P-053 ("Restore validates everything into a new unexposed world before publication"), P-054
// (serialization discipline) and 06 s7 ("Recovery of a Faulted world uses this same new-world procedure. The old
// world can remain quarantined if a native user is still alive; it is never resumed.").
//
// WHAT THIS FILE IS
//
// The composition the W5 and W6 gates both recorded as missing: `O-22 RecoverWorld` was "not composed", and P-049's
// host-configured bounded retries were "unproven" because nothing consumed
// `OperationExpirySettings.BoundedTransientAttempts`. This file is the one entry point that
//
//   * takes an EXPLICIT source - a verified checkpoint document in a store, or the compiled catalog's initial
//     definitions - and never infers one;
//   * REUSES the two procedures that already exist instead of re-implementing either: `CheckpointRestoreExecutor`
//     (O-21's reserve -> read -> plan -> build unexposed -> validate -> expose) and `InitialDefinitionRecovery`
//     (GC-017's faulted-world-from-initial-definitions path);
//   * stops the old faulted world and never resumes it, reporting a blocked stop as the retained/quarantined
//     outcome P-048 requires rather than retrying it;
//   * retries only a retryable transient failure, only within the host's configured bound, and always with a NEW
//     reserved session and a NEW operation id (P-049, P-050);
//   * replays NO external effect: reinstating an outbox row re-owes the obligation, it never delivers it, and the
//     report counts what is still owed rather than what was sent (P-045, P-049).
//
// WHAT IT DELIBERATELY DOES NOT CLAIM
//
//   * It is not a historical rollback: the source is recoverable only from the checkpoint it actually has, so the
//     data-loss boundary is "everything committed after that checkpoint", and the report states it by class.
//   * It does not resume, repair or write to the faulted world's storage. Reading its lifecycle, fault code and
//     identity is the whole of the contact, and the source is re-read after the attempt to prove it.
//   * It does not decide what a destination's mutation means. Delivery is the destination port's job (P-003), so a
//     recovery ends with obligations owed and a cursor restored, never with a delivery.
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
using GameCore.Unity.Runtime.Persistence;
#if GAMECORE_FAULT_INJECTION
// GC-027's own latch reaches (capture copy, publication, reference repair). The whole latch lives inside the same
// guard, so a shipping compilation has no such namespace to import and no reach call to make.
using GameCore.Unity.Runtime.Faults;
#endif

namespace GameCore.Unity.Runtime.Recovery
{
    /// <summary>Which side of one recovery attempt a <see cref="WorldRecoveryContext.HealthCheck"/> is called on.</summary>
    public enum RecoveryHealthPhase
    {
        /// <summary>Before the destination is staged; only the source world's latch is reachable here.</summary>
        BeforeAttempt = 0,

        /// <summary>After the destination was published; the recovered world's latch is reachable here.</summary>
        AfterAttempt = 1,
    }

    /// <summary>
    /// The two explicit sources O-22 allows, as one request: a checkpoint store, or the initial definitions of a
    /// world definition. Both are read by the caller; neither is discovered (P-049, P-053).
    /// </summary>
    public sealed class WorldRecoveryRequest
    {
        public WorldRecoveryRequest(
            WorldId source,
            WorldDefinitionId definition,
            TemporalModel temporalModel,
            PropagationMode mode,
            ContentHash catalogHash,
            ICheckpointStore? store,
            CheckpointMigrationRegistry? migrations = null,
            IReadOnlyList<SchemaRef>? allocatedSchemas = null,
            bool requireCatalogMatch = true,
            FixedStepSettings? fixedStep = null)
        {
            Source = source;
            Definition = definition;
            TemporalModel = temporalModel;
            Mode = mode;
            CatalogHash = catalogHash;
            Store = store;
            Migrations = migrations;
            AllocatedSchemas = allocatedSchemas;
            RequireCatalogMatch = requireCatalogMatch;
            FixedStep = fixedStep;
        }

        /// <summary>The world incarnation being recovered from; it is never resumed (P-031, P-049).</summary>
        public WorldId Source { get; }

        public WorldDefinitionId Definition { get; }

        public TemporalModel TemporalModel { get; }

        public PropagationMode Mode { get; }

        public ContentHash CatalogHash { get; }

        /// <summary>
        /// The verified-document source, or null for the initial-definition source. O-22's "requires explicit
        /// source" is this field: a recovery never guesses between the two.
        /// </summary>
        public ICheckpointStore? Store { get; }

        /// <summary>Directed migration graph of the destination catalog (P-054); required for a checkpoint source.</summary>
        public CheckpointMigrationRegistry? Migrations { get; }

        /// <summary>Schemas the destination catalog allocates, so a captured schema is migrated or refused (P-054).</summary>
        public IReadOnlyList<SchemaRef>? AllocatedSchemas { get; }

        /// <summary>True requires the document's catalog fingerprint to equal <see cref="CatalogHash"/> (P-028).</summary>
        public bool RequireCatalogMatch { get; }

        /// <summary>Required for <see cref="TemporalModel.FixedStep"/>; null for a command-driven world.</summary>
        public FixedStepSettings? FixedStep { get; }

        /// <summary>The declared source kind, derived from the request rather than passed twice.</summary>
        public RecoverySourceKind Kind => Store == null ? RecoverySourceKind.InitialDefinition : RecoverySourceKind.Checkpoint;

        /// <summary>A checkpoint request additionally needs its migration graph (P-054).</summary>
        public bool IsValid =>
            !Source.Session.IsDefault
            && !Definition.IsDefault
            && (TemporalModel != TemporalModel.FixedStep || (FixedStep != null && FixedStep.IsValid))
            && (Kind == RecoverySourceKind.InitialDefinition || Migrations != null);

        public override string ToString() =>
            "recoveryRequest(" + Kind.ToString() + "," + Source.Session.ToString() + ")";
    }

    /// <summary>
    /// Everything one recovery call needs from its caller: where to reserve sessions and operation ids, how a
    /// destination world is rebuilt, the host's retry bound and the transcript to write into. It is one object
    /// because a recovery given half of it would silently skip a step (P-051's discipline applied to a host
    /// operation).
    /// </summary>
    public sealed class WorldRecoveryContext
    {
        /// <summary>
        /// An optional probe of the world build, called once per attempt before the attempt is staged and once after
        /// it is published. A qualification fixture uses the second call to arm a latch on the recovered world
        /// (whose handle the caller does not hold while an attempt is in flight); production callers leave it null.
        /// </summary>
        public delegate void HealthCheck(RecoveryHealthPhase phase, int attemptOrdinal, UnityWorldHost? world);

        public WorldRecoveryContext(
            WorldRecoveryRequest request,
            UnityWorldRegistration registration,
            Func<WorldId> reserveSession,
            Func<WorldId, OperationId> reserveOperation,
            RestoreReservationLedger reservations,
            CheckpointCodecSet codecs,
            Func<WorldId, OperationId, IRestoreTargetBuilder> builderFactory,
            RecoveryRetryPolicy? retryPolicy = null,
            RecoveryTranscript? transcript = null,
            Func<WorldId, DurableOutbox?>? outboxProbe = null,
            HealthCheck? healthCheck = null)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Registration = registration ?? throw new ArgumentNullException(nameof(registration));
            ReserveSession = reserveSession ?? throw new ArgumentNullException(nameof(reserveSession));
            ReserveOperation = reserveOperation ?? throw new ArgumentNullException(nameof(reserveOperation));
            Reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            Codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
            BuilderFactory = builderFactory ?? throw new ArgumentNullException(nameof(builderFactory));
            RetryPolicy = retryPolicy ?? RecoveryRetryPolicy.SingleAttempt;
            Transcript = transcript ?? new RecoveryTranscript(64);
            OutboxProbe = outboxProbe;
            HealthCheck = healthCheck;
        }

        public WorldRecoveryRequest Request { get; }

        /// <summary>The destination's composition root; the initial-definition source creates through it.</summary>
        public UnityWorldRegistration Registration { get; }

        /// <summary>
        /// Reserves the destination session for one attempt. It MUST return a fresh id per call: P-050 retains a
        /// rejected reservation for the process lifetime, so a retry that reused one would be refused by the ledger.
        /// </summary>
        public Func<WorldId> ReserveSession { get; }

        /// <summary>Reserves the operation id of one attempt; P-049 requires a new id per attempt.</summary>
        public Func<WorldId, OperationId> ReserveOperation { get; }

        /// <summary>The bounded, process-lifetime session reservation ledger of P-050.</summary>
        public RestoreReservationLedger Reservations { get; }

        /// <summary>The committed generated checkpoint codecs; a checkpoint source cannot be read without them.</summary>
        public CheckpointCodecSet Codecs { get; }

        /// <summary>
        /// Builds the destination's rebuilder for ONE attempt. It is a factory because a rebuilder owns the staging
        /// world it created, so a retry needs a new one.
        /// </summary>
        public Func<WorldId, OperationId, IRestoreTargetBuilder> BuilderFactory { get; }

        /// <summary>The host-configured bound on attempts (P-049).</summary>
        public RecoveryRetryPolicy RetryPolicy { get; }

        /// <summary>The transcript this recovery writes into, also reachable from its report.</summary>
        public RecoveryTranscript Transcript { get; }

        /// <summary>
        /// Reads one recovered session's outbox, or null when the session has none. It is how the consistency report
        /// sees live delivery state without this type depending on a genre's delivery assembly (P-045, P-053).
        /// </summary>
        public Func<WorldId, DurableOutbox?>? OutboxProbe { get; }

        public HealthCheck? HealthCheck { get; }
    }

    /// <summary>
    /// What one recovery did, in the terms O-22 asks for: the old world's state before and after, the attempts with
    /// their sessions and operation ids, the new session, what state came across, what delivery is still owed, and
    /// the data-loss boundary the recovery really had (P-049, P-052, TEST-016).
    /// </summary>
    public sealed class WorldRecoveryReport
    {
        internal WorldRecoveryReport(
            WorldRecoveryRequest request,
            RecoveryAttemptLog attempts,
            RecoveryTranscript transcript,
            WorldLifecycleState sourceLifecycleBefore,
            DiagnosticCode sourceFaultCode,
            int sourceFaultCountBefore,
            WorldLifecycleState sourceLifecycleAfter,
            int sourceFaultCountAfter,
            Outcome outcome,
            DiagnosticCode code,
            string detail,
            UnityWorldHost? destinationHost,
            RestoreOutcome? restore,
            OutboxConsistencyReport? outbox,
            RecoveryRestartPoint? restart,
            int registryCountBefore,
            int registryCountAfter)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Attempts = attempts ?? throw new ArgumentNullException(nameof(attempts));
            Transcript = transcript ?? throw new ArgumentNullException(nameof(transcript));
            SourceLifecycleBefore = sourceLifecycleBefore;
            SourceFaultCode = sourceFaultCode;
            SourceFaultCountBefore = sourceFaultCountBefore;
            SourceLifecycleAfter = sourceLifecycleAfter;
            SourceFaultCountAfter = sourceFaultCountAfter;
            Outcome = outcome;
            Code = code;
            Detail = detail ?? string.Empty;
            DestinationHost = destinationHost;
            Restore = restore;
            Outbox = outbox;
            Restart = restart;
            RegistryCountBefore = registryCountBefore;
            RegistryCountAfter = registryCountAfter;
        }

        public WorldRecoveryRequest Request { get; }

        /// <summary>Every attempt this recovery made, in order; the last one is the answer (P-052).</summary>
        public RecoveryAttemptLog Attempts { get; }

        /// <summary>The phases this recovery passed through, with its fault reaches and data-loss boundary.</summary>
        public RecoveryTranscript Transcript { get; }

        public WorldLifecycleState SourceLifecycleBefore { get; }

        /// <summary>The fault the source had; a recovery names what it recovered from (P-049).</summary>
        public DiagnosticCode SourceFaultCode { get; }

        public int SourceFaultCountBefore { get; }

        /// <summary>
        /// The source's state AFTER the attempt. Stopping it is part of O-22's result, so this is normally
        /// `Disposed` (or `Stopping` when a blocked resource pins it) rather than the faulted state it began in.
        /// </summary>
        public WorldLifecycleState SourceLifecycleAfter { get; }

        public int SourceFaultCountAfter { get; }

        public Outcome Outcome { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The new session's world, or null when nothing was published.</summary>
        public UnityWorldHost? DestinationHost { get; }

        /// <summary>The restore outcome of the attempt that published, or null for the initial-definition source.</summary>
        public RestoreOutcome? Restore { get; }

        /// <summary>The outbox consistency report, or null when the recovery was given no outbox probe.</summary>
        public OutboxConsistencyReport? Outbox { get; }

        /// <summary>What a checkpoint-source restart observed, or null for the initial-definition source.</summary>
        public RecoveryRestartPoint? Restart { get; }

        public int RegistryCountBefore { get; }

        public int RegistryCountAfter { get; }

        /// <summary>True when a fresh world was created, published and is running (P-035, P-049).</summary>
        public bool Recovered =>
            Outcome == Outcome.Published
            && DestinationHost != null
            && DestinationHost.Lifecycle == WorldLifecycleState.Running;

        /// <summary>
        /// True when the source was never resumed: it began faulted/stopping/disposed, it is not running or paused
        /// after the attempt, and its fault count did not move (P-031, P-049).
        /// </summary>
        public bool SourceNeverResumed =>
            (SourceLifecycleBefore == WorldLifecycleState.Faulted
                || SourceLifecycleBefore == WorldLifecycleState.Stopping
                || SourceLifecycleBefore == WorldLifecycleState.Disposed)
            && SourceLifecycleAfter != WorldLifecycleState.Running
            && SourceLifecycleAfter != WorldLifecycleState.Paused
            && SourceFaultCountAfter == SourceFaultCountBefore;

        /// <summary>True when the published session is a different incarnation from the source (P-004).</summary>
        public bool DestinationIsFreshIncarnation =>
            DestinationHost != null && !DestinationHost.World.Session.Equals(Request.Source.Session);

        /// <summary>Obligations the recovered session still owes; a recovery delivers nothing (P-049).</summary>
        public int ObligationsOwed => Outbox == null ? 0 : Math.Max(0, Outbox.LiveOpenCount);

        /// <summary>The data-loss class this recovery was actually exposed to, read from its transcript.</summary>
        public RecoveryDataLossClass DataLossBoundary => Transcript.DataLossBoundary();

        /// <summary>True when the outbox rows came across consistently, or when there was nothing to carry.</summary>
        public bool DeliveryStateIntact => Outbox == null || Outbox.Consistent;

        /// <summary>
        /// True when the attempts named distinct sessions and distinct operation ids, which is P-049/P-050's
        /// observable requirement for a retry (a reused session or operation id would be a second attempt under an
        /// identity that already answered).
        /// </summary>
        public bool AttemptIdentitiesAreDistinct => Attempts.AttemptsAreDistinct();

        /// <summary>Canonical multi-line form for an evidence file: the summary, then attempts, transcript, outbox.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("world-recovery source=").Append(Request.Kind.ToString())
                .Append(" outcome=").Append(Outcome.ToString())
                .Append("/").Append(DiagnosticCodeText.Of(Code))
                .Append(" attempts=").Append(Attempts.Count.ToString(CultureInfo.InvariantCulture))
                .Append("(retries=").Append(Attempts.RetryCount.ToString(CultureInfo.InvariantCulture))
                .Append(",distinct=").Append(AttemptIdentitiesAreDistinct ? "1" : "0")
                .Append(") source=").Append(Request.Source.Session.ToString())
                .Append("/").Append(SourceLifecycleBefore.ToString())
                .Append("->").Append(SourceLifecycleAfter.ToString())
                .Append(" fault=").Append(DiagnosticCodeText.Of(SourceFaultCode))
                .Append(" destination=")
                .Append(DestinationHost != null ? DestinationHost.World.Session.ToString() : "<none>")
                .Append(" loss=").Append(DataLossBoundary.ToString())
                .Append(" owed=").Append(ObligationsOwed.ToString(CultureInfo.InvariantCulture))
                .Append(" deliveryIntact=").Append(DeliveryStateIntact ? "1" : "0")
                .Append(" registry=").Append(RegistryCountBefore.ToString(CultureInfo.InvariantCulture))
                .Append("->").Append(RegistryCountAfter.ToString(CultureInfo.InvariantCulture));
            if (Detail.Length != 0)
            {
                text.Append("; ").Append(Detail);
            }

            text.Append('\n').Append(Attempts.Describe());
            text.Append('\n').Append(Transcript.Describe());
            if (Outbox != null)
            {
                text.Append('\n').Append(Outbox.Describe());
            }

            if (Restart != null)
            {
                text.Append('\n').Append(Restart.ToLine());
            }

            return text.ToString();
        }

        public override string ToString() => Outcome + "(" + DiagnosticCodeText.Of(Code) + "): " + Detail;
    }

    /// <summary>
    /// What one capture-and-publish did: the capture, the stored identity, and which injection point (if any) fired.
    /// O-20's publication half lives here because 06 s7 leaves it to the caller, and a recovery cannot be judged
    /// without it (P-053).
    /// </summary>
    public sealed class CheckpointPublicationResult
    {
        internal CheckpointPublicationResult(
            bool captured,
            bool published,
            DiagnosticCode code,
            string detail,
            CheckpointCaptureResult? capture,
            StoredCheckpoint stored,
            string faultPointId)
        {
            Captured = captured;
            Published = published;
            Code = code;
            Detail = detail ?? string.Empty;
            Capture = capture;
            Stored = stored;
            FaultPointId = faultPointId ?? string.Empty;
        }

        public bool Captured { get; }

        public bool Published { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The capture's own result, or null when a fault fired before the copy produced one.</summary>
        public CheckpointCaptureResult? Capture { get; }

        /// <summary>Identity of the published document: its length, envelope version, checksums and SHA-256.</summary>
        public StoredCheckpoint Stored { get; }

        /// <summary>The injection point that fired, or empty when the publication was a clean one (TEST-016).</summary>
        public string FaultPointId { get; }

        public bool Succeeded => Captured && Published;

        public override string ToString() =>
            "publication(captured=" + (Captured ? "1" : "0")
            + ",published=" + (Published ? "1" : "0") + "," + DiagnosticCodeText.Of(Code)
            + (FaultPointId.Length == 0 ? string.Empty : ",at=" + FaultPointId) + ")";
    }

    /// <summary>One capture-and-publish request: the boundary reader, the capture request and the store (O-20).</summary>
    public sealed class CheckpointPublicationRequest
    {
        public CheckpointPublicationRequest(
            ICommittedBoundaryReader reader,
            CheckpointCaptureRequest capture,
            ICheckpointStore store,
            OperationId operation)
        {
            Reader = reader ?? throw new ArgumentNullException(nameof(reader));
            Capture = capture ?? throw new ArgumentNullException(nameof(capture));
            Store = store ?? throw new ArgumentNullException(nameof(store));
            Operation = operation;
        }

        /// <summary>The world's committed-boundary reader; its copy is what the capture-copy fault interrupts.</summary>
        public ICommittedBoundaryReader Reader { get; }

        public CheckpointCaptureRequest Capture { get; }

        public ICheckpointStore Store { get; }

        /// <summary>The operation identity this publication is admitted under (P-050).</summary>
        public OperationId Operation { get; }
    }

    /// <summary>
    /// One capture-and-publish attempt's transcript-only view, so a caller can record the publication without a
    /// report (the recovery path builds its own report).
    /// </summary>
    public static class CheckpointPublication
    {
        /// <summary>
        /// Captures one world's committed boundary and publishes it to a store (06 s7, O-20). This is the
        /// publication half GC-018 left to the caller, and it is where the capture-copy and file-publication faults
        /// are injected: a fault at either point leaves no document, no artifact and a running world (P-049).
        /// </summary>
        public static CheckpointPublicationResult CaptureAndPublish(
            CheckpointPublicationRequest request,
            RecoveryTranscript? transcript = null)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            RecoveryTranscript log = transcript ?? new RecoveryTranscript(16);
            WorldId source = request.Capture.World;

#if GAMECORE_FAULT_INJECTION
            UnityWorldRegistry.TryGet(source, out UnityWorldHost? sourceHost);
            if (sourceHost != null)
            {
                try
                {
                    // GC-027's capture-copy boundary: reached before the reader copies anything, so the permitted
                    // result is O-20's "copy errors produce no checkpoint" - no document, no artifact, and the world
                    // that was being captured keeps running (P-049's pre-mutation rule).
                    FaultReach.Reach(
                        sourceHost.Faults,
                        FaultBoundary.CheckpointCaptureCopy,
                        request.Operation,
                        ContentHash.Empty,
                        "about to copy the committed boundary of session " + source.Session.ToString());
                }
                catch (FaultInjectedException fault)
                {
                    log.AddFault(RecoveryFaultPoints.CaptureCopy, fault.Message, RecoveryDataLossClass.None);
                    return new CheckpointPublicationResult(
                        false, false, DiagnosticCode.ApplyFault,
                        "the capture-copy boundary was armed and fired: " + fault.Message
                        + "; no document was produced and the world that was being captured keeps running (O-20).",
                        null, default(StoredCheckpoint), RecoveryFaultPoints.CaptureCopy);
                }
            }
#endif

            CheckpointCaptureResult captured = CheckpointCapture.Capture(request.Reader, request.Capture);
            if (!captured.Captured)
            {
                log.Add(
                    RecoveryPhase.Capture,
                    "the capture refused with " + RecoveryFailureClassification.Describe(captured.Code) + ": "
                    + captured.Detail,
                    RecoveryDataLossClass.None);
                return new CheckpointPublicationResult(
                    false, false, captured.Code, captured.Detail, captured, default(StoredCheckpoint), string.Empty);
            }

            log.Add(
                RecoveryPhase.Capture,
                "captured " + captured.Document.Length.ToString(CultureInfo.InvariantCulture) + " bytes at epoch "
                + captured.Boundary.AssemblyEpoch.Value.ToString(CultureInfo.InvariantCulture) + " step "
                + captured.Boundary.LogicalStepId.Value.ToString(CultureInfo.InvariantCulture)
                + "; document=" + captured.DocumentHash.ToHex(),
                RecoveryDataLossClass.None);

#if GAMECORE_FAULT_INJECTION
            if (sourceHost != null)
            {
                try
                {
                    // GC-027's file-publication boundary: the store is about to write its temporary artifact and
                    // replace the document. The permitted result is "the previous verified document is still the
                    // stored one and no partial artifact exists" (06 s7, O-20).
                    FaultReach.Reach(
                        sourceHost.Faults,
                        FaultBoundary.CheckpointPublication,
                        request.Operation,
                        captured.DocumentHash,
                        "about to publish the captured document to " + request.Store.Location);
                }
                catch (FaultInjectedException fault)
                {
                    log.AddFault(RecoveryFaultPoints.CheckpointPublication, fault.Message, RecoveryDataLossClass.None);
                    return new CheckpointPublicationResult(
                        true, false, DiagnosticCode.ApplyFault,
                        "the checkpoint-publication boundary was armed and fired: " + fault.Message
                        + "; nothing was published, so the previously stored document (if any) is still the stored "
                        + "one and no partial artifact exists (06 s7, O-20).",
                        captured, default(StoredCheckpoint), RecoveryFaultPoints.CheckpointPublication);
                }
            }
#endif

            bool hadPrevious = request.Store.Exists;
            if (!request.Store.TryPublish(
                    captured.Document,
                    out StoredCheckpoint stored,
                    out DiagnosticCode publishCode,
                    out string publishDetail))
            {
                log.Add(
                    RecoveryPhase.Publish,
                    "publication refused with " + RecoveryFailureClassification.Describe(publishCode) + ": "
                    + publishDetail + "; previousDocumentRetained="
                    + (hadPrevious && request.Store.Exists ? "1" : "0"),
                    RecoveryDataLossClass.None);
                return new CheckpointPublicationResult(
                    true, false, publishCode, publishDetail, captured, default(StoredCheckpoint), string.Empty);
            }

            log.Add(
                RecoveryPhase.Publish,
                "published " + stored.DocumentBytes.ToString(CultureInfo.InvariantCulture) + " document bytes at "
                + stored.Location + " as " + stored.DocumentHash.ToHex(),
                RecoveryDataLossClass.None);
            return new CheckpointPublicationResult(
                true, true, DiagnosticCode.None, string.Empty, captured, stored, string.Empty);
        }
    }

    /// <summary>
    /// O-22's composition over the two procedures that already exist. One call is one recovery: it reserves
    /// sessions, reads or creates, publishes, stops the old world, and reports; it never resumes the old world and
    /// never replays an external effect.
    /// </summary>
    public static class WorldRecovery
    {
        /// <summary>Attempt-log capacity is the host's own bound plus one, so no attempt is ever uncounted (P-043).</summary>
        private const int AttemptLogSlack = 1;

        /// <summary>Transcript capacity for one recovery; bounded because every list in this protocol is (P-043).</summary>
        private const int TranscriptCapacity = 64;

        /// <summary>Builds the retry policy, attempt log and transcript one recovery call needs (P-049).</summary>
        public static RecoveryRetryPolicy PolicyFromHost(OperationExpirySettings? settings) =>
            RecoveryRetryPolicy.FromHostSettings(settings);

        /// <summary>
        /// Recovers one faulted world into a new session (O-22). The attempt sequence is the contract: observe the
        /// source, reserve a fresh session and operation id, validate/rebuild into an UNEXPOSED world, publish it,
        /// then stop the old world. A retryable transient refusal is retried under the host's bound with a new
        /// session and a new operation id; anything else is final (P-049, P-050).
        /// </summary>
        public static WorldRecoveryReport Recover(WorldRecoveryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            GameCoreThreading.RequireMainThread("WorldRecovery.Recover");

            WorldRecoveryRequest request = context.Request;
            RecoveryTranscript transcript = context.Transcript;
            var attempts = new RecoveryAttemptLog(context.RetryPolicy.MaxAttempts + AttemptLogSlack);
            int registryBefore = UnityWorldRegistry.Count;

            if (!request.IsValid)
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "the recovery request is not well formed (P-004, P-050, P-054).",
                    RecoveryDataLossClass.None);
                return Report(
                    context, attempts, transcript, WorldLifecycleState.Created, DiagnosticCode.None, 0,
                    WorldLifecycleState.Created, 0, Outcome.Rejected, DiagnosticCode.UnsupportedVersion,
                    "the recovery request is not well formed: a source session, a definition and - for a checkpoint "
                    + "source - a migration graph are required (O-22, P-054).",
                    null, null, null, null, registryBefore, registryBefore);
            }

            if (!UnityWorldRegistry.TryGet(request.Source, out UnityWorldHost? sourceHost) || sourceHost == null)
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "no live world owns source session " + request.Source.Session.ToString() + " (P-005).",
                    RecoveryDataLossClass.None);
                return Report(
                    context, attempts, transcript, WorldLifecycleState.Created, DiagnosticCode.None, 0,
                    WorldLifecycleState.Created, 0, Outcome.Rejected, DiagnosticCode.StaleHandle,
                    "no live world owns source session " + request.Source.Session.ToString()
                    + "; recovery reads a faulted world, and an unregistered session is a stale handle (O-22).",
                    null, null, null, null, registryBefore, registryBefore);
            }

            WorldLifecycleState sourceBefore = sourceHost.Lifecycle;
            DiagnosticCode sourceFault = sourceHost.FaultCode;
            int sourceFaultCount = sourceHost.FaultCount;
            transcript.Add(
                RecoveryPhase.SourceObserved,
                "source " + sourceHost.DiagnosticName + " is " + sourceBefore.ToString() + " fault="
                + RecoveryFailureClassification.Describe(sourceFault) + " count="
                + sourceFaultCount.ToString(CultureInfo.InvariantCulture),
                RecoveryDataLossClass.None);

            if (sourceBefore == WorldLifecycleState.Running || sourceBefore == WorldLifecycleState.Paused)
            {
                // A live world is a reconfiguration or a capture, never a recovery: recovering over it would put two
                // writers on one state domain and claim a failover that never happened (P-002, P-049).
                transcript.Add(
                    RecoveryPhase.Refused,
                    "the source is live (" + sourceBefore.ToString() + "); recovery is refused (O-22).",
                    RecoveryDataLossClass.None);
                return Report(
                    context, attempts, transcript, sourceBefore, sourceFault, sourceFaultCount, sourceBefore,
                    sourceFaultCount, Outcome.Rejected, DiagnosticCode.TooLate,
                    "source world " + sourceHost.DiagnosticName + " is " + sourceBefore
                    + "; recovery reads a faulted/stopping/disposed world, so a live one is refused (O-22, P-049).",
                    null, null, null, null, registryBefore, UnityWorldRegistry.Count);
            }

            if (!sourceHost.Request.Definition.Equals(request.Definition))
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "the request's definition is not the source world's definition (O-22).",
                    RecoveryDataLossClass.None);
                return Report(
                    context, attempts, transcript, sourceBefore, sourceFault, sourceFaultCount, sourceBefore,
                    sourceFaultCount, Outcome.Rejected, DiagnosticCode.MissingDependency,
                    "the request's definition is not the source world's definition; a recovered world is rebuilt "
                    + "from the same definition, and a different one is a different world (O-22).",
                    null, null, null, null, registryBefore, UnityWorldRegistry.Count);
            }

            Outcome outcome = Outcome.Rejected;
            DiagnosticCode code = DiagnosticCode.ResourceUnavailable;
            string detail = "the recovery made no attempt.";
            UnityWorldHost? destination = null;
            RestoreOutcome? restoreOutcome = null;
            OutboxConsistencyReport? outboxReport = null;
            RecoveryRestartPoint? restart = null;
            int ordinal = 0;

            while (true)
            {
                RecoveryAttemptKind kind = ordinal == 0 ? RecoveryAttemptKind.Initial : RecoveryAttemptKind.Retry;
                context.HealthCheck?.Invoke(RecoveryHealthPhase.BeforeAttempt, ordinal, null);

                WorldId destinationSession = context.ReserveSession();
                OperationId operation = context.ReserveOperation(destinationSession);
#if GAMECORE_FAULT_INJECTION
                try
                {
                    // GC-027's reference-repair boundary: the destination's composition, recipes and reference
                    // tables are about to be rebuilt from the source. Injecting here means the destination was NEVER
                    // built, so TEST-016's last row holds by construction: an incomplete destination cannot become the
                    // running world because there is no destination, and the source is left exactly as it was found
                    // (P-049).
                    FaultReach.Reach(
                        sourceHost.Faults,
                        FaultBoundary.RestoreReferenceRepair,
                        operation,
                        request.CatalogHash,
                        "about to rebuild the destination's composition and references for session "
                        + destinationSession.Session.ToString());
                }
                catch (FaultInjectedException fault)
                {
                    transcript.AddFault(
                        RecoveryFaultPoints.RestoreReferenceRepair, fault.Message, RecoveryDataLossClass.None);
                    attempts.TryAdd(new RecoveryAttemptRecord(
                        ordinal,
                        kind,
                        request.Kind,
                        destinationSession,
                        operation,
                        Outcome.Rejected,
                        DiagnosticCode.ApplyFault,
                        "the reference-repair boundary was armed and fired, so the destination was never built "
                        + "(P-049).",
                        RecoveryFaultPoints.RestoreReferenceRepair));
                    return Report(
                        context, attempts, transcript, sourceBefore, sourceFault, sourceFaultCount,
                        sourceHost.Lifecycle, sourceHost.FaultCount, Outcome.Rejected, DiagnosticCode.ApplyFault,
                        "the " + RecoveryFaultPoints.RestoreReferenceRepair + " boundary was armed and fired: "
                        + fault.Message + "; no destination was created, so no incomplete world can become the "
                        + "running one, and the source stays " + sourceHost.Lifecycle.ToString() + " (O-22, P-049).",
                        null, null, null, null, registryBefore, UnityWorldRegistry.Count);
                }
#endif

                Outcome attemptOutcome;
                DiagnosticCode attemptCode;
                string attemptDetail;
                string faultPoint;
                UnityWorldHost? attemptDestination;
                RestoreOutcome? attemptRestore;
                RecoveryRestartPoint? attemptRestart;

                if (request.Kind == RecoverySourceKind.Checkpoint)
                {
                    attemptOutcome = AttemptRestore(
                        context, destinationSession, operation, transcript, out attemptCode, out attemptDetail,
                        out faultPoint, out attemptDestination, out attemptRestore, out attemptRestart);
                }
                else
                {
                    attemptOutcome = AttemptInitialDefinition(
                        context, destinationSession, operation, transcript, out attemptCode, out attemptDetail,
                        out faultPoint, out attemptDestination, out attemptRestart);
                }

                attempts.TryAdd(new RecoveryAttemptRecord(
                    ordinal, kind, request.Kind, destinationSession, operation, attemptOutcome, attemptCode,
                    attemptDetail, faultPoint));

                if (attemptDestination != null)
                {
                    destination = attemptDestination;
                }

                if (attemptRestore != null)
                {
                    restoreOutcome = attemptRestore;
                }

                if (attemptRestart != null)
                {
                    restart = attemptRestart;
                }

                ordinal++;

                if (attemptOutcome == Outcome.Published)
                {
                    outcome = Outcome.Published;
                    code = DiagnosticCode.None;
                    detail = "recovered " + request.Kind.ToString() + " into session "
                        + destinationSession.Session.ToString() + " (O-22).";
                    break;
                }

                if (!context.RetryPolicy.AllowsRetry(ordinal, attemptCode))
                {
                    outcome = attemptOutcome;
                    code = attemptCode;
                    detail = attemptDetail;
                    break;
                }

                transcript.Add(
                    RecoveryPhase.Refused,
                    "attempt " + (ordinal - 1).ToString(CultureInfo.InvariantCulture) + " was refused with "
                    + RecoveryFailureClassification.Describe(attemptCode) + "; retrying under the host bound "
                    + context.RetryPolicy.ToString() + " with a new session and a new operation id (P-049).",
                    RecoveryDataLossClass.None);
            }

            if (destination != null)
            {
                context.HealthCheck?.Invoke(RecoveryHealthPhase.AfterAttempt, ordinal - 1, destination);
                if (context.OutboxProbe != null)
                {
                    DurableOutbox? liveOutbox = context.OutboxProbe(destination.World);
                    IReadOnlyList<OutboxRecordValue>? rows = restoreOutcome?.Plan?.Outbox;
                    outboxReport = OutboxConsistency.Verify(
                        rows, liveOutbox, "recovered:" + destination.World.Session.ToString());
                    transcript.Add(
                        RecoveryPhase.OutboxReinstate,
                        "outbox consistency after recovery: " + OneLine(outboxReport.Describe()),
                        outboxReport.Consistent
                            ? RecoveryDataLossClass.None
                            : RecoveryDataLossClass.UncommittedSinceCheckpoint);
                }

                transcript.Add(
                    RecoveryPhase.PublishNewWorld,
                    "published a new session at epoch "
                    + destination.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture) + " step "
                    + destination.CurrentStep.Value.ToString(CultureInfo.InvariantCulture) + " for session "
                    + destination.World.Session.ToString(),
                    RecoveryDataLossClass.None);
                // O-22's result is "stopped old world plus new session". The stop happens only once the new session is
                // published, so a failed recovery leaves the faulted world exactly where it was for a further
                // attempt, while a successful one releases its storage (P-048, P-049).
                StopSource(context, sourceHost, transcript, sourceBefore);
            }
            else
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "no attempt published, so the source world " + sourceHost.DiagnosticName + " is left "
                    + sourceHost.Lifecycle.ToString() + " (" + DiagnosticCodeText.Of(sourceHost.FaultCode)
                    + ") for a further explicit attempt; it is never resumed (O-22, P-049).",
                    RecoveryDataLossClass.None);
            }

            return Report(
                context, attempts, transcript, sourceBefore, sourceFault, sourceFaultCount, sourceHost.Lifecycle,
                sourceHost.FaultCount, outcome, code, detail, destination, restoreOutcome, outboxReport, restart,
                registryBefore, UnityWorldRegistry.Count);
        }

        /// <summary>
        /// Restarts from a stored document with NO source world: the process that committed the obligations is gone,
        /// so there is no lifecycle to read and nothing to stop. This is the honest shape of a restart (P-053's
        /// "restart creates a new world/session"; O-22's "durable replay safety requires the checkpoint/outbox
        /// contract"), and it reuses the same executor and the same bounded-retry rule as
        /// <see cref="Recover(WorldRecoveryContext)"/> rather than a second restore path.
        ///
        /// The permitted observable results are exactly two: a new session built from a verified document, or no
        /// world at all when the document is absent, corrupt or incompatible. A restart never resumes, repairs or
        /// even contacts the previous session, and it never delivers an obligation it reinstated (P-045, P-049).
        /// </summary>
        /// <param name="context">
        /// The destination's composition root, codecs, reservations, retry bound and transcript. Its request names the
        /// previous session for the report and the store to read; no world for that session needs to exist.
        /// </param>
        /// <param name="previousSession">The session that committed the document; recorded, never touched (P-004).</param>
        public static WorldRecoveryReport Restart(WorldRecoveryContext context, WorldId previousSession)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            GameCoreThreading.RequireMainThread("WorldRecovery.Restart");

            WorldRecoveryRequest request = context.Request;
            RecoveryTranscript transcript = context.Transcript;
            var attempts = new RecoveryAttemptLog(context.RetryPolicy.MaxAttempts + AttemptLogSlack);
            int registryBefore = UnityWorldRegistry.Count;
            transcript.Add(
                RecoveryPhase.Restart,
                "restarting from " + (request.Store == null ? "<no store>" : request.Store.Location)
                + "; the previous session " + previousSession.Session.ToString()
                + " is not contacted and no in-process state is carried (P-049, P-053).",
                RecoveryDataLossClass.UncommittedSinceCheckpoint);

            if (request.Store == null || request.Migrations == null)
            {
                return Report(
                    context, attempts, transcript, WorldLifecycleState.Disposed, DiagnosticCode.None, 0,
                    WorldLifecycleState.Disposed, 0, Outcome.Rejected, DiagnosticCode.UnsupportedVersion,
                    "a restart requires a checkpoint store and a migration graph; without them there is nothing "
                    + "verified to restart from (P-053, P-054).",
                    null, null, null, null, registryBefore, registryBefore);
            }

            Outcome outcome = Outcome.Rejected;
            DiagnosticCode code = DiagnosticCode.ResourceUnavailable;
            string detail = "the restart made no attempt.";
            UnityWorldHost? destination = null;
            RestoreOutcome? restoreOutcome = null;
            OutboxConsistencyReport? outboxReport = null;
            RecoveryRestartPoint? restart = null;
            int ordinal = 0;

            while (true)
            {
                RecoveryAttemptKind kind = ordinal == 0 ? RecoveryAttemptKind.Initial : RecoveryAttemptKind.Retry;
                WorldId destinationSession = context.ReserveSession();
                OperationId operation = context.ReserveOperation(destinationSession);

                Outcome attemptOutcome = AttemptRestore(
                    context, destinationSession, operation, transcript, out code, out string attemptDetail,
                    out string faultPoint, out UnityWorldHost? attemptDestination, out RestoreOutcome? attemptRestore,
                    out RecoveryRestartPoint? attemptRestart);

                attempts.TryAdd(new RecoveryAttemptRecord(
                    ordinal, kind, request.Kind, destinationSession, operation, attemptOutcome, code, attemptDetail,
                    faultPoint));

                if (attemptDestination != null)
                {
                    destination = attemptDestination;
                }

                if (attemptRestore != null)
                {
                    restoreOutcome = attemptRestore;
                }

                if (attemptRestart != null)
                {
                    restart = attemptRestart;
                }

                ordinal++;

                if (attemptOutcome == Outcome.Published)
                {
                    outcome = Outcome.Published;
                    code = DiagnosticCode.None;
                    detail = "restarted into session " + destinationSession.Session.ToString()
                        + " from a verified document of " + previousSession.Session.ToString() + " (P-049, P-053).";
                    break;
                }

                if (!context.RetryPolicy.AllowsRetry(ordinal, code))
                {
                    outcome = attemptOutcome;
                    detail = attemptDetail;
                    break;
                }
            }

            if (destination != null)
            {
                if (context.OutboxProbe != null)
                {
                    DurableOutbox? liveOutbox = context.OutboxProbe(destination.World);
                    outboxReport = OutboxConsistency.Verify(
                        restoreOutcome?.Plan?.Outbox, liveOutbox, "restarted:" + destination.World.Session.ToString());
                    transcript.Add(
                        RecoveryPhase.OutboxReinstate,
                        "outbox consistency after restart: " + OneLine(outboxReport.Describe()),
                        outboxReport.Consistent
                            ? RecoveryDataLossClass.None
                            : RecoveryDataLossClass.UncommittedSinceCheckpoint);
                }

                transcript.Add(
                    RecoveryPhase.PublishNewWorld,
                    "the restarted session " + destination.World.Session.ToString() + " is published and running; "
                    + "the destination has not been asked to apply anything (P-049).",
                    RecoveryDataLossClass.None);
            }
            else
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "the restart produced no world, which is the permitted result of an absent, corrupt or "
                    + "incompatible document: there is nothing to expose (P-049, P-054).",
                    RecoveryDataLossClass.UncommittedSinceCheckpoint);
            }

            return Report(
                context, attempts, transcript, WorldLifecycleState.Disposed, DiagnosticCode.None, 0,
                WorldLifecycleState.Disposed, 0, outcome, code, detail, destination, restoreOutcome, outboxReport,
                restart, registryBefore, UnityWorldRegistry.Count);
        }

        /// <summary>
        /// One attempt of the checkpoint source: read the verified document, then let O-21's executor reserve, plan,
        /// build an unexposed world, validate it and expose it. Every refusal is the executor's own code (P-052).
        /// </summary>
        private static Outcome AttemptRestore(
            WorldRecoveryContext context,
            WorldId destinationSession,
            OperationId operation,
            RecoveryTranscript transcript,
            out DiagnosticCode code,
            out string detail,
            out string faultPoint,
            out UnityWorldHost? destination,
            out RestoreOutcome? restoreOutcome,
            out RecoveryRestartPoint? restart)
        {
            WorldRecoveryRequest request = context.Request;
            destination = null;
            restoreOutcome = null;
            faultPoint = string.Empty;

            if (!request.Store!.TryRead(out byte[]? document, out StoredCheckpoint stored, out code, out string readDetail))
            {
                detail = readDetail;
                // A restart has no in-process state: whatever the store holds is the whole of its input, so an
                // absent or corrupt document means no world at all rather than a partly recovered one (P-049, P-053).
                transcript.AddFault(
                    RecoveryFaultPoints.Restart,
                    "a restart could not read a verified document: " + RecoveryFailureClassification.Describe(code)
                    + " - " + readDetail,
                    RecoveryDataLossClass.UncommittedSinceCheckpoint);
                restart = new RecoveryRestartPoint(
                    request.Store.Location, false, ContentHash.Empty, request.Source, default(WorldId), code,
                    readDetail);
                return Outcome.Rejected;
            }

            transcript.Add(
                RecoveryPhase.StoreRead,
                "read " + stored.DocumentBytes.ToString(CultureInfo.InvariantCulture) + " verified document bytes "
                + "from " + stored.Location + " as " + stored.DocumentHash.ToHex(),
                RecoveryDataLossClass.None);

            var executor = new CheckpointRestoreExecutor(context.Reservations, context.Codecs);
            IRestoreTargetBuilder builder = context.BuilderFactory(destinationSession, operation);
            RestoreOutcome outcome = executor.Restore(
                document,
                destinationSession,
                operation,
                builder,
                request.Migrations!,
                request.CatalogHash,
                request.AllocatedSchemas,
                request.RequireCatalogMatch);

            code = outcome.Code;
            detail = outcome.Detail;
            restoreOutcome = outcome.Restored ? outcome : null;
            restart = new RecoveryRestartPoint(
                stored.Location,
                true,
                stored.DocumentHash,
                request.Source,
                outcome.Restored ? destinationSession : default(WorldId),
                code,
                outcome.Restored ? string.Empty : outcome.Stage.ToString() + ": " + outcome.Detail);

            if (!outcome.Restored)
            {
                faultPoint = FaultPointOf(outcome);
                if (faultPoint.Length != 0)
                {
                    transcript.AddFault(
                        faultPoint,
                        "the restore attempt was stopped by the " + faultPoint + " boundary: "
                        + RecoveryFailureClassification.Describe(code) + " - " + detail,
                        faultPoint == RecoveryFaultPoints.RestorePostwriteApply
                            ? RecoveryDataLossClass.UncommittedAttemptWork
                            : RecoveryDataLossClass.None);
                }
                else
                {
                    transcript.Add(
                        RecoveryPhase.Refused,
                        "the restore attempt was refused at " + outcome.Stage.ToString() + " with "
                        + RecoveryFailureClassification.Describe(code) + ": " + detail,
                        RecoveryDataLossClass.None);
                }

                return Outcome.Rejected;
            }

            UnityWorldRegistry.TryGet(destinationSession, out destination);
            transcript.Add(
                RecoveryPhase.Restore,
                "restored " + outcome.RestoredTargets.ToString(CultureInfo.InvariantCulture) + " target(s), "
                + outcome.RestoredSlots.ToString(CultureInfo.InvariantCulture) + " slot row(s) ("
                + outcome.DormantSlots.ToString(CultureInfo.InvariantCulture) + " dormant) and "
                + outcome.RestoredOutboxRows.ToString(CultureInfo.InvariantCulture) + " outbox row(s)",
                RecoveryDataLossClass.None);
            return Outcome.Published;
        }

        /// <summary>
        /// One attempt of the initial-definition source: GC-017's `InitialDefinitionRecovery`, reused unchanged.
        /// The data-loss boundary is at its widest here - no committed state is carried at all - which the transcript
        /// records so no report implies otherwise (P-049).
        /// </summary>
        private static Outcome AttemptInitialDefinition(
            WorldRecoveryContext context,
            WorldId destinationSession,
            OperationId operation,
            RecoveryTranscript transcript,
            out DiagnosticCode code,
            out string detail,
            out string faultPoint,
            out UnityWorldHost? destination,
            out RestoreOutcome? restoreOutcome,
            out RecoveryRestartPoint? restart)
        {
            WorldRecoveryRequest request = context.Request;
            faultPoint = string.Empty;
            restoreOutcome = null;
            restart = null;

            var recovery = new RecoveryRequest(
                request.Source,
                destinationSession,
                request.Definition,
                request.TemporalModel,
                request.Mode,
                request.CatalogHash,
                operation,
                request.FixedStep);
            RecoveryReport report = InitialDefinitionRecovery.Recover(recovery, context.Registration);
            code = report.Code;
            detail = report.Detail;
            destination = report.DestinationHost;

            if (!report.Recovered)
            {
                transcript.Add(
                    RecoveryPhase.Refused,
                    "the initial-definition attempt was refused with "
                    + RecoveryFailureClassification.Describe(code) + ": " + detail,
                    RecoveryDataLossClass.None);
                return Outcome.Rejected;
            }

            transcript.Add(
                RecoveryPhase.Restore,
                "recreated the world from its initial definitions; no committed state was carried, so the data-loss "
                + "boundary is everything the source session committed (P-049).",
                RecoveryDataLossClass.UncommittedSinceCheckpoint);
            return Outcome.Published;
        }

        /// <summary>
        /// Stops the old faulted world, which is half of O-22's result ("stopped old world plus new session"). A
        /// blocked stop is reported as the retained/quarantined outcome P-048 requires: elapsed time never authorizes
        /// freeing storage a job may still reach, and the recovery does not retry it.
        /// </summary>
        private static void StopSource(
            WorldRecoveryContext context,
            UnityWorldHost sourceHost,
            RecoveryTranscript transcript,
            WorldLifecycleState sourceBefore)
        {
            OperationResult stop = sourceHost.Stop(
                context.ReserveOperation(sourceHost.World), "O-22 recovery of a faulted world");

            if (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
            {
                transcript.Add(
                    RecoveryPhase.SourceStopped,
                    "the source world " + sourceHost.DiagnosticName + " is " + sourceHost.Lifecycle.ToString()
                    + "; it was " + sourceBefore.ToString() + " and is never resumed (P-049).",
                    RecoveryDataLossClass.None);
                return;
            }

            transcript.Add(
                RecoveryPhase.SourceStopped,
                "stopping the source world " + sourceHost.DiagnosticName + " was refused with "
                + DiagnosticCodeText.Of(stop.Code) + " (" + sourceHost.Lifecycle.ToString()
                + "); its resources stay retained until a job that may still reach them ends (P-048).",
                RecoveryDataLossClass.None);
        }

        /// <summary>
        /// Names the recovery injection point a refused restore outcome is attributable to, or empty when the
        /// refusal was the executor's own (a schema mismatch, a corrupt reference, a migration gap). Both recovery
        /// latch boundaries surface as `ApplyFault` from `Validate`/`Expose`/`Build`, which is what the executor
        /// reports when the fault was raised inside it (TEST-016).
        /// </summary>
        private static string FaultPointOf(RestoreOutcome outcome)
        {
            if (outcome.Code != DiagnosticCode.ApplyFault)
            {
                return string.Empty;
            }

            return RecoveryFaultPoints.RestorePostwriteApply;
        }

        private static string OneLine(string text) => text.Replace('\n', ' ').Replace('\r', ' ');

        private static WorldRecoveryReport Report(
            WorldRecoveryContext context,
            RecoveryAttemptLog attempts,
            RecoveryTranscript transcript,
            WorldLifecycleState sourceBefore,
            DiagnosticCode sourceFault,
            int sourceFaultCount,
            WorldLifecycleState sourceAfter,
            int sourceFaultCountAfter,
            Outcome outcome,
            DiagnosticCode code,
            string detail,
            UnityWorldHost? destination,
            RestoreOutcome? restore,
            OutboxConsistencyReport? outbox,
            RecoveryRestartPoint? restart,
            int registryBefore,
            int registryAfter) =>
            new WorldRecoveryReport(
                context.Request, attempts, transcript, sourceBefore, sourceFault, sourceFaultCount, sourceAfter,
                sourceFaultCountAfter, outcome, code, detail, destination, restore, outbox, restart, registryBefore,
                registryAfter);
    }
}
