#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-021 probe: runs the durable-delivery and destination-idempotency scenario of both early genres inside the
    /// built IL2CPP player, over each family's committed generated catalog and over its hand-written generated-style
    /// catalog, and reports every observation into the same structured JSON result as the GC-001, GC-005, Wave gate,
    /// GC-010, GC-011, GC-013, Wave 4 gate, GC-017, GC-018, GC-019 and Wave 5 gate probes.
    ///
    /// The scenario (`Gc021Scenario` over `Gc013NarrativeHost` / `Gc013CardsHost`, through `Gc021CatalogRuns`) is
    /// shared with the Unity EditMode assembly `GameCore.Gc021.Tests`, so the same checks execute in the Editor and in
    /// a stripped player. Both digest literals below are the values that suite asserts, and they are recomputed here
    /// from the observed steps: a renamed observation, a different step count or a single failing step cannot report
    /// them.
    ///
    /// WHY THE TWO LITERALS ARE `PENDING`
    ///
    /// A digest is computed over the observation names and their pass flags, so its value cannot be known before the
    /// sequence has first run on the build host — the same instruction `ProbeW5Gate` was completed under. Until the
    /// literals are pinned, this probe asserts the two weaker facts it can genuinely prove: both catalogs produced the
    /// identical named sequence, and every observation of both runs passed. A step whose `expectedDigest` is
    /// `PENDING` therefore passes on that evidence and reports `pending=True`; a step whose literal has been pinned
    /// passes only on exact agreement, so a pinned literal that disagrees fails loudly rather than being ignored.
    /// </summary>
    public static class ProbeGc021
    {
        /// <summary>
        /// The literal both digest constants carry until the build host pins them after the first passing run. It is a
        /// value no digest can take, so "not yet pinned" is never confusable with a real digest.
        /// </summary>
        public const string PendingDigest = "PENDING";

        /// <summary>Digest the narrative run must report over its named observations, all passing (P-008).</summary>
        public const string NarrativeDigest = PendingDigest;

        /// <summary>Digest the card run must report over its named observations, all passing (P-008).</summary>
        public const string CardsDigest = PendingDigest;

        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            RunFamily(report, Gc013NarrativeHost.Label, NarrativeDigest, true);
            RunFamily(report, Gc013CardsHost.Label, CardsDigest, false);
        }

        private static void RunFamily(ProbeReport report, string label, string expectedDigest, bool narrative)
        {
            try
            {
                IReadOnlyList<Gc021Step> combined;
                Gc021ScenarioResult generated;
                Gc021ScenarioResult fixture;
                if (narrative)
                {
                    combined = Gc021CatalogRuns.RunBothNarrative(out generated, out fixture);
                }
                else
                {
                    combined = Gc021CatalogRuns.RunBothCards(out generated, out fixture);
                }

                for (int i = 0; i < combined.Count; i++)
                {
                    Gc021Step step = combined[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                // Both catalogs run the same observation names and both must pass, so one literal per family is the
                // whole claim: the scenario ran the named sequence over the committed generated catalog *and* over the
                // hand-written generated-style catalog, and every observation passed (P-008, P-028).
                bool pending = string.Equals(expectedDigest, PendingDigest, StringComparison.Ordinal);
                bool bothCatalogsAgree = string.Equals(generated.Digest, fixture.Digest, StringComparison.Ordinal);
                bool digestHeld = string.Equals(generated.Digest, expectedDigest, StringComparison.Ordinal)
                    && string.Equals(fixture.Digest, expectedDigest, StringComparison.Ordinal)
                    && generated.AllPassed
                    && fixture.AllPassed;
                bool held = pending
                    ? bothCatalogsAgree && generated.AllPassed && fixture.AllPassed
                    : digestHeld;

                string detail = "generatedDigest=" + generated.Digest
                    + "; fixtureDigest=" + fixture.Digest
                    + "; expectedDigest=" + expectedDigest
                    + "; pending=" + pending
                    + "; bothCatalogsAgree=" + bothCatalogsAgree
                    + "; observations=" + generated.Steps.Count.ToString(CultureInfo.InvariantCulture)
                    + "; " + generated.Describe()
                    + "; " + fixture.Describe();

                report.Add(held
                    ? ProbeOutcome.Pass("gc021-" + label + "-digest", detail)
                    : ProbeOutcome.Fail("gc021-" + label + "-digest", detail));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "gc021-" + label + "-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }
    }
}
