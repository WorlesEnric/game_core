#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Adapters.Fixtures;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;
using NUnit.Framework;

namespace GameCore.Adapters.Tests
{
    /// <summary>
    /// The GC-019 pure adapter contracts (docs/game-core/09-implementation-guide.md GC-019; 08 TEST-002, TEST-015,
    /// TEST-018, TEST-019, TEST-020):
    ///
    ///   "stamped typed input ingress (into existing command ports), asynchronous asset leases (bounded; tokens
    ///    validated at completion — late completions cannot write retired worlds), GameObject/Transform view registry
    ///    with stable target-view maps and committed-output presentation from snapshots ... an external
    ///    physical-authority descriptor and adapter seam WITHOUT requiring physics in card/narrative worlds. Adapter
    ///    mirrors are never separately authoritative."
    ///
    /// Everything here is in-process: a recording command port stands in for the world's own `ICommandIngress`, so
    /// the assertions are about the adapter's decisions (stamping, idempotent retry, gating, bounding) and never
    /// about Unity. The Unity worlds those commands reach are covered by the PlayMode/qualification suites.
    /// </summary>
    [TestFixture]
    public sealed class AdapterContractTests
    {
        private static readonly Id128 Issuer = new Id128(0x4743303139495353UL, 1UL);
        private static readonly Id128 Source = new Id128(0x4743303139535243UL, 1UL);
        private static readonly Id128 Domain = new Id128(0x4743303139444F4DUL, 7UL);
        private static readonly OwnerId PhysicsOwner = new OwnerId(new Id128(0x4743303139504859UL, 3UL));

        private static readonly RouteId Route = new RouteId(new Id128(0x4743303139524F55UL, 1UL));
        private static readonly TargetId Target = new TargetId(new Id128(0x4743303139544152UL, 1UL));
        private static readonly TargetId OtherTarget = new TargetId(new Id128(0x4743303139544152UL, 2UL));
        private static readonly SchemaRef CommandSchema = new SchemaRef(new SchemaId(new Id128(0x4743303139534348UL, 1UL)), 1U);

        private static int worldCounter;

        private static WorldId NextWorld()
        {
            worldCounter++;
            return new WorldId(new Id128(0x4743303139574F52UL, (ulong)worldCounter));
        }

        private static InputSourceStamp StampOf(WorldId world, ulong sequence) =>
            new InputSourceStamp(world, Source, sequence, LogicalStepId.Zero, AssemblyEpoch.First);

        private static SampledInputCommand CommandOf(InputSourceStamp stamp, int value) =>
            new SampledInputCommand(
                stamp,
                Route,
                Target,
                CommandSchema,
                null,
                new FrozenPayload(CommandPayloadCodec.Int32(value)));

        // ------------------------------------------------------------------ input ingress

        /// <summary>
        /// A sample becomes one command with the sample's own identity as its request key, and a retransmission of the
        /// same sample returns the recorded admission instead of executing twice (P-037, P-050).
        /// </summary>
        [Test]
        public void ARetransmittedSampleReturnsTheRecordedAdmissionAndDoesNotExecuteTwice()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 8U);

            InputAdmissionResult first = ingress.Submit(CommandOf(StampOf(world, 1UL), 42));
            InputAdmissionResult retry = ingress.Submit(CommandOf(StampOf(world, 1UL), 42));

