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

        private static CompositionHost NewHost() => NewHostWithSource(out TestManifestSource _);

        private static CompositionHost NewHostWithSource(out TestManifestSource source)
        {
            source = new TestManifestSource();
            return CompositionHost.CreateDefault(World, Root, source, null);
        }

        [Test]
        public void RootScopeIsTheOnlyScopeWithoutAParent()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 11UL));
            ScopeSnapshot? root = host.FindScope(Root);

            Assert.That(root, Is.Not.Null);
            Assert.That(root!.IsRoot, Is.True);
            Assert.That(root.Parent.IsDefault, Is.True);
            Assert.That(host.FindScope(Ids.Scope()), Is.Null, "An unknown scope is never invented.");

            // One rooted acyclic tree per world: a second root is an ownership conflict (P-010).
            ScopeId secondRoot = Ids.Scope();
            EditAdmission extraRoot = host.SubmitEdit(Payloads.ScopeCreate(secondRoot, default(ScopeId)), issuer.Next(), CompositionRevision.Zero);
            Assert.That(extraRoot.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL));

            // The root cannot be removed or moved under one of its own descendants.
            ScopeId child = Ids.Scope();
            Assert.That(host.SubmitEdit(Payloads.ScopeCreate(child, Root), issuer.Next(), CompositionRevision.Zero).Staged, Is.True);
            host.Drain();

            EditAdmission removeRoot = host.SubmitEdit(Payloads.ScopeRemove(Root, true), issuer.Next(), new CompositionRevision(1UL));
            Assert.That(removeRoot.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.FindScope(Root), Is.Not.Null, "The world root stays present.");

            EditAdmission moveRoot = host.SubmitEdit(Payloads.ScopeReparent(Root, child), issuer.Next(), new CompositionRevision(1UL));
            Assert.That(moveRoot.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(1UL));
        }

        [Test]
        public void CreatedScopeCarriesMembershipAndInheritsItsParentDepth()
        {
            CompositionHost host = NewHostWithSource(out TestManifestSource source);
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

            // Membership is real: an installation belongs to exactly one scope, and only that scope lists it
            // (P-010). A descendant is not reported as a member of its ancestor.
            PluginTypeId type = Ids.Type();
            PluginManifest manifest = Manifests.Plain(type, Ids);
            source.Add(manifest, null);
            PluginInstanceId atMarket = Ids.Instance();
            EditAdmission mount = host.SubmitEdit(Payloads.Mount(manifest, atMarket, market, null), issuer.Next(), new CompositionRevision(2UL));
            Assert.That(mount.Staged, Is.True, mount.Code.ToString());
            host.Drain();

            CompositionStateSnapshot after = host.Snapshot();
            Assert.That(after.Revision.Value, Is.EqualTo(3UL));
            Assert.That(after.Scopes.Count, Is.EqualTo(3));
            for (int i = 0; i < after.Scopes.Count; i++)
            {
                ScopeSnapshot scope = after.Scopes[i];
                bool expected = scope.Scope.Equals(market);
                Assert.That(scope.Installs.Count, Is.EqualTo(expected ? 1 : 0), "Scope " + scope.Scope + " membership");
                if (expected)
                {
                    Assert.That(scope.Installs[0], Is.EqualTo(atMarket));
                }
            }

            Assert.That(after.Installs.Count, Is.EqualTo(1));
            Assert.That(after.Installs[0].Record.Scope, Is.EqualTo(market), "A live installation has exactly one owner scope (P-010).");
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
            host.SubmitEdit(Payloads.ScopeCreate(right, left), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(moved, left), issuer.Next(), new CompositionRevision(2UL));
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(leaf, moved), issuer.Next(), new CompositionRevision(3UL));
            host.Drain();

            EditAdmission reparent = host.SubmitEdit(Payloads.ScopeReparent(moved, right), issuer.Next(), new CompositionRevision(4UL));
            Assert.That(reparent.Staged, Is.True);
            host.Drain();

            Assert.That(host.Committed.Scopes.Depth(moved), Is.EqualTo(3));
            Assert.That(host.Committed.Scopes.Depth(leaf), Is.EqualTo(4));
            Assert.That(host.FindScope(moved)!.Parent, Is.EqualTo(right));
            Assert.That(host.FindScope(leaf)!.Scope, Is.EqualTo(leaf), "Moving a subtree preserves descendant identities (P-025).");
        }

        [Test]
        public void RemovingANonemptyScopeRequiresAnExplicitSubtreeDisposition()
        {
            CompositionHost host = NewHostWithSource(out TestManifestSource source);
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 5UL));
            ScopeId parent = Ids.Scope();
            ScopeId child = Ids.Scope();
            PluginTypeId type = Ids.Type();
            PluginManifest manifest = Manifests.Plain(type, Ids);
            source.Add(manifest, null);
            PluginInstanceId insideSubtree = Ids.Instance();

            host.SubmitEdit(Payloads.ScopeCreate(parent, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(child, parent), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();
            host.SubmitEdit(Payloads.Mount(manifest, insideSubtree, child, null), issuer.Next(), new CompositionRevision(2UL));
            host.Drain();
            Assert.That(host.FindInstall(insideSubtree)!.Record.Scope, Is.EqualTo(child));

            // A nonempty scope needs a disposition: child scopes and installations both count (P-010).
            EditAdmission refused = host.SubmitEdit(Payloads.ScopeRemove(parent, false), issuer.Next(), new CompositionRevision(3UL));
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(3UL), "A refused removal publishes nothing.");
            Assert.That(host.FindScope(child), Is.Not.Null);
            Assert.That(host.FindInstall(insideSubtree), Is.Not.Null);

            // DestroySubtree carries the affected targets and installations with it (O-02).
            EditAdmission removed = host.SubmitEdit(Payloads.ScopeRemove(parent, true), issuer.Next(), new CompositionRevision(3UL));
            Assert.That(removed.Staged, Is.True, removed.Code.ToString());
            Assert.That(removed.Plan!.RetiredInstances.Count, Is.EqualTo(1));
            Assert.That(removed.Plan.RetiredInstances[0], Is.EqualTo(insideSubtree));
            host.Drain();

            Assert.That(host.FindScope(parent), Is.Null);
            Assert.That(host.FindScope(child), Is.Null);
            Assert.That(host.FindScope(Root), Is.Not.Null);
            Assert.That(host.FindInstall(insideSubtree), Is.Null);
        }

        [Test]
        public void AnEmptyScopeCanBeRemovedAfterItsMembersAreReparentedOut()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 12UL));
            ScopeId keep = Ids.Scope();
            ScopeId temporary = Ids.Scope();
            ScopeId child = Ids.Scope();

            host.SubmitEdit(Payloads.ScopeCreate(keep, Root), issuer.Next(), CompositionRevision.Zero);
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(temporary, keep), issuer.Next(), new CompositionRevision(1UL));
            host.Drain();
            host.SubmitEdit(Payloads.ScopeCreate(child, temporary), issuer.Next(), new CompositionRevision(2UL));
            host.Drain();

            // An explicit ReparentTo destination is the other lawful disposition: move the member out first.
            EditAdmission move = host.SubmitEdit(Payloads.ScopeReparent(child, keep), issuer.Next(), new CompositionRevision(3UL));
            Assert.That(move.Staged, Is.True, move.Code.ToString());
            host.Drain();
            Assert.That(host.FindScope(child)!.Parent, Is.EqualTo(keep));
            Assert.That(host.Committed.Scopes.Depth(child), Is.EqualTo(1));

            // The scope is now empty, so a plain removal no longer needs a destruction disposition.
            EditAdmission remove = host.SubmitEdit(Payloads.ScopeRemove(temporary, false), issuer.Next(), new CompositionRevision(4UL));
            Assert.That(remove.Staged, Is.True, remove.Code.ToString());
            host.Drain();
            Assert.That(host.FindScope(temporary), Is.Null);
            Assert.That(host.FindScope(child), Is.Not.Null, "The moved-out member is untouched (P-025).");
        }

        [Test]
        public void IsolationSetCannotNameContractsWhileDeclaringAll()
        {
            CompositionHost host = NewHost();
            OperationIssuer issuer = new OperationIssuer(World, new Id128(0x686F7374UL, 6UL));
            IsolationSet contradictory = new IsolationSet(true, new[] { Ids.Capability().Value });

            EditAdmission refused = host.SubmitEdit(Payloads.ScopeIsolation(Root, contradictory, null), issuer.Next(), CompositionRevision.Zero);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(refused.Entry!.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(host.Snapshot().Revision.Value, Is.EqualTo(0UL), "A contradictory declaration publishes nothing.");
            Assert.That(host.FindScope(Root)!.ServiceIsolation.AllContracts, Is.False, "The previous root isolation stands.");
            Assert.That(host.Drain(), Is.Empty);
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
            Assert.That(set.Kind, Is.EqualTo(ConfigValueKind.IdSet));
            Assert.That(set.AsIds.Count, Is.EqualTo(2), "An id set field is composed as a canonical set union.");
            Assert.That(set.AsIds[0], Is.EqualTo(tagA.CompareTo(tagB) <= 0 ? tagA : tagB), "A union is stored in canonical order.");
            Assert.That(set.AsIds[1], Is.EqualTo(tagA.CompareTo(tagB) <= 0 ? tagB : tagA));
            Assert.That(
                composed.Value.TryGetField(tagA, out ConfigFieldValue _),
                Is.False,
                "A union produces one field, not one field per element.");

            // Provenance names the winning layer for every field, and only for fields that exist.
            Assert.That(composed.Provenance.Count, Is.EqualTo(2));
            for (int i = 0; i < composed.Provenance.Count; i++)
            {
                Assert.That(composed.Provenance[i].Origin, Is.EqualTo(ConfigLayerOrigin.LocalPatch));
                Assert.That(composed.Provenance[i].Source, Is.EqualTo(new Id128(2UL, 2UL)));
                Assert.That(composed.Provenance[i].IsExplicitNull, Is.False);
            }

            // An explicit null clears the field; omitting the key leaves the inherited value alone.
            ConfigDocument clear = ConfigDocument.Clear(limit);
            ConfigComposeResult cleared = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, new Id128(1UL, 1UL), defaults),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, new Id128(2UL, 2UL), clear),
            });

            Assert.That(cleared.Value.HasField(limit), Is.False);
            Assert.That(cleared.Value.HasField(tags), Is.True, "A field the patch omits keeps its inherited value.");
            Assert.That(cleared.Value.TryGetField(tags, out ConfigFieldValue inherited), Is.True);
            Assert.That(inherited.AsIds.Count, Is.EqualTo(1));
            Assert.That(cleared.Provenance.Count, Is.EqualTo(2));
            for (int i = 0; i < cleared.Provenance.Count; i++)
            {
                bool isClearedField = cleared.Provenance[i].Key.Equals(limit);
                Assert.That(cleared.Provenance[i].IsExplicitNull, Is.EqualTo(isClearedField), "Explicit null is recorded, not hidden.");
                Assert.That(
                    cleared.Provenance[i].Origin,
                    Is.EqualTo(isClearedField ? ConfigLayerOrigin.LocalPatch : ConfigLayerOrigin.SchemaDefaults));
            }
        }

        [Test]
        public void EveryFieldKindRoundTripsThroughTheCanonicalDocument()
        {
            Id128 u64Key = Ids.Capability().Value;
            Id128 i32Key = Ids.Capability().Value;
            Id128 i64Key = Ids.Capability().Value;
            Id128 boolKey = Ids.Capability().Value;
            Id128 idKey = Ids.Capability().Value;
            Id128 bytesKey = Ids.Capability().Value;
            Id128 textKey = Ids.Capability().Value;
            Id128 orderedKey = Ids.Capability().Value;
            Id128 setKey = Ids.Capability().Value;
            Id128 nullKey = Ids.Capability().Value;

            ConfigDocument document = ConfigDocument.Of(
                new ConfigField(u64Key, ConfigFieldValue.OfUInt64(ulong.MaxValue)),
                new ConfigField(i32Key, ConfigFieldValue.OfInt32(int.MinValue)),
                new ConfigField(i64Key, ConfigFieldValue.OfInt64(long.MinValue)),
                new ConfigField(boolKey, ConfigFieldValue.OfBool(true)),
                new ConfigField(idKey, ConfigFieldValue.OfId(new Id128(0xABUL, 0xCDUL))),
                new ConfigField(bytesKey, ConfigFieldValue.OfBytes(new FrozenPayload(new byte[] { 1, 2, 3 }))),
                new ConfigField(textKey, ConfigFieldValue.OfText("chapter-1")),
                new ConfigField(orderedKey, ConfigFieldValue.OfOrderedIds(new[] { new Id128(9UL, 9UL), new Id128(1UL, 1UL) })),
                new ConfigField(setKey, ConfigFieldValue.OfIdSet(new[] { new Id128(5UL, 5UL), new Id128(2UL, 2UL), new Id128(5UL, 5UL) })),
                new ConfigField(nullKey, ConfigFieldValue.Null));

            FrozenPayload encoded = ConfigDocumentCodec.Encode(document);
            Assert.That(ConfigDocumentCodec.TryDecode(encoded, out ConfigDocument? decoded), Is.True);

            Assert.That(decoded!.Count, Is.EqualTo(document.Count));
            for (int i = 0; i < document.Count; i++)
            {
                Assert.That(decoded.Fields[i].Key, Is.EqualTo(document.Fields[i].Key), "Canonical field order is preserved.");
                Assert.That(decoded.Fields[i].Value.Kind, Is.EqualTo(document.Fields[i].Value.Kind), "Field " + i + " kind");
                Assert.That(decoded.Fields[i].Value, Is.EqualTo(document.Fields[i].Value), "Field " + i + " value");
            }

            // Each kind survives with its exact payload, not merely its kind tag.
            Assert.That(ValueOf(decoded, u64Key).AsUInt64, Is.EqualTo(ulong.MaxValue));
            Assert.That(ValueOf(decoded, i32Key).AsInt32, Is.EqualTo(int.MinValue));
            Assert.That(ValueOf(decoded, i64Key).AsInt64, Is.EqualTo(long.MinValue));
            Assert.That(ValueOf(decoded, boolKey).AsBool, Is.True);
            Assert.That(ValueOf(decoded, idKey).AsId, Is.EqualTo(new Id128(0xABUL, 0xCDUL)));
            Assert.That(ValueOf(decoded, bytesKey).AsBytes!.Length, Is.EqualTo(3));
            Assert.That(ValueOf(decoded, bytesKey).AsBytes!.Bytes[2], Is.EqualTo((byte)3));
            Assert.That(ValueOf(decoded, textKey).AsText, Is.EqualTo("chapter-1"));
            Assert.That(ValueOf(decoded, nullKey).Kind, Is.EqualTo(ConfigValueKind.Null), "An explicit null keeps its kind.");

            // An ordered array keeps its declared order; an id set is canonical and duplicate-free.
            ConfigFieldValue ordered = ValueOf(decoded, orderedKey);
            Assert.That(ordered.Kind, Is.EqualTo(ConfigValueKind.OrderedIds));
            Assert.That(ordered.AsIds.Count, Is.EqualTo(2));
            Assert.That(ordered.AsIds[0], Is.EqualTo(new Id128(9UL, 9UL)));
            Assert.That(ordered.AsIds[1], Is.EqualTo(new Id128(1UL, 1UL)));

            ConfigFieldValue set = ValueOf(decoded, setKey);
            Assert.That(set.AsIds.Count, Is.EqualTo(2), "The set drops its duplicate.");
            Assert.That(set.AsIds[0], Is.EqualTo(new Id128(2UL, 2UL)));
            Assert.That(set.AsIds[1], Is.EqualTo(new Id128(5UL, 5UL)));

            // Field order is a property of the document, not of the declaration: the same field set encodes
            // and hashes identically whatever order the caller listed it in.
            ConfigDocument forward = ConfigDocument.Of(
                new ConfigField(u64Key, ConfigFieldValue.OfUInt64(1UL)),
                new ConfigField(textKey, ConfigFieldValue.OfText("x")));
            ConfigDocument reversed = ConfigDocument.Of(
                new ConfigField(textKey, ConfigFieldValue.OfText("x")),
                new ConfigField(u64Key, ConfigFieldValue.OfUInt64(1UL)));

            Assert.That(ConfigDocumentCodec.HashOf(reversed), Is.EqualTo(ConfigDocumentCodec.HashOf(forward)));
            Assert.That(decoded.Fields[0].Key.CompareTo(decoded.Fields[1].Key), Is.LessThan(0), "Fields are in ascending key order.");

            // A structurally broken document is refused rather than partially applied.
            byte[] truncated = DocumentCodec.ToBytes(encoded);
            Assert.That(ConfigDocumentCodec.TryDecode(new FrozenPayload(new byte[truncated.Length - 1]), out ConfigDocument? _), Is.False);
            truncated[truncated.Length - 1] = (byte)(truncated[truncated.Length - 1] ^ 0xFF);
            Assert.That(ConfigDocumentCodec.TryDecode(new FrozenPayload(truncated), out ConfigDocument? _), Is.False, "A tampered checksum is detected.");
        }

        private static ConfigFieldValue ValueOf(ConfigDocument document, Id128 key)
        {
            Assert.That(document.TryGetField(key, out ConfigFieldValue value), Is.True, "Field " + key + " must be present.");
            return value;
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

            // The stored configuration is the composed effective document, asserted value by value rather than
            // only through the hash the same helper produced (P-020).
            InstallEntry entry = host.Committed.Installs[0];
            Assert.That(entry.Config.Count, Is.EqualTo(1));
            Assert.That(entry.Config.TryGetField(limit, out ConfigFieldValue stagedValue), Is.True);
            Assert.That(stagedValue.Kind, Is.EqualTo(ConfigValueKind.UInt32));
            Assert.That(stagedValue.AsUInt32, Is.EqualTo(7U), "The local declaration wins over the schema default.");
            Assert.That(entry.Record.ConfigRevision, Is.EqualTo(DefinitionRevision.First));
            Assert.That(entry.Selections, Is.Empty);
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
