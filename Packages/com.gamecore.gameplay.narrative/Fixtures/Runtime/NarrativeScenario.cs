// GameCore.Gameplay.Narrative.Fixtures — the GC-010 narrative scenario (the Wave 3 narrative exit demonstration).
//
// The sentence this file implements, from `docs/game-core/09-implementation-guide.md` (GC-010):
//
//   "Mount at a chapter, create a future target and observe a gate decision through a committed snapshot; use
//    reusable descriptors with no per-instance capability imports."
//
// Every check below runs the real modules over real storage:
//
//   * GC-004's control lane builds the seven-scope chapter tree and mounts the chapter provider at `chapter-one`;
//   * GC-006 derives the chapter's contributions over the committed composition — the rules select the reusable
//     `narrative.villager-recipe`, `narrative.quest-gate-recipe` and `narrative.quest-encounter-recipe`, so no target
//     imports anything (P-013, P-015);
//   * GC-007 validates the owners, the generated partitions and the slot policies, and its bounded message plane
//     carries the admitted choice;
//   * GC-009 compiles the six-stage graph of 07 section 3.2 and drives the frames through its temporal drivers;
//   * GC-008 plans the publication, publishes the derived binding layout inside one epoch and spawns the future
//     villager fully assembled (P-024, P-030).
//
// The runner is shared by the Unity EditMode test (`GameCore.Narrative.Tests`) and by the standalone player probe
// mode `-probeNarrative`, so the same scenario is proven in the Editor and in an IL2CPP player.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using GameCore.Rules.Narrative;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>One named narrative observation: what was checked and the observed values.</summary>
    public sealed class NarrativeStep
    {
        public NarrativeStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Facts the scenario observed, exposed so a caller can assert on them instead of trusting a boolean. Every value
    /// is read from live module state at the moment named in its own comment.
    /// </summary>
    public sealed class NarrativeFacts
    {
        /// <summary>Fingerprint of the catalog the run was given, so a probe can name the committed literal (P-028).</summary>
        public string CatalogFingerprint { get; set; } = string.Empty;

        // ---------------------------------------------------------------- composition and storage

        public int RegistryBeforeCreate { get; set; }

        public int RegistryAfterCreate { get; set; }

        public string WorldSession { get; set; } = string.Empty;

        public int LiveTargetCount { get; set; }

        public int IneligibleTargetCount { get; set; }

        public int MappedTargetCount { get; set; }

        public bool MessagePlanePresent { get; set; }

        public bool LaneJoinedAfterChapterOne { get; set; }

        // ---------------------------------------------------------------- chapter one

        public ulong LaneRevisionAfterChapterOne { get; set; }

        public ulong LaneEpochAfterChapterOne { get; set; }

        public ulong WorldEpochAfterChapterOne { get; set; }

        public int DerivedTargetCountAfterChapterOne { get; set; }

        public int DerivedContributionCountAfterChapterOne { get; set; }

        public int ChapterOneInstalledRows { get; set; }

        public int ChapterOneRetractedRows { get; set; }

        /// <summary>State slots the plan migrated on bounded scratch: the seeded version-1 conversation slots.</summary>
        public int MigratedConversationSlotCount { get; set; }

        public int MigrationInvocations { get; set; }

        public string PublicationOutcomeAfterChapterOne { get; set; } = string.Empty;

        // ---------------------------------------------------------------- the derived layout in real storage

        public int MaraBindingRowCount { get; set; }

        public int MaraDialogueBindingValue { get; set; }

        public int MaraChoiceBindingValue { get; set; }

        public bool MaraBindingIsActive { get; set; }

        public ulong MaraStampEpoch { get; set; }

        public int GateBindingRowCount { get; set; }

        public int GateBindingValue { get; set; }

        public int EncounterBindingRowCount { get; set; }

        public int EncounterBindingValue { get; set; }

        public int CrowdBindingRowCount { get; set; }

        public int MuseumBindingRowCount { get; set; }

        public int SailorBindingRowCountBeforeChapterTwo { get; set; }

        public int ConversationNodeAfterMigration { get; set; }

        public uint ConversationSchemaVersionAfterMigration { get; set; }

        // ---------------------------------------------------------------- chapter two

        public ulong LaneEpochAfterChapterTwo { get; set; }

        public ulong WorldEpochAfterChapterTwo { get; set; }

        public int ChapterTwoInstalledRows { get; set; }

        public int SailorBindingRowCount { get; set; }

        public int SailorDialogueBindingValue { get; set; }

        public int PublishedBindingRowCountAfterChapterTwo { get; set; }

        // ---------------------------------------------------------------- the bounded choice command

        public int CompiledStageCount { get; set; }

        public int CompiledSystemCount { get; set; }

        public bool CommandAdmitted { get; set; }

        public string CommandAdmissionKind { get; set; } = string.Empty;

        public ulong StepsAfterCommand { get; set; }

        public int AdmittedChoiceCount { get; set; }

        public int ForwardedChoiceCount { get; set; }

        public int RefusedChoiceCount { get; set; }

        public int CommittedFactCount { get; set; }

        public int DuplicateMutationCount { get; set; }

        public int GateDecisionCount { get; set; }

        public int EncounterHookCount { get; set; }

        /// <summary>The gate's decision on `gate-east` before the choice: the reference's `Closed`.</summary>
        public int GateDecisionBeforeCommand { get; set; }

        /// <summary>The gate's decision after the committed choice: `Open` (07 section 3.2).</summary>
        public int GateDecisionAfterCommand { get; set; }

        /// <summary>The fact version the gate's decision was evaluated from (P-032).</summary>
        public int GateEvaluatedFactVersion { get; set; }

        public int QuestFactValueAfterCommand { get; set; }

        public int QuestFactVersionAfterCommand { get; set; }

        public int CommittedEventCountAfterCommand { get; set; }

        public string CommittedEventSchemaAfterCommand { get; set; } = string.Empty;

        public ulong CommittedEventStepAfterCommand { get; set; }

        public ulong CommittedEventEpochAfterCommand { get; set; }

        public int LedgerCommittedCount { get; set; }

        public int LedgerPendingCount { get; set; }

        public int EncounterStatusAfterCommand { get; set; }

        public int TrailStepsAfterCommand { get; set; }

        public int TrailProjectedStepsAfterCommand { get; set; }

        public int TrailCommittedFactsAfterCommand { get; set; }

        public int TrailGateDecisionsAfterCommand { get; set; }

        public int TrailEncounterHooksAfterCommand { get; set; }

        public int StepGroupDispatchRuns { get; set; }

        public int StepGroupDispatchedEntries { get; set; }

        // ---------------------------------------------------------------- the duplicate request

        public ulong StepsAfterDuplicate { get; set; }

        public int DuplicatePumpSteps { get; set; }

        public int CommittedEventCountAfterDuplicate { get; set; }

        // ---------------------------------------------------------------- the forward provider and the spawn

        public bool ForwardDerivationHadNoTargetChange { get; set; }

        public ulong LaneEpochAfterForward { get; set; }

        public ulong WorldEpochAfterSpawn { get; set; }

        public bool CountersJoinedAfterSpawn { get; set; }

        public int SpawnedBindingRowCount { get; set; }

        public int SpawnedBindingValue { get; set; }

        public ulong SpawnedStampEpoch { get; set; }

        public bool SpawnedStampPublished { get; set; }

        public bool SpawnedTargetInPublishedView { get; set; }

        public int PublishedBindingRowCountAfterSpawn { get; set; }

        public int ChapterTwoBindingRowCountInPublishedView { get; set; }

        public int RecipeApplyCount { get; set; }

        public int RecipeInstalledSlotCount { get; set; }

        // ---------------------------------------------------------------- idle and neutrality

        public int IdleFrames { get; set; }

        public int IdleStepsCommitted { get; set; }

        public int IdlePublishedImages { get; set; }

        public int IdleDispatchRuns { get; set; }

        public ulong PendingDemandAfterIdle { get; set; }

        public int GenreAuditChecked { get; set; }

        public bool GenreAuditNeutral { get; set; }

        public int ForbiddenGenreNameCount { get; set; }

        /// <summary>Names of the whole slice inventory (rules content plus gameplay names) the audit examined.</summary>
        /// <summary>True when this run's recorded observations equal the committed trace's declaration (P-060).</summary>
        public bool TraceMatchesDeclaration { get; set; }

        /// <summary>Observations that disagree with the declared canonical trace; empty is the required value.</summary>
        public string TraceMismatchDetail { get; set; } = string.Empty;

        public int InventoryAuditChecked { get; set; }

        public bool InventoryAuditNeutral { get; set; }

        public int InventoryForbiddenNameCount { get; set; }

        public int ComponentAuditChecked { get; set; }

        public bool ComponentAuditNeutral { get; set; }

        // ---------------------------------------------------------------- teardown

        public int OutstandingJobsBeforeTeardown { get; set; }

        public int OutstandingJobsAfterTeardown { get; set; }

        public int RetainedResourcesAfterTeardown { get; set; }

        public int RegistryAfterTeardown { get; set; }

        /// <summary>
        /// One-line digest of every recorded fact, so the standalone probe can archive the observed values beside
        /// the per-check outcomes without a second result shape. The first field is the catalog fingerprint, so a
        /// probe can assert the run really used the committed catalog (P-028).
        /// </summary>
        public string Describe()
        {
            return "catalogFingerprint=" + CatalogFingerprint
                + "; session=" + WorldSession
                + "; registryBefore=" + I(RegistryBeforeCreate)
                + "; registryAfter=" + I(RegistryAfterCreate)
                + "; liveTargets=" + I(LiveTargetCount)
                + "; ineligibleTargets=" + I(IneligibleTargetCount)
                + "; mappedTargets=" + I(MappedTargetCount)
                + "; plane=" + MessagePlanePresent
                + "; stages=" + I(CompiledStageCount)
                + "; systems=" + I(CompiledSystemCount)
                + "; laneJoinedAfterChapterOne=" + LaneJoinedAfterChapterOne
                + "; laneRevisionAfterChapterOne=" + U(LaneRevisionAfterChapterOne)
                + "; laneEpochAfterChapterOne=" + U(LaneEpochAfterChapterOne)
                + "; worldEpochAfterChapterOne=" + U(WorldEpochAfterChapterOne)
                + "; derivedTargetsAfterChapterOne=" + I(DerivedTargetCountAfterChapterOne)
                + "; derivedContributionsAfterChapterOne=" + I(DerivedContributionCountAfterChapterOne)
                + "; chapterOneInstalledRows=" + I(ChapterOneInstalledRows)
                + "; chapterOneRetractedRows=" + I(ChapterOneRetractedRows)
                + "; migratedConversationSlots=" + I(MigratedConversationSlotCount)
                + "; migrationInvocations=" + I(MigrationInvocations)
                + "; publicationOutcomeAfterChapterOne=" + PublicationOutcomeAfterChapterOne
                + "; maraRows=" + I(MaraBindingRowCount)
                + "; maraDialogueValue=" + I(MaraDialogueBindingValue)
                + "; maraChoiceValue=" + I(MaraChoiceBindingValue)
                + "; maraActive=" + MaraBindingIsActive
                + "; maraStampEpoch=" + U(MaraStampEpoch)
                + "; gateRows=" + I(GateBindingRowCount)
                + "; gateValue=" + I(GateBindingValue)
                + "; encounterRows=" + I(EncounterBindingRowCount)
                + "; encounterValue=" + I(EncounterBindingValue)
                + "; crowdRows=" + I(CrowdBindingRowCount)
                + "; museumRows=" + I(MuseumBindingRowCount)
                + "; sailorRowsBeforeChapterTwo=" + I(SailorBindingRowCountBeforeChapterTwo)
                + "; conversationNodeAfterMigration=" + I(ConversationNodeAfterMigration)
                + "; conversationVersionAfterMigration=" + ConversationSchemaVersionAfterMigration.ToString(CultureInfo.InvariantCulture)
                + "; laneEpochAfterChapterTwo=" + U(LaneEpochAfterChapterTwo)
                + "; worldEpochAfterChapterTwo=" + U(WorldEpochAfterChapterTwo)
                + "; chapterTwoInstalledRows=" + I(ChapterTwoInstalledRows)
                + "; sailorRows=" + I(SailorBindingRowCount)
                + "; sailorDialogueValue=" + I(SailorDialogueBindingValue)
                + "; publishedRowsAfterChapterTwo=" + I(PublishedBindingRowCountAfterChapterTwo)
                + "; commandAdmitted=" + CommandAdmitted
                + "; commandAdmission=" + CommandAdmissionKind
                + "; admittedChoices=" + I(AdmittedChoiceCount)
                + "; forwardedChoices=" + I(ForwardedChoiceCount)
                + "; refusedChoices=" + I(RefusedChoiceCount)
                + "; committedFacts=" + I(CommittedFactCount)
                + "; duplicateMutations=" + I(DuplicateMutationCount)
                + "; gateDecisions=" + I(GateDecisionCount)
                + "; encounterHooks=" + I(EncounterHookCount)
                + "; gateDecisionBeforeCommand=" + I(GateDecisionBeforeCommand)
                + "; gateDecisionAfterCommand=" + I(GateDecisionAfterCommand)
                + "; gateEvaluatedFactVersion=" + I(GateEvaluatedFactVersion)
                + "; questFactValueAfterCommand=" + I(QuestFactValueAfterCommand)
                + "; questFactVersionAfterCommand=" + I(QuestFactVersionAfterCommand)
                + "; eventsAfterCommand=" + I(CommittedEventCountAfterCommand)
                + "; eventSchemasAfterCommand=" + CommittedEventSchemaAfterCommand
                + "; eventStepAfterCommand=" + U(CommittedEventStepAfterCommand)
                + "; eventEpochAfterCommand=" + U(CommittedEventEpochAfterCommand)
                + "; ledgerCommitted=" + I(LedgerCommittedCount)
                + "; ledgerPending=" + I(LedgerPendingCount)
                + "; encounterStatusAfterCommand=" + I(EncounterStatusAfterCommand)
                + "; trailSteps=" + I(TrailStepsAfterCommand)
                + "; trailProjected=" + I(TrailProjectedStepsAfterCommand)
                + "; trailFacts=" + I(TrailCommittedFactsAfterCommand)
                + "; trailGateDecisions=" + I(TrailGateDecisionsAfterCommand)
                + "; trailEncounterHooks=" + I(TrailEncounterHooksAfterCommand)
                + "; dispatchRuns=" + I(StepGroupDispatchRuns)
                + "; dispatchedEntries=" + I(StepGroupDispatchedEntries)
                + "; stepsAfterCommand=" + U(StepsAfterCommand)
                + "; stepsAfterDuplicate=" + U(StepsAfterDuplicate)
                + "; duplicatePumpSteps=" + I(DuplicatePumpSteps)
                + "; eventsAfterDuplicate=" + I(CommittedEventCountAfterDuplicate)
                + "; forwardNoTargetChange=" + ForwardDerivationHadNoTargetChange
                + "; laneEpochAfterForward=" + U(LaneEpochAfterForward)
                + "; worldEpochAfterSpawn=" + U(WorldEpochAfterSpawn)
                + "; joinedAfterSpawn=" + CountersJoinedAfterSpawn
                + "; spawnedRows=" + I(SpawnedBindingRowCount)
                + "; spawnedValue=" + I(SpawnedBindingValue)
                + "; spawnedStampEpoch=" + U(SpawnedStampEpoch)
                + "; spawnedStampPublished=" + SpawnedStampPublished
                + "; spawnedInView=" + SpawnedTargetInPublishedView
                + "; publishedRowsAfterSpawn=" + I(PublishedBindingRowCountAfterSpawn)
                + "; chapterTwoRowsInView=" + I(ChapterTwoBindingRowCountInPublishedView)
                + "; recipeApplies=" + I(RecipeApplyCount)
                + "; recipeInstalledSlots=" + I(RecipeInstalledSlotCount)
                + "; idleFrames=" + I(IdleFrames)
                + "; idleSteps=" + I(IdleStepsCommitted)
                + "; idleImages=" + I(IdlePublishedImages)
                + "; idleDispatchRuns=" + I(IdleDispatchRuns)
                + "; pendingDemandAfterIdle=" + U(PendingDemandAfterIdle)
                + "; genreChecked=" + I(GenreAuditChecked)
                + "; genreNeutral=" + GenreAuditNeutral
                + "; forbiddenNames=" + I(ForbiddenGenreNameCount)
                + "; traceMatchesDeclaration=" + TraceMatchesDeclaration
                + "; traceMismatchDetail=" + TraceMismatchDetail
                + "; inventoryChecked=" + I(InventoryAuditChecked)
                + "; inventoryNeutral=" + InventoryAuditNeutral
                + "; inventoryForbiddenNames=" + I(InventoryForbiddenNameCount)
                + "; componentChecked=" + I(ComponentAuditChecked)
                + "; componentNeutral=" + ComponentAuditNeutral
                + "; outstandingJobsBeforeTeardown=" + I(OutstandingJobsBeforeTeardown)
                + "; outstandingJobsAfterTeardown=" + I(OutstandingJobsAfterTeardown)
                + "; retainedResourcesAfterTeardown=" + I(RetainedResourcesAfterTeardown)
                + "; registryAfterTeardown=" + I(RegistryAfterTeardown);
        }

        /// <summary>
        /// The pipeline section of one run's own trace document, in the order the committed canonical document
        /// declares its keys, so the two can be compared as text (P-008, P-060).
        /// </summary>
        public IReadOnlyList<NarrativeTraceEntry> PipelineEntries()
        {
            IReadOnlyList<NarrativeTraceEntry> declared = NarrativeScenarioTrace.ExpectedPipeline();
            var entries = new List<NarrativeTraceEntry>(declared.Count);
            for (int i = 0; i < declared.Count; i++)
            {
                entries.Add(new NarrativeTraceEntry(declared[i].Key, ValueOf(declared[i].Key)));
            }

            return entries;
        }

        /// <summary>The observed value of one declared trace key, or a marker when this run did not record it.</summary>
        public string ValueOf(string key)
        {
            switch (key)
            {
                case "liveTargetCount": return I(LiveTargetCount);
                case "ineligibleTargetCount": return I(IneligibleTargetCount);
                case "derivedTargetCountAfterChapterOne": return I(DerivedTargetCountAfterChapterOne);
                case "chapterOneInstalledRows": return I(ChapterOneInstalledRows);
                case "chapterOneRetractedRows": return I(ChapterOneRetractedRows);
                case "migratedConversationSlotCount": return I(MigratedConversationSlotCount);
                case "chapterTwoInstalledRows": return I(ChapterTwoInstalledRows);
                case "publishedBindingRowCountAfterChapterTwo": return I(PublishedBindingRowCountAfterChapterTwo);
                case "compiledStageCount": return I(CompiledStageCount);
                case "compiledSystemCount": return I(CompiledSystemCount);
                case "forwardDerivationHadNoTargetChange": return ForwardDerivationHadNoTargetChange ? "true" : "false";
                case "spawnedBindingRowCount": return I(SpawnedBindingRowCount);
                case "spawnedBindingValue": return I(SpawnedBindingValue);
                case "publishedBindingRowCountAfterSpawn": return I(PublishedBindingRowCountAfterSpawn);
                case "recipeApplyCount": return I(RecipeApplyCount);
                case "admittedChoiceCount": return I(AdmittedChoiceCount);
                case "forwardedChoiceCount": return I(ForwardedChoiceCount);
                case "refusedChoiceCount": return I(RefusedChoiceCount);
                case "committedFactCount": return I(CommittedFactCount);
                case "duplicateMutationCount": return I(DuplicateMutationCount);
                case "gateDecisionCount": return I(GateDecisionCount);
                case "encounterHookCount": return I(EncounterHookCount);
                case "gateDecisionBeforeCommand": return I(GateDecisionBeforeCommand);
                case "gateDecisionAfterCommand": return I(GateDecisionAfterCommand);
                case "questFactValueAfterCommand": return I(QuestFactValueAfterCommand);
                case "questFactVersionAfterCommand": return I(QuestFactVersionAfterCommand);
                case "committedEventCountAfterCommand": return I(CommittedEventCountAfterCommand);
                case "committedEventSchemaAfterCommand": return CommittedEventSchemaAfterCommand;
                case "stepsAfterCommand": return U(StepsAfterCommand);
                case "stepsAfterDuplicate": return U(StepsAfterDuplicate);
                case "duplicatePumpSteps": return I(DuplicatePumpSteps);
                case "laneEpochAfterChapterOne": return U(LaneEpochAfterChapterOne);
                case "laneEpochAfterChapterTwo": return U(LaneEpochAfterChapterTwo);
                case "worldEpochAfterSpawn": return U(WorldEpochAfterSpawn);
                case "trailStepsAfterCommand": return I(TrailStepsAfterCommand);
                case "trailProjectedStepsAfterCommand": return I(TrailProjectedStepsAfterCommand);
                case "trailCommittedFactsAfterCommand": return I(TrailCommittedFactsAfterCommand);
                case "trailGateDecisionsAfterCommand": return I(TrailGateDecisionsAfterCommand);
                case "trailEncounterHooksAfterCommand": return I(TrailEncounterHooksAfterCommand);
                case "idleFrames": return I(IdleFrames);
                case "idleStepsCommitted": return I(IdleStepsCommitted);
                case "idleDispatchRuns": return I(IdleDispatchRuns);
                case "genreAuditChecked": return I(GenreAuditChecked);
                case "genreAuditNeutral": return GenreAuditNeutral ? "true" : "false";
                case "forbiddenGenreNameCount": return I(ForbiddenGenreNameCount);
                case "registeredNamesDigest": return NarrativeDigest.OfLines(NarrativeRegistrations.AllNames);
                default: return "<undeclared>";
            }
        }

        private static string I(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string U(ulong value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Full result of one narrative run: the named observations plus the facts they were computed from.</summary>
    public sealed class NarrativeScenarioResult
    {
        public NarrativeScenarioResult(IReadOnlyList<NarrativeStep> steps, NarrativeFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<NarrativeStep> Steps { get; }

        public NarrativeFacts Facts { get; }

        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " narrative checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " narrative check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>Mount payloads of the narrative scenario, carrying the configuration hash a mount must declare.</summary>
    public static class NarrativeMounts
    {
        /// <summary>
        /// O-03 mount of one provider instance at one scope. The declared configuration hash is the canonical hash of
        /// the effective configuration (schema defaults over the local patch), exactly as the composition applier
        /// recomputes it (P-020).
        /// </summary>
        public static CompositionEditPayload Mount(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope,
            ConfigDocument? schemaDefaults)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ConfigDocument local = ConfigDocument.Empty;
            ConfigDocument effective = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(
                    ConfigLayerOrigin.SchemaDefaults,
                    manifest.ConfigSchema.Id.Value,
                    schemaDefaults ?? ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, local),
            }).Value;

            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                local,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>O-02 creation of one scope under an existing parent, with an optional capability isolation set.</summary>
        public static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent, bool isolateCapabilities)
        {
            IsolationSet isolation = isolateCapabilities
                ? new IsolationSet(true, null)
                : new IsolationSet(false, null);

            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                isolation,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }
    }

    /// <summary>Runs the GC-010 narrative scenario against real modules only.</summary>
    public static class NarrativeScenario
    {
        /// <summary>Bounded temporary storage the slice's plans may reserve, in bytes.</summary>
        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes.</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>Conversation node the reference graph opens at (07 section 3.2's accepted choice node).</summary>
        private const int ChoiceNodeOrdinal = 1;

        /// <summary>The choice that accepts the chapter's offer and sets the permit fact.</summary>
        private const int PermitChoiceOrdinal = NarrativeDialogueRules.PermitChoice;

        /// <summary>
        /// Runs the slice with the hand-written generated-style catalog in this assembly, which is the same table
        /// shape the content compiler emits.
        /// </summary>
        public static NarrativeScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the generated-style narrative catalog was rejected: " + build.Describe());
            }

            var declarations = new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ChapterProvider(
                        NarrativeKeys.PluginTypeId(1UL),
                        NarrativeScenarioCatalog.PluginFactoryKey,
                        NarrativeScenarioCatalog.RecordSchema),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ChapterTwoProvider(
                        NarrativeKeys.PluginTypeId(2UL),
                        NarrativeScenarioCatalog.PluginFactoryKey,
                        NarrativeScenarioCatalog.RecordSchema),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ForwardProvider(
                        NarrativeKeys.PluginTypeId(4UL),
                        NarrativeScenarioCatalog.PluginFactoryKey,
                        NarrativeScenarioCatalog.RecordSchema),
                    ConfigDocument.Empty),
            };

            return Run(
                build.Catalog,
                declarations,
                NarrativeKeys.Key("narrative.absent.plugin.factory"),
                NarrativeKeys.PluginTypeId(3UL),
                NarrativeScenarioCatalog.Fingerprint());
        }

        /// <summary>
        /// Runs the slice against one catalog. <paramref name="declarations"/> are the generated-style declarations
        /// the three mounts resolve; <paramref name="absentFactoryKey"/> and <paramref name="absentPluginType"/> must
        /// be unregistered, so the P-009 miss stays observable; <paramref name="declaredFingerprint"/> is the
        /// fingerprint the catalog's declarations were published with (P-028).
        /// </summary>
        public static NarrativeScenarioResult Run(
            ImmutableCatalog catalog,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            FactoryKey absentFactoryKey,
            PluginTypeId absentPluginType,
            ContentHash declaredFingerprint)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            if (declarations == null || declarations.Count < 3)
            {
                throw new ArgumentException(
                    "the scenario mounts three providers, so it needs all three generated-style declarations.",
                    nameof(declarations));
            }

            return new Executor(catalog, declarations, absentFactoryKey, absentPluginType, declaredFingerprint).Run();
        }

        private sealed class Executor
        {
            private readonly ImmutableCatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly FactoryKey absentFactoryKey;
            private readonly PluginTypeId absentPluginType;
            private readonly ContentHash declaredFingerprint;
            private readonly List<NarrativeStep> steps = new List<NarrativeStep>();
            private readonly NarrativeFacts facts = new NarrativeFacts();

            private readonly IdSequence sessionSequence = new IdSequence(0x4E41525241545353UL);
            private readonly NarrativeRecipeApplier applier = new NarrativeRecipeApplier();
            private readonly NarrativeConversationNodeMigration nodeMigration = new NarrativeConversationNodeMigration();
            private readonly NarrativeConversationStatusMigration statusMigration =
                new NarrativeConversationStatusMigration();

            private UnityWorldHost? host;
            private NarrativeModule? module;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private PipelineDescriptorReport? descriptorReport;
            private WorldTimeDriver? time;
            private ulong operationSequence;
            private int registryBeforeCreate;
            private CommandEnvelope? choiceEnvelope;
            private string compileFailure = string.Empty;
            private string scopeFailure = string.Empty;
            private string seedFailure = string.Empty;

            public Executor(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                FactoryKey absentFactoryKey,
                PluginTypeId absentPluginType,
                ContentHash declaredFingerprint)
            {
                this.catalog = catalog;
                this.declarations = declarations;
                this.absentFactoryKey = absentFactoryKey;
                this.absentPluginType = absentPluginType;
                this.declaredFingerprint = declaredFingerprint;
            }

            public NarrativeScenarioResult Run()
            {
                CheckCatalogAndDeclarations();
                CreateWorldAndTargets();
                MountChapterOneAndPublish();
                ProveDerivedLayoutInEntities();
                MountChapterTwoAndPublish();
                ExecuteOneChoiceCommand();
                ProveDuplicateRequestCommitsNothing();
                PublishForwardProviderAndSpawn();
                ProveIdleWorldPerformsNoSteps();
                ProveGenreNeutrality();
                TearDownSafely();

                return new NarrativeScenarioResult(steps, facts);
            }

            // ------------------------------------------------------------------ 1. catalog and declarations

            private void CheckCatalogAndDeclarations()
            {
                const string name = "narrative-catalog-and-declarations";
                try
                {
                    CatalogLookup factory = catalog.Lookup(declarations[0].Manifest.FactoryKey);
                    CatalogLookup absent = catalog.Lookup(absentFactoryKey);
                    bool missReported = !absent.Found && absent.Code == DiagnosticCode.MissingDependency;
                    bool fingerprintAsDeclared = catalog.Fingerprint.Equals(declaredFingerprint);
                    facts.CatalogFingerprint = catalog.Fingerprint.ToHex();

                    var manifests = new CatalogManifestSource(catalog, declarations);
                    bool allAccepted = manifests.AcceptedCount == declarations.Count && manifests.Rejected.Count == 0;
                    bool unregisteredResolved = manifests.TryGetManifest(absentPluginType, out PluginManifest? resolved);

                    bool pass = fingerprintAsDeclared
                        && factory.Found
                        && factory.Factory != null
                        && factory.Factory.Kind == FactoryKind.PluginFactory
                        && missReported
                        && allAccepted
                        && !unregisteredResolved
                        && resolved == null;

                    steps.Add(new NarrativeStep(name, pass,
                        "fingerprint=" + catalog.Fingerprint.ToHex()
                        + "; fingerprintAsDeclared=" + fingerprintAsDeclared
                        + "; declaredFingerprint=" + declaredFingerprint.ToHex()
                        + "; acceptedDeclarations=" + manifests.AcceptedCount.ToString(CultureInfo.InvariantCulture)
                        + "; rejectedDeclarations=" + manifests.Rejected.Count.ToString(CultureInfo.InvariantCulture)
                        + "; factoryLookup=" + factory.Describe()
                        + "; unknownKeyLookup=" + absent.Describe()
                        + "; unregisteredResolved=" + (resolved != null)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 2. GC-007 ownership + GC-009 schedule

            /// <summary>
            /// GC-007's ownership validator and GC-009's compiler produce the one descriptor and schedule the world
            /// runs. It is part of the world step because a refusal here means the world cannot start at all.
            /// </summary>
            private bool CompileOwnershipAndSchedule()
            {
                try
                {
                    var kinds = new ScheduleDispatchKindTable()
                        .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                        .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

                    var manifests = new List<PluginManifest>();
                    for (int i = 0; i < declarations.Count; i++)
                    {
                        manifests.Add(declarations[i].Manifest);
                    }

                    descriptorReport = OwnershipSchedulePipeline.Build(
                        manifests,
                        kinds,
                        new NarrativeSlotMigrations());
                    return true;
                }
                catch (Exception exception)
                {
                    compileFailure = DescribeException(exception);
                    return false;
                }
            }

            // ------------------------------------------------------------------ 3. the real world and its targets

            private void CreateWorldAndTargets()
            {
                const string name = "narrative-world-and-live-targets";
                try
                {
                    if (!CompileOwnershipAndSchedule() || descriptorReport == null
                        || descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                    {
                        steps.Add(new NarrativeStep(name, false,
                            "the ownership/schedule descriptor was not built: "
                            + (compileFailure.Length != 0
                                ? compileFailure
                                : (descriptorReport != null ? descriptorReport.Describe() : "<none>"))));
                        return;
                    }

                    CompiledSchedule schedule = descriptorReport.Compilation!.Schedule!;
                    facts.CompiledStageCount = schedule.StageCount;
                    facts.CompiledSystemCount = schedule.SystemCount;

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId world = NextSession();
                    facts.WorldSession = world.Session.ToString();

                    WorldCreateRequest request = NarrativeRegistration.CommandDrivenRequest(
                        world,
                        NextOperation(world),
                        ContentHash.Empty);
                    UnityWorldRegistration registration = NarrativeRegistration.Create(
                        descriptorReport.Adaptation,
                        NarrativeRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(
                        request,
                        registration,
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    facts.RegistryAfterCreate = UnityWorldRegistry.Count;

                    if (!created || host == null)
                    {
                        steps.Add(new NarrativeStep(name, false,
                            "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = NarrativeModule.Attach(host, schedule);
                    facts.MessagePlanePresent = host.Messages != null;

                    registry = new TargetRegistry(world, 16);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        NarrativeRecipes.Catalog(applier),
                        new MigrationRegistry(new List<ISlotMigration> { nodeMigration, statusMigration }),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    // The control lane owns the scope tree, so it exists before any scope is created; the composition
                    // publication series starts at the world's initial assembly (05 section 2, P-006).
                    lane = CompositionHost.CreateDefault(
                        world,
                        NarrativeKeys.RootScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly);
                    bridge = new WorldCompositionBridge(host, lane, publisher);

                    bool scopes = BuildScopeTree();

                    bool seeded = scopes
                        && SeedTarget(NarrativeKeys.Mara, NarrativeKeys.VillageScope, NarrativeKeys.VillagerRecipe)
                        && SeedTarget(NarrativeKeys.GateEast, NarrativeKeys.VillageScope, NarrativeKeys.QuestGateRecipe)
                        && SeedTarget(NarrativeKeys.CrowdProp, NarrativeKeys.VillageScope, NarrativeKeys.DecorativeCrowdRecipe)
                        && SeedTarget(NarrativeKeys.EncounterOak, NarrativeKeys.GroveScope, NarrativeKeys.QuestEncounterRecipe)
                        && SeedTarget(NarrativeKeys.Display, NarrativeKeys.MuseumScope, NarrativeKeys.VillagerRecipe)
                        && SeedTarget(NarrativeKeys.Sailor, NarrativeKeys.HarborScope, NarrativeKeys.VillagerRecipe)
                        && SeedTarget(NarrativeKeys.QuestLedger, NarrativeKeys.RootScope, NarrativeKeys.QuestLedgerRecipe);

                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        NarrativeCompositionValueSource(),
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, NarrativeKeys.Issuer),
                        new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);
                    module.Time = time;

                    bool clockRegistered = time.Clocks.TryRegister(
                        new PluginClockSpec(
                            NarrativeKeys.DomainClock,
                            "narrative.clock.domain",
                            PluginClockKind.DomainExplicit,
                            WakePausePolicy.Defer,
                            true),
                        out DiagnosticCode _);

                    // The durable facts: the ledger holds its own slots, seeded at the declared versions, and the
                    // conversation state of the one target a chapter will cover is seeded one version behind so the
                    // registered migration really runs (P-029, P-032).
                    bool factSlots =
                        SeedFactSlots()
                        && SeedConversationAtVersionOne(NarrativeKeys.Mara);

                    facts.LiveTargetCount = targets.Count;
                    facts.MappedTargetCount = module.MappedTargetCount;
                    facts.IneligibleTargetCount = IneligibleTargetCount();

                    bool laneJoined = AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch);

                    bool pass = scopes
                        && seeded
                        && factSlots
                        && lane.Committed.Scopes.Count == 7
                        && clockRegistered
                        && facts.MessagePlanePresent
                        && laneJoined
                        && facts.LiveTargetCount == 7
                        && facts.MappedTargetCount == 7
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.Lifecycle == WorldLifecycleState.Running;

                    steps.Add(new NarrativeStep(name, pass,
                        "session=" + world.Session.ToString()
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + facts.RegistryAfterCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + facts.LiveTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; ineligibleTargets=" + facts.IneligibleTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; mappedTargets=" + facts.MappedTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; plane=" + facts.MessagePlanePresent
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; laneRevision=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; scopeTargetsSeeded=" + seeded
                        + "; factSlotsSeeded=" + factSlots
                        + "; laneJoined=" + laneJoined
                        + "; clockRegistered=" + clockRegistered
                        + "; lifecycle=" + host.Lifecycle
                        + DescribeFailures()));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            /// <summary>
            /// Builds the seven-scope chapter tree of 07 section 3.1 through the control lane, including the museum's
            /// capability isolation boundary (P-010, P-016).
            /// </summary>
            /// <summary>
            /// Builds the seven-scope chapter tree of 07 section 3.1 through the control lane, including the museum's
            /// capability isolation boundary (P-010, P-016).
            /// </summary>
            private bool BuildScopeTree()
            {
                if (lane == null)
                {
                    return false;
                }

                bool created =
                    CreateScope(NarrativeKeys.ChapterOneScope, NarrativeKeys.RootScope, false)
                    && CreateScope(NarrativeKeys.ChapterTwoScope, NarrativeKeys.RootScope, false)
                    && CreateScope(NarrativeKeys.VillageScope, NarrativeKeys.ChapterOneScope, false)
                    && CreateScope(NarrativeKeys.GroveScope, NarrativeKeys.ChapterOneScope, false)
                    && CreateScope(NarrativeKeys.MuseumScope, NarrativeKeys.ChapterOneScope, true)
                    && CreateScope(NarrativeKeys.HarborScope, NarrativeKeys.ChapterTwoScope, false);

                if (!created || lane.Committed.Scopes.Count != 7)
                {
                    scopeFailure = "the scope tree is "
                        + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + " scope(s), not the seven the reference composition declares (07 section 3.1)";
                    return false;
                }

                return true;
            }

            private bool CreateScope(ScopeId scope, ScopeId parent, bool isolateCapabilities)
            {
                if (lane == null)
                {
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(
                    NarrativeMounts.ScopeCreate(scope, parent, isolateCapabilities),
                    NextOperation(lane.World),
                    lane.Committed.Revision);

                if (!admission.Staged)
                {
                    scopeFailure = "scope " + scope.ToString() + " was refused: " + admission.Code
                        + ": " + DescribeDiagnostics(admission);
                    return false;
                }

                lane.Drain();
                return true;
            }

            private bool SeedTarget(TargetId target, ScopeId scope, DefinitionRef recipe)
            {
                if (seeder == null || module == null)
                {
                    return false;
                }

                if (!seeder.TrySeed(target, scope, recipe, out _, out DiagnosticCode code, out string detail))
                {
                    seedFailure = "target " + target.ToString() + " was refused: " + code + ": " + detail;
                    return false;
                }

                if (!seeder.TryGetEntity(target, out Entity entity))
                {
                    return false;
                }

                module.MapTarget(target, entity);
                return true;
            }

            /// <summary>
            /// Seeds the ledger's durable fact slots at the declared versions, and the trail's counters, so the
            /// quest owner writes state it owns rather than state it invents (P-032).
            /// </summary>
            private bool SeedFactSlots()
            {
                if (seeder == null || module == null)
                {
                    return false;
                }

                bool ok = true;
                for (int i = 0; i < RulesNarrativeFacts.DeclaredFactKeys.Count; i++)
                {
                    string factKey = RulesNarrativeFacts.DeclaredFactKeys[i];
                    if (!RulesNarrativeFacts.TryGetFactSlotTag(factKey, out string slotTag))
                    {
                        ok = false;
                        continue;
                    }

                    ok &= seeder.TrySeedSlot(
                        NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner, NarrativeKeys.FactValueSlot(slotTag),
                        NarrativeKeys.QuestDomain.Version, RulesNarrativeFacts.InitialValue, out DiagnosticCode _, out string _);
                    ok &= seeder.TrySeedSlot(
                        NarrativeKeys.QuestLedger, NarrativeKeys.QuestOwner, NarrativeKeys.FactVersionSlot(slotTag),
                        NarrativeKeys.QuestDomain.Version, RulesNarrativeFacts.InitialVersion, out DiagnosticCode _, out string _);
                }

                ok &= seeder.TrySeedSlot(
                    NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailStepsSlot,
                    NarrativeKeys.TrailDomain.Version, 0, out DiagnosticCode _, out string _);
                ok &= seeder.TrySeedSlot(
                    NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailProjectedSlot,
                    NarrativeKeys.TrailDomain.Version, 0, out DiagnosticCode _, out string _);
                ok &= seeder.TrySeedSlot(
                    NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailFactsSlot,
                    NarrativeKeys.TrailDomain.Version, 0, out DiagnosticCode _, out string _);
                ok &= seeder.TrySeedSlot(
                    NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailHooksSlot,
                    NarrativeKeys.TrailDomain.Version, 0, out DiagnosticCode _, out string _);
                ok &= seeder.TrySeedSlot(
                    NarrativeKeys.QuestLedger, NarrativeKeys.TrailOwner, NarrativeKeys.TrailGateDecisionsSlot,
                    NarrativeKeys.TrailDomain.Version, 0, out DiagnosticCode _, out string _);

                // The trail lives on the ledger target: the one entity that exists for the whole run.
                if (seeder.TryGetEntity(NarrativeKeys.QuestLedger, out Entity ledger))
                {
                    module.SetRootEntity(ledger);
                }

                if (!ok)
                {
                    seedFailure = "seeding the ledger's fact or trail slots failed";
                }

                return ok;
            }

            /// <summary>
            /// Seeds one target's conversation state one schema version behind the descriptor, so the next publication
            /// must run the registered migration on bounded scratch instead of zero-initializing live state (P-029).
            /// </summary>
            private bool SeedConversationAtVersionOne(TargetId target)
            {
                if (seeder == null)
                {
                    return false;
                }

                return seeder.TrySeedSlot(
                        target, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationNodeSlot,
                        1U, 0, out DiagnosticCode _, out string _)
                    && seeder.TrySeedSlot(
                        target, NarrativeKeys.DialogueOwner, NarrativeKeys.ConversationStatusSlot,
                        1U, NarrativeConversationStatus.Idle, out DiagnosticCode _, out string _);
            }

            /// <summary>
            /// Targets whose recipe no chapter selects: the decorative crowd prop and the world-level ledger. They
            /// keep their base recipe and receive no derived row (P-015).
            /// </summary>
            private int IneligibleTargetCount()
            {
                if (targets == null)
                {
                    return 0;
                }

                int eligible = 0;
                IReadOnlyList<LiveTarget> live = targets.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    if (NarrativeKeys.IsChapterEligibleRecipe(live[i].Recipe))
                    {
                        eligible++;
                    }
                }

                return live.Count - eligible;
            }

            // ------------------------------------------------------------------ 4. mount chapter one

            private void MountChapterOneAndPublish()
            {
                const string name = "narrative-chapter-one-mounted-and-published";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    OperationId operation = NextOperation(host.World);
                    CompositionEditPayload payload = NarrativeMounts.Mount(
                        declarations[0].Manifest,
                        NarrativeKeys.ChapterOneInstall,
                        NarrativeKeys.ChapterOneScope,
                        declarations[0].SchemaDefaults);

                    EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    DerivedAssemblyReport report = pipeline.PublishDerived(operation);

                    facts.LaneRevisionAfterChapterOne = lane.Committed.Revision.Value;
                    facts.LaneEpochAfterChapterOne = lane.Committed.Epoch.Value;
                    facts.WorldEpochAfterChapterOne = host.CurrentEpoch.Value;
                    facts.LaneJoinedAfterChapterOne = report.CountersJoined;
                    facts.DerivedTargetCountAfterChapterOne = 0;
                    if (report.Derivation != null)
                    {
                        for (int i = 0; i < report.Derivation.Assemblies.Count; i++)
                        {
                            if (!report.Derivation.Assemblies[i].IsBaseOnly)
                            {
                                facts.DerivedTargetCountAfterChapterOne++;
                            }
                        }
                    }

                    facts.DerivedContributionCountAfterChapterOne =
                        report.Derivation != null ? report.Derivation.Contributions.Count : 0;
                    facts.ChapterOneInstalledRows = report.InstalledRows;
                    facts.ChapterOneRetractedRows = report.RetractedRows;
                    facts.MigratedConversationSlotCount = report.MigratedSlots;
                    facts.MigrationInvocations = nodeMigration.Invocations + statusMigration.Invocations;
                    facts.PublicationOutcomeAfterChapterOne =
                        report.Publication != null ? report.Publication.Outcome.ToString() : "none";

                    bool pass = admission.Staged
                        && published.Count == 1
                        && published[0].Outcome == Outcome.Published
                        && report.Outcome == DerivedAssemblyOutcome.Published
                        && report.Succeeded
                        && facts.DerivedTargetCountAfterChapterOne == 3
                        && facts.DerivedContributionCountAfterChapterOne == 4
                        && facts.ChapterOneInstalledRows == 4
                        && facts.ChapterOneRetractedRows == 0
                        && facts.MigratedConversationSlotCount == 2
                        && facts.MigrationInvocations >= 2
                        && facts.LaneEpochAfterChapterOne == 2UL
                        && facts.WorldEpochAfterChapterOne == 2UL
                        && facts.LaneJoinedAfterChapterOne;

                    steps.Add(new NarrativeStep(name, pass, report.Describe()
                        + "; admission=" + admission.Kind
                        + "; drained=" + published.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lane=" + facts.LaneRevisionAfterChapterOne.ToString(CultureInfo.InvariantCulture)
                        + "/" + facts.LaneEpochAfterChapterOne.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + facts.LaneJoinedAfterChapterOne
                        + "; migrations=" + facts.MigrationInvocations.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 5. the derived layout in real storage

            private void ProveDerivedLayoutInEntities()
            {
                const string name = "narrative-derived-layout-in-entities";
                try
                {
                    if (publisher == null || host == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "no publisher"));
                        return;
                    }

                    IReadOnlyList<CapabilityBinding> maraRows = publisher.ReadBindingRows(NarrativeKeys.Mara);
                    IReadOnlyList<CapabilityBinding> gateRows = publisher.ReadBindingRows(NarrativeKeys.GateEast);
                    IReadOnlyList<CapabilityBinding> encounterRows =
                        publisher.ReadBindingRows(NarrativeKeys.EncounterOak);
                    IReadOnlyList<CapabilityBinding> crowdRows = publisher.ReadBindingRows(NarrativeKeys.CrowdProp);
                    IReadOnlyList<CapabilityBinding> museumRows = publisher.ReadBindingRows(NarrativeKeys.Display);
                    IReadOnlyList<CapabilityBinding> sailorRows = publisher.ReadBindingRows(NarrativeKeys.Sailor);

                    facts.MaraBindingRowCount = maraRows.Count;
                    facts.GateBindingRowCount = gateRows.Count;
                    facts.EncounterBindingRowCount = encounterRows.Count;
                    facts.CrowdBindingRowCount = crowdRows.Count;
                    facts.MuseumBindingRowCount = museumRows.Count;
                    facts.SailorBindingRowCountBeforeChapterTwo = sailorRows.Count;

                    facts.MaraDialogueBindingValue = ValueOfRow(maraRows, NarrativeKeys.DialogueBinding);
                    facts.MaraChoiceBindingValue = ValueOfRow(maraRows, NarrativeKeys.ChoiceBinding);
                    facts.GateBindingValue = ValueOfRow(gateRows, NarrativeKeys.GateBinding);
                    facts.EncounterBindingValue = ValueOfRow(encounterRows, NarrativeKeys.EncounterBinding);
                    facts.MaraBindingIsActive = AllRowsActive(maraRows);
                    facts.MaraStampEpoch = StampEpochOf(NarrativeKeys.Mara);

                    // The publication migrated the seeded version-1 conversation state on bounded scratch: the
                    // chapter's opening node at the descriptor's schema version (P-029, P-032).
                    if (NarrativeState.TryRead(
                            host.EntityWorld.EntityManager,
                            registry!.EntityOf(NarrativeKeys.Mara),
                            NarrativeKeys.DialogueOwner,
                            NarrativeKeys.ConversationNodeSlot,
                            out int node,
                            out uint version))
                    {
                        facts.ConversationNodeAfterMigration = node;
                        facts.ConversationSchemaVersionAfterMigration = version;
                    }

                    bool pass = facts.MaraBindingRowCount == 2
                        && facts.MaraDialogueBindingValue == 1
                        && facts.MaraChoiceBindingValue == 1
                        && facts.MaraBindingIsActive
                        && facts.GateBindingRowCount == 1
                        && facts.GateBindingValue == 1
                        && facts.EncounterBindingRowCount == 1
                        && facts.EncounterBindingValue == 1
                        && facts.CrowdBindingRowCount == 0
                        && facts.MuseumBindingRowCount == 0
                        && facts.SailorBindingRowCountBeforeChapterTwo == 0
                        && facts.MaraStampEpoch == facts.WorldEpochAfterChapterOne
                        && facts.ConversationNodeAfterMigration == ChoiceNodeOrdinal
                        && facts.ConversationSchemaVersionAfterMigration == NarrativeKeys.ConversationDomain.Version;

                    steps.Add(new NarrativeStep(name, pass,
                        "maraRows=" + facts.MaraBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; maraDialogue=" + facts.MaraDialogueBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; maraChoice=" + facts.MaraChoiceBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; maraStampEpoch=" + facts.MaraStampEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; gateRows=" + facts.GateBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; encounterRows=" + facts.EncounterBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; crowdRows=" + facts.CrowdBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; museumRows=" + facts.MuseumBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; sailorRowsBeforeChapterTwo="
                        + facts.SailorBindingRowCountBeforeChapterTwo.ToString(CultureInfo.InvariantCulture)
                        + "; conversationNode=" + facts.ConversationNodeAfterMigration.ToString(CultureInfo.InvariantCulture)
                        + "; conversationVersion="
                        + facts.ConversationSchemaVersionAfterMigration.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 6. mount chapter two (a sibling branch)

            private void MountChapterTwoAndPublish()
            {
                const string name = "narrative-chapter-two-mounted-and-published";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    OperationId operation = NextOperation(host.World);
                    CompositionEditPayload payload = NarrativeMounts.Mount(
                        declarations[1].Manifest,
                        NarrativeKeys.ChapterTwoInstall,
                        NarrativeKeys.ChapterTwoScope,
                        declarations[1].SchemaDefaults);

                    EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    DerivedAssemblyReport report = pipeline.PublishDerived(operation);

                    facts.LaneEpochAfterChapterTwo = lane.Committed.Epoch.Value;
                    facts.WorldEpochAfterChapterTwo = host.CurrentEpoch.Value;
                    facts.ChapterTwoInstalledRows = report.InstalledRows;

                    IReadOnlyList<CapabilityBinding> sailorRows = publisher.ReadBindingRows(NarrativeKeys.Sailor);
                    facts.SailorBindingRowCount = sailorRows.Count;
                    facts.SailorDialogueBindingValue = ValueOfRow(sailorRows, NarrativeKeys.DialogueBinding);
                    facts.PublishedBindingRowCountAfterChapterTwo = publisher.Published.BindingRowCount;

                    // The sibling chapter owns the harbor subtree only: chapter one's rows are untouched, and the
                    // museum target stays isolated from both (P-013, P-016).
                    bool pass = admission.Staged
                        && published.Count == 1
                        && published[0].Outcome == Outcome.Published
                        && report.Outcome == DerivedAssemblyOutcome.Published
                        && facts.ChapterTwoInstalledRows == 2
                        && facts.SailorBindingRowCount == 2
                        && facts.SailorDialogueBindingValue == 2
                        && facts.PublishedBindingRowCountAfterChapterTwo == 6
                        && facts.LaneEpochAfterChapterTwo == 3UL
                        && facts.WorldEpochAfterChapterTwo == 3UL
                        && report.CountersJoined
                        && publisher.ReadBindingRows(NarrativeKeys.CrowdProp).Count == 0
                        && publisher.ReadBindingRows(NarrativeKeys.Display).Count == 0;

                    steps.Add(new NarrativeStep(name, pass, report.Describe()
                        + "; sailorRows=" + facts.SailorBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; sailorDialogue=" + facts.SailorDialogueBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; publishedRows="
                        + facts.PublishedBindingRowCountAfterChapterTwo.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 7. one consumed choice command

            private void ExecuteOneChoiceCommand()
            {
                const string name = "narrative-one-choice-command-committed";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world or its time driver is missing"));
                        return;
                    }

                    WorldMessagePlane? plane = host.Messages;
                    if (plane == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world has no message plane"));
                        return;
                    }

                    facts.GateDecisionBeforeCommand = NarrativeState.ReadOrDefault(
                        host.EntityWorld.EntityManager,
                        registry!.EntityOf(NarrativeKeys.GateEast),
                        NarrativeKeys.GateOwner,
                        NarrativeKeys.GateDecisionSlot,
                        NarrativeGateRules.Closed);

                    var envelope = new CommandEnvelope(
                        NextOperation(host.World),
                        NarrativeKeys.ChoiceRoute,
                        NarrativeKeys.Mara,
                        NarrativeKeys.ChoiceCommandSchema,
                        null,
                        new FrozenPayload(NarrativePayloadCodec.EncodeChoice(
                            new NarrativeChoice(ChoiceNodeOrdinal, PermitChoiceOrdinal))));
                    choiceEnvelope = envelope;

                    CommandAdmissionReceipt receipt = host.Submit(envelope);
                    facts.CommandAdmitted = receipt.Admitted;
                    facts.CommandAdmissionKind = receipt.Result.Kind.ToString();
                    if (receipt.Admitted)
                    {
                        facts.AdmittedChoiceCount = 1;
                    }

                    TimeFrameReport frame = time.PumpFrame(NarrativeScenarioTrace.IdleTicksPerFrame);

                    facts.StepsAfterCommand = host.CurrentStep.Value;
                    facts.ForwardedChoiceCount = module.ForwardedChoices;
                    facts.RefusedChoiceCount = module.RefusedChoices;
                    facts.CommittedFactCount = module.CommittedFacts;
                    facts.DuplicateMutationCount = module.DuplicateMutations;
                    facts.GateDecisionCount = module.GateDecisions;
                    facts.EncounterHookCount = module.EncounterHooks;

                    EntityManager entityManager = host.EntityWorld.EntityManager;
                    facts.GateDecisionAfterCommand = NarrativeState.ReadOrDefault(
                        entityManager, registry.EntityOf(NarrativeKeys.GateEast),
                        NarrativeKeys.GateOwner, NarrativeKeys.GateDecisionSlot, NarrativeGateRules.Closed);
                    facts.GateEvaluatedFactVersion = NarrativeState.ReadOrDefault(
                        entityManager, registry.EntityOf(NarrativeKeys.GateEast),
                        NarrativeKeys.GateOwner, NarrativeKeys.GateEvaluatedVersionSlot, 0);
                    facts.QuestFactValueAfterCommand = NarrativeState.ReadOrDefault(
                        entityManager, registry.EntityOf(NarrativeKeys.QuestLedger),
                        NarrativeKeys.QuestOwner, NarrativeKeys.BridgePermitValueSlot, -1);
                    facts.QuestFactVersionAfterCommand = NarrativeState.ReadOrDefault(
                        entityManager, registry.EntityOf(NarrativeKeys.QuestLedger),
                        NarrativeKeys.QuestOwner, NarrativeKeys.BridgePermitVersionSlot, -1);
                    facts.EncounterStatusAfterCommand = NarrativeState.ReadOrDefault(
                        entityManager, registry.EntityOf(NarrativeKeys.EncounterOak),
                        NarrativeKeys.EncounterOwner, NarrativeKeys.EncounterStatusSlot,
                        NarrativeEncounterStatus.Idle);

                    facts.TrailStepsAfterCommand = module.ReadTrail(NarrativeKeys.TrailStepsSlot);
                    facts.TrailProjectedStepsAfterCommand = module.ReadTrail(NarrativeKeys.TrailProjectedSlot);
                    facts.TrailCommittedFactsAfterCommand = module.ReadTrail(NarrativeKeys.TrailFactsSlot);
                    facts.TrailGateDecisionsAfterCommand = module.ReadTrail(NarrativeKeys.TrailGateDecisionsSlot);
                    facts.TrailEncounterHooksAfterCommand = module.ReadTrail(NarrativeKeys.TrailHooksSlot);
                    facts.StepGroupDispatchRuns = host.StepGroup.DispatchRunCount;
                    facts.StepGroupDispatchedEntries = host.StepGroup.TotalDispatchedCount;

                    // One committed choice publishes exactly the two results it produced: the accepted choice and the
                    // gate decision the fact caused (P-044, P-045).
                    CommittedEventPage page = plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8);
                    facts.CommittedEventCountAfterCommand = page.Events.Count;
                    for (int i = 0; i < page.Events.Count; i++)
                    {
                        if (i != 0)
                        {
                            facts.CommittedEventSchemaAfterCommand += "|";
                        }

                        facts.CommittedEventSchemaAfterCommand += Id128Codec.ToHex(page.Events[i].Schema.Id.Value);
                    }

                    if (page.Events.Count > 0)
                    {
                        facts.CommittedEventStepAfterCommand = page.Events[0].Step.Value;
                        facts.CommittedEventEpochAfterCommand = page.Events[0].Epoch.Value;
                    }

                    facts.LedgerCommittedCount = plane.Requests.CommittedCount;
                    facts.LedgerPendingCount = plane.Requests.PendingCount;
                    bridge!.SyncStepFromWorld();

                    bool pass = receipt.Admitted
                        && frame.StepsCommitted == 1UL
                        && facts.StepsAfterCommand == 1UL
                        && facts.ForwardedChoiceCount == 1
                        && facts.RefusedChoiceCount == 0
                        && facts.CommittedFactCount == 1
                        && facts.DuplicateMutationCount == 0
                        && facts.GateDecisionCount == 1
                        && facts.EncounterHookCount == 1
                        && facts.GateDecisionBeforeCommand == NarrativeGateRules.Closed
                        && facts.GateDecisionAfterCommand == NarrativeGateRules.Open
                        && facts.GateEvaluatedFactVersion == RulesNarrativeFacts.NextVersion(RulesNarrativeFacts.InitialVersion)
                        && facts.QuestFactValueAfterCommand == RulesNarrativeFacts.True
                        && facts.QuestFactVersionAfterCommand == RulesNarrativeFacts.NextVersion(RulesNarrativeFacts.InitialVersion)
                        && facts.CommittedEventCountAfterCommand == 2
                        && facts.CommittedEventStepAfterCommand == 1UL
                        && facts.CommittedEventEpochAfterCommand == facts.WorldEpochAfterChapterTwo
                        && facts.LedgerCommittedCount == 1
                        && facts.LedgerPendingCount == 0
                        && facts.EncounterStatusAfterCommand == NarrativeEncounterStatus.Active
                        && facts.TrailStepsAfterCommand == 1
                        && facts.TrailProjectedStepsAfterCommand == 1
                        && facts.TrailCommittedFactsAfterCommand == 1
                        && facts.TrailGateDecisionsAfterCommand == 1
                        && facts.TrailEncounterHooksAfterCommand == 1
                        && host.PendingDemand == 0UL
                        && !host.Driver.IsFaulted;

                    steps.Add(new NarrativeStep(name, pass,
                        "admitted=" + receipt.Admitted
                        + "; admission=" + facts.CommandAdmissionKind
                        + "; steps=" + facts.StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; forwarded=" + facts.ForwardedChoiceCount.ToString(CultureInfo.InvariantCulture)
                        + "; refused=" + facts.RefusedChoiceCount.ToString(CultureInfo.InvariantCulture)
                        + "; facts=" + facts.CommittedFactCount.ToString(CultureInfo.InvariantCulture)
                        + "; gateBefore=" + facts.GateDecisionBeforeCommand.ToString(CultureInfo.InvariantCulture)
                        + "; gateAfter=" + facts.GateDecisionAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; gateEvaluatedFactVersion="
                        + facts.GateEvaluatedFactVersion.ToString(CultureInfo.InvariantCulture)
                        + "; questFact=" + facts.QuestFactValueAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "@v" + facts.QuestFactVersionAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; events=" + facts.CommittedEventCountAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; eventSchemas=" + facts.CommittedEventSchemaAfterCommand
                        + "; ledger=" + facts.LedgerCommittedCount.ToString(CultureInfo.InvariantCulture)
                        + "/" + facts.LedgerPendingCount.ToString(CultureInfo.InvariantCulture)
                        + "; encounter=" + facts.EncounterStatusAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; trail=" + facts.TrailStepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "/" + facts.TrailProjectedStepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + host.PendingDemand.ToString(CultureInfo.InvariantCulture)
                        + "; faulted=" + host.Driver.IsFaulted));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 8. the duplicate request

            private void ProveDuplicateRequestCommitsNothing()
            {
                const string name = "narrative-duplicate-request-commits-nothing-new";
                try
                {
                    if (host == null || time == null || module == null || choiceEnvelope == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world or its admitted choice is missing"));
                        return;
                    }

                    CommandEnvelope admittedChoice = choiceEnvelope;
                    WorldMessagePlane plane = host.Messages!;
                    CommandAdmissionReceipt receipt = host.Submit(admittedChoice);
                    bool retransmitted = receipt.Result.Kind == RequestResultKind.Committed;

                    TimeFrameReport frame = time.PumpFrame(NarrativeScenarioTrace.IdleTicksPerFrame);

                    facts.StepsAfterDuplicate = host.CurrentStep.Value;
                    facts.DuplicatePumpSteps = (int)frame.StepsCommitted;
                    facts.CommittedEventCountAfterDuplicate =
                        plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 8).Events.Count;

                    // A duplicate request key returns its recorded result instead of executing twice, so the world
                    // commits no further step and no second result appears (P-037, P-050).
                    bool pass = retransmitted
                        && facts.DuplicatePumpSteps == 0
                        && facts.StepsAfterDuplicate == 1UL
                        && facts.CommittedEventCountAfterDuplicate == facts.CommittedEventCountAfterCommand
                        && module.CommittedFacts == 1
                        && module.GateDecisions == 1
                        && !host.Driver.IsFaulted;

                    steps.Add(new NarrativeStep(name, pass,
                        "retransmitted=" + retransmitted
                        + "; admission=" + receipt.Result.Kind
                        + "; pumpSteps=" + facts.DuplicatePumpSteps.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + facts.StepsAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                        + "; events=" + facts.CommittedEventCountAfterDuplicate.ToString(CultureInfo.InvariantCulture)
                        + "; facts=" + module.CommittedFacts.ToString(CultureInfo.InvariantCulture)
                        + "; gates=" + module.GateDecisions.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 9. the forward provider and the spawn

            private void PublishForwardProviderAndSpawn()
            {
                const string name = "narrative-forward-provider-and-spawned-target";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    OperationId operation = NextOperation(host.World);
                    CompositionEditPayload payload = NarrativeMounts.Mount(
                        declarations[2].Manifest,
                        NarrativeKeys.ForwardInstall,
                        NarrativeKeys.ChapterOneScope,
                        declarations[2].SchemaDefaults);

                    EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    facts.LaneEpochAfterForward = lane.Committed.Epoch.Value;

                    DerivedAssemblyReport forward = pipeline.PublishDerived(operation);
                    facts.ForwardDerivationHadNoTargetChange = forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    // The forward publication carries no derivable target change, so the world's assembly for it is
                    // the spawn: both counters end on the same value (P-006, P-024).
                    DerivedAssemblyReport spawn = pipeline.PublishSpawn(
                        NextOperation(host.World),
                        NarrativeKeys.FutureVillager,
                        NarrativeKeys.VillagerRecipe,
                        NarrativeKeys.VillageScope);

                    facts.WorldEpochAfterSpawn = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterSpawn = spawn.CountersJoined;
                    facts.RecipeApplyCount = applier.AppliedCount;
                    facts.RecipeInstalledSlotCount = applier.InstalledSlotCount;

                    bool registered = targets.TryRegister(
                        NarrativeKeys.FutureVillager,
                        NarrativeKeys.VillageScope,
                        NarrativeKeys.VillagerRecipe,
                        out DiagnosticCode _, out string _);

                    if (publisher.Registry.TryResolveTarget(NarrativeKeys.FutureVillager, out _, out Entity entity))
                    {
                        IReadOnlyList<CapabilityBinding> rows =
                            publisher.ReadBindingRows(NarrativeKeys.FutureVillager);
                        facts.SpawnedBindingRowCount = rows.Count;
                        if (rows.Count > 0)
                        {
                            facts.SpawnedBindingValue = rows[0].Value;
                        }

                        EntityManager entityManager = host.EntityWorld.EntityManager;
                        if (entityManager.HasComponent<AssemblyStamp>(entity))
                        {
                            AssemblyStamp stamp = entityManager.GetComponentData<AssemblyStamp>(entity);
                            facts.SpawnedStampEpoch = stamp.AssemblyEpoch;
                            facts.SpawnedStampPublished = stamp.Published != 0U;
                        }

                        module!.MapTarget(NarrativeKeys.FutureVillager, entity);
                    }

                    facts.SpawnedTargetInPublishedView = publisher.Published.Bindings.HasTarget(NarrativeKeys.FutureVillager);
                    facts.PublishedBindingRowCountAfterSpawn = publisher.Published.BindingRowCount;
                    facts.ChapterTwoBindingRowCountInPublishedView =
                        publisher.ReadBindingRows(NarrativeKeys.Sailor).Count;

                    bool pass = admission.Staged
                        && published.Count == 1
                        && published[0].Outcome == Outcome.Published
                        && facts.ForwardDerivationHadNoTargetChange
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.IsSpawn
                        && facts.LaneEpochAfterForward == 4UL
                        && facts.WorldEpochAfterSpawn == 4UL
                        && facts.CountersJoinedAfterSpawn
                        && facts.SpawnedBindingRowCount == 2
                        && facts.SpawnedBindingValue == 1
                        && facts.SpawnedStampEpoch == facts.WorldEpochAfterSpawn
                        && facts.SpawnedStampPublished
                        && facts.SpawnedTargetInPublishedView
                        && facts.PublishedBindingRowCountAfterSpawn == 8
                        && facts.ChapterTwoBindingRowCountInPublishedView == 2
                        && facts.RecipeApplyCount == 1
                        && registered;

                    steps.Add(new NarrativeStep(name, pass,
                        "forward=" + forward.Outcome
                        + "; noTargetChange=" + facts.ForwardDerivationHadNoTargetChange
                        + "; spawn=" + spawn.Describe()
                        + "; laneEpoch=" + facts.LaneEpochAfterForward.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + facts.WorldEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + facts.CountersJoinedAfterSpawn
                        + "; spawnedRows=" + facts.SpawnedBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; spawnedValue=" + facts.SpawnedBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; publishedRows="
                        + facts.PublishedBindingRowCountAfterSpawn.ToString(CultureInfo.InvariantCulture)
                        + "; recipeApplies=" + facts.RecipeApplyCount.ToString(CultureInfo.InvariantCulture)
                        + "; indexRegistered=" + registered));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 10. an idle world performs no step

            private void ProveIdleWorldPerformsNoSteps()
            {
                const string name = "narrative-idle-world-performs-zero-steps";
                try
                {
                    if (host == null || time == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "no time driver"));
                        return;
                    }

                    ulong stepBefore = host.CurrentStep.Value;
                    int imagesBefore = host.Publications.PublishedCount;
                    int runsBefore = host.StepGroup.DispatchRunCount;

                    ulong committed = 0UL;
                    for (int i = 0; i < NarrativeScenarioTrace.IdleFrames; i++)
                    {
                        TimeFrameReport frame = time.PumpFrame(NarrativeScenarioTrace.IdleTicksPerFrame);
                        committed += frame.StepsCommitted;
                    }

                    facts.IdleFrames = NarrativeScenarioTrace.IdleFrames;
                    facts.IdleStepsCommitted = (int)committed;
                    facts.IdlePublishedImages = host.Publications.PublishedCount;
                    facts.IdleDispatchRuns = host.StepGroup.DispatchRunCount - runsBefore;
                    facts.PendingDemandAfterIdle = host.PendingDemand;

                    // Ten idle seconds of host time: a command-driven world executes no simulation step at all, and
                    // no stage of the compiled graph runs (P-036, TEST-011).
                    bool pass = committed == 0UL
                        && host.CurrentStep.Value == stepBefore
                        && facts.IdlePublishedImages == imagesBefore
                        && facts.IdleDispatchRuns == 0
                        && facts.PendingDemandAfterIdle == 0UL
                        && time.Clocks.PendingWakeCount == 0;

                    steps.Add(new NarrativeStep(name, pass,
                        "frames=" + facts.IdleFrames.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; stepBefore=" + stepBefore.ToString(CultureInfo.InvariantCulture)
                        + "; stepAfter=" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; images=" + imagesBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + facts.IdlePublishedImages.ToString(CultureInfo.InvariantCulture)
                        + "; dispatchRuns=" + runsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.StepGroup.DispatchRunCount.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + facts.PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture)
                        + "; pendingWakes=" + time.Clocks.PendingWakeCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 11. genre neutrality

            private void ProveGenreNeutrality()
            {
                const string name = "narrative-genre-neutrality";
                try
                {
                    // The content names the rules package owns, the whole slice inventory and the declared component
                    // inventory are audited separately, so "no actor/vitality/physics name" is a claim about each
                    // fixed set rather than about one of them (P-001, TEST-021).
                    GenreAuditReport content = NarrativeRegistrations.Audit();
                    GenreAuditReport inventory = NarrativeInventory.Audit();
                    GenreAuditReport components = NarrativeInventory.AuditComponents();

                    facts.GenreAuditChecked = content.CheckedCount;
                    facts.GenreAuditNeutral = content.Neutral;
                    facts.ForbiddenGenreNameCount = content.ForbiddenNames.Count;
                    facts.InventoryAuditChecked = inventory.CheckedCount;
                    facts.InventoryAuditNeutral = inventory.Neutral;
                    facts.InventoryForbiddenNameCount = inventory.ForbiddenNames.Count;
                    facts.ComponentAuditChecked = components.CheckedCount;
                    facts.ComponentAuditNeutral = components.Neutral;

                    // The composition the world published registers no actor, vitality, physics or animation schema
                    // or stage: every name is narrative vocabulary, and the component inventory is what the slice
                    // declares (P-001, TEST-021).
                    // The run's own recording of its observations must equal the committed canonical trace's
                    // declaration, key by key: a run that disagrees with the trace, or records an observation the
                    // trace does not declare, is a drift in the slice (P-060).
                    bool declared = NarrativeScenarioTrace.TryCompare(
                        PipelineEntries(),
                        out IReadOnlyList<string> mismatches);
                    facts.TraceMatchesDeclaration = declared;
                    facts.TraceMismatchDetail = mismatches.Count == 0
                        ? "<none>"
                        : string.Join(" | ", ToArray(mismatches));

                    bool pass = declared
                        && content.Neutral
                        && inventory.Neutral
                        && components.Neutral
                        && content.CheckedCount == NarrativeRegistrations.Count
                        && inventory.CheckedCount == NarrativeInventory.Count
                        && inventory.CheckedCount
                            == NarrativeRegistrations.Count + NarrativeInventory.GameplayNames.Count
                        && components.CheckedCount == NarrativeComponentInventory.Count
                        && NarrativeScenarioTrace.ExpectedValue("genreAuditChecked")
                            == content.CheckedCount.ToString(CultureInfo.InvariantCulture);

                    steps.Add(new NarrativeStep(name, pass,
                        "content=" + GenreAuditReportText(content)
                        + "; inventory=" + GenreAuditReportText(inventory)
                        + "; components=" + GenreAuditReportText(components)
                        + "; registrations=" + NarrativeRegistrations.Count.ToString(CultureInfo.InvariantCulture)
                        + "; gameplayNames=" + NarrativeInventory.GameplayNames.Count.ToString(CultureInfo.InvariantCulture)
                        + "; componentTypes=" + NarrativeComponentInventory.Count.ToString(CultureInfo.InvariantCulture)
                        + "; traceMatchesDeclaration=" + facts.TraceMatchesDeclaration
                        + "; traceMismatches=" + facts.TraceMismatchDetail));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 12. teardown

            private void TearDownSafely()
            {
                const string name = "narrative-teardown-settles-and-disposes";
                try
                {
                    if (host == null)
                    {
                        steps.Add(new NarrativeStep(name, false, "no world"));
                        return;
                    }

                    if (time != null)
                    {
                        time.Clear(out int discardedCommands, out int pendingWakes);
                        _ = discardedCommands;
                        _ = pendingWakes;
                    }

                    NarrativeModule.DetachAll();

                    facts.OutstandingJobsBeforeTeardown = host.Ledger.OutstandingJobCount;
                    OperationResult stop = host.Stop(NextOperation(host.World), "gc-010 narrative teardown");
                    host.Dispose();

                    facts.OutstandingJobsAfterTeardown = host.Ledger.OutstandingJobCount;
                    facts.RetainedResourcesAfterTeardown = host.Ledger.RetainedResourceCount;
                    facts.RegistryAfterTeardown = UnityWorldRegistry.Count;

                    bool pass = (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && facts.OutstandingJobsAfterTeardown == 0
                        && facts.RetainedResourcesAfterTeardown == 0
                        && facts.RegistryAfterTeardown == registryBeforeCreate;

                    steps.Add(new NarrativeStep(name, pass,
                        "stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; outstandingBefore="
                        + facts.OutstandingJobsBeforeTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; outstandingAfter="
                        + facts.OutstandingJobsAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources="
                        + facts.RetainedResourcesAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + facts.RegistryAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ helpers

            private static int ValueOfRow(IReadOnlyList<CapabilityBinding> rows, CapabilityId capability)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Capability.Equals(capability))
                    {
                        return rows[i].Value;
                    }
                }

                return int.MinValue;
            }

            private static bool AllRowsActive(IReadOnlyList<CapabilityBinding> rows)
            {
                if (rows.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < rows.Count; i++)
                {
                    if (!rows[i].IsActive)
                    {
                        return false;
                    }
                }

                return true;
            }

            private ulong StampEpochOf(TargetId target)
            {
                if (registry == null || host == null || !registry.TryResolveTarget(target, out _, out Entity entity))
                {
                    return 0UL;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                return entityManager.HasComponent<AssemblyStamp>(entity)
                    ? entityManager.GetComponentData<AssemblyStamp>(entity).AssemblyEpoch
                    : 0UL;
            }

            private static string[] ToArray(IReadOnlyList<string> values)
            {
                var array = new string[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    array[i] = values[i];
                }

                return array;
            }

            private static string GenreAuditReportText(GenreAuditReport report)
                => "checked=" + report.CheckedCount.ToString(CultureInfo.InvariantCulture)
                    + "/neutral=" + report.Neutral
                    + "/forbidden=" + report.ForbiddenNames.Count.ToString(CultureInfo.InvariantCulture);

            private string DescribeFailures()
            {
                var text = new System.Text.StringBuilder();
                if (compileFailure.Length != 0)
                {
                    text.Append("; compileFailure=").Append(compileFailure);
                }

                if (scopeFailure.Length != 0)
                {
                    text.Append("; scopeFailure=").Append(scopeFailure);
                }

                if (seedFailure.Length != 0)
                {
                    text.Append("; seedFailure=").Append(seedFailure);
                }

                return text.ToString();
            }

            private static string DescribeDiagnostics(EditAdmission admission)
            {
                if (admission.Diagnostics == null || admission.Diagnostics.Count == 0)
                {
                    return "<none>";
                }

                return admission.Diagnostics[0].Summary;
            }

            private static IDerivationValueSource NarrativeCompositionValueSource()
                => new GameCore.Derivation.Fixtures.FixtureValueSource()
                    .RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return NarrativeKeys.Operation(world, operationSequence);
            }

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