            Assert.That(first.Outcome, Is.EqualTo(InputAdmissionOutcome.Admitted), first.ToString());
            Assert.That(retry.Outcome, Is.EqualTo(InputAdmissionOutcome.Retransmission), retry.ToString());
            Assert.That(port.SubmittedCount, Is.EqualTo(1), "the port must have seen exactly one envelope (P-050).");
            Assert.That(retry.Admission, Is.SameAs(first.Admission), "the recorded receipt is returned (P-050).");
        }

        /// <summary>The same request key with different content is an `IdempotencyConflict`, never a second attempt.</summary>
        [Test]
        public void TheSameSampleIdentityWithDifferentContentIsRefused()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 8U);

            ingress.Submit(CommandOf(StampOf(world, 1UL), 42));
            InputAdmissionResult conflict = ingress.Submit(CommandOf(StampOf(world, 1UL), 43));

            Assert.That(conflict.Outcome, Is.EqualTo(InputAdmissionOutcome.IdempotencyConflict), conflict.ToString());
            Assert.That(conflict.Code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(port.SubmittedCount, Is.EqualTo(1));
            Assert.That(ingress.ConflictCount, Is.EqualTo(1));
        }

        /// <summary>A source's sequences are strictly increasing, and one source's namespace is its own (P-050).</summary>
        [Test]
        public void ARegressingSourceSequenceIsRefusedAndAGapIsLegal()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 8U);

            Assert.That(ingress.Submit(CommandOf(StampOf(world, 1UL), 1)).Accepted, Is.True);
            // A gap is legal: the sequence identifies the source's events, it does not have to be contiguous.
            Assert.That(ingress.Submit(CommandOf(StampOf(world, 7UL), 2)).Accepted, Is.True);
            InputAdmissionResult behind = ingress.Submit(CommandOf(StampOf(world, 3UL), 3));

            Assert.That(behind.Outcome, Is.EqualTo(InputAdmissionOutcome.RejectedSequenceRegression), behind.ToString());
            Assert.That(port.SubmittedCount, Is.EqualTo(2), "a regressing sample never reaches the command port.");
            Assert.That(ingress.RegressionCount, Is.EqualTo(1));
        }

        /// <summary>A same-key retry outside the recorded retention is `ResultExpired`, never a silent re-execution.</summary>
        [Test]
        public void ARetryOutsideTheBoundedRetentionReportsResultExpired()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 1U);

            ingress.Submit(CommandOf(StampOf(world, 1UL), 1));
            ingress.Submit(CommandOf(StampOf(world, 2UL), 2));
            InputAdmissionResult expired = ingress.Submit(CommandOf(StampOf(world, 1UL), 1));

            Assert.That(expired.Outcome, Is.EqualTo(InputAdmissionOutcome.RejectedResultExpired), expired.ToString());
            Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(port.SubmittedCount, Is.EqualTo(2));
        }

        /// <summary>A sample stamped by another world incarnation is refused as stale (P-004).</summary>
        [Test]
        public void ASampleStampedByAnotherWorldIsRefused()
        {
            WorldId world = NextWorld();
            WorldId foreign = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 4U);

            InputAdmissionResult refused = ingress.Submit(CommandOf(StampOf(foreign, 1UL), 1));

            Assert.That(refused.Outcome, Is.EqualTo(InputAdmissionOutcome.RejectedForeignWorld), refused.ToString());
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(port.SubmittedCount, Is.Zero);
        }

        /// <summary>A default stamp is never a live sample, so a degenerate command is refused (P-005).</summary>
        [Test]
        public void ADegenerateStampIsRefusedRatherThanReadAsTheFirstEventOfASource()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 4U);

            var degenerate = new SampledInputCommand(
                new InputSourceStamp(world, Source, 0UL, LogicalStepId.Zero, AssemblyEpoch.First),
                Route,
                Target,
                CommandSchema,
                null,
                new FrozenPayload(CommandPayloadCodec.Int32(1)));
            InputAdmissionResult refused = ingress.Submit(degenerate);

            Assert.That(refused.Outcome, Is.EqualTo(InputAdmissionOutcome.RejectedMalformedSample), refused.ToString());
            Assert.That(port.SubmittedCount, Is.Zero);
        }

        /// <summary>
        /// A delayed completion is validated at completion against the real callback gate: after the activation is
        /// retired it is discarded and never reaches the command port (P-007, P-047).
        /// </summary>
        [Test]
        public void ADelayedInputCompletionIsDiscardedAfterTheActivationIsRetired()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 4U);
            var gate = new CallbackGate(world);
            var pending = new PendingInputCompletionTable(gate, ingress, 4U);

            var instance = new PluginInstanceId(new Id128(0x4743303139504C55UL, 1UL));
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);

            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));
            Assert.That(pending.TryRegister(token, out DiagnosticCode registerCode, out string registerDetail), Is.True,
                registerCode + ": " + registerDetail);

            gate.RetireActivation(instance);
            InputCompletionResult result = pending.Complete(token, CommandOf(StampOf(world, 1UL), 9));

            Assert.That(result.Outcome, Is.EqualTo(InputCompletionOutcome.Discarded), result.ToString());
            Assert.That(result.Gate, Is.EqualTo(CallbackGateDecision.DiscardRetiredRoute));
            Assert.That(port.SubmittedCount, Is.Zero, "a stale completion cannot write (P-007).");
            Assert.That(pending.ReleasedStagedCount, Is.EqualTo(1), "its own acquisition was released (P-007).");
        }

        /// <summary>The same completion with a live activation reaches the port, and a repeat is not a second dispatch.</summary>
        [Test]
        public void ADelayedInputCompletionWithALiveActivationReachesTheCommandPort()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 4U);
            var gate = new CallbackGate(world);
            var pending = new PendingInputCompletionTable(gate, ingress, 4U);

            var instance = new PluginInstanceId(new Id128(0x4743303139504C55UL, 2UL));
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 2UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(2UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(2UL));
            pending.TryRegister(token, out _, out _);

            InputCompletionResult result = pending.Complete(token, CommandOf(StampOf(world, 1UL), 5));
            InputCompletionResult repeat = pending.Complete(token, CommandOf(StampOf(world, 1UL), 5));

            Assert.That(result.Outcome, Is.EqualTo(InputCompletionOutcome.Dispatched), result.ToString());
            Assert.That(result.Admission, Is.Not.Null);
            Assert.That(result.Admission!.Accepted, Is.True);
            Assert.That(port.SubmittedCount, Is.EqualTo(1));
            Assert.That(repeat.Outcome, Is.EqualTo(InputCompletionOutcome.UnknownRequest), repeat.ToString());
        }

        /// <summary>A full pending table refuses with `BudgetExceeded` instead of dropping an in-flight request (P-043).</summary>
        [Test]
        public void AFullPendingTableRefusesInsteadOfDroppingInFlightWork()
        {
            WorldId world = NextWorld();
            var port = new RecordingCommandPort(world);
            var ingress = new TypedInputIngress(world, port, 4U);
            var gate = new CallbackGate(world);
            var pending = new PendingInputCompletionTable(gate, ingress, 1U);

            var instance = new PluginInstanceId(new Id128(0x4743303139504C55UL, 3UL));
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));
            var first = new AsyncWorkToken(new OperationId(world, Issuer, 1UL), instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL), 1U);
            var second = new AsyncWorkToken(new OperationId(world, Issuer, 1UL), instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL), 2U);

            Assert.That(pending.TryRegister(first, out _, out _), Is.True);
            Assert.That(pending.TryRegister(second, out DiagnosticCode code, out string detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(detail, Is.Not.Empty);
            Assert.That(pending.BackpressureCount, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ asset leases

        /// <summary>
        /// An asset lease is bounded, the completion is validated against the gate, and a completion that arrives
        /// after the table retired installs nothing while still releasing its own acquisition (P-007, P-048).
        /// </summary>
        [Test]
        public void ALateAssetCompletionAfterRetirementInstallsNothing()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var table = new AssetLeaseTable(
                world, backend, gate, ledger, PhysicsOwner, new PluginInstanceId(new Id128(0x4743303139415353UL, 1UL)), 4U, 4096UL);

            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 1UL));
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));

            Assert.That(table.TryRequest(Resource(), token, EmptyConfig(), 64UL, out Id128 leaseId, out DiagnosticCode code, out string detail),
                Is.True, code + ": " + detail);
            long handle = table.Leases()[0].Handle;
            Assert.That(backend.Complete(handle), Is.True);

            AssetReleaseReport retired = table.Retire();
            Assert.That(retired.AllReleased, Is.True, retired.ToString());

            AssetCompletionResult completion = table.Complete(leaseId, new FrozenPayload(new byte[] { 1, 2, 3, 4 }));
            Assert.That(completion.Outcome, Is.EqualTo(AssetCompletionOutcome.AlreadyTerminal), completion.ToString());
            Assert.That(table.PostRetireCompletionCount, Is.EqualTo(1), "the table was retired first.");
            Assert.That(table.TryReadPayload(leaseId, out FrozenPayload? payload), Is.False);
            Assert.That(payload, Is.Null, "a late completion cannot install data after teardown (P-007).");
            Assert.That(backend.OutstandingLoadCount, Is.Zero, "the backend reference was dropped exactly once.");
        }

        /// <summary>
        /// A completion whose activation was replaced is discarded while the load is still in flight, and the lease is
        /// released rather than retained as if it had succeeded (P-047).
        /// </summary>
        [Test]
        public void ACompletionFromAStaleActivationIsDiscardedAndReleased()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 2UL));
            var table = new AssetLeaseTable(world, backend, gate, ledger, PhysicsOwner, instance, 4U, 4096UL);

            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 2UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));
            table.TryRequest(Resource(), token, EmptyConfig(), 64UL, out Id128 leaseId, out _, out _);

            // An in-place reconfigure advances the activation epoch while the load is in flight (P-046).
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(2UL));
            AssetCompletionResult completion = table.Complete(leaseId, new FrozenPayload(new byte[] { 9 }));

            Assert.That(completion.Outcome, Is.EqualTo(AssetCompletionOutcome.DiscardedStale), completion.ToString());
            Assert.That(completion.Gate, Is.EqualTo(CallbackGateDecision.DiscardStaleActivation));
            Assert.That(table.StaleDiscardCount, Is.EqualTo(1));
            Assert.That(table.TryReadPayload(leaseId, out _), Is.False);
            Assert.That(backend.OutstandingLoadCount, Is.Zero);
        }

        /// <summary>The table is bounded by count and by bytes; both refusals are values, not waits (P-022, P-043).</summary>
        [Test]
        public void TheAssetTableIsBoundedByCountAndByBytes()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 3UL));
            var table = new AssetLeaseTable(world, backend, gate, ledger, PhysicsOwner, instance, 1U, 100UL);
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));

            Assert.That(table.TryRequest(Resource(), token, EmptyConfig(), 40UL, out _, out DiagnosticCode firstCode, out _),
                Is.True, firstCode.ToString());
            Assert.That(table.TryRequest(Resource(), token, EmptyConfig(), 40UL, out _, out DiagnosticCode secondCode, out string secondDetail),
                Is.False);
            Assert.That(secondCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(secondDetail, Does.Contain("capacity"));
            Assert.That(table.BackpressureCount, Is.EqualTo(1));

            // A byte budget is a second bound: the table is not full here, but the reservation is not admissible.
            var wide = new AssetLeaseTable(world, new DeterministicAssetBackend(), gate, new WorldResourceLedger(world),
                PhysicsOwner, instance, 8U, 100UL);
            Assert.That(wide.TryRequest(Resource(), token, EmptyConfig(), 60UL, out _, out _, out _), Is.True);
            Assert.That(wide.TryRequest(Resource(), token, EmptyConfig(), 60UL, out _, out DiagnosticCode byteCode, out string byteDetail),
                Is.False);
            Assert.That(byteCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(byteDetail, Does.Contain("byte budget"));
        }

        /// <summary>A lease with a live consumer is retained, and a throwing engine release is quarantined (P-048).</summary>
        [Test]
        public void ALeaseWithAConsumerIsRetainedAndAFailingReleaseIsQuarantined()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 4UL));
            var table = new AssetLeaseTable(world, backend, gate, ledger, PhysicsOwner, instance, 4U, 4096UL);
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));

            table.TryRequest(Resource(), token, EmptyConfig(), 16UL, out Id128 leaseId, out _, out _);
            backend.Complete(table.Leases()[0].Handle);
            table.PumpCompletions();
            Assert.That(table.AcquireConsumer(leaseId, out _), Is.True);

            Assert.That(table.Release(leaseId), Is.EqualTo(AssetReleaseOutcome.RetainedByConsumers));
            Assert.That(table.QuarantinedCount, Is.EqualTo(1));
            Assert.That(table.ReleaseQuarantine(leaseId), Is.False, "a consumer still owns it.");

            Assert.That(table.ReleaseConsumer(leaseId, out _), Is.True);
            Assert.That(table.ReleaseQuarantine(leaseId), Is.True);
            Assert.That(table.Release(leaseId), Is.EqualTo(AssetReleaseOutcome.Released));
            Assert.That(table.Release(leaseId), Is.EqualTo(AssetReleaseOutcome.AlreadyReleased),
                "dispose each lease at most once (P-048).");
            Assert.That(backend.ReleaseCount, Is.EqualTo(1));

            var failing = new DeterministicAssetBackend();
            var failingTable = new AssetLeaseTable(world, failing, gate, new WorldResourceLedger(world),
                PhysicsOwner, instance, 4U, 4096UL);
            failingTable.TryRequest(Resource(), token, EmptyConfig(), 16UL, out Id128 failingLease, out _, out _);
            failing.FailNextRelease = true;
            Assert.That(failingTable.Release(failingLease), Is.EqualTo(AssetReleaseOutcome.ReleaseFailed));
            Assert.That(failing.ReleaseFaultCount, Is.EqualTo(1));
            Assert.That(failingTable.QuarantinedCount, Is.EqualTo(1),
                "a throwing release is quarantined, never reported as a success (P-048).");
            Assert.That(failingTable.Retire().AllReleased, Is.False,
                "a quarantined lease keeps teardown from claiming a clean release.");
        }

        /// <summary>A failed load exposes nothing and releases immediately (P-049 shares the shape with P-029).</summary>
        [Test]
        public void AFailedLoadExposesNothingAndIsReleased()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 5UL));
            var table = new AssetLeaseTable(world, backend, gate, ledger, PhysicsOwner, instance, 4U, 4096UL);
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));

            table.TryRequest(Resource(), token, EmptyConfig(), 16UL, out Id128 leaseId, out _, out _);
            long handle = table.Leases()[0].Handle;
            Assert.That(backend.Fail(handle), Is.True);
            Assert.That(table.PumpCompletions(), Is.Zero);

            Assert.That(table.FailureCount, Is.EqualTo(1));
            Assert.That(table.TryReadPayload(leaseId, out FrozenPayload? payload), Is.False);
            Assert.That(payload, Is.Null);
            Assert.That(backend.OutstandingLoadCount, Is.Zero, "a failed acquisition is released at once (P-029).");

            AssetCompletionResult reported = table.FailCompletion(leaseId, DiagnosticCode.ResourceUnavailable, "probe");
            Assert.That(reported.Outcome, Is.EqualTo(AssetCompletionOutcome.AlreadyTerminal), reported.ToString());
            Assert.That(table.FailureCount, Is.EqualTo(1), "a repeated failure report is not a second failure.");
        }

        /// <summary>
        /// The table's own report of a failed load is a value the caller can act on, and a completion that arrives
        /// after the table retired is counted as exactly that (P-007, P-049).
        /// </summary>
        [Test]
        public void AFailedCompletionIsReportedAndAPostRetireCompletionIsCounted()
        {
            WorldId world = NextWorld();
            var backend = new DeterministicAssetBackend();
            var gate = new CallbackGate(world);
            var ledger = new WorldResourceLedger(world);
            var instance = new PluginInstanceId(new Id128(0x4743303139415353UL, 6UL));
            var table = new AssetLeaseTable(world, backend, gate, ledger, PhysicsOwner, instance, 4U, 4096UL);
            var token = new AsyncWorkToken(
                new OperationId(world, Issuer, 1UL),
                instance,
                new InstallationGeneration(1UL),
                new ActivationEpoch(1UL),
                1U);
            gate.RegisterActivation(instance, new InstallationGeneration(1UL), new ActivationEpoch(1UL));

            table.TryRequest(Resource(), token, EmptyConfig(), 16UL, out Id128 failedLease, out _, out _);
            AssetCompletionResult failure = table.FailCompletion(
                failedLease, DiagnosticCode.ResourceUnavailable, "the backend reported a missing asset");
            Assert.That(failure.Outcome, Is.EqualTo(AssetCompletionOutcome.Failed), failure.ToString());
            Assert.That(failure.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(failure.Detail, Is.Not.Empty);
            Assert.That(backend.OutstandingLoadCount, Is.Zero);

            table.TryRequest(Resource(), token, EmptyConfig(), 16UL, out Id128 pendingLease, out _, out _);
            AssetReleaseReport retired = table.Retire();
            Assert.That(retired.AllReleased, Is.True, retired.ToString());

            AssetCompletionResult late = table.Complete(pendingLease, new FrozenPayload(new byte[] { 7 }));
            Assert.That(late.Outcome, Is.EqualTo(AssetCompletionOutcome.AlreadyTerminal), late.ToString());
            Assert.That(table.PostRetireCompletionCount, Is.EqualTo(1));
            Assert.That(table.TryReadPayload(pendingLease, out _), Is.False,
                "a completion after retirement cannot install data (P-007).");
        }

        // ------------------------------------------------------------------ views and presentation

        /// <summary>
        /// Destroying a view changes no gameplay: the registry has no write path, so the only observable effects are
        /// on the binder, and the registry's own counters report what happened (P-024, TEST-019).
        /// </summary>
        [Test]
        public void DestroyingAViewDestroysOnlyTheView()
        {
            WorldId world = NextWorld();
            var binder = new RecordingViewBinder();
            var registry = new ViewRegistry(world, 8U);
            var table = new TargetScopeTable().With(Target, RootScope(), Recipe());

            SnapshotToken token = Token(world, 1UL, 1UL);
            Assert.That(registry.Create(Key(Target), token, targetIsInCommittedAssembly: true, binder, out ViewRecord? record),
                Is.EqualTo(ViewCreateOutcome.Created));
            Assert.That(record, Is.Not.Null);

            Assert.That(registry.Destroy(Key(Target), targetIsLiveInComposition: true, binder),
                Is.EqualTo(ViewDestroyOutcome.Destroyed));
            Assert.That(registry.Destroy(Key(Target), targetIsLiveInComposition: false, binder),
                Is.EqualTo(ViewDestroyOutcome.AlreadyAbsent), "a repeated destroy is not a second destroy (P-050).");
            Assert.That(registry.LiveViewCount, Is.Zero);
            Assert.That(binder.LiveViewCount, Is.Zero);
            Assert.That(registry.DestroyCount, Is.EqualTo(1));
            Assert.That(table.Count, Is.EqualTo(1), "the committed membership is untouched by view destruction.");
        }

        /// <summary>A target the committed assembly does not carry cannot get a view (P-024).</summary>
        [Test]
        public void AViewForAnUncommittedTargetIsRefused()
        {
            WorldId world = NextWorld();
            var binder = new RecordingViewBinder();
            var registry = new ViewRegistry(world, 4U);

            Assert.That(registry.Create(Key(Target), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: false, binder, out _),
                Is.EqualTo(ViewCreateOutcome.Refused));
            Assert.That(registry.UnassembledTargetRefusalCount, Is.EqualTo(1));
            Assert.That(binder.CreatedCount, Is.Zero);

            Assert.That(registry.Create(Key(Target), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: true, binder, out _),
                Is.EqualTo(ViewCreateOutcome.Created));
            Assert.That(registry.Create(Key(Target), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: true, binder, out _),
                Is.EqualTo(ViewCreateOutcome.AlreadyPresent));
            Assert.That(registry.DuplicateCreateRefusalCount, Is.EqualTo(1));
        }

        /// <summary>A stale apply cannot overwrite a newer image; the same token is refused too (P-045).</summary>
        [Test]
        public void AStalePresentationApplyIsRefused()
        {
            WorldId world = NextWorld();
            var binder = new RecordingViewBinder();
            var registry = new ViewRegistry(world, 4U);
            registry.Create(Key(Target), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: true, binder, out _);

            Assert.That(registry.TryApply(Key(Target), ApplyData(Target, Token(world, 1UL, 2UL)), binder), Is.True);
            Assert.That(registry.TryApply(Key(Target), ApplyData(Target, Token(world, 1UL, 2UL)), binder), Is.False,
                "the same token is not newer than itself.");
            Assert.That(registry.TryApply(Key(Target), ApplyData(Target, Token(world, 1UL, 1UL)), binder), Is.False);
            Assert.That(registry.TryApply(Key(Target), ApplyData(Target, Token(NextWorld(), 9UL, 9UL)), binder), Is.False,
                "another world incarnation is never newer for this registry (P-004).");
            Assert.That(registry.ApplyCount, Is.EqualTo(1));
            Assert.That(registry.StaleApplyRefusalCount, Is.EqualTo(3));
            Assert.That(binder.ApplyCount, Is.EqualTo(1));
        }

        /// <summary>
        /// A visual reparent moves the binder's parent only: the committed composition parent reported by the
        /// snapshot is unchanged, and the registry counts exactly that (P-010, TEST-019).
        /// </summary>
        [Test]
        public void AVisualReparentDoesNotMoveComposition()
        {
            WorldId world = NextWorld();
            var binder = new RecordingViewBinder();
            var registry = new ViewRegistry(world, 8U);
            registry.Create(Key(Target), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: true, binder, out ViewRecord? child);
            registry.Create(Key(OtherTarget), Token(world, 1UL, 1UL), targetIsInCommittedAssembly: true, binder, out ViewRecord? parent);

            ScopeId committedParent = RootScope();
            Assert.That(registry.TrySetVisualParent(Key(Target), Key(OtherTarget), committedParent, binder), Is.True);

            Assert.That(binder.ParentChanges.Count, Is.EqualTo(1));
            Assert.That(child!.VisualParentChanged, Is.True);
            Assert.That(child.VisualParent, Is.EqualTo(parent!.Handle));
            Assert.That(child.CompositionParent, Is.EqualTo(committedParent),
                "the committed composition parent is what the snapshot said, not what the Transform says (P-010).");
            Assert.That(registry.VisualReparentsWithoutCompositionChange, Is.EqualTo(1));
        }

        /// <summary>
        /// The presenter reads committed output only: it presents a target's row values, destroys the views of a
        /// target that left the assembly, and cannot advance a step because the source it reads never moves (P-024,
        /// P-036, P-045).
        /// </summary>
        [Test]
        public void ThePresenterReadsCommittedOutputAndRemovesOrphanedViews()
        {
            WorldId world = NextWorld();
            var binder = new RecordingViewBinder();
            var registry = new ViewRegistry(world, 8U);
            var source = new CommittedImageSource(world);
            var presenter = new CommittedOutputPresenter(registry, source, binder);

            Assert.That(presenter.Present().Outcome, Is.EqualTo(PresentationOutcome.NoCommittedImage),
                "a world that has published nothing presents nothing (P-030).");

            Assert.That(source.Refresh(Image(world, 1UL, Target, value: 11)), Is.True);
            Assert.That(presenter.CreateView(Target, 0U, out ViewRecord? record), Is.EqualTo(ViewCreateOutcome.Created));
            Assert.That(record, Is.Not.Null);

            PresentationReport first = presenter.Present();
            Assert.That(first.Outcome, Is.EqualTo(PresentationOutcome.Presented), first.ToString());
            Assert.That(first.Applied, Is.EqualTo(1));
            Assert.That(binder.Applies.Count, Is.EqualTo(1));
            Assert.That(ValueOf(binder.Applies[0], Capability()), Is.EqualTo(11));

            // Same image, second pass: nothing is re-applied, because the token is not newer (P-045).
            PresentationReport again = presenter.Present();
            Assert.That(again.Applied, Is.Zero);
            Assert.That(again.StaleRefused, Is.EqualTo(1));
            Assert.That(source.RefreshCount, Is.EqualTo(1), "the presenter never produces an image itself (P-036).");

            // A newer image is applied, then the target leaves the committed assembly and its view is destroyed.
            source.Refresh(Image(world, 2UL, Target, value: 12));
            Assert.That(presenter.Present().Applied, Is.EqualTo(1));
            Assert.That(ValueOf(binder.Applies[1], Capability()), Is.EqualTo(12));

            source.Refresh(Image(world, 3UL, target: null, value: 0));
            PresentationReport orphaned = presenter.Present();
            Assert.That(orphaned.OrphanedDestroyed, Is.EqualTo(1), orphaned.ToString());
            Assert.That(registry.LiveViewCount, Is.Zero);
            Assert.That(binder.LiveViewCount, Is.Zero);
        }

        /// <summary>A presenter bound to another world's source is refused at construction (P-004).</summary>
        [Test]
        public void APresenterCannotBeBoundToAnotherWorldsSource()
        {
            WorldId world = NextWorld();
            var registry = new ViewRegistry(world, 4U);
            var foreign = new CommittedImageSource(NextWorld());

            Assert.Throws<ArgumentException>(() => new CommittedOutputPresenter(registry, foreign, null));
        }

        /// <summary>
        /// A headless world presents nothing and keeps running: the presenter reports `NoViews`, and the frame that
        /// owns no binder reports the headless answer instead of failing (04 s7, TEST-018).
        /// </summary>
        [Test]
        public void AHeadlessCompositionPresentsNothingAndKeepsRunning()
        {
            WorldId world = NextWorld();
            var headless = new HeadlessViewBinder();
            var registry = new ViewRegistry(world, 4U);
            var source = new CommittedImageSource(world);
            source.Refresh(Image(world, 1UL, Target, value: 3));
            var presenter = new CommittedOutputPresenter(registry, source, headless);

            PresentationReport report = presenter.Present();

            Assert.That(report.Outcome, Is.EqualTo(PresentationOutcome.NoViews), report.ToString());
            Assert.That(report.Applied, Is.Zero);
            Assert.That(headless.Refusals, Is.Zero, "no view was ever requested, so nothing was refused either.");
            Assert.That(registry.LiveViewCount, Is.Zero);
        }

        // ------------------------------------------------------------------ external authority

        /// <summary>
        /// A card/narrative world declares no external domain, so no physics adapter is needed and an intent is
        /// refused as ECS-owned rather than forwarded to an absent adapter (P-034, P-059).
        /// </summary>
        [Test]
        public void AWorldWithNoExternalDomainNeedsNoPhysicsAdapter()
        {
            WorldId world = NextWorld();
            var ledger = new ExternalAuthorityLedger(world);

            Assert.That(ledger.ExternalDomainCount, Is.Zero);
            Assert.That(ledger.AuthorityOf(Recipe()), Is.EqualTo(MotionAuthority.EcsOwnedKinematic));
            Assert.That(ExternalAuthority.Classify(ledger, Recipe(), null),
                Is.EqualTo(AuthorityIntentOutcome.RefusedEcsOwned));
            Assert.That(ledger.TryWriteFromGameplay(Target, Domain, out DiagnosticCode code, out _), Is.True,
                "with no external owner, ECS owns the quantity, so a gameplay write is the ordinary path.");
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
        }

        /// <summary>
        /// A declared external domain has exactly one owner, a foreign sample is refused, and a gameplay write to it
        /// is an `OwnershipConflict` — the two things 04 s7 forbids (P-034, TEST-019).
        /// </summary>
        [Test]
        public void AnExternallyOwnedDomainHasOneOwnerAndRefusesGameplayWrites()
        {
            WorldId world = NextWorld();
            var ledger = new ExternalAuthorityLedger(world);
            var descriptor = new ExternalAuthorityDescriptor(
                Recipe(),
                Domain,
                PhysicsOwner,
                new StageId(new Id128(0x4743303139535447UL, 1UL)),
                new SchemaRef(new SchemaId(new Id128(0x4743303139534348UL, 2UL)), 1U),
                MotionAuthority.ExternalEngineOwned);

            Assert.That(ledger.TryDeclare(descriptor, out _, out _), Is.True);
            Assert.That(ledger.AuthorityOf(Recipe()), Is.EqualTo(MotionAuthority.ExternalEngineOwned));
            Assert.That(ExternalAuthority.Classify(ledger, Recipe(), null),
                Is.EqualTo(AuthorityIntentOutcome.RefusedUnavailable));

            // A second external owner for the same domain is an OwnershipConflict, never a second writable copy.
            var other = new ExternalAuthorityDescriptor(
                Recipe(),
                Domain,
                new OwnerId(new Id128(0x4743303139504859UL, 4UL)),
                descriptor.SynchronizationStage,
                descriptor.ObservationSchema,
                MotionAuthority.ExternalEngineOwned);
            Assert.That(ledger.TryDeclare(other, out DiagnosticCode conflict, out string conflictDetail), Is.False);
            Assert.That(conflict, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(conflictDetail, Does.Contain("already owned"));
            Assert.That(ledger.DeclarationConflictCount, Is.EqualTo(1));

            // Gameplay cannot write the externally owned quantity; it must submit an intent instead.
            Assert.That(ledger.TryWriteFromGameplay(Target, Domain, out DiagnosticCode writeCode, out string writeDetail),
                Is.False);
            Assert.That(writeCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(writeDetail, Does.Contain("intent"));
            Assert.That(ledger.RefusedGameplayWriteCount, Is.EqualTo(1));

            var adapter = new RecordingExternalAuthorityAdapter(Domain, PhysicsOwner);
            Assert.That(ExternalAuthority.Classify(ledger, Recipe(), adapter),
                Is.EqualTo(AuthorityIntentOutcome.Accepted));
            AuthorityIntentOutcome submitted = adapter.SubmitIntent(new AuthorityIntent(
                new OperationId(world, Issuer, 1UL), Target, Domain, AuthorityIntentKind.Impulse, EmptyConfig()));
            Assert.That(submitted, Is.EqualTo(AuthorityIntentOutcome.Accepted));
            Assert.That(adapter.IntentCount, Is.EqualTo(1));
        }

        /// <summary>A sample from an authority that does not own the domain is refused, and stale samples never win.</summary>
        [Test]
        public void ASampleFromTheWrongAuthorityOrAnOlderStepIsRefused()
        {
            WorldId world = NextWorld();
            var ledger = new ExternalAuthorityLedger(world);
            var descriptor = new ExternalAuthorityDescriptor(
                Recipe(),
                Domain,
                PhysicsOwner,
                new StageId(new Id128(0x4743303139535447UL, 2UL)),
                new SchemaRef(new SchemaId(new Id128(0x4743303139534348UL, 2UL)), 1U),
                MotionAuthority.ExternalEngineOwned);
            ledger.TryDeclare(descriptor, out _, out _);

            var adapter = new RecordingExternalAuthorityAdapter(Domain, PhysicsOwner);
            Assert.That(adapter.TrySample(Target, Token(world, 1UL, 5UL), out EngineObservation good, out _), Is.True);
            Assert.That(ledger.Record(good, out _, out _), Is.EqualTo(ObservationOutcome.Recorded));
            Assert.That(ledger.TryReadObservation(Target, Domain, out EngineObservation recorded), Is.True);
            Assert.That(recorded.SampledStep, Is.EqualTo(new LogicalStepId(5UL)),
                "an observation is stamped with the sampling step (TEST-019).");

            // The same step again is not newer: an older image never overwrites a newer one (P-045).
            Assert.That(ledger.Record(good, out DiagnosticCode staleCode, out _), Is.EqualTo(ObservationOutcome.RefusedStaleSample));
            Assert.That(staleCode, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(ledger.RefusedStaleSampleCount, Is.EqualTo(1));

            // A sample claiming another authority is refused rather than recorded.
            adapter.ReportedAuthority = new OwnerId(new Id128(0x4743303139504859UL, 9UL));
            adapter.TrySample(Target, Token(world, 1UL, 6UL), out EngineObservation foreign, out _);
            Assert.That(ledger.Record(foreign, out DiagnosticCode foreignCode, out string foreignDetail),
                Is.EqualTo(ObservationOutcome.RefusedForeignAuthority));
            Assert.That(foreignCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(foreignDetail, Does.Contain("owned by"));

            // An undeclared domain has no owner, so no sample may claim it.
            adapter.ReportedAuthority = default(OwnerId);
            var otherDomain = new RecordingExternalAuthorityAdapter(new Id128(0x4743303139444F4DUL, 8UL), PhysicsOwner);
            otherDomain.TrySample(Target, Token(world, 1UL, 7UL), out EngineObservation undeclared, out _);
            Assert.That(ledger.Record(undeclared, out DiagnosticCode undeclaredCode, out _),
                Is.EqualTo(ObservationOutcome.RefusedUndeclaredDomain));
            Assert.That(undeclaredCode, Is.EqualTo(DiagnosticCode.MissingDependency));
        }

        /// <summary>The adapter frame registry refuses a foreign world's frame and resets per session (04 s9).</summary>
        [Test]
        public void TheAdapterFrameRegistryReportsSkippedForAWorldWithNoFrame()
        {
            AdapterFrameRegistry.Reset();
            try
            {
                WorldId world = NextWorld();
                AdapterFrameReport report = AdapterFrameRegistry.CollectInput(world);
                Assert.That(report.Outcome, Is.EqualTo(AdapterFrameOutcome.Skipped));
                Assert.That(AdapterFrameRegistry.SkippedCalls, Is.EqualTo(1));

                var frame = new RecordingAdapterFrame(world);
                Assert.That(AdapterFrameRegistry.Register(frame), Is.False, "the first registration replaces nothing.");
                Assert.That(AdapterFrameRegistry.Count, Is.EqualTo(1));
                Assert.That(AdapterFrameRegistry.CollectInput(world).Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
                Assert.That(AdapterFrameRegistry.Present(world).Outcome, Is.EqualTo(AdapterFrameOutcome.Completed));
                Assert.That(frame.InputCalls, Is.EqualTo(1));
                Assert.That(frame.PresentationCalls, Is.EqualTo(1));

                Assert.That(AdapterFrameRegistry.Register(frame), Is.True, "a second registration for one world is explicit.");
                Assert.That(AdapterFrameRegistry.Unregister(world), Is.True);
                Assert.That(AdapterFrameRegistry.Count, Is.Zero);
            }
            finally
            {
                AdapterFrameRegistry.Reset();
            }
        }

        /// <summary>A frame that throws is counted and the pump keeps running: a defect cannot abort a host frame.</summary>
        [Test]
        public void AThrowingAdapterFrameIsCountedAndDoesNotPropagate()
        {
            AdapterFrameRegistry.Reset();
            try
            {
                WorldId world = NextWorld();
                AdapterFrameRegistry.Register(new RecordingAdapterFrame(world) { ThrowOnPresent = true });
                AdapterFrameReport report = AdapterFrameRegistry.Present(world);

                Assert.That(report.Outcome, Is.EqualTo(AdapterFrameOutcome.Faulted), report.ToString());
                Assert.That(AdapterFrameRegistry.FaultedCalls, Is.EqualTo(1));
            }
            finally
            {
                AdapterFrameRegistry.Reset();
            }
        }

        /// <summary>
        /// The input binding table resolves by (kind, code) in canonical numeric order, and the adapter stamps the
        /// sample with the world, its source and a strictly increasing sequence (P-008, P-042).
        /// </summary>
        [Test]
        public void TheBindingTableResolvesSamplesInCanonicalOrder()
        {
            var table = new InputBindingTable()
                .Add(new InputCommandBinding(InputDeviceKind.Button, 32, Route, Target, CommandSchema, 7UL))
                .Add(new InputCommandBinding(InputDeviceKind.Button, 9, Route, OtherTarget, CommandSchema, null))
                .Add(new InputCommandBinding(InputDeviceKind.Axis, 1, Route, Target, CommandSchema, null));

            IReadOnlyList<InputCommandBinding> ordered = table.Ordered();
            Assert.That(ordered[0].Kind, Is.EqualTo(InputDeviceKind.Button));
            Assert.That(ordered[0].Code, Is.EqualTo(9));
            Assert.That(ordered[2].Kind, Is.EqualTo(InputDeviceKind.Axis));

            Assert.That(table.TryFind(InputDeviceKind.Button, 32, out InputCommandBinding? found), Is.True);
            Assert.That(found!.Target, Is.EqualTo(Target));
            Assert.That(found.ExpectedDomainVersion, Is.EqualTo(7UL));

            InputSourceStamp stamp = StampOf(NextWorld(), 3UL);
            SampledInputCommand command = found.Bind(stamp, new DeviceInputSample(InputDeviceKind.Button, 32, 5));
            Assert.That(CommandPayloadCodec.TryReadInt32(command.Payload.Bytes, out int value), Is.True);
            Assert.That(value, Is.EqualTo(5));
            Assert.That(command.Stamp.Operation.IssuerSequence, Is.EqualTo(3UL));
            Assert.That(command.InputHash(), Is.EqualTo(found.Bind(stamp, new DeviceInputSample(InputDeviceKind.Button, 32, 5)).InputHash()));
        }

        // ------------------------------------------------------------------ helpers

        private static ResourceKey Resource() => new ResourceKey(new Id128(0x4743303139524553UL, 1UL));

        private static FrozenPayload EmptyConfig() => new FrozenPayload(Array.Empty<byte>());

        private static CapabilityId Capability() => new CapabilityId(new Id128(0x4743303139434150UL, 1UL));

        private static ScopeId RootScope() => new ScopeId(new Id128(0x4743303139534350UL, 1UL));

        private static DefinitionRef Recipe() =>
            new DefinitionRef(
                new DefinitionId(new Id128(0x4743303139524350UL, 1UL)),
                new SchemaRef(new SchemaId(new Id128(0x4743303139534348UL, 3UL)), 1U),
                new DefinitionRevision(1UL));

        private static ViewKey Key(TargetId target) => new ViewKey(target, 0U);

        private static SnapshotToken Token(WorldId world, ulong epoch, ulong step) =>
            new SnapshotToken(world, new AssemblyEpoch(epoch), new LogicalStepId(step));

        private static PresentationApplyData ApplyData(TargetId target, SnapshotToken token) =>
            new PresentationApplyData(
                Key(target),
                token,
                RootScope(),
                new[] { new PresentationField(Capability(), 0U, 5) });

        private static CommittedAssemblyImage Image(WorldId world, ulong step, TargetId? target, int value)
        {
            var entries = new List<CommittedTargetEntry>();
            if (target.HasValue)
            {
                entries.Add(new CommittedTargetEntry(
                    target.Value,
                    RootScope(),
                    Recipe(),
                    new[] { new PresentationField(Capability(), 0U, value) }));
            }

            return new CommittedAssemblyImage(new SnapshotToken(world, AssemblyEpoch.First, new LogicalStepId(step)), entries);
        }

        private static int ValueOf(PresentationApplyData data, CapabilityId capability)
        {
            for (int i = 0; i < data.Fields.Count; i++)
            {
                if (data.Fields[i].Capability.Equals(capability))
                {
                    return data.Fields[i].Value;
                }
            }

            return int.MinValue;
        }

        /// <summary>
        /// A recording `ICommandIngress`: the port the adapter submits into. It admits everything and records how
        /// many distinct envelopes it saw, so "the retransmission did not execute twice" is a count (P-050).
        /// </summary>
        private sealed class RecordingCommandPort : ICommandIngress
        {
            private readonly WorldId world;

            public RecordingCommandPort(WorldId world)
            {
                this.world = world;
            }

            public int SubmittedCount { get; private set; }

            public CommandAdmissionReceipt Submit(CommandEnvelope command)
            {
                SubmittedCount++;
                return new CommandAdmissionReceipt(
                    command.RequestId,
                    new RequestResult(RequestResultKind.Accepted, DiagnosticCode.None, default(EventCursor)),
                    new AdmissionSequence((ulong)SubmittedCount));
            }
        }

        /// <summary>A recording adapter frame, including a switch that makes `Present` throw (P-031).</summary>
        private sealed class RecordingAdapterFrame : IAdapterFrame
        {
            public RecordingAdapterFrame(WorldId world)
            {
                World = world;
            }

            public WorldId World { get; }

            public bool ThrowOnPresent { get; set; }

            public int InputCalls { get; private set; }

            public int PresentationCalls { get; private set; }

            public AdapterFrameReport CollectInput()
            {
                InputCalls++;
                return AdapterFrameReport.Completed("recording", 1, 0, "recorded");
            }

            public AdapterFrameReport Present()
            {
                PresentationCalls++;
                if (ThrowOnPresent)
                {
                    throw new InvalidOperationException("configured to throw");
                }

                return AdapterFrameReport.Completed("recording", 0, 0, "recorded");
            }
        }
    }
}
