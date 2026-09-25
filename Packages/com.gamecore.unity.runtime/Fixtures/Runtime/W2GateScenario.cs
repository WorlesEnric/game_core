// GameCore.Unity.Fixtures — W2 integration gate scenario (the Wave 2 exit demonstration).
//
// The gate sentence this file implements, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 2 —
// Automatic assembly and generic execution):
//
//   "Use all real W2 outputs together: mount a provider, derive a compatible target, publish its real Entities
//    layout and compiled schedule, execute one bounded command, observe one consistent result, spawn a future
//    target, and run the player smoke path. Independent seam fixtures do not substitute for this integration."
//
// Every check below runs the real modules over real storage, and each Wave 2 task's private seam fixture is gone:
//
//   * GC-006 derives the mounted provider's contribution delta over the committed composition (no fixture snapshot);
//   * GC-007 validates the owners, the generated partitions and the slot policies, and its bounded message plane
//     carries the one command and publishes its committed output;
//   * GC-009 compiles the real stage DAG, adapts it into GC-005's dispatch plan and drives the frames through its
//     temporal drivers (input cutoff, plugin clocks, the non-component dependency table);
//   * GC-008 plans the publication, publishes bindings, compiled schedule and snapshot at one epoch, and spawns the
//     future target fully assembled — with the control lane's epoch equal to the world's at every boundary.
//
// The runner is shared by the Unity EditMode test (`W2Gate` under `unity/GameCore.Validation`) and by the standalone
// player probe mode `-probeW2Gate`, so the same scenario is proven in the Editor and in an IL2CPP player.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Planning.Scheduling;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Collections;
using Unity.Entities;

namespace GameCore.Unity.Fixtures
{
    /// <summary>One named gate observation: what was checked and the observed values.</summary>
    public sealed class W2GateStep
    {
        public W2GateStep(string name, bool passed, string detail)
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
    /// Facts the scenario observed, exposed so a caller can assert on them instead of trusting a boolean. Every
    /// value is read from live module state at the moment named in its own comment.
    /// </summary>
    public sealed class W2GateFacts
    {
        /// <summary>Fingerprint of the catalog the run was given, so a probe can name the committed literal (P-028).</summary>
        public string CatalogFingerprint { get; set; } = string.Empty;

        // ---------------------------------------------------------------- compilation and ownership (GC-009, GC-007)
        public int CompiledStageCount { get; set; }

        public int CompiledSystemCount { get; set; }

        public string ScheduleHash { get; set; } = string.Empty;

        public int ScheduleEdgeCount { get; set; }

        public int SchedulePlaybackPointCount { get; set; }

        public int SettleStageIndex { get; set; }

        public int ProjectStageIndex { get; set; }

        public bool BufferEdgeSettleBeforeProject { get; set; }

        public int OwnershipDomainCount { get; set; }

        public int OwnershipPartitionCount { get; set; }

        /// <summary>Domains whose writers GC-007 proved ordered or provably disjoint.</summary>
        public int OrderedOrDisjointWriterPairs { get; set; }

        public bool TraitWritersProvablyDisjoint { get; set; }

        public bool TrailWritersOrderedByStage { get; set; }

        public int ValidatedSlotPolicyCount { get; set; }

        public string DescriptorRevision { get; set; } = string.Empty;

        // ---------------------------------------------------------------- world and targets

        public int RegistryBeforeCreate { get; set; }

        public int RegistryAfterCreate { get; set; }

        public string WorldSession { get; set; } = string.Empty;

        public int LiveTargetCount { get; set; }

        public int MappedTargetCount { get; set; }

        public bool MessagePlanePresent { get; set; }

        // ---------------------------------------------------------------- publication 2: the mount

        public ulong LaneRevisionAfterMount { get; set; }

        public ulong LaneEpochAfterMount { get; set; }

        public ulong WorldEpochAfterMount { get; set; }

        public bool CountersJoinedAfterMount { get; set; }

        public int DerivedTargetCount { get; set; }

        public int DerivedContributionCount { get; set; }

        public int ProposalMountCount { get; set; }

        public int ProposalCapabilityCount { get; set; }

        public int PlanInstalledRowCount { get; set; }

        public int PlanMigratedSlotCount { get; set; }

        public int PublishedStructuralWrites { get; set; }

        public string PublicationOutcome { get; set; } = string.Empty;

        /// <summary>Epoch of the image an observer captured before the publication: the pre-publication assembly.</summary>
        public ulong ObserverCapturedEpoch { get; set; }

        /// <summary>Binding rows that captured image held: none, because it is the initial assembly (P-030).</summary>
        public int ObserverCapturedRows { get; set; }

        /// <summary>True when the switch replaced the reference instead of mutating the old image (P-030).</summary>
        public bool ObserverSeesOneCompleteImage { get; set; }

        // ---------------------------------------------------------------- the derived layout in real storage

        public int MaraBindingRowCount { get; set; }

        public int MaraBindingValue { get; set; }

        public bool MaraBindingIsActive { get; set; }

        public ulong MaraStampEpoch { get; set; }

        public int GateBindingRowCount { get; set; }

        public int GateBindingValue { get; set; }

        public int CrowdBindingRowCount { get; set; }

        public int EncounterBindingRowCount { get; set; }

        /// <summary>Value the publication's migration left in the addressed target's quest slot (P-029, P-032).</summary>
        public int MigratedSlotValue { get; set; }

        /// <summary>Schema version that migrated slot carries: the descriptor's declared version (P-032).</summary>
        public uint MigratedSlotSchemaVersion { get; set; }

        /// <summary>State slots the addressed target holds, so a migration cannot appear as a lost row.</summary>
        public int MigratedSlotCount { get; set; }

        /// <summary>Migration invocations of the registered handler; at least the live-slot count proves the plan
        /// migrated copies rather than touching live state (P-029).</summary>
        public int MigrationInvocations { get; set; }

        // ---------------------------------------------------------------- the bounded command

        public bool CommandAdmitted { get; set; }

        public string CommandAdmissionKind { get; set; } = string.Empty;

        public ulong StepsAfterCommand { get; set; }

        /// <summary>Value of the addressed target's quest slot after the publication and the command (P-032).</summary>
        public int ProgressOnTarget { get; set; }

        /// <summary>Schema version that slot carries, which the publication's migration advanced (P-029, P-032).</summary>
        public uint CommandSlotSchemaVersion { get; set; }

        public int CommittedEventCount { get; set; }

        public int CommittedEventPayload { get; set; }

        public ulong CommittedEventStep { get; set; }

        public ulong CommittedEventEpoch { get; set; }

        public int LedgerRowCount { get; set; }

        public int LedgerCommittedCount { get; set; }

        public int LedgerPendingCount { get; set; }

        public bool PublishedImageForFirstStep { get; set; }

        // ---------------------------------------------------------------- ordered dispatch through the plan

        public int StepGroupDispatchRuns { get; set; }

        public int StepGroupDispatchedEntries { get; set; }

        public int TrailSteps { get; set; }

        public int TrailProjectedValue { get; set; }

        public bool TrailWaitedOnNativeFence { get; set; }

        public int TraitLeftValue { get; set; }

        public int TraitRightValue { get; set; }

        public int SettleJobCount { get; set; }

        public int NativeProducedCount { get; set; }

        public int NativeDependentReadCount { get; set; }

        public int NativeUnproducedReadCount { get; set; }

