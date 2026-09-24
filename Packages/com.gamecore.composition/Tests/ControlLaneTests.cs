// GameCore.Composition tests — the serialized control lane: admission, idempotency, retention, capacity,
// cancellation cutoffs and atomic publication (P-050, P-051, P-006, P-027, O-03, O-08, O-18).
//
// Every case reads the ledger and the committed snapshot, so an assertion about "no new epoch" or "the original
// result was kept" is checked against observable lane state rather than an internal flag.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ControlLaneTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 4UL));
        private static readonly IdFactory Ids = new IdFactory(0x6C616E65UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 4UL));

        private static CompositionHost NewHost(int queued = 16, int retained = 16, ulong retainedSteps = 0UL)
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHostSettings settings = new CompositionHostSettings(
                new ControlLaneCapacitySettings(queued, retained),
                new OperationExpirySettings(3, retainedSteps));
            return new CompositionHost(World, Root, settings, source, null, PropagationMode.Automatic);
        }

        private static ScopeId NewScope(CompositionHost host, OperationIssuer issuer)
        {
            ScopeId scope = Ids.Scope();
            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), issuer.Next(), host.Snapshot().Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            host.Drain();
            return scope;
        }

        [Test]
        public void PublicationMovesRevisionAndEpochButNeverTheLogicalStep()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 1UL));

            NewScope(host, issuer);

            CompositionStateSnapshot snapshot = host.Snapshot();
            Assert.That(snapshot.Revision.Value, Is.EqualTo(1UL), "The initial publication takes revision 1 (05 s2).");
            Assert.That(snapshot.Epoch.Value, Is.EqualTo(1UL));
            Assert.That(snapshot.Step, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(host.OperationLedger.PublishedRevision.Value, Is.EqualTo(1UL));
            Assert.That(host.PublicationCount, Is.EqualTo(1));
        }

        [Test]
        public void SameOperationIdAndSameInputCoalesceToOnePublication()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 2UL));
            ScopeId scope = Ids.Scope();
            CompositionEditPayload payload = Payloads.ScopeCreate(scope, Root);
            OperationId operation = issuer.Next();

            EditAdmission first = host.SubmitEdit(payload, operation, CompositionRevision.Zero);
            EditAdmission retry = host.SubmitEdit(payload, operation, CompositionRevision.Zero);

            Assert.That(first.Kind, Is.EqualTo(AdmissionKind.Fresh));
            Assert.That(retry.Kind, Is.EqualTo(AdmissionKind.Retransmission));
            Assert.That(retry.Handle, Is.EqualTo(first.Handle));

            IReadOnlyList<PublishedOperation> published = host.Drain();
            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL), "A retransmission never publishes twice.");
            Assert.That(host.OperationLedger.AdmittedCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.DuplicateCount, Is.EqualTo(1));

            OperationReadResult read = host.Read(first.Handle);
            Assert.That(read.Outcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(read.Entry!.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(read.Entry.PublishedSnapshot.HasValue, Is.True);
        }

        [Test]
        public void ConflictingReuseIsRejectedAndKeepsTheOriginalResult()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 3UL));
            OperationId operation = issuer.Next();

            host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), operation, CompositionRevision.Zero);
            host.Drain();
            OperationLedgerEntry original = host.Read(new OperationStatusHandle(operation, CompositionRevision.Zero)).Entry!;

            // Same identity, different declaration: the original row must not be overwritten (P-050).
            EditAdmission conflict = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), operation, new CompositionRevision(1UL));

            Assert.That(conflict.Kind, Is.EqualTo(AdmissionKind.IdempotencyConflict));
            Assert.That(conflict.Code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(host.OperationLedger.ConflictCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.LastConflictCode, Is.EqualTo(DiagnosticCode.IdempotencyConflict));

            OperationLedgerEntry after = host.Read(conflict.Handle).Entry!;
            Assert.That(after.InputHash, Is.EqualTo(original.InputHash));
            Assert.That(after.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(after.PublishedRevision.Value, Is.EqualTo(1UL));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void StaleExpectedRevisionIsRejectedWithoutPublishing()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 4UL));
            NewScope(host, issuer);

            EditAdmission stale = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), CompositionRevision.Zero);

            Assert.That(stale.Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(2));
            Assert.That(stale.Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
        }

        [Test]
        public void StagedProposalIsInspectableWhileTheCommittedRevisionIsUnchanged()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 5UL));
            ScopeId scope = Ids.Scope();

            EditAdmission staged = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), issuer.Next(), CompositionRevision.Zero);

            Assert.That(staged.Staged, Is.True);
            Assert.That(host.StagedPlan(staged.Handle.Operation), Is.Not.Null, "Staged progress is readable per operation (00 s9).");
            Assert.That(host.StagedPlan(staged.Handle.Operation)!.After.Scopes.Count, Is.EqualTo(2));
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(1), "Public queries see only the committed assembly (00 s9).");
            Assert.That(host.FindScope(scope), Is.Null);
            Assert.That(staged.Entry!.Outcome, Is.EqualTo(Outcome.Pending));

            host.Drain();
            Assert.That(host.FindScope(scope), Is.Not.Null);
            Assert.That(host.Read(staged.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Published));
        }

        [Test]
        public void NoChangeIncrementsNothing()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 6UL));

            EditAdmission admission = host.SubmitEdit(Payloads.SetMode(PropagationMode.Automatic), issuer.Next(), CompositionRevision.Zero);

            Assert.That(admission.Staged, Is.False);
            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(admission.Entry!.Outcome, Is.EqualTo(Outcome.NoChange));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
            Assert.That(host.Snapshot().Epoch.Value, Is.EqualTo(0UL));
            Assert.That(host.OperationLedger.PublishedRevision.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void ModeSwitchPublishesOneSettingAndKeepsAutomaticAsTheDefault()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 7UL));

            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Automatic), "Automatic is the default mode (P-013).");

            EditAdmission admission = host.SubmitEdit(Payloads.SetMode(PropagationMode.Conservative), issuer.Next(), CompositionRevision.Zero);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Automatic), "A staged mode switch is not yet visible (00 s9).");

            host.Drain();
            Assert.That(host.Mode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
            Assert.That(admission.Plan!.Delta!.Mode.HasValue, Is.True);
            Assert.That(admission.Plan.Delta!.Mode!.Value.NewMode, Is.EqualTo(PropagationMode.Conservative));
        }

        [Test]
        public void ResultsExpireByCountAndAnExpiredOperationCannotReexecute()
        {
            CompositionHost host = NewHost(retained: 2);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 8UL));
            List<OperationStatusHandle> handles = new List<OperationStatusHandle>();

            for (int i = 0; i < 3; i++)
            {
                EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), host.Snapshot().Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                handles.Add(admission.Handle);
                host.Drain();
            }

            Assert.That(host.Read(handles[0]).Outcome, Is.EqualTo(OperationReadOutcome.Expired), "Expired retention is distinct from unknown (05 s5).");
            Assert.That(host.Read(handles[2]).Outcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(host.Read(handles[0]).Code, Is.EqualTo(DiagnosticCode.ResultExpired));

            CompositionEditPayload late = Payloads.ScopeCreate(Ids.Scope(), Root);
            EditAdmission reexecute = host.SubmitEdit(late, handles[0].Operation, host.Snapshot().Revision);
            Assert.That(reexecute.Kind, Is.EqualTo(AdmissionKind.ResultExpired));
            Assert.That(reexecute.Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(3UL), "An expired result cannot re-execute (P-050).");
        }

        [Test]
        public void ResultsAlsoExpireByLogicalStepWindow()
        {
            CompositionHost host = NewHost(retainedSteps: 1UL);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 9UL));

            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            Assert.That(host.Read(admission.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Found));

            Assert.That(host.AdvanceSteps(new LogicalStepId(1UL)), Is.EqualTo(0));
            Assert.That(host.Read(admission.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Found));

            Assert.That(host.AdvanceSteps(new LogicalStepId(3UL)), Is.EqualTo(1));
            Assert.That(host.Read(admission.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Expired));
            Assert.That(host.Snapshot().Step, Is.EqualTo(new LogicalStepId(3UL)));
        }

        [Test]
        public void LaneCapacityRefusesFurtherAdmission()
        {
            CompositionHost host = NewHost(queued: 1);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 10UL));

            EditAdmission first = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), CompositionRevision.Zero);
            Assert.That(first.Staged, Is.True);
            Assert.That(host.OperationLedger.RowCount, Is.EqualTo(1));

            EditAdmission second = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), CompositionRevision.Zero);
            Assert.That(second.Kind, Is.EqualTo(AdmissionKind.CapacityRejected));
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(host.OperationLedger.CapacityRejectedCount, Is.EqualTo(1));

            host.Drain();
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(2), "The refused operation published nothing.");

            // Capacity is released when settled rows leave the retention window, so the lane recovers rather
            // than staying permanently full (P-050's bounded ledger).
            Assert.That(host.Read(first.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Found));
            host.OperationLedger.AdvanceRetention(new LogicalStepId(0UL));
            Assert.That(host.OperationLedger.RowCount, Is.EqualTo(1), "A settled row stays retained until it expires.");

            CompositionHost small = NewHost(queued: 1, retained: 1, retainedSteps: 1UL);
            OperationIssuer recoveryIssuer = new OperationIssuer(World, new Id128(0x6973737565UL, 20UL));
            EditAdmission occupying = small.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), recoveryIssuer.Next(), CompositionRevision.Zero);
            small.Drain();
            EditAdmission refused = small.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), recoveryIssuer.Next(), new CompositionRevision(1UL));
            Assert.That(refused.Kind, Is.EqualTo(AdmissionKind.CapacityRejected));

            // The retained result expires once the window closes, freeing the row for a new operation.
            Assert.That(small.AdvanceSteps(new LogicalStepId(2UL)), Is.EqualTo(1));
            Assert.That(small.Read(occupying.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Expired));
            Assert.That(small.OperationLedger.RowCount, Is.EqualTo(0));
            EditAdmission admitted = small.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), recoveryIssuer.Next(), new CompositionRevision(1UL));
            Assert.That(admitted.Staged, Is.True, admitted.Code.ToString());
            Assert.That(small.Drain().Count, Is.EqualTo(1));
            Assert.That(small.Snapshot().Revision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void ReorderedIssuerSequenceIsRefused()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 11UL));

            host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.At(5UL), CompositionRevision.Zero);
            host.Drain();

            EditAdmission reordered = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.At(3UL), new CompositionRevision(1UL));

            Assert.That(reordered.Kind, Is.EqualTo(AdmissionKind.ResultExpired));
            Assert.That(reordered.Code, Is.EqualTo(DiagnosticCode.ResultExpired), "P-050 includes unseen IDs below the issuer high-water mark.");
            Assert.That(host.OperationLedger.SequenceViolationCount, Is.EqualTo(1));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));

            // A strictly increasing sequence from the same issuer is still accepted.
            EditAdmission next = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.At(6UL), new CompositionRevision(1UL));
            Assert.That(next.Staged, Is.True, next.Code.ToString());
            host.Drain();
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 12UL));
            ScopeId cancelled = Ids.Scope();
            ScopeId published = Ids.Scope();

            EditAdmission pending = host.SubmitEdit(Payloads.ScopeCreate(cancelled, Root), issuer.Next(), CompositionRevision.Zero);
            OperationId beforeCutoff = issuer.Next();
            Assert.That(host.Cancel(beforeCutoff, pending.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL), "A cancelled operation publishes no new epoch (P-051).");
            Assert.That(host.Read(pending.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(host.Read(pending.Handle).Entry!.Code, Is.EqualTo(DiagnosticCode.Cancelled));
            Assert.That(host.StagedPlan(pending.Handle.Operation), Is.Null, "A cancelled operation leaves no staged proposal.");
            Assert.That(host.Drain(), Is.Empty);

            // Retransmitting the same request returns its recorded result; it never decides a second time.
            Assert.That(host.Cancel(beforeCutoff, pending.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.OperationLedger.CancelRetransmissionCount, Is.EqualTo(1));

            EditAdmission admitted = host.SubmitEdit(Payloads.ScopeCreate(published, Root), issuer.Next(), CompositionRevision.Zero);
            Assert.That(host.Drain().Count, Is.EqualTo(1));

            // A distinct request identity, submitted after publication, is the too-late case: the terminal
            // result of the published target stands and the cancellation made no write (P-051).
            OperationId afterCutoff = issuer.Next();
            Assert.That(host.Cancel(afterCutoff, admitted.Handle.Operation), Is.EqualTo(CancelOutcome.TooLate));
            Assert.That(host.Read(admitted.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Published), "A too-late cancellation never implies rollback (P-051).");
            Assert.That(host.Read(host.OperationLedger.RowOf(afterCutoff)!.Handle).Entry!.Code, Is.EqualTo(DiagnosticCode.TooLate));
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(2), "The published scope stays published.");

            // An unknown target that was never on this lane is reported as such, not as a cutoff outcome.
            Assert.That(host.Cancel(issuer.Next(), new OperationId(World, issuer.Id, 99UL)), Is.EqualTo(CancelOutcome.Unknown));
        }

        [Test]
        public void CancellingARequiredStepRejectsTheDependentProposal()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 13UL));
            ScopeId parent = Ids.Scope();
            ScopeId child = Ids.Scope();

            EditAdmission first = host.SubmitEdit(Payloads.ScopeCreate(parent, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission second = host.SubmitEdit(Payloads.ScopeCreate(child, parent), issuer.Next(), CompositionRevision.Zero);
            Assert.That(second.Staged, Is.True, second.Code.ToString());

            Assert.That(host.Cancel(issuer.Next(), first.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));

            // The dependent proposal is replanned against the committed definition, where its parent is gone, and
            // is rejected rather than published on top of a plan that assumed it existed (P-051).
            Assert.That(host.Read(second.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(host.Read(second.Handle).Entry!.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(host.Drain(), Is.Empty);
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void CancellingAnExpiredOperationReportsExpiry()
        {
            CompositionHost host = NewHost(retained: 1);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 14UL));

            EditAdmission first = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            EditAdmission second = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();

            Assert.That(host.Read(first.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Expired));
            Assert.That(host.Cancel(issuer.Next(), first.Handle.Operation), Is.EqualTo(CancelOutcome.ResultExpired));
            Assert.That(host.Read(second.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Expired), "The cancellation result displaced the last retained edit result.");
            Assert.That(host.Cancel(issuer.Next(), second.Handle.Operation), Is.EqualTo(CancelOutcome.ResultExpired));
        }

        [Test]
        public void SeveralAdmittedProposalsPublishInAdmissionOrder()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 15UL));
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            host.SubmitEdit(Payloads.ScopeCreate(second, first), issuer.Next(), CompositionRevision.Zero);

            IReadOnlyList<PublishedOperation> published = host.Drain();

            Assert.That(published.Count, Is.EqualTo(2));
            Assert.That(published[0].Token!.Value.AssemblyEpoch.Value, Is.EqualTo(1UL));
            Assert.That(published[1].Token!.Value.AssemblyEpoch.Value, Is.EqualTo(2UL));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
            Assert.That(host.Committed.Scopes.Depth(second), Is.EqualTo(2));
        }

        [Test]
        public void UndecodableProposalIsRejectedThroughTheFrozenSeam()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 16UL));

            FrozenPayload garbage = new FrozenPayload(new byte[] { 1, 2, 3, 4, 5 });
            OperationStatusHandle handle = host.Submit(new CompositionEditRequest(
                issuer.Next(),
                CompositionRevision.Zero,
                CompositionEditKind.Add,
                garbage));

            OperationReadResult read = host.Read(handle);
            Assert.That(read.Outcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(read.Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(read.Entry.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void SeamSubmissionRequiresTheDeclaredKindToMatchTheSubject()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 17UL));
            FrozenPayload payload = CompositionEditCodec.Encode(Payloads.ScopeCreate(Ids.Scope(), Root));

            OperationStatusHandle handle = host.Submit(new CompositionEditRequest(
                issuer.Next(),
                CompositionRevision.Zero,
                CompositionEditKind.Remove,
                payload));

            OperationReadResult read = host.Read(handle);
            Assert.That(read.Entry!.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));

            OperationStatusHandle matching = host.Submit(new CompositionEditRequest(
                issuer.Next(),
                CompositionRevision.Zero,
                CompositionEditKind.Add,
                payload));
            Assert.That(host.Read(matching).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Drain().Count, Is.EqualTo(1));
        }

        [Test]
        public void PayloadEncodingRoundTripsEveryField()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            ScopeId scope = Ids.Scope();
            Id128 contract = Ids.Capability().Value;
            CapabilityId imported = Ids.Capability();
            ConfigDocument config = ConfigDocument.Of(new ConfigField(contract, ConfigFieldValue.OfUInt32(4U)));

            CompositionEditPayload payload = new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                Root,
                true,
                new IsolationSet(false, new[] { contract }),
                new IsolationSet(true, null),
                new[] { new ExclusionRule(ExclusionTargetKind.Capability, contract, scope, new TargetId(Ids.NextId()), true) },
                new[] { new CapabilityImport(imported, new ProviderInstallationId(instance.Value)) },
                type,
                instance,
                new DefinitionRevision(3UL),
                ConfigDocumentCodec.HashOf(config),
                config,
                7,
                new[] { new ServiceSelection(new ContractRef(contract, 1U), new ProviderInstallationId(instance.Value)) },
                PropagationMode.Conservative);

            FrozenPayload encoded = CompositionEditCodec.Encode(payload);
            Assert.That(CompositionEditCodec.TryDecode(encoded, out CompositionEditPayload? decoded, out DiagnosticCode code), Is.True, code.ToString());

            Assert.That(decoded!.Subject, Is.EqualTo(payload.Subject));
            Assert.That(decoded.Scope, Is.EqualTo(payload.Scope));
            Assert.That(decoded.Parent, Is.EqualTo(payload.Parent));
            Assert.That(decoded.DestroySubtree, Is.True);
            Assert.That(decoded.ServiceIsolation.AllContracts, Is.False);
            Assert.That(decoded.ServiceIsolation.Contracts.Count, Is.EqualTo(1));
            Assert.That(decoded.ServiceIsolation.Contracts[0], Is.EqualTo(contract));
            Assert.That(decoded.CapabilityIsolation.AllContracts, Is.True);
            Assert.That(decoded.CapabilityIsolation.Contracts, Is.Empty);
            Assert.That(decoded.Exclusions.Count, Is.EqualTo(1));
            Assert.That(decoded.Exclusions[0].Kind, Is.EqualTo(ExclusionTargetKind.Capability));
            Assert.That(decoded.Exclusions[0].TargetId, Is.EqualTo(contract));
            Assert.That(decoded.Exclusions[0].AtScope, Is.EqualTo(scope));
            Assert.That(decoded.Exclusions[0].AppliesToSubtree, Is.True);
            Assert.That(decoded.Imports.Count, Is.EqualTo(1));
            Assert.That(decoded.Imports[0].CapabilityId, Is.EqualTo(imported));
            Assert.That(decoded.Imports[0].ProviderInstallationId, Is.EqualTo(new ProviderInstallationId(instance.Value)));
            Assert.That(decoded.PluginType, Is.EqualTo(payload.PluginType));
            Assert.That(decoded.Instance, Is.EqualTo(payload.Instance));
            Assert.That(decoded.ConfigRevision, Is.EqualTo(payload.ConfigRevision));
            Assert.That(decoded.ConfigHash, Is.EqualTo(payload.ConfigHash));
            Assert.That(decoded.Priority, Is.EqualTo(7));
            Assert.That(decoded.Selections.Count, Is.EqualTo(1));
            Assert.That(decoded.Selections[0].Contract, Is.EqualTo(new ContractRef(contract, 1U)));
            Assert.That(decoded.Selections[0].Provider, Is.EqualTo(new ProviderInstallationId(instance.Value)));
            Assert.That(decoded.Mode, Is.EqualTo(PropagationMode.Conservative));
            Assert.That(decoded.Config.Count, Is.EqualTo(1));
            Assert.That(decoded.Config.TryGetField(contract, out ConfigFieldValue field), Is.True);
            Assert.That(field.Kind, Is.EqualTo(ConfigValueKind.UInt32));
            Assert.That(field.AsUInt32, Is.EqualTo(4U));

            // Canonical encoding: the same declaration always produces identical bytes and the same input hash,
            // which is what makes one operation identity idempotent against a retransmitted payload (P-050).
            byte[] reencoded = DocumentCodec.ToBytes(CompositionEditCodec.Encode(decoded!));
            byte[] original = DocumentCodec.ToBytes(encoded);
            Assert.That(reencoded.Length, Is.EqualTo(original.Length));
            for (int i = 0; i < original.Length; i++)
            {
                Assert.That(reencoded[i], Is.EqualTo(original[i]), "Byte " + i + " must match exactly.");
            }

            Assert.That(CompositionEditApplier.InputHashOf(CompositionEditCodec.Encode(decoded!)), Is.EqualTo(CompositionEditApplier.InputHashOf(encoded)));

            // Two documents that differ in one field must not share an input hash: exclusions carry their scope.
            CompositionEditPayload otherScope = new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                Root,
                true,
                new IsolationSet(false, new[] { contract }),
                new IsolationSet(true, null),
                new[] { new ExclusionRule(ExclusionTargetKind.Capability, contract, Ids.Scope(), new TargetId(Ids.NextId()), true) },
                new[] { new CapabilityImport(imported, new ProviderInstallationId(instance.Value)) },
                type,
                instance,
                new DefinitionRevision(3UL),
                ConfigDocumentCodec.HashOf(config),
                config,
                7,
                new[] { new ServiceSelection(new ContractRef(contract, 1U), new ProviderInstallationId(instance.Value)) },
                PropagationMode.Conservative);

            Assert.That(
                CompositionEditApplier.InputHashOf(CompositionEditCodec.Encode(otherScope)),
                Is.Not.EqualTo(CompositionEditApplier.InputHashOf(encoded)),
                "A semantic difference changes the canonical input hash (P-027).");
        }

        [Test]
        public void RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration()
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 19UL));
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            source.Add(Manifests.Plain(type, Ids), null);

            host.SubmitEdit(Payloads.Mount(source.ManifestOf(type), instance, Root, null), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            InstallationGeneration first = host.FindInstall(instance)!.Record.Generation;

            host.SubmitEdit(Payloads.Unmount(instance), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Disposed));

            host.SubmitEdit(Payloads.Mount(source.ManifestOf(type), instance, Root, null), issuer.Next(), new CompositionRevision(2UL));
            host.Drain();

            InstallationGeneration second = host.FindInstall(instance)!.Record.Generation;
            Assert.That(second.Value, Is.EqualTo(first.Value + 1UL), "Unmount/remount changes the installation generation (P-005).");
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Active));
        }

        [Test]
        public void PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6973737565UL, 18UL));
            ScopeId scope = Ids.Scope();
            CompositionEditPayload payload = Payloads.ScopeCreate(scope, Root);
            ContentHash input = CompositionEditApplier.InputHashOf(CompositionEditCodec.Encode(payload));
            TestManifestSource manifests = new TestManifestSource();
            CompositionState baseline = host.Committed;
            CompositionEditPlan first = CompositionEditApplier.Plan(baseline, payload, issuer.Next(), baseline.Revision, input, manifests);
            CompositionEditPlan same = CompositionEditApplier.Plan(baseline, payload, issuer.Next(), baseline.Revision, input, manifests);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(same.PlanHash(), Is.EqualTo(first.PlanHash()), "Operation issuer sequence does not change semantic plan identity.");

            CompositionState later = baseline.With(revision: new CompositionRevision(1UL));
            CompositionEditPlan changedBase = CompositionEditApplier.Plan(later, payload, issuer.Next(), later.Revision, input, manifests);
            Assert.That(changedBase.Succeeded, Is.True);
            Assert.That(changedBase.PlanHash(), Is.Not.EqualTo(first.PlanHash()), "P-027 plan identity includes the base revision.");
        }
    }
}
