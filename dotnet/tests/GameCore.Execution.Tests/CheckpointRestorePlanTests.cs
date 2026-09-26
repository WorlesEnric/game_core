// GameCore.Execution.Tests - restore reservation ledger and restore plan tests (GC-018, O-21, P-050, P-053, P-054).
//
// Two halves. The ledger is the durable answer to "can this session id be reserved for this restore request": one
// entry per reserved session, retained for the process lifetime including after a failed attempt, and a
// retransmission returning the same attempt rather than staging a second world (P-050). The planner is the
// validation half of restore: it turns a verified document into an ordered plan or one actionable refusal, and it
// never produces a partial plan, never mutates its inputs and never plans a state the document does not carry
// (O-21, P-053, P-054).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>One registered directed migration step over the checkpoint document schema (P-054).</summary>
    internal sealed class CheckpointTestMigrationStep : ISchemaMigrationStep
    {
        private static readonly Id128 StepKeyTag = new Id128(0x47433031384D4947UL, 1UL);

        internal CheckpointTestMigrationStep(ulong ordinal, uint fromVersion, uint toVersion)
        {
            Key = new FactoryKey(new Id128(StepKeyTag.High, ordinal), 1U);
            From = new SchemaRef(CheckpointFormat.DocumentSchema.Id, fromVersion);
            To = new SchemaRef(CheckpointFormat.DocumentSchema.Id, toVersion);
        }

        public FactoryKey Key { get; }

        public SchemaRef From { get; }

        public SchemaRef To { get; }
    }

    /// <summary>Restore reservations: one session is reserved once and never handed out again (P-050).</summary>
    [TestFixture]
    public sealed class RestoreReservationLedgerTests
    {
        private static readonly WorldId Session = CheckpointTestFixture.World;
        private static readonly WorldId OtherSession = CheckpointTestFixture.RestoredWorld;
        private static readonly WorldId UnusedSession = CheckpointTestFixture.UnusedWorld;
        private static readonly OperationId Operation = new OperationId(Session, CheckpointTestIds.CommandIssuer, 1UL);
        private static readonly OperationId OtherOperation = new OperationId(OtherSession, CheckpointTestIds.CommandIssuer, 2UL);
        private static readonly ContentHash Hash = ContentHash.Compute(new byte[] { 0x01, 0x02, 0x03 });
        private static readonly ContentHash OtherHash = ContentHash.Compute(new byte[] { 0x04, 0x05, 0x06 });

        [Test]
        public void ARetransmittedRestoreReturnsTheSameFreshReservation()
        {
            var ledger = new RestoreReservationLedger(4);

            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? reserved, out DiagnosticCode code, out string detail),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(reserved, Is.Not.Null);
            Assert.That(reserved!.RequestCount, Is.EqualTo(1));
            Assert.That(reserved.Outcome, Is.EqualTo(RestoreAttemptOutcome.Pending));
            Assert.That(reserved.Session, Is.EqualTo(Session));
            Assert.That(reserved.InputHash, Is.EqualTo(Hash));
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(detail, Is.Empty);
            Assert.That(ledger.Count, Is.EqualTo(1));

            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? retransmitted, out DiagnosticCode secondCode, out string _),
                Is.EqualTo(RestoreReservationKind.Retransmission));
            Assert.That(retransmitted, Is.SameAs(reserved));
            Assert.That(retransmitted!.RequestCount, Is.EqualTo(2));
            Assert.That(secondCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(ledger.RetransmissionCount, Is.EqualTo(1));
            Assert.That(ledger.ConflictCount, Is.Zero);
            Assert.That(ledger.ReuseRejectionCount, Is.Zero);
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void AConflictingOperationIdIsRefusedAndLeavesTheOriginalReservationUntouched()
        {
            var ledger = new RestoreReservationLedger(4);
            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? original, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(original, Is.Not.Null);

            Assert.That(
                ledger.TryReserve(OtherSession, Operation, OtherHash, out RestoreAttempt? conflicting, out DiagnosticCode code, out string detail),
                Is.EqualTo(RestoreReservationKind.IdempotencyConflict));
            Assert.That(conflicting, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(detail, Is.Not.Empty);
            Assert.That(ledger.ConflictCount, Is.EqualTo(1));
            Assert.That(ledger.Count, Is.EqualTo(1));

            Assert.That(original!.InputHash, Is.EqualTo(Hash));
            Assert.That(original.RequestCount, Is.EqualTo(1));
            Assert.That(ledger.TryGet(Session, out RestoreAttempt? kept), Is.True);
            Assert.That(kept, Is.SameAs(original));
            Assert.That(ledger.TryGet(OtherSession, out RestoreAttempt? neverReserved), Is.False);
            Assert.That(neverReserved, Is.Null);
        }

        [Test]
        public void ReusingAReservedSessionIsRefusedWithStaleHandle()
        {
            var ledger = new RestoreReservationLedger(4);
            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? reserved, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));

            Assert.That(
                ledger.TryReserve(Session, OtherOperation, OtherHash, out RestoreAttempt? reused, out DiagnosticCode code, out string detail),
                Is.EqualTo(RestoreReservationKind.SessionInUse));
            Assert.That(reused, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(detail, Does.Contain(Session.Session.ToString()));
            Assert.That(ledger.ReuseRejectionCount, Is.EqualTo(1));
            Assert.That(ledger.ConflictCount, Is.Zero);
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.TryGet(OtherOperation, out RestoreAttempt? _), Is.False);
            Assert.That(ledger.TryGet(Session, out RestoreAttempt? stillReserved), Is.True);
            Assert.That(stillReserved, Is.SameAs(reserved));
        }

        [Test]
        public void AnAllZeroSessionIsAnInvalidReservation()
        {
            var ledger = new RestoreReservationLedger(4);

            Assert.That(
                ledger.TryReserve(default(WorldId), Operation, Hash, out RestoreAttempt? attempt, out DiagnosticCode code, out string detail),
                Is.EqualTo(RestoreReservationKind.InvalidSession));
            Assert.That(attempt, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(detail, Is.Not.Empty);
            Assert.That(ledger.Count, Is.Zero);

            // A refused reservation leaves the ledger usable for a well-formed request (P-052).
            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? reserved, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(reserved, Is.Not.Null);
            Assert.That(ledger.Count, Is.EqualTo(1));
        }

        [Test]
        public void ALedgerFillsToCapacityAndRefusesFurtherReservations()
        {
            var ledger = new RestoreReservationLedger(1);

            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? first, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(first, Is.Not.Null);
            Assert.That(ledger.Capacity, Is.EqualTo(1));

            Assert.That(
                ledger.TryReserve(OtherSession, OtherOperation, OtherHash, out RestoreAttempt? refused, out DiagnosticCode code, out string detail),
                Is.EqualTo(RestoreReservationKind.LedgerFull));
            Assert.That(refused, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(detail, Is.Not.Empty);
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.TryGet(OtherSession, out RestoreAttempt? _), Is.False);
        }

        [Test]
        public void ALedgerRequiresPositiveCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new RestoreReservationLedger(0); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new RestoreReservationLedger(-1); });
            Assert.That(new RestoreReservationLedger(1).Capacity, Is.EqualTo(1));
        }

        [Test]
        public void ASettledAttemptKeepsItsTerminalOutcomeAndAFailedAttemptIsNeverForgotten()
        {
            var ledger = new RestoreReservationLedger(4);
            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? attempt, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(attempt, Is.Not.Null);
            Assert.That(attempt!.IsTerminal, Is.False);

            // Pending is a reservation awaiting an outcome, not an outcome: it can still be settled (O-21).
            Assert.That(ledger.TrySettle(Session, RestoreAttemptOutcome.Pending, DiagnosticCode.None, "reserved"), Is.True);
            Assert.That(attempt.IsTerminal, Is.False);
            Assert.That(attempt.Outcome, Is.EqualTo(RestoreAttemptOutcome.Pending));

            Assert.That(ledger.TrySettle(Session, RestoreAttemptOutcome.Published, DiagnosticCode.None, "published"), Is.True);
            Assert.That(attempt.IsTerminal, Is.True);
            Assert.That(attempt.Outcome, Is.EqualTo(RestoreAttemptOutcome.Published));
            Assert.That(attempt.Detail, Is.EqualTo("published"));

            // A terminal outcome is never overwritten: a later report cannot unpublish an exposed world (P-030, P-051).
            Assert.That(ledger.TrySettle(Session, RestoreAttemptOutcome.Rejected, DiagnosticCode.StaleHandle, "rejected"), Is.False);
            Assert.That(attempt.Outcome, Is.EqualTo(RestoreAttemptOutcome.Published));
            Assert.That(attempt.Detail, Is.EqualTo("published"));

            // A failed attempt's reservation is retained for the process lifetime, so the session id is not reusable
            // and the ledger never shrinks (P-050).
            Assert.That(
                ledger.TryReserve(OtherSession, OtherOperation, OtherHash, out RestoreAttempt? failed, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));
            Assert.That(ledger.TrySettle(OtherSession, RestoreAttemptOutcome.Rejected, DiagnosticCode.MigrationRequired, "refused"), Is.True);
            Assert.That(failed!.Outcome, Is.EqualTo(RestoreAttemptOutcome.Rejected));
            Assert.That(ledger.TrySettle(OtherSession, RestoreAttemptOutcome.Published, DiagnosticCode.None, "late"), Is.False);
            Assert.That(failed.Outcome, Is.EqualTo(RestoreAttemptOutcome.Rejected));
            Assert.That(ledger.Count, Is.EqualTo(2));
            Assert.That(ledger.Attempts.Count, Is.EqualTo(2));

            Assert.That(
                ledger.TryReserve(OtherSession, OtherOperation, OtherHash, out RestoreAttempt? afterFailure, out DiagnosticCode reuseCode, out string _),
                Is.EqualTo(RestoreReservationKind.SessionInUse));
            Assert.That(afterFailure, Is.Null);
            Assert.That(reuseCode, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(ledger.Count, Is.EqualTo(2));

            Assert.That(ledger.TrySettle(UnusedSession, RestoreAttemptOutcome.Published, DiagnosticCode.None, "unknown"), Is.False);
        }

        [Test]
        public void LookupsResolveBySessionAndByOperationAndNeverInventAnAttempt()
        {
            var ledger = new RestoreReservationLedger(4);
            Assert.That(
                ledger.TryReserve(Session, Operation, Hash, out RestoreAttempt? reserved, out DiagnosticCode _, out string _),
                Is.EqualTo(RestoreReservationKind.Fresh));

            Assert.That(ledger.TryGet(Session, out RestoreAttempt? bySession), Is.True);
            Assert.That(bySession, Is.SameAs(reserved));
            Assert.That(ledger.TryGet(Operation, out RestoreAttempt? byOperation), Is.True);
            Assert.That(byOperation, Is.SameAs(reserved));

            Assert.That(ledger.TryGet(UnusedSession, out RestoreAttempt? unknownSession), Is.False);
            Assert.That(unknownSession, Is.Null);
            Assert.That(
                ledger.TryGet(new OperationId(UnusedSession, CheckpointTestIds.CommandIssuer, 5UL), out RestoreAttempt? unknownOperation),
                Is.False);
            Assert.That(unknownOperation, Is.Null);
            Assert.That(ledger.Count, Is.EqualTo(1));
        }
    }

    /// <summary>
    /// Restore validation: a verified document either plans completely into a fresh session, or is refused with one
    /// actionable reason and no partial plan (O-21, P-053, P-054).
    /// </summary>
    [TestFixture]
    public sealed class CheckpointRestorePlanTests
    {
        [Test]
        public void ASelfConsistentDocumentPlansIntoAFreshSessionWithItsDormantState()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(CheckpointTestFixture.Minimum(), codecs);

            RestorePlanResult result = CheckpointRestorePlanner.Plan(
                Request(document, codecs, CheckpointTestFixture.RestoredWorld));

            Assert.That(result.Succeeded, Is.True, result.Detail);
            Assert.That(result.Refusal, Is.EqualTo(RestoreRefusal.None));
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(result.Diagnostics, Is.Empty);

            RestorePlan plan = result.Plan!;
            Assert.That(plan.TargetSession, Is.EqualTo(CheckpointTestFixture.RestoredWorld));
            Assert.That(
                plan.TargetSession.Session,
                Is.Not.EqualTo(document.Header.SourceSession.Session));
            Assert.That(plan.Header.LogicalStep, Is.EqualTo(document.Header.LogicalStep));
            AssertSameCounts(document.Counts, plan.Counts);
            Assert.That(plan.Scopes.Count, Is.EqualTo(1));
            Assert.That(plan.Installs.Count, Is.EqualTo(1));
            Assert.That(plan.Targets.Count, Is.EqualTo(1));
            Assert.That(plan.Slots.Count, Is.EqualTo(2));
            Assert.That(plan.DormantSlotCount, Is.EqualTo(1));
            Assert.That(plan.Slots[0].Active, Is.True);
            Assert.That(plan.Slots[1].Active, Is.False);
            Assert.That(plan.RngStreams.Count, Is.EqualTo(1));
            Assert.That(plan.Cursors.Count, Is.EqualTo(1));
            Assert.That(plan.IsDirect, Is.True);
            Assert.That(plan.Migrations, Is.Empty);
            Assert.That(plan.DocumentHash, Is.EqualTo(document.DocumentHash));
            Assert.That(plan.DocumentHash, Is.EqualTo(ContentHash.Compute(document.RawBytes)));
        }

        [Test]
        public void RestoringIntoTheCapturedSessionIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(CheckpointTestFixture.Minimum(), codecs);

            // The captured session is never a restore target: recovery always creates a new WorldId, so old callbacks
            // and handles never become valid (P-004, P-049).
            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.World);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.ReservationRejected, DiagnosticCode.IdempotencyConflict);
            Assert.That(result.Detail, Does.Contain("captured session"));
            Assert.That(CheckpointTestFixture.World.Session, Is.EqualTo(document.Header.SourceSession.Session));
        }

        [Test]
        public void AnAllZeroRestoreTargetIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(CheckpointTestFixture.Minimum(), codecs);

            CheckpointRestoreRequest request = Request(document, codecs, default(WorldId));
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.ReservationRejected, DiagnosticCode.UnsupportedVersion);
            Assert.That(result.Detail, Does.Contain("caller-reserved fresh session"));
        }

        [Test]
        public void AProtocolMajorTwoDocumentIsRefusedWithUnsupportedVersionBeforeItReachesThePlanner()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            HeaderRecordValue unsupported = UnsupportedProtocolHeader();
            Assert.That(unsupported.ProtocolMajor, Is.EqualTo(2U));
            Assert.That(unsupported.IsSupportedProtocol, Is.False);

            // The document reader refuses the protocol before any plan can exist, so the planner's own protocol
            // guard is a backstop for a document the public surface cannot even produce (P-055).
            Assert.That(
                CheckpointDocument.TryRead(
                    Frame(unsupported),
                    codecs,
                    out CheckpointDocument? read,
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(read, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(detail, Is.Not.Empty);
            Assert.That(CheckpointFormat.ProtocolMajor, Is.EqualTo((byte)1));
        }

        [Test]
        public void ACatalogFingerprintMismatchRefusesUnlessTheCallerRelaxesIt()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(CheckpointTestFixture.Minimum(), codecs);
            ContentHash foreign = ContentHash.Compute(new byte[] { 0x11, 0x22, 0x33 });
            Assert.That(foreign, Is.Not.EqualTo(document.Header.CatalogFingerprint));

            CheckpointRestoreRequest refused = Request(
                document, codecs, CheckpointTestFixture.RestoredWorld, catalogFingerprint: foreign);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(refused);
            AssertRefused(result, refused, RestoreRefusal.CatalogMismatch, DiagnosticCode.UnsupportedVersion);

            // RequireCatalogMatch is the only reason the same document plans, which is why it defaults to true.
            CheckpointRestoreRequest relaxed = Request(
                document, codecs, CheckpointTestFixture.RestoredWorld, catalogFingerprint: foreign, requireCatalogMatch: false);
            RestorePlanResult planned = CheckpointRestorePlanner.Plan(relaxed);
            Assert.That(planned.Succeeded, Is.True, planned.Detail);
            Assert.That(planned.Plan!.IsDirect, Is.True);

            CheckpointRestoreRequest matching = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            Assert.That(CheckpointRestorePlanner.Plan(matching).Succeeded, Is.True);
        }

        [Test]
        public void ASlotNamingAnUndeclaredTargetIsACorruptReference()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Boundary(
                scopes: CheckpointTestFixture.SingleRootScope(),
                targets: new[] { CheckpointTestFixture.Targets()[0] },
                slots: new[]
                {
                    CheckpointTestFixture.Slot(CheckpointTestIds.UndeclaredTarget, CheckpointTestIds.SlotActive, 5, true),
                });
            CheckpointDocument document = Capture(boundary, codecs);

            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.CorruptReference, DiagnosticCode.MissingDependency);
            Assert.That(result.Diagnostics, Is.Not.Null);
            Assert.That(result.Diagnostics, Is.Not.Empty);
            Assert.That(result.Diagnostics[0].Summary, Is.Not.Empty);
            Assert.That(result.Detail, Does.Contain(CheckpointTestIds.UndeclaredTarget.ToString()));
        }

        [Test]
        public void ADocumentWithNoScopeIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: Array.Empty<ScopeRecordValue>()),
                codecs);

            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.InvalidComposition, DiagnosticCode.MissingDependency);
            Assert.That(result.Detail, Does.Contain("no scope"));
        }

        [Test]
        public void ADocumentWithTwoRootScopesIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: TwoRootScopes()),
                codecs);

            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.InvalidComposition, DiagnosticCode.MissingDependency);
            Assert.That(result.Detail, Does.Contain("root scopes"));
        }

        [Test]
        public void AParentCycleIsRefused()
        {
            // The cycle is built from the raw record constructors: the planner must refuse a decoded document whose
            // parent chain loops, because such a tree cannot be rebuilt as one rooted acyclic tree (P-010).
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: CycleScopes()),
                codecs);

            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.InvalidComposition, DiagnosticCode.MissingDependency);
            Assert.That(result.Detail, Does.Contain("cycle"));
        }

        [Test]
        public void TwoSlotRecordsForOneAuthoritativeKeyAreRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(
                    scopes: CheckpointTestFixture.SingleRootScope(),
                    targets: new[] { CheckpointTestFixture.Targets()[0] },
                    slots: DuplicateSlotPair()),
                codecs);

            CheckpointRestoreRequest request = Request(document, codecs, CheckpointTestFixture.RestoredWorld);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            AssertRefused(result, request, RestoreRefusal.MissingRequiredState, DiagnosticCode.MigrationRequired);
            Assert.That(result.Detail, Does.Contain("more than once"));
        }

        [Test]
        public void ASchemaVersionWithoutAPathIsRefusedWithTheMigrationPlanCode()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: CheckpointTestFixture.SingleRootScope()),
                codecs);
            var registry = new CheckpointMigrationRegistry(null);
            var allocated = new[] { new SchemaRef(CheckpointFormat.DocumentSchema.Id, 2U) };

            CheckpointRestoreRequest request = Request(
                document, codecs, CheckpointTestFixture.RestoredWorld, migrations: registry, allocatedSchemas: allocated);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            MigrationPlan expected = registry.Plan(CheckpointFormat.DocumentSchema, allocated[0]);
            Assert.That(expected.Outcome, Is.EqualTo(MigrationPlanOutcome.Unreachable));
            Assert.That(expected.IsRunnable, Is.False);
            AssertRefused(result, request, RestoreRefusal.MigrationRejected, expected.Code);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
        }

        [Test]
        public void AnAmbiguousMigrationGraphIsRefusedWithTheMigrationPlanCode()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: CheckpointTestFixture.SingleRootScope()),
                codecs);

            // Two distinct chains reach version 3, which the protocol refuses rather than silently picking one.
            var registry = new CheckpointMigrationRegistry(new ISchemaMigrationStep[]
            {
                new CheckpointTestMigrationStep(1UL, 1U, 2U),
                new CheckpointTestMigrationStep(2UL, 2U, 3U),
                new CheckpointTestMigrationStep(3UL, 1U, 3U),
            });
            Assert.That(registry.IsWellFormed, Is.True);
            var allocated = new[] { new SchemaRef(CheckpointFormat.DocumentSchema.Id, 3U) };

            CheckpointRestoreRequest request = Request(
                document, codecs, CheckpointTestFixture.RestoredWorld, migrations: registry, allocatedSchemas: allocated);
            RestorePlanResult result = CheckpointRestorePlanner.Plan(request);

            MigrationPlan expected = registry.Plan(CheckpointFormat.DocumentSchema, allocated[0]);
            Assert.That(expected.Outcome, Is.EqualTo(MigrationPlanOutcome.Ambiguous));
            Assert.That(expected.PathCount, Is.EqualTo(2));
            AssertRefused(result, request, RestoreRefusal.MigrationRejected, expected.Code);
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
        }

        [Test]
        public void AUniqueMigrationPathIsPlannedRatherThanRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(
                CheckpointTestFixture.Boundary(scopes: CheckpointTestFixture.SingleRootScope()),
                codecs);
            var registry = new CheckpointMigrationRegistry(new ISchemaMigrationStep[]
            {
                new CheckpointTestMigrationStep(1UL, 1U, 2U),
                new CheckpointTestMigrationStep(2UL, 2U, 3U),
            });
            var allocated = new[] { new SchemaRef(CheckpointFormat.DocumentSchema.Id, 3U) };

            RestorePlanResult result = CheckpointRestorePlanner.Plan(Request(
                document, codecs, CheckpointTestFixture.RestoredWorld, migrations: registry, allocatedSchemas: allocated));

            Assert.That(result.Succeeded, Is.True, result.Detail);
            RestorePlan plan = result.Plan!;
            Assert.That(plan.IsDirect, Is.False);
            Assert.That(plan.Migrations.Count, Is.EqualTo(1));
            Assert.That(plan.Migrations[0].Outcome, Is.EqualTo(MigrationPlanOutcome.Unique));
            Assert.That(plan.Migrations[0].Steps.Count, Is.EqualTo(2));
            Assert.That(plan.Migrations[0].From.Version, Is.EqualTo(1U));
            Assert.That(plan.Migrations[0].To.Version, Is.EqualTo(3U));
        }

        [Test]
        public void EveryRefusalShapeYieldsNoPartialPlanAndReplaysIdentically()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            var requests = new List<CheckpointRestoreRequest>
            {
                Request(
                    Capture(CheckpointTestFixture.Boundary(scopes: Array.Empty<ScopeRecordValue>()), codecs),
                    codecs,
                    CheckpointTestFixture.RestoredWorld),
                Request(
                    Capture(CheckpointTestFixture.Boundary(scopes: TwoRootScopes()), codecs),
                    codecs,
                    CheckpointTestFixture.RestoredWorld),
                Request(
                    Capture(CheckpointTestFixture.Boundary(scopes: CycleScopes()), codecs),
                    codecs,
                    CheckpointTestFixture.RestoredWorld),
                Request(
                    Capture(
                        CheckpointTestFixture.Boundary(
                            scopes: CheckpointTestFixture.SingleRootScope(),
                            targets: new[] { CheckpointTestFixture.Targets()[0] },
                            slots: DuplicateSlotPair()),
                        codecs),
                    codecs,
                    CheckpointTestFixture.RestoredWorld),
                Request(
                    Capture(
                        CheckpointTestFixture.Boundary(
                            scopes: CheckpointTestFixture.SingleRootScope(),
                            targets: new[] { CheckpointTestFixture.Targets()[0] },
                            slots: new[]
                            {
                                CheckpointTestFixture.Slot(
                                    CheckpointTestIds.UndeclaredTarget, CheckpointTestIds.SlotActive, 5, true),
                            }),
                        codecs),
                    codecs,
                    CheckpointTestFixture.RestoredWorld),
                Request(Capture(CheckpointTestFixture.Minimum(), codecs), codecs, CheckpointTestFixture.World),
                Request(Capture(CheckpointTestFixture.Minimum(), codecs), codecs, default(WorldId)),
            };

            for (int i = 0; i < requests.Count; i++)
            {
                AssertRefused(
                    CheckpointRestorePlanner.Plan(requests[i]),
                    requests[i],
                    RestoreRefusal.None,
                    DiagnosticCode.None,
                    refusalMayVary: true);
            }
        }

        /// <summary>
        /// GC-021: the outbox section a checkpoint carries is reinstated by a restore, so the plan must carry it. A
        /// capture of a world whose outbox holds one open obligation and one delivery cursor produces a plan whose
        /// `Outbox` list holds exactly those rows, with the obligation still open — that is what "source unload does
        /// not erase committed delivery obligation" means at the plan level (P-045, P-053).
        /// </summary>
        [Test]
        public void APlanCarriesTheOutboxRowsTheDocumentDeclares()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Boundary(
                scopes: CheckpointTestFixture.Scopes(),
                installs: CheckpointTestFixture.Installs(),
                selections: CheckpointTestFixture.Selections(),
                targets: CheckpointTestFixture.Targets(),
                slots: CheckpointTestFixture.Slots(),
                messages: CheckpointTestFixture.Messages(),
                rngStreams: CheckpointTestFixture.RngStreams(),
                cursors: CheckpointTestFixture.Cursors(),
                outbox: CheckpointTestFixture.OutboxRows());
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(boundary, codecs);

            Assert.That(
                document.Header.OutboxCount,
                Is.EqualTo(2U),
                "the header declares the outbox rows the document carries (P-053)");
            Assert.That(document.Counts.Outbox, Is.EqualTo(2));
            Assert.That(
                document.TryReadRecords(
                    CheckpointRecordKind.Outbox,
                    out IReadOnlyList<OutboxRecordValue> rows,
                    out DiagnosticCode readCode,
                    out string readDetail),
                Is.True,
                readDetail);
            Assert.That(readCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(rows.Count, Is.EqualTo(2));

            RestorePlanResult result = CheckpointRestorePlanner.Plan(
                Request(document, codecs, CheckpointTestFixture.RestoredWorld));

            Assert.That(result.Succeeded, Is.True, result.Detail);
            Assert.That(result.Plan, Is.Not.Null);
            RestorePlan plan = result.Plan!;
            Assert.That(plan.Outbox.Count, Is.EqualTo(2), "the plan reinstates the outbox the document carries");
            Assert.That(plan.Counts.Outbox, Is.EqualTo(2));

            int open = 0;
            int cursors = 0;
            for (int i = 0; i < plan.Outbox.Count; i++)
            {
                if (plan.Outbox[i].IsOpen)
                {
                    open++;
                }

                if (plan.Outbox[i].Row == OutboxRowKind.Cursor)
                {
                    cursors++;
                }
            }

            Assert.That(open, Is.EqualTo(1), "the open obligation survives the capture/plan round trip (P-045)");
            Assert.That(cursors, Is.EqualTo(1), "the per-destination delivery cursor travels with it (P-053)");
        }

        /// <summary>
        /// GC-021: a cursor that disagrees with the terminal rows its own document carries is refused. A restore that
        /// accepted it would believe it still retained a terminal record the document dropped, which is exactly the
        /// silent loss P-043 and P-045 forbid (P-008: one record, one answer).
        /// </summary>
        [Test]
        public void ACursorThatDisagreesWithItsTerminalRowsRefusesThePlan()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Boundary(
                scopes: CheckpointTestFixture.Scopes(),
                installs: CheckpointTestFixture.Installs(),
                selections: CheckpointTestFixture.Selections(),
                targets: CheckpointTestFixture.Targets(),
                slots: CheckpointTestFixture.Slots(),
                messages: CheckpointTestFixture.Messages(),
                rngStreams: CheckpointTestFixture.RngStreams(),
                cursors: CheckpointTestFixture.Cursors(),
                outbox: CheckpointTestFixture.OutboxRowsWithOverstatedCursor());
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(boundary, codecs);

            RestorePlanResult result = CheckpointRestorePlanner.Plan(
                Request(document, codecs, CheckpointTestFixture.RestoredWorld));

            Assert.That(result.Succeeded, Is.False, "an outbox section that contradicts itself is never planned");
            Assert.That(result.Plan, Is.Null);
            Assert.That(result.Refusal, Is.EqualTo(RestoreRefusal.InvalidOutbox));
            Assert.That(result.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(result.Detail, Does.Contain("retained terminal"));
        }

        /// <summary>
        /// GC-021: two rows claiming one obligation identity are refused. One obligation has one record, and a
        /// document that carries two cannot be restored into a world that must be able to answer "is this already
        /// delivered?" (P-008, P-045).
        /// </summary>
        [Test]
        public void TwoObligationRowsForOneIdentityRefuseThePlan()
        {
            CommittedBoundarySnapshot boundary = CheckpointTestFixture.Boundary(
                scopes: CheckpointTestFixture.Scopes(),
                installs: CheckpointTestFixture.Installs(),
                selections: CheckpointTestFixture.Selections(),
                targets: CheckpointTestFixture.Targets(),
                slots: CheckpointTestFixture.Slots(),
                messages: CheckpointTestFixture.Messages(),
                rngStreams: CheckpointTestFixture.RngStreams(),
                cursors: CheckpointTestFixture.Cursors(),
                outbox: CheckpointTestFixture.DuplicatedObligationRows());
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            CheckpointDocument document = Capture(boundary, codecs);

            RestorePlanResult result = CheckpointRestorePlanner.Plan(
                Request(document, codecs, CheckpointTestFixture.RestoredWorld));

            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Refusal, Is.EqualTo(RestoreRefusal.InvalidOutbox));
            Assert.That(result.Detail, Does.Contain("appears twice"));
        }

        private static CheckpointDocument Capture(CommittedBoundarySnapshot boundary, CheckpointCodecSet codecs)
        {
            CheckpointCaptureResult captured = CheckpointCapture.Capture(
                CheckpointTestFixture.Reader(boundary),
                CheckpointTestFixture.Request(codecs, catalogFingerprint: boundary.CatalogFingerprint));
            Assert.That(captured.Captured, Is.True, captured.Detail);
            Assert.That(
                CheckpointDocument.TryRead(
                    captured.Document,
                    codecs,
                    out CheckpointDocument? document,
                    out DiagnosticCode code,
                    out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(document, Is.Not.Null);
            return document!;
        }

        private static CheckpointRestoreRequest Request(
            CheckpointDocument document,
            CheckpointCodecSet codecs,
            WorldId targetSession,
            ContentHash? catalogFingerprint = null,
            CheckpointMigrationRegistry? migrations = null,
            IReadOnlyList<SchemaRef>? allocatedSchemas = null,
            bool requireCatalogMatch = true) =>
            new CheckpointRestoreRequest(
                targetSession,
                document,
                codecs,
                migrations ?? new CheckpointMigrationRegistry(null),
                catalogFingerprint ?? document.Header.CatalogFingerprint,
                allocatedSchemas,
                requireCatalogMatch);

        /// <summary>
        /// Asserts one refusal: it names a reason and a code, it never carries a partial plan (P-053), and planning
        /// the identical request again mutates nothing, because the document, the request and the boundary are
        /// read-only inputs (O-21's "rejects without affecting existing world").
        /// </summary>
        private static void AssertRefused(
            RestorePlanResult result,
            CheckpointRestoreRequest request,
            RestoreRefusal refusal,
            DiagnosticCode code,
            bool refusalMayVary = false)
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Plan, Is.Null);
            if (!refusalMayVary)
            {
                Assert.That(result.Refusal, Is.EqualTo(refusal));
                Assert.That(result.Code, Is.EqualTo(code));
            }
            else
            {
                Assert.That(result.Refusal, Is.Not.EqualTo(RestoreRefusal.None));
                Assert.That(result.Code, Is.Not.EqualTo(DiagnosticCode.None));
            }

            Assert.That(result.Detail, Is.Not.Empty);

            byte[] bytes = request.Document.RawBytes;
            var copy = new byte[bytes.Length];
            Buffer.BlockCopy(bytes, 0, copy, 0, bytes.Length);

            RestorePlanResult replay = CheckpointRestorePlanner.Plan(request);
            Assert.That(replay.Succeeded, Is.False);
            Assert.That(replay.Plan, Is.Null);
            Assert.That(replay.Refusal, Is.EqualTo(result.Refusal));
            Assert.That(replay.Code, Is.EqualTo(result.Code));
            Assert.That(replay.Detail, Is.EqualTo(result.Detail));
            Assert.That(replay.Diagnostics.Count, Is.EqualTo(result.Diagnostics.Count));
            Assert.That(bytes, Is.EqualTo(copy));
        }

        private static void AssertSameCounts(CheckpointCounts expected, CheckpointCounts actual)
        {
            Assert.That(actual.Scopes, Is.EqualTo(expected.Scopes));
            Assert.That(actual.Installs, Is.EqualTo(expected.Installs));
            Assert.That(actual.Selections, Is.EqualTo(expected.Selections));
            Assert.That(actual.Targets, Is.EqualTo(expected.Targets));
            Assert.That(actual.Slots, Is.EqualTo(expected.Slots));
            Assert.That(actual.Grants, Is.EqualTo(expected.Grants));
            Assert.That(actual.Clocks, Is.EqualTo(expected.Clocks));
            Assert.That(actual.Commands, Is.EqualTo(expected.Commands));
            Assert.That(actual.Messages, Is.EqualTo(expected.Messages));
            Assert.That(actual.RngStreams, Is.EqualTo(expected.RngStreams));
            Assert.That(actual.Cursors, Is.EqualTo(expected.Cursors));
        }

        private static ScopeRecordValue[] TwoRootScopes() => new[]
        {
            CheckpointTestFixture.SingleRootScope()[0],
            new ScopeRecordValue(
                CheckpointTestIds.SecondRootScope.High, CheckpointTestIds.SecondRootScope.Low,
                0UL, 0UL,
                0U, (uint)PropagationMode.Conservative,
                0U, 0U,
                false, false,
                0U, 0U),
        };

        private static ScopeRecordValue[] CycleScopes() => new[]
        {
            CheckpointTestFixture.SingleRootScope()[0],
            new ScopeRecordValue(
                CheckpointTestIds.CycleScopeA.High, CheckpointTestIds.CycleScopeA.Low,
                CheckpointTestIds.CycleScopeB.High, CheckpointTestIds.CycleScopeB.Low,
                1U, (uint)PropagationMode.Conservative,
                0U, 0U,
                false, false,
                0U, 0U),
            new ScopeRecordValue(
                CheckpointTestIds.CycleScopeB.High, CheckpointTestIds.CycleScopeB.Low,
                CheckpointTestIds.CycleScopeA.High, CheckpointTestIds.CycleScopeA.Low,
                1U, (uint)PropagationMode.Conservative,
                0U, 0U,
                false, false,
                0U, 0U),
        };

        private static SlotRecordValue[] DuplicateSlotPair() => new[]
        {
            CheckpointTestFixture.Slot(CheckpointTestIds.TargetA, CheckpointTestIds.SlotActive, 11, true),
            CheckpointTestFixture.Slot(CheckpointTestIds.TargetA, CheckpointTestIds.SlotActive, 22, false),
        };

        /// <summary>
        /// The header record a future protocol would write: protocol 2.0 and no body records. It is built here rather
        /// than captured because a capture refuses to write it (P-055).
        /// </summary>
        private static HeaderRecordValue UnsupportedProtocolHeader() => new HeaderRecordValue(
            CheckpointTestIds.WorldDefinition.High,
            CheckpointTestIds.WorldDefinition.Low,
            CheckpointTestIds.WorldSession.High,
            CheckpointTestIds.WorldSession.Low,
            2U,
            0U,
            (uint)TemporalModel.FixedStep,
            CheckpointTestFixture.StepDurationTicks,
            CheckpointTestFixture.TicksPerSecond,
            CheckpointTestFixture.MaxStepsPerPump,
            false,
            CheckpointTestFixture.LogicalStep,
            CheckpointTestFixture.RetainedDebtTicks,
            CheckpointTestFixture.DomainSeconds,
            CheckpointTestFixture.PendingDemand,
            (uint)PropagationMode.Automatic,
            0UL,
            0UL,
            0UL,
            0UL,
            (uint)CheckpointQueuePolicy.RejectQueued,
            CheckpointTestFixture.AdmissionCutoff,
            0U,
            CheckpointTestFixture.LastEventSequence,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            0U,
            CheckpointTestFixture.PublishedRevision,
            CheckpointTestFixture.PublishedEpoch,
            CheckpointTestFixture.HostTicksPerSecond,
            0U,
            0U);

        /// <summary>
        /// Frames one header record in a canonical checkpoint container whose own protocol is this build's, which is
        /// the document a reader decodes a record from. The record's declared counts are zero, so no body record is
        /// needed to make the container internally consistent.
        /// </summary>
        private static byte[] Frame(HeaderRecordValue header)
        {
            byte[] record = new HeaderRecordCodec().Encode(header);
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(
                    CheckpointFormat.ProtocolMajor,
                    CheckpointFormat.ProtocolMinor,
                    CheckpointFormat.DocumentSchema,
                    CheckpointFormat.KnownFeatureIds),
                CheckpointFormat.Limits);
            writer.WriteBytesField(CheckpointFormat.HeaderFieldId, record);
            writer.WriteChecksum();
            return writer.ToArray();
        }
    }
}
