#nullable enable
using System;
using GameCore.Gameplay.Narrative.Fixtures;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-010 narrative vertical slice probe: runs the narrative scenario inside the built IL2CPP player, over the
    /// committed generated catalog and over the fixture's hand-written generated-style catalog, and reports every
    /// observation into the same structured JSON result as the GC-001, GC-005 and Wave gate probes.
    ///
    /// The scenario itself (`GameCore.Gameplay.Narrative.Fixtures.NarrativeScenario`) is shared with the Unity
    /// EditMode assembly `GameCore.Narrative.Tests`, so the same checks are executed in the Editor and in a stripped
    /// player.
    /// </summary>
    public static class ProbeNarrative
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                var combined = NarrativeScenarioHost.RunBoth(
                    out NarrativeFacts generatedFacts,
                    out NarrativeFacts fixtureFacts);

                for (int i = 0; i < combined.Count; i++)
                {
                    NarrativeStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The observed facts are archived beside the per-check outcomes, so the artifact carries the values
                // the outcomes were computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("narrative-generated-catalog-facts", generatedFacts.Describe()));
                report.Add(ProbeOutcome.Pass("narrative-fixture-catalog-facts", fixtureFacts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "narrative-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
