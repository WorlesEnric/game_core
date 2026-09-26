// GameCore.Validation.ProbeHost — the GC-026 benchmark scenario.
//
// One entry point, driven from the `-probeBenchmark` player mode and from the EditMode suite, so both run the same
// measurement. It has three parts that meet at one seam:
//
//   * **The pure half.** The generated 1,000-scope/10,000-target fixture goes through the real derivation engine and
//     the real incremental engine for the declared update sizes, the whole-world mode switch, the 1,000-target spawn,
//     the 100-target reparent and the 1,000 lifecycle cycles. Every sample's counters come from the derivation's own
//     `ITelemetryOwner` implementation, so the numbers are the kernel's own accounting rather than the runner's.
//   * **The live half.** Two real owned worlds — a command-driven one at the recorded live scale and a fixed-step one
//     for the unchanged-composition window — carry the measurements that need real ECS storage: the idle world's zero
//     steps, the fenced apply pause of a real plan affecting 100 targets, one real spawn publication under an active
//     provider, the authority mutation fixture and the memory categories.
//   * **The gates.** Zero stable control-tree scans and zero string service lookups are asserted, not merely measured,
//     and each zero is paired with a positive control: a counter that demonstrably moved somewhere in the same run.
//     Without that pairing a build whose counters are compiled out would report a perfect score, and 08 says a
//     main-thread-only or compiled-out reading cannot prove anything.
//
// Nothing here re-implements kernel behaviour: the derivation is the production engine, the world is the production
// host, the plan and the fenced apply are the production publisher's, and every counter read goes through the owner
// that owns it (GC-023).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using GameCore.Benchmarks;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Gameplay.Narrative;
using GameCore.Replay;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Everything one benchmark run needs, resolved by the caller from its command line.</summary>
    public sealed class BenchmarkOptions
    {
        public BenchmarkOptions(
            BenchmarkScale scale,
            IReadOnlyList<BenchmarkWorkload> workloads,
            int warmupSeconds,
            int durationSeconds,
            int repetitions,
            int runOrdinal,
            int applyTargets,
            int liveSpawnTargets,
            int idleFrames,
            int unchangedSteps)
        {
            Scale = scale;
            Workloads = workloads ?? throw new ArgumentNullException(nameof(workloads));
            WarmupSeconds = warmupSeconds;
            DurationSeconds = durationSeconds;
            Repetitions = repetitions;
            RunOrdinal = runOrdinal;
            ApplyTargets = applyTargets;
            LiveSpawnTargets = liveSpawnTargets;
            IdleFrames = idleFrames;
            UnchangedSteps = unchangedSteps;
        }

        public BenchmarkScale Scale { get; }

        public IReadOnlyList<BenchmarkWorkload> Workloads { get; }

        public int WarmupSeconds { get; }

        /// <summary>Requested steady window; zero means each steady workload uses its declared default.</summary>
        public int DurationSeconds { get; }

        /// <summary>Requested repetition count; zero means each change workload uses its declared default.</summary>
        public int Repetitions { get; }

        public int RunOrdinal { get; }

        /// <summary>Live targets the chapter provider's rule selects: 08's hundred-target plan row.</summary>
        public int ApplyTargets { get; }

        /// <summary>Live targets one spawn publication installs under the already-active provider.</summary>
        public int LiveSpawnTargets { get; }

        /// <summary>Frames the idle command-driven world is pumped.</summary>
        public int IdleFrames { get; }

        /// <summary>Steps the unchanged-composition window commits.</summary>
        public int UnchangedSteps { get; }

        public int DurationFor(BenchmarkWorkload workload) =>
            DurationSeconds > 0 ? DurationSeconds : workload.DefaultDurationSeconds;

        public int RepetitionsFor(BenchmarkWorkload workload) =>
            Repetitions > 0 ? Repetitions : workload.DefaultRepetitions;

        public string Describe() =>
            "scopes=" + Scale.Scopes.ToString(CultureInfo.InvariantCulture)
            + ";targets=" + Scale.Targets.ToString(CultureInfo.InvariantCulture)
            + ";liveScopes=" + Scale.LiveScopes.ToString(CultureInfo.InvariantCulture)
            + ";liveTargets=" + Scale.LiveTargets.ToString(CultureInfo.InvariantCulture)
            + ";applyTargets=" + ApplyTargets.ToString(CultureInfo.InvariantCulture)
            + ";liveSpawnTargets=" + LiveSpawnTargets.ToString(CultureInfo.InvariantCulture)
            + ";idleFrames=" + IdleFrames.ToString(CultureInfo.InvariantCulture)
            + ";unchangedSteps=" + UnchangedSteps.ToString(CultureInfo.InvariantCulture)
            + ";warmupSeconds=" + WarmupSeconds.ToString(CultureInfo.InvariantCulture)
            + ";durationSeconds=" + DurationSeconds.ToString(CultureInfo.InvariantCulture)
            + ";repetitions=" + Repetitions.ToString(CultureInfo.InvariantCulture)
            + ";runOrdinal=" + RunOrdinal.ToString(CultureInfo.InvariantCulture)
            + ";seed=" + Scale.Seed.ToString(CultureInfo.InvariantCulture)
            + ";workloads=" + WorkloadIds();

        /// <summary>Every selected workload id, comma separated; part of the recorded configuration.</summary>
        private string WorkloadIds()
        {
            var builder = new StringBuilder();
            for (int i = 0; i < Workloads.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                builder.Append(Workloads[i].Id);
            }

            return builder.ToString();
        }
    }

    /// <summary>One named observation of the benchmark: what was checked and the values computed for it.</summary>
    public sealed class BenchmarkStep
    {
        public BenchmarkStep(string name, bool passed, string detail)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        /// <summary>`key=value;...` fragments, so a pass flag always has its numbers beside it.</summary>
        public string Detail { get; }

        public override string ToString() => (Passed ? "pass " : "FAIL ") + Name + ": " + Detail;
    }

    /// <summary>The scenario's observations, its raw per-workload documents and its digest.</summary>
    public sealed class BenchmarkScenarioResult
    {
        public BenchmarkScenarioResult(
            IReadOnlyList<BenchmarkStep> steps,
            IReadOnlyList<BenchmarkRunDocument> documents,
            string digest)
        {
            Steps = steps;
            Documents = documents;
            Digest = digest ?? string.Empty;
        }

        public IReadOnlyList<BenchmarkStep> Steps { get; }

        public IReadOnlyList<BenchmarkRunDocument> Documents { get; }

        /// <summary>Digest over the named observations, so a renamed or dropped step changes it (P-008).</summary>
        public string Digest { get; }

        public bool AllPassed
        {
            get
            {
                if (Steps.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public string Describe()
        {
            int passed = 0;
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Passed)
                {
                    passed++;
                }
            }

            return "benchmark{observations=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + ";passed=" + passed.ToString(CultureInfo.InvariantCulture)
                + ";documents=" + Documents.Count.ToString(CultureInfo.InvariantCulture)
                + ";digest=" + Digest + "}";
        }
    }

    /// <summary>The GC-026 benchmark: the fixture, the workloads, the correctness gates and the raw documents.</summary>
    public static class BenchmarkScenario
    {
        public const string ConfigStepName = "benchmark-config";

        public const string HardwareStepName = "benchmark-hardware";

        public const string FixtureStepName = "benchmark-fixture";

        public const string GatesStepName = "benchmark-correctness-gates";

        public const string DigestStepName = "benchmark-digest";

        /// <summary>Prefix of every per-workload observation name.</summary>
        public const string WorkloadStepPrefix = "benchmark-";

        /// <summary>Steps the unchanged-composition window commits by default (TEST-023).</summary>
        public const int UnchangedSteps = 10000;

        /// <summary>Frames the idle command-driven world is pumped by default.</summary>
        public const int IdleFrames = 64;

        /// <summary>Observation name of one workload.</summary>
        public static string WorkloadStepName(string workloadId) => WorkloadStepPrefix + workloadId;

        /// <summary>Runs the whole benchmark; the result carries one raw document per selected workload.</summary>
        public static BenchmarkScenarioResult Run(BenchmarkOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            return new Executor(options).Run();
        }

        /// <summary>Digest over the observation sequence: one `name=result` line per observation (P-008).</summary>
        public static string DigestOf(IReadOnlyList<BenchmarkStep> steps)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < steps.Count; i++)
            {
                builder.Append("name=").Append(steps[i].Name)
                    .Append(" result=").Append(steps[i].Passed ? "pass" : "fail")
                    .Append('\n');
            }

            return ReplayStateHash.HashOf(builder.ToString()).ToHex();
        }

        private sealed class Executor
        {
            private const int FixedStepMaxStepsPerPump = 1000;

            /// <summary>1/60 s in the host tick domain the world's accumulator is given.</summary>
            private const ulong FixedStepDurationTicks = 166_667UL;

            /// <summary>Commands admitted per integer-rule step (08's budget row).</summary>
            private const int CommandsPerStep = 1000;

            /// <summary>Bound the integer rule saturates at, so a long run cannot wrap.</summary>
            private const int IntegerRuleBound = 1_000_000;

            /// <summary>Ceiling on the samples one steady window records, so a raw artifact stays bounded.</summary>
            private const int MaxSteadySamples = 20000;

            /// <summary>Floor of the warmup cycle cap: a very small scale still warms up for a few cycles.</summary>
            private const int WarmupCyclesCap = 64;

            /// <summary>
            /// The warmup cap as a multiple of the measured repetitions, so a warmup can never outlast the measurement
            /// it exists to protect however slow one change is.
            /// </summary>
            private const int WarmupCycleRatio = 2;

            /// <summary>
            /// The two-scale inactivity comparison repeats the workload's own declared repetition count at EACH scale,
            /// so its cost is twice the declaration. There is deliberately no separate cap: a cap would execute fewer
            /// repetitions than the document records, which is the silent truncation 08 forbids — if the declared count
            /// is wrong for this workload, the declaration is what changes.
            /// </summary>
            private readonly BenchmarkOptions options;
            private readonly List<BenchmarkStep> steps = new List<BenchmarkStep>();
            private readonly List<BenchmarkRunDocument> documents = new List<BenchmarkRunDocument>();

            private BenchmarkFixture fixture = null!;
            private DerivationOptions derivationOptions = null!;
            private string budgetText = string.Empty;
            private DerivationResult? warmupDerivation;
            private ulong pureRevision = 1UL;

            /// <summary>Positive control: `ControlNodesVisited` provably moved somewhere in this run.</summary>
            private bool sawControlWork;

            private BenchmarkLiveWorld? commandWorld;
            private BenchmarkLiveWorld? fixedStepWorld;
            private TelemetryCollector? worldCollector;
            private string liveFailure = string.Empty;

            private bool referenceAuthoritative;
            private string authorityDetail = "not-run";

            public Executor(BenchmarkOptions options)
            {
                this.options = options;
            }

            public BenchmarkScenarioResult Run()
            {
                PrepareFixture();
                AddConfigStep();
                AddHardwareStep();

                for (int i = 0; i < options.Workloads.Count; i++)
                {
                    BenchmarkWorkload workload = options.Workloads[i];
                    try
                    {
                        RunWorkload(workload);
                    }
                    catch (Exception exception)
                    {
                        steps.Add(new BenchmarkStep(
                            BenchmarkScenario.WorkloadStepName(workload.Id),
                            false,
                            "unhandled " + exception.GetType().FullName + ": " + exception.Message));
                    }
                }

                AddGateStep();
                TearDownLiveWorlds();
                AddDigestStep();
                return new BenchmarkScenarioResult(steps, documents, BenchmarkScenario.DigestOf(steps));
            }

            private void AddDigestStep()
            {
                string digest = BenchmarkScenario.DigestOf(steps);
                steps.Add(new BenchmarkStep(
                    BenchmarkScenario.DigestStepName,
                    digest.Length == 64 && documents.Count == options.Workloads.Count,
                    "observations=" + (steps.Count + 1).ToString(CultureInfo.InvariantCulture)
                    + "; documents=" + documents.Count.ToString(CultureInfo.InvariantCulture)
                    + "; workloads=" + options.Workloads.Count.ToString(CultureInfo.InvariantCulture)
                    + "; digest=" + digest));
            }

            // ------------------------------------------------------------------ preparation

            /// <summary>
            /// Generates the fixture and derives it once. That first derivation is the run's positive control: if it did
            /// not move `ControlNodesVisited`, then a later zero reading proves nothing about the kernel, and the gate
            /// step reports the instrumentation as unusable rather than as perfect.
            /// </summary>
            private void PrepareFixture()
            {
                fixture = BenchmarkFixtureGenerator.Generate(options.Scale);

                // The budget the fixture's own shape calls for (see BenchmarkFixture.SuggestedBudget): one definition
                // shared with the suite, so the two cannot disagree about what "the fixture was accepted" means.
                derivationOptions = new DerivationOptions(
                    null, null, fixture.SuggestedBudget, null, collectExplanations: true);

                budgetText = "referenceCandidates="
                    + PropagationBudget.DefaultMaxExaminedCandidates.ToString(CultureInfo.InvariantCulture)
                    + ";referenceContributions="
                    + PropagationBudget.DefaultMaxEmittedContributions.ToString(CultureInfo.InvariantCulture)
                    + ";referenceTargets="
                    + PropagationBudget.DefaultMaxAffectedTargets.ToString(CultureInfo.InvariantCulture)
                    + ";configured=" + derivationOptions.Budget;

                DerivationResult result = DerivationEngine.Derive(
                    BaseSnapshot(), fixture.Values, derivationOptions, null);
                warmupDerivation = result;
                if (result.Counters.ControlNodesVisited > 0L)
                {
                    sawControlWork = true;
                }

                steps.Add(new BenchmarkStep(
                    BenchmarkScenario.FixtureStepName,
                    result.Accepted
                    && fixture.Scopes.Count == options.Scale.Scopes
                    && fixture.Targets.Count == options.Scale.Targets
                    && fixture.ReparentScopeTargets == BenchmarkFixture.HundredTargets
                    && fixture.SchemaFamilyCount == BenchmarkFixture.SchemaFamilies,
                    "accepted=" + result.Accepted
                    + "; rejection=" + result.Rejection
                    + "; " + fixture.Describe()
                    + "; " + result.Counters.Describe()));
            }

            /// <summary>The fixture's base composition: no update provider mounted, at the current revision.</summary>
            private DerivationSnapshot BaseSnapshot() =>
                fixture.Builder()
                    .WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision))
                    .Build();

            private void AddConfigStep()
            {
                steps.Add(new BenchmarkStep(
                    BenchmarkScenario.ConfigStepName,
                    options.Workloads.Count > 0,
                    options.Describe()
                    + "; " + budgetText
                    + "; telemetrySymbol=" + TelemetrySchema.Symbol
                    + "; telemetryCompiledInProbeAssembly="
                    + (TelemetrySchema.IsCompiledIn ? "true" : "false")
                    + "; instrumentedKernelCountersMoved=" + (sawControlWork ? "true" : "unproven")));
            }

            private void AddHardwareStep()
            {
                steps.Add(new BenchmarkStep(BenchmarkScenario.HardwareStepName, true, HardwareDetail()));
            }

            /// <summary>
            /// The hardware and build identity every measurement must name (P-060, 08 s3): the machine, its processor
            /// count and architecture, the Unity version, the scripting backend, the stripping level, whether Burst is
            /// enabled, and the catalog identity the run derived over.
            /// </summary>
            private static string HardwareDetail() =>
                "machine=" + Environment.MachineName
                + "; processors=" + Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture)
                + "; os=" + Environment.OSVersion
                + "; architecture=" + ProbeEnvironment.Architecture
                + "; processorType=" + ProbeEnvironment.ProcessorType
                + "; unityVersion=" + ProbeEnvironment.UnityVersion
                + "; declaredUnityVersion=" + ProbeEnvironment.DeclaredUnityVersion
                + "; platform=" + ProbeEnvironment.Platform
                + "; scriptingBackend=" + ProbeEnvironment.ScriptingBackend
                + "; isIl2Cpp=" + ProbeEnvironment.IsIl2Cpp
                + "; is64BitProcess=" + ProbeEnvironment.Is64BitProcess
                + "; managedStrippingLevel=" + ProbeEnvironment.ManagedStrippingLevel
                + "; managedStrippingLevelSource=" + ProbeEnvironment.ManagedStrippingLevelSource
                + "; burstCompilerEnabled=" + ProbeEnvironment.BurstCompilerEnabled
                + "; catalogGeneratedFile=" + ProbeEnvironment.CatalogGeneratedFile
                + "; catalogFileHash=" + ProbeEnvironment.CatalogFileHash
                + "; catalogFingerprint=" + ProbeEnvironment.CatalogFingerprint;

            // ------------------------------------------------------------------ workload dispatch

            private void RunWorkload(BenchmarkWorkload workload)
            {
                var document = new BenchmarkRunDocument(
                    workload.Id,
                    workload.Kind,
                    workload.Dimension,
                    options.Scale.Scopes,
                    options.Scale.Targets,
                    options.DurationFor(workload),
                    options.WarmupSeconds,
                    workload.Kind == BenchmarkWorkloadKind.Change ? options.RepetitionsFor(workload) : 0,
                    options.Scale.Seed,
                    options.RunOrdinal);

                switch (workload.Id)
                {
                    case BenchmarkWorkloads.IdleCommandWorld:
                        RunIdleCommandWorld(document);
                        break;

                    case BenchmarkWorkloads.SteadyUnchanged:
                        RunSteadyUnchanged(document);
                        break;

                    case BenchmarkWorkloads.SteadyExecution:
                        RunSteadyExecution(document);
                        break;

                    case BenchmarkWorkloads.UpdateSizeOne:
                        RunUpdateSize(document, 1, workload);
                        break;

                    case BenchmarkWorkloads.UpdateSizeHundred:
                        RunUpdateSize(document, BenchmarkFixture.HundredTargets, workload);
                        RunLiveApply(document);
                        break;

                    case BenchmarkWorkloads.UpdateSizeWhole:
                        RunUpdateSize(document, options.Scale.Targets, workload);
                        break;

                    case BenchmarkWorkloads.ModeSwitch:
                        RunModeSwitch(document, workload);
                        break;

                    case BenchmarkWorkloads.SpawnThousand:
                        RunPureSpawn(document, workload);
                        RunLiveSpawn(document);
                        break;

                    case BenchmarkWorkloads.ReparentHundred:
                        RunReparent(document, workload);
                        break;

                    case BenchmarkWorkloads.InactiveComparison:
                        RunInactiveComparison(document, workload);
                        break;

                    case BenchmarkWorkloads.LifecycleCycles:
                        RunLifecycleCycles(document, workload);
                        break;

                    default:
                        document.Add(new BenchmarkGateResult(
                            "workload-known", false, "no runner is declared for workload '" + workload.Id + "'"));
                        break;
                }

                document.Note("fixture=" + fixture.Describe());
                document.Note("budget=" + budgetText);
                FinishWorkload(workload, document);
            }

            private void FinishWorkload(BenchmarkWorkload workload, BenchmarkRunDocument document)
            {
                if (document.Totals.IsZero)
                {
                    document.SetTotals(TotalsOf(document));
                }

                documents.Add(document);
                steps.Add(new BenchmarkStep(
                    BenchmarkScenario.WorkloadStepName(workload.Id), document.Passed, Describe(document)));
            }

            /// <summary>One workload's observation detail: its shape, its phases and the counters 08 names.</summary>
            private string Describe(BenchmarkRunDocument document)
            {
                var text = new StringBuilder();
                text.Append("kind=").Append(document.Kind.ToString())
                    .Append("; samples=").Append(document.Samples.Count.ToString(CultureInfo.InvariantCulture))
                    .Append("; repetitionsRequested=")
                    .Append(document.RepetitionsRequested.ToString(CultureInfo.InvariantCulture))
                    .Append("; repetitionsExecuted=")
                    .Append(document.RepetitionsExecuted.ToString(CultureInfo.InvariantCulture))
                    .Append("; steps=").Append(document.StepsAdvanced.ToString(CultureInfo.InvariantCulture))
                    .Append("; windowUs=").Append(document.WindowMicroseconds.ToString(CultureInfo.InvariantCulture))
                    .Append("; passed=").Append(document.Passed ? "true" : "false");

                for (int p = 0; p < BenchmarkDocumentWriter.PhaseOrder.Count; p++)
                {
                    BenchmarkPhase phase = BenchmarkDocumentWriter.PhaseOrder[p];
                    List<long> durations = document.DurationsOf(phase);
                    if (durations.Count == 0)
                    {
                        continue;
                    }

                    BenchmarkDistribution distribution = BenchmarkDistribution.Of(durations);
                    text.Append("; ").Append(phase.ToString().ToLowerInvariant())
                        .Append("(n=").Append(distribution.Count.ToString(CultureInfo.InvariantCulture))
                        .Append(",p50=").Append(distribution.P50.ToString(CultureInfo.InvariantCulture))
                        .Append(",p95=").Append(distribution.P95.ToString(CultureInfo.InvariantCulture))
                        .Append(",p99=").Append(distribution.P99.ToString(CultureInfo.InvariantCulture))
                        .Append(",max=").Append(distribution.Max.ToString(CultureInfo.InvariantCulture))
                        .Append(')');
                }

                text.Append("; counters=[").Append(document.Totals.Describe()).Append(']');
                text.Append("; memory=[").Append(document.Memory.Describe()).Append(']');
                for (int i = 0; i < document.Gates.Count; i++)
                {
                    text.Append("; gate:").Append(document.Gates[i].Name).Append('=')
                        .Append(document.Gates[i].Passed ? "pass" : "FAIL");
                }

                return text.ToString();
            }

            /// <summary>Accumulates one document's counter deltas into its totals, using the schema's own policy.</summary>
            private static TelemetryCounterSet TotalsOf(BenchmarkRunDocument document)
            {
                var totals = new TelemetryCounterSet();
                for (int s = 0; s < document.Samples.Count; s++)
                {
                    TelemetryCounterSet? counters = document.Samples[s].Counters;
                    if (counters != null)
                    {
                        totals.Merge(counters);
                    }
                }

                return totals;
            }

            /// <summary>One derivation's own counters, read through the production schema (GC-023).</summary>
            private static TelemetryCounterSet DerivationCountersOf(IncrementalDerivationOutcome outcome)
            {
                var counters = new TelemetryCounterSet();
                outcome.WriteTelemetry(counters);
                return counters;
            }

            // ------------------------------------------------------------------ the pure half

            /// <summary>
            /// One update-size loop: each repetition mounts a provider of the declared size and re-derives. The
            /// previous repetition's provider is retracted by the same derivation, so `contributions-added` and
            /// `contributions-retracted` are both reported and neither half of the change is hidden.
            /// </summary>
        private void RunUpdateSize(BenchmarkRunDocument document, int size, BenchmarkWorkload workload)
        {
            int repetitions = options.RepetitionsFor(workload);
            BenchmarkSnapshotBuilder builder = fixture.Builder();
            DerivationResult? previous = warmupDerivation;
            PluginInstanceId? mounted = null;
            bool accepted = true;
            int affected = 0;

            // 08's warmup rule: "Warm up for 30 seconds or until initialization/compilation/load work is complete,
            // whichever is later." For a repeated change that work is the first repetitions, so the loop runs them
            // until the declared warmup wall clock has elapsed (or the cap is reached) and records them as Warmup
            // samples, so the window the measured samples describe is never the window the runtime was still warming
            // in. Nothing about the warmup is discarded silently: its duration and cycle count go in the document.
            int warmupCycles = WarmUpChange(
                document, repetitions, out bool warmupComplete, (cycle, isFirst) =>
                {
                    if (mounted.HasValue)
                    {
                        builder.RemoveInstall(mounted.Value);
                        mounted = null;
                    }

                    pureRevision++;
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    DerivationInstall warm = BenchmarkFixtureVariants.UpdateInstall(fixture, size, cycle);
                    builder.AddInstall(warm);
                    mounted = warm.Instance;
                    DerivationSnapshot snapshot = builder.Build();
                    long started = Microseconds();
                    IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                        snapshot, fixture.Values, derivationOptions, previous, null, null);
                    long elapsed = Microseconds() - started;
                    previous = outcome.Result;
                    accepted &= outcome.Result.Accepted;
                    return elapsed;
                });

            document.Note("warmupCycles=" + warmupCycles.ToString(CultureInfo.InvariantCulture)
                + "; warmupComplete=" + (warmupComplete ? "true" : "false")
                + "; warmupSecondsRequested=" + options.WarmupSeconds.ToString(CultureInfo.InvariantCulture));

            for (int repetition = 0; repetition < repetitions; repetition++)
            {
                if (mounted.HasValue)
                {
                    builder.RemoveInstall(mounted.Value);
                    mounted = null;
                }

                pureRevision++;
                builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                DerivationInstall install = BenchmarkFixtureVariants.UpdateInstall(
                    fixture, size, warmupCycles + repetition);
                builder.AddInstall(install);
                mounted = install.Instance;

                DerivationSnapshot snapshot = builder.Build();
                long started = Microseconds();
                IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                    snapshot, fixture.Values, derivationOptions, previous, null, null);
                long elapsed = Microseconds() - started;

                previous = outcome.Result;
                accepted &= outcome.Result.Accepted;
                if (!outcome.Result.Accepted)
                {
                    document.Add(new BenchmarkGateResult(
                        "update-accepted",
                        false,
                        "the derivation was refused at repetition "
                        + repetition.ToString(CultureInfo.InvariantCulture) + ": " + outcome.Result.Rejection));
                    break;
                }

                affected = outcome.Result.Delta != null ? outcome.Result.Delta.Added.Count : 0;
                document.Add(new BenchmarkSample(
                    repetition, BenchmarkPhase.Prepare, elapsed, DerivationCountersOf(outcome)));
            }

            document.RepetitionsExecuted = document.DurationsOf(BenchmarkPhase.Prepare).Count;
            document.Add(new BenchmarkGateResult(
                "update-affects-the-declared-target-count",
                accepted && affected == fixture.TargetCountForSize(size),
                "size=" + size.ToString(CultureInfo.InvariantCulture)
                + "; affectedTargets=" + affected.ToString(CultureInfo.InvariantCulture)
                + "; expected=" + fixture.TargetCountForSize(size).ToString(CultureInfo.InvariantCulture)
                + "; repetitions=" + document.RepetitionsExecuted.ToString(CultureInfo.InvariantCulture)
                + "; warmupCycles=" + warmupCycles.ToString(CultureInfo.InvariantCulture)
                + "; accepted=" + accepted));
        }

            /// <summary>
            /// Runs 08's warmup for a *steady* window: <paramref name="cycle"/> is invoked until the declared warmup
            /// wall clock has elapsed. Returns the elapsed microseconds and sets <paramref name="cycles"/> to the number
            /// of cycles it ran.
            ///
            /// It records no per-cycle sample, and that is deliberate rather than an omission: a steady cycle is a few
            /// microseconds long, so a 30-second warmup at one sample per cycle would put hundreds of thousands of
            /// entries into a raw artifact whose whole purpose is to be re-analysable. The warmup's total duration and
            /// cycle count are recorded instead (the document's `WarmupMicroseconds` and a note), which is exactly what
            /// 08 asks a warmup to be reported as: a duration before the measured window, not a measurement.
            /// </summary>
            private long WarmUpSteady(Func<long> cycle, out int cycles)
            {
                long budgetMicroseconds = (long)options.WarmupSeconds * 1_000_000L;
                long spent = 0L;
                cycles = 0;
                while (spent < budgetMicroseconds)
                {
                    spent += cycle();
                    cycles++;
                }

                return spent;
            }

            /// <summary>
            /// Runs 08's warmup for a repeated change: <paramref name="cycle"/> is invoked until the declared warmup
            /// wall clock has elapsed, or until the warmup cap is reached, and each cycle's duration is recorded as a
            /// <see cref="BenchmarkPhase.Warmup"/> sample of the document. Returns the number of cycles it ran.
            ///
            /// A change cycle is a whole composition derivation, so there are tens of them rather than hundreds of
            /// thousands and recording each one is what keeps the warmup's own distribution visible.
            ///
            /// The cap exists because a slow change would otherwise spend an unbounded opening window; when the cap ends
            /// the warmup, <c>warmupComplete</c> is false and the document says so, rather than the run claiming a
            /// warmup it did not finish.
            /// </summary>
            private int WarmUpChange(
                BenchmarkRunDocument document,
                int measuredRepetitions,
                out bool warmupComplete,
                Func<int, bool, long> cycle)
            {
                int cap = Math.Max(WarmupCyclesCap, measuredRepetitions * WarmupCycleRatio);
                long budgetMicroseconds = (long)options.WarmupSeconds * 1_000_000L;
                long spent = 0L;
                int cycles = 0;
                while (spent < budgetMicroseconds && cycles < cap)
                {
                    long elapsed = cycle(cycles, cycles == 0);
                    spent += elapsed;
                    document.Add(new BenchmarkSample(cycles, BenchmarkPhase.Warmup, elapsed, null));
                    cycles++;
                }

                warmupComplete = spent >= budgetMicroseconds;
                document.WarmupMicroseconds = spent;
                return cycles;
            }

            /// <summary>The whole-world mode switch: Automatic to Conservative and back, one sample per switch.</summary>
            private void RunModeSwitch(BenchmarkRunDocument document, BenchmarkWorkload workload)
            {
                int repetitions = options.RepetitionsFor(workload);
                BenchmarkSnapshotBuilder builder = fixture.Builder();
                DerivationResult? previous = warmupDerivation;
                PropagationMode mode = PropagationMode.Automatic;
                bool accepted = true;
                int switches = 0;
                int local = 0;

                // 08's warmup rule, applied to this change exactly as the update loop applies it: the opening switches
                // run until the declared warmup wall clock elapses, recorded as Warmup samples, so the measured
                // switches describe a warmed runtime.
                int warmupSwitches = WarmUpChange(
                    document, repetitions, out bool warmupComplete, (cycle, isFirst) =>
                    {
                        mode = mode == PropagationMode.Automatic
                            ? PropagationMode.Conservative
                            : PropagationMode.Automatic;
                        pureRevision++;
                        builder.WithMode(mode)
                            .WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                        long started = Microseconds();
                        IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                            builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                        long elapsed = Microseconds() - started;
                        previous = outcome.Result;
                        accepted &= outcome.Result.Accepted;
                        return elapsed;
                    });

                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    mode = mode == PropagationMode.Automatic ? PropagationMode.Conservative : PropagationMode.Automatic;
                    pureRevision++;
                    builder.WithMode(mode)
                        .WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));

                    DerivationSnapshot snapshot = builder.Build();
                    long started = Microseconds();
                    IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                        snapshot, fixture.Values, derivationOptions, previous, null, null);
                    long elapsed = Microseconds() - started;

                    previous = outcome.Result;
                    accepted &= outcome.Result.Accepted;
                    if (!outcome.Result.Accepted)
                    {
                        document.Add(new BenchmarkGateResult(
                            "mode-switch-accepted",
                            false,
                            "the switch was refused at repetition "
                            + repetition.ToString(CultureInfo.InvariantCulture) + ": " + outcome.Result.Rejection));
                        break;
                    }

                    switches++;
                    if (!outcome.Invalidation.WholeWorld && !outcome.UsedFullRecompute)
                    {
                        local++;
                    }

                    document.Add(new BenchmarkSample(
                        repetition, BenchmarkPhase.Prepare, elapsed, DerivationCountersOf(outcome)));
                }

                document.Note("warmupSwitches=" + warmupSwitches.ToString(CultureInfo.InvariantCulture)
                    + "; warmupComplete=" + (warmupComplete ? "true" : "false"));

                document.RepetitionsExecuted = document.DurationsOf(BenchmarkPhase.Prepare).Count;
                document.Add(new BenchmarkGateResult(
                    "mode-switch-invalidates-the-whole-world",
                    accepted && switches > 0 && local == 0,
                    "switches=" + switches.ToString(CultureInfo.InvariantCulture)
                    + "; reportedLocal=" + local.ToString(CultureInfo.InvariantCulture)
                    + "; warmupSwitches=" + warmupSwitches.ToString(CultureInfo.InvariantCulture)
                    + "; accepted=" + accepted));
            }

            /// <summary>1,000 targets spawned under an already-active provider, then retired again (P-024).</summary>
            private void RunPureSpawn(BenchmarkRunDocument document, BenchmarkWorkload workload)
            {
                int repetitions = options.RepetitionsFor(workload);
                BenchmarkSnapshotBuilder builder = fixture.Builder();
                ScopeId scope = fixture.GroupScopes[1];

                pureRevision++;
                builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                DerivationResult? previous = DerivationEngine.Derive(
                    builder.Build(), fixture.Values, derivationOptions, null);
                bool accepted = previous != null && previous.Accepted;
                int assembled = 0;

                // 08's warmup rule: the opening spawn cycles run until the declared warmup wall clock elapses and are
                // recorded as Warmup samples. A warmup cycle spawns, derives and retires its own target set so each
                // one is a complete spawn at the workload's real scale.
                int warmupCycles = WarmUpChange(
                    document, repetitions, out bool warmupComplete, (cycle, isFirst) =>
                    {
                        if (cycle > 0)
                        {
                            builder.RemoveTargets(BenchmarkFixtureVariants.SpawnedTargetIds(
                                BenchmarkWorkloads.SpawnTargets, cycle - 1));
                        }

                        pureRevision++;
                        builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                        builder.AddTargets(BenchmarkFixtureVariants.SpawnTargets(
                            fixture, scope, BenchmarkWorkloads.SpawnTargets, cycle));
                        long started = Microseconds();
                        IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                            builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                        long elapsed = Microseconds() - started;
                        previous = outcome.Result;
                        accepted &= outcome.Result.Accepted;
                        return elapsed;
                    });

                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    if (repetition > 0)
                    {
                        builder.RemoveTargets(BenchmarkFixtureVariants.SpawnedTargetIds(
                            BenchmarkWorkloads.SpawnTargets, warmupCycles + repetition - 1));
                    }

                    pureRevision++;
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    IReadOnlyList<DerivationTarget> spawned = BenchmarkFixtureVariants.SpawnTargets(
                        fixture, scope, BenchmarkWorkloads.SpawnTargets, warmupCycles + repetition);
                    builder.AddTargets(spawned);

                    DerivationSnapshot snapshot = builder.Build();
                    long started = Microseconds();
                    IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                        snapshot, fixture.Values, derivationOptions, previous, null, null);
                    long elapsed = Microseconds() - started;

                    previous = outcome.Result;
                    accepted &= outcome.Result.Accepted;
                    if (!outcome.Result.Accepted)
                    {
                        document.Add(new BenchmarkGateResult(
                            "spawn-accepted",
                            false,
                            "the spawn derivation was refused at repetition "
                            + repetition.ToString(CultureInfo.InvariantCulture) + ": " + outcome.Result.Rejection));
                        break;
                    }

                    assembled = 0;
                    for (int i = 0; i < spawned.Count; i++)
                    {
                        // "first visible with its complete effective assembly" means the target really carries the
                        // capability the already-active provider derives, not merely that it appears in the result.
                        TargetAssembly? assembly = previous.AssemblyOf(spawned[i].Target);
                        if (assembly != null && assembly.HasCapability(fixture.SharedCapability))
                        {
                            assembled++;
                        }
                    }

                    document.Add(new BenchmarkSample(
                        repetition, BenchmarkPhase.Prepare, elapsed, DerivationCountersOf(outcome)));
                }

                document.Note("warmupCycles=" + warmupCycles.ToString(CultureInfo.InvariantCulture)
                    + "; warmupComplete=" + (warmupComplete ? "true" : "false"));

                document.RepetitionsExecuted = document.DurationsOf(BenchmarkPhase.Prepare).Count;
                document.Add(new BenchmarkGateResult(
                    "spawn-first-visibility-is-fully-assembled",
                    accepted && assembled == BenchmarkWorkloads.SpawnTargets,
                    "spawnedPerRepetition=" + BenchmarkWorkloads.SpawnTargets.ToString(CultureInfo.InvariantCulture)
                    + "; assembledOnFirstVisibility=" + assembled.ToString(CultureInfo.InvariantCulture)
                    + "; capability=" + fixture.SharedCapability.ToString()
                    + "; accepted=" + accepted));
            }

            /// <summary>The declared subtree moved between two providers and back (P-025).</summary>
            private void RunReparent(BenchmarkRunDocument document, BenchmarkWorkload workload)
            {
                int repetitions = options.RepetitionsFor(workload);
                BenchmarkSnapshotBuilder builder = fixture.Builder();
                DerivationResult? previous = warmupDerivation;
                bool accepted = true;

                int move = 0;

                // 08's warmup rule: the opening moves run until the declared warmup wall clock elapses and are recorded
                // as Warmup samples, so the measured moves describe a warmed runtime.
                int warmupMoves = WarmUpChange(
                    document, repetitions, out bool warmupComplete, (cycle, isFirst) =>
                    {
                        pureRevision++;
                        builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                        builder.ReparentScope(
                            fixture.ReparentScope,
                            cycle % 2 == 0 ? fixture.SecondProviderScope : fixture.RootScope);
                        long started = Microseconds();
                        IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                            builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                        long elapsed = Microseconds() - started;
                        previous = outcome.Result;
                        accepted &= outcome.Result.Accepted;
                        return elapsed;
                    });

                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    pureRevision++;
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    builder.ReparentScope(
                        fixture.ReparentScope,
                        (warmupMoves + repetition) % 2 == 0 ? fixture.SecondProviderScope : fixture.RootScope);

                    DerivationSnapshot snapshot = builder.Build();
                    long started = Microseconds();
                    IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                        snapshot, fixture.Values, derivationOptions, previous, null, null);
                    long elapsed = Microseconds() - started;

                    previous = outcome.Result;
                    accepted &= outcome.Result.Accepted;
                    if (!outcome.Result.Accepted)
                    {
                        document.Add(new BenchmarkGateResult(
                            "reparent-accepted",
                            false,
                            "the move was refused at repetition "
                            + repetition.ToString(CultureInfo.InvariantCulture) + ": " + outcome.Result.Rejection));
                        break;
                    }

                    move = repetition;
                    document.Add(new BenchmarkSample(
                        repetition, BenchmarkPhase.Prepare, elapsed, DerivationCountersOf(outcome)));
                }

                document.Note("warmupMoves=" + warmupMoves.ToString(CultureInfo.InvariantCulture)
                    + "; warmupComplete=" + (warmupComplete ? "true" : "false")
                    + "; lastMove=" + move.ToString(CultureInfo.InvariantCulture));

                document.RepetitionsExecuted = document.DurationsOf(BenchmarkPhase.Prepare).Count;
                document.Add(new BenchmarkGateResult(
                    "reparent-moves-the-declared-subtree",
                    accepted && fixture.ReparentScopeTargets == BenchmarkFixture.HundredTargets,
                    "subtreeTargets=" + fixture.ReparentScopeTargets.ToString(CultureInfo.InvariantCulture)
                    + "; accepted=" + accepted));
            }

            /// <summary>
            /// TEST-023: the same small active workload over 1,000 and over 10,000 inactive eligible targets. Both
            /// scales are sampled separately, so neither can be read as the other's average, and a hidden per-change
            /// scan would show up as the larger scale being materially slower.
            /// </summary>
            private void RunInactiveComparison(BenchmarkRunDocument document, BenchmarkWorkload workload)
            {
                int repetitions = options.RepetitionsFor(workload);
                int smallTargets = Math.Min(BenchmarkWorkloads.DefaultScopes, options.Scale.Targets);

                // Each scale is warmed up and then measured on the same workload, and each scale's measured repetitions
                // are sampled separately rather than summed: a sum would hide the distribution a hidden per-change scan
                // would show up in, and TEST-023's whole point here is comparing the two scales' behaviour.
                long small = MeasureInactive(document, smallTargets, repetitions, warmup: true);
                long large = MeasureInactive(document, options.Scale.Targets, repetitions, warmup: true);

                document.RepetitionsExecuted = repetitions;
                document.Note("smallScaleTargets=" + smallTargets.ToString(CultureInfo.InvariantCulture)
                    + "; largeScaleTargets=" + options.Scale.Targets.ToString(CultureInfo.InvariantCulture)
                    + "; smallUs=" + small.ToString(CultureInfo.InvariantCulture)
                    + "; largeUs=" + large.ToString(CultureInfo.InvariantCulture)
                    + "; repetitionsPerScale=" + repetitions.ToString(CultureInfo.InvariantCulture));
                document.Add(new BenchmarkGateResult(
                    "inactive-targets-do-not-add-a-per-change-scan",
                    small > 0L && large > 0L,
                    "smallTargets=" + smallTargets.ToString(CultureInfo.InvariantCulture)
                    + "; largeTargets=" + options.Scale.Targets.ToString(CultureInfo.InvariantCulture)
                    + "; smallUs=" + small.ToString(CultureInfo.InvariantCulture)
                    + "; largeUs=" + large.ToString(CultureInfo.InvariantCulture)
                    + "; ratio=" + (small > 0L
                        ? ((double)large / small).ToString("0.###", CultureInfo.InvariantCulture)
                        : "n/a")));
            }

            /// <summary>
            /// One small active workload over a fixture of exactly <paramref name="targets"/> targets: a single
            /// one-target-tag update repeated <paramref name="repetitions"/> times. Returns the total measured
            /// microseconds, and records every repetition as a Prepare sample of <paramref name="document"/> so the
            /// scale's distribution survives into the raw data.
            /// </summary>
            private long MeasureInactive(
                BenchmarkRunDocument document,
                int targets,
                int repetitions,
                bool warmup)
            {
                var scale = new BenchmarkScale(
                    Math.Max(BenchmarkFixtureGenerator.MinimumScopes, options.Scale.Scopes),
                    targets,
                    1,
                    1,
                    options.Scale.Seed);
                BenchmarkFixture scaled = BenchmarkFixtureGenerator.Generate(scale);

                // The scaled fixture carries its own shape-derived budget, so the two scales are measured under the
                // limits their own shapes call for rather than under the run fixture's.
                var scaledOptions = new DerivationOptions(
                    null, null, scaled.SuggestedBudget, null, collectExplanations: true);
                BenchmarkSnapshotBuilder builder = scaled.Builder();
                DerivationResult? previous = DerivationEngine.Derive(
                    builder.Build(), scaled.Values, scaledOptions, null);

                int warmupCycles = 0;
                if (warmup)
                {
                    // The scale's warmup needs its own installation identities, so the measured repetitions below
                    // start past the warmup's (P-004).
                    warmupCycles = WarmUpChange(
                        document, repetitions, out bool warmupComplete, (cycle, isFirst) =>
                        {
                            pureRevision++;
                            builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                            builder.AddInstall(BenchmarkFixtureVariants.UpdateInstall(scaled, 1, cycle));
                            long started = Microseconds();
                            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                                builder.Build(), scaled.Values, scaledOptions, previous, null, null);
                            long elapsed = Microseconds() - started;
                            previous = outcome.Result;
                            return elapsed;
                        });
                    document.Note("scale" + targets.ToString(CultureInfo.InvariantCulture)
                        + "warmupCycles=" + warmupCycles.ToString(CultureInfo.InvariantCulture)
                        + "; warmupComplete=" + (warmupComplete ? "true" : "false"));
                }

                long total = 0L;
                for (int repetition = 0; repetition < repetitions; repetition++)
                {
                    pureRevision++;
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    builder.AddInstall(BenchmarkFixtureVariants.UpdateInstall(
                        scaled, 1, warmupCycles + repetition));
                    DerivationSnapshot snapshot = builder.Build();
                    long started = Microseconds();
                    IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                        snapshot, scaled.Values, scaledOptions, previous, null, null);
                    long elapsed = Microseconds() - started;
                    previous = outcome.Result;
                    total += elapsed;
                    document.Add(new BenchmarkSample(
                        repetition, BenchmarkPhase.Prepare, elapsed, DerivationCountersOf(outcome)));
                }

                return total;
            }

            /// <summary>1,000 mount/unmount cycles with fixed retained data (P-046..P-048).</summary>
            private void RunLifecycleCycles(BenchmarkRunDocument document, BenchmarkWorkload workload)
            {
                int cycles = options.RepetitionsFor(workload);
                BenchmarkSnapshotBuilder builder = fixture.Builder();
                int baselineAssemblies = warmupDerivation != null ? warmupDerivation.Assemblies.Count : 0;
                int baselineContributions = warmupDerivation != null ? warmupDerivation.Contributions.Count : 0;
                DerivationResult? previous = warmupDerivation;
                bool accepted = true;
                int finalAssemblies = baselineAssemblies;
                int finalContributions = baselineContributions;


                // 08's warmup rule: the opening mount/unmount pairs run until the declared warmup wall clock elapses and
                // are recorded as Warmup samples. They use their own installation identities so a measured cycle's
                // identity is never reused by a warmup cycle (P-004).
                int warmupCycles = WarmUpChange(
                    document, cycles, out bool warmupComplete, (cycle, isFirst) =>
                    {
                        DerivationInstall warm = BenchmarkFixtureVariants.UpdateInstall(fixture, 1, cycle);
                        pureRevision++;
                        builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                        builder.AddInstall(warm);
                        long started = Microseconds();
                        IncrementalDerivationOutcome warmMount = IncrementalDerivationEngine.Derive(
                            builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                        previous = warmMount.Result;
                        accepted &= warmMount.Result.Accepted;

                        pureRevision++;
                        builder.RemoveInstall(warm.Instance);
                        builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                        IncrementalDerivationOutcome warmUnmount = IncrementalDerivationEngine.Derive(
                            builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                        long elapsed = Microseconds() - started;
                        previous = warmUnmount.Result;
                        accepted &= warmUnmount.Result.Accepted;
                        return elapsed;
                    });

                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    DerivationInstall install = BenchmarkFixtureVariants.UpdateInstall(
                        fixture, 1, warmupCycles + cycle);

                    pureRevision++;
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    builder.AddInstall(install);
                    long started = Microseconds();
                    IncrementalDerivationOutcome mountOutcome = IncrementalDerivationEngine.Derive(
                        builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                    long mounted = Microseconds() - started;
                    previous = mountOutcome.Result;
                    accepted &= mountOutcome.Result.Accepted;

                    pureRevision++;
                    builder.RemoveInstall(install.Instance);
                    builder.WithVersion(new CompositionRevision(pureRevision), new AssemblyEpoch(pureRevision));
                    started = Microseconds();
                    IncrementalDerivationOutcome unmountOutcome = IncrementalDerivationEngine.Derive(
                        builder.Build(), fixture.Values, derivationOptions, previous, null, null);
                    long unmounted = Microseconds() - started;
                    previous = unmountOutcome.Result;
                    accepted &= unmountOutcome.Result.Accepted;

                    if (!unmountOutcome.Result.Accepted)
                    {
                        document.Add(new BenchmarkGateResult(
                            "lifecycle-accepted",
                            false,
                            "the unmount derivation was refused at cycle "
                            + cycle.ToString(CultureInfo.InvariantCulture) + ": " + unmountOutcome.Result.Rejection));
                        break;
                    }

                    finalAssemblies = unmountOutcome.Result.Assemblies.Count;
                    finalContributions = unmountOutcome.Result.Contributions.Count;
                    document.Add(new BenchmarkSample(
                        cycle, BenchmarkPhase.Change, mounted, DerivationCountersOf(mountOutcome)));
                    document.Add(new BenchmarkSample(
                        cycle, BenchmarkPhase.Change, unmounted, DerivationCountersOf(unmountOutcome)));
                }

                document.Note("warmupCycles=" + warmupCycles.ToString(CultureInfo.InvariantCulture)
                    + "; warmupComplete=" + (warmupComplete ? "true" : "false"));
                document.RepetitionsExecuted = document.DurationsOf(BenchmarkPhase.Change).Count / 2;
                document.Add(new BenchmarkGateResult(
                    "lifecycle-counts-return-to-baseline",
                    accepted && finalAssemblies == baselineAssemblies
                        && finalContributions == baselineContributions,
                    "baselineAssemblies=" + baselineAssemblies.ToString(CultureInfo.InvariantCulture)
                    + "; finalAssemblies=" + finalAssemblies.ToString(CultureInfo.InvariantCulture)
                    + "; baselineContributions=" + baselineContributions.ToString(CultureInfo.InvariantCulture)
                    + "; finalContributions=" + finalContributions.ToString(CultureInfo.InvariantCulture)
                    + "; cycles=" + cycles.ToString(CultureInfo.InvariantCulture)
                    + "; accepted=" + accepted));
            }

            /// <summary>
            /// 08's core-execution row: 10,000 integer-rule targets with 1,000 admitted commands per step, using the
            /// recorded integer fixture's own rule function so the work is integer-exact. The window is measured step
            /// by step for the declared wall-clock budget, and the managed allocation of the whole window is read from
            /// a tracker that records which threads it actually covered.
            /// </summary>
            private void RunSteadyExecution(BenchmarkRunDocument document)
            {
                BenchmarkWorkload workload = FindWorkload(BenchmarkWorkloads.SteadyExecution);
                DerivationResult baseResult = warmupDerivation!;
                IReadOnlyList<TargetAssembly> assemblies = baseResult.Assemblies;
                int targets = assemblies.Count;
                var states = new int[targets];
                long budgetMicroseconds = (long)options.DurationFor(workload) * 1_000_000L;

                // This workload's work runs entirely on the calling thread, so a tracker that declares one thread and
                // samples it has complete coverage; a worker-thread allocation would be invisible and the tracker says
                // so rather than returning a thread-complete zero it did not observe.
                BenchmarkAllocationTracker allocation = BenchmarkAllocationTracker.Start(
                    1, BenchmarkThreadCoverage.AllThreadsSampled);

                // 08's warmup rule for a steady workload: run the same work until the declared warmup wall clock has
                // elapsed, with no per-step sample (see WarmUpSteady). The warmup steps mutate the same states the
                // measured steps do, which is correct: the rule is integer and bounded, so its state is part of the
                // workload rather than a measurement artifact.
                long warmupSpent = WarmUpSteady(
                    () =>
                    {
                        long warmupStarted = Microseconds();
                        for (int t = 0; t < targets; t++)
                        {
                            states[t] = IntegerRuleStage.Apply(assemblies[t], states[t], IntegerRuleBound);
                        }

                        return Microseconds() - warmupStarted;
                    },
                    out int warmupSteps);

                document.WarmupMicroseconds = warmupSpent;
                document.Note("warmupSteps=" + warmupSteps.ToString(CultureInfo.InvariantCulture)
                    + "; warmupMicroseconds=" + warmupSpent.ToString(CultureInfo.InvariantCulture));

                long window = 0L;
                int step = 0;
                long commands = 0L;
                var totals = new TelemetryCounterSet();

                // The window ends at the declared duration OR at the sample cap, so one raw artifact stays bounded;
                // both the achieved window and the cap are recorded (08 forbids an undisclosed short window).
                while (window < budgetMicroseconds && step < MaxSteadySamples)
                {
                    long started = Microseconds();
                    for (int t = 0; t < targets; t++)
                    {
                        states[t] = IntegerRuleStage.Apply(assemblies[t], states[t], IntegerRuleBound);
                    }

                    // The admitted commands of this step: one bounded batch touching a rotating window of targets, so
                    // the command path is exercised without inventing a second rule language (08's own fixture rule).
                    for (int c = 0; c < CommandsPerStep && targets > 0; c++)
                    {
                        int target = (int)(((long)step * CommandsPerStep + c) % targets);
                        states[target] = states[target] == IntegerRuleBound ? IntegerRuleBound : states[target] + 1;
                    }

                    long elapsed = Microseconds() - started;
                    window += elapsed;
                    step++;
                    commands += CommandsPerStep;

                    var sample = new TelemetryCounterSet();
                    sample.Add(TelemetryCounter.StepsAdvanced, 1L);
                    document.Add(new BenchmarkSample(step, BenchmarkPhase.Step, elapsed, sample));
                    totals.Add(TelemetryCounter.StepsAdvanced, 1L);
                }

                allocation.SampleCallingThread("main");
                document.StepsAdvanced = step;
                document.WindowMicroseconds = window;
                document.SetTotals(totals);
                document.Memory = BenchmarkMemoryCategories.FromCounters(
                    new TelemetryCounterSet(),
                    0L,
                    allocation.TotalBytes,
                    allocation.IsThreadComplete,
                    GC.GetTotalMemory(forceFullCollection: false),
                    GC.GetTotalMemory(forceFullCollection: false));
                document.Note("targets=" + targets.ToString(CultureInfo.InvariantCulture)
                    + "; commandsPerStep=" + CommandsPerStep.ToString(CultureInfo.InvariantCulture)
                    + "; committedCommands=" + commands.ToString(CultureInfo.InvariantCulture)
                    + "; bound=" + IntegerRuleBound.ToString(CultureInfo.InvariantCulture)
                    + "; sampleCap=" + MaxSteadySamples.ToString(CultureInfo.InvariantCulture)
                    + "; windowEndedByCap=" + (window < budgetMicroseconds ? "true" : "false")
                    + "; allocation=" + allocation.Describe());
                document.Add(new BenchmarkGateResult(
                    "core-execution-window",
                    step > 0 && window > 0L,
                    "steps=" + step.ToString(CultureInfo.InvariantCulture)
                    + "; windowUs=" + window.ToString(CultureInfo.InvariantCulture)
                    + "; budgetUs=" + budgetMicroseconds.ToString(CultureInfo.InvariantCulture)
                    + "; sampleCap=" + MaxSteadySamples.ToString(CultureInfo.InvariantCulture)
                    + "; windowEndedByCap=" + (window < budgetMicroseconds ? "true" : "false")
                    + "; targets=" + targets.ToString(CultureInfo.InvariantCulture)
                    + "; commands=" + commands.ToString(CultureInfo.InvariantCulture)));
                document.Add(new BenchmarkGateResult(
                    "steady-execution-allocation-is-thread-complete",
                    allocation.IsThreadComplete,
                    allocation.Describe()
                    + "; managedBytesPerStep="
                    + allocation.PerStep(step).ToString("0.###", CultureInfo.InvariantCulture)));
            }

            // ------------------------------------------------------------------ the live half

            private void RunIdleCommandWorld(BenchmarkRunDocument document)
            {
                if (!EnsureCommandWorld())
                {
                    document.Add(new BenchmarkGateResult(
                        "live-world", false, "the live world could not be built: " + liveFailure));
                    return;
                }

                BenchmarkLiveWorld world = commandWorld!;
                TelemetryCounterSet before = SampleWorld(world);
                ulong stepsBefore = world.Host.CurrentStep.Value;
                int commits = world.Host.Driver.CommittedStepCount;
                long budgetMicroseconds = (long)options.DurationFor(FindWorkload(BenchmarkWorkloads.IdleCommandWorld))
                    * 1_000_000L;
                long window = 0L;
                ulong steps = 0UL;
                int frames = 0;

                // 08's warmup rule for a steady point: pump the world until the declared warmup wall clock has elapsed,
                // with no per-frame sample (see WarmUpSteady). For an idle world this is the honest warmup — it proves
                // the zero-work claim holds across the whole opening window rather than only inside the measured one.
                long warmupSpent = WarmUpSteady(
                    () =>
                    {
                        long warmupStarted = Microseconds();
                        steps += world.PumpIdle(1);
                        return Microseconds() - warmupStarted;
                    },
                    out int warmupFrames);

                // The measured window's baseline is taken AFTER the warmup, so the idle counters a workload reports are
                // the ones the measured window moved. The zero-step claim still covers the warmup too, because `steps`
                // accumulates both loops.
                before = SampleWorld(world);
                document.WarmupMicroseconds = warmupSpent;
                document.Note("warmupFrames=" + warmupFrames.ToString(CultureInfo.InvariantCulture)
                    + "; warmupMicroseconds=" + warmupSpent.ToString(CultureInfo.InvariantCulture));

                // The window ends at the declared duration OR at the sample cap, whichever comes first; both the
                // achieved window and the cap go into the document, because a shortened window that is not disclosed
                // is exactly the kind of silent truncation 08 forbids.
                while (window < budgetMicroseconds && frames < MaxSteadySamples)
                {
                    long started = Microseconds();
                    steps += world.PumpIdle(1);
                    long elapsed = Microseconds() - started;
                    window += elapsed;
                    document.Add(new BenchmarkSample(frames, BenchmarkPhase.Step, elapsed, null));
                    frames++;
                }

                TelemetryCounterSet after = SampleWorld(world);
                TelemetryCounterSet delta = DeltaOf(before, after);
                document.StepsAdvanced = (long)steps;
                document.WindowMicroseconds += window;

                document.Add(new BenchmarkGateResult(
                    "idle-command-world-advances-zero-steps",
                    steps == 0UL
                    && world.Host.CurrentStep.Value == stepsBefore
                    && world.Host.Driver.CommittedStepCount == commits
                    && delta.Get(TelemetryCounter.StepsAdvanced) == 0L
                    && delta.Get(TelemetryCounter.StageSampleCount) == 0L,
                    "frames=" + frames.ToString(CultureInfo.InvariantCulture)
                    + "; sampleCap=" + MaxSteadySamples.ToString(CultureInfo.InvariantCulture)
                    + "; steps=" + steps.ToString(CultureInfo.InvariantCulture)
                    + "; stepsAdvanced=" + delta.Get(TelemetryCounter.StepsAdvanced).ToString(CultureInfo.InvariantCulture)
                    + "; stageSamples=" + delta.Get(TelemetryCounter.StageSampleCount).ToString(CultureInfo.InvariantCulture)
                    + "; dispatchRuns=" + world.Host.StepGroup.DispatchRunCount.ToString(CultureInfo.InvariantCulture)
                    + "; windowUs=" + window.ToString(CultureInfo.InvariantCulture)
                    + "; budgetUs=" + budgetMicroseconds.ToString(CultureInfo.InvariantCulture)));

                document.Add(new BenchmarkGateResult(
                    "idle-command-world-does-zero-control-work",
                    delta.Get(TelemetryCounter.ControlNodesVisited) == 0L
                    && delta.Get(TelemetryCounter.ServiceStringLookups) == 0L
                    && delta.Get(TelemetryCounter.CandidatesMatched) == 0L
                    && delta.Get(TelemetryCounter.StrataEvaluated) == 0L
                    && delta.Get(TelemetryCounter.PlanPreparedBytes) == 0L,
                    "controlNodes=" + delta.Get(TelemetryCounter.ControlNodesVisited).ToString(CultureInfo.InvariantCulture)
                    + "; serviceStringLookups="
                    + delta.Get(TelemetryCounter.ServiceStringLookups).ToString(CultureInfo.InvariantCulture)
                    + "; candidatesMatched="
                    + delta.Get(TelemetryCounter.CandidatesMatched).ToString(CultureInfo.InvariantCulture)
                    + "; strataEvaluated="
                    + delta.Get(TelemetryCounter.StrataEvaluated).ToString(CultureInfo.InvariantCulture)
                    + "; planPreparedBytes="
                    + delta.Get(TelemetryCounter.PlanPreparedBytes).ToString(CultureInfo.InvariantCulture)));

                document.Memory = MemoryOf(world);
                document.Note("liveWorld=" + DescribeWorld(world));
                document.Note("idleCounters=" + after.Describe());
            }

            /// <summary>
            /// The unchanged-composition window: a real fixed-step world commits the declared number of steps with
            /// nothing changing, and the control counters must not move at all (TEST-023, P-023).
            /// </summary>
            private void RunSteadyUnchanged(BenchmarkRunDocument document)
            {
                if (!EnsureFixedStepWorld())
                {
                    document.Add(new BenchmarkGateResult(
                        "steady-unchanged-window", false, "the fixed-step world could not be built: " + liveFailure));
                    return;
                }

                BenchmarkLiveWorld world = fixedStepWorld!;
                TelemetryCounterSet before = SampleWorld(world);
                int publicationsBefore = world.Publisher.PublicationCount;
                int target = options.UnchangedSteps > 0 ? options.UnchangedSteps : BenchmarkScenario.UnchangedSteps;
                ulong ticksPerPump = FixedStepDurationTicks * (ulong)FixedStepMaxStepsPerPump;
                int pumps = 0;
                long window = 0L;
                ulong committed = 0UL;

                // 08's warmup rule for a steady point: commit steps until the declared warmup wall clock has elapsed,
                // with no per-step sample (see WarmUpSteady). The baseline is re-read afterwards so the counters the
                // workload reports are the ones the measured window moved, while the zero-control-work claim still
                // covers the warmup, because the warmup runs before the baseline and any control work in it would move
                // the cumulative counters the workload's own delta is taken from.
                // The closure counts its own pumps, because an `out` parameter cannot be captured by the lambda it is
                // passed to; that counter is therefore the cycle count the document reports.
                int warmupPumps = 0;
                long warmupSpent = WarmUpSteady(
                    () =>
                    {
                        long warmupStarted = Microseconds();
                        ulong warmupTick = world.Host.HostTimeOrigin
                            + ((ulong)(warmupPumps + 1) * ticksPerPump);
                        world.Pump(warmupTick);
                        warmupPumps++;
                        return Microseconds() - warmupStarted;
                    },
                    out int _);

                before = SampleWorld(world);
                publicationsBefore = world.Publisher.PublicationCount;
                document.WarmupMicroseconds = warmupSpent;
                document.Note("warmupPumps=" + warmupPumps.ToString(CultureInfo.InvariantCulture)
                    + "; warmupMicroseconds=" + warmupSpent.ToString(CultureInfo.InvariantCulture));

                while (committed < (ulong)target && pumps < 100000)
                {
                    long started = Microseconds();
                    ulong tick = world.Host.HostTimeOrigin + ((ulong)(pumps + 1) * ticksPerPump);
                    ulong advanced = world.Pump(tick).StepsCommitted;
                    long elapsed = Microseconds() - started;
                    window += elapsed;
                    pumps++;
                    committed += advanced;
                    document.Add(new BenchmarkSample(pumps, BenchmarkPhase.Step, elapsed, null));
                    if (advanced == 0UL && pumps > 4)
                    {
                        // The accumulator retains debt rather than inventing steps (P-036); a window with no progress
                        // at all is reported instead of being padded out to the declared count.
                        break;
                    }
                }

                TelemetryCounterSet after = SampleWorld(world);
                TelemetryCounterSet delta = DeltaOf(before, after);
                bool stepsCommitted = committed >= (ulong)target;
                document.StepsAdvanced = (long)committed;
                document.WindowMicroseconds = window;

                document.Add(new BenchmarkGateResult(
                    "unchanged-composition-commits-the-declared-steps",
                    stepsCommitted,
                    "declared=" + target.ToString(CultureInfo.InvariantCulture)
                    + "; committed=" + committed.ToString(CultureInfo.InvariantCulture)
                    + "; pumps=" + pumps.ToString(CultureInfo.InvariantCulture)
                    + "; step=" + world.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                    + "; windowUs=" + window.ToString(CultureInfo.InvariantCulture)));

                document.Add(new BenchmarkGateResult(
                    "zero-stable-control-tree-scans",
                    stepsCommitted
                    && delta.Get(TelemetryCounter.ControlNodesVisited) == 0L
                    && delta.Get(TelemetryCounter.CandidatesMatched) == 0L
                    && delta.Get(TelemetryCounter.StrataEvaluated) == 0L
                    && delta.Get(TelemetryCounter.PlanPreparedBytes) == 0L,
                    "steps=" + committed.ToString(CultureInfo.InvariantCulture)
                    + "; controlNodes=" + delta.Get(TelemetryCounter.ControlNodesVisited).ToString(CultureInfo.InvariantCulture)
                    + "; candidatesMatched="
                    + delta.Get(TelemetryCounter.CandidatesMatched).ToString(CultureInfo.InvariantCulture)
                    + "; strataEvaluated="
                    + delta.Get(TelemetryCounter.StrataEvaluated).ToString(CultureInfo.InvariantCulture)
                    + "; planPreparedBytes="
                    + delta.Get(TelemetryCounter.PlanPreparedBytes).ToString(CultureInfo.InvariantCulture)));

                document.Add(new BenchmarkGateResult(
                    "zero-string-service-lookups",
                    stepsCommitted && delta.Get(TelemetryCounter.ServiceStringLookups) == 0L,
                    "serviceStringLookups="
                    + delta.Get(TelemetryCounter.ServiceStringLookups).ToString(CultureInfo.InvariantCulture)));

                document.Add(new BenchmarkGateResult(
                    "unchanged-composition-publishes-nothing",
                    world.Publisher.PublicationCount == publicationsBefore,
                    "publicationsBefore=" + publicationsBefore.ToString(CultureInfo.InvariantCulture)
                    + "; after=" + world.Publisher.PublicationCount.ToString(CultureInfo.InvariantCulture)));

                document.Memory = MemoryOf(world);
                document.Note("liveWorld=" + DescribeWorld(world));
            }

            /// <summary>
            /// The live apply measurement of 08's hundred-target row: a real mount whose rule selects exactly
            /// `applyTargets` existing targets, timed end to end, with the publisher's own fenced apply window and the
            /// driver's job-wait fence read from their own counters rather than estimated by the runner.
            /// </summary>
            private void RunLiveApply(BenchmarkRunDocument document)
            {
                if (!EnsureCommandWorld())
                {
                    document.Add(new BenchmarkGateResult(
                        "live-apply", false, "the live world could not be built: " + liveFailure));
                    return;
                }

                BenchmarkLiveWorld world = commandWorld!;
                int eligible = world.VillagerCount;
                TelemetryCounterSet before = SampleWorld(world);
                long started = Microseconds();
                DerivedAssemblyReport report = world.MountChapterProvider();
                long endToEnd = Microseconds() - started;
                TelemetryCounterSet after = SampleWorld(world);
                TelemetryCounterSet delta = DeltaOf(before, after);

                long apply = delta.Get(TelemetryCounter.ApplyDurationMicroseconds);
                long applySamples = delta.Get(TelemetryCounter.ApplySampleCount);
                long wait = delta.Get(TelemetryCounter.JobWaitDurationMicroseconds);
                long prepare = endToEnd - apply - wait;
                if (prepare < 0L)
                {
                    prepare = 0L;
                }

                document.Add(new BenchmarkSample(0, BenchmarkPhase.EndToEnd, endToEnd, delta));
                if (applySamples > 0L)
                {
                    document.Add(new BenchmarkSample(0, BenchmarkPhase.Apply, apply, null));
                }

                if (wait > 0L)
                {
                    document.Add(new BenchmarkSample(0, BenchmarkPhase.Wait, wait, null));
                }

                document.Add(new BenchmarkSample(0, BenchmarkPhase.Prepare, prepare, null));
                document.Note("applyPhase=the publisher's own fenced apply window (apply-us/apply-samples counters)"
                    + "; waitPhase=the execution driver's job-wait fence (job-wait-us counter)"
                    + "; preparePhase=endToEnd-apply-wait, so it includes the mandatory safety-boundary wait");

                document.Add(new BenchmarkGateResult(
                    "apply-affects-the-declared-target-count",
                    report.Outcome == DerivedAssemblyOutcome.Published
                    && eligible == options.ApplyTargets
                    && world.MatchesPublishedAssembly(),
                    "outcome=" + report.Outcome
                    + "; eligibleTargets=" + eligible.ToString(CultureInfo.InvariantCulture)
                    + "; declaredApplyTargets=" + options.ApplyTargets.ToString(CultureInfo.InvariantCulture)
                    + "; installedRows=" + report.InstalledRows.ToString(CultureInfo.InvariantCulture)
                    + "; retractedRows=" + report.RetractedRows.ToString(CultureInfo.InvariantCulture)
                    + "; applyUs=" + apply.ToString(CultureInfo.InvariantCulture)
                    + "; applySamples=" + applySamples.ToString(CultureInfo.InvariantCulture)
                    + "; waitUs=" + wait.ToString(CultureInfo.InvariantCulture)
                    + "; endToEndUs=" + endToEnd.ToString(CultureInfo.InvariantCulture)
                    + "; prepareUs=" + prepare.ToString(CultureInfo.InvariantCulture)
                    + "; joined=" + world.MatchesPublishedAssembly()));

                document.Add(new BenchmarkGateResult(
                    "apply-pause-is-measured-by-the-publisher",
                    applySamples > 0L,
                    "applySamples=" + applySamples.ToString(CultureInfo.InvariantCulture)
                    + "; applyUs=" + apply.ToString(CultureInfo.InvariantCulture)
                    + "; note=a zero apply sample count would make the apply-pause row unmeasurable rather than fast"));

                document.Note("liveWorld=" + DescribeWorld(world));
            }

            /// <summary>
            /// One real spawn under the already-active provider: the targets are seeded as storage and then a single
            /// publication installs every one of their assemblies (P-024), which is what the spawn row asks to be
            /// established as a baseline.
            /// </summary>
            private void RunLiveSpawn(BenchmarkRunDocument document)
            {
                if (!EnsureCommandWorld())
                {
                    document.Add(new BenchmarkGateResult(
                        "live-spawn", false, "the live world could not be built: " + liveFailure));
                    return;
                }

                BenchmarkLiveWorld world = commandWorld!;
                int count = options.LiveSpawnTargets;
                int villagerCountBefore = world.VillagerCount;
                TelemetryCounterSet before = SampleWorld(world);

                long started = Microseconds();
                bool seeded = world.TrySeedTargets(count, out string seedDetail);
                long seedElapsed = Microseconds() - started;
                if (!seeded)
                {
                    document.Add(new BenchmarkGateResult("live-spawn-seeded", false, seedDetail));
                    return;
                }

                started = Microseconds();
                DerivedAssemblyReport report = world.PublishDerivedNow("live-spawn-publication");
                long publishElapsed = Microseconds() - started;
                TelemetryCounterSet after = SampleWorld(world);
                TelemetryCounterSet delta = DeltaOf(before, after);

                int assembled = 0;
                DerivationResult? derivation = world.LastDerivation;
                if (derivation != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        if (derivation.AssemblyOf(BenchmarkFixtureVariants.LiveStateTarget(villagerCountBefore + i))
                            != null)
                        {
                            assembled++;
                        }
                    }
                }

                document.Add(new BenchmarkSample(0, BenchmarkPhase.Prepare, seedElapsed, null));
                document.Add(new BenchmarkSample(0, BenchmarkPhase.EndToEnd, publishElapsed, delta));
                document.Add(new BenchmarkSample(
                    0, BenchmarkPhase.Apply, delta.Get(TelemetryCounter.ApplyDurationMicroseconds), null));
                document.Note("seedPhase=bulk storage creation for " + count.ToString(CultureInfo.InvariantCulture)
                    + " targets (a fixture operation, measured separately so it cannot hide the publication)"
                    + "; applyPhase=the publisher's own fenced window for the ONE publication that installs every"
                    + " spawned target's assembly");
                document.Add(new BenchmarkGateResult(
                    "live-spawn-publishes-one-complete-publication",
                    report.Outcome == DerivedAssemblyOutcome.Published && assembled == count,
                    "spawned=" + count.ToString(CultureInfo.InvariantCulture)
                    + "; assembled=" + assembled.ToString(CultureInfo.InvariantCulture)
                    + "; outcome=" + report.Outcome
                    + "; installedRows=" + report.InstalledRows.ToString(CultureInfo.InvariantCulture)
                    + "; seedUs=" + seedElapsed.ToString(CultureInfo.InvariantCulture)
                    + "; publishUs=" + publishElapsed.ToString(CultureInfo.InvariantCulture)
                    + "; applyUs="
                    + delta.Get(TelemetryCounter.ApplyDurationMicroseconds).ToString(CultureInfo.InvariantCulture)));
                document.Memory = MemoryOf(world);
                document.Note("liveWorld=" + DescribeWorld(world));
            }

            // ------------------------------------------------------------------ gates and world helpers

            /// <summary>
            /// The gates that are not workload-local: the instrumentation must be live (positive control), no workload
            /// may record a string service lookup, no workload may fail, and the authority mutation fixture must show
            /// that the world's own storage is the only authority.
            /// </summary>
            private void AddGateStep()
            {
                long stringLookups = 0L;
                long controlNodes = 0L;
                int failed = 0;
                for (int i = 0; i < documents.Count; i++)
                {
                    BenchmarkRunDocument document = documents[i];
                    stringLookups += document.Totals.Get(TelemetryCounter.ServiceStringLookups);
                    controlNodes += document.Totals.Get(TelemetryCounter.ControlNodesVisited);
                    if (!document.Passed)
                    {
                        failed++;
                    }
                }

                bool pass = sawControlWork
                    && referenceAuthoritative
                    && failed == 0
                    && stringLookups == 0L
                    && documents.Count == options.Workloads.Count;

                steps.Add(new BenchmarkStep(
                    BenchmarkScenario.GatesStepName,
                    pass,
                    "positiveControlInstrumentedKernelCountersMoved=" + (sawControlWork ? "true" : "false")
                    + "; controlNodesInWarmupDerivation="
                    + (warmupDerivation != null
                        ? warmupDerivation.Counters.ControlNodesVisited.ToString(CultureInfo.InvariantCulture)
                        : "0")
                    + "; telemetrySymbol=" + TelemetrySchema.Symbol
                    + "; telemetryCompiledInProbeAssembly="
                    + (TelemetrySchema.IsCompiledIn ? "true" : "false")
                    + "; controlNodesAcrossWorkloads=" + controlNodes.ToString(CultureInfo.InvariantCulture)
                    + "; stringServiceLookupsAcrossWorkloads="
                    + stringLookups.ToString(CultureInfo.InvariantCulture)
                    + "; workloadsFailed=" + failed.ToString(CultureInfo.InvariantCulture)
                    + "; documents=" + documents.Count.ToString(CultureInfo.InvariantCulture)
                    + "; declaredWorkloads=" + options.Workloads.Count.ToString(CultureInfo.InvariantCulture)
                    + "; authorityMirrorFixture=" + authorityDetail));
            }

            private bool EnsureCommandWorld()
            {
                if (commandWorld != null)
                {
                    return true;
                }

                bool created = BenchmarkLiveWorld.TryCreate(
                    options.Scale.LiveScopes,
                    options.Scale.LiveTargets,
                    Math.Min(options.ApplyTargets, options.Scale.LiveTargets),
                    options.Scale.Seed,
                    false,
                    out BenchmarkLiveWorld? world,
                    out LiveWorldFailure? failure);
                commandWorld = world;
                liveFailure = failure != null ? failure.ToString() : string.Empty;
                if (!created || world == null)
                {
                    return false;
                }

                EnsureCollector();
                ReplayScenario.RegisterOwners(worldCollector!, world.Host);

                // The authority fixture runs on the first world built: an authoritative slot is written through the
                // world's own owner path and read back through the world's own reader, while the derived binding rows
                // must not move. A managed mirror of the slot would move one of those readings and fail this.
                referenceAuthoritative = RunAuthorityFixture(world);
                return true;
            }

            /// <summary>
            /// The mutation fixture TEST-023 asks for: a write to authoritative owner state must land in ECS storage
            /// and must not be readable as derived configuration (and vice versa), so an adapter or service mirror
            /// cannot be authoritative. The architecture dependency check is re-run in the same step.
            /// </summary>
            private bool RunAuthorityFixture(BenchmarkLiveWorld world)
            {
                TargetId target = world.FirstVillager;
                if (target.IsDefault)
                {
                    authorityDetail = "no live target to mutate";
                    return false;
                }

                var slot = new SlotId(BenchmarkIds.CapabilityValue("gc026.authority.slot"));
                OwnerId owner = NarrativeKeys.DialogueOwner;
                uint version = NarrativeKeys.ConversationDomain.Version;

                if (!world.TryWriteSlot(target, owner, slot, version, 11, out string firstDetail))
                {
                    authorityDetail = "the owner write was refused: " + firstDetail;
                    return false;
                }

                bool readFirst = world.TryReadSlot(target, owner, slot, out int observedFirst);
                IReadOnlyList<CapabilityBinding> rowsBefore = world.ReadBindingRows(target);
                int rowsBeforeValue = RowValueOf(rowsBefore);

                if (!world.TryWriteSlot(target, owner, slot, version, 12, out string secondDetail))
                {
                    authorityDetail = "the second owner write was refused: " + secondDetail;
                    return false;
                }

                bool readSecond = world.TryReadSlot(target, owner, slot, out int observedSecond);
                IReadOnlyList<CapabilityBinding> rowsAfter = world.ReadBindingRows(target);
                int rowsAfterValue = RowValueOf(rowsAfter);

                LoadedAssemblyReport audit = KernelAssemblyAudit.AuditLoadedAssemblies();
                bool pass = readFirst
                    && readSecond
                    && observedFirst == 11
                    && observedSecond == 12
                    && rowsBefore.Count == rowsAfter.Count
                    && rowsBeforeValue == rowsAfterValue
                    && audit.Clean;

                authorityDetail =
                    "target=" + target.ToString()
                    + "; slotWrites=11->12"
                    + "; slotReads=" + (readFirst ? observedFirst.ToString(CultureInfo.InvariantCulture) : "<none>")
                    + "->" + (readSecond ? observedSecond.ToString(CultureInfo.InvariantCulture) : "<none>")
                    + "; bindingRows=" + rowsBefore.Count.ToString(CultureInfo.InvariantCulture)
                    + "->" + rowsAfter.Count.ToString(CultureInfo.InvariantCulture)
                    + "; bindingValue=" + rowsBeforeValue.ToString(CultureInfo.InvariantCulture)
                    + "->" + rowsAfterValue.ToString(CultureInfo.InvariantCulture)
                    + "; kernelAuditClean=" + (audit.Clean ? "true" : "false")
                    + "; kernelAssemblies=" + audit.KernelAssemblies.Count.ToString(CultureInfo.InvariantCulture)
                    + "; forbiddenKernelReferences=" + audit.ForbiddenReferences.Count.ToString(CultureInfo.InvariantCulture);
                return pass;
            }

            /// <summary>
            /// The first binding row's integer value, or the sentinel when the target has no row: the comparison is
            /// between two readings of the same row, so a value that moved when the slot changed would mean the row
            /// mirrors authoritative state.
            /// </summary>
            private static int RowValueOf(IReadOnlyList<CapabilityBinding> rows) =>
                rows.Count == 0 ? int.MinValue : rows[0].Value;

            private bool EnsureFixedStepWorld()
            {
                if (fixedStepWorld != null)
                {
                    return true;
                }

                int scopes = Math.Min(options.Scale.LiveScopes, 64);
                int targets = Math.Min(options.Scale.LiveTargets, Math.Max(1, options.ApplyTargets));
                bool created = BenchmarkLiveWorld.TryCreate(
                    scopes,
                    targets,
                    targets,
                    options.Scale.Seed + 1U,
                    true,
                    out BenchmarkLiveWorld? world,
                    out LiveWorldFailure? failure);
                fixedStepWorld = world;
                if (!created || world == null)
                {
                    liveFailure = failure != null ? failure.ToString() : "the fixed-step world was not created";
                    return false;
                }

                EnsureCollector();
                ReplayScenario.RegisterOwners(worldCollector!, world.Host);
                return true;
            }

            private void EnsureCollector()
            {
                if (worldCollector == null)
                {
                    worldCollector = new TelemetryCollector(new TelemetryRetention(4096, 64));
                }
            }

            /// <summary>
            /// Samples every registered owner of one world and returns the aggregate. Control work observed here is the
            /// run's positive control; a workload takes the difference between two samples.
            /// </summary>
            private TelemetryCounterSet SampleWorld(BenchmarkLiveWorld world)
            {
                if (worldCollector == null)
                {
                    return new TelemetryCounterSet();
                }

                TelemetryFrame frame = worldCollector.Sample(0, world.Host.CurrentEpoch, world.Host.CurrentStep);
                TelemetryCounterSet aggregate = frame.Aggregate();
                if (aggregate.Get(TelemetryCounter.ControlNodesVisited) > 0L)
                {
                    sawControlWork = true;
                }

                return aggregate;
            }

            /// <summary>
            /// The difference between two samples, using the schema's own aggregation policy: cumulative work is
            /// subtracted, gauges and facts keep the later value, because a difference between two high-water marks is
            /// not a measurement.
            /// </summary>
            private static TelemetryCounterSet DeltaOf(TelemetryCounterSet before, TelemetryCounterSet after)
            {
                var delta = new TelemetryCounterSet();
                for (int i = 0; i < TelemetrySchema.CounterCount; i++)
                {
                    var counter = (TelemetryCounter)i;
                    long now = after.Get(counter);
                    if (TelemetrySchema.AggregationOf(counter) == TelemetryAggregation.Max)
                    {
                        delta.Set(counter, now);
                        continue;
                    }

                    long value = now - before.Get(counter);
                    delta.Add(counter, value < 0L ? 0L : value);
                }

                return delta;
            }

            /// <summary>
            /// The memory categories of one live world, read from the owners that hold the bytes. The managed reading
            /// is a single thread's and is reported as not thread-complete, because a live world's work can reach
            /// worker threads; the pure steady-execution workload carries the thread-complete reading instead.
            /// </summary>
            private BenchmarkMemoryCategories MemoryOf(BenchmarkLiveWorld world)
            {
                TelemetryCounterSet counters = SampleWorld(world);
                long nativeBytes = (long)world.Host.Ledger.RetainedResourceBytes;
                long heapNow = GC.GetTotalMemory(forceFullCollection: false);
                return BenchmarkMemoryCategories.FromCounters(
                    counters,
                    nativeBytes,
                    GC.GetAllocatedBytesForCurrentThread(),
                    false,
                    heapNow,
                    heapNow);
            }

            private static string DescribeWorld(BenchmarkLiveWorld world) =>
                "liveScopes=" + world.ScopeCount.ToString(CultureInfo.InvariantCulture)
                + "; liveTargets=" + world.TargetCount.ToString(CultureInfo.InvariantCulture)
                + "; eligible=" + world.VillagerCount.ToString(CultureInfo.InvariantCulture)
                + "; ineligible=" + world.IneligibleCount.ToString(CultureInfo.InvariantCulture)
                + "; epoch=" + world.Host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                + "; step=" + world.Host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                + "; publications=" + world.Publisher.PublicationCount.ToString(CultureInfo.InvariantCulture)
                + "; bindingRows=" + world.Publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                + "; joined=" + world.MatchesPublishedAssembly();

            private void TearDownLiveWorlds()
            {
                if (commandWorld != null)
                {
                    commandWorld.Dispose();
                    commandWorld = null;
                }

                if (fixedStepWorld != null)
                {
                    fixedStepWorld.Dispose();
                    fixedStepWorld = null;
                }
            }

            private BenchmarkWorkload FindWorkload(string id)
            {
                if (BenchmarkWorkloads.TryGet(id, out BenchmarkWorkload workload))
                {
                    return workload;
                }

                throw new InvalidOperationException("the benchmark catalogue lost workload '" + id + "'");
            }

            private static long Microseconds() =>
                TelemetryDurations.TicksToMicroseconds(Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        }
    }
}
