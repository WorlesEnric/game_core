// GameCore.Composition tests — the observable service-closure delta of one publication (P-012, P-025, P-046).
//
// `ServiceClosureDelta.Compute` is a pure function of one plan's before/after states, so the fixtures below build
// real proposals through the control lane (`SubmitEdit` + `StagedPlan`) and read the delta of the staged
// proposal. That is the same object a publication archives as evidence (P-026), so every assertion here is about
// a published composition fact — which consumer started waiting, which one resumed, and which binding moved —
// rather than about intermediate bookkeeping.
//
// A rejected proposal is planned directly through `CompositionEditApplier.Plan`, because the lane clears a
// rejected plan's proposal before a caller can read it through `StagedPlan`.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ServiceClosureDeltaTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 0x400AUL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 0x400AUL));

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                Sources = new TestManifestSource();
                Host = CompositionHost.CreateDefault(World, Root, Sources, null);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
            }

            public IdFactory Ids { get; }

            public TestManifestSource Sources { get; }

            public CompositionHost Host { get; }

            public OperationIssuer Issuer { get; }

            public void Add(PluginManifest manifest) => Sources.Add(manifest, null);

            /// <summary>Admits one mount and publishes it, so the next edit plans against a committed world.</summary>
            public void Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
            {
                EditAdmission admission = Host.SubmitEdit(
                    Payloads.Mount(manifest, instance, scope, null),
                    Issuer.Next(),
                    Host.Snapshot().Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                Host.Drain();
            }

            /// <summary>Admits one edit and proves the proposal is staged rather than published (00 s9).</summary>
            public EditAdmission Submit(CompositionEditPayload payload)
            {
                EditAdmission admission = Host.SubmitEdit(payload, Issuer.Next(), Host.Snapshot().Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                return admission;
            }

            /// <summary>The proposal of one admitted, still-unpublished operation (00 s9).</summary>
            public CompositionEditPlan Staged(EditAdmission admission)
            {
                CompositionEditPlan? plan = Host.StagedPlan(admission.Handle.Operation);
                Assert.That(plan, Is.Not.Null, "a staged edit exposes its proposal");
                return plan!;
            }
        }

        /// <summary>A provider of one contract and a consumer that requires it, both mounted and bound in the root.</summary>
        private sealed class Contracted
        {
            public Contracted(Rig rig)
            {
                Contract = new ContractRef(rig.Ids.Capability().Value, 1U);
                PluginTypeId providerType = rig.Ids.Type();
                PluginTypeId consumerType = rig.Ids.Type();
                Provider = rig.Ids.Instance();
                Consumer = rig.Ids.Instance();
                ProviderManifest = Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract, rig.Ids.NextId()) });
                ConsumerManifest = Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract) });
                rig.Add(ProviderManifest);
                rig.Add(ConsumerManifest);
                rig.Mount(ProviderManifest, Provider, Root);
                rig.Mount(ConsumerManifest, Consumer, Root);
            }

            public ContractRef Contract { get; }

            public PluginInstanceId Provider { get; }

            public PluginInstanceId Consumer { get; }

            public PluginManifest ProviderManifest { get; }

            public PluginManifest ConsumerManifest { get; }
        }

        /// <summary>The lifecycle edge this publication recorded for one installation.</summary>
        private static LifecycleEdge EdgeFor(IReadOnlyList<LifecycleEdge> edges, PluginInstanceId instance)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Instance.Equals(instance))
                {
                    return edges[i];
                }
            }

            Assert.Fail("no lifecycle edge was recorded for " + instance.ToString());
            return default(LifecycleEdge);
        }

        private static bool HasBindingFor(
            IReadOnlyList<BindingDelta> bindings,
            PluginInstanceId consumer,
            ContractRef contract,
            PluginInstanceId provider,
            bool added)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                BindingDelta binding = bindings[i];
                if (binding.Consumer.Equals(consumer) &&
                    binding.Added == added &&
                    binding.Contract.Equals(contract) &&
                    binding.Provider.Value.Equals(provider.Value))
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void RemovingARequiredProviderMakesItsConsumerWaitInTheSamePlan()
        {
            Rig rig = new Rig(0x636C6F31UL);
            Contracted pair = new Contracted(rig);

            EditAdmission unmount = rig.Submit(Payloads.Unmount(pair.Provider));
            CompositionEditPlan plan = rig.Staged(unmount);
            ServiceClosureDelta delta = ServiceClosureDelta.Compute(plan);

            Assert.That(delta.Changed, Is.True);
            Assert.That(delta.HasWaits, Is.True);
            Assert.That(delta.WaitingConsumers.Count, Is.EqualTo(1));
            Assert.That(delta.WaitingConsumers[0].Equals(pair.Consumer), Is.True, "the waiting consumer is the one that required the provider.");
            Assert.That(delta.Waits(pair.Consumer), Is.True);
            Assert.That(delta.WaitingDiagnostics.Count, Is.GreaterThan(0), "each wait carries the reason it waits (P-052).");
            Assert.That(HasCode(delta.WaitingDiagnostics, DiagnosticCode.MissingDependency), Is.True);
            Assert.That(delta.RetractedConsumers, Does.Contain(pair.Consumer), "a consumer that starts waiting retracts its contribution in the same plan (P-012).");
            Assert.That(delta.RetiredInstances, Is.EqualTo(plan.RetiredInstances), "the delta copies the plan's retirement list; the plan is the authority on what retires (P-048).");
            Assert.That(delta.RetiredInstances, Does.Contain(pair.Provider), "the removed provider is one of the retired activations.");
        }

        [Test]
        public void ReturningTheProviderResumesTheConsumerThatWaited()
        {
            Rig rig = new Rig(0x636C6F32UL);
            Contracted pair = new Contracted(rig);
            rig.Submit(Payloads.Unmount(pair.Provider));
            rig.Host.Drain();
            Assert.That(rig.Host.FindInstall(pair.Consumer)!.State, Is.EqualTo(InstallationState.WaitingForDependencies), "the consumer must be waiting before it can resume.");

            EditAdmission remount = rig.Submit(Payloads.Mount(pair.ProviderManifest, pair.Provider, Root, null));
            ServiceClosureDelta delta = ServiceClosureDelta.Compute(rig.Staged(remount));

            Assert.That(delta.HasResumes, Is.True);
            Assert.That(delta.ResumedConsumers.Count, Is.EqualTo(1));
            Assert.That(delta.ResumedConsumers[0].Equals(pair.Consumer), Is.True, "the resumed consumer is the one whose provider returned.");
            Assert.That(delta.Resumed(pair.Consumer), Is.True);
            Assert.That(delta.WaitingConsumers, Is.Empty);
            Assert.That(delta.Changed, Is.True);
        }

        [Test]
        public void ASuspendPublicationCarriesItsEdgeAndItsRetractedConsumer()
        {
            Rig rig = new Rig(0x636C6F33UL);
            PluginTypeId type = rig.Ids.Type();
            PluginInstanceId instance = rig.Ids.Instance();
            rig.Add(Manifests.Plain(type, rig.Ids));
            rig.Mount(rig.Sources.ManifestOf(type), instance, Root);

            EditAdmission suspend = rig.Submit(Payloads.Suspend(instance));
            ServiceClosureDelta delta = ServiceClosureDelta.Compute(rig.Staged(suspend));

            LifecycleEdge edge = EdgeFor(delta.LifecycleEdges, instance);
            Assert.That(edge.From, Is.EqualTo(InstallationState.Active));
            Assert.That(edge.To, Is.EqualTo(InstallationState.Suspended), "a suspend publishes the Active -> Suspended edge (P-046).");
            Assert.That(delta.RetractedConsumers, Does.Contain(instance), "a suspended installation retracts its active contribution (P-046).");
            Assert.That(delta.Changed, Is.True);
        }

        [Test]
        public void BindingsNameTheContractAndProviderThatLeftAndReturned()
        {
            Rig rig = new Rig(0x636C6F34UL);
            Contracted pair = new Contracted(rig);

            EditAdmission unmount = rig.Submit(Payloads.Unmount(pair.Provider));
            ServiceClosureDelta removal = ServiceClosureDelta.Compute(rig.Staged(unmount));
            rig.Host.Drain();

            EditAdmission remount = rig.Submit(Payloads.Mount(pair.ProviderManifest, pair.Provider, Root, null));
            ServiceClosureDelta addition = ServiceClosureDelta.Compute(rig.Staged(remount));

            IReadOnlyList<BindingDelta> removed = removal.RemovedBindings();
            Assert.That(removed.Count, Is.GreaterThan(0), "losing the provider removes the consumer's binding to it.");
            Assert.That(HasBindingFor(removed, pair.Consumer, pair.Contract, pair.Provider, false), Is.True,
                "the removed binding names the consumer, the contract and the provider that left.");
            Assert.That(removal.AddedBindings(), Is.Empty);

            IReadOnlyList<BindingDelta> added = addition.AddedBindings();
            Assert.That(added.Count, Is.EqualTo(1), "a returned provider adds one binding: the consumer's to it.");
            Assert.That(HasBindingFor(added, pair.Consumer, pair.Contract, pair.Provider, true), Is.True,
                "the added binding names the consumer, the contract and the provider that returned.");
            Assert.That(addition.RemovedBindings(), Is.Empty);
        }

        [Test]
        public void ANoChangePlanReportsNoClosureFacts()
        {
            Rig rig = new Rig(0x636C6F35UL);
            PluginTypeId type = rig.Ids.Type();
            PluginInstanceId instance = rig.Ids.Instance();
            rig.Add(Manifests.Plain(type, rig.Ids));
            rig.Mount(rig.Sources.ManifestOf(type), instance, Root);

            // The lane is already in Automatic mode, so setting that value again changes nothing (P-006).
            EditAdmission admission = rig.Host.SubmitEdit(
                Payloads.SetMode(PropagationMode.Automatic),
                rig.Issuer.Next(),
                rig.Host.Snapshot().Revision);

            Assert.That(admission.Plan, Is.Not.Null);
            Assert.That(admission.Plan!.IsNoChange, Is.True);
            Assert.That(admission.Plan!.After.Fingerprint().Equals(admission.Plan!.Before.Fingerprint()), Is.True);

            ServiceClosureDelta delta = ServiceClosureDelta.Compute(admission.Plan!);

            Assert.That(delta.Changed, Is.False);
            Assert.That(delta.LifecycleEdges, Is.Empty);
            Assert.That(delta.WaitingConsumers, Is.Empty);
            Assert.That(delta.ResumedConsumers, Is.Empty);
            Assert.That(delta.Bindings, Is.Empty);
            Assert.That(delta.RetiredInstances, Is.Empty);
        }

        [Test]
        public void ARejectedPlanHasAnEmptyClosureDelta()
        {
            Rig rig = new Rig(0x636C6F36UL);
            CompositionEditPayload payload = Payloads.Unmount(rig.Ids.Instance());
            ContentHash inputHash = CompositionEditApplier.InputHashOf(CompositionEditCodec.Encode(payload));

            CompositionEditPlan plan = CompositionEditApplier.Plan(
                rig.Host.Committed,
                payload,
                rig.Issuer.Next(),
                rig.Host.Snapshot().Revision,
                inputHash,
                rig.Sources);

            Assert.That(plan.Succeeded, Is.False, "an unmount of an installation this world never had is refused, not published.");
            Assert.That(plan.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(plan.RetiredInstances, Is.Empty);

            ServiceClosureDelta delta = ServiceClosureDelta.Compute(plan);

            Assert.That(delta.Changed, Is.False);
            Assert.That(delta.LifecycleEdges, Is.Empty);
            Assert.That(delta.WaitingConsumers, Is.Empty);
            Assert.That(delta.ResumedConsumers, Is.Empty);
            Assert.That(delta.Bindings, Is.Empty);
            Assert.That(delta.WaitingDiagnostics, Is.Empty, "a plan that resolved no closure reports no waiting reason.");
            Assert.That(delta.ClosureDiagnostics, Is.Empty);
        }

        [Test]
        public void DescribeIsAStableEvidenceFormThatNamesTheOperation()
        {
            Rig rig = new Rig(0x636C6F37UL);
            Contracted pair = new Contracted(rig);
            EditAdmission unmount = rig.Submit(Payloads.Unmount(pair.Provider));
            CompositionEditPlan plan = rig.Staged(unmount);
            ServiceClosureDelta delta = ServiceClosureDelta.Compute(plan);

            string first = delta.Describe();
            string second = delta.Describe();

            Assert.That(second, Is.EqualTo(first), "the archived rendering is a pure function of the delta (P-026).");
            Assert.That(first, Does.Contain("operation="));
            Assert.That(first, Does.Contain(plan.Operation.ToString()));
            Assert.That(first, Does.Contain("waiting=[" + pair.Consumer.ToString() + "]"));
            Assert.That(first, Does.Contain("retired=["));
            Assert.That(first, Does.Contain(pair.Provider.ToString()), "the rendering names the provider that left.");
        }

        [Test]
        public void ComputingADeltaRequiresAPlan()
        {
            Assert.Throws<ArgumentNullException>(() => ServiceClosureDelta.Compute(null!));
        }
    }
}
