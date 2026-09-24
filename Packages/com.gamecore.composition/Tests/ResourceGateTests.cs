// GameCore.Composition tests — inert managed resource gates and teardown (P-007, P-029, P-047, P-048, TEST-015).
//
// The fixtures drive real staged acquisitions through the control lane's `StageResource` path, so a test proves
// the observable statement: nothing is usable before publication, a closed gate drops late work, and a resource
// whose release failed stays retained instead of being reported as disposed.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ResourceGateTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 5UL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 5UL));

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                Manifests = new TestManifestSource();
                Factory = new TestResourceFactory(Ids);
                Host = CompositionHost.CreateDefault(World, Root, Manifests, Factory);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
                Callbacks = Host.Callbacks;
            }

            public IdFactory Ids { get; }

            public TestManifestSource Manifests { get; }

            public TestResourceFactory Factory { get; }

            public CompositionHost Host { get; }

            public OperationIssuer Issuer { get; }

            public CallbackGate Callbacks { get; }

            public PluginManifest Manifest { get; private set; } = null!;

            public PluginTypeId Type { get; private set; }

            public void UseManifest(PluginTypeId type, PluginManifest manifest)
            {
                Type = type;
                Manifest = manifest;
                Manifests.Add(manifest, null);
            }

            public OperationId BeginMount(PluginInstanceId instance, ScopeId scope)
            {
                OperationId operation = Issuer.Next();
                EditAdmission admission = Host.SubmitEdit(Payloads.Mount(Manifest, instance, scope, null), operation, Host.Snapshot().Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                return operation;
            }

            public OperationId Mount(PluginInstanceId instance, ScopeId scope)
            {
                OperationId operation = BeginMount(instance, scope);
                Host.Drain();
                return operation;
            }
        }

        private static AsyncWorkToken TokenFor(OperationId operation, PluginInstanceId instance, ulong generation, ulong epoch) =>
            new AsyncWorkToken(operation, instance, new InstallationGeneration(generation), new ActivationEpoch(epoch), 0U);

        [Test]
        public void PreparedLeaseStaysInertUntilPublication()
        {
            Rig rig = new Rig(0x7265737631UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            ResourceKey resource = rig.Ids.Resource();
            OperationId operation = rig.BeginMount(instance, Root);

            Assert.That(
                rig.Host.StageResource(operation, instance, resource, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode code),
                Is.True,
                code.ToString());

            IReadOnlyList<StagedLease> staged = rig.Host.StagedLeases(operation);
            Assert.That(staged.Count, Is.EqualTo(1));
            Assert.That(staged[0].Readiness, Is.EqualTo(ResourceReadiness.Pending), "A staged lease is not ready (P-029).");
            Assert.That(rig.Factory.Leases[0].Gate.IsOpen, Is.False, "A prepared callback sits behind a closed gate.");
            Assert.That(rig.Host.Resources.RetainedResourceIds().Count, Is.EqualTo(1));

            // A callback that arrives now is dropped rather than delivered (P-029, P-047).
            AsyncWorkToken token = TokenFor(operation, instance, 1UL, 1UL);
            CallbackGateDecision decision = GatedCallbackPath.Evaluate(rig.Factory.Leases[0], rig.Callbacks, token);
            Assert.That(decision, Is.EqualTo(CallbackGateDecision.DiscardPostPublicationFence));
            Assert.That(((ManagedResourceGate)rig.Factory.Leases[0].Gate).DroppedDispatches, Is.EqualTo(1));
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0));

            rig.Host.Drain();

            Assert.That(rig.Factory.Leases[0].Gate.IsOpen, Is.True, "Publication is the only moment the gate opens (P-030).");
            Assert.That(rig.Factory.Leases[0].Readiness, Is.EqualTo(ResourceReadiness.Ready));
            Assert.That(rig.Host.ResourceGatesOpened, Is.EqualTo(1));
            Assert.That(rig.Host.Resources.RetainedResourceIds().Count, Is.EqualTo(1));

            // Now the activation exists, so dispatch reaches the callback gate and is accepted.
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(1));
            AsyncWorkToken live = TokenFor(operation, instance, 1UL, 1UL);
            Assert.That(GatedCallbackPath.Evaluate(rig.Factory.Leases[0], rig.Callbacks, live), Is.EqualTo(CallbackGateDecision.Dispatch));
        }

        [Test]
        public void CancellingAStagedOperationReleasesItsGatedLeases()
        {
            Rig rig = new Rig(0x7265737632UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId operation = rig.BeginMount(instance, Root);

            Assert.That(rig.Host.StageResource(operation, instance, rig.Ids.Resource(), new FrozenPayload(new byte[] { 2 }), null, out DiagnosticCode code), Is.True, code.ToString());
            Assert.That(rig.Host.Cancel(rig.Issuer.Next(), operation), Is.EqualTo(CancelOutcome.Cancelled));

            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(1), "A cancelled operation releases its staged resources (P-029).");
            Assert.That(rig.Factory.Leases[0].Gate.IsOpen, Is.False, "A gate closed for a cancelled operation never opens.");
            Assert.That(rig.Host.Resources.RetainedResourceIds(), Is.Empty);
            Assert.That(rig.Host.StagedLeases(operation), Is.Empty);
            Assert.That(rig.Host.Drain(), Is.Empty);
        }

        [Test]
        public void FailedPreparationReleasesEarlierStagedAcquisitions()
        {
            Rig rig = new Rig(0x7265737633UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId operation = rig.BeginMount(instance, Root);

            ResourceKey first = rig.Ids.Resource();
            Assert.That(rig.Host.StageResource(operation, instance, first, new FrozenPayload(new byte[] { 3 }), null, out DiagnosticCode code), Is.True, code.ToString());

            rig.Factory.FailNextPrepare = true;
            ResourceKey second = rig.Ids.Resource();
            bool staged = rig.Host.StageResource(operation, instance, second, new FrozenPayload(new byte[] { 4 }), null, out DiagnosticCode failureCode);

            Assert.That(staged, Is.False);
            Assert.That(failureCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(1), "The earlier staged acquisition is released in reverse order (P-029).");
            Assert.That(rig.Host.Resources.RetainedResourceIds(), Is.Empty);
        }

        [Test]
        public void RetiringAnInstanceDisposesEachLeaseOnceInReverseAcquisitionOrder()
        {
            Rig rig = new Rig(0x7265737634UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            ResourceLedger ledger = new ResourceLedger();
            ResourcePreparationSet set = new ResourcePreparationSet(rig.Factory, ledger, World, instance);
            OperationId operation = rig.Issuer.Next();

            for (int i = 0; i < 3; i++)
            {
                Assert.That(
                    set.TryPrepare(
                        rig.Ids.Resource(),
                        TokenFor(operation, instance, 1UL, 1UL),
                        new FrozenPayload(new byte[] { (byte)i }),
                        null,
                        out Id128 leaseId,
                        out DiagnosticCode code),
                    Is.True,
                    code.ToString());
                _ = leaseId;
            }

            Assert.That(set.PublishReady(), Is.EqualTo(3));
            CleanupReport report = set.ReleaseStaged();

            Assert.That(report.Retired.Count, Is.EqualTo(3));
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(3));
            Assert.That(rig.Factory.Leases[2].IsDisposed, Is.True);
            Assert.That(rig.Factory.Leases[2].DisposeCount, Is.EqualTo(1));

            // Disposing twice is neither attempted nor reported as a second retirement (P-048).
            Assert.That(ledger.Retire(rig.Factory.Leases[2].LeaseId), Is.False);
            Assert.That(rig.Factory.Leases[2].DisposeCount, Is.EqualTo(1));
            Assert.That(ledger.RetainedResourceIds(), Is.Empty);
        }

        [Test]
        public void FailedReleaseQuarantinesTheResourceInsteadOfReportingADisposal()
        {
            Rig rig = new Rig(0x7265737635UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            ResourceKey resource = rig.Ids.Resource();
            rig.Factory.FailingDisposals.Add(resource.Value);

            OperationId operation = rig.BeginMount(instance, Root);
            Assert.That(rig.Host.StageResource(operation, instance, resource, new FrozenPayload(new byte[] { 5 }), null, out DiagnosticCode code), Is.True, code.ToString());
            rig.Host.Drain();

            ResourceLedger ledger = rig.Host.Resources;
            bool retired = ledger.Retire(rig.Factory.Leases[0].LeaseId);

            Assert.That(retired, Is.False, "A release that throws is not reported as a successful retirement.");
            Assert.That(ledger.FailedReleaseCount, Is.EqualTo(1));
            Assert.That(ledger.QuarantinedCount, Is.EqualTo(1));
            Assert.That(ledger.RetainedResourceIds().Count, Is.EqualTo(1), "A quarantined resource stays tracked (P-048).");
            Assert.That(rig.Factory.Leases[0].Readiness, Is.EqualTo(ResourceReadiness.Failed));

            // The quarantine is only released once the caller knows its users ended (P-048).
            Assert.That(ledger.ReleaseQuarantine(rig.Factory.Leases[0].LeaseId), Is.True);
            Assert.That(ledger.Retire(rig.Factory.Leases[0].LeaseId), Is.False);
            Assert.That(ledger.RetainedResourceIds().Count, Is.EqualTo(1), "Elapsed time never authorizes a free; the retry is explicit.");
        }

        [Test]
        public void UnmountPublishesCleanupErrorsWithRetainedReferences()
        {
            Rig rig = new Rig(0x7265737636UL);
            PluginTypeId type = rig.Ids.Type();
            rig.UseManifest(type, Manifests.Plain(type, rig.Ids));
            PluginInstanceId instance = rig.Ids.Instance();
            ResourceKey resource = rig.Ids.Resource();
            OperationId mount = rig.BeginMount(instance, Root);
            Assert.That(rig.Host.StageResource(mount, instance, resource, new FrozenPayload(new byte[] { 7 }), null, out DiagnosticCode code), Is.True, code.ToString());
            rig.Host.Drain();

            rig.Factory.FailingDisposals.Add(resource.Value);
            OperationId unmount = rig.Issuer.Next();
            EditAdmission admission = rig.Host.SubmitEdit(Payloads.Unmount(instance), unmount, rig.Host.Snapshot().Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());

            IReadOnlyList<PublishedOperation> published = rig.Host.Drain();

            Assert.That(published.Count, Is.EqualTo(1));
            Assert.That(published[0].Outcome, Is.EqualTo(Outcome.PublishedWithCleanupErrors), "A cleanup failure after publication cannot roll it back (P-048).");
            Assert.That(published[0].Cleanup!.Failed.Count, Is.EqualTo(1));
            Assert.That(rig.Host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Disposed));
            Assert.That(rig.Host.Callbacks.LiveActivationCount, Is.EqualTo(0), "A removed installation's activation is retired (P-047).");

            OperationResult? result = rig.Host.ResultOf(unmount);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Outcome, Is.EqualTo(Outcome.PublishedWithCleanupErrors));
            Assert.That(result!.QuarantineReferences.Count, Is.EqualTo(1), "A failed release is reported as a retained quarantine (P-048).");
        }

        [Test]
        public void RetirementRunsConsumersBeforeTheirProviders()
        {
            Rig rig = new Rig(0x7265737637UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();
            rig.Manifests.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(new ContractRef(contract, 1U), rig.Ids.NextId()) }), null);
            rig.Manifests.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(new ContractRef(contract, 1U)) }), null);

            ScopeId branch = rig.Ids.Scope();
            EditAdmission scope = rig.Host.SubmitEdit(Payloads.ScopeCreate(branch, Root), rig.Issuer.Next(), CompositionRevision.Zero);
            Assert.That(scope.Staged, Is.True, scope.Code.ToString());
            rig.Host.Drain();

            PluginInstanceId provider = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            ResourceKey providerResource = rig.Ids.Resource();
            ResourceKey consumerResource = rig.Ids.Resource();

            OperationId providerMount = rig.Issuer.Next();
            Assert.That(rig.Host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(providerType), provider, branch, null), providerMount, rig.Host.Snapshot().Revision).Staged, Is.True);
            Assert.That(rig.Host.StageResource(providerMount, provider, providerResource, new FrozenPayload(new byte[] { 8 }), null, out DiagnosticCode one), Is.True, one.ToString());
            rig.Host.Drain();

            OperationId consumerMount = rig.Issuer.Next();
            Assert.That(rig.Host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, branch, null), consumerMount, rig.Host.Snapshot().Revision).Staged, Is.True);
            Assert.That(rig.Host.StageResource(consumerMount, consumer, consumerResource, new FrozenPayload(new byte[] { 9 }), null, out DiagnosticCode two), Is.True, two.ToString());
            rig.Host.Drain();

            Assert.That(rig.Host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.Active), "The consumer must bind before teardown ordering can be observed.");

            // Destroying the subtree removes both installations in one publication.
            EditAdmission remove = rig.Host.SubmitEdit(Payloads.ScopeRemove(branch, true), rig.Issuer.Next(), rig.Host.Snapshot().Revision);
            Assert.That(remove.Staged, Is.True, remove.Code.ToString());
            rig.Host.Drain();

            Assert.That(rig.Factory.ConsumerDisposedBeforeProvider(consumerResource.Value, providerResource.Value), Is.True, "Consumers retire before their providers (P-012, P-048).");
            Assert.That(rig.Host.Resources.RetainedCountFor(consumer), Is.EqualTo(0));
            Assert.That(rig.Host.Resources.RetainedCountFor(provider), Is.EqualTo(0));
        }
    }
}
