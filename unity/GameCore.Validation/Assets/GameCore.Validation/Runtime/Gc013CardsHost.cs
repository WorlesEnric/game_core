// GameCore.Validation.ProbeHost — the GC-013 card family adapter.
//
// The card market's declared facts (07 section 2.1's league tree, GC-011's scoring providers) as the GC-013
// scenario's `IGc013Family`: the scenario owns the shared scripted sequence, and this file owns everything the card
// genre declares — its catalog declarations, its scope tree, its live seats and the composition edits the sequence
// submits.
//
// The card gameplay package already provides the two payload builders the task's sequence needs and no earlier
// scenario called: `CardTablePayloads.ScopeReparent` (07 s2.4's "Reparent `SeatA` from `LeagueA` to `LeagueB`") and
// `CardTablePayloads.ModeSet`. This file uses them rather than re-declaring the same edit shape.
//
// Step D needs a pair of providers whose `Exclusive` rules both reach the same targets. The card vocabulary declares
// `cards.draw-policy` as that exclusive capability (07 s2.4) but no provider of it, so the pair is declared here: one
// provider declares the `cards.draw-policy` contract and its rule, the other declares the rule only, because one
// capability identity has exactly one declaration per catalog revision (P-019).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.GeneratedCards;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-013 card host of the qualification project: it supplies the two catalogs and the declaration sets the card
    /// scenario is run against, exactly as <see cref="CardsScenarioHost"/> does for GC-011, and hands the resulting
    /// observations to the caller.
    /// </summary>
    public static class Gc013CardsHost
    {
        /// <summary>Family label every observation name of this family carries.</summary>
        public const string Label = "cards";

        /// <summary>Package version every card declaration carries (mirrors the gameplay package's own).</summary>
        private const string PackageVersion = "1.0.0";

        /// <summary>Stable name of the first provider of the exclusive draw-policy pair (07 s2.4).</summary>
        private const string DrawPolicyOne = "cards.gc013.draw-policy-one";

        /// <summary>Stable name of the second provider of that pair.</summary>
        private const string DrawPolicyTwo = "cards.gc013.draw-policy-two";

        /// <summary>Stratum the `cards.draw-policy` capability occupies (P-021).</summary>
        private const int DrawPolicyStratum = 0;

        /// <summary>
        /// The exclusive capability step D's two providers both emit. The identity is the card vocabulary's own
        /// `cards.draw-policy`, and its one output slot is derived exactly as a contract declaration derives it
        /// (`&lt;capability&gt;.slot-0`, 05 section 2), so the pair composes over the real card slot identity.
        /// </summary>
        public static readonly CapabilityId DrawPolicyCapability = CardIdentity.Capability(CardVocabulary.DrawPolicy);

        /// <summary>`cards.draw-policy`'s single output slot.</summary>
        public static readonly SlotId DrawPolicySlot = CardIdentity.Slot(CardVocabulary.DrawPolicy + ".slot-0");

        /// <summary>The payload schema of that slot (07 s2.2's row for `cards.draw-policy`).</summary>
        public static readonly SchemaRef DrawPolicySchema = CardIdentity.SchemaRef(CardVocabulary.DrawPolicySchema);

        /// <summary>Two scopes no live target lives in: the neutral publications the sequence publishes through.</summary>
        public static readonly ScopeId SpareScopeA = CardIdentity.Scope("cards.gc013.spare-scope-a");

        public static readonly ScopeId SpareScopeB = CardIdentity.Scope("cards.gc013.spare-scope-b");

        /// <summary>The branch the future seat is spawned in: League B, which the move leaves untouched.</summary>
        public static readonly ScopeId FutureBranchScope = CardIdentity.Scope(CardVocabulary.SeatBScope);

        /// <summary>The one target whose descriptor declares the complete explicit opt-in (P-013).</summary>
        public static readonly TargetId OptedInTarget = CardIdentity.Target("cards.gc013.opted-in-seat");

        /// <summary>The opted-in target's recipe: the seat recipe's selector schema under its own identity.</summary>
        public static readonly DefinitionRef OptedInSeatRecipe = CardIdentity.Recipe(
            CardVocabulary.CardSeatRecipe + ".gc013-opted-in.definition",
            CardVocabulary.CardSeatRecipe);

        /// <summary>Installation of the first draw-policy provider.</summary>
        public static readonly PluginInstanceId DrawPolicyOneInstance = CardTableKeys.Instance(DrawPolicyOne);

        /// <summary>Installation of the second draw-policy provider.</summary>
        public static readonly PluginInstanceId DrawPolicyTwoInstance = CardTableKeys.Instance(DrawPolicyTwo);

        /// <summary>Scope the first provider is mounted at: League A, the festival provider's own scope.</summary>
        public static readonly ScopeId DrawPolicyOneScope = CardIdentity.Scope(CardVocabulary.LeagueA);

        /// <summary>Scope the second provider is mounted at: the match root, so its reach overlaps the first's.</summary>
        public static readonly ScopeId DrawPolicyTwoScope = CardMarketComposition.MatchScope;

        /// <summary>Ordinal the opted-in seat is seated with; it is not the spawn's ordinal (P-008).</summary>
        private const uint OptedInSeatOrdinal = 3U;

        /// <summary>Ordinal the spawned seat is seated with; the practice seat's declared ordinal stays its own.</summary>
        private const uint FutureSeatOrdinal = 5U;

        /// <summary>Value the moved seat's live state slot is seeded with; the move must not change it (P-025).</summary>
        private const int MutableStateValue = 4242;

        public const ulong SessionSalt = 0x4341524743303133UL;

        /// <summary>
        /// The market's declared scope tree as lane-seed records: the world definition's own tree, opened with the
        /// initial composition rather than published as eight scope-create edits, exactly as the narrative family
        /// declares its chapter tree (P-010, P-006). A scope is not an assembly, so publishing creates for it would
        /// advance the composition counter with no matching assembly publication and the world could never rejoin.
        /// </summary>
        private static IReadOnlyList<ScopeRecord> DeclaredMarketScopes()
        {
            return new List<ScopeRecord>
            {
                Scope(CardVocabulary.TableArea, CardVocabulary.Match, 1, false),
                Scope(CardVocabulary.LeagueA, CardVocabulary.Match, 1, false),
                Scope(CardVocabulary.LeagueB, CardVocabulary.Match, 1, false),
                Scope(CardVocabulary.Spectators, CardVocabulary.Match, 1, false),
                Scope(CardVocabulary.SeatAScope, CardVocabulary.LeagueA, 2, false),
                Scope(CardVocabulary.SeatBScope, CardVocabulary.LeagueA, 2, false),
                Scope(CardVocabulary.SeatCScope, CardVocabulary.LeagueB, 2, false),
                // The practice seat is eligible and beneath the festival provider, but its isolation boundary
                // blocks the contribution in either mode (P-016, 07 s2.1).
                Scope(CardVocabulary.Practice, CardVocabulary.LeagueA, 2, true),
            };
        }

        private static ScopeRecord Scope(string scope, string parent, int depth, bool isolateAllCapabilities)
        {
            return new ScopeRecord(
                CardIdentity.Scope(scope),
                CardIdentity.Scope(parent),
                depth,
                new IsolationSet(false, null),
                new IsolationSet(isolateAllCapabilities, null),
                null,
                null);
        }

        /// <summary>Runs the GC-013 sequence against the committed generated catalog (GC-011 compiler output).</summary>
        public static Gc013ScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return Gc013Scenario.Run(new CardFamily(
                catalog,
                Declarations(),
                CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-013 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc013ScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Gc013Scenario.Run(new CardFamily(
                build.Catalog,
                Declarations(),
                CardCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="Gc013Scenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<Gc013Step> RunBoth(out Gc013ScenarioResult generated, out Gc013ScenarioResult fixture)
        {
            generated = RunGeneratedCatalog();
            fixture = RunFixtureCatalog();

            var combined = new List<Gc013Step>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                Gc013Step step = fixture.Steps[i];
                combined.Add(new Gc013Step(Gc013Scenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        /// <summary>
        /// The card declaration set one run mounts: the table runtime, both scoring providers, the rule library and the
        /// two draw-policy providers of step D. Every declaration resolves the card catalog's registered plugin
        /// factory and configuration schema (P-009).
        /// </summary>
        private static IReadOnlyList<CatalogPluginDeclaration> Declarations()
        {
            var declarations = new List<CatalogPluginDeclaration>(CardTableFixture.Declarations())
            {
                new CatalogPluginDeclaration(
                    DrawPolicyManifest(
                        CardTableKeys.PluginType(DrawPolicyOne),
                        DrawPolicyOne,
                        true,
                        CardVocabulary.FestivalBonus),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    DrawPolicyManifest(
                        CardTableKeys.PluginType(DrawPolicyTwo),
                        DrawPolicyTwo,
                        false,
                        CardVocabulary.QuietBonus),
                    ConfigDocument.Empty),
            };

            return declarations;
        }

        /// <summary>
        /// One provider of the exclusive pair. Exactly one of the two declares the capability contract: two differing
        /// declarations of one capability identity are a catalog error (P-019, 02 section 4), and identical ones are
        /// deduplicated, so the second provider declares the rule only.
        /// </summary>
        private static PluginManifest DrawPolicyManifest(
            PluginTypeId pluginType,
            string ruleStableName,
            bool declaresContract,
            int value)
        {
            return new PluginManifest(
                pluginType,
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                CardTableKeys.ConfigSchema,
                CardTableKeys.PluginFactoryKey,
                null,
                null,
                declaresContract
                    ? new List<CapabilityContract> { DrawPolicyContract() }
                    : new List<CapabilityContract>(),
                new List<DerivationRule> { DrawPolicyRule(ruleStableName, value) },
                null,
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// The `cards.draw-policy` contract: one output slot under the `Exclusive` policy and no reducer, because an
        /// exclusive slot has one member and folding several is what the policy refuses (P-019).
        /// </summary>
        private static CapabilityContract DrawPolicyContract()
        {
            return new CapabilityContract(
                CardIdentity.CapabilityRef(CardVocabulary.DrawPolicy),
                DrawPolicyStratum,
                new List<OutputSlotSchema> { new OutputSlotSchema(DrawPolicySlot, DrawPolicySchema) },
                new List<SlotCompositionPolicy>
                {
                    new SlotCompositionPolicy(DrawPolicySlot, CompositionPolicy.Exclusive, default(FactoryKey)),
                },
                null);
        }

        /// <summary>
        /// One draw-policy provider's rule. Its selector is the reusable seat recipe, exactly like a scoring rule's, so
        /// the two providers' candidates are the same seats and the pair really overlaps (P-013, P-015, P-019).
        /// </summary>
        private static DerivationRule DrawPolicyRule(string ruleStableName, int value)
        {
            return new DerivationRule(
                CardIdentity.Rule(ruleStableName + CardVocabulary.DrawPolicySuffix),
                CardIdentity.CapabilityRef(CardVocabulary.DrawPolicy),
                DrawPolicyStratum,
                1U,
                new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe) },
                CardVocabulary.AlwaysPredicateKey,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Exclusive,
                CardTableDeclarations.WriteInt32(value));
        }

        private sealed class CardFamily : IGc013Family
        {
            private readonly ICatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly SpawnRecipeCatalog recipes;
            private readonly MigrationRegistry migrations;
            private readonly IDerivationValueSource values;
            private readonly CardSeatApplier seatApplier = new CardSeatApplier();
            private readonly IReadOnlyList<CompositionEditPayload> spareScopes;
            private readonly IReadOnlyList<TargetId> automaticTargets;

            public CardFamily(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                string catalogFingerprint)
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
                CatalogFingerprint = catalogFingerprint ?? string.Empty;

                var tableApplier = new MarketTableApplier();
                var catalogRecipes = new List<SpawnRecipe>(CardTableRecipes.Catalog(seatApplier, tableApplier).Recipes);
                catalogRecipes.Add(OptedInSeat(seatApplier));
                recipes = new SpawnRecipeCatalog(catalogRecipes);

                migrations = new MigrationRegistry(new List<ISlotMigration>());
                values = CardDerivationValueSource.Default();

                spareScopes = new List<CompositionEditPayload>
                {
                    CardTablePayloads.ScopeCreate(SpareScopeA, CardMarketComposition.MatchScope, false),
                    CardTablePayloads.ScopeCreate(SpareScopeB, CardMarketComposition.MatchScope, false),
                };

                // The seats Automatic must reach with no import and no opt-in of their own (P-013). Seat A sits in the
                // branch the move relocates into the quiet league; seats B and C sit in the branches the move leaves
                // under the festival provider and under the quiet one respectively.
                automaticTargets = new List<TargetId>
                {
                    CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal),
                    CardTableFixture.SeatTarget(CardTableKeys.SeatBOrdinal),
                    CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal),
                };
            }

            public string Label => Gc013CardsHost.Label;

            public string CatalogFingerprint { get; }

            public ulong SessionSalt => Gc013CardsHost.SessionSalt;

            public Id128 Issuer => CardTableFixture.Issuer;

            public ICatalog Catalog => catalog;

            public IReadOnlyList<CatalogPluginDeclaration> Declarations => declarations;

            public ScopeId WorldRootScope => CardMarketComposition.MatchScope;

            /// <summary>
            /// The lane opens joined to the world's initial assembly with the market's declared tree attached, so the
            /// scope tree is part of the initial composition rather than eight publications with no assembly (P-006).
            /// </summary>
            public CompositionLaneSeed LaneSeed =>
                CompositionLaneSeed.InitialAssembly.WithScopes(DeclaredMarketScopes());

            /// <summary>The tree is declared, not published: no setup edit exists (P-010, P-006).</summary>
            public IReadOnlyList<CompositionEditPayload> SetupEdits => Array.Empty<CompositionEditPayload>();

            public IReadOnlyList<CompositionEditPayload> SpareScopeEdits => spareScopes;

            public PipelineDescriptorReport CompilePipeline()
            {
                var manifests = new List<PluginManifest>(declarations.Count);
                for (int i = 0; i < declarations.Count; i++)
                {
                    manifests.Add(declarations[i].Manifest);
                }

                return OwnershipSchedulePipeline.Build(
                    manifests, CardTableRegistration.DispatchKinds(), new SlotMigrationRegistry());
            }

            public WorldCreateRequest CreateRequest(WorldId world, OperationId operation)
                => CardTableRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty);

            public UnityWorldRegistration CreateRegistration(ScheduleAdaptation adaptation)
                => CardTableRegistration.Create(adaptation, CardTableRegistration.Systems());

            public SpawnRecipeCatalog CreateRecipes() => recipes;

            public MigrationRegistry CreateMigrations() => migrations;

            public IDerivationValueSource CreateValues() => values;

            public bool SeedTargets(Gc013WorldContext context)
            {
                EntityManager entityManager = context.Host.EntityWorld.EntityManager;

                Entity table = Seed(
                    context, CardIdentity.Target(CardVocabulary.TableOne), CardMarketComposition.TableScope,
                    CardTableKeys.MarketTableRecipe);
                CardTableAccess.InstallTableStorage(entityManager, table, CardTableKeys.SeededTableVersion);

                // The table's own bounded assignment of card identities (07 s2.2: "one authoritative assignment per
                // card"), exactly as the fixture seeds it, so the seeded world is the slice's declared market.
                DynamicBuffer<CardMarketRow> market = entityManager.GetBuffer<CardMarketRow>(table);
                DynamicBuffer<CardDeckRow> deck = entityManager.GetBuffer<CardDeckRow>(table);
                for (int i = 0; i < CardTableKeys.MarketCapacity; i++)
                {
                    market.Add(new CardMarketRow { Card = CardTableKeys.MarketCard(i).Value, Reserved = 0 });
                }

                for (int i = 0; i < CardTableKeys.DeckCapacity; i++)
                {
                    deck.Add(new CardDeckRow { Card = CardTableKeys.DeckCard(i).Value });
                }

                SeedSeat(context, CardTableKeys.SeatAOrdinal, CardVocabulary.SeatAScope);
                SeedSeat(context, CardTableKeys.SeatBOrdinal, CardVocabulary.SeatBScope);
                SeedSeat(context, CardTableKeys.SeatCOrdinal, CardVocabulary.SeatCScope);

                // The practice seat is eligible and beneath the festival provider, but its scope's isolation boundary
                // blocks the contribution in either mode (P-016), and the scoreboard view takes no card rule at all
                // (P-015): both are seeded as real targets so "they received nothing" is observable.
                Entity practice = Seed(
                    context,
                    CardIdentity.Target(CardVocabulary.PracticeSeat),
                    CardMarketComposition.PracticeScope,
                    CardTableKeys.SeatRecipe);
                CardTableAccess.InstallSeatStorage(
                    entityManager, practice, CardTableKeys.PracticeOrdinal, CardTableKeys.SeededSeatScore);
                Seed(
                    context,
                    CardIdentity.Target(CardVocabulary.Scoreboard),
                    CardMarketComposition.SpectatorScope,
                    CardTableKeys.ScoreboardViewRecipe);

                // Real gameplay state, not derived data: the moved seat's own live slot is seeded at the version its
                // descriptor declares, so no migration runs and its survival across the move is a statement about
                // state rather than about the derivation (P-025, P-032).
                if (!context.Seeder.TrySeedSlot(
                        CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal),
                        CardTableKeys.TableOwner,
                        CardTableKeys.SeatSlot,
                        CardTableKeys.SeatDomain.Version,
                        MutableStateValue,
                        out DiagnosticCode code,
                        out string detail))
                {
                    throw new InvalidOperationException(
                        "seeding the moved seat's live state slot was refused: " + code + ": " + detail);
                }

                return true;
            }

            public bool SeedOptedInTarget(Gc013WorldContext context)
            {
                Entity seat = Seed(context, OptedInTarget, FutureBranchScope, OptedInSeatRecipe);
                CardTableAccess.InstallSeatStorage(
                    context.Host.EntityWorld.EntityManager,
                    seat,
                    OptedInSeatOrdinal,
                    CardTableKeys.SeededSeatScore);
                return true;
            }

            public void PrepareSpawn()
            {
                // The seat applier installs the ordinal and the score it is told to, so a spawned seat is seated and
                // scores from the same base state every other seat does (04 section 6, P-024).
                seatApplier.NextOrdinal = FutureSeatOrdinal;
                seatApplier.InitialScore = CardTableKeys.SeededSeatScore;
            }

            public CompositionEditPayload MountProvider() => CardTablePayloads.Mount(
                declarations[1].Manifest,
                CardTableFixture.FestivalScoringInstance,
                CardIdentity.Scope(CardVocabulary.LeagueA));

            public CompositionEditPayload MountSecondProvider() => CardTablePayloads.Mount(
                declarations[2].Manifest,
                CardTableFixture.QuietScoringInstance,
                CardIdentity.Scope(CardVocabulary.LeagueB));

            public CompositionEditPayload ScopeReparent() => CardTablePayloads.ScopeReparent(
                CardIdentity.Scope(CardVocabulary.SeatAScope),
                CardIdentity.Scope(CardVocabulary.LeagueB));

            public CompositionEditPayload ModeSet(PropagationMode mode) => CardTablePayloads.ModeSet(mode);

            public CompositionEditPayload MountConflictProvider() => CardTablePayloads.Mount(
                declarations[4].Manifest,
                DrawPolicyOneInstance,
                DrawPolicyOneScope);

            public CompositionEditPayload MountConflictSecondProvider() => CardTablePayloads.Mount(
                declarations[5].Manifest,
                DrawPolicyTwoInstance,
                DrawPolicyTwoScope);

            public ScopeId ProviderScope => CardIdentity.Scope(CardVocabulary.LeagueA);

            public ScopeId MovedScope => CardIdentity.Scope(CardVocabulary.SeatAScope);

            public ScopeId MoveDestination => CardIdentity.Scope(CardVocabulary.LeagueB);

            public PluginInstanceId SecondProviderInstance => CardTableFixture.QuietScoringInstance;

            public PluginInstanceId ConflictProviderInstance => Gc013CardsHost.DrawPolicyOneInstance;

            public PluginInstanceId ConflictSecondProviderInstance => Gc013CardsHost.DrawPolicyTwoInstance;

            public ScopeId ConflictProviderScope => Gc013CardsHost.DrawPolicyOneScope;

            public ScopeId ConflictSecondProviderScope => Gc013CardsHost.DrawPolicyTwoScope;

            public CapabilityId DerivedCapability => CardVocabulary.SetBonusCapability;

            public CapabilityId ConflictCapability => Gc013CardsHost.DrawPolicyCapability;

            public int ProviderValue => CardVocabulary.FestivalBonus;

            public int SecondProviderValue => CardVocabulary.QuietBonus;

            public IReadOnlyList<TargetId> AutomaticTargets => automaticTargets;

            public TargetId MovedTarget => CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);

            public TargetId IsolatedTarget => CardIdentity.Target(CardVocabulary.PracticeSeat);

            public TargetId IneligibleTarget => CardIdentity.Target(CardVocabulary.Scoreboard);

            public TargetId OptedInTarget => Gc013CardsHost.OptedInTarget;

            public TargetId FutureTarget => CardTableFixture.SeatTarget(CardTableKeys.SeatCOrdinal + 1U);

            public DefinitionRef FutureRecipe => CardTableKeys.SeatRecipe;

            public ScopeId FutureScope => FutureBranchScope;

            public OwnerId MutableOwner => CardTableKeys.TableOwner;

            public SlotId MutableSlot => CardTableKeys.SeatSlot;

            public int MutableValue => MutableStateValue;

            /// <summary>
            /// The opted-in seat's recipe: the seat recipe's own selector schema under a distinct definition identity,
            /// with a complete explicit opt-in naming the festival installation (P-013).
            /// </summary>
            private static SpawnRecipe OptedInSeat(ISpawnApplier applier)
            {
                var schemas = new List<SchemaRef>
                {
                    CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe),
                    CardTableKeys.SeatDomain,
                };

                var descriptor = new TargetDescriptor(
                    OptedInSeatRecipe,
                    schemas,
                    null,
                    new List<Id128> { CardIdentity.Id(CardVocabulary.CardSeatRecipe) },
                    default(AssetAdapterDescriptor),
                    null,
                    null,
                    new List<TargetOptIn>
                    {
                        new TargetOptIn(
                            new ProviderInstallationId(CardTableFixture.FestivalScoringInstance.Value),
                            CardVocabulary.SetBonusCapability),
                    },
                    null);

                return new SpawnRecipe(OptedInSeatRecipe, descriptor, schemas, applier);
            }

            private static void SeedSeat(Gc013WorldContext context, uint ordinal, string scopeStableName)
            {
                Entity seat = Seed(
                    context,
                    CardTableFixture.SeatTarget(ordinal),
                    CardIdentity.Scope(scopeStableName),
                    CardTableKeys.SeatRecipe);

                EntityManager entityManager = context.Host.EntityWorld.EntityManager;
                CardTableAccess.InstallSeatStorage(
                    entityManager, seat, ordinal, CardTableKeys.SeededSeatScore);
                for (int i = 0; i < CardTableKeys.SeededHandCount; i++)
                {
                    CardTableAccess.TryAddHeldCard(entityManager, seat, CardTableKeys.SeatCard(ordinal, i));
                }
            }

            private static Entity Seed(Gc013WorldContext context, TargetId target, ScopeId scope, DefinitionRef recipe)
            {
                if (!context.Seeder.TrySeed(
                        target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was refused: " + code + ": " + detail);
                }

                if (!context.Seeder.TryGetEntity(target, out Entity entity))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was created but the registry cannot resolve it (P-005).");
                }

                return entity;
            }
        }
    }
}
