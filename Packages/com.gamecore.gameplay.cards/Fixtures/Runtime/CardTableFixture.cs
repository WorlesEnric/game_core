// GameCore.Gameplay.Cards.Fixtures — the card market's fixture: its declared plugin set and its seeded world.
//
// Normative sources: 07 s2.1 (the market's composition and its two scoring providers), P-009 (a mount resolves a
// manifest the catalog registered), P-032/P-034 (live authoritative state belongs to its owner's slots) and P-024
// (a target first becomes visible with its complete effective assembly).
//
// This file holds the generated-style declaration set the composition lane mounts and the ECS seeding of the
// targets that set is derived for. It contains no rule of its own: every value it produces comes either from
// `CardTableDeclarations` (the card plugin's real declarations) or from the real kernel.
#nullable enable
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime.Integration;
using Unity.Entities;

namespace GameCore.Gameplay.Cards.Fixtures
{
    /// <summary>The card market's declarations: one table runtime, two scoring providers and one rule library.</summary>
    public static class CardTableFixture
    {
        /// <summary>Issuer of every operation a card scenario mints; a stable identity, never a timing value (P-050).</summary>
        public static readonly Id128 Issuer = CardTableKeys.Issuer;

        /// <summary>Declared plugin type of the table runtime.</summary>
        public static PluginTypeId TableRuntimeType => CardTableKeys.PluginType("cards.table-runtime");

        /// <summary>Declared plugin type of the festival scoring provider.</summary>
        public static PluginTypeId FestivalScoringType => CardTableKeys.PluginType(CardVocabulary.FestivalScoring);

        /// <summary>Declared plugin type of the quiet scoring provider.</summary>
        public static PluginTypeId QuietScoringType => CardTableKeys.PluginType(CardVocabulary.QuietScoring);

        /// <summary>Declared plugin type of the rule library.</summary>
        public static PluginTypeId RuleLibraryType => CardTableKeys.PluginType(CardVocabulary.CardRuleLibrary);

        /// <summary>Mount identity of the table runtime.</summary>
        public static PluginInstanceId TableRuntimeInstance => CardTableKeys.Instance("cards.table-runtime");

        /// <summary>Mount identity of the festival scoring provider.</summary>
        public static PluginInstanceId FestivalScoringInstance => CardTableKeys.Instance(CardVocabulary.FestivalScoring);

        /// <summary>Mount identity of the quiet scoring provider.</summary>
        public static PluginInstanceId QuietScoringInstance => CardTableKeys.Instance(CardVocabulary.QuietScoring);

        /// <summary>Mount identity of the rule library.</summary>
        public static PluginInstanceId RuleLibraryInstance => CardTableKeys.Instance(CardVocabulary.CardRuleLibrary);

        /// <summary>
        /// The table runtime's declaration: the four settlement stages, its five owned slots and its declared step
        /// buffer. It declares its rule-library dependency once, in its manifest (07 s2.1), so no seat needs to
        /// import the lookup service to be eligible for scoring.
        /// </summary>
        public static CatalogPluginDeclaration TableRuntimeDeclaration() =>
            new CatalogPluginDeclaration(
                CardTableDeclarations.TableRuntime(
                    TableRuntimeType,
                    CardTableKeys.PluginFactoryKey,
                    CardTableKeys.ConfigSchema,
                    null,
                    new List<ServiceDependency>
                    {
                        new ServiceDependency(
                            CardTableKeys.LookupContract,
                            new VersionRange(1U, 1U),
                            false,
                            ServiceResolutionDomain.AncestorsAndSelf,
                            default(ProviderInstallationId),
                            default(FactoryKey)),
                    }),
                ConfigDocument.Empty);

        /// <summary>One scoring provider's declaration at the given bonus (07 s2.1).</summary>
        public static CatalogPluginDeclaration ScoringDeclaration(bool festival)
        {
            string install = festival ? CardVocabulary.FestivalScoring : CardVocabulary.QuietScoring;
            int bonus = festival ? CardVocabulary.FestivalBonus : CardVocabulary.QuietBonus;
            return new CatalogPluginDeclaration(
                CardTableDeclarations.ScoringProvider(
                    festival ? FestivalScoringType : QuietScoringType,
                    CardTableKeys.PluginFactoryKey,
                    CardTableKeys.ConfigSchema,
                    install + CardVocabulary.SetBonusSuffix,
                    bonus),
                ConfigDocument.Empty);
        }

