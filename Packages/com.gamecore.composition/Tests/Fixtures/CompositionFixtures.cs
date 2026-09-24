// GameCore.Composition tests — deterministic collaborators and payload builders.
//
// Every helper here is a pure function of its arguments: ids come from an explicit ordinal, shuffles use a
// fixed seed, and no helper reads a clock or a static mutable counter. That is what makes the resolution and
// control-lane tests reproducible (P-008) and lets the same files run as Unity EditMode tests and under plain
// dotnet (docs/game-core/04-unity-integration.md s10).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;

namespace GameCore.Composition.Tests
{
    /// <summary>Deterministic 128-bit identities: word 1 is the domain tag, word 2 the ordinal.</summary>
    public sealed class IdFactory
    {
        private readonly ulong domain;
        private ulong next = 1UL;

        public IdFactory(ulong domain)
        {
            this.domain = domain;
        }

        public Id128 NextId() => new Id128(domain, next++);

        public PluginTypeId Type() => new PluginTypeId(NextId());

        public PluginInstanceId Instance() => new PluginInstanceId(NextId());

        public ScopeId Scope() => new ScopeId(NextId());

        public CapabilityId Capability() => new CapabilityId(NextId());

        public SchemaId Schema() => new SchemaId(NextId());

        public SlotId Slot() => new SlotId(NextId());

        public OwnerId Owner() => new OwnerId(NextId());

        public ResourceKey Resource() => new ResourceKey(NextId());

        public FactoryKey Factory() => new FactoryKey(NextId(), 1U);
    }

    /// <summary>Issuer sequence helper: an issuer is one stable id plus strictly increasing sequences (P-050).</summary>
    public sealed class OperationIssuer
    {
        private readonly WorldId world;
        private readonly Id128 issuer;
        private ulong next = 1UL;

        public OperationIssuer(WorldId world, Id128 issuer)
        {
            this.world = world;
            this.issuer = issuer;
        }

        public Id128 Id => issuer;

        /// <summary>The next operation with a strictly greater sequence; the safe path for a new attempt.</summary>
        public OperationId Next() => new OperationId(world, issuer, next++);

        /// <summary>An explicit sequence, used to test reordering and reuse rules.</summary>
        public OperationId At(ulong sequence) => new OperationId(world, issuer, sequence);
    }

    /// <summary>Manifest source double: explicit manifests and configuration schema defaults, no discovery (P-009).</summary>
    public sealed class TestManifestSource : IPluginManifestSource
    {
        private readonly Dictionary<Id128, PluginManifest> manifests = new Dictionary<Id128, PluginManifest>();
        private readonly Dictionary<Id128, ConfigDocument> defaults = new Dictionary<Id128, ConfigDocument>();

        public void Add(PluginManifest manifest, ConfigDocument? configDefaults)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            manifests[manifest.PluginTypeId.Value] = manifest;
            defaults[manifest.ConfigSchema.Id.Value] = configDefaults ?? ConfigDocument.Empty;
        }

        /// <summary>The registered manifest of a type; a test that asks for an unregistered type fails loudly.</summary>
        public PluginManifest ManifestOf(PluginTypeId pluginType)
        {
            if (manifests.TryGetValue(pluginType.Value, out PluginManifest? found))
            {
                return found;
            }

            throw new KeyNotFoundException("No manifest was registered for " + pluginType.ToString() + ".");
        }

        public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
        {
            if (manifests.TryGetValue(pluginType.Value, out PluginManifest? found))
            {
                manifest = found;
                return true;
            }

            manifest = null;
            return false;
        }

