#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Unity.Fixtures;
using GameCore.Validation.Generated;
using GameCore.Validation.Probe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// W1 integration gate host of the qualification project: it supplies the two catalogs the gate scenario is run
    /// against and hands the resulting observations to the caller.
    ///
    ///  * the **generated** catalog is the committed compiler output
    ///    (`Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`, GC-003), validated by the production
    ///    `ImmutableCatalog` — the strongest available proof that the gate runs over real generated registrations;
    ///  * the **fixture** catalog is the hand-written generated-style table in `GameCore.Unity.Fixtures`, which is
    ///    the same shape the compiler emits and needs no build step.
    ///
    /// Both runs share one implementation (<c>W1GateScenario</c>), so the EditMode test and the player probe
    /// exercise identical scenarios.
    /// </summary>
    public static class W1GateScenarioHost
    {
        /// <summary>Step-name prefix of the run over the hand-written generated-style catalog.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Runs the gate against the committed generated catalog (GC-003 compiler output).</summary>
        public static W1GateScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not a canonical content hash.");
            }

            var declarations = new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(
                    W1GateManifests.Plain(
                        W1GateKeys.PluginType,
                        ProbeCatalog.FixturePluginKey,
                        W1GateKeys.CatalogSchema),
                    ConfigDocument.Empty),
            };

            // The scenario's own first check compares the rebuilt catalog against the emitted literal (P-028), so a
            // stale generated file fails the gate instead of driving it silently.
            return W1GateScenario.Run(
                build.Catalog,
                declarations,
                ProbeKeys.AbsentFixturePluginKey,
                W1GateKeys.AbsentPluginType,
                emittedFingerprint);
        }

        /// <summary>Runs the gate against the hand-written generated-style catalog in the fixture package.</summary>
        public static W1GateScenarioResult RunFixtureCatalog() => W1GateScenario.RunFixtureCatalog();

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names,
        /// and the fixture-catalog steps are prefixed with <see cref="FixtureRunPrefix"/> so no two steps collide.
        /// </summary>
        public static IReadOnlyList<W1GateStep> RunBoth(out W1GateFacts generatedFacts, out W1GateFacts fixtureFacts)
        {
            W1GateScenarioResult generated = RunGeneratedCatalog();
            generatedFacts = generated.Facts;

            W1GateScenarioResult fixture = RunFixtureCatalog();
            fixtureFacts = fixture.Facts;

            var combined = new List<W1GateStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                W1GateStep step = fixture.Steps[i];
                combined.Add(new W1GateStep(FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
