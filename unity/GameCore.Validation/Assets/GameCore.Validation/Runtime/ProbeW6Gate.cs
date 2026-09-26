// GameCore.Validation.ProbeHost — the Wave 6 gate probe (`-probeW6Gate`).
//
// Runs the Wave 6 gate scenario inside the built IL2CPP player: over each of the three genres, the committed generated
// catalog (or the catalog the genre really owns) and, for the two genres that have one, the hand-written
// generated-style catalog as well. Every observation is reported into the same structured JSON result as the
// GC-001/GC-005/Wave-1..5 probes, so the gate's player evidence has the shape every earlier gate's evidence has.
//
// The scenario (`W6GateScenario` over the three family adapters) is shared with the Unity EditMode assembly, so the
// same checks execute in the Editor and in a stripped player. The digest literals below are the values over the frozen
// observation names in the fixed emission order (P-008, P-028): a renamed observation, a different step count or a
// single failing step cannot report them. A fixture-catalog run's names carry the `fixture:` prefix, so its literal
// differs from the generated run's — which is exactly what makes one literal per catalog worth claiming. The
// traversal course has ONE literal, because this revision has no committed generated traversal catalog (that emission
// is GC-025's work) and the gate says so rather than implying a second catalog ran.
//
// Three process-level observations come first and none of them can fail the run by throwing:
//
//   * the resolved cycle count (`W6GateScenario.CycleCount`, honouring `GC_W6_GATE_CYCLES`), so the archived result
//     names how much churn it measured rather than leaving it to the launch script;
//   * the native leak detection mode of this process. A 1,000-cycle teardown run is only evidence about native
//     allocations when leak detection with full stack traces was actually armed (`UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE`
//     for the Collections package's Editor hook, and the same variable or `-probeNativeLeakDetection=<0|1|2>` here),
//     and `tools/attribute_native_leaks.py` then attributes this player's own report against the resource policy;
//   * the telemetry build shape (`GAMECORE_TELEMETRY`), because the cost counters this gate integrates are compiled out
//     of a release build by design and must be compiled IN for the qualification player's counters to record anything.
//
// Nothing here enables audio (crash-139) and nothing here passes `-quit`: the runner exits through Application.Quit
// with the code that encodes its result.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using Unity.Collections;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeW6Gate` player mode: the Wave 6 gate over all three genres and every catalog they own.</summary>
    public static class ProbeW6Gate
    {
        /// <summary>Step naming the cycle count the scenario resolved for this process.</summary>
        public const string CycleCountStepName = "w6-gate-cycle-count";

        /// <summary>Step naming the process's native leak detection mode.</summary>
        public const string LeakDetectionStepName = "w6-gate-native-leak-detection";

        /// <summary>Step naming the telemetry build shape this process was compiled with.</summary>
        public const string TelemetryStepName = "w6-gate-telemetry-build-shape";

        /// <summary>Environment variable both halves of the leak-detection enablement read (0/1/2).</summary>
        public const string LeakDetectionEnvironmentVariable = "UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE";

        /// <summary>Command-line switch that requests the same mode for this process.</summary>
        public const string LeakDetectionArgumentPrefix = "-probeNativeLeakDetection=";

        /// <summary>Digest the narrative generated-catalog run reports over its six named observations (P-008).</summary>
        public const string NarrativeGeneratedDigest =
            "4235cea3c22cf93e38307000fcc863edd4ada3e2fc4a3b475ca719765dc17279";

        /// <summary>Digest the narrative fixture-catalog run reports over its six `fixture:`-prefixed observations.</summary>
        public const string NarrativeFixtureDigest =
            "ab198371e8d276dcc2833e88e64e278abde57274ea3c68e2497fa2b75f97fa73";

        /// <summary>Digest the card generated-catalog run reports over its seven named observations (P-008).</summary>
        public const string CardsGeneratedDigest =
            "894a975e4a5eb98d733ec213778ec275e4abe02cb4028271857bd4fa68d6fe80";

        /// <summary>Digest the card fixture-catalog run reports over its seven `fixture:`-prefixed observations.</summary>
        public const string CardsFixtureDigest =
            "9c3d3b5d2f8e959920aa9678e65f56658cbe4a513fcd2895cd458fbda4f9ea14";

        /// <summary>Digest the traversal run reports over its nine named observations (P-008).</summary>
        public const string TraversalDigest =
            "ca29b5f7099f9fbc4abeb7031c7b1e35ba39ec1ad39db70dd0f037fa8d45675e";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            // All three process observations are recorded before any world is built and none of them throws: they
            // describe the environment the gate ran in, so a failing scenario must not be able to lose them.
            AddCycleCountStep(report);
            AddLeakDetectionStep(report);
            AddTelemetryStep(report);

            try
            {
                IReadOnlyList<string> families = W6GateScenario.Families();
                for (int i = 0; i < families.Count; i++)
                {
                    RunFamily(report, families[i]);
                }
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail("w6-gate-scenario", DescribeException(exception)));
            }
        }

        private static void RunFamily(ProbeReport report, string family)
        {
            if (string.Equals(family, Gc020TraversalHost.Label, StringComparison.Ordinal))
            {
                RunTraversal(report);
                return;
            }

            if (string.Equals(family, Gc013NarrativeHost.Label, StringComparison.Ordinal))
            {
                RunBothCatalogs(
                    report, family, NarrativeGeneratedDigest, NarrativeFixtureDigest,
                    Gc013NarrativeHost.RunBothW6Gate);
                return;
            }

            RunBothCatalogs(
                report, family, CardsGeneratedDigest, CardsFixtureDigest,
                Gc013CardsHost.RunBothW6Gate);
        }

        private delegate IReadOnlyList<W6GateStep> BothCatalogs(
            out W6GateScenarioResult generated, out W6GateScenarioResult fixture);

        private static void RunBothCatalogs(
            ProbeReport report,
            string family,
            string expectedGenerated,
            string expectedFixture,
            BothCatalogs run)
        {
            W6GateScenarioResult generated;
            W6GateScenarioResult fixture;
            try
            {
                IReadOnlyList<W6GateStep> combined = run(out generated, out fixture);
                AddSteps(report, combined);
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail("w6-" + family + "-scenario", DescribeException(exception)));
                return;
            }

            bool generatedHeld = generated.AllPassed
                && string.Equals(generated.Digest, expectedGenerated, StringComparison.Ordinal);
            bool fixtureHeld = fixture.AllPassed
                && string.Equals(fixture.Digest, expectedFixture, StringComparison.Ordinal);
            bool held = generatedHeld && fixtureHeld;

            string digestName = "w6-" + family + "-digest";
            report.Add(held
                ? ProbeOutcome.Pass(digestName,
                    "generatedDigest=" + generated.Digest
                    + "; fixtureDigest=" + fixture.Digest
                    + "; expectedGeneratedDigest=" + expectedGenerated
                    + "; expectedFixtureDigest=" + expectedFixture
                    + "; observations=" + generated.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; fixtureObservations=" + fixture.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; " + generated.Describe()
                    + "; " + fixture.Describe())
                : ProbeOutcome.Fail(digestName,
                    "generatedDigest=" + generated.Digest
                    + "; fixtureDigest=" + fixture.Digest
                    + "; expectedGeneratedDigest=" + expectedGenerated
                    + "; expectedFixtureDigest=" + expectedFixture
                    + "; " + generated.Describe()
                    + "; " + fixture.Describe()));
        }

        private static void RunTraversal(ProbeReport report)
        {
            W6GateScenarioResult generated;
            W6GateScenarioResult fixture;
            try
            {
                IReadOnlyList<W6GateStep> combined = Gc020TraversalHost.RunBothW6Gate(out generated, out fixture);
                AddSteps(report, combined);
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail("w6-traversal-scenario", DescribeException(exception)));
                return;
            }

            bool singleCatalog = ReferenceEquals(generated, fixture);
            bool held = generated.AllPassed
                && string.Equals(generated.Digest, TraversalDigest, StringComparison.Ordinal)
                && singleCatalog;

            string digestName = "w6-traversal-digest";
            report.Add(held
                ? ProbeOutcome.Pass(digestName,
                    "digest=" + generated.Digest
                    + "; expectedDigest=" + TraversalDigest
                    + "; observations=" + generated.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; catalogs=1"
                    + "; " + generated.Describe())
                : ProbeOutcome.Fail(digestName,
                    "digest=" + generated.Digest
                    + "; expectedDigest=" + TraversalDigest
                    + "; singleCatalog=" + singleCatalog
                    + "; catalogs=1"
                    + "; " + generated.Describe()));
        }

        private static void AddSteps(ProbeReport report, IReadOnlyList<W6GateStep> steps)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                W6GateStep step = steps[i];
                report.Add(step.Passed
                    ? ProbeOutcome.Pass(step.Name, step.Detail)
                    : ProbeOutcome.Fail(step.Name, step.Detail));
            }
        }

        private static void AddCycleCountStep(ProbeReport report)
        {
            try
            {
                int resolved = W6GateScenario.CycleCount;
                int requested = RequestedCycleCount();
                bool pass = resolved > 0 && resolved == requested;
                string detail = "resolvedCycleCount=" + resolved.ToString(CultureInfo.InvariantCulture)
                    + "; requestedCycleCount=" + requested.ToString(CultureInfo.InvariantCulture)
                    + "; defaultCycleCount=" + W6GateScenario.DefaultCycleCount.ToString(CultureInfo.InvariantCulture)
                    + "; source=" + W6GateScenario.CycleCountVariable;
                report.Add(pass
                    ? ProbeOutcome.Pass(CycleCountStepName, detail)
                    : ProbeOutcome.Fail(CycleCountStepName, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(CycleCountStepName, "unhandled " + DescribeException(exception)));
            }
        }

        private static int RequestedCycleCount()
        {
            string? raw = Environment.GetEnvironmentVariable(W6GateScenario.CycleCountVariable);
            int parsed;
            if (raw != null
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                && parsed > 0)
            {
                return parsed;
            }

            return W6GateScenario.DefaultCycleCount;
        }

        /// <summary>
        /// Arms native leak detection with full stack traces when this process was asked to, and reports the mode that
        /// actually resulted. Nothing here can throw: a request that cannot be honoured is a failing step, because leak
        /// evidence produced without the requested mode does not attribute the allocations it lists.
        /// </summary>
        private static void AddLeakDetectionStep(ProbeReport report)
        {
            NativeLeakDetectionMode prior;
            try
            {
                prior = NativeLeakDetection.Mode;
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    LeakDetectionStepName,
                    "reading NativeLeakDetection.Mode failed: " + DescribeException(exception)));
                return;
            }

            string requested;
            string source;
            if (!TryReadRequestedLeakMode(out requested, out source))
            {
                report.Add(ProbeOutcome.Pass(
                    LeakDetectionStepName,
                    "requested=<none>; mode=" + prior
                    + "; applied=False; source=<default>; environmentVariable=" + LeakDetectionEnvironmentVariable));
                return;
            }

            NativeLeakDetectionMode mode;
            if (!TryParseLeakMode(requested, out mode))
            {
                report.Add(ProbeOutcome.Fail(
                    LeakDetectionStepName,
                    "requested=" + requested + "; source=" + source + "; mode=" + prior
                    + "; applied=False; accepted=<0|1|2|Disabled|Enabled|EnabledWithStackTrace>"));
                return;
            }

            try
            {
                NativeLeakDetection.Mode = mode;
                NativeLeakDetectionMode applied = NativeLeakDetection.Mode;
                bool pass = applied == mode;
                string detail = "requested=" + mode
                    + "; appliedMode=" + applied
                    + "; priorMode=" + prior
                    + "; source=" + source
                    + "; applied=" + (pass ? "True" : "False");
                report.Add(pass
                    ? ProbeOutcome.Pass(LeakDetectionStepName, detail)
                    : ProbeOutcome.Fail(LeakDetectionStepName, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    LeakDetectionStepName,
                    "requested=" + mode + "; source=" + source + "; mode=" + prior
                    + "; applied=False; failure=" + DescribeException(exception)));
            }
        }

        /// <summary>
        /// Reports the telemetry build shape. The counters this gate integrates are `[Conditional]` call sites, so a
        /// release compilation has no call, no branch and no argument evaluation; the qualification player must have
        /// the symbol, or the counters observation would be reading zeros that mean "not compiled in" rather than
        /// "nothing happened" (GC-023's switch, TEST-023).
        /// </summary>
        private static void AddTelemetryStep(ProbeReport report)
        {
            try
            {
                bool compiledIn = TelemetrySchema.IsCompiledIn;
                var counters = new TelemetryCounterSet();
                counters.Add(TelemetryCounter.StepsAdvanced, 1L);
                bool writes = counters.Get(TelemetryCounter.StepsAdvanced) == 1L;
                report.Add(compiledIn && writes
                    ? ProbeOutcome.Pass(
                        TelemetryStepName,
                        "symbol=" + TelemetrySchema.Symbol
                        + "; compiledIn=True"
                        + "; counterCount=" + TelemetrySchema.CounterCount.ToString(CultureInfo.InvariantCulture)
                        + "; counterSetWritable=True")
                    : ProbeOutcome.Fail(
                        TelemetryStepName,
                        "symbol=" + TelemetrySchema.Symbol
                        + "; compiledIn=" + (compiledIn ? "True" : "False")
                        + "; counterSetWritable=" + (writes ? "True" : "False")
                        + "; detail=<the qualification player must be compiled with the telemetry marker>"));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(TelemetryStepName, "unhandled " + DescribeException(exception)));
            }
        }

        private static bool TryReadRequestedLeakMode(out string requested, out string source)
        {
            requested = string.Empty;
            source = string.Empty;

            string[] arguments;
            try
            {
                arguments = Environment.GetCommandLineArgs();
            }
            catch (Exception)
            {
                arguments = Array.Empty<string>();
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument != null && argument.StartsWith(LeakDetectionArgumentPrefix, StringComparison.Ordinal))
                {
                    requested = argument.Substring(LeakDetectionArgumentPrefix.Length);
                    source = LeakDetectionArgumentPrefix;
                    return true;
                }
            }

            string? environment = Environment.GetEnvironmentVariable(LeakDetectionEnvironmentVariable);
            if (!string.IsNullOrEmpty(environment))
            {
                requested = environment!;
                source = LeakDetectionEnvironmentVariable;
                return true;
            }

            return false;
        }

        private static bool TryParseLeakMode(string text, out NativeLeakDetectionMode mode)
        {
            mode = NativeLeakDetectionMode.Disabled;
            if (string.Equals(text, "0", StringComparison.Ordinal)
                || string.Equals(text, "Disabled", StringComparison.OrdinalIgnoreCase))
            {
                mode = NativeLeakDetectionMode.Disabled;
                return true;
            }

            if (string.Equals(text, "1", StringComparison.Ordinal)
                || string.Equals(text, "Enabled", StringComparison.OrdinalIgnoreCase))
            {
                mode = NativeLeakDetectionMode.Enabled;
                return true;
            }

            if (string.Equals(text, "2", StringComparison.Ordinal)
                || string.Equals(text, "EnabledWithStackTrace", StringComparison.OrdinalIgnoreCase))
            {
                mode = NativeLeakDetectionMode.EnabledWithStackTrace;
                return true;
            }

            return false;
        }

        private static string DescribeException(Exception exception) =>
            exception.GetType().FullName + ": " + exception.Message;
    }
}
