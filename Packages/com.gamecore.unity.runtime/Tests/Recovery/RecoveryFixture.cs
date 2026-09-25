// GameCore.Unity.Runtime.Tests — the shared fixture of the GC-017 recovery suite.
//
// Normative sources: 00 P-031 (a world that faulted after its first live write never resumes; its last committed
// image stays inspectable and its live storage is available only to controlled teardown/recovery), P-049 (recovery
// creates a new `WorldId` from the initial catalog and validates/rebuilds composition and recipes before admission
// reopens) and P-035 (a created world becomes Running only after its initial validated assembly publication).
//
// Every case of `InitialDefinitionRecoveryTests` drives the real modules through this fixture only: a real owned
// command-driven world created through `FixtureRegistration`, the real pure `AssemblyPlanner`, the real
// `AssemblyPublisher` over a real `TargetRegistry`, real ECS storage read back through that registry, and the
// world's own fault latch. Nothing here is a managed model of a world, and nothing here is a second implementation
// of the publisher or the planner.
//
// The shape is the GC-008 assembly fixture's, for the same reason: each fixture exists so each test varies exactly
// one input.
//
//   * `RecoveryFixtureKeys`       — the fixture's own identities, in a namespace word no other fixture uses;
//   * `RecoveryFixtureDescriptor` — one frozen ownership/stage descriptor built over the fixture world's own
//     generated-style system keys, so the published schedule is a real dispatch table the world can resolve;
//   * `RecoveryFixtureRecipes`    — one precompiled recipe, one pure registered migration, one lease gate;
//   * `RecoveryFixturePlans`      — the proposal/plan builders plus the target and live-state seed helpers;
//   * `RecoveryFixture`           — the world, its publisher, the one ordinary change that gives the world a real
//     published assembly, and the injected postwrite fault that faults it without publishing (P-031).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning;
using CompositionProposal = GameCore.Planning.CompositionProposal;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Recovery;
using Unity.Entities;
using NUnit.Framework;

namespace GameCore.Unity.Runtime.Tests.Recovery
{
    /// <summary>Stable identities of the recovery fixture; its own namespace word, like every other fixture's.</summary>
    public static class RecoveryFixtureKeys
    {
        public const ulong Namespace = 0x4730313752454356UL;

        /// <summary>High word of a fixture source world's session: a fresh incarnation per fixture (P-004).</summary>
        public const ulong WorldSalt = 0x47303137574F524CUL;

        /// <summary>
        /// High word of a destination session, deliberately different from <see cref="WorldSalt"/> so a destination
        /// can never collide with the source it recovers from (P-004, P-050).
        /// </summary>
        public const ulong DestinationSalt = 0x4730313744455354UL;

        public static readonly ScopeId RootScope = new ScopeId(new Id128(Namespace, 0x0001UL));

        public static readonly OwnerId SlotOwner = new OwnerId(new Id128(Namespace, 0x0010UL));

        public static readonly SlotId QuestSlot = new SlotId(new Id128(Namespace, 0x0011UL));

        public static readonly CapabilityId SelectionLimit = new CapabilityId(new Id128(Namespace, 0x0020UL));

        public static readonly RuleId LimitRule = new RuleId(new Id128(Namespace, 0x0021UL));

        public static readonly SchemaId CardSchemaId = new SchemaId(new Id128(Namespace, 0x0030UL));

        public static readonly SchemaId LimitSchemaId = new SchemaId(new Id128(Namespace, 0x0031UL));

        public static readonly SchemaId QuestStateSchemaId = new SchemaId(new Id128(Namespace, 0x0032UL));

        public static readonly DefinitionId CardRecipeId = new DefinitionId(new Id128(Namespace, 0x0040UL));

        public static readonly FactoryKey ApplyCardRecipe = Key(0x0050UL, "fixture.recovery.apply.card");

        public static readonly FactoryKey QuestMigrationV1ToV2 = Key(0x0051UL, "fixture.recovery.migrate.quest.v1-v2");

        public static readonly Id128 Issuer = new Id128(Namespace, 0x0060UL);

        /// <summary>State schema version the descriptor declares, so live state at 1 must migrate (P-032).</summary>
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
    public sealed class RecoveryFixtureMigration : ISlotMigration
    {
        private readonly int delta;

