// GameCore.Cards.Tests — GC-015 card half: state retention, migration and explicit reset in a real world.
//
// The scenario builds the card market exactly as the GC-011 slice does (GC-004's control lane over the market's
// scope tree, GC-006's derivation, GC-007's ownership validator and slot policies, GC-009's compiled settlement
// graph, GC-008's planner and publisher into a real `Unity.Entities.World`, and the card package's own table
// module) and then drives GC-015's state policies through it:
//
//   * the table's and its seats' non-default counters survive a tuning mount (a scoring provider publication) and
//     a scope reparent (P-020, P-025);
//   * every declared slot policy of the card catalog is executed over real live rows: `Preserve`,
//     `PreserveDormant` (the table, seat, draft and output domains), `RemoveDerived` (the step-scoped decision
//     draft), a registered `Migrate`, an explicit `Reset` and a `TransferTo` (P-032, P-033);
//   * a pass whose migration cannot run fails before the first live write, leaving the old assembly revision,
//     epoch and live values untouched (P-029);
//   * the pass's bounded temporary storage is observable and a pass beyond the configured budget is refused (P-022).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Cards;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using GameCore.Planning.StatePolicies;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.StateMigration;
using Unity.Entities;

namespace GameCore.Cards.Tests
{
    /// <summary>One named GC-015 observation of the card world.</summary>
    public sealed class CardStatePolicyStep
    {
        public CardStatePolicyStep(string name, bool passed, string detail)
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

    /// <summary>Runs the GC-015 card observations against real modules only.</summary>
    public static class CardStatePolicyScenario
    {
        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>A turn number no default initialization produces (P-032).</summary>
        private const int NonDefaultTurnNumber = 6;

        /// <summary>The seat score the fixture seeds; a tuning publication must not touch it (07 s2.4).</summary>
        private const int SeededSeatScore = CardTableKeys.SeededSeatScore;

        /// <summary>GC-015 test-declared policy keys, in the card package's own key namespace.</summary>
        private static readonly FactoryKey SeatInitPolicy = CardIdentity.Key("gc015.init.seat-state");

        private static readonly FactoryKey SeatMigrationKey = CardIdentity.Key("gc015.migrate.seat-state.v1-v2");

        private static readonly FactoryKey SeatTransferPolicy = CardIdentity.Key("gc015.transfer.seat-state");

        private static readonly FactoryKey AuditSlot = CardIdentity.Slot("gc015.slot.audit-cursor");

        private static readonly OwnerId AuditOwner = CardIdentity.Owner("gc015.owner.audit");

        /// <summary>The seat domain at version 2, which the GC-015 migration moves version-1 state into.</summary>
        private static readonly SchemaRef SeatDomainV2 =
            new SchemaRef(CardTableKeys.SeatDomain.Id, CardTableKeys.SeatDomain.Version + 1U);

        public static IReadOnlyList<CardStatePolicyStep> Run()
            => new Executor().Run();

        private sealed class Executor
        {
            private readonly List<CardStatePolicyStep> steps = new List<CardStatePolicyStep>();
            private readonly IdSequence sessionSequence = new IdSequence(0x4743303135434152UL);
            private readonly CardSeatApplier seatApplier = new CardSeatApplier();
            private readonly MarketTableApplier tableApplier = new MarketTableApplier();
            private readonly CardDerivationValueSource values = CardDerivationValueSource.Default();

            private ImmutableCatalog catalog = null!;
            private IReadOnlyList<CatalogPluginDeclaration> declarations = null!;
            private PipelineDescriptorReport descriptorReport = null!;
            private StatePolicyCatalog policyCatalog = null!;

            private UnityWorldHost? host;
            private CardTableModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private StateMigrationPipeline? policies;
            private ulong operationSequence;

