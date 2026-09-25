#nullable enable
using System;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-012 Wave 4 provisional generic-execution profile probe: runs the profile gate inside the built IL2CPP
    /// player — both families over their committed generated catalog and their hand-written fixture catalog with
    /// canonical comparison, the multi-supporter `Additive` slot in a live world, the generated inactive family
    /// entries and the kernel-separation audit — and reports every observation plus the gate's own facts digest into
    /// the same structured JSON result as the GC-001, GC-005, W1, W2, W3 and per-slice probes.
    ///
    /// The scenario itself (`GameCore.Validation.ProbeHost.W4ProfileScenario`) is shared with the Unity EditMode
    /// assembly `GameCore.W4Profile.Tests`, so the same checks are executed in the Editor and in a stripped player.
    /// The player run is what makes the stripping clauses meaningful: `Assets/link.xml` no longer preserves the
    /// narrative gameplay/rules assemblies, so a missing generated root shows up here as a missing type rather than
    /// as an Editor-only success.
    /// </summary>
    public static class ProbeW4Profile
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                W4ProfileScenarioResult result = W4ProfileScenario.Run();
                for (int i = 0; i < result.Steps.Count; i++)
                {
                    W4ProfileStep step = result.Steps[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The gate's own observed values are archived beside the per-check outcomes, so the artifact carries
                // the numbers every verdict was computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("w4-profile-facts", result.Facts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "w4-profile-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
