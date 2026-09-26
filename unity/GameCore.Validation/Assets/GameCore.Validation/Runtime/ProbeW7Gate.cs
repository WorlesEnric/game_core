// GameCore.Validation.ProbeHost — the Wave 7 integration-gate probe (`-probeW7Gate`).
//
// Runs the Wave 7 gate scenario inside the built IL2CPP player: on the merged revision, GC-025's own catalog coverage
// sequence, the GC-026-affected incremental-versus-clean derivation equivalence at the declared 10,000-target scale,
// the merge invariants (every probe mode still parses; the ten recorded budget rows are still the declared ten), and
// GC-027's own recovery sequence for all three genres. Every observation is reported into the same structured JSON
// result as every earlier probe, so this gate's evidence has the shape the earlier gates' evidence has.
//
// The scenario (`W7GateScenario`) is shared with the Unity EditMode assembly `GameCore.W7Gate.Tests`, so the same
// checks run in the Editor and in a stripped player. The digest literal below is the value over the frozen
// observation table in the fixed emission order (P-008): a renamed observation, a different step count or a single
// failing step cannot report it, and the value the build's own table implies is compared against it here, so a table
// edit and a literal edit cannot drift apart silently.
//
// Nothing here enables audio (crash-139) and nothing here passes `-quit`: the runner exits through Application.Quit
// with the code that encodes its result.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeW7Gate` player mode: the Wave 7 exit gate on the merged revision.</summary>
    public static class ProbeW7Gate
    {
        /// <summary>
        /// Digest the run must report over every frozen observation of `W7GateScenario.ObservationNames()`,
        /// all passing: `NarrativeDigest.OfLines` over `<name>=pass` lines in the frozen order. It is the literal the
        /// table implies, quoted here so a table edit is a loud mismatch in this mode rather than a value that
        /// quietly follows the code (P-008, TEST-022).
        /// </summary>
        public const string ExpectedDigest =
            "f7853e42502c146fadb44e15e612a7ee67fd2605b6d2cff1951591567c719102";

        /// <summary>
        /// The observation names this mode must report, in emission order, each as the probe result spells it. The
        /// harness asserts this list by name, and the scenario's digest is computed from it.
        /// </summary>
        public static readonly string[] ExpectedObservations = W7GateScenario.ObservationNames();

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            W7GateScenarioResult result;
            try
            {
                result = W7GateScenario.Run();
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "w7-gate-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
                return;
            }

            for (int i = 0; i < result.Steps.Count; i++)
            {
                W7GateStep step = result.Steps[i];
                report.Add(
                    step.Passed
                        ? ProbeOutcome.Pass(step.Name, step.Detail)
                        : ProbeOutcome.Fail(step.Name, step.Detail));
            }

            // Two independent claims about the same run, so neither can cover for the other:
            //   * the recorded sequence is exactly the frozen table, in order — a renamed, reordered, added or
            //     dropped observation fails the scenario's own digest step rather than shrinking the gate;
            //   * the literal quoted above is the digest this build's table implies, which is the claim the EditMode
            //     suite recomputes from the table alone and the harness greps out of the result.
            string implied = W7GateScenario.ExpectedDigest();
            string detail = "digest=" + implied
                + "; expectedDigest=" + ExpectedDigest
                + "; observations=" + (result.Steps.Count - 1).ToString(CultureInfo.InvariantCulture)
                + "; expectedObservations=" + ExpectedObservations.Length.ToString(CultureInfo.InvariantCulture)
                + "; " + result.Describe();

            report.Add(string.Equals(implied, ExpectedDigest, StringComparison.Ordinal)
                ? ProbeOutcome.Pass("w7-frozen-digest-agrees-with-the-table", detail)
                : ProbeOutcome.Fail("w7-frozen-digest-agrees-with-the-table", detail));
        }
    }
}