            public IReadOnlyList<CardStatePolicyStep> Run()
            {
                BuildPolicySurface();
                CreateWorldAndMarket();
                TuningKeepsNonDefaultCounters();
                ReparentKeepsState();
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
                const string name = "gc015-cards-policy-surface";
                try
                {
                    CatalogBuildResult build = CardCatalogTable.Build();
                    catalog = build.Catalog!;
                    declarations = CardTableFixture.Declarations();

                    var kinds = new ScheduleDispatchKindTable()
                        .Add(CardTableKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                        .Add(CardTableKeys.ValidateSystem, SystemDispatchKind.ManagedSystem)
                        .Add(CardTableKeys.CommitSystem, SystemDispatchKind.ManagedSystem)
                        .Add(CardTableKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

                    descriptorReport = OwnershipSchedulePipeline.Build(
                        CardTableFixture.Manifests(), kinds, new SlotMigrationRegistry());

                    policyCatalog = StatePolicyCatalog.Build(
                        CardTableFixture.Manifests(),
                        new List<ISlotMigration> { new SeatStateMigration() },
                        InitialValues());

                    bool descriptorBuilt = descriptorReport.Descriptor != null && descriptorReport.Adaptation != null;
                    bool fiveSlots = policyCatalog.Succeeded && policyCatalog.Policies!.Count == 5;
                    bool fiveComponents = policyCatalog.Succeeded && policyCatalog.Layouts!.PhysicalComponentCount == 5;
                    bool tableFieldsMapped = policyCatalog.Succeeded
                        && policyCatalog.Layouts!.TryGet(CardTableKeys.TableSlot, out GeneratedSlotLayout? table)
                        && table != null
                        && table.Fields.Count == 3
                        && table.DeclaresField(CardTableKeys.TableDomain, CardTableKeys.TurnNumberField.RegistrationKey);

                    // The declared dispositions are the card slice's own (P-032): the durable domains retain dormant
                    // state, and the step-scoped decision draft is disposable derived data.
                    bool declaredPolicies = true;
                    declaredPolicies &= HasLastSupport(CardTableKeys.TableSlot, LastSupportPolicy.PreserveDormant);
                    declaredPolicies &= HasLastSupport(CardTableKeys.SeatSlot, LastSupportPolicy.PreserveDormant);
                    declaredPolicies &= HasLastSupport(CardTableKeys.CommandDraftSlot, LastSupportPolicy.PreserveDormant);
                    declaredPolicies &= HasLastSupport(CardTableKeys.DecisionDraftSlot, LastSupportPolicy.RemoveDerived);
                    declaredPolicies &= HasLastSupport(CardTableKeys.OutputSlot, LastSupportPolicy.PreserveDormant);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        descriptorBuilt && fiveSlots && fiveComponents && tableFieldsMapped && declaredPolicies,
                        "descriptor=" + (descriptorBuilt ? "built" : descriptorReport.Describe())
                        + "; policies=" + policyCatalog.Describe()
                        + "; fiveSlots=" + fiveSlots
                        + "; fivePhysicalComponents=" + fiveComponents
                        + "; tableFieldsMapped=" + tableFieldsMapped
                        + "; declaredLastSupportPolicies=" + declaredPolicies));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private bool HasLastSupport(SlotId slot, LastSupportPolicy expected)
                => policyCatalog.Policies!.TryFind(slot, out SlotStatePolicy? policy, out DiagnosticCode _, out string _)
                    && policy != null
                    && policy.LastSupport == expected;

            /// <summary>The declared initialization values of the card slots, by GC-015's test-declared keys (P-032).</summary>
            private static IInitializationPolicyRegistry InitialValues()
            {
                var registry = new InitializationPolicyRegistry();
                registry.Register(SeatInitPolicy, CardTableKeys.SeatDomain, 0);
                return registry;
            }

            // ------------------------------------------------------------------ 2. the real world and its live state

            private void CreateWorldAndMarket()
            {
                const string name = "gc015-cards-world-and-live-state";
                try
                {
                    WorldId world = NextSession();
                    WorldCreateRequest request = CardTableRegistration.CommandDrivenRequest(
                        world, NextOperation(world), ContentHash.Empty);
                    UnityWorldRegistration registration = CardTableRegistration.Create(
                        descriptorReport.Adaptation!, CardTableRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        steps.Add(new CardStatePolicyStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = CardTableModule.Attach(host);

                    registry = new TargetRegistry(world, 16);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        CardTableRecipes.Catalog(seatApplier, tableApplier),
                        new MigrationRegistry(new List<ISlotMigration> { new SeatStateMigration() }),
                        descriptorReport.Descriptor!);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    lane = CompositionHost.CreateDefault(
                        world,
                        CardMarketComposition.MatchScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly);

                    pipeline = new DerivedAssemblyPipeline(
                        host, lane, publisher, targets, seeder, values, null, null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                        DefaultBudget());

                    policies = new StateMigrationPipeline(host, publisher, seeder, policyCatalog, DefaultBudget());

                    // The control plane is enough for this suite: a command-driven world with no admitted step needs
                    // no host pump, and the GC-009 time driver belongs to the slice's own scenario.
                    bool scopesCreated = PublishSetupEdits(CardMarketComposition.ScopeCreates());
                    bool seededMarket = CardTableFixture.SeedMarket(seeder, module, out DiagnosticCode seedCode, out string seedDetail);

                    // Real non-default runtime state on the protocol's own slot storage: the table's turn number,
                    // a seat's score slot and a committed output. A mis-implemented preserve loses these.
                    bool seededSlots = SeedSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.TableSlot, 1U, NonDefaultTurnNumber)
                        && SeedSlot(CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal), CardTableKeys.TableOwner, CardTableKeys.SeatSlot, 1U, SeededSeatScore)
                        && SeedSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.OutputSlot, 1U, 12);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        scopesCreated && seededMarket && seededSlots
                        && targets.Count == 6
                        && module.SeatCount == 4
                        && host.Lifecycle == WorldLifecycleState.Running,
                        "session=" + world.Session.ToString()
                        + "; scopesCreated=" + scopesCreated
                        + "; seededMarket=" + seededMarket
                        + (seededMarket ? string.Empty : "(" + seedCode + ": " + seedDetail + ")")
                        + "; seededSlots=" + seededSlots
                        + "; targets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; seats=" + module.SeatCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private bool SeedSlot(TargetId target, OwnerId owner, SlotId slot, uint version, int value)
                => seeder!.TrySeedSlot(target, owner, slot, version, value, out DiagnosticCode _, out string _);

            // ------------------------------------------------------------------ 3. tuning and reparenting

            private void TuningKeepsNonDefaultCounters()
            {
                const string name = "gc015-cards-tuning-keeps-non-default-counters";
                try
                {
                    PublishSetupEdits(new List<CompositionEditPayload>
                    {
                        CardTablePayloads.Mount(
                            CardTableFixture.TableRuntimeDeclaration().Manifest,
                            CardTableFixture.TableRuntimeInstance,
                            CardMarketComposition.MatchScope),
                        CardTablePayloads.Mount(
                            CardTableFixture.RuleLibraryDeclaration().Manifest,
                            CardTableFixture.RuleLibraryInstance,
                            CardMarketComposition.MatchScope),
                    });

                    // The tuning publication: a scoring provider whose contribution changes derived bonus rows.
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        CardTablePayloads.Mount(
                            CardTableFixture.ScoringDeclaration(false).Manifest,
                            CardTableFixture.QuietScoringInstance,
                            CardIdentity.Scope(CardVocabulary.LeagueB)),
                        null,
                        null,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? derived);
                    bool admitted = publication != null;

                    bool turnKept = ReadSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.TableSlot, out int turn, out uint _);
                    bool seatKept = ReadSlot(CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal), CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int score, out uint _);
                    bool domainScoresKept = SeatScoresAreSeedValues();
                    bool rowsPublished = publisher!.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal)).Count == 1;

