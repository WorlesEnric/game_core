// GameCore.Narrative.Tests — GC-015 narrative half: state retention, migration and explicit reset in a real world.
//
// The scenario builds the narrative world exactly as the GC-010 slice does (GC-004's control lane over the declared
// chapter tree, GC-006's derivation, GC-007's ownership validator and slot policies, GC-009's compiled schedule,
// GC-008's planner and publisher into a real `Unity.Entities.World`) and then drives GC-015's state policies
// through it:
//
//   * non-default runtime counters survive a tuning mount (P-020) and a subtree reparent (P-025);
//   * every declared slot policy is executed over real live rows: `Preserve`, `PreserveDormant`, `RemoveDerived`,
//     a registered `Migrate`, an explicit `Reset` and a `TransferTo` (P-032, P-033);
//   * a pass whose migration cannot run fails before the first live write: the world keeps its old assembly
//     revision, its old epoch and its live values (P-029);
//   * the pass's bounded temporary storage is observable and a pass beyond the configured budget is refused (P-022);
//   * the ownership transfer moves the value to the named available owner's key and retires the source row.
//
// Every expectation is read from the publisher's own report or from live ECS storage.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using GameCore.Planning.StatePolicies;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.StateMigration;
using Unity.Entities;
using NarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.Narrative.Tests
{
    /// <summary>One named GC-015 observation of the narrative world.</summary>
    public sealed class NarrativeStatePolicyStep
    {
        public NarrativeStatePolicyStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => (Passed ? "PASS " : "FAIL ") + Name + ": " + Detail;
    }

    /// <summary>Runs the GC-015 narrative observations against real modules only.</summary>
    public static class NarrativeStatePolicyScenario
    {
        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>Conversation node the seed opens at; no default would ever produce it (P-032).</summary>
        private const int SeedNodeOrdinal = 4;

        /// <summary>A trail counter written by committed steps, so accidental reconstruction is visible.</summary>
        private const int SeedTrailSteps = 7;

        public static IReadOnlyList<NarrativeStatePolicyStep> Run()
            => new Executor().Run();

        private sealed class Executor
        {
            private readonly List<NarrativeStatePolicyStep> steps = new List<NarrativeStatePolicyStep>();
            private readonly IdSequence sessionSequence = new IdSequence(0x47433031354E4152UL);
            private readonly GameCore.Gameplay.Narrative.Fixtures.NarrativeRecipeApplier applier =
                new GameCore.Gameplay.Narrative.Fixtures.NarrativeRecipeApplier();

            private ImmutableCatalog catalog = null!;
            private IReadOnlyList<CatalogPluginDeclaration> declarations = null!;
            private PipelineDescriptorReport descriptorReport = null!;
            private StatePolicyCatalog policyCatalog = null!;

            private UnityWorldHost? host;
            private GameCore.Gameplay.Narrative.Fixtures.NarrativeModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private StateMigrationPipeline? policies;
            private ulong operationSequence;

            public IReadOnlyList<NarrativeStatePolicyStep> Run()
            {
                BuildPolicySurface();
                CreateWorld();
                NonDefaultCountersSurviveTuning();
                NonDefaultCountersSurviveReparent();
                DeclaredSurface();
                PreserveDormantInTheWorld();
                RemoveDerivedInTheWorld();
                RegisteredMigrationInTheWorld();
                DeclaredResetInTheWorld();
                OwnershipTransferInTheWorld();
                FailedMigrationKeepsOldAssembly();
                MigrationBounds();
                return steps;
            }

            // ------------------------------------------------------------------ 1. the declared policy surface

            private void BuildPolicySurface()
            {
                const string name = "gc015-narrative-policy-surface";
                try
                {
                    CatalogBuildResult build = GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.Build();
                    catalog = build.Catalog!;
                    declarations = new List<CatalogPluginDeclaration>
                    {
                        ChapterDeclaration(1UL, true),
                        ChapterDeclaration(2UL, false),
                        new CatalogPluginDeclaration(
                            GameCore.Gameplay.Narrative.Fixtures.NarrativeDeclarations.ForwardProvider(
                                NarrativeKeys.PluginTypeId(4UL),
                                GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.PluginFactoryKey,
                                GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.RecordSchema),
                            ConfigDocument.Empty),
                    };

                    var kinds = new ScheduleDispatchKindTable()
                        .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

                    descriptorReport = OwnershipSchedulePipeline.Build(
                        Manifests(), kinds, new GameCore.Gameplay.Narrative.Fixtures.NarrativeSlotMigrations());

                    policyCatalog = StatePolicyCatalog.Build(MigrationHandlers(), NarrativeInitialValues());

                    bool descriptorBuilt = descriptorReport.Descriptor != null && descriptorReport.Adaptation != null;

                    // P-033: the conversation layout implements two slots and the ledger layout five, so the world
                    // really does store several slots in one physical component.
                    bool sharedConversation = policyCatalog.Succeeded
                        && policyCatalog.Layouts!.TryGet(NarrativeKeys.ConversationNodeSlot, out GeneratedSlotLayout? conversation)
                        && conversation != null
                        && conversation.ImplementsSeveralSlots
                        && conversation.Slots.Count == 2;
                    bool sharedTrail = policyCatalog.Succeeded
                        && policyCatalog.Layouts!.TryGet(NarrativeKeys.TrailStepsSlot, out GeneratedSlotLayout? trail)
                        && trail != null
                        && trail.Slots.Count == 5;
                    bool declarationsValidated = policyCatalog.Succeeded
                        && policyCatalog.SlotPolicyResults.Count == policyCatalog.Policies!.Count;

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        descriptorBuilt && policyCatalog.Succeeded && sharedConversation && sharedTrail && declarationsValidated,
                        "descriptor=" + (descriptorBuilt ? "built" : descriptorReport.Describe())
                        + "; policies=" + policyCatalog.Describe()
                        + "; sharedConversationComponent=" + sharedConversation
                        + "; sharedTrailComponent=" + sharedTrail
                        + "; validatedDeclarations=" + declarationsValidated));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private static CatalogPluginDeclaration ChapterDeclaration(ulong ordinal, bool first)
            {
                PluginManifest manifest = first
                    ? GameCore.Gameplay.Narrative.Fixtures.NarrativeDeclarations.ChapterProvider(
                        NarrativeKeys.PluginTypeId(ordinal),
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.PluginFactoryKey,
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.RecordSchema)
                    : GameCore.Gameplay.Narrative.Fixtures.NarrativeDeclarations.ChapterTwoProvider(
                        NarrativeKeys.PluginTypeId(ordinal),
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.PluginFactoryKey,
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.RecordSchema);

                return new CatalogPluginDeclaration(manifest, ConfigDocument.Empty);
            }

            private IReadOnlyList<PluginManifest> Manifests()
            {
                var manifests = new List<PluginManifest>(declarations.Count);
                for (int i = 0; i < declarations.Count; i++)
                {
                    manifests.Add(declarations[i].Manifest);
                }

                return manifests;
            }

            private static IReadOnlyList<ISlotMigration> MigrationHandlers()
                => new List<ISlotMigration>
                {
                    new GameCore.Gameplay.Narrative.Fixtures.NarrativeConversationNodeMigration(),
                    new GameCore.Gameplay.Narrative.Fixtures.NarrativeConversationStatusMigration(),
                };

            /// <summary>
            /// The declared initialization values, registered by the very keys the manifests declare, so a permitted
            /// reset writes a declared value rather than a zero (P-032).
            /// </summary>
            private static IInitializationPolicyRegistry NarrativeInitialValues()
            {
                var registry = new InitializationPolicyRegistry();
                registry.Register(NarrativeKeys.ConversationInit, NarrativeKeys.ConversationDomain, NarrativeConversationStatus.Idle);
                registry.Register(NarrativeKeys.QuestInit, NarrativeKeys.QuestDomain, NarrativeFacts.InitialValue);
                registry.Register(NarrativeKeys.GateInit, NarrativeKeys.GateDomain, 0);
                registry.Register(NarrativeKeys.TrailInit, NarrativeKeys.TrailDomain, 0);
                registry.Register(NarrativeKeys.EncounterInit, NarrativeKeys.EncounterDomain, 0);
                return registry;
            }

            // ------------------------------------------------------------------ 2. the real world and its live state

            private void CreateWorld()
            {
                const string name = "gc015-narrative-world-and-live-state";
                try
                {
                    WorldId world = NextSession();
                    WorldCreateRequest request = GameCore.Gameplay.Narrative.Fixtures.NarrativeRegistration.CommandDrivenRequest(
                        world, NextOperation(world), ContentHash.Empty);
                    UnityWorldRegistration registration = GameCore.Gameplay.Narrative.Fixtures.NarrativeRegistration.Create(
                        descriptorReport.Adaptation!, GameCore.Gameplay.Narrative.Fixtures.NarrativeRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        steps.Add(new NarrativeStatePolicyStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = GameCore.Gameplay.Narrative.Fixtures.NarrativeModule.Attach(
                        host, descriptorReport.Compilation!.Schedule!);

                    registry = new TargetRegistry(world, 16);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeRecipes.Catalog(applier),
                        new MigrationRegistry(MigrationHandlers()),
                        descriptorReport.Descriptor!);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    SeedTarget(NarrativeKeys.Mara, NarrativeKeys.VillageScope, NarrativeKeys.VillagerRecipe);
                    SeedTarget(NarrativeKeys.GateEast, NarrativeKeys.VillageScope, NarrativeKeys.QuestGateRecipe);
                    SeedTarget(NarrativeKeys.QuestLedger, NarrativeKeys.RootScope, NarrativeKeys.QuestLedgerRecipe);
                    SeedTarget(NarrativeKeys.Sailor, NarrativeKeys.HarborScope, NarrativeKeys.VillagerRecipe);

                    lane = CompositionHost.CreateDefault(
                        world,
                        NarrativeKeys.RootScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly.WithScopes(
                            GameCore.Gameplay.Narrative.Fixtures.NarrativeScopes.DeclaredChildren()));

                    pipeline = new DerivedAssemblyPipeline(
                        host, lane, publisher, targets, seeder, NarrativeValues(), null, null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer),
                        DefaultBudget());

                    policies = new StateMigrationPipeline(host, publisher, seeder, policyCatalog, DefaultBudget());

                    // The control plane is enough for this suite: a command-driven world with no admitted step needs
                    // no host pump, and the GC-009 time driver belongs to the slice's own scenario.
                    // Real non-default runtime state: a conversation that has progressed, trail counters written by
                    // committed steps and a gate decision already taken. A mis-implemented preserve loses these.
                    bool seeded = SeedSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 2U, SeedNodeOrdinal)
                        && SeedSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot, 2U, NarrativeConversationStatus.Closed)
                        && SeedSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, 1U, SeedTrailSteps)
                        && SeedSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailProjectedSlot, 1U, 5)
                        && SeedSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailFactsSlot, 1U, 3)
                        && SeedSlot(NarrativeKeys.GateEast, NarrativeKeys.GateOwner, NarrativeKeys.GateDecisionSlot, 1U, 1);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        seeded
                        && lane.Committed.Scopes.Count == GameCore.Gameplay.Narrative.Fixtures.NarrativeScopes.DeclaredScopeCount
                        && host.Lifecycle == WorldLifecycleState.Running
                        && AssemblyPublisher.MatchesPublishedAssembly(
                            lane.Committed.Revision, lane.Committed.Epoch, publisher.PublishedRevision, host.CurrentEpoch),
                        "session=" + world.Session.ToString()
                        + "; targets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; mappedTargets=" + module.MappedTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; seededState=" + seeded
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private void SeedTarget(TargetId target, ScopeId scope, DefinitionRef recipe)
            {
                if (!seeder!.TrySeed(target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    throw new InvalidOperationException("seeding " + target.ToString() + " failed: " + code + ": " + detail);
                }

                seeder.TryGetEntity(target, out Entity entity);
                module!.MapTarget(target, entity);
            }

            private bool SeedSlot(TargetId target, OwnerId owner, SlotId slot, uint version, int value)
                => seeder!.TrySeedSlot(target, owner, slot, version, value, out DiagnosticCode _, out string _);

            private static GameCore.Derivation.IDerivationValueSource NarrativeValues()
                => new GameCore.Derivation.Fixtures.FixtureValueSource()
                    .RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

            // ------------------------------------------------------------------ 3. tuning and reparenting

            private void NonDefaultCountersSurviveTuning()
            {
                const string name = "gc015-narrative-tuning-keeps-non-default-counters";
                try
                {
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeMounts.Mount(
                            GameCore.Gameplay.Narrative.Fixtures.NarrativeDeclarations.ChapterProvider(
                                NarrativeKeys.PluginTypeId(1UL),
                                GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.PluginFactoryKey,
                                GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.RecordSchema),
                            NarrativeKeys.ChapterOneInstall,
                            NarrativeKeys.ChapterOneScope,
                            null),
                        null,
                        null,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? derived);
                    bool admitted = publication != null;

                    bool nodeKept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int node, out uint nodeVersion);
                    bool statusKept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot, out int status, out uint _);
                    bool trailKept = ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, out int trail, out uint _);
                    bool rowsPublished = module!.MappedTargetCount == 4;

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        admitted
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.PreservedCount == plan.Decisions.Count
                        && nodeKept && node == SeedNodeOrdinal && nodeVersion == 2U
                        && statusKept && status == NarrativeConversationStatus.Closed
                        && trailKept && trail == SeedTrailSteps
                        && rowsPublished,
                        "admitted=" + admitted
                        + "; publication=" + Describe(publication)
                        + "; policies=" + (plan != null ? plan.ToString() : "<none>")
                        + "; node=" + Value(nodeKept, node, nodeVersion)
                        + "; status=" + Value(statusKept, status, 0U)
                        + "; trailSteps=" + Value(trailKept, trail, 0U)
                        + "; mutatedTargets=" + module.MappedTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; derived=" + (derived != null ? derived.Describe() : "<none>")));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// Reparenting `harbor` from `chapter-two` under `chapter-one`: a real subtree move whose new ancestry
            /// changes inheritance, while the moved target's and every unrelated target's state survives (P-025).
            /// </summary>
            private void NonDefaultCountersSurviveReparent()
            {
                const string name = "gc015-narrative-reparent-keeps-unrelated-state";
                try
                {
                    // Chapter two must be mounted first, so the move really changes what harbor inherits.
                    bool chapterTwoReady = PublishSetupEdit(ChapterTwoMount());

                    // Two directions of the same subtree move, so the observation is about state, not about one
                    // lucky ordering: harbor joins chapter-one and then returns to chapter-two (P-025).
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        ScopeReparent(NarrativeKeys.HarborScope, NarrativeKeys.ChapterOneScope),
                        null,
                        null,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);
                    bool admitted = publication != null;

                    bool nodeKept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int node, out uint _);
                    bool trailKept = ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, out int trail, out uint _);
                    bool reparented = lane!.Committed.Scopes.TryGet(NarrativeKeys.HarborScope, out ScopeRecord? harbor)
                        && harbor != null
                        && harbor.Parent.Equals(NarrativeKeys.ChapterOneScope);
                    AssemblyPublicationReport? back = PublishPolicyEdit(
                        ScopeReparent(NarrativeKeys.HarborScope, NarrativeKeys.ChapterTwoScope),
                        null,
                        null,
                        out StatePolicyPlan? backPlan,
                        out DerivedAssemblyReport? _);
                    bool returned = back != null
                        && lane!.Committed.Scopes.TryGet(NarrativeKeys.HarborScope, out ScopeRecord? home)
                        && home != null
                        && home.Parent.Equals(NarrativeKeys.ChapterTwoScope);
                    bool nodeStillKept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int nodeAgain, out uint _);
                    bool trailStillKept = ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, out int trailAgain, out uint _);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        chapterTwoReady && admitted && reparented && returned
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded
                        && nodeKept && node == SeedNodeOrdinal
                        && trailKept && trail == SeedTrailSteps
                        && back != null && back.Published
                        && backPlan != null && backPlan.Succeeded
                        && nodeStillKept && nodeAgain == SeedNodeOrdinal
                        && trailStillKept && trailAgain == SeedTrailSteps,
                        "chapterTwoReady=" + chapterTwoReady
                        + "; admitted=" + admitted
                        + "; harborParent=" + (reparented ? NarrativeKeys.ChapterOneScope.ToString() : "<unchanged>")
                        + "; publication=" + Describe(publication)
                        + "; returnedToChapterTwo=" + returned
                        + "; node=" + Value(nodeKept, node, 0U)
                        + "; trailSteps=" + Value(trailKept, trail, 0U)
                        + "; nodeAfterReturn=" + Value(nodeStillKept, nodeAgain, 0U)
                        + "; trailStepsAfterReturn=" + Value(trailStillKept, trailAgain, 0U)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 4. the declared surface, read live

            private void DeclaredSurface()
            {
                const string name = "gc015-narrative-declared-surface";
                try
                {
                    bool factHasTransferPolicy = policyCatalog.Policies!.TryFind(
                            NarrativeKeys.BridgePermitValueSlot, out SlotStatePolicy? fact, out DiagnosticCode _, out string _)
                        && fact != null
                        && fact.HasTransferPolicy
                        && fact.LastSupport == LastSupportPolicy.PreserveDormant;
                    bool durableKept = policies!.HasActiveWriter(
                        new StateSlotKey(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot));

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        factHasTransferPolicy && durableKept,
                        "factSlotHasTransferPolicy=" + factHasTransferPolicy
                        + "; conversationHasActiveWriter=" + durableKept
                        + "; layouts=" + policyCatalog.Layouts!.ToString()
                        + "; dormant=" + policies.Dormant.ToString()));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 5. each slot policy in the world

            private void PreserveDormantInTheWorld()
            {
                const string name = "gc015-narrative-preserve-dormant";
                try
                {
                    var gate = new StateSlotKey(NarrativeKeys.GateEast, NarrativeKeys.GateOwner, NarrativeKeys.GateDecisionSlot);
                    var requests = new List<StatePolicyRequest> { StatePolicyRequest.PreserveDormant(gate) };

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(), requests, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool recorded = policies!.Dormant.TryGet(gate, out DormantSlotRecord record);
                    bool rowKept = ReadSlot(NarrativeKeys.GateEast, NarrativeKeys.GateOwner, NarrativeKeys.GateDecisionSlot, out int value, out uint _);
                    bool hasWriter = policies.HasActiveWriter(gate);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.RetainedDormantCount == 1
                        && recorded && record.RetainedValue == 1
                        && rowKept && value == 1
                        && !hasWriter,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; dormant=" + (recorded ? record.ToString() : "<none>")
                        + "; liveValue=" + Value(rowKept, value, 0U)
                        + "; hasActiveWriter=" + hasWriter));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private void RemoveDerivedInTheWorld()
            {
                const string name = "gc015-narrative-remove-derived";
                try
                {
                    var projected = new StateSlotKey(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailProjectedSlot);
                    var requests = new List<StatePolicyRequest> { StatePolicyRequest.RemoveDerived(projected) };

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(), requests, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool removed = !ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailProjectedSlot, out int _, out uint _);
                    bool siblingOfSameComponentKept = ReadSlot(
                        NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot, out int steps, out uint _)
                        && steps == SeedTrailSteps;

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.RemovedDerivedCount == 1
                        && removed && siblingOfSameComponentKept,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; removed=" + removed
                        + "; siblingSlotOfTheSameComponentKept=" + siblingOfSameComponentKept));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The conversation node is seeded one version behind, so the pass must run the registered migration on
            /// its copy and publish the migrated value with its new version (P-029, P-032).
            /// </summary>
            private void RegisteredMigrationInTheWorld()
            {
                const string name = "gc015-narrative-registered-migration";
                try
                {
                    // Version-1 state at the pre-chapter node: the registered migration must rewrite the *copy*
                    // into the chapter's opening node rather than merely relabelling the version.
                    bool seeded = SeedSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 1U, 0);

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(), null, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool read = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int value, out uint version);
                    bool migratedOnTheCopy = NarrativeDialogueRules.TryUpgradeNodeToVersionTwo(0, out int expected);
                    bool transformed = expected != 0;
                    bool outcome = read && migratedOnTheCopy && transformed && value == expected && version == 2U;

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        seeded
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.MigratedCount == 1
                        && outcome,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; value=" + Value(read, value, version)
                        + "; expected=" + (migratedOnTheCopy ? expected.ToString(CultureInfo.InvariantCulture) : "<refused>")
                        + "; transformed=" + transformed
                        + "; highWaterBytes=" + (plan != null ? plan.ScratchHighWaterBytes.ToString(CultureInfo.InvariantCulture) : "<none>")));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// A reset is legal only for a schema/policy that explicitly permits it (P-032). The narrative catalog
            /// permits none, so the world refuses an undeclared reset and keeps the value; the same production
            /// executor then performs a declared reset — a manifest-supported reason plus the declared
            /// initialization policy — as a real publication over the same live row.
            /// </summary>
            private void DeclaredResetInTheWorld()
            {
                const string name = "gc015-narrative-explicit-reset";
                try
                {
                    var node = new StateSlotKey(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot);
                    bool beforeRead = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int before, out uint _);

                    StatePolicyPlan undeclared = StatePolicyExecutor.Execute(
                        policyCatalog.Policies!,
                        seeder!.ReadLiveSlots(TargetIds()),
                        new List<StatePolicyRequest> { StatePolicyRequest.Reset(node, "content repair") },
                        policyCatalog.Migrations,
                        new DeclaredSlotMigrationRegistry(policyCatalog.Policies!),
                        policyCatalog.InitialValues,
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot));

                    bool keptAfterRefusal = ReadSlot(
                        NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int afterRefusal, out uint _);

                    // The declared reset: the same slot, a manifest-supported reason and the declared
                    // initialization policy, executed by the same executor and applied by the same publisher.
                    SlotStatePolicySet declaredSet = SetWith(Resettable(ResetReason));
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(),
                        new List<StatePolicyRequest> { StatePolicyRequest.Reset(node, ResetReason) },
                        declaredSet,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);

                    bool afterRead = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int after, out uint _);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        beforeRead
                        && !undeclared.Succeeded
                        && undeclared.Code == DiagnosticCode.OwnershipConflict
                        && undeclared.Detail.Contains("does not declare a permitted reset")
                        && keptAfterRefusal && afterRefusal == SeedNodeOrdinal
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.ResetCount == 1
                        && plan.Decisions[0].Reason == ResetReason
                        && afterRead && after == NarrativeConversationStatus.Idle,
                        "undeclaredReset=" + undeclared.Code.ToString() + ": " + undeclared.Detail
                        + "; valueAfterRefusal=" + Value(keptAfterRefusal, afterRefusal, 0U)
                        + "; publication=" + Describe(publication)
                        + "; declaredReset=" + (plan != null ? plan.ToString() : "<none>")
                        + "; valueAfterReset=" + Value(afterRead, after, 0U)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The ownership transfer: the state moves to the named available owner's key and the source row is
            /// retired in the same publication (P-025, P-032).
            /// </summary>
            private void OwnershipTransferInTheWorld()
            {
                const string name = "gc015-narrative-ownership-transfer";
                try
                {
                    bool seeded = SeedSlot(NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner, NarrativeKeys.BridgePermitValueSlot, 1U, 12);
                    var source = new StateSlotKey(NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner, NarrativeKeys.BridgePermitValueSlot);

                    SlotStatePolicySet transferSet = SetWith(Transferable());
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(),
                        new List<StatePolicyRequest>
                        {
                            StatePolicyRequest.LastSupportTransfer(source, NarrativeKeys.GateOwner, NarrativeKeys.QuestLedger),
                        },
                        transferSet,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);

                    bool sourceRetired = !ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner, NarrativeKeys.BridgePermitValueSlot, out int _, out uint _);
                    bool destinationHolds = ReadSlot(NarrativeKeys.QuestLedger, NarrativeKeys.GateOwner, NarrativeKeys.BridgePermitValueSlot, out int moved, out uint movedVersion);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        seeded
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.TransferredCount == 1
                        && plan.Dispositions[0].DestinationOwner.Equals(NarrativeKeys.GateOwner)
                        && plan.Decisions[0].MovesToAnotherOwner
                        && plan.Decisions[0].DeclaredLastSupportTransfer
                        && sourceRetired
                        && destinationHolds && moved == 12 && movedVersion == 1U
                        && policies!.Transfers.Count == 1,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; sourceRetired=" + sourceRetired
                        + "; destination=" + Value(destinationHolds, moved, movedVersion)
                        + "; observedTransfers=" + policies!.Transfers.Count.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 6. prewrite failure and bounds

            /// <summary>
            /// A migration that cannot run fails before the first live write: the policy pass stages nothing, the
            /// planner rejects the plan, the publisher publishes no epoch and the live value stays exactly as it was
            /// (P-028, P-029, P-032).
            /// </summary>
            private void FailedMigrationKeepsOldAssembly()
            {
                const string name = "gc015-narrative-failed-migration-keeps-old-assembly";
                try
                {
                    // The live slot claims a version the registered migration does not accept.
                    bool seeded = SeedSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 3U, 9);

                    AssemblyEpoch epochBefore = host!.CurrentEpoch;
                    CompositionRevision revisionBefore = publisher!.PublishedRevision;

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(), null, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool kept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int value, out uint version);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        seeded
                        && plan != null && !plan.Succeeded
                        && plan.Code == DiagnosticCode.UnsupportedVersion
                        && plan.Dispositions.Count == 0
                        && publication != null && !publication.Published && publication.Outcome == Outcome.Rejected
                        && host.CurrentEpoch.Equals(epochBefore)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && kept && value == 9 && version == 3U,
                        "plan=" + (plan != null ? (plan.Succeeded ? "succeeded" : plan.Code + ": " + plan.Detail) : "<none>")
                        + "; publication=" + Describe(publication)
                        + "; epoch=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; live=" + Value(kept, value, version)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// Temporary storage is a hard, configured limit: a pass that cannot reserve its scratch is refused with
            /// `BudgetExceeded`, carries no disposition at all, and changes neither the live value nor the epoch
            /// (P-022, P-029).
            /// </summary>
            private void MigrationBounds()
            {
                const string name = "gc015-narrative-migration-bounds";
                try
                {
                    bool seeded = SeedSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, 1U, SeedNodeOrdinal);
                    AssemblyEpoch epochBefore = host!.CurrentEpoch;
                    var tinyBudget = new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, 32UL, 64UL);

                    // The measured pass first: it reports the temporary storage it needed (P-022).
                    StatePolicyPlan measured = StatePolicyExecutor.Execute(
                        policyCatalog.Policies!,
                        seeder!.ReadLiveSlots(TargetIds()),
                        null,
                        policyCatalog.Migrations,
                        new DeclaredSlotMigrationRegistry(policyCatalog.Policies!),
                        policyCatalog.InitialValues,
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot));

                    var tinyPolicies = new StateMigrationPipeline(host, publisher!, seeder!, policyCatalog, tinyBudget);
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextChapterTwoEdit(), null, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _, tinyPolicies);
                    bool kept = ReadSlot(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot, out int value, out uint version);

                    steps.Add(new NarrativeStatePolicyStep(
                        name,
                        seeded
                        && measured.ScratchHighWaterBytes > 0UL
                        && plan != null && !plan.Succeeded
                        && plan.Code == DiagnosticCode.BudgetExceeded
                        && plan.Dispositions.Count == 0
                        && host.CurrentEpoch.Equals(epochBefore)
                        && kept && value == SeedNodeOrdinal && version == 1U,
                        "measuredHighWaterBytes=" + measured.ScratchHighWaterBytes.ToString(CultureInfo.InvariantCulture)
                        + "; tinyBudgetPlan=" + (plan != null ? (plan.Succeeded ? "succeeded" : plan.Code.ToString() + ": " + plan.Detail) : "<none>")
                        + "; publication=" + Describe(publication)
                        + "; live=" + Value(kept, value, version)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ policy publication plumbing

            private static PlanBudget DefaultBudget()
                => new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot);

            /// <summary>
            /// Publishes one composition edit and its world assembly with no state-policy pass: the setup path this
            /// slice already uses (mount, scope edit), so both counters stay joined at every boundary (P-006).
            /// </summary>
            private bool PublishSetupEdit(CompositionEditPayload payload)
            {
                if (!PublishCompositionEdit(payload))
                {
                    return false;
                }

                OperationId operation = NextOperation(host!.World.Session);
                DerivedAssemblyReport derived = pipeline!.PublishDerived(operation);
                if (derived.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    return false;
                }

                if (derived.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher!.PublishUnchangedAssembly(
                        NextOperation(host.World.Session), lane!.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        return false;
                    }
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane!.Committed.Revision, lane.Committed.Epoch, publisher!.PublishedRevision, host.CurrentEpoch);
            }

            /// <summary>
            /// Publishes one composition edit whose state dispositions are GC-015's own policy pass (P-029, P-032):
            /// the edit is admitted and published, the derivation is GC-006's, and the plan the publisher applies
            /// carries the executor's dispositions instead of the planner's compatibility fallback.
            /// </summary>
            private AssemblyPublicationReport? PublishPolicyEdit(
                CompositionEditPayload payload,
                IReadOnlyList<StatePolicyRequest>? requests,
                SlotStatePolicySet? policyOverride,
                out StatePolicyPlan? policyPlan,
                out DerivedAssemblyReport? derived,
                StateMigrationPipeline? policyPipeline = null)
            {
                policyPlan = null;
                derived = null;
                if (pipeline == null || publisher == null || host == null)
                {
                    return null;
                }

                if (!PublishCompositionEdit(payload))
                {
                    return null;
                }

                derived = pipeline.Derive(NextOperation(host.World.Session));
                if (derived.Proposal == null || derived.Proposal.Proposal == null)
                {
                    return null;
                }

                policyPlan = (policyPipeline ?? policies!).Execute(TargetIds(), requests, policyOverride);
                if (!publisher.TryAdoptLanePublication(
                        lane!.Committed.Revision, lane.Committed.Epoch, out AssemblyEpoch _, out DiagnosticCode _))
                {
                    return null;
                }

                PlannedPublication plan = AssemblyPlanner.Build(
                    derived.Proposal.Proposal,
                    publisher.Descriptor,
                    publisher.PublishedRevision,
                    host.CurrentEpoch,
                    publisher.Published.Bindings,
                    publisher.Published.Rules,
                    targets!.PlannerTargets(),
                    seeder!.ReadLiveSlots(TargetIds()),
                    publisher.Migrations,
                    new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot),
                    new InertAcquisitionSet(new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer),
                        NextOperation(host.World.Session)),
                    DefaultBudget(),
                    policyPlan);

                return publisher.Publish(plan);
            }

            private bool PublishCompositionEdit(CompositionEditPayload payload)
            {
                EditAdmission admission = lane!.SubmitEdit(payload, NextOperation(lane.World.Session), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    return false;
                }

                PublishedOperation? published = lane.Publish(admission.Handle.Operation);
                return published != null && published.Outcome == Outcome.Published;
            }

            /// <summary>
            /// Alternates the chapter-two mount and unmount, so every GC-015 policy observation is published with a
            /// real composition publication whose derivation also changes a target (P-006, P-024).
            /// </summary>
            private CompositionEditPayload NextChapterTwoEdit()
            {
                // The toggle reads the lane's committed state rather than a local flag, so a mount that some other
                // step already performed can never be repeated (P-010).
                bool mounted = lane!.Committed.TryGetInstall(NarrativeKeys.ChapterTwoInstall, out InstallEntry? entry)
                    && entry != null;
                return mounted ? Unmount(NarrativeKeys.ChapterTwoInstall) : ChapterTwoMount();
            }

            /// <summary>O-03 mount of the sibling chapter provider (07 section 3.1).</summary>
            private static CompositionEditPayload ChapterTwoMount()
                => GameCore.Gameplay.Narrative.Fixtures.NarrativeMounts.Mount(
                    GameCore.Gameplay.Narrative.Fixtures.NarrativeDeclarations.ChapterTwoProvider(
                        NarrativeKeys.PluginTypeId(2UL),
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.PluginFactoryKey,
                        GameCore.Gameplay.Narrative.Fixtures.NarrativeScenarioCatalog.RecordSchema),
                    NarrativeKeys.ChapterTwoInstall,
                    NarrativeKeys.ChapterTwoScope,
                    null);

            private const string ResetReason = "content repair";

            /// <summary>O-07 unmount of one installation (P-046). The slice's own payloads carry the same shape.</summary>
            private static CompositionEditPayload Unmount(PluginInstanceId instance)
            {
                return new CompositionEditPayload(
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
            }

            /// <summary>O-02 subtree move: membership, contributions and bindings publish together (P-025).</summary>
            private static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId newParent)
            {
                return new CompositionEditPayload(
                    CompositionEditSubject.ScopeReparent,
                    scope,
                    newParent,
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
            }

            /// <summary>The catalog's declared policies with one slot's declaration replaced (never duplicated).</summary>
            private SlotStatePolicySet SetWith(SlotStatePolicy replacement)
            {
                var policies = new List<SlotStatePolicy>();
                for (int i = 0; i < policyCatalog.Policies!.Count; i++)
                {
                    SlotStatePolicy existing = policyCatalog.Policies.Policies[i];
                    if (!existing.SlotId.Equals(replacement.SlotId))
                    {
                        policies.Add(existing);
                    }
                }

                policies.Add(replacement);
                return new SlotStatePolicySet(policies);
            }

            /// <summary>The conversation node slot with the manifest-supported reset P-032 requires.</summary>
            private static SlotStatePolicy Resettable(string reason)
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        NarrativeKeys.ConversationNodeSlot,
                        NarrativeKeys.DialogueOwner,
                        NarrativeKeys.ConversationDomain,
                        NarrativeKeys.ConversationLayout,
                        null,
                        LastSupportPolicy.PreserveDormant,
                        default(FactoryKey),
                        new List<FactoryKey> { NarrativeKeys.ConversationNodeMigration },
                        SlotAuthorityOptions.Resettable(reason, true, false)),
                    NarrativeKeys.ConversationInit,
                    NarrativeKeys.ConversationConfigChange,
                    NarrativeKeys.ConversationNodeMigration);

            /// <summary>The bridge-permit fact slot with the declared `TransferTo` last-support policy (P-032).</summary>
            private static SlotStatePolicy Transferable()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        NarrativeKeys.BridgePermitValueSlot,
                        NarrativeKeys.QuestOwner,
                        NarrativeKeys.QuestDomain,
                        NarrativeKeys.QuestLayout,
                        null,
                        LastSupportPolicy.TransferTo,
                        NarrativeKeys.QuestTransfer,
                        null,
                        SlotAuthorityOptions.Durable()),
                    NarrativeKeys.QuestInit,
                    NarrativeKeys.QuestConfigChange,
                    default(FactoryKey));

            // ------------------------------------------------------------------ helpers

            private IReadOnlyList<TargetId> TargetIds()
            {
                IReadOnlyList<LiveTarget> live = targets!.Targets;
                var ids = new List<TargetId>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    ids.Add(live[i].Target);
                }

                return ids;
            }

            private bool ReadSlot(TargetId target, OwnerId owner, SlotId slot, out int value, out uint version)
            {
                value = 0;
                version = 0;
                IReadOnlyList<TargetSlotState> rows = publisher!.ReadSlotStates(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Owner.Equals(owner) && rows[i].Slot.Equals(slot))
                    {
                        value = rows[i].Value;
                        version = rows[i].SchemaVersion;
                        return true;
                    }
                }

                return false;
            }

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            private OperationId NextOperation(WorldId world) => NarrativeKeys.Operation(world, ++operationSequence);

            private static string Describe(AssemblyPublicationReport? publication)
                => publication == null
                    ? "<none>"
                    : publication.Outcome.ToString()
                        + (publication.Code == DiagnosticCode.None ? string.Empty : "(" + publication.Code.ToString() + ")")
                        + " " + publication.Detail;

            private static string Value(bool present, int value, uint version)
                => present
                    ? value.ToString(CultureInfo.InvariantCulture) + "@" + version.ToString(CultureInfo.InvariantCulture)
                    : "<missing>";

            private static string Describe(Exception exception)
                => exception.GetType().Name + ": " + exception.Message + Environment.NewLine + exception.StackTrace;
        }
    }
}
