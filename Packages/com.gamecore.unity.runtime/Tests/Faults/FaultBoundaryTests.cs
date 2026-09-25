// GameCore.Unity.Runtime.Tests — the package-level fault-boundary suite (GC-017, TEST-016 rows 1, 2, 4, 5 and 8).
//
// Every case drives the real modules: the real `AssemblyPublisher` over a real owned `Unity.Entities.World`, the
// real `AssemblyPlanner` over a frozen ownership/stage descriptor, the real `StagedResourceGate` and the world's
// own `AssemblyFaultInjection` latch, with live ECS storage read back through the publisher's target registry.
// Nothing here asserts a managed model of the world, and no case asserts a probability: each boundary is armed by
// identity and reached at exactly one place in the real apply path.
//
// The cases this file owns, by TEST-016 row:
//
//   1. manifest/dependency/capability/schedule validation — an injected validation fault refuses before any live
//      write, releases exactly what the plan staged, and the same edit publishes once the latch is disarmed.
//   2. resource acquisition or plan preparation — the refusal is a value (`TryAcquire` false +
//      `ResourceUnavailable`), in the staged gate and through the real publish path.
//   4. the publication fence — every tracked handle of the old assembly is settled before the injection refuses,
//      and the old assembly stays the published one.
//   5. migration and the postwrite boundaries — the prewrite forms preserve the old assembly and keep running; the
//      postwrite forms (first live write, gate installation) fault the world with no epoch and no image.
//   8. cleanup — a refused cleanup retains the staged ownership instead of reporting a release.
//   plus the latch's own trace: every reach is recorded in order with its operation and plan provenance.
//
// The two original GC-008 switches (`FailDuringMigration`, `FailAfterFirstLiveWrite`) are exercised next to their
// enumerated GC-017 counterparts, so the suite proves the original switch still works unchanged AND that it is a
// separate path from the latch.
//
// The latches exist only in a compilation that defines `GAMECORE_FAULT_INJECTION`, so every case begins by
// asserting `IsCompiledIn`; a false there means the symbol is missing rather than a boundary being unreachable.
//
// The fixture below stands this suite's own world up the way `AssemblyPublisherTests`/`AssemblyTestFixture` do.
// It is re-declared here instead of referenced because a Unity test assembly cannot reference another test
// assembly, so the identities, the descriptor, the recipes and the plan builders live in this file.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning;
using CompositionProposal = GameCore.Planning.CompositionProposal;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using NUnit.Framework;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Faults
{
    /// <summary>Stable identities of the fault-boundary fixture; a separate namespace word from every other suite.</summary>
    public static class FaultFixtureKeys
    {
        public const ulong Namespace = 0x4730313750414B54UL;

        public static readonly ScopeId RootScope = new ScopeId(new Id128(Namespace, 0x0001UL));

        public static readonly OwnerId SlotOwner = new OwnerId(new Id128(Namespace, 0x0010UL));

        public static readonly SlotId QuestSlot = new SlotId(new Id128(Namespace, 0x0011UL));

        public static readonly CapabilityId SelectionLimit = new CapabilityId(new Id128(Namespace, 0x0020UL));

        public static readonly RuleId LimitRule = new RuleId(new Id128(Namespace, 0x0021UL));

        public static readonly SchemaId CardSchemaId = new SchemaId(new Id128(Namespace, 0x0030UL));

        public static readonly SchemaId LimitSchemaId = new SchemaId(new Id128(Namespace, 0x0031UL));

        public static readonly SchemaId QuestStateSchemaId = new SchemaId(new Id128(Namespace, 0x0032UL));

        public static readonly DefinitionId CardRecipeId = new DefinitionId(new Id128(Namespace, 0x0040UL));

        public static readonly FactoryKey ApplyCardRecipe = Key(0x0050UL, "fixture.faults.apply.card");

        public static readonly FactoryKey QuestMigrationV1ToV2 = Key(0x0051UL, "fixture.faults.migrate.quest.v1-v2");

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

        public static ResourceKey Resource(ulong ordinal) => new ResourceKey(new Id128(Namespace, 0x0900UL + ordinal));

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
    public sealed class FaultFixtureMigration : ISlotMigration
    {
        private readonly int delta;

        public FaultFixtureMigration(FactoryKey key, uint fromVersion, uint toVersion, int delta)
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

    /// <summary>The fixture spawn applier: a direct typed install of the recipe's base layout (04 s6, 04 s8).</summary>
    public sealed class FaultFixtureApplier : ISpawnApplier
    {
        public FactoryKey Key => FaultFixtureKeys.ApplyCardRecipe;

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
    public static class FaultFixtureDescriptor
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
                FaultFixtureKeys.QuestSlot,
                FaultFixtureKeys.SlotOwner,
                new SchemaRef(FaultFixtureKeys.QuestStateSchemaId, FaultFixtureKeys.QuestSchemaVersion),
                1U,
                LastSupportPolicy.PreserveDormant,
                default(Id128),
                FaultFixtureKeys.QuestMigrationV1ToV2);

            var buffer = new BufferBinding(
                FixtureKeys.CounterBuffer,
                new List<FactoryKey> { FixtureKeys.SettleSystem },
                FixtureKeys.ProjectStage);

            return new OwnershipStageDescriptor(
                PlanHashing.Of("gc-017 fault-boundary fixture ownership/stage descriptor v1"),
                new List<OwnedSlotSpec> { slot },
                new List<DescriptorStage> { accept, settle, project },
                new List<BufferBinding> { buffer });
        }
    }

    /// <summary>The fixture recipe table and migration registry.</summary>
    public static class FaultFixtureRecipes
    {
        public static SpawnRecipe CardRecipe(FaultFixtureApplier? applier = null)
        {
            var descriptor = new TargetDescriptor(
                FaultFixtureKeys.CardRecipe,
                new List<SchemaRef> { new SchemaRef(FaultFixtureKeys.CardSchemaId, 1U) },
                new List<CapabilityRef> { new CapabilityRef(FaultFixtureKeys.SelectionLimit, 1U) },
                new List<Id128> { new Id128(FaultFixtureKeys.Namespace, 0x0070UL) },
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(
                FaultFixtureKeys.CardRecipe,
                descriptor,
                new List<SchemaRef> { new SchemaRef(FaultFixtureKeys.CardSchemaId, 1U) },
                applier ?? new FaultFixtureApplier());
        }

        public static SpawnRecipeCatalog Catalog(FaultFixtureApplier? applier = null) =>
            new SpawnRecipeCatalog(new List<SpawnRecipe> { CardRecipe(applier) });

        /// <summary>A registry with the one migration the fixture descriptor needs.</summary>
        public static MigrationRegistry Migrations() =>
            new MigrationRegistry(new List<ISlotMigration>
            {
                new FaultFixtureMigration(
                    FaultFixtureKeys.QuestMigrationV1ToV2,
                    1U,
                    FaultFixtureKeys.QuestSchemaVersion,
                    10),
            });
    }

    /// <summary>Proposal and plan builders plus the seed helpers for targets and live state.</summary>
    public static class FaultFixturePlans
    {
        /// <summary>One capability declaration for the card recipe: `Replace`, value 3, the given priority (P-018).</summary>
        public static ProposedCapability LimitCapability(
            int value = 3,
            int priority = 10,
            CompositionPolicy policy = CompositionPolicy.Replace)
        {
            return new ProposedCapability(
                FaultFixtureKeys.LimitRule,
                new CapabilityRef(FaultFixtureKeys.SelectionLimit, 1U),
                FaultFixtureKeys.LimitSchema,
                0U,
                policy,
                value,
                priority,
                new List<DefinitionRef> { FaultFixtureKeys.CardRecipe });
        }

        /// <summary>A mount of one plugin instance declaring the limit capability at the given priority.</summary>
        public static ProposedMount Mount(ulong ordinal, int priority)
        {
            return new ProposedMount(
                FaultFixtureKeys.Instance(ordinal),
                FaultFixtureKeys.PluginType(ordinal),
                FaultFixtureKeys.Provider(ordinal),
                FaultFixtureKeys.RootScope,
                1UL,
                new List<ProposedCapability> { LimitCapability(priority: priority) });
        }

        /// <summary>
        /// A proposal against what the world currently publishes, which is what P-028 rechecks. The operation id is
        /// <paramref name="sequence"/> (P-050: a new attempt has a new id) while the effective edit is identified by
        /// <paramref name="mountOrdinal"/>, so a case can re-propose the same edit under a new operation.
        /// </summary>
        public static CompositionProposal MountProposal(
            AssemblyPublisher publisher,
            ulong sequence,
            int priority,
            int inputSalt,
            ulong mountOrdinal)
        {
            return new CompositionProposal(
                FaultFixtureKeys.Operation(publisher.World.World, sequence),
                PlanHashing.Of("gc-017 fault fixture input " + sequence.ToString() + "/" + inputSalt.ToString()),
                publisher.PublishedRevision,
                publisher.World.CurrentEpoch,
                PlanHashing.Of("gc-017 fault fixture catalog"),
                PropagationMode.Automatic,
                new List<ProposedMount> { Mount(mountOrdinal, priority) },
                null);
        }

        /// <summary>Builds one plan with the fixture inputs, so each case varies exactly one of them (P-002).</summary>
        public static PlannedPublication Plan(
            AssemblyPublisher publisher,
            CompositionProposal proposal,
            InertAcquisitionSet acquisitions,
            IReadOnlyList<LiveSlotState>? liveSlots = null,
            IReadOnlyList<TargetDefinition>? targets = null,
            PlanBudget? budget = null)
        {
            PlanBudget planBudget = budget ?? new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, 4096UL, 64UL);
            return AssemblyPlanner.Build(
                proposal,
                publisher.Descriptor,
                publisher.PublishedRevision,
                publisher.World.CurrentEpoch,
                publisher.Published.Bindings,
                publisher.Published.Rules,
                targets ?? KnownTargets(),
                liveSlots,
                FaultFixtureRecipes.Migrations(),
                new MigrationScratch(planBudget.ScratchCapacityBytes, planBudget.ScratchBytesPerSlot),
                acquisitions,
                planBudget);
        }

        /// <summary>The two targets the fixture registers before the first publication (P-013: existing targets).</summary>
        public static IReadOnlyList<TargetDefinition> KnownTargets() => new List<TargetDefinition>
        {
            new TargetDefinition(FaultFixtureKeys.Target(1UL), FaultFixtureKeys.CardRecipe, FaultFixtureKeys.RootScope),
            new TargetDefinition(FaultFixtureKeys.Target(2UL), FaultFixtureKeys.CardRecipe, FaultFixtureKeys.RootScope),
        };

        /// <summary>Live state of one target's quest slot, as the publisher copies it into scratch (P-029).</summary>
        public static LiveSlotState QuestSlot(TargetId target, int value, uint version)
        {
            return new LiveSlotState(
                new StateSlotKey(target, FaultFixtureKeys.SlotOwner, FaultFixtureKeys.QuestSlot),
                version,
                value);
        }

        /// <summary>The live quest-slot state of both fixture targets at one schema version.</summary>
        public static IReadOnlyList<LiveSlotState> LiveSlots(int first, int second, uint version) =>
            new List<LiveSlotState>
            {
                QuestSlot(FaultFixtureKeys.Target(1UL), first, version),
                QuestSlot(FaultFixtureKeys.Target(2UL), second, version),
            };

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
                Slot = FaultFixtureKeys.QuestSlot,
                Owner = FaultFixtureKeys.SlotOwner,
                SchemaVersion = slotVersion,
                Value = slotValue,
                Active = 1,
            });

            return handle;
        }
    }

    [TestFixture]
    public sealed class FaultBoundaryTests
    {
        /// <summary>Session salt of this fixture; distinct from every other suite's, so worlds never collide.</summary>
        private const ulong SessionSalt = 0x4730313750414B54UL;

        private static ulong sessionSequence;

        [TearDown]
        public void TearDown() => UnityWorldRegistry.ResetAll();

        /// <summary>One owned world and its real publisher; the two targets exist before any publication.</summary>
        private sealed class Fixture
        {
            public Fixture(UnityWorldHost world, AssemblyPublisher publisher)
            {
                World = world;
                Publisher = publisher;
            }

            public UnityWorldHost World { get; }

            public AssemblyPublisher Publisher { get; }
        }

        /// <summary>
        /// One owned command-driven world and its real publisher. The world's initial assembly publishes
        /// revision/epoch 1 (05 s2), which is the first publication of the one series the world and its composition
        /// lane share (P-006), so the `ordinal`-th publication of this world is revision/epoch `ordinal + 1`.
        /// </summary>
        private static Fixture CreateFixture()
        {
            sessionSequence++;
            var world = new WorldId(new Id128(SessionSalt, sessionSequence));
            var operation = FaultFixtureKeys.Operation(world, 1UL);

            bool created = UnityWorldRegistry.TryCreate(
                FixtureRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty),
                FixtureRegistration.Create(FixtureWorldShape.CommandDriven, includeFaultStage: false),
                out UnityWorldHost? host,
                out WorldCreateResult result);

            Assert.That(created, Is.True, result.Code + ": " + result.Detail);
            Assert.That(host, Is.Not.Null);

            var applier = new FaultFixtureApplier();
            var publisher = new AssemblyPublisher(
                host!,
                new TargetRegistry(world, 8U),
                FaultFixtureRecipes.Catalog(applier),
                FaultFixtureRecipes.Migrations(),
                FaultFixtureDescriptor.Build());

            FaultFixturePlans.SeedTarget(publisher, FaultFixtureKeys.Target(1UL), slotValue: 7, slotVersion: 1U);
            FaultFixturePlans.SeedTarget(publisher, FaultFixtureKeys.Target(2UL), slotValue: 9, slotVersion: 1U);
            return new Fixture(host!, publisher);
        }

        /// <summary>
        /// The world's latch is the one the publisher reaches, and the latches are compiled in: an armed boundary is
        /// therefore the boundary the real apply path reaches, and a false <c>IsCompiledIn</c> is reported as a
        /// missing compilation symbol rather than as an unreachable boundary.
        /// </summary>
        private static void AssertLatchShared(Fixture fixture)
        {
            Assert.That(
                ReferenceEquals(fixture.World.Faults, fixture.Publisher.Faults),
                Is.True,
                "the publisher, the driver and the staged gate share the world's one latch (GC-017)");
            Assert.That(
                fixture.World.Faults.IsCompiledIn,
                Is.True,
                "the fault latches need GAMECORE_FAULT_INJECTION; it is declared by GameCore.Unity.Runtime.asmdef's"
                + " versionDefines entry on com.unity.test-framework, so a false here means the symbol is missing.");
        }

        /// <summary>The staged-resource gate of one world, with the world's own latch so both boundaries meet it.</summary>
        private static StagedResourceGate NewStagedGate(Fixture fixture) =>
            new StagedResourceGate(1024UL * 1024UL, FaultFixtureKeys.Issuer, fixture.World.Faults);

        /// <summary>
        /// Adopts the next composition publication of the world's ONE series (P-006) and stages its acquisitions
        /// through the given gate.
        /// </summary>
        private static InertAcquisitionSet AdoptAndStage(
            Fixture fixture,
            ulong ordinal,
            StagedResourceGate gate,
            out AssemblyEpoch laneEpoch)
        {
            var laneRevision = new CompositionRevision(ordinal + 1UL);
            laneEpoch = new AssemblyEpoch(ordinal + 1UL);

            Assert.That(
                fixture.Publisher.TryAdoptLanePublication(
                    laneRevision,
                    laneEpoch,
                    out AssemblyEpoch worldEpoch,
                    out DiagnosticCode code),
                Is.True,
                "a publication must belong to an adopted composition publication: " + code);
            Assert.That(worldEpoch, Is.EqualTo(laneEpoch),
                "P-006: the composition publication IS the world's next assembly, with no offset");

            return new InertAcquisitionSet(
                gate,
                FaultFixtureKeys.Operation(fixture.World.World, ordinal));
        }

        /// <summary>Stages one lease; the caller asserts what happened to it through the gate and the set.</summary>
        private static void StageOne(InertAcquisitionSet acquisitions, ulong ordinal, ulong bytes)
        {
            Assert.That(
                acquisitions.TryAcquire(FaultFixtureKeys.Resource(ordinal), bytes, null, out DiagnosticCode code),
                Is.True,
                "staging lease " + ordinal.ToString() + " failed: " + code);
        }

        /// <summary>
        /// Builds one plan whose mount is a real change (a higher priority than the published one). The mount
        /// identity defaults to the lane sequence; a caller that must re-propose *the same* edit under a new
        /// operation id passes <paramref name="mountOrdinal"/> explicitly, which is how P-050's "a new attempt has a
        /// new operation id" and "the same edit" are both expressible.
        /// </summary>
        private static PlannedPublication MountPlan(
            Fixture fixture,
            ulong laneSequence,
            int priority,
            IReadOnlyList<LiveSlotState>? liveSlots,
            InertAcquisitionSet acquisitions,
            ulong? mountOrdinal = null)
        {
            CompositionProposal proposal = FaultFixturePlans.MountProposal(
                fixture.Publisher,
                laneSequence,
                priority,
                inputSalt: 0,
                mountOrdinal: mountOrdinal ?? laneSequence);
            return FaultFixturePlans.Plan(fixture.Publisher, proposal, acquisitions, liveSlots);
        }

        /// <summary>The publisher's records of what it retained behind a faulted or refused publication.</summary>
        private static IReadOnlyList<Id128> QuarantineReferences(AssemblyPublicationReport report) =>
            report.Record.QuarantineReferences;

        private static bool HasId(IReadOnlyList<Id128> ids, Id128 candidate)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Row 1. A validation fault is a prewrite refusal: the prepared plan is refused before the fence, no live
        /// write happens, the staged leases are released, the plan is terminal `Rejected`, the old assembly stays
        /// published and running — and the very same edit publishes once the latch is disarmed, which is what makes
        /// the refusal the latch's doing rather than the plan's.
        /// </summary>
        [Test]
        public void AnInjectedValidationFaultRejectsBeforeAnyLiveWrite()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out AssemblyEpoch laneEpoch);
            StageOne(acquisitions, 1UL, 64UL);
            StageOne(acquisitions, 2UL, 64UL);
            Assert.That(acquisitions.CanEmitGameplay, Is.False, "a staged lease cannot emit gameplay (P-029)");

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());
            Assert.That(plan.Installs.Count, Is.GreaterThan(0), "the mount is a real change, not a no-op");

            fixture.Publisher.Faults.Arm(FaultBoundary.Validation);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            int rowsBefore = fixture.Publisher.Published.BindingRowCount;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0), "an injected validation fault makes no live write");
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running), "the old assembly runs on");
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published");
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore), "no image is published");
            Assert.That(
                fixture.Publisher.Published.BindingRowCount,
                Is.EqualTo(rowsBefore),
                "the published assembly is still the old one");
            Assert.That(fixture.Publisher.Published.Epoch, Is.EqualTo(epochBefore));

            // The staged work is released by the refusal, and the plan is terminal.
            Assert.That(acquisitions.RetainedLeaseIds(), Is.Empty, "the refusal released what the plan staged");
            Assert.That(acquisitions.CanEmitGameplay, Is.False);
            Assert.That(gate.ReleasedCount, Is.EqualTo(2));
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(plan.State.HasCrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.Publisher.PrewriteFailureCount, Is.EqualTo(1));

            // The boundary is reached exactly once and the record names it.
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.Validation), Is.EqualTo(1));
            Assert.That(fixture.World.Faults.Trace.Of(FaultBoundary.Validation)[0].Injected, Is.True);

            // Live storage is untouched: no binding row, and the state kept its value and schema version (P-029).
            for (int i = 0; i < 2; i++)
            {
                TargetId target = FaultFixtureKeys.Target((ulong)(i + 1));
                Assert.That(fixture.Publisher.ReadBindingRows(target), Is.Empty);
                IReadOnlyList<TargetSlotState> slots = fixture.Publisher.ReadSlotStates(target);
                Assert.That(slots.Count, Is.EqualTo(1));
                Assert.That(slots[0].SchemaVersion, Is.EqualTo(1U));
                Assert.That(slots[0].Value, Is.EqualTo(i == 0 ? 7 : 9));
            }

            // Disarm and publish the same edit: it publishes, on the composition publication the refusal did not
            // consume, so the only reason for the refusal was the armed boundary.
            fixture.Publisher.Faults.Disarm(FaultBoundary.Validation);
            Assert.That(fixture.Publisher.Faults.ArmedBoundaryCount, Is.EqualTo(0));
            Assert.That(
                fixture.Publisher.HasAdoptedPublication,
                Is.True,
                "a prewrite refusal does not consume the pending composition publication");

            StagedResourceGate retryGate = NewStagedGate(fixture);
            InertAcquisitionSet retry = new InertAcquisitionSet(
                retryGate,
                FaultFixtureKeys.Operation(fixture.World.World, 2UL));
            StageOne(retry, 1UL, 64UL);
            StageOne(retry, 2UL, 64UL);

            AssemblyPublicationReport retryReport = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, priority: 11, liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U), acquisitions: retry, mountOrdinal: 1UL));

            Assert.That(retryReport.Published, Is.True, retryReport.ToString());
            Assert.That(retryReport.WorldEpochAfter, Is.EqualTo(laneEpoch));
            Assert.That(retryReport.MigratedSlots, Is.EqualTo(2), "the refused plan wrote no state, so it still migrates");
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(2));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(laneEpoch));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore + 1));
        }

        /// <summary>
        /// Row 2. A resource-acquisition refusal is a value, not an exception: the staged gate returns false with
        /// `ResourceUnavailable`, the acquisition set records exactly one failed acquisition and stages nothing, and
        /// the same boundary reached through the real publish path refuses the plan before any live write and
        /// releases what it had staged. The failure's provenance is the operation and the plan hash the latch
        /// recorded (P-052).
        /// </summary>
        [Test]
        public void AnInjectedAcquisitionFaultRefusesTheLeaseAndReleasesWhatWasStaged()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            var gate = new StagedResourceGate(1024UL, FaultFixtureKeys.Issuer, fixture.World.Faults);
            var acquisitions = new InertAcquisitionSet(
                gate,
                FaultFixtureKeys.Operation(fixture.World.World, 1UL));

            fixture.World.Faults.Arm(FaultBoundary.Acquisition);

            bool acquired = acquisitions.TryAcquire(
                FaultFixtureKeys.Resource(1UL),
                64UL,
                null,
                out DiagnosticCode code);

            Assert.That(acquired, Is.False, "an armed acquisition boundary refuses the lease");
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(acquisitions.FailedCount, Is.EqualTo(1), "the failed acquisition is recorded");
            Assert.That(acquisitions.RetainedLeaseIds(), Is.Empty, "nothing was staged, so nothing is retained");
            Assert.That(acquisitions.Count, Is.EqualTo(0));
            Assert.That(gate.AcquiredCount, Is.EqualTo(0), "the gate handed out no lease");
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(gate.InjectionRefusalCount, Is.EqualTo(1), "the injected refusal is counted by the gate"
                + " without touching the byte-ceiling counter, which a budget reading owns");
            Assert.That(gate.BudgetExceededCount, Is.EqualTo(0), "the 64-byte lease is far below the ceiling");
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.Acquisition), Is.EqualTo(1));
            IReadOnlyList<FaultRecord> refusedRecords = fixture.World.Faults.Trace.Of(FaultBoundary.Acquisition);
            Assert.That(refusedRecords.Count, Is.EqualTo(1));
            Assert.That(refusedRecords[0].Injected, Is.True);
            Assert.That(refusedRecords[0].Detail, Does.Contain("refused as a value"));

            // Disarm: the same acquisition now succeeds, so the refusal was the latch's and not the gate's ceiling.
            fixture.World.Faults.Disarm(FaultBoundary.Acquisition);
            bool retried = acquisitions.TryAcquire(
                FaultFixtureKeys.Resource(1UL),
                64UL,
                null,
                out DiagnosticCode retryCode);

            Assert.That(retried, Is.True, "the production path acquires the lease");
            Assert.That(retryCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(acquisitions.FailedCount, Is.EqualTo(1), "the recorded failure is not erased");
            Assert.That(acquisitions.RetainedLeaseIds().Count, Is.EqualTo(1));
            Assert.That(gate.AcquiredCount, Is.EqualTo(1));
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(gate.ReleasedCount, Is.EqualTo(0));

            // The same boundary through the real publish path: the plan is refused before the fence, exactly what
            // it had staged is released, and the old assembly stays the published one.
            StagedResourceGate publishGate = NewStagedGate(fixture);
            InertAcquisitionSet staged = AdoptAndStage(fixture, 1UL, publishGate, out AssemblyEpoch laneEpoch);
            StageOne(staged, 3UL, 64UL);
            StageOne(staged, 4UL, 64UL);

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: staged);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            fixture.World.Faults.Arm(FaultBoundary.Acquisition);
            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            int reachesBeforePublish = fixture.World.Faults.ReachCountOf(FaultBoundary.Acquisition);

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0));
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(report.Detail, Does.Contain("acquisition"), "the failure detail names the boundary");
            Assert.That(report.Record.Operation.Equals(plan.Plan.Operation), Is.True, "the failure names its operation");
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0), "the old assembly is published");
            Assert.That(staged.RetainedLeaseIds(), Is.Empty, "the refusal released what the plan staged");
            Assert.That(publishGate.ReleasedCount, Is.EqualTo(2));
            Assert.That(publishGate.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));

            // The publish reached the boundary exactly once more, and both reaches are recorded: the value refusal
            // and the publish refusal, each with its own form.
            Assert.That(
                fixture.World.Faults.ReachCountOf(FaultBoundary.Acquisition),
                Is.EqualTo(reachesBeforePublish + 1),
                "the publication reached the acquisition boundary once");
            IReadOnlyList<FaultRecord> acquisitionRecords =
                fixture.World.Faults.Trace.Of(FaultBoundary.Acquisition);
            Assert.That(acquisitionRecords.Count, Is.EqualTo(2));
            FaultRecord published = acquisitionRecords[1];
            Assert.That(published.Operation.Equals(plan.Plan.Operation), Is.True);
            Assert.That(published.PlanHash.Equals(plan.Plan.PlanHash), Is.True);
            Assert.That(published.ToLine(), Does.Contain(plan.Plan.PlanHash.ToHex()));

            // Still running on the old assembly, and the pending composition publication is reusable.
            fixture.World.NotifyCommandAdmitted(1U);
            fixture.World.PumpFrame(1_000_000UL);
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.First), "the old assembly keeps running");
            Assert.That(fixture.World.Faults.IsArmed(FaultBoundary.Acquisition), Is.True);

            fixture.World.Faults.Disarm(FaultBoundary.Acquisition);
            StagedResourceGate retryGate = NewStagedGate(fixture);
            InertAcquisitionSet retry = new InertAcquisitionSet(
                retryGate,
                FaultFixtureKeys.Operation(fixture.World.World, 2UL));
            StageOne(retry, 3UL, 64UL);
            StageOne(retry, 4UL, 64UL);
            AssemblyPublicationReport retryReport = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, priority: 11, liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U), acquisitions: retry, mountOrdinal: 1UL));
            Assert.That(retryReport.Published, Is.True, retryReport.ToString());
            Assert.That(retryReport.WorldEpochAfter, Is.EqualTo(laneEpoch));
        }

        /// <summary>
        /// Row 4. The fence runs before its boundary is reached, so an injected fence fault refuses the publication
        /// with every tracked handle of the old assembly already settled: the step, ingress and output fences are
        /// completed and cleared, the ledger's jobs are settled, nothing was written and the old assembly is still
        /// the published one and still running.
        /// </summary>
        [Test]
        public void AnInjectedFenceFaultSettlesTrackedHandlesAndKeepsTheOldAssembly()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            // Leave a real tracked handle pending: dispatching the world's own step table runs its systems (which
            // schedule a real job) without committing a step, so the job and its stage fence are still live.
            DispatchRunResult run = fixture.World.Driver.Dispatch(new StageDispatchRequest(
                fixture.World.World,
                fixture.World.CurrentEpoch,
                fixture.World.CurrentStep,
                fixture.World.StepGroup.InstalledPlan.ToOrderedTable(fixture.World.CurrentEpoch)));

            Assert.That(run.Completed, Is.True, run.Code + " at " + run.StoppedAtIndex.ToString());
            Assert.That(fixture.World.Ledger.OutstandingJobCount, Is.GreaterThan(0), "a job of the step is tracked");
            Assert.That(
                fixture.World.StepGroup.Fences!.Combined.Equals(default(JobHandle)),
                Is.False,
                "the step's fence holds the scheduled job's handle");

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out AssemblyEpoch laneEpoch);
            StageOne(acquisitions, 1UL, 64UL);
            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            fixture.Publisher.Faults.Arm(FaultBoundary.Fence);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            LogicalStepId stepBefore = fixture.World.CurrentStep;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0), "the injection happens before any live write");
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(
                report.DrainedHandles,
                Is.GreaterThanOrEqualTo(3),
                "the fence settled the old assembly's step, ingress and output fences before the boundary");

            // The tracked handle really is settled, and so is every tracked job, before anything else happened.
            Assert.That(
                fixture.World.StepGroup.Fences!.Combined.Equals(default(JobHandle)),
                Is.True,
                "the step fence was completed and cleared by the fence (P-041)");
            Assert.That(
                fixture.World.IngressGroup.Fences!.Combined.Equals(default(JobHandle)),
                Is.True,
                "the ingress fence was completed and cleared by the fence");
            Assert.That(fixture.World.Ledger.OutstandingJobCount, Is.EqualTo(0), "every tracked job was settled");
            Assert.That(fixture.World.Driver.RetainedJobs.Count, Is.EqualTo(0));

            // The old assembly remains the published one and the world keeps running.
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0));
            Assert.That(fixture.Publisher.Published.Epoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(stepBefore), "no step advanced");
            Assert.That(acquisitions.RetainedLeaseIds(), Is.Empty, "the refusal released the staged lease");
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.Fence), Is.EqualTo(1));
            Assert.That(fixture.World.Faults.Trace.Of(FaultBoundary.Fence)[0].Injected, Is.True);

            fixture.World.NotifyCommandAdmitted(1U);
            fixture.World.PumpFrame(1_000_000UL);
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.First), "the old assembly still runs");

            // Disarm: the same edit publishes, so the fence consumed nothing but the failure.
            fixture.Publisher.Faults.Disarm(FaultBoundary.Fence);
            StagedResourceGate retryGate = NewStagedGate(fixture);
            InertAcquisitionSet retry = new InertAcquisitionSet(
                retryGate,
                FaultFixtureKeys.Operation(fixture.World.World, 2UL));
            StageOne(retry, 1UL, 64UL);
            AssemblyPublicationReport retryReport = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, priority: 11, liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U), acquisitions: retry, mountOrdinal: 1UL));
            Assert.That(retryReport.Published, Is.True, retryReport.ToString());
            Assert.That(retryReport.MigratedSlots, Is.EqualTo(2), "the refused plan wrote no state, so it still migrates");
            Assert.That(retryReport.WorldEpochAfter, Is.EqualTo(laneEpoch));
        }

        /// <summary>
        /// Row 5, prewrite half: the migration boundary is reached on copied values only, so an injected migration
        /// fault is a refusal that releases the staged leases, preserves the old assembly's live state and keeps the
        /// world running — and the same edit publishes after disarming. The second half drives the original GC-008
        /// switch and shows it is a separate path: it fires once, before the enumerated boundary is even reached,
        /// and preserves exactly the same prewrite shape.
        /// </summary>
        [Test]
        public void AnInjectedMigrationFaultAndTheOriginalPrewriteSwitchBothPreserveTheOldAssembly()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out _);
            StageOne(acquisitions, 1UL, 64UL);

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            fixture.Publisher.Faults.Arm(FaultBoundary.Migration);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0));
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published");
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0));
            Assert.That(gate.ReleasedCount, Is.EqualTo(1), "the staged lease is released in the refusal (P-029)");
            Assert.That(acquisitions.RetainedLeaseIds(), Is.Empty);
            Assert.That(acquisitions.CanEmitGameplay, Is.False);
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(plan.State.HasCrossedLiveWriteBoundary, Is.False);
            Assert.That(fixture.Publisher.PrewriteFailureCount, Is.EqualTo(1));
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.Migration), Is.EqualTo(1));
            Assert.That(fixture.World.Faults.Trace.Of(FaultBoundary.Migration)[0].Injected, Is.True);
            Assert.That(
                fixture.Publisher.Faults.MigrationInjections,
                Is.EqualTo(0),
                "the enumerated latch, not the original switch, produced this refusal");

            for (int i = 0; i < 2; i++)
            {
                TargetId target = FaultFixtureKeys.Target((ulong)(i + 1));
                Assert.That(fixture.Publisher.ReadBindingRows(target), Is.Empty);
                IReadOnlyList<TargetSlotState> slots = fixture.Publisher.ReadSlotStates(target);
                Assert.That(slots[0].SchemaVersion, Is.EqualTo(1U), "live state kept its schema version");
                Assert.That(slots[0].Value, Is.EqualTo(i == 0 ? 7 : 9));
            }

            fixture.World.NotifyCommandAdmitted(1U);
            fixture.World.PumpFrame(1_000_000UL);
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.First), "the old assembly keeps running");

            fixture.Publisher.Faults.Disarm(FaultBoundary.Migration);
            StagedResourceGate retryGate = NewStagedGate(fixture);
            InertAcquisitionSet retry = new InertAcquisitionSet(
                retryGate,
                FaultFixtureKeys.Operation(fixture.World.World, 2UL));
            StageOne(retry, 1UL, 64UL);
            AssemblyPublicationReport retryReport = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, priority: 11, liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U), acquisitions: retry, mountOrdinal: 1UL));

            Assert.That(retryReport.Published, Is.True, retryReport.ToString());
            Assert.That(retryReport.MigratedSlots, Is.EqualTo(2), "the retry migrated both live slots");
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(2));

            // The original GC-008 switch, unchanged, on a world of its own.
            Fixture legacy = CreateFixture();
            StagedResourceGate legacyGate = NewStagedGate(legacy);
            InertAcquisitionSet legacyStaged = AdoptAndStage(legacy, 1UL, legacyGate, out _);
            StageOne(legacyStaged, 1UL, 64UL);
            PlannedPublication legacyPlan = MountPlan(
                legacy,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: legacyStaged);
            Assert.That(legacyPlan.IsPrepared, Is.True, legacyPlan.State.Describe());

            legacy.Publisher.Faults.FailDuringMigration = true;
            AssemblyEpoch legacyEpochBefore = legacy.World.CurrentEpoch;
            AssemblyPublicationReport legacyReport = legacy.Publisher.Publish(legacyPlan);

            Assert.That(legacyReport.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(legacyReport.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(legacyReport.StructuralWrites, Is.EqualTo(0));
            Assert.That(legacyReport.CrossedLiveWriteBoundary, Is.False);
            Assert.That(legacy.Publisher.Faults.MigrationInjections, Is.EqualTo(1));
            Assert.That(
                legacy.World.Faults.ReachCountOf(FaultBoundary.Migration),
                Is.EqualTo(0),
                "the original switch throws before the enumerated boundary is reached: they are separate paths");
            Assert.That(legacy.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(legacy.World.CurrentEpoch, Is.EqualTo(legacyEpochBefore));
            Assert.That(legacyGate.ReleasedCount, Is.EqualTo(1), "the same staged release happens on this path too");
            Assert.That(legacyStaged.RetainedLeaseIds(), Is.Empty);
            Assert.That(legacyPlan.State.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(legacy.Publisher.Published.BindingRowCount, Is.EqualTo(0));
            Assert.That(legacy.Publisher.ReadSlotStates(FaultFixtureKeys.Target(1UL))[0].SchemaVersion, Is.EqualTo(1U));

            legacy.Publisher.Faults.FailDuringMigration = false;
            StagedResourceGate legacyRetryGate = NewStagedGate(legacy);
            InertAcquisitionSet legacyRetry = new InertAcquisitionSet(
                legacyRetryGate,
                FaultFixtureKeys.Operation(legacy.World.World, 2UL));
            StageOne(legacyRetry, 1UL, 64UL);
            AssemblyPublicationReport legacyRetryReport = legacy.Publisher.Publish(
                MountPlan(legacy, 2UL, priority: 11, liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U), acquisitions: legacyRetry, mountOrdinal: 1UL));
            Assert.That(legacyRetryReport.Published, Is.True, legacyRetryReport.ToString());
            Assert.That(legacyRetryReport.MigratedSlots, Is.EqualTo(2));
        }

        /// <summary>
        /// Row 5, postwrite half. The first-live-write boundary is reached after the last authoritative write of the
        /// fence, so an injected fault there and the original GC-008 postwrite switch must produce the same thing:
        /// `Faulted` with `ApplyFault`, the live-write boundary crossed, no published token, live writes already
        /// made, the world `Faulted`, no epoch and no image, no further pump and no further publication — while the
        /// staged leases stay retained behind the fault because unfinished work may still reach them (P-031, P-048).
        /// </summary>
        [Test]
        public void AnInjectedFirstLiveWriteFaultAndTheOriginalPostwriteSwitchBothFaultTheWorld()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out _);
            StageOne(acquisitions, 1UL, 64UL);
            StageOne(acquisitions, 2UL, 64UL);

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            fixture.Publisher.Faults.Arm(FaultBoundary.FirstLiveWrite);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(report.CrossedLiveWriteBoundary, Is.True, "the failure happened after live writes");
            Assert.That(report.PublishedToken, Is.Null, "no image is published after a postwrite fault (P-031)");
            Assert.That(report.StructuralWrites, Is.GreaterThan(0), "live rows were already written");
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(fixture.World.FaultCode, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore), "no epoch is published");
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore), "no image is published");
            Assert.That(fixture.Publisher.Published.Epoch, Is.EqualTo(epochBefore));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Faulted));
            Assert.That(plan.State.HasCrossedLiveWriteBoundary, Is.True);
            Assert.That(fixture.Publisher.PostwriteFaultCount, Is.EqualTo(1));
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite), Is.EqualTo(1));
            Assert.That(fixture.World.Faults.Trace.Of(FaultBoundary.FirstLiveWrite)[0].Injected, Is.True);

            // The staged ownership is retained and reported: unfinished work may still reach it (P-048).
            Assert.That(QuarantineReferences(report).Count, Is.EqualTo(2));
            Assert.That(gate.ReleasedCount, Is.EqualTo(0), "a postwrite fault releases nothing");
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(2));
            Assert.That(acquisitions.RetainedLeaseIds().Count, Is.EqualTo(2));

            // No simulation resumes and no further publication is accepted.
            int framesBefore = fixture.World.PumpCount;
            fixture.World.NotifyCommandAdmitted(1U);
            WorldPumpResult pump = fixture.World.PumpFrame(2_000_000UL);
            Assert.That(pump.Pumped, Is.False, "a faulted world admits nothing (P-031)");
            Assert.That(fixture.World.PumpCount, Is.EqualTo(framesBefore));
            Assert.That(fixture.World.CurrentStep, Is.EqualTo(LogicalStepId.Zero));

            InertAcquisitionSet later = new InertAcquisitionSet(
                NewStagedGate(fixture),
                FaultFixtureKeys.Operation(fixture.World.World, 2UL));
            AssemblyPublicationReport second = fixture.Publisher.Publish(
                MountPlan(fixture, 2UL, priority: 12, liveSlots: null, acquisitions: later));
            Assert.That(second.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(second.CrossedLiveWriteBoundary, Is.False);
            Assert.That(second.StructuralWrites, Is.EqualTo(0));

            // The original GC-008 postwrite switch, unchanged, on a world of its own.
            Fixture legacy = CreateFixture();
            StagedResourceGate legacyGate = NewStagedGate(legacy);
            InertAcquisitionSet legacyStaged = AdoptAndStage(legacy, 1UL, legacyGate, out _);
            StageOne(legacyStaged, 1UL, 64UL);
            PlannedPublication legacyPlan = MountPlan(
                legacy,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: legacyStaged);
            Assert.That(legacyPlan.IsPrepared, Is.True, legacyPlan.State.Describe());

            legacy.Publisher.Faults.FailAfterFirstLiveWrite = true;
            AssemblyEpoch legacyEpochBefore = legacy.World.CurrentEpoch;
            int legacyImagesBefore = legacy.World.Publications.PublishedCount;
            AssemblyPublicationReport legacyReport = legacy.Publisher.Publish(legacyPlan);

            Assert.That(legacyReport.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(legacyReport.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(legacyReport.CrossedLiveWriteBoundary, Is.True);
            Assert.That(legacyReport.PublishedToken, Is.Null);
            Assert.That(legacyReport.StructuralWrites, Is.GreaterThan(0));
            Assert.That(legacy.Publisher.Faults.PostWriteInjections, Is.EqualTo(1));
            Assert.That(
                legacy.World.Faults.ReachCountOf(FaultBoundary.FirstLiveWrite),
                Is.EqualTo(0),
                "the original switch throws before the enumerated boundary is reached: they are separate paths");
            Assert.That(legacy.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(legacy.World.CurrentEpoch, Is.EqualTo(legacyEpochBefore));
            Assert.That(legacy.World.Publications.PublishedCount, Is.EqualTo(legacyImagesBefore));
            Assert.That(legacy.Publisher.Published.Epoch, Is.EqualTo(legacyEpochBefore));
            Assert.That(legacyPlan.State.Phase, Is.EqualTo(PlanPhase.Faulted));
            Assert.That(QuarantineReferences(legacyReport).Count, Is.EqualTo(1), "the staged lease stays retained");
            Assert.That(legacyGate.ReleasedCount, Is.EqualTo(0));
        }

        /// <summary>
        /// The gate-installation boundary: installing the new execution graph and opening its staged gates happens
        /// after the apply stage already wrote live storage, so an injected fault there is postwrite — `Faulted`,
        /// `ApplyFault`, no epoch and no image, the world stopped — while every staged lease is still retained in the
        /// report, because nothing in this path has released anything.
        /// </summary>
        [Test]
        public void AnInjectedGateInstallationFaultFaultsAfterTheApplyStage()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out _);
            StageOne(acquisitions, 1UL, 64UL);
            StageOne(acquisitions, 2UL, 64UL);

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            fixture.Publisher.Faults.Arm(FaultBoundary.GateInstallation);

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;

            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Faulted));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
            Assert.That(report.CrossedLiveWriteBoundary, Is.True);
            Assert.That(report.PublishedToken, Is.Null);
            Assert.That(report.StructuralWrites, Is.GreaterThan(0), "the apply stage already wrote live rows");
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Faulted));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Faulted));
            Assert.That(fixture.Publisher.PostwriteFaultCount, Is.EqualTo(1));

            // Reached exactly once, after the apply stage, with provenance.
            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.GateInstallation), Is.EqualTo(1));
            FaultRecord record = fixture.World.Faults.Trace.Of(FaultBoundary.GateInstallation)[0];
            Assert.That(record.Injected, Is.True);
            Assert.That(record.Operation.Equals(plan.Plan.Operation), Is.True);
            Assert.That(record.PlanHash.Equals(plan.Plan.PlanHash), Is.True);
            Assert.That(record.ToLine(), Does.Contain(FaultBoundaryText.Of(FaultBoundary.GateInstallation)));
            Assert.That(record.ToLine(), Does.Contain(plan.Plan.Operation.ToString()));
            Assert.That(record.ToLine(), Does.Contain(plan.Plan.PlanHash.ToHex()));

            // Every staged lease is still retained in the record: this path released nothing.
            IReadOnlyList<Id128> retained = QuarantineReferences(report);
            IReadOnlyList<Id128> stillOwned = acquisitions.RetainedLeaseIds();
            Assert.That(retained.Count, Is.EqualTo(acquisitions.Count));
            Assert.That(stillOwned.Count, Is.EqualTo(retained.Count));
            for (int i = 0; i < stillOwned.Count; i++)
            {
                Assert.That(HasId(retained, stillOwned[i]), Is.True);
            }

            Assert.That(gate.ReleasedCount, Is.EqualTo(0));
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(2));
            Assert.That(acquisitions.RetainedLeaseIds().Count, Is.EqualTo(2));

            // The world is stopped for good: no pump, no publication.
            fixture.World.NotifyCommandAdmitted(1U);
            WorldPumpResult pump = fixture.World.PumpFrame(2_000_000UL);
            Assert.That(pump.Pumped, Is.False);
            Assert.That(pump.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
        }

        /// <summary>
        /// Row 8. The refusal path reaches its cleanup boundary first, so an armed cleanup latch plus a prewrite
        /// failure is the "one release refuses" case: the plan is refused, nothing is reported as released, every
        /// staged lease stays retained and is named in the report, and the retention is real — once the latch is
        /// disarmed the same acquisition set releases everything through the same gate.
        /// </summary>
        [Test]
        public void AnInjectedCleanupFaultRetainsStagedOwnershipInsteadOfReportingRelease()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);

            StagedResourceGate gate = NewStagedGate(fixture);
            InertAcquisitionSet acquisitions = AdoptAndStage(fixture, 1UL, gate, out _);
            StageOne(acquisitions, 1UL, 64UL);
            StageOne(acquisitions, 2UL, 64UL);

            PlannedPublication plan = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: acquisitions);
            Assert.That(plan.IsPrepared, Is.True, plan.State.Describe());

            // The cleanup boundary is part of the refusal path, so a prewrite migration failure is what reaches it.
            fixture.Publisher.Faults.Arm(FaultBoundary.Cleanup);
            fixture.Publisher.Faults.FailDuringMigration = true;

            AssemblyEpoch epochBefore = fixture.World.CurrentEpoch;
            int imagesBefore = fixture.World.Publications.PublishedCount;
            AssemblyPublicationReport report = fixture.Publisher.Publish(plan);

            Assert.That(report.Outcome, Is.EqualTo(Outcome.Rejected));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(report.StructuralWrites, Is.EqualTo(0));
            Assert.That(report.CrossedLiveWriteBoundary, Is.False);
            Assert.That(report.Detail, Does.Contain("prewrite migration failure"));
            Assert.That(report.Detail, Does.Contain("cleanup fault"), "the failure names the cleanup boundary");
            Assert.That(fixture.World.Lifecycle, Is.EqualTo(WorldLifecycleState.Running));
            Assert.That(fixture.World.CurrentEpoch, Is.EqualTo(epochBefore));
            Assert.That(fixture.World.Publications.PublishedCount, Is.EqualTo(imagesBefore));
            Assert.That(fixture.Publisher.Published.BindingRowCount, Is.EqualTo(0));
            Assert.That(plan.State.Phase, Is.EqualTo(PlanPhase.Rejected));

            // No false release: nothing was released, nothing was reported as released, and both staged leases are
            // still owned by the plan and named by the report.
            Assert.That(gate.ReleasedCount, Is.EqualTo(0), "the refused cleanup released nothing");
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(2));
            Assert.That(
                acquisitions.RetainedLeaseIds().Count,
                Is.EqualTo(2),
                "no lease may be reported as released when its release was refused");
            IReadOnlyList<Id128> reported = QuarantineReferences(report);
            IReadOnlyList<Id128> stillStaged = acquisitions.RetainedLeaseIds();
            Assert.That(reported.Count, Is.EqualTo(2), "the retained leases are reported, not silently dropped");
            Assert.That(stillStaged.Count, Is.EqualTo(2));
            for (int i = 0; i < stillStaged.Count; i++)
            {
                Assert.That(HasId(reported, stillStaged[i]), Is.True);
            }

            Assert.That(fixture.World.Faults.ReachCountOf(FaultBoundary.Cleanup), Is.EqualTo(1));
            Assert.That(fixture.World.Faults.Trace.Of(FaultBoundary.Cleanup)[0].Injected, Is.True);

            // Disarm and release explicitly: the retention was real ownership, not a lost reference.
            fixture.Publisher.Faults.Disarm(FaultBoundary.Cleanup);
            fixture.Publisher.Faults.FailDuringMigration = false;

            AcquisitionCleanup cleanup = plan.Acquisitions.ReleaseAll();

            Assert.That(cleanup.Released.Count, Is.EqualTo(2));
            Assert.That(cleanup.Failed, Is.Empty);
            Assert.That(cleanup.Quarantined, Is.Empty);
            Assert.That(cleanup.HasCleanupErrors, Is.False);
            Assert.That(plan.Acquisitions.RetainedLeaseIds(), Is.Empty, "the leases are released once cleanup runs");
            Assert.That(gate.ReleasedCount, Is.EqualTo(2));
            Assert.That(gate.LiveLeaseCount, Is.EqualTo(0));
        }

        /// <summary>
        /// The latch's own evidence (TEST-016 row 2's "failure includes operation ID and provenance"). One world
        /// reaches the boundaries in a known order: a successful publication reaches the five prewrite/postwrite
        /// boundaries without injection, an injected cleanup refusal adds a fourth reach, and an armed acquisition
        /// boundary reached as a *value* adds the refusal form. The trace is asserted in order, each record is
        /// checked against the operation and plan identity of the run that produced it, and with every boundary
        /// disarmed a further publication still records its reaches while the injected count does not move.
        /// </summary>
        [Test]
        public void TheLatchRecordsEveryReachInOrderWithProvenance()
        {
            Fixture fixture = CreateFixture();
            AssertLatchShared(fixture);
            AssemblyFaultInjection latch = fixture.World.Faults;
            latch.DisarmAll();
            latch.Trace.Clear();

            // Run A: a successful publication reaches validation, acquisition, fence, migration, first-live-write
            // and gate installation, in that order, with nothing injected.
            StagedResourceGate gateA = NewStagedGate(fixture);
            InertAcquisitionSet setA = AdoptAndStage(fixture, 1UL, gateA, out AssemblyEpoch laneEpochA);
            StageOne(setA, 1UL, 64UL);
            PlannedPublication planA = MountPlan(
                fixture,
                1UL,
                priority: 11,
                liveSlots: FaultFixturePlans.LiveSlots(7, 9, 1U),
                acquisitions: setA);
            AssemblyPublicationReport reportA = fixture.Publisher.Publish(planA);
            Assert.That(reportA.Published, Is.True, reportA.ToString());
            Assert.That(reportA.WorldEpochAfter, Is.EqualTo(laneEpochA), "the adopted publication is the one published");
            Assert.That(latch.Trace.Count, Is.EqualTo(6), "run A records its six publication reaches");
            Assert.That(
                latch.ReachCount,
                Is.EqualTo(7),
                "run A's six publication reaches plus the staged gate's acquisition reach: `TryReach` and"
                + " `TryRefuse` both count a reach, but `TryRefuse` records a trace entry only for the armed"
                + " (injected) form, because an unarmed value refusal has no injected fault to give provenance to");

            // Run B: an injected cleanup refusal reaches validation, acquisition and fence normally, then the
            // cleanup boundary with the injection. The migration boundary is NOT reached, because the original
            // prewrite switch throws before it: the two paths are distinct.
            StagedResourceGate gateB = NewStagedGate(fixture);
            InertAcquisitionSet setB = AdoptAndStage(fixture, 2UL, gateB, out AssemblyEpoch laneEpochB);
            StageOne(setB, 1UL, 64UL);
            PlannedPublication planB = MountPlan(
                fixture,
                2UL,
                priority: 12,
                liveSlots: FaultFixturePlans.LiveSlots(17, 19, 2U),
                acquisitions: setB);
            latch.Arm(FaultBoundary.Cleanup);
            latch.FailDuringMigration = true;
            AssemblyPublicationReport reportB = fixture.Publisher.Publish(planB);
            Assert.That(reportB.Outcome, Is.EqualTo(Outcome.Rejected), reportB.ToString());
            Assert.That(latch.Trace.Count, Is.EqualTo(10), "run B adds four reaches, not five: the prewrite switch"
                + " throws before the migration boundary, so the two paths are visibly distinct");
            Assert.That(latch.ReachCount, Is.EqualTo(12), "run B's four publication reaches and one staged value refusal"
                + " on top of run A's seven");
            latch.DisarmAll();
            latch.FailDuringMigration = false;

            // Run C: the same acquisition boundary reached as a refused *value* by a staged gate.
            var gateC = new StagedResourceGate(1024UL, FaultFixtureKeys.Issuer, latch);
            var setC = new InertAcquisitionSet(gateC, FaultFixtureKeys.Operation(fixture.World.World, 3UL));
            latch.Arm(FaultBoundary.Acquisition);
            Assert.That(
                setC.TryAcquire(FaultFixtureKeys.Resource(5UL), 64UL, null, out DiagnosticCode refused),
                Is.False);
            Assert.That(refused, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(latch.Trace.Count, Is.EqualTo(11), "run C adds the value-refusal record");
            Assert.That(latch.ReachCount, Is.EqualTo(13), "run C's one armed refusal on top of run B's twelve");
            latch.DisarmAll();

            // The exact ordered trace. Run A's six publication reaches, run B's four (the migration boundary is
            // absent because the original prewrite switch throws before it) and run C's value refusal.
            var expected = new List<string>
            {
                "validation",
                "acquisition",
                "fence",
                "migration",
                "first-live-write",
                "gate-installation",
                "validation",
                "acquisition",
                "fence",
                "cleanup",
                "acquisition",
            };

            IReadOnlyList<FaultRecord> records = latch.Trace.Records;
            var failed = new List<string>();
            if (records.Count != expected.Count)
            {
                failed.Add("trace holds " + records.Count.ToString() + " records, expected "
                    + expected.Count.ToString());
            }

            for (int i = 0; i < records.Count && i < expected.Count; i++)
            {
                if (!FaultBoundaryText.Of(records[i].Boundary).Equals(expected[i]))
                {
                    failed.Add("record " + i.ToString() + " is "
                        + FaultBoundaryText.Of(records[i].Boundary) + ", expected " + expected[i]);
                }

                if (records[i].Ordinal != i)
                {
                    failed.Add("record " + i.ToString() + " carries ordinal "
                        + records[i].Ordinal.ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed observations: " + string.Join(" | ", failed.ToArray()));

            // Provenance: every record of a publication names that run's operation and plan hash.
            string opA = planA.Plan.Operation.ToString();
            string hashA = planA.Plan.PlanHash.ToHex();
            string opB = planB.Plan.Operation.ToString();
            string hashB = planB.Plan.PlanHash.ToHex();
            var provenance = new List<string>();
            for (int i = 0; i < 6; i++)
            {
                string line = records[i].ToLine();
                if (!line.Contains(opA) || !line.Contains(hashA))
                {
                    provenance.Add("record " + i.ToString() + " lacks run A provenance: " + line);
                }
            }

            for (int i = 6; i < 10; i++)
            {
                string line = records[i].ToLine();
                if (!line.Contains(opB) || !line.Contains(hashB))
                {
                    provenance.Add("record " + i.ToString() + " lacks run B provenance: " + line);
                }
            }

            Assert.That(
                hashA,
                Is.Not.EqualTo(hashB),
                "two different plans carry two different hashes, so the provenance field is really per plan and not"
                + " one constant recorded for both runs");
            Assert.That(records[0].ToLine(), Does.Contain(FaultBoundaryText.Of(FaultBoundary.Validation)));
            Assert.That(records[9].ToLine(), Does.Contain(FaultBoundaryText.Of(FaultBoundary.Cleanup)));
            Assert.That(records[10].ToLine(), Does.Contain("refused as a value"));
            Assert.That(
                provenance,
                Is.Empty,
                "failed observations: " + string.Join(" | ", provenance.ToArray()));

            Assert.That(latch.InjectedCount, Is.EqualTo(2), "the cleanup refusal and the value refusal");
            Assert.That(latch.Trace.InjectedCount, Is.EqualTo(2));
            var observedNames = new List<string>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                observedNames.Add(FaultBoundaryText.Of(records[i].Boundary));
            }

            Assert.That(
                string.Join(",", observedNames.ToArray()),
                Is.EqualTo(
                    "validation,acquisition,fence,migration,first-live-write,gate-installation,"
                    + "validation,acquisition,fence,cleanup,acquisition"),
                "the trace's ordered boundary names: run A's six reaches, run B's four and run C's value refusal");

            // With everything disarmed, a further publication still records its reaches and injects nothing.
            int reachesBefore = latch.ReachCount;
            int injectedBefore = latch.InjectedCount;
            int recordedBefore = latch.Trace.Count;
            Assert.That(latch.ArmedBoundaryCount, Is.EqualTo(0));

            StagedResourceGate gateD = NewStagedGate(fixture);
            InertAcquisitionSet setD = new InertAcquisitionSet(
                gateD,
                FaultFixtureKeys.Operation(fixture.World.World, 4UL));
            StageOne(setD, 1UL, 64UL);
            AssemblyPublicationReport reportD = fixture.Publisher.Publish(
                MountPlan(fixture, 3UL, priority: 13, liveSlots: FaultFixturePlans.LiveSlots(17, 19, 2U), acquisitions: setD));

            Assert.That(reportD.Published, Is.True, reportD.ToString());
            Assert.That(reportD.WorldEpochAfter, Is.EqualTo(laneEpochB), "the pending composition publication is used");
            Assert.That(
                latch.ReachCount,
                Is.EqualTo(reachesBefore + 7),
                "run D reaches the six publication boundaries plus its staged gate's acquisition reach, and every"
                + " one of them is reached with the latch disarmed");
            Assert.That(latch.InjectedCount, Is.EqualTo(injectedBefore), "a disarmed boundary injects nothing");
            Assert.That(latch.Trace.Count, Is.EqualTo(recordedBefore + 6));
            Assert.That(latch.Trace.InjectedCount, Is.EqualTo(2));
            Assert.That(latch.Describe(), Does.Contain("armed=0"));

            for (int i = recordedBefore; i < latch.Trace.Count; i++)
            {
                Assert.That(latch.Trace.Records[i].Injected, Is.False, "no boundary is armed any more");
            }

            Assert.That(latch.Trace.Of(FaultBoundary.GateInstallation).Count, Is.EqualTo(2));
            Assert.That(latch.Trace.Of(FaultBoundary.FirstLiveWrite).Count, Is.EqualTo(2));
        }
    }
}
