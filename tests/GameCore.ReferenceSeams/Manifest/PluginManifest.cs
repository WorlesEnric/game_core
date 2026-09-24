// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Canonical manifest/installation shape for docs/game-core/05-contracts-and-data-model.md s3 (P-009).
// Pure immutable data; catalog validation and activation policy belong to GC-003/GC-004.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// Versioned declarative plugin manifest. Every declaration array is explicit; an empty category
    /// means "none", never "discover later" (P-009).
    /// </summary>
    public sealed class PluginManifest
    {
        public PluginManifest(
            PluginTypeId pluginTypeId,
            string packageVersion,
            ContentHash packageContentHash,
            SupportedProtocolRange protocolRange,
            IReadOnlyList<Id128>? requiredFeatureIds,
            SchemaRef configSchema,
            FactoryKey factoryKey,
            IReadOnlyList<ServiceExport>? serviceExports,
            IReadOnlyList<ServiceDependency>? serviceDependencies,
            IReadOnlyList<CapabilityContract>? capabilityContracts,
            IReadOnlyList<DerivationRule>? derivationRules,
            IReadOnlyList<TargetDescriptor>? targetDescriptors,
            IReadOnlyList<StateSlotSpec>? stateSlots,
            IReadOnlyList<StageSpec>? stages,
            IReadOnlyList<BufferSpec>? buffers,
            IReadOnlyList<ResourceSpec>? resources)
        {
            PluginTypeId = pluginTypeId;
            PackageVersion = packageVersion ?? throw new ArgumentNullException(nameof(packageVersion));
            PackageContentHash = packageContentHash;
            ProtocolRange = protocolRange;
            RequiredFeatureIds = ContractCollections.Freeze(requiredFeatureIds);
            ConfigSchema = configSchema;
            FactoryKey = factoryKey;
            ServiceExports = ContractCollections.Freeze(serviceExports);
            ServiceDependencies = ContractCollections.Freeze(serviceDependencies);
            CapabilityContracts = ContractCollections.Freeze(capabilityContracts);
            DerivationRules = ContractCollections.Freeze(derivationRules);
            TargetDescriptors = ContractCollections.Freeze(targetDescriptors);
            StateSlots = ContractCollections.Freeze(stateSlots);
            Stages = ContractCollections.Freeze(stages);
            Buffers = ContractCollections.Freeze(buffers);
            Resources = ContractCollections.Freeze(resources);
        }

        public PluginTypeId PluginTypeId { get; }

        public string PackageVersion { get; }

        public ContentHash PackageContentHash { get; }

        public SupportedProtocolRange ProtocolRange { get; }

        public IReadOnlyList<Id128> RequiredFeatureIds { get; }

        public SchemaRef ConfigSchema { get; }

        public FactoryKey FactoryKey { get; }

        public IReadOnlyList<ServiceExport> ServiceExports { get; }

        public IReadOnlyList<ServiceDependency> ServiceDependencies { get; }

        public IReadOnlyList<CapabilityContract> CapabilityContracts { get; }

        public IReadOnlyList<DerivationRule> DerivationRules { get; }

        public IReadOnlyList<TargetDescriptor> TargetDescriptors { get; }

        public IReadOnlyList<StateSlotSpec> StateSlots { get; }

        public IReadOnlyList<StageSpec> Stages { get; }

        public IReadOnlyList<BufferSpec> Buffers { get; }

        public IReadOnlyList<ResourceSpec> Resources { get; }
    }

    /// <summary>
    /// One explicit saved installation: plugin instance, owning scope, config revision and blob,
    /// priority and instance service selections. Identity is stored assembly data, never derived from
    /// creation order (P-004, P-009).
    /// </summary>
    public sealed class InstallationSpec
    {
        public InstallationSpec(
            PluginInstanceId pluginInstanceId,
            ScopeId scope,
            DefinitionRevision configRevision,
            FrozenPayload configBlob,
            int priority,
            IReadOnlyList<ServiceSelection>? serviceSelections)
        {
            PluginInstanceId = pluginInstanceId;
            Scope = scope;
            ConfigRevision = configRevision;
            ConfigBlob = configBlob ?? throw new ArgumentNullException(nameof(configBlob));
            Priority = priority;
            ServiceSelections = ContractCollections.Freeze(serviceSelections);
        }

        public PluginInstanceId PluginInstanceId { get; }

        public ScopeId Scope { get; }

        public DefinitionRevision ConfigRevision { get; }

        public FrozenPayload ConfigBlob { get; }

        public int Priority { get; }

        public IReadOnlyList<ServiceSelection> ServiceSelections { get; }
    }
}
