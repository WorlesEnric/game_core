// GameCore.Validation.ProbeHost - the Wave 5 integration-gate probe (W5-GATE).
//
// Runs the gate scenario inside the built IL2CPP player, over each family's committed generated catalog and over its
// hand-written generated-style catalog, and reports every observation into the same structured JSON result as the
// GC-001/GC-005/Wave-1..4/GC-010..GC-019 probes.
//
// The scenario (`W5GateScenario` over the two family hosts) is shared with the Unity EditMode assembly
// `GameCore.W5Gate.Tests`, so the same checks execute in the Editor and in a stripped player. The two digest
// literals below are the values that suite asserts, and they are recomputed here from the observed steps: a renamed
// observation, a different step count or a single failing step cannot report them.
//
// The latches this scenario arms exist only in a compilation that defines `GAMECORE_FAULT_INJECTION`, which the
// validation project gets from the qualification marker package. A player built without that marker does not contain
// this file at all (`tools/unity/prepare_gc017_release_project.py` removes it, exactly as it removes the GC-017 fault
// scenario), so a release player cannot report a Wave 5 gate result it never ran.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeW5Gate` player mode: the Wave 5 integration gate over both families and both catalogs.</summary>
    public static class ProbeW5Gate
    {
        /// <summary>Digest the narrative run must report over its 8 named observations, all passing (P-008).</summary>
        public const string NarrativeDigest =
            "768dc415bd79e76adacb2472f7af5383bff25b4da44635a94eb3fa64f4dc250c";

        /// <summary>Digest the card run must report over its 8 named observations, all passing (P-008).</summary>
        public const string CardsDigest =
            "5740b580c5b807796af5456396195baf761947ff6fc819e173074aeeeef297ff";

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
                IReadOnlyList<W5GateStep> combined;
                W5GateScenarioResult generated;
                W5GateScenarioResult fixture;
                if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = Gc013NarrativeHost.RunBothW5Gate(out generated, out fixture);
                }
                else
                {
                    combined = Gc013CardsHost.RunBothW5Gate(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    W5GateStep step = combined[i];
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
                    ? ProbeOutcome.Pass("w5gate-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("w5gate-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "w5gate-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
