// GameCore.Validation.ProbeHost — the GC-017 fault scenario host.
//
// TEST-016 rows this file serves: the whole matrix, because it is the entry point the four observations of a
// family run are reached through, over both the committed generated catalog and the family's hand-written
// generated-style catalog.
//
// Normative anchors: P-002 (the host is the sole world authority, so each run stands its own world up through the
// registry), P-028 (the catalog fingerprint a run derived over is named, not implied), P-035 (a created world is
// `Running` only after its initial validated publication), P-047/P-048 (each run's worlds are the caller's to
// settle and dispose), P-049 (recovery is exercised inside the same run, from a world the fault left terminal).
//
// `FaultScenarioHost` builds the families exactly as `W4GateNarrativeHost` / `W4GateCardsHost` do: the same
// `ProbeCatalog.BuildVerifiedCatalog(out _)` / `CardCatalog.BuildVerifiedCatalog(out _)` fingerprint check, the same
// declaration sets (`Gc013NarrativeHost.Declarations(...)` plus the state-policy provider, `Gc013CardsHost.
// Declarations()`), and the same fixture catalogs (`NarrativeScenarioCatalog.Build()` / `CardCatalogTable.Build()`).
// It deliberately adds no sixth declaration and no synthetic catalog: a fault observation is only worth its name if
// it ran over the same revision the other Wave gates run over.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The two families' catalog entry points for the GC-017 fault sequence, in the shape every other host of this
    /// project exposes: one run per catalog, and two `out`-pair helpers that combine them with the fixture-catalog
    /// steps prefixed so no observation name collides.
    /// </summary>
    public static class FaultScenarioHost
    {
        /// <summary>Runs the fault sequence for the narrative family over the committed generated catalog.</summary>
        public static FaultScenarioResult RunNarrativeGenerated()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: " + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return FaultScenario.Run(new Gc013NarrativeHost.NarrativeFamily(
                catalog,
                NarrativeDeclarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the fault sequence for the narrative family over the hand-written fixture catalog.</summary>
        public static FaultScenarioResult RunNarrativeFixture()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return FaultScenario.Run(new Gc013NarrativeHost.NarrativeFamily(
                build.Catalog,
                NarrativeDeclarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                NarrativeScenarioCatalog.Fingerprint().ToHex()));
        }

        /// <summary>Runs the fault sequence for the card family over the committed generated catalog.</summary>
        public static FaultScenarioResult RunCardsGenerated()
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

            return FaultScenario.Run(new Gc013CardsHost.CardFamily(
                catalog,
                Gc013CardsHost.Declarations(),
                CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the fault sequence for the card family over the hand-written fixture catalog.</summary>
        public static FaultScenarioResult RunCardsFixture()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return FaultScenario.Run(new Gc013CardsHost.CardFamily(
                build.Catalog,
                Gc013CardsHost.Declarations(),
                CardCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both narrative catalogs and returns the combined observations: the generated-catalog steps keep
        /// their names and the fixture-catalog steps carry <see cref="FaultScenario.FixtureRunPrefix"/>, exactly as
        /// the W4 gate combines its two runs.
        /// </summary>
        public static IReadOnlyList<FaultScenarioStep> RunNarrative(
            out FaultScenarioResult generated,
            out FaultScenarioResult fixture)
        {
            generated = RunNarrativeGenerated();
            fixture = RunNarrativeFixture();
            return Combine(generated, fixture);
        }

        /// <summary>Runs both card catalogs and returns the combined observations.</summary>
        public static IReadOnlyList<FaultScenarioStep> RunCards(
            out FaultScenarioResult generated,
            out FaultScenarioResult fixture)
        {
            generated = RunCardsGenerated();
            fixture = RunCardsFixture();
            return Combine(generated, fixture);
        }

        /// <summary>
        /// One run's narrative declaration set: the GC-013 half plus the state-policy provider, whose declared slots
        /// must belong to the compiled ownership surface of the revision the fault run's migration observation acts
        /// on (P-009, P-032). It mirrors `W4GateNarrativeHost`'s own set, which is private to that type.
        /// </summary>
        private static IReadOnlyList<CatalogPluginDeclaration> NarrativeDeclarations(
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            var declarations = new List<CatalogPluginDeclaration>(
                Gc013NarrativeHost.Declarations(factoryKey, configSchema));
            declarations.Add(new CatalogPluginDeclaration(
                W4GateNarrativeHost.StatePolicyProviderManifest(
                    W4GateNarrativeHost.StatePolicyPluginType, factoryKey, configSchema),
                ConfigDocument.Empty));
            return declarations;
        }

        private static IReadOnlyList<FaultScenarioStep> Combine(
            FaultScenarioResult generated,
            FaultScenarioResult fixture)
        {
            var combined = new List<FaultScenarioStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                FaultScenarioStep step = fixture.Steps[i];
                combined.Add(new FaultScenarioStep(
                    FaultScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
