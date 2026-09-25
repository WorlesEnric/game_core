// GameCore.Validation.ProbeHost — the GC-019 qualification scenario (common input, asset and presentation adapters).
//
// The gate sentences this file implements, verbatim from `docs/game-core/09-implementation-guide.md` (Wave 5, GC-019):
//
//   "Connect host resources and views while maintaining one authority per state domain." — with the acceptance
//   "Late asset/input completions cannot write retired worlds; view destruction leaves gameplay state intact;
//    reparenting a Transform does not move composition." — and the definition of done "Card/narrative outputs can be
//   presented from snapshots and run headless without those views; all adapter leases participate in lifecycle
//   tests."
//
// One runner, two family adapters (`IGc019Family`), exactly as GC-013's sequence and the Wave 4 gate have one runner
// and two adapters. The adapters own only what a genre declares (its command identity and payload, the committed
// targets that must receive a view); this file owns the one scripted sequence both genres run, so the adapters are
// demonstrated inside the *same* narrative and card worlds the earlier gates build rather than in a third world:
//
//   * the world and its committed targets are the GC-013 construction (`UnityWorldRegistry` + `AssemblyPublisher` +
//     `LiveTargetIndex`/`LiveTargetSeeder` over the family's declarations, the real `CompositionHost` control lane
//     with the production `DerivationModeSwitchValidator`, and `DerivedAssemblyPipeline` for the derivation), with the
//     compiled schedule's time driver (`WorldTimeDriver`) pumped through the family's own command-driven world
//     (P-030, P-042);
//   * input is the real `TypedInputIngress` over the world's own `ICommandIngress` (`UnityWorldHost`), so admission
//     keeps the one path the kernel already owns and a sampled event is not itself a committed game event (P-007,
//     P-037, P-042, 04 s7);
//   * delayed completions are validated at completion against the world's real `CallbackGate`
//     (`CompositionHost.Callbacks`) through `PendingInputCompletionTable` and `AssetLeaseTable`, so a stale activation
//     can only release its own acquisition (P-007, P-047);
//   * presentation reads one immutable `CommittedAssemblyImage` built from the world's published assembly reference
//     and applies it to views through the real `ViewRegistry`, `CommittedOutputPresenter` and a recording binder, so
//     composition parentage and Transform parentage are two separately observed facts (P-010, P-024, P-034, P-045);
//   * the adapter frame (`WorldAdapterFrame`) is registered in `AdapterFrameRegistry` and torn down through it, so
//     "all adapter leases participate in lifecycle tests" is a statement about the same object the application pump
//     drives (P-002, P-048, 04 s3).
//
// Nothing here re-implements a kernel module and nothing is discovered reflectively.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Unity.Adapters;
using GameCore.Unity.Adapters.Assets;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Adapters.Fixtures;
using GameCore.Unity.Adapters.Input;
using GameCore.Unity.Adapters.Views;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Runs the GC-019 adapter-gate sequence over one family and one catalog.</summary>
    public static class Gc019Scenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite instead of shrinking it
        /// silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc019-world-and-committed-targets",
            "gc019-input-sample-becomes-a-committed-command",
            "gc019-input-completion-from-a-retired-activation-is-discarded",
            "gc019-asset-lease-completes-under-a-live-token",
            "gc019-late-asset-completion-cannot-write-a-retired-world",
            "gc019-presentation-reads-the-committed-snapshot",
            "gc019-idle-world-presents-without-stepping",
            "gc019-view-destruction-leaves-gameplay-intact",
            "gc019-visual-reparent-does-not-move-composition",
            "gc019-does-not-declare-external-authority",
            "gc019-adapter-teardown-participates-in-lifecycle",
        };

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole sequence for one family and one catalog.</summary>
        public static Gc019ScenarioResult Run(IGc019Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            /// <summary>Bounded temporary storage the plans of this scenario may reserve, in bytes (P-022).</summary>
            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            private const ulong PrepareBytesLimit = 1024UL * 1024UL;

            /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
            private const ulong IdlePumpTicks = 1000000UL;

            private const int IdlePumpFrames = 4;

            /// <summary>Asset byte budget of the scenario's lease table; both leases together stay far below it.</summary>
            private const ulong AssetByteBudget = 4096UL;

            /// <summary>Bytes one requested lease accounts for (P-022, P-048).</summary>
            private const ulong LeaseBytes = 128UL;

            /// <summary>Capacity of the scenario's pending-input and asset-lease tables (P-043).</summary>
            private const uint AdapterTableCapacity = 8U;

            /// <summary>Capacity of the scenario's view registry: far above the two views it creates (TEST-015).</summary>
            private const uint ViewCapacity = 64U;

            /// <summary>
            /// The one declared scalar of the family's own command payload: the narrative family's permit choice
            /// (`NarrativeDialogueRules.PermitChoice` = 1) and the card family's seeded table version
            /// (`CardTableKeys.SeededTableVersion` = 1), i.e. a value both genres' own rules accept.
            /// </summary>
            private const int DeclaredCommandScalar = 1;

            /// <summary>Low half of the input-source identity: this scenario's own headless sampling source (P-008).</summary>
            private const ulong InputSourceLow = 0x6763303139696E70UL;

            /// <summary>Low half of the asset resource keys this scenario requests; each lease adds its own ordinal.</summary>
            private const ulong LeaseResourceLow = 0x67633031396C6561UL;

            /// <summary>Low half of the device binding's key code: a scenario-declared device ordinal (P-008).</summary>
            private const int BoundDeviceCode = 1;

            private readonly IGc019Family family;
            private readonly List<Gc019Step> steps = new List<Gc019Step>();
            private readonly IdSequence sessionSequence;

            private UnityWorldHost? host;
            private UnityWorldRegistration? registration;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private DerivationModeSwitchValidator? validator;
            private WorldTimeDriver? time;

            private TypedInputIngress? ingress;
            private PendingInputCompletionTable? pending;
            private DeterministicAssetBackend? backend;
            private AssetLeaseTable? assets;
            private ViewRegistry? views;
            private RecordingViewBinder? binder;
            private CommittedImageSource? source;
            private CommittedOutputPresenter? presenter;
            private InputBindingTable? bindings;
            private WorldAdapterFrame? frame;

            /// <summary>
            /// The genre's own stage runtime this run's world dispatches through: attached once the family seeded its
            /// targets, disposed in the teardown, so the world's systems resolve the module its own genre's scenario
            /// attaches instead of dispatching against no stage at all (P-043, P-005).
            /// </summary>
            private Gc019StageRuntime? stageRuntime;

            private PluginInstanceId providerInstance;
            private bool providerInstanceKnown;
            private ulong operationSequence;
            private int registryBeforeCreate;
            private int framesBeforeRegister = -1;
            private string lastFailure = string.Empty;

            /// <summary>Every publication this run made, counted against the P-006 series check.</summary>
            private int publications;

            /// <summary>Publications after which the lane pair and the world pair disagreed; zero is the gate.</summary>
            private int counterMismatches;

            /// <summary>First mismatch, verbatim, so a failure names the publication that broke the series.</summary>
            private string firstMismatch = "<none>";

            public Executor(IGc019Family family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public Gc019ScenarioResult Run()
            {
                CreateWorldAndCommittedTargets();
                SubmitOneInputSample();
                DiscardOneRetiredActivationCompletion();
                CompleteOneAssetLeaseUnderALiveToken();
                RefuseOneLateAssetCompletion();
                PresentTheCommittedSnapshot();
                PresentAnIdleWorld();
                DestroyEveryViewAndKeepGameplay();
                ReparentVisualsOnly();
                ProveEcsOwnedAuthority();
                TearDownAdaptersAndTheWorld();

                return new Gc019ScenarioResult(family.Label, steps);
            }

            // ------------------------------------------------------------------ 1. the world and its committed assembly

            /// <summary>
            /// Builds the one real world of this run — the family's compiled schedule, its live targets, the control
            /// lane over the family's own manifest source, the composition bridge, the derived-assembly pipeline and
            /// the command-driven time driver — then mounts the family's own provider and publishes the assembly that
            /// carries its rows. An adapter has no subject until an assembly is committed, so this is where the input,
            /// asset and presentation observations get theirs (P-030, P-042).
            /// </summary>
            private void CreateWorldAndCommittedTargets()
            {
                const string name = "gc019-world-and-committed-targets";
                try
                {
                    PipelineDescriptorReport descriptorReport = family.CompilePipeline();
                    if (!descriptorReport.Succeeded
                        || descriptorReport.Descriptor == null
                        || descriptorReport.Adaptation == null
                        || descriptorReport.Compilation == null)
                    {
                        Add(name, false, "the ownership and schedule pipeline refused: " + descriptorReport.Describe());
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId world = new WorldId(sessionSequence.Next());
                    WorldCreateRequest request = family.CreateRequest(world, NextOperation(world));
                    registration = family.CreateRegistration(descriptorReport.Adaptation);

                    bool created = UnityWorldRegistry.TryCreate(
                        request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Add(name, false, "world creation failed: " + result.Code + ": " + result.Detail);
                        return;
                    }

                    registry = new TargetRegistry(world, 32);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        family.CreateRecipes(),
                        family.CreateMigrations(),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    // One registered value source serves the mode-switch validator and the derivation pipeline, so
                    // every identity the lane checks is the one the world derives with (GC-013, P-009).
                    IDerivationValueSource valueSource = family.CreateValues();
                    validator = new DerivationModeSwitchValidator(valueSource, TargetView);

                    lane = CompositionHost.CreateDefault(
                        world,
                        family.WorldRootScope,
                        new CatalogManifestSource(family.Catalog, family.Declarations),
                        null,
                        family.LaneSeed,
                        validator);

                    bridge = new WorldCompositionBridge(host, lane, publisher);
                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        valueSource,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        new PlanBudget(
                            PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));

                    // The genre's own stage runtime, attached exactly where its own scenario attaches it: after the
                    // world exists and its targets are seeded, before any step is pumped. Without it the genre's
                    // systems resolve no module, every command stays unconsumed in its ingress lane and the step
                    // commit faults the world (P-043) — the adapter gate would then observe a faulted world rather
                    // than the adapters it exists to qualify.
                    stageRuntime = family.AttachStageRuntime(host, descriptorReport, targets, seeder);

                    // The family's own provider mounts are the publications that give the world its complete binding
                    // set, so every adapter observation afterwards has a committed assembly to observe (P-030, P-042).
                    // Both providers are mounted exactly the way the family's own GC-013 scenario mounts them, because
                    // "the committed targets that must receive a view" are the targets the family's *whole* declared
                    // revision derives into (P-013).
                    CompositionEditPayload provider = family.MountProvider();
                    providerInstance = provider.Instance;
                    providerInstanceKnown = !providerInstance.Value.IsDefault;
                    bool mounted = PublishEdit(provider, "mount-provider");
                    bool secondMounted = PublishEdit(family.MountSecondProvider(), "mount-second-provider");

                    var missing = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        TargetId target = family.ViewTargets[i];
                        if (!targets.Contains(target) || publisher.Published.Bindings.BindingsOf(target).Count == 0)
                        {
                            missing.Add(target.ToString());
                        }
                    }

                    ulong idleSteps = PumpIdleFrames();
                    bool joined = MatchesPublishedAssembly();

                    bool pass = seeded
                        && providerInstanceKnown
                        && mounted
                        && secondMounted
                        && publisher.PublishedRevision.Equals(lane.Committed.Revision)
                        && publisher.Published.BindingRowCount > 0
                        && family.ViewTargets.Count >= 2
                        && missing.Count == 0
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(lane.Committed.Epoch)
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.Lifecycle == WorldLifecycleState.Running
                        && UnityWorldRegistry.Count == registryBeforeCreate + 1
                        && idleSteps == 0UL
                        && joined;

                    Add(name, pass,
                        "session=" + world.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; viewTargets=" + family.ViewTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; targetsWithoutRows=" + Join(missing)
                        + "; providerInstance=" + (providerInstanceKnown ? providerInstance.ToString() : "<none>")
                        + "; bindingRows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + "; revision=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; providerMounted=" + mounted
                        + "; secondProviderMounted=" + secondMounted
                        + "; epoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; bridge=" + (bridge != null)
                        + "; lifecycle=" + host.Lifecycle
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 2. one sampled input command

            /// <summary>
            /// One sample of the family's own typed command becomes exactly one committed logical step, and its
            /// retransmission becomes none. The adapter stamps the sample with the world's own incarnation and the
            /// source's strictly increasing sequence, offers it to the world's existing command port, and returns the
            /// recorded admission on a retry, so a resend can never execute twice (P-037, P-042, P-050).
            /// </summary>
            private void SubmitOneInputSample()
            {
                const string name = "gc019-input-sample-becomes-a-committed-command";
                try
                {
                    if (host == null || time == null || lane == null || publisher == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    WorldMessagePlane? plane = host.Messages;
                    if (plane == null)
                    {
                        Add(name, false, "the world has no command plane, so no typed command can be admitted (P-042)");
                        return;
                    }

                    ingress = new TypedInputIngress(host.World, host);

                    InputSourceStamp stamp = MintStamp(host.World, sequence: 1UL);
                    SampledInputCommand sample = new SampledInputCommand(
                        stamp,
                        family.CommandRoute,
                        family.CommandTarget,
                        family.CommandSchema,
                        null,
                        family.CommandPayload(DeclaredCommandScalar));

                    LogicalStepId stepBefore = host.CurrentStep;
                    int committedBefore = plane.Requests.CommittedCount;
                    int admittedRequestsBefore = plane.Requests.AdmittedCount;
                    int rejectedBefore = plane.Requests.RejectedCount;
                    CompositionRevision revisionBefore = publisher.PublishedRevision;
                    string rowsBefore = BindingRowFingerprint(family.CommandTarget);

                    InputAdmissionResult admission = ingress.Submit(sample);
                    TimeFrameReport frame = time.PumpFrame(IdlePumpTicks);

                    LogicalStepId stepAfter = host.CurrentStep;
                    int committedAfter = plane.Requests.CommittedCount;

                    // The domain really moved: the world's own request ledger recorded a committed result for the
                    // command, because both genres' owners answer their route and commit that answer (P-042, P-044).
                    bool domainMoved = committedAfter == committedBefore + 1;

                    // The very same sample again: one request key, one recorded admission, no second execution
                    // (P-037, P-050).
                    InputAdmissionResult retransmission = ingress.Submit(sample);
                    TimeFrameReport idle = time.PumpFrame(IdlePumpTicks);

                    bool pass = admission.Outcome == InputAdmissionOutcome.Admitted
                        && admission.Admission != null
                        && admission.Admission.Admitted
                        && ingress.AdmittedCount == 1
                        && ingress.RefusedCount == 0
                        && ingress.RetransmissionCount == 1
                        && ingress.SourceCount == 1
                        && ingress.RetainedAdmissionCount == 1
                        && plane.Requests.AdmittedCount == admittedRequestsBefore + 1
                        && frame.StepsCommitted == 1UL
                        && stepAfter.Value == stepBefore.Value + 1UL
                        && domainMoved
                        && retransmission.Outcome == InputAdmissionOutcome.Retransmission
                        && idle.StepsCommitted == 0UL
                        && host.CurrentStep.Equals(stepAfter)
                        && plane.Requests.CommittedCount == committedAfter
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && string.Equals(BindingRowFingerprint(family.CommandTarget), rowsBefore, StringComparison.Ordinal)
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "admission=" + admission.Outcome
                        + "; admissionKind=" + (admission.Admission != null ? admission.Admission.Result.Kind.ToString() : "<none>")
                        + "; route=" + family.CommandRoute.ToString()
                        + "; target=" + family.CommandTarget.ToString()
                        + "; schema=" + family.CommandSchema.ToString()
                        + "; sample=" + sample.ToString()
                        + "; stepsCommitted=" + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + stepAfter.Value.ToString(CultureInfo.InvariantCulture)
                        + "; committedRequests=" + committedBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + committedAfter.ToString(CultureInfo.InvariantCulture)
                        + "; refusedRequestsDelta=" + (plane.Requests.RejectedCount - rejectedBefore).ToString(CultureInfo.InvariantCulture)
                        + "; retransmission=" + retransmission.Outcome
                        + "; retransmittedSteps=" + idle.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + "; admittedRequests=" + admittedRequestsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + plane.Requests.AdmittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; admitted=" + ingress.AdmittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; refused=" + ingress.RefusedCount.ToString(CultureInfo.InvariantCulture)
                        + "; sources=" + ingress.SourceCount.ToString(CultureInfo.InvariantCulture)
                        + "; retainedAdmissions=" + ingress.RetainedAdmissionCount.ToString(CultureInfo.InvariantCulture)
                        + "; rowsBefore=" + rowsBefore
                        + "; rowsAfter=" + BindingRowFingerprint(family.CommandTarget)
                        + "; familyState=" + DescribeFamilyState()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. a discarded input completion

            /// <summary>
            /// A delayed input completion whose activation was retired must be discarded, not delivered: the pending
            /// table evaluates the world's real callback gate *at completion*, so an installation the lane has
            /// suspended cannot reacquire execution authority through a late answer (P-007, P-047).
            /// </summary>
            private void DiscardOneRetiredActivationCompletion()
            {
                const string name = "gc019-input-completion-from-a-retired-activation-is-discarded";
                try
                {
                    if (host == null || lane == null || ingress == null || !providerInstanceKnown)
                    {
                        Add(name, false, "the world, its lane, its ingress or its provider installation is missing");
                        return;
                    }

                    if (!TryLiveToken(out AsyncWorkToken liveToken, workOrdinal: 1U, out string tokenDetail))
                    {
                        Add(name, false, "no live activation stamp for the provider installation: " + tokenDetail);
                        return;
                    }

                    bool liveAtIssue = lane.Callbacks.Evaluate(liveToken) == CallbackGateDecision.Dispatch;
                    pending = new PendingInputCompletionTable(lane.Callbacks, ingress, AdapterTableCapacity);
                    bool registered = pending.TryRegister(liveToken, out DiagnosticCode registerCode, out string registerDetail);

                    bool suspended = PublishEdit(LifecycleEdit(CompositionEditSubject.InstallSuspend, providerInstance), "suspend-provider");
                    bool stateSuspended = StateOf(providerInstance) == InstallationState.Suspended.ToString();
                    CallbackGateDecision afterRetirement = lane.Callbacks.Evaluate(liveToken);

                    WorldMessagePlane? plane = host.Messages;
                    int admittedRequestsBefore = plane != null ? plane.Requests.AdmittedCount : 0;
                    int admittedBefore = ingress.AdmittedCount;
                    int refusedBefore = ingress.RefusedCount;
                    LogicalStepId stepBefore = host.CurrentStep;

                    // The late answer carries a *new* sample of its own source, so a delivered completion would be a
                    // real second command rather than a retry (P-047, P-050).
                    SampledInputCommand late = new SampledInputCommand(
                        MintStamp(host.World, sequence: 2UL),
                        family.CommandRoute,
                        family.CommandTarget,
                        family.CommandSchema,
                        null,
                        family.CommandPayload(DeclaredCommandScalar));

                    InputCompletionResult completion = pending.Complete(liveToken, late);

                    bool pass = liveAtIssue
                        && registered
                        && suspended
                        && stateSuspended
                        && afterRetirement != CallbackGateDecision.Dispatch
                        && completion.Outcome == InputCompletionOutcome.Discarded
                        && completion.Gate != CallbackGateDecision.Dispatch
                        && completion.Admission == null
                        && host.CurrentStep.Equals(stepBefore)
                        && ingress.AdmittedCount == admittedBefore
                        && ingress.RefusedCount == refusedBefore
                        && (plane == null || plane.Requests.AdmittedCount == admittedRequestsBefore)
                        && pending.PendingCount == 0
                        && pending.RegisteredCount == 1
                        && pending.DiscardedCount == 1
                        && pending.ReleasedStagedCount == 1
                        && pending.DispatchedCount == 0
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "liveAtIssue=" + liveAtIssue
                        + "; registered=" + registered
                        + (registered ? string.Empty : "(" + registerCode + ": " + registerDetail + ")")
                        + "; token=" + liveToken.ToString()
                        + "; suspended=" + suspended
                        + "(" + StateOf(providerInstance) + ")"
                        + "; gateAfterRetirement=" + afterRetirement
                        + "; completion=" + completion.Outcome
                        + "; completionGate=" + completion.Gate
                        + "; plane=" + (plane != null ? "present" : "<none>")
                        + "; refusedBefore=" + refusedBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + ingress.RefusedCount.ToString(CultureInfo.InvariantCulture)
                        + "; admitted=" + admittedBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + ingress.AdmittedCount.ToString(CultureInfo.InvariantCulture)
                        + "; discarded=" + pending.DiscardedCount.ToString(CultureInfo.InvariantCulture)
                        + "; releasedStaged=" + pending.ReleasedStagedCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. one asset lease under a live token

            /// <summary>
            /// One asynchronous asset lease completed while its activation still holds authority becomes usable
            /// read-only data, and its acquisition is recorded in the world's own resource ledger, so the existing
            /// P-048 teardown path fences and retires adapter leases like any other managed lease (P-007, P-029,
            /// P-048).
            /// </summary>
            private void CompleteOneAssetLeaseUnderALiveToken()
            {
                const string name = "gc019-asset-lease-completes-under-a-live-token";
                try
                {
                    if (host == null || lane == null || !providerInstanceKnown)
                    {
                        Add(name, false, "the world, its lane or its provider installation is missing");
                        return;
                    }

                    // The installation was suspended by the previous step, so it is resumed here: "a live token" has
                    // to be a live activation, not a remembered one (P-046).
                    bool resumed = PublishEdit(LifecycleEdit(CompositionEditSubject.InstallResume, providerInstance), "resume-provider");
                    if (!TryLiveToken(out AsyncWorkToken token, workOrdinal: 2U, out string tokenDetail))
                    {
                        Add(name, false, "the resumed installation has no activation stamp: " + tokenDetail);
                        return;
                    }

                    bool liveAtIssue = lane.Callbacks.Evaluate(token) == CallbackGateDecision.Dispatch;

                    backend = new DeterministicAssetBackend();
                    assets = new AssetLeaseTable(
                        host.World,
                        backend,
                        lane.Callbacks,
                        host.Ledger,
                        family.MutableOwner,
                        providerInstance,
                        AdapterTableCapacity,
                        AssetByteBudget);

                    ResourceKey resource = LeaseResource(1UL);
                    bool requested = assets.TryRequest(
                        resource,
                        token,
                        new FrozenPayload(new byte[] { 1, 2, 3, 4 }),
                        LeaseBytes,
                        out Id128 leaseId,
                        out DiagnosticCode requestCode,
                        out string requestDetail);

                    bool found = assets.TryGet(leaseId, out AssetLease? lease) && lease != null;
                    long handle = found && lease != null ? lease.Handle : 0L;
                    bool loadCompleted = found && backend.Complete(handle);
                    int pumped = assets.PumpCompletions();

                    bool usable = found
                        && lease != null
                        && lease.State == AssetLeaseState.Ready
                        && lease.Payload != null
                        && lease.CompletionCount == 1
                        && assets.TryReadPayload(leaseId, out FrozenPayload? payload)
                        && payload != null
                        && payload.Bytes.Count == 4;

                    Id128 ledgerResourceId = found && lease != null ? lease.LedgerResourceId : Id128.Zero;
                    ResourceRetirementState? ledgerState = found && lease != null ? LedgerStateOf(ledgerResourceId) : null;

                    bool pass = resumed
                        && liveAtIssue
                        && requested
                        && found
                        && loadCompleted
                        && pumped == 1
                        && usable
                        && assets.CompletedCount == 1
                        && assets.StaleDiscardCount == 0
                        && assets.LiveLeaseCount == 1
                        && assets.ReservedBytes == LeaseBytes
                        && backend.OutstandingLoadCount == 1
                        && ledgerState == ResourceRetirementState.Ready
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "resumed=" + resumed
                        + "; liveAtIssue=" + liveAtIssue
                        + "; token=" + token.ToString()
                        + "; requested=" + requested
                        + (requested ? string.Empty : "(" + requestCode + ": " + requestDetail + ")")
                        + "; lease=" + leaseId.ToString()
                        + "; handle=" + handle.ToString(CultureInfo.InvariantCulture)
                        + "; pumped=" + pumped.ToString(CultureInfo.InvariantCulture)
                        + "; state=" + (found && lease != null ? lease.State.ToString() : "<none>")
                        + "; payloadBytes=" + (found && lease != null && lease.Payload != null
                            ? lease.Payload.Bytes.Count.ToString(CultureInfo.InvariantCulture)
                            : "<none>")
                        + "; reservedBytes=" + assets.ReservedBytes.ToString(CultureInfo.InvariantCulture)
                        + "; backendOutstanding=" + backend.OutstandingLoadCount.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerResource=" + ledgerResourceId.ToString()
                        + "; ledgerState=" + (ledgerState.HasValue ? ledgerState.Value.ToString() : "<missing>")
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. a late asset completion

            /// <summary>
            /// After the installation's activation is retired, no completion may install anything: a late completion
            /// is discarded and releases only its own acquisition, and a completion that arrives after the table was
            /// retired installs nothing while still releasing what its request acquired (P-007, P-047, P-048). This is
            /// TEST-015's "late asset callback rejection".
            /// </summary>
            private void RefuseOneLateAssetCompletion()
            {
                const string name = "gc019-late-asset-completion-cannot-write-a-retired-world";
                try
                {
                    if (host == null || lane == null || assets == null || backend == null || !providerInstanceKnown)
                    {
                        Add(name, false, "the world, its lane or its asset lease table is missing");
                        return;
                    }

                    bool suspended = PublishEdit(LifecycleEdit(CompositionEditSubject.InstallSuspend, providerInstance), "suspend-provider-again");
                    if (!TryRetiredToken(out AsyncWorkToken staleToken, workOrdinal: 3U, out string tokenDetail))
                    {
                        Add(name, false, "the suspended installation has no stamp to retire: " + tokenDetail);
                        return;
                    }

                    CallbackGateDecision staleGate = lane.Callbacks.Evaluate(staleToken);

                    // The lease table validates a completion, not a request: the request is accepted and the
                    // *completion* is what the retired activation refuses (P-007).
                    bool requested = assets.TryRequest(
                        LeaseResource(2UL),
                        staleToken,
                        new FrozenPayload(new byte[] { 5, 6, 7, 8 }),
                        LeaseBytes,
                        out Id128 lateLeaseId,
                        out DiagnosticCode lateCode,
                        out string lateDetail);

                    bool foundLate = assets.TryGet(lateLeaseId, out AssetLease? lateLease) && lateLease != null;
                    bool loadCompleted = foundLate && backend.Complete(lateLease != null ? lateLease.Handle : 0L);
                    AssetCompletionResult late = assets.Complete(
                        lateLeaseId,
                        new FrozenPayload(new byte[] { 9, 9, 9, 9 }));

                    bool lateDiscarded = late.Outcome == AssetCompletionOutcome.DiscardedStale
                        && late.Gate != CallbackGateDecision.Dispatch
                        && foundLate
                        && lateLease != null
                        && lateLease.State == AssetLeaseState.DiscardedStale
                        && lateLease.Payload == null
                        && !IsPayloadReadable(lateLeaseId)
                        && assets.StaleDiscardCount == 1;

                    Id128 lateLedgerId = foundLate && lateLease != null ? lateLease.LedgerResourceId : Id128.Zero;
                    bool lateNotRetained = foundLate && !IsRetainedInLedger(lateLedgerId);

                    // A lease whose engine release throws is quarantined rather than reported as released (P-048), and
                    // releasing that quarantine after every user ended makes the lease live again — after the table
                    // was retired. The completion that follows may release its acquisition but must install nothing.
                    backend.FailNextRelease = true;
                    bool quarantinedRequest = assets.TryRequest(
                        LeaseResource(3UL),
                        staleToken,
                        new FrozenPayload(new byte[] { 10, 11, 12, 13 }),
                        LeaseBytes,
                        out Id128 quarantinedLeaseId,
                        out DiagnosticCode quarantinedCode,
                        out string quarantinedDetail);

                    AssetReleaseReport retired = assets.Retire();
                    bool retainedQuarantine = Contains(retired.Retained, quarantinedLeaseId);
                    bool releasedOthers = Contains(retired.Released, lateLeaseId);
                    bool releasedQuarantine = assets.ReleaseQuarantine(quarantinedLeaseId);

                    int postRetireBefore = assets.PostRetireCompletionCount;
                    AssetCompletionResult postRetire = assets.Complete(
                        quarantinedLeaseId,
                        new FrozenPayload(new byte[] { 14, 15, 16, 17 }));
                    int postRetireAfter = assets.PostRetireCompletionCount;
                    bool postRetiredNothing = postRetire.Outcome == AssetCompletionOutcome.DiscardedStale
                        && postRetire.Gate != CallbackGateDecision.Dispatch
                        && postRetireAfter > postRetireBefore
                        && assets.TryGet(quarantinedLeaseId, out AssetLease? quarantinedLease)
                        && quarantinedLease != null
                        && quarantinedLease.State == AssetLeaseState.DiscardedStale
                        && quarantinedLease.Payload == null;

                    // One more attempt on a terminal lease installs nothing and leaves the lease exactly as it was.
                    // The attempt itself still counts as a completion that reached a retired table (P-050 forbids the
                    // second *installation*, not the arrival), so the counter moves by exactly one and no further.
                    AssetCompletionResult repeated = assets.Complete(
                        quarantinedLeaseId,
                        new FrozenPayload(new byte[] { 18, 19, 20, 21 }));
                    bool terminalRefused = repeated.Outcome == AssetCompletionOutcome.AlreadyTerminal
                        && assets.PostRetireCompletionCount == postRetireAfter + 1;

                    int retainedLeases = RetainedLeaseCountOf(providerInstance);

                    bool pass = suspended
                        && staleGate != CallbackGateDecision.Dispatch
                        && requested
                        && foundLate
                        && loadCompleted
                        && lateDiscarded
                        && lateNotRetained
                        && quarantinedRequest
                        && assets.IsRetired
                        && retainedQuarantine
                        && releasedOthers
                        && releasedQuarantine
                        && postRetiredNothing
                        && terminalRefused
                        && backend.OutstandingLoadCount == 0
                        && retainedLeases == 0
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "suspended=" + suspended
                        + "(" + StateOf(providerInstance) + ")"
                        + "; staleGate=" + staleGate
                        + "; requested=" + requested
                        + (requested ? string.Empty : "(" + lateCode + ": " + lateDetail + ")")
                        + "; lateLease=" + lateLeaseId.ToString()
                        + "; late=" + late.Outcome
                        + "; lateGate=" + late.Gate
                        + "; lateState=" + (foundLate && lateLease != null ? lateLease.State.ToString() : "<none>")
                        + "; lateRetainedInLedger=" + !lateNotRetained
                        + "; quarantinedRequest=" + quarantinedRequest
                        + (quarantinedRequest ? string.Empty : "(" + quarantinedCode + ": " + quarantinedDetail + ")")
                        + "; retired=" + assets.IsRetired
                        + "; report=" + retired.ToString()
                        + "; quarantinedLease=" + quarantinedLeaseId.ToString()
                        + "; postRetire=" + postRetire.Outcome
                        + "; postRetireCompletions=" + postRetireBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + postRetireAfter.ToString(CultureInfo.InvariantCulture)
                        + "; backendOutstanding=" + backend.OutstandingLoadCount.ToString(CultureInfo.InvariantCulture)
                        + "; retainedLeasesOfTheInstallation=" + retainedLeases.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 6. committed-output presentation

            /// <summary>
            /// Presentation reads one immutable committed image and applies it to the views of the family's own
            /// derived targets: every presented value equals the published binding row of that target, capability and
            /// slot, and every presented composition parent equals the scope the live target index reports — read from
            /// composition, never from the Transform (P-010, P-024, P-034, P-045).
            /// </summary>
            private void PresentTheCommittedSnapshot()
            {
                const string name = "gc019-presentation-reads-the-committed-snapshot";
                try
                {
                    if (host == null || lane == null || targets == null || publisher == null || ingress == null
                        || pending == null || assets == null)
                    {
                        Add(name, false, "the world, its targets or its adapters are missing");
                        return;
                    }

                    // The previous step left the installation suspended, so the committed assembly currently carries
                    // no rows for the family's view targets; the composition is put back before presentation reads it.
                    bool resumed = PublishEdit(LifecycleEdit(CompositionEditSubject.InstallResume, providerInstance), "resume-for-presentation");

                    CommittedAssemblyImage image = LiveAssemblyImageBuilder.Build(
                        publisher.Published, new LiveTargetScopeIndex(targets));

                    source = new CommittedImageSource(host.World);
                    bool refreshed = source.Refresh(image);
                    views = new ViewRegistry(host.World, ViewCapacity);
                    binder = new RecordingViewBinder();
                    presenter = new CommittedOutputPresenter(views, source, binder);

                    var created = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        TargetId target = family.ViewTargets[i];
                        ViewCreateOutcome outcome = presenter.CreateView(target, 0U, out ViewRecord? record);
                        created.Add(target.ToString() + "=" + outcome + (record != null ? "@" + record.Handle.ToString(CultureInfo.InvariantCulture) : string.Empty));
                    }

                    PresentationReport presented = presenter.Present();

                    var dissenting = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        TargetId target = family.ViewTargets[i];
                        if (!PresentedMatchesPublished(target, image.Token, out string dissent))
                        {
                            dissenting.Add(dissent);
                        }
                    }

                    // The adapter frame is the object the application pump drives; registering it here — once its
                    // ingress, pending table, asset table and presenter all exist — is what makes the idle pass and the
                    // teardown statements apply to that one object (P-002, 04 s3).
                    bindings = new InputBindingTable().Add(new InputCommandBinding(
                        InputDeviceKind.Button,
                        BoundDeviceCode,
                        family.CommandRoute,
                        family.CommandTarget,
                        family.CommandSchema,
                        null));

                    framesBeforeRegister = AdapterFrameRegistry.Count;
                    frame = new WorldAdapterFrame(host, ingress, bindings, null, pending, assets, presenter);
                    bool registered = AdapterFrameRegistry.Register(frame);
                    bool found = AdapterFrameRegistry.TryGet(host.World, out IAdapterFrame? registeredFrame);

                    bool pass = resumed
                        && refreshed
                        && image.TargetCount > 0
                        && image.FieldCount > 0
                        && presented.Outcome == PresentationOutcome.Presented
                        && presented.Applied == family.ViewTargets.Count
                        && presented.StaleRefused == 0
                        && presented.OrphanedDestroyed == 0
                        && presented.ViewsAfter == family.ViewTargets.Count
                        && dissenting.Count == 0
                        && views.LiveViewCount == family.ViewTargets.Count
                        && binder.LiveViewCount == family.ViewTargets.Count
                        && binder.ApplyCount == family.ViewTargets.Count
                        && bindings.Count == 1
                        && !registered
                        && found
                        && registeredFrame != null
                        && ReferenceEquals(registeredFrame, frame)
                        && frame.World.Equals(host.World)
                        && AdapterFrameRegistry.Count == framesBeforeRegister + 1;

                    Add(name, pass,
                        "resumed=" + resumed
                        + "; image=" + image.ToString()
                        + "; imageToken=" + image.Token.ToString()
                        + "; refreshed=" + refreshed
                        + "; created=" + Join(created)
                        + "; presented=" + presented.Outcome
                        + "; applied=" + presented.Applied.ToString(CultureInfo.InvariantCulture)
                        + "; stale=" + presented.StaleRefused.ToString(CultureInfo.InvariantCulture)
                        + "; orphaned=" + presented.OrphanedDestroyed.ToString(CultureInfo.InvariantCulture)
                        + "; dissenting=" + Join(dissenting)
                        + "; liveViews=" + views.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; binderViews=" + binder.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; deviceBindings=" + bindings.Count.ToString(CultureInfo.InvariantCulture)
                        + "; frameRegistered=" + found
                        + "; framesBeforeRegister=" + framesBeforeRegister.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. an idle world still presents

            /// <summary>
            /// Host frames with no command commit no logical step and still present: the adapter frame is called at
            /// the two points the pump algorithm names, and a presentation pass whose token is not newer than the last
            /// applied one applies nothing (P-036, P-045, 04 s3).
            /// </summary>
            private void PresentAnIdleWorld()
            {
                const string name = "gc019-idle-world-presents-without-stepping";
                try
                {
                    if (host == null || time == null || frame == null || presenter == null || views == null)
                    {
                        Add(name, false, "the world or its adapter frame is missing");
                        return;
                    }

                    LogicalStepId stepBefore = host.CurrentStep;
                    int presentCallsBefore = presenter.PresentCallCount;
                    int inputCallsBefore = AdapterFrameRegistry.InputCalls;
                    int presentationCallsBefore = AdapterFrameRegistry.PresentationCalls;
                    int staleRefusalsBefore = presenter.StaleRefusalCount;

                    ulong committed = 0UL;
                    bool framesRan = true;
                    var passes = new List<string>();
                    for (int i = 0; i < IdlePumpFrames; i++)
                    {
                        AdapterFrameReport collected = AdapterFrameRegistry.CollectInput(host.World);
                        TimeFrameReport pumped = time.PumpFrame(IdlePumpTicks);
                        AdapterFrameReport presented = AdapterFrameRegistry.Present(host.World);

                        committed += pumped.StepsCommitted;
                        framesRan &= collected.Ran && presented.Ran;
                        passes.Add(pumped.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                            + "/" + collected.Outcome
                            + "/" + presented.Outcome
                            + "/" + presented.Items.ToString(CultureInfo.InvariantCulture));
                    }

                    int staleRefusalsAfter = presenter.StaleRefusalCount;
                    bool appliedNothingNew = staleRefusalsAfter - staleRefusalsBefore
                        == family.ViewTargets.Count * IdlePumpFrames;

                    bool pass = framesRan
                        && committed == 0UL
                        && presenter.PresentCallCount == presentCallsBefore + IdlePumpFrames
                        && frame.PresentPassCount == IdlePumpFrames
                        && AdapterFrameRegistry.InputCalls == inputCallsBefore + IdlePumpFrames
                        && AdapterFrameRegistry.PresentationCalls == presentationCallsBefore + IdlePumpFrames
                        && frame.DeviceSampleCount == 0
                        && frame.UnboundSampleCount == 0
                        && appliedNothingNew
                        && views.LiveViewCount == family.ViewTargets.Count
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "frames=" + IdlePumpFrames.ToString(CultureInfo.InvariantCulture)
                        + "; stepsCommitted=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; presentCalls=" + presentCallsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + presenter.PresentCallCount.ToString(CultureInfo.InvariantCulture)
                        + "; staleRefusals=" + staleRefusalsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + staleRefusalsAfter.ToString(CultureInfo.InvariantCulture)
                        + "; passes=" + Join(passes)
                        + "; deviceSamples=" + frame.DeviceSampleCount.ToString(CultureInfo.InvariantCulture)
                        + "; unbound=" + frame.UnboundSampleCount.ToString(CultureInfo.InvariantCulture)
                        + "; liveViews=" + views.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; framesRan=" + framesRan
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 8. destroying views changes no gameplay

            /// <summary>
            /// Destroying every view changes nothing in gameplay: a view is presentation-only, so the published
            /// assembly, its rows, the published revision, the world's epoch and step and the committed composition are
            /// exactly what they were, and a real command still commits a step afterwards. This is GC-019's acceptance
            /// clause "view destruction leaves gameplay state intact" (P-003, P-024).
            /// </summary>
            private void DestroyEveryViewAndKeepGameplay()
            {
                const string name = "gc019-view-destruction-leaves-gameplay-intact";
                try
                {
                    if (host == null || time == null || presenter == null || views == null || binder == null
                        || publisher == null || lane == null || ingress == null)
                    {
                        Add(name, false, "the world, its presenter or its ingress is missing");
                        return;
                    }

                    string rowsBefore = PublishedRowsFingerprint();
                    string scopesBefore = ScopeFingerprint();
                    CompositionRevision revisionBefore = publisher.PublishedRevision;
                    AssemblyEpoch epochBefore = host.CurrentEpoch;
                    LogicalStepId stepBefore = host.CurrentStep;
                    int liveBefore = views.LiveViewCount;
                    int ledgerBefore = host.Ledger.ResourceCount;

                    int destroyed = presenter.DestroyAllViews();

                    bool destroyedAll = destroyed == liveBefore
                        && views.LiveViewCount == 0
                        && binder.LiveViewCount == 0
                        && binder.DestroyedCount == destroyed;

                    // ... and gameplay keeps running: one more typed command commits exactly one step (P-042).
                    SampledInputCommand sample = new SampledInputCommand(
                        MintStamp(host.World, sequence: 3UL),
                        family.CommandRoute,
                        family.CommandTarget,
                        family.CommandSchema,
                        null,
                        family.CommandPayload(DeclaredCommandScalar));
                    InputAdmissionResult admission = ingress.Submit(sample);
                    LogicalStepId commandStepBefore = host.CurrentStep;
                    TimeFrameReport frameAfter = time.PumpFrame(IdlePumpTicks);

                    bool pass = destroyedAll
                        && string.Equals(PublishedRowsFingerprint(), rowsBefore, StringComparison.Ordinal)
                        && string.Equals(ScopeFingerprint(), scopesBefore, StringComparison.Ordinal)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && host.CurrentEpoch.Equals(epochBefore)
                        && host.CurrentStep.Equals(stepBefore)
                        && host.Ledger.ResourceCount == ledgerBefore
                        && admission.Outcome == InputAdmissionOutcome.Admitted
                        && frameAfter.StepsCommitted == 1UL
                        && host.CurrentStep.Value == commandStepBefore.Value + 1UL
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "destroyed=" + destroyed.ToString(CultureInfo.InvariantCulture)
                        + "; liveViews=" + liveBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + views.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; binderViews=" + binder.LiveViewCount.ToString(CultureInfo.InvariantCulture)
                        + "; rowsUnchanged=" + string.Equals(PublishedRowsFingerprint(), rowsBefore, StringComparison.Ordinal)
                        + "; scopesUnchanged=" + string.Equals(ScopeFingerprint(), scopesBefore, StringComparison.Ordinal)
                        + "; revision=" + revisionBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; epoch=" + epochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; ledgerResources=" + ledgerBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.Ledger.ResourceCount.ToString(CultureInfo.InvariantCulture)
                        + "; nextCommand=" + admission.Outcome
                        + "; stepsCommitted=" + frameAfter.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 9. a visual reparent moves no composition

            /// <summary>
            /// An explicit visual reparent moves the binder's parent pointer and nothing else: the committed
            /// composition parent of the view is unchanged, the scope tree and every scope record are unchanged, and no
            /// publication happened. This is GC-019's acceptance clause "reparenting a Transform does not move
            /// composition" (P-010, 04 s6).
            /// </summary>
            private void ReparentVisualsOnly()
            {
                const string name = "gc019-visual-reparent-does-not-move-composition";
                try
                {
                    if (host == null || presenter == null || views == null || binder == null || publisher == null
                        || lane == null || family.ViewTargets.Count < 2)
                    {
                        Add(name, false, "the world, its presenter or two view targets are missing");
                        return;
                    }

                    TargetId childTarget = family.ViewTargets[0];
                    TargetId parentTarget = family.ViewTargets[1];
                    ViewCreateOutcome childCreated = presenter.CreateView(childTarget, 0U, out ViewRecord? childRecord);
                    ViewCreateOutcome parentCreated = presenter.CreateView(parentTarget, 0U, out ViewRecord? parentRecord);
                    if (childCreated != ViewCreateOutcome.Created || parentCreated != ViewCreateOutcome.Created
                        || childRecord == null || parentRecord == null)
                    {
                        Add(name, false, "the two views could not be recreated: " + childCreated + "/" + parentCreated);
                        return;
                    }

                    // One presentation pass first, so the view's composition parent is the committed one rather than
                    // a default value the comparison could pass with by accident (P-045).
                    PresentationReport applied = presenter.Present();
                    ScopeId compositionParentBefore = childRecord.CompositionParent;
                    long childHandle = childRecord.Handle;
                    long parentHandle = parentRecord.Handle;
                    string scopesBefore = ScopeFingerprint();
                    CompositionRevision revisionBefore = publisher.PublishedRevision;

                    bool reparented = presenter.SetVisualParent(childRecord.Key, parentRecord.Key);

                    bool recorded = views.TryGet(childRecord.Key, out ViewRecord? after) && after != null;
                    bool pass = applied.Applied == 2
                        && applied.StaleRefused == 0
                        && reparented
                        && binder.ParentChanges.Count == 1
                        && binder.ParentChanges[0].Child == childHandle
                        && binder.ParentChanges[0].Parent == parentHandle
                        && recorded
                        && after != null
                        && after.VisualParent == parentHandle
                        && after.VisualParentChanged
                        && after.CompositionParent.Equals(compositionParentBefore)
                        && views.VisualReparentCount == 1
                        && views.VisualReparentsWithoutCompositionChange == 1
                        && string.Equals(ScopeFingerprint(), scopesBefore, StringComparison.Ordinal)
                        && publisher.PublishedRevision.Equals(revisionBefore)
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "views=" + applied.Applied.ToString(CultureInfo.InvariantCulture)
                        + "; reparented=" + reparented
                        + "; child=" + childTarget.ToString() + "@" + childHandle.ToString(CultureInfo.InvariantCulture)
                        + "; parent=" + parentTarget.ToString() + "@" + parentHandle.ToString(CultureInfo.InvariantCulture)
                        + "; parentChanges=" + binder.ParentChanges.Count.ToString(CultureInfo.InvariantCulture)
                        + "; compositionParent=" + compositionParentBefore.ToString()
                        + "->" + (recorded && after != null ? after.CompositionParent.ToString() : "<missing>")
                        + "; visualParent=" + (recorded && after != null ? after.VisualParent.ToString(CultureInfo.InvariantCulture) : "<missing>")
                        + "; scopesUnchanged=" + string.Equals(ScopeFingerprint(), scopesBefore, StringComparison.Ordinal)
                        + "; revision=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; reparentsWithoutCompositionChange="
                        + views.VisualReparentsWithoutCompositionChange.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 10. no external authority is declared

            /// <summary>
            /// These worlds need no physics at all: every live target's recipe is ECS-owned kinematic by default and
            /// an intent for it is refused as a value rather than forwarded to an absent adapter, no external domain is
            /// declared, and the compiled schedule contains no physics stage. This is P-059/TEST-019's "optional
            /// physics/animation are absent from cards/narrative" (P-034, P-059).
            /// </summary>
            private void ProveEcsOwnedAuthority()
            {
                const string name = "gc019-does-not-declare-external-authority";
                try
                {
                    if (host == null || registration == null || targets == null || lane == null)
                    {
                        Add(name, false, "the world, its registration or its targets are missing");
                        return;
                    }

                    var ledger = new ExternalAuthorityLedger(host.World);

                    DefinitionRef recipe = default(DefinitionRef);
                    bool recipeKnown = targets.TryGet(family.CommandTarget, out LiveTarget live);
                    if (recipeKnown)
                    {
                        recipe = live.Recipe;
                    }

                    MotionAuthority authority = ledger.AuthorityOf(recipe);
                    AuthorityIntentOutcome classification = ExternalAuthority.Classify(ledger, recipe, null);
                    ExternalAuthorityDescriptor declared = default(ExternalAuthorityDescriptor);
                    bool undeclared = !ledger.TryDescribe(recipe, out declared);

                    var physicsStages = new List<string>();
                    for (int i = 0; i < registration.Stages.Count; i++)
                    {
                        if (NamesPhysics(registration.Stages[i].DiagnosticName))
                        {
                            physicsStages.Add(registration.Stages[i].DiagnosticName);
                        }
                    }

                    var physicsSystems = new List<string>();
                    for (int i = 0; i < registration.Systems.Count; i++)
                    {
                        if (NamesPhysics(registration.Systems[i].DiagnosticName))
                        {
                            physicsSystems.Add(registration.Systems[i].DiagnosticName);
                        }
                    }

                    var authoring = new List<string>();
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        if (targets.TryGet(family.ViewTargets[i], out LiveTarget viewTarget)
                            && ledger.AuthorityOf(viewTarget.Recipe) != MotionAuthority.EcsOwnedKinematic)
                        {
                            authoring.Add(family.ViewTargets[i].ToString());
                        }
                    }

                    bool pass = recipeKnown
                        && authority == MotionAuthority.EcsOwnedKinematic
                        && classification == AuthorityIntentOutcome.RefusedEcsOwned
                        && undeclared
                        && ledger.ExternalDomainCount == 0
                        && ledger.DeclarationCount == 0
                        && ledger.AuthorityOf(default(DefinitionRef)) == MotionAuthority.EcsOwnedKinematic
                        && physicsStages.Count == 0
                        && physicsSystems.Count == 0
                        && authoring.Count == 0
                        && registration.Stages.Count > 0
                        && registration.Systems.Count > 0
                        && UnityWorldRegistry.Count == registryBeforeCreate + 1;

                    Add(name, pass,
                        "recipe=" + recipe.ToString()
                        + "; recipeKnown=" + recipeKnown
                        + "; authority=" + authority
                        + "; classify=" + classification
                        + "; undeclared=" + undeclared
                        + "; externalDomains=" + ledger.ExternalDomainCount.ToString(CultureInfo.InvariantCulture)
                        + "; declarations=" + ledger.DeclarationCount.ToString(CultureInfo.InvariantCulture)
                        + "; physicsStages=" + Join(physicsStages)
                        + "; physicsSystems=" + Join(physicsSystems)
                        + "; externallyAuthoredTargets=" + Join(authoring)
                        + "; stages=" + registration.Stages.Count.ToString(CultureInfo.InvariantCulture)
                        + "; systems=" + registration.Systems.Count.ToString(CultureInfo.InvariantCulture)
                        + "; worlds=" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 11. teardown

            /// <summary>
            /// Every adapter lease participates in the world lifecycle: the frame retires its asset leases and views,
            /// the registry forgets it, the time driver is cleared, and the world stops with no outstanding job, no
            /// retained resource and the owned-world registry back at its pre-create baseline (P-048, TEST-015,
            /// TEST-018).
            /// </summary>
            private void TearDownAdaptersAndTheWorld()
            {
                const string name = "gc019-adapter-teardown-participates-in-lifecycle";
                try
                {
                    if (host == null || time == null)
                    {
                        Add(name, false, "no world");
                        return;
                    }

                    ulong idleSteps = PumpIdleFrames();

                    AdapterTeardownReport teardown = frame != null
                        ? frame.Retire()
                        : new AdapterTeardownReport(null, 0, "the run never registered an adapter frame");
                    bool unregistered = AdapterFrameRegistry.Unregister(host.World);
                    IAdapterFrame? registered = null;
                    bool frameGone = !AdapterFrameRegistry.TryGet(host.World, out registered)
                        && AdapterFrameRegistry.Count == (framesBeforeRegister < 0 ? 0 : framesBeforeRegister);

                    time.Clear(out int discardedCommands, out int pendingWakes);
                    _ = discardedCommands;
                    _ = pendingWakes;

                    int registryBeforeStop = UnityWorldRegistry.Count;
                    OperationResult stop = host.Stop(
                        NextOperation(host.World), "gc-019 adapter gate teardown");
                    UnityWorldHost stopped = host;
                    stopped.Dispose();

                    // The genre's stage runtime leaves with its world, so a process that runs both catalogs holds
                    // only the modules of live worlds (the genre's own teardown detaches its module the same way).
                    stageRuntime?.Dispose();
                    stageRuntime = null;

                    int outstanding = stopped.Ledger.OutstandingJobCount;
                    int retained = stopped.Ledger.RetainedResourceCount;
                    int registryAfter = UnityWorldRegistry.Count;

                    bool pass = idleSteps == 0UL
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && unregistered
                        && frameGone
                        && teardown.ViewsDestroyed == 2
                        && teardown.Assets != null
                        && teardown.Assets.AllReleased
                        && assets != null
                        && assets.IsRetired
                        && backend != null
                        && backend.OutstandingLoadCount == 0
                        && views != null
                        && views.LiveViewCount == 0
                        && binder != null
                        && binder.LiveViewCount == 0
                        && RetainedLeaseCountOf(providerInstance) == 0
                        && outstanding == 0
                        && retained == 0
                        && registryAfter == registryBeforeCreate
                        && registryBeforeStop == registryAfter + 1;

                    Add(name, pass,
                        "idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; lifecycle=" + stopped.Lifecycle
                        + "; adapterTeardown=" + teardown.ToString()
                        + "; unregistered=" + unregistered
                        + "; frames=" + AdapterFrameRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; backendOutstanding=" + (backend != null ? backend.OutstandingLoadCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; assetsRetired=" + (assets != null && assets.IsRetired)
                        + "; liveViews=" + (views != null ? views.LiveViewCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; binderViews=" + (binder != null ? binder.LiveViewCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; retainedLeasesOfTheInstallation=" + RetainedLeaseCountOf(providerInstance).ToString(CultureInfo.InvariantCulture)
                        + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeCreate=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeStop=" + registryBeforeStop.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ the control lane

            private DerivedAssemblyOutcome LastOutcome { get; set; } = DerivedAssemblyOutcome.Refused;

            private DerivationResult? LastDerivation { get; set; }

            private InvalidationClosureResult? LastInvalidation { get; set; }

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has one
            /// publication series, so an edit the world does not answer would leave the lane one publication ahead and
            /// every later adoption would be refused as stale: the two halves are always done together, and a
            /// `NoTargetChange` derivation is answered with the unchanged assembly so the counters stay joined.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (!ApplyEdit(payload, label, out DerivedAssemblyReport report))
                {
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange
                    && !PublishUnchangedAssembly(NextOperation(host!.World)))
                {
                    return false;
                }

                return NotePublication(label);
            }

            private bool ApplyEdit(CompositionEditPayload payload, string label, out DerivedAssemblyReport report)
            {
                report = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
                LastOutcome = DerivedAssemblyOutcome.Refused;
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = "the world or its pipeline is missing";
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(
                    payload, NextOperation(host.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    lastFailure = label + ": the edit was refused by the lane (" + admission.Kind + "/"
                        + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = label + ": the publication was refused ("
                        + (published.Count > 0 ? published[0].Outcome.ToString() + "/" + published[0].Code : "none")
                        + ")";
                    return false;
                }

                report = pipeline.PublishDerived(NextOperation(host.World));
                LastOutcome = report.Outcome;
                LastDerivation = report.Derivation;
                LastInvalidation = report.Invalidation;
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = label + ": the world refused the assembly: " + report.Describe();
                    return false;
                }

                return true;
            }

            private bool PublishUnchangedAssembly(OperationId operation)
            {
                if (publisher == null || lane == null)
                {
                    lastFailure = "no publisher";
                    return false;
                }

                AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                    operation, lane.Committed.Revision, lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    lastFailure = "the unchanged assembly publication was refused: " + unchanged.Detail;
                    return false;
                }

                return NotePublication("unchanged");
            }

            /// <summary>
            /// One O-06/O-04 composition edit for one installation. The subject plus the installation identity is the
            /// whole payload a suspend or a resume carries (the same shape the gameplay lifecycle payload builders
            /// use), so the sequence can retire and restore one activation without knowing the genre.
            /// </summary>
            private static CompositionEditPayload LifecycleEdit(CompositionEditSubject subject, PluginInstanceId instance) =>
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

            // ------------------------------------------------------------------ readings

            private IReadOnlyList<DerivationTarget> TargetView()
            {
                if (targets == null)
                {
                    return Array.Empty<DerivationTarget>();
                }

                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            /// <summary>Counts one publication and immediately checks P-006's one-series invariant (P-006).</summary>
            private bool NotePublication(string label)
            {
                publications++;
                bool joined = MatchesPublishedAssembly();
                if (!joined)
                {
                    counterMismatches++;
                    if (counterMismatches == 1)
                    {
                        firstMismatch = label + ": " + PublishedStateText();
                    }
                }

                return true;
            }

            private bool MatchesPublishedAssembly()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            private string PublishedStateText()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return "<no-world>";
                }

                return "lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + ",assembly=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + publisher.Published.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + ",rows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                    + ",worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture);
            }

            private ulong PumpIdleFrames()
            {
                if (time == null)
                {
                    return 0UL;
                }

                ulong committed = 0UL;
                for (int i = 0; i < IdlePumpFrames; i++)
                {
                    committed += time.PumpFrame(IdlePumpTicks).StepsCommitted;
                }

                return committed;
            }

            /// <summary>
            /// The world's committed scope tree plus the committed scope of every family view target: what "composition
            /// did not move" is compared against, so a reparent that only renamed a Transform cannot pass by comparing
            /// one field the adapter wrote itself (P-010).
            /// </summary>
            private string ScopeFingerprint()
            {
                if (lane == null)
                {
                    return "<no-lane>";
                }

                var text = new StringBuilder();
                IReadOnlyList<ScopeRecord> scopes = lane.Committed.Scopes.Scopes;
                text.Append("scopes=").Append(scopes.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < scopes.Count; i++)
                {
                    text.Append(';').Append(scopes[i].Scope.ToString())
                        .Append('<').Append(scopes[i].Parent.ToString())
                        .Append('@').Append(scopes[i].Depth.ToString(CultureInfo.InvariantCulture));
                    IReadOnlyList<PluginInstanceId> installs = lane.Committed.InstallsAt(scopes[i].Scope);
                    for (int n = 0; n < installs.Count; n++)
                    {
                        text.Append('+').Append(installs[n].ToString());
                    }
                }

                if (targets != null)
                {
                    for (int i = 0; i < family.ViewTargets.Count; i++)
                    {
                        TargetId target = family.ViewTargets[i];
                        text.Append('|').Append(target.ToString()).Append(':');
                        text.Append(targets.TryGet(target, out LiveTarget live)
                            ? live.Scope.ToString()
                            : "<not-live>");
                    }
                }

                return text.ToString();
            }

            /// <summary>The published rows of every committed target, as one comparable text (P-017).</summary>
            private string PublishedRowsFingerprint()
            {
                if (publisher == null)
                {
                    return "<no-publisher>";
                }

                IReadOnlyList<TargetId> publishedTargets = publisher.Published.Targets;
                var text = new StringBuilder();
                text.Append("targets=").Append(publishedTargets.Count.ToString(CultureInfo.InvariantCulture));
                for (int t = 0; t < publishedTargets.Count; t++)
                {
                    TargetId target = publishedTargets[t];
                    IReadOnlyList<TargetBindingRow> rows = publisher.Published.Bindings.BindingsOf(target);
                    text.Append(';').Append(target.ToString()).Append('=').Append(rows.Count.ToString(CultureInfo.InvariantCulture));
                    for (int r = 0; r < rows.Count; r++)
                    {
                        text.Append('|').Append(rows[r].Capability.ToString())
                            .Append('#').Append(rows[r].OutputSlot.ToString(CultureInfo.InvariantCulture))
                            .Append(':').Append(rows[r].Value.ToString(CultureInfo.InvariantCulture));
                    }
                }

                return text.ToString();
            }

            /// <summary>The published binding rows of one target, as one comparable text.</summary>
            private string BindingRowFingerprint(TargetId target)
            {
                if (publisher == null)
                {
                    return "<no-publisher>";
                }

                IReadOnlyList<TargetBindingRow> rows = publisher.Published.Bindings.BindingsOf(target);
                var text = new StringBuilder();
                text.Append(rows.Count.ToString(CultureInfo.InvariantCulture));
                for (int r = 0; r < rows.Count; r++)
                {
                    text.Append('|').Append(rows[r].Capability.ToString())
                        .Append('#').Append(rows[r].OutputSlot.ToString(CultureInfo.InvariantCulture))
                        .Append(':').Append(rows[r].Value.ToString(CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }

            /// <summary>
            /// The family's own declared live slot on the target its own command addresses, as a diagnostic: it is the
            /// genre's observable, read through `IGc013Family` (owner/slot identity and the live slot copy) rather than
            /// through a genre-specific reader, so the adapter gate can report whether the command's domain moved
            /// without claiming a fact the interface does not promise (P-034).
            /// </summary>
            private string DescribeFamilyState()
            {
                if (seeder == null || targets == null)
                {
                    return "<no-seeder>";
                }

                TargetId target = family.CommandTarget;
                if (!targets.Contains(target))
                {
                    return "<target-not-live>";
                }

                IReadOnlyList<LiveSlotState> slots = seeder.ReadLiveSlots(new[] { target });
                int value = int.MinValue;
                uint version = 0U;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Owner.Equals(family.MutableOwner) && slots[i].Slot.Slot.Equals(family.MutableSlot))
                    {
                        value = slots[i].Value;
                        version = slots[i].SchemaVersion;
                        break;
                    }
                }

                return target.ToString() + ":" + (value == int.MinValue ? "<missing>" : value.ToString(CultureInfo.InvariantCulture))
                    + "@" + version.ToString(CultureInfo.InvariantCulture)
                    + " seeded=" + family.MutableValue.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>
            /// True when every published row of one view target was applied to its view with the same value, and the
            /// view's recorded composition parent is the scope the live target index reports (P-045, P-010).
            /// </summary>
            private bool PresentedMatchesPublished(TargetId target, SnapshotToken token, out string dissent)
            {
                dissent = string.Empty;
                if (publisher == null || binder == null || targets == null)
                {
                    dissent = target.ToString() + ":no-adapter";
                    return false;
                }

                IReadOnlyList<TargetBindingRow> publishedRows = publisher.Published.Bindings.BindingsOf(target);
                IReadOnlyList<CapabilityBinding> liveRows = publisher.ReadBindingRows(target);
                int activeLiveRows = CountActive(liveRows);
                PresentationApplyData? applied = LastApplyOf(target, 0U);
                bool liveKnown = targets.TryGet(target, out LiveTarget live);

                if (publishedRows.Count == 0)
                {
                    dissent = target.ToString() + ":no-published-row";
                    return false;
                }

                if (applied == null)
                {
                    dissent = target.ToString() + ":not-applied";
                    return false;
                }

                if (!applied.Token.Equals(token))
                {
                    dissent = target.ToString() + ":token=" + applied.Token.ToString();
                    return false;
                }

                if (!liveKnown || !applied.CompositionParent.Equals(live.Scope))
                {
                    dissent = target.ToString() + ":compositionParent=" + applied.CompositionParent.ToString()
                        + " (live " + (liveKnown ? live.Scope.ToString() : "<not-live>") + ")";
                    return false;
                }

                // The published assembly is the complete committed observation image (P-030): its rows are the live
                // *active* rows, and a dormant row retained by a last-support policy is not one of them (P-032).
                if (applied.Fields.Count != publishedRows.Count || activeLiveRows != publishedRows.Count)
                {
                    dissent = target.ToString() + ":fields=" + applied.Fields.Count.ToString(CultureInfo.InvariantCulture)
                        + ",published=" + publishedRows.Count.ToString(CultureInfo.InvariantCulture)
                        + ",liveActive=" + activeLiveRows.ToString(CultureInfo.InvariantCulture)
                        + ",live=" + liveRows.Count.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                for (int r = 0; r < publishedRows.Count; r++)
                {
                    TargetBindingRow row = publishedRows[r];
                    bool presented = TryFindPresented(applied.Fields, row.Capability, row.OutputSlot, out int presentedValue);
                    bool liveMatch = TryFindLive(liveRows, row.Capability, row.OutputSlot, out int liveValue);
                    if (!presented || presentedValue != row.Value || !liveMatch || liveValue != row.Value)
                    {
                        dissent = target.ToString() + ":" + row.ToString()
                            + " published=" + row.Value.ToString(CultureInfo.InvariantCulture)
                            + " presented=" + (presented ? presentedValue.ToString(CultureInfo.InvariantCulture) : "<missing>")
                            + " live=" + (liveMatch ? liveValue.ToString(CultureInfo.InvariantCulture) : "<missing>");
                        return false;
                    }
                }

                return true;
            }

            private PresentationApplyData? LastApplyOf(TargetId target, uint slot)
            {
                if (binder == null)
                {
                    return null;
                }

                for (int i = binder.Applies.Count - 1; i >= 0; i--)
                {
                    PresentationApplyData data = binder.Applies[i];
                    if (data.Key.Target.Equals(target) && data.Key.Slot == slot)
                    {
                        return data;
                    }
                }

                return null;
            }

            private static bool TryFindPresented(
                IReadOnlyList<PresentationField> fields,
                CapabilityId capability,
                uint outputSlot,
                out int value)
            {
                value = 0;
                for (int i = 0; i < fields.Count; i++)
                {
                    if (fields[i].Capability.Equals(capability) && fields[i].OutputSlot == outputSlot)
                    {
                        value = fields[i].Value;
                        return true;
                    }
                }

                return false;
            }

            private static bool TryFindLive(
                IReadOnlyList<CapabilityBinding> rows,
                CapabilityId capability,
                uint outputSlot,
                out int value)
            {
                value = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsActive && rows[i].Capability.Equals(capability) && rows[i].OutputSlot == outputSlot)
                    {
                        value = rows[i].Value;
                        return true;
                    }
                }

                return false;
            }

            private static int CountActive(IReadOnlyList<CapabilityBinding> rows)
            {
                int count = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsActive)
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>One sample stamp of this scenario's own headless input source (P-004, P-050).</summary>
            private InputSourceStamp MintStamp(WorldId world, ulong sequence) =>
                new InputSourceStamp(
                    world,
                    new Id128(family.SessionSalt, InputSourceLow),
                    sequence,
                    host != null ? host.CurrentStep : LogicalStepId.Zero,
                    host != null ? host.CurrentEpoch : AssemblyEpoch.Zero);

            /// <summary>Stable resource identity of one requested lease; the ordinal keeps the keys distinct (P-008).</summary>
            private ResourceKey LeaseResource(ulong ordinal) =>
                new ResourceKey(new Id128(family.SessionSalt, LeaseResourceLow + ordinal));

            /// <summary>
            /// The activation stamp of the provider installation as the lane's committed composition holds it, i.e. the
            /// generation and epoch the world's callback gate currently accepts (P-005, P-007).
            /// </summary>
            private bool TryLiveToken(out AsyncWorkToken token, uint workOrdinal, out string detail)
            {
                token = default(AsyncWorkToken);
                detail = string.Empty;
                if (host == null || lane == null || !providerInstanceKnown)
                {
                    detail = "no world, lane or installation";
                    return false;
                }

                if (!lane.Committed.TryGetInstall(providerInstance, out InstallEntry? entry) || entry == null)
                {
                    detail = "the installation is not in the committed composition";
                    return false;
                }

                token = new AsyncWorkToken(
                    NextOperation(host.World),
                    providerInstance,
                    entry.Record.Generation,
                    entry.Record.ActivationEpoch,
                    workOrdinal);
                detail = entry.State.ToString();
                return true;
            }

            /// <summary>
            /// The same stamp as <see cref="TryLiveToken"/>, for a completion that arrives after the activation was
            /// retired: the values are read from the committed record, so the token names exactly the activation the
            /// gate has already forgotten (P-007).
            /// </summary>
            private bool TryRetiredToken(out AsyncWorkToken token, uint workOrdinal, out string detail) =>
                TryLiveToken(out token, workOrdinal, out detail);

            /// <summary>One ledger record's state, or null when the ledger has no such record (P-048).</summary>
            private ResourceRetirementState? LedgerStateOf(Id128 resourceId)
            {
                if (host == null)
                {
                    return null;
                }

                WorldResourceLedgerSnapshot snapshot = host.ReadResourceLedger();
                for (int i = 0; i < snapshot.Resources.Count; i++)
                {
                    if (snapshot.Resources[i].ResourceId.Equals(resourceId))
                    {
                        return snapshot.Resources[i].State;
                    }
                }

                return null;
            }

            /// <summary>True when the table still exposes a payload for one lease; a discarded one exposes none.</summary>
            private bool IsPayloadReadable(Id128 leaseId)
            {
                if (assets == null)
                {
                    return false;
                }

                FrozenPayload? payload = null;
                return assets.TryReadPayload(leaseId, out payload) && payload != null;
            }

            private bool IsRetainedInLedger(Id128 resourceId)
            {
                if (host == null)
                {
                    return false;
                }

                WorldResourceLedgerSnapshot snapshot = host.ReadResourceLedger();
                for (int i = 0; i < snapshot.Resources.Count; i++)
                {
                    if (snapshot.Resources[i].ResourceId.Equals(resourceId))
                    {
                        return snapshot.Resources[i].IsRetained;
                    }
                }

                return false;
            }

            /// <summary>World-ledger records still retained for one installation (P-048, TEST-015).</summary>
            private int RetainedLeaseCountOf(PluginInstanceId instance)
            {
                if (host == null || instance.Value.IsDefault)
                {
                    return 0;
                }

                int count = 0;
                WorldResourceLedgerSnapshot snapshot = host.ReadResourceLedger();
                for (int i = 0; i < snapshot.Resources.Count; i++)
                {
                    WorldResourceRecord record = snapshot.Resources[i];
                    if (record.Instance.Value.Equals(instance.Value) && record.IsRetained)
                    {
                        count++;
                    }
                }

                return count;
            }

            private string StateOf(PluginInstanceId instance)
            {
                if (lane == null
                    || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry)
                    || entry == null)
                {
                    return "<absent>";
                }

                return entry.State.ToString();
            }

            /// <summary>True when a generated stage or system name names an engine physics module (P-059).</summary>
            private static bool NamesPhysics(string diagnosticName)
            {
                if (string.IsNullOrEmpty(diagnosticName))
                {
                    return false;
                }

                return diagnosticName.IndexOf("phys", StringComparison.OrdinalIgnoreCase) >= 0
                    || diagnosticName.IndexOf("rigid", StringComparison.OrdinalIgnoreCase) >= 0;
            }

            private static bool Contains(IReadOnlyList<Id128> ids, Id128 candidate)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i].Equals(candidate))
                    {
                        return true;
                    }
                }

                return false;
            }

            // ------------------------------------------------------------------ helpers

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            /// <summary>
            /// Records one observation. The family qualification is applied here, at the single recording point, so
            /// every step method passes the bare name from <see cref="ObservationNames"/> and the recorded sequence
            /// is exactly the qualified list: a step that qualified its own name (or forgot to) would change the digest
            /// the EditMode suite asserts on.
            /// </summary>
            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new Gc019Step(family.Label + "/" + bareName, passed, detail ?? string.Empty));
            }

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string Join(IReadOnlyList<string> values)
            {
                if (values.Count == 0)
                {
                    return "<none>";
                }

                var array = new string[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    array[i] = values[i];
                }

                return string.Join(",", array);
            }

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
