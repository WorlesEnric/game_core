// GameCore.Validation.ProbeHost — the GC-019 card family adapter.
//
// `Gc013CardsHost.CardFamily` already declares the whole card slice as an `IGc013Family`: its catalog declarations,
// its market scope tree, its live seats and table, the festival scoring provider it mounts and every composition
// edit the GC-013 sequence submits. The Wave 4 gate adds the lifecycle and slot-policy half of the same class in
// `W4GateCardsHost.cs`. This file adds the *GC-019* half — the input/asset/presentation surface the adapters need —
// as the third partial part of the same class, so the adapter gate runs the same card market the earlier gates run
// (P-001).
//
// Everything it declares is the genre's own data:
//
//   * the ordinary command route, the market table it addresses and the command schema are `CardTableKeys`'
//     generated identities, i.e. exactly what `CardTableRegistration.Messages()` registers and
//     `CardTableSystems.CardInputSystem` decodes through `CardCommandReader`;
//   * the payload is `CardPayloadCodec.WriteCommand` over the ordinary `CardCommandPayload`, in the exact shape the
//     card fixture's own scenario submits for 07 s2.3's example: seat A plays its first three held cards against the
//     seeded table version;
//   * the view targets are the seats the festival provider derives a `cards.set-bonus` row into in Automatic mode,
//     i.e. the rows `AssemblyPublisher` publishes for this revision.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
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
        /// <summary>Runs the GC-019 sequence against the committed generated catalog (GC-003 compiler output).</summary>
        public static Gc019ScenarioResult RunGc019GeneratedCatalog()
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

            return Gc019Scenario.Run(new CardFamily(
                catalog,
                Declarations(),
                CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-019 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc019ScenarioResult RunGc019FixtureCatalog()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Gc019Scenario.Run(new CardFamily(
                build.Catalog,
                Declarations(),
                CardCatalogTable.Fingerprint().ToHex()));
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
        /// The adapter-gate half of the card family: the ordinary command identity its own table runtime registers,
        /// the payload its own reader decodes and the seats its own provider derived into.
        /// </summary>
        public sealed partial class CardFamily : IGc019Family
        {
            public RouteId CommandRoute => CardTableKeys.CommandRoute;

            public TargetId CommandTarget => CardIdentity.Target(CardVocabulary.TableOne);

            public SchemaRef CommandSchema => CardTableKeys.CommandSchema;

            /// <summary>
            /// One ordinary card command the table runtime's own input stage decodes: kind, issuing seat,
            /// counterparty, the table version the issuer authored against and the three candidate cards
            /// (`CardPayloadCodec.WriteCommand`, 05 s6). The expected table version is the declared scalar, so the
            /// runner can author a command the table really accepts without knowing the settlement rules.
            /// </summary>
            public FrozenPayload CommandPayload(int value) =>
                CardPayloadCodec.WriteCommand(new CardCommandPayload(
                    CardCommandKind.SubmitSet,
                    CardTableKeys.SeatAOrdinal,
                    CardTableKeys.SeatAOrdinal,
                    (uint)value,
                    CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 0),
                    CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 1),
                    CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 2)));

            /// <summary>
            /// The seats the festival scoring provider derives a `cards.set-bonus` row into in Automatic mode: the
            /// same set the GC-013 card sequence checks for automatic inheritance (P-013), so every presented value
            /// has a committed binding row to be equal to (P-045).
            /// </summary>
            public IReadOnlyList<TargetId> ViewTargets => AutomaticTargets;

            /// <summary>
            /// Attaches the card genre's own stage runtime: the same module `CardMarketScenario` attaches, with the
            /// seeded table and every seat bound. A card world whose systems dispatch without one leaves the command
            /// lane unconsumed and faults the step commit (P-043), so this is the table runtime's real stage the
            /// adapter gate drives (P-005).
            /// </summary>
            public Gc019StageRuntime AttachStageRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder) =>
                Gc019StageRuntime.AttachCards(host, targets, seeder);
        }
    }
}
