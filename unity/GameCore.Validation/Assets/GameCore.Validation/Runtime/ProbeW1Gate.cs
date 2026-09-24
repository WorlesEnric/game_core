#nullable enable
using System;
using GameCore.Unity.Fixtures;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// W1 integration gate probe: runs the Wave 1 exit-gate scenario inside the built IL2CPP player, over the
    /// committed generated catalog and over the fixture's hand-written generated-style catalog, and reports every
    /// observation into the same structured JSON result as the GC-001 and GC-005 probes.
    ///
    /// The scenario itself (`GameCore.Unity.Fixtures.W1GateScenario`) is shared with the Unity EditMode assembly
    /// `GameCore.Unity.W1Gate`, so the same checks are executed in the Editor and in a stripped player.
    /// </summary>
    public static class ProbeW1Gate
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                var combined = W1GateScenarioHost.RunBoth(
                    out W1GateFacts generatedFacts,
                    out W1GateFacts fixtureFacts);

                for (int i = 0; i < combined.Count; i++)
                {
                    W1GateStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The observed facts are archived beside the per-check outcomes, so the artifact carries the values
                // the outcomes were computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("gate-generated-catalog-facts", generatedFacts.Describe()));
                report.Add(ProbeOutcome.Pass("gate-fixture-catalog-facts", fixtureFacts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gate-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