        /// <summary>The rule library's declaration: it exports the definition-lookup service and owns no state.</summary>
        public static CatalogPluginDeclaration RuleLibraryDeclaration() =>
            new CatalogPluginDeclaration(
                CardTableDeclarations.RuleLibrary(
                    RuleLibraryType,
                    CardTableKeys.PluginFactoryKey,
                    CardTableKeys.ConfigSchema),
                ConfigDocument.Empty);

        /// <summary>
        /// The generated-style declaration set of one card world: the table runtime, both scoring providers and the
        /// rule library. Every declaration resolves the card catalog's registered plugin factory and its registered
        /// configuration schema (P-009).
        /// </summary>
        public static IReadOnlyList<CatalogPluginDeclaration> Declarations() =>
            new List<CatalogPluginDeclaration>
            {
                TableRuntimeDeclaration(),
                ScoringDeclaration(true),
                ScoringDeclaration(false),
                RuleLibraryDeclaration(),
            };

        /// <summary>The five declarations as manifests, in declaration order (the pipeline's own input).</summary>
        public static IReadOnlyList<PluginManifest> Manifests()
        {
            IReadOnlyList<CatalogPluginDeclaration> declarations = Declarations();
            var manifests = new List<PluginManifest>(declarations.Count);
            for (int i = 0; i < declarations.Count; i++)
            {
                manifests.Add(declarations[i].Manifest);
            }

            return manifests;
        }