        public RecoveryFixtureMigration(FactoryKey key, uint fromVersion, uint toVersion, int delta)
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
    public sealed class RecoveryFixtureGate : IPlanResourceGate
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
            leaseId = new Id128(0x5245435652454356UL, next);
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
    public sealed class RecoveryFixtureApplier : ISpawnApplier
    {
        public FactoryKey Key => RecoveryFixtureKeys.ApplyCardRecipe;

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
    public static class RecoveryFixtureDescriptor
    {
        /// <summary>
        /// Stages reuse the fixture world's generated-style keys and fence indices, so the schedule the publisher
        /// installs at the fence is one the world can actually resolve (P-039): accept (2) then settle (3), with the
        /// settle stage producing the declared step buffer the project stage (5) consumes (P-043).
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
                RecoveryFixtureKeys.QuestSlot,
                RecoveryFixtureKeys.SlotOwner,
                new SchemaRef(RecoveryFixtureKeys.QuestStateSchemaId, RecoveryFixtureKeys.QuestSchemaVersion),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128),
                RecoveryFixtureKeys.QuestMigrationV1ToV2);

            var buffer = new BufferBinding(
                FixtureKeys.CounterBuffer,
                new List<FactoryKey> { FixtureKeys.SettleSystem },
                FixtureKeys.ProjectStage);

            return new OwnershipStageDescriptor(
                PlanHashing.Of("gc-017 recovery fixture ownership/stage descriptor v1"),
                new List<OwnedSlotSpec> { slot },
                new List<DescriptorStage> { accept, settle, project },
                new List<BufferBinding> { buffer });
        }
    }

    /// <summary>The fixture recipe table and migration registry.</summary>
    public static class RecoveryFixtureRecipes
    {
        public static SpawnRecipe CardRecipe(RecoveryFixtureApplier? applier = null)
        {
            var descriptor = new TargetDescriptor(
                RecoveryFixtureKeys.CardRecipe,
                new List<SchemaRef> { new SchemaRef(RecoveryFixtureKeys.CardSchemaId, 1U) },
                new List<CapabilityRef> { new CapabilityRef(RecoveryFixtureKeys.SelectionLimit, 1U) },
                new List<Id128> { new Id128(RecoveryFixtureKeys.Namespace, 0x0070UL) },
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(
                RecoveryFixtureKeys.CardRecipe,
                descriptor,
                new List<SchemaRef> { new SchemaRef(RecoveryFixtureKeys.CardSchemaId, 1U) },
                applier ?? new RecoveryFixtureApplier());
        }

        public static SpawnRecipeCatalog Catalog(RecoveryFixtureApplier? applier = null) =>
            new SpawnRecipeCatalog(new List<SpawnRecipe> { CardRecipe(applier) });

        /// <summary>The one migration the fixture descriptor's slot declares (P-032).</summary>
        public static MigrationRegistry Migrations() =>
            new MigrationRegistry(new List<ISlotMigration>
            {
                new RecoveryFixtureMigration(
                    RecoveryFixtureKeys.QuestMigrationV1ToV2,
                    1U,
                    RecoveryFixtureKeys.QuestSchemaVersion,
                    10),
            });
    }

    /// <summary>Proposal and plan builders plus the seed helpers for targets and live state.</summary>
    public static class RecoveryFixturePlans
    {
        /// <summary>One capability declaration for the card recipe: `Replace`, value 3 (P-018).</summary>
        public static ProposedCapability LimitCapability(
            int value = 3,
            int priority = 10,
            CompositionPolicy policy = CompositionPolicy.Replace)
        {
            return new ProposedCapability(
                RecoveryFixtureKeys.LimitRule,
                new CapabilityRef(RecoveryFixtureKeys.SelectionLimit, 1U),
                RecoveryFixtureKeys.LimitSchema,
                0U,
                policy,
                value,
                priority,
                new List<DefinitionRef> { RecoveryFixtureKeys.CardRecipe });
        }

        /// <summary>A mount of one plugin instance declaring the limit capability.</summary>
        public static ProposedMount Mount(
            ulong ordinal,
            ScopeId? scope = null,
            ProposedCapability? capability = null,
            ulong generation = 1UL)
        {
            return new ProposedMount(
                RecoveryFixtureKeys.Instance(ordinal),
                RecoveryFixtureKeys.PluginType(ordinal),
                RecoveryFixtureKeys.Provider(ordinal),
                scope ?? RecoveryFixtureKeys.RootScope,
                generation,
                new List<ProposedCapability> { capability ?? LimitCapability() });
        }

