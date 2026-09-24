// GameCore.Composition tests — cancellation request identity (P-050, P-051, O-18).
//
// A cancellation is a mutating public operation with its own `OperationId` and canonical input hash, so it is
// ledgered exactly like an edit: a retransmission returns the original recorded outcome, a conflicting reuse is
// refused while keeping the original row, and a request the lane cannot admit changes nothing. These fixtures
// are the permanent form of the conformance probe that first demonstrated the missing behaviour.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class CancellationIdentityTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 6UL));
        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x6F74686572UL, 1UL));
        private static readonly IdFactory Ids = new IdFactory(0x63616E63656CUL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 6UL));

        private static CompositionHost NewHost(int queued = 16)
        {
            TestManifestSource source = new TestManifestSource();
            CompositionHostSettings settings = new CompositionHostSettings(
                new ControlLaneCapacitySettings(queued, 16),
                new OperationExpirySettings(3, 0UL));
            return new CompositionHost(World, Root, settings, source, null, PropagationMode.Automatic);
        }

        private static OperationIssuer Issuer(ulong domain) => new OperationIssuer(World, new Id128(domain, 1UL));

        [Test]
        public void CancellationRetransmissionCoalescesToTheOriginalOutcomeWithoutRepeatingTheCutoff()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6331UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);
            OperationId cancellation = issuer.Next();

            Assert.That(host.Cancel(cancellation, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            OperationStatusHandle handle = host.OperationLedger.RowOf(cancellation)!.Handle;
            Assert.That(host.Read(handle).Outcome, Is.EqualTo(OperationReadOutcome.Found), "An admitted cancellation attempt is retrievable (P-050).");
            Assert.That(host.Read(handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(host.Read(handle).Entry!.Code, Is.EqualTo(DiagnosticCode.Cancelled));

            // Retransmission of the same request with the same target: the recorded outcome, nothing re-executed.
            Assert.That(host.Cancel(cancellation, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.OperationLedger.CancelRequestCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.CancelRetransmissionCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.CancelledCount, Is.EqualTo(1), "The cutoff is applied exactly once.");

            // The other pending proposal is untouched and still publishable in admission order.
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Drain().Count, Is.EqualTo(1));
            Assert.That(host.FindScope(second), Is.Not.Null);
            Assert.That(host.FindScope(first), Is.Null);

            // A coalesced retransmission also ignores the world moving on: the recorded result still stands.
            Assert.That(host.Cancel(cancellation, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.OperationLedger.TooLateCount, Is.EqualTo(0));
            Assert.That(host.OperationLedger.CancelRetransmissionCount, Is.EqualTo(2));
        }

        [Test]
        public void ConflictingCancellationReuseKeepsTheOriginalAndLeavesTheSecondTargetUntouched()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6332UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);
            OperationId cancellation = issuer.Next();

            Assert.That(host.Cancel(cancellation, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            OperationStatusHandle handle = host.OperationLedger.RowOf(cancellation)!.Handle;

            // The same request identity aimed at a different target is a conflicting reuse (P-050).
            Assert.That(host.Cancel(cancellation, pendingSecond.Handle.Operation), Is.EqualTo(CancelOutcome.IdempotencyConflict));

            Assert.That(host.OperationLedger.CancelConflictCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.CancelledCount, Is.EqualTo(1), "The second target is not cancelled.");
            Assert.That(host.OperationLedger.LastConflictCode, Is.EqualTo(DiagnosticCode.IdempotencyConflict));

            // The original row is kept, not overwritten by the conflicting input.
            OperationLedgerEntry original = host.Read(handle).Entry!;
            Assert.That(original.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(original.Code, Is.EqualTo(DiagnosticCode.Cancelled));
            Assert.That(original.InputHash, Is.EqualTo(OperationLedger.CancellationInputHash(pendingFirst.Handle.Operation)));

            // Both targets keep their own outcome: the first is cancelled, the second publishes.
            Assert.That(host.Read(pendingFirst.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Drain().Count, Is.EqualTo(1));
            Assert.That(host.FindScope(second), Is.Not.Null);
            Assert.That(host.FindScope(first), Is.Null);
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void ReusingAnEditIdentityAsACancellationIsAConflictThatCancelsNothing()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6333UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);

            // One operation id names one operation: the edit id cannot become a cancellation identity.
            Assert.That(host.Cancel(pendingFirst.Handle.Operation, pendingSecond.Handle.Operation), Is.EqualTo(CancelOutcome.IdempotencyConflict));
            Assert.That(host.OperationLedger.CancelConflictCount, Is.EqualTo(1));
            Assert.That(host.Read(pendingFirst.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending), "The edit row is untouched.");
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Drain().Count, Is.EqualTo(2));
        }

        [Test]
        public void UnadmittedCancellationRequestIsRejectedAndChangesNothing()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6334UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);

            // bad session: the request itself belongs to another world incarnation (P-004)
            OperationId foreign = new OperationId(OtherWorld, issuer.Id, 900UL);
            Assert.That(host.Cancel(foreign, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Rejected));
            Assert.That(host.Read(foreign).Outcome, Is.EqualTo(OperationReadOutcome.Unknown), "A refused request leaves no ledger row.");

            // bad issuer: a default identity is not an issuer (P-004)
            OperationId ownerless = new OperationId(World, default(Id128), 901UL);
            Assert.That(host.Cancel(ownerless, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Rejected));

            // cross-world target: this lane can never decide another world's operation
            OperationId crossWorld = new OperationId(World, issuer.Id, 902UL);
            Assert.That(host.Cancel(crossWorld, new OperationId(OtherWorld, issuer.Id, 1UL)), Is.EqualTo(CancelOutcome.Rejected));

            // self-target: the row it would cancel is the request itself
            OperationId selfTarget = new OperationId(World, issuer.Id, 903UL);
            Assert.That(host.Cancel(selfTarget, selfTarget), Is.EqualTo(CancelOutcome.Rejected));

            Assert.That(host.OperationLedger.CancelRejectedCount, Is.EqualTo(4));
            Assert.That(host.OperationLedger.RowCount, Is.EqualTo(2), "Only the two admitted edits are on the lane.");

            // No target moved, and every refused identity is still reusable as a first submission.
            Assert.That(host.Read(pendingFirst.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Cancel(ownerless, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Rejected));

            Assert.That(host.Drain().Count, Is.EqualTo(2));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void CancellationRequestWithoutRemainingLaneCapacityIsRejectedAndDecidesNothing()
        {
            CompositionHost host = NewHost(queued: 2);
            OperationIssuer issuer = Issuer(0x63616E6335UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);
            Assert.That(host.OperationLedger.RowCount, Is.EqualTo(2), "The lane is exactly full.");

            Assert.That(host.Cancel(issuer.Next(), pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Rejected));
            Assert.That(host.OperationLedger.CancelRejectedCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.CapacityRejectedCount, Is.EqualTo(1));
            Assert.That(host.OperationLedger.RowCount, Is.EqualTo(2), "A refused request is not queued.");

            Assert.That(host.Read(pendingFirst.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(host.Drain().Count, Is.EqualTo(2));
        }

        [Test]
        public void CancellationRequestAtAnAlreadyUsedIssuerSequenceIsRejected()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6336UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            // Sequences 1 and 3 are used, so sequence 2 is an absent id below the issuer high-water mark.
            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.At(1UL), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.At(3UL), CompositionRevision.Zero);

            Assert.That(host.Cancel(issuer.At(2UL), pendingSecond.Handle.Operation), Is.EqualTo(CancelOutcome.Rejected));
            Assert.That(host.OperationLedger.CancelRejectedCount, Is.EqualTo(1));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending));

            // The refusal consumed nothing: a strictly increasing request is still a first submission.
            Assert.That(host.Cancel(issuer.At(4UL), pendingSecond.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(host.Drain().Count, Is.EqualTo(1));
            Assert.That(host.FindScope(first), Is.Not.Null);
            Assert.That(host.FindScope(second), Is.Null);
        }

        [Test]
        public void AdmittedCancellationBindsItsIdentityAndKeepsTheIssuerSequenceInStep()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6337UL);
            ScopeId first = Ids.Scope();

            EditAdmission pending = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.At(1UL), CompositionRevision.Zero);
            OperationId cancellation = issuer.At(2UL);
            Assert.That(host.Cancel(cancellation, pending.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.OperationLedger.RowOf(cancellation), Is.Not.Null, "An admitted cancellation occupies its operation identity.");

            // That identity is now bound to the cancellation request, so an edit cannot claim it (P-050).
            EditAdmission reuse = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), cancellation, CompositionRevision.Zero);
            Assert.That(reuse.Kind, Is.EqualTo(AdmissionKind.IdempotencyConflict));
            Assert.That(reuse.Code, Is.EqualTo(DiagnosticCode.IdempotencyConflict));
            Assert.That(host.Read(host.OperationLedger.RowOf(cancellation)!.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled), "The recorded cancellation result stands.");

            // The next sequence from the same issuer is an ordinary first submission.
            EditAdmission next = host.SubmitEdit(Payloads.ScopeCreate(Ids.Scope(), Root), issuer.At(3UL), CompositionRevision.Zero);
            Assert.That(next.Staged, Is.True, next.Code.ToString());
            Assert.That(host.OperationLedger.CancelRequestCount, Is.EqualTo(1));
            Assert.That(host.Drain().Count, Is.EqualTo(1));
        }

        [Test]
        public void CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6338UL);
            ScopeId scope = Ids.Scope();

            EditAdmission pending = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), issuer.Next(), CompositionRevision.Zero);

            // Hold the target exactly at the cutoff: inside publication, before the swap (P-051).
            Assert.That(host.OperationLedger.BeginApplying(pending.Handle.Operation), Is.True);

            OperationId cancellation = issuer.Next();
            Assert.That(host.Cancel(cancellation, pending.Handle.Operation), Is.EqualTo(CancelOutcome.TooLate));

            OperationStatusHandle handle = host.OperationLedger.RowOf(cancellation)!.Handle;
            Assert.That(host.Read(handle).Entry!.Outcome, Is.EqualTo(Outcome.Rejected), "A too-late cancellation made no write (00 s9).");
            Assert.That(host.Read(handle).Entry!.Code, Is.EqualTo(DiagnosticCode.TooLate));
            Assert.That(host.OperationLedger.TooLateCount, Is.EqualTo(1));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));
        }

        [Test]
        public void CancellationOfAnUnknownTargetIsRejectedAgainstThatHandle()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6339UL);

            OperationId unknownTarget = new OperationId(World, issuer.Id, 42UL);
            OperationId cancellation = issuer.Next();

            Assert.That(host.Cancel(cancellation, unknownTarget), Is.EqualTo(CancelOutcome.Unknown));

            OperationStatusHandle handle = host.OperationLedger.RowOf(cancellation)!.Handle;
            Assert.That(host.Read(handle).Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(host.Read(handle).Entry!.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(host.Read(unknownTarget).Outcome, Is.EqualTo(OperationReadOutcome.Unknown));
        }

        [Test]
        public void TheConformanceProbeForCancellationIdentityNowResolvesCorrectly()
        {
            // The permanent form of the probe recorded in artifacts/gc-004/cancellation-seam-blocker.log:
            // two independent pending edits, one cancellation identity, then the same identity aimed at the
            // second target. The recorded probe observed "Cancelled" for the conflicting call and "Unknown" for
            // the cancellation's own status; both are wrong under P-050.
            CompositionHost host = NewHost();
            OperationIssuer issuer = Issuer(0x63616E6341UL);
            ScopeId first = Ids.Scope();
            ScopeId second = Ids.Scope();

            EditAdmission pendingFirst = host.SubmitEdit(Payloads.ScopeCreate(first, Root), issuer.Next(), CompositionRevision.Zero);
            EditAdmission pendingSecond = host.SubmitEdit(Payloads.ScopeCreate(second, Root), issuer.Next(), CompositionRevision.Zero);
            OperationId cancellationId = issuer.Next();

            Assert.That(host.Cancel(cancellationId, pendingFirst.Handle.Operation), Is.EqualTo(CancelOutcome.Cancelled));
            Assert.That(host.Cancel(cancellationId, pendingSecond.Handle.Operation), Is.EqualTo(CancelOutcome.IdempotencyConflict));

            Assert.That(host.Read(pendingFirst.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Cancelled));
            Assert.That(host.Read(pendingSecond.Handle).Entry!.Outcome, Is.EqualTo(Outcome.Pending), "The second target must stay pending.");
            Assert.That(host.Read(host.OperationLedger.RowOf(cancellationId)!.Handle).Outcome, Is.EqualTo(OperationReadOutcome.Found), "The cancellation attempt must be retrievable.");

            // The second edit still publishes, so the conflict cost the world nothing but the refusal.
            Assert.That(host.Drain().Count, Is.EqualTo(1));
            Assert.That(host.FindScope(second), Is.Not.Null);
            Assert.That(host.FindScope(first), Is.Null);
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void CancellationInputHashIsDomainSeparatedFromEditInputs()
        {
            ScopeId scope = Ids.Scope();
            OperationId operation = new OperationId(World, new Id128(0x63616E633AUL, 1UL), 7UL);

            ContentHash cancellationHash = OperationLedger.CancellationInputHash(operation);
            ContentHash editHash = CompositionEditApplier.InputHashOf(CompositionEditCodec.Encode(Payloads.ScopeCreate(scope, Root)));

            Assert.That(cancellationHash, Is.EqualTo(OperationLedger.CancellationInputHash(operation)), "The same target always hashes the same.");
            Assert.That(cancellationHash, Is.Not.EqualTo(editHash));

            OperationId otherTarget = new OperationId(World, operation.IssuerId, 8UL);
            Assert.That(OperationLedger.CancellationInputHash(otherTarget), Is.Not.EqualTo(cancellationHash), "A different target is a different input.");

            OperationId sameSequenceOtherWorld = new OperationId(OtherWorld, operation.IssuerId, 7UL);
            Assert.That(OperationLedger.CancellationInputHash(sameSequenceOtherWorld), Is.Not.EqualTo(cancellationHash), "A cross-world target is a different input.");
        }
    }
}
