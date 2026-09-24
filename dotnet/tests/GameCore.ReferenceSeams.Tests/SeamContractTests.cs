// Seam contract tests (GC-002). Covers the correctness fixes and the W1-facing seams the review required:
// strict canonical hex, ledger idempotency and expiry, callback-gate world validation, handle generation
// reservation, canonical map ordering, catalog lookup misses, the composition host double, guarded dispatch
// fault propagation and the world/job resource ledger.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.TestFixtures;
using GameCore.ProtocolFixtures;
using GameCore.ProtocolFixtures.Oracle;
using NUnit.Framework;

namespace GameCore.ReferenceSeams.Tests
{
    [TestFixture]
    public sealed class SeamContractTests
    {
        private static readonly WorldId WorldA = new WorldId(ParseId("11111111111111111111111111111111"));

        private static readonly WorldId WorldB = new WorldId(ParseId("22222222222222222222222222222222"));

        private static Id128 ParseId(string text)
        {
            Assert.That(Id128Codec.TryParseHex(text, out Id128 parsed), Is.True, "Fixture id must be canonical hex: " + text);
            return parsed;
        }

        [Test]
        public void Id128ParsingAcceptsOnlyTheCanonicalLowercaseForm()
        {
            const string Canonical = "0102030405060708090a0b0c0d0e0f10";
            Assert.That(Id128Codec.TryParseHex(Canonical, out Id128 parsed), Is.True);
            Assert.That(Id128Codec.ToHex(parsed), Is.EqualTo(Canonical));

            foreach (string rejected in new[]
            {
                Canonical.ToUpperInvariant(),
                " " + Canonical,
                Canonical + " ",
                Canonical.Substring(0, 31),
                Canonical + "0",
                "0x" + Canonical.Substring(2),
                string.Empty,
            })
            {
                Assert.That(Id128Codec.TryParseHex(rejected, out Id128 _), Is.False, "Input should have been rejected: '" + rejected + "'");
            }

            Assert.That(Id128Codec.TryParseHex(null, out Id128 _), Is.False);
        }

        [Test]
        public void ContentHashParsingAcceptsOnlyTheCanonicalLowercaseForm()
        {
            byte[] bytes = new byte[ContentHash.SizeInBytes];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)i;
            }

            ContentHash hash = new ContentHash(bytes);
            string canonical = hash.ToHex();
            Assert.That(canonical.Length, Is.EqualTo(64));
            Assert.That(ContentHash.TryParseHex(canonical, out ContentHash parsed), Is.True);
            Assert.That(parsed, Is.EqualTo(hash));

