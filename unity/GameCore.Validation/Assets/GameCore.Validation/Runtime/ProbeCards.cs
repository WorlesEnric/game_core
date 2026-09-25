#nullable enable
using System;
using GameCore.Gameplay.Cards.Fixtures;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-011 card-slice probe: runs the card-market Automatic vertical slice inside the built IL2CPP player,
    /// over the committed generated card catalog and over the fixture's hand-written generated-style catalog, and
    /// reports every observation into the same structured JSON result as the GC-001, GC-005, W1-gate and W2-gate
    /// probes.
    ///
    /// The scenario itself (`GameCore.Gameplay.Cards.Fixtures.CardMarketScenario`) is shared with the Unity EditMode
    /// assembly `GameCore.Cards.Tests`, so the same checks are executed in the Editor and in a stripped player.
    /// </summary>
    public static class ProbeCards
    {
        public static void Run(ProbeReport report)
        {
            try
            {
                var combined = CardsScenarioHost.RunBoth(
                    out CardFacts generatedFacts,
                    out CardFacts fixtureFacts);

                for (int i = 0; i < combined.Count; i++)
                {
                    CardStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The observed facts are archived beside the per-check outcomes, so the artifact carries the values
                // the outcomes were computed from rather than only the verdicts.
                report.Add(ProbeOutcome.Pass("cards-generated-catalog-facts", generatedFacts.Describe()));
                report.Add(ProbeOutcome.Pass("cards-fixture-catalog-facts", fixtureFacts.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "cards-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
