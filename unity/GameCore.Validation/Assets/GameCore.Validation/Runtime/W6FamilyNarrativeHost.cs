// GameCore.Validation.ProbeHost — the Wave 6 gate's narrative family adapter.
//
// `W6GateFamily.cs` defines what the gate requires from one genre; `Gc013NarrativeHost.NarrativeFamily` already
// declares the whole narrative slice as `IGc013Family` (its catalog declarations, chapter scope tree, live villagers,
// provider mounts and composition edits), as `IGc018Family` (checkpoint facts), as `IGc019Family` (its choice route,
// payload and committed view targets) and as `ILifecycleStressFamily` (GC-022's four stress manifests, per-cycle
// instance identity and mount/unmount payloads). This part adds exactly two things and nothing else:
//
//   * the declaration that the narrative slice satisfies `IW6Family`, so a slice that lost the stress or adapter half
//     fails to compile rather than at run time (P-001, P-002);
//   * the two catalog entry points the EditMode suite and the player probe call, mirroring
//     `RunBothLifecycleStress` so the gate runs the *same* narrative world the earlier gates run — the committed
//     generated catalog and the hand-written generated-style catalog, nothing new — plus the two frozen digest
//     literals its runs must report (P-008, P-028).
//
// Every member it answers with is the genre's own: the cycle manifests and payloads are GC-022's stress declarations
// (the same four manifests the lifecycle stress mounts), the cycle command is the genre's own choice command, and the
// cycle step is one host-clock advance in which that choice commits exactly one logical step (P-036, P-042).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.Generated;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013NarrativeHost
    {
        /// <summary>
        /// Digest the narrative gate run must report over its six named observations, all passing, over the committed
        /// generated catalog: `NarrativeDigest.OfLines` over `narrative/w6-...=pass` lines in the frozen order. It is
        /// computed from the frozen name table, so a renamed, reordered, added or dropped observation changes the
        /// literal and the gate cannot silently shrink (P-008).
        /// </summary>
        public const string W6GateGeneratedDigest =
            "4235cea3c22cf93e38307000fcc863edd4ada3e2fc4a3b475ca719765dc17279";

        /// <summary>
        /// The fixture-catalog run's literal, computed the same way over `fixture:narrative/w6-...` names: the fixture
        /// run's observation names carry the prefix, so its table — and therefore its digest — differs from the
        /// generated run's.
        /// </summary>
        public const string W6GateFixtureDigest =
            "ab198371e8d276dcc2833e88e64e278abde57274ea3c68e2497fa2b75f97fa73";

        /// <summary>Runs the Wave 6 gate against the committed generated catalog (GC-003 compiler output).</summary>
        public static W6GateScenarioResult RunGeneratedCatalogW6Gate()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: " + build.Describe());
            }

            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return W6GateScenario.Run(
                new NarrativeFamily(
                    catalog,
                    Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                    ProbeCatalog.CatalogFingerprint),
                false);
        }

        /// <summary>Runs the Wave 6 gate against the hand-written generated-style catalog in the fixture package.</summary>
        public static W6GateScenarioResult RunFixtureCatalogW6Gate()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return W6GateScenario.Run(
                new NarrativeFamily(
                    catalog,
                    Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                    NarrativeScenarioCatalog.Fingerprint().ToHex()),
                true);
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="W6GateScenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<W6GateStep> RunBothW6Gate(
            out W6GateScenarioResult generated,
            out W6GateScenarioResult fixture)
        {
            generated = RunGeneratedCatalogW6Gate();
            fixture = RunFixtureCatalogW6Gate();

            var combined = new List<W6GateStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                W6GateStep step = fixture.Steps[i];
                combined.Add(new W6GateStep(W6GateScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        /// <summary>
        /// The Wave 6 gate half of the narrative family: the three-family cycle contract's narrative answers. Every
        /// one of them delegates to the genre's own declaration (the lifecycle stress half, the adapter half and the
        /// choice payload), so this class never introduces a second narrative surface (P-001).
        /// </summary>
        public sealed partial class NarrativeFamily : IW6Family
        {
            LifecycleStressDeclarations IW6Family.StressDeclarations => StressDeclarations;

            PluginInstanceId IW6Family.StressInstance(ulong ordinal) => StressInstance(ordinal);

            CompositionEditPayload IW6Family.StressMount(
                PluginManifest manifest,
                PluginInstanceId instance,
                ScopeId scope) => StressMount(manifest, instance, scope);

            CompositionEditPayload IW6Family.StressUnmount(PluginInstanceId instance) => StressUnmount(instance);

            /// <summary>The genre's own choice route: the one command a cycle admits (P-042).</summary>
            RouteId IW6Family.CycleRoute => CommandRoute;

            /// <summary>The villager the choice addresses: a live target of the seeded world.</summary>
            TargetId IW6Family.CycleTarget => CommandTarget;

            /// <summary>The choice command schema the genre's own compile unit registers.</summary>
            SchemaRef IW6Family.CycleSchema => CommandSchema;

            /// <summary>
            /// The genre's own choice payload for one cycle. The choice ordinal is the declared permit value, so the
            /// command is accepted by the narrative rules rather than merely admitted by the host (07 s3.2).
            /// </summary>
            FrozenPayload IW6Family.CyclePayload(ulong ordinal)
            {
                _ = ordinal;
                return CommandPayload(NarrativeDialogueRules.PermitChoice);
            }

            /// <summary>
            /// A command-driven world commits one logical step per admitted command, so one cycle advances the host
            /// clock by the same million-tick frame the earlier gates use (P-036, TEST-011).
            /// </summary>
            ulong IW6Family.CyclePumpTicks => W6GateScenario.CommandDrivenPumpTicks;

            W6StageRuntime IW6Family.AttachGateRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder) =>
                new W6StageRuntime(
                    Label,
                    Gc019StageRuntime.AttachNarrative(host, descriptor.Compilation!.Schedule!, targets, seeder),
                    null);
        }
    }
}