            Assert.That(ContentHash.TryParseHex(canonical.ToUpperInvariant(), out ContentHash _), Is.False);
            Assert.That(ContentHash.TryParseHex(canonical.Insert(0, " "), out ContentHash _), Is.False);
            Assert.That(ContentHash.TryParseHex(canonical.Substring(1), out ContentHash _), Is.False);
            Assert.That(ContentHash.TryParseHex(null, out ContentHash _), Is.False);
        }

        [Test]
        public void HandleGenerationZeroIsNeverAllocatedAndSlotsAreUnsigned()
        {
            Assert.That(default(TargetHandle).IsAllocated, Is.False, "A default handle must never be live (P-005).");
            Assert.Throws<ArgumentOutOfRangeException>(() => new TargetHandle(WorldA, 0U, 0UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScopeHandle(WorldA, 0U, 0UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PluginHandle(WorldA, 0U, 0UL));

            TargetHandle live = new TargetHandle(WorldA, 7U, 1UL);
            Assert.That(live.IsAllocated, Is.True);
            Assert.That(live.Slot, Is.EqualTo(7U), "Slots are unsigned, so a negative slot cannot be expressed.");
        }

        [Test]
        public void VersionDomainsUseTypedCounters()
        {
            SnapshotToken token = new SnapshotToken(WorldA, new AssemblyEpoch(3UL), new LogicalStepId(9UL));
            Assert.That(token.AssemblyEpoch.Value, Is.EqualTo(3UL));
            Assert.That(token.LogicalStepId.Value, Is.EqualTo(9UL));
            PublicationAdvance committed = CounterOracle.AfterCommittedStep(new PublicationState(3UL, 3UL, 9UL));
            Assert.That(committed.StepUnchanged, Is.False);
            Assert.That(committed.After.LogicalStepId, Is.EqualTo(10UL));
        }

        [Test]
        public void HostLedgerKeepsTheOriginalResultOnConflictingReuse()
        {
            InMemoryHost host = new InMemoryHost(Outcome.Pending, 4);
            OperationId operation = new OperationId(WorldA, Id128.Zero, 1UL);
            SchemaRef schema = new SchemaRef(new SchemaId(new Id128(1UL, 1UL)), 1U);

            CommandEnvelope first = new CommandEnvelope(operation, new RouteId(new Id128(2UL, 2UL)), new TargetId(new Id128(3UL, 3UL)), schema, null, new FrozenPayload(new byte[] { 1 }));
            CommandAdmissionReceipt receipt = host.Submit(first);
            Assert.That(receipt.Admitted, Is.True);

            CommandEnvelope later = new CommandEnvelope(new OperationId(WorldA, Id128.Zero, 2UL), first.RouteId, first.TargetId, schema, null, first.Payload);
            CommandAdmissionReceipt laterReceipt = host.Submit(later);
            Assert.That(laterReceipt.Admitted, Is.True);
            Assert.That(laterReceipt.AcceptedSequence, Is.GreaterThan(receipt.AcceptedSequence));

            // Same operation id, same payload: a retransmission returns the original outcome (P-050).
            CommandAdmissionReceipt duplicate = host.Submit(first);
            Assert.That(duplicate.Admitted, Is.True);
            Assert.That(host.DuplicateCount, Is.EqualTo(1));
            Assert.That(duplicate.AcceptedSequence, Is.EqualTo(receipt.AcceptedSequence), "A retransmission keeps its original replay order after later admissions (P-050).");
            Assert.That(host.SubmissionCount, Is.EqualTo(2), "A retransmission must not enqueue a second command.");

            // Same operation id, different payload: reported as a conflict, original row preserved (P-050).
            CommandEnvelope conflicting = new CommandEnvelope(operation, first.RouteId, first.TargetId, schema, null, new FrozenPayload(new byte[] { 9 }));
            CommandAdmissionReceipt conflict = host.Submit(conflicting);
            Assert.That(conflict.Admitted, Is.False);
            Assert.That(conflict.Result.Reason, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(host.ConflictCount, Is.EqualTo(1));

            OperationReadResult read = host.Read(operation);
            Assert.That(read.Outcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(read.Entry, Is.Not.Null);
            Assert.That(read.Entry!.Code, Is.EqualTo(DiagnosticCode.None), "The stored row must not have been overwritten by the conflict.");
            Assert.That(read.Entry.InputHash, Is.Not.EqualTo(ContentHash.Empty));
        }

        [Test]
        public void HostLedgerDistinguishesUnknownFromExpired()
        {
            InMemoryHost host = new InMemoryHost(Outcome.Pending, 1);
            OperationId first = new OperationId(WorldA, Id128.Zero, 1UL);
            OperationId second = new OperationId(WorldA, Id128.Zero, 2UL);
            OperationId never = new OperationId(WorldA, Id128.Zero, 99UL);
            SchemaRef schema = new SchemaRef(new SchemaId(new Id128(1UL, 1UL)), 1U);
            RouteId route = new RouteId(new Id128(2UL, 2UL));
            TargetId target = new TargetId(new Id128(3UL, 3UL));

            host.Submit(new CommandEnvelope(first, route, target, schema, null, new FrozenPayload(new byte[] { 1 })));
            host.Submit(new CommandEnvelope(second, route, target, schema, null, new FrozenPayload(new byte[] { 2 })));

            Assert.That(host.Read(never).Outcome, Is.EqualTo(OperationReadOutcome.Unknown));
            Assert.That(host.Read(never).Code, Is.EqualTo(DiagnosticCode.None));

            OperationReadResult expired = host.Read(first);
            Assert.That(expired.Outcome, Is.EqualTo(OperationReadOutcome.Expired), "Retention must drop the oldest result, not the newest.");
            Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(host.Read(second).Outcome, Is.EqualTo(OperationReadOutcome.Found));
        }

        [Test]
        public void CallbackGateRejectsForeignWorldAndStaleActivation()
        {
            StubCallbackGate gate = new StubCallbackGate(WorldA);
            PluginInstanceId instance = new PluginInstanceId(new Id128(4UL, 4UL));
            gate.RegisterActivation(instance, new InstallationGeneration(2UL), new ActivationEpoch(5UL));

            OperationId operation = new OperationId(WorldA, Id128.Zero, 1UL);
            AsyncWorkToken current = new AsyncWorkToken(operation, instance, new InstallationGeneration(2UL), new ActivationEpoch(5UL), 0U);
            Assert.That(gate.Evaluate(current), Is.EqualTo(CallbackGateDecision.Dispatch));

            AsyncWorkToken stale = new AsyncWorkToken(operation, instance, new InstallationGeneration(2UL), new ActivationEpoch(4UL), 0U);
            Assert.That(gate.Evaluate(stale), Is.EqualTo(CallbackGateDecision.DiscardStaleActivation));

            AsyncWorkToken foreign = new AsyncWorkToken(new OperationId(WorldB, Id128.Zero, 1UL), instance, new InstallationGeneration(2UL), new ActivationEpoch(5UL), 0U);
            Assert.That(gate.Evaluate(foreign), Is.EqualTo(CallbackGateDecision.DiscardForeignWorld));

            gate.RetireActivation(instance);
            Assert.That(gate.Evaluate(current), Is.EqualTo(CallbackGateDecision.DiscardRetiredRoute));
        }

        [Test]
        public void ObservationReaderReturnsExpiryAndBackpressureInsteadOfThrowing()
        {
            StubObservationReader reader = new StubObservationReader(WorldA, 1);
            SnapshotToken retained = new SnapshotToken(WorldA, new AssemblyEpoch(1UL), new LogicalStepId(0UL));
            SnapshotToken alsoRetained = new SnapshotToken(WorldA, new AssemblyEpoch(1UL), new LogicalStepId(1UL));
            SnapshotToken missing = new SnapshotToken(WorldA, new AssemblyEpoch(9UL), new LogicalStepId(9UL));
            SnapshotToken foreign = new SnapshotToken(WorldB, new AssemblyEpoch(1UL), new LogicalStepId(0UL));

            reader.Retain(retained, new byte[] { 1 });

            Assert.That(reader.Acquire(missing).Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired));
            Assert.That(reader.Acquire(missing).Code, Is.EqualTo(DiagnosticCode.CursorExpired));
            Assert.That(reader.Acquire(foreign).Outcome, Is.EqualTo(SnapshotAcquireOutcome.ForeignWorld));

            SnapshotAcquireResult acquired = reader.Acquire(retained);
            Assert.That(acquired.Succeeded, Is.True);
            Assert.That(acquired.Lease, Is.Not.Null);
            acquired.Lease!.Dispose();

            reader.Retain(alsoRetained, new byte[] { 2 });
            SnapshotAcquireResult backpressure = reader.Acquire(alsoRetained);
            Assert.That(backpressure.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Backpressure), "Retention capacity must refuse a new lease rather than overwrite leased memory (P-007).");
            Assert.That(backpressure.Code, Is.EqualTo(DiagnosticCode.SnapshotBackpressure));
        }

        [Test]
        public void CatalogLookupReportsMissingKeysAndUnsupportedVersions()
        {
            StubCatalog catalog = new StubCatalog();
            FactoryKey known = new FactoryKey(new Id128(0x1000UL, 1UL), 1U);
            FactoryKey unknown = new FactoryKey(new Id128(0x1000UL, 2UL), 1U);
            FactoryKey wrongVersion = new FactoryKey(new Id128(0x1000UL, 1UL), 2U);
            SchemaId schemaId = new SchemaId(new Id128(0x2000UL, 1UL));

            Assert.That(catalog.RegisterFactory(new FactoryRegistration(known, FactoryKind.SystemFactory, new Id128(0x3000UL, 1UL), new Id128(0x4000UL, 1UL), 1U)), Is.Empty);
            Assert.That(catalog.RegisterSchema(new SchemaRegistration(new SchemaRef(schemaId, 1U), new Id128(0x3000UL, 1UL), known, true)), Is.Empty);

            Assert.That(catalog.Lookup(known).Found, Is.True);
            Assert.That(catalog.Lookup(unknown).Found, Is.False);
            Assert.That(catalog.Lookup(unknown).Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(catalog.Lookup(wrongVersion).Found, Is.False);
            Assert.That(catalog.Lookup(wrongVersion).Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(catalog.Lookup(default(FactoryKey)).Found, Is.False, "A default key is not a catalog identity.");

            Assert.That(catalog.LookupSchema(new SchemaRef(schemaId, 1U)).Found, Is.True);
            Assert.That(catalog.LookupSchema(new SchemaRef(schemaId, 2U)).Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(catalog.LookupSchema(new SchemaRef(new SchemaId(new Id128(0x2000UL, 9UL)), 1U)).Code, Is.EqualTo(DiagnosticCode.MissingDependency));

            // A duplicate registration is rejected rather than silently replacing the first owner (P-009).
            Assert.That(catalog.RegisterFactory(new FactoryRegistration(known, FactoryKind.Reducer, Id128.Zero, Id128.Zero, 1U)), Is.Not.Empty);
            Assert.That(catalog.FactoryCount, Is.EqualTo(1));
            Assert.That(catalog.FactoryKeysInCanonicalOrder().Count, Is.EqualTo(1));
            Assert.That(catalog.Fingerprint.IsEmpty, Is.False);
        }

        [Test]
        public void CompositionHostDoubleAdmitsStaleRejectsRetainsAndExpires()
        {
            CompositionHostSettings settings = new CompositionHostSettings(
                new ControlLaneCapacitySettings(8, 4),
                OperationExpirySettings.Default);
            StubCompositionHost host = new StubCompositionHost(WorldA, new CompositionRevision(1UL), AssemblyEpoch.First, PropagationMode.Automatic, settings, 2);

            ScopeId root = new ScopeId(new Id128(0x5000UL, 1UL));
            host.RegisterScope(new ScopeSnapshot(root, default(ScopeId), PropagationMode.Automatic, new IsolationSet(false, null), new IsolationSet(false, null), null, null));
            Assert.That(host.FindScope(root), Is.Not.Null);
            Assert.That(host.FindScope(root)!.IsRoot, Is.True);
            Assert.That(host.FindScope(new ScopeId(new Id128(0x5000UL, 9UL))), Is.Null, "An unknown scope must not be invented.");

            PluginInstanceId instance = new PluginInstanceId(new Id128(0x6000UL, 1UL));
            InstallRecord record = new InstallRecord(instance, new PluginTypeId(new Id128(0x7000UL, 1UL)), root, new DefinitionRevision(1UL), ContentHash.Empty, 0, new InstallationGeneration(1UL), ActivationEpoch.First);
            host.RegisterInstall(new InstallSnapshot(record, InstallationState.WaitingForDependencies, null, null));
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));

            OperationId staleOperation = new OperationId(WorldA, Id128.Zero, 1UL);
            OperationStatusHandle stale = host.Submit(new CompositionEditRequest(staleOperation, new CompositionRevision(0UL), CompositionEditKind.Add, new FrozenPayload(new byte[] { 1 })));
            OperationReadResult staleResult = host.Read(stale);
            Assert.That(staleResult.Outcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(staleResult.Entry!.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(staleResult.Entry.Outcome, Is.EqualTo(Outcome.Rejected));

            OperationId good = new OperationId(WorldA, Id128.Zero, 2UL);
            OperationStatusHandle handle = host.Submit(new CompositionEditRequest(good, host.PublishedRevision, CompositionEditKind.Add, new FrozenPayload(new byte[] { 2 })));
            Assert.That(host.Read(handle).Entry!.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(host.PublishedRevision.Value, Is.EqualTo(2UL));

            // Retransmission of the same id and payload is idempotent and returns the original row (P-050).
            OperationStatusHandle again = host.Submit(new CompositionEditRequest(good, host.PublishedRevision, CompositionEditKind.Add, new FrozenPayload(new byte[] { 2 })));
            Assert.That(again.Operation, Is.EqualTo(handle.Operation));
            Assert.That(host.DuplicateCount, Is.EqualTo(1));
            Assert.That(host.PublishedRevision.Value, Is.EqualTo(2UL), "A retransmission must not publish again.");

            // Retention drops the oldest retained result; reads then report expiry rather than unknown (P-050).
            host.Submit(new CompositionEditRequest(new OperationId(WorldA, Id128.Zero, 3UL), host.PublishedRevision, CompositionEditKind.Update, new FrozenPayload(new byte[] { 3 })));
            host.Submit(new CompositionEditRequest(new OperationId(WorldA, Id128.Zero, 4UL), host.PublishedRevision, CompositionEditKind.Update, new FrozenPayload(new byte[] { 4 })));
            Assert.That(host.Read(stale).Outcome, Is.EqualTo(OperationReadOutcome.Expired));
            Assert.That(host.Read(new OperationStatusHandle(new OperationId(WorldA, Id128.Zero, 77UL), CompositionRevision.Zero)).Outcome, Is.EqualTo(OperationReadOutcome.Unknown));
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(1));
            Assert.That(host.Snapshot().Installs.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExecutionDriverRetainsTimeDebtAndStopsDispatchAtTheFailingEntry()
        {
            FixedStepSettings fixedStep = new FixedStepSettings(4UL, 60UL, 2U, false);
            StubResourceLedger ledger = new StubResourceLedger();
            StubWorldHost host = new StubWorldHost(WorldA, TemporalModel.FixedStep, fixedStep, ledger);

            WorldCreateResult created = host.Create(new WorldCreateRequest(
                WorldA,
                new WorldDefinitionId(new Id128(0x8000UL, 1UL)),
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                ContentHash.Empty,
                new OperationId(WorldA, Id128.Zero, 1UL),
                fixedStep));
            Assert.That(created.Created, Is.True);
            Assert.That(created.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));

            // A malformed fixed-step configuration and a default definition id are refused, not defaulted.
            Assert.That(host.Create(new WorldCreateRequest(WorldA, default(WorldDefinitionId), TemporalModel.FixedStep, PropagationMode.Automatic, ContentHash.Empty, new OperationId(WorldA, Id128.Zero, 2UL), fixedStep)).Created, Is.False);
            Assert.That(host.Create(new WorldCreateRequest(WorldA, new WorldDefinitionId(new Id128(0x8000UL, 1UL)), TemporalModel.FixedStep, PropagationMode.Automatic, ContentHash.Empty, new OperationId(WorldA, Id128.Zero, 3UL), null)).Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));

            StubExecutionDriver driver = new StubExecutionDriver(host, ledger);

            // Five requested steps at a catch-up limit of two: two run, three stay as retained debt (P-036).
            StepAdvanceResult advance = driver.Advance(new StepAdvanceRequest(WorldA, host.CurrentEpoch, host.CurrentStep, 5UL, TimeDebt.Zero));
            Assert.That(advance.Accepted, Is.True);
            Assert.That(advance.Step.Value, Is.EqualTo(2UL));
            Assert.That(advance.Debt.Ticks, Is.EqualTo(3UL));
            Assert.That(advance.Epoch.Value, Is.EqualTo(host.CurrentEpoch.Value), "A committed step must not move the assembly epoch (P-006).");
            Assert.That(advance.PublishedSnapshot, Is.Not.Null);

            // A stale expected step or epoch rejects without advancing (P-027, P-031).
            Assert.That(driver.Advance(new StepAdvanceRequest(WorldA, host.CurrentEpoch, new LogicalStepId(0UL), 1UL, TimeDebt.Zero)).Accepted, Is.False);
            Assert.That(driver.Advance(new StepAdvanceRequest(WorldB, host.CurrentEpoch, host.CurrentStep, 1UL, TimeDebt.Zero)).Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(driver.Advance(new StepAdvanceRequest(WorldA, host.CurrentEpoch, host.CurrentStep, 0UL, TimeDebt.Zero)).Outcome, Is.EqualTo(Outcome.NoChange));

            OrderedDispatchTable table = new OrderedDispatchTable(host.CurrentEpoch, new[]
            {
                new SystemDispatchEntry(new StageId(new Id128(0x9000UL, 1UL)), new FactoryKey(new Id128(0xa000UL, 1UL), 1U), SystemDispatchKind.ManagedSystem, 0),
                new SystemDispatchEntry(new StageId(new Id128(0x9000UL, 1UL)), new FactoryKey(new Id128(0xa000UL, 2UL), 1U), SystemDispatchKind.ManagedSystem, 1),
                new SystemDispatchEntry(new StageId(new Id128(0x9000UL, 2UL)), new FactoryKey(new Id128(0xa000UL, 3UL), 1U), SystemDispatchKind.UnmanagedSystem, 2),
            }, null);
            Assert.That(table.IsWellFormed(), Is.True);

            driver.FaultAtDispatchIndex = 1;
            DispatchRunResult faulted = driver.Dispatch(new StageDispatchRequest(WorldA, host.CurrentEpoch, host.CurrentStep, table));
            Assert.That(faulted.Completed, Is.False);
            Assert.That(faulted.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(faulted.StoppedAtIndex, Is.EqualTo(1));
            Assert.That(faulted.DispatchedCount, Is.EqualTo(1), "Only the entry before the failure may have run.");
            Assert.That(driver.DispatchedSystemKeys.Count, Is.EqualTo(1));
            Assert.That(faulted.UnreachedSystemKeys.Count, Is.EqualTo(1), "The entry after the failure must not run (P-031).");
            Assert.That(driver.IsFaulted, Is.True);
            Assert.That(ledger.OutstandingJobCount, Is.EqualTo(1), "The failing entry's job stays tracked until safe teardown (P-041, P-048).");
            Assert.That(ledger.QuarantinedJobCount, Is.EqualTo(1), "The unfinished job's buffers stay retained behind quarantine (P-048).");
            Assert.That(ledger.Snapshot(WorldA, host.CurrentEpoch).Jobs.Count, Is.EqualTo(2));

            // A faulted world publishes nothing and refuses further steps.
            Assert.That(driver.Advance(new StepAdvanceRequest(WorldA, host.CurrentEpoch, host.CurrentStep, 1UL, TimeDebt.Zero)).Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            host.FaultForFixture();
            // The failing entry still owns an unfinished job; P-047/P-048 forbid disposal before it ends.
            OperationResult stopped = host.Stop(new OperationId(WorldA, Id128.Zero, 9UL), "test");
            Assert.That(stopped.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(stopped.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Stopping));
            Assert.That(ledger.OutstandingJobCount, Is.EqualTo(1));
            Assert.That(ledger.QuarantinedJobCount, Is.EqualTo(1));
        }

        [Test]
        public void ResourceLedgerRetiresInReverseOrderAndQuarantinesTheFailedRelease()
        {
            StubResourceLedger ledger = new StubResourceLedger();
            PluginInstanceId instance = new PluginInstanceId(new Id128(0xb000UL, 1UL));
            Id128 failing = new Id128(0xc000UL, 2UL);

            for (uint ordinal = 0; ordinal < 3U; ordinal++)
            {
                ledger.Acquire(new WorldResourceRecord(
                    new Id128(0xc000UL, ordinal + 1UL),
                    WorldResourceKind.ManagedLease,
                    new ResourceKey(new Id128(0xd000UL, ordinal + 1UL)),
                    new OwnerId(new Id128(0xe000UL, 1UL)),
                    instance,
                    ResourceRetirementState.Ready,
                    Id128.Zero,
                    ordinal,
                    64UL));
            }

            IReadOnlyList<Id128> attempted = ledger.RetireInstanceInReverseAcquisitionOrder(instance, failing);
            Assert.That(attempted.Count, Is.EqualTo(3));
            Assert.That(attempted[0], Is.EqualTo(new Id128(0xc000UL, 3UL)), "Retirement runs in reverse acquisition order (P-048).");
            Assert.That(attempted[2], Is.EqualTo(new Id128(0xc000UL, 1UL)));

            Assert.That(ledger.Retire(failing), Is.False, "A quarantined resource is retained, not retired.");
            Assert.That(ledger.Retire(attempted[0]), Is.False, "A resource is retired at most once (P-048).");

            WorldResourceLedgerSnapshot snapshot = ledger.Snapshot(WorldA, AssemblyEpoch.First);
            Assert.That(snapshot.QuarantinedCount, Is.EqualTo(1));
            Assert.That(snapshot.QuarantinedBytes, Is.EqualTo(64UL));
            Assert.That(snapshot.Resources.Count, Is.EqualTo(3));
        }

        [Test]
        public void StalledJobBlocksStopInsteadOfBeingFreedOnTimeout()
        {
            StubResourceLedger ledger = new StubResourceLedger();
            StubWorldHost host = new StubWorldHost(WorldA, TemporalModel.CommandDriven, null, ledger);
            ledger.AddJob(new JobLedgerRecord(new Id128(0xf000UL, 1UL), default(StageId), default(FactoryKey), AssemblyEpoch.First, LogicalStepId.Zero, false, false));

            OperationResult stopped = host.Stop(new OperationId(WorldA, Id128.Zero, 1UL), "stalled job");
            Assert.That(stopped.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(stopped.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Stopping));

            ledger.CompleteJob(new Id128(0xf000UL, 1UL));
            Assert.That(host.Stop(new OperationId(WorldA, Id128.Zero, 2UL), "settled").Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(host.Lifecycle, Is.EqualTo(WorldLifecycleState.Disposed));
        }

        [Test]
        public void LifecycleObserverRecordsChangesAndCommittedSteps()
        {
            StubResourceLedger ledger = new StubResourceLedger();
            StubWorldHost host = new StubWorldHost(WorldA, TemporalModel.CommandDriven, null, ledger);
            StubWorldLifecycleObserver observer = new StubWorldLifecycleObserver();

            observer.OnWorldLifecycleChanged(new WorldLifecycleChange(WorldA, WorldLifecycleState.Created, WorldLifecycleState.Running, DiagnosticCode.None));
            observer.OnStepCommitted(new StepCommitEvent(
                new SnapshotToken(WorldA, AssemblyEpoch.First, new LogicalStepId(1UL)),
                null,
                new EventSequence(7UL),
                ContentHash.Empty));

            Assert.That(observer.Changes.Count, Is.EqualTo(1));
            Assert.That(observer.Commits.Count, Is.EqualTo(1));
            Assert.That(observer.Commits[0].FirstEventSequence.Value, Is.EqualTo(7UL));
            Assert.That(host.CurrentStep, Is.EqualTo(LogicalStepId.Zero));
        }

        [Test]
        public void ExplanationPagingIsBoundedAndStagedPlanReadsAreLabelled()
        {
            StubExplanationReader reader = new StubExplanationReader();
            TargetId target = new TargetId(ParseId("33333333333333333333333333333333"));
            CapabilityId capability = new CapabilityId(ParseId("44444444444444444444444444444444"));
            SnapshotToken token = new SnapshotToken(WorldA, AssemblyEpoch.First, LogicalStepId.Zero);
            SnapshotToken stagedToken = new SnapshotToken(WorldA, AssemblyEpoch.First, new LogicalStepId(4UL));
            OperationId operation = new OperationId(WorldA, Id128.Zero, 3UL);

            List<ExplanationRecord> matching = new List<ExplanationRecord>
            {
                new ExplanationRecord(new Id128(1UL, 1UL), new RuleId(new Id128(2UL, 1UL)), new ProviderInstallationId(new Id128(3UL, 1UL)), DiagnosticCode.None, null),
                new ExplanationRecord(new Id128(1UL, 2UL), new RuleId(new Id128(2UL, 2UL)), new ProviderInstallationId(new Id128(3UL, 2UL)), DiagnosticCode.None, null),
                new ExplanationRecord(new Id128(1UL, 3UL), new RuleId(new Id128(2UL, 3UL)), new ProviderInstallationId(new Id128(3UL, 3UL)), DiagnosticCode.None, null),
            };
            List<ExplanationRecord> rejected = new List<ExplanationRecord>
            {
                new ExplanationRecord(new Id128(9UL, 1UL), new RuleId(new Id128(8UL, 1UL)), new ProviderInstallationId(new Id128(7UL, 1UL)), DiagnosticCode.Ineligible, null),
            };

            reader.AddPublished(target, capability, token, matching, rejected);
            reader.AddStaged(operation, stagedToken, target, capability, matching, rejected);

            ExplanationPage first = reader.Explain(target, capability, token, ExplanationPageRequest.FirstPage(2U));
            Assert.That(first.Source, Is.EqualTo(ExplanationSource.PublishedComposition));
            Assert.That(first.Matching.Count, Is.EqualTo(2));
            Assert.That(first.Rejected, Is.Empty);
            Assert.That(first.TotalMatching, Is.EqualTo(3UL));
            Assert.That(first.TotalRejected, Is.EqualTo(1UL));
            Assert.That(first.HasMore, Is.True);
            Assert.That(first.NextOffset, Is.EqualTo(2U));

            // The second page crosses the boundary between matching and rejected records without loss.
            ExplanationPage second = reader.Explain(target, capability, token, new ExplanationPageRequest(first.NextOffset, 2U));
            Assert.That(second.Matching.Count, Is.EqualTo(1));
            Assert.That(second.Rejected.Count, Is.EqualTo(1));
            Assert.That(second.HasMore, Is.False);

            ExplanationPage staged = reader.ReadStaged(operation, target, capability, ExplanationPageRequest.FirstPage(1U));
            Assert.That(staged.Source, Is.EqualTo(ExplanationSource.StagedPlan), "Staged plan data must be labelled distinctly from world observation (05 s5).");
            Assert.That(staged.Token, Is.EqualTo(stagedToken));
            Assert.That(staged.Matching.Count, Is.EqualTo(1));

            ExplanationPage unknown = reader.ReadStaged(new OperationId(WorldA, Id128.Zero, 99UL), target, capability, ExplanationPageRequest.FirstPage(1U));
            Assert.That(unknown.Matching, Is.Empty);
            Assert.That(reader.UnknownStagedOperationCount, Is.EqualTo(1));

            ExplanationPage invalid = reader.Explain(target, capability, token, new ExplanationPageRequest(0U, 0U));
            Assert.That(invalid.Matching, Is.Empty);
            Assert.That(reader.InvalidPageRequestCount, Is.EqualTo(1));
        }

        [Test]
        public void TimeDebtRefusesToBecomeNegativeOrWrap()
        {
            TimeDebt debt = TimeDebt.Zero.Add(10UL, out bool accepted);
            Assert.That(accepted, Is.True);
            Assert.That(debt.WholeSteps(4UL, out bool validSteps), Is.EqualTo(2UL));
            Assert.That(validSteps, Is.True);

            debt.Subtract(11UL, out bool overConsumed);
            Assert.That(overConsumed, Is.False, "Consuming more debt than exists is refused, not clamped (P-036).");

            TimeDebt nearMax = new TimeDebt(ulong.MaxValue - 1UL);
            nearMax.Add(5UL, out bool overflowed);
            Assert.That(overflowed, Is.False);
            Assert.That(nearMax.Ticks, Is.EqualTo(ulong.MaxValue - 1UL), "Debt accumulation must never wrap (P-005).");

            Assert.That(TimeDebt.Zero.WholeSteps(0UL, out bool zeroStepValid), Is.EqualTo(0UL));
            Assert.That(zeroStepValid, Is.False, "A zero step duration is an invalid configuration, not a free step.");
        }

        [Test]
        public void CanonicalMapOrderIsIndependentOfInsertionOrderAndReportsDuplicates()
        {
            List<KeyValuePair<Id128, int>> entries = new List<KeyValuePair<Id128, int>>
            {
                new KeyValuePair<Id128, int>(SeamContractTests.ParseId("00000000000000020000000000000001"), 2),
                new KeyValuePair<Id128, int>(SeamContractTests.ParseId("00000000000000010000000000000001"), 1),
                new KeyValuePair<Id128, int>(SeamContractTests.ParseId("00000000000000010000000000000002"), 3),
            };

            IReadOnlyList<KeyValuePair<Id128, int>> ordered = CanonicalMapOrder.ByIdKey(entries, out bool duplicates);
            Assert.That(duplicates, Is.False);
            Assert.That(ordered[0].Value, Is.EqualTo(1));
            Assert.That(ordered[1].Value, Is.EqualTo(3));
            Assert.That(ordered[2].Value, Is.EqualTo(2));

            // The same set in a different insertion order produces the identical canonical sequence.
            List<KeyValuePair<Id128, int>> reversed = new List<KeyValuePair<Id128, int>> { entries[2], entries[1], entries[0] };
            IReadOnlyList<KeyValuePair<Id128, int>> reordered = CanonicalMapOrder.ByIdKey(reversed, out bool _);
            for (int i = 0; i < ordered.Count; i++)
            {
                Assert.That(reordered[i].Key, Is.EqualTo(ordered[i].Key));
            }

            List<KeyValuePair<Id128, int>> withDuplicate = new List<KeyValuePair<Id128, int>> { entries[0], entries[1], entries[0] };
            CanonicalMapOrder.ByIdKey(withDuplicate, out bool hasDuplicate);
            Assert.That(hasDuplicate, Is.True, "Duplicate keys must be reported so the caller can reject the document.");

            List<KeyValuePair<string, int>> strings = new List<KeyValuePair<string, int>>
            {
                new KeyValuePair<string, int>("market.basic", 1),
                new KeyValuePair<string, int>("Market.basic", 2),
                new KeyValuePair<string, int>("card", 3),
            };
            IReadOnlyList<KeyValuePair<string, int>> orderedStrings = CanonicalMapOrder.ByOrdinalStringKey(strings, out bool _);
            Assert.That(orderedStrings[0].Key, Is.EqualTo("Market.basic"), "Ordinal code-point order must be used, not culture-aware order.");
            Assert.That(orderedStrings[1].Key, Is.EqualTo("card"));
        }

        [Test]
        public void ApiSnapshotAndFixtureRootsResolveFromTheRepository()
        {
            string root = RepoLayout.FindRoot();
            Assert.That(System.IO.File.Exists(RepoLayout.Resolve(root, RepoLayout.ApiSnapshotPath)), Is.True);
            Assert.That(System.IO.Directory.Exists(RepoLayout.Resolve(root, RepoLayout.FixtureCaseDirectory)), Is.True);
        }
    }
}
