#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// W4-gate probe: runs the Wave 4 integration-gate scenario of both early genres inside the built IL2CPP player,
    /// over each family's committed generated catalog and over its hand-written generated-style catalog, and reports
    /// every observation into the same structured JSON result as the GC-001, GC-005, Wave gate, GC-010, GC-011 and
    /// GC-013 probes.
    ///
    /// The runner (`W4GateScenario` over `W4GateNarrativeHost` and `W4GateCardsHost`) is shared with the Unity EditMode
    /// assembly `GameCore.W4Gate.Tests`, so the same checks execute in the Editor and in a stripped player — which is
    /// what the wave exit gate asks for: "Run both families in IL2CPP, then integrate indexed move/mode changes,
    /// service-closure lifecycle and slot policies." Both digest literals below are the values that suite asserts, and
    /// they are recomputed here from the observed steps: a renamed observation, a different step count or a single
    /// failing step cannot report them.
    /// </summary>
    public static class ProbeW4Gate
    {
        public const string NarrativeDigest = "113c17f030d7840398fbf6737bc3bedcd6bbd37bdb86745e514845ecc2cd9cdc";

        public const string CardsDigest = "fada14b67acd65af73453cc00a7f0ad7016af88573dbd51a986614521148bf21";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            RunFamily(report, W4GateNarrativeHost.Label, NarrativeDigest);
            RunFamily(report, W4GateCardsHost.Label, CardsDigest);
        }

        private static void RunFamily(ProbeReport report, string label, string expectedDigest)
        {
            try
            {
                IReadOnlyList<W4GateStep> combined;
                W4GateScenarioResult generated;
                W4GateScenarioResult fixture;
                if (string.Equals(label, W4GateNarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = W4GateNarrativeHost.RunBoth(out generated, out fixture);
                }
                else
                {
                    combined = W4GateCardsHost.RunBoth(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    W4GateStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // Both catalogs run the same observation names and both must pass, so one literal per family is the
                // whole claim: the scenario ran the named sequence over the committed generated catalog *and* over the
                // hand-written generated-style catalog, and every observation passed (P-008, P-028).
                bool digestHeld = string.Equals(generated.Digest, expectedDigest, StringComparison.Ordinal)
                    && string.Equals(fixture.Digest, expectedDigest, StringComparison.Ordinal)
                    && generated.AllPassed
                    && fixture.AllPassed;

                string detail = "generatedDigest=" + generated.Digest
                    + "; fixtureDigest=" + fixture.Digest
                    + "; expectedDigest=" + expectedDigest
                    + "; observations=" + generated.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; " + generated.Describe()
                    + "; " + fixture.Describe();

                report.Add(digestHeld
                    ? ProbeOutcome.Pass("w4gate-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("w4gate-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "w4gate-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
