// GameCore.Composition.Tests.Diagnostics — staged-operation status versus published state (GC-016).
//
// O-25 requires the two to be distinguished "explicitly". These cases drive a real `CompositionHost` and read a real
// `StagedOperationStatus` before publication, after publication, for a refusal and after the result window closed,
// so the distinction is proven against the lane's own ledger rather than a modelled flag.
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Composition.Diagnostics;
using NUnit.Framework;

namespace GameCore.Composition.Tests.Diagnostics
{
    [TestFixture]
    public sealed class StagedOperationStatusTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x4743303136535447UL, 1UL));

        private static readonly ScopeId Root = new ScopeId(new Id128(0x4743303136524F4FUL, 1UL));

        private static CompositionHost NewHost(ulong retainedSteps = 0UL)
        {
            var source = new TestManifestSource();
            var settings = new CompositionHostSettings(
                new ControlLaneCapacitySettings(16, 16),
                new OperationExpirySettings(3, retainedSteps));
            return new CompositionHost(World, Root, settings, source, null, PropagationMode.Automatic);
        }

        [Test]
        public void AStagedProposalIsReportedAsStagedAndNeverAsPublishedState()
        {
            CompositionHost host = NewHost();
            var reader = new StagedStatusReader(host);
            var ids = new IdFactory(0x4743303136535449UL);
            var issuer = new OperationIssuer(World, new Id128(0x4743303136495355UL, 1UL));
            ScopeId scope = ids.Scope();
            OperationId operation = issuer.Next();

            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(scope, Root), operation, host.Snapshot().Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());

            StagedOperationStatus staged = reader.Read(operation);
            Assert.That(staged.Source, Is.EqualTo(StagedStatusSource.StagedPlan));
            Assert.That(staged.HasStagedPlan, Is.True);
            Assert.That(staged.StagedIsNoChange, Is.False);
            Assert.That(staged.StagedPlanHash.IsEmpty, Is.False, "The staged proposal has a semantic hash (05 s4).");
            Assert.That(staged.StagedCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(staged.StagedActivationCount, Is.Zero);
            Assert.That(staged.StagedRetirementCount, Is.Zero);
            Assert.That(staged.StagedResourceLeaseCount, Is.Zero);
            Assert.That(staged.ReadOutcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(staged.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(staged.IsTerminal, Is.False);
            Assert.That(staged.PublishedSnapshot.HasValue, Is.False, "Nothing has published for this operation yet.");
            Assert.That(staged.DescribesPublishedState, Is.False, "A staged proposal is never world observation (00 s9).");
            Assert.That(staged.Detail, Does.Contain("staged"));
            Assert.That(reader.ReadCount, Is.EqualTo(1));
            Assert.That(reader.StagedCount, Is.EqualTo(1));

            host.Drain();

            StagedOperationStatus published = reader.Read(operation);
            Assert.That(published.Source, Is.EqualTo(StagedStatusSource.PublishedComposition));
            Assert.That(published.HasStagedPlan, Is.False, "Once published the proposal is no longer staged.");
            Assert.That(published.Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(published.IsTerminal, Is.True);
            Assert.That(published.PublishedRevision.Value, Is.EqualTo(1UL));
            Assert.That(published.PublishedEpoch.Value, Is.EqualTo(1UL));
            Assert.That(published.PublishedSnapshot.HasValue, Is.True);
            Assert.That(published.PublishedSnapshot!.Value.LogicalStepId, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(published.DescribesPublishedState, Is.True);
            Assert.That(published.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(published.ToString(), Does.Contain("PublishedComposition"));
            Assert.That(reader.StagedCount, Is.EqualTo(1), "Only the pre-publication read was staged.");
        }

        [Test]
        public void ARefusedOperationReportsItsOwnCodeAndNoPublishedImage()
        {
            CompositionHost host = NewHost();
            var reader = new StagedStatusReader(host);
            var ids = new IdFactory(0x4743303136524546UL);
            var issuer = new OperationIssuer(World, new Id128(0x4743303136495355UL, 2UL));
            PluginManifest manifest = Manifests.Plain(ids.Type(), ids);
            PluginInstanceId instance = ids.Instance();
            ScopeId unknownScope = ids.Scope();
            OperationId operation = issuer.Next();

            // Mounting at a scope the committed tree does not contain is a pre-publication refusal with real
            // diagnostics; nothing is written and the old visible revision stands (P-009, P-028).
            EditAdmission admission = host.SubmitEdit(
                Payloads.Mount(manifest, instance, unknownScope, null), operation, host.Snapshot().Revision);

            Assert.That(admission.Rejected, Is.True, admission.Code.ToString());
            Assert.That(admission.Code, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(admission.Plan, Is.Not.Null);
            Assert.That(admission.Plan!.Diagnostics.Count, Is.GreaterThan(0), "A rejection names its reasons (P-052).");
            Assert.That(host.FindInstall(instance), Is.Null, "A refused mount never registers an installation (P-012).");

            StagedOperationStatus refused = reader.Read(operation);
            Assert.That(refused.Source, Is.Not.EqualTo(StagedStatusSource.StagedPlan),
                "A refused plan is settled immediately, so it is never reported as still staged.");
            Assert.That(refused.HasStagedPlan, Is.False);
            Assert.That(refused.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(refused.Code, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(refused.IsTerminal, Is.True);
            Assert.That(refused.PublishedSnapshot.HasValue, Is.False);
            Assert.That(refused.DescribesPublishedState, Is.False);
            Assert.That(refused.PublishedEpoch.Value, Is.EqualTo(host.Snapshot().Epoch.Value),
                "A rejection keeps the visible epoch it was checked against (P-029).");
        }

        [Test]
        public void AnUnknownOperationIsDistinctFromAnExpiredOne()
        {
            CompositionHost host = NewHost(retainedSteps: 1UL);
            var reader = new StagedStatusReader(host);
            var ids = new IdFactory(0x47433031364558UL);
            var issuer = new OperationIssuer(World, new Id128(0x4743303136495355UL, 3UL));

            StagedOperationStatus unknown = reader.Read(issuer.Next());
            Assert.That(unknown.ReadOutcome, Is.EqualTo(OperationReadOutcome.Unknown));
            Assert.That(unknown.Source, Is.EqualTo(StagedStatusSource.None));
            Assert.That(unknown.HasStagedPlan, Is.False);
            Assert.That(unknown.DescribesPublishedState, Is.False);
            Assert.That(unknown.Detail, Does.Contain("never admitted"));
            Assert.That(reader.UnknownCount, Is.EqualTo(1));

            OperationId published = issuer.Next();
            host.SubmitEdit(Payloads.ScopeCreate(ids.Scope(), Root), published, host.Snapshot().Revision);
            host.Drain();

            Assert.That(reader.Read(published).ReadOutcome, Is.EqualTo(OperationReadOutcome.Found));
            Assert.That(host.AdvanceSteps(new LogicalStepId(5UL)), Is.EqualTo(1),
                "The lane's retention window closes one step later (P-050).");

            StagedOperationStatus expired = reader.Read(published);
            Assert.That(expired.ReadOutcome, Is.EqualTo(OperationReadOutcome.Expired));
            Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(expired.CodeText, Is.EqualTo("ResultExpired"));
            Assert.That(expired.Source, Is.EqualTo(StagedStatusSource.None));
            Assert.That(expired.DescribesPublishedState, Is.False);
            Assert.That(expired.Detail, Does.Contain("window has closed"));
            Assert.That(reader.ExpiredCount, Is.EqualTo(1));
            Assert.That(reader.ReadCount, Is.EqualTo(3));
            Assert.That(reader.ToString(), Does.Contain("reads=3"));
        }

        [Test]
        public void AHandleReadResolvesTheSameStatusAsTheIdentityRead()
        {
            CompositionHost host = NewHost();
            var reader = new StagedStatusReader(host);
            var ids = new IdFactory(0x4743303136484E44UL);
            var issuer = new OperationIssuer(World, new Id128(0x4743303136495355UL, 4UL));
            OperationId operation = issuer.Next();

            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(ids.Scope(), Root), operation, host.Snapshot().Revision);
            StagedOperationStatus byIdentity = reader.Read(operation);
            StagedOperationStatus byHandle = reader.Read(admission.Handle);

            Assert.That(byHandle.Operation.Equals(byIdentity.Operation), Is.True);
            Assert.That(byHandle.Source, Is.EqualTo(byIdentity.Source));
            Assert.That(byHandle.StagedPlanHash, Is.EqualTo(byIdentity.StagedPlanHash));
            Assert.That(byHandle.HasStagedPlan, Is.EqualTo(byIdentity.HasStagedPlan));
            Assert.That(ReferenceEquals(reader.Lane, host), Is.True);
            Assert.Throws<ArgumentNullException>(() => new StagedStatusReader(null!));
        }
    }
}
