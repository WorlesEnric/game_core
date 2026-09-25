// GameCore.Unity.Runtime.Tests — fixtures of the assembly publication suite (GC-008).
//
// The suite runs the real planner, the real publisher and real Entities storage. Its fixtures exist so each test
// varies exactly one input:
//
//   * `AssemblyFixtureKeys`  — one frozen ownership/stage descriptor's identities, in this test's own namespace;
//   * `AssemblyFixtureDescriptor` — that descriptor, built over the fixture world's own generated-style system
//     keys, so the published schedule is a real dispatch table the world can resolve;
//   * `AssemblyFixtureRecipes` — one precompiled `SpawnRecipe` with a direct typed applier (04 s8);
//   * `AssemblyFixturePlans`  — proposal/plan builders and the seed helpers for targets and live state.
//
// Nothing here is a second implementation of the planner or the publisher: the helpers only assemble inputs and
// read live ECS storage back.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Unity.Runtime.Tests.Assembly
{
    /// <summary>Stable identities of the assembly fixture; a separate namespace word from every other fixture.</summary>
    public static class AssemblyFixtureKeys
    {
        public const ulong Namespace = 0x473038415353454DUL;

        public static readonly ScopeId RootScope = new ScopeId(new Id128(Namespace, 0x0001UL));

        public static readonly ScopeId ChildScope = new ScopeId(new Id128(Namespace, 0x0002UL));

        public static readonly OwnerId SlotOwner = new OwnerId(new Id128(Namespace, 0x0010UL));

        public static readonly SlotId QuestSlot = new SlotId(new Id128(Namespace, 0x0011UL));

        public static readonly CapabilityId SelectionLimit = new CapabilityId(new Id128(Namespace, 0x0020UL));

        public static readonly RuleId LimitRule = new RuleId(new Id128(Namespace, 0x0021UL));

        public static readonly SchemaId CardSchemaId = new SchemaId(new Id128(Namespace, 0x0030UL));

        public static readonly SchemaId LimitSchemaId = new SchemaId(new Id128(Namespace, 0x0031UL));

        public static readonly SchemaId QuestStateSchemaId = new SchemaId(new Id128(Namespace, 0x0032UL));

        public static readonly DefinitionId CardRecipeId = new DefinitionId(new Id128(Namespace, 0x0040UL));

        public static readonly FactoryKey ApplyCardRecipe = Key(0x0050UL, "fixture.assembly.apply.card");

        public static readonly FactoryKey QuestMigrationV1ToV2 = Key(0x0051UL, "fixture.assembly.migrate.quest.v1-v2");

        public static readonly FactoryKey MissingMigration = Key(0x0052UL, "fixture.assembly.migrate.quest.unregistered");

        public static readonly Id128 Issuer = new Id128(Namespace, 0x0060UL);

        /// <summary>State schema version the descriptor declares; live state at 1 must migrate (P-032).</summary>
        public const uint QuestSchemaVersion = 2U;

        public static DefinitionRef CardRecipe =>
            new DefinitionRef(CardRecipeId, new SchemaRef(CardSchemaId, 1U), new DefinitionRevision(1UL));

        public static SchemaRef LimitSchema => new SchemaRef(LimitSchemaId, 1U);

        public static TargetId Target(ulong ordinal) => new TargetId(new Id128(Namespace, 0x0100UL + ordinal));

        public static PluginInstanceId Instance(ulong ordinal) =>
            new PluginInstanceId(new Id128(Namespace, 0x0200UL + ordinal));

        public static PluginTypeId PluginType(ulong ordinal) =>
            new PluginTypeId(new Id128(Namespace, 0x0300UL + ordinal));

        public static ProviderInstallationId Provider(ulong ordinal) =>
            new ProviderInstallationId(new Id128(Namespace, 0x0400UL + ordinal));

        public static OperationId Operation(WorldId world, ulong sequence) => new OperationId(world, Issuer, sequence);

        private static FactoryKey Key(ulong ordinal, string stableName)
        {
            uint hash = 2166136261U;
            for (int i = 0; i < stableName.Length; i++)
            {
                hash ^= stableName[i];
                hash *= 16777619U;
            }

            return new FactoryKey(new Id128(Namespace, ordinal), hash == 0U ? 1U : hash);
        }
    }

    /// <summary>One pure registered migration: adds a delta, refusing negative input (P-032).</summary>
    public sealed class AssemblyFixtureMigration : ISlotMigration
    {
        private readonly int delta;

        public AssemblyFixtureMigration(FactoryKey key, uint fromVersion, uint toVersion, int delta)
        {
            Key = key;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.delta = delta;
        }

        public FactoryKey Key { get; }

        public uint FromVersion { get; }

        public uint ToVersion { get; }

        public int Invocations { get; private set; }

        public bool TryMigrate(int source, out int migrated)
        {
            Invocations++;
            if (source < 0)
            {
                migrated = source;
                return false;
            }

            migrated = source + delta;
            return true;
        }
    }

    /// <summary>The fixture's acquisition gate: a deterministic in-memory lease table (P-029).</summary>
    public sealed class AssemblyFixtureGate : IPlanResourceGate
    {
        private readonly HashSet<Id128> released = new HashSet<Id128>();
        private ulong next;

        public int AcquireCount { get; private set; }

        public int ReleaseCount { get; private set; }

        public bool TryAcquire(ResourceKey resource, ulong bytes, out Id128 leaseId, out DiagnosticCode code)
        {
            _ = resource;
            _ = bytes;
            next++;
            leaseId = new Id128(0x415353454D4C4541UL, next);
            AcquireCount++;
            code = DiagnosticCode.None;
            return true;
        }

        public bool Release(Id128 leaseId, out DiagnosticCode code)
        {
            ReleaseCount++;
            released.Add(leaseId);
            code = DiagnosticCode.None;
            return true;
        }

        public int ReleasedCount => released.Count;
    }

    /// <summary>The fixture spawn applier: a direct typed install of the recipe's base layout (04 s6, 04 s8).</summary>
    public sealed class AssemblyFixtureApplier : ISpawnApplier
    {
        public FactoryKey Key => AssemblyFixtureKeys.ApplyCardRecipe;

        public int AppliedCount { get; private set; }

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            entityManager.AddComponentData(entity, new FixtureCounter { Value = 0 });
            entityManager.AddComponentData(entity, new FixtureJobResult { Value = 0 });
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>The frozen ownership/stage descriptor of this suite, built over the fixture world's system keys.</summary>
    public static class AssemblyFixtureDescriptor
    {
        /// <summary>
        /// Stages reuse the fixture world's generated-style keys and fence indices, so the schedule the publisher
        /// installs at the fence is one the world can actually resolve (P-039): accept (2) then settle (3), with the
        /// settle stage producing the declared step buffer that the project stage (5) consumes (P-043).
        /// </summary>
        public static OwnershipStageDescriptor Build()
        {
            var accept = new DescriptorStage(
                FixtureKeys.AcceptStage,
                1U,
                FixtureRegistration.AcceptStageIndex,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(FixtureKeys.AcceptSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                null);

            var settle = new DescriptorStage(
                FixtureKeys.SettleStage,
                1U,
                FixtureRegistration.SettleStageIndex,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(FixtureKeys.SettleSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                new List<int> { FixtureRegistration.AcceptStageIndex });

            var project = new DescriptorStage(
                FixtureKeys.ProjectStage,
                1U,
                FixtureRegistration.ProjectStageIndex,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(FixtureKeys.ProjectSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                new List<int> { FixtureRegistration.SettleStageIndex });

            var slot = new OwnedSlotSpec(
                AssemblyFixtureKeys.QuestSlot,
                AssemblyFixtureKeys.SlotOwner,
                new SchemaRef(AssemblyFixtureKeys.QuestStateSchemaId, AssemblyFixtureKeys.QuestSchemaVersion),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128),
                AssemblyFixtureKeys.QuestMigrationV1ToV2);

            var buffer = new BufferBinding(
                FixtureKeys.CounterBuffer,
                new List<FactoryKey> { FixtureKeys.SettleSystem },
                FixtureKeys.ProjectStage);

            return new OwnershipStageDescriptor(
                PlanHashing.Of("gc-008 assembly fixture ownership/stage descriptor v1"),
                new List<OwnedSlotSpec> { slot },
                new List<DescriptorStage> { accept, settle, project },
                new List<BufferBinding> { buffer });
        }
    }

    /// <summary>The fixture recipe table and migration registry.</summary>
    public static class AssemblyFixtureRecipes
    {
        public static SpawnRecipe CardRecipe(AssemblyFixtureApplier? applier = null)
        {
            var descriptor = new TargetDescriptor(
                AssemblyFixtureKeys.CardRecipe,
                new List<SchemaRef> { new SchemaRef(AssemblyFixtureKeys.CardSchemaId, 1U) },
                new List<CapabilityRef> { new CapabilityRef(AssemblyFixtureKeys.SelectionLimit, 1U) },
                new List<Id128> { new Id128(AssemblyFixtureKeys.Namespace, 0x0070UL) },
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(
                AssemblyFixtureKeys.CardRecipe,
                descriptor,
                new List<SchemaRef> { new SchemaRef(AssemblyFixtureKeys.CardSchemaId, 1U) },
                applier ?? new AssemblyFixtureApplier());
        }

        public static SpawnRecipeCatalog Catalog(AssemblyFixtureApplier? applier = null) =>
            new SpawnRecipeCatalog(new List<SpawnRecipe> { CardRecipe(applier) });

        /// <summary>A registry with the one migration the fixture descriptor needs, plus an unusable second pair.</summary>
        public static MigrationRegistry Migrations() =>
            new MigrationRegistry(new List<ISlotMigration>
            {
                new AssemblyFixtureMigration(AssemblyFixtureKeys.QuestMigrationV1ToV2, 1U, AssemblyFixtureKeys.QuestSchemaVersion, 10),
                new AssemblyFixtureMigration(AssemblyFixtureKeys.MissingMigration, 1U, 5U, 1),
            });
    }

    /// <summary>Proposal and plan builders plus the seed helpers for targets and live state.</summary>
    public static class AssemblyFixturePlans
    {
        /// <summary>One capability declaration for the card recipe: `Replace`, value 3, priority 10 (P-018).</summary>
        public static ProposedCapability LimitCapability(
            int value = 3,
            int priority = 10,
            CompositionPolicy policy = CompositionPolicy.Replace)
        {
            return new ProposedCapability(
                AssemblyFixtureKeys.LimitRule,
                new CapabilityRef(AssemblyFixtureKeys.SelectionLimit, 1U),
                AssemblyFixtureKeys.LimitSchema,
                0U,
                policy,
                value,
                priority,
                new List<DefinitionRef> { AssemblyFixtureKeys.CardRecipe });
        }

        /// <summary>A mount of one plugin instance declaring the limit capability.</summary>
        public static ProposedMount Mount(ulong ordinal, ScopeId? scope = null, ProposedCapability? capability = null, ulong generation = 1UL)
        {
            return new ProposedMount(
                AssemblyFixtureKeys.Instance(ordinal),
                AssemblyFixtureKeys.PluginType(ordinal),
                AssemblyFixtureKeys.Provider(ordinal),
                scope ?? AssemblyFixtureKeys.RootScope,
                generation,
                new List<ProposedCapability> { capability ?? LimitCapability() });
        }

        /// <summary>A proposal against what the world currently publishes, which is what P-028 rechecks.</summary>
        public static CompositionProposal MountProposal(
            AssemblyPublisher publisher,
            ulong sequence,
            IReadOnlyList<ProposedMount>? mounts = null,
            IReadOnlyList<ProposedUnmount>? unmounts = null)
        {
            return new CompositionProposal(
                AssemblyFixtureKeys.Operation(publisher.World.World, sequence),
                PlanHashing.Of("gc-008 assembly fixture input " + sequence),
                publisher.PublishedRevision,
                publisher.World.CurrentEpoch,
                PlanHashing.Of("gc-008 assembly fixture catalog"),
                PropagationMode.Automatic,
                mounts ?? new List<ProposedMount> { Mount(1UL) },
                unmounts);
        }

        /// <summary>Builds one plan with the fixture inputs, so each test varies exactly one of them (P-002).</summary>
        public static PlannedPublication Plan(
            AssemblyPublisher publisher,
            CompositionProposal proposal,
            IReadOnlyList<TargetDefinition>? targets = null,
            IReadOnlyList<LiveSlotState>? liveSlots = null,
            TargetBindingTable? current = null,
            IReadOnlyList<DerivedBindingRule>? rules = null,
            MigrationRegistry? migrations = null,
            MigrationScratch? scratch = null,
            InertAcquisitionSet? acquisitions = null,
            PlanBudget? budget = null)
        {
            PlanBudget planBudget = budget ?? new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, 4096UL, 64UL);
            return AssemblyPlanner.Build(
                proposal,
                publisher.Descriptor,
                publisher.PublishedRevision,
                publisher.World.CurrentEpoch,
                current ?? publisher.Published.Bindings,
                rules ?? publisher.Published.Rules,
                targets ?? KnownTargets(),
                liveSlots,
                migrations ?? AssemblyFixtureRecipes.Migrations(),
                scratch ?? new MigrationScratch(planBudget.ScratchCapacityBytes, planBudget.ScratchBytesPerSlot),
                acquisitions ?? new InertAcquisitionSet(new AssemblyFixtureGate(), proposal.Operation),
                planBudget);
        }

        /// <summary>
        /// A proposal whose expected revision/epoch deliberately differ from the published ones, which is how the
        /// stale-plan case is expressed: the plan then rejects before any live write (P-028).
        /// </summary>
        public static CompositionProposal WithStaleBase(
            CompositionProposal proposal,
            CompositionRevision expectedRevision,
            AssemblyEpoch baseEpoch)
        {
            return new CompositionProposal(
                proposal.Operation,
                proposal.InputHash,
                expectedRevision,
                baseEpoch,
                proposal.CatalogHash,
                proposal.Mode,
                proposal.Mounts,
                proposal.Unmounts);
        }

        /// <summary>The two targets the fixture registers before the first publication (P-013: existing targets).</summary>
        public static IReadOnlyList<TargetDefinition> KnownTargets() => new List<TargetDefinition>
        {
            new TargetDefinition(AssemblyFixtureKeys.Target(1UL), AssemblyFixtureKeys.CardRecipe, AssemblyFixtureKeys.RootScope),
            new TargetDefinition(AssemblyFixtureKeys.Target(2UL), AssemblyFixtureKeys.CardRecipe, AssemblyFixtureKeys.RootScope),
        };

        /// <summary>Live state of one target's quest slot, as the publisher copies it into scratch (P-029).</summary>
        public static LiveSlotState QuestSlot(TargetId target, int value, uint version)
        {
            return new LiveSlotState(
                new StateSlotKey(target, AssemblyFixtureKeys.SlotOwner, AssemblyFixtureKeys.QuestSlot),
                version,
                value);
        }

        /// <summary>
        /// Registers one existing target with its stable identity, its published stamp and optionally a live state
        /// slot at a schema version. This is the "target that existed before the publication" of P-013's existing
        /// descendant case; a spawn is a separate publication (P-024).
        /// </summary>
        public static TargetHandle SeedTarget(
            AssemblyPublisher publisher,
            TargetId target,
            int slotValue,
            uint slotVersion)
        {
            EntityManager entityManager = publisher.World.EntityWorld.EntityManager;
            Entity entity = entityManager.CreateEntity();
            if (!publisher.Registry.TryAllocate(target, entity, out TargetHandle handle, out DiagnosticCode code))
            {
                throw new InvalidOperationException("seeding target " + target.ToString() + " failed: " + code);
            }

            entityManager.AddComponentData(entity, new TargetIdentity
            {
                Target = target,
                Generation = handle.Generation,
            });

            entityManager.AddComponentData(entity, new AssemblyStamp
            {
                AssemblyEpoch = publisher.World.CurrentEpoch.Value,
                CompositionRevision = publisher.PublishedRevision.Value,
                Slot = handle.Slot,
                Published = 1,
            });

            entityManager.AddBuffer<CapabilityBinding>(entity);

            DynamicBuffer<TargetSlotState> slots = entityManager.AddBuffer<TargetSlotState>(entity);
            slots.Add(new TargetSlotState
            {
                Slot = AssemblyFixtureKeys.QuestSlot,
                Owner = AssemblyFixtureKeys.SlotOwner,
                SchemaVersion = slotVersion,
                Value = slotValue,
                Active = 1,
            });

            return handle;
        }
    }
}