        // ---------------------------------------------------------------- the registered wake (GC-009 clocks)

        public bool WakeScheduled { get; set; }

        public int WakeDueCount { get; set; }

        public ulong StepsAfterWake { get; set; }

        public int CommittedEventCountAfterWake { get; set; }

        // ---------------------------------------------------------------- publication 3: forward provider + spawn

        public ulong LaneEpochAfterForward { get; set; }

        public ulong WorldEpochAfterSpawn { get; set; }

        public bool CountersJoinedAfterSpawn { get; set; }

        public bool ForwardDerivationHadNoTargetChange { get; set; }

        public int SpawnedBindingRowCount { get; set; }

        public int SpawnedBindingValue { get; set; }

        public ulong SpawnedStampEpoch { get; set; }

        public bool SpawnedStampPublished { get; set; }

        public bool SpawnedTargetInPublishedView { get; set; }

        public int PublishedBindingRowCount { get; set; }

        public int RecipeApplyCount { get; set; }

        // ---------------------------------------------------------------- idle and teardown

        public int IdleFrames { get; set; }

        public int IdleStepsCommitted { get; set; }

        public int IdlePublishedImages { get; set; }

        public int IdleDispatchRuns { get; set; }

        public ulong PendingDemandAfterIdle { get; set; }

        public int OutstandingJobsBeforeTeardown { get; set; }

        public int OutstandingJobsAfterTeardown { get; set; }

        public int RetainedResourcesAfterTeardown { get; set; }

        public int RegistryAfterTeardown { get; set; }

        /// <summary>
        /// One-line digest of every recorded fact, so the standalone probe can archive the observed values beside
        /// the per-check outcomes without a second result shape.
        /// </summary>
        public string Describe()
        {
            return "catalogFingerprint=" + CatalogFingerprint
                + "; stages=" + CompiledStageCount.ToString(CultureInfo.InvariantCulture)
                + "; systems=" + CompiledSystemCount.ToString(CultureInfo.InvariantCulture)
                + "; scheduleHash=" + ScheduleHash
                + "; edges=" + ScheduleEdgeCount.ToString(CultureInfo.InvariantCulture)
                + "; playback=" + SchedulePlaybackPointCount.ToString(CultureInfo.InvariantCulture)
                + "; settleStage=" + SettleStageIndex.ToString(CultureInfo.InvariantCulture)
                + "; projectStage=" + ProjectStageIndex.ToString(CultureInfo.InvariantCulture)
                + "; bufferEdge=" + BufferEdgeSettleBeforeProject
                + "; domains=" + OwnershipDomainCount.ToString(CultureInfo.InvariantCulture)
                + "; partitions=" + OwnershipPartitionCount.ToString(CultureInfo.InvariantCulture)
                + "; traitWritersDisjoint=" + TraitWritersProvablyDisjoint
                + "; trailWritersOrdered=" + TrailWritersOrderedByStage
                + "; slotPolicies=" + ValidatedSlotPolicyCount.ToString(CultureInfo.InvariantCulture)
                + "; descriptorRevision=" + DescriptorRevision
                + "; session=" + WorldSession
                + "; liveTargets=" + LiveTargetCount.ToString(CultureInfo.InvariantCulture)
                + "; mappedTargets=" + MappedTargetCount.ToString(CultureInfo.InvariantCulture)
                + "; plane=" + MessagePlanePresent
                + "; laneRevisionAfterMount=" + LaneRevisionAfterMount.ToString(CultureInfo.InvariantCulture)
                + "; laneEpochAfterMount=" + LaneEpochAfterMount.ToString(CultureInfo.InvariantCulture)
                + "; worldEpochAfterMount=" + WorldEpochAfterMount.ToString(CultureInfo.InvariantCulture)
                + "; joinedAfterMount=" + CountersJoinedAfterMount
                + "; derivedTargets=" + DerivedTargetCount.ToString(CultureInfo.InvariantCulture)
                + "; derivedContributions=" + DerivedContributionCount.ToString(CultureInfo.InvariantCulture)
                + "; proposalMounts=" + ProposalMountCount.ToString(CultureInfo.InvariantCulture)
                + "; proposalCapabilities=" + ProposalCapabilityCount.ToString(CultureInfo.InvariantCulture)
                + "; installedRows=" + PlanInstalledRowCount.ToString(CultureInfo.InvariantCulture)
                + "; migratedSlots=" + PlanMigratedSlotCount.ToString(CultureInfo.InvariantCulture)
                + "; structuralWrites=" + PublishedStructuralWrites.ToString(CultureInfo.InvariantCulture)
                + "; publicationOutcome=" + PublicationOutcome
                + "; observerCapturedEpoch=" + ObserverCapturedEpoch.ToString(CultureInfo.InvariantCulture)
                + "; observerCapturedRows=" + ObserverCapturedRows.ToString(CultureInfo.InvariantCulture)
                + "; observerOneCompleteImage=" + ObserverSeesOneCompleteImage
                + "; maraRows=" + MaraBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; maraValue=" + MaraBindingValue.ToString(CultureInfo.InvariantCulture)
                + "; maraActive=" + MaraBindingIsActive
                + "; maraStampEpoch=" + MaraStampEpoch.ToString(CultureInfo.InvariantCulture)
                + "; gateRows=" + GateBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; gateValue=" + GateBindingValue.ToString(CultureInfo.InvariantCulture)
                + "; crowdRows=" + CrowdBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; encounterRows=" + EncounterBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; migrationInvocations=" + MigrationInvocations.ToString(CultureInfo.InvariantCulture)
                + "; commandAdmitted=" + CommandAdmitted
                + "; commandAdmission=" + CommandAdmissionKind
                + "; steps=" + StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                + "; questSlot=" + ProgressOnTarget.ToString(CultureInfo.InvariantCulture)
                + "; questSlotVersion=" + CommandSlotSchemaVersion.ToString(CultureInfo.InvariantCulture)
                + "; events=" + CommittedEventCount.ToString(CultureInfo.InvariantCulture)
                + "; eventPayload=" + CommittedEventPayload.ToString(CultureInfo.InvariantCulture)
                + "; eventStep=" + CommittedEventStep.ToString(CultureInfo.InvariantCulture)
                + "; eventEpoch=" + CommittedEventEpoch.ToString(CultureInfo.InvariantCulture)
                + "; ledgerRows=" + LedgerRowCount.ToString(CultureInfo.InvariantCulture)
                + "; ledgerCommitted=" + LedgerCommittedCount.ToString(CultureInfo.InvariantCulture)
                + "; ledgerPending=" + LedgerPendingCount.ToString(CultureInfo.InvariantCulture)
                + "; firstStepImage=" + PublishedImageForFirstStep
                + "; dispatchRuns=" + StepGroupDispatchRuns.ToString(CultureInfo.InvariantCulture)
                + "; dispatchedEntries=" + StepGroupDispatchedEntries.ToString(CultureInfo.InvariantCulture)
                + "; trailSteps=" + TrailSteps.ToString(CultureInfo.InvariantCulture)
                + "; trailProjected=" + TrailProjectedValue.ToString(CultureInfo.InvariantCulture)
                + "; trailWaited=" + TrailWaitedOnNativeFence
                + "; traitLeft=" + TraitLeftValue.ToString(CultureInfo.InvariantCulture)
                + "; traitRight=" + TraitRightValue.ToString(CultureInfo.InvariantCulture)
                + "; settleJobs=" + SettleJobCount.ToString(CultureInfo.InvariantCulture)
                + "; nativeProduced=" + NativeProducedCount.ToString(CultureInfo.InvariantCulture)
                + "; nativeReads=" + NativeDependentReadCount.ToString(CultureInfo.InvariantCulture)
                + "; nativeUnproducedReads=" + NativeUnproducedReadCount.ToString(CultureInfo.InvariantCulture)
                + "; wakeScheduled=" + WakeScheduled
                + "; wakeDue=" + WakeDueCount.ToString(CultureInfo.InvariantCulture)
                + "; stepsAfterWake=" + StepsAfterWake.ToString(CultureInfo.InvariantCulture)
                + "; eventsAfterWake=" + CommittedEventCountAfterWake.ToString(CultureInfo.InvariantCulture)
                + "; laneEpochAfterForward=" + LaneEpochAfterForward.ToString(CultureInfo.InvariantCulture)
                + "; worldEpochAfterSpawn=" + WorldEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                + "; joinedAfterSpawn=" + CountersJoinedAfterSpawn
                + "; forwardNoTargetChange=" + ForwardDerivationHadNoTargetChange
                + "; spawnedRows=" + SpawnedBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; spawnedValue=" + SpawnedBindingValue.ToString(CultureInfo.InvariantCulture)
                + "; spawnedStampEpoch=" + SpawnedStampEpoch.ToString(CultureInfo.InvariantCulture)
                + "; spawnedPublished=" + SpawnedStampPublished
                + "; spawnedInView=" + SpawnedTargetInPublishedView
                + "; publishedRows=" + PublishedBindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; recipeApplies=" + RecipeApplyCount.ToString(CultureInfo.InvariantCulture)
                + "; idleFrames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
                + "; idleSteps=" + IdleStepsCommitted.ToString(CultureInfo.InvariantCulture)
                + "; idleImages=" + IdlePublishedImages.ToString(CultureInfo.InvariantCulture)
                + "; idleDispatchRuns=" + IdleDispatchRuns.ToString(CultureInfo.InvariantCulture)
                + "; pendingDemandAfterIdle=" + PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture)
                + "; outstandingJobsBeforeTeardown=" + OutstandingJobsBeforeTeardown.ToString(CultureInfo.InvariantCulture)
                + "; outstandingJobsAfterTeardown=" + OutstandingJobsAfterTeardown.ToString(CultureInfo.InvariantCulture)
                + "; retainedResourcesAfterTeardown=" + RetainedResourcesAfterTeardown.ToString(CultureInfo.InvariantCulture)
                + "; registryAfterTeardown=" + RegistryAfterTeardown.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Full result of one gate run: the named observations plus the facts they were computed from.</summary>
    public sealed class W2GateScenarioResult
    {
        public W2GateScenarioResult(IReadOnlyList<W2GateStep> steps, W2GateFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<W2GateStep> Steps { get; }

        public W2GateFacts Facts { get; }

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
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " gate checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " gate check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>Runs the Wave 2 integration scenario against real modules only.</summary>
    public static class W2GateScenario
    {
        /// <summary>Host ticks handed to the time driver; the world captures its origin at its first pump.</summary>
        private const ulong HostTicks = 1_000_000UL;

        /// <summary>Frames the world is pumped while it must stay still (TEST-011).</summary>
        private const int IdleFrames = 8;

        /// <summary>Bounded temporary storage the gate's plans may reserve, in bytes.</summary>
        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        /// <summary>Staged lease ceiling of the gate's plan resource gate, in bytes.</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        /// <summary>
        /// Runs the gate with the hand-written generated-style catalog in this assembly (`W1GateCatalog`), which is
        /// the same table shape the content compiler emits.
        /// </summary>
        public static W2GateScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = W1GateCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the generated-style gate catalog was rejected: " + build.Describe());
            }

            PluginManifest binding = W2GateDeclarations.BindingProvider(
                W2GateKeys.PluginTypeId(1UL),
                W1GateCatalog.PluginFactoryKey,
                W1GateKeys.CatalogSchema);
            PluginManifest forward = W2GateDeclarations.ForwardProvider(
                W2GateKeys.PluginTypeId(2UL),
                W1GateCatalog.PluginFactoryKey,
                W1GateKeys.CatalogSchema);

            var declarations = new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(binding, ConfigDocument.Empty),
                new CatalogPluginDeclaration(forward, ConfigDocument.Empty),
            };

