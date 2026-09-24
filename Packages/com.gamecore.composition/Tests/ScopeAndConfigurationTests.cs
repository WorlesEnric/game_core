// GameCore.Composition tests — scopes, membership and immutable configuration patches.
//
// Covers the P-010 scope-tree/removal rules and the P-020 configuration rules: layered composition with an
// explicit field mask, null distinct from missing, canonical set union, no generic deep merge, and a refusal
// when a declaration claims a content hash its own document does not have.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ScopeTreeTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 1UL));
        private static readonly IdFactory Ids = new IdFactory(0x73636F7065UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 1UL));

        private static CompositionHost NewHost()
        {
            TestManifestSource source = new TestManifestSource();
            return CompositionHost.CreateDefault(World, Root, source, null);
        }

        [Test]
        public void RootScopeIsTheOnlyScopeWithoutAParent()
        {
            CompositionHost host = NewHost();
            ScopeSnapshot? root = host.FindScope(Root);

            Assert.That(root, Is.Not.Null);
            Assert.That(root!.IsRoot, Is.True);
            Assert.That(root.Parent.IsDefault, Is.True);
            Assert.That(host.FindScope(Ids.Scope()), Is.Null, "An unknown scope is never invented.");
        }

        [Test]
        public void CreatedScopeCarriesMembershipAndInheritsItsParentDepth()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 1UL));
            ScopeId chapter = Ids.Scope();
            ScopeId market = Ids.Scope();

            Assert.That(host.SubmitEdit(Payloads.ScopeCreate(chapter, Root), issuer.Next(), CompositionRevision.Zero).Staged, Is.True);
            host.Drain();
            Assert.That(host.SubmitEdit(Payloads.ScopeCreate(market, chapter), issuer.Next(), new CompositionRevision(1UL)).Staged, Is.True);
            host.Drain();

            CompositionStateSnapshot snapshot = host.Snapshot();
            Assert.That(snapshot.Revision.Value, Is.EqualTo(2UL));
            Assert.That(snapshot.Scopes.Count, Is.EqualTo(3));
            Assert.That(host.Committed.Scopes.Depth(market), Is.EqualTo(2));
            Assert.That(host.Committed.Scopes.Depth(chapter), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateScopeIdentityIsRejectedAsAnOwnershipConflict()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 2UL));
            ScopeId chapter = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(chapter, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();

            EditAdmission second = host.SubmitEdit(Payloads.ScopeCreate(chapter, Root), issuer.Next(), new CompositionRevision(1UL));
            Assert.That(second.Rejected, Is.True);
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL), "A rejected scope creation publishes nothing.");
        }

        [Test]
        public void ReparentCyclesAndCrossAncestryMovesAreRejected()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 3UL));
            ScopeId parent = Ids.Scope();
            ScopeId child = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(parent, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(child, parent), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();

            EditAdmission cycle = host.SubmitEdit(Payloads.ScopeReparent(parent, child), issuer.Next(), new CompositionRevision(2UL));
            Assert.That(cycle.Code, Is.EqualTo(DiagnosticCode.Cycle));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));

            EditAdmission self = host.SubmitEdit(Payloads.ScopeReparent(parent, parent), issuer.Next(), new CompositionRevision(2UL));
            Assert.That(self.Code, Is.EqualTo(DiagnosticCode.Cycle), "A self-parent edge is also a cycle (P-010, O-02).");
        }

        [Test]
        public void ReparentPreservesDescendantIdentityAndRecomputesDepth()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 4UL));
            ScopeId left = Ids.Scope();
            ScopeId right = Ids.Scope();
            ScopeId moved = Ids.Scope();
            ScopeId leaf = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(left, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(right, Root), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(moved, left), issuer.Next(), new CompositionRevision(2UL));
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(leaf, moved), issuer.Next(), new CompositionRevision(3UL));
            host.Drain();

            EditAdmission reparent = host.SubmitEdit(Payloads.ScopeReparent(moved, right), issuer.Next(), new CompositionRevision(4UL));
            Assert.That(reparent.Staged, Is.True);
            host.Drain();

            Assert.That(host.Committed.Scopes.Depth(moved), Is.EqualTo(2));
            Assert.That(host.Committed.Scopes.Depth(leaf), Is.EqualTo(3));
            Assert.That(host.FindScope(moved)!.Parent, Is.EqualTo(right));
            Assert.That(host.FindScope(leaf)!.Scope, Is.EqualTo(leaf), "Moving a subtree preserves descendant identities (P-025).");
        }

        [Test]
        public void RemovingANonemptyScopeRequiresAnExplicitSubtreeDisposition()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 5UL));
            ScopeId parent = Ids.Scope();
            ScopeId child = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(parent, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(child, parent), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();

            EditAdmission refused = host.SubmitEdit(Payloads.ScopeRemove(parent, false), issuer.Next(), new CompositionRevision(2UL));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));

            EditAdmission removed = host.SubmitEdit(Payloads.ScopeRemove(parent, true), issuer.Next(), new CompositionRevision(2UL));
            Assert.That(removed.Staged, Is.True);
            host.Drain();

            Assert.That(host.FindScope(parent), Is.Null);
            Assert.That(host.FindScope(child), Is.Null);
            Assert.That(host.FindScope(Root), Is.Not.Null);
        }

        [Test]
        public void IsolationSetCannotNameContractsWhileDeclaringAll()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 6UL));
            IsolationSet contradictory = new IsolationSet(true, new[] { Ids.Capability().Value });

            EditAdmission refused = host.SubmitEdit(Payloads.ScopeIsolation(Root, contradictory, null), issuer.Next(), CompositionRevision.Zero);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
        }

        [Test]
        public void IsolationIsStoredPerScopeAndVisibleInTheSnapshot()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 7UL));
            Id128 contract = Ids.Capability().Value;

            EditAdmission edit = host.SubmitEdit(
                Payloads.ScopeIsolation(Root, new IsolationSet(false, new[] { contract }), new IsolationSet(true, null)),
                issuer.Next(),
                CompositionRevision.Zero);
            Assert.That(edit.Staged, Is.True);
            host.Drain();

            ScopeSnapshot root = host.FindScope(Root)!;
            Assert.That(root.ServiceIsolation.Contracts.Count, Is.EqualTo(1));
            Assert.That(root.ServiceIsolation.Contracts[0], Is.EqualTo(contract));
            Assert.That(root.CapabilityIsolation.AllContracts, Is.True);
        }

        [Test]
        public void ConservativeGrantDataIsStoredAndValidatedAgainstRealProviders()
        {
            CompositionHost host = NewHost();
            TestManifestSource source = new TestManifestSource();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 8UL));
            PluginTypeId type = Ids.Type();
            PluginInstanceId provider = Ids.Instance();
            CompositionHost withProvider = CompositionHost.CreateDefault(World, Root, source, null);
            PluginManifest manifest = Manifests.Plain(type, Ids);
            source.Add(manifest, null);
            withProvider.SubmitEdit(Payloads.Mount(manifest, provider, Root, null), issuer.Next(), CompositionRevision.Zero);
            withProvider.Drain();

            CapabilityId capability = Ids.Capability();
            EditAdmission granted = withProvider.SubmitEdit(
                Payloads.ScopeGrants(Root, new[] { new CapabilityImport(capability, new ProviderInstallationId(provider.Value)) }),
                issuer.Next(),
                new CompositionRevision(1UL));
            Assert.That(granted.Staged, Is.True);
            withProvider.Drain();
            Assert.That(withProvider.Committed.Scopes.TryGet(Root, out ScopeRecord? record), Is.True);
            Assert.That(record!.Grants.ImportsFrom(capability, new ProviderInstallationId(provider.Value)), Is.True);

            // A grant naming no registered provider installation cannot grant anything (P-013).
            EditAdmission unknown = host.SubmitEdit(
                Payloads.ScopeGrants(Root, new[] { new CapabilityImport(capability, new ProviderInstallationId(Ids.Instance().Value)) }),
                issuer.Next(),
                CompositionRevision.Zero);
            Assert.That(unknown.Code, Is.EqualTo(DiagnosticCode.MissingDependency));
        }
    }

    [TestFixture]
    public sealed class ConfigurationTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 2UL));
        private static readonly IdFactory Ids = new IdFactory(0x636F6E666967UL);
        private static readonly ScopeId Root = new ScopeId(new Id128(0x726F6F74UL, 2UL));

        [Test]
        public void FieldMaskDistinguishesNullFromMissingAndUnionsSets()
        {
            Id128 limit = Ids.Capability().Value;
            Id128 tags = Ids.Capability().Value;
            Id128 tagA = Ids.Capability().Value;
            Id128 tagB = Ids.Capability().Value;

            ConfigDocument defaults = ConfigDocument.Of(
                new ConfigField(limit, ConfigFieldValue.OfUInt32(3U)),
                new ConfigField(tags, ConfigFieldValue.OfIdSet(new[] { tagA })));

            ConfigDocument patch = ConfigDocument.Of(
                new ConfigField(limit, ConfigFieldValue.OfUInt32(5U)),
                new ConfigField(tags, ConfigFieldValue.OfIdSet(new[] { tagB })));

            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, new Id128(1UL, 1UL), defaults),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, new Id128(2UL, 2UL), patch),
            });

            Assert.That(composed.Succeeded, Is.True);
            Assert.That(composed.Value.TryGetField(limit, out ConfigFieldValue value), Is.True);
            Assert.That(value.AsUInt32, Is.EqualTo(5U), "A scalar field is replaced, not deep-merged (P-020).");
            Assert.That(composed.Value.TryGetField(tags, out ConfigFieldValue set), Is.True);
            Assert.That(set.AsIds.Count, Is.EqualTo(2), "An id set field is composed as a canonical set union.");

            // An explicit null clears the field; omitting the key leaves the inherited value alone.
            ConfigDocument clear = ConfigDocument.Clear(limit);
            ConfigComposeResult cleared = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, new Id128(1UL, 1UL), defaults),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, new Id128(2UL, 2UL), clear),
            });

            Assert.That(cleared.Value.HasField(limit), Is.False);
            Assert.That(cleared.Provenance.Count, Is.GreaterThan(0));
        }

        [Test]
        public void ComposedDocumentHasAStableCanonicalHashAndRoundTrips()
        {
            Id128 field = Ids.Capability().Value;
            ConfigDocument document = ConfigDocument.Of(new ConfigField(field, ConfigFieldValue.OfText("chapter-1")));

            ContentHash first = ConfigDocumentCodec.HashOf(document);
            ContentHash second = ConfigDocumentCodec.HashOf(ConfigDocument.Of(new ConfigField(field, ConfigFieldValue.OfText("chapter-1"))));
            Assert.That(first, Is.EqualTo(second));

            FrozenPayload encoded = ConfigDocumentCodec.Encode(document);
            Assert.That(ConfigDocumentCodec.TryDecode(encoded, out ConfigDocument? decoded), Is.True);
            Assert.That(decoded!.Count, Is.EqualTo(document.Count));
            Assert.That(ConfigDocumentCodec.HashOf(decoded), Is.EqualTo(first));
        }

        [Test]
        public void ConfigDocumentRejectsDuplicateFieldKeys()
        {
            Id128 field = Ids.Capability().Value;
            Assert.Throws<ArgumentException>(() => ConfigDocument.Of(
                new ConfigField(field, ConfigFieldValue.OfUInt32(1U)),
                new ConfigField(field, ConfigFieldValue.OfUInt32(2U))));
        }

        [Test]
        public void MountStoresTheComposedEffectiveConfiguration()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            SchemaId schema = Ids.Schema();
            Id128 limit = Ids.Capability().Value;
            ConfigDocument defaults = ConfigDocument.Of(new ConfigField(limit, ConfigFieldValue.OfUInt32(3U)));
            ConfigDocument local = ConfigDocument.Of(new ConfigField(limit, ConfigFieldValue.OfUInt32(7U)));

            TestManifestSource source = new TestManifestSource();
            PluginManifest manifest = Manifests.Plain(type, Ids, schema);
            source.Add(manifest, defaults);

            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 21UL));

            EditAdmission mount = host.SubmitEdit(
                Payloads.Mount(manifest, instance, Root, local, schemaDefaults: defaults),
                issuer.Next(),
                CompositionRevision.Zero);
            Assert.That(mount.Staged, Is.True, mount.Code.ToString());
            host.Drain();

            InstallSnapshot install = host.FindInstall(instance)!;
            Assert.That(install.State, Is.EqualTo(InstallationState.Active));
            Assert.That(install.Record.ConfigHash, Is.EqualTo(ConfigDocumentCodec.HashOf(Payloads.Effective(defaults, local))));
        }

        [Test]
        public void UndeclaredConfigurationFieldIsRejected()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            SchemaId schema = Ids.Schema();
            Id128 declared = Ids.Capability().Value;
            Id128 undeclared = Ids.Capability().Value;

            TestManifestSource source = new TestManifestSource();
            PluginManifest manifest = Manifests.Plain(type, Ids, schema);
            source.Add(manifest, ConfigDocument.Of(new ConfigField(declared, ConfigFieldValue.OfUInt32(1U))));

            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 22UL));
            ConfigDocument wrong = ConfigDocument.Of(new ConfigField(undeclared, ConfigFieldValue.OfUInt32(9U)));

            EditAdmission mount = host.SubmitEdit(Payloads.Mount(manifest, instance, Root, wrong), issuer.Next(), CompositionRevision.Zero);
            Assert.That(mount.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(host.FindInstall(instance), Is.Null);
        }

        [Test]
        public void DeclaredConfigurationHashMustDescribeTheComposedDocument()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            SchemaId schema = Ids.Schema();
            Id128 field = Ids.Capability().Value;
            ConfigDocument defaults = ConfigDocument.Of(new ConfigField(field, ConfigFieldValue.OfUInt32(1U)));

            TestManifestSource source = new TestManifestSource();
            PluginManifest manifest = Manifests.Plain(type, Ids, schema);
            source.Add(manifest, defaults);

            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 23UL));

            // Same declaration, but the payload claims a different configuration hash: refuse, do not repair.
            CompositionEditPayload honest = Payloads.Mount(manifest, instance, Root, ConfigDocument.Empty, schemaDefaults: defaults);
            CompositionEditPayload dishonest = new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                Root,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                type,
                instance,
                DefinitionRevision.First,
                ContentHash.Empty,
                ConfigDocument.Empty,
                0,
                null,
                PropagationMode.Automatic);

            Assert.That(host.SubmitEdit(dishonest, issuer.Next(), CompositionRevision.Zero).Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(host.SubmitEdit(honest, issuer.Next(), CompositionRevision.Zero).Staged, Is.True);
        }

        [Test]
        public void ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch()
        {
            PluginTypeId type = Ids.Type();
            PluginInstanceId instance = Ids.Instance();
            SchemaId schema = Ids.Schema();
            Id128 limit = Ids.Capability().Value;
            ConfigDocument defaults = ConfigDocument.Of(new ConfigField(limit, ConfigFieldValue.OfUInt32(3U)));

            TestManifestSource source = new TestManifestSource();
            PluginManifest manifest = Manifests.Plain(type, Ids, schema);
            source.Add(manifest, defaults);

            CompositionHost host = CompositionHost.CreateDefault(World, Root, source, null);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 24UL));

            host.SubmitEdit(Payloads.Mount(manifest, instance, Root, ConfigDocument.Empty, schemaDefaults: defaults), issuer.Next(), CompositionRevision.Zero);
            host.Drain();

            InstallRecord before = host.FindInstall(instance)!.Record;
            ConfigDocument previous = Payloads.Effective(defaults, ConfigDocument.Empty);
            ConfigDocument patch = ConfigDocument.Of(new ConfigField(limit, ConfigFieldValue.OfUInt32(5U)));

            EditAdmission reconfigure = host.SubmitEdit(
                Payloads.Reconfigure(manifest, instance, patch, previous, new DefinitionRevision(2UL), defaults),
                issuer.Next(),
                new CompositionRevision(1UL));
            Assert.That(reconfigure.Staged, Is.True, reconfigure.Code.ToString());
            host.Drain();

            InstallRecord after = host.FindInstall(instance)!.Record;
            Assert.That(after.Generation, Is.EqualTo(before.Generation), "An in-place reconfiguration keeps the installation generation (P-005).");
            Assert.That(after.ActivationEpoch, Is.EqualTo(new ActivationEpoch(before.ActivationEpoch.Value + 1UL)));
            Assert.That(after.ConfigRevision, Is.EqualTo(new DefinitionRevision(2UL)));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(2UL));
            Assert.That(host.Snapshot().Epoch.Value, Is.EqualTo(2UL));
            Assert.That(host.Snapshot().Step, Is.EqualTo(LogicalStepId.Zero), "A publication never advances the logical step (P-006).");
        }
    }
}
