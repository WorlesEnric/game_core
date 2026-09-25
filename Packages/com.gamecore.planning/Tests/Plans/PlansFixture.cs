// GameCore.Planning tests — the frozen fixture this task's planner is exercised against (GC-008).
//
// Wave 2 context: GC-007 owns the real ownership validator and GC-009 the real stage/schedule compiler, and 09
// requires each Wave 2 task to work through a frozen ownership/stage descriptor fixture until the W2 gate
// substitutes the real modules. The keys below are one such fixture: stable literals in this test's own namespace
// word, a descriptor that satisfies `OwnershipStageDescriptor.TryValidate`, and the load-bearing
// failure cases each requirement needs (migration, scratch budget, exclusive conflict).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Planning.Tests
{
    /// <summary>Stable identities of the fixture, so every assertion names the same values as the production code.</summary>
    public static class PlansFixtureKeys
    {
        /// <summary>Namespace word of every fixture key; never collides with a package or another task's fixture.</summary>
        public const ulong Namespace = 0x473038504C414E31UL;

        public static readonly ScopeId RootScope = Scope(1UL);

        public static readonly ScopeId ChildScope = Scope(2UL);

        public static readonly OwnerId SlotOwner = new OwnerId(new Id128(Namespace, 0x2010UL));

        public static readonly SlotId QuestSlot = new SlotId(new Id128(Namespace, 0x2020UL));

        public static readonly CapabilityId SelectionLimit = new CapabilityId(new Id128(Namespace, 0x2030UL));

        public static readonly RuleId LimitRule = new RuleId(new Id128(Namespace, 0x2040UL));

        public static readonly StageId AcceptStage = new StageId(new Id128(Namespace, 0x2050UL));

        /// <summary>A second capability on its own output slot, so two providers can support one target (P-033).</summary>
        public static readonly CapabilityId QuestFlag = new CapabilityId(new Id128(Namespace, 0x2031UL));

        public static readonly RuleId FlagRule = new RuleId(new Id128(Namespace, 0x2041UL));

        public static readonly SchemaId FlagSchemaId = new SchemaId(new Id128(Namespace, 0x20A1UL));

        public static SchemaRef FlagSchema => new SchemaRef(FlagSchemaId, 1U);

        public static readonly StageId SettleStage = new StageId(new Id128(Namespace, 0x2060UL));

        public static readonly BufferId StepBuffer = new BufferId(new Id128(Namespace, 0x2070UL));

        public static readonly DefinitionId CardRecipeId = new DefinitionId(new Id128(Namespace, 0x2080UL));

        public static readonly SchemaId CardSchemaId = new SchemaId(new Id128(Namespace, 0x2090UL));

        public static readonly SchemaId LimitSchemaId = new SchemaId(new Id128(Namespace, 0x20A0UL));

        public static readonly SchemaId QuestStateSchemaId = new SchemaId(new Id128(Namespace, 0x20B0UL));

        public static readonly FactoryKey AcceptSystem = Key(0x2100UL, "fixture.accept");

        public static readonly FactoryKey SettleSystem = Key(0x2110UL, "fixture.settle");

        public static readonly FactoryKey QuestMigrationV1ToV2 = Key(0x2120UL, "fixture.migrate.quest.v1-v2");

        public static readonly FactoryKey QuestMigrationV1ToV3 = Key(0x2130UL, "fixture.migrate.quest.v1-v3");

        public static readonly FactoryKey ApplyRecipeKey = Key(0x2140UL, "fixture.apply.card");

        public static readonly Id128 Issuer = new Id128(Namespace, 0x2200UL);

        /// <summary>Card recipe at revision 1; a spawn must resolve exactly this revision (P-024).</summary>
        public static DefinitionRef CardRecipe =>
            new DefinitionRef(CardRecipeId, new SchemaRef(CardSchemaId, 1U), new DefinitionRevision(1UL));

        /// <summary>Card recipe at revision 2; used as the stale request in a spawn-revalidation case.</summary>
        public static DefinitionRef CardRecipeV2 =>
            new DefinitionRef(CardRecipeId, new SchemaRef(CardSchemaId, 1U), new DefinitionRevision(2UL));

        public static SchemaRef LimitSchema => new SchemaRef(LimitSchemaId, 1U);

        public static TargetId Target(ulong ordinal) => new TargetId(new Id128(Namespace, 0x3000UL + ordinal));

        public static PluginTypeId PluginType(ulong ordinal) => new PluginTypeId(new Id128(Namespace, 0x4000UL + ordinal));

        public static PluginInstanceId Instance(ulong ordinal) =>
            new PluginInstanceId(new Id128(Namespace, 0x5000UL + ordinal));

        public static ProviderInstallationId Provider(ulong ordinal) =>
            new ProviderInstallationId(new Id128(Namespace, 0x6000UL + ordinal));

        public static OperationId Operation(WorldId world, ulong sequence) => new OperationId(world, Issuer, sequence);

        public static WorldId World(ulong ordinal) => new WorldId(new Id128(Namespace, 0x7000UL + ordinal));

        private static ScopeId Scope(ulong ordinal) => new ScopeId(new Id128(Namespace, 0x1000UL + ordinal));

        /// <summary>FNV-1a fold of a stable name into the key version, matching the repository's other fixtures.</summary>
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

    /// <summary>A pure, registered slot migration: adds a delta and reports failure for negative input (P-032).</summary>
    public sealed class DeltaMigration : ISlotMigration
    {
        private readonly int delta;

        public DeltaMigration(FactoryKey key, uint fromVersion, uint toVersion, int delta)
        {
            Key = key;
            FromVersion = fromVersion;
            ToVersion = toVersion;
            this.delta = delta;
        }

        public FactoryKey Key { get; }

        public uint FromVersion { get; }

        public uint ToVersion { get; }

        public int InvocationCount { get; private set; }

        public bool TryMigrate(int source, out int migrated)
        {
            InvocationCount++;
            if (source < 0)
            {
                migrated = source;
                return false;
            }

            migrated = source + delta;
            return true;
        }
    }

    /// <summary>An acquisition gate that records every call, so inertness and cleanup are observable (P-029, P-048).</summary>
    public sealed class RecordingResourceGate : IPlanResourceGate
    {
        private readonly List<Id128> acquired = new List<Id128>();
        private readonly HashSet<Id128> released = new HashSet<Id128>();
        private readonly Dictionary<Id128, ulong> bytes = new Dictionary<Id128, ulong>();
        private ulong nextLease;
        private Id128 failOnLease;
        private Id128 throwOnRelease;
        private Id128 quarantineOnRelease;

        /// <summary>Acquisitions to refuse with <see cref="DiagnosticCode.ResourceUnavailable"/>.</summary>
        public int AcquireFailureCount { get; set; }

        public IReadOnlyList<Id128> Acquired => acquired;

        public IReadOnlyList<Id128> Released
        {
            get
            {
                var list = new List<Id128>(released);
                return list;
            }
        }

        public int ReleaseAttemptCount { get; private set; }

        public int DuplicateReleaseAttemptCount { get; private set; }

        /// <summary>Makes the lease with this ordinal fail to release, i.e. the "one disposer throws" case (P-048).</summary>
        public void FailReleaseOf(Id128 leaseId) => throwOnRelease = leaseId;

        /// <summary>Makes the lease with this ordinal stay quarantined instead of being freed (P-048).</summary>
        public void QuarantineReleaseOf(Id128 leaseId) => quarantineOnRelease = leaseId;

        /// <summary>Makes one specific acquisition fail, identified by the lease it would have received (P-029).</summary>
        public void FailAcquisitionOf(Id128 leaseId) => failOnLease = leaseId;

        public bool TryAcquire(ResourceKey resource, ulong size, out Id128 leaseId, out DiagnosticCode code)
        {
            nextLease++;
            leaseId = new Id128(0x4C45415345UL, nextLease);
            if (AcquireFailureCount > 0)
            {
                AcquireFailureCount--;
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            if (!failOnLease.IsDefault && failOnLease.Equals(leaseId))
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            acquired.Add(leaseId);
            bytes[leaseId] = size;
            _ = resource;
            code = DiagnosticCode.None;
            return true;
        }

        public bool Release(Id128 leaseId, out DiagnosticCode code)
        {
            ReleaseAttemptCount++;
            if (released.Contains(leaseId))
            {
                DuplicateReleaseAttemptCount++;
            }

            if (!throwOnRelease.IsDefault && throwOnRelease.Equals(leaseId))
            {
                code = DiagnosticCode.TeardownBlocked;
                return false;
            }

            if (!quarantineOnRelease.IsDefault && quarantineOnRelease.Equals(leaseId))
            {
                code = DiagnosticCode.TeardownBlocked;
                return false;
            }

            released.Add(leaseId);
            code = DiagnosticCode.None;
            return true;
        }

        public ulong BytesOf(Id128 leaseId) => bytes.TryGetValue(leaseId, out ulong size) ? size : 0UL;
    }

    /// <summary>The frozen ownership/stage descriptor and the proposal/migration fixtures built on top of it.</summary>
    public static class PlansFixture
    {
        /// <summary>State schema version of the fixture descriptor; a live slot at another version must migrate.</summary>
        public const uint QuestSchemaVersion = 2U;

        /// <summary>
        /// The frozen descriptor: two stages with a backward edge, one owned slot with `PreserveDormant`, and one
        /// declared buffer whose consumer stage exists (P-032, P-039, P-040, P-043). The slot's version-change
        /// policy is the fixture's v1->v2 migration, which is what makes a schema change migratable at all.
        /// </summary>
        public static OwnershipStageDescriptor Descriptor() => Descriptor(PlansFixtureKeys.QuestMigrationV1ToV2);

        /// <summary>
        /// The same descriptor with an explicit version-change policy, so one test can build the case where the
        /// descriptor declares no migration for the slot at all (P-032).
        /// </summary>
        public static OwnershipStageDescriptor Descriptor(FactoryKey versionChangePolicy)
        {
            var accept = new DescriptorStage(
                PlansFixtureKeys.AcceptStage,
                1U,
                0,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(PlansFixtureKeys.AcceptSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                null);

            var settle = new DescriptorStage(
                PlansFixtureKeys.SettleStage,
                1U,
                1,
                new List<DescriptorSystem>
                {
                    new DescriptorSystem(PlansFixtureKeys.SettleSystem, SystemDispatchKind.ManagedSystem, null, null),
                },
                new List<int> { 0 });

            var slot = new OwnedSlotSpec(
                PlansFixtureKeys.QuestSlot,
                PlansFixtureKeys.SlotOwner,
                new SchemaRef(PlansFixtureKeys.QuestStateSchemaId, QuestSchemaVersion),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128),
                versionChangePolicy);

            var buffer = new BufferBinding(
                PlansFixtureKeys.StepBuffer,
                new List<FactoryKey> { PlansFixtureKeys.SettleSystem },
                PlansFixtureKeys.SettleStage);

            return new OwnershipStageDescriptor(
                PlanHashing.Of("gc-008 fixture ownership/stage descriptor v1"),
                new List<OwnedSlotSpec> { slot },
                new List<DescriptorStage> { accept, settle },
                new List<BufferBinding> { buffer });
        }

        /// <summary>The fixture migration registry: v1->v2 valid, plus a v1->v3 handler that must not be picked.</summary>
        public static MigrationRegistry Migrations()
        {
            return new MigrationRegistry(new List<ISlotMigration>
            {
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV2, 1U, QuestSchemaVersion, 10),
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV3, 1U, 3U, 100),
            });
        }

        /// <summary>A budget large enough for one slot of scratch and a modest binding count (P-022).</summary>
        public static PlanBudget Budget(ulong scratchBytes = 4096UL) =>
            new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, scratchBytes, 64UL);

        /// <summary>Two live targets of the same recipe in the root scope, which is the multi-target mount case.</summary>
        public static IReadOnlyList<TargetDefinition> TwoTargets() => new List<TargetDefinition>
        {
            new TargetDefinition(PlansFixtureKeys.Target(1UL), PlansFixtureKeys.CardRecipe, PlansFixtureKeys.RootScope),
            new TargetDefinition(PlansFixtureKeys.Target(2UL), PlansFixtureKeys.CardRecipe, PlansFixtureKeys.RootScope),
        };

        /// <summary>One capability declared for the card recipe: `Replace`, priority 10, value 3 (P-018).</summary>
        public static ProposedCapability LimitCapability(int value = 3, int priority = 10, CompositionPolicy policy = CompositionPolicy.Replace)
        {
            return new ProposedCapability(
                PlansFixtureKeys.LimitRule,
                new CapabilityRef(PlansFixtureKeys.SelectionLimit, 1U),
                PlansFixtureKeys.LimitSchema,
                0U,
                policy,
                value,
                priority,
                new List<DefinitionRef> { PlansFixtureKeys.CardRecipe });
        }

        /// <summary>A declaration on a second output slot; both slots may be supported at once (P-017, P-033).</summary>
        public static ProposedCapability FlagCapability(int value = 1, int priority = 10)
        {
            return new ProposedCapability(
                PlansFixtureKeys.FlagRule,
                new CapabilityRef(PlansFixtureKeys.QuestFlag, 1U),
                PlansFixtureKeys.FlagSchema,
                1U,
                CompositionPolicy.Replace,
                value,
                priority,
                new List<DefinitionRef> { PlansFixtureKeys.CardRecipe });
        }

        /// <summary>A mount of one plugin instance with one capability declaration (O-03).</summary>
        public static ProposedMount Mount(
            ulong ordinal,
            IReadOnlyList<ProposedCapability>? capabilities,
            ScopeId? scope = null)
        {
            return new ProposedMount(
                PlansFixtureKeys.Instance(ordinal),
                PlansFixtureKeys.PluginType(ordinal),
                PlansFixtureKeys.Provider(ordinal),
                scope ?? PlansFixtureKeys.RootScope,
                1UL,
                capabilities ?? new List<ProposedCapability> { LimitCapability() });
        }

        /// <summary>A proposal against one world/revision with a single mount (the ordinary automatic case).</summary>
        public static CompositionProposal MountProposal(
            WorldId world,
            CompositionRevision expectedRevision,
            AssemblyEpoch baseEpoch,
            ulong sequence = 1UL,
            IReadOnlyList<ProposedMount>? mounts = null,
            IReadOnlyList<ProposedUnmount>? unmounts = null,
            PropagationMode mode = PropagationMode.Automatic)
        {
            return new CompositionProposal(
                PlansFixtureKeys.Operation(world, sequence),
                PlanHashing.Of("gc-008 fixture input " + sequence),
                expectedRevision,
                baseEpoch,
                PlanHashing.Of("gc-008 fixture catalog"),
                mode,
                mounts ?? new List<ProposedMount> { Mount(1UL, null) },
                unmounts);
        }

        /// <summary>A live quest slot at schema version 1 with a non-default value, i.e. a real migration source.</summary>
        public static LiveSlotState QuestSlotState(TargetId target, int value = 7, uint version = 1U)
        {
            return new LiveSlotState(
                new StateSlotKey(target, PlansFixtureKeys.SlotOwner, PlansFixtureKeys.QuestSlot),
                version,
                value);
        }

        /// <summary>Builds one plan with the fixture inputs, so each test varies exactly one input (P-002).</summary>
        public static PlannedPublication Plan(
            CompositionProposal proposal,
            TargetBindingTable? current = null,
            IReadOnlyList<DerivedBindingRule>? rules = null,
            IReadOnlyList<TargetDefinition>? targets = null,
            IReadOnlyList<LiveSlotState>? liveSlots = null,
            MigrationScratch? scratch = null,
            InertAcquisitionSet? acquisitions = null,
            OwnershipStageDescriptor? descriptor = null,
            MigrationRegistry? migrations = null,
            PlanBudget? budget = null,
            CompositionRevision? currentRevision = null,
            AssemblyEpoch? currentEpoch = null)
        {
            WorldId world = proposal.Operation.World;
            return AssemblyPlanner.Build(
                proposal,
                descriptor ?? Descriptor(),
                currentRevision ?? proposal.ExpectedRevision,
                currentEpoch ?? proposal.BaseEpoch,
                current ?? TargetBindingTable.Empty,
                rules,
                targets ?? TwoTargets(),
                liveSlots,
                migrations ?? Migrations(),
                scratch ?? new MigrationScratch(Budget().ScratchCapacityBytes, Budget().ScratchBytesPerSlot),
                acquisitions ?? new InertAcquisitionSet(new RecordingResourceGate(), proposal.Operation),
                budget ?? Budget());
        }

        /// <summary>The published revision/epoch the fixture plans against (05 s2: a fresh session publishes 1).</summary>
        public static CompositionRevision Revision => CompositionRevision.First;

        public static AssemblyEpoch Epoch => AssemblyEpoch.First;
    }
}
