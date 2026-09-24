// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>One scope edit with its old/new parent (05 s4 composition delta).</summary>
    public sealed class ScopeEdit
    {
        public ScopeEdit(CompositionEditKind kind, ScopeId scope, ScopeId oldParent, ScopeId newParent)
        {
            Kind = kind;
            Scope = scope;
            OldParent = oldParent;
            NewParent = newParent;
        }

        public CompositionEditKind Kind { get; }

        public ScopeId Scope { get; }

        public ScopeId OldParent { get; }

        public ScopeId NewParent { get; }
    }

    /// <summary>One installation edit with its old/new scope and lifecycle state (05 s4).</summary>
    public sealed class InstallEdit
    {
        public InstallEdit(
            CompositionEditKind kind,
            PluginInstanceId instance,
            ScopeId oldScope,
            ScopeId newScope,
            InstallationState oldState,
            InstallationState newState)
        {
            Kind = kind;
            Instance = instance;
            OldScope = oldScope;
            NewScope = newScope;
            OldState = oldState;
            NewState = newState;
        }

        public CompositionEditKind Kind { get; }

        public PluginInstanceId Instance { get; }

        public ScopeId OldScope { get; }

        public ScopeId NewScope { get; }

        public InstallationState OldState { get; }

        public InstallationState NewState { get; }
    }

    /// <summary>One target membership edit (05 s4).</summary>
    public sealed class MembershipEdit
    {
        public MembershipEdit(TargetId target, ScopeId oldScope, ScopeId newScope)
        {
            Target = target;
            OldScope = oldScope;
            NewScope = newScope;
        }

        public TargetId Target { get; }

        public ScopeId OldScope { get; }

        public ScopeId NewScope { get; }
    }

    /// <summary>One configuration edit with its old/new revision and hash (05 s4).</summary>
    public sealed class ConfigEdit
    {
        public ConfigEdit(PluginInstanceId instance, DefinitionRevision oldRevision, DefinitionRevision newRevision, ContentHash oldHash, ContentHash newHash)
        {
            Instance = instance;
            OldRevision = oldRevision;
            NewRevision = newRevision;
            OldHash = oldHash;
            NewHash = newHash;
        }

        public PluginInstanceId Instance { get; }

        public DefinitionRevision OldRevision { get; }

        public DefinitionRevision NewRevision { get; }

        public ContentHash OldHash { get; }

        public ContentHash NewHash { get; }
    }

    /// <summary>
    /// Propagation-mode edit. The setting is world-level (P-013); <see cref="Scope"/> records the scope the
    /// proposal was declared against, which is the world root in V1 and the only valid owner of the setting.
    /// </summary>
    public readonly struct ModeEdit
    {
        public readonly ScopeId Scope;
        public readonly PropagationMode OldMode;
        public readonly PropagationMode NewMode;

        public ModeEdit(ScopeId scope, PropagationMode oldMode, PropagationMode newMode)
        {
            Scope = scope;
            OldMode = oldMode;
            NewMode = newMode;
        }

        /// <summary>True when the edit was declared at the world root rather than a descendant scope.</summary>
        public bool IsWorldSetting => Scope.IsDefault;

        public bool IsNoChange => OldMode == NewMode;

        public override string ToString() =>
            (IsWorldSetting ? "world" : Scope.ToString()) + ": " + OldMode.ToString() + " -> " + NewMode.ToString();
    }

    /// <summary>Complete composition delta of one plan (05 s4).</summary>
    public sealed class CompositionDelta
    {
        public CompositionDelta(
            IReadOnlyList<ScopeEdit>? scopes,
            IReadOnlyList<InstallEdit>? installs,
            IReadOnlyList<MembershipEdit>? memberships,
            IReadOnlyList<ConfigEdit>? configs,
            ModeEdit? mode)
        {
            Scopes = ContractCollections.Freeze(scopes);
            Installs = ContractCollections.Freeze(installs);
            Memberships = ContractCollections.Freeze(memberships);
            Configs = ContractCollections.Freeze(configs);
            Mode = mode;
        }

        public IReadOnlyList<ScopeEdit> Scopes { get; }

        public IReadOnlyList<InstallEdit> Installs { get; }

        public IReadOnlyList<MembershipEdit> Memberships { get; }

        public IReadOnlyList<ConfigEdit> Configs { get; }

        /// <summary>Null when the plan does not change the world-level mode.</summary>
        public ModeEdit? Mode { get; }
    }

    /// <summary>Effective change to one state slot (05 s4 derivation delta).</summary>
    public sealed class SlotDelta
    {
        public SlotDelta(StateSlotKey slot, StateDispositionKind disposition, CompositionPolicy policy, uint oldVersion, uint newVersion)
        {
            Slot = slot;
            Disposition = disposition;
            Policy = policy;
            OldVersion = oldVersion;
            NewVersion = newVersion;
        }

        public StateSlotKey Slot { get; }

        public StateDispositionKind Disposition { get; }

        public CompositionPolicy Policy { get; }

        public uint OldVersion { get; }

        public uint NewVersion { get; }
    }

    /// <summary>One provider support of one state slot (05 s4).</summary>
    public readonly struct SupportRecord
    {
        public readonly StateSlotKey Slot;
        public readonly ProviderInstallationId Provider;
        public readonly RuleId Rule;
        public readonly CapabilityId Capability;

        public SupportRecord(StateSlotKey slot, ProviderInstallationId provider, RuleId rule, CapabilityId capability)
        {
            Slot = slot;
            Provider = provider;
            Rule = rule;
            Capability = capability;
        }

        public bool Equals(SupportRecord other) =>
            Slot.Equals(other.Slot) && Provider.Equals(other.Provider) && Rule.Equals(other.Rule) && Capability.Equals(other.Capability);

        public override bool Equals(object? obj) => obj is SupportRecord other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Slot.GetHashCode();
                hash = (hash * 31) + Provider.GetHashCode();
                hash = (hash * 31) + Rule.GetHashCode();
                hash = (hash * 31) + Capability.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => Capability.ToString() + "@" + Slot.ToString();
    }

    /// <summary>Reference into retained explanation records (05 s4, P-029).</summary>
    public readonly struct ExplanationReference
    {
        public readonly TargetId Target;
        public readonly CapabilityId Capability;
        public readonly Id128 ExplanationKey;

        public ExplanationReference(TargetId target, CapabilityId capability, Id128 explanationKey)
        {
            Target = target;
            Capability = capability;
            ExplanationKey = explanationKey;
        }

        public bool Equals(ExplanationReference other) =>
            Target.Equals(other.Target) && Capability.Equals(other.Capability) && ExplanationKey.Equals(other.ExplanationKey);

        public override bool Equals(object? obj) => obj is ExplanationReference other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + Target.GetHashCode();
                hash = (hash * 31) + Capability.GetHashCode();
                hash = (hash * 31) + ExplanationKey.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => Target.ToString() + "/" + Capability.ToString();
    }

    /// <summary>Derivation delta of one plan (05 s4).</summary>
    public sealed class DerivationDelta
    {
        public DerivationDelta(
            IReadOnlyList<ContributionKey>? added,
            IReadOnlyList<ContributionKey>? removed,
            IReadOnlyList<ContributionKey>? changed,
            IReadOnlyList<SlotDelta>? effectiveSlots,
            IReadOnlyList<SupportRecord>? supports,
            IReadOnlyList<ExplanationReference>? explanations)
        {
            Added = ContractCollections.Freeze(added);
            Removed = ContractCollections.Freeze(removed);
            Changed = ContractCollections.Freeze(changed);
            EffectiveSlots = ContractCollections.Freeze(effectiveSlots);
            Supports = ContractCollections.Freeze(supports);
            Explanations = ContractCollections.Freeze(explanations);
        }

        public IReadOnlyList<ContributionKey> Added { get; }

        public IReadOnlyList<ContributionKey> Removed { get; }

        public IReadOnlyList<ContributionKey> Changed { get; }

        public IReadOnlyList<SlotDelta> EffectiveSlots { get; }

        public IReadOnlyList<SupportRecord> Supports { get; }

        public IReadOnlyList<ExplanationReference> Explanations { get; }
    }

    /// <summary>Kind of node in a compiled execution plan (P-040).</summary>
    public enum PlanNodeKind
    {
        Stage = 0,
        System = 1,
    }

    /// <summary>
    /// One execution-plan node: a stage, or a system inside its owning stage. Independent ready nodes
    /// are ordered by ascending StageId then system key for trace stability (P-040).
    /// </summary>
    public readonly struct PlanNode
    {
        public readonly PlanNodeKind Kind;
        public readonly StageId Stage;
        public readonly FactoryKey System;

        private PlanNode(PlanNodeKind kind, StageId stage, FactoryKey system)
        {
            Kind = kind;
            Stage = stage;
            System = system;
        }

        public static PlanNode ForStage(StageId stage) => new PlanNode(PlanNodeKind.Stage, stage, default(FactoryKey));

        public static PlanNode ForSystem(StageId stage, FactoryKey system) => new PlanNode(PlanNodeKind.System, stage, system);

        public bool Equals(PlanNode other) =>
            Kind == other.Kind && Stage.Equals(other.Stage) && System.Equals(other.System);

        public override bool Equals(object? obj) => obj is PlanNode other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + (int)Kind;
                hash = (hash * 31) + Stage.GetHashCode();
                hash = (hash * 31) + System.GetHashCode();
                return hash;
            }
        }

        public override string ToString() =>
            Kind == PlanNodeKind.Stage ? "stage:" + Stage.ToString() : "system:" + Stage.ToString() + "/" + System.ToString();
    }

    /// <summary>Directed edge between two execution-plan nodes (P-040).</summary>
    public readonly struct PlanEdge
    {
        public readonly PlanNode From;
        public readonly PlanNode To;
        public readonly bool Required;

        public PlanEdge(PlanNode from, PlanNode to, bool required)
        {
            From = from;
            To = to;
            Required = required;
        }

        public bool Equals(PlanEdge other) =>
            From.Equals(other.From) && To.Equals(other.To) && Required == other.Required;

        public override bool Equals(object? obj) => obj is PlanEdge other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + From.GetHashCode();
                hash = (hash * 31) + To.GetHashCode();
                hash = (hash * 31) + (Required ? 1 : 0);
                return hash;
            }
        }

        public override string ToString() => From.ToString() + " -> " + To.ToString();
    }

    /// <summary>Compiled stage/system DAG with its semantic hash (P-040).</summary>
    public sealed class ExecutionPlan
    {
        public ExecutionPlan(IReadOnlyList<PlanNode>? nodes, IReadOnlyList<PlanEdge>? edges, ContentHash planHash)
        {
            Nodes = ContractCollections.Freeze(nodes);
            Edges = ContractCollections.Freeze(edges);
            PlanHash = planHash;
        }

        public IReadOnlyList<PlanNode> Nodes { get; }

        public IReadOnlyList<PlanEdge> Edges { get; }

        public ContentHash PlanHash { get; }
    }

    /// <summary>Generated recipe apply operation for one target (05 s4, P-030).</summary>
    public sealed class RecipeOperation
    {
        public RecipeOperation(CompositionEditKind kind, TargetId target, DefinitionRef recipe, FactoryKey applyKey)
        {
            Kind = kind;
            Target = target;
            Recipe = recipe;
            ApplyKey = applyKey;
        }

        public CompositionEditKind Kind { get; }

        public TargetId Target { get; }

        public DefinitionRef Recipe { get; }

        public FactoryKey ApplyKey { get; }
    }

    /// <summary>Generated component-layout operation for one target (05 s4, P-022).</summary>
    public sealed class LayoutOperation
    {
        public LayoutOperation(CompositionEditKind kind, TargetId target, SchemaRef schema)
        {
            Kind = kind;
            Target = target;
            Schema = schema;
        }

        public CompositionEditKind Kind { get; }

        public TargetId Target { get; }

        public SchemaRef Schema { get; }
    }

    /// <summary>Owner grant over one state slot; a logical authority, not one system instance (P-021).</summary>
    public readonly struct OwnerGrant
    {
        public readonly OwnerId Owner;
        public readonly StateSlotKey Slot;
        public readonly uint OwnerVersion;

        public OwnerGrant(OwnerId owner, StateSlotKey slot, uint ownerVersion)
        {
            Owner = owner;
            Slot = slot;
            OwnerVersion = ownerVersion;
        }

        public override string ToString() => Owner.ToString() + ":" + Slot.ToString();
    }

    /// <summary>Staged state disposition or migration for one slot (05 s4, P-033).</summary>
    public readonly struct StateDisposition
    {
        public readonly StateSlotKey Slot;
        public readonly StateDispositionKind Kind;
        public readonly TargetId TransferTo;
        public readonly FactoryKey MigrationKey;

        public StateDisposition(StateSlotKey slot, StateDispositionKind kind, TargetId transferTo, FactoryKey migrationKey)
        {
            Slot = slot;
            Kind = kind;
            TransferTo = transferTo;
            MigrationKey = migrationKey;
        }

        public override string ToString() => Kind.ToString() + ":" + Slot.ToString();
    }

    /// <summary>Buffer binding table row: producer keys and the single consuming stage (P-043).</summary>
    public sealed class BufferBinding
    {
        public BufferBinding(BufferId buffer, IReadOnlyList<FactoryKey>? producers, StageId consumerStage)
        {
            Buffer = buffer;
            Producers = ContractCollections.Freeze(producers);
            ConsumerStage = consumerStage;
        }

        public BufferId Buffer { get; }

        public IReadOnlyList<FactoryKey> Producers { get; }

        public StageId ConsumerStage { get; }
    }

    /// <summary>Runtime delta of one plan (05 s4).</summary>
    public sealed class RuntimeDelta
    {
        public RuntimeDelta(
            IReadOnlyList<RecipeOperation>? recipes,
            IReadOnlyList<LayoutOperation>? layouts,
            IReadOnlyList<OwnerGrant>? ownerGrants,
            IReadOnlyList<StateDisposition>? stateDispositions,
            ExecutionPlan? executionPlan,
            IReadOnlyList<BufferBinding>? bufferBindings)
        {
            Recipes = ContractCollections.Freeze(recipes);
            Layouts = ContractCollections.Freeze(layouts);
            OwnerGrants = ContractCollections.Freeze(ownerGrants);
            StateDispositions = ContractCollections.Freeze(stateDispositions);
            Plan = executionPlan;
            BufferBindings = ContractCollections.Freeze(bufferBindings);
        }

        public IReadOnlyList<RecipeOperation> Recipes { get; }

        public IReadOnlyList<LayoutOperation> Layouts { get; }

        public IReadOnlyList<OwnerGrant> OwnerGrants { get; }

        public IReadOnlyList<StateDisposition> StateDispositions { get; }

        public ExecutionPlan? Plan { get; }

        public IReadOnlyList<BufferBinding> BufferBindings { get; }
    }

    /// <summary>
    /// One staged managed-resource acquisition. Lease ids are process-local and never enter the plan's
    /// stable hash (05 s4).
    /// </summary>
    public readonly struct StagedLease
    {
        public readonly ResourceKey Resource;
        public readonly Id128 LeaseId;
        public readonly ResourceReadiness Readiness;

        /// <summary>Resources that must be prepared before this one and retired after it (P-012, P-048).</summary>
        public readonly IReadOnlyList<ResourceKey> Dependencies;

        /// <summary>Acquisition ordinal within its instance; leases retire in reverse acquisition order (P-048).</summary>
        public readonly uint AcquisitionOrdinal;

        public StagedLease(
            ResourceKey resource,
            Id128 leaseId,
            ResourceReadiness readiness,
            IReadOnlyList<ResourceKey>? dependencies,
            uint acquisitionOrdinal)
        {
            Resource = resource;
            LeaseId = leaseId;
            Readiness = readiness;
            Dependencies = ContractCollections.Freeze(dependencies);
            AcquisitionOrdinal = acquisitionOrdinal;
        }

        public bool HasDependencies => Dependencies.Count != 0;

        public override string ToString() =>
            Resource.ToString() + ":" + Readiness.ToString() + "@" + AcquisitionOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Staged resource table of one plan (05 s4).</summary>
    public sealed class ResourceStaging
    {
        public ResourceStaging(IReadOnlyList<StagedLease>? handles, IReadOnlyList<Id128>? retiringLeaseIds, ulong scratchCapacityBytes)
        {
            Handles = ContractCollections.Freeze(handles);
            RetiringLeaseIds = ContractCollections.Freeze(retiringLeaseIds);
            ScratchCapacityBytes = scratchCapacityBytes;
        }

        public IReadOnlyList<StagedLease> Handles { get; }

        public IReadOnlyList<Id128> RetiringLeaseIds { get; }

        public ulong ScratchCapacityBytes { get; }
    }

    /// <summary>Affected counts reported by a plan or publication event (05 s4, P-029).</summary>
    public sealed class AffectedCounts
    {
        public AffectedCounts(int targets, int installs, int contributionsAdded, int contributionsRetracted, int stages)
        {
            Targets = targets;
            Installs = installs;
            ContributionsAdded = contributionsAdded;
            ContributionsRetracted = contributionsRetracted;
            Stages = stages;
        }

        public int Targets { get; }

        public int Installs { get; }

        public int ContributionsAdded { get; }

        public int ContributionsRetracted { get; }

        public int Stages { get; }
    }

    /// <summary>Prepare/apply estimates of one plan (05 s4).</summary>
    public sealed class PlanCostEstimate
    {
        public PlanCostEstimate(ulong prepareBytes, ulong applyBytes)
        {
            PrepareBytes = prepareBytes;
            ApplyBytes = applyBytes;
        }

        public ulong PrepareBytes { get; }

        public ulong ApplyBytes { get; }
    }

    /// <summary>Hard budget usage of one plan (05 s4, P-028).</summary>
    public sealed class HardBudgetUsage
    {
        public HardBudgetUsage(ulong prepareBytesLimit, ulong applyBytesLimit, ulong prepareBytesUsed, ulong applyBytesUsed)
        {
            PrepareBytesLimit = prepareBytesLimit;
            ApplyBytesLimit = applyBytesLimit;
            PrepareBytesUsed = prepareBytesUsed;
            ApplyBytesUsed = applyBytesUsed;
        }

        public ulong PrepareBytesLimit { get; }

        public ulong ApplyBytesLimit { get; }

        public ulong PrepareBytesUsed { get; }

        public ulong ApplyBytesUsed { get; }

        public bool Exceeded => PrepareBytesUsed > PrepareBytesLimit || ApplyBytesUsed > ApplyBytesLimit;
    }

    /// <summary>Validity, invalidation and cost section of one plan (05 s4).</summary>
    public sealed class ValidityAndCost
    {
        public ValidityAndCost(
            IReadOnlyList<Diagnostic>? invalidation,
            IReadOnlyList<Id128>? preconditions,
            AffectedCounts affected,
            PlanCostEstimate estimate,
            HardBudgetUsage budget)
        {
            Invalidation = ContractCollections.Freeze(invalidation);
            Preconditions = ContractCollections.Freeze(preconditions);
            Affected = affected;
            Estimate = estimate;
            Budget = budget;
        }

        public IReadOnlyList<Diagnostic> Invalidation { get; }

        /// <summary>Declared precondition keys that application rechecks (P-027).</summary>
        public IReadOnlyList<Id128> Preconditions { get; }

        public AffectedCounts Affected { get; }

        public PlanCostEstimate Estimate { get; }

        public HardBudgetUsage Budget { get; }
    }
}
