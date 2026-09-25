// GameCore.Gameplay.Cards.Fixtures — the Wave 4 additive multi-supporter card fixture (GC-012).
//
// WHY THIS FILE EXISTS
//
// P-017 says "Support from multiple contributions is a set of IDs, not a boolean owned by the last plugin" and
// P-019 says an `Additive` slot's effective value is its registered reducer's fold over *every* eligible
// contribution. Before GC-012 the derivation-to-publication seam refused any effective slot with more than one
// supporter (`DerivedCompositionProposal.Build`), so the nested +3 festival on top of the +2 festival was provable
// only in the pure reducer test (`CardRulesTests.TryReduceBonusFoldsTheContributionsInAscendingIndexOrder`) and in
// the pure derivation fixture (`ReferenceCompositionTests.RefC04_...`). No live world ever carried two supporters
// on one seat's `cards.set-bonus` slot, and `AssemblyPlanner` published the top-ranked candidate's raw value.
//
// This fixture closes that with a real world: it creates the real card composition, seeds the real market, mounts
// the table runtime, its rule library, the festival provider at League A and the nested-festival provider at Seat
// A's own scope, and then reads the published storage. It observes
//
//   * one binding row for `cards.set-bonus` on Seat A whose value is the *composed* 5 (2 + 3), not a candidate's 2;
//   * exactly two support rows for that slot, one per provider, each carrying its own contribution value, so the
//     composed value is explainable from the published storage (P-017, P-026);
//   * the planner does not collapse the slot to the top-ranked candidate (which would publish 2 or 3);
//   * retracting the nested provider removes exactly its own support row and leaves the surviving provider's
//     support and the recomposed value intact (P-033);
//   * the Co-supporter is still observable, so "removing one provider" and "the slot disappeared" are different
//     outcomes (P-017).
//
// It is a new file, deliberately separate from `CardMarketScenario`: that scenario's thirteen observations are
// GC-011's canonical step list, asserted by `GameCore.Cards.Tests` and by the `-probeCards` driver, and a Wave 4
// gap correction must not renumber them. Both files use the same real modules and the same fixture declarations.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Cards.Fixtures
{
    /// <summary>Facts the additive fixture observed, so a caller can assert on values rather than a boolean.</summary>
    public sealed class CardAdditiveFacts
    {
        /// <summary>Fingerprint of the catalog the run was given (P-028).</summary>
        public string CatalogFingerprint { get; set; } = string.Empty;

        /// <summary>World incarnation this run created.</summary>
        public string WorldSession { get; set; } = string.Empty;

        /// <summary>Live targets and installed rows after the four mounts.</summary>
        public int LiveTargetCount { get; set; }
        public int InstalledRows { get; set; }

        /// <summary>Seat A's single `cards.set-bonus` binding row after both scoring providers mounted.</summary>
        public int SeatABonusRowCount { get; set; }
        public int SeatABonusValue { get; set; }

        /// <summary>Contributions the published row reports as its support set (P-017).</summary>
        public int SeatABonusSupporterCount { get; set; }

        /// <summary>Support rows the published storage holds for that slot, one per supporter.</summary>
        public int SeatABonusSupportRowCount { get; set; }

        /// <summary>Support values read from the published support rows, ascending by provider identity.</summary>
        public int SeatABonusSupportValueSum { get; set; }

        /// <summary>Distinct provider installations the support rows name.</summary>
        public int SeatABonusDistinctProviderCount { get; set; }

        /// <summary>True when the festival provider (+2) is one of the recorded supporters.</summary>
        public bool FestivalSupportPresent { get; set; }

        /// <summary>True when the nested-festival provider (+3) is one of the recorded supporters.</summary>
        public bool NestedSupportPresent { get; set; }

        /// <summary>Seat B and Seat C bonus values: the control seats that must be untouched by the nested mount.</summary>
        public int SeatBBonusValue { get; set; }
        public int SeatCBonusValue { get; set; }

        /// <summary>Reductions and predicate evaluations the derivation performed (bounded work evidence).</summary>
        public int Reductions { get; set; }
        public int PredicateEvaluations { get; set; }

        /// <summary>After retracting the nested provider: the surviving row and its support set (P-033).</summary>
        public int SeatABonusValueAfterNestedUnmount { get; set; }
        public int SeatABonusSupporterCountAfterNestedUnmount { get; set; }
        public int SeatABonusSupportRowCountAfterNestedUnmount { get; set; }
        public bool FestivalSupportSurvivesNestedUnmount { get; set; }

        /// <summary>Registered worlds after teardown; zero means this run left nothing behind.</summary>
        public int RegistryAfterTeardown { get; set; }

        /// <summary>One-line digest of every recorded fact, so a probe archives values beside verdicts.</summary>
        public string Describe() =>
            "catalogFingerprint=" + CatalogFingerprint
            + "; session=" + WorldSession
            + "; liveTargets=" + I(LiveTargetCount)
            + "; installedRows=" + I(InstalledRows)
            + "; seatARows=" + I(SeatABonusRowCount)
            + "; seatAComposedValue=" + I(SeatABonusValue)
            + "; seatASupporters=" + I(SeatABonusSupporterCount)
            + "; seatASupportRows=" + I(SeatABonusSupportRowCount)
            + "; seatASupportValueSum=" + I(SeatABonusSupportValueSum)
            + "; seatADistinctProviders=" + I(SeatABonusDistinctProviderCount)
            + "; festivalSupport=" + FestivalSupportPresent
            + "; nestedSupport=" + NestedSupportPresent
            + "; seatBValue=" + I(SeatBBonusValue)
            + "; seatCValue=" + I(SeatCBonusValue)
            + "; reductions=" + I(Reductions)
            + "; predicateEvals=" + I(PredicateEvaluations)
            + "; seatAValueAfterNestedUnmount=" + I(SeatABonusValueAfterNestedUnmount)
            + "; seatASupportersAfterNestedUnmount=" + I(SeatABonusSupporterCountAfterNestedUnmount)
            + "; seatASupportRowsAfterNestedUnmount=" + I(SeatABonusSupportRowCountAfterNestedUnmount)
            + "; festivalSupportSurvives=" + FestivalSupportSurvivesNestedUnmount
            + "; registryAfterTeardown=" + I(RegistryAfterTeardown);

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Result of one additive run: the named observations plus the facts they were computed from.</summary>
    public sealed class CardAdditiveResult
    {
        public CardAdditiveResult(IReadOnlyList<CardStep> steps, CardAdditiveFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<CardStep> Steps { get; }

        public CardAdditiveFacts Facts { get; }

        /// <summary>True when every observation passed and there was at least one.</summary>
        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        /// <summary>One-line digest naming every failed observation.</summary>
        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " additive checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " additive check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>
    /// Runs the additive multi-supporter proof over the committed generated card catalog and over the hand-written
    /// generated-style catalog, each in its own world (P-017, P-019, P-033).
    /// </summary>
    public static class CardAdditiveScenario
    {
        private const ulong HostTicks = 1_000_000UL;
        private const ulong ScratchCapacityBytes = 4096UL;
        private const ulong ScratchBytesPerSlot = 64UL;
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>World-session salt of this fixture; distinct from the card slice's, so two runs never collide.</summary>
        private const ulong SessionSalt = 0x4144444954495645UL;

        /// <summary>Stable name of the nested festival provider's rule, per the vendor's own naming convention.</summary>
        private const string NestedRuleSuffix = ".set-bonus";

        /// <summary>Runs the fixture over the hand-written generated-style catalog in this assembly.</summary>
        public static CardAdditiveResult RunFixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Run(
                build.Catalog,
                Declarations(),
                new FactoryKey(new Id128(CardTableKeys.Issuer.High, CardTableKeys.Issuer.Low), 1U),
                CardTableKeys.PluginType("cards.absent-plugin"),
                CardCatalogTable.Fingerprint());
        }

        /// <summary>
        /// Runs the fixture against one catalog. The declarations must include the table runtime, both scoring
        /// providers and the rule library, because the mounts resolve a manifest the catalog registered (P-009).
        /// </summary>
        public static CardAdditiveResult Run(
            ImmutableCatalog catalog,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            FactoryKey absentFactoryKey,
            PluginTypeId absentPluginType,
            ContentHash declaredFingerprint)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (declarations == null || declarations.Count < 4)
            {
                throw new ArgumentException(
                    "the additive fixture mounts a table runtime, a rule library and two scoring providers, so it"
                    + " needs all four generated-style declarations.",
                    nameof(declarations));
            }

            return new Executor(catalog, declarations, absentFactoryKey, absentPluginType, declaredFingerprint).Run();
        }

        /// <summary>The nested festival provider's declared plugin type (07 s2.1's "a festival under a festival").</summary>
        public static PluginTypeId NestedFestivalType => CardTableKeys.PluginType(CardVocabulary.NestedFestival);

        /// <summary>The nested festival provider's mount identity.</summary>
        public static PluginInstanceId NestedFestivalInstance => CardTableKeys.Instance(CardVocabulary.NestedFestival);

        /// <summary>The nested festival provider's declaration: the +3 rule of 07 s2.1.</summary>
        public static CatalogPluginDeclaration NestedFestivalDeclaration() =>
            new CatalogPluginDeclaration(
                CardTableDeclarations.ScoringProvider(
                    NestedFestivalType,
                    CardTableKeys.PluginFactoryKey,
                    CardTableKeys.ConfigSchema,
                    CardVocabulary.NestedFestival + NestedRuleSuffix,
                    CardVocabulary.NestedFestivalBonus),
                ConfigDocument.Empty);

        /// <summary>The extra declaration set this fixture adds on top of the card slice's own four.</summary>
        public static IReadOnlyList<CatalogPluginDeclaration> Declarations()
        {
            var declarations = new List<CatalogPluginDeclaration>(CardTableFixture.Declarations());
            declarations.Add(NestedFestivalDeclaration());
            return declarations;
        }

        private sealed class Executor
        {
            private readonly ImmutableCatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly FactoryKey absentFactoryKey;
            private readonly PluginTypeId absentPluginType;
            private readonly ContentHash declaredFingerprint;
            private readonly List<CardStep> steps = new List<CardStep>();
            private readonly CardAdditiveFacts facts = new CardAdditiveFacts();
            private readonly IdSequence sessionSequence = new IdSequence(SessionSalt);
            private readonly CardSeatApplier seatApplier = new CardSeatApplier();
            private readonly MarketTableApplier tableApplier = new MarketTableApplier();
            private readonly CardDerivationValueSource values = CardDerivationValueSource.Default();

            private UnityWorldHost? host;
            private CardTableModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private PipelineDescriptorReport? descriptorReport;
            private WorldTimeDriver? time;
            private ulong operationSequence;
            private int installedRows;

            public Executor(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                FactoryKey absentFactoryKey,
                PluginTypeId absentPluginType,
                ContentHash declaredFingerprint)
            {
                this.catalog = catalog;
                this.declarations = declarations;
                this.absentFactoryKey = absentFactoryKey;
                this.absentPluginType = absentPluginType;
                this.declaredFingerprint = declaredFingerprint;
            }

            public CardAdditiveResult Run()
            {
                Prepare();
                InstallBothProviders();
                RetractTheNestedSupport();
                TearDown();

                return new CardAdditiveResult(steps, facts);
            }

            // ---------------------------------------------------------------- 1. one real card world

            private void Prepare()
            {
                const string name = "w4-additive-catalog-and-world";
                try
                {
                    CatalogLookup factory = catalog.Lookup(CardTableKeys.PluginFactoryKey);
                    CatalogLookup absent = catalog.Lookup(absentFactoryKey);
                    var manifests = new CatalogManifestSource(catalog, declarations);
                    bool fingerprintAsDeclared = catalog.Fingerprint.Equals(declaredFingerprint);
                    facts.CatalogFingerprint = catalog.Fingerprint.ToHex();

                    PipelineDescriptorReport report = OwnershipSchedulePipeline.Build(
                        CardTableFixture.Manifests(),
                        CardTableRegistration.DispatchKinds(),
                        new SlotMigrationRegistry());
                    descriptorReport = report;
                    if (!report.Succeeded || report.Descriptor == null || report.Adaptation == null)
                    {
                        steps.Add(new CardStep(name, false, "the pipeline refused: " + report.Describe()));
                        return;
                    }

                    WorldId world = new WorldId(sessionSequence.Next());
                    facts.WorldSession = world.Session.ToString();
                    WorldCreateRequest request = CardTableRegistration.CommandDrivenRequest(
                        world,
                        NextOperation(world),
                        ContentHash.Empty);
                    UnityWorldRegistration registration = CardTableRegistration.Create(
                        report.Adaptation!,
                        CardTableRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(
                        request,
                        registration,
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        steps.Add(new CardStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = CardTableModule.Attach(host);
                    registry = new TargetRegistry(world, 16);
                    SpawnRecipeCatalog recipes = CardTableRecipes.Catalog(seatApplier, tableApplier);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        recipes,
                        new MigrationRegistry(new List<ISlotMigration>()),
                        report.Descriptor!);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    lane = CompositionHost.CreateDefault(
                        world,
                        CardMarketComposition.MatchScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly);

                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        values,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, CardTableKeys.Issuer),
                        new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(report.Adaptation!.NativeTable!);

                    DiagnosticCode seedCode = DiagnosticCode.None;
                    string seedDetail = string.Empty;
                    bool scopesCreated = PublishEdits(CardMarketComposition.ScopeCreates());
                    bool seeded = scopesCreated
                        && CardTableFixture.SeedMarket(seeder, module, out seedCode, out seedDetail);
                    bool mounted = seeded && PublishEdits(new List<CompositionEditPayload>
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

                    facts.LiveTargetCount = targets.Count;
                    bool pass = fingerprintAsDeclared
                        && factory.Found
                        && !absent.Found
                        && manifests.AcceptedCount == declarations.Count
                        && manifests.Rejected.Count == 0
                        && report.Descriptor!.TryValidate(out DiagnosticCode _, out string _)
                        && mounted
                        && facts.LiveTargetCount == 6
                        && host.Lifecycle == WorldLifecycleState.Running;

                    steps.Add(new CardStep(name, pass,
                        "fingerprint=" + facts.CatalogFingerprint
                        + "; fingerprintAsDeclared=" + fingerprintAsDeclared
                        + "; accepted=" + I(manifests.AcceptedCount)
                        + "; rejected=" + I(manifests.Rejected.Count)
                        + "; factoryLookup=" + factory.Describe()
                        + "; unknownKey=" + absent.Describe()
                        + "; scopesCreated=" + scopesCreated
                        + "; seeded=" + seeded
                        + (seeded ? string.Empty : "; seedFailure=" + seedCode + ": " + seedDetail)
                        + "; baseMounted=" + mounted
                        + "; liveTargets=" + I(facts.LiveTargetCount)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 2. both providers on Seat A

            private void InstallBothProviders()
            {
                const string name = "w4-additive-two-supporters-publish-one-composed-value";
                try
                {
                    if (lane == null || host == null || pipeline == null || publisher == null || module == null)
                    {
                        steps.Add(new CardStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    // The festival provider is mounted at League A (so it reaches Seat A and Seat B), and the nested
                    // festival provider at Seat A's *own* scope. Both rules are `SelfAndDescendants` with
                    // `ExportToDescendants`, so in Automatic mode Seat A is eligible for both with no per-instance
                    // import (P-013, P-015).
                    bool festival = PublishEdit(CardTablePayloads.Mount(
                        CardTableFixture.ScoringDeclaration(true).Manifest,
                        CardTableFixture.FestivalScoringInstance,
                        CardIdentity.Scope(CardVocabulary.LeagueA)));
                    bool nested = festival && PublishEdit(CardTablePayloads.Mount(
                        NestedFestivalDeclaration().Manifest,
                        NestedFestivalInstance,
                        CardIdentity.Scope(CardVocabulary.SeatAScope)));

                    facts.InstalledRows = installedRows;
                    facts.Reductions = values.ReductionCount;
                    facts.PredicateEvaluations = values.EvaluationCount;

                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    TargetId seatB = CardTableFixture.SeatTarget(CardTableKeys.SeatBOrdinal);
                    TargetId seatC = CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal);

                    IReadOnlyList<CapabilityBinding> seatARows = publisher.ReadBindingRows(seatA);
                    facts.SeatABonusRowCount = seatARows.Count;
                    facts.SeatABonusValue = seatARows.Count > 0 ? seatARows[0].Value : -1;
                    facts.SeatABonusSupporterCount = seatARows.Count > 0 ? seatARows[0].SupporterCount : -1;
                    facts.SeatBBonusValue = FirstBonusValue(publisher, seatB);
                    facts.SeatCBonusValue = FirstBonusValue(publisher, seatC);

                    // The published support rows are the load-bearing observation: one row per contributing
                    // provider, each carrying that provider's own value, so "2 + 3 = 5" is readable from the world
                    // rather than inferred from the composed number (P-017, P-019, P-026).
                    IReadOnlyList<CapabilitySupportRow> supportRows = publisher.ReadSupportRows(
                        seatA,
                        CardVocabulary.SetBonusCapability,
                        0U);
                    facts.SeatABonusSupportRowCount = supportRows.Count;
                    int sum = 0;
                    var providers = new HashSet<string>();
                    for (int i = 0; i < supportRows.Count; i++)
                    {
                        sum += supportRows[i].Value;
                        providers.Add(supportRows[i].Provider.ToString());
                        if (supportRows[i].Value == CardVocabulary.FestivalBonus)
                        {
                            facts.FestivalSupportPresent = true;
                        }

                        if (supportRows[i].Value == CardVocabulary.NestedFestivalBonus)
                        {
                            facts.NestedSupportPresent = true;
                        }
                    }

                    facts.SeatABonusSupportValueSum = sum;
                    facts.SeatABonusDistinctProviderCount = providers.Count;

                    int expectedComposed = CardVocabulary.FestivalBonus + CardVocabulary.NestedFestivalBonus;
                    bool pass = festival
                        && nested
                        && facts.SeatABonusRowCount == 1
                        && facts.SeatABonusValue == expectedComposed
                        && facts.SeatABonusSupporterCount == 2
                        && facts.SeatABonusSupportRowCount == 2
                        && facts.SeatABonusDistinctProviderCount == 2
                        && facts.FestivalSupportPresent
                        && facts.NestedSupportPresent
                        && facts.SeatABonusSupportValueSum == expectedComposed
                        && facts.SeatBBonusValue == CardVocabulary.FestivalBonus
                        && facts.SeatCBonusValue == -1;

                    steps.Add(new CardStep(name, pass,
                        "festivalMounted=" + festival
                        + "; nestedMounted=" + nested
                        + "; seatARows=" + I(facts.SeatABonusRowCount)
                        + "; seatAComposedValue=" + I(facts.SeatABonusValue)
                        + "; expectedComposed=" + I(expectedComposed)
                        + "; seatASupporters=" + I(facts.SeatABonusSupporterCount)
                        + "; seatASupportRows=" + I(facts.SeatABonusSupportRowCount)
                        + "; seatASupportValueSum=" + I(facts.SeatABonusSupportValueSum)
                        + "; seatADistinctProviders=" + I(facts.SeatABonusDistinctProviderCount)
                        + "; festivalSupport=" + facts.FestivalSupportPresent
                        + "; nestedSupport=" + facts.NestedSupportPresent
                        + "; seatBValue=" + I(facts.SeatBBonusValue)
                        + "; seatCValue=" + I(facts.SeatCBonusValue)
                        + "; reductions=" + I(facts.Reductions)
                        + "; predicateEvals=" + I(facts.PredicateEvaluations)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 3. retracting one supporter

            private void RetractTheNestedSupport()
            {
                const string name = "w4-additive-retracting-one-supporter-keeps-the-other";
                try
                {
                    if (lane == null || pipeline == null || publisher == null)
                    {
                        steps.Add(new CardStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    // P-033: removing one provider removes exactly its own support; the surviving provider's
                    // contribution and the recomposed value stay, so the slot is not deleted wholesale.
                    bool unmounted = PublishEdit(CardTablePayloads.Unmount(NestedFestivalInstance));

                    TargetId seatA = CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);
                    IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(seatA);
                    facts.SeatABonusValueAfterNestedUnmount = rows.Count > 0 ? rows[0].Value : -1;
                    facts.SeatABonusSupporterCountAfterNestedUnmount = rows.Count > 0 ? rows[0].SupporterCount : -1;

                    IReadOnlyList<CapabilitySupportRow> supportRows = publisher.ReadSupportRows(
                        seatA,
                        CardVocabulary.SetBonusCapability,
                        0U);
                    facts.SeatABonusSupportRowCountAfterNestedUnmount = supportRows.Count;
                    for (int i = 0; i < supportRows.Count; i++)
                    {
                        if (supportRows[i].Value == CardVocabulary.FestivalBonus)
                        {
                            facts.FestivalSupportSurvivesNestedUnmount = true;
                        }
                    }

                    bool pass = unmounted
                        && facts.SeatABonusValueAfterNestedUnmount == CardVocabulary.FestivalBonus
                        && facts.SeatABonusSupporterCountAfterNestedUnmount == 1
                        && facts.SeatABonusSupportRowCountAfterNestedUnmount == 1
                        && facts.FestivalSupportSurvivesNestedUnmount;

                    steps.Add(new CardStep(name, pass,
                        "unmounted=" + unmounted
                        + "; seatAValueAfter=" + I(facts.SeatABonusValueAfterNestedUnmount)
                        + "; seatASupportersAfter=" + I(facts.SeatABonusSupporterCountAfterNestedUnmount)
                        + "; seatASupportRowsAfter=" + I(facts.SeatABonusSupportRowCountAfterNestedUnmount)
                        + "; festivalSupportSurvives=" + facts.FestivalSupportSurvivesNestedUnmount));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- 4. teardown

            private void TearDown()
            {
                const string name = "w4-additive-teardown-settles-and-disposes";
                try
                {
                    CardTableModule.DetachAll();
                    UnityWorldRegistry.ResetAll();
                    facts.RegistryAfterTeardown = UnityWorldRegistry.Count;

                    steps.Add(new CardStep(
                        name,
                        facts.RegistryAfterTeardown == 0,
                        "registryAfterTeardown=" + I(facts.RegistryAfterTeardown)));
                }
                catch (Exception exception)
                {
                    steps.Add(new CardStep(name, false, DescribeException(exception)));
                }
            }

            // ---------------------------------------------------------------- helpers

            private static int FirstBonusValue(AssemblyPublisher publisher, TargetId target)
            {
                IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(target);
                return rows.Count > 0 ? rows[0].Value : -1;
            }

            private bool PublishEdit(CompositionEditPayload payload)
            {
                if (lane == null || pipeline == null || host == null)
                {
                    return false;
                }

                string editName = "w4-additive-edit-" + payload.Subject.ToString();
                EditAdmission admission = lane.SubmitEdit(payload, NextOperation(lane.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    steps.Add(new CardStep(
                        editName, false, "the edit was refused: " + admission.Kind + "/" + admission.Code));
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    steps.Add(new CardStep(
                        editName,
                        false,
                        "the publication was refused: "
                        + (published.Count > 0 ? published[0].Outcome.ToString() : "none")));
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(host.World));
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    steps.Add(new CardStep(editName, false, "the world refused the assembly: " + report.Describe()));
                    return false;
                }

                installedRows += report.InstalledRows;
                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher!.PublishUnchangedAssembly(
                        NextOperation(host.World), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        steps.Add(new CardStep(editName, false, "unchanged assembly publication refused: " + unchanged.Detail));
                        return false;
                    }
                }
                bool joined = AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher!.PublishedRevision,
                    host.CurrentEpoch);
                if (!joined)
                {
                    steps.Add(new CardStep(editName, false, "the lane and the world epochs disagree after the edit"));
                    return false;
                }

                return true;
            }

            private bool PublishEdits(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishEdit(payloads[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, CardTableFixture.Issuer, operationSequence);
            }

            private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