                    steps.Add(new CardStatePolicyStep(
                        name,
                        admitted
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.PreservedCount == plan.Decisions.Count
                        && turnKept && turn == NonDefaultTurnNumber
                        && seatKept && score == SeededSeatScore
                        && domainScoresKept
                        && rowsPublished,
                        "admitted=" + admitted
                        + "; publication=" + Describe(publication)
                        + "; policies=" + (plan != null ? plan.ToString() : "<none>")
                        + "; turnNumber=" + Value(turnKept, turn)
                        + "; seatScore=" + Value(seatKept, score)
                        + "; domainSeatScoresKept=" + domainScoresKept
                        + "; leagueBSeatRows=" + publisher.ReadBindingRows(CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal)).Count.ToString(CultureInfo.InvariantCulture)
                        + "; derived=" + (derived != null ? derived.Describe() : "<none>")));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private bool SeatScoresAreSeedValues()
            {
                for (int i = 0; i < module!.SeatCount; i++)
                {
                    if (!module.TrySeat((uint)i, out Entity seat))
                    {
                        continue;
                    }

                    if (module.Host.EntityWorld.EntityManager.GetComponentData<CardSeatState>(seat).Score != SeededSeatScore)
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Moving the practice scope's subtree under league B and back: both moves are real reparent publications
            /// and neither may disturb the table's or a seat's state (P-025).
            /// </summary>
            private void ReparentKeepsState()
            {
                const string name = "gc015-cards-reparent-keeps-state";
                try
                {
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        CardTablePayloads.ScopeReparent(
                            CardIdentity.Scope(CardVocabulary.Practice), CardIdentity.Scope(CardVocabulary.LeagueB)),
                        null,
                        null,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);
                    bool moved = publication != null;
                    bool practiceIsUnderLeagueB = lane!.Committed.Scopes.TryGet(
                            CardIdentity.Scope(CardVocabulary.Practice), out ScopeRecord? practice)
                        && practice != null
                        && practice.Parent.Equals(CardIdentity.Scope(CardVocabulary.LeagueB));

                    AssemblyPublicationReport? back = PublishPolicyEdit(
                        CardTablePayloads.ScopeReparent(
                            CardIdentity.Scope(CardVocabulary.Practice), CardIdentity.Scope(CardVocabulary.LeagueA)),
                        null,
                        null,
                        out StatePolicyPlan? backPlan,
                        out DerivedAssemblyReport? _);
                    bool returned = back != null;

                    bool turnKept = ReadSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.TableSlot, out int turn, out uint _);
                    bool seatKept = ReadSlot(CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal), CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int score, out uint _);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        moved && practiceIsUnderLeagueB && publication != null && publication.Published
                        && plan != null && plan.Succeeded
                        && returned && back != null && back.Published && backPlan != null && backPlan.Succeeded
                        && turnKept && turn == NonDefaultTurnNumber
                        && seatKept && score == SeededSeatScore
                        && SeatScoresAreSeedValues(),
                        "moved=" + moved
                        + "; practiceUnderLeagueB=" + practiceIsUnderLeagueB
                        + "; publication=" + Describe(publication)
                        + "; returned=" + returned
                        + "; publicationBack=" + Describe(back)
                        + "; turnNumber=" + Value(turnKept, turn)
                        + "; seatScore=" + Value(seatKept, score)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 4. the declared surface, read live

            private void DeclaredSurface()
            {
                const string name = "gc015-cards-declared-surface";
                try
                {
                    bool decisionDraftIsDerived = HasLastSupport(CardTableKeys.DecisionDraftSlot, LastSupportPolicy.RemoveDerived);
                    bool tableHasWriter = policies!.HasActiveWriter(
                        new StateSlotKey(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.TableSlot));

                    steps.Add(new CardStatePolicyStep(
                        name,
                        decisionDraftIsDerived && tableHasWriter,
                        "decisionDraftDisposable=" + decisionDraftIsDerived
                        + "; tableSlotHasActiveWriter=" + tableHasWriter
                        + "; layouts=" + policyCatalog.Layouts!.ToString()));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 5. each slot policy in the world

            private void PreserveDormantInTheWorld()
            {
                const string name = "gc015-cards-preserve-dormant";
                try
                {
                    var seat = new StateSlotKey(
                        CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal), CardTableKeys.TableOwner, CardTableKeys.SeatSlot);
                    var requests = new List<StatePolicyRequest> { StatePolicyRequest.PreserveDormant(seat) };

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(), requests, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool recorded = policies!.Dormant.TryGet(seat, out DormantSlotRecord record);
                    bool rowKept = ReadSlot(seat.Target, seat.Owner, seat.Slot, out int value, out uint _);
                    bool hasWriter = policies.HasActiveWriter(seat);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.RetainedDormantCount == 1
                        && recorded && record.RetainedValue == SeededSeatScore
                        && rowKept && value == SeededSeatScore
                        && !hasWriter,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; dormant=" + (recorded ? record.ToString() : "<none>")
                        + "; liveValue=" + Value(rowKept, value)
                        + "; hasActiveWriter=" + hasWriter));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private void RemoveDerivedInTheWorld()
            {
                const string name = "gc015-cards-remove-derived";
                try
                {
                    bool seeded = SeedSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.DecisionDraftSlot, 1U, 3);
                    var draft = new StateSlotKey(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.DecisionDraftSlot);
                    var requests = new List<StatePolicyRequest> { StatePolicyRequest.RemoveDerived(draft) };

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(), requests, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);

                    bool removed = !ReadSlot(draft.Target, draft.Owner, draft.Slot, out int _, out uint _);
                    bool durableSiblingKept = ReadSlot(CardIdentity.Target(CardVocabulary.TableOne), CardTableKeys.TableOwner, CardTableKeys.TableSlot, out int turn, out uint _)
                        && turn == NonDefaultTurnNumber;

                    steps.Add(new CardStatePolicyStep(
                        name,
                        seeded
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.RemovedDerivedCount == 1
                        && removed && durableSiblingKept,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; removed=" + removed
                        + "; durableSiblingKept=" + durableSiblingKept));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The card catalog declares no migration for its slots, so a pass that must cross a version is refused
            /// with `MigrationRequired` rather than zero-initialising the value (P-032); with a test-declared
            /// migration key and a registered handler the same executor moves the state on its copy (05 s5).
            /// </summary>
            private void RegisteredMigrationInTheWorld()
            {
                const string name = "gc015-cards-registered-migration";
                try
                {
                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    var seat = new StateSlotKey(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot);
                    bool seeded = SeedSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, 1U, -2);

                    // Without a declared migration key the pass cannot cross the version: the seat declaration names
                    // the version-2 domain but no migration, which P-032 makes a validation error.
                    SlotStatePolicySet migratableWithoutKey = SetWith(MigratableWithoutKey());
                    StatePolicyPlan undeclared = StatePolicyExecutor.Execute(
                        migratableWithoutKey,
                        seeder!.ReadLiveSlots(TargetIds()),
                        new List<StatePolicyRequest>
                        {
                            StatePolicyRequest.Migrate(seat, SeatMigrationKey),
                        },
                        policyCatalog.Migrations,
                        new DeclaredSlotMigrationRegistry(migratableWithoutKey),
                        InitialValues(),
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot));

                    // With the declared key and a registered handler it does, on the copied value.
                    SlotStatePolicySet migratedSet = SetWith(Migratable());
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(),
                        new List<StatePolicyRequest> { StatePolicyRequest.Migrate(seat, SeatMigrationKey) },
                        migratedSet,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);

                    bool read = ReadSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int value, out uint version);
                    bool migratedOnTheCopy = new SeatStateMigration().TryMigrate(-2, out int expected);
                    bool transformed = expected != -2;

