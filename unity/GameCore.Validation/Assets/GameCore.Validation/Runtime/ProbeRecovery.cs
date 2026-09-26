#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-027 probe: runs the checkpoint-and-durable-delivery recovery sequence of both early genres inside the built
    /// IL2CPP player — the faulted-world `RecoverWorld` composition, the eight injected fault points with their
    /// permitted observable results, the delivery obligation and cursor crossing into a new session, and the
    /// host-configured bounded retry — and reports every observation into the same structured JSON result as the
    /// GC-001, GC-005, Wave gate, GC-010, GC-011, GC-013, Wave 4/5/6 gate, GC-018 and GC-021 probes.
    ///
    /// The scenario (`Gc027Scenario` over `Gc027NarrativeHost` and `Gc027CardsHost`) is shared with the Unity
    /// EditMode assembly `GameCore.Gc027.Tests`, so the same checks execute in the Editor and in a stripped player.
    /// Both digest literals below are the values that suite asserts, and they are recomputed here from the observed
    /// steps: a renamed observation, a different step count or a single failing step cannot report them.
    /// </summary>
    public static class ProbeRecovery
    {
        /// <summary>Digest the narrative run must report over its 17 named observations, all passing (P-008).</summary>
        public const string NarrativeDigest =
            "2644b55aee8bedbbae60e04627e4f6b16d114ac4f418bed4a1e5960e2bdf80f9";

        /// <summary>Digest the card run must report over its 17 named observations, all passing (P-008).</summary>
        public const string CardsDigest =
            "3c5923b2559b869767c49906e181c4e5efd0f351816926c4095e0aa36ff9074c";

        /// <summary>
        /// Digest the traversal run must report over its 17 named observations (the shared table minus the four
        /// delivery ones the course does not declare, plus its four engine-physics ones), all passing (P-008, P-054).
        /// </summary>
        public const string TraversalDigest =
            "30ff0f929889137733dea7bc047692f35488bde6c4d5af093b50af726784cefe";

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            RunFamily(report, Gc013NarrativeHost.Label, NarrativeDigest, Family.Narrative);
            RunFamily(report, Gc013CardsHost.Label, CardsDigest, Family.Cards);
            RunFamily(report, Gc020TraversalHost.Label, TraversalDigest, Family.Traversal);
        }

        /// <summary>Which family's adapter a run uses; the runner is the same for all three (P-001).</summary>
        private enum Family
        {
            Narrative = 0,
            Cards = 1,
            Traversal = 2,
        }

        private static void RunFamily(ProbeReport report, string label, string expectedDigest, Family family)
        {
            try
            {
                IGc027Family adapter = family == Family.Narrative
                    ? Gc013NarrativeHost.RecoveryFamily()
                    : family == Family.Cards
                        ? Gc013CardsHost.RecoveryFamily()
                        : Gc020TraversalHost.RecoveryFamily();
                Gc027ScenarioResult result = Gc027Scenario.Run(adapter);
                int expectedObservations = Gc027Scenario.ExpectedNames(adapter).Length;

                for (int i = 0; i < result.Steps.Count; i++)
                {
                    Gc027Step step = result.Steps[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // The scenario runs the whole sequence at once, so one literal per family is the whole claim: the
                // named sequence ran and every observation passed (P-008).
                bool digestHeld = string.Equals(result.Digest, expectedDigest, StringComparison.Ordinal)
                    && result.AllPassed
                    && result.Steps.Count == expectedObservations;

                string detail = "digest=" + result.Digest
                    + "; expectedDigest=" + expectedDigest
                    + "; observations=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; expectedObservations="
                    + expectedObservations.ToString(CultureInfo.InvariantCulture)
                    + "; " + result.Describe();

                report.Add(digestHeld
                    ? ProbeOutcome.Pass("gc027-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc027-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc027-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
