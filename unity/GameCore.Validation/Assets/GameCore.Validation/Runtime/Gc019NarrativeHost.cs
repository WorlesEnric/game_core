// GameCore.Validation.ProbeHost — the GC-019 narrative family adapter.
//
// `Gc013NarrativeHost.NarrativeFamily` already declares the whole narrative slice as an `IGc013Family`: its catalog
// declarations, its chapter scope tree, its live villagers, the chapter provider it mounts and every composition
// edit the GC-013 sequence submits. The Wave 4 gate adds the lifecycle and slot-policy half of the same class in
// `W4GateNarrativeHost.cs`. This file adds the *GC-019* half — the input/asset/presentation surface the adapters
// need — as the third partial part of the same class, precisely so that the adapter gate runs the same narrative
// world the earlier gates run instead of a narrative world built for adapters (P-001).
//
// Everything it declares is the genre's own data:
//
//   * the choice route, the villager it addresses and the choice command schema are `NarrativeKeys`' generated
//     identities, i.e. exactly what `NarrativeRegistration.Messages()` registers and `NarrativeWorld` decodes;
//   * the payload is `NarrativePayloadCodec.EncodeChoice`, the same two-scalar encoding `NarrativeChoicePayloadReader`
//     reads and the narrative fixture's own scenario submits;
//   * the dialogue node ordinal is the value this family seeds into the addressed villager's conversation node slot,
//     so `NarrativeDialogueRules.Validate` sees a choice taken at the node the conversation really sits on and the
//     command is *accepted* by gameplay rather than merely admitted by the host;
//   * the view targets are the family's own automatically eligible targets, i.e. the villagers its chapter provider
//     really derived a dialogue binding into.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Fixtures;
using GameCore.Validation.Generated;
using GameCore.Validation.Probe;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013NarrativeHost
    {
        /// <summary>Runs the GC-019 sequence against the committed generated catalog (GC-003 compiler output).</summary>
        public static Gc019ScenarioResult RunGc019GeneratedCatalog()
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

            return Gc019Scenario.Run(new NarrativeFamily(
                catalog,
                Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-019 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc019ScenarioResult RunGc019FixtureCatalog()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return Gc019Scenario.Run(new NarrativeFamily(
                build.Catalog,
                Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                NarrativeScenarioCatalog.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="Gc019Scenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<Gc019Step> RunBothGc019(
            out Gc019ScenarioResult generated,
            out Gc019ScenarioResult fixture)
        {
            generated = RunGc019GeneratedCatalog();
            fixture = RunGc019FixtureCatalog();

            var combined = new List<Gc019Step>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                Gc019Step step = fixture.Steps[i];
                combined.Add(new Gc019Step(Gc019Scenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        /// <summary>
        /// The adapter-gate half of the narrative family: the choice identity its own world registers, the payload its
        /// own reader decodes and the villagers its own provider derived into.
        /// </summary>
        public sealed partial class NarrativeFamily : IGc019Family
        {
            /// <summary>
            /// Dialogue node ordinal this family's choice is taken at: the conversation node the family's own
            /// `SeedTargets` writes into the addressed villager, i.e. the node the live conversation really sits on.
            /// A choice naming any other node is refused by `NarrativeDialogueRules.Validate` (07 s3.2).
            /// </summary>
            private const int DeclaredChoiceNodeOrdinal = MutableStateValue;

            public RouteId CommandRoute => NarrativeKeys.ChoiceRoute;

            public TargetId CommandTarget => NarrativeKeys.Mara;

            public SchemaRef CommandSchema => NarrativeKeys.ChoiceCommandSchema;

            /// <summary>
            /// The choice payload the narrative input stage decodes: one dialogue node ordinal and one choice ordinal,
            /// both big-endian int32 (`NarrativePayloadCodec`, 05 s6). The node is the family's declared node and the
            /// choice ordinal is the declared scalar, so the runner can submit the family's permit or decline choice
            /// without knowing the graph.
            /// </summary>
            public FrozenPayload CommandPayload(int value) =>
                new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                    new NarrativeChoice(DeclaredChoiceNodeOrdinal, value)));

            /// <summary>
            /// The villagers the chapter provider derives a dialogue binding into in Automatic mode: the same set the
            /// GC-013 narrative sequence checks for automatic inheritance (P-013), so every presented value has a
            /// committed binding row to be equal to (P-045).
            /// </summary>
            public IReadOnlyList<TargetId> ViewTargets => AutomaticTargets;

            /// <summary>
            /// Attaches the narrative genre's own stage runtime: the same module `NarrativeScenario` attaches over
            /// its compiled schedule, with every live target of this world mapped to its entity. A narrative world
            /// whose systems dispatch without one leaves the choice lane unconsumed and faults the step commit
            /// (P-043), so this is the world's real step stage the adapter gate drives (P-005).
            /// </summary>
            public Gc019StageRuntime AttachStageRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder) =>
                Gc019StageRuntime.AttachNarrative(
                    host,
                    descriptor.Compilation!.Schedule!,
                    targets,
                    seeder);
        }
    }
}