        /// <summary>A proposal against what the world currently publishes, which is what P-028 rechecks.</summary>
        public static CompositionProposal MountProposal(
            AssemblyPublisher publisher,
            ulong sequence,
            int priority,
            IReadOnlyList<ProposedMount>? mounts = null)
        {
            return new CompositionProposal(
                RecoveryFixtureKeys.Operation(publisher.World.World, sequence),
                PlanHashing.Of("gc-017 recovery fixture input " + sequence),
                publisher.PublishedRevision,
                publisher.World.CurrentEpoch,
                PlanHashing.Of("gc-017 recovery fixture catalog"),
                PropagationMode.Automatic,
                mounts ?? new List<ProposedMount>
                {
                    Mount(1UL, capability: LimitCapability(priority: priority)),
                },
                null);
        }

        /// <summary>Builds one plan with the fixture inputs, so each case varies exactly one of them (P-002).</summary>
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
                migrations ?? RecoveryFixtureRecipes.Migrations(),
                scratch ?? new MigrationScratch(planBudget.ScratchCapacityBytes, planBudget.ScratchBytesPerSlot),
                acquisitions ?? new InertAcquisitionSet(new RecoveryFixtureGate(), proposal.Operation),
                planBudget);
        }

        /// <summary>The two targets the fixture registers before the first publication (P-013: existing targets).</summary>
        public static IReadOnlyList<TargetDefinition> KnownTargets() => new List<TargetDefinition>
        {
            new TargetDefinition(RecoveryFixtureKeys.Target(1UL), RecoveryFixtureKeys.CardRecipe, RecoveryFixtureKeys.RootScope),
            new TargetDefinition(RecoveryFixtureKeys.Target(2UL), RecoveryFixtureKeys.CardRecipe, RecoveryFixtureKeys.RootScope),
        };

        /// <summary>Live state of one target's quest slot, as the publisher copies it into scratch (P-029).</summary>
        public static LiveSlotState QuestSlot(TargetId target, int value, uint version)
        {
            return new LiveSlotState(
                new StateSlotKey(target, RecoveryFixtureKeys.SlotOwner, RecoveryFixtureKeys.QuestSlot),
                version,
                value);
        }

        /// <summary>The live slot values of the two seeded targets, at the pre-migration schema version.</summary>
        public static IReadOnlyList<LiveSlotState> SeededSlots() => new List<LiveSlotState>
        {
            QuestSlot(RecoveryFixtureKeys.Target(1UL), 7, 1U),
            QuestSlot(RecoveryFixtureKeys.Target(2UL), 9, 1U),
        };

