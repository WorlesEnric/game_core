// GameCore.Composition tests — P-012's unexpected-failure path, both halves.
//
// 00 P-012's second paragraph is one sentence with two outcomes, and this fixture asserts both through the real
// control lane rather than through a hand-built plan:
//
//   "An existing provider that fails unexpectedly cannot be kept active: the world stops admission and faults if a
//    safe dependency-closure deactivation cannot publish. There is no invisible partial success or timeout-based
//    unsafe release."
//
//   * **Deactivation publishes.** `CompositionHost.FailActiveProvider` marks the failed provider `Failed` and the
//     ordinary resolver moves every consumer that required it to `WaitingForDependencies` with its contribution
//     retracted, in one new revision and epoch. The failed provider is absent from the new assembly, unrelated
//     installations keep their state, and nothing was published partially.
//   * **Deactivation cannot publish.** A validator that refuses the deactivation leaves the old assembly visible
//     and the failed provider still holding its place, and the report says so. That is the state the world turns
//     into a fault; the world-side half (admission closed, `WorldLifecycleState.Faulted`, checkpoint restore) is
//     proved by the Unity EditMode suite, because the fault latch lives in the Unity layer.
//
// A third outcome does not exist here: the report either carries a token of the published deactivation or a
// refusal code, never both and never neither.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ProviderFailureTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 0x400BUL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 0x400BUL));

        private sealed class Rig
        {
            public Rig(ulong domain, ICompositionEditValidator? validator)
            {
                Ids = new IdFactory(domain);
                Sources = new TestManifestSource();
                Host = CompositionHost.CreateDefault(World, Root, Sources, null, default(CompositionLaneSeed), validator);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
            }

            public IdFactory Ids { get; }

            public TestManifestSource Sources { get; }

            public CompositionHost Host { get; }

            public OperationIssuer Issuer { get; }

            public void Add(PluginManifest manifest) => Sources.Add(manifest, null);

            public void Mount(PluginManifest manifest, PluginInstanceId instance)
            {
                EditAdmission admission = Host.SubmitEdit(
                    Payloads.Mount(manifest, instance, Root, null),
                    Issuer.Next(),
                    Host.Snapshot().Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                Host.Drain();
            }

            /// <summary>The committed state of one installation; a missing record is a test failure (P-004).</summary>
            public InstallationState StateOf(PluginInstanceId instance)
            {
                Assert.That(Host.Committed.TryGetInstall(instance, out InstallEntry? entry), Is.True,
                    "the installation must be registered in the committed world");
                Assert.That(entry, Is.Not.Null);
                return entry!.State;
            }
        }

        /// <summary>A provider of one contract and a consumer that requires it, both Active in the root scope.</summary>
        private sealed class Contracted
        {
            public Contracted(Rig rig)
            {
                Contract = new ContractRef(rig.Ids.Capability().Value, 1U);
                Provider = rig.Ids.Instance();
                Consumer = rig.Ids.Instance();
                Unrelated = rig.Ids.Instance();
                ProviderManifest = Manifests.Plain(rig.Ids.Type(), rig.Ids, null, new[] { Manifests.Export(Contract, rig.Ids.NextId()) });
                ConsumerManifest = Manifests.Plain(rig.Ids.Type(), rig.Ids, null, null, new[] { Manifests.Requires(Contract) });
                UnrelatedManifest = Manifests.Plain(rig.Ids.Type(), rig.Ids, null, null);
                rig.Add(ProviderManifest);
                rig.Add(ConsumerManifest);
                rig.Add(UnrelatedManifest);
                rig.Mount(ProviderManifest, Provider);
                rig.Mount(ConsumerManifest, Consumer);
                rig.Mount(UnrelatedManifest, Unrelated);
            }

            public ContractRef Contract { get; }

            public PluginInstanceId Provider { get; }

            public PluginInstanceId Consumer { get; }

            /// <summary>An installation with no dependency on the provider: it must be untouched by the failure.</summary>
            public PluginInstanceId Unrelated { get; }

            public PluginManifest ProviderManifest { get; }

            public PluginManifest ConsumerManifest { get; }

            public PluginManifest UnrelatedManifest { get; }
        }

        [Test]
        public void AnUnexpectedProviderFailurePublishesASafeDeactivationOfTheProviderAndItsDependents()
        {
            var rig = new Rig(0x7001UL, null);
            var world = new Contracted(rig);

            Assert.That(rig.StateOf(world.Provider), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.StateOf(world.Consumer), Is.EqualTo(InstallationState.Active));
            CompositionRevision before = rig.Host.Committed.Revision;
            AssemblyEpoch epochBefore = rig.Host.Committed.Epoch;

            ProviderFailureReport report = rig.Host.FailActiveProvider(
                world.Provider, rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "the provider's live stage threw");

            Assert.That(report.Deactivated, Is.True, report.Detail);
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(report.Token, Is.Not.Null, "a deactivation that published carries the token it published at");

            // The failed provider is not kept active, and it is not removed either: it keeps its identity so an
            // explicit retry or an unmount stays possible (P-046).
            Assert.That(rig.StateOf(world.Provider), Is.EqualTo(InstallationState.Failed),
                "an existing provider that fails unexpectedly cannot be kept active (P-012)");

            // Its dependent deactivated in the same publication, exactly as a lost provider does.
            Assert.That(rig.StateOf(world.Consumer), Is.EqualTo(InstallationState.WaitingForDependencies),
                "the dependency closure deactivates in the same publication (P-012)");
            Assert.That(report.WaitingConsumers, Does.Contain(world.Consumer));
            Assert.That(report.Closure, Is.Not.Null);
            Assert.That(report.Closure!.RetiredInstances, Does.Contain(world.Provider),
                "the failed provider's own activation is the one that retires authority (P-046)");

            // No invisible partial success: the publication is one new revision and epoch for the whole world.
            Assert.That(rig.Host.Committed.Revision.Value, Is.GreaterThan(before.Value), "one publication, one revision");
            Assert.That(rig.Host.Committed.Epoch.Value, Is.GreaterThan(epochBefore.Value), "one publication, one epoch");

            // Unrelated state and stages keep running: an installation that depended on nothing is untouched.
            Assert.That(rig.StateOf(world.Unrelated), Is.EqualTo(InstallationState.Active),
                "the deactivation is scoped to the dependency closure, not to the world");
        }

        [Test]
        public void AProviderFailureReportIsHonestAboutTheRecordedCauseAndNeverClaimsAPartialPublication()
        {
            var rig = new Rig(0x7002UL, null);
            var world = new Contracted(rig);

            ProviderFailureReport first = rig.Host.FailActiveProvider(
                world.Provider, rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "stage threw");

            Assert.That(first.Deactivated, Is.True, first.Detail);
            Assert.That(first.Detail, Does.Contain("ProviderFailed"), "the reported cause is named in the report (P-052)");
            Assert.That(first.Detail, Does.Contain("stage threw"), "the caller's detail survives into the report");
            Assert.That(first.Describe(), Does.Contain("deactivated=True"));

            // A second report of the same failure is a no-op publication rather than a second epoch (P-050).
            CompositionRevision afterFirst = rig.Host.Committed.Revision;
            ProviderFailureReport second = rig.Host.FailActiveProvider(
                world.Provider, rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "stage threw again");

            Assert.That(second.Deactivated, Is.True, "the provider is already failed, so the deactivated state holds");
            Assert.That(rig.StateOf(world.Provider), Is.EqualTo(InstallationState.Failed));
            Assert.That(rig.StateOf(world.Consumer), Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.Host.Committed.Revision.Value, Is.EqualTo(afterFirst.Value),
                "a repeated failure report publishes no second change (P-006)");
        }

        [Test]
        public void AProviderThatIsNotActiveCannotReportAnUnexpectedFailure()
        {
            var rig = new Rig(0x7003UL, null);

            // A standalone provider with no consumer: suspension is a deliberate teardown, not a failure, so the
            // unexpected-failure edge must refuse it rather than mark a suspended installation Failed (P-012).
            ContractRef contract = new ContractRef(rig.Ids.Capability().Value, 1U);
            PluginInstanceId provider = rig.Ids.Instance();
            PluginManifest manifest = Manifests.Plain(rig.Ids.Type(), rig.Ids, null, new[] { Manifests.Export(contract, rig.Ids.NextId()) });
            rig.Add(manifest);
            rig.Mount(manifest, provider);
            Assert.That(rig.StateOf(provider), Is.EqualTo(InstallationState.Active));

            EditAdmission suspend = rig.Host.SubmitEdit(
                Payloads.Suspend(provider), rig.Issuer.Next(), rig.Host.Snapshot().Revision);
            Assert.That(suspend.Staged, Is.True, suspend.Code.ToString());
            rig.Host.Drain();
            Assert.That(rig.StateOf(provider), Is.EqualTo(InstallationState.Suspended));

            CompositionRevision before = rig.Host.Committed.Revision;
            ProviderFailureReport report = rig.Host.FailActiveProvider(
                provider, rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "reported against a suspended installation");

            Assert.That(report.Deactivated, Is.False, "a suspended installation has no live authority to lose");
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict),
                "the refusal names the ownership rule the request violated (P-052)");
            Assert.That(rig.Host.Committed.Revision.Value, Is.EqualTo(before.Value), "a refusal publishes nothing");
            Assert.That(rig.StateOf(provider), Is.EqualTo(InstallationState.Suspended),
                "a refused edge leaves the committed state alone (P-051)");

            // An installation that was never mounted is refused for the missing-dependency reason instead.
            ProviderFailureReport absent = rig.Host.FailActiveProvider(
                rig.Ids.Instance(), rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "no such installation");
            Assert.That(absent.Deactivated, Is.False);
            Assert.That(absent.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void ADeactivationThatCannotPublishLeavesTheOldAssemblyAndReportsTheRefusal()
        {
            // A validator that refuses everything is the "cannot publish" injection point on the composition side:
            // the lane plans, the validator rejects, and nothing is written (P-028). The world's half - admission
            // closed and Faulted - is the Unity suite's, because the fault latch lives there.
            var rig = new Rig(0x7004UL, new RefusingValidator());
            var world = new Contracted(rig);

            CompositionRevision before = rig.Host.Committed.Revision;

            ProviderFailureReport report = rig.Host.FailActiveProvider(
                world.Provider, rig.Issuer.Next(), DiagnosticCode.ProviderFailed, "the provider's live stage threw");

            Assert.That(report.Deactivated, Is.False,
                "a deactivation that cannot publish must never report itself as published (P-012)");
            Assert.That(report.Token, Is.Null, "nothing published, so there is no token to inspect");
            Assert.That(report.Code, Is.Not.EqualTo(DiagnosticCode.None), "the refusal names its reason (P-052)");
            Assert.That(report.Detail, Does.Contain("fault"),
                "the report says what the world must do next, rather than implying the failure was handled");

            // No partial publication: the revision is unchanged, the failed provider still holds its place, and its
            // consumer keeps running against it. That is precisely the state the world turns into a fault.
            Assert.That(rig.Host.Committed.Revision.Value, Is.EqualTo(before.Value), "nothing published");
            Assert.That(rig.StateOf(world.Provider), Is.EqualTo(InstallationState.Active),
                "the committed assembly still has the provider, which is why the world must fault instead");
            Assert.That(rig.StateOf(world.Consumer), Is.EqualTo(InstallationState.Active));

            // The two outcomes are mutually exclusive and exhaustive: a report either carries the token of a
            // published deactivation or the code of a refusal, never both and never neither. That property is what
            // P-012's "no invisible partial success" reduces to at this boundary.
            Assert.That(report.Deactivated && report.Token != null && report.Code == DiagnosticCode.None
                || !report.Deactivated && report.Token == null && report.Code != DiagnosticCode.None,
                Is.True,
                "a report either published (token, no code) or did not (code, no token): " + report.Describe());
        }

        /// <summary>Refuses every proposal, so a publication attempt cannot complete (P-028, P-012).</summary>
        private sealed class RefusingValidator : ICompositionEditValidator
        {
            public EditValidationResult Validate(CompositionState before, CompositionState after, CompositionChangeSet changeSet) =>
                EditValidationResult.Refuse(DiagnosticCode.OwnershipConflict, "the deactivation was refused by policy (P-012)");
        }
    }
}
