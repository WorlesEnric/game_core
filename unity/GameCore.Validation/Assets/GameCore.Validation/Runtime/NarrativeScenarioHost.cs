#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Fixtures;
using GameCore.Validation.Generated;
using GameCore.Validation.Probe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-010 narrative vertical slice host of the qualification project: it supplies the two catalogs the narrative
    /// scenario is run against and hands the resulting observations to the caller.
    ///
    ///  * the **generated** catalog is the committed compiler output
    ///    (`Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`, GC-003), validated by the production
    ///    `ImmutableCatalog` — the strongest available proof that the slice runs over real generated registrations;
    ///  * the **fixture** catalog is the hand-written generated-style table in `GameCore.Unity.Fixtures`, which is
    ///    the same shape the compiler emits and needs no build step.
    ///
    /// Both runs share one implementation (<c>NarrativeScenario</c>), so the EditMode test and the player probe
    /// exercise identical scenarios. Each run tears its own world down and leaves the owned-world registry as it
    /// found it.
    /// </summary>
    public static class NarrativeScenarioHost
    {
        /// <summary>Step-name prefix of the run over the hand-written generated-style catalog.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Runs the narrative slice against the committed generated catalog (GC-003 compiler output).</summary>
        public static NarrativeScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: " + build.Describe());
            }

            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not a canonical content hash.");
            }

            // Both chapter providers mount through the same precompiled factory the committed catalog registers; the
            // plugin types differ because one catalog declaration resolves one manifest (P-009, 04 section 8).
            PluginManifest chapterOne = NarrativeDeclarations.ChapterProvider(
                NarrativeKeys.PluginTypeId(1UL),
                ProbeCatalog.FixturePluginKey,
                W1GateKeys.CatalogSchema);
            PluginManifest chapterTwo = NarrativeDeclarations.ChapterTwoProvider(
                NarrativeKeys.PluginTypeId(2UL),
                ProbeCatalog.FixturePluginKey,
                W1GateKeys.CatalogSchema);

            var declarations = new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(chapterOne, ConfigDocument.Empty),
                new CatalogPluginDeclaration(chapterTwo, ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ForwardProvider(
                        NarrativeKeys.PluginTypeId(4UL),
                        ProbeCatalog.FixturePluginKey,
                        W1GateKeys.CatalogSchema),
                    ConfigDocument.Empty),
            };

            return NarrativeScenario.Run(
                build.Catalog,
                declarations,
                ProbeKeys.AbsentFixturePluginKey,
                NarrativeKeys.PluginTypeId(3UL),
                emittedFingerprint);
        }

        /// <summary>Runs the narrative slice against the hand-written generated-style catalog in the fixture package.</summary>
        public static NarrativeScenarioResult RunFixtureCatalog() => NarrativeScenario.RunFixtureCatalog();

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names,
        /// and the fixture-catalog steps are prefixed with <see cref="FixtureRunPrefix"/> so no two steps collide.
        /// </summary>
        public static IReadOnlyList<NarrativeStep> RunBoth(
            out NarrativeFacts generatedFacts,
            out NarrativeFacts fixtureFacts)
        {
            NarrativeScenarioResult generated = RunGeneratedCatalog();
            generatedFacts = generated.Facts;

            NarrativeScenarioResult fixture = RunFixtureCatalog();
            fixtureFacts = fixture.Facts;

            var combined = new List<NarrativeStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                NarrativeStep step = fixture.Steps[i];
                combined.Add(new NarrativeStep(FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
