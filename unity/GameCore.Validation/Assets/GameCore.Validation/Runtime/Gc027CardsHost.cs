// GameCore.Validation.ProbeHost — the GC-027 card family adapter.
//
// `Gc027Family.cs` defines what one genre must declare for the recovery proof; `Gc018CardsHost.cs` already
// implements everything a checkpoint round trip needed (catalog, declared scope tree, live seats, the provider to
// derive from, the dormant row, the persistent clock, the composition enrichment, the runtime attach). This file is
// the other half: `Gc013CardsHost.CardFamily` is a partial type, and the part declared here adds exactly the surface
// `IGc027Family` does not have —
//
//   * the one delivery obligation the run commits and never delivers, addressed to the card market's OWN declared
//     command route with the production card codec's payload. That route identity is exactly what the reward bridge
//     addresses its own obligations to (`CardTableConstants.DestinationId`), so no parallel endpoint is invented
//     (P-004, P-045);
//   * the outbox parameters the world runs with, so the run's durability is declared rather than assumed (P-045);
//   * the composition edit that faults the source world after its first live write: the family's own second-provider
//     mount, a real validated publication that therefore really reaches the apply path (TEST-016 row 5, P-031);
//   * the catalog fingerprint, the committed codecs, the migration graph and the allocated schema set, so the runner
//     never rebuilds a binding table of its own (P-054).
//
// Nothing here models a checkpoint or a recovery: the runner owns the captures, the faults, the recoveries and the
// observations, and this file only declares the card genre's facts.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Cards;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The GC-027 half of the card family: its obligation, its outbox and its fault edit.</summary>
    public static partial class Gc013CardsHost
    {
        /// <summary>Card ordinal the recovery obligation's transfer drafts; seat A's first declared card (P-037).</summary>
        private const int ObligationCardOrdinal = 1;

        public sealed partial class CardFamily : IGc027Family
        {
            private CheckpointCodecSet? checkpointCodecs;
            private CheckpointMigrationRegistry? recoveryMigrations;
            private IReadOnlyList<SchemaRef>? allocatedSchemas;

            /// <summary>
            /// The card market's own declared command route is the destination of the run's one obligation: it is the
            /// endpoint a card command is addressed to, and it is the same identity the reward bridge uses, so no
            /// parallel one is invented (P-004).
            /// </summary>
            public Id128 DeliveryDestinationId => CardTableKeys.CommandRoute.Value;

            /// <summary>The command schema that route accepts; a mismatch is refused before the port is asked (P-054).</summary>
            public SchemaRef DeliveryCommandSchema => CardTableKeys.CommandSchema;

            /// <summary>The schema the obligation's payload is recorded under, which is the command's own schema (P-053).</summary>
            public SchemaRef DeliveryPayloadSchema => CardTableKeys.CommandSchema;

            /// <summary>
            /// The production card codec's bytes for one declared command: a transfer from seat A to seat B under the
            /// seeded table version, so the payload is real, canonical and stable across runs (P-054).
            /// </summary>
            public byte[] DeliveryPayload()
            {
                var payload = new CardCommandPayload(
                    CardCommandKind.Transfer,
                    CardTableKeys.SeatAOrdinal,
                    CardTableKeys.SeatBOrdinal,
                    CardTableKeys.SeededTableVersion,
                    CardTableKeys.SeatCard(CardTableKeys.SeatAOrdinal, ObligationCardOrdinal),
                    new CardId(0UL),
                    new CardId(0UL));
                // The codec returns a frozen payload; the obligation carrier is a byte array, so the bytes are
                // copied out once, here, rather than handed over as an aliased view (P-054).
                FrozenPayload command = CardPayloadCodec.WriteCommand(payload);
                var bytes = new byte[command.Length];
                for (int i = 0; i < bytes.Length; i++)
                {
                    bytes[i] = command.Bytes[i];
                }

                return bytes;
            }

            /// <summary>Open obligations the run's outbox may hold; exhaustion is explicit, never a silent drop (P-043).</summary>
            public int OutboxCapacity => 8;

            /// <summary>Terminal delivery records retained per destination before the oldest is pruned (P-045).</summary>
            public int OutboxTerminalRetention => 4;

            /// <summary>The run's outbox is durable, and the recovery observation proves that is what it configured.</summary>
            public OutboxDurability OutboxDurabilityClass => OutboxDurability.Durable;

            /// <summary>
            /// The family's own second-provider mount, submitted to fault the source world after its first live
            /// write. It is a real publication the declared catalog accepts, so the injected fault fires inside a real
            /// apply rather than in validation (TEST-016 row 5, P-031).
            /// </summary>
            public CompositionEditPayload FaultEdit() => MountSecondProvider();

            /// <summary>The emitted catalog fingerprint as a value, so a scenario can never mistype the literal (P-028).</summary>
            public ContentHash CatalogHash()
            {
                if (!ContentHash.TryParseHex(CatalogFingerprint, out ContentHash parsed))
                {
                    throw new InvalidOperationException(
                        "the card family's catalog fingerprint literal is not 64 lowercase hex characters (P-028).");
                }

                return parsed;
            }

            /// <summary>The committed generated checkpoint codecs of this family's catalog (P-054).</summary>
            public CheckpointCodecSet Codecs
            {
                get
                {
                    if (checkpointCodecs == null)
                    {
                        if (!Gc018CheckpointCodecs.TryBuild(
                                out CheckpointSerializerBindings? _, out CheckpointCodecSet? built, out string detail)
                            || built == null)
                        {
                            throw new InvalidOperationException(
                                "the committed checkpoint catalog's serializers could not be bound: " + detail);
                        }

                        checkpointCodecs = built;
                    }

                    return checkpointCodecs;
                }
            }

            /// <summary>
            /// The directed migration graph this destination registers. The card catalog declares no schema step, so
            /// the registry is empty and a captured schema version without a path is refused by the planner rather
            /// than approximated (P-054).
            /// </summary>
            public CheckpointMigrationRegistry DirectMigrations =>
                recoveryMigrations ?? (recoveryMigrations = new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>()));

            /// <summary>
            /// The schemas this destination can allocate at the version it carries: the checkpoint container, every
            /// recipe the family's catalog registers and the payload schema its declared wake names (P-054).
            /// </summary>
            public IReadOnlyList<SchemaRef> AllocatedSchemas
            {
                get
                {
                    if (allocatedSchemas != null)
                    {
                        return allocatedSchemas;
                    }

                    var schemas = new List<SchemaRef> { CheckpointFormat.DocumentSchema };
                    SpawnRecipeCatalog recipes = CreateRecipes();
                    for (int i = 0; i < recipes.Recipes.Count; i++)
                    {
                        AddDistinct(schemas, recipes.Recipes[i].Recipe.Schema);
                    }

                    AddDistinct(schemas, WakePayloadSchema);
                    allocatedSchemas = schemas;
                    return allocatedSchemas;
                }
            }

            private static void AddDistinct(List<SchemaRef> schemas, SchemaRef candidate)
            {
                for (int i = 0; i < schemas.Count; i++)
                {
                    if (schemas[i].Equals(candidate))
                    {
                        return;
                    }
                }

                schemas.Add(candidate);
            }
        }

        /// <summary>
        /// The card family a GC-027 run drives, over the committed generated catalog (GC-011 compiler output). It is
        /// built exactly as `RunGeneratedCatalogGc018` builds its own family, so the recovery proof runs the same
        /// catalog and the same declarations the checkpoint round trip runs (P-009, P-028).
        /// </summary>
        public static IGc027Family RecoveryFamily()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: " + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return new CardFamily(catalog, Declarations(), CardCatalog.CatalogFingerprint);
        }
    }
}
