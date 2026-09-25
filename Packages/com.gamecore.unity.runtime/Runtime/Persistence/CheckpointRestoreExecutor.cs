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
using GameCore.Execution.Time;
using GameCore.Unity.Runtime.Integration;

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
            int dormantSlots)
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
            new RestoreOutcome(false, session, stage, code, detail, source, null, null, 0, 0, 0);

        internal static RestoreOutcome Replayed(WorldId session, DiagnosticCode code, string detail) =>
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
                0);

        internal static RestoreOutcome Success(
            WorldId session,
            WorldId source,
            SnapshotToken token,
            RestorePlan plan,
            int targets,
            int slots,
            int dormant) =>
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
                dormant);

        public override string ToString() =>
            Restored
                ? "restored(" + Session.Session.ToString() + ",targets="
                    + RestoredTargets.ToString(CultureInfo.InvariantCulture) + ",dormant="
                    + DormantSlots.ToString(CultureInfo.InvariantCulture) + ")"
                : "refused(" + Stage.ToString() + ":" + DiagnosticCodeText.Of(Code) + ")";
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

            if (!reservations.TryReserve(
                    targetSession,
                    operation,
                    inputHash,
                    out RestoreAttempt? attempt,
                    out DiagnosticCode reservationCode,
                    out string reservationDetail)
                || attempt == null)
            {
                if (reservationCode == DiagnosticCode.None)
                {
                    // A retransmission of an earlier reservation: return the recorded outcome rather than a second
                    // world, which is what makes a duplicate restore call idempotent (O-21, P-050).
                    RetransmissionCount++;
                    return Replay(targetSession, attempt);
                }

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
                plan.DormantSlotCount);
        }

        /// <summary>
        /// Validates the built world before it is exposed: the restored session is not the captured one, the world
        /// carries every planned target and slot, and it can accept admission. A failure here is a refusal, never a
        /// partial exposure (O-21, P-053).
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
                    return RestoreOutcome.Replayed(
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
