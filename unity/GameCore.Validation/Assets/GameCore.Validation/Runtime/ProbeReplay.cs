// GameCore.Validation.ProbeHost — the `-probeReplay` player mode (GC-023).
//
// Runs the replay and instrumentation scenario inside the built player. Two things are proved here that no
// Editor-only run can prove: the replay fixtures and the telemetry schema survive IL2CPP's managed stripping (the
// suite exercises them through real generic instantiations and a real `long[]` counter set), and a real owned world
// reports its own counters through the compact schema *in the player*.
//
// The digest below is recomputed from the observed steps in this process, so a renamed observation, a dropped
// observation or one failing step cannot report the same value. The literal comparison lives in
// `tools/unity/run_replay_probe.sh`: the first build-host run records the digest into
// `tests/GameCore.Replay/Data/replay-record.json`, and the harness requires it thereafter (it is skipped while the
// recorded value is still empty, so an unrecorded digest can never pass as a recorded one).
//
// The raw benchmark trace is written next to the probe result (`<result>.trace` and `<result>.trace.json`), which is
// the measurement artifact 08 s3 asks a cost trace to be.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Replay;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeReplay` player mode: replay, differential propagation and complete cost instrumentation.</summary>
    public static class ProbeReplay
    {
        /// <summary>Expected number of named observations; a dropped observation changes the report shape.</summary>
        public const int ExpectedObservations = 12;

        /// <summary>Expected logical steps of the recorded fixture (TEST-022).</summary>
        public const int ExpectedRecordedSteps = 10000;

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            try
            {
                ReplayScenarioResult result = ReplayScenario.RunAll();
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    ReplayStep step = result.Steps[i];
                    report.Add(step.Passed
                        ? ProbeOutcome.Pass(step.Name, step.Detail)
                        : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The digest is the observation table's canonical hash, recomputed here from the same steps, so it
                // is a statement about this run rather than a remembered string.
                string recomputed = ReplayScenario.DigestOf(result.Steps);
                bool digestStable = string.Equals(recomputed, result.Digest, StringComparison.Ordinal)
                    && recomputed.Length == 64;
                report.Add(digestStable
                    ? ProbeOutcome.Pass(
                        "replay-digest",
                        "observations=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                        + "; digest=" + result.Digest
                        + "; " + result.Describe())
                    : ProbeOutcome.Fail(
                        "replay-digest",
                        "the digest does not describe this run's observations: recomputed=" + recomputed
                        + " reported=" + result.Digest));

                bool shapeHeld = result.Steps.Count == ExpectedObservations;
                report.Add(shapeHeld
                    ? ProbeOutcome.Pass(
                        "replay-observation-count",
                        "named observations=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                        + "; expected=" + ExpectedObservations.ToString(CultureInfo.InvariantCulture)
                        + "; steps=" + ExpectedRecordedSteps.ToString(CultureInfo.InvariantCulture))
                    : ProbeOutcome.Fail(
                        "replay-observation-count",
                        "the scenario reported " + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                        + " observations instead of " + ExpectedObservations.ToString(CultureInfo.InvariantCulture)));

                WriteTraceArtifacts(report, result);
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    "replay-scenario",
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }

        /// <summary>
        /// Writes the raw benchmark trace and its JSON form beside the probe result. A missing result path or a write
        /// failure is a reported failure rather than an exception, so a probe without artifacts cannot look complete.
        /// </summary>
        private static void WriteTraceArtifacts(ProbeReport report, ReplayScenarioResult result)
        {
            ProbeArguments arguments = ProbeArguments.Parse(Environment.GetCommandLineArgs());
            string? resultPath = arguments.ResultPath;
            if (string.IsNullOrEmpty(resultPath) || result.RawTrace.Length == 0)
            {
                report.Add(ProbeOutcome.Fail(
                    "replay-benchmark-trace",
                    "no result path or no trace was produced; path='" + (resultPath ?? "<none>") + "'"));
                return;
            }

            try
            {
                string rawPath = resultPath + ".trace";
                string jsonPath = resultPath + ".trace.json";
                File.WriteAllText(rawPath, result.RawTrace);
                File.WriteAllText(jsonPath, result.JsonTrace);
                bool readBack = BenchmarkTrace.TryRead(
                    File.ReadAllText(rawPath), out var frames, out string failure);
                report.Add(readBack
                    ? ProbeOutcome.Pass(
                        "replay-benchmark-trace",
                        "frames=" + frames.Count.ToString(CultureInfo.InvariantCulture)
                        + "; rawBytes=" + result.RawTrace.Length.ToString(CultureInfo.InvariantCulture)
                        + "; jsonBytes=" + result.JsonTrace.Length.ToString(CultureInfo.InvariantCulture)
                        + "; chainRoundTrips=true")
                    : ProbeOutcome.Fail(
                        "replay-benchmark-trace",
                        "the written trace did not read back: " + failure));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    "replay-benchmark-trace",
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
