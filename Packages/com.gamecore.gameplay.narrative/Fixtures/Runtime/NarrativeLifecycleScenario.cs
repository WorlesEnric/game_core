// GameCore.Gameplay.Narrative.Fixtures — the GC-014 narrative installation-lifecycle scenario.
//
// The sentences this file implements, from `docs/game-core/09-implementation-guide.md` (GC-014) and
// `docs/game-core/08-validation-and-performance.md` (TEST-002/003/008/015/016/018):
//
//   "All installation transitions of the P-046 table over the control/publication path (activation, reconfiguration,
//    replacement, suspend/resume, unload), invalid transitions rejected."
//   "Removing a required provider makes consumers WAIT in the same publication; a compatible provider's return
//    resumes them."
//   "Suspension retracts active behavior; late completions (async work tokens) cannot resurrect it; a blocked job
//    prevents buffer release."
//   "Repeated operations obey the ledger."
//   "Teardown reports unreleased/quarantined resources accurately and does not report successful disposal while jobs
//    still own native buffers."
//
// Every step below drives the real modules and reads their own reports:
//
//   * GC-010's narrative world is built exactly as `NarrativeScenario` builds it (same registration, same
//     ownership/schedule compile, same recipes/seeder/lane seed/derived pipeline), and the chapter provider is
//     mounted at `chapter-one`;
//   * `LifecycleController` (GC-014's Unity glue) is the one driver of suspend / resume / unmount / unload, so the
//     lane submission, the publication boundary and the derived assembly publication stay in the fixed order;
//   * the suspend/resume/reconfigure/unmount payloads are built here, because the narrative slice's own scenario
//     publishes only mounts;
//   * the required-service consumer/provider pair of steps 4 and 5 is declared by this file (the narrative
//     vocabulary's two chapters declare no service exports and no service dependencies at all, so nothing in the
//     slice can lose a required provider) and is installed through a private `IPluginManifestSource` wrapper, so the
//     required-service relationship is a real `ServiceDependency` with `Required = true` resolved by the real
//     `ServiceResolver` — not a scenario-side model of one.
//
// The runner is shared by the Unity EditMode test (`GameCore.Lifecycle.Tests`) and by any caller that wants the
// scenario's own fact bag.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;
using Unity.Jobs;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.Gameplay.Narrative.Fixtures
{
    /// <summary>One named lifecycle observation: what was checked and the values it was checked from.</summary>
    public sealed class NarrativeLifecycleStep
    {
        public NarrativeLifecycleStep(string name, bool passed, string detail)
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
    /// Facts one lifecycle run observed, exposed by key so a caller asserts on values instead of trusting a boolean.
    /// Every value is read from live module state at the moment its own step describes, and the key set of one run is
    /// fixed: the same keys are set on a passing run and on a failing one.
    /// </summary>
    public sealed class NarrativeLifecycleFacts
    {
        private readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);

        public void Set(string key, string value)
        {
            if (key == null)
            {
                throw new ArgumentNullException(nameof(key));
            }

            values[key] = value ?? string.Empty;
        }

        /// <summary>Counts are recorded as invariant integers (P-008: no culture-dependent evidence).</summary>
        public void Set(string key, long value) => Set(key, value.ToString(CultureInfo.InvariantCulture));

        /// <summary>Booleans are recorded as `True`/`False`, matching the repo's own facts digests.</summary>
        public void Set(string key, bool value) => Set(key, value ? "True" : "False");

        /// <summary>Reads one fact; a miss is refused rather than answered with an invented default (P-052).</summary>
        public string ValueOf(string key)
        {
            if (values.TryGetValue(key, out string? found))
            {
                return found;
            }

            throw new KeyNotFoundException("The lifecycle facts carry no value for the key '" + key + "'.");
        }

        public bool Has(string key) => values.ContainsKey(key);

        /// <summary>The fact keys of this run in canonical (ordinal) order.</summary>
        public IReadOnlyList<string> Keys
        {
            get
            {
                List<string> keys = new List<string>(values.Keys);
                keys.Sort(StringComparer.Ordinal);
                return keys;
            }
        }

        /// <summary>Stable rendering: one `key=value` line per key, in the same canonical order as <see cref="Keys"/>.</summary>
        public string Describe()
        {
            List<string> keys = new List<string>(values.Keys);
            keys.Sort(StringComparer.Ordinal);
            List<string> lines = new List<string>(keys.Count);
            for (int i = 0; i < keys.Count; i++)
            {
                lines.Add(keys[i] + "=" + values[keys[i]]);
            }

            return string.Join("\n", lines.ToArray());
        }
    }

    /// <summary>Full result of one lifecycle run: the named observations plus the facts they were read from.</summary>
    public sealed class NarrativeLifecycleScenarioResult
    {
        public NarrativeLifecycleScenarioResult(IReadOnlyList<NarrativeLifecycleStep> steps, NarrativeLifecycleFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<NarrativeLifecycleStep> Steps { get; }

        public NarrativeLifecycleFacts Facts { get; }

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
            List<string> failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " narrative lifecycle checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " narrative lifecycle check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>
    /// The scenario's fixed vocabulary: the twelve step names and the fifty-seven fact keys. A caller asserts on these
    /// names, so nothing here is derived from a runtime value.
    /// </summary>
    public static class NarrativeLifecycleKeys
    {
        // ---------------------------------------------------------------- step names

        public const string StepWorld = "narrative-lifecycle-world-and-provider";

        public const string StepSuspend = "narrative-lifecycle-suspend-retracts-behavior";

        public const string StepResume = "narrative-lifecycle-resume-restores-behavior";

        public const string StepProviderLoss = "narrative-lifecycle-provider-loss-makes-consumers-wait";

        public const string StepProviderReturn = "narrative-lifecycle-provider-return-resumes-consumers";

        public const string StepReplacement = "narrative-lifecycle-replacement-stages-while-old-runs";

        public const string StepUnload = "narrative-lifecycle-unload-closes-ingress-and-retracts";

        public const string StepJobFence = "narrative-lifecycle-blocked-job-prevents-buffer-release";

        public const string StepInvalidTransitions = "narrative-lifecycle-invalid-transitions-rejected";

        public const string StepRepeatedOperations = "narrative-lifecycle-repeated-operations-obey-ledger";

        public const string StepTeardown = "narrative-lifecycle-teardown-settles-and-disposes";

        public const string StepFacts = "narrative-lifecycle-facts";

        // ---------------------------------------------------------------- world

        public const string FactWorldLifecycle = "worldLifecycle";

        public const string FactLaneJoined = "laneJoined";

        public const string FactProviderStateBefore = "providerStateBefore";

        public const string FactProviderRowsBefore = "providerRowsBefore";

        public const string FactRegistryBeforeCreate = "registryBeforeCreate";

        // ---------------------------------------------------------------- suspend

        public const string FactSuspendState = "suspendState";

        public const string FactSuspendRowsAfter = "suspendRowsAfter";

        public const string FactSuspendClosedRoutes = "suspendClosedRoutes";

        public const string FactSuspendLateCompletion = "suspendLateCompletion";

        public const string FactSuspendGateLiveActivations = "suspendGateLiveActivations";

        // ---------------------------------------------------------------- resume

        public const string FactResumeState = "resumeState";

        public const string FactResumeRowsAfter = "resumeRowsAfter";

        // ---------------------------------------------------------------- provider loss

        public const string FactLossConsumerState = "lossConsumerState";

        public const string FactLossWaitingConsumers = "lossWaitingConsumers";

        public const string FactLossConsumerBindings = "lossConsumerBindings";

        public const string FactLossRetractedRows = "lossRetractedRows";

        // ---------------------------------------------------------------- provider return

        public const string FactReturnConsumerState = "returnConsumerState";

        public const string FactReturnResumedConsumers = "returnResumedConsumers";

        public const string FactReturnRows = "returnRows";

        // ---------------------------------------------------------------- replacement

        public const string FactReplacementStagedCandidates = "replacementStagedCandidates";

        public const string FactReplacementOldHoldsAuthority = "replacementOldHoldsAuthority";

        public const string FactReplacementEpochChanged = "replacementEpochChanged";

        public const string FactReplacementGenerationUnchanged = "replacementGenerationUnchanged";

        public const string FactReplacementStateAfter = "replacementStateAfter";

        public const string FactReplacementRowsAfter = "replacementRowsAfter";

        // ---------------------------------------------------------------- unload

        public const string FactUnloadState = "unloadState";

        public const string FactUnloadIngressClosed = "unloadIngressClosed";

        public const string FactUnloadRetractedRows = "unloadRetractedRows";

        public const string FactUnloadRetiredLeases = "unloadRetiredLeases";

        public const string FactUnloadQuarantined = "unloadQuarantined";

        public const string FactUnloadLateCompletion = "unloadLateCompletion";

        // ---------------------------------------------------------------- job fence

        public const string FactFenceOutstandingJobs = "fenceOutstandingJobs";

        public const string FactFenceBlockedCode = "fenceBlockedCode";

        public const string FactFenceRetainedWhileOutstanding = "fenceRetainedWhileOutstanding";

        public const string FactFenceDisposeSettledWhileOutstanding = "fenceDisposeSettledWhileOutstanding";

        public const string FactFenceQuarantineBeforeRelease = "fenceQuarantineBeforeRelease";

        public const string FactFenceReleasedAfterCompletion = "fenceReleasedAfterCompletion";

        public const string FactFenceQuarantineAfterRelease = "fenceQuarantineAfterRelease";

        // ---------------------------------------------------------------- invalid transitions

        public const string FactInvalidRejectedCount = "invalidRejectedCount";

        public const string FactInvalidStateUnchanged = "invalidStateUnchanged";

        public const string FactInvalidSuspendTwiceCode = "invalidSuspendTwiceCode";

        public const string FactInvalidResumeActiveCode = "invalidResumeActiveCode";

        public const string FactInvalidUnmountDisposedCode = "invalidUnmountDisposedCode";

        public const string FactInvalidReconfigureDisposedCode = "invalidReconfigureDisposedCode";

        public const string FactInvalidRemountLiveIdentityCode = "invalidRemountLiveIdentityCode";

        public const string FactInvalidTeardownPathRefusedCode = "invalidTeardownPathRefusedCode";

        // ---------------------------------------------------------------- repeated operations

        public const string FactRepeatSuspendRefusedCode = "repeatSuspendRefusedCode";

        public const string FactRepeatRetransmissionKind = "repeatRetransmissionKind";

        public const string FactRepeatReconfigureSameOutcome = "repeatReconfigureSameOutcome";

        public const string FactRepeatUnmountRefusedCode = "repeatUnmountRefusedCode";

        public const string FactLedgerRowCount = "ledgerRowCount";

        // ---------------------------------------------------------------- teardown

        public const string FactRegistryAfterTeardown = "registryAfterTeardown";

        public const string FactOutstandingJobsAfterTeardown = "outstandingJobsAfterTeardown";

        public const string FactRetainedResourcesAfterTeardown = "retainedResourcesAfterTeardown";

        public const string FactIdleSteps = "idleSteps";

        // ---------------------------------------------------------------- family semantics

        public const string FactGateDecisionAfterSuspend = "gateDecisionAfterSuspend";

        public const string FactFactVersionAfterResume = "factVersionAfterResume";
    }

    /// <summary>
    /// The lifecycle payloads the narrative slice's own scenario does not publish: suspend, resume, unmount and
    /// reconfigure. A mount is `NarrativeMounts.Mount`, which already declares the effective configuration hash the
    /// composition applier recomputes (P-020); the other four declare only the installation identity, because that is
    /// all the applier's lifecycle/reconfigure/unmount paths read. A payload's expected edit kind follows from its
    /// subject (`InstallUnmount` is `Remove`; suspend, resume and reconfigure are `Update`).
    /// </summary>
    public static class NarrativeLifecyclePayloads
    {
        /// <summary>O-03 mount of one provider instance at one scope, with the declared effective configuration hash.</summary>
        public static CompositionEditPayload Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope) =>
            NarrativeMounts.Mount(manifest, instance, scope, ConfigDocument.Empty);

        /// <summary>O-06: suspend one installation, retaining its definition and configuration (P-046).</summary>
        public static CompositionEditPayload Suspend(PluginInstanceId instance) =>
            Lifecycle(CompositionEditSubject.InstallSuspend, instance);

        /// <summary>O-04: resume an installation that requested an explicit suspend.</summary>
        public static CompositionEditPayload Resume(PluginInstanceId instance) =>
            Lifecycle(CompositionEditSubject.InstallResume, instance);

        /// <summary>O-07: unmount one installation; the publication carries its removal (P-048).</summary>
        public static CompositionEditPayload Unmount(PluginInstanceId instance) =>
            Lifecycle(CompositionEditSubject.InstallUnmount, instance);

        /// <summary>
        /// O-05: reconfigure one installation's immutable configuration patch. The declared configuration hash must be
        /// the canonical hash of schema defaults over the inherited effective configuration over this patch, exactly
        /// as `CompositionEditApplier.PlanReconfigure` recomposes it, and the revision must advance (P-020, P-027).
        /// </summary>
        public static CompositionEditPayload Reconfigure(
            PluginManifest manifest,
            PluginInstanceId instance,
            DefinitionRevision revision,
            ContentHash configHash,
            ConfigDocument patch)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return new CompositionEditPayload(
                CompositionEditSubject.InstallReconfigure,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                revision,
                configHash,
                patch ?? ConfigDocument.Empty,
                0,
                null,
                PropagationMode.Automatic);
        }

        private static CompositionEditPayload Lifecycle(CompositionEditSubject subject, PluginInstanceId instance) =>
            new CompositionEditPayload(
                subject,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                instance,
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
    }

    /// <summary>
    /// Runs the GC-014 narrative installation-lifecycle scenario against real modules only.
    ///
    /// Both entry points build the same real narrative world (the same registration, ownership/schedule compile,
    /// recipes, seeder, lane seed and derived pipeline the GC-010 slice uses) over this package's generated-style
    /// `NarrativeScenarioCatalog`, with two independent declaration identity sets; the second entry labels its steps
    /// with <see cref="FixtureRunPrefix"/>. This package cannot reference the Unity project's committed generated
    /// catalog (`GameCore.Validation.Generated` lives above it), so the committed-catalog path is exercised by
    /// `NarrativeScenarioHost.RunGeneratedCatalog` from the qualification project on the same revision - the
    /// lifecycle suite runs that host alongside this scenario, so both paths are covered in one run. The names are
    /// kept identical to the family's own hosts so a caller reads them the same way in both places.
    /// </summary>
    public static class NarrativeLifecycleScenario
    {
        /// <summary>Prefix a fixture run's step names carry (the repo's `RunBoth` convention).</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Runs the scenario over the first declaration identity set, without a step-name prefix.</summary>
        public static NarrativeLifecycleScenarioResult RunGeneratedCatalog() => new Executor(false).Run();

        /// <summary>Runs the scenario over the second declaration identity set, with <see cref="FixtureRunPrefix"/>.</summary>
        public static NarrativeLifecycleScenarioResult RunFixtureCatalog() => new Executor(true).Run();

        /// <summary>
        /// Runs both entries and returns their steps together, the fixture run's names prefixed. Neither run throws:
        /// a failure is one step with the failure text in its detail.
        /// </summary>
        public static IReadOnlyList<NarrativeLifecycleStep> RunBoth(
            out NarrativeLifecycleFacts generatedFacts,
            out NarrativeLifecycleFacts fixtureFacts)
        {
            NarrativeLifecycleScenarioResult generated = RunGeneratedCatalog();
            NarrativeLifecycleScenarioResult fixture = RunFixtureCatalog();
            generatedFacts = generated.Facts;
            fixtureFacts = fixture.Facts;

            List<NarrativeLifecycleStep> steps = new List<NarrativeLifecycleStep>(
                generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                steps.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                steps.Add(fixture.Steps[i]);
            }

            return steps;
        }

        private sealed class Executor
        {
            /// <summary>Bounded temporary storage the slice's plans may reserve, in bytes.</summary>
            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes.</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            /// <summary>Identity salt of the no-row carriers that consume an adopted-and-pending publication pair (P-006, P-024).</summary>
            private static readonly Id128 CarrierSalt = new Id128(0x4E4C434152524945UL, 1UL);

            /// <summary>Stable contract identity of the required service steps 4 and 5 remove and restore (P-011).</summary>
            private static readonly ContractRef RequiredService = new ContractRef(new Id128(0x4E4C535256434F4EUL, 1UL), 1U);

            /// <summary>Factory key the required service's export names (the exporter's registration identity).</summary>
            private static readonly FactoryKey RequiredServiceFactory =
                NarrativeKeys.Key("narrative.lifecycle.service-factory");

            // ---------------------------------------------------------------- lifecycle installation identities

            /// <summary>Installation of the required service's consumer (P-011).</summary>
            private static readonly PluginInstanceId ServiceConsumerInstall =
                NarrativeKeys.Instance(11UL);

            /// <summary>Installation of the required service's provider (P-011).</summary>
            private static readonly PluginInstanceId ServiceProviderInstall =
                NarrativeKeys.Instance(12UL);

            /// <summary>Installation of the compatible provider whose return resumes the consumer.</summary>
            private static readonly PluginInstanceId ServiceProviderReplacementInstall =
                NarrativeKeys.Instance(13UL);

            /// <summary>Installation of the job-fence step's tracked lease (P-047).</summary>
            private static readonly PluginInstanceId FencedInstall =
                NarrativeKeys.Instance(14UL);

            /// <summary>Every fact key one run sets; the same set on a passing and on a failing run (P-060).</summary>
            private static readonly string[] DeclaredFactKeys =
            {
                NarrativeLifecycleKeys.FactWorldLifecycle,
                NarrativeLifecycleKeys.FactLaneJoined,
                NarrativeLifecycleKeys.FactProviderStateBefore,
                NarrativeLifecycleKeys.FactProviderRowsBefore,
                NarrativeLifecycleKeys.FactRegistryBeforeCreate,
                NarrativeLifecycleKeys.FactSuspendState,
                NarrativeLifecycleKeys.FactSuspendRowsAfter,
                NarrativeLifecycleKeys.FactSuspendClosedRoutes,
                NarrativeLifecycleKeys.FactSuspendLateCompletion,
                NarrativeLifecycleKeys.FactSuspendGateLiveActivations,
                NarrativeLifecycleKeys.FactResumeState,
                NarrativeLifecycleKeys.FactResumeRowsAfter,
                NarrativeLifecycleKeys.FactLossConsumerState,
                NarrativeLifecycleKeys.FactLossWaitingConsumers,
                NarrativeLifecycleKeys.FactLossConsumerBindings,
                NarrativeLifecycleKeys.FactLossRetractedRows,
                NarrativeLifecycleKeys.FactReturnConsumerState,
                NarrativeLifecycleKeys.FactReturnResumedConsumers,
                NarrativeLifecycleKeys.FactReturnRows,
                NarrativeLifecycleKeys.FactReplacementStagedCandidates,
                NarrativeLifecycleKeys.FactReplacementOldHoldsAuthority,
                NarrativeLifecycleKeys.FactReplacementEpochChanged,
                NarrativeLifecycleKeys.FactReplacementGenerationUnchanged,
                NarrativeLifecycleKeys.FactReplacementStateAfter,
                NarrativeLifecycleKeys.FactReplacementRowsAfter,
                NarrativeLifecycleKeys.FactUnloadState,
                NarrativeLifecycleKeys.FactUnloadIngressClosed,
                NarrativeLifecycleKeys.FactUnloadRetractedRows,
                NarrativeLifecycleKeys.FactUnloadRetiredLeases,
                NarrativeLifecycleKeys.FactUnloadQuarantined,
                NarrativeLifecycleKeys.FactUnloadLateCompletion,
                NarrativeLifecycleKeys.FactFenceOutstandingJobs,
                NarrativeLifecycleKeys.FactFenceBlockedCode,
                NarrativeLifecycleKeys.FactFenceRetainedWhileOutstanding,
                NarrativeLifecycleKeys.FactFenceDisposeSettledWhileOutstanding,
                NarrativeLifecycleKeys.FactFenceQuarantineBeforeRelease,
                NarrativeLifecycleKeys.FactFenceReleasedAfterCompletion,
                NarrativeLifecycleKeys.FactFenceQuarantineAfterRelease,
                NarrativeLifecycleKeys.FactInvalidRejectedCount,
                NarrativeLifecycleKeys.FactInvalidStateUnchanged,
                NarrativeLifecycleKeys.FactInvalidSuspendTwiceCode,
                NarrativeLifecycleKeys.FactInvalidResumeActiveCode,
                NarrativeLifecycleKeys.FactInvalidUnmountDisposedCode,
                NarrativeLifecycleKeys.FactInvalidReconfigureDisposedCode,
                NarrativeLifecycleKeys.FactInvalidRemountLiveIdentityCode,
                NarrativeLifecycleKeys.FactInvalidTeardownPathRefusedCode,
                NarrativeLifecycleKeys.FactRepeatSuspendRefusedCode,
                NarrativeLifecycleKeys.FactRepeatRetransmissionKind,
                NarrativeLifecycleKeys.FactRepeatReconfigureSameOutcome,
                NarrativeLifecycleKeys.FactRepeatUnmountRefusedCode,
                NarrativeLifecycleKeys.FactLedgerRowCount,
                NarrativeLifecycleKeys.FactRegistryAfterTeardown,
                NarrativeLifecycleKeys.FactOutstandingJobsAfterTeardown,
                NarrativeLifecycleKeys.FactRetainedResourcesAfterTeardown,
                NarrativeLifecycleKeys.FactIdleSteps,
                NarrativeLifecycleKeys.FactGateDecisionAfterSuspend,
                NarrativeLifecycleKeys.FactFactVersionAfterResume,
            };

            private readonly bool fixtureRun;
            private readonly List<NarrativeLifecycleStep> steps = new List<NarrativeLifecycleStep>();
            private readonly NarrativeLifecycleFacts facts = new NarrativeLifecycleFacts();
            private readonly IdSequence sessionSequence = new IdSequence(0x4E4C53455353494FUL);
            private readonly IdSequence resourceSequence = new IdSequence(0x4E4C5245534F5552UL);
            private readonly IdSequence jobSequence = new IdSequence(0x4E4C4A4F42534551UL);
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
            private LifecycleController? controller;
            private NarrativeLifecycleManifestSource? manifests;
            private NarrativeLifecycleResourceFactory? resources;
            private OperationId suspendOperation;
            private bool suspendOperationKnown;
            private ulong operationSequence;
            private int expectedLaneRows;
            private int carrierOrdinal;
            private int registryBeforeCreate;
            private long providerRowsBefore;
            private long consumerRowsBeforeLoss;
            private long consumerBindingsBeforeLoss;
            private ulong providerEpochBefore;
            private ulong providerGenerationBefore;
            private string buildFailure = string.Empty;
            private string seedFailure = string.Empty;
            private bool worldStopped;

            public Executor(bool fixtureRun)
            {
                this.fixtureRun = fixtureRun;
            }

            public NarrativeLifecycleScenarioResult Run()
            {
                registryBeforeCreate = UnityWorldRegistry.Count;
                try
                {
                    WorldAndProvider();
                    SuspendRetractsBehavior();
                    ResumeRestoresBehavior();
                    ProviderLossMakesConsumersWait();
                    ProviderReturnResumesConsumers();
                    ReplacementStagesWhileOldRuns();
                    UnloadClosesIngressAndRetracts();
                    BlockedJobPreventsBufferRelease();
                    InvalidTransitionsRejected();
                    RepeatedOperationsObeyLedger();
                    TeardownSettlesAndDisposes();
                    FactsStep();
                }
                finally
                {
                    // A failed step must not leave a world behind: the EditMode suite asserts the registry baseline.
                    TearDownSafely();
                    EnsureEveryFactIsSet();
                }

                if (fixtureRun)
                {
                    for (int i = 0; i < steps.Count; i++)
                    {
                        NarrativeLifecycleStep step = steps[i];
                        steps[i] = new NarrativeLifecycleStep(FixtureRunPrefix + step.Name, step.Passed, step.Detail);
                    }
                }

                return new NarrativeLifecycleScenarioResult(steps, facts);
            }

            // ------------------------------------------------------------------ 1. the world and its provider

            private void WorldAndProvider()
            {
                const string name = NarrativeLifecycleKeys.StepWorld;
                long providerRows = 0;
                string providerState = "<none>";
                bool laneJoined = false;
                bool pairActive = false;
                try
                {
                    if (!BuildWorld())
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, buildFailure));
                        return;
                    }

                    // The slice's own chapter provider, mounted at chapter-one exactly as its scenario mounts it.
                    MountChapterProvider();

                    // The required-service pair: the consumer declares a required dependency, the provider exports
                    // the contract. Both live at the world root scope, so the export is visible to its consumer.
                    LifecycleRequestReport providerReport = SubmitAndPublish(
                        NarrativeLifecyclePayloads.Mount(
                            ProviderManifest(), ServiceProviderInstall, NarrativeKeys.RootScope));
                    LifecycleRequestReport consumerReport = SubmitAndPublish(
                        NarrativeLifecyclePayloads.Mount(
                            ConsumerManifest(), ServiceConsumerInstall, NarrativeKeys.RootScope));

                    providerState = StateOf(NarrativeKeys.ChapterOneInstall);
                    providerRows = AttributedRows(NarrativeKeys.ChapterOneInstall);
                    laneJoined = LaneJoined(host!, lane!, publisher!);

                    pairActive = InstallStateOf(ServiceConsumerInstall) == InstallationState.Active
                        && InstallStateOf(ServiceProviderInstall) == InstallationState.Active
                        && consumerReport.Admission != null
                        && consumerReport.Admission.Staged;
                    bool pass = host != null
                        && module != null
                        && lane != null
                        && publisher != null
                        && pipeline != null
                        && controller != null
                        && UnityWorldRegistry.Count == registryBeforeCreate + 1
                        && laneJoined
                        && providerState == InstallationState.Active.ToString()
                        && providerRows > 0
                        && pairActive
                        && providerReport.Admission != null
                        && providerReport.Admission.Staged
                        && consumerReport.Admission != null
                        && consumerReport.Admission.Staged
                        && lane.Committed.Scopes.Count == NarrativeScopes.DeclaredScopeCount
                        && LaneJoined(host!, lane!, publisher!)
                        && host.Lifecycle == WorldLifecycleState.Running;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "session=" + host!.World.Session.ToString()
                        + "; registryBefore=" + Text(registryBeforeCreate)
                        + "; registryAfter=" + Text(UnityWorldRegistry.Count)
                        + "; providerState=" + providerState
                        + "; providerRows=" + Text(providerRows)
                        + "; laneJoined=" + laneJoined
                        + "; scopes=" + Text(lane.Committed.Scopes.Count)
                        + "; lifecycle=" + host.Lifecycle
                        + "; pairInstalled=" + pairActive
                        + DescribeFailures()));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                    return;
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactWorldLifecycle, LifecycleText());
                    facts.Set(NarrativeLifecycleKeys.FactLaneJoined, laneJoined);
                    facts.Set(NarrativeLifecycleKeys.FactProviderStateBefore, providerState);
                    facts.Set(NarrativeLifecycleKeys.FactProviderRowsBefore, providerRows);
                    facts.Set(NarrativeLifecycleKeys.FactRegistryBeforeCreate, registryBeforeCreate);
                    providerRowsBefore = providerRows;
                }

            }

            // ------------------------------------------------------------------ 2. suspend retracts behavior

            private void SuspendRetractsBehavior()
            {
                const string name = NarrativeLifecycleKeys.StepSuspend;
                string state = "<none>";
                long rows = -1;
                int closedRoutes = -1;
                string lateCompletion = "<none>";
                int liveActivations = -1;
                try
                {
                    if (controller == null || lane == null || host == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    PluginInstanceId provider = NarrativeKeys.ChapterOneInstall;
                    bool before = lane.Committed.TryGetInstall(provider, out InstallEntry? entry) && entry != null;
                    ulong epochBefore = before ? entry!.Record.ActivationEpoch.Value : 0UL;
                    InstallationGeneration generation = before ? entry!.Record.Generation : default(InstallationGeneration);
                    int liveBefore = lane.Callbacks.LiveActivationCount;

                    OperationId operation = NextLaneOperation();
                    suspendOperation = operation;
                    suspendOperationKnown = true;
                    bool joined = SubmitChapterEdit(
                        NarrativeLifecyclePayloads.Suspend(provider),
                        operation,
                        out EditAdmission _,
                        out IReadOnlyList<PublishedOperation> chapterPublished,
                        out DerivedAssemblyReport _);
                    PublishedOperation? published = PublishedOf(chapterPublished, operation);

                    state = StateOf(provider);
                    rows = AttributedRows(provider);
                    bool holdsAuthority = lane.Lifecycle.HoldsAuthority(provider);
                    liveActivations = lane.Callbacks.LiveActivationCount;
                    bool gateRetired = liveActivations < liveBefore;

                    LifecycleCommitReport? lifecycle = published != null ? published.Lifecycle : null;
                    ContributionRetraction? retraction = RetractionOf(lifecycle, provider);
                    closedRoutes = retraction != null ? retraction.ClosedRoutes : -1;
                    bool retracted = retraction != null && retraction.AttributedRows > 0;

                    // A completion that was in flight before the suspend: its route is retired, so the gate discards
                    // it instead of delivering it into an installation without authority (P-047).
                    lateCompletion = "notEvaluated";
                    CallbackGateDecision decision = CallbackGateDecision.Dispatch;
                    if (epochBefore != 0UL)
                    {
                        AsyncWorkToken token = new AsyncWorkToken(operation, provider, generation, new ActivationEpoch(epochBefore), 1U);
                        decision = lane.Lifecycle.EvaluateCompletion(token);
                        lateCompletion = decision.ToString();
                    }

                    bool lateDiscarded = decision == CallbackGateDecision.DiscardRetiredRoute
                        || decision == CallbackGateDecision.DiscardStaleActivation;
                    bool publicationOk = joined
                        && lifecycle != null
                        && lifecycle.Operation.Equals(operation)
                        && published != null
                        && published.Outcome == Outcome.Published;

                    bool pass = state == InstallationState.Suspended.ToString()
                        && !holdsAuthority
                        && gateRetired
                        && retracted
                        && rows == 0
                        && closedRoutes >= 0
                        && host.Lifecycle == WorldLifecycleState.Running
                        && lateDiscarded
                        && publicationOk;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "state=" + state
                        + "; holdsAuthority=" + holdsAuthority
                        + "; liveActivations=" + Text(liveBefore) + "->" + Text(liveActivations)
                        + "; attributedRows=" + Text(retraction != null ? retraction.AttributedRows : -1)
                        + "; closedRoutes=" + Text(closedRoutes)
                        + "; lateCompletion=" + lateCompletion
                        + "; publication=" + (published != null ? published.Outcome.ToString() : "<none>")
                        + "; lifecycle=" + host.Lifecycle));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactSuspendState, state);
                    facts.Set(NarrativeLifecycleKeys.FactSuspendRowsAfter, rows);
                    facts.Set(NarrativeLifecycleKeys.FactSuspendClosedRoutes, closedRoutes);
                    facts.Set(NarrativeLifecycleKeys.FactSuspendLateCompletion, lateCompletion);
                    facts.Set(NarrativeLifecycleKeys.FactSuspendGateLiveActivations, liveActivations);
                    facts.Set(NarrativeLifecycleKeys.FactGateDecisionAfterSuspend, GateDecisionText());
                }
            }

            // ------------------------------------------------------------------ 3. resume restores behavior

            private void ResumeRestoresBehavior()
            {
                const string name = NarrativeLifecycleKeys.StepResume;
                string state = "<none>";
                long rows = -1;
                try
                {
                    if (controller == null || lane == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    PluginInstanceId provider = NarrativeKeys.ChapterOneInstall;
                    OperationId operation = NextLaneOperation();
                    bool joined = SubmitChapterEdit(
                        NarrativeLifecyclePayloads.Resume(provider),
                        operation,
                        out EditAdmission _,
                        out _,
                        out DerivedAssemblyReport derived);

                    state = StateOf(provider);
                    rows = AttributedRows(provider);
                    bool holdsAuthority = lane.Lifecycle.HoldsAuthority(provider);
                    bool rowsRestored = rows == providerRowsBefore && rows > 0;

                    // Resume rederives against the current ancestry, so the resumed activation carries a fresh
                    // generation/epoch pair and a completion from before the suspend can never be delivered again.
                    bool dispatched = false;
                    if (lane.Committed.TryGetInstall(provider, out InstallEntry? entry) && entry != null)
                    {
                        AsyncWorkToken fresh = new AsyncWorkToken(
                            operation, provider, entry.Record.Generation, entry.Record.ActivationEpoch, 1U);
                        dispatched = lane.Lifecycle.EvaluateCompletion(fresh) == CallbackGateDecision.Dispatch;
                    }


                    bool pass = joined
                        && state == InstallationState.Active.ToString()
                        && holdsAuthority
                        && rowsRestored
                        && dispatched
                        && derived.Outcome != DerivedAssemblyOutcome.Refused;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "state=" + state
                        + "; holdsAuthority=" + holdsAuthority
                        + "; rows=" + Text(rows) + "/before=" + Text(providerRowsBefore)
                        + "; freshToken=" + (dispatched ? CallbackGateDecision.Dispatch.ToString() : "<discarded>")
                        + "; derived=" + derived.Outcome + "(" + DiagnosticCodeText.Of(derived.Code) + ")"));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactResumeState, state);
                    facts.Set(NarrativeLifecycleKeys.FactResumeRowsAfter, rows);
                    facts.Set(NarrativeLifecycleKeys.FactFactVersionAfterResume, FactVersionText());
                }
            }

            // ------------------------------------------------------------------ 4. a lost provider makes consumers wait

            private void ProviderLossMakesConsumersWait()
            {
                const string name = NarrativeLifecycleKeys.StepProviderLoss;
                string state = "<none>";
                int waiting = -1;
                int bindings = -1;
                long retractedRows = -1;
                try
                {
                    if (controller == null || lane == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    consumerRowsBeforeLoss = AttributedRows(ServiceConsumerInstall);
                    consumerBindingsBeforeLoss = BindingCount(ServiceConsumerInstall);

                    OperationId operation = NextLaneOperation();
                    LifecycleRequestReport report = SubmitAndPublish(
                        NarrativeLifecyclePayloads.Unmount(ServiceProviderInstall), operation);
                    PublishedOperation? published = FindPublished(report, operation);

                    state = StateOf(ServiceConsumerInstall);
                    bindings = BindingCount(ServiceConsumerInstall);
                    waiting = published != null ? published.WaitingConsumers.Count : -1;

                    ContributionRetraction? retraction = RetractionOf(report.Lifecycle, ServiceConsumerInstall);
                    retractedRows = retraction != null ? retraction.AttributedRows : -1;

                    bool named = published != null && NamesExactly(published.WaitingConsumers, ServiceConsumerInstall);
                    bool retractedAsBefore = retraction != null
                        && retractedRows == consumerRowsBeforeLoss
                        && consumerRowsBeforeLoss > 0;

                    bool pass = state == InstallationState.WaitingForDependencies.ToString()
                        && waiting == 1
                        && named
                        && bindings == 0
                        && consumerBindingsBeforeLoss > 0
                        && retractedAsBefore
                        && report.Derived != null
                        && report.Derived.Outcome != DerivedAssemblyOutcome.Refused;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "consumerState=" + state
                        + "; waitingConsumers=" + Text(waiting)
                        + "; consumerBindings=" + Text(consumerBindingsBeforeLoss) + "->" + Text(bindings)
                        + "; attributedRows=" + Text(consumerRowsBeforeLoss)
                        + "; retractedRows=" + Text(retractedRows)
                        + "; providerState=" + StateOf(ServiceProviderInstall)
                        + "; samePublication=" + (published != null)
                        + "; derived=" + DescribeDerived(report)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactLossConsumerState, state);
                    facts.Set(NarrativeLifecycleKeys.FactLossWaitingConsumers, waiting);
                    facts.Set(NarrativeLifecycleKeys.FactLossConsumerBindings, bindings);
                    facts.Set(NarrativeLifecycleKeys.FactLossRetractedRows, retractedRows);
                }
            }

            // ------------------------------------------------------------------ 5. a returned provider resumes them

            private void ProviderReturnResumesConsumers()
            {
                const string name = NarrativeLifecycleKeys.StepProviderReturn;
                string state = "<none>";
                int resumed = -1;
                long rows = -1;
                try
                {
                    if (controller == null || lane == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    // A compatible provider: the same contract, its own installation identity and its own type.
                    OperationId operation = NextLaneOperation();
                    LifecycleRequestReport report = SubmitAndPublish(
                        NarrativeLifecyclePayloads.Mount(
                            ProviderReplacementManifest(), ServiceProviderReplacementInstall, NarrativeKeys.RootScope),
                        operation);
                    PublishedOperation? published = FindPublished(report, operation);

                    state = StateOf(ServiceConsumerInstall);
                    rows = AttributedRows(ServiceConsumerInstall);
                    resumed = published != null ? published.ResumedConsumers.Count : -1;

                    bool named = published != null
                        && NamesExactly(published.ResumedConsumers, ServiceConsumerInstall);
                    int bindings = BindingCount(ServiceConsumerInstall);

                    bool pass = state == InstallationState.Active.ToString()
                        && resumed == 1
                        && named
                        && bindings == (int)consumerBindingsBeforeLoss
                        && rows == consumerRowsBeforeLoss
                        && report.Derived != null
                        && report.Derived.Outcome != DerivedAssemblyOutcome.Refused;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "consumerState=" + state
                        + "; resumedConsumers=" + Text(resumed)
                        + "; bindings=" + Text(bindings) + "/before=" + Text(consumerBindingsBeforeLoss)
                        + "; rows=" + Text(rows) + "/before=" + Text(consumerRowsBeforeLoss)
                        + "; derived=" + DescribeDerived(report)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactReturnConsumerState, state);
                    facts.Set(NarrativeLifecycleKeys.FactReturnResumedConsumers, resumed);
                    facts.Set(NarrativeLifecycleKeys.FactReturnRows, rows);
                }
            }

            // ------------------------------------------------------------------ 6. replacement stages while the old runs

            private void ReplacementStagesWhileOldRuns()
            {
                const string name = NarrativeLifecycleKeys.StepReplacement;
                long stagedCandidates = -1;
                bool oldHeldAuthority = false;
                bool epochChanged = false;
                bool generationUnchanged = false;
                string state = "<none>";
                long rows = -1;
                try
                {
                    if (controller == null || lane == null || pipeline == null || host == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    PluginInstanceId provider = NarrativeKeys.ChapterOneInstall;
                    if (!lane.Committed.TryGetInstall(provider, out InstallEntry? entry) || entry == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the chapter provider is not installed"));
                        return;
                    }

                    providerEpochBefore = entry.Record.ActivationEpoch.Value;
                    providerGenerationBefore = entry.Record.Generation.Value;
                    ConfigDocument previousConfig = entry.Config;

                    // The reconfigure declares the hash the applier itself recomposes: schema defaults, then the
                    // installation's inherited effective configuration, then this (empty) patch (P-020).
                    ConfigComposeResult composed = ConfigComposer.Compose(new[]
                    {
                        new ConfigLayer(
                            ConfigLayerOrigin.SchemaDefaults,
                            entry.Manifest.ConfigSchema.Id.Value,
                            ConfigDocument.Empty),
                        new ConfigLayer(
                            ConfigLayerOrigin.InheritedContribution,
                            provider.Value,
                            previousConfig),
                        new ConfigLayer(ConfigLayerOrigin.LocalPatch, provider.Value, ConfigDocument.Empty),
                    });

                    DefinitionRevision revision = new DefinitionRevision(entry.Record.ConfigRevision.Value + 1UL);
                    CompositionEditPayload reconfigurePayload = NarrativeLifecyclePayloads.Reconfigure(
                        entry.Manifest, provider, revision, ConfigDocumentCodec.HashOf(composed.Value), ConfigDocument.Empty);

                    OperationId operation = NextLaneOperation();
                    int replacementsBefore = lane.Lifecycle.Activations.CommittedReplacementCount;

                    // A staged resource for the provider, acquired under the activation epoch the reconfigure is about
                    // to publish: the unmount of step 7 retires exactly the leases of the epoch that acquired them.
                    DeclareChapterIngress();
                    EditAdmission admission = lane.SubmitEdit(reconfigurePayload, operation, lane.Committed.Revision);

                    // Phase 1 of the plan: staging really happened and the running activation is untouched. These two
                    // observations are taken between admission and the publication boundary, which is the only
                    // moment "a candidate is staged while the old activation continues" is directly readable (P-046).
                    stagedCandidates = lane.Lifecycle.Activations.StagedCandidateCount;
                    bool stagedObserved = lane.Lifecycle.Activations.TryGetCandidate(provider, out ActivationAttempt? stagedAttempt)
                        && stagedAttempt != null
                        && !stagedAttempt.HoldsAuthority;
                    bool oldRanAtStaging = lane.Lifecycle.HoldsAuthority(provider);

                    ResourceKey leaseKey = new ResourceKey(resourceSequence.Next());
                    bool stagedLease = lane.StageResource(
                        operation, provider, leaseKey, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode leaseCode);
                    IReadOnlyList<StagedLease> leases = lane.StagedLeases(operation);
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    DerivedAssemblyReport derived = pipeline.PublishDerived(operation);

                    bool candidateCommitted = lane.Lifecycle.Activations.CommittedReplacementCount > replacementsBefore;
                    epochChanged = lane.Committed.TryGetInstall(provider, out InstallEntry? after) && after != null
                        && after.Record.ActivationEpoch.Value != providerEpochBefore;
                    generationUnchanged = after != null && after.Record.Generation.Value == providerGenerationBefore;
                    state = StateOf(provider);

                    // The rows the replacement publication carries: identical to the ones the old activation
                    // contributed, because a reconfiguration stages a candidate and retracts nothing but its
                    // predecessor's leases (P-046). Read before the carrier of a no-change publication below.
                    rows = AttributedRows(provider);
                    LifecycleCommitReport? lifecycle = LifecycleOf(published, operation);
                    bool displacedTornDown = HasTeardownAtEpoch(lifecycle, provider, providerEpochBefore);
                    oldHeldAuthority = oldRanAtStaging && displacedTornDown;
                    bool leaseAccepted = stagedLease && leases.Count == 1;

                    // A reconfiguration changes no target's effective assembly, so the world's half of this
                    // publication is its unchanged assembly, not a spawn carrier: a carrier would copy the
                    // chapter's published rules onto the carrier target and inflate the installation's attributed
                    // rows (P-006).
                    bool carried = CompleteWorldHalf(operation, derived);

                    bool pass = admission.Staged
                        && leaseAccepted
                        && stagedCandidates == 1
                        && stagedObserved
                        && oldRanAtStaging
                        && candidateCommitted
                        && oldHeldAuthority
                        && epochChanged
                        && generationUnchanged
                        && state == InstallationState.Active.ToString()
                        && rows == providerRowsBefore
                        && lifecycle != null
                        && lifecycle.Outcome == Outcome.Published
                        && carried
                        && LaneJoined(host, lane, publisher!);

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "admission=" + admission.Kind + "/" + DiagnosticCodeText.Of(admission.Code)
                        + "; stagedLease=" + leaseAccepted + "/" + DiagnosticCodeText.Of(leaseCode)
                        + "; stagedCandidates=" + Text(stagedCandidates)
                        + "; stagedCandidateRecorded=" + stagedObserved
                        + "; oldRanAtStaging=" + oldRanAtStaging
                        + "; candidateCommitted=" + candidateCommitted
                        + "; displacedTornDown=" + displacedTornDown
                        + "; oldHeldAuthority=" + oldHeldAuthority
                        + "; epoch=" + Text(providerEpochBefore) + "->" + (after != null ? Text(after.Record.ActivationEpoch.Value) : "?")
                        + "; generation=" + Text(providerGenerationBefore) + "->" + (after != null ? Text(after.Record.Generation.Value) : "?")
                        + "; state=" + state
                        + "; rows=" + Text(rows) + "/before=" + Text(providerRowsBefore)
                        + "; derived=" + derived.Outcome
                        + "; carried=" + carried
                        + "; leaseId=" + (leases.Count != 0 ? leases[0].LeaseId.ToString() : "<none>")));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactReplacementStagedCandidates, stagedCandidates);
                    facts.Set(NarrativeLifecycleKeys.FactReplacementOldHoldsAuthority, oldHeldAuthority);
                    facts.Set(NarrativeLifecycleKeys.FactReplacementEpochChanged, epochChanged);
                    facts.Set(NarrativeLifecycleKeys.FactReplacementGenerationUnchanged, generationUnchanged);
                    facts.Set(NarrativeLifecycleKeys.FactReplacementStateAfter, state);
                    facts.Set(NarrativeLifecycleKeys.FactReplacementRowsAfter, rows);
                }
            }

            // ------------------------------------------------------------------ 7. unload closes ingress and retracts

            private void UnloadClosesIngressAndRetracts()
            {
                const string name = NarrativeLifecycleKeys.StepUnload;
                string state = "<none>";
                long ingressClosed = -1;
                long retractedRows = -1;
                long retiredLeases = -1;
                long quarantined = -1;
                string lateCompletion = "<none>";
                try
                {
                    if (controller == null || lane == null || host == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    PluginInstanceId provider = NarrativeKeys.ChapterOneInstall;
                    long rowsBefore = AttributedRows(provider);
                    int closedBefore = controller.Binding.ClosedInstallationCount;
                    int routesBefore = controller.Binding.RetiredRouteCount;
                    int disposedBefore = resources != null ? resources.DisposeCount : 0;

                    bool epochKnown = lane.Committed.TryGetInstall(provider, out InstallEntry? entry) && entry != null;
                    ulong epoch = epochKnown ? entry!.Record.ActivationEpoch.Value : 0UL;
                    InstallationGeneration generation = epochKnown ? entry!.Record.Generation : default(InstallationGeneration);

                    OperationId operation = NextLaneOperation();
                    bool joined = SubmitChapterEdit(
                        NarrativeLifecyclePayloads.Unmount(provider),
                        operation,
                        out EditAdmission _,
                        out IReadOnlyList<PublishedOperation> chapterPublished,
                        out DerivedAssemblyReport _);
                    PublishedOperation? published = PublishedOf(chapterPublished, operation);

                    state = StateOf(provider);
                    ingressClosed = controller.Binding.ClosedInstallationCount - closedBefore;
                    long routesRetired = controller.Binding.RetiredRouteCount - routesBefore;

                    TeardownReport? teardown = TeardownOf(published != null ? published.Lifecycle : null, provider);
                    retractedRows = teardown != null ? teardown.Retraction.AttributedRows : -1;
                    retiredLeases = teardown != null ? teardown.Cleanup.Retired.Count : -1;
                    quarantined = teardown != null ? teardown.Quarantined.Count : -1;

                    bool lateDiscarded = false;
                    lateCompletion = "notEvaluated";
                    if (epochKnown && epoch != 0UL)
                    {
                        AsyncWorkToken token = new AsyncWorkToken(
                            suspendOperationKnown ? suspendOperation : operation, provider, generation, new ActivationEpoch(epoch), 1U);
                        CallbackGateDecision decision = lane.Lifecycle.EvaluateCompletion(token);
                        lateCompletion = decision.ToString();
                        lateDiscarded = decision == CallbackGateDecision.DiscardRetiredRoute
                            || decision == CallbackGateDecision.DiscardStaleActivation;
                    }

                    bool routesClosed = DeclaredRoutesAreRetired(provider);
                    bool leasesRetired = retiredLeases >= 1
                        && resources != null
                        && resources.DisposeCount > disposedBefore;

                    bool pass = joined
                        && state == InstallationState.Disposed.ToString()
                        && ingressClosed >= 1
                        && routesRetired >= 1
                        && routesClosed
                        && retractedRows == rowsBefore
                        && leasesRetired
                        && quarantined == 0
                        && lateDiscarded
                        && published != null
                        && published.Outcome == Outcome.Published;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "state=" + state
                        + "; closedInstallations=" + Text(ingressClosed)
                        + "; retiredRoutes=" + Text(routesRetired)
                        + "; declaredRoutesRetired=" + routesClosed
                        + "; rows=" + Text(rowsBefore) + "/retracted=" + Text(retractedRows)
                        + "; retiredLeases=" + Text(retiredLeases)
                        + "; disposedByFactory=" + Text(resources != null ? resources.DisposeCount : -1)
                        + "; quarantined=" + Text(quarantined)
                        + "; lateCompletion=" + lateCompletion
                        + "; publication=" + (published != null ? published.Outcome.ToString() : "<none>")));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactUnloadState, state);
                    facts.Set(NarrativeLifecycleKeys.FactUnloadIngressClosed, ingressClosed);
                    facts.Set(NarrativeLifecycleKeys.FactUnloadRetractedRows, retractedRows);
                    facts.Set(NarrativeLifecycleKeys.FactUnloadRetiredLeases, retiredLeases);
                    facts.Set(NarrativeLifecycleKeys.FactUnloadQuarantined, quarantined);
                    facts.Set(NarrativeLifecycleKeys.FactUnloadLateCompletion, lateCompletion);
                }
            }

            // ------------------------------------------------------------------ 8. a blocked job prevents buffer release

            private void BlockedJobPreventsBufferRelease()
            {
                const string name = NarrativeLifecycleKeys.StepJobFence;
                long outstandingJobs = -1;
                string blockedCode = "<none>";
                bool retained = false;
                bool disposeSettled = true;
                long quarantineBefore = -1;
                bool releasedOnce = false;
                long quarantineAfter = -1;
                try
                {
                    if (controller == null || lane == null || host == null || pipeline == null || resources == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its pipeline is missing"));
                        return;
                    }

                    // A fresh installation, so the earlier steps stay valid: mounted with a real staged resource.
                    PluginInstanceId instance = FencedInstall;
                    OperationId mountOperation = NextLaneOperation();
                    EditAdmission admission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Mount(FencedManifest(), instance, NarrativeKeys.RootScope),
                        mountOperation,
                        lane.Committed.Revision);
                    ResourceKey leaseKey = new ResourceKey(resourceSequence.Next());
                    bool staged = lane.StageResource(
                        mountOperation, instance, leaseKey, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode code);
                    IReadOnlyList<StagedLease> leases = lane.StagedLeases(mountOperation);
                    lane.Drain();
                    DerivedAssemblyReport derived = pipeline.PublishDerived(mountOperation);

                    // The fenced mount derives no target change, so the world's half of its publication is the
                    // unchanged assembly (P-006), not a spawn carrier.
                    CompleteWorldHalf(mountOperation, derived);

                    if (!staged || leases.Count != 1 || !admission.Staged)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false,
                            "the fenced installation's lease was not staged: " + DiagnosticCodeText.Of(code)));
                        return;
                    }

                    Id128 leaseId = leases[0].LeaseId;
                    Id128 jobId = jobSequence.Next();

                    // A tracked job of the installation that may still reach the lease, left outstanding: teardown
                    // must retain the buffer rather than release it (P-047, P-048).
                    controller.JobFence.Track(
                        jobId,
                        instance,
                        NarrativeKeys.InputStage,
                        NarrativeKeys.InputSystem,
                        host.CurrentEpoch,
                        host.CurrentStep,
                        new List<Id128> { leaseId },
                        default(JobHandle));

                    outstandingJobs = controller.JobFence.OutstandingCount;

                    // The coordinator's own unload, not the controller's: `LifecycleController.Unload` completes the
                    // installation's blocking jobs first by design (P-047 "jobs finish before resources are
                    // released"), so a *blocked* teardown is only reachable by asking the coordinator for the P-048
                    // order while the fence is still held.
                    int retiredBefore = lane.Resources.RetiredCount;
                    TeardownReport teardown = controller.Lifecycle.Unload(instance, NextWorldOperation());

                    blockedCode = DiagnosticCodeText.Of(teardown.Code);
                    disposeSettled = teardown.DisposeSettled;
                    retained = lane.Resources.TryGetRecord(leaseId, out WorldResourceRecord record)
                        && record.IsRetained
                        && record.State == ResourceRetirementState.Quarantined;
                    quarantineBefore = lane.Lifecycle.Quarantine.EntriesFor(instance).Count;

                    string retiringState = "<none>";
                    if (lane.Lifecycle.Activations.TryGetCurrent(instance, out ActivationAttempt? attempt) && attempt != null)
                    {
                        retiringState = attempt.State.ToString();
                    }

                    // The job completes, then the quarantine is released: the lease is retired exactly once (P-048).
                    bool completed = controller.JobFence.Complete(jobId);
                    CleanupReport cleanup = controller.ReleaseQuarantine(instance);
                    releasedOnce = completed
                        && cleanup.Retired.Count == 1
                        && lane.Resources.RetiredCount - retiredBefore == 1
                        && lane.Resources.RetainedCountFor(instance) == 0;
                    quarantineAfter = lane.Lifecycle.Quarantine.EntriesFor(instance).Count;

                    bool pass = teardown.BlockedByJobFence
                        && teardown.Code == DiagnosticCode.TeardownBlocked
                        && !teardown.DisposeSettled
                        && teardown.OutstandingJobs == 1
                        && retained
                        && quarantineBefore >= 1
                        && retiringState == InstallationState.Retiring.ToString()
                        && releasedOnce
                        && quarantineAfter == 0
                        && resources.DisposedOrder.Contains(leaseId);

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "outstandingJobs=" + Text(outstandingJobs)
                        + "; code=" + blockedCode
                        + "; disposeSettled=" + disposeSettled
                        + "; leaseRetained=" + retained
                        + "; quarantine=" + Text(quarantineBefore) + "->" + Text(quarantineAfter)
                        + "; state=" + retiringState
                        + "; jobCompleted=" + completed
                        + "; retiredLeases=" + Text(cleanup.Retired.Count)
                        + "; factoryDisposals=" + Text(resources.DisposeCount)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactFenceOutstandingJobs, outstandingJobs);
                    facts.Set(NarrativeLifecycleKeys.FactFenceBlockedCode, blockedCode);
                    facts.Set(NarrativeLifecycleKeys.FactFenceRetainedWhileOutstanding, retained);
                    facts.Set(NarrativeLifecycleKeys.FactFenceDisposeSettledWhileOutstanding, disposeSettled);
                    facts.Set(NarrativeLifecycleKeys.FactFenceQuarantineBeforeRelease, quarantineBefore);
                    facts.Set(NarrativeLifecycleKeys.FactFenceReleasedAfterCompletion, releasedOnce);
                    facts.Set(NarrativeLifecycleKeys.FactFenceQuarantineAfterRelease, quarantineAfter);
                }
            }

            // ------------------------------------------------------------------ 9. invalid transitions rejected

            private void InvalidTransitionsRejected()
            {
                const string name = NarrativeLifecycleKeys.StepInvalidTransitions;
                long rejected = 0;
                bool stateUnchanged = false;
                string suspendTwice = "<none>";
                string resumeActive = "<none>";
                string unmountDisposed = "<none>";
                string reconfigureDisposed = "<none>";
                string remountLive = "<none>";
                string teardownPath = "<none>";
                try
                {
                    if (controller == null || lane == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    // A genuinely suspended installation is the precondition of the first case: suspend the consumer
                    // (a real, accepted publication), then ask for the same edge again.
                    LifecycleRequestReport accepted = SubmitAndPublish(
                        NarrativeLifecyclePayloads.Suspend(ServiceConsumerInstall));
                    bool suspended = accepted.Lifecycle != null
                        && InstallStateOf(ServiceConsumerInstall) == InstallationState.Suspended;

                    ulong revisionBefore = lane.Committed.Revision.Value;
                    string consumerBefore = StateOf(ServiceConsumerInstall);
                    string providerBefore = StateOf(ServiceProviderReplacementInstall);
                    string chapterBefore = StateOf(NarrativeKeys.ChapterOneInstall);

                    // 1. suspend when already suspended.
                    EditAdmission suspendTwiceAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Suspend(ServiceConsumerInstall),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    suspendTwice = DiagnosticCodeText.Of(suspendTwiceAdmission.Code);
                    if (Refused(suspendTwiceAdmission))
                    {
                        rejected++;
                    }

                    // 2. resume when active.
                    EditAdmission resumeActiveAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Resume(ServiceProviderReplacementInstall),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    resumeActive = DiagnosticCodeText.Of(resumeActiveAdmission.Code);
                    if (Refused(resumeActiveAdmission))
                    {
                        rejected++;
                    }

                    // 3. unmount when disposed.
                    EditAdmission unmountDisposedAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Unmount(NarrativeKeys.ChapterOneInstall),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    unmountDisposed = DiagnosticCodeText.Of(unmountDisposedAdmission.Code);
                    if (Refused(unmountDisposedAdmission))
                    {
                        rejected++;
                    }

                    // 4. reconfigure when disposed.
                    CompositionEditPayload chapterReconfigure = ChapterReconfigurePayload();
                    EditAdmission reconfigureDisposedAdmission = lane.SubmitEdit(
                        chapterReconfigure, NextLaneOperation(), lane.Committed.Revision);
                    reconfigureDisposed = DiagnosticCodeText.Of(reconfigureDisposedAdmission.Code);
                    if (Refused(reconfigureDisposedAdmission))
                    {
                        rejected++;
                    }

                    // 5. remount over a live identity.
                    EditAdmission remountAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Mount(
                            ProviderReplacementManifest(), ServiceProviderReplacementInstall, NarrativeKeys.RootScope),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    remountLive = DiagnosticCodeText.Of(remountAdmission.Code);
                    if (Refused(remountAdmission))
                    {
                        rejected++;
                    }

                    // 6. a state the P-046 table gives no teardown path: a Disposed installation has no outgoing
                    //    edge at all, and a Preparing activation has no teardown path.
                    LifecycleTransition teardownRefusal = InstallationStateMachine.Request(
                        InstallationState.Disposed, InstallationState.Retiring);
                    bool noPath = !InstallationStateMachine.TryTeardownPath(InstallationState.Preparing, out _);
                    teardownPath = DiagnosticCodeText.Of(teardownRefusal.Code);
                    bool teardownRefused = !teardownRefusal.Allowed
                        && teardownRefusal.Code == DiagnosticCode.OwnershipConflict
                        && noPath;
                    if (teardownRefused)
                    {
                        rejected++;
                    }

                    stateUnchanged = suspended
                        && lane.Committed.Revision.Value == revisionBefore
                        && StateOf(ServiceConsumerInstall) == consumerBefore
                        && StateOf(ServiceProviderReplacementInstall) == providerBefore
                        && StateOf(NarrativeKeys.ChapterOneInstall) == chapterBefore;

                    bool pass = rejected == 6 && stateUnchanged;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "rejected=" + Text(rejected)
                        + "; stateUnchanged=" + stateUnchanged
                        + "; suspendTwice=" + suspendTwice
                        + "; resumeActive=" + resumeActive
                        + "; unmountDisposed=" + unmountDisposed
                        + "; reconfigureDisposed=" + reconfigureDisposed
                        + "; remountLive=" + remountLive
                        + "; teardownPath=" + teardownPath
                        + "; revision=" + Text(revisionBefore)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactInvalidRejectedCount, rejected);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidStateUnchanged, stateUnchanged);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidSuspendTwiceCode, suspendTwice);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidResumeActiveCode, resumeActive);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidUnmountDisposedCode, unmountDisposed);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidReconfigureDisposedCode, reconfigureDisposed);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidRemountLiveIdentityCode, remountLive);
                    facts.Set(NarrativeLifecycleKeys.FactInvalidTeardownPathRefusedCode, teardownPath);
                }
            }

            // ------------------------------------------------------------------ 10. repeated operations obey the ledger

            private void RepeatedOperationsObeyLedger()
            {
                const string name = NarrativeLifecycleKeys.StepRepeatedOperations;
                string suspendRefused = "<none>";
                string retransmission = "<none>";
                bool reconfigureSame = false;
                string unmountRefused = "<none>";
                long ledgerRows = -1;
                try
                {
                    if (controller == null || lane == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its controller is missing"));
                        return;
                    }

                    // A repeat of an already-settled suspend under a NEW operation id: refused, state untouched.
                    EditAdmission repeatSuspendAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Suspend(ServiceConsumerInstall),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    suspendRefused = DiagnosticCodeText.Of(repeatSuspendAdmission.Code);
                    bool suspendRefusedValue = Refused(repeatSuspendAdmission)
                        && InstallStateOf(ServiceConsumerInstall) == InstallationState.Suspended;

                    // The *same* operation id and payload: the original row and its recorded outcome come back.
                    bool retransmitted = false;
                    bool originalOutcome = false;
                    if (suspendOperationKnown)
                    {
                        // The identical payload of the settled chapter-one suspend: same subject, same instance,
                        // same canonical encoding, so the lane recognises the retransmission by identity and hash.
                        EditAdmission again = lane.SubmitEdit(
                            NarrativeLifecyclePayloads.Suspend(NarrativeKeys.ChapterOneInstall),
                            suspendOperation,
                            lane.Committed.Revision);
                        retransmission = again.Kind.ToString();
                        OperationReadResult read = lane.Read(again.Handle);
                        originalOutcome = read.Outcome == OperationReadOutcome.Found
                            && read.Entry != null
                            && read.Entry.Outcome == Outcome.Published;
                        retransmitted = again.Kind == AdmissionKind.Retransmission && originalOutcome;
                    }

                    // A second unmount of a disposed installation: refused.
                    EditAdmission repeatUnmountAdmission = lane.SubmitEdit(
                        NarrativeLifecyclePayloads.Unmount(NarrativeKeys.ChapterOneInstall),
                        NextLaneOperation(),
                        lane.Committed.Revision);
                    unmountRefused = DiagnosticCodeText.Of(repeatUnmountAdmission.Code);
                    bool unmountRefusedValue = Refused(repeatUnmountAdmission);

                    // A repeated reconfigure returns the original refusal, so a retry can never publish twice.
                    CompositionEditPayload payload = ChapterReconfigurePayload();
                    OperationId reconfigureOperation = NextLaneOperation();
                    EditAdmission first = lane.SubmitEdit(payload, reconfigureOperation, lane.Committed.Revision);
                    EditAdmission second = lane.SubmitEdit(payload, reconfigureOperation, lane.Committed.Revision);
                    OperationReadResult reconfigureRead = lane.Read(second.Handle);
                    reconfigureSame = second.Kind == AdmissionKind.Retransmission
                        && reconfigureRead.Outcome == OperationReadOutcome.Found
                        && reconfigureRead.Entry != null
                        && reconfigureRead.Entry.Code == first.Code
                        && reconfigureRead.Entry.Outcome == Outcome.Rejected;

                    ledgerRows = lane.OperationLedger.RowCount;

                    bool pass = suspendRefusedValue
                        && retransmitted
                        && reconfigureSame
                        && unmountRefusedValue
                        && ledgerRows == expectedLaneRows;

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "repeatSuspend=" + suspendRefused
                        + "; retransmission=" + retransmission + "/originalOutcome=" + originalOutcome
                        + "; reconfigureSameOutcome=" + reconfigureSame
                        + "; repeatUnmount=" + unmountRefused
                        + "; ledgerRows=" + Text(ledgerRows) + "/expected=" + Text(expectedLaneRows)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactRepeatSuspendRefusedCode, suspendRefused);
                    facts.Set(NarrativeLifecycleKeys.FactRepeatRetransmissionKind, retransmission);
                    facts.Set(NarrativeLifecycleKeys.FactRepeatReconfigureSameOutcome, reconfigureSame);
                    facts.Set(NarrativeLifecycleKeys.FactRepeatUnmountRefusedCode, unmountRefused);
                    facts.Set(NarrativeLifecycleKeys.FactLedgerRowCount, ledgerRows);
                }
            }

            // ------------------------------------------------------------------ 11. teardown settles and disposes

            private void TeardownSettlesAndDisposes()
            {
                const string name = NarrativeLifecycleKeys.StepTeardown;
                long idleSteps = -1;
                try
                {
                    if (controller == null || lane == null || host == null || time == null)
                    {
                        steps.Add(new NarrativeLifecycleStep(name, false, "the world or its time driver is missing"));
                        return;
                    }

                    // P-036: a command-driven world executes no step of its own, so ten idle seconds commit nothing.
                    ulong committed = 0UL;
                    for (int i = 0; i < 10; i++)
                    {
                        TimeFrameReport frame = time.PumpFrame(NarrativeScenarioTrace.IdleTicksPerFrame);
                        committed += frame.StepsCommitted;
                    }

                    idleSteps = (long)committed;

                    // Every remaining installation: the suspended consumer, the compatible provider, the fenced one.
                    UnmountAndPublish(ServiceConsumerInstall);
                    UnmountAndPublish(ServiceProviderReplacementInstall);
                    UnmountAndPublish(FencedInstall);

                    time.Clear(out int discardedCommands, out int pendingWakes);
                    NarrativeModule.DetachAll();

                    OperationResult stop = host.Stop(NextWorldOperation(), "gc-014 narrative lifecycle teardown");
                    host.Dispose();
                    worldStopped = true;

                    TeardownFacts();

                    bool remaining = StateOf(ServiceConsumerInstall) == InstallationState.Disposed.ToString()
                        && StateOf(ServiceProviderReplacementInstall) == InstallationState.Disposed.ToString()
                        && StateOf(FencedInstall) == InstallationState.Disposed.ToString();
                    bool noJobs = controller.JobFence.OutstandingCount == 0;

                    bool pass = idleSteps == 0
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && remaining
                        && noJobs
                        && facts.ValueOf(NarrativeLifecycleKeys.FactOutstandingJobsAfterTeardown) == Text((long)0)
                        && facts.ValueOf(NarrativeLifecycleKeys.FactRetainedResourcesAfterTeardown) == Text((long)0)
                        && facts.ValueOf(NarrativeLifecycleKeys.FactRegistryAfterTeardown) == Text(registryBeforeCreate);

                    steps.Add(new NarrativeLifecycleStep(name, pass,
                        "idleSteps=" + Text(idleSteps)
                        + "; stopped=" + stop.Outcome + "(" + DiagnosticCodeText.Of(stop.Code) + ")"
                        + "; remainingDisposed=" + remaining
                        + "; discardedCommands=" + Text(discardedCommands)
                        + "; pendingWakes=" + Text(pendingWakes)
                        + "; outstandingJobs=" + Text(controller.JobFence.OutstandingCount)
                        + "; registryAfter=" + Text(UnityWorldRegistry.Count) + "/before=" + Text(registryBeforeCreate)));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
                finally
                {
                    facts.Set(NarrativeLifecycleKeys.FactIdleSteps, idleSteps);
                    TearDownSafely();
                    TeardownFacts();
                }
            }

            // ------------------------------------------------------------------ 12. the run's own facts

            private void FactsStep()
            {
                const string name = NarrativeLifecycleKeys.StepFacts;
                try
                {
                    EnsureEveryFactIsSet();
                    steps.Add(new NarrativeLifecycleStep(name, true, facts.Describe()));
                }
                catch (Exception exception)
                {
                    steps.Add(new NarrativeLifecycleStep(name, false, DescribeException(exception)));
                }
            }

            // ------------------------------------------------------------------ world construction

            /// <summary>
            /// Builds the narrative world exactly as the slice's own scenario builds it: the same generated-style
            /// catalog and declarations, the same ownership/schedule compile, the same recipes, seeder, lane seed and
            /// derived pipeline. The only additions are the four service/lease manifests this scenario installs.
            /// </summary>
            private bool BuildWorld()
            {
                CatalogBuildResult build = NarrativeScenarioCatalog.Build();
                if (build.Catalog == null)
                {
                    buildFailure = "the generated-style narrative catalog was rejected: " + build.Describe();
                    return false;
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

                if (!CompileOwnershipAndSchedule(declarations))
                {
                    return false;
                }

                if (descriptorReport == null || descriptorReport.Descriptor == null || descriptorReport.Adaptation == null)
                {
                    buildFailure = "the ownership/schedule descriptor was not built: "
                        + (descriptorReport != null ? descriptorReport.Describe() : "<none>");
                    return false;
                }

                CompiledSchedule schedule = descriptorReport.Compilation!.Schedule!;
                WorldId world = NextSession();

                WorldCreateRequest request = NarrativeRegistration.CommandDrivenRequest(
                    world, NextOperation(world), ContentHash.Empty);
                UnityWorldRegistration registration = NarrativeRegistration.Create(
                    descriptorReport.Adaptation, NarrativeRegistration.Systems());

                bool created = UnityWorldRegistry.TryCreate(
                    request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                host = createdHost;
                if (!created || host == null)
                {
                    buildFailure = "world creation failed: " + result.Code + ": " + result.Detail;
                    return false;
                }

                module = NarrativeModule.Attach(host, schedule);
                registry = new TargetRegistry(world, 16);
                publisher = new AssemblyPublisher(
                    host,
                    registry,
                    NarrativeRecipes.Catalog(applier),
                    new MigrationRegistry(new List<ISlotMigration> { nodeMigration, statusMigration }),
                    descriptorReport.Descriptor);

                targets = new LiveTargetIndex(publisher.Recipes);
                seeder = new LiveTargetSeeder(host, registry, targets);

                // The lane opens with the world's declared scope tree and publishes nothing for it, and its first
                // publication is a mount: a scope is not an assembly (P-006, P-010).
                var catalogSource = new CatalogManifestSource(build.Catalog, declarations);
                var manifestSource = new NarrativeLifecycleManifestSource(catalogSource);
                manifestSource.Add(ConsumerManifest());
                manifestSource.Add(ProviderManifest());
                manifestSource.Add(ProviderReplacementManifest());
                manifestSource.Add(FencedManifest());
                manifests = manifestSource;

                resources = new NarrativeLifecycleResourceFactory(resourceSequence);

                lane = CompositionHost.CreateDefault(
                    world,
                    NarrativeKeys.RootScope,
                    manifestSource,
                    resources,
                    CompositionLaneSeed.InitialAssembly.WithScopes(NarrativeScopes.DeclaredChildren()));
                bridge = new WorldCompositionBridge(host, lane, publisher);

                if (!SeedTargets())
                {
                    return false;
                }

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

                // The controller owns the one lifecycle binding of this world, and a lane that has already published
                // cannot swap its binding, so it is built before the first mount (P-030).
                controller = new LifecycleController(host, lane, publisher, pipeline);

                bool factSlots = SeedFactSlots()
                    && SeedConversationAtVersionOne(NarrativeKeys.Mara);

                facts.Set(NarrativeLifecycleKeys.FactWorldLifecycle, host.Lifecycle.ToString());

                bool pass = clockRegistered
                    && factSlots
                    && lane.Committed.Scopes.Count == NarrativeScopes.DeclaredScopeCount
                    && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                    && host.Lifecycle == WorldLifecycleState.Running;
                if (!pass)
                {
                    buildFailure = "the world did not reach its declared opening state: clock="
                        + clockRegistered + "; factSlots=" + factSlots
                        + "; scopes=" + Text(lane.Committed.Scopes.Count)
                        + "; lifecycle=" + host.Lifecycle;
                }

                return pass;
            }

            private bool CompileOwnershipAndSchedule(IReadOnlyList<CatalogPluginDeclaration> declarations)
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

                    var chapterManifests = new List<PluginManifest>();
                    for (int i = 0; i < declarations.Count; i++)
                    {
                        chapterManifests.Add(declarations[i].Manifest);
                    }

                    // The execute surface is the chapters': the service/lease manifests of this scenario declare no
                    // stage, no buffer and no state slot, so they must not enter the compiled schedule.
                    descriptorReport = OwnershipSchedulePipeline.Build(
                        chapterManifests,
                        kinds,
                        new NarrativeSlotMigrations());
                    return true;
                }
                catch (Exception exception)
                {
                    buildFailure = DescribeException(exception);
                    return false;
                }
            }

            private bool SeedTargets()
            {
                if (seeder == null || module == null)
                {
                    return false;
                }

                return SeedTarget(NarrativeKeys.Mara, NarrativeKeys.VillageScope, NarrativeKeys.VillagerRecipe)
                    && SeedTarget(NarrativeKeys.GateEast, NarrativeKeys.VillageScope, NarrativeKeys.QuestGateRecipe)
                    && SeedTarget(NarrativeKeys.CrowdProp, NarrativeKeys.VillageScope, NarrativeKeys.DecorativeCrowdRecipe)
                    && SeedTarget(NarrativeKeys.EncounterOak, NarrativeKeys.GroveScope, NarrativeKeys.QuestEncounterRecipe)
                    && SeedTarget(NarrativeKeys.Display, NarrativeKeys.MuseumScope, NarrativeKeys.VillagerRecipe)
                    && SeedTarget(NarrativeKeys.Sailor, NarrativeKeys.HarborScope, NarrativeKeys.VillagerRecipe)
                    && SeedTarget(NarrativeKeys.QuestLedger, NarrativeKeys.RootScope, NarrativeKeys.QuestLedgerRecipe);
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
            /// Seeds the ledger's durable fact slots and the trail's counters at their declared versions, so the state
            /// a suspension and a resume keep is state the run really owns (P-032, P-046).
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

            // ------------------------------------------------------------------ driving the real modules

            /// <summary>
            /// Mounts the slice's chapter provider at its scope and declares the owners whose ingress closes with it.
            /// The chapter answers the world's one admitted route, whose owner is the ingress routing identity, so
            /// that owner closes with the chapter's ingress as well as the owners its manifest declares (P-047).
            /// </summary>
            private void MountChapterProvider()
            {
                if (controller == null || lane == null)
                {
                    throw new InvalidOperationException("The lifecycle controller is not attached.");
                }

                LifecycleRequestReport report = SubmitAndPublish(
                    NarrativeLifecyclePayloads.Mount(
                        ChapterOneManifest(), NarrativeKeys.ChapterOneInstall, NarrativeKeys.ChapterOneScope));
                if (report.Admission == null || !report.Admission.Staged)
                {
                    throw new InvalidOperationException(
                        "The chapter provider's mount was refused: " + report.Detail);
                }

                if (lane.Committed.TryGetInstall(NarrativeKeys.ChapterOneInstall, out InstallEntry? entry)
                    && entry != null)
                {
                    List<OwnerId> owners = new List<OwnerId>(
                        InstallationIngressOwners.FromManifest(entry.Instance, entry).Owners);
                    if (!ContainsOwner(owners, NarrativeKeys.IngressOwner))
                    {
                        owners.Add(NarrativeKeys.IngressOwner);
                    }

                    controller.Binding.DeclareIngressOwners(
                        new InstallationIngressOwners(entry.Instance, owners));
                }
            }

            private void UnmountAndPublish(PluginInstanceId instance)
            {
                if (controller == null)
                {
                    throw new InvalidOperationException("The lifecycle controller is not attached.");
                }

                if (InstallStateOf(instance) == InstallationState.Disposed)
                {
                    return;
                }

                SubmitAndPublish(NarrativeLifecyclePayloads.Unmount(instance));
            }

            /// <summary>
            /// Submits one lifecycle payload through the controller, which performs the lane submission, the drain and
            /// the derived publication in the fixed order of P-046 (P-033, P-051), then completes the world's half of
            /// that same publication (P-006). The chapter's ingress declaration is restored after the controller's
            /// own refresh rebuilt every declaration from the manifests, so a later close of the chapter installation
            /// still finds the ChoiceRoute owner (P-047).
            /// </summary>
            private LifecycleRequestReport SubmitAndPublish(CompositionEditPayload payload) =>
                SubmitAndPublish(payload, NextLaneOperation());

            private LifecycleRequestReport SubmitAndPublish(CompositionEditPayload payload, OperationId operation)
            {
                if (controller == null)
                {
                    throw new InvalidOperationException("The lifecycle controller is not attached.");
                }

                DeclareChapterIngress();
                LifecycleRequestReport report = controller.Submit(payload, operation);
                DeclareChapterIngress();
                if (report.Derived != null)
                {
                    CompleteWorldHalf(operation, report.Derived);
                }

                return report;
            }

            /// <summary>
            /// Submits a lifecycle edit of the chapter installation over the same three seams the controller drives,
            /// with the chapter's ingress declaration live at the publication boundary. The controller's own
            /// pre-submission refresh rebuilds every declaration from the manifests, and the chapter manifest's
            private bool SubmitChapterEdit(
                CompositionEditPayload payload,
                OperationId operation,
                out EditAdmission admission,
                out IReadOnlyList<PublishedOperation> published,
                out DerivedAssemblyReport derived)
            {
                if (lane == null || pipeline == null)
                {
                    throw new InvalidOperationException("The composition lane is not attached.");
                }

                DeclareChapterIngress();
                admission = lane.SubmitEdit(payload, operation, lane.Committed.Revision);
                published = lane.Drain();
                derived = pipeline.PublishDerived(operation);
                return CompleteWorldHalf(operation, derived);
            }

            /// <summary>
            /// Completes the world's half of one lifecycle publication. A lifecycle edit whose derivation changed no
            /// effective binding is answered `NoTargetChange`, and P-006 still counts it as one publication of the one
            /// series, so the world must publish its assembly for that pair — exactly as the slice's own scenario
            /// completes every publication it drains. Two shapes exist:
            ///
            ///   * a derivation that never adopted (an empty delta) leaves the pair free, so the world publishes its
            ///     unchanged assembly for it;
            ///   * a derivation that adopted and then collapsed to a planner no-op leaves the pair adopted and
            ///     pending, and the one legal consumer of a pending pair is a spawn: the carrier is seeded from the
            ///     ledger recipe, which no chapter rule selects, so it adds zero binding rows and no installation's
            ///     attributed rows change (P-006, P-024).
            /// </summary>
            private bool CompleteWorldHalf(OperationId operation, DerivedAssemblyReport derived)
            {
                if (lane == null || publisher == null || host == null || pipeline == null)
                {
                    throw new InvalidOperationException("The world's assembly publisher is not attached.");
                }

                if (derived.Outcome != DerivedAssemblyOutcome.NoTargetChange
                    || AssemblyPublisher.MatchesPublishedAssembly(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        publisher.PublishedRevision,
                        host.CurrentEpoch))
                {
                    // The derivation published its own assembly, or this publication's numbers are already joined.
                    return true;
                }

                if (!publisher.HasAdoptedPublication)
                {
                    publisher.PublishUnchangedAssembly(operation, lane.Committed.Revision, lane.Committed.Epoch);
                }
                else
                {
                    // The pair is adopted and pending: only a spawn may consume it, and a ledger-recipe carrier
                    // carries no derived row, so the completion changes no installation's contribution.
                    carrierOrdinal++;
                    TargetId carrier = TargetId.FromRaw(CarrierSalt.High, CarrierSalt.Low + (ulong)carrierOrdinal);
                    DerivedAssemblyReport consumed = pipeline.PublishSpawn(
                        NextWorldOperation(), carrier, NarrativeKeys.QuestLedgerRecipe, NarrativeKeys.RootScope);
                    if (!consumed.Succeeded)
                    {
                        return false;
                    }
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            /// <summary>
            /// Declares the chapter installation's ingress owners: the manifest's own state-slot owners plus the
            /// world's ingress routing owner (the ChoiceRoute owner, which no state slot names). The controller's
            /// refresh rebuilds declarations from manifests only, so this is redeclared around every submission —
            /// idempotently, as a redeclaration of the same entry yields the same owners (P-047).
            /// </summary>
            private void DeclareChapterIngress()
            {
                if (controller == null || lane == null)
                {
                    return;
                }

                if (lane.Committed.TryGetInstall(NarrativeKeys.ChapterOneInstall, out InstallEntry? entry)
                    && entry != null)
                {
                    var owners = new List<OwnerId>(
                        InstallationIngressOwners.FromManifest(entry.Instance, entry).Owners);
                    if (!ContainsOwner(owners, NarrativeKeys.IngressOwner))
                    {
                        owners.Add(NarrativeKeys.IngressOwner);
                    }

                    controller.Binding.DeclareIngressOwners(
                        new InstallationIngressOwners(entry.Instance, owners));
                }
            }


            /// <summary>
            /// Unloads every remaining installation and disposes the world, so the registry returns to its baseline
            /// even when a step failed before its own teardown ran.
            /// </summary>
            private void TearDownSafely()
            {
                try
                {
                    if (worldStopped || host == null)
                    {
                        return;
                    }

                    if (time != null)
                    {
                        time.Clear(out int discardedCommands, out int pendingWakes);
                        _ = discardedCommands;
                        _ = pendingWakes;
                    }

                    NarrativeModule.DetachAll();
                    host.Stop(NextWorldOperation(), "gc-014 narrative lifecycle teardown (safety path)");
                    host.Dispose();
                    worldStopped = true;
                }
                catch (Exception)
                {
                    // The safety path never replaces a step's own failure with its own.
                }
            }

            /// <summary>Records the teardown facts from live state, whether or not the world has already stopped.</summary>
            private void TeardownFacts()
            {
                long outstanding = -1;
                long retained = -1;
                try
                {
                    if (host != null)
                    {
                        outstanding = host.Ledger.OutstandingJobCount;
                        retained = host.Ledger.RetainedResourceCount;
                    }
                }
                catch (Exception)
                {
                    outstanding = -1;
                    retained = -1;
                }

                facts.Set(NarrativeLifecycleKeys.FactOutstandingJobsAfterTeardown, outstanding);
                facts.Set(NarrativeLifecycleKeys.FactRetainedResourcesAfterTeardown, retained);
                facts.Set(NarrativeLifecycleKeys.FactRegistryAfterTeardown, UnityWorldRegistry.Count);
            }

            // ------------------------------------------------------------------ manifests of the service pair

            /// <summary>
            /// The chapter provider's manifest, as the world's declarations build it. It is the slice's own
            /// declaration, so the mounted installation is the one the narrative scenario mounts.
            /// </summary>
            private static PluginManifest ChapterOneManifest() =>
                NarrativeDeclarations.ChapterProvider(
                    NarrativeKeys.PluginTypeId(1UL),
                    NarrativeScenarioCatalog.PluginFactoryKey,
                    NarrativeScenarioCatalog.RecordSchema);

            /// <summary>
            /// The consumer of the required service: one capability of its own over the reusable villager recipe, so
            /// it really contributes rows whose retraction a provider loss can be observed through (P-017), plus a
            /// required `ServiceDependency` on the contract the provider exports (P-011, P-012).
            /// </summary>
            private static PluginManifest ConsumerManifest() =>
                LifecycleManifest(
                    NarrativeKeys.PluginTypeId(11UL),
                    NarrativeKeys.Key("narrative.lifecycle.consumer-factory"),
                    "narrative.lifecycle.consumer",
                    "narrative.lifecycle.consumer-binding",
                    "narrative.lifecycle.consumer-binding-schema",
                    "narrative.lifecycle.consumer-rule",
                    null,
                    new List<ServiceDependency>
                    {
                        new ServiceDependency(
                            RequiredService,
                            new VersionRange(RequiredService.Version, RequiredService.Version),
                            true,
                            ServiceResolutionDomain.AncestorsAndSelf,
                            default(ProviderInstallationId),
                            default(FactoryKey)),
                    });

            /// <summary>The provider the required service's consumer binds to, and whose removal makes it wait.</summary>
            private static PluginManifest ProviderManifest() =>
                LifecycleManifest(
                    NarrativeKeys.PluginTypeId(12UL),
                    NarrativeKeys.Key("narrative.lifecycle.provider-factory"),
                    "narrative.lifecycle.provider",
                    "narrative.lifecycle.provider-binding",
                    "narrative.lifecycle.provider-binding-schema",
                    "narrative.lifecycle.provider-rule",
                    new List<ServiceExport>
                    {
                        new ServiceExport(
                            RequiredService,
                            RequiredServiceFactory,
                            ServiceVisibility.Private,
                            false,
                            ServiceBindingKind.Single),
                    },
                    null);

            /// <summary>
            /// A compatible provider: the same contract, its own installation identity, its own plugin type and its
            /// own rule identities, so its return resumes the consumer without colliding with the removed one.
            /// </summary>
            private static PluginManifest ProviderReplacementManifest() =>
                LifecycleManifest(
                    NarrativeKeys.PluginTypeId(13UL),
                    NarrativeKeys.Key("narrative.lifecycle.provider-alt-factory"),
                    "narrative.lifecycle.provider-alt",
                    "narrative.lifecycle.provider-alt-binding",
                    "narrative.lifecycle.provider-alt-binding-schema",
                    "narrative.lifecycle.provider-alt-rule",
                    new List<ServiceExport>
                    {
                        new ServiceExport(
                            RequiredService,
                            RequiredServiceFactory,
                            ServiceVisibility.Private,
                            false,
                            ServiceBindingKind.Single),
                    },
                    null);

            /// <summary>The installation whose staged lease a tracked job keeps alive through its teardown (P-047).</summary>
            private static PluginManifest FencedManifest() =>
                LifecycleManifest(
                    NarrativeKeys.PluginTypeId(14UL),
                    NarrativeKeys.Key("narrative.lifecycle.fenced-factory"),
                    "narrative.lifecycle.fenced",
                    "narrative.lifecycle.fenced-binding",
                    "narrative.lifecycle.fenced-binding-schema",
                    "narrative.lifecycle.fenced-rule",
                    null,
                    null);

            /// <summary>
            /// One scenario manifest: the same declaration shape the narrative chapter uses (one capability contract
            /// with one output slot and one derivation rule over the reusable villager recipe), plus the declared
            /// service surface of its role. It declares no stage, no buffer, no state slot and no resource, so it
            /// never enters the compiled schedule.
            /// </summary>
            private static PluginManifest LifecycleManifest(
                PluginTypeId pluginType,
                FactoryKey factoryKey,
                string chapterTag,
                string capabilityName,
                string schemaName,
                string ruleSuffix,
                IReadOnlyList<ServiceExport>? exports,
                IReadOnlyList<ServiceDependency>? dependencies)
            {
                var contracts = new List<CapabilityContract>
                {
                    NarrativeDeclarations.Contract(capabilityName, schemaName, 0, NarrativeDerivationPlan.ReplacePolicy),
                };
                var rules = new List<DerivationRule>
                {
                    NarrativeDeclarations.Rule(
                        chapterTag,
                        ruleSuffix,
                        capabilityName,
                        schemaName,
                        0,
                        NarrativeCompositionNames.VillagerRecipe,
                        string.Empty,
                        NarrativeDerivationPlan.ReplacePolicy,
                        1),
                };

                return new PluginManifest(
                    pluginType,
                    NarrativeDeclarations.PackageVersion,
                    ContentHash.Empty,
                    new SupportedProtocolRange(1, 0, 0),
                    null,
                    NarrativeScenarioCatalog.RecordSchema,
                    factoryKey,
                    exports,
                    dependencies,
                    contracts,
                    rules,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            private static IDerivationValueSource NarrativeCompositionValueSource() =>
                new GameCore.Derivation.Fixtures.FixtureValueSource()
                    .RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

            // ------------------------------------------------------------------ helpers

            /// <summary>
            /// A reconfigure of the chapter provider for the refusals of steps 9 and 10. The applier refuses a
            /// disposed or retiring installation before it composes anything, so the declared hash only has to be a
            /// real document hash (P-027, P-046).
            /// </summary>
            private static CompositionEditPayload ChapterReconfigurePayload()
            {
                ConfigComposeResult composed = ConfigComposer.Compose(new[]
                {
                    new ConfigLayer(
                        ConfigLayerOrigin.SchemaDefaults,
                        NarrativeScenarioCatalog.RecordSchema.Id.Value,
                        ConfigDocument.Empty),
                });

                return NarrativeLifecyclePayloads.Reconfigure(
                    ChapterOneManifest(),
                    NarrativeKeys.ChapterOneInstall,
                    new DefinitionRevision(2UL),
                    ConfigDocumentCodec.HashOf(composed.Value),
                    ConfigDocument.Empty);
            }

            /// <summary>
            /// The next operation identity of this run's issuer. One sequence is shared by every identity the run
            /// mints, so a lane operation and a world-side publication can never collide (P-050).
            /// </summary>
            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return NarrativeKeys.Operation(world, operationSequence);
            }

            /// <summary>An operation identity for a lane submission, which creates one ledger row (P-050).</summary>
            private OperationId NextLaneOperation()
            {
                if (host == null)
                {
                    throw new InvalidOperationException("No world exists to issue an operation identity.");
                }

                expectedLaneRows++;
                return NextOperation(host.World);
            }

            /// <summary>
            /// An operation identity for a world-side publication (a spawn carrier or an explicit unload). It consumes
            /// a sequence number so identities stay unique, but it is not a lane submission, so it creates no row.
            /// </summary>
            private OperationId NextWorldOperation()
            {
                if (host == null)
                {
                    throw new InvalidOperationException("No world exists to issue an operation identity.");
                }

                return NextOperation(host.World);
            }

            private WorldId NextSession() => new WorldId(sessionSequence.Next());

            /// <summary>The published lifecycle state of one installation, or `&lt;absent&gt;` when it is not installed.</summary>
            private string StateOf(PluginInstanceId instance)
            {
                if (lane == null || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return "<absent>";
                }

                return entry.State.ToString();
            }

            private InstallationState InstallStateOf(PluginInstanceId instance)
            {
                if (lane == null || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return InstallationState.Disposed;
                }

                return entry.State;
            }

            private int BindingCount(PluginInstanceId instance)
            {
                if (lane == null || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return -1;
                }

                return entry.Bindings.Count;
            }

            /// <summary>Rows of the published assembly this installation's rules provide (P-017).</summary>
            private long AttributedRows(PluginInstanceId instance) =>
                controller != null ? controller.Binding.CountAttributedRows(instance) : -1L;

            private bool LaneJoined(UnityWorldHost world, CompositionHost composition, AssemblyPublisher assembly) =>
                AssemblyPublisher.MatchesPublishedAssembly(
                    composition.Committed.Revision,
                    composition.Committed.Epoch,
                    assembly.PublishedRevision,
                    world.CurrentEpoch);

            private string LifecycleText() => host != null ? host.Lifecycle.ToString() : "<none>";

            /// <summary>The scenario's own family semantics: the gate decision the published state carries.</summary>
            private string GateDecisionText()
            {
                int decision = NarrativeGateRules.Closed;
                if (host != null && module != null && module.TryEntity(NarrativeKeys.Mara, out Entity entity))
                {
                    NarrativeState.TryRead(
                        host.EntityWorld.EntityManager,
                        entity,
                        NarrativeKeys.GateOwner,
                        NarrativeKeys.GateDecisionSlot,
                        out int value,
                        out uint _);
                    decision = value;
                }

                return decision.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// The durable bridge-permit fact version the ledger carries after a resume: the installation keeps its
            /// state across a suspension, so the fact's own version is still the seeded one (P-032, P-046).
            /// </summary>
            private string FactVersionText()
            {
                int version = RulesNarrativeFacts.InitialVersion;
                if (host != null
                    && module != null
                    && module.TryEntity(NarrativeKeys.QuestLedger, out Entity entity)
                    && NarrativeState.TryRead(
                        host.EntityWorld.EntityManager,
                        entity,
                        NarrativeKeys.QuestOwner,
                        NarrativeKeys.BridgePermitVersionSlot,
                        out int _,
                        out uint schemaVersion))
                {
                    version = (int)schemaVersion;
                }

                return version.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// True when every command route of the installation's declared owners is retired: closing the ingress of
            /// an installation closes exactly those owners' routes (P-047).
            /// </summary>
            private bool DeclaredRoutesAreRetired(PluginInstanceId instance)
            {
                if (host == null || controller == null || host.Messages == null)
                {
                    return false;
                }

                if (lane == null || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return false;
                }

                var owners = new List<OwnerId>(InstallationIngressOwners.FromManifest(instance, entry).Owners);
                if (!ContainsOwner(owners, NarrativeKeys.IngressOwner))
                {
                    owners.Add(NarrativeKeys.IngressOwner);
                }

                var routes = host.Messages.Routes;
                var all = routes.RoutesInCanonicalOrder();
                int relevant = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    var route = all[i];
                    if (!ContainsOwner(owners, route.Owner))
                    {
                        continue;
                    }

                    relevant++;
                    if (!routes.IsRetired(route.Route))
                    {
                        return false;
                    }
                }

                return relevant > 0;
            }

            private static ContributionRetraction? RetractionOf(LifecycleCommitReport? report, PluginInstanceId instance)
            {
                if (report == null)
                {
                    return null;
                }

                for (int i = 0; i < report.Retractions.Count; i++)
                {
                    if (report.Retractions[i].Instance.Equals(instance))
                    {
                        return report.Retractions[i];
                    }
                }

                return null;
            }

            private static TeardownReport? TeardownOf(LifecycleCommitReport? report, PluginInstanceId instance)
            {
                if (report == null)
                {
                    return null;
                }

                for (int i = 0; i < report.Teardowns.Count; i++)
                {
                    if (report.Teardowns[i].Instance.Equals(instance))
                    {
                        return report.Teardowns[i];
                    }
                }

                return null;
            }

            /// <summary>
            /// True when this publication ran a P-048 pass for an activation of the installation stamped with the
            /// given epoch. A replacement's displaced predecessor tears down under the *old* activation epoch
            /// (P-005, P-048), so that pass running in the same publication is what proves the old activation was
            /// still live while the candidate staged (P-046) — the same witness the card fixture reads.
            /// </summary>
            private static bool HasTeardownAtEpoch(
                LifecycleCommitReport? report,
                PluginInstanceId instance,
                ulong epoch)
            {
                if (report == null)
                {
                    return false;
                }

                for (int i = 0; i < report.Teardowns.Count; i++)
                {
                    if (report.Teardowns[i].Instance.Equals(instance)
                        && report.Teardowns[i].Stamp.ActivationEpoch.Value == epoch)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static PublishedOperation? PublishedOf(
                IReadOnlyList<PublishedOperation> published,
                OperationId operation)
            {
                for (int i = 0; i < published.Count; i++)
                {
                    if (published[i].Operation.Equals(operation))
                    {
                        return published[i];
                    }
                }

                return null;
            }

            private static LifecycleCommitReport? LifecycleOf(
                IReadOnlyList<PublishedOperation> published,
                OperationId operation)
            {
                for (int i = 0; i < published.Count; i++)
                {
                    if (published[i].Operation.Equals(operation))
                    {
                        return published[i].Lifecycle;
                    }
                }

                return null;
            }

            private static PublishedOperation? FindPublished(LifecycleRequestReport report, OperationId operation)
            {
                for (int i = 0; i < report.Published.Count; i++)
                {
                    if (report.Published[i].Operation.Equals(operation))
                    {
                        return report.Published[i];
                    }
                }

                return null;
            }

            private static bool NamesExactly(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId instance) =>
                instances.Count == 1 && instances[0].Equals(instance);

            /// <summary>
            /// A refusal is a value: a rejected admission with a code and nothing staged. Every P-046 edge the table
            /// does not have is refused exactly this way (P-046).
            /// </summary>
            private static bool Refused(EditAdmission admission) =>
                admission.Code != DiagnosticCode.None && !admission.Staged;

            private static bool ContainsOwner(IReadOnlyList<OwnerId> owners, OwnerId candidate)
            {
                for (int i = 0; i < owners.Count; i++)
                {
                    if (owners[i].Equals(candidate))
                    {
                        return true;
                    }
                }

                return false;
            }

            private string DescribeDerived(LifecycleRequestReport report) =>
                report.Derived != null
                    ? report.Derived.Outcome + "(" + DiagnosticCodeText.Of(report.Derived.Code) + ")"
                    : "<none>";

            private string DescribeFailures()
            {
                var text = new System.Text.StringBuilder();
                if (buildFailure.Length != 0)
                {
                    text.Append("; buildFailure=").Append(buildFailure);
                }

                if (seedFailure.Length != 0)
                {
                    text.Append("; seedFailure=").Append(seedFailure);
                }

                return text.ToString();
            }

            private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;

            /// <summary>
            /// Guarantees the run carries every key of the fixed fact set: a step that failed before it could observe
            /// a value leaves it empty rather than absent, so the key set never depends on how far the run got.
            /// </summary>
            private void EnsureEveryFactIsSet()
            {
                for (int i = 0; i < DeclaredFactKeys.Length; i++)
                {
                    if (!facts.Has(DeclaredFactKeys[i]))
                    {
                        facts.Set(DeclaredFactKeys[i], string.Empty);
                    }
                }
            }

            /// <summary>
            /// The world's manifests plus this scenario's own: a miss in the generated catalog is reported, never
            /// substituted, and the added manifests are the ones this scenario installs (P-009).
            /// </summary>
            private sealed class NarrativeLifecycleManifestSource : IPluginManifestSource
            {
                private readonly CatalogManifestSource catalog;
                private readonly Dictionary<Id128, PluginManifest> added = new Dictionary<Id128, PluginManifest>();

                public NarrativeLifecycleManifestSource(CatalogManifestSource catalog)
                {
                    this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                }

                public void Add(PluginManifest manifest)
                {
                    if (manifest == null)
                    {
                        throw new ArgumentNullException(nameof(manifest));
                    }

                    added[manifest.PluginTypeId.Value] = manifest;
                }

                public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
                {
                    if (added.TryGetValue(pluginType.Value, out PluginManifest? found))
                    {
                        manifest = found;
                        return true;
                    }

                    return catalog.TryGetManifest(pluginType, out manifest);
                }

                public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults) =>
                    catalog.TryGetConfigDefaults(schema, out defaults);
            }

            /// <summary>
            /// The scenario's managed-resource factory: counted preparations, deterministic lease identities and a
            /// recorded disposal order, so "each lease was prepared once and disposed at most once, in a real order"
            /// is data rather than an intention (P-007, P-048).
            /// </summary>
            private sealed class NarrativeLifecycleResourceFactory : IManagedResourceFactory
            {
                private readonly IdSequence ids;
                private readonly List<ManagedResourceLease> leases = new List<ManagedResourceLease>();

                public NarrativeLifecycleResourceFactory(IdSequence ids)
                {
                    this.ids = ids;
                }

                public int PrepareCount { get; private set; }

                public int DisposeCount { get; private set; }

                /// <summary>Lease identities in disposal order, so teardown ordering is observable.</summary>
                public List<Id128> DisposedOrder { get; } = new List<Id128>();

                public FactoryKey DisposerKey { get; } =
                    new FactoryKey(new Id128(0x67636C6966656379UL, 1UL), 1U);

                public IReadOnlyList<ManagedResourceLease> Leases => leases;

                public IManagedResourceLease Prepare(ManagedResourceRequest request)
                {
                    if (request == null)
                    {
                        throw new ArgumentNullException(nameof(request));
                    }

                    PrepareCount++;
                    Id128 leaseId = ids.Next();
                    var lease = new ManagedResourceLease(
                        request.Resource,
                        leaseId,
                        request.Token,
                        DisposerKey,
                        new ManagedResourceGate(),
                        disposed => OnDisposed(disposed));
                    leases.Add(lease);
                    return lease;
                }

                private void OnDisposed(Id128 leaseId)
                {
                    DisposedOrder.Add(leaseId);
                    DisposeCount++;
                }
            }
        }
    }
}
