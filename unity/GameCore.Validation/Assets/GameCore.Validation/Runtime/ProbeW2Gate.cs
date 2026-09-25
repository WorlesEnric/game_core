#nullable enable
using System;
using GameCore.Unity.Fixtures;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// W2 integration gate probe: runs the Wave 2 exit-gate scenario inside the built IL2CPP player, over the
    /// committed generated catalog and over the fixture's hand-written generated-style catalog, and reports every
    /// observation into the same structured JSON result as the GC-001, GC-005 and W1-gate probes.
    ///
    /// The scenario itself (`GameCore.Unity.Fixtures.W2GateScenario`) is shared with the Unity EditMode assembly
    /// `GameCore.W2Gate.Tests`, so the same checks are executed in the Editor and in a stripped player.
    /// </summary>
    public static class ProbeW2Gate
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                var combined = W2GateScenarioHost.RunBoth(
                    out W2GateFacts generatedFacts,
                    out W2GateFacts fixtureFacts);

                for (int i = 0; i < combined.Count; i++)
                {
                    W2GateStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The observed facts are archived beside the per-check outcomes, so the artifact carries the values
                // the outcomes were computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("gate2-generated-catalog-facts", generatedFacts.Describe()));
                report.Add(ProbeOutcome.Pass("gate2-fixture-catalog-facts", fixtureFacts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gate2-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
