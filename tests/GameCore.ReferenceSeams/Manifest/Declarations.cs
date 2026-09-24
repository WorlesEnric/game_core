// Test-only reference seam for the shared GameCore.Contracts surface (see TestOnlyMarker.cs).
// Manifest declaration shape from docs/game-core/05-contracts-and-data-model.md s3 and P-009/P-039/P-040/P-043.
// Pure immutable data: validation, catalog conflict detection and derivation live in later tasks.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>Direction of a declared buffer port (P-043).</summary>
    public enum PortDirection
    {
        Producer = 0,
        Consumer = 1,
    }

    /// <summary>Declared service export of a plugin instance (P-009, P-011).</summary>
    public sealed class ServiceExport
    {
        public ServiceExport(
            ContractRef contract,
            FactoryKey factory,
            ServiceVisibility visibility,
            bool overridesAncestor,
            ServiceBindingKind bindingKind)
        {
            Contract = contract;
            Factory = factory;
            Visibility = visibility;
            OverridesAncestor = overridesAncestor;
            BindingKind = bindingKind;
        }

        public ContractRef Contract { get; }

        public FactoryKey Factory { get; }

        public ServiceVisibility Visibility { get; }

        public bool OverridesAncestor { get; }

        public ServiceBindingKind BindingKind { get; }
    }

    /// <summary>Declared service dependency of a plugin instance (P-011, P-012).</summary>
    public sealed class ServiceDependency
    {
        public ServiceDependency(
            ContractRef contract,
            VersionRange supportedVersions,
            bool required,
            ServiceResolutionDomain domain,
            ProviderInstallationId selectedProvider,
            FactoryKey fallbackFactory)
        {
            Contract = contract;
            SupportedVersions = supportedVersions;
            Required = required;
            Domain = domain;
            SelectedProvider = selectedProvider;
            FallbackFactory = fallbackFactory;
        }

        public ContractRef Contract { get; }

        public VersionRange SupportedVersions { get; }

        public bool Required { get; }

        public ServiceResolutionDomain Domain { get; }

        /// <summary>Explicitly selected provider inside the visibility boundary; default means unset (P-011).</summary>
        public ProviderInstallationId SelectedProvider { get; }

        public bool HasSelectedProvider => !SelectedProvider.IsDefault;

        /// <summary>Declared fallback for an optional binding; default registration key means unset (P-012).</summary>
        public FactoryKey FallbackFactory { get; }

        public bool HasFallback => !FallbackFactory.RegistrationKey.IsDefault;
    }

    /// <summary>Capability contract: stratum, output slots and per-slot composition policy (05 s3, P-017).</summary>
    public sealed class CapabilityContract
    {
        public CapabilityContract(
            CapabilityRef capability,
            int stratum,
            IReadOnlyList<OutputSlotSchema>? outputSlots,
            IReadOnlyList<SlotCompositionPolicy>? slotPolicies,
            IReadOnlyList<CapabilityId>? incompatibleCapabilities)
        {
            Capability = capability;
            Stratum = stratum;
            OutputSlots = ContractCollections.Freeze(outputSlots);
            SlotPolicies = ContractCollections.Freeze(slotPolicies);
            IncompatibleCapabilities = ContractCollections.Freeze(incompatibleCapabilities);
        }

        public CapabilityRef Capability { get; }

        public int Stratum { get; }

        public IReadOnlyList<OutputSlotSchema> OutputSlots { get; }

        public IReadOnlyList<SlotCompositionPolicy> SlotPolicies { get; }

        public IReadOnlyList<CapabilityId> IncompatibleCapabilities { get; }
    }

    /// <summary>Finite derivation rule from lower-stratum inputs to one output capability (05 s3, P-017).</summary>
    public sealed class DerivationRule
    {
        public DerivationRule(
            RuleId ruleId,
            CapabilityRef outputCapability,
            int outputStratum,
            uint maxOutputSlots,
            IReadOnlyList<SchemaRef>? selectorContracts,
            FactoryKey staticPredicate,
            IReadOnlyList<CapabilityRef>? inputCapabilities,
            PropagationReach reach,
            bool exportToDescendants,
            int priority,
            CompositionPolicy policy,
            FrozenPayload payloadDefinition)
        {
            RuleId = ruleId;
            OutputCapability = outputCapability;
            OutputStratum = outputStratum;
            MaxOutputSlots = maxOutputSlots;
            SelectorContracts = ContractCollections.Freeze(selectorContracts);
            StaticPredicate = staticPredicate;
            InputCapabilities = ContractCollections.Freeze(inputCapabilities);
            Reach = reach;
            ExportToDescendants = exportToDescendants;
            Priority = priority;
            Policy = policy;
            PayloadDefinition = payloadDefinition;
        }

        public RuleId RuleId { get; }

        public CapabilityRef OutputCapability { get; }

        public int OutputStratum { get; }

        public uint MaxOutputSlots { get; }

        public IReadOnlyList<SchemaRef> SelectorContracts { get; }

        public FactoryKey StaticPredicate { get; }

        public IReadOnlyList<CapabilityRef> InputCapabilities { get; }

        public PropagationReach Reach { get; }

        public bool ExportToDescendants { get; }

        public int Priority { get; }

        public CompositionPolicy Policy { get; }

        public FrozenPayload PayloadDefinition { get; }
    }

    /// <summary>Immutable reusable target compatibility descriptor (05 s3, P-015).</summary>
    public sealed class TargetDescriptor
    {
        public TargetDescriptor(
            DefinitionRef recipe,
            IReadOnlyList<SchemaRef>? supportedSchemas,
            IReadOnlyList<CapabilityRef>? supportedCapabilities,
            IReadOnlyList<Id128>? tags,
            AssetAdapterDescriptor assetAdapter,
            IReadOnlyList<DefinitionRef>? localPatches,
            IReadOnlyList<CapabilityImport>? imports,
            IReadOnlyList<TargetOptIn>? optIns,
            IReadOnlyList<ExclusionRule>? exclusions)
        {
            Recipe = recipe;
            SupportedSchemas = ContractCollections.Freeze(supportedSchemas);
            SupportedCapabilities = ContractCollections.Freeze(supportedCapabilities);
            Tags = ContractCollections.Freeze(tags);
            AssetAdapter = assetAdapter;
            LocalPatches = ContractCollections.Freeze(localPatches);
            Imports = ContractCollections.Freeze(imports);
            OptIns = ContractCollections.Freeze(optIns);
            Exclusions = ContractCollections.Freeze(exclusions);
        }

        public DefinitionRef Recipe { get; }

        public IReadOnlyList<SchemaRef> SupportedSchemas { get; }

        public IReadOnlyList<CapabilityRef> SupportedCapabilities { get; }

        public IReadOnlyList<Id128> Tags { get; }

        public AssetAdapterDescriptor AssetAdapter { get; }

        public IReadOnlyList<DefinitionRef> LocalPatches { get; }

        public IReadOnlyList<CapabilityImport> Imports { get; }

        public IReadOnlyList<TargetOptIn> OptIns { get; }

        public IReadOnlyList<ExclusionRule> Exclusions { get; }
    }

    /// <summary>State slot owned by one registered owner, with lifecycle policies (05 s3, P-021).</summary>
    public sealed class StateSlotSpec
    {
        public StateSlotSpec(
            SlotId slotId,
            OwnerId owner,
            SchemaRef schema,
            FactoryKey physicalLayoutKey,
            IReadOnlyList<FieldOwnership>? fieldOwnership,
            FactoryKey initPolicy,
            FactoryKey configChangePolicy,
            FactoryKey versionChangePolicy,
            LastSupportPolicy lastSupport,
            FactoryKey transferPolicy,
            IReadOnlyList<FactoryKey>? migrationKeys)
        {
            SlotId = slotId;
            Owner = owner;
            Schema = schema;
            PhysicalLayoutKey = physicalLayoutKey;
            FieldOwnership = ContractCollections.Freeze(fieldOwnership);
            InitPolicy = initPolicy;
            ConfigChangePolicy = configChangePolicy;
            VersionChangePolicy = versionChangePolicy;
            LastSupport = lastSupport;
            TransferPolicy = transferPolicy;
            MigrationKeys = ContractCollections.Freeze(migrationKeys);
        }

        public SlotId SlotId { get; }

        public OwnerId Owner { get; }

        public SchemaRef Schema { get; }

        public FactoryKey PhysicalLayoutKey { get; }

        public IReadOnlyList<FieldOwnership> FieldOwnership { get; }

        public FactoryKey InitPolicy { get; }

        public FactoryKey ConfigChangePolicy { get; }

        public FactoryKey VersionChangePolicy { get; }

        public LastSupportPolicy LastSupport { get; }

        public FactoryKey TransferPolicy { get; }

        public IReadOnlyList<FactoryKey> MigrationKeys { get; }
    }

    /// <summary>One system entry inside a stage, with its own access set and inner-DAG edges (P-039).</summary>
    public sealed class SystemSpec
    {
        public SystemSpec(
            FactoryKey systemKey,
            SystemMultiplicity multiplicity,
            AccessSet access,
            IReadOnlyList<FactoryKey>? requiredBeforeSystems,
            IReadOnlyList<FactoryKey>? requiredAfterSystems,
            IReadOnlyList<FactoryKey>? optionalBeforeSystems,
            IReadOnlyList<FactoryKey>? optionalAfterSystems)
        {
            SystemKey = systemKey;
            Multiplicity = multiplicity;
            Access = access;
            RequiredBeforeSystems = ContractCollections.Freeze(requiredBeforeSystems);
            RequiredAfterSystems = ContractCollections.Freeze(requiredAfterSystems);
            OptionalBeforeSystems = ContractCollections.Freeze(optionalBeforeSystems);
            OptionalAfterSystems = ContractCollections.Freeze(optionalAfterSystems);
        }

        public FactoryKey SystemKey { get; }

        public SystemMultiplicity Multiplicity { get; }

        public AccessSet Access { get; }

        public IReadOnlyList<FactoryKey> RequiredBeforeSystems { get; }

        public IReadOnlyList<FactoryKey> RequiredAfterSystems { get; }

        public IReadOnlyList<FactoryKey> OptionalBeforeSystems { get; }

        public IReadOnlyList<FactoryKey> OptionalAfterSystems { get; }
    }

    /// <summary>One declared buffer port of a stage (P-039, P-043).</summary>
    public readonly struct BufferPort
    {
        public readonly BufferId Buffer;
        public readonly PortDirection Direction;
        public readonly StageId Stage;

        public BufferPort(BufferId buffer, PortDirection direction, StageId stage)
        {
            Buffer = buffer;
            Direction = direction;
            Stage = stage;
        }

        public override string ToString() => Buffer.ToString() + ":" + Direction.ToString();
    }

    /// <summary>Stage registration declaration (05 s3, P-039).</summary>
    public sealed class StageSpec
    {
        public StageSpec(
            StageId stageId,
            uint stageVersion,
            Id128 ownerPackageId,
            HostAffinity affinity,
            IReadOnlyList<FactoryKey>? factoryKeys,
            IReadOnlyList<Id128>? activationMemberships,
            AccessSet readWriteSet,
            IReadOnlyList<StageId>? requiredBefore,
            IReadOnlyList<StageId>? requiredAfter,
            IReadOnlyList<StageId>? optionalBefore,
            IReadOnlyList<StageId>? optionalAfter,
            IReadOnlyList<SystemSpec>? systems,
            IReadOnlyList<BufferPort>? bufferPorts)
        {
            StageId = stageId;
            StageVersion = stageVersion;
            OwnerPackageId = ownerPackageId;
            Affinity = affinity;
            FactoryKeys = ContractCollections.Freeze(factoryKeys);
            ActivationMemberships = ContractCollections.Freeze(activationMemberships);
            ReadWriteSet = readWriteSet;
            RequiredBefore = ContractCollections.Freeze(requiredBefore);
            RequiredAfter = ContractCollections.Freeze(requiredAfter);
            OptionalBefore = ContractCollections.Freeze(optionalBefore);
            OptionalAfter = ContractCollections.Freeze(optionalAfter);
            Systems = ContractCollections.Freeze(systems);
            BufferPorts = ContractCollections.Freeze(bufferPorts);
        }

        public StageId StageId { get; }

        public uint StageVersion { get; }

        public Id128 OwnerPackageId { get; }

        public HostAffinity Affinity { get; }

        /// <summary>Stage-level generated factory keys; per-system keys live on each <see cref="SystemSpec"/>.</summary>
        public IReadOnlyList<FactoryKey> FactoryKeys { get; }

        /// <summary>Declared activation membership keys; an empty list is explicit (P-009, P-039).</summary>
        public IReadOnlyList<Id128> ActivationMemberships { get; }

        public AccessSet ReadWriteSet { get; }

        public IReadOnlyList<StageId> RequiredBefore { get; }

        public IReadOnlyList<StageId> RequiredAfter { get; }

        public IReadOnlyList<StageId> OptionalBefore { get; }

        public IReadOnlyList<StageId> OptionalAfter { get; }

        public IReadOnlyList<SystemSpec> Systems { get; }

        public IReadOnlyList<BufferPort> BufferPorts { get; }
    }

    /// <summary>Buffer contract: one consuming owner, order key, bounds and overflow policy (05 s3, P-043).</summary>
    public sealed class BufferSpec
    {
        public BufferSpec(
            BufferId bufferId,
            SchemaRef schema,
            IReadOnlyList<FactoryKey>? producers,
            StageId ownerStage,
            StageId consumerStage,
            FactoryKey orderKey,
            BufferLifetime lifetime,
            int capacity,
            BufferOverflowPolicy overflow,
            BufferCancellationPolicy cancellation)
        {
            BufferId = bufferId;
            Schema = schema;
            Producers = ContractCollections.Freeze(producers);
            OwnerStage = ownerStage;
            ConsumerStage = consumerStage;
            OrderKey = orderKey;
            Lifetime = lifetime;
            Capacity = capacity;
            Overflow = overflow;
            Cancellation = cancellation;
        }

        public BufferId BufferId { get; }

        public SchemaRef Schema { get; }

        public IReadOnlyList<FactoryKey> Producers { get; }

        public StageId OwnerStage { get; }

        public StageId ConsumerStage { get; }

        public FactoryKey OrderKey { get; }

        public BufferLifetime Lifetime { get; }

        public int Capacity { get; }

        public BufferOverflowPolicy Overflow { get; }

        public BufferCancellationPolicy Cancellation { get; }
    }

    /// <summary>Registered managed-resource factory and its lifetime/cleanup declaration (05 s3, P-048).</summary>
    public sealed class ResourceSpec
    {
        public ResourceSpec(
            ResourceKey resourceKey,
            FactoryKey factory,
            IReadOnlyList<ResourceKey>? dependencies,
            FactoryKey preparationGate,
            OwnerId lifetimeOwner,
            FactoryKey disposer,
            FailureClassification failure)
        {
            ResourceKey = resourceKey;
            Factory = factory;
            Dependencies = ContractCollections.Freeze(dependencies);
            PreparationGate = preparationGate;
            LifetimeOwner = lifetimeOwner;
            Disposer = disposer;
            Failure = failure;
        }

        public ResourceKey ResourceKey { get; }

        public FactoryKey Factory { get; }

        public IReadOnlyList<ResourceKey> Dependencies { get; }

        public FactoryKey PreparationGate { get; }

        public OwnerId LifetimeOwner { get; }

        public FactoryKey Disposer { get; }

        public FailureClassification Failure { get; }
    }
}