                    steps.Add(new CardStatePolicyStep(
                        name,
                        seeded
                        && !undeclared.Succeeded
                        && undeclared.Code == DiagnosticCode.MigrationRequired
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.MigratedCount == 1
                        && read && migratedOnTheCopy && transformed && value == expected && version == SeatDomainV2.Version,
                        "undeclaredMigration=" + undeclared.Code.ToString()
                        + "; publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; value=" + Value(read, value, version)
                        + "; expected=" + (migratedOnTheCopy ? expected.ToString(CultureInfo.InvariantCulture) : "<refused>")
                        + "; transformed=" + transformed));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The card catalog permits no reset either, so an undeclared reset is refused and the value stays; the
            /// declared reset — GC-015's test-declared initialization policy with its reason — is published and the
            /// row keeps the declared value (P-032).
            /// </summary>
            private void DeclaredResetInTheWorld()
            {
                const string name = "gc015-cards-explicit-reset";
                try
                {
                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    var seat = new StateSlotKey(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot);
                    bool beforeRead = ReadSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int before, out uint _);

                    StatePolicyPlan undeclared = StatePolicyExecutor.Execute(
                        policyCatalog.Policies!,
                        seeder!.ReadLiveSlots(TargetIds()),
                        new List<StatePolicyRequest> { StatePolicyRequest.Reset(seat, "table repair") },
                        policyCatalog.Migrations,
                        new DeclaredSlotMigrationRegistry(policyCatalog.Policies!),
                        InitialValues(),
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot));

