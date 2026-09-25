// GameCore.Validation.ProbeHost — the GC-018 narrative family adapter.
//
// `Gc018Family.cs` defines what one genre must declare for the checkpoint round trip; `Gc013NarrativeHost.cs`
// already implements the half every GC-013-family scenario needed (catalog, scope tree, live targets, the provider
// to derive from, the branch to move, the mode edits, the neutral scope creations). This file is the other half:
// `Gc013NarrativeHost.NarrativeFamily` is a partial type, and the part declared here adds exactly the surface
// `IGc018Family` does not have —
//
//   * the one external command the run queues and leaves unexecuted, built on the family's own declared choice
//     route, target and payload schema (P-037, P-053);
//   * the persistent logical-step clock and the payload schema its wake declares, plus the one dormant state row,
//     so the capture carries real clock and dormant-state facts (P-032, P-038);
//   * the composition enrichment the source world applies before the capture: one named capability-isolation member
//     and one explicit exclusion on the village scope, which owns live targets (P-016).
//
// Nothing here models a checkpoint: the scenario owns the capture, the refusals, the restore and the observations,
// and this file only declares the narrative genre's facts.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Validation.Generated;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The GC-018 half of the narrative family: its command, its clock, its dormant row and its boundaries.</summary>
    public static partial class Gc013NarrativeHost
    {
        /// <summary>Node ordinal the queued choice names; it is the declared chapter-one node (P-037).</summary>
        private const int QueuedChoiceNodeOrdinal = 1;

        /// <summary>Value the dormant conversation-status row is seeded with; it is not the default status (P-032).</summary>
        private const int DormantStatusValue = 3;

        /// <summary>Stable name of the narrative world's persistent clock (P-038).</summary>
        private const string WakeClockName = "narrative.clock.domain";

        public sealed partial class NarrativeFamily : IGc018Family
        {
            /// <summary>
            /// One choice on the family's own declared route, left unexecuted so the capture has a real queued
            /// external command to disposition (P-037, P-053). Its payload is the production choice codec's output,
            /// so a restore re-admits exactly the bytes the source world admitted.
            /// </summary>
            public CommandEnvelope QueuedCommand(WorldId world, OperationId operation)
            {
                return new CommandEnvelope(
                    operation,
                    NarrativeKeys.ChoiceRoute,
                    NarrativeKeys.Mara,
                    NarrativeKeys.ChoiceCommandSchema,
                    null,
                    new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                        new NarrativeChoice(QueuedChoiceNodeOrdinal, NarrativeDialogueRules.PermitChoice))));
            }

            /// <summary>Schema version the active conversation-node row is seeded at (P-032).</summary>
            public uint ActiveSlotVersion => NarrativeKeys.ConversationDomain.Version;

            /// <summary>The narrative slice's declared domain clock, which persists across a boundary (P-038).</summary>
            public Id128 WakeClockId => NarrativeIds.Id(WakeClockName);

            /// <summary>The conversation domain schema the clock's wake declares (P-038, P-053).</summary>
            public SchemaRef WakePayloadSchema => NarrativeKeys.ConversationDomain;

            /// <summary>Mara owns both the active node row and the dormant status row (P-032).</summary>
            public TargetId DormantTarget => NarrativeKeys.Mara;

            public OwnerId DormantOwner => NarrativeKeys.DialogueOwner;

            /// <summary>The conversation-status slot: a different slot of the same owner and target as the active row.</summary>
            public SlotId DormantSlot => NarrativeKeys.ConversationStatusSlot;

            public uint DormantVersion => NarrativeKeys.ConversationDomain.Version;

            public int DormantValue => DormantStatusValue;

            /// <summary>
            /// One capability-isolation member and one explicit exclusion on the village scope (P-016). The village
            /// already owns live targets and carries no boundary of its own, so a restore that reopened the boundary
            /// or dropped the exclusion cannot report the same grant rows.
            /// </summary>
            public CompositionEditPayload BoundaryEnrichment()
            {
                var members = new List<Id128> { NarrativeKeys.DialogueBinding.Value };
                var exclusions = new List<ExclusionRule>
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        NarrativeKeys.DialogueBinding.Value,
                        default(ScopeId),
                        default(TargetId),
                        false),
                };

                return Gc018Scenario.ScopeBoundaries(
                    NarrativeKeys.VillageScope,
                    new IsolationSet(false, null),
                    new IsolationSet(false, members),
                    exclusions);
            }

            public ScopeId EnrichedScope => NarrativeKeys.VillageScope;
        }

        /// <summary>Runs the GC-018 sequence against the committed generated catalog (GC-003 compiler output).</summary>
        public static Gc018ScenarioResult RunGeneratedCatalogGc018()
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

            return Gc018Scenario.Run(new NarrativeFamily(
                catalog,
                Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-018 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc018ScenarioResult RunFixtureCatalogGc018()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return Gc018Scenario.Run(new NarrativeFamily(
                build.Catalog,
                Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                NarrativeScenarioCatalog.Fingerprint().ToHex()));
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