        /// <summary>
        /// Seeds one target and installs its recipe's card base layout into real ECS storage. The seeder creates
        /// the entity, its identity, its published stamp and its empty published buffers; the install below adds
        /// this package's own components and buffers, which is where the table's authoritative state lives (P-032).
        /// </summary>
        public static bool SeedTarget(
            LiveTargetSeeder seeder,
            CardTableModule module,
            TargetId target,
            ScopeId scope,
            DefinitionRef recipe,
            out DiagnosticCode code,
            out string detail,
            out Entity entity)
        {
            entity = Entity.Null;
            if (!seeder.TrySeed(target, scope, recipe, out TargetHandle _, out code, out detail))
            {
                return false;
            }

            if (!seeder.TryGetEntity(target, out entity))
            {
                code = DiagnosticCode.StaleHandle;
                detail = "the seeder created " + target.ToString() + " but the registry cannot resolve it (P-005).";
                return false;
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// Seeds the table entity of `table-1` with its market rows, then its seats, its scoreboard and its
        /// practice seat. The market and deck rows are the table's own bounded assignment of card identities
        /// (07 s2.2: "one authoritative assignment per card").
        /// </summary>
        public static bool SeedMarket(
            LiveTargetSeeder seeder,
            CardTableModule module,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (!SeedTarget(
                    seeder,
                    module,
                    CardIdentity.Target(CardVocabulary.TableOne),
                    CardMarketComposition.TableScope,
                    CardTableKeys.MarketTableRecipe,
                    out code,
                    out detail,
                    out Entity table))
            {
                return false;
            }

            module.BindTable(table);

            // The seeder created the entity and its published buffers; the recipe's own base layout is this
            // package's card storage, which the fixture installs here (04 s6: a generated applier installs the
            // recipe's base components, and nothing else).
            EntityManager entityManager = module.Host.EntityWorld.EntityManager;
            CardTableAccess.InstallTableStorage(entityManager, table, CardTableKeys.SeededTableVersion);
            DynamicBuffer<CardMarketRow> market = entityManager.GetBuffer<CardMarketRow>(table);
            DynamicBuffer<CardDeckRow> deck = entityManager.GetBuffer<CardDeckRow>(table);
            for (int i = 0; i < CardMarketCapacity; i++)
            {
                market.Add(new CardMarketRow { Card = CardTableKeys.MarketCard(i).Value, Reserved = 0 });
            }

            for (int i = 0; i < CardTableKeys.DeckCapacity; i++)
            {
                deck.Add(new CardDeckRow { Card = CardTableKeys.DeckCard(i).Value });
            }

            if (!SeedSeat(seeder, module, CardTableKeys.SeatAOrdinal, CardVocabulary.SeatAScope, out code, out detail))
            {
                return false;
            }

            if (!SeedSeat(seeder, module, CardTableKeys.SeatBOrdinal, CardVocabulary.SeatBScope, out code, out detail))
            {
                return false;
            }

            if (!SeedSeat(seeder, module, CardTableKeys.SeatCOrdinal, CardVocabulary.SeatCScope, out code, out detail))
            {
                return false;
            }

            // The practice seat is eligible and beneath the festival provider, but its scope's isolation boundary
            // blocks the contribution (P-016).
            var practiceScope = CardMarketComposition.PracticeScope;
            if (!SeedTarget(
                    seeder,
                    module,
                    CardIdentity.Target(CardVocabulary.PracticeSeat),
                    practiceScope,
                    CardTableKeys.SeatRecipe,
                    out code,
                    out detail,
                    out Entity practice))
            {
                return false;
            }

            CardTableAccess.InstallSeatStorage(
                entityManager, practice, CardTableKeys.PracticeOrdinal, CardTableKeys.SeededSeatScore);
            module.BindSeat(CardTableKeys.PracticeOrdinal, practice);

            // The scoreboard view declares neither card schema, so no card rule selects it: it keeps its own base
            // layout (the seeder's published buffers and nothing else) and gains no derived row (P-015's
            // ineligible case). It is deliberately given no card storage at all, so "it received nothing" is a
            // structural fact rather than an empty-buffer reading.
            return SeedTarget(
                seeder,
                module,
                CardIdentity.Target(CardVocabulary.Scoreboard),
                CardMarketComposition.SpectatorScope,
                CardTableKeys.ScoreboardViewRecipe,
                out code,
                out detail,
                out Entity _);
        }

        /// <summary>Seeds one seat with its ordinal, its seeded score and its seeded hand (07 s2.3's example).</summary>
        public static bool SeedSeat(
            LiveTargetSeeder seeder,
            CardTableModule module,
            uint ordinal,
            string scopeStableName,
            out DiagnosticCode code,
            out string detail)
        {
            TargetId target = SeatTarget(ordinal);
            if (!SeedTarget(
                    seeder,
                    module,
                    target,
                    CardIdentity.Scope(scopeStableName),
                    CardTableKeys.SeatRecipe,
                    out code,
                    out detail,
                    out Entity seat))
            {
                return false;
            }

            EntityManager entityManager = module.Host.EntityWorld.EntityManager;
            CardTableAccess.InstallSeatStorage(entityManager, seat, ordinal, CardTableKeys.SeededSeatScore);
            for (int i = 0; i < CardTableKeys.SeededHandCount; i++)
            {
                CardTableAccess.TryAddHeldCard(entityManager, seat, CardTableKeys.SeatCard(ordinal, i));
            }

            module.BindSeat(ordinal, seat);
            return true;
        }

        /// <summary>The stable target identity of one market seat (07 s2.2's `seat-a`, `seat-b`, `seat-c`).</summary>
        public static TargetId SeatTarget(uint seatOrdinal)
        {
            switch (seatOrdinal)
            {
                case CardTableKeys.SeatAOrdinal:
                    return CardIdentity.Target(CardVocabulary.SeatA);
                case CardTableKeys.SeatBOrdinal:
                    return CardIdentity.Target(CardVocabulary.SeatB);
                case CardTableKeys.SeatCOrdinal:
                    return CardIdentity.Target(CardVocabulary.SeatC);
                default:
                    return CardIdentity.Target(CardVocabulary.SeatD);
            }
        }

        /// <summary>Market rows the seeded table holds, so a total-card count is reproducible.</summary>
        public const int CardMarketCapacity = CardTableKeys.MarketCapacity;

        /// <summary>The full mount sequence of the market, in the order the control lane accepts it (P-010).</summary>
        public static IReadOnlyList<CompositionEditPayload> MarketMounts(bool mountFestival, bool mountQuiet)
        {
            var payloads = new List<CompositionEditPayload>();
            if (mountFestival)
            {
                payloads.Add(CardTablePayloads.Mount(
                    ScoringDeclaration(true).Manifest,
                    FestivalScoringInstance,
                    CardIdentity.Scope(CardVocabulary.LeagueA)));
            }

            if (mountQuiet)
            {
                payloads.Add(CardTablePayloads.Mount(
                    ScoringDeclaration(false).Manifest,
                    QuietScoringInstance,
                    CardIdentity.Scope(CardVocabulary.LeagueB)));
            }

            return payloads;
        }
    }
}
