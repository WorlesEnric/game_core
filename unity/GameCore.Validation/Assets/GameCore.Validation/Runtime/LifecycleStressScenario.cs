// GameCore.Validation.ProbeHost - the GC-022 Unity-world lifecycle-stress runtime scenario.
//
// The task sentence this file implements, verbatim from `docs/game-core/09-implementation-guide.md` (GC-022):
//
//   "Run 1,000 mount/unmount cycles, 100 delayed completions, stalled jobs, throwing disposers and required-provider
//    churn. Exercise domain reload on/off plus scene reload settings, stop/recreate and headless cleanup. Trace every
//    acquisition to retirement or quarantine."
//   "Acceptance: Lease/system/callback/view counts return to baseline after bounded retention; no stale result writes
//    authority. Stalled jobs retain reachable buffers; independent cleanup continues after a disposer error. Loop
//    nodes/subscriptions do not accumulate."
//
// One runner, two family adapters (see `LifecycleStressFamily.cs`), and this file owns the order and nothing else. It
// builds exactly one *real* world per run - through the same sequence every earlier gate uses, so nothing here is a
// model of the kernel - and then drives the twelve observations of Contract B over it:
//
//   1. the world baseline, recorded from the host's own counters;
//   2. the counted mount/unmount cycles, each one a real lane publication with a real staged lease and a fresh
//      plugin-instance identity, driven through `LifecycleController`/`InstallationLifecycleCoordinator`;
//   3. the counters of the composition lane and of the world ledger, back at their baselines;
//   4. the last cycle's acquisitions traced to retirement in both the world's and the lane's ledgers;
//   5. one hundred completions stamped for an activation that no longer exists - fifty before the same identity is
//      remounted, fifty after - every one of them discarded and none of them moving a step, an epoch or a revision;
//   6. a stalled job that pins a staged lease *and* a world-side buffer, the blocked teardown that retains both, and
//      the explicit completion-plus-release that retires them exactly once;
//   7. a disposer that throws once: the failed reference stays retained and quarantined, every other lease of that
//      publication is still retired, and the explicit later release retires it exactly once;
//   8. a required-service consumer that waits in the same publication its provider leaves and is resumed with its
//      rows by the provider's return, ten times over;
//   9. the player-loop node count and install count of the whole process, unchanged by the stress;
//  10. the world's own teardown, back at the registry and ledger baselines;
//  11. every incarnation this run created, distinct and non-default;
//  12. the digest over the eleven observations above plus this one, equal to the family's frozen literal.
//
// Every check is a read of a module's own report; a check that raises is recorded as a failing step carrying the
// exception text, never swallowed into a passing step (P-060).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning;
using GameCore.Rules.Narrative;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.Time;
using Unity.Jobs;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named lifecycle-stress observation: what was checked and the values it was checked from.</summary>
    public sealed class LifecycleStressStep
    {
        public LifecycleStressStep(string name, bool passed, string detail)
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
    /// Full result of one lifecycle-stress run: the named observations plus one digest over them, computed over the
    /// canonical `name=pass|fail` lines with the same digest function the narrative trace, the GC-013 result and every
    /// earlier gate use. A run that records a different set of observations (or a failing one) therefore cannot report
    /// the digest the observation table implies (P-008).
    /// </summary>
    public sealed class LifecycleStressResult
    {
        public LifecycleStressResult(string label, IReadOnlyList<LifecycleStressStep> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        /// <summary>The family label every observation name of this run is qualified with.</summary>
        public string Label { get; }

        /// <summary>The named observations, in execution order, qualified with <see cref="Label"/>.</summary>
        public IReadOnlyList<LifecycleStressStep> Steps { get; }

        /// <summary>Canonical digest over this run's observation names and pass flags.</summary>
        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }

        public override string ToString() => Describe();
    }

    /// <summary>Runs the GC-022 Unity-world lifecycle stress over one family and one catalog.</summary>
    public static class LifecycleStressScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling gates use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Environment variable that overrides the counted cycles (a positive integer).</summary>
        public const string CycleCountVariable = "GC_LIFECYCLE_STRESS_CYCLES";

        /// <summary>Cycles one run executes unless <see cref="CycleCountVariable"/> overrides them.</summary>
        public const int DefaultCycleCount = 1000;

        /// <summary>Cycles the fixture-catalog run executes: it proves the second catalog, it does not repeat 1,000.</summary>
        public const int FixtureCycleCount = 3;

        /// <summary>Delayed completions observation 5 submits: half retired, half after the remount.</summary>
        public const int DelayedCompletionCount = 100;

        private const int DelayedCompletionHalf = 50;

        /// <summary>Provider removal/return rounds observation 8 executes; the contract asks for at least ten.</summary>
        public const int RequiredProviderChurnRounds = 10;

        /// <summary>Leases observation 7 stages for one installation; the last of them is scripted to fail once.</summary>
        private const int ThrowingDisposerLeaseCount = 3;

        /// <summary>Staged-resource and plan budgets of this run's lane, as the family gates declare them.</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>
        /// The twelve observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite and the player probe
        /// instead of shrinking them silently (Contract B).
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "lifecycle-stress-world-baseline",
            "lifecycle-stress-cycles-complete",
            "lifecycle-stress-counters-return-to-baseline",
            "lifecycle-stress-acquisitions-traced-to-retirement",
            "lifecycle-stress-delayed-completions-are-discarded",
            "lifecycle-stress-stalled-job-retains-buffers",
            "lifecycle-stress-throwing-disposer-keeps-cleanup-going",
            "lifecycle-stress-required-provider-churn",
            "lifecycle-stress-loop-nodes-do-not-accumulate",
            "lifecycle-stress-registry-returns-to-baseline",
            "lifecycle-stress-incarnations-are-fresh",
            "lifecycle-stress-repeatable-digest",
        };

        private static readonly List<string> FamilyLabels = new List<string> { "narrative", "cards" };

        /// <summary>
        /// Cycles one counted run executes, resolved once from <see cref="CycleCountVariable"/> (a positive integer)
        /// and otherwise <see cref="DefaultCycleCount"/>.
        /// </summary>
        public static int CycleCount { get; } = ResolveCycleCount();

        /// <summary>The family labels this scenario accepts, in the order the contract fixes them.</summary>
        public static IReadOnlyList<string> Families() => FamilyLabels;

        /// <summary>The observation names of one run, qualified with the family label, in order.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>
        /// Builds one real world of the named family over the committed generated catalog and runs the counted
        /// mount/unmount cycles over it. An unknown label is refused naming the accepted labels.
        /// </summary>
        public static LifecycleStressResult RunGeneratedCatalog(string family)
        {
            switch (RequireFamily(family))
            {
                case "narrative":
                    return Gc013NarrativeHost.RunGeneratedCatalogLifecycleStress();
                default:
                    return Gc013CardsHost.RunGeneratedCatalogLifecycleStress();
            }
        }

        /// <summary>
        /// Builds one real world of the named family over the fixture declaration identity set and runs a short cycle
        /// count over it, so the second catalog is proven without repeating the 1,000.
        /// </summary>
        public static LifecycleStressResult RunFixtureCatalog(string family)
        {
            switch (RequireFamily(family))
            {
                case "narrative":
                    return Gc013NarrativeHost.RunFixtureCatalogLifecycleStress();
                default:
                    return Gc013CardsHost.RunFixtureCatalogLifecycleStress();
            }
        }

        /// <summary>Runs the stress over one family, over the generated or the fixture catalog's world.</summary>
        public static LifecycleStressResult Run(ILifecycleStressFamily family, bool fixtureRun)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family, fixtureRun, fixtureRun ? FixtureCycleCount : CycleCount).Run();
        }

        private static int ResolveCycleCount()
        {
            string? text = Environment.GetEnvironmentVariable(CycleCountVariable);
            if (text != null
                && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && parsed > 0)
            {
                return parsed;
            }

            return DefaultCycleCount;
        }

        private static string RequireFamily(string family)
        {
            for (int i = 0; i < FamilyLabels.Count; i++)
            {
                if (string.Equals(family, FamilyLabels[i], StringComparison.Ordinal))
                {
                    return FamilyLabels[i];
                }
            }

            throw new ArgumentException(
                "Unknown lifecycle-stress family label '" + (family ?? "<null>") + "'; the accepted labels are "
                + string.Join(", ", FamilyLabels.ToArray()) + ".", nameof(family));
        }

        /// <summary>
        /// The stress executor: one world, the twelve observations in order, and the teardown that returns the
        /// process-wide registries to their baselines even when a step failed before its own teardown ran. It is a
        /// runner, not a module: it owns no protocol behaviour and never re-implements a check a module performs (it
        /// asserts the module's own outcome).
        /// </summary>
        private sealed class Executor
        {
            /// <summary>Identity salt of the no-row carriers that consume an adopted-and-pending pair (P-006, P-024).</summary>
            private static readonly Id128 CarrierSalt = new Id128(0x4C53545243415252UL, 1UL);

            private readonly ILifecycleStressFamily family;
            private readonly bool fixtureRun;
            private readonly int cycles;
            private readonly List<LifecycleStressStep> steps = new List<LifecycleStressStep>();
            private readonly IdSequence sessionSequence = new IdSequence(0x4C535452455353UL);
            private readonly IdSequence leaseSequence = new IdSequence(0x4C5354524C454153UL);
            private readonly IdSequence jobSequence = new IdSequence(0x4C5354524A4F4253UL);
            private readonly List<WorldId> worlds = new List<WorldId>();
            private readonly List<PluginInstanceId> cycleInstances = new List<PluginInstanceId>();
            private readonly List<PluginInstanceId> incarnationInstances = new List<PluginInstanceId>();
            private readonly List<string> incarnationKeys = new List<string>();
            private readonly List<string> incarnationText = new List<string>();

            private UnityWorldHost? host;
            private TargetRegistry? registry;
            private AssemblyPublisher? publisher;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private CompositionHost? lane;
            private WorldCompositionBridge? bridge;
            private DerivedAssemblyPipeline? pipeline;
            private WorldTimeDriver? time;
            private LifecycleController? controller;
            private LifecycleStressManifestSource? manifests;
            private LifecycleStressResourceFactory? resources;
            private Gc019StageRuntime? stageRuntime;
            private PipelineDescriptorReport? descriptorReport;

            private WorldId world;
            private bool worldStopped;
            private string buildFailure = string.Empty;
            private ulong operationSequence;
            private ulong instanceOrdinal;
            private int carrierOrdinal;
            private int registryBaseline;
            private int loopNodeBaseline;
            private int loopInstallCountBaseline;
            private int worldRetainedBaseline;
            private int worldOutstandingBaseline;
            private int worldBalanceBaseline;
            private ulong retiredGeneration;
            private ulong remountGeneration;

            public Executor(ILifecycleStressFamily family, bool fixtureRun, int cycles)
            {
                this.family = family;
                this.fixtureRun = fixtureRun;
                this.cycles = cycles;
            }

            public LifecycleStressResult Run()
            {
                registryBaseline = UnityWorldRegistry.Count;
                try
                {
                    WorldBaseline();
                    CyclesComplete();
                    CountersReturnToBaseline();
                    AcquisitionsTracedToRetirement();
                    DelayedCompletionsAreDiscarded();
                    StalledJobRetainsBuffers();
                    ThrowingDisposerKeepsCleanupGoing();
                    RequiredProviderChurn();
                    LoopNodesDoNotAccumulate();
                    RegistryReturnsToBaseline();
                    IncarnationsAreFresh();
                    RepeatableDigest();
                }
                finally
                {
                    // A failed step must not leave a world behind: the EditMode suite asserts the registry baseline.
                    TearDownSafely();
                    EnsureEveryObservationIsRecorded();
                }

                return new LifecycleStressResult(family.Label, steps);
            }

            // ================================================================== 1. the one real world

            /// <summary>
            /// Builds the one real world this run stresses, in the fixed order the task sentence fixes: the family's
            /// compiled ownership/schedule, its live targets, the control lane over a manifest source that resolves
            /// the family's catalog plus this run's four declarations, the composition bridge, the family's own
            /// seeding and module, the derived-assembly pipeline, the command-driven time driver, and - before the
            /// first publication, which is the only moment `InstallationLifecycleCoordinator` accepts a world binding
            /// (P-030) - the lifecycle controller. Nothing here is a model of the kernel: every object is the
            /// production module the earlier gates run (P-002, P-042).
            /// </summary>
            private void WorldBaseline()
            {
                const string name = "lifecycle-stress-world-baseline";
                try
                {
                    if (!BuildWorld())
                    {
                        Add(name, false, buildFailure);
                        return;
                    }

                    loopNodeBaseline = GameCorePlayerLoopInstaller.CountInstalledNodes();
                    loopInstallCountBaseline = GameCorePlayerLoopInstaller.InstallCount;
                    worldRetainedBaseline = Host.Ledger.RetainedResourceCount;
                    worldOutstandingBaseline = Host.Ledger.OutstandingJobCount;
                    worldBalanceBaseline = Host.Ledger.AcquireCount - Host.Ledger.RetireCount;

                    bool pass = Host.Lifecycle == WorldLifecycleState.Running
                        && UnityWorldRegistry.Count == registryBaseline + 1
                        && Host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && Host.CurrentStep.Equals(LogicalStepId.Zero)
                        && stageRuntime != null
                        && Lane.Committed.Scopes.Count > 0
                        && Lane.Committed.Mode == PropagationMode.Automatic
                        && worlds.Count == 1;

                    Add(name, pass,
                        "session=" + Host.World.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + Text(registryBaseline)
                        + "; registryAfter=" + Text(UnityWorldRegistry.Count)
                        + "; lifecycle=" + Host.Lifecycle
                        + "; epoch=" + Text(Host.CurrentEpoch.Value)
                        + "; step=" + Text(Host.CurrentStep.Value)
                        + "; scopes=" + Text(Lane.Committed.Scopes.Count)
                        + "; installs=" + Text(Lane.Committed.Installs.Count)
                        + "; laneRevision=" + Text(Lane.Committed.Revision.Value)
                        + "; loopNodes=" + Text(loopNodeBaseline)
                        + "; loopInstalls=" + Text(loopInstallCountBaseline)
                        + "; ledgerRetained=" + Text(worldRetainedBaseline)
                        + "; ledgerOutstanding=" + Text(worldOutstandingBaseline)
                        + "; ledgerBalance=" + Text(worldBalanceBaseline)
                        + "; stressInstalls=" + Text(Lane.Committed.Installs.Count)
                        + DescribeFailures());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            private bool BuildWorld()
            {
                try
                {
                    descriptorReport = family.CompilePipeline();
                    if (!descriptorReport.Succeeded
                        || descriptorReport.Descriptor == null
                        || descriptorReport.Adaptation == null
                        || descriptorReport.Compilation == null)
                    {
                        buildFailure = "the ownership and schedule pipeline refused: " + descriptorReport.Describe();
                        return false;
                    }

                    world = new WorldId(sessionSequence.Next());
                    worlds.Add(world);

                    WorldCreateRequest request = family.CreateRequest(world, NextOperation(world));
                    UnityWorldRegistration registration = family.CreateRegistration(descriptorReport.Adaptation);
                    bool created = UnityWorldRegistry.TryCreate(
                        request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        buildFailure = "world creation failed: " + result.Code + ": " + result.Detail;
                        return false;
                    }

                    registry = new TargetRegistry(world, 32);
                    publisher = new AssemblyPublisher(
                        host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptorReport.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    var catalogSource = new CatalogManifestSource(family.Catalog, family.Declarations);
                    var manifestSource = new LifecycleStressManifestSource(catalogSource);
                    LifecycleStressDeclarations declarations = family.StressDeclarations;
                    for (int i = 0; i < declarations.All.Count; i++)
                    {
                        manifestSource.Add(declarations.All[i]);
                    }

                    manifests = manifestSource;
                    resources = new LifecycleStressResourceFactory();

                    lane = CompositionHost.CreateDefault(
                        world, family.WorldRootScope, manifestSource, resources, family.LaneSeed);
                    bridge = new WorldCompositionBridge(host, lane, publisher);

                    if (!family.SeedTargets(new Gc013WorldContext(host, targets, seeder)))
                    {
                        buildFailure = "the family refused to seed its declared targets";
                        return false;
                    }

                    // The genre's own stage runtime, attached exactly where its own scenario attaches it: without it
                    // the genre's systems resolve no module (P-043). It maps every live target of the world, so it
                    // runs after seeding and before the lane's first publication.
                    stageRuntime = family.AttachStageRuntime(host, descriptorReport, targets, seeder);

                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        family.CreateValues(),
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    // The controller owns the one lifecycle binding of this world, and a lane that has already
                    // published cannot swap its binding (P-030), so it is built before the first mount.
                    controller = new LifecycleController(host, lane, publisher, pipeline);

                    return host.Lifecycle == WorldLifecycleState.Running && UnityWorldRegistry.Count == registryBaseline + 1;
                }
                catch (Exception exception)
                {
                    buildFailure = DescribeException(exception);
                    return false;
                }
            }

            // ================================================================== 2. the counted cycles

            /// <summary>
            /// Runs the counted mount/unmount cycles of one real installation on the one live world: each cycle mounts
            /// a fresh plugin-instance identity at the world root through the lane, stages one real lease for that
            /// publication, publishes it, asserts the installation published `Active` with attributed rows, then
            /// unmounts the same instance through the family's own payload and the lifecycle controller and asserts
            /// the publication reported the removal, the lease was retired and the installation reached `Disposed`.
            /// A cycle that does not publish is recorded as a failing step naming the cycle index, the diagnostic code
            /// and the detail (P-046, P-048, P-050).
            /// </summary>
            private void CyclesComplete()
            {
                const string name = "lifecycle-stress-cycles-complete";
                int completed = 0;
                int failedCycle = -1;
                string failedCode = "<none>";
                string failure = string.Empty;
                try
                {
                    if (!Attached())
                    {
                        Add(name, false, "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    for (int index = 1; index <= cycles; index++)
                    {
                        PluginInstanceId instance = NextStressInstance();
                        if (!RunCycle(index, instance, out DiagnosticCode code, out string detail))
                        {
                            failedCycle = index;
                            failedCode = DiagnosticCodeText.Of(code);
                            failure = detail;
                            break;
                        }

                        cycleInstances.Add(instance);
                        completed++;
                    }

                    bool pass = failedCycle < 0 && completed == cycles && cycleInstances.Count == cycles;
                    Add(name, pass,
                        "cycles=" + Text(cycles)
                        + "; completed=" + Text(completed)
                        + "; failedCycle=" + Text(failedCycle)
                        + "; failedCode=" + failedCode
                        + "; instances=" + Text(cycleInstances.Count)
                        + "; laneRevision=" + Text(Lane.Committed.Revision.Value)
                        + "; laneEpoch=" + Text(Lane.Committed.Epoch.Value)
                        + (failure.Length == 0 ? string.Empty : "; failure=" + failure));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            private bool RunCycle(int index, PluginInstanceId instance, out DiagnosticCode code, out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                CompositionHost laneRef = Lane;
                LifecycleController controllerRef = Controller;

                OperationId mountOperation = NextLaneOperation();
                EditAdmission admission = laneRef.SubmitEdit(
                    family.StressMount(family.StressDeclarations.Installation, instance, family.WorldRootScope),
                    mountOperation,
                    laneRef.Committed.Revision);
                if (!admission.Staged)
                {
                    code = admission.Code;
                    detail = "cycle " + Text(index) + ": the mount was refused (" + admission.Kind + "/"
                        + DiagnosticCodeText.Of(admission.Code) + ")";
                    return false;
                }

                ResourceKey leaseKey = new ResourceKey(leaseSequence.Next());
                bool staged = laneRef.StageResource(
                    mountOperation, instance, leaseKey, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode stageCode);
                IReadOnlyList<StagedLease> leases = laneRef.StagedLeases(mountOperation);
                IReadOnlyList<PublishedOperation> published = laneRef.Drain();
                DerivedAssemblyReport derived = Pipeline.PublishDerived(mountOperation);
                CompleteWorldHalf(mountOperation, derived);

                PublishedOperation? mountPublication = PublishedOf(published, mountOperation);
                bool mounted = mountPublication != null
                    && mountPublication.Outcome == Outcome.Published
                    && TryInstallState(instance, out InstallationState mountState)
                    && mountState == InstallationState.Active
                    && controllerRef.Binding.CountAttributedRows(instance) > 0;
                if (!mounted)
                {
                    code = mountPublication != null ? mountPublication.Code : derived.Code;
                    detail = "cycle " + Text(index) + ": the mount did not publish (state=" + StateOf(instance)
                        + "; outcome=" + (mountPublication != null ? mountPublication.Outcome.ToString() : "<none>")
                        + "; leases=" + Text(leases.Count)
                        + "; staged=" + staged + "/" + DiagnosticCodeText.Of(stageCode)
                        + "; derived=" + derived.Outcome + "/" + DiagnosticCodeText.Of(derived.Code)
                        + "; rows=" + Text(SafeAttributedRows(instance)) + ")";
                    return false;
                }

                incarnationInstances.Add(instance);
                incarnationKeys.Add(IncarnationKey(instance));
                incarnationText.Add("cycle" + Text(index) + "=" + DescribeIncarnation(instance));

                // The next cycle's identity is fresh, so the remount is a new installation, and the operation
                // sequence only ever moves forward (P-004, P-005, P-050).
                OperationId unmountOperation = NextLaneOperation();
                LifecycleRequestReport unmountReport = SubmitAndPublish(family.StressUnmount(instance), unmountOperation);
                TeardownReport? teardown = TeardownOf(unmountReport.Lifecycle, instance);
                bool unmounted = unmountReport.Succeeded
                    && TryInstallState(instance, out InstallationState unmountState)
                    && unmountState == InstallationState.Disposed
                    && teardown != null
                    && teardown.Cleanup.Retired.Count == 1
                    && teardown.Retraction.AttributedRows > 0
                    && laneRef.Resources.RetainedCountFor(instance) == 0
                    && laneRef.Resources.LiveLeaseCount == 0;
                if (!unmounted)
                {
                    code = unmountReport.Code;
                    detail = "cycle " + Text(index) + ": the unmount did not settle (state=" + StateOf(instance)
                        + "; succeeded=" + unmountReport.Succeeded
                        + "; retired=" + Text(teardown != null ? teardown.Cleanup.Retired.Count : -1)
                        + "; retractedRows=" + Text(teardown != null ? teardown.Retraction.AttributedRows : -1)
                        + "; retained=" + Text(laneRef.Resources.RetainedCountFor(instance))
                        + "; detail=" + unmountReport.Detail + ")";
                    return false;
                }

                return true;
            }

            // ================================================================== 3. counters back at baseline

            /// <summary>
            /// The composition lane's live counts and the world ledger's counts after the cycles. The two ledger
            /// counters a *world* owns are compared against the baseline the world reported before the cycles (the
            /// host's own storage, identity-index, message-plane and system registrations are acquisitions that live
            /// as long as the world does), and the lane's are asserted zero, because the lane's leases only ever come
            /// from this run's staged publications (P-048).
            /// </summary>
            private void CountersReturnToBaseline()
            {
                const string name = "lifecycle-stress-counters-return-to-baseline";
                try
                {
                    if (!Attached())
                    {
                        Add(name, false, "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    CompositionHost laneRef = Lane;
                    LifecycleController controllerRef = Controller;
                    UnityWorldHost hostRef = Host;

                    int instancesRetained = 0;
                    int instancesMissing = 0;
                    for (int i = 0; i < cycleInstances.Count; i++)
                    {
                        if (!TryInstallState(cycleInstances[i], out InstallationState _))
                        {
                            instancesMissing++;
                        }

                        instancesRetained += laneRef.Resources.RetainedCountFor(cycleInstances[i]);
                    }

                    int balance = hostRef.Ledger.AcquireCount - hostRef.Ledger.RetireCount;
                    bool pass = laneRef.Resources.LiveLeaseCount == 0
                        && laneRef.Resources.RetainedCountFor(cycleInstances.Count > 0
                            ? cycleInstances[cycleInstances.Count - 1]
                            : default(PluginInstanceId)) == 0
                        && instancesRetained == 0
                        && instancesMissing == 0
                        && laneRef.Resources.QuarantinedCount == 0
                        && laneRef.Resources.FailedReleaseCount == 0
                        && laneRef.Resources.RetiredCount == cycleInstances.Count
                        && laneRef.Callbacks.LiveActivationCount == 0
                        && !laneRef.Callbacks.TryGetActivation(cycleInstances.Count > 0
                            ? cycleInstances[cycleInstances.Count - 1]
                            : default(PluginInstanceId), out ActivationStamp _)
                        && controllerRef.JobFence.OutstandingCount == 0
                        && controllerRef.JobFence.CompletionFailureCount == 0
                        && laneRef.Lifecycle.Jobs.OutstandingCount == 0
                        && laneRef.Lifecycle.Quarantine.Count == 0
                        && laneRef.Lifecycle.Quarantine.ExhaustionCount == 0
                        && laneRef.Lifecycle.Quarantine.DuplicateCount == 0
                        && hostRef.Ledger.RetainedResourceCount == worldRetainedBaseline
                        && hostRef.Ledger.OutstandingJobCount == worldOutstandingBaseline
                        && hostRef.Ledger.QuarantinedBytes == 0UL
                        && balance == worldBalanceBaseline;

                    Add(name, pass,
                        "liveLeases=" + Text(laneRef.Resources.LiveLeaseCount)
                        + "; retired=" + Text(laneRef.Resources.RetiredCount) + "@cycles=" + Text(cycleInstances.Count)
                        + "; instancesRetained=" + Text(instancesRetained)
                        + "; instancesMissing=" + Text(instancesMissing)
                        + "; quarantined=" + Text(laneRef.Resources.QuarantinedCount)
                        + "; failedReleases=" + Text(laneRef.Resources.FailedReleaseCount)
                        + "; liveActivations=" + Text(laneRef.Callbacks.LiveActivationCount)
                        + "; fenceOutstanding=" + Text(controllerRef.JobFence.OutstandingCount)
                        + "; fenceFailures=" + Text(controllerRef.JobFence.CompletionFailureCount)
                        + "; laneJobsOutstanding=" + Text(laneRef.Lifecycle.Jobs.OutstandingCount)
                        + "; quarantineEntries=" + Text(laneRef.Lifecycle.Quarantine.Count)
                        + "; ledgerRetained=" + Text(hostRef.Ledger.RetainedResourceCount)
                        + "@baseline" + Text(worldRetainedBaseline)
                        + "; ledgerOutstanding=" + Text(hostRef.Ledger.OutstandingJobCount)
                        + "@baseline" + Text(worldOutstandingBaseline)
                        + "; quarantinedBytes=" + Text(hostRef.Ledger.QuarantinedBytes)
                        + "; ledgerBalance=" + Text(balance) + "@baseline" + Text(worldBalanceBaseline));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 4. acquisition to retirement

            /// <summary>
            /// The last cycle's acquisitions walked to their end in both ledgers: every record the world's ledger
            /// holds for that installation is `Retired`, no record of it is left `Acquired`, `Ready`, `Retiring` or
            /// `Quarantined`, and every record the composition lane holds for it is non-retained (P-048).
            /// </summary>
            private void AcquisitionsTracedToRetirement()
            {
                const string name = "lifecycle-stress-acquisitions-traced-to-retirement";
                try
                {
                    if (!Attached() || cycleInstances.Count == 0)
                    {
                        Add(name, false, cycleInstances.Count == 0
                            ? "no cycle instance was recorded, so there is nothing to trace"
                            : "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    PluginInstanceId instance = cycleInstances[cycleInstances.Count - 1];
                    CompositionHost laneRef = Lane;

                    int compositionRecords = 0;
                    int compositionRetained = 0;
                    int compositionNotRetired = 0;
                    IReadOnlyList<WorldResourceRecord> records = laneRef.Resources.Records();
                    for (int i = 0; i < records.Count; i++)
                    {
                        if (!records[i].Instance.Equals(instance))
                        {
                            continue;
                        }

                        compositionRecords++;
                        if (records[i].IsRetained)
                        {
                            compositionRetained++;
                        }

                        if (records[i].State != ResourceRetirementState.Retired)
                        {
                            compositionNotRetired++;
                        }
                    }

                    WorldResourceLedgerSnapshot snapshot = Host.ReadResourceLedger();
                    int worldRecords = 0;
                    int worldNotRetired = 0;
                    int worldOpenStates = 0;
                    for (int i = 0; i < snapshot.Resources.Count; i++)
                    {
                        WorldResourceRecord record = snapshot.Resources[i];
                        if (!record.Instance.Equals(instance))
                        {
                            continue;
                        }

                        worldRecords++;
                        if (record.State != ResourceRetirementState.Retired)
                        {
                            worldNotRetired++;
                        }

                        if (IsOpenState(record.State))
                        {
                            worldOpenStates++;
                        }
                    }

                    bool pass = compositionRecords == 1
                        && compositionRetained == 0
                        && compositionNotRetired == 0
                        && worldNotRetired == 0
                        && worldOpenStates == 0;

                    Add(name, pass,
                        "instance=" + instance.ToString()
                        + "; compositionRecords=" + Text(compositionRecords)
                        + "/notRetired=" + Text(compositionNotRetired)
                        + "/retained=" + Text(compositionRetained)
                        + "; worldRecords=" + Text(worldRecords)
                        + "/notRetired=" + Text(worldNotRetired)
                        + "/openStates=" + Text(worldOpenStates)
                        + "; worldEpoch=" + Text(snapshot.Epoch.Value)
                        + "; worldQuarantinedBytes=" + Text(snapshot.QuarantinedBytes));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 5. delayed completions

            /// <summary>
            /// One hundred completions stamped for an activation that no longer exists: the first fifty are evaluated
            /// while the installation is `Disposed`, then the same plugin-instance identity is remounted so a new
            /// installation generation exists, and the remaining fifty - still carrying the retired activation's
            /// generation and epoch - are evaluated against the live one. Every decision must refuse authority, no
            /// evaluation may move a step, an epoch or a revision, and no resource of the retired activation may be
            /// released again (P-005, P-007, P-047).
            /// </summary>
            private void DelayedCompletionsAreDiscarded()
            {
                const string name = "lifecycle-stress-delayed-completions-are-discarded";
                int discardedFirst = 0;
                int discardedSecond = 0;
                int dispatched = 0;
                string decisions = "<none>";
                bool generationAdvanced = false;
                bool refusalsOnly = true;
                try
                {
                    if (!Attached() || cycleInstances.Count == 0)
                    {
                        Add(name, false, cycleInstances.Count == 0
                            ? "no cycle instance was recorded, so there is no retired activation"
                            : "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    CompositionHost laneRef = Lane;
                    UnityWorldHost hostRef = Host;
                    PluginInstanceId instance = cycleInstances[cycleInstances.Count - 1];

                    if (!TryActivationToken(instance, 1U, out AsyncWorkToken retiredToken))
                    {
                        Add(name, false, "the retired installation published no activation stamp to stamp a token from");
                        return;
                    }

                    retiredGeneration = retiredToken.InstallationGeneration.Value;
                    int liveBefore = laneRef.Callbacks.LiveActivationCount;
                    int retiredBefore = laneRef.Resources.RetiredCount;

                    var seen = new List<CallbackGateDecision>(DelayedCompletionCount);
                    for (uint ordinal = 1U; ordinal <= DelayedCompletionHalf; ordinal++)
                    {
                        CallbackGateDecision decision = laneRef.Lifecycle.EvaluateCompletion(
                            new AsyncWorkToken(
                                retiredToken.Operation,
                                instance,
                                retiredToken.InstallationGeneration,
                                retiredToken.ActivationEpoch,
                                ordinal));
                        seen.Add(decision);
                        if (decision == CallbackGateDecision.Dispatch)
                        {
                            dispatched++;
                        }
                        else
                        {
                            discardedFirst++;
                        }

                        refusalsOnly &= decision != CallbackGateDecision.Dispatch;
                    }

                    int liveAfterRetiredBatch = laneRef.Callbacks.LiveActivationCount;
                    ulong revisionBefore = laneRef.Committed.Revision.Value;
                    ulong laneEpochBefore = laneRef.Committed.Epoch.Value;
                    ulong hostEpochBefore = hostRef.CurrentEpoch.Value;
                    LogicalStepId stepBefore = hostRef.CurrentStep;

                    // The same identity, remounted: a new installation generation, and the old tokens still carry
                    // the retired one (P-005).
                    OperationId remountOperation = NextLaneOperation();
                    LifecycleRequestReport remount = SubmitAndPublish(
                        family.StressMount(family.StressDeclarations.Installation, instance, family.WorldRootScope),
                        remountOperation);
                    bool remounted = remount.Succeeded
                        && TryInstallState(instance, out InstallationState remountState)
                        && remountState == InstallationState.Active
                        && TryCurrentGeneration(instance, out ulong generation)
                        && generation > retiredGeneration;
                    remountGeneration = TryCurrentGeneration(instance, out ulong current) ? current : 0UL;
                    generationAdvanced = remounted && remountGeneration > retiredGeneration;
                    incarnationInstances.Add(instance);
                    incarnationKeys.Add(IncarnationKey(instance));
                    incarnationText.Add("remount=" + DescribeIncarnation(instance));
                    if (!remounted)
                    {
                        Add(name, false, "the remount of the same identity did not publish (state=" + StateOf(instance)
                            + "; generation=" + Text(remountGeneration) + "@retired" + Text(retiredGeneration)
                            + "; detail=" + remount.Detail + ")");
                        return;
                    }

                    int liveAfterRemount = laneRef.Callbacks.LiveActivationCount;
                    ulong revisionAfterRemount = laneRef.Committed.Revision.Value;
                    ulong laneEpochAfterRemount = laneRef.Committed.Epoch.Value;
                    ulong hostEpochAfterRemount = hostRef.CurrentEpoch.Value;
                    LogicalStepId stepAfterRemount = hostRef.CurrentStep;

                    for (uint ordinal = 1U; ordinal <= DelayedCompletionHalf; ordinal++)
                    {
                        CallbackGateDecision decision = laneRef.Lifecycle.EvaluateCompletion(
                            new AsyncWorkToken(
                                retiredToken.Operation,
                                instance,
                                retiredToken.InstallationGeneration,
                                retiredToken.ActivationEpoch,
                                (uint)DelayedCompletionHalf + ordinal));
                        seen.Add(decision);
                        if (decision == CallbackGateDecision.Dispatch)
                        {
                            dispatched++;
                        }
                        else
                        {
                            discardedSecond++;
                        }

                        refusalsOnly &= decision != CallbackGateDecision.Dispatch;
                    }

                    int liveAfterAllBatches = laneRef.Callbacks.LiveActivationCount;
                    bool settled = laneRef.Resources.RetiredCount == retiredBefore
                        && laneRef.Resources.LiveLeaseCount == 0
                        && laneRef.Committed.Revision.Value == revisionAfterRemount
                        && laneRef.Committed.Epoch.Value == laneEpochAfterRemount
                        && hostRef.CurrentEpoch.Value == hostEpochAfterRemount
                        && hostRef.CurrentStep.Equals(stepAfterRemount);

                    decisions = Describe(seen);

                    // The remounted installation is unmounted again, so this observation leaves no installation
                    // behind for the later observations' baselines.
                    LifecycleRequestReport removal = SubmitAndPublish(
                        family.StressUnmount(instance), NextLaneOperation());
                    bool removed = removal.Succeeded
                        && TryInstallState(instance, out InstallationState removedState)
                        && removedState == InstallationState.Disposed;

                    bool pass = discardedFirst == DelayedCompletionHalf
                        && discardedSecond == DelayedCompletionHalf
                        && dispatched == 0
                        && refusalsOnly
                        && generationAdvanced
                        && liveAfterRetiredBatch == liveBefore
                        && liveAfterRemount == liveBefore + 1
                        && liveAfterAllBatches == liveAfterRemount
                        && revisionBefore <= revisionAfterRemount
                        && hostEpochBefore <= hostEpochAfterRemount
                        && stepBefore.Equals(stepAfterRemount)
                        && settled
                        && removed
                        && laneRef.Callbacks.LiveActivationCount == 0;

                    Add(name, pass,
                        "firstBatch=" + Text(discardedFirst) + " discarded/" + Text(DelayedCompletionHalf)
                        + "; secondBatch=" + Text(discardedSecond) + " discarded/" + Text(DelayedCompletionHalf)
                        + "; dispatched=" + Text(dispatched)
                        + "; decisions=" + decisions
                        + "; retiredGeneration=" + Text(retiredGeneration)
                        + "; remountGeneration=" + Text(remountGeneration)
                        + "; liveActivations=" + Text(liveBefore) + "->" + Text(liveAfterRetiredBatch)
                        + "->" + Text(liveAfterRemount) + "->" + Text(liveAfterAllBatches)
                        + "; step=" + stepBefore.Value.ToString() + "->" + stepAfterRemount.Value.ToString()
                        + "; laneEpoch=" + Text(laneEpochBefore) + "->" + Text(laneEpochAfterRemount)
                        + "; worldEpoch=" + Text(hostEpochBefore) + "->" + Text(hostEpochAfterRemount)
                        + "; retiredLeases=" + Text(retiredBefore) + "->" + Text(laneRef.Resources.RetiredCount)
                        + "; remountRemoved=" + removed);
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 6. a stalled job

            /// <summary>
            /// A tracked job of one installation that may still reach the installation's staged lease and its
            /// world-side buffer. The teardown must report `TeardownBlocked`, must not settle, must fence the job's
            /// resources, must leave the lease undisposed and quarantined, and `FenceUsers` must name the buffer.
            /// Completing the job and releasing the quarantine explicitly then retires the lease exactly once and
            /// returns every count to its baseline (P-047, P-048).
            /// </summary>
            private void StalledJobRetainsBuffers()
            {
                const string name = "lifecycle-stress-stalled-job-retains-buffers";
                long outstandingJobs = -1;
                string blockedCode = "<none>";
                bool leaseRetained = false;
                bool disposeSettled = true;
                long quarantineBefore = -1;
                long quarantineAfter = -1;
                bool releasedOnce = false;
                bool fenceNamesBuffer = false;
                try
                {
                    if (!Attached())
                    {
                        Add(name, false, "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    CompositionHost laneRef = Lane;
                    LifecycleController controllerRef = Controller;
                    UnityWorldHost hostRef = Host;
                    LifecycleStressResourceFactory factory = Resources;
                    LifecycleStressDeclarations declarations = family.StressDeclarations;

                    PluginInstanceId instance = NextStressInstance();
                    OperationId mountOperation = NextLaneOperation();
                    EditAdmission admission = laneRef.SubmitEdit(
                        family.StressMount(declarations.Fenced, instance, family.WorldRootScope),
                        mountOperation,
                        laneRef.Committed.Revision);
                    ResourceKey leaseKey = new ResourceKey(leaseSequence.Next());
                    bool staged = laneRef.StageResource(
                        mountOperation, instance, leaseKey, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode stageCode);
                    IReadOnlyList<StagedLease> leases = laneRef.StagedLeases(mountOperation);
                    laneRef.Drain();
                    DerivedAssemblyReport derived = Pipeline.PublishDerived(mountOperation);
                    CompleteWorldHalf(mountOperation, derived);

                    if (!staged || !admission.Staged || leases.Count != 1)
                    {
                        Add(name, false, "the fenced installation's lease was not staged: "
                            + DiagnosticCodeText.Of(stageCode) + "/staged=" + admission.Staged
                            + "/leases=" + Text(leases.Count));
                        return;
                    }

                    Id128 leaseId = leases[0].LeaseId;

                    // A world-side buffer of the installation. `FenceUsers` reads the *world's* ledger, so the buffer
                    // the stalled job may reach is acquired there, beside the staged lease the lane owns.
                    Id128 worldBuffer = AcquireWorldBuffer(instance);

                    Id128 jobId = jobSequence.Next();
                    controllerRef.JobFence.Track(
                        jobId,
                        instance,
                        declarations.Stage,
                        declarations.System,
                        hostRef.CurrentEpoch,
                        hostRef.CurrentStep,
                        new List<Id128> { leaseId, worldBuffer },
                        default(JobHandle));

                    outstandingJobs = controllerRef.JobFence.OutstandingCount;

                    ActivationStamp stamp = ActivationStampOf(instance);
                    IReadOnlyList<Id128> fencedNow = controllerRef.Binding.FenceUsers(instance, stamp);
                    fenceNamesBuffer = Contains(fencedNow, worldBuffer);

                    int retiredBefore = laneRef.Resources.RetiredCount;
                    int quarantinedBefore = laneRef.Resources.QuarantinedCount;
                    int failedBefore = laneRef.Resources.FailedReleaseCount;
                    int worldRetiredBefore = hostRef.Ledger.RetireCount;

                    // The coordinator's own unload, not the controller's: `LifecycleController.Unload` completes the
                    // installation's blocking jobs first by design, so a *blocked* teardown is only reachable by
                    // asking the coordinator for the P-048 order while the fence is still held.
                    TeardownReport teardown = laneRef.Lifecycle.Unload(instance, NextWorldOperation());
                    blockedCode = DiagnosticCodeText.Of(teardown.Code);
                    disposeSettled = teardown.DisposeSettled;
                    bool blockedByFence = teardown.BlockedByJobFence;
                    bool fenceNamesAfter = Contains(teardown.FencedResources, worldBuffer);
                    bool quarantinesLease = Contains(teardown.Quarantined, leaseId);
                    quarantineBefore = laneRef.Lifecycle.Quarantine.EntriesFor(instance).Count;

                    leaseRetained = laneRef.Resources.TryGetRecord(leaseId, out WorldResourceRecord record)
                        && record.IsRetained
                        && record.State == ResourceRetirementState.Quarantined;
                    bool leaseNotDisposed = factory.TryGetLease(leaseId, out ScriptedResourceLease? lease)
                        && lease != null
                        && !lease.IsDisposed;
                    bool quarantineContainsLease = laneRef.Lifecycle.Quarantine.Contains(leaseId);
                    bool worldBufferStillRetained = TryGetWorldRecord(worldBuffer, out WorldResourceRecord bufferRecord)
                        && bufferRecord.IsRetained;
                    bool nothingRetiredWhileBlocked = laneRef.Resources.RetiredCount == retiredBefore;

                    // The job completes, then the quarantine is released explicitly: the lease retires exactly once.
                    bool completed = controllerRef.JobFence.Complete(jobId);
                    CleanupReport cleanup = controllerRef.ReleaseQuarantine(instance);
                    releasedOnce = completed
                        && cleanup.Retired.Count == 1
                        && Contains(cleanup.Retired, leaseId)
                        && laneRef.Resources.RetiredCount - retiredBefore == 1
                        && laneRef.Resources.RetainedCountFor(instance) == 0
                        && factory.DisposalAttemptsOf(leaseId) == 1;
                    quarantineAfter = laneRef.Lifecycle.Quarantine.EntriesFor(instance).Count;
                    bool worldBufferRetired = hostRef.Ledger.Retire(worldBuffer);
                    bool worldRetiredOnce = worldBufferRetired && hostRef.Ledger.RetireCount - worldRetiredBefore == 1;

                    // The installation itself is unmounted, so the observation leaves no installation behind.
                    LifecycleRequestReport removal = SubmitAndPublish(
                        family.StressUnmount(instance), NextLaneOperation());
                    bool removed = removal.Succeeded
                        && TryInstallState(instance, out InstallationState removedState)
                        && removedState == InstallationState.Disposed;

                    bool pass = teardown.Code == DiagnosticCode.TeardownBlocked
                        && !disposeSettled
                        && blockedByFence
                        && teardown.OutstandingJobs == 1
                        && outstandingJobs == 1
                        && fenceNamesBuffer
                        && fenceNamesAfter
                        && quarantinesLease
                        && quarantineContainsLease
                        && leaseRetained
                        && leaseNotDisposed
                        && worldBufferStillRetained
                        && nothingRetiredWhileBlocked
                        && releasedOnce
                        && quarantineAfter == 0
                        && worldRetiredOnce
                        && removed
                        && controllerRef.JobFence.OutstandingCount == 0
                        && controllerRef.JobFence.CompletionFailureCount == 0
                        && laneRef.Resources.QuarantinedCount == quarantinedBefore + 1
                        && laneRef.Resources.FailedReleaseCount == failedBefore
                        && laneRef.Lifecycle.Quarantine.Count == 0
                        && laneRef.Resources.LiveLeaseCount == 0
                        && laneRef.Callbacks.LiveActivationCount == 0
                        && hostRef.Ledger.RetainedResourceCount == worldRetainedBaseline
                        && hostRef.Ledger.OutstandingJobCount == worldOutstandingBaseline;

                    Add(name, pass,
                        "outstandingJobs=" + Text(outstandingJobs)
                        + "; code=" + blockedCode
                        + "; disposeSettled=" + disposeSettled
                        + "; blockedByJobFence=" + blockedByFence
                        + "; teardownOutstanding=" + Text(teardown.OutstandingJobs)
                        + "; fenceUsersNamesBuffer=" + fenceNamesBuffer
                        + "; teardownFencesBuffer=" + fenceNamesAfter
                        + "; leaseRetained=" + leaseRetained
                        + "; leaseNotDisposed=" + leaseNotDisposed
                        + "; quarantineContainsLease=" + quarantineContainsLease
                        + "; quarantine=" + Text(quarantineBefore) + "->" + Text(quarantineAfter)
                        + "; quarantineAdmissions=" + Text(quarantinedBefore) + "->"
                        + Text(laneRef.Resources.QuarantinedCount)
                        + "; failedReleases=" + Text(failedBefore) + "->"
                        + Text(laneRef.Resources.FailedReleaseCount)
                        + "; worldBufferRetained=" + worldBufferStillRetained
                        + "; jobCompleted=" + completed
                        + "; retiredLeases=" + Text(cleanup.Retired.Count)
                        + "; worldBufferRetired=" + worldBufferRetired
                        + "; removalSettled=" + removed
                        + "; factory=" + factory.DescribeDisposals());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 7. a throwing disposer

            /// <summary>
            /// Three leases of one publication, the last of which is scripted to fail exactly one disposal. The
            /// teardown reports cleanup errors, the failed reference is never reported as a disposal and stays
            /// retained, and every *other* lease of that publication is still retired - the failure of one disposer
            /// does not stop the independent cleanup. The explicit later release then retires the retained reference
            /// exactly once, so the observation ends at the baseline (P-048, "independent cleanup continues after a
            /// disposer error").
            /// </summary>
            private void ThrowingDisposerKeepsCleanupGoing()
            {
                const string name = "lifecycle-stress-throwing-disposer-keeps-cleanup-going";
                int retired = -1;
                int failed = -1;
                int quarantined = -1;
                bool failedNotDisposed = false;
                bool othersRetired = false;
                bool hasCleanupErrors = false;
                bool releaseRetiredOnce = false;
                bool releaseKeptItSingle = false;
                try
                {
                    if (!Attached())
                    {
                        Add(name, false, "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    CompositionHost laneRef = Lane;
                    LifecycleController controllerRef = Controller;
                    LifecycleStressResourceFactory factory = Resources;

                    PluginInstanceId instance = NextStressInstance();
                    OperationId mountOperation = NextLaneOperation();
                    EditAdmission admission = laneRef.SubmitEdit(
                        family.StressMount(family.StressDeclarations.Installation, instance, family.WorldRootScope),
                        mountOperation,
                        laneRef.Committed.Revision);

                    var keys = new List<ResourceKey>(ThrowingDisposerLeaseCount);
                    bool staged = true;
                    for (int i = 0; i < ThrowingDisposerLeaseCount; i++)
                    {
                        ResourceKey key = new ResourceKey(leaseSequence.Next());
                        keys.Add(key);
                        staged &= laneRef.StageResource(
                            mountOperation, instance, key, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode _);
                    }

                    IReadOnlyList<StagedLease> leases = laneRef.StagedLeases(mountOperation);
                    laneRef.Drain();
                    DerivedAssemblyReport derived = Pipeline.PublishDerived(mountOperation);
                    CompleteWorldHalf(mountOperation, derived);

                    if (!staged || !admission.Staged || leases.Count != ThrowingDisposerLeaseCount)
                    {
                        Add(name, false, "the installation's leases were not all staged: staged=" + staged
                            + "/admission=" + admission.Staged + "/leases=" + Text(leases.Count));
                        return;
                    }

                    // The lease acquired last is retired first (reverse acquisition order), so the scripted failure
                    // happens before the two independent releases and "cleanup continues" is a claim about real order.
                    Id128 failingLease = LeaseIdOf(leases, keys[ThrowingDisposerLeaseCount - 1]);
                    Id128 firstHealthy = LeaseIdOf(leases, keys[0]);
                    Id128 secondHealthy = LeaseIdOf(leases, keys[1]);
                    factory.FailingDisposals.Add(failingLease);

                    int retiredBefore = laneRef.Resources.RetiredCount;
                    int quarantinedBefore = laneRef.Resources.QuarantinedCount;
                    int failedBefore = laneRef.Resources.FailedReleaseCount;

                    LifecycleRequestReport unmountReport = SubmitAndPublish(
                        family.StressUnmount(instance), NextLaneOperation());
                    TeardownReport? teardown = TeardownOf(unmountReport.Lifecycle, instance);

                    hasCleanupErrors = unmountReport.HasCleanupErrors || (teardown != null && teardown.HasCleanupErrors);
                    bool failedReported = teardown != null && Contains(teardown.FailedReleases, failingLease);
                    bool failedQuarantined = teardown != null && Contains(teardown.Quarantined, failingLease);
                    failedNotDisposed = factory.TryGetLease(failingLease, out ScriptedResourceLease? failedLease)
                        && failedLease != null
                        && !failedLease.IsDisposed;
                    othersRetired = teardown != null
                        && teardown.Cleanup.Retired.Count == ThrowingDisposerLeaseCount - 1
                        && IsDisposed(factory, firstHealthy)
                        && IsDisposed(factory, secondHealthy)
                        && laneRef.Resources.RetiredCount - retiredBefore == ThrowingDisposerLeaseCount - 1;
                    bool retainedOnlyTheFailure = laneRef.Resources.RetainedCountFor(instance) == 1;
                    bool quarantineHoldsIt = laneRef.Lifecycle.Quarantine.Contains(failingLease)
                        && laneRef.Lifecycle.Quarantine.EntriesFor(instance).Count == 1;
                    bool neverDisposedTwice = factory.DisposalAttemptsOf(failingLease) == 1;
                    retired = laneRef.Resources.RetiredCount - retiredBefore;
                    failed = laneRef.Resources.FailedReleaseCount - failedBefore;
                    quarantined = laneRef.Lifecycle.Quarantine.EntriesFor(instance).Count;

                    // The explicit later release P-048 allows. The retained reference is retired exactly once here,
                    // and nothing else is released by it.
                    int retiredAtRelease = laneRef.Resources.RetiredCount;
                    CleanupReport release = controllerRef.ReleaseQuarantine(instance);
                    releaseRetiredOnce = release.Retired.Count == 1
                        && Contains(release.Retired, failingLease)
                        && laneRef.Resources.RetiredCount - retiredAtRelease == 1
                        && laneRef.Resources.RetainedCountFor(instance) == 0
                        && laneRef.Lifecycle.Quarantine.EntriesFor(instance).Count == 0
                        && IsDisposed(factory, failingLease);
                    releaseKeptItSingle = factory.DisposalAttemptsOf(failingLease) == 2
                        && CountOccurrences(factory.DisposedOrder, failingLease) == 1;

                    // A later, clean installation proves the release path itself is unaffected by the failure.
                    bool cleanRound = RunCleanRound();

                    bool pass = hasCleanupErrors
                        && failedReported
                        && failedQuarantined
                        && failedNotDisposed
                        && neverDisposedTwice
                        && othersRetired
                        && retainedOnlyTheFailure
                        && quarantineHoldsIt
                        && releaseRetiredOnce
                        && releaseKeptItSingle
                        && cleanRound
                        && laneRef.Resources.LiveLeaseCount == 0
                        && laneRef.Resources.QuarantinedCount == quarantinedBefore + 1
                        && laneRef.Lifecycle.Quarantine.Count == 0
                        && laneRef.Resources.FailedReleaseCount == failedBefore + 1
                        && Host.Ledger.RetainedResourceCount == worldRetainedBaseline;

                    Add(name, pass,
                        "cleanupErrors=" + hasCleanupErrors
                        + "; failedReported=" + failedReported
                        + "; failedQuarantined=" + failedQuarantined
                        + "; failedNotDisposed=" + failedNotDisposed
                        + "; disposalAttemptsOfFailedLease=" + Text(factory.DisposalAttemptsOf(failingLease))
                        + "; othersRetired=" + othersRetired
                        + "; retainedOnlyTheFailure=" + retainedOnlyTheFailure
                        + "; quarantineHoldsIt=" + quarantineHoldsIt
                        + "; releasedRetired=" + Text(release.Retired.Count)
                        + "; releaseRetiredOnce=" + releaseRetiredOnce
                        + "; releaseKeptItSingle=" + releaseKeptItSingle
                        + "; cleanRound=" + cleanRound
                        + "; retiredByPublication=" + Text(retired)
                        + "; failedByPublication=" + Text(failed)
                        + "; quarantinedAtFailure=" + Text(quarantined)
                        + "; quarantineAdmissions=" + Text(quarantinedBefore) + "->"
                        + Text(laneRef.Resources.QuarantinedCount)
                        + "; liveLeases=" + Text(laneRef.Resources.LiveLeaseCount)
                        + "; quarantined=" + Text(laneRef.Resources.QuarantinedCount)
                        + "; failedReleases=" + Text(laneRef.Resources.FailedReleaseCount)
                        + "; factory=" + factory.DescribeDisposals());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// One clean later installation: a fresh identity mounted with one staged lease and unmounted again, so
            /// "the release path still works after a disposer threw" is a fact about the same world and the same
            /// ledger, not about a different run (P-048).
            /// </summary>
            private bool RunCleanRound()
            {
                CompositionHost laneRef = Lane;
                LifecycleStressResourceFactory factory = Resources;
                PluginInstanceId instance = NextStressInstance();
                OperationId mountOperation = NextLaneOperation();
                EditAdmission admission = laneRef.SubmitEdit(
                    family.StressMount(family.StressDeclarations.Installation, instance, family.WorldRootScope),
                    mountOperation,
                    laneRef.Committed.Revision);
                ResourceKey key = new ResourceKey(leaseSequence.Next());
                bool staged = laneRef.StageResource(
                    mountOperation, instance, key, new FrozenPayload(new byte[] { 1 }), null, out DiagnosticCode _);
                IReadOnlyList<StagedLease> leases = laneRef.StagedLeases(mountOperation);
                laneRef.Drain();
                DerivedAssemblyReport derived = Pipeline.PublishDerived(mountOperation);
                CompleteWorldHalf(mountOperation, derived);
                if (!staged || !admission.Staged || leases.Count != 1)
                {
                    return false;
                }

                LifecycleRequestReport removal = SubmitAndPublish(
                    family.StressUnmount(instance), NextLaneOperation());
                return removal.Succeeded
                    && TryInstallState(instance, out InstallationState state)
                    && state == InstallationState.Disposed
                    && laneRef.Resources.RetainedCountFor(instance) == 0
                    && IsDisposed(factory, leases[0].LeaseId);
            }

            // ================================================================== 8. required-provider churn

            /// <summary>
            /// The required-service pair, mounted once and then churned: the consumer declares a required dependency
            /// on the contract the provider exports at the same scope. Each removal must leave the consumer
            /// `WaitingForDependencies` in that same publication, with zero bindings and its attributed rows retracted;
            /// each return must leave it `Active` again with the rows and bindings it had. Ten rounds, and the
            /// counters end at their baseline (P-011, P-012).
            /// </summary>
            private void RequiredProviderChurn()
            {
                const string name = "lifecycle-stress-required-provider-churn";
                int rounds = 0;
                int failedRound = -1;
                string failure = string.Empty;
                long rows = -1;
                long bindings = -1;
                string initialConsumerState = "<none>";
                try
                {
                    if (!Attached())
                    {
                        Add(name, false, "the world is not attached: " + DescribeMissing());
                        return;
                    }

                    CompositionHost laneRef = Lane;
                    LifecycleStressDeclarations declarations = family.StressDeclarations;

                    PluginInstanceId consumer = NextStressInstance();
                    LifecycleRequestReport consumerMount = SubmitAndPublish(
                        family.StressMount(declarations.ServiceConsumer, consumer, family.WorldRootScope),
                        NextLaneOperation());
                    if (!consumerMount.Succeeded
                        || !TryInstallState(consumer, out InstallationState consumerState)
                        || consumerState != InstallationState.WaitingForDependencies)
                    {
                        Add(name, false, "the consumer did not start waiting for its missing provider: state="
                            + StateOf(consumer) + "; succeeded=" + consumerMount.Succeeded
                            + "; detail=" + consumerMount.Detail);
                        return;
                    }

                    initialConsumerState = StateOf(consumer);
                    PluginInstanceId provider = NextStressInstance();
                    LifecycleRequestReport firstReturn = SubmitAndPublish(
                        family.StressMount(declarations.ServiceProvider, provider, family.WorldRootScope),
                        NextLaneOperation());
                    if (!firstReturn.Succeeded
                        || !TryInstallState(consumer, out InstallationState activeState)
                        || activeState != InstallationState.Active)
                    {
                        Add(name, false, "the consumer was not resumed by the provider's first mount: state="
                            + StateOf(consumer) + "; resumed=" + Text(firstReturn.ResumedConsumers.Count)
                            + "; detail=" + firstReturn.Detail);
                        return;
                    }

                    rows = AttributedRows(consumer);
                    bindings = BindingCount(consumer);
                    if (rows <= 0 || bindings <= 0)
                    {
                        Add(name, false, "the active consumer contributed no rows or bindings: rows=" + Text(rows)
                            + "; bindings=" + Text(bindings));
                        return;
                    }

                    // The churn stages no lease, so the retired, quarantined and failedrelease counters of the
                    // ledger must not move at all across the ten rounds (P-048).
                    int retiredAtChurn = laneRef.Resources.RetiredCount;
                    int quarantinedAtChurn = laneRef.Resources.QuarantinedCount;
                    int failedAtChurn = laneRef.Resources.FailedReleaseCount;
                    for (int round = 1; round <= RequiredProviderChurnRounds; round++)
                    {
                        LifecycleRequestReport loss = SubmitAndPublish(
                            family.StressUnmount(provider), NextLaneOperation());
                        ContributionRetraction? retraction = RetractionOf(loss.Lifecycle, consumer);
                        bool waits = loss.Succeeded
                            && NamesExactly(loss.WaitingConsumers, consumer)
                            && TryInstallState(consumer, out InstallationState waitingState)
                            && waitingState == InstallationState.WaitingForDependencies
                            && BindingCount(consumer) == 0
                            && AttributedRows(consumer) == 0
                            && retraction != null
                            && retraction.AttributedRows == rows;
                        if (!waits)
                        {
                            failedRound = round;
                            failure = "the removal left the consumer in " + StateOf(consumer)
                                + " (waiting=" + Text(loss.WaitingConsumers.Count)
                                + "; bindings=" + Text(BindingCount(consumer))
                                + "; rows=" + Text(AttributedRows(consumer))
                                + "; retracted=" + Text(retraction != null ? retraction.AttributedRows : -1)
                                + "/expected=" + Text(rows) + ")";
                            break;
                        }

                        PluginInstanceId returned = NextStressInstance();
                        LifecycleRequestReport resume = SubmitAndPublish(
                            family.StressMount(declarations.ServiceProvider, returned, family.WorldRootScope),
                            NextLaneOperation());
                        bool resumed = resume.Succeeded
                            && NamesExactly(resume.ResumedConsumers, consumer)
                            && TryInstallState(consumer, out InstallationState resumedState)
                            && resumedState == InstallationState.Active
                            && AttributedRows(consumer) == rows
                            && BindingCount(consumer) == bindings;
                        if (!resumed)
                        {
                            failedRound = round;
                            failure = "the return left the consumer in " + StateOf(consumer)
                                + " (resumed=" + Text(resume.ResumedConsumers.Count)
                                + "; rows=" + Text(AttributedRows(consumer)) + "/expected=" + Text(rows)
                                + "; bindings=" + Text(BindingCount(consumer)) + "/expected=" + Text(bindings) + ")";
                            break;
                        }

                        provider = returned;
                        rounds++;
                    }

                    bool pass = failedRound < 0
                        && rounds == RequiredProviderChurnRounds
                        && laneRef.Resources.LiveLeaseCount == 0
                        && laneRef.Resources.RetiredCount == retiredAtChurn
                        && laneRef.Resources.QuarantinedCount == quarantinedAtChurn
                        && laneRef.Resources.FailedReleaseCount == failedAtChurn
                        && laneRef.Lifecycle.Quarantine.Count == 0
                        && laneRef.Callbacks.LiveActivationCount == 2
                        && laneRef.Lifecycle.Jobs.OutstandingCount == 0
                        && Host.Ledger.RetainedResourceCount == worldRetainedBaseline;

                    Add(name, pass,
                        "rounds=" + Text(rounds) + "/" + Text(RequiredProviderChurnRounds)
                        + "; failedRound=" + Text(failedRound)
                        + "; initialConsumerState=" + initialConsumerState
                        + "; consumerState=" + StateOf(consumer)
                        + "; rows=" + Text(rows)
                        + "; bindings=" + Text(bindings)
                        + "; providerState=" + StateOf(provider)
                        + "; liveLeases=" + Text(laneRef.Resources.LiveLeaseCount)
                        + "; liveActivations=" + Text(laneRef.Callbacks.LiveActivationCount)
                        + "; ledgerRetired=" + Text(retiredAtChurn) + "->" + Text(laneRef.Resources.RetiredCount)
                        + "; ledgerQuarantine=" + Text(quarantinedAtChurn) + "->"
                        + Text(laneRef.Resources.QuarantinedCount)
                        + "; quarantineEntries=" + Text(laneRef.Lifecycle.Quarantine.Count)
                        + (failure.Length == 0 ? string.Empty : "; failure=" + failure));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 9. loop nodes do not accumulate

            /// <summary>
            /// The whole process's player-loop node count and install count, before and after the stress. This
            /// scenario installs nothing, so both must be unchanged: a stress that leaked a node or a subscription
            /// would show up here even when every world-side counter returned to its baseline (GC-022 acceptance).
            /// </summary>
            private void LoopNodesDoNotAccumulate()
            {
                const string name = "lifecycle-stress-loop-nodes-do-not-accumulate";
                try
                {
                    int nodesNow = GameCorePlayerLoopInstaller.CountInstalledNodes();
                    int installsNow = GameCorePlayerLoopInstaller.InstallCount;
                    bool pass = nodesNow == loopNodeBaseline && installsNow == loopInstallCountBaseline;
                    Add(name, pass,
                        "loopNodes=" + Text(loopNodeBaseline) + "->" + Text(nodesNow)
                        + "; loopInstalls=" + Text(loopInstallCountBaseline) + "->" + Text(installsNow)
                        + "; removeCount=" + Text(GameCorePlayerLoopInstaller.RemoveCount)
                        + "; duplicateRefusals=" + Text(GameCorePlayerLoopInstaller.DuplicateInstallRefusalCount));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 10. registry back at baseline

            /// <summary>
            /// Unloads every installation the run still has mounted, then stops and disposes the world, and asserts
            /// the process-wide registry and the world ledger are back where they started: no world, no entity world,
            /// the lifecycle `Disposed`, and nothing retained or outstanding (P-048).
            /// </summary>
            private void RegistryReturnsToBaseline()
            {
                const string name = "lifecycle-stress-registry-returns-to-baseline";
                int remaining = -1;
                int settled = -1;
                try
                {
                    if (host == null)
                    {
                        Add(name, false, "no world was created: " + DescribeFailures());
                        return;
                    }

                    UnityWorldHost hostRef = host;
                    remaining = CountRemainingInstalls();
                    settled = UnmountAllRemaining();

                    if (!worldStopped)
                    {
                        if (time != null)
                        {
                            time.Clear(out int discardedCommands, out int pendingWakes);
                            _ = discardedCommands;
                            _ = pendingWakes;
                        }

                        stageRuntime?.Dispose();
                        stageRuntime = null;
                        hostRef.Stop(NextWorldOperation(), "gc-022 lifecycle stress teardown");
                        hostRef.Dispose();
                        worldStopped = true;
                    }

                    bool pass = UnityWorldRegistry.Count == registryBaseline
                        && !hostRef.IsEntityWorldCreated
                        && hostRef.Lifecycle == WorldLifecycleState.Disposed
                        && hostRef.Ledger.RetainedResourceCount == 0
                        && hostRef.Ledger.OutstandingJobCount == 0
                        && settled == remaining;

                    Add(name, pass,
                        "registryBefore=" + Text(registryBaseline)
                        + "; registryAfter=" + Text(UnityWorldRegistry.Count)
                        + "; entityWorld=" + hostRef.IsEntityWorldCreated
                        + "; lifecycle=" + hostRef.Lifecycle
                        + "; ledgerRetained=" + Text(hostRef.Ledger.RetainedResourceCount)
                        + "; ledgerOutstanding=" + Text(hostRef.Ledger.OutstandingJobCount)
                        + "; ledgerJobs=" + Text(hostRef.Ledger.JobCount)
                        + "; remainingInstalls=" + Text(remaining)
                        + "; settledInstalls=" + Text(settled)
                        + "; stopOutcome=" + (worldStopped ? "stopped" : "notStopped")
                        + "; pumpCount=" + Text(hostRef.PumpCount)
                        + "; reentrantPumps=" + Text(hostRef.ReentrantPumpCount)
                        + "; faults=" + Text(hostRef.FaultCount));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 11. incarnations are fresh

            /// <summary>
            /// Every incarnation this run created is distinct and non-default: the single world's session identity,
            /// every cycle's plugin-instance identity, and the generation the remounted identity reached. Nothing is
            /// identified by an index, a name or an elapsed value (P-005).
            /// </summary>
            private void IncarnationsAreFresh()
            {
                const string name = "lifecycle-stress-incarnations-are-fresh";
                try
                {
                    int distinctIncarnations = 0;
                    var seenIncarnations = new HashSet<string>(StringComparer.Ordinal);
                    int duplicateIncarnations = 0;
                    int defaultIncarnations = 0;
                    for (int i = 0; i < incarnationKeys.Count; i++)
                    {
                        if (!seenIncarnations.Add(incarnationKeys[i]))
                        {
                            duplicateIncarnations++;
                        }

                        if (incarnationInstances[i].Value.IsDefault)
                        {
                            defaultIncarnations++;
                        }
                    }

                    distinctIncarnations = seenIncarnations.Count;

                    int defaultCycles = 0;
                    var seenCycleInstances = new HashSet<Id128>();
                    int duplicateCycleInstances = 0;
                    for (int i = 0; i < cycleInstances.Count; i++)
                    {
                        if (cycleInstances[i].Value.IsDefault)
                        {
                            defaultCycles++;
                        }

                        if (!seenCycleInstances.Add(cycleInstances[i].Value))
                        {
                            duplicateCycleInstances++;
                        }
                    }

                    int defaultWorlds = 0;
                    var seenWorlds = new HashSet<Id128>();
                    int duplicateWorlds = 0;
                    for (int i = 0; i < worlds.Count; i++)
                    {
                        if (worlds[i].Session.IsDefault)
                        {
                            defaultWorlds++;
                        }

                        if (!seenWorlds.Add(worlds[i].Session))
                        {
                            duplicateWorlds++;
                        }
                    }

                    bool generationAdvanced = remountGeneration > retiredGeneration
                        && retiredGeneration != 0UL
                        && remountGeneration != 0UL;

                    bool pass = incarnationKeys.Count == cycles + 1
                        && duplicateIncarnations == 0
                        && defaultIncarnations == 0
                        && distinctIncarnations == incarnationKeys.Count
                        && cycleInstances.Count == cycles
                        && duplicateCycleInstances == 0
                        && defaultCycles == 0
                        && worlds.Count == 1
                        && defaultWorlds == 0
                        && duplicateWorlds == 0
                        && generationAdvanced;

                    Add(name, pass,
                        "worlds=" + Text(worlds.Count)
                        + "; defaultWorlds=" + Text(defaultWorlds)
                        + "; duplicateWorlds=" + Text(duplicateWorlds)
                        + "; cycles=" + Text(cycleInstances.Count)
                        + "; defaultCycles=" + Text(defaultCycles)
                        + "; duplicateCycleInstances=" + Text(duplicateCycleInstances)
                        + "; incarnations=" + Text(incarnationKeys.Count)
                        + "; distinctIncarnations=" + Text(distinctIncarnations)
                        + "; duplicateIncarnations=" + Text(duplicateIncarnations)
                        + "; defaultIncarnations=" + Text(defaultIncarnations)
                        + "; retiredGeneration=" + Text(retiredGeneration)
                        + "; remountGeneration=" + Text(remountGeneration)
                        + "; generationAdvanced=" + generationAdvanced
                        + "; sample=" + Join(incarnationText, 3));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== 12. the frozen digest

            /// <summary>
            /// The digest over the eleven observations above plus this one, computed exactly as
            /// <see cref="LifecycleStressResult"/> computes it, and compared with the family's frozen literal. A
            /// renamed, reordered, added or dropped observation - or a single failing step - changes the digest, so
            /// this observation is the whole table's claim rather than a twelfth independent one (P-008, P-028).
            /// </summary>
            private void RepeatableDigest()
            {
                const string name = "lifecycle-stress-repeatable-digest";
                try
                {
                    string expected = fixtureRun ? family.FixtureCatalogDigest : family.GeneratedCatalogDigest;
                    var lines = new List<string>(ObservationNames.Length);
                    for (int i = 0; i < steps.Count; i++)
                    {
                        lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                    }

                    // This observation's own line, as a passing one: on a clean run the provisional table below is
                    // the table the result will carry, so the literal is the digest of the frozen observation set.
                    lines.Add(Label(name) + "=pass");
                    string digest = NarrativeDigest.OfLines(lines);
                    bool pass = string.Equals(digest, expected, StringComparison.Ordinal);
                    Add(name, pass,
                        "digest=" + digest
                        + "; expected=" + expected
                        + "; observations=" + Text(lines.Count)
                        + "; label=" + family.Label
                        + "; fixtureRun=" + fixtureRun
                        + "; prefix=" + FixtureRunPrefix
                        + "; cycleCount=" + Text(cycles));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ================================================================== the real modules

            /// <summary>
            /// Submits one lifecycle payload through the controller - which performs the lane submission, the drain
            /// and the derived publication in the fixed order of P-046 - then completes the world's half of that same
            /// publication (P-006, P-033, P-051).
            /// </summary>
            private LifecycleRequestReport SubmitAndPublish(CompositionEditPayload payload, OperationId operation)
            {
                LifecycleRequestReport report = Controller.Submit(payload, operation);
                if (report.Derived != null)
                {
                    CompleteWorldHalf(operation, report.Derived);
                }

                return report;
            }

            /// <summary>
            /// Completes the world's half of one lifecycle publication. A publication whose derivation changed no
            /// effective binding is answered `NoTargetChange`, and P-006 still counts it as one publication of the
            /// one series, so the world must publish its assembly for that pair - exactly as the narrative lifecycle
            /// scenario completes every publication it drains. Two shapes exist:
            ///
            ///   * a derivation that never adopted (an empty delta) leaves the pair free, so the world publishes its
            ///     unchanged assembly for it;
            ///   * a derivation that adopted and then collapsed to a planner no-op leaves the pair adopted and
            ///     pending, and the one legal consumer of a pending pair is a spawn: the carrier is seeded from the
            ///     family's declared future recipe and scope, so the pair is consumed (P-006, P-024).
            /// </summary>
            private bool CompleteWorldHalf(OperationId operation, DerivedAssemblyReport derived)
            {
                CompositionHost laneRef = Lane;
                AssemblyPublisher publisherRef = Publisher;
                UnityWorldHost hostRef = Host;

                if (derived.Outcome != DerivedAssemblyOutcome.NoTargetChange
                    || AssemblyPublisher.MatchesPublishedAssembly(
                        laneRef.Committed.Revision,
                        laneRef.Committed.Epoch,
                        publisherRef.PublishedRevision,
                        hostRef.CurrentEpoch))
                {
                    return true;
                }

                if (!publisherRef.HasAdoptedPublication)
                {
                    publisherRef.PublishUnchangedAssembly(operation, laneRef.Committed.Revision, laneRef.Committed.Epoch);
                }
                else
                {
                    carrierOrdinal++;
                    TargetId carrier = TargetId.FromRaw(CarrierSalt.High, CarrierSalt.Low + (ulong)carrierOrdinal);
                    family.PrepareSpawn();
                    DerivedAssemblyReport consumed = Pipeline.PublishSpawn(
                        NextWorldOperation(), carrier, family.FutureRecipe, family.WorldRootScope);
                    if (!consumed.Succeeded)
                    {
                        return false;
                    }
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    laneRef.Committed.Revision,
                    laneRef.Committed.Epoch,
                    publisherRef.PublishedRevision,
                    hostRef.CurrentEpoch);
            }

            /// <summary>
            /// One world-side buffer of one installation, acquired and marked ready in the world's own ledger, so
            /// the resources a stalled job may reach are recorded where `FenceUsers` reads them (P-047). The owner is
            /// derived from the installation identity: a resource of one installation has that installation's owner.
            /// </summary>
            private Id128 AcquireWorldBuffer(PluginInstanceId instance)
            {
                UnityWorldHost hostRef = Host;
                var owner = new OwnerId(instance.Value.High, instance.Value.Low);
                Id128 resourceId = hostRef.Ledger.Acquire(
                    WorldResourceKind.ManagedLease,
                    new ResourceKey(leaseSequence.Next()),
                    owner,
                    instance,
                    Id128.Zero,
                    1UL);
                hostRef.Ledger.MarkReady(resourceId);
                return resourceId;
            }

            // ================================================================== teardown and helpers

            /// <summary>
            /// Unloads every installation the run still has mounted through the family's own unmount payload, so the
            /// world's own stop has nothing left to retain (P-048).
            /// </summary>
            private int CountRemainingInstalls()
            {
                CompositionHost laneRef = Lane;
                int remaining = 0;
                for (int i = 0; i < laneRef.Committed.Installs.Count; i++)
                {
                    if (laneRef.Committed.Installs[i].State != InstallationState.Disposed)
                    {
                        remaining++;
                    }
                }

                return remaining;
            }

            private int UnmountAllRemaining()
            {
                CompositionHost laneRef = Lane;
                var pending = new List<PluginInstanceId>();
                for (int i = 0; i < laneRef.Committed.Installs.Count; i++)
                {
                    InstallEntry entry = laneRef.Committed.Installs[i];
                    if (entry.State != InstallationState.Disposed)
                    {
                        pending.Add(entry.Instance);
                    }
                }

                int settled = 0;
                for (int i = 0; i < pending.Count; i++)
                {
                    try
                    {
                        SubmitAndPublish(family.StressUnmount(pending[i]), NextLaneOperation());
                        if (TryInstallState(pending[i], out InstallationState state)
                            && state == InstallationState.Disposed)
                        {
                            settled++;
                        }
                    }
                    catch (Exception)
                    {
                        // The registry baseline is what this observation measures; an installation that refuses the
                        // teardown edge is reported by the counters below rather than replaced by an exception here.
                    }
                }

                return settled;
            }

            /// <summary>
            /// Stops and disposes the world when a step failed before its own teardown ran, so the process-wide
            /// registry returns to its baseline even after a failure.
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

                    stageRuntime?.Dispose();
                    stageRuntime = null;
                    host.Stop(NextWorldOperation(), "gc-022 lifecycle stress teardown (safety path)");
                    host.Dispose();
                    worldStopped = true;
                }
                catch (Exception)
                {
                    // The safety path never replaces a step's own failure with its own.
                }
            }

            /// <summary>
            /// Records every observation the frozen table names, in the frozen order, so a run that failed before a
            /// step could record its own outcome still reports the whole table (with the missing ones failing) rather
            /// than a shorter one.
            /// </summary>
            private void EnsureEveryObservationIsRecorded()
            {
                if (steps.Count == ObservationNames.Length)
                {
                    return;
                }

                var byName = new Dictionary<string, LifecycleStressStep>(StringComparer.Ordinal);
                for (int i = 0; i < steps.Count; i++)
                {
                    byName[steps[i].Name] = steps[i];
                }

                var ordered = new List<LifecycleStressStep>(ObservationNames.Length);
                for (int i = 0; i < ObservationNames.Length; i++)
                {
                    string qualified = Label(ObservationNames[i]);
                    ordered.Add(byName.TryGetValue(qualified, out LifecycleStressStep? found)
                        ? found
                        : new LifecycleStressStep(qualified, false, "the observation was not recorded by this run"));
                }

                steps.Clear();
                steps.AddRange(ordered);
            }

            private void Add(string bareName, bool passed, string detail) =>
                steps.Add(new LifecycleStressStep(Label(bareName), passed, detail));

            private string Label(string bareName) =>
                (fixtureRun ? FixtureRunPrefix : string.Empty) + family.Label + "/" + bareName;

            private UnityWorldHost Host =>
                host ?? throw new InvalidOperationException("The world is not attached.");

            private CompositionHost Lane =>
                lane ?? throw new InvalidOperationException("The composition lane is not attached.");

            private AssemblyPublisher Publisher =>
                publisher ?? throw new InvalidOperationException("The assembly publisher is not attached.");

            private DerivedAssemblyPipeline Pipeline =>
                pipeline ?? throw new InvalidOperationException("The derived-assembly pipeline is not attached.");

            private LifecycleController Controller =>
                controller ?? throw new InvalidOperationException("The lifecycle controller is not attached.");

            private LifecycleStressResourceFactory Resources =>
                resources ?? throw new InvalidOperationException("The resource factory is not attached.");

            /// <summary>True when the one world this run builds reached its attached state (P-030).</summary>
            private bool Attached() =>
                host != null
                && lane != null
                && publisher != null
                && pipeline != null
                && controller != null
                && resources != null
                && manifests != null
                && bridge != null;

            private string DescribeMissing() =>
                "host=" + (host != null)
                + "; manifests=" + (manifests != null)
                + "; bridge=" + (bridge != null)
                + "; lane=" + (lane != null)
                + "; publisher=" + (publisher != null)
                + "; pipeline=" + (pipeline != null)
                + "; controller=" + (controller != null)
                + DescribeFailures();

            /// <summary>
            /// The next operation identity of this run's issuer. One sequence is shared by every identity the run
            /// mints, so a lane operation and a world-side publication can never collide (P-050).
            /// </summary>
            private OperationId NextOperation(WorldId target)
            {
                operationSequence++;
                return new OperationId(target, family.Issuer, operationSequence);
            }

            /// <summary>An operation identity for a lane submission, which creates one ledger row (P-050).</summary>
            private OperationId NextLaneOperation() => NextOperation(Host.World);

            /// <summary>
            /// An operation identity for a world-side publication. It consumes a sequence number so identities stay
            /// unique, but it is not a lane submission, so it creates no row.
            /// </summary>
            private OperationId NextWorldOperation() => NextOperation(Host.World);

            /// <summary>
            /// The next fresh plugin-instance identity: a cycle mounts a new installation, never the identity an
            /// earlier cycle disposed (P-004, P-005).
            /// </summary>
            private PluginInstanceId NextStressInstance()
            {
                instanceOrdinal++;
                return family.StressInstance(instanceOrdinal);
            }

            /// <summary>The activation token an installation would stamp its work with (P-047).</summary>
            private bool TryActivationToken(PluginInstanceId instance, uint workOrdinal, out AsyncWorkToken token)
            {
                token = default(AsyncWorkToken);
                CompositionHost laneRef = Lane;
                if (!laneRef.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return false;
                }

                if (entry.Record.Generation.Value == 0UL)
                {
                    return false;
                }

                token = new AsyncWorkToken(
                    NextWorldOperation(),
                    instance,
                    entry.Record.Generation,
                    entry.Record.ActivationEpoch,
                    workOrdinal);
                return true;
            }

            private ActivationStamp ActivationStampOf(PluginInstanceId instance)
            {
                CompositionHost laneRef = Lane;
                if (laneRef.Lifecycle.Activations.TryGetCurrent(instance, out ActivationAttempt? attempt)
                    && attempt != null)
                {
                    return attempt.Stamp();
                }

                if (laneRef.Committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null)
                {
                    return new ActivationStamp(entry.Record.Generation, entry.Record.ActivationEpoch);
                }

                return default(ActivationStamp);
            }

            private bool TryCurrentGeneration(PluginInstanceId instance, out ulong generation)
            {
                generation = 0UL;
                CompositionHost laneRef = Lane;
                if (!laneRef.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return false;
                }

                generation = entry.Record.Generation.Value;
                return true;
            }

            /// <summary>The published lifecycle state of one installation, or `&lt;absent&gt;` when it is not installed.</summary>
            private string StateOf(PluginInstanceId instance) =>
                TryInstallState(instance, out InstallationState state) ? state.ToString() : "<absent>";

            private bool TryInstallState(PluginInstanceId instance, out InstallationState state)
            {
                state = default(InstallationState);
                CompositionHost laneRef = Lane;
                if (!laneRef.Committed.TryGetInstall(instance, out InstallEntry? entry) || entry == null)
                {
                    return false;
                }

                state = entry.State;
                return true;
            }

            private int BindingCount(PluginInstanceId instance)
            {
                CompositionHost laneRef = Lane;
                return laneRef.Committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null
                    ? entry.Bindings.Count
                    : -1;
            }

            /// <summary>Derived binding rows of the published assembly attributed to one installation (P-017, P-033).</summary>
            private long AttributedRows(PluginInstanceId instance) => Controller.Binding.CountAttributedRows(instance);

            private long SafeAttributedRows(PluginInstanceId instance) =>
                controller != null ? controller.Binding.CountAttributedRows(instance) : -1L;

            /// <summary>Every incarnation of this run rendered as `instance@generation/epoch`.</summary>
            private string DescribeIncarnation(PluginInstanceId instance) =>
                instance.ToString() + "@generation" + Text(GenerationOf(instance)) + "/epoch" + Text(EpochOf(instance));

            private string IncarnationKey(PluginInstanceId instance) =>
                instance.Value.ToString() + "@" + Text(GenerationOf(instance)) + "/" + Text(EpochOf(instance));

            private ulong GenerationOf(PluginInstanceId instance) =>
                Lane.Committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null
                    ? entry.Record.Generation.Value
                    : 0UL;

            private ulong EpochOf(PluginInstanceId instance) =>
                Lane.Committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null
                    ? entry.Record.ActivationEpoch.Value
                    : 0UL;

            private bool TryGetWorldRecord(Id128 resourceId, out WorldResourceRecord record) =>
                Host.Ledger.TryGetResource(resourceId, out record);

            private static bool IsOpenState(ResourceRetirementState state) =>
                state == ResourceRetirementState.Acquired
                || state == ResourceRetirementState.Ready
                || state == ResourceRetirementState.Retiring
                || state == ResourceRetirementState.Quarantined;

            private static bool IsDisposed(LifecycleStressResourceFactory factory, Id128 leaseId) =>
                factory.TryGetLease(leaseId, out ScriptedResourceLease? lease) && lease != null && lease.IsDisposed;

            private static PublishedOperation? PublishedOf(IReadOnlyList<PublishedOperation> published, OperationId operation)
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

            private static Id128 LeaseIdOf(IReadOnlyList<StagedLease> leases, ResourceKey key)
            {
                for (int i = 0; i < leases.Count; i++)
                {
                    if (leases[i].Resource.Value.Equals(key.Value))
                    {
                        return leases[i].LeaseId;
                    }
                }

                return default(Id128);
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

            private static int CountOccurrences(IReadOnlyList<Id128> ids, Id128 candidate)
            {
                int count = 0;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (ids[i].Equals(candidate))
                    {
                        count++;
                    }
                }

                return count;
            }

            private static bool NamesExactly(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId instance) =>
                instances.Count == 1 && instances[0].Equals(instance);

            private static string Describe(IReadOnlyList<CallbackGateDecision> decisions)
            {
                var distinct = new List<string>();
                for (int i = 0; i < decisions.Count; i++)
                {
                    string text = decisions[i].ToString();
                    if (!distinct.Contains(text))
                    {
                        distinct.Add(text);
                    }
                }

                return string.Join(",", distinct.ToArray());
            }

            private static string Join(IReadOnlyList<string> values, int limit)
            {
                int count = values.Count < limit ? values.Count : limit;
                var text = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(" | ");
                    }

                    text.Append(values[i]);
                }

                if (values.Count > count)
                {
                    text.Append(" | ...");
                }

                return text.ToString();
            }

            private string DescribeFailures() =>
                buildFailure.Length == 0 ? string.Empty : "; buildFailure=" + buildFailure;

            private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);

            private static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);

            private static string DescribeException(Exception exception) =>
                "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
