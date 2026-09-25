#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-017 fault-boundary probe: runs the deterministic fault-injection scenario of both early genres inside the
    /// built IL2CPP player, over each family's committed generated catalog and over its hand-written
    /// generated-style catalog, and reports every observation into the same structured JSON result as the GC-001,
    /// GC-005, W1/W2/W3 gate, GC-010, GC-011, GC-012, GC-013 and W4-gate probes.
    ///
    /// The runner (`FaultScenario` over `FaultScenarioHost`) is shared with the Unity EditMode assembly
    /// `GameCore.Faults.Tests`, so the same 15 named observations execute in the Editor and in a stripped player.
    /// That matters here more than anywhere else: the fault latches of GC-017 are compiled in by
    /// `GameCore.Unity.Runtime.asmdef`'s `GAMECORE_FAULT_INJECTION` version define, so a player that lost the
    /// symbol would report a bounded sequence of unpassed steps instead of silently succeeding. Both digest
    /// literals below are the values that suite asserts, and they are recomputed here from the observed steps: a
    /// renamed observation, a different step count or a single failing step cannot report them.
    ///
    /// The fixture run's steps carry the `fixture:` name prefix, exactly as the GC-013 and W4-gate probes combine
    /// their two catalog runs; the digest is computed over the unprefixed qualified names.
    /// </summary>
    public static class ProbeFaults
    {
        /// <summary>Digest the narrative run must report over its 15 named observations, all passing (TEST-016).</summary>
        public const string NarrativeDigest = "701a3c286098501456390975bbdc7e4bdb7218d3094f23e39e61b3744fa52b61";

        /// <summary>Digest the card run must report over its 15 named observations, all passing (TEST-016).</summary>
        public const string CardsDigest = "5cd97d38a1023fe0c8b5239d611061e5696454be9ec7cc68d440506810201732";

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
                IReadOnlyList<FaultScenarioStep> combined;
                FaultScenarioResult generated;
                FaultScenarioResult fixture;
                if (string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal))
                {
                    combined = FaultScenarioHost.RunNarrative(out generated, out fixture);
                }
                else
                {
                    combined = FaultScenarioHost.RunCards(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    FaultScenarioStep step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // Both catalogs run the same observation names and both must pass, so one literal per family is the
                // whole claim: the scenario reached every named TEST-016 boundary over the committed generated
                // catalog *and* over the hand-written generated-style catalog, and every observation passed.
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
                    ? ProbeOutcome.Pass("gc017-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc017-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc017-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
