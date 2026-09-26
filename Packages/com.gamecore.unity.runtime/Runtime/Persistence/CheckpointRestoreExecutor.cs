// GameCore.Unity.Runtime - checkpoint restore into a new unexposed world (GC-018, O-21).
//
// Normative sources: 00 O-21 ("Host+catalog+migrators; verified blob → new WorldId and fully published restored
// world. Preconditions: C/unexposed new world→B; validate schema/catalog, rebuild identities/composition, repair
// references, restore state and cursors before Running. Failure: Unsupported schema/migration rejects without
// affecting existing world; cancel destroys staged world; duplicate returns same restored session. Probe: old
// callback cannot target restored entity" — P-049, P-053–P-055), P-049 ("Recovery creates a new `WorldId` from a
// verified checkpoint or initial catalog, validates/rebuilds composition and recipes, restores state, then reopens
// admission; old callbacks/handles never become valid") and 06 s7 ("Build an unexposed world with a fresh WorldId,
// reconstruct scope/target stable IDs, rederive capabilities, allocate recipes, restore owner slots, repair stable
// references, restore clocks/RNG/outbox cursors, then publish.").
//
// The order of the sequence below is the contract: reserve, read, plan, build *unexposed*, validate the built world,
// and only then publish it into the registry. Every failure before the final `Expose` step destroys the staging
// world and leaves no partial world reachable, and the reservation is retained so the session id is never handed out
// again (P-050).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using Unity.Collections;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Persistence
{
    /// <summary>Which stage of the restore sequence produced an outcome; reported for diagnosis (P-052).</summary>
    public enum RestoreStage
    {
        None = 0,
        Reservation = 1,
        Read = 2,
        Plan = 3,
        Stage = 4,
        Build = 5,
        Validate = 6,
        Expose = 7,
    }

    /// <summary>The outcome of one restore: the new session, or the stage and code that refused it (P-052).</summary>
    public sealed class RestoreOutcome
    {
        internal RestoreOutcome(
            bool restored,
            WorldId session,
            RestoreStage stage,
            DiagnosticCode code,
            string detail,
            WorldId sourceWorld,
            SnapshotToken? publishedToken,
            RestorePlan? plan,
            int restoredTargets,
            int restoredSlots,
            int dormantSlots,
            int restoredOutboxRows)
        {
            Restored = restored;
            Replayed = plan == null && restored;
            Session = session;
            Stage = stage;
            Code = code;
            Detail = detail;
            SourceWorld = sourceWorld;
            PublishedToken = publishedToken;
            Plan = plan;
            RestoredTargets = restoredTargets;
            RestoredSlots = restoredSlots;
            DormantSlots = dormantSlots;
            RestoredOutboxRows = restoredOutboxRows;
        }

        public bool Restored { get; }

        /// <summary>The new session, or the reserved-but-unexposed session a refusal destroyed (P-004, P-049).</summary>
        public WorldId Session { get; }

        public RestoreStage Stage { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>The session the checkpoint was captured from; never equal to <see cref="Session"/> (P-004).</summary>
        public WorldId SourceWorld { get; }

        /// <summary>The image the restored world published; null when nothing was published (P-030).</summary>
        public SnapshotToken? PublishedToken { get; }

        /// <summary>The validated plan that was applied; null when the restore never reached a plan (P-053).</summary>
        public RestorePlan? Plan { get; }

        public int RestoredTargets { get; }

        public int RestoredSlots { get; }

        /// <summary>Dormant slots restored with no active writer; they are state, not leftovers (P-032).</summary>
        public int DormantSlots { get; }

        /// <summary>
        /// Delivery obligation rows reinstated into the restored world (GC-021, P-053). Zero is the honest answer of
        /// a world with no outbox; it is not evidence that an obligation was dropped, which the outbox proof covers.
        /// </summary>
        public int RestoredOutboxRows { get; }

        /// <summary>
        /// True when this outcome replays a completed reservation rather than a fresh restore. The session is
        /// restored either way; this distinguishes "I just restored it" from "it was already restored" for a caller
        /// that retransmitted a request (O-21, P-050).
        /// </summary>
        public bool Replayed { get; private set; }

        internal static RestoreOutcome Refused(
            WorldId session,
            RestoreStage stage,
            DiagnosticCode code,
            string detail,
            WorldId source) =>
            new RestoreOutcome(false, session, stage, code, detail, source, null, null, 0, 0, 0, 0);

        internal static RestoreOutcome Replay(WorldId session, DiagnosticCode code, string detail) =>
            new RestoreOutcome(
                true,
                session,
                RestoreStage.Expose,
                code,
                detail,
                session,
                null,
                null,
                0,
                0,
                0,
                0);

        internal static RestoreOutcome Success(
            WorldId session,
            WorldId source,
            SnapshotToken token,
            RestorePlan plan,
            int targets,
            int slots,
            int dormant,
            int outboxRows) =>
            new RestoreOutcome(
                true,
                session,
                RestoreStage.Expose,
                DiagnosticCode.None,
                "restored into a new session from a checkpoint of " + source.Session.ToString() + ".",
                source,
                token,
                plan,
                targets,
                slots,
                dormant,
                outboxRows);

        public override string ToString() =>
            Restored
                ? "restored(" + Session.Session.ToString() + ",targets="
                    + RestoredTargets.ToString(CultureInfo.InvariantCulture) + ",dormant="
                    + DormantSlots.ToString(CultureInfo.InvariantCulture) + ")"
                : "refused(" + Stage.ToString() + ":" + DiagnosticCodeText.Of(Code) + ")";
    }

    /// <summary>
    /// Reinstates a checkpoint's outbox section into the staging world (GC-021, P-053). It is separate from
    /// <see cref="IRestoreTargetBuilder"/> because an outbox is not ECS storage: it is a managed owner inside the
    /// world, and a world that has no outbox simply does not implement this. A builder that does implement it must
    /// reinstate *before* the world is exposed, so no caller can observe a restored world whose committed delivery
    /// obligations were dropped (P-045, P-049).
    /// </summary>
    public interface IRestoreOutboxBuilder
    {
        /// <summary>Reinstates the plan's outbox rows, reporting how many rows were applied.</summary>
        bool TryReinstateOutbox(
            WorldId session,
            RestorePlan plan,
            out int reinstatedRows,
            out DiagnosticCode code,
            out string detail);

        /// <summary>
        /// Proves that the world it built carries exactly the plan's outbox rows. The executor calls this before
        /// exposure, so a restore that lost or invented an obligation is refused rather than published (P-053).
        /// </summary>
        bool TryProveOutbox(
            WorldId session,
            IReadOnlyList<OutboxRecordValue> expected,
            out DiagnosticCode code,
            out string detail);
    }

    /// <summary>
    /// Applies a validated <see cref="RestorePlan"/> to a staging world. This is the one seam a test fixture
    /// implements differently from a production world: the executor owns the sequence and the refusals, while the
    /// rebuilder owns the ECS work of recreating a declared world (P-030, 04 s5).
    /// </summary>
    public interface IRestoreTargetBuilder
    {
        /// <summary>
        /// Builds the unexposed staging world for the reserved session and applies the plan to it. A false result
        /// destroys the staging world and reports why; the world is never exposed in that case (O-21).
        /// </summary>
        bool TryBuild(
            WorldId session,
            RestorePlan plan,
            int migrationCount,
            out UnityWorldHost? staging,
            out DiagnosticCode code,
            out string detail);
    }

    /// <summary>Runs the O-21 sequence: reserve, read, plan, build unexposed, validate, expose (P-049, P-053).</summary>
    public sealed class CheckpointRestoreExecutor
    {
        private readonly RestoreReservationLedger reservations;
        private readonly CheckpointCodecSet codecs;

        public CheckpointRestoreExecutor(RestoreReservationLedger reservations, CheckpointCodecSet codecs)
        {
            this.reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
            this.codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
        }

        public RestoreReservationLedger Reservations => reservations;

        /// <summary>Restores are sequenced on the control lane, so only one runs at a time (P-051).</summary>
        public int RestoreCount { get; private set; }

        public int RefusedCount { get; private set; }

        /// <summary>Requests served from an existing reservation instead of staging a world (P-050).</summary>
        public int RetransmissionCount { get; private set; }

        /// <summary>
        /// Restores one checkpoint into the reserved session. A refusal never exposes a world and never mutates an
        /// existing one; a retransmission returns the original attempt's outcome rather than staging again (O-21).
        /// </summary>
        public RestoreOutcome Restore(
            byte[]? documentBytes,
            WorldId targetSession,
            OperationId operation,
            IRestoreTargetBuilder builder,
            CheckpointMigrationRegistry migrations,
            ContentHash catalogFingerprint,
            IReadOnlyList<SchemaRef>? allocatedSchemas = null,
            bool requireCatalogMatch = true)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (migrations == null)
            {
                throw new ArgumentNullException(nameof(migrations));
            }

            ContentHash inputHash = documentBytes == null
                ? ContentHash.Empty
                : ContentHash.Compute(documentBytes);

            RestoreReservationKind reservation = reservations.TryReserve(
                targetSession,
                operation,
                inputHash,
                out RestoreAttempt? attempt,
                out DiagnosticCode reservationCode,
                out string reservationDetail);
            if (reservation == RestoreReservationKind.Retransmission && attempt != null)
            {
                // Return the recorded outcome without staging a second world (O-21, P-050).
                RetransmissionCount++;
                return Replay(targetSession, attempt);
            }

            if (reservation != RestoreReservationKind.Fresh || attempt == null)
            {
                RefusedCount++;
                return RestoreOutcome.Refused(
                    targetSession,
                    RestoreStage.Reservation,
                    reservationCode,
                    reservationDetail,
                    targetSession);
            }

            if (!CheckpointDocument.TryRead(documentBytes, codecs, out CheckpointDocument? document, out DiagnosticCode readCode, out string readDetail)
                || document == null)
            {
                return Refuse(targetSession, operation, RestoreStage.Read, readCode, readDetail);
            }

            var request = new CheckpointRestoreRequest(
                targetSession,
                document,
                codecs,
                migrations,
                catalogFingerprint,
                allocatedSchemas,
                requireCatalogMatch);
            RestorePlanResult planResult = CheckpointRestorePlanner.Plan(request);
            if (!planResult.Succeeded || planResult.Plan == null)
            {
                return Refuse(
                    targetSession,
                    operation,
                    RestoreStage.Plan,
                    planResult.Code,
                    planResult.Refusal.ToString() + ": " + planResult.Detail);
            }

            RestorePlan plan = planResult.Plan;
            if (!builder.TryBuild(
                    targetSession,
                    plan,
                    plan.Migrations.Count,
                    out UnityWorldHost? staging,
                    out DiagnosticCode buildCode,
                    out string buildDetail)
                || staging == null)
            {
                return Refuse(targetSession, operation, RestoreStage.Build, buildCode, buildDetail);
            }

            int reinstatedOutboxRows = 0;
            if (builder is IRestoreOutboxBuilder outboxBuilder)
            {
                // The outbox is reinstated before validation and before exposure, so a restored world is never
                // observable with a dropped delivery obligation (P-045, P-053).
                if (!outboxBuilder.TryReinstateOutbox(
                        targetSession,
                        plan,
                        out reinstatedOutboxRows,
                        out DiagnosticCode outboxCode,
                        out string outboxDetail))
                {
                    Discard(staging);
                    return Refuse(targetSession, operation, RestoreStage.Build, outboxCode, outboxDetail);
                }

                if (!outboxBuilder.TryProveOutbox(
                        targetSession,
                        plan.Outbox,
                        out DiagnosticCode proveCode,
                        out string proveDetail))
                {
                    Discard(staging);
                    return Refuse(targetSession, operation, RestoreStage.Validate, proveCode, proveDetail);
                }
            }

            if (!Validate(staging, plan, out DiagnosticCode validationCode, out string validationDetail))
            {
                // The staging world is destroyed before anything can observe it: a restore that cannot prove its
                // result never exposes one (O-21).
                Discard(staging);
                return Refuse(targetSession, operation, RestoreStage.Validate, validationCode, validationDetail);
            }

            if (!UnityWorldRegistry.TryExpose(staging))
            {
                Discard(staging);
                return Refuse(
                    targetSession,
                    operation,
                    RestoreStage.Expose,
                    DiagnosticCode.OwnershipConflict,
                    "session " + targetSession.Session.ToString()
                    + " is already registered; one session id names one world (P-004).");
            }

            RestoreCount++;
            SnapshotToken token = new SnapshotToken(targetSession, staging.CurrentEpoch, staging.CurrentStep);
            reservations.TrySettle(targetSession, RestoreAttemptOutcome.Published, DiagnosticCode.None, plan.ToString());
            return RestoreOutcome.Success(
                targetSession,
                plan.Header.SourceSession,
                token,
                plan,
                plan.Targets.Count,
                plan.Slots.Count,
                plan.DormantSlotCount,
                reinstatedOutboxRows);
        }

        /// <summary>
        /// Validates the built world before it is exposed: the restored session is not the captured one, the world
        /// carries exactly the plan's targets and authoritative state rows (active and dormant alike), and it can
        /// accept admission. A failure here is a refusal, never a partial exposure (O-21, P-053).
        /// </summary>
        private static bool Validate(
            UnityWorldHost staging,
            RestorePlan plan,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (staging.World.Session.Equals(plan.Header.SourceSession.Session))
            {
                code = DiagnosticCode.IdempotencyConflict;
                detail = "the staging world reused the captured session; recovery always creates a new WorldId so old"
                    + " callbacks and handles never become valid (P-004, P-049).";
                return false;
            }

            if (!staging.World.Session.Equals(plan.TargetSession.Session))
            {
                code = DiagnosticCode.StaleHandle;
                detail = "the staging world carries session " + staging.World.Session.ToString()
                    + " and the reservation names " + plan.TargetSession.Session.ToString() + " (P-050).";
                return false;
            }

            if (staging.Lifecycle != WorldLifecycleState.Running)
            {
                code = staging.FaultCode == DiagnosticCode.None ? DiagnosticCode.ApplyFault : staging.FaultCode;
                detail = "the staging world is " + staging.Lifecycle.ToString()
                    + " and a restored world is published only after its initial assembly (P-035).";
                return false;
            }

            if (staging.Driver.IsFaulted)
            {
                code = staging.FaultCode;
                detail = "the staging world faulted while being rebuilt and is never exposed (P-031).";
                return false;
            }

            // A restored world opens a fresh publication series, so its revision deliberately differs from the
            // captured one; what must hold is that it published an initial assembly at all, because a created world
            // becomes Running only after that publication (P-035, P-053).
            if (staging.CurrentEpoch.Value == 0UL)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the staging world published no initial assembly, so it has no committed image to expose"
                    + " (P-030, P-035).";
                return false;
            }

            // The built world must prove it carries exactly the plan's authoritative state before it can be
            // exposed: the same targets, and the same owner-state rows active and dormant alike, with no row a
            // rebuild path invented beyond the plan (06 s7 "restore owner slots", P-032, P-053).
            if (!TryProvePlannedState(staging, plan, out string stateDetail))
            {
                code = DiagnosticCode.StalePlan;
                detail = stateDetail;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Reads the built world's own census of targets and owner-state rows and compares it to the plan in both
        /// directions: every planned target and slot exists in the built world, and the built world holds no target
        /// or slot row the plan does not name. A rebuild that invents authoritative rows the checkpoint never
        /// carried, or drops one, is refused here, before the world is exposed (O-21, P-032, P-053).
        /// </summary>
        private static bool TryProvePlannedState(UnityWorldHost staging, RestorePlan plan, out string detail)
        {
            EntityManager entityManager = staging.EntityWorld.EntityManager;
            var builtTargets = new HashSet<TargetId>();
            var plannedSlots = new Dictionary<StateSlotKey, SlotRecordValue>(plan.Slots.Count);
            for (int i = 0; i < plan.Slots.Count; i++)
            {
                // Last row wins on a duplicated key; the row-count parity below is what refuses such a plan,
                // because a built buffer holds at most one row per (owner, slot).
                plannedSlots[plan.Slots[i].Key] = plan.Slots[i];
            }

            var plannedTargets = new HashSet<TargetId>();
            for (int i = 0; i < plan.Targets.Count; i++)
            {
                plannedTargets.Add(plan.Targets[i].Target);
            }

            SlotRecordValue? firstBeyondPlan = null;
            var builtSlotKeys = new HashSet<StateSlotKey>();
            NativeArray<Entity> entities = entityManager.GetAllEntities(Allocator.Temp);
            try
            {
                for (int e = 0; e < entities.Length; e++)
                {
                    Entity entity = entities[e];
                    if (!entityManager.HasComponent<TargetIdentity>(entity))
                    {
                        continue;
                    }

                    TargetId target = entityManager.GetComponentData<TargetIdentity>(entity).Target;
                    if (!builtTargets.Add(target))
                    {
                        detail = "the built world carries target " + target.ToString() + " more than once (P-004).";
                        return false;
                    }

                    if (!entityManager.HasBuffer<TargetSlotState>(entity))
                    {
                        continue;
                    }

                    DynamicBuffer<TargetSlotState> rows = entityManager.GetBuffer<TargetSlotState>(entity);
                    for (int r = 0; r < rows.Length; r++)
                    {
                        TargetSlotState row = rows[r];
                        StateSlotKey key = new StateSlotKey(target, row.Owner, row.Slot);
                        if (!builtSlotKeys.Add(key))
                        {
                            detail = "the built world carries state slot " + key.ToString()
                                + " more than once (P-032).";
                            return false;
                        }

                        if (!plannedSlots.TryGetValue(key, out SlotRecordValue planned))
                        {
                            if (firstBeyondPlan == null)
                            {
                                firstBeyondPlan = new SlotRecordValue(
                                    target.Value.High,
                                    target.Value.Low,
                                    row.Owner.Value.High,
                                    row.Owner.Value.Low,
                                    row.Slot.Value.High,
                                    row.Slot.Value.Low,
                                    row.SchemaVersion,
                                    row.Value,
                                    row.IsActive);
                            }

                            continue;
                        }

                        // A dormant row stays dormant: the disposition is state, not schema, so no migration may
                        // reactivate it (P-032).
                        if (planned.Active != row.IsActive)
                        {
                            detail = "the built world carries state slot " + key.ToString()
                                + (row.IsActive ? " active" : " dormant")
                                + " but the plan names it " + (planned.Active ? "active" : "dormant")
                                + " (P-032, P-053).";
                            return false;
                        }

                        // A direct plan restores the captured bytes verbatim, so version and value must match too.
                        // A plan that runs migrations writes their output instead, so only the row's existence and
                        // disposition are comparable here (P-029, P-054).
                        if (plan.Migrations.Count == 0
                            && (planned.SchemaVersion != row.SchemaVersion || planned.Value != row.Value))
                        {
                            detail = "the built world carries state slot " + key.ToString()
                                + " as " + row.SchemaVersion.ToString(CultureInfo.InvariantCulture) + "/"
                                + row.Value.ToString(CultureInfo.InvariantCulture)
                                + " but the plan names "
                                + planned.SchemaVersion.ToString(CultureInfo.InvariantCulture) + "/"
                                + planned.Value.ToString(CultureInfo.InvariantCulture)
                                + " (P-032, P-053).";
                            return false;
                        }
                    }
                }
            }
            finally
            {
                entities.Dispose();
            }

            if (firstBeyondPlan != null)
            {
                detail = "the built world carries " + builtSlotKeys.Count.ToString(CultureInfo.InvariantCulture)
                    + " authoritative state rows but the plan carries "
                    + plan.Slots.Count.ToString(CultureInfo.InvariantCulture)
                    + "; the first row beyond the plan is " + firstBeyondPlan.ToString() + " (P-032, P-053).";
                return false;
            }

            // Equal counts close the proof: with no row beyond the plan, no duplicated row and every planned key
            // distinct, equal row counts mean the built keys are exactly the planned keys.
            if (builtSlotKeys.Count != plan.Slots.Count)
            {
                detail = "the built world carries " + builtSlotKeys.Count.ToString(CultureInfo.InvariantCulture)
                    + " authoritative state rows but the plan carries "
                    + plan.Slots.Count.ToString(CultureInfo.InvariantCulture) + " (P-032, P-053).";
                return false;
            }

            if (!builtTargets.SetEquals(plannedTargets))
            {
                detail = "the built world carries " + builtTargets.Count.ToString(CultureInfo.InvariantCulture)
                    + " targets but the plan carries "
                    + plannedTargets.Count.ToString(CultureInfo.InvariantCulture) + " (P-004, P-053).";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private RestoreOutcome Refuse(
            WorldId session,
            OperationId operation,
            RestoreStage stage,
            DiagnosticCode code,
            string detail)
        {
            RefusedCount++;
            reservations.TrySettle(session, RestoreAttemptOutcome.Rejected, code, detail);
            _ = operation;
            return RestoreOutcome.Refused(session, stage, code, detail, session);
        }

        /// <summary>
        /// Reports the recorded terminal state of an existing reservation. A repeated restore request never stages a
        /// second world and never returns a second session; it returns what the first attempt concluded (O-21, P-050).
        /// </summary>
        private static RestoreOutcome Replay(WorldId session, RestoreAttempt attempt)
        {
            switch (attempt.Outcome)
            {
                case RestoreAttemptOutcome.Published:
                    return RestoreOutcome.Replay(
                        session,
                        DiagnosticCode.None,
                        "session " + session.Session.ToString()
                        + " was already restored by operation " + attempt.Operation.ToString()
                        + "; a repeated request returns the recorded outcome rather than a second world (O-21, P-050).");
                case RestoreAttemptOutcome.Pending:
                    return RestoreOutcome.Refused(
                        session,
                        RestoreStage.Stage,
                        DiagnosticCode.TooLate,
                        "a restore of session " + session.Session.ToString()
                        + " is still pending on the control lane; one restore per reservation (P-051).",
                        session);
                default:
                    return RestoreOutcome.Refused(
                        session,
                        RestoreStage.Plan,
                        attempt.Code == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : attempt.Code,
                        attempt.Detail,
                        session);
            }
        }

        private static void Discard(UnityWorldHost staging)
        {
            // A staging world that never became the registry's world is destroyed here, so a refused restore leaves
            // neither a live world nor a registry entry behind (O-21's "cancel destroys staged world").
            if (staging.Lifecycle != WorldLifecycleState.Disposed)
            {
                staging.Dispose();
            }
        }
    }
}