        public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? configDefaults)
        {
            if (defaults.TryGetValue(schema.Id.Value, out ConfigDocument? found))
            {
                configDefaults = found;
                return true;
            }

            configDefaults = null;
            return false;
        }
    }

    /// <summary>Managed-resource factory double: counted prepares, deterministic lease ids and scripted faults.</summary>
    public sealed class TestResourceFactory : IManagedResourceFactory
    {
        private readonly IdFactory ids;
        private readonly List<ManagedResourceLease> leases = new List<ManagedResourceLease>();

        public TestResourceFactory(IdFactory ids)
        {
            this.ids = ids;
        }

        public int PrepareCount { get; private set; }

        public int DisposeCount { get; private set; }

        /// <summary>Resource keys whose disposal throws once; a failed release stays retained (P-048).</summary>
        public HashSet<Id128> FailingDisposals { get; } = new HashSet<Id128>();

        /// <summary>When set, the next preparation throws and clears the flag (P-049-style bounded retry fixture).</summary>
        public bool FailNextPrepare { get; set; }

        /// <summary>Resource keys in disposal order, so teardown ordering is observable rather than inferred.</summary>
        public List<Id128> DisposedOrder { get; } = new List<Id128>();

        /// <summary>
        /// True when the consumer's resource was released before the provider's, which is the reverse dependency
        /// order P-048 requires. Both must actually appear in the log.
        /// </summary>
        public bool ConsumerDisposedBeforeProvider(Id128 consumerResource, Id128 providerResource)
        {
            int consumerIndex = DisposedOrder.IndexOf(consumerResource);
            int providerIndex = DisposedOrder.IndexOf(providerResource);
            return consumerIndex >= 0 && providerIndex >= 0 && consumerIndex < providerIndex;
        }

        public FactoryKey DisposerKey { get; set; } = new FactoryKey(new Id128(0x646973706F736572UL, 1UL), 1U);

        public IReadOnlyList<ManagedResourceLease> Leases => leases;

        public IManagedResourceLease Prepare(ManagedResourceRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (FailNextPrepare)
            {
                FailNextPrepare = false;
                throw new ResourcePreparationException(DiagnosticCode.ResourceUnavailable, "Scripted preparation failure.");
            }

            PrepareCount++;
            ResourceKey key = request.Resource;
            Id128 leaseId = ids.NextId();
            ManagedResourceLease lease = new ManagedResourceLease(
                key,
                leaseId,
                request.Token,
                DisposerKey,
                new ManagedResourceGate(),
                disposed => OnDisposed(key, disposed));
            leases.Add(lease);
            return lease;
        }

        private void OnDisposed(ResourceKey key, Id128 leaseId)
        {
            _ = leaseId;
            DisposedOrder.Add(key.Value);
            if (FailingDisposals.Contains(key.Value))
            {
                throw new InvalidOperationException("Scripted disposal failure for resource " + key.ToString());
            }

            DisposeCount++;
        }
    }

    /// <summary>Fluent builders for edit payloads, including the declared configuration hashes they must carry.</summary>
    public static class Payloads
    {
        public static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent, IsolationSet? serviceIsolation = null, IsolationSet? capabilityIsolation = null)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                serviceIsolation ?? new IsolationSet(false, null),
                capabilityIsolation ?? new IsolationSet(false, null),
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        public static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId parent) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeReparent,
                scope,
                parent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        public static CompositionEditPayload ScopeRemove(ScopeId scope, bool destroySubtree) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeRemove,
                scope,
                default(ScopeId),
                destroySubtree,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        public static CompositionEditPayload ScopeIsolation(ScopeId scope, IsolationSet? service, IsolationSet? capability) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeIsolation,
                scope,
                default(ScopeId),
                false,
                service,
                capability,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        public static CompositionEditPayload ScopeGrants(ScopeId scope, IReadOnlyList<CapabilityImport>? imports) =>
            new CompositionEditPayload(
                CompositionEditSubject.ScopeGrants,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                imports,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        public static CompositionEditPayload Mount(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope,
            ConfigDocument? config,
            int priority = 0,
            IReadOnlyList<ServiceSelection>? selections = null,
            DefinitionRevision configRevision = default(DefinitionRevision),
            ConfigDocument? schemaDefaults = null)
        {
            ConfigDocument local = config ?? ConfigDocument.Empty;
            ConfigDocument effective = Effective(schemaDefaults, local);
            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                configRevision.Value == 0UL ? DefinitionRevision.First : configRevision,
                ConfigDocumentCodec.HashOf(effective),
                local,
                priority,
                selections,
                PropagationMode.Automatic);
        }

        public static CompositionEditPayload Reconfigure(
            PluginManifest manifest,
            PluginInstanceId instance,
            ConfigDocument patch,
            ConfigDocument previousEffective,
            DefinitionRevision newRevision,
            ConfigDocument? schemaDefaults = null)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, manifest.ConfigSchema.Id.Value, schemaDefaults ?? ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.InheritedContribution, instance.Value, previousEffective),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, patch),
            });

            return new CompositionEditPayload(
                CompositionEditSubject.InstallReconfigure,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                newRevision,
                ConfigDocumentCodec.HashOf(composed.Value),
                patch,
                0,
                null,
                PropagationMode.Automatic);
        }

        public static CompositionEditPayload Unmount(PluginInstanceId instance) =>
            new CompositionEditPayload(
                CompositionEditSubject.InstallUnmount,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);

        public static CompositionEditPayload Suspend(PluginInstanceId instance) =>
            Lifecycle(CompositionEditSubject.InstallSuspend, instance);

        public static CompositionEditPayload Resume(PluginInstanceId instance) =>
            Lifecycle(CompositionEditSubject.InstallResume, instance);

        public static CompositionEditPayload SetMode(PropagationMode mode, ScopeId scope = default(ScopeId)) =>
            new CompositionEditPayload(
                CompositionEditSubject.ModeSet,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                mode);

        /// <summary>The effective configuration a mount publishes: schema defaults then the local declaration (P-020).</summary>
        public static ConfigDocument Effective(ConfigDocument? schemaDefaults, ConfigDocument local)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, new Id128(0x736368656D61UL, 1UL), schemaDefaults ?? ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, new Id128(0x6C6F63616CUL, 1UL), local),
            });

            return composed.Value;
        }

        private static CompositionEditPayload Lifecycle(CompositionEditSubject subject, PluginInstanceId instance) =>
            new CompositionEditPayload(
                subject,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
               0,
                null,
                PropagationMode.Automatic);
    }

    /// <summary>Deterministic projection of a composition snapshot, for order-independence assertions (P-008).</summary>
    public static class Projection
    {
        public static string Of(CompositionStateSnapshot snapshot)
        {
            List<string> lines = new List<string>();
            lines.Add("mode=" + snapshot.Mode);
            for (int i = 0; i < snapshot.Scopes.Count; i++)
            {
                ScopeSnapshot scope = snapshot.Scopes[i];
                List<string> installs = new List<string>();
                for (int j = 0; j < scope.Installs.Count; j++)
                {
                    installs.Add(scope.Installs[j].ToString());
                }

                lines.Add("scope=" + scope.Scope.ToString() + " parent=" + scope.Parent.ToString() + " installs=[" + string.Join(",", installs) + "]");
            }

            for (int i = 0; i < snapshot.Installs.Count; i++)
            {
                InstallSnapshot install = snapshot.Installs[i];
                List<string> bindings = new List<string>();
                for (int j = 0; j < install.Bindings.Count; j++)
                {
                    ServiceBinding binding = install.Bindings[j];
                    bindings.Add(binding.Contract.ToString() + "->" + binding.Provider.ToString() + (binding.IsFallback ? ":fallback" : string.Empty));
                }

                lines.Add(
                    "install=" + install.Record.Instance.ToString() +
                    " state=" + install.State +
                    " scope=" + install.Record.Scope.ToString() +
                    " bindings=[" + string.Join(",", bindings) + "]");
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }
    }

    /// <summary>Seeded Fisher-Yates shuffle: the same seed always produces the same permutation.</summary>
    public static class Shuffle
    {
        public static List<T> WithSeed<T>(IReadOnlyList<T> source, int seed)
        {
            List<T> copy = new List<T>(source);
            Random random = new Random(seed);
            for (int i = copy.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                T swap = copy[i];
                copy[i] = copy[j];
                copy[j] = swap;
            }

            return copy;
        }
    }
}
