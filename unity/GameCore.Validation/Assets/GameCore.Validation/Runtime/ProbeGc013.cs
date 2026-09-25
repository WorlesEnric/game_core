#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-013 probe: runs the incremental-invalidation, reparenting and live-mode-switching scenario of both early
    /// genres inside the built IL2CPP player, over each family's committed generated catalog and over its hand-written
    /// generated-style catalog, and reports every observation into the same structured JSON result as the GC-001,
    /// GC-005, Wave gate, GC-010 and GC-011 probes.
    ///
    /// The scenarios themselves (`Gc013Scenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) are shared with the
    /// Unity EditMode assembly `GameCore.Gc013.Tests`, so the same checks are executed in the Editor and in a stripped
    /// player. Both digest literals below are the values that suite asserts, and they are recomputed here from the
    /// observed steps: a renamed observation, a different step count or a single failing step cannot report them.
    /// </summary>
    public static class ProbeGc013
    {
        /// <summary>Digest the narrative run must report over its 15 named observations, all passing (P-008).</summary>
        public const string NarrativeDigest = "8d0ca4d2e31cdf6e1ead4a57fa6427acb1dfb4d7c11ab9cab1590857ee81befd";

        /// <summary>Digest the card run must report over its 15 named observations, all passing (P-008).</summary>
        public const string CardsDigest = "ac6dc0b11d32a60328cfcc2724ce23a36919afd88aa01e6afa0a135ac470c5c9";

        public static void Run(ProbeReport report)
        {
            RunFamily(report, Gc013NarrativeHost.Label, NarrativeDigest);
            RunFamily(report, Gc013CardsHost.Label, CardsDigest);
        }

        private static void RunFamily(ProbeReport report, string label, string expectedDigest)
        {
            try
            {
                IReadOnlyList<Gc013Step> combined;
                Gc013ScenarioResult generated;
                Gc013ScenarioResult fixture;
                if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = Gc013NarrativeHost.RunBoth(out generated, out fixture);
                }
                else
                {
                    combined = Gc013CardsHost.RunBoth(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    Gc013Step step = combined[i];
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
                    ? ProbeOutcome.Pass("gc013-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc013-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc013-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
