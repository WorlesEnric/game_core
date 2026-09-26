// GameCore.Validation.ProbeHost — the Wave 6 gate's card family adapter.
//
// The card twin of `W6FamilyNarrativeHost.cs`, and the receiving world of the gate's durable-reward observation:
// GC-021's reward composition reaches a hand through the card family's own `Transfer` command, so the card market is
// the world whose unload/reload cycle the exactly-once claim is made across. `Gc013CardsHost.CardFamily` already
// declares the card slice as `IGc013Family`, `IGc018Family`, `IGc019Family` and `ILifecycleStressFamily`; this part
// adds only the two catalog entry points and the `IW6Family` answers, all of which delegate to those declarations.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        /// <summary>
        /// Digest the card gate run must report over its seven named observations, all passing, over the committed
        /// generated catalog: `NarrativeDigest.OfLines` over `cards/w6-...=pass` lines in the frozen order. It is
        /// computed from the frozen name table, so a renamed, reordered, added or dropped observation changes the
        /// literal and the gate cannot silently shrink (P-008).
        /// </summary>
        public const string W6GateGeneratedDigest =
            "894a975e4a5eb98d733ec213778ec275e4abe02cb4028271857bd4fa68d6fe80";

        /// <summary>
        /// The fixture-catalog run's literal, computed the same way over `fixture:cards/w6-...` names.
        /// </summary>
        public const string W6GateFixtureDigest =
            "9c3d3b5d2f8e959920aa9678e65f56658cbe4a513fcd2895cd458fbda4f9ea14";

        /// <summary>Runs the Wave 6 gate against the committed generated catalog (GC-011 compiler output).</summary>
        public static W6GateScenarioResult RunGeneratedCatalogW6Gate()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: " + build.Describe());
            }

            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return W6GateScenario.Run(
                new CardFamily(catalog, Declarations(), CardCatalog.CatalogFingerprint),
                false);
        }

        /// <summary>Runs the Wave 6 gate against the hand-written generated-style catalog in the fixture package.</summary>
        public static W6GateScenarioResult RunFixtureCatalogW6Gate()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            ImmutableCatalog? catalog = build.Catalog;
            if (catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style card catalog was rejected: " + build.Describe());
            }

            return W6GateScenario.Run(
                new CardFamily(catalog, Declarations(), CardCatalogTable.Fingerprint().ToHex()),
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
        /// The Wave 6 gate half of the card family: the three-family cycle contract's card answers, each delegating to
        /// the genre's own declaration (the lifecycle stress half, the adapter half and the table command payload).
        /// </summary>
        public sealed partial class CardFamily : IW6Family
        {
            LifecycleStressDeclarations IW6Family.StressDeclarations => StressDeclarations;

            PluginInstanceId IW6Family.StressInstance(ulong ordinal) => StressInstance(ordinal);
            ScopeId IW6Family.CycleMountScope => WorldRootScope;

            CompositionEditPayload IW6Family.StressMount(
                PluginManifest manifest,
                PluginInstanceId instance,
                ScopeId scope) => StressMount(manifest, instance, scope);

            CompositionEditPayload IW6Family.StressUnmount(PluginInstanceId instance) => StressUnmount(instance);

            /// <summary>The card table's own command route: the one command a cycle admits (P-042).</summary>
            RouteId IW6Family.CycleRoute => CommandRoute;

            /// <summary>The table the command addresses: a live target of the seeded world.</summary>
            TargetId IW6Family.CycleTarget => CommandTarget;

            /// <summary>The table command schema the genre's own compile unit registers.</summary>
            SchemaRef IW6Family.CycleSchema => CommandSchema;

            /// <summary>
            /// The genre's own table command for one cycle, authored against the table version the fixture seeds, so
            /// the table really accepts it rather than refusing it as stale (P-042, 05 s6).
            /// </summary>
            FrozenPayload IW6Family.CyclePayload(ulong ordinal)
            {
                _ = ordinal;
                return CommandPayload((int)CardTableKeys.SeededTableVersion);
            }

            /// <summary>A command-driven world commits one logical step per admitted command (P-036).</summary>
            ulong IW6Family.CyclePumpTicks => W6GateScenario.CommandDrivenPumpTicks;

            W6StageRuntime IW6Family.AttachGateRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder)
            {
                _ = descriptor;
                return new W6StageRuntime(Label, Gc019StageRuntime.AttachCards(host, targets, seeder), null);
            }
        }
    }
}
