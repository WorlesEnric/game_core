#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.Probe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-011 card-slice host of the qualification project: it supplies the two catalogs the card scenario is run
    /// against and hands the resulting observations to the caller.
    ///
    ///  * the **generated** catalog is the committed compiler output
    ///    (`Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs`, GC-011), validated by the production
    ///    `ImmutableCatalog` — the strongest available proof that the slice runs over real generated registrations;
    ///  * the **fixture** catalog is the hand-written generated-style table in `GameCore.Gameplay.Cards.Fixtures`,
    ///    which is the same shape the compiler emits and needs no build step.
    ///
    /// Both runs share one implementation (<c>CardMarketScenario</c>), so the EditMode test and the player probe
    /// exercise identical scenarios. Each run tears its own world down and leaves the owned-world registry as it
    /// found it, which is what lets the two runs share one process without sharing host state.
    /// </summary>
    public static class CardsScenarioHost
    {
        /// <summary>Step-name prefix of the run over the hand-written generated-style catalog.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// Plugin type that must stay unregistered in both catalogs, so the P-009 miss stays observable. The
        /// fixture's own absent type is `cards.absent-plugin.type`; this host declares the same stable name through
        /// the gameplay package's own derivation instead of reaching into the fixture's private constants.
        /// </summary>
        public static readonly PluginTypeId AbsentCardsPluginType = CardTableKeys.PluginType("cards.absent-plugin");

        /// <summary>Runs the slice against the committed generated catalog (GC-011 compiler output).</summary>
        public static CardScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not a canonical content hash.");
            }

            // The declarations are the fixture's generated-style set: the table runtime, both scoring providers and
            // the rule library, every one of them resolving the factory key and configuration schema the committed
            // catalog registers (P-009, 04 section 8). The absent factory key is the probe fixture's registered
            // absent plugin, which is deliberately not part of the card catalog.
            return CardMarketScenario.Run(
                build.Catalog,
                CardTableFixture.Declarations(),
                ProbeKeys.AbsentFixturePluginKey,
                AbsentCardsPluginType,
                emittedFingerprint);
        }

        /// <summary>Runs the slice against the hand-written generated-style catalog in the fixture package.</summary>
        public static CardScenarioResult RunFixtureCatalog() => CardMarketScenario.RunFixtureCatalog();

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names,
        /// and the fixture-catalog steps are prefixed with <see cref="FixtureRunPrefix"/> so no two steps collide.
        /// </summary>
        public static IReadOnlyList<CardStep> RunBoth(out CardFacts generatedFacts, out CardFacts fixtureFacts)
        {
            CardScenarioResult generated = RunGeneratedCatalog();
            generatedFacts = generated.Facts;

            CardScenarioResult fixture = RunFixtureCatalog();
            fixtureFacts = fixture.Facts;

            var combined = new List<CardStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                CardStep step = fixture.Steps[i];
                combined.Add(new CardStep(FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
