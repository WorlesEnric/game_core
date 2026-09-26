// GameCore.Benchmarks — the GC-026 workload catalogue.
//
// Normative source: docs/game-core/08-validation-and-performance.md section "Instrumentation and reproducible
// performance method" and "Provisional budgets and failure decisions", plus GC-026's brief. 08 names the workloads
// and the measurement rule (a deterministic fixture whose seed and shape are recorded; 30 s warmup; five independent
// 120-second runs for steady workloads; at least 1,000 repetitions for small composition changes; per-sample
// p50/p95/p99/max and total counts; idle worlds reported as zero steps rather than divided by a fabricated tick
// count). GC-026's brief names the runner parameters (workload/duration/warmup).
//
// This file is the catalogue as data. Nothing here measures anything: a workload declares *what* is run, *how* the
// budget is expressed (a duration window or a repetition count) and *why* the declaration is the one 08 asks for.
// The player runner executes the catalogue; the summarizer compares the measured numbers to the budget rows in
// `PerformanceBudgets`. Keeping the catalogue in one place is what stops a gate from quietly dropping a workload:
// every consumer iterates `All`, and the observation count the harness requires is derived from it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Benchmarks
{
    /// <summary>
    /// How a workload's measurement window is declared. 08 asks for two different things and conflating them would
    /// hide a miss: a steady workload is measured for a wall-clock duration, a composition change is measured over a
    /// repetition count.
    /// </summary>
    public enum BenchmarkWorkloadKind
    {
        /// <summary>A steady window measured for <see cref="BenchmarkWorkload.DefaultDurationSeconds"/> (120 s after warmup).</summary>
        Steady = 0,

        /// <summary>A repeated change measured over <see cref="BenchmarkWorkload.DefaultRepetitions"/> repetitions.</summary>
        Change = 1,
    }

    /// <summary>One declared workload of the 08 method, with the reason its window is shaped the way it is.</summary>
    public sealed class BenchmarkWorkload
    {
        public BenchmarkWorkload(
            string id,
            BenchmarkWorkloadKind kind,
            string dimension,
            string description,
            int defaultDurationSeconds,
            int defaultRepetitions)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentException("A workload needs a stable id.", nameof(id));
            }

            if (defaultDurationSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultDurationSeconds));
            }

            if (defaultRepetitions < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(defaultRepetitions));
            }

            if (kind == BenchmarkWorkloadKind.Steady && defaultDurationSeconds <= 0)
            {
                throw new ArgumentException(
                    "A steady workload is measured for a positive duration or it cannot be reported as steady.",
                    nameof(defaultDurationSeconds));
            }

            if (kind == BenchmarkWorkloadKind.Change && defaultRepetitions <= 0)
            {
                throw new ArgumentException(
                    "A change workload is measured over a positive repetition count or it has no samples.",
                    nameof(defaultRepetitions));
            }

            Id = id;
            Kind = kind;
            Dimension = dimension ?? string.Empty;
            Description = description ?? string.Empty;
            DefaultDurationSeconds = defaultDurationSeconds;
            DefaultRepetitions = defaultRepetitions;
        }

        /// <summary>Stable id: the report key, the CSV key and the CLI selector. Never a display name.</summary>
        public string Id { get; }

        public BenchmarkWorkloadKind Kind { get; }

        /// <summary>The budget dimension this workload exists to measure, e.g. <c>update-size</c> or <c>idle</c>.</summary>
        public string Dimension { get; }

        /// <summary>What 08 asks for and which protocol rule the workload is evidence about.</summary>
        public string Description { get; }

        public int DefaultDurationSeconds { get; }

        public int DefaultRepetitions { get; }

        public override string ToString() =>
            Id + "(" + Kind.ToString() + ", duration=" + DefaultDurationSeconds.ToString(CultureInfo.InvariantCulture)
            + "s, repetitions=" + DefaultRepetitions.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The workloads of the 08 method and the parameters GC-026's runner accepts. The ids are stable because they
    /// are report keys: a renamed workload changes the raw sample document and the summary table, and the harness
    /// requires every id to appear in the result.
    /// </summary>
    public static class BenchmarkWorkloads
    {
        /// <summary>P-036/P-037: a command-driven world with no demand advances zero steps and does zero control work.</summary>
        public const string IdleCommandWorld = "idle-command-world";

        /// <summary>TEST-023: an unchanged composition over 10,000 steps makes zero control-tree visits and zero string lookups.</summary>
        public const string SteadyUnchanged = "steady-unchanged-10000-steps";

        /// <summary>08's budget row: 10,000 integer-rule targets with 1,000 active commands per fixed step, core p95 at most 4 ms.</summary>
        public const string SteadyExecution = "steady-execution-10000-targets";

        /// <summary>08's first update size: a composition change whose effective assembly changes for exactly 1 target.</summary>
        public const string UpdateSizeOne = "update-size-1";

        /// <summary>08's second update size: a composition change affecting exactly 100 targets.</summary>
        public const string UpdateSizeHundred = "update-size-100";

        /// <summary>08's third update size: a composition change affecting all 10,000 targets.</summary>
        public const string UpdateSizeWhole = "update-size-10000";

        /// <summary>08's fourth update size: a whole-world propagation-mode switch (P-014).</summary>
        public const string ModeSwitch = "whole-world-mode-switch";

        /// <summary>08: 1,000-target spawns under an already-active provider (P-024).</summary>
        public const string SpawnThousand = "spawn-1000";

        /// <summary>08: reparent a 100-target subtree between two providers (P-025).</summary>
        public const string ReparentHundred = "reparent-100";

        /// <summary>TEST-023: 1,000 versus 10,000 inactive eligible targets running the same small active workload.</summary>
        public const string InactiveComparison = "inactive-target-comparison";

        /// <summary>08: 1,000 lifecycle cycles with fixed retained data return to a plateau (P-046..P-048).</summary>
        public const string LifecycleCycles = "lifecycle-cycles-1000";

        /// <summary>Default scope count of the 08 fixture: 1,000 scopes.</summary>
        public const int DefaultScopes = 1000;

        /// <summary>Default target count of the 08 fixture: 10,000 targets.</summary>
        public const int DefaultTargets = 10000;

        /// <summary>Default warmup in seconds; 08: "Warm up for 30 seconds or until initialization work is complete".</summary>
        public const int DefaultWarmupSeconds = 30;

        /// <summary>Default measurement window of one steady workload, in seconds.</summary>
        public const int DefaultDurationSeconds = 120;

        /// <summary>Default number of independent runs of the whole runner. 08: "five independent 120-second runs".</summary>
        public const int DefaultRuns = 5;

        /// <summary>Default repetition count of a small composition change. 08: "at least 1,000 repetitions".</summary>
        public const int DefaultChangeRepetitions = 1000;

        /// <summary>Repetitions of a change whose single occurrence already costs one whole-world derivation.</summary>
        public const int HeavyChangeRepetitions = 200;

        /// <summary>Targets the reparent workload moves, and the update size the hundred-target workload emits.</summary>
        public const int HundredTargets = 100;

        /// <summary>Targets the spawn workload creates under an already-active provider.</summary>
        public const int SpawnTargets = 1000;

        /// <summary>Lifecycle cycles the unload workload runs; 08 says 1,000 cycles with fixed retained data.</summary>
        public const int LifecycleCycleCount = 1000;

        /// <summary>Recorded fixture seed; the shape and seed both travel in every raw sample document (08 s3).</summary>
        public const uint DefaultSeed = 20260926U;

        /// <summary>The declared catalogue, in report order. Every consumer iterates this list.</summary>
        public static readonly IReadOnlyList<BenchmarkWorkload> All = Array.AsReadOnly(new[]
        {
            new BenchmarkWorkload(
                IdleCommandWorld,
                BenchmarkWorkloadKind.Steady,
                "idle",
                "A command-driven world with no admitted demand: zero simulation steps and zero simulation-stage updates "
                + "for the whole window (P-036, P-037). 08: report zero steps and control-plane wake counts rather "
                + "than dividing by a fabricated tick count.",
                DefaultDurationSeconds,
                0),
            new BenchmarkWorkload(
                SteadyUnchanged,
                BenchmarkWorkloadKind.Steady,
                "no-composition-change",
                "The composition is left unchanged for at least 10,000 committed steps (TEST-023). The delta of "
                + "ControlNodesVisited and ServiceStringLookups over the window must be exactly zero, which is the "
                + "regression a per-step propagation scan would break.",
                DefaultDurationSeconds,
                0),
            new BenchmarkWorkload(
                SteadyExecution,
                BenchmarkWorkloadKind.Steady,
                "core-execution",
                "10,000 integer-rule targets with 1,000 active commands per fixed step (08's provisional budget row: "
                + "core execution p95 at most 4 ms). The rule stage is the recorded integer fixture of TEST-022, so the "
                + "work is integer-exact and the per-step duration is comparable sample by sample.",
                DefaultDurationSeconds,
                0),
            new BenchmarkWorkload(
                UpdateSizeOne,
                BenchmarkWorkloadKind.Change,
                "update-size",
                "A composition change (one provider mount plus its derivation) whose effective assembly changes for "
                + "exactly one target. 08: at least 1,000 repetitions for small composition changes.",
                0,
                DefaultChangeRepetitions),
            new BenchmarkWorkload(
                UpdateSizeHundred,
                BenchmarkWorkloadKind.Change,
                "update-size",
                "The same change against a rule that selects exactly 100 targets by declared tag.",
                0,
                DefaultChangeRepetitions),
            new BenchmarkWorkload(
                UpdateSizeWhole,
                BenchmarkWorkloadKind.Change,
                "update-size",
                "The same change against a rule whose selector reaches every target, so all 10,000 effective "
                + "assemblies change. Fewer repetitions, because each one is a whole-world publication.",
                0,
                HeavyChangeRepetitions),
            new BenchmarkWorkload(
                ModeSwitch,
                BenchmarkWorkloadKind.Change,
                "mode-switch",
                "Automatic to Conservative and back over the whole world (P-013, P-014). A mode switch legitimately "
                + "invalidates the world, so this workload measures the reported whole-world cost rather than "
                + "pretending it is local.",
                0,
                HeavyChangeRepetitions),
            new BenchmarkWorkload(
                SpawnThousand,
                BenchmarkWorkloadKind.Change,
                "future-spawn",
                "1,000 targets spawned under an already-active provider, each first visible with its complete derived "
                + "assembly (P-024), then retired again so the next repetition starts from the same scale.",
                0,
                HeavyChangeRepetitions),
            new BenchmarkWorkload(
                ReparentHundred,
                BenchmarkWorkloadKind.Change,
                "reparent",
                "A 100-target subtree moved between two providers and back (P-025). The move must retract the old "
                + "inherited support and derive the new one for exactly the moved subtree.",
                0,
                DefaultChangeRepetitions),
            new BenchmarkWorkload(
                InactiveComparison,
                BenchmarkWorkloadKind.Change,
                "inactive-targets",
                "The same small active workload with 1,000 and with 10,000 inactive eligible targets (TEST-023). The "
                + "inactive scope count must not introduce a hidden per-step propagation scan, so the two measured "
                + "durations are reported side by side rather than as one average.",
                0,
                DefaultChangeRepetitions),
            new BenchmarkWorkload(
                LifecycleCycles,
                BenchmarkWorkloadKind.Change,
                "lifecycle",
                "1,000 mount/unmount cycles with fixed retained data (08's budget row). The workload asserts the "
                + "counter baseline, not just the duration; GC-022 owns the in-world native-leak half of the same claim.",
                0,
                LifecycleCycleCount),
        });

        /// <summary>Finds one workload by its exact id.</summary>
        public static bool TryGet(string id, out BenchmarkWorkload workload)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].Id, id, StringComparison.Ordinal))
                {
                    workload = All[i];
                    return true;
                }
            }

            workload = null!;
            return false;
        }

        /// <summary>
        /// Resolves the runner's <c>-probeBenchmarkWorkload</c> selector: <c>all</c> (the default) or a comma-separated
        /// list of exact ids, in catalogue order. An unknown id is refused with the list of known ids instead of being
        /// skipped, because a silently skipped workload is a silently shortened gate.
        /// </summary>
        public static IReadOnlyList<BenchmarkWorkload> Select(string? specification, out string failure)
        {
            failure = string.Empty;
            if (string.IsNullOrEmpty(specification)
                || string.Equals(specification, "all", StringComparison.Ordinal))
            {
                return All;
            }

            var selected = new List<BenchmarkWorkload>();
            string[] parts = specification!.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string id = parts[i].Trim();
                if (id.Length == 0)
                {
                    continue;
                }

                if (!TryGet(id, out BenchmarkWorkload workload))
                {
                    failure = "unknown benchmark workload '" + id + "'; known ids are " + JoinIds();
                    return Array.Empty<BenchmarkWorkload>();
                }

                if (!selected.Contains(workload))
                {
                    selected.Add(workload);
                }
            }

            if (selected.Count == 0)
            {
                failure = "the workload selector named no workload; use 'all' or a comma-separated list of " + JoinIds();
                return Array.Empty<BenchmarkWorkload>();
            }

            // Catalogue order, not request order: a report that is a permutation of the catalogue is different
            // evidence although it measured the same work (P-008's stable-ordering habit).
            var ordered = new List<BenchmarkWorkload>(selected.Count);
            for (int i = 0; i < All.Count; i++)
            {
                if (selected.Contains(All[i]))
                {
                    ordered.Add(All[i]);
                }
            }

            return ordered;
        }

        /// <summary>Every workload id, comma separated; the error text and the harness's step list both use it.</summary>
        public static string JoinIds()
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < All.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                builder.Append(All[i].Id);
            }

            return builder.ToString();
        }
    }
}
