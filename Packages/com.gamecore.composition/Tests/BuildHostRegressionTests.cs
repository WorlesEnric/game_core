// Regressions discovered by the first Linux build and conformance review.
#nullable enable
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class BuildHostRegressionTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x6275696c64UL, 1UL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726f6f74UL, 99UL));
        private IdFactory ids = null!;
        private TestManifestSource manifests = null!;
        private TestResourceFactory factory = null!;
        private CompositionHost host = null!;
        private OperationIssuer issuer = null!;

        [SetUp]
        public void SetUp()
        {
            ids = new IdFactory(0x72656772657373UL);
            manifests = new TestManifestSource();
            factory = new TestResourceFactory(ids);
            host = CompositionHost.CreateDefault(World, Root, manifests, factory);
            issuer = new OperationIssuer(World, ids.NextId());
        }

        private EditAdmission Submit(CompositionEditPayload payload) =>
            host.SubmitEdit(payload, issuer.Next(), host.Snapshot().Revision);

        private EditAdmission Mount(PluginManifest manifest, PluginInstanceId instance)
        {
            manifests.Add(manifest, null);
            EditAdmission admission = Submit(Payloads.Mount(manifest, instance, Root, null));
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            host.Drain();
            return admission;
        }

        private AsyncWorkToken Token(PluginInstanceId instance, OperationId operation)
        {
            InstallRecord record = host.FindInstall(instance)!.Record;
            return new AsyncWorkToken(operation, instance, record.Generation, record.ActivationEpoch, 0U);
        }

        [Test]
        public void SuspensionClosesAuthorityAndExplicitResumeUsesAFreshActivation()
        {
            PluginInstanceId instance = ids.Instance();
            EditAdmission mount = Mount(Manifests.Plain(ids.Type(), ids), instance);
            AsyncWorkToken old = Token(instance, mount.Handle.Operation);
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.Dispatch));

            EditAdmission suspend = Submit(Payloads.Suspend(instance));
            Assert.That(suspend.Staged, Is.True, suspend.Code.ToString());
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Suspended));
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.DiscardRetiredRoute));

            EditAdmission resume = Submit(Payloads.Resume(instance));
            Assert.That(resume.Staged, Is.True, resume.Code.ToString());
            Assert.That(host.StageResource(resume.Handle.Operation, instance, ids.Resource(), new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode code), Is.True, code.ToString());
            host.Drain();
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Active));
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.DiscardStaleActivation));
            Assert.That(GatedCallbackPath.Evaluate(factory.Leases[0], host.Callbacks, factory.Leases[0].Token), Is.EqualTo(CallbackGateDecision.Dispatch));
        }

        [Test]
        public void RequiredProviderLossRetractsTheWholeChainAndReturnReactivatesIt()
        {
            ContractRef first = new ContractRef(ids.NextId(), 1U);
            ContractRef second = new ContractRef(ids.NextId(), 1U);
            PluginInstanceId consumer = ids.Instance(); // Canonical order deliberately opposes dependency order.
            PluginInstanceId middle = ids.Instance();
            PluginInstanceId provider = ids.Instance();
            PluginManifest leaf = Manifests.Plain(ids.Type(), ids, null, null, new[] { Manifests.Requires(second) });
            PluginManifest bridge = Manifests.Plain(ids.Type(), ids, null,
                new[] { Manifests.Export(second, ids.NextId()) }, new[] { Manifests.Requires(first) });
            PluginManifest source = Manifests.Plain(ids.Type(), ids, null, new[] { Manifests.Export(first, ids.NextId()) });
            Mount(leaf, consumer);
            Mount(bridge, middle);
            Assert.That(host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));
            EditAdmission mounted = Mount(source, provider);
            Assert.That(host.FindInstall(middle)!.State, Is.EqualTo(InstallationState.Active));
            Assert.That(host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.Active));
            AsyncWorkToken old = Token(consumer, mounted.Handle.Operation);

            EditAdmission removed = Submit(Payloads.Unmount(provider));
            Assert.That(removed.Staged, Is.True, removed.Code.ToString());
            host.Drain();
            Assert.That(host.FindInstall(middle)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(host.FindInstall(consumer)!.Bindings, Is.Empty);
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.DiscardRetiredRoute));

            Mount(source, provider);
            Assert.That(host.FindInstall(consumer)!.State, Is.EqualTo(InstallationState.Active));
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.DiscardStaleActivation));
        }

        [Test]
        public void TerminalRetransmissionKeepsItsOriginalHandle()
        {
            CompositionEditPayload payload = Payloads.ScopeCreate(ids.Scope(), Root);
            EditAdmission first = Submit(payload);
            host.Drain();
            EditAdmission retry = host.SubmitEdit(payload, first.Handle.Operation, CompositionRevision.Zero);
            Assert.That(retry.Kind, Is.EqualTo(AdmissionKind.Retransmission));
            Assert.That(retry.Handle, Is.EqualTo(first.Handle));
            Assert.That(host.Read(first.Handle).Entry!.Handle, Is.EqualTo(first.Handle));
            Assert.That(host.Drain(), Is.Empty);
        }

        [Test]
        public void DirectPublicationCannotOvertakeAnEarlierProposal()
        {
            ScopeId parent = ids.Scope();
            ScopeId child = ids.Scope();
            EditAdmission first = Submit(Payloads.ScopeCreate(parent, Root));
            EditAdmission second = Submit(Payloads.ScopeCreate(child, parent));
            Assert.That(host.Publish(second.Handle.Operation), Is.Null, "Publication must follow the serialized admission order.");
            Assert.That(host.FindScope(parent), Is.Null);
            Assert.That(host.Drain().Count, Is.EqualTo(2));
            Assert.That(host.FindScope(child)!.Parent, Is.EqualTo(parent));
            Assert.That(host.Read(first.Handle).Entry!.PublishedEpoch.Value, Is.EqualTo(1UL));
            Assert.That(host.Read(second.Handle).Entry!.PublishedEpoch.Value, Is.EqualTo(2UL));
        }

        [Test]
        public void ForeignWorldOperationCannotMutateThisScopeTree()
        {
            OperationId foreign = new OperationId(new WorldId(ids.NextId()), ids.NextId(), 1UL);
            EditAdmission admission = host.SubmitEdit(Payloads.ScopeCreate(ids.Scope(), Root), foreign, CompositionRevision.Zero);
            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(host.Drain(), Is.Empty);
            Assert.That(host.Snapshot().Scopes.Count, Is.EqualTo(1));
        }

        [Test]
        public void ForgottenAndUnseenLowerSequencesRemainExpired()
        {
            OperationLedger ledger = new OperationLedger(new ControlLaneCapacitySettings(4, 1), OperationExpirySettings.Default);
            ContentHash hash = ContentHash.Compute(new byte[] { 1 });
            OperationId first = issuer.At(2UL);
            for (ulong sequence = 2UL; sequence <= 4UL; sequence++)
            {
                OperationId operation = issuer.At(sequence);
                ledger.Admit(operation, hash, null);
                ledger.Settle(operation, Outcome.NoChange, DiagnosticCode.None, CompositionRevision.Zero, AssemblyEpoch.Zero, null, LogicalStepId.Zero);
            }

            Assert.That(ledger.IsExpired(first), Is.True, "Evicting a tombstone cannot turn an expired ID into unknown.");
            Assert.That(ledger.Admit(first, hash, null).Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(ledger.Admit(issuer.At(1UL), hash, null).Code, Is.EqualTo(DiagnosticCode.ResultExpired), "P-050 includes unseen out-of-order submissions.");
        }

        [Test]
        public void UnrelatedCleanupCannotChangeAnEarlierOperationResult()
        {
            PluginInstanceId instance = ids.Instance();
            PluginManifest manifest = Manifests.Plain(ids.Type(), ids);
            manifests.Add(manifest, null);
            EditAdmission mount = Submit(Payloads.Mount(manifest, instance, Root, null));
            ResourceKey resource = ids.Resource();
            Assert.That(host.StageResource(mount.Handle.Operation, instance, resource, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode code), Is.True, code.ToString());
            host.Drain();
            OperationResult original = host.ResultOf(mount.Handle.Operation)!;
            Assert.That(original.QuarantineReferences, Is.Empty);

            factory.FailingDisposals.Add(resource.Value);
            EditAdmission unmount = Submit(Payloads.Unmount(instance));
            host.Drain();
            Assert.That(host.ResultOf(unmount.Handle.Operation)!.QuarantineReferences.Count, Is.EqualTo(1));
            Assert.That(host.ResultOf(mount.Handle.Operation)!.QuarantineReferences, Is.Empty, "P-050 terminal results belong to their own attempt, not the current resource ledger.");
            Assert.That(host.FindInstall(instance)!.State, Is.EqualTo(InstallationState.Retiring), "P-048 forbids reporting Disposed while resources remain quarantined.");
        }

        [Test]
        public void ReconfigurationRetiresOnlyTheOldActivationResources()
        {
            PluginInstanceId instance = ids.Instance();
            Id128 field = ids.NextId();
            ConfigDocument defaults = ConfigDocument.Of(new ConfigField(field, ConfigFieldValue.OfUInt32(1U)));
            PluginManifest manifest = Manifests.Plain(ids.Type(), ids);
            manifests.Add(manifest, defaults);
            EditAdmission mount = Submit(Payloads.Mount(manifest, instance, Root, null, schemaDefaults: defaults));
            Assert.That(host.StageResource(mount.Handle.Operation, instance, ids.Resource(), new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode code), Is.True, code.ToString());
            host.Drain();
            AsyncWorkToken old = factory.Leases[0].Token;

            ConfigDocument patch = ConfigDocument.Of(new ConfigField(field, ConfigFieldValue.OfUInt32(2U)));
            EditAdmission changed = Submit(Payloads.Reconfigure(manifest, instance, patch, defaults, new DefinitionRevision(2UL), defaults));
            Assert.That(changed.Staged, Is.True, changed.Code.ToString());
            Assert.That(host.StageResource(changed.Handle.Operation, instance, ids.Resource(), new FrozenPayload(new byte[] { 2 }), null, out code), Is.True, code.ToString());
            Assert.That(GatedCallbackPath.Evaluate(factory.Leases[0], host.Callbacks, old), Is.EqualTo(CallbackGateDecision.Dispatch));
            host.Drain();
            Assert.That(factory.Leases[0].IsDisposed, Is.True);
            Assert.That(factory.Leases[1].IsDisposed, Is.False);
            Assert.That(host.Resources.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(host.Callbacks.Evaluate(old), Is.EqualTo(CallbackGateDecision.DiscardStaleActivation));
            Assert.That(GatedCallbackPath.Evaluate(factory.Leases[1], host.Callbacks, factory.Leases[1].Token), Is.EqualTo(CallbackGateDecision.Dispatch));
        }
    }
}
