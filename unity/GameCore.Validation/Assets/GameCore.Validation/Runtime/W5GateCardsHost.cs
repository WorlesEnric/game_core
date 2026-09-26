// GameCore.Validation.ProbeHost - the Wave 5 gate's card family adapter.
//
// `W5GateFamily.cs` defines what the gate requires from one genre (the checkpoint contract and the adapter contract
// at once); `Gc013CardsHost.CardFamily` already implements both halves in its other partial parts. This part adds
// exactly two things and nothing else:
//
//   * the declaration that the card family satisfies `IW5GateFamily`, so a family that lost one of the two contracts
//     fails to compile rather than at run time;
//   * the two catalog entry points the Wave 5 probe and the EditMode suite call, mirroring
//     `RunGeneratedCatalogGc018`/`RunGc019GeneratedCatalog` so the gate runs the *same* card market the earlier gates
//     run - the committed generated catalog and the hand-written generated-style catalog, nothing new.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        /// <summary>The Wave 5 gate half of the card family: it satisfies both gate contracts (P-001).</summary>
        public sealed partial class CardFamily : IW5GateFamily
        {
        }

        /// <summary>Runs the Wave 5 gate against the committed generated catalog (GC-011 compiler output).</summary>
        public static W5GateScenarioResult RunGeneratedCatalogW5Gate()
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

            return W5GateScenario.Run(new CardFamily(catalog, Declarations(), CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the Wave 5 gate against the hand-written card catalog in the fixture package.</summary>
        public static W5GateScenarioResult RunFixtureCatalogW5Gate()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return W5GateScenario.Run(new CardFamily(build.Catalog, Declarations(), CardCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="W5GateScenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<W5GateStep> RunBothW5Gate(
            out W5GateScenarioResult generated,
            out W5GateScenarioResult fixture)
        {
            generated = RunGeneratedCatalogW5Gate();
            fixture = RunFixtureCatalogW5Gate();

            var combined = new List<W5GateStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                W5GateStep step = fixture.Steps[i];
                combined.Add(new W5GateStep(W5GateScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
