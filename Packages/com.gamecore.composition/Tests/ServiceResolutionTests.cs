// GameCore.Composition tests — deterministic service visibility (P-011), dependency closure (P-012) and the
// Automatic/Conservative independence of service resolution (P-013).
//
// The fixtures mount real declarations through the control lane; assertions read committed snapshots, so a test
// proves the published result rather than an intermediate.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ServiceResolutionTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 3UL));
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 3UL));

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                Manifests = new TestManifestSource();
                Host = CompositionHost.CreateDefault(World, Root, Manifests, null);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
            }

            public IdFactory Ids { get; }

            public TestManifestSource Manifests { get; }

            public CompositionHost Host { get; }

            public OperationIssuer Issuer { get; }

            public CompositionRevision Revision => Host.Snapshot().Revision;

            public void Add(PluginManifest manifest, ConfigDocument? defaults = null) => Manifests.Add(manifest, defaults);

            public void CreateScope(ScopeId scope, ScopeId parent)
            {
                EditAdmission admission = Host.SubmitEdit(Payloads.ScopeCreate(scope, parent), Issuer.Next(), Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                Host.Drain();
            }

            public OperationId Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope, IReadOnlyList<ServiceSelection>? selections = null)
            {
                OperationId operation = Issuer.Next();
                EditAdmission admission = Host.SubmitEdit(Payloads.Mount(manifest, instance, scope, null, 0, selections), operation, Revision);
                Host.Drain();
                return operation;
            }

            public void Unmount(PluginInstanceId instance)
            {
                EditAdmission admission = Host.SubmitEdit(Payloads.Unmount(instance), Issuer.Next(), Revision);
                Assert.That(admission.Staged, Is.True, admission.Code.ToString());
                Host.Drain();
            }

            public InstallationState StateOf(PluginInstanceId instance)
            {
                InstallSnapshot? install = Host.FindInstall(instance);
                Assert.That(install, Is.Not.Null, "The installation must exist in the committed composition.");
                return install!.State;
            }

            public IReadOnlyList<ServiceBinding> BindingsOf(PluginInstanceId instance) => Host.FindInstall(instance)!.Bindings;
        }

        private static ContractRef Contract(Id128 id, uint version) => new ContractRef(id, version);

        [Test]
        public void PrivateProviderInAnAncestorIsInvisible()
        {
            Rig rig = new Rig(0x7365727631UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId chapter = rig.Ids.Scope();
            PluginInstanceId provider = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId()) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(chapter, Root);
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, chapter);
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root);

            // A provider is private to its scope unless it exports to descendants (P-011).
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.StateOf(provider), Is.EqualTo(InstallationState.Active));
        }

        [Test]
        public void SiblingScopesNeverSeeEachOthersProviders()
        {
            Rig rig = new Rig(0x7365727632UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId left = rig.Ids.Scope();
            ScopeId right = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(left, Root);
            rig.CreateScope(right, Root);
            PluginInstanceId provider = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, left);

            EditAdmission mounted = rig.Host.SubmitEdit(
                Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, right, null),
                rig.Issuer.Next(),
                rig.Revision);
            rig.Host.Drain();

            Assert.That(mounted.Staged, Is.True, mounted.Code.ToString());
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies), "Sibling search is forbidden (P-011).");
            Assert.That(rig.BindingsOf(consumer), Is.Empty);
        }

        [Test]
        public void ExportedProviderReachesDescendantsIncludingTheProvidersOwnScopeTargets()
        {
            Rig rig = new Rig(0x7365727633UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId chapter = rig.Ids.Scope();
            ScopeId market = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(chapter, Root);
            rig.CreateScope(market, chapter);
            PluginInstanceId provider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, chapter);

            PluginInstanceId descendant = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), descendant, market);
            PluginInstanceId sameScope = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), sameScope, chapter);

            Assert.That(rig.StateOf(descendant), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.StateOf(sameScope), Is.EqualTo(InstallationState.Active));
            ServiceBinding binding = rig.BindingsOf(descendant)[0];
            Assert.That(binding.Provider, Is.EqualTo(new ProviderInstallationId(provider.Value)));
            Assert.That(binding.ActivationEpoch, Is.EqualTo(rig.Host.FindInstall(provider)!.Record.ActivationEpoch));
            Assert.That(binding.IsFallback, Is.False);
        }

        [Test]
        public void ServiceIsolationBoundaryBlocksAncestorProvidersButNotBoundaryProviders()
        {
            Rig rig = new Rig(0x7365727634UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId chapter = rig.Ids.Scope();
            ScopeId boundary = rig.Ids.Scope();
            ScopeId below = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(chapter, Root);
            rig.CreateScope(boundary, chapter);
            rig.CreateScope(below, boundary);

            PluginInstanceId ancestorProvider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), ancestorProvider, chapter);

            // The boundary names the contract, so the ancestor provider is blocked below it (P-016).
            EditAdmission isolation = rig.Host.SubmitEdit(
                Payloads.ScopeIsolation(boundary, new IsolationSet(false, new[] { contract }), null),
                rig.Issuer.Next(),
                rig.Revision);
            Assert.That(isolation.Staged, Is.True, isolation.Code.ToString());
            rig.Host.Drain();

            PluginInstanceId blocked = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), blocked, below);
            Assert.That(rig.StateOf(blocked), Is.EqualTo(InstallationState.WaitingForDependencies));

            // A provider installed at the boundary itself still works (P-016).
            PluginInstanceId atBoundary = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), atBoundary, boundary);
            Assert.That(rig.StateOf(blocked), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.BindingsOf(blocked)[0].Provider, Is.EqualTo(new ProviderInstallationId(atBoundary.Value)));
        }

        [Test]
        public void SameScopeDuplicateSingleBindingsAlwaysConflict()
        {
            Rig rig = new Rig(0x7365727635UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId()) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            PluginInstanceId first = rig.Ids.Instance();
            PluginInstanceId second = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), first, Root);
            rig.Mount(rig.Manifests.ManifestOf(providerType), second, Root);

            PluginInstanceId consumer = rig.Ids.Instance();
            OperationId operation = rig.Issuer.Next();
            EditAdmission admission = rig.Host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root, null), operation, rig.Revision);
            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            rig.Host.Drain();

            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));
            InstallSnapshot install = rig.Host.FindInstall(consumer)!;
            Assert.That(install.Diagnostics.Count, Is.GreaterThan(0));
            Assert.That(install.Diagnostics[0].CodeText, Is.EqualTo("ServiceConflict"));
        }

        [Test]
        public void TwoVisibleAncestorProvidersConflictWithoutAnOverride()
        {
            Rig rig = new Rig(0x7365727636UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId outer = rig.Ids.Scope();
            ScopeId inner = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(outer, Root);
            rig.CreateScope(inner, outer);
            rig.Mount(rig.Manifests.ManifestOf(providerType), rig.Ids.Instance(), outer);
            rig.Mount(rig.Manifests.ManifestOf(providerType), rig.Ids.Instance(), inner);

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, inner);

            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.Host.FindInstall(consumer)!.Diagnostics[0].CodeText, Is.EqualTo("ServiceConflict"));
        }

        [Test]
        public void NearestAncestorProviderWinsWhenItDeclaresTheOverride()
        {
            Rig rig = new Rig(0x7365727645UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId outer = rig.Ids.Scope();
            ScopeId inner = rig.Ids.Scope();
            PluginTypeId plainType = rig.Ids.Type();
            PluginTypeId overridingType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(plainType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(overridingType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants, true) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(outer, Root);
            rig.CreateScope(inner, outer);
            PluginInstanceId outerProvider = rig.Ids.Instance();
            PluginInstanceId innerProvider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(plainType), outerProvider, outer);
            rig.Mount(rig.Manifests.ManifestOf(overridingType), innerProvider, inner);

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, inner);

            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.BindingsOf(consumer)[0].Provider, Is.EqualTo(new ProviderInstallationId(innerProvider.Value)));
        }

        [Test]
        public void MultiBindingReturnsEveryVisibleProviderInCanonicalOrder()
        {
            Rig rig = new Rig(0x7365727637UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants, false, ServiceBindingKind.Multi) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            List<PluginInstanceId> providers = new List<PluginInstanceId>
            {
                rig.Ids.Instance(),
                rig.Ids.Instance(),
                rig.Ids.Instance(),
            };

            // Mount in a scrambled order: registration timing must not decide the reported order (P-008).
            rig.Mount(rig.Manifests.ManifestOf(providerType), providers[1], Root);
            rig.Mount(rig.Manifests.ManifestOf(providerType), providers[2], Root);
            rig.Mount(rig.Manifests.ManifestOf(providerType), providers[0], Root);

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root);

            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.Active));
            IReadOnlyList<ServiceBinding> bindings = rig.BindingsOf(consumer);
            Assert.That(bindings.Count, Is.EqualTo(3));
            providers.Sort((left, right) => left.Value.CompareTo(right.Value));
            for (int i = 0; i < bindings.Count; i++)
            {
                Assert.That(bindings[i].BindingKind, Is.EqualTo(ServiceBindingKind.Multi));
                Assert.That(bindings[i].Provider, Is.EqualTo(new ProviderInstallationId(providers[i].Value)));
            }
        }

        [Test]
        public void ExplicitSelectionIsHonoredInsideTheBoundaryAndNeverFallsBack()
        {
            Rig rig = new Rig(0x7365727638UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId outer = rig.Ids.Scope();
            ScopeId inner = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            rig.CreateScope(outer, Root);
            rig.CreateScope(inner, outer);
            PluginInstanceId outerProvider = rig.Ids.Instance();
            PluginInstanceId innerProvider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), outerProvider, outer);
            rig.Mount(rig.Manifests.ManifestOf(providerType), innerProvider, inner);

            PluginInstanceId selecting = rig.Ids.Instance();
            rig.Mount(
                rig.Manifests.ManifestOf(consumerType),
                selecting,
                inner,
                new[] { new ServiceSelection(Contract(contract, 1U), new ProviderInstallationId(outerProvider.Value)) });

            Assert.That(rig.StateOf(selecting), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.BindingsOf(selecting)[0].Provider, Is.EqualTo(new ProviderInstallationId(outerProvider.Value)));

            // Selecting a provider outside the visibility boundary is refused, never silently replaced: a
            // sibling scope's provider is not in this consumer's domain at all (P-011).
            ScopeId elsewhere = rig.Ids.Scope();
            rig.CreateScope(elsewhere, outer);
            PluginInstanceId foreign = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), foreign, elsewhere);
            PluginInstanceId badSelection = rig.Ids.Instance();
            OperationId operation = rig.Issuer.Next();
            EditAdmission admission = rig.Host.SubmitEdit(
                Payloads.Mount(
                    rig.Manifests.ManifestOf(consumerType),
                    badSelection,
                    inner,
                    null,
                    0,
                    new[] { new ServiceSelection(Contract(contract, 1U), new ProviderInstallationId(foreign.Value)) }),
                operation,
                rig.Revision);

            Assert.That(admission.Staged, Is.True, admission.Code.ToString());
            rig.Host.Drain();
            Assert.That(rig.StateOf(badSelection), Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.Host.FindInstall(badSelection)!.Diagnostics[0].CodeText, Is.EqualTo("ServiceConflict"));
        }

        [Test]
        public void OptionalAbsenceIsExplicitAndRebindsToItsDeclaredFallback()
        {
            Rig rig = new Rig(0x7365727639UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[]
            {
                Manifests.Requires(Contract(contract, 1U), isRequired: false, fallbackKey: rig.Ids.NextId()),
            }));

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root);

            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.Active), "An optional absence still activates (P-011).");
            IReadOnlyList<ServiceBinding> bindings = rig.BindingsOf(consumer);
            Assert.That(bindings.Count, Is.EqualTo(1));
            Assert.That(bindings[0].IsFallback, Is.True);
            Assert.That(rig.Host.FindInstall(consumer)!.Diagnostics.Count, Is.GreaterThan(0), "The absent optional service is reported, not hidden.");
        }

        [Test]
        public void WaitingInstallationActivatesWhenItsProviderAppearsAndWaitsAgainWhenItLeaves()
        {
            Rig rig = new Rig(0x7365727641UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId()) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root);
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));

            PluginInstanceId provider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, Root);
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.Active), "A restored provider activates waiting dependents without target wiring (P-012, O-04).");

            rig.Unmount(provider);
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies), "Losing a required provider retracts the consumer in the same publication (P-012).");
            Assert.That(rig.BindingsOf(consumer), Is.Empty);
        }

        [Test]
        public void IncompatibleContractVersionLeavesTheConsumerWaiting()
        {
            Rig rig = new Rig(0x7365727642UL);
            Id128 contract = rig.Ids.Capability().Value;
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 2U), rig.Ids.NextId()) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            PluginInstanceId provider = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, Root);
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, Root);

            Assert.That(rig.StateOf(provider), Is.EqualTo(InstallationState.Active));
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));
        }

        [Test]
        public void RequiredDependencyCycleRejectsTheWholeProposal()
        {
            Rig rig = new Rig(0x7365727643UL);
            Id128 first = rig.Ids.Capability().Value;
            Id128 second = rig.Ids.Capability().Value;
            PluginTypeId leftType = rig.Ids.Type();
            PluginTypeId rightType = rig.Ids.Type();

            rig.Add(Manifests.Plain(
                leftType,
                rig.Ids,
                null,
                new[] { Manifests.Export(Contract(first, 1U), rig.Ids.NextId()) },
                new[] { Manifests.Requires(Contract(second, 1U)) }));
            rig.Add(Manifests.Plain(
                rightType,
                rig.Ids,
                null,
                new[] { Manifests.Export(Contract(second, 1U), rig.Ids.NextId()) },
                new[] { Manifests.Requires(Contract(first, 1U)) }));

            PluginInstanceId left = rig.Ids.Instance();
            PluginInstanceId right = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(leftType), left, Root);
            CompositionRevision revisionBefore = rig.Revision;

            EditAdmission admission = rig.Host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(rightType), right, Root, null), rig.Issuer.Next(), rig.Revision);

            Assert.That(admission.Code, Is.EqualTo(DiagnosticCode.Cycle), "A required dependency cycle rejects the whole proposal (P-012).");
            Assert.That(rig.Revision, Is.EqualTo(revisionBefore));
            Assert.That(rig.Host.FindInstall(right), Is.Null);
            Assert.That(rig.Host.FindInstall(left), Is.Not.Null, "The old assembly keeps its provider (P-012).");
        }

        [Test]
        public void SelfOnlyDomainNeverResolvesAnAncestorProvider()
        {
            Rig rig = new Rig(0x7365727644UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId child = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U), domain: ServiceResolutionDomain.SelfOnly) }));

            rig.CreateScope(child, Root);
            PluginInstanceId provider = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(providerType), provider, Root);

            PluginInstanceId consumer = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), consumer, child);
            Assert.That(rig.StateOf(consumer), Is.EqualTo(InstallationState.WaitingForDependencies));

            PluginInstanceId sameScope = rig.Ids.Instance();
            rig.Mount(rig.Manifests.ManifestOf(consumerType), sameScope, Root);
            Assert.That(rig.StateOf(sameScope), Is.EqualTo(InstallationState.Active));
        }

        [Test]
        public void ServiceResolutionIsIdenticalInBothPropagationModes()
        {
            string automatic = ResolutionProjection(PropagationMode.Automatic);
            string conservative = ResolutionProjection(PropagationMode.Conservative);

            Assert.That(conservative, Is.EqualTo(automatic), "Service resolution is identical in both modes (P-011).");
        }

        [Test]
        public void ResolutionIsIndependentOfMountInsertionOrderForFixedSeeds()
        {
            string reference = null!;
            for (int seed = 1; seed <= 8; seed++)
            {
                List<int> order = Shuffle.WithSeed(new[] { 2, 3, 4, 5 }, seed);
                string projection = MountInOrder(order);
                if (reference == null)
                {
                    reference = projection;
                }

                Assert.That(projection, Is.EqualTo(reference), "Seed " + seed + " must produce the same committed composition.");
            }
        }

        private static string ResolutionProjection(PropagationMode mode)
        {
            Rig rig = new Rig(mode == PropagationMode.Automatic ? 0x6D6F6431UL : 0x6D6F6432UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId child = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            CompositionHost host = new CompositionHost(
                World,
                Root,
                new CompositionHostSettings(ControlLaneCapacitySettings.Default, OperationExpirySettings.Default),
                rig.Manifests,
                null,
                mode);

            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x6D6F6465UL, 1UL));
            host.SubmitEdit(Payloads.ScopeCreate(child, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            PluginInstanceId provider = rig.Ids.Instance();
            PluginInstanceId consumer = rig.Ids.Instance();
            host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(providerType), provider, Root, null), issuer.Next(), host.Snapshot().Revision);
            host.Drain();
            host.SubmitEdit(Payloads.Mount(rig.Manifests.ManifestOf(consumerType), consumer, child, null), issuer.Next(), host.Snapshot().Revision);
            host.Drain();

            // The mode itself is excluded from the comparison: only resolution behaviour is under test.
            List<string> lines = new List<string>(Projection.Of(host.Snapshot()).Split('\n'));
            lines.RemoveAll(line => line.StartsWith("mode=", StringComparison.Ordinal));
            return string.Join("\n", lines);
        }

        private static string MountInOrder(IReadOnlyList<int> order)
        {
            Rig rig = new Rig(0x6F72646572UL);
            Id128 contract = rig.Ids.Capability().Value;
            ScopeId outer = rig.Ids.Scope();
            ScopeId inner = rig.Ids.Scope();
            PluginTypeId providerType = rig.Ids.Type();
            PluginTypeId overridingType = rig.Ids.Type();
            PluginTypeId consumerType = rig.Ids.Type();

            rig.Add(Manifests.Plain(providerType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants) }));
            rig.Add(Manifests.Plain(overridingType, rig.Ids, null, new[] { Manifests.Export(Contract(contract, 1U), rig.Ids.NextId(), ServiceVisibility.ExportToDescendants, true) }));
            rig.Add(Manifests.Plain(consumerType, rig.Ids, null, null, new[] { Manifests.Requires(Contract(contract, 1U)) }));

            PluginInstanceId[] instances =
            {
                rig.Ids.Instance(),
                rig.Ids.Instance(),
                rig.Ids.Instance(),
                rig.Ids.Instance(),
            };

            // The two scopes exist in every permutation; only the installation order varies (P-008).
            rig.CreateScope(outer, Root);
            rig.CreateScope(inner, outer);

            for (int i = 0; i < order.Count; i++)
            {
                switch (order[i])
                {
                    case 2:
                        rig.Mount(rig.Manifests.ManifestOf(providerType), instances[0], outer);
                        break;
                    case 3:
                        rig.Mount(rig.Manifests.ManifestOf(overridingType), instances[1], inner);
                        break;
                    case 4:
                        rig.Mount(rig.Manifests.ManifestOf(consumerType), instances[2], inner);
                        break;
                    default:
                        rig.Mount(rig.Manifests.ManifestOf(consumerType), instances[3], outer);
                        break;
                }
            }

            return Projection.Of(rig.Host.Snapshot());
        }
    }
}
