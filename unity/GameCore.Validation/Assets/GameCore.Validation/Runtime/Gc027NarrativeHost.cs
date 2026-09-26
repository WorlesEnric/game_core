// GameCore.Validation.ProbeHost — the GC-027 narrative family adapter.
//
// `Gc027Family.cs` defines what one genre must declare for the recovery proof; `Gc018NarrativeHost.cs` already
// implements everything a checkpoint round trip needed (catalog, scope tree, live targets, the provider to derive
// from, the dormant row, the persistent clock, the composition enrichment, the runtime attach). This file is the
// other half: `Gc013NarrativeHost.NarrativeFamily` is a partial type, and the part declared here adds exactly the
// surface `IGc027Family` does not have —
//
//   * the one delivery obligation the run commits and never delivers, addressed to the narrative family's OWN
//     declared choice route with the production choice codec's payload. Using the genre's own route identity rather
//     than a parallel invented one is what P-004 asks for ("the route *is* the endpoint");
//   * the outbox parameters the world runs with, so the run's durability is declared rather than assumed (P-045);
//   * the composition edit that faults the source world after its first live write: the family's own second-provider
//     mount, which is a real validated publication and therefore really reaches the apply path (P-031);
//   * the catalog fingerprint, the committed codecs, the migration graph and the allocated schema set, so the runner
//     never rebuilds a binding table of its own (P-054).
//
// Nothing here models a checkpoint or a recovery: the runner owns the captures, the faults, the recoveries and the
// observations, and this file only declares the narrative genre's facts.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Gameplay.Narrative;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Validation.Generated;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The GC-027 half of the narrative family: its obligation, its outbox and its fault edit.</summary>
    public static partial class Gc013NarrativeHost
    {
        /// <summary>Node ordinal the recovery obligation's choice names; the declared chapter-one node (P-037).</summary>
        private const int ObligationChoiceNodeOrdinal = 2;

        public sealed partial class NarrativeFamily : IGc027Family
        {
            private CheckpointCodecSet? checkpointCodecs;
            private CheckpointMigrationRegistry? recoveryMigrations;
            private IReadOnlyList<SchemaRef>? allocatedSchemas;

            /// <summary>
            /// The narrative family's own declared choice route is the destination of the run's one obligation: it is
            /// the endpoint the choice command is addressed to, so no parallel identity is invented (P-004).
            /// </summary>
            public Id128 DeliveryDestinationId => NarrativeKeys.ChoiceRoute.Value;

            /// <summary>The command schema that route accepts; a mismatch is refused before the port is asked (P-054).</summary>
            public SchemaRef DeliveryCommandSchema => NarrativeKeys.ChoiceCommandSchema;

            /// <summary>The schema the obligation's payload is recorded under, which is the command's own schema (P-053).</summary>
            public SchemaRef DeliveryPayloadSchema => NarrativeKeys.ChoiceCommandSchema;

            /// <summary>The production choice codec's bytes for a declared node, so the payload is real and stable.</summary>
            public byte[] DeliveryPayload() =>
                NarrativePayloadCodec.EncodeChoice(
                    new NarrativeChoice(ObligationChoiceNodeOrdinal, NarrativeDialogueRules.PermitChoice));

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
                        "the narrative family's catalog fingerprint literal is not 64 lowercase hex characters (P-028).");
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
            /// The directed migration graph this destination registers. The narrative catalog declares no schema
            /// step, so the registry is empty and a captured schema version without a path is refused by the planner
            /// rather than approximated (P-054).
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

            /// <summary>
            /// True: this genre commits delivery obligations the run carries across a recovery, so the run records
            /// the delivery observations for it (P-045).
            /// </summary>
            public bool HasDeliveryObligation => true;

            /// <summary>
            /// True: this genre's world has no declared tree of its own, so its setup edits are real scope creations
            /// rather than scopes the lane seed already carries (P-010).
            /// </summary>
            public bool PublishesDeclaredSetupEdits => true;

            /// <summary>The genre's declared temporal model: a command-driven world (P-036).</summary>
            public TemporalModel TemporalModel => TemporalModel.CommandDriven;

            /// <summary>Null: a command-driven world declares no fixed step (P-036).</summary>
            public FixedStepSettings? FixedStep => null;

            /// <summary>Zero: a command-driven world has no fixed step to step an engine for (P-036).</summary>
            public double FixedStepSeconds => 0d;

            /// <summary>
            /// Null: this genre is ECS-owned and declares no engine physical domain, so the recovery run records no
            /// physics observations for it (P-034, P-059).
            /// </summary>
            public Gc027PhysicsDomain? PhysicsDomain => null;

            /// <summary>
            /// Ignored: this genre's runtime attaches from the world alone and needs no compiled descriptor
            /// (the traversal course is the one that passes it to its stage runtime).
            /// </summary>
            public void NotePipelineForAttach(PipelineDescriptorReport descriptor) => _ = descriptor;

            /// <summary>
            /// False: this genre is ECS-owned and installs no engine physical domain, so the run records no physics
            /// observation for it (P-034, P-059).
            /// </summary>
            public bool DeclaresEnginePhysicsDomain => false;

            /// <summary>
            /// Null: this genre's committed state is its owner slot rows, which the restore outcome already carries
            /// and the active-and-dormant observation already compares. It declares no separate engine quantity.
            /// </summary>
            public string? AuthoritativeStateText(UnityWorldHost world) => null;

            public void ReleaseRecoveryResources() { }

            /// <summary>
            /// Null: an admitted step of this genre needs no input beyond the commands its own run submits, so the
            /// runner pumps without staging one (P-036).
            /// </summary>
            public CommandEnvelope? StepInput(WorldId world, OperationId operation) => null;

            /// <summary>
            /// True with no write: this genre's whole committed state is its owner slot rows, which the checkpoint
            /// copies on its own, so there is no ECS state left for a family hook to persist (P-032, P-053).
            /// </summary>
            public bool TryCaptureAuthoritativeState(
                UnityWorldHost world,
                LiveTargetSeeder seeder,
                out DiagnosticCode code,
                out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                _ = world;
                _ = seeder;
                return true;
            }

            /// <summary>
            /// True with no write: the plan's slot rows are the whole of this genre's committed state and the
            /// builder seeds them directly, so a second application would be a fabricated copy (P-053).
            /// </summary>
            public bool TryApplyAuthoritativeState(
                UnityWorldHost world,
                LiveTargetSeeder seeder,
                IReadOnlyList<SlotRecordValue> slots,
                out DiagnosticCode code,
                out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                _ = world;
                _ = seeder;
                _ = slots;
                return true;
            }

            /// <summary>Zero: this genre's state is complete at creation, so no steps precede the capture.</summary>
            public uint AdmittedStepsBeforeFault => 0U;

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
        /// The narrative family a GC-027 run drives, over the committed generated catalog (GC-003 compiler output).
        /// It is built exactly as `RunGeneratedCatalogGc018` builds its own family, so the recovery proof runs the
        /// same catalog and the same declarations the checkpoint round trip runs (P-009, P-028).
        /// </summary>
        public static IGc027Family RecoveryFamily()
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

            return new NarrativeFamily(
                catalog,
                Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint);
        }
    }
}