                    bool keptAfterRefusal = ReadSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int afterRefusal, out uint _);

                    SlotStatePolicySet declaredSet = SetWith(Resettable());
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(),
                        new List<StatePolicyRequest> { StatePolicyRequest.Reset(seat, ResetReason) },
                        declaredSet,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);

                    bool afterRead = ReadSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int after, out uint _);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        beforeRead
                        && !undeclared.Succeeded
                        && undeclared.Code == DiagnosticCode.OwnershipConflict
                        && undeclared.Detail.Contains("does not declare a permitted reset")
                        && keptAfterRefusal && afterRefusal == before
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.ResetCount == 1
                        && plan.Decisions[0].Reason == ResetReason
                        && plan.Decisions[0].PolicyKey.Equals(SeatInitPolicy)
                        && afterRead && after == 0,
                        "undeclaredReset=" + undeclared.Code.ToString() + ": " + undeclared.Detail
                        + "; valueAfterRefusal=" + Value(keptAfterRefusal, afterRefusal)
                        + "; publication=" + Describe(publication)
                        + "; declaredReset=" + (plan != null ? plan.ToString() : "<none>")
                        + "; valueAfterReset=" + Value(afterRead, after)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            /// <summary>
            /// The ownership transfer of card state to a named available owner: the value moves to the destination
            /// owner's key and the source row is retired in the same publication (P-025, P-032).
            /// </summary>
            private void OwnershipTransferInTheWorld()
            {
                const string name = "gc015-cards-ownership-transfer";
                try
                {
                    TargetId table = CardIdentity.Target(CardVocabulary.TableOne);
                    bool seeded = SeedSlot(table, CardTableKeys.TableOwner, CardTableKeys.OutputSlot, 1U, 21);
                    var source = new StateSlotKey(table, CardTableKeys.TableOwner, CardTableKeys.OutputSlot);

                    SlotStatePolicySet transferSet = SetWith(Transferable(), AuditDeclaration());
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(),
                        new List<StatePolicyRequest> { StatePolicyRequest.LastSupportTransfer(source, AuditOwner, table) },
                        transferSet,
                        out StatePolicyPlan? plan,
                        out DerivedAssemblyReport? _);

                    bool sourceRetired = !ReadSlot(table, CardTableKeys.TableOwner, CardTableKeys.OutputSlot, out int _, out uint _);
                    bool destinationHolds = ReadSlot(table, AuditOwner, CardTableKeys.OutputSlot, out int moved, out uint movedVersion);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        seeded
                        && publication != null && publication.Published
                        && plan != null && plan.Succeeded && plan.TransferredCount == 1
                        && plan.Dispositions[0].DestinationOwner.Equals(AuditOwner)
                        && plan.Decisions[0].MovesToAnotherOwner
                        && sourceRetired
                        && destinationHolds && moved == 21 && movedVersion == 1U
                        && policies!.Transfers.Count == 1,
                        "publication=" + Describe(publication)
                        + "; plan=" + (plan != null ? plan.ToString() : "<none>")
                        + "; sourceRetired=" + sourceRetired
                        + "; destination=" + Value(destinationHolds, moved, movedVersion)
                        + "; observedTransfers=" + policies!.Transfers.Count.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ 6. prewrite failure and bounds

            private void FailedMigrationKeepsOldAssembly()
            {
                const string name = "gc015-cards-failed-migration-keeps-old-assembly";
                try
                {
                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    // The live row is ahead of the declared schema, which no registered migration may reverse.
                    bool seeded = SeedSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, SeatDomainV2.Version, 8);

                    AssemblyEpoch epochBefore = host!.CurrentEpoch;
                    CompositionRevision revisionBefore = publisher!.PublishedRevision;

                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(), null, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _);
                    bool kept = ReadSlot(seatA, CardTableKeys.TableOwner, CardTableKeys.SeatSlot, out int value, out uint version);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        seeded
                        && plan != null && !plan.Succeeded
                        && plan.Code == DiagnosticCode.UnsupportedVersion
                        && plan.Dispositions.Count == 0
                        && publication != null && !publication.Published && publication.Outcome == Outcome.Rejected
                        && host.CurrentEpoch.Equals(epochBefore)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && kept && value == 8 && version == SeatDomainV2.Version,
                        "plan=" + (plan != null ? (plan.Succeeded ? "succeeded" : plan.Code + ": " + plan.Detail) : "<none>")
                        + "; publication=" + Describe(publication)
                        + "; epoch=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; live=" + Value(kept, value, version)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            private void MigrationBounds()
            {
                const string name = "gc015-cards-migration-bounds";
                try
                {
                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    TargetId table = CardIdentity.Target(CardVocabulary.TableOne);
                    bool seeded = SeedSlot(table, CardTableKeys.TableOwner, CardTableKeys.TableSlot, 1U, NonDefaultTurnNumber);
                    AssemblyEpoch epochBefore = host!.CurrentEpoch;
                    var tinyBudget = new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, 32UL, 64UL);

                    // A migratable declaration, so the pass really tries to reserve scratch for two slots.
                    SlotStatePolicySet migratableSet = SetWith(Migratable());
                    StatePolicyPlan measured = StatePolicyExecutor.Execute(
                        migratableSet,
                        seeder!.ReadLiveSlots(TargetIds()),
                        null,
                        policyCatalog.Migrations,
                        new DeclaredSlotMigrationRegistry(migratableSet),
                        InitialValues(),
                        new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot));

                    var tinyPolicies = new StateMigrationPipeline(host, publisher!, seeder!, policyCatalog, tinyBudget);
                    AssemblyPublicationReport? publication = PublishPolicyEdit(
                        NextQuietScoringEdit(), null, null, out StatePolicyPlan? plan, out DerivedAssemblyReport? _, tinyPolicies, tinyBudget);
                    bool kept = ReadSlot(table, CardTableKeys.TableOwner, CardTableKeys.TableSlot, out int turn, out uint _);

                    steps.Add(new CardStatePolicyStep(
                        name,
                        seeded
                        && measured.ScratchHighWaterBytes > 0UL
                        && plan != null && !plan.Succeeded
                        && plan.Code == DiagnosticCode.BudgetExceeded
                        && plan.Dispositions.Count == 0
                        && host.CurrentEpoch.Equals(epochBefore)
                        && kept && turn == NonDefaultTurnNumber,
                        "measuredHighWaterBytes=" + measured.ScratchHighWaterBytes.ToString(CultureInfo.InvariantCulture)
                        + "; tinyBudgetPlan=" + (plan != null ? (plan.Succeeded ? "succeeded" : plan.Code.ToString() + ": " + plan.Detail) : "<none>")
                        + "; publication=" + Describe(publication)
                        + "; turnNumber=" + Value(kept, turn)
                        + "; seatA=" + seatA.ToString()));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStatePolicyStep(name, false, Describe(exception)));
                }
            }

            // ------------------------------------------------------------------ policy publication plumbing

            private static PlanBudget DefaultBudget()
                => new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot);

            /// <summary>
            /// Publishes one composition edit and its world assembly with no state-policy pass: the setup path the
            /// card slice already uses (scope creation, a provider mount), so both counters stay joined (P-006).
            /// </summary>
            private bool PublishSetupEdit(CompositionEditPayload payload)
            {
                if (!PublishCompositionEdit(payload))
                {
                    return false;
                }

                DerivedAssemblyReport derived = pipeline!.PublishDerived(NextOperation(host!.World.Session));
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

            private bool PublishSetupEdits(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishSetupEdit(payloads[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Publishes one composition edit whose state dispositions are GC-015's own policy pass (P-029, P-032).
            /// </summary>
            private AssemblyPublicationReport? PublishPolicyEdit(
                CompositionEditPayload payload,
                IReadOnlyList<StatePolicyRequest>? requests,
                SlotStatePolicySet? policyOverride,
                out StatePolicyPlan? policyPlan,
                out DerivedAssemblyReport? derived,
                StateMigrationPipeline? policyPipeline = null,
                PlanBudget? policyBudget = null)
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
                    new InertAcquisitionSet(new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                        NextOperation(host.World.Session)),
                    policyBudget ?? DefaultBudget(),
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

            /// <summary>Alternates the league-B scoring provider, so each policy observation has a real publication.</summary>
            private CompositionEditPayload NextQuietScoringEdit()
            {
                // The toggle reads the lane's committed state rather than a local flag, so a mount that some other
                // step already performed can never be repeated (P-010).
                bool mounted = lane!.Committed.TryGetInstall(CardTableFixture.QuietScoringInstance, out InstallEntry? entry)
                    && entry != null;
                return mounted
                    ? CardTablePayloads.Unmount(CardTableFixture.QuietScoringInstance)
                    : CardTablePayloads.Mount(
                        CardTableFixture.ScoringDeclaration(false).Manifest,
                        CardTableFixture.QuietScoringInstance,
                        CardIdentity.Scope(CardVocabulary.LeagueB));
            }

            private const string ResetReason = "table repair";

            /// <summary>The catalog's declared policies with one slot's declaration replaced (never duplicated).</summary>
            private SlotStatePolicySet SetWith(params SlotStatePolicy[] replacements)
            {
                var policies = new List<SlotStatePolicy>();
                for (int i = 0; i < policyCatalog.Policies!.Count; i++)
                {
                    SlotStatePolicy existing = policyCatalog.Policies.Policies[i];
                    bool replaced = false;
                    for (int r = 0; r < replacements.Length; r++)
                    {
                        if (existing.SlotId.Equals(replacements[r].SlotId))
                        {
                            replaced = true;
                            break;
                        }
                    }

                    if (!replaced)
                    {
                        policies.Add(existing);
                    }
                }

                for (int r = 0; r < replacements.Length; r++)
                {
                    policies.Add(replacements[r]);
                }

                return new SlotStatePolicySet(policies);
            }

            /// <summary>The seat slot with the GC-015 test-declared version change (P-032, 05 s5).</summary>
            private static SlotStatePolicy Migratable()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        CardTableKeys.SeatSlot,
                        CardTableKeys.TableOwner,
                        SeatDomainV2,
                        CardTableKeys.SeatLayout,
                        null,
                        LastSupportPolicy.PreserveDormant,
                        default(FactoryKey),
                        new List<FactoryKey> { SeatMigrationKey },
                        SlotAuthorityOptions.Dormant()),
                    SeatInitPolicy,
                    default(FactoryKey),
                    SeatMigrationKey);

            /// <summary>The seat slot at version 2 with no declared migration: a version change that cannot run.</summary>
            private static SlotStatePolicy MigratableWithoutKey()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        CardTableKeys.SeatSlot,
                        CardTableKeys.TableOwner,
                        SeatDomainV2,
                        CardTableKeys.SeatLayout,
                        null,
                        LastSupportPolicy.PreserveDormant,
                        default(FactoryKey),
                        null,
                        SlotAuthorityOptions.Dormant()),
                    SeatInitPolicy,
                    default(FactoryKey),
                    default(FactoryKey));

            /// <summary>The seat slot with the manifest-supported reset GC-015 declares for this pass (P-032).</summary>
            private static SlotStatePolicy Resettable()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        CardTableKeys.SeatSlot,
                        CardTableKeys.TableOwner,
                        CardTableKeys.SeatDomain,
                        CardTableKeys.SeatLayout,
                        null,
                        LastSupportPolicy.PreserveDormant,
                        default(FactoryKey),
                        null,
                        SlotAuthorityOptions.Resettable(ResetReason, true, false)),
                    SeatInitPolicy,
                    default(FactoryKey),
                    default(FactoryKey));

            /// <summary>The committed-output slot with the declared `TransferTo` last-support policy (P-032).</summary>
            private static SlotStatePolicy Transferable()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        CardTableKeys.OutputSlot,
                        CardTableKeys.TableOwner,
                        CardTableKeys.OutputDomain,
                        CardTableKeys.OutputLayout,
                        null,
                        LastSupportPolicy.TransferTo,
                        SeatTransferPolicy,
                        null,
                        SlotAuthorityOptions.Durable()),
                    default(FactoryKey),
                    default(FactoryKey),
                    default(FactoryKey));

            /// <summary>The GC-015 test-declared slot that makes the audit owner an available owner (P-032).</summary>
            private static SlotStatePolicy AuditDeclaration()
                => new SlotStatePolicy(
                    new SlotAuthorityDeclaration(
                        AuditSlot,
                        AuditOwner,
                        CardTableKeys.OutputDomain,
                        CardTableKeys.OutputLayout,
                        null,
                        LastSupportPolicy.PreserveDormant,
                        default(FactoryKey),
                        null,
                        SlotAuthorityOptions.Dormant()),
                    default(FactoryKey),
                    default(FactoryKey),
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

            private OperationId NextOperation(WorldId world)
                => new OperationId(world, CardTableKeys.Issuer, ++operationSequence);

            private static string Describe(AssemblyPublicationReport? publication)
                => publication == null
                    ? "<none>"
                    : publication.Outcome.ToString()
                        + (publication.Code == DiagnosticCode.None ? string.Empty : "(" + publication.Code.ToString() + ")")
                        + " " + publication.Detail;

            private static string Value(bool present, int value)
                => present ? value.ToString(CultureInfo.InvariantCulture) : "<missing>";

            private static string Value(bool present, int value, uint version)
                => present
                    ? value.ToString(CultureInfo.InvariantCulture) + "@" + version.ToString(CultureInfo.InvariantCulture)
                    : "<missing>";

            private static string Describe(Exception exception)
                => exception.GetType().Name + ": " + exception.Message + Environment.NewLine + exception.StackTrace;
        }

        /// <summary>
        /// The GC-015 test-declared seat-state migration: version-2 scores carry an offset the version-1 encoding
        /// did not, so a migrated value differs from its source (05 s5, P-032).
        /// </summary>
        private sealed class SeatStateMigration : ISlotMigration
        {
            public FactoryKey Key => SeatMigrationKey;

            public uint FromVersion => 1U;

            public uint ToVersion => SeatDomainV2.Version;

            public bool TryMigrate(int source, out int migrated)
            {
                // The version-2 seat score carries a fixed offset the version-1 encoding did not, so the transform
                // is observable on the copied value rather than a relabelling of the version (05 s5).
                migrated = source + 100;
                return true;
            }
        }
    }
}