            return Run(
                build.Catalog,
                declarations,
                W1GateKeys.AbsentPluginFactory,
                W1GateKeys.AbsentPluginType,
                W1GateCatalog.Fingerprint());
        }

        /// <summary>
        /// Runs the gate against one catalog. <paramref name="declarations"/> are the generated-style declarations
        /// the two mounts resolve; <paramref name="absentFactoryKey"/> and <paramref name="absentPluginType"/> must
        /// be unregistered, so the P-009 miss stays observable; <paramref name="declaredFingerprint"/> is the
        /// fingerprint the catalog's declarations were published with (a generated literal or an independent
        /// recomputation), and the gate fails when the built catalog disagrees (P-028).
        /// </summary>
        public static W2GateScenarioResult Run(
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

            if (declarations == null || declarations.Count < 2)
            {
                throw new ArgumentException(
                    "the gate mounts two providers, so it needs both generated-style declarations.", nameof(declarations));
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
            private readonly List<W2GateStep> steps = new List<W2GateStep>();
            private readonly W2GateFacts facts = new W2GateFacts();

            private readonly IdSequence sessionSequence = new IdSequence(0x5732474154453232UL);
            private readonly W2GateQuestMigration migration = new W2GateQuestMigration();
            private readonly W2GateRecipeApplier applier = new W2GateRecipeApplier(W2GateRecipes.BaseProgress);

            private UnityWorldHost? host;
            private W2GateModule? module;
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

            public W2GateScenarioResult Run()
            {
                CheckCatalogAndDeclarations();
                CompileOwnershipAndSchedule();
                CreateWorldAndTargets();
                MountProviderAndPublish();
                ProveDerivedLayoutInStorage();
                ExecuteOneBoundedCommand();
                ProveOrderedDispatch();
                AdvanceByRegisteredWake();
                PublishForwardProviderAndSpawn();
                ProveIdleWorldPerformsNoStep();
                TearDownSafely();

                return new W2GateScenarioResult(steps, facts);
            }

            // ------------------------------------------------------------------ 1. catalog and declarations

            private void CheckCatalogAndDeclarations()
            {
                const string name = "gate2-catalog-and-declarations";
                try
                {
                    CatalogLookup bindingFactory = catalog.Lookup(declarations[0].Manifest.FactoryKey);
                    CatalogLookup absent = catalog.Lookup(absentFactoryKey);
                    bool missReported = !absent.Found && absent.Code == DiagnosticCode.MissingDependency;
                    bool fingerprintAsDeclared = catalog.Fingerprint.Equals(declaredFingerprint);
                    facts.CatalogFingerprint = catalog.Fingerprint.ToHex();

                    var manifests = new CatalogManifestSource(catalog, declarations);
                    bool bothAccepted = manifests.AcceptedCount == 2 && manifests.Rejected.Count == 0;

                    bool unregisteredResolved = manifests.TryGetManifest(absentPluginType, out PluginManifest? resolved);
                    bool pass = fingerprintAsDeclared
                        && bindingFactory.Found
                        && bindingFactory.Factory != null
                        && bindingFactory.Factory.Kind == FactoryKind.PluginFactory
                        && missReported
                        && bothAccepted
                        && !unregisteredResolved
                        && resolved == null;

                    steps.Add(new W2GateStep(name, pass,
                        "fingerprint=" + catalog.Fingerprint.ToHex()
                        + "; fingerprintAsDeclared=" + fingerprintAsDeclared
                        + "; declaredFingerprint=" + declaredFingerprint.ToHex()
                        + "; acceptedDeclarations=" + manifests.AcceptedCount.ToString(CultureInfo.InvariantCulture)
                        + "; rejectedDeclarations=" + manifests.Rejected.Count.ToString(CultureInfo.InvariantCulture)
                        + "; factoryLookup=" + bindingFactory.Describe()
                        + "; unknownKeyLookup=" + absent.Describe()
                        + "; unregisteredResolved=" + (resolved != null)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 2. GC-007 ownership + GC-009 schedule

            private void CompileOwnershipAndSchedule()
            {
                const string name = "gate2-ownership-and-schedule-compiled";
                try
                {
                    var kinds = new ScheduleDispatchKindTable()
                        .Add(W2GateKeys.CommandSystem, SystemDispatchKind.ManagedSystem)
                        .Add(W2GateKeys.SettleSystem, SystemDispatchKind.ManagedSystem)
                        .Add(W2GateKeys.ClaimLeftSystem, SystemDispatchKind.ManagedSystem)
                        .Add(W2GateKeys.ClaimRightSystem, SystemDispatchKind.ManagedSystem)
                        .Add(W2GateKeys.ProjectSystem, SystemDispatchKind.ManagedSystem);

                    var manifests = new List<PluginManifest>();
                    for (int i = 0; i < declarations.Count; i++)
                    {
                        manifests.Add(declarations[i].Manifest);
                    }

                    PipelineDescriptorReport report = OwnershipSchedulePipeline.Build(
                        manifests,
                        kinds,
                        new W2GateSlotMigrations());
                    descriptorReport = report;

                    if (!report.Succeeded || report.Descriptor == null || report.Compilation == null || report.Adaptation == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the pipeline refused: " + report.Describe()));
                        return;
                    }

                    CompiledSchedule schedule = report.Compilation.Schedule!;
                    facts.CompiledStageCount = schedule.StageCount;
                    facts.CompiledSystemCount = schedule.SystemCount;
                    facts.ScheduleHash = schedule.Hash.ToHex();
                    facts.ScheduleEdgeCount = schedule.Edges.Count;
                    facts.SchedulePlaybackPointCount = schedule.PlaybackPoints.Count;
                    facts.DescriptorRevision = report.Descriptor.Revision.ToHex();

                    bool settleIndex = schedule.TryGetStageIndex(W2GateKeys.SettleStage, out int settle);
                    bool projectIndex = schedule.TryGetStageIndex(W2GateKeys.ProjectStage, out int project);
                    facts.SettleStageIndex = settleIndex ? settle : -1;
                    facts.ProjectStageIndex = projectIndex ? project : -1;

                    // The declared buffer must have produced a real producer→consumer edge (P-041, P-043).
                    facts.BufferEdgeSettleBeforeProject = settleIndex && projectIndex && schedule.HasEdge(settle, project);

                    // GC-007's own verdicts, read from its report rather than restated here.
                    OwnershipReport ownership = report.Ownership!;
                    facts.OwnershipDomainCount = ownership.Map.DomainCount;
                    facts.OwnershipPartitionCount = ownership.Map.PartitionCount;
                    facts.TraitWritersProvablyDisjoint = TraitWritersDisjoint(ownership);
                    facts.TrailWritersOrderedByStage = ownership.Map.WritersOf(W2GateKeys.TrailDomain).Count == 2
                        && report.Descriptor.TryGetStage(W2GateKeys.SettleStage, out DescriptorStage? settleStage)
                        && settleStage != null
                        && report.Descriptor.TryGetStage(W2GateKeys.ProjectStage, out DescriptorStage? projectStage)
                        && projectStage != null
                        && settleStage.StageIndex != projectStage.StageIndex;
                    facts.ValidatedSlotPolicyCount = report.SlotPolicies.Count;

                    bool everySlotValidated = report.SlotPolicies.Count >= 3;
                    for (int i = 0; i < report.SlotPolicies.Count; i++)
                    {
                        everySlotValidated &= report.SlotPolicies[i].Succeeded;
                    }

                    bool pass = report.Compilation.Succeeded
                        && report.Adaptation.Succeeded
                        && report.Descriptor.TryValidate(out DiagnosticCode _, out string _)
                        && ownership.IsValid
                        && schedule.StageCount == 4
                        && schedule.SystemCount == 5
                        && facts.BufferEdgeSettleBeforeProject
                        && facts.TraitWritersProvablyDisjoint
                        && facts.TrailWritersOrderedByStage
                        && everySlotValidated
                        && !schedule.Hash.IsEmpty;

                    steps.Add(new W2GateStep(name, pass,
                        "compilation=" + report.Compilation.Explain()
                        + "; adaptation=" + report.Adaptation.Explain()
                        + "; descriptor=" + report.Describe()
                        + "; ownershipValid=" + ownership.IsValid
                        + "; domains=" + facts.OwnershipDomainCount.ToString(CultureInfo.InvariantCulture)
                        + "; partitions=" + facts.OwnershipPartitionCount.ToString(CultureInfo.InvariantCulture)
                        + "; traitDisjoint=" + facts.TraitWritersProvablyDisjoint
                        + "; schedule=" + schedule.Describe()));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            private static bool TraitWritersDisjoint(OwnershipReport ownership)
            {
                IReadOnlyList<FactoryKey> writers = ownership.Map.WritersOf(W2GateKeys.TraitDomain);
                if (writers.Count != 2)
                {
                    return false;
                }

                if (!ownership.Map.TryGetPartition(W2GateKeys.TraitDomain, writers[0], out PartitionAssignment left)
                    || !ownership.Map.TryGetPartition(W2GateKeys.TraitDomain, writers[1], out PartitionAssignment right))
                {
                    return false;
                }

                return left.IsPartitioned
                    && right.IsPartitioned
                    && !left.PartitionId.Equals(right.PartitionId)
                    && PartitionIdGenerator.ProvablyDisjoint(left, right);
            }

            // ------------------------------------------------------------------ 3. the real world and its targets

            private void CreateWorldAndTargets()
            {
                const string name = "gate2-world-and-live-targets";
                try
                {
                    if (descriptorReport == null || descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the descriptor was not built"));
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId world = NextSession();
                    facts.WorldSession = world.Session.ToString();

                    WorldCreateRequest request = W2GateRegistration.CommandDrivenRequest(
                        world,
                        NextOperation(world),
                        ContentHash.Empty);
                    UnityWorldRegistration registration = W2GateRegistration.Create(
                        descriptorReport.Adaptation!,
                        W2GateRegistration.Systems());

                    bool created = UnityWorldRegistry.TryCreate(
                        request,
                        registration,
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    facts.RegistryAfterCreate = UnityWorldRegistry.Count;

                    if (!created || host == null)
                    {
                        steps.Add(new W2GateStep(name, false, "world creation failed: " + result.Code + ": " + result.Detail));
                        return;
                    }

                    module = W2GateModule.Attach(host, descriptorReport.Compilation!.Schedule!, descriptorReport.Adaptation!);
                    facts.MessagePlanePresent = host.Messages != null;

                    registry = new TargetRegistry(world, 16);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        W2GateRecipes.Catalog(applier),
                        new MigrationRegistry(new List<ISlotMigration> { migration }),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    bool seeded = SeedTarget(W2GateKeys.Mara, W2GateKeys.VillagerRecipe)
                        && SeedTarget(W2GateKeys.GateEast, W2GateKeys.QuestGateRecipe)
                        && SeedTarget(W2GateKeys.CrowdProp, W2GateKeys.DecorativeCrowdRecipe)
                        && SeedTarget(W2GateKeys.EncounterOak, W2GateKeys.QuestEncounterRecipe);

                    lane = CompositionHost.CreateDefault(
                        world,
                        W2GateKeys.RootScope,
                        new CatalogManifestSource(catalog, declarations),
                        null,
                        CompositionLaneSeed.InitialAssembly);
                    bridge = new WorldCompositionBridge(host, lane, publisher);

                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        new FixtureValueSource().RegisterAlwaysPredicate(W2GateKeys.AlwaysPredicateName),
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, W2GateKeys.Issuer),
                        new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);
                    module.Time = time;

                    bool clockRegistered = time.Clocks.TryRegister(
                        new PluginClockSpec(
                            W2GateKeys.DomainClock,
                            "w2.clock.domain",
                            PluginClockKind.DomainExplicit,
                            WakePausePolicy.Defer,
                            true),
                        out DiagnosticCode _);

                    facts.LiveTargetCount = targets.Count;
                    facts.MappedTargetCount = module.MappedTargetCount;

                    bool laneJoined = AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch);

                    bool pass = seeded
                        && clockRegistered
                        && facts.MessagePlanePresent
                        && laneJoined
                        && facts.LiveTargetCount == 4
                        && facts.MappedTargetCount == 4
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.Lifecycle == WorldLifecycleState.Running;

                    steps.Add(new W2GateStep(name, pass,
                        "session=" + world.Session.ToString()
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + facts.RegistryAfterCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + facts.LiveTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; mappedTargets=" + facts.MappedTargetCount.ToString(CultureInfo.InvariantCulture)
                        + "; plane=" + facts.MessagePlanePresent
                        + "; laneJoined=" + laneJoined
                        + "; lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; clockRegistered=" + clockRegistered
                        + "; lifecycle=" + host.Lifecycle));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            private bool SeedTarget(TargetId target, DefinitionRef recipe)
            {
                if (seeder == null || module == null)
                {
                    return false;
                }

                if (!seeder.TrySeed(target, W2GateKeys.RootScope, recipe, out _, out DiagnosticCode code, out string detail))
                {
                    steps.Add(new W2GateStep("gate2-seed-" + target.ToString(), false, code + ": " + detail));
                    return false;
                }

                if (!seeder.TryGetEntity(target, out Entity entity))
                {
                    return false;
                }

                module.MapTarget(target, entity);

                // Live state at schema version 1 while the descriptor declares version 2: the plan must migrate a
                // copy, and it must never zero-initialise live state (P-029, P-032).
                return seeder.TrySeedSlot(
                    target,
                    W2GateKeys.QuestOwner,
                    W2GateKeys.QuestSlot,
                    1U,
                    W2GateKeys.SeededQuestValue,
                    out DiagnosticCode _,
                    out string _);
            }

            // ------------------------------------------------------------------ 4. mount the provider, publish its assembly

            private void MountProviderAndPublish()
            {
                const string name = "gate2-provider-mounted-and-published";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    OperationId operation = NextOperation(host.World);
                    CompositionEditPayload payload = W1GatePayloads.Mount(
                        declarations[0].Manifest,
                        W2GateKeys.Instance(1UL),
                        W2GateKeys.RootScope,
                        declarations[0].SchemaDefaults);

                    // An observer's single read before the publication: it holds the pre-publication image, and the
                    // switch that follows replaces the reference rather than mutating the image, so no reader can
                    // ever hold a mixture of the two assemblies (P-030).
                    PublishedWorldView capturedBefore = publisher.Published;

                    EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();

                    DerivedAssemblyReport report = pipeline.PublishDerived(operation);

                    facts.ObserverCapturedEpoch = capturedBefore.Epoch.Value;
                    facts.ObserverCapturedRows = capturedBefore.BindingRowCount;
                    facts.ObserverSeesOneCompleteImage = capturedBefore.Epoch.Value == AssemblyEpoch.First.Value
                        && capturedBefore.BindingRowCount == 0
                        && !ReferenceEquals(capturedBefore, publisher.Published)
                        && publisher.Published.Epoch.Value == 2UL
                        && publisher.Published.BindingRowCount == 2;
                    facts.LaneRevisionAfterMount = lane.Committed.Revision.Value;
                    facts.LaneEpochAfterMount = lane.Committed.Epoch.Value;
                    facts.WorldEpochAfterMount = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterMount = report.CountersJoined;
                    facts.DerivedTargetCount = 0;
                    if (report.Derivation != null)
                    {
                        for (int i = 0; i < report.Derivation.Assemblies.Count; i++)
                        {
                            if (!report.Derivation.Assemblies[i].IsBaseOnly)
                            {
                                facts.DerivedTargetCount++;
                            }
                        }
                    }
                    facts.DerivedContributionCount = report.Derivation != null ? report.Derivation.Contributions.Count : 0;
                    facts.ProposalMountCount = report.Proposal != null ? report.Proposal.MountCount : 0;
                    facts.ProposalCapabilityCount = report.Proposal != null ? report.Proposal.CapabilityCount : 0;
                    facts.PlanInstalledRowCount = report.InstalledRows;
                    facts.PlanMigratedSlotCount = report.MigratedSlots;
                    facts.PublishedStructuralWrites = report.Publication != null ? report.Publication.StructuralWrites : 0;
                    facts.PublicationOutcome = report.Publication != null ? report.Publication.Outcome.ToString() : "none";
                    facts.MigrationInvocations = migration.Invocations;

                    bool pass = admission.Staged
                        && published.Count == 1
                        && published[0].Outcome == Outcome.Published
                        && report.Outcome == DerivedAssemblyOutcome.Published
                        && report.Succeeded
                        && report.Input != null && report.Input.Succeeded
                        && report.Derivation != null && report.Derivation.Accepted
                        && facts.DerivedTargetCount == 2
                        && facts.ProposalMountCount == 1
                        && facts.ProposalCapabilityCount == 2
                        && facts.PlanInstalledRowCount == 2
                        && facts.PlanMigratedSlotCount == 4
                        && facts.LaneRevisionAfterMount == 2UL
                        && facts.LaneEpochAfterMount == 2UL
                        && facts.WorldEpochAfterMount == 2UL
                        && facts.CountersJoinedAfterMount
                        && facts.ObserverSeesOneCompleteImage
                        && facts.MigrationInvocations >= 4;

                    steps.Add(new W2GateStep(name, pass, report.Describe()
                        + "; admission=" + admission.Kind
                        + "; drained=" + published.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lane=" + facts.LaneRevisionAfterMount.ToString(CultureInfo.InvariantCulture)
                        + "/" + facts.LaneEpochAfterMount.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + facts.CountersJoinedAfterMount
                        + "; observerOneCompleteImage=" + facts.ObserverSeesOneCompleteImage
                        + "; observerCapturedEpoch=" + facts.ObserverCapturedEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; migrations=" + facts.MigrationInvocations.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 5. the derived layout in real storage

            private void ProveDerivedLayoutInStorage()
            {
                const string name = "gate2-derived-layout-in-entities";
                try
                {
                    if (publisher == null)
                    {
                        steps.Add(new W2GateStep(name, false, "no publisher"));
                        return;
                    }

                    IReadOnlyList<CapabilityBinding> maraRows = publisher.ReadBindingRows(W2GateKeys.Mara);
                    IReadOnlyList<CapabilityBinding> gateRows = publisher.ReadBindingRows(W2GateKeys.GateEast);
                    IReadOnlyList<CapabilityBinding> crowdRows = publisher.ReadBindingRows(W2GateKeys.CrowdProp);
                    IReadOnlyList<CapabilityBinding> encounterRows = publisher.ReadBindingRows(W2GateKeys.EncounterOak);

                    facts.MaraBindingRowCount = maraRows.Count;
                    facts.GateBindingRowCount = gateRows.Count;
                    facts.CrowdBindingRowCount = crowdRows.Count;
                    facts.EncounterBindingRowCount = encounterRows.Count;

                    if (maraRows.Count == 1)
                    {
                        facts.MaraBindingValue = maraRows[0].Value;
                        facts.MaraBindingIsActive = maraRows[0].IsActive;
                    }

                    if (gateRows.Count == 1)
                    {
                        facts.GateBindingValue = gateRows[0].Value;
                    }

                    facts.MaraStampEpoch = StampEpochOf(W2GateKeys.Mara);

                    // The publication migrated every live quest slot on bounded scratch: the value the seeded state
                    // carried plus the registered delta, at the descriptor's schema version (P-029, P-032).
                    TargetSlotState migrated = ReadQuestSlot(W2GateKeys.Mara);
                    facts.MigratedSlotValue = migrated.Value;
                    facts.MigratedSlotSchemaVersion = migrated.SchemaVersion;
                    facts.MigratedSlotCount = publisher.ReadSlotStates(W2GateKeys.Mara).Count;

                    // Automatic propagation reaches an eligible target with no import and no opt-in (P-013, P-015),
                    // and it leaves an ineligible one exactly on its base recipe.
                    bool pass = facts.MaraBindingRowCount == 1
                        && facts.MaraBindingValue == W2GateDeclarations.VillagerBindingValue
                        && facts.MaraBindingIsActive
                        && maraRows[0].Provider.Equals(new ProviderInstallationId(W2GateKeys.Instance(1UL).Value))
                        && facts.MaraStampEpoch == facts.WorldEpochAfterMount
                        && facts.GateBindingRowCount == 1
                        && facts.GateBindingValue == W2GateDeclarations.GateBindingValue
                        && facts.CrowdBindingRowCount == 0
                        && facts.EncounterBindingRowCount == 0
                        && facts.MigratedSlotValue == W2GateKeys.MigratedQuestValue
                        && facts.MigratedSlotSchemaVersion == W2GateKeys.QuestDomain.Version
                        && facts.MigratedSlotCount == 1;

                    steps.Add(new W2GateStep(name, pass,
                        "maraRows=" + facts.MaraBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; maraValue=" + facts.MaraBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; maraActive=" + facts.MaraBindingIsActive
                        + "; maraStampEpoch=" + facts.MaraStampEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; migratedSlot=" + facts.MigratedSlotValue.ToString(CultureInfo.InvariantCulture)
                        + "; migratedVersion=" + facts.MigratedSlotSchemaVersion.ToString(CultureInfo.InvariantCulture)
                        + "; targetSlotCount=" + facts.MigratedSlotCount.ToString(CultureInfo.InvariantCulture)
                        + "; gateRows=" + facts.GateBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; gateValue=" + facts.GateBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; crowdRows=" + facts.CrowdBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; encounterRows=" + facts.EncounterBindingRowCount.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            private ulong StampEpochOf(TargetId target)
            {
                if (registry == null || host == null || !registry.TryResolveTarget(target, out _, out Entity entity))
                {
                    return 0UL;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                if (!entityManager.HasComponent<AssemblyStamp>(entity))
                {
                    return 0UL;
                }

                return entityManager.GetComponentData<AssemblyStamp>(entity).AssemblyEpoch;
            }

            // ------------------------------------------------------------------ 6. one bounded command

            private void ExecuteOneBoundedCommand()
            {
                const string name = "gate2-one-bounded-command-committed";
                try
                {
                    if (host == null || time == null || lane == null || bridge == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the world or its time driver is missing"));
                        return;
                    }

                    WorldMessagePlane? plane = host.Messages;
                    if (plane == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the world has no message plane"));
                        return;
                    }

                    var envelope = new CommandEnvelope(
                        NextOperation(host.World),
                        W2GateKeys.CommandRoute,
                        W2GateKeys.Mara,
                        W2GateKeys.CommandSchema,
                        null,
                        IntegrationSlotValues.WriteInt32(W2GateKeys.CommandProgressDelta));

                    CommandAdmissionReceipt receipt = host.Submit(envelope);
                    facts.CommandAdmitted = receipt.Admitted;
                    facts.CommandAdmissionKind = receipt.Result.Kind.ToString();

                    TimeFrameReport frame = time.PumpFrame(HostTicks);

                    facts.StepsAfterCommand = host.CurrentStep.Value;
                    facts.SettleJobCount = module != null ? module.SettleJobCount : 0;

                    // One authoritative store per domain: the owner advanced the quest slot, so the assertion reads
                    // that slot back out of live ECS storage (P-032, P-034).
                    TargetSlotState questSlot = ReadQuestSlot(W2GateKeys.Mara);
                    facts.ProgressOnTarget = questSlot.Value;
                    facts.CommandSlotSchemaVersion = questSlot.SchemaVersion;

                    // Exactly one committed result: one event, carrying the authoritative value the step produced,
                    // stamped with the image's epoch and the step that committed it (P-044, P-045).
                    CommittedEventPage page = plane.ReadEvents(
                        new EventCursor(host.World, EventSequence.Zero),
                        8);
                    facts.CommittedEventCount = page.Events.Count;
                    if (page.Events.Count > 0)
                    {
                        CommittedEvent committed = page.Events[0];
                        facts.CommittedEventStep = committed.Step.Value;
                        facts.CommittedEventEpoch = committed.Epoch.Value;
                        IntegrationSlotValues.TryReadInt32(committed.Payload, out int payloadValue);
                        facts.CommittedEventPayload = payloadValue;
                    }

                    facts.LedgerRowCount = plane.Requests.RowCount;
                    facts.LedgerCommittedCount = plane.Requests.CommittedCount;
                    facts.LedgerPendingCount = plane.Requests.PendingCount;
                    facts.PublishedImageForFirstStep = host.Publications.HasPublished(
                        new SnapshotToken(host.World, host.CurrentEpoch, LogicalStepId.First));

                    bridge.SyncStepFromWorld();

                    bool pass = receipt.Admitted
                        && frame.StepsCommitted == 1UL
                        && frame.Pump.Pumped
                        && facts.StepsAfterCommand == 1UL
                        && facts.ProgressOnTarget == W2GateKeys.CommandedQuestValue
                        && facts.CommandSlotSchemaVersion == W2GateKeys.QuestDomain.Version
                        && facts.CommittedEventCount == 1
                        && facts.CommittedEventPayload == W2GateKeys.CommandedQuestValue
                        && facts.CommittedEventStep == 1UL
                        && facts.CommittedEventEpoch == facts.WorldEpochAfterMount
                        && facts.LedgerCommittedCount == 1
                        && facts.LedgerPendingCount == 0
                        && facts.PublishedImageForFirstStep
                        && host.PendingDemand == 0UL;

                    steps.Add(new W2GateStep(name, pass,
                        "admitted=" + receipt.Admitted
                        + "; admission=" + facts.CommandAdmissionKind
                        + "; steps=" + facts.StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
                        + "; questSlot=" + facts.ProgressOnTarget.ToString(CultureInfo.InvariantCulture)
                        + "; questSlotVersion=" + facts.CommandSlotSchemaVersion.ToString(CultureInfo.InvariantCulture)
                        + "; events=" + facts.CommittedEventCount.ToString(CultureInfo.InvariantCulture)
                        + "; eventPayload=" + facts.CommittedEventPayload.ToString(CultureInfo.InvariantCulture)
                        + "; eventStep=" + facts.CommittedEventStep.ToString(CultureInfo.InvariantCulture)
                        + "; eventEpoch=" + facts.CommittedEventEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerRows=" + facts.LedgerRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerCommitted=" + facts.LedgerCommittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; demand=" + host.PendingDemand.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            private TargetSlotState ReadQuestSlot(TargetId target)
            {
                if (registry == null || host == null || !registry.TryResolveTarget(target, out _, out Entity entity))
                {
                    return default(TargetSlotState);
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                if (!entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    return default(TargetSlotState);
                }

                DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                return AssemblyStorage.TryFindSlot(slots, W2GateKeys.QuestOwner, W2GateKeys.QuestSlot, out int row)
                    ? slots[row]
                    : default(TargetSlotState);
            }

            // ------------------------------------------------------------------ 7. the compiled order really ran

            private void ProveOrderedDispatch()
            {
                const string name = "gate2-ordered-dispatch-and-dependent-read";
                try
                {
                    if (host == null || module == null || descriptorReport?.Adaptation?.NativeTable == null)
                    {
                        steps.Add(new W2GateStep(name, false, "no module or native table"));
                        return;
                    }

                    module.TryReadTrail(out W2GateTrail trail);
                    module.TryReadTrait(out W2GateTrait trait);

                    facts.StepGroupDispatchRuns = host.StepGroup.DispatchRunCount;
                    facts.StepGroupDispatchedEntries = host.StepGroup.TotalDispatchedCount;
                    facts.TrailSteps = trail.Steps;
                    facts.TrailProjectedValue = trail.ProjectedValue;
                    facts.TrailWaitedOnNativeFence = trail.WaitedOnNativeFence != 0;
                    facts.TraitLeftValue = trait.LeftValue;
                    facts.TraitRightValue = trait.RightValue;
                    facts.NativeProducedCount = module.Native.ProducedCount;
                    facts.NativeDependentReadCount = module.Native.DependentReadCount;
                    facts.NativeUnproducedReadCount = module.Native.UnproducedReadCount;

                    bool pass = facts.StepGroupDispatchRuns == 1
                        && facts.StepGroupDispatchedEntries == 5
                        && facts.TrailSteps == 1
                        && facts.TrailWaitedOnNativeFence
                        && facts.TrailProjectedValue == facts.SettleJobCount
                        && facts.TraitLeftValue == 1
                        && facts.TraitRightValue == 1
                        && facts.NativeUnproducedReadCount == 0
                        && !host.Driver.IsFaulted
                        && host.Driver.LastDrain.Succeeded
                        && host.FaultCount == 0;

                    steps.Add(new W2GateStep(name, pass,
                        "dispatchRuns=" + facts.StepGroupDispatchRuns.ToString(CultureInfo.InvariantCulture)
                        + "; dispatchedEntries=" + facts.StepGroupDispatchedEntries.ToString(CultureInfo.InvariantCulture)
                        + "; trailSteps=" + facts.TrailSteps.ToString(CultureInfo.InvariantCulture)
                        + "; projected=" + facts.TrailProjectedValue.ToString(CultureInfo.InvariantCulture)
                        + "; settleJobs=" + facts.SettleJobCount.ToString(CultureInfo.InvariantCulture)
                        + "; waitedOnNativeFence=" + facts.TrailWaitedOnNativeFence
                        + "; traitLeft=" + facts.TraitLeftValue.ToString(CultureInfo.InvariantCulture)
                        + "; traitRight=" + facts.TraitRightValue.ToString(CultureInfo.InvariantCulture)
                        + "; nativeProduced=" + facts.NativeProducedCount.ToString(CultureInfo.InvariantCulture)
                        + "; nativeReads=" + facts.NativeDependentReadCount.ToString(CultureInfo.InvariantCulture)
                        + "; nativeUnproducedReads=" + facts.NativeUnproducedReadCount.ToString(CultureInfo.InvariantCulture)
                        + "; faulted=" + host.Driver.IsFaulted
                        + "; drain=" + host.Driver.LastDrain.Succeeded));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, exception.ToString()));
                }
            }

            // ------------------------------------------------------------------ 8. a registered wake advances one step

            private void AdvanceByRegisteredWake()
            {
                const string name = "gate2-registered-wake-advances-one-step";
                try
                {
                    if (host == null || time == null || module == null)
                    {
                        steps.Add(new W2GateStep(name, false, "no time driver"));
                        return;
                    }

                    bool scheduled = time.TryScheduleWake(
                        W2GateKeys.DomainClock,
                        new Id128(W2GateKeys.Namespace, 0x0F01UL),
                        W2GateKeys.ResultSchema,
                        0UL,
                        0UL,
                        out WakeRecord? _,
                        out DiagnosticCode _);
                    facts.WakeScheduled = scheduled;

                    TimeFrameReport frame = time.PumpFrame(HostTicks);
                    facts.WakeDueCount = frame.DueWakes;
                    facts.StepsAfterWake = host.CurrentStep.Value;

                    WorldMessagePlane plane = host.Messages!;
                    CommittedEventPage page = plane.ReadEvents(
                        new EventCursor(host.World, EventSequence.Zero),
                        8);
                    facts.CommittedEventCountAfterWake = page.Events.Count;

                    module.TryReadTrail(out W2GateTrail trail);

                    // A registered wake is demand for exactly one step, and it commits no request: the committed
                    // result of the command step is still the only one (P-036, P-038, P-042).
                    bool pass = scheduled
                        && facts.WakeDueCount == 1
                        && frame.StepsCommitted == 1UL
                        && facts.StepsAfterWake == 2UL
                        && trail.Steps == 2
                        && facts.CommittedEventCountAfterWake == 1
                        && !host.Driver.IsFaulted;

                    steps.Add(new W2GateStep(name, pass,
                        "scheduled=" + scheduled
                        + "; dueWakes=" + facts.WakeDueCount.ToString(CultureInfo.InvariantCulture)
                        + "; stepsCommitted=" + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + "; worldStep=" + facts.StepsAfterWake.ToString(CultureInfo.InvariantCulture)
                        + "; trailSteps=" + trail.Steps.ToString(CultureInfo.InvariantCulture)
                        + "; events=" + facts.CommittedEventCountAfterWake.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 9. forward provider and the spawn

            private void PublishForwardProviderAndSpawn()
            {
                const string name = "gate2-forward-provider-and-spawned-target";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null)
                    {
                        steps.Add(new W2GateStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    OperationId operation = NextOperation(host.World);
                    CompositionEditPayload payload = W1GatePayloads.Mount(
                        declarations[1].Manifest,
                        W2GateKeys.Instance(2UL),
                        W2GateKeys.RootScope,
                        declarations[1].SchemaDefaults);

                    EditAdmission admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    facts.LaneEpochAfterForward = lane.Committed.Epoch.Value;

                    DerivedAssemblyReport forward = pipeline.PublishDerived(operation);
                    facts.ForwardDerivationHadNoTargetChange =
                        forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    // The forward provider's composition publication carries no derivable target change, so the
                    // world's assembly for that publication is the spawn: the lane's publication number is the
                    // epoch the spawn publishes at, and both counters end on the same value (P-006, P-024).
                    DerivedAssemblyReport spawn = pipeline.PublishSpawn(
                        NextOperation(host.World),
                        W2GateKeys.FutureVillager,
                        W2GateKeys.VillagerRecipe,
                        W2GateKeys.RootScope);

                    facts.WorldEpochAfterSpawn = host.CurrentEpoch.Value;
                    facts.CountersJoinedAfterSpawn = spawn.CountersJoined;
                    facts.RecipeApplyCount = applier.AppliedCount;

                    bool registered = targets.TryRegister(
                        W2GateKeys.FutureVillager,
                        W2GateKeys.RootScope,
                        W2GateKeys.VillagerRecipe,
                        out DiagnosticCode registeredCode,
                        out string registeredDetail);

                    if (publisher.Registry.TryResolveTarget(W2GateKeys.FutureVillager, out _, out Entity entity))
                    {
                        IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(W2GateKeys.FutureVillager);
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
                    }

                    facts.SpawnedTargetInPublishedView = publisher.Published.Bindings.HasTarget(W2GateKeys.FutureVillager);
                    facts.PublishedBindingRowCount = publisher.Published.BindingRowCount;

                    bool pass = admission.Staged
                        && published.Count == 1
                        && published[0].Outcome == Outcome.Published
                        && facts.ForwardDerivationHadNoTargetChange
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.IsSpawn
                        && facts.LaneEpochAfterForward == 3UL
                        && facts.WorldEpochAfterSpawn == 3UL
                        && facts.CountersJoinedAfterSpawn
                        && facts.SpawnedBindingRowCount == 1
                        && facts.SpawnedBindingValue == W2GateDeclarations.VillagerBindingValue
                        && facts.SpawnedStampEpoch == facts.WorldEpochAfterSpawn
                        && facts.SpawnedStampPublished
                        && facts.SpawnedTargetInPublishedView
                        && facts.PublishedBindingRowCount == 3
                        && facts.RecipeApplyCount == 1
                        && registered;

                    steps.Add(new W2GateStep(name, pass,
                        "forward=" + forward.Outcome
                        + "; noTargetChange=" + facts.ForwardDerivationHadNoTargetChange
                        + "; spawn=" + spawn.Describe()
                        + "; laneEpoch=" + facts.LaneEpochAfterForward.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + facts.WorldEpochAfterSpawn.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + facts.CountersJoinedAfterSpawn
                        + "; spawnedRows=" + facts.SpawnedBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; spawnedValue=" + facts.SpawnedBindingValue.ToString(CultureInfo.InvariantCulture)
                        + "; spawnedStampEpoch=" + facts.SpawnedStampEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; spawnedPublished=" + facts.SpawnedStampPublished
                        + "; spawnedInView=" + facts.SpawnedTargetInPublishedView
                        + "; publishedRows=" + facts.PublishedBindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; recipeApplies=" + facts.RecipeApplyCount.ToString(CultureInfo.InvariantCulture)
                        + "; indexRegistered=" + registered
                        + (registered ? string.Empty : " (" + registeredCode + ": " + registeredDetail + ")")));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 10. an idle world performs no step

            private void ProveIdleWorldPerformsNoStep()
            {
                const string name = "gate2-idle-world-performs-zero-steps";
                try
                {
                    if (host == null || time == null)
                    {
                        steps.Add(new W2GateStep(name, false, "no time driver"));
                        return;
                    }

                    ulong stepBefore = host.CurrentStep.Value;
                    int imagesBefore = host.Publications.PublishedCount;
                    int runsBefore = host.StepGroup.DispatchRunCount;

                    ulong committed = 0UL;
                    for (int i = 0; i < IdleFrames; i++)
                    {
                        TimeFrameReport frame = time.PumpFrame(HostTicks);
                        committed += frame.StepsCommitted;
                    }

                    facts.IdleFrames = IdleFrames;
                    facts.IdleStepsCommitted = (int)committed;
                    facts.IdlePublishedImages = host.Publications.PublishedCount;
                    facts.IdleDispatchRuns = host.StepGroup.DispatchRunCount - runsBefore;
                    facts.PendingDemandAfterIdle = host.PendingDemand;

                    bool pass = committed == 0UL
                        && host.CurrentStep.Value == stepBefore
                        && facts.IdlePublishedImages == imagesBefore
                        && facts.IdleDispatchRuns == 0
                        && facts.PendingDemandAfterIdle == 0UL
                        && time.Clocks.PendingWakeCount == 0;

                    steps.Add(new W2GateStep(name, pass,
                        "frames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
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
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ 11. teardown

            private void TearDownSafely()
            {
                const string name = "gate2-teardown-settles-and-disposes";
                try
                {
                    if (host == null)
                    {
                        steps.Add(new W2GateStep(name, false, "no world"));
                        return;
                    }

                    facts.OutstandingJobsBeforeTeardown = host.Ledger.OutstandingJobCount;

                    if (time != null)
                    {
                        time.Clear(out int discardedCommands, out int pendingWakes);
                    }

                    // The producer's container is released after its writer settled, so no job can write into freed
                    // memory (P-047, P-048).
                    W2GateModule.DetachAll();

                    OperationResult stop = host.Stop(NextOperation(host.World), "w2 gate teardown");
                    host.Dispose();

                    facts.OutstandingJobsAfterTeardown = host.Ledger.OutstandingJobCount;
                    facts.RetainedResourcesAfterTeardown = host.Ledger.RetainedResourceCount;
                    facts.RegistryAfterTeardown = UnityWorldRegistry.Count;

                    bool pass = (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        ? facts.OutstandingJobsAfterTeardown == 0
                            && facts.RetainedResourcesAfterTeardown == 0
                            && facts.RegistryAfterTeardown == registryBeforeCreate
                        : false;

                    steps.Add(new W2GateStep(name, pass,
                        "stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; outstandingBefore=" + facts.OutstandingJobsBeforeTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; outstandingAfter=" + facts.OutstandingJobsAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + facts.RetainedResourcesAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + facts.RegistryAfterTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)));
                }
                catch (Exception exception)
                {
                    steps.Add(new W2GateStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ helpers

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, W2GateKeys.Issuer, operationSequence);
            }

            private static ulong StagedByteLimit() => StagedByteCeiling;

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
