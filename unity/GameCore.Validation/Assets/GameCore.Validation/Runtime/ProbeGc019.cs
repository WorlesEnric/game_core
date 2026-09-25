#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-019 adapter probe: runs the adapter gate scenario of both early genres inside the built IL2CPP player, over
    /// each family's committed generated catalog and over its hand-written generated-style catalog, and reports every
    /// observation into the same structured JSON result as the GC-001, GC-005, Wave gate, GC-010, GC-011, GC-013 and
    /// Wave 4 gate probes.
    ///
    /// The runner (`Gc019Scenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) is shared with the Unity EditMode
    /// assembly `GameCore.Gc019.Tests`, so the same checks execute in the Editor and in a stripped player — which is
    /// the GC-019 definition of done: "Card/narrative outputs can be presented from snapshots and run headless without
    /// those views; all adapter leases participate in lifecycle tests." The scenario presents through a recording
    /// binder rather than through GameObjects, so this is exactly the headless composition 04 s7 describes, running
    /// with no renderer and no presentation service installed. Both digest literals below are the values that suite
    /// asserts, and they are recomputed here from the observed steps: a renamed observation, a different step count or
    /// a single failing step cannot report them.
    /// </summary>
    public static class ProbeGc019
    {
        /// <summary>Digest the narrative run must report over its 11 named observations, all passing (P-008).</summary>
        public const string NarrativeDigest = "c812ccdce22cee6098d3f8dba5c23cfb744c7644aa83a99ae24bb790fbdde837";

        /// <summary>Digest the card run must report over its 11 named observations, all passing (P-008).</summary>
        public const string CardsDigest = "deaff62c2a643221511524063dc0a23cb80b0fe071a3b00c8245fbd1fc6097ce";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            RunFamily(report, Gc013NarrativeHost.Label, NarrativeDigest);
            RunFamily(report, Gc013CardsHost.Label, CardsDigest);
        }

        private static void RunFamily(ProbeReport report, string label, string expectedDigest)
        {
            try
            {
                IReadOnlyList<Gc019Step> combined;
                Gc019ScenarioResult generated;
                Gc019ScenarioResult fixture;
                if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = Gc013NarrativeHost.RunBothGc019(out generated, out fixture);
                }
                else
                {
                    combined = Gc013CardsHost.RunBothGc019(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    Gc019Step step = combined[i];
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
                    ? ProbeOutcome.Pass("gc019-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc019-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc019-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
