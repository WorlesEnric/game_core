#nullable enable
using System;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// W3 integration gate probe: runs the Wave 3 exit-gate scenario inside the built IL2CPP player — the narrative
    /// composition and then the card composition, each in its own world, in this one process — and reports every
    /// observation plus the gate's own facts digest into the same structured JSON result as the GC-001, GC-005, W1,
    /// W2 and per-slice probes.
    ///
    /// The scenario itself (`GameCore.Validation.ProbeHost.W3GateScenario`) is shared with the Unity EditMode
    /// assembly `GameCore.W3Gate.Tests`, so the same checks are executed in the Editor and in a stripped player; the
    /// loaded-assembly half of the kernel-separation audit is what makes the player run meaningful, because the
    /// build-time `.asmdef` half cannot see a project tree from inside a player.
    /// </summary>
    public static class ProbeW3Gate
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                W3GateScenarioResult result = W3GateScenario.Run();
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    W3GateStep step = result.Steps[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The gate's own observed values are archived beside the per-check outcomes, so the artifact carries
                // the numbers every verdict was computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("w3-gate-facts", result.Facts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "w3-gate-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
