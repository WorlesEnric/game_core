#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-018 probe: runs the checkpoint capture/restore round trip of both early genres inside the built IL2CPP
    /// player, over each family's committed generated catalog and over its hand-written generated-style catalog, and
    /// reports every observation into the same structured JSON result as the GC-001, GC-005, Wave gate, GC-010,
    /// GC-011, GC-013 and Wave 4 gate probes.
    ///
    /// The scenario (`Gc018Scenario` over `Gc018NarrativeHost` and `Gc018CardsHost`) is shared with the Unity
    /// EditMode assembly `GameCore.Gc018.Tests`, so the same checks execute in the Editor and in a stripped player.
    /// Both digest literals below are the values that suite asserts, and they are recomputed here from the observed
    /// steps: a renamed observation, a different step count or a single failing step cannot report them.
    /// </summary>
    public static class ProbeGc018
    {
        /// <summary>Digest the narrative run must report over its 16 named observations, all passing (P-008).</summary>
        public const string NarrativeDigest =
            "f881469b2a2ba43e4c1bf7972913dd2c93f945a623fef770e00319409ece7ac0";

        /// <summary>Digest the card run must report over its 16 named observations, all passing (P-008).</summary>
        public const string CardsDigest =
            "fe1aaae38982be120fe3c668742990504f984b54b053de3b2d7886ff899b5256";

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
                IReadOnlyList<Gc018Step> combined;
                Gc018ScenarioResult generated;
                Gc018ScenarioResult fixture;
                if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = Gc013NarrativeHost.RunBothGc018(out generated, out fixture);
                }
                else
                {
                    combined = Gc013CardsHost.RunBothGc018(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    Gc018Step step = combined[i];
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
                    ? ProbeOutcome.Pass("gc018-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc018-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc018-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
