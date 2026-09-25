// GameCore.Validation.ProbeHost — the GC-018 card family adapter.
//
// `Gc018Family.cs` defines what one genre must declare for the checkpoint round trip; `Gc013CardsHost.cs` already
// implements the half every GC-013-family scenario needed (catalog, declared scope tree, live seats, the provider to
// derive from, the branch to move, the mode edits, the neutral scope creations). This file is the other half:
// `Gc013CardsHost.CardFamily` is a partial type, and the part declared here adds exactly the surface
// `IGc018Family` does not have —
//
//   * the one external command the run queues and leaves unexecuted, built on the family's own declared command
//     route, table target and payload schema with the production card payload codec (P-037, P-053);
//   * the persistent logical-step clock and the table-domain schema its wake declares, plus the one dormant state
//     row on seat A (a different slot from the active seat-state row), so the capture carries real clock and
//     dormant-state facts (P-032, P-038);
//   * the composition enrichment the source world applies before the capture: one named capability-isolation member
//     and one explicit exclusion on league A, which owns live seats (P-016).
//
// Nothing here models a checkpoint: the scenario owns the capture, the refusals, the restore and the observations,
// and this file only declares the card genre's facts.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.GeneratedCards;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The GC-018 half of the card family: its command, its clock, its dormant row and its boundaries.</summary>
    public static partial class Gc013CardsHost
    {
        /// <summary>Value the dormant table-state row is seeded with; it is not the seeded table version (P-032).</summary>
        private const int DormantTableValue = 99;

        /// <summary>Stable name of the card world's persistent clock (P-038).</summary>
        private const string WakeClockName = "cards.clock.domain";

        /// <summary>Ordinal the queued transfer names as the receiving seat; seat B is a declared seat (P-037).</summary>
        private const uint QueuedCounterpartyOrdinal = CardTableKeys.SeatBOrdinal;

        public sealed partial class CardFamily : IGc018Family
        {
            private CardTableModule? attachedRuntime;

            /// <summary>
            /// One card command on the family's own declared route, left unexecuted so the capture has a real queued
            /// external command to disposition (P-037, P-053). Its payload is the production card codec's output, so
            /// a restore re-admits exactly the bytes the source world admitted.
            /// </summary>
            public CommandEnvelope QueuedCommand(WorldId world, OperationId operation)
            {
                var payload = new CardCommandPayload(
                    CardCommandKind.Transfer,
                    CardTableKeys.SeatAOrdinal,
                    QueuedCounterpartyOrdinal,
                    CardTableKeys.SeededTableVersion,
                    CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, 0),
                    new CardId(0UL),
                    new CardId(0UL));

                return new CommandEnvelope(
                    operation,
                    CardTableKeys.CommandRoute,
                    CardIdentity.Target(CardVocabulary.TableOne),
                    CardTableKeys.CommandSchema,
                    null,
                    CardPayloadCodec.WriteCommand(payload));
            }

            /// <summary>Schema version the active seat-state row is seeded at (P-032).</summary>
            public uint ActiveSlotVersion => CardTableKeys.SeatDomain.Version;

            /// <summary>The card slice's declared domain clock, which persists across a boundary (P-038).</summary>
            public Id128 WakeClockId => CardIdentity.Id(WakeClockName);

            /// <summary>The table domain schema the clock's wake declares (P-038, P-053).</summary>
            public SchemaRef WakePayloadSchema => CardTableKeys.TableDomain;

            /// <summary>Seat A owns both the active seat-state row and the dormant table-state row (P-032).</summary>
            public TargetId DormantTarget => CardTableFixture.SeatTarget(CardTableKeys.SeatAOrdinal);

            public OwnerId DormantOwner => CardTableKeys.TableOwner;

            /// <summary>The table-state slot: a different slot of the same owner and target as the active row.</summary>
            public SlotId DormantSlot => CardTableKeys.TableSlot;

            public uint DormantVersion => CardTableKeys.TableDomain.Version;

            public int DormantValue => DormantTableValue;

            /// <summary>
            /// One capability-isolation member and one explicit exclusion on league A (P-016). League A already owns
            /// two live seats and carries no boundary of its own, so a restore that reopened the boundary or dropped
            /// the exclusion cannot report the same grant rows.
            /// </summary>
            public CompositionEditPayload BoundaryEnrichment()
            {
                var members = new List<Id128> { CardVocabulary.SetBonusCapability.Value };
                var exclusions = new List<ExclusionRule>
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        CardVocabulary.SetBonusCapability.Value,
                        default(ScopeId),
                        default(TargetId),
                        false),
                };

                return Gc018Scenario.ScopeBoundaries(
                    CardIdentity.Scope(CardVocabulary.LeagueA),
                    new IsolationSet(false, null),
                    new IsolationSet(false, members),
                    exclusions);
            }

            public ScopeId EnrichedScope => CardIdentity.Scope(CardVocabulary.LeagueA);

            /// <summary>
            /// Attaches this genre's runtime module to a GC-018 world, exactly as the card market fixture
            /// attaches its own: the module the four card systems resolve their world through, with the table
            /// entity bound as the table and every live seat bound to its ordinal, so the re-admitted command is
            /// drafted, settled and released by the table stages rather than stranded in the ingress lane
            /// (P-042, P-043).
            /// </summary>
            public bool TryAttachRuntime(Gc018RuntimeWorld world, out string detail)
            {
                CardTableModule module = CardTableModule.Attach(world.Host);
                IReadOnlyList<LiveTarget> live = world.Targets.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    TargetId target = live[i].Target;
                    if (!world.Seeder.TryGetEntity(target, out Entity entity))
                    {
                        detail = "live target " + target.ToString()
                            + " has no native entity to map into the card module (P-005).";
                        return false;
                    }

                    if (target.Equals(CardIdentity.Target(CardVocabulary.TableOne)))
                    {
                        module.BindTable(entity);
                        continue;
                    }

                    if (TrySeatOrdinalOf(target, out uint ordinal))
                    {
                        module.BindSeat(ordinal, entity);
                    }
                }

                if (module.TableEntity == Entity.Null)
                {
                    detail = "the restored market has no table entity, so the card stages cannot run (P-005).";
                    return false;
                }

                attachedRuntime = module;
                detail = string.Empty;
                return true;
            }

            /// <summary>Releases the module the last attach created for this family's GC-018 world (P-048).</summary>
            public void DetachRuntime(Gc018RuntimeWorld world)
            {
                attachedRuntime?.Dispose();
                attachedRuntime = null;
                _ = world;
            }

            /// <summary>
            /// The declared seat ordinal of one live target, from the ordinals this family seeds; a non-seat
            /// target reports false and is simply not bound (07 s2.2).
            /// </summary>
            private static bool TrySeatOrdinalOf(TargetId target, out uint ordinal)
            {
                uint[] declared = { CardTableKeys.SeatAOrdinal, CardTableKeys.SeatBOrdinal, CardTableKeys.SeatCOrdinal };
                for (int i = 0; i < declared.Length; i++)
                {
                    if (target.Equals(CardTableFixture.SeatTarget(declared[i])))
                    {
                        ordinal = declared[i];
                        return true;
                    }
                }

                ordinal = 0U;
                return false;
            }
        }

        /// <summary>Runs the GC-018 sequence against the committed generated catalog (GC-011 compiler output).</summary>
        public static Gc018ScenarioResult RunGeneratedCatalogGc018()
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

            return Gc018Scenario.Run(new CardFamily(catalog, Declarations(), CardCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-018 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc018ScenarioResult RunFixtureCatalogGc018()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return Gc018Scenario.Run(new CardFamily(build.Catalog, Declarations(), CardCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names,
        /// and the fixture-catalog steps are prefixed with <see cref="Gc018Scenario.FixtureRunPrefix"/> so no two
        /// collide.
        /// </summary>
        public static IReadOnlyList<Gc018Step> RunBothGc018(
            out Gc018ScenarioResult generated,
            out Gc018ScenarioResult fixture)
        {
            generated = RunGeneratedCatalogGc018();
            fixture = RunFixtureCatalogGc018();

            var combined = new List<Gc018Step>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                Gc018Step step = fixture.Steps[i];
                combined.Add(new Gc018Step(Gc018Scenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }
    }
}
