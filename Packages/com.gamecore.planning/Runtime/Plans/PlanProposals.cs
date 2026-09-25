// GameCore.Planning — the immutable inputs of one assembly plan (GC-008).
//
// Normative sources: 00 P-002 (`AssemblyPlanner` is a pure consumer of immutable snapshots), P-027 (a proposal
// specifies expected `CompositionRevision`, catalog hash, operation id and canonical input hash), P-028 (stale
// plans reject without mutation) and P-040 (the compiled execution plan is part of the plan, with its own hash).
//
// These types are the planner's whole input surface. Nothing here is a live world, an EntityManager or a captured
// closure: a proposal, the target definitions the plan addresses, the live slot values migrations run on, the
// budget and the descriptor (see `OwnershipStageDescriptor`). That is what lets the planner run while the old
// composition keeps simulating (P-027 can overlap simulation).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Planning
{
    /// <summary>One capability a mount contributes to matching targets, with its per-slot policy (P-017, P-019).</summary>
    public sealed class ProposedCapability
    {
        public ProposedCapability(
            RuleId rule,
            CapabilityRef capability,
            SchemaRef schema,
            uint outputSlot,
            CompositionPolicy policy,
            int value,
            int priority,
            IReadOnlyList<DefinitionRef>? targetRecipes,
            IReadOnlyList<TargetId>? targetIds = null)
        {
            Rule = rule;
            Capability = capability;
            Schema = schema;
            OutputSlot = outputSlot;
            Policy = policy;
            Value = value;
            Priority = priority;
            TargetRecipes = ContractCollections.Freeze(targetRecipes);
            TargetIds = ContractCollections.Freeze(targetIds);
        }

        /// <summary>Stable derivation-rule identity of this declaration; it is part of the contribution key (P-017).</summary>
        public RuleId Rule { get; }

        public CapabilityRef Capability { get; }

        /// <summary>Effective value schema this slot publishes; it also selects the state slot when it owns one.</summary>
        public SchemaRef Schema { get; }

        public uint OutputSlot { get; }

        public CompositionPolicy Policy { get; }

        public int Value { get; }

        /// <summary>Bounded 32-bit manifest priority; higher wins before any identity tie-break (P-018).</summary>
        public int Priority { get; }

        /// <summary>Recipes this rule is eligible for; Automatic needs no per-instance import (P-013, P-015).</summary>
        public IReadOnlyList<DefinitionRef> TargetRecipes { get; }
        /// <summary>Explicit target identities when the declaration comes from a derived target assembly.</summary>
        public IReadOnlyList<TargetId> TargetIds { get; }

        public bool AppliesTo(DefinitionRef recipe, TargetId target)
        {
            if (!AppliesTo(recipe)) return false;
            if (TargetIds.Count == 0) return true;
            for (int i = 0; i < TargetIds.Count; i++)
            {
                if (TargetIds[i].Equals(target)) return true;
            }

            return false;
        }


        /// <summary>True when this declaration is eligible for a target of the given recipe (P-015).</summary>
        public bool AppliesTo(DefinitionRef recipe)
        {
            for (int i = 0; i < TargetRecipes.Count; i++)
            {
                if (TargetRecipes[i].Equals(recipe))
                {
                    return true;
                }
            }

            return false;
        }

        public override string ToString() =>
            Capability.ToString() + "#" + OutputSlot.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One proposed mount: a precompiled plugin instance, its scope and its derived contributions (O-03).</summary>
    public sealed class ProposedMount
    {
        public ProposedMount(
            PluginInstanceId instance,
            PluginTypeId pluginType,
            ProviderInstallationId provider,
            ScopeId scope,
            ulong providerGeneration,
            IReadOnlyList<ProposedCapability>? capabilities)
        {
            Instance = instance;
            PluginType = pluginType;
            Provider = provider;
            Scope = scope;
            ProviderGeneration = providerGeneration;
            Capabilities = ContractCollections.Freeze(capabilities);
        }

        public PluginInstanceId Instance { get; }

        public PluginTypeId PluginType { get; }

        /// <summary>Identity the derived contributions carry as their support (P-017, P-033).</summary>
        public ProviderInstallationId Provider { get; }

        public ScopeId Scope { get; }

        /// <summary>Activation generation stamped on the contributions; it changes on remount (P-005).</summary>
        public ulong ProviderGeneration { get; }

        public IReadOnlyList<ProposedCapability> Capabilities { get; }

        public override string ToString() => Instance.ToString() + "@" + Scope.ToString();
    }

    /// <summary>One proposed unmount: the provider whose support retracts exactly (P-033, O-07).</summary>
    public sealed class ProposedUnmount
    {
        public ProposedUnmount(PluginInstanceId instance, ProviderInstallationId provider, ScopeId scope)
        {
            Instance = instance;
            Provider = provider;
            Scope = scope;
        }

        public PluginInstanceId Instance { get; }

        public ProviderInstallationId Provider { get; }

        public ScopeId Scope { get; }

        public override string ToString() => Instance.ToString() + "@" + Scope.ToString();
    }

    /// <summary>
    /// One immutable composition proposal. The planner is a pure consumer: it validates the expected revision and
    /// the catalog hash, plans the derived closure and returns an immutable plan (P-002, P-027).
    /// </summary>
    public sealed class CompositionProposal
    {
        public CompositionProposal(
            OperationId operation,
            ContentHash inputHash,
            CompositionRevision expectedRevision,
            AssemblyEpoch baseEpoch,
            ContentHash catalogHash,
            PropagationMode mode,
            IReadOnlyList<ProposedMount>? mounts,
            IReadOnlyList<ProposedUnmount>? unmounts)
        {
            Operation = operation;
            InputHash = inputHash;
            ExpectedRevision = expectedRevision;
            BaseEpoch = baseEpoch;
            CatalogHash = catalogHash;
            Mode = mode;
            Mounts = ContractCollections.Freeze(mounts);
            Unmounts = ContractCollections.Freeze(unmounts);
        }

        public OperationId Operation { get; }

        public ContentHash InputHash { get; }

        public CompositionRevision ExpectedRevision { get; }

        public AssemblyEpoch BaseEpoch { get; }

        public ContentHash CatalogHash { get; }

        public PropagationMode Mode { get; }

        public IReadOnlyList<ProposedMount> Mounts { get; }

        public IReadOnlyList<ProposedUnmount> Unmounts { get; }

        public bool IsEmpty => Mounts.Count == 0 && Unmounts.Count == 0;
    }

    /// <summary>One existing target as the planner sees it: stable identity, recipe and owning scope (P-015).</summary>
    public readonly struct TargetDefinition
    {
        public readonly TargetId Target;
        public readonly DefinitionRef Recipe;
        public readonly ScopeId Scope;

        public TargetDefinition(TargetId target, DefinitionRef recipe, ScopeId scope)
        {
            Target = target;
            Recipe = recipe;
            Scope = scope;
        }

        public override string ToString() => Target.ToString() + ":" + Recipe.ToString();
    }

    /// <summary>Live slot value read out of the world before the fence; migrations run on this copy (P-029).</summary>
    public readonly struct LiveSlotState
    {
        public readonly StateSlotKey Slot;
        public readonly uint SchemaVersion;
        public readonly int Value;

        public LiveSlotState(StateSlotKey slot, uint schemaVersion, int value)
        {
            Slot = slot;
            SchemaVersion = schemaVersion;
            Value = value;
        }

        public override string ToString() =>
            Slot.ToString() + "@" + SchemaVersion.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Configured hard budgets of one plan; the protocol's defaults are provisional guardrails (P-022).</summary>
    public sealed class PlanBudget
    {
        /// <summary>Reference defaults from P-022: 128 MiB temporary storage and an 8 ms estimated apply.</summary>
        public static PlanBudget Reference => new PlanBudget(128UL * 1024UL * 1024UL, 8UL * 1024UL * 1024UL, 1024UL * 1024UL, 64UL);

        public PlanBudget(ulong prepareBytesLimit, ulong applyBytesLimit, ulong scratchCapacityBytes, ulong scratchBytesPerSlot)
        {
            PrepareBytesLimit = prepareBytesLimit;
            ApplyBytesLimit = applyBytesLimit;
            ScratchCapacityBytes = scratchCapacityBytes;
            ScratchBytesPerSlot = scratchBytesPerSlot;
        }

        public ulong PrepareBytesLimit { get; }

        public ulong ApplyBytesLimit { get; }

        /// <summary>Temporary storage the plan may reserve for migrations (P-022).</summary>
        public ulong ScratchCapacityBytes { get; }

        public ulong ScratchBytesPerSlot { get; }
    }

    /// <summary>
    /// The compiled execution order of one publication: contract-level entries plus the buffer bindings and the
    /// plan's own hash (P-040). The Unity adapter turns this into its flat dispatch table with fence indices; the
    /// planner never needs a Unity type to express the order.
    /// </summary>
    public sealed class CompiledSchedule
    {
        public CompiledSchedule(
            IReadOnlyList<SystemDispatchEntry>? entries,
            IReadOnlyList<BufferBinding>? buffers,
            IReadOnlyList<PlanEdge>? edges,
            ContentHash hash)
        {
            Entries = ContractCollections.Freeze(entries);
            Buffers = ContractCollections.Freeze(buffers);
            Edges = ContractCollections.Freeze(edges);
            Hash = hash;
        }

        public IReadOnlyList<SystemDispatchEntry> Entries { get; }

        public IReadOnlyList<BufferBinding> Buffers { get; }

        public IReadOnlyList<PlanEdge> Edges { get; }

        public ContentHash Hash { get; }

        /// <summary>Epoch-bound dispatch table of this schedule (05 s5 `OrderedDispatchTable`).</summary>
        public OrderedDispatchTable ToTable(AssemblyEpoch epoch) => new OrderedDispatchTable(epoch, Entries, Buffers);

        public static CompiledSchedule Empty { get; } =
            new CompiledSchedule(null, null, null, PlanHashing.Of("schedule\nempty"));
    }

    /// <summary>One planned state migration: which target slot changes version and through which handler (P-032).</summary>
    public readonly struct PlannedMigration
    {
        public readonly TargetId Target;
        public readonly StateSlotKey Slot;
        public readonly uint FromVersion;
        public readonly uint ToVersion;
        public readonly FactoryKey MigrationKey;

        public PlannedMigration(TargetId target, StateSlotKey slot, uint fromVersion, uint toVersion, FactoryKey migrationKey)
        {
            Target = target;
            Slot = slot;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            MigrationKey = migrationKey;
        }

        public override string ToString() =>
            Slot.ToString() + " " + FromVersion.ToString(CultureInfo.InvariantCulture)
            + "->" + ToVersion.ToString(CultureInfo.InvariantCulture);
    }
}
