// GameCore.Validation.ProbeHost — the `-probeBenchmark` player mode (GC-026).
//
// Runs the benchmark scenario inside the built player and writes its raw per-sample data beside the probe result. Two
// things only the player can prove are why this mode exists: the generated fixture, the statistics and the sample
// writers survive IL2CPP's managed stripping (they run through real generic instantiations and a real `long[]`
// counter set), and the measurement is taken on the standalone target the budgets are recorded against rather than in
// the Editor.
//
// THE FLAGS ARE VALUE FLAGS, AND THEY ARE PARSED HERE. `-probeBenchmark` itself is one boolean switch in
// `ProbeArguments`, exactly like every other mode, so the release-clone tooling keeps checking one known shape. The
// parameters are `-name=value` pairs read from this mode's own parser, which is the pattern the lifecycle-stress mode
// established for `-probeNativeLeakDetection=`. A malformed value is refused with a failing observation rather than
// silently substituted, because a benchmark that quietly rounded its duration down is not evidence.
//
// Artifacts written beside `-probeResult <path>`:
//   <dir>/<workloadId>.samples.json   the raw sample document of one workload (including its gates)
//   <dir>/<workloadId>.samples.csv    the wide per-sample matrix, one row per sample
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Benchmarks;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeBenchmark` player mode: the generated fixture, the workloads and the raw sample artifacts.</summary>
    public static class ProbeBenchmark
    {
        /// <summary>Workload selector: <c>all</c> or a comma-separated list of workload ids.</summary>
        public const string WorkloadArgument = "-probeBenchmarkWorkload=";

        /// <summary>Scopes of the pure generated composition; 08 declares 1,000.</summary>
        public const string ScopesArgument = "-probeBenchmarkScopes=";

        /// <summary>Targets of the pure generated composition; 08 declares 10,000.</summary>
        public const string TargetsArgument = "-probeBenchmarkTargets=";

        /// <summary>Scopes of the live ECS world; defaults to the pure scope count.</summary>
        public const string LiveScopesArgument = "-probeBenchmarkLiveScopes=";

        /// <summary>Targets of the live ECS world; defaults to the pure target count.</summary>
        public const string LiveTargetsArgument = "-probeBenchmarkLiveTargets=";

        /// <summary>Warmup seconds before a steady window; 08 declares 30.</summary>
        public const string WarmupArgument = "-probeBenchmarkWarmup=";

        /// <summary>Steady window seconds; 08 declares 120.</summary>
        public const string DurationArgument = "-probeBenchmarkDuration=";

        /// <summary>Repetitions of each change workload; zero uses each workload's declared default.</summary>
        public const string RepetitionsArgument = "-probeBenchmarkRepetitions=";

        /// <summary>Ordinal of this run, recorded in every document; the harness passes 1..N.</summary>
        public const string RunOrdinalArgument = "-probeBenchmarkRunOrdinal=";

        /// <summary>Recorded fixture seed.</summary>
        public const string SeedArgument = "-probeBenchmarkSeed=";

        /// <summary>Live targets the chapter provider's rule selects; 08's hundred-target plan row.</summary>
        public const string ApplyTargetsArgument = "-probeBenchmarkApplyTargets=";

        /// <summary>Live targets one spawn publication installs under the already-active provider.</summary>
        public const string LiveSpawnTargetsArgument = "-probeBenchmarkLiveSpawnTargets=";

        /// <summary>Frames the idle command-driven world is pumped.</summary>
        public const string IdleFramesArgument = "-probeBenchmarkIdleFrames=";

        /// <summary>Steps the unchanged-composition window commits.</summary>
        public const string UnchangedStepsArgument = "-probeBenchmarkUnchangedSteps=";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            try
            {
                string[] arguments = Environment.GetCommandLineArgs();
                if (!TryResolveOptions(arguments, out BenchmarkOptions? options, out string failure))
                {
                    report.Add(ProbeOutcome.Fail("benchmark-config", failure));
                    return;
                }

                BenchmarkOptions resolved = options!;
                BenchmarkScenarioResult result = BenchmarkScenario.Run(resolved);
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    BenchmarkStep step = result.Steps[i];
                    report.Add(step.Passed
                        ? ProbeOutcome.Pass(step.Name, step.Detail)
                        : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                WriteArtifacts(report, arguments, result);
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    "benchmark-scenario",
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }

        /// <summary>
        /// Resolves the run's configuration from the command line. Every value is validated; a value the documented
        /// grammar rejects fails the mode with the flag and the offending text rather than being rounded or ignored.
        /// </summary>
        public static bool TryResolveOptions(
            string[] arguments,
            out BenchmarkOptions? options,
            out string failure)
        {
            options = null;
            failure = string.Empty;

            int scopes = ReadInt(arguments, ScopesArgument, BenchmarkWorkloads.DefaultScopes, out string scopesFailure);
            int targets = ReadInt(arguments, TargetsArgument, BenchmarkWorkloads.DefaultTargets, out string targetsFailure);
            int liveScopes = ReadInt(arguments, LiveScopesArgument, scopes, out string liveScopesFailure);
            int liveTargets = ReadInt(arguments, LiveTargetsArgument, targets, out string liveTargetsFailure);
            int warmup = ReadInt(arguments, WarmupArgument, BenchmarkWorkloads.DefaultWarmupSeconds, out string warmupFailure);
            int duration = ReadInt(arguments, DurationArgument, BenchmarkWorkloads.DefaultDurationSeconds, out string durationFailure);
            int repetitions = ReadInt(arguments, RepetitionsArgument, 0, out string repetitionsFailure);
            int runOrdinal = ReadInt(arguments, RunOrdinalArgument, 1, out string runOrdinalFailure);
            int applyTargets = ReadInt(arguments, ApplyTargetsArgument, BenchmarkFixture.HundredTargets, out string applyFailure);
            int liveSpawnTargets = ReadInt(
                arguments, LiveSpawnTargetsArgument, BenchmarkWorkloads.SpawnTargets, out string spawnFailure);
            int idleFrames = ReadInt(arguments, IdleFramesArgument, BenchmarkScenario.IdleFrames, out string idleFailure);
            int unchangedSteps = ReadInt(
                arguments, UnchangedStepsArgument, BenchmarkScenario.UnchangedSteps, out string unchangedFailure);
            long seed = ReadLong(arguments, SeedArgument, BenchmarkWorkloads.DefaultSeed, out string seedFailure);

            if (FirstFailure(
                out failure,
                scopesFailure,
                targetsFailure,
                liveScopesFailure,
                liveTargetsFailure,
                warmupFailure,
                durationFailure,
                repetitionsFailure,
                runOrdinalFailure,
                applyFailure,
                spawnFailure,
                idleFailure,
                unchangedFailure,
                seedFailure))
            {
                return false;
            }

            if (scopes <= 0 || targets <= 0 || liveScopes <= 0 || liveTargets <= 0)
            {
                failure = "the fixture scale must be positive: scopes=" + scopes.ToString(CultureInfo.InvariantCulture)
                    + " targets=" + targets.ToString(CultureInfo.InvariantCulture)
                    + " liveScopes=" + liveScopes.ToString(CultureInfo.InvariantCulture)
                    + " liveTargets=" + liveTargets.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (warmup < 0 || duration < 0 || repetitions < 0 || idleFrames < 0 || unchangedSteps < 0)
            {
                failure = "a window, repetition, frame or step count is never negative ("
                    + warmup.ToString(CultureInfo.InvariantCulture) + "/"
                    + duration.ToString(CultureInfo.InvariantCulture) + "/"
                    + repetitions.ToString(CultureInfo.InvariantCulture) + "/"
                    + idleFrames.ToString(CultureInfo.InvariantCulture) + "/"
                    + unchangedSteps.ToString(CultureInfo.InvariantCulture) + ")";
                return false;
            }

            if (seed < 0L || seed > uint.MaxValue)
            {
                failure = SeedArgument + " needs a value in [0, 4294967295], got "
                    + seed.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            if (applyTargets <= 0 || applyTargets > liveTargets)
            {
                failure = ApplyTargetsArgument + " needs a value in [1, liveTargets], got "
                    + applyTargets.ToString(CultureInfo.InvariantCulture) + " with liveTargets="
                    + liveTargets.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            IReadOnlyList<BenchmarkWorkload> workloads = BenchmarkWorkloads.Select(
                ReadString(arguments, WorkloadArgument, "all"), out string selectFailure);
            if (workloads.Count == 0)
            {
                failure = selectFailure;
                return false;
            }

            options = new BenchmarkOptions(
                new BenchmarkScale(scopes, targets, liveScopes, liveTargets, (uint)seed),
                workloads,
                warmup,
                duration,
                repetitions,
                runOrdinal,
                applyTargets,
                liveSpawnTargets,
                idleFrames,
                unchangedSteps);
            return true;
        }

        /// <summary>
        /// Writes every workload's raw sample document and wide matrix in the directory of the probe result, then
        /// records one observation per format. A missing result path or a write failure is a reported failure rather
        /// than an exception, so a run that produced no raw data cannot look complete.
        /// </summary>
        private static void WriteArtifacts(
            ProbeReport report,
            string[] arguments,
            BenchmarkScenarioResult result)
        {
            string resultPath = string.Empty;
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (string.Equals(arguments[i], "-probeResult", StringComparison.Ordinal))
                {
                    resultPath = arguments[i + 1];
                    break;
                }
            }
            if (resultPath.Length == 0)
            {
                report.Add(ProbeOutcome.Fail(
                    "benchmark-artifacts-json", "no -probeResult path was supplied, so the raw samples have no home"));
                report.Add(ProbeOutcome.Fail(
                    "benchmark-artifacts-csv", "no -probeResult path was supplied, so the wide matrix has no home"));
                return;
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(resultPath)) ?? ".";
            var jsonNames = new List<string>(result.Documents.Count);
            var csvNames = new List<string>(result.Documents.Count);
            int jsonSamples = 0;
            int csvSamples = 0;

            try
            {
                for (int i = 0; i < result.Documents.Count; i++)
                {
                    BenchmarkRunDocument document = result.Documents[i];
                    string jsonPath = Path.Combine(directory, document.WorkloadId + ".samples.json");
                    string csvPath = Path.Combine(directory, document.WorkloadId + ".samples.csv");
                    string json = BenchmarkDocumentWriter.WriteJson(document);
                    string csv = BenchmarkDocumentWriter.WriteCsv(document);
                    File.WriteAllText(jsonPath, json);
                    File.WriteAllText(csvPath, csv);

                    // Read both back: a document whose bytes do not round-trip is not an artifact.
                    string jsonBack = File.ReadAllText(jsonPath);
                    string csvBack = File.ReadAllText(csvPath);
                    if (!string.Equals(json, jsonBack, StringComparison.Ordinal))
                    {
                        report.Add(ProbeOutcome.Fail(
                            "benchmark-artifacts-json",
                            "the JSON document of " + document.WorkloadId + " did not read back identically"));
                        return;
                    }

                    jsonSamples += document.Samples.Count;
                    csvSamples += CountRows(csvBack);
                    jsonNames.Add(Path.GetFileName(jsonPath));
                    csvNames.Add(Path.GetFileName(csvPath));
                }

                report.Add(ProbeOutcome.Pass(
                    "benchmark-artifacts-json",
                    "documents=" + jsonNames.Count.ToString(CultureInfo.InvariantCulture)
                    + "; samples=" + jsonSamples.ToString(CultureInfo.InvariantCulture)
                    + "; format=" + BenchmarkDocumentWriter.JsonFormat
                    + "; files=" + Join(jsonNames)));
                report.Add(ProbeOutcome.Pass(
                    "benchmark-artifacts-csv",
                    "documents=" + csvNames.Count.ToString(CultureInfo.InvariantCulture)
                    + "; rows=" + csvSamples.ToString(CultureInfo.InvariantCulture)
                    + "; format=" + BenchmarkDocumentWriter.CsvFormat
                    + "; columns=" + (BenchmarkDocumentWriter.CounterColumns().Count + 4).ToString(CultureInfo.InvariantCulture)
                    + "; files=" + Join(csvNames)));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    "benchmark-artifacts-json",
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
                report.Add(ProbeOutcome.Fail(
                    "benchmark-artifacts-csv",
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }

        /// <summary>Data rows of a written matrix: everything after the comment line and the header row.</summary>
        private static int CountRows(string csv)
        {
            int rows = 0;
            bool headerSeen = false;
            for (int i = 0; i < csv.Length; i++)
            {
                if (csv[i] != '\n')
                {
                    continue;
                }

                if (!headerSeen)
                {
                    // The first newline ends the format comment; the second ends the column header.
                    if (csv[0] == '#')
                    {
                        headerSeen = true;
                        continue;
                    }

                    headerSeen = true;
                    continue;
                }

                rows++;
            }

            return rows;
        }

        private static string Join(IReadOnlyList<string> values)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i != 0)
                {
                    builder.Append(',');
                }

                builder.Append(values[i]);
            }

            return builder.ToString();
        }

        private static bool FirstFailure(out string failure, params string[] failures)
        {
            for (int i = 0; i < failures.Length; i++)
            {
                if (!string.IsNullOrEmpty(failures[i]))
                {
                    failure = failures[i];
                    return true;
                }
            }

            failure = string.Empty;
            return false;
        }

        private static string ReadString(string[] arguments, string name, string fallback)
        {
            for (int i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].StartsWith(name, StringComparison.Ordinal))
                {
                    return arguments[i].Substring(name.Length);
                }
            }

            return fallback;
        }

        private static int ReadInt(string[] arguments, string name, int fallback, out string failure)
        {
            failure = string.Empty;
            string text = ReadString(arguments, name, string.Empty);
            if (text.Length == 0)
            {
                return fallback;
            }

            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            {
                failure = name + " expects an integer, got '" + text + "'";
                return fallback;
            }

            return value;
        }

        private static long ReadLong(string[] arguments, string name, long fallback, out string failure)
        {
            failure = string.Empty;
            string text = ReadString(arguments, name, string.Empty);
            if (text.Length == 0)
            {
                return fallback;
            }

            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                failure = name + " expects an integer, got '" + text + "'";
                return fallback;
            }

            return value;
        }
    }
}
