// GC-023's telemetry switch probe (qualification tooling, never shipped).
//
// The claim under test: a build without `GAMECORE_TELEMETRY` performs no counting work at all, because every
// counting call site is a `[Conditional]` helper call and the compiler removes the call — including the evaluation
// of its arguments — when the symbol is undefined.
//
// "Including the argument evaluation" is the load-bearing half, and it is the half a source review cannot confirm:
// an argument expression with a side effect either runs or it does not. So every argument below is a call to a
// method that increments `sideEffects`, and the counters the helpers write are read back. The probe prints one
// deterministic line; build with `-c Release` and it must read `sideEffects=0;observed=0`, build with
// `-c Qualification` and both numbers must be positive. There is no third possibility: the values are either
// written by the helper or they are not.
//
// Exit code 0 when the observed shape matches the configuration the caller asked for (`--expect enabled|disabled`),
// 1 otherwise, so the driving script fails loudly instead of parsing prose.
#nullable enable
using System;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.TelemetryProbeTool
{
    /// <summary>One deterministic line of observed facts about the counting switch.</summary>
    internal static class Probe
    {
        /// <summary>Incremented by the argument expressions; non-zero only when the calls survived compilation.</summary>
        private static int sideEffects;

        private static int Main(string[] arguments)
        {
            string expectation = arguments.Length > 0 ? arguments[0] : string.Empty;
            bool expectedEnabled = string.Equals(expectation, "enabled", StringComparison.Ordinal);
            bool expectedDisabled = string.Equals(expectation, "disabled", StringComparison.Ordinal);
            if (!expectedEnabled && !expectedDisabled)
            {
                Console.Error.WriteLine("usage: GameCore.TelemetryProbe <enabled|disabled>");
                return 1;
            }

            var counters = new TelemetryCounterSet();

            // Three helper families, each with a side-effecting argument. In a disabled compilation none of these
            // statements emits anything at all, so neither the argument nor the helper body runs.
            TelemetryCounting.Count(counters, NextCounter());
            TelemetryCounting.Add(counters, TelemetryCounter.CandidatesMatched, NextDelta());
            TelemetryCounting.Observe(counters, TelemetryCounter.RequestHighWater, NextDelta());
            TelemetryCounting.Count(null, NextCounter());

            long observed = counters.Get(TelemetryCounter.StepsAdvanced)
                + counters.Get(TelemetryCounter.CandidatesMatched)
                + counters.Get(TelemetryCounter.RequestHighWater);

            // The contracts assembly in the dotnet solution is instrumented in both probe configurations;
            // this check describes this probe's call-site compilation, not its referenced assembly.
#if GAMECORE_TELEMETRY
            bool compiledIn = true;
#else
            bool compiledIn = false;
#endif
            Console.WriteLine(
                "compiledIn=" + (compiledIn ? "1" : "0")
                + ";sideEffects=" + sideEffects.ToString(CultureInfo.InvariantCulture)
                + ";observed=" + observed.ToString(CultureInfo.InvariantCulture)
                + ";schemaCounters=" + TelemetrySchema.CounterCount.ToString(CultureInfo.InvariantCulture)
                + ";symbol=" + TelemetrySchema.Symbol
                + ";expectation=" + expectation);

            // The two shapes differ in exactly one observable way, and both halves of the difference are asserted:
            // an enabled build must have evaluated its arguments *and* written the counters, while a disabled build
            // must have done neither — and must still report the schema it never writes to, which is what keeps this
            // from passing by producing no output at all.
            bool shapeHeld = expectedEnabled
                ? compiledIn && sideEffects > 0 && observed > 0L
                : !compiledIn && sideEffects == 0 && observed == 0L;
            bool schemaHeld = TelemetrySchema.CounterCount > 0
                && TelemetrySchema.Name(TelemetryCounter.ControlNodesVisited).Length > 0;
            return shapeHeld && schemaHeld ? 0 : 1;
        }

        private static TelemetryCounter NextCounter()
        {
            sideEffects++;
            return TelemetryCounter.StepsAdvanced;
        }

        private static long NextDelta()
        {
            sideEffects++;
            return 1L;
        }
    }
}