        /// <summary>
        /// Registers one existing target with its stable identity, its published stamp and the live state slot the
        /// descriptor's migration acts on. This is the "target that existed before the publication" of P-013's
        /// existing descendant case; a spawn is a separate publication (P-024).
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
                Slot = RecoveryFixtureKeys.QuestSlot,
                Owner = RecoveryFixtureKeys.SlotOwner,
                SchemaVersion = slotVersion,
                Value = slotValue,
                Active = 1,
            });

            return handle;
        }
    }

    /// <summary>
    /// One owned command-driven world with the real publisher of a real published assembly, which is what a recovery
    /// source has to be: a world that stopped after a postwrite failure, keeping its last committed image (P-031).
    /// </summary>
    public sealed class RecoveryFixture
    {
        /// <summary>Target capacity of a fixture world's registry, so a read-back can prove a registry is empty.</summary>
        public const uint RegistryCapacity = 32U;

        /// <summary>Fixture worlds created so far in this run: one fresh session per fixture (P-004).</summary>
        private static ulong worldOrdinal;

        /// <summary>Destination sessions handed out so far; a session is reserved, never reused (P-050).</summary>
        private static ulong destinationOrdinal;

        private readonly RecoveryFixtureGate gate;

        private RecoveryFixture(UnityWorldHost source, AssemblyPublisher publisher, RecoveryFixtureGate gate)
        {
            Source = source;
            Publisher = publisher;
            this.gate = gate;
        }

        /// <summary>The recovery source: the owned world the cases fault and recover from.</summary>
        public UnityWorldHost Source { get; }

        /// <summary>The real publisher of that world; its fault latch is the world's own latch (GC-017).</summary>
        public AssemblyPublisher Publisher { get; }

        public TargetRegistry Registry => Publisher.Registry;

        /// <summary>Binding rows of the source's last committed image, which a recovery must not carry over (P-049).</summary>
        public int PublishedBindingRowCount => Publisher.Published.BindingRowCount;

        /// <summary>
        /// One owned command-driven world, its real publisher and the two targets that exist before any publication,
        /// followed by one ordinary publication that leaves a real published assembly: two binding rows, two
        /// migrated slots and epoch 2. The world's initial assembly publishes revision/epoch 1 (05 s2), which is the
        /// first publication of the one series this world and its composition lane share (P-006).
        /// </summary>
        public static RecoveryFixture Create()
        {
            var world = new WorldId(new Id128(RecoveryFixtureKeys.WorldSalt, ++worldOrdinal));
            var operation = RecoveryFixtureKeys.Operation(world, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                WorldRegistration(),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);
            Assert.That(host!.Lifecycle, Is.EqualTo(WorldLifecycleState.Running), "P-035: a created world is Running");

            var applier = new RecoveryFixtureApplier();
            var gate = new RecoveryFixtureGate();
            var publisher = new AssemblyPublisher(
                host,
                new TargetRegistry(world, RegistryCapacity),
                RecoveryFixtureRecipes.Catalog(applier),
                RecoveryFixtureRecipes.Migrations(),
                RecoveryFixtureDescriptor.Build());

            var fixture = new RecoveryFixture(host, publisher, gate);

            RecoveryFixturePlans.SeedTarget(publisher, RecoveryFixtureKeys.Target(1UL), slotValue: 7, slotVersion: 1U);
            RecoveryFixturePlans.SeedTarget(publisher, RecoveryFixtureKeys.Target(2UL), slotValue: 9, slotVersion: 1U);
            Assert.That(publisher.Registry.Count, Is.EqualTo(2), "the two targets exist before the publication (P-013)");

            AssemblyPublicationReport report = fixture.PublishChange(1UL, priority: 10);
            Assert.That(report.Published, Is.True, report.ToString());
            Assert.That(report.MigratedSlots, Is.EqualTo(2), "both live slots were migrated on scratch (P-029)");
            Assert.That(fixture.PublishedBindingRowCount, Is.EqualTo(2), "both targets carry one row (P-017)");
            Assert.That(host.CurrentEpoch, Is.EqualTo(new AssemblyEpoch(2UL)), "the initial assembly was epoch 1");
            Assert.That(fixture.Publisher.PostwriteFaultCount, Is.EqualTo(0), "nothing has faulted yet");
            return fixture;
        }

        /// <summary>
        /// A fresh session id, for a recovery destination or for a handle nobody registered (P-004, P-050). Its high
        /// word is a different one from a fixture world's, so a destination is never the source's session.
        /// </summary>
        public WorldId NextWorldId() => new WorldId(new Id128(RecoveryFixtureKeys.DestinationSalt, ++destinationOrdinal));

        /// <summary>
        /// The one real change that faults the world after its first live write: the same mount at a higher priority
        /// is an effective change (P-018), and `FaultBoundary.FirstLiveWrite` is the boundary the real apply path
        /// reaches after the last authoritative write (P-031). The world faults, publishes no epoch and keeps its
        /// last committed image.
        /// </summary>
        public AssemblyPublicationReport FaultOnTheNextPublication()
        {
            Assert.That(
                Source.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.unity.test-framework, so a false here means the symbol is missing.");
            Assert.That(Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Running), "the source must be live before the fault");

            AssemblyEpoch epochBefore = Source.CurrentEpoch;
            int imagesBefore = Source.Publications.PublishedCount;
            int rowsBefore = PublishedBindingRowCount;
            int faultsBefore = Source.FaultCount;

            Source.Faults.Arm(FaultBoundary.FirstLiveWrite);
            Assert.That(Source.Faults.IsArmed(FaultBoundary.FirstLiveWrite), Is.True);
            Assert.That(Source.Faults.IsArmed(FaultBoundary.Validation), Is.False, "one armed boundary, not a blanket switch");
            Assert.That(Source.Faults.ArmedBoundaryCount, Is.EqualTo(1));

            AssemblyPublicationReport report = PublishChange(2UL, priority: 20);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Faulted), report.ToString());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(report.CrossedLiveWriteBoundary, Is.True, "the failure is after live writes (P-031)");
            Assert.That(report.PublishedToken, Is.Null, "no epoch or image publishes after a postwrite fault (P-031)");
            Assert.That(report.StructuralWrites, Is.GreaterThan(0), "the injected boundary is reached after real writes");
            Assert.That(Source.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite), Is.EqualTo(1));
            Assert.That(Source.Faults.InjectedCount, Is.EqualTo(1));
            Assert.That(Source.Faults.FailAfterFirstLiveWrite, Is.False, "the GC-008 boolean switch is not set");
            Assert.That(
                Source.Faults.FailDuringMigration,
                Is.False,
                "neither legacy boolean switch is set: the world faulted through the enumerated boundary");
            Assert.That(
                Source.Faults.PostWriteInjections,
                Is.EqualTo(0),
                "so the injection that faulted this world came from the boundary latch, not the boolean switch");
            Assert.That(Source.Faults.MigrationInjections, Is.EqualTo(0));
            Assert.That(Source.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted), "P-031: the world faults");
            Assert.That(Source.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(Source.FaultCount, Is.EqualTo(faultsBefore + 1));
            Assert.That(Source.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published (P-031)");
            Assert.That(Source.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(PublishedBindingRowCount, Is.EqualTo(rowsBefore), "the last committed image is inspectable (P-031)");
            Assert.That(Publisher.PostwriteFaultCount, Is.EqualTo(1));
            return report;
        }

        /// <summary>
        /// The registration of a fixture world: the fixture's own command-driven composition root, which the caller
        /// owns and hands to `Recover` (the call never replaces it). It is also the registration a case deliberately
        /// corrupts to prove that an invalid one creates nothing.
        /// </summary>
        public static UnityWorldRegistration WorldRegistration() =>
            FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: false);

        /// <summary>
        /// One well-formed recovery request over the fixture's world shape: a fresh destination session and the
        /// compiled initial catalog of a command-driven world (P-004, P-049, P-050).
        /// </summary>
        public static RecoveryRequest Request(WorldId source, WorldId destination)
        {
            return new RecoveryRequest(
                source,
                destination,
                FixtureRegistration.CommandWorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                ContentHash.Empty,
                RecoveryFixtureKeys.Operation(destination, 1UL),
                null);
        }

        /// <summary>
        /// The real read-back of a destination world: the same publisher, over that world's own fresh registry. A
        /// recovered world starts from the initial catalog, so its published assembly has no binding rows and its
        /// registry knows none of the source's targets (P-049: no state is carried across).
        /// </summary>
        public static AssemblyPublisher AttachPublisher(UnityWorldHost host)
        {
            return new AssemblyPublisher(
                host,
                new TargetRegistry(host.World, RegistryCapacity),
                RecoveryFixtureRecipes.Catalog(new RecoveryFixtureApplier()),
                RecoveryFixtureRecipes.Migrations(),
                RecoveryFixtureDescriptor.Build());
        }

        /// <summary>One host frame on the source, which is how a case proves the source never resumed (P-031).</summary>
        public WorldPumpResult PumpSource(ulong hostTicksNow)
        {
            Source.NotifyCommandAdmitted(1U);
            return Source.PumpFrame(hostTicksNow);
        }

        private AssemblyPublicationReport PublishChange(ulong ordinal, int priority)
        {
            // The publication belongs to the next composition publication of the world's ONE series (P-006).
            var laneRevision = new CompositionRevision(Publisher.PublishedRevision.Value + 1UL);
            var laneEpoch = new AssemblyEpoch(Source.CurrentEpoch.Value + 1UL);
            Assert.That(
                Publisher.TryAdoptLanePublication(laneRevision, laneEpoch, out AssemblyEpoch worldEpoch, out DiagnosticCode code),
                Is.True,
                "a publication must belong to an adopted composition publication: " + code);
            Assert.That(
                worldEpoch,
                Is.EqualTo(laneEpoch),
                "P-006: the composition publication IS the world's next assembly, with no offset");

            var acquisitions = new InertAcquisitionSet(gate, RecoveryFixtureKeys.Operation(Source.World, ordinal));
            Assert.That(
                acquisitions.TryAcquire(new ResourceKey(new Id128(RecoveryFixtureKeys.Namespace, 0x0900UL)), 64UL, null, out _),
                Is.True);
            Assert.That(
                acquisitions.TryAcquire(new ResourceKey(new Id128(RecoveryFixtureKeys.Namespace, 0x0901UL)), 64UL, null, out _),
                Is.True);
            Assert.That(acquisitions.CanEmitGameplay, Is.False, "a staged lease cannot emit gameplay (P-029)");

            CompositionProposal proposal = RecoveryFixturePlans.MountProposal(Publisher, ordinal, priority);
            PlannedPublication plan = RecoveryFixturePlans.Plan(
                Publisher,
                proposal,
                liveSlots: RecoveryFixturePlans.SeededSlots(),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());
            return Publisher.Publish(plan);
        }
    }
}
