// GameCore.Validation.ProbeHost - the GC-022 lifecycle stress probe (`-probeLifecycleStress`).
//
// Runs the lifecycle stress scenario inside the built IL2CPP player: for each family, one real family world over the
// committed generated catalog (the counted 1,000-cycle stress) and one over the fixture declaration identity set, and
// reports every observation into the same structured JSON result as the GC-001/GC-005/Wave-1..5 probes.
//
// The scenario (`LifecycleStressScenario` over the family hosts) is shared with the Unity EditMode assembly the
// lifecycle-stress slice owns, so the same checks execute in the Editor and in a stripped player. The four digest
// literals below are the values over the frozen observation names in the fixed emission order (P-008, P-028): a renamed
// observation, a different step count or a single failing step cannot report them. The generated catalog's digest and
// the fixture catalog's digest differ, because a fixture step's name carries the `fixture:` prefix; that is exactly
// what makes one literal per catalog worth claiming.
//
// Two process-level observations come first, and neither can fail the run by throwing:
//
//   * the resolved cycle count (`LifecycleStressScenario.CycleCount`, honouring `GC_LIFECYCLE_STRESS_CYCLES`), so the
//     archived result names how much churn it measured rather than leaving it to the launch script;
//   * the native leak detection mode of this process. A qualification stress run is only evidence about native
//     allocations when leak detection with full stack traces was actually armed (`UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE`
//     for the Editor's Collections hook, and the same variable or `-probeNativeLeakDetection=<0|1|2>` in this player).
//
// The default behaviour is unchanged when neither is present: the probe only reads the mode and reports it. Nothing
// here enables audio (crash-139) and nothing here passes `-quit`: the runner exits through Application.Quit with the
// code that encodes its result.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Collections;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeLifecycleStress` player mode: GC-022's lifecycle stress over both families and both catalogs.</summary>
    public static class ProbeLifecycleStress
    {
        /// <summary>Step naming the cycle count the scenario resolved for this process.</summary>
        public const string CycleCountStepName = "lifecycle-stress-cycle-count";

        /// <summary>Step naming the process's native leak detection mode.</summary>
        public const string LeakDetectionStepName = "lifecycle-stress-native-leak-detection";

        /// <summary>
        /// Environment variable both halves of the leak-detection enablement read: the Collections package's Editor hook
        /// assigns `NativeLeakDetection.Mode` from it, and this player probe applies it in-process (0 Disabled,
        /// 1 Enabled, 2 EnabledWithStackTrace).
        /// </summary>
        public const string LeakDetectionEnvironmentVariable = "UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE";

        /// <summary>Command-line switch that requests the same mode for this process, e.g. `-probeNativeLeakDetection=2`.</summary>
        public const string LeakDetectionArgumentPrefix = "-probeNativeLeakDetection=";

        /// <summary>Environment variable that overrides the resolved cycle count; a positive integer, default 1000.</summary>
        public const string CyclesEnvironmentVariable = "GC_LIFECYCLE_STRESS_CYCLES";

        /// <summary>Cycle count the scenario resolves when <see cref="CyclesEnvironmentVariable"/> is absent or invalid.</summary>
        public const int DefaultCycleCount = 1000;

        /// <summary>Digest the narrative generated-catalog run reports over its 12 named observations, all passing (P-008).</summary>
        public const string NarrativeGeneratedDigest =
            "c743b4503dff2cf719bda1055964ec225307100bfb828a94aaa26af39639c4af";

        /// <summary>Digest the narrative fixture-catalog run reports over its 12 `fixture:`-prefixed observations (P-008).</summary>
        public const string NarrativeFixtureDigest =
            "dfe2e14f66e912febed6c2e32d0697f4f981c1c09467f500242f06a838ee3652";

        /// <summary>Digest the card generated-catalog run reports over its 12 named observations, all passing (P-008).</summary>
        public const string CardsGeneratedDigest =
            "bc9321062734d84582a7ff50ac3f0e18c24f77bfdd45103c2958a1991e849f23";

        /// <summary>Digest the card fixture-catalog run reports over its 12 `fixture:`-prefixed observations (P-008).</summary>
        public const string CardsFixtureDigest =
            "f601e7378d5a800bf740441b626d323cd0dc6b3d4b3c53b43a0db3a20c7544b3";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            // Both process observations are recorded before the stress and never throw: they describe the environment
            // the stress ran in, so a failing scenario must not be able to lose them.
            AddCycleCountStep(report);
            AddLeakDetectionStep(report);

            try
            {
                IReadOnlyList<string> families = LifecycleStressScenario.Families();
                if (families.Count == 0)
                {
                    report.Add(ProbeOutcome.Fail("lifecycle-stress-families", "families=0"));
                    return;
                }

                for (int i = 0; i < families.Count; i++)
                {
                    RunFamily(report, families[i]);
                }
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail("lifecycle-stress-scenario", DescribeException(exception)));
            }
        }

        private static void RunFamily(ProbeReport report, string family)
        {
            string expectedGenerated;
            string expectedFixture;
            if (string.Equals(family, "narrative", StringComparison.Ordinal))
            {
                expectedGenerated = NarrativeGeneratedDigest;
                expectedFixture = NarrativeFixtureDigest;
            }
            else if (string.Equals(family, "cards", StringComparison.Ordinal))
            {
                expectedGenerated = CardsGeneratedDigest;
                expectedFixture = CardsFixtureDigest;
            }
            else
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "lifecycle-stress-" + family + "-digest",
                        "no digest literal is declared for family '" + family + "'"));
                return;
            }

            try
            {
                LifecycleStressResult generated = LifecycleStressScenario.RunGeneratedCatalog(family);
                LifecycleStressResult fixture = LifecycleStressScenario.RunFixtureCatalog(family);
                if (generated == null || fixture == null)
                {
                    report.Add(
                        ProbeOutcome.Fail(
                            "lifecycle-stress-" + family + "-scenario",
                            "RunGeneratedCatalog/RunFixtureCatalog returned null for family '" + family + "'"));
                    return;
                }

                AddSteps(report, generated.Steps);
                AddSteps(report, fixture.Steps);

                // One literal per catalog is the whole claim: the family ran the named sequence over the committed
                // generated catalog *and* over the fixture identity set, and every observation of both passed. The
                // digest is over the step names and their pass flags, so a renamed observation or a dropped step
                // cannot produce these values (P-008, P-028).
                bool generatedHeld = generated.AllPassed
                    && string.Equals(generated.Digest, expectedGenerated, StringComparison.Ordinal);
                bool fixtureHeld = fixture.AllPassed
                    && string.Equals(fixture.Digest, expectedFixture, StringComparison.Ordinal);

                string detail = "generatedDigest=" + generated.Digest
                    + "; fixtureDigest=" + fixture.Digest
                    + "; expectedGeneratedDigest=" + expectedGenerated
                    + "; expectedFixtureDigest=" + expectedFixture
                    + "; observations=" + generated.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; fixtureObservations=" + fixture.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; " + generated.Describe()
                    + "; " + fixture.Describe();

                report.Add(generatedHeld && fixtureHeld
                    ? ProbeOutcome.Pass("lifecycle-stress-" + family + "-digest", detail)
                    : ProbeOutcome.Fail("lifecycle-stress-" + family + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "lifecycle-stress-" + family + "-scenario",
                        "unhandled " + DescribeException(exception)));
            }
        }

        private static void AddSteps(ProbeReport report, IReadOnlyList<LifecycleStressStep> steps)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                LifecycleStressStep step = steps[i];
                report.Add(
                    step.Passed
                        ? ProbeOutcome.Pass(step.Name, step.Detail)
                        : ProbeOutcome.Fail(step.Name, step.Detail));
            }
        }

        private static void AddCycleCountStep(ProbeReport report)
        {
            try
            {
                int resolved = LifecycleStressScenario.CycleCount;
                int requested = RequestedCycleCount();
                bool pass = resolved > 0 && resolved == requested;
                string detail = "resolvedCycleCount=" + resolved.ToString(CultureInfo.InvariantCulture)
                    + "; requestedCycleCount=" + requested.ToString(CultureInfo.InvariantCulture)
                    + "; defaultCycleCount=" + DefaultCycleCount.ToString(CultureInfo.InvariantCulture)
                    + "; source=" + CyclesEnvironmentVariable;
                report.Add(pass ? ProbeOutcome.Pass(CycleCountStepName, detail) : ProbeOutcome.Fail(CycleCountStepName, detail));
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(CycleCountStepName, "unhandled " + DescribeException(exception)));
            }
        }

        /// <summary>
        /// Resolves the cycle count the same way the scenario does: <see cref="CyclesEnvironmentVariable"/> when it names
        /// a positive integer, and <see cref="DefaultCycleCount"/> otherwise.
        /// </summary>
        private static int RequestedCycleCount()
        {
            string? raw = Environment.GetEnvironmentVariable(CyclesEnvironmentVariable);
            int parsed;
            if (raw != null
                && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                && parsed > 0)
            {
                return parsed;
            }

            return DefaultCycleCount;
        }

        /// <summary>
        /// Arms native leak detection with full stack traces when this process was asked to, and reports the mode that
        /// actually resulted. Nothing here can throw: a request that cannot be honoured is reported as a failing step,
        /// because leak evidence produced without the requested mode does not attribute the allocations it lists.
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
                report.Add(
                    ProbeOutcome.Fail(
                        LeakDetectionStepName,
                        "reading NativeLeakDetection.Mode failed: " + DescribeException(exception)));
                return;
            }

            string requested;
            string source;
            if (!TryReadRequestedLeakMode(out requested, out source))
            {
                report.Add(
                    ProbeOutcome.Pass(
                        LeakDetectionStepName,
                        "requested=<none>; mode=" + prior
                        + "; applied=False; source=<default>; environmentVariable=" + LeakDetectionEnvironmentVariable));
                return;
            }

            NativeLeakDetectionMode mode;
            if (!TryParseLeakMode(requested, out mode))
            {
                report.Add(
                    ProbeOutcome.Fail(
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
                report.Add(pass ? ProbeOutcome.Pass(LeakDetectionStepName, detail) : ProbeOutcome.Fail(LeakDetectionStepName, detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        LeakDetectionStepName,
                        "requested=" + mode + "; source=" + source + "; mode=" + prior
                        + "; applied=False; failure=" + DescribeException(exception)));
            }
        }

        /// <summary>
        /// Reads the requested leak detection mode, the command-line switch first and the environment variable second,
        /// without touching the leak detection API and without throwing.
        /// </summary>
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
                arguments = new string[0];
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument != null && argument.StartsWith(LeakDetectionArgumentPrefix, StringComparison.Ordinal))
                {
                    requested = argument.Substring(LeakDetectionArgumentPrefix.Length).Trim();
                    source = LeakDetectionArgumentPrefix + requested;
                    return requested.Length != 0;
                }
            }

            string? fromEnvironment = Environment.GetEnvironmentVariable(LeakDetectionEnvironmentVariable);
            if (string.IsNullOrEmpty(fromEnvironment))
            {
                return false;
            }

            requested = fromEnvironment.Trim();
            source = LeakDetectionEnvironmentVariable;
            return requested.Length != 0;
        }

        private static bool TryParseLeakMode(string raw, out NativeLeakDetectionMode mode)
        {
            switch (raw)
            {
                case "0":
                case "Disabled":
                case "disabled":
                    mode = NativeLeakDetectionMode.Disabled;
                    return true;
                case "1":
                case "Enabled":
                case "enabled":
                    mode = NativeLeakDetectionMode.Enabled;
                    return true;
                case "2":
                case "EnabledWithStackTrace":
                case "enabledWithStackTrace":
                    mode = NativeLeakDetectionMode.EnabledWithStackTrace;
                    return true;
                default:
                    mode = NativeLeakDetectionMode.Disabled;
                    return false;
            }
        }

        private static string DescribeException(Exception exception)
            => exception.GetType().FullName + ": " + exception.Message;
    }
}
