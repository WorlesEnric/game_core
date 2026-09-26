// GameCore.Validation.ProbeHost — the `-probeCatalogCoverage` player mode (GC-025).
//
// The mode runs `CatalogCoverageScenario` inside the built player and reports every observation plus the digest and
// the frozen literal into the same structured JSON result the other probes write. The scenario is shared with the
// Unity EditMode assembly `GameCore.CatalogCoverage.Tests`, so the same checks execute in the Editor and in a
// stripped headless IL2CPP player; the player run is what makes the stripping clauses meaningful, because a
// registration or serializer the linker dropped fails the generated coverage companion there and not in the Editor.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeCatalogCoverage` mode: the complete standalone IL2CPP and headless catalog profile.</summary>
    public static class ProbeCatalogCoverage
    {
        /// <summary>
        /// Digest of the coverage sequence's observation table, computed from its NAMES alone, so a renamed,
        /// reordered, added or dropped observation changes this literal instead of silently shrinking the gate
        /// (P-008). It is the SHA-256 over the LF-separated `catalogCoverage/&lt;name&gt;=pass` lines of
        /// <see cref="CatalogCoverageScenario.ObservationNames"/>, exactly the function
        /// <see cref="CatalogCoverageResult"/> uses.
        ///
        /// NotRun (pending orchestrator build host).
        /// </summary>
        public const string ExpectedDigest =
            "77bc74196ec96b076bcc63b007cc0f57f5322121bad69f36c0e280d0576fbbb2";

        /// <summary>Number of observations the sequence records; the literal above covers exactly this table.</summary>
        public static int ExpectedObservations => CatalogCoverageScenario.ObservationNames.Length;

        /// <summary>
        /// Runs the coverage sequence and adds one probe step per observation, plus the digest step whose detail
        /// carries the literal this mode and the EditMode suite both freeze.
        /// </summary>
        public static void Run(ProbeReport report)
        {
            try
            {
                CatalogCoverageResult result = CatalogCoverageScenario.Run();

                for (int i = 0; i < result.Steps.Count; i++)
                {
                    CatalogCoverageStep step = result.Steps[i];
                    report.Add(
                        step.Passed
                            ? ProbeOutcome.Pass(step.Name, step.Detail)
                            : ProbeOutcome.Fail(step.Name, step.Detail));
                }

                bool digestHeld = string.Equals(result.Digest, ExpectedDigest, StringComparison.Ordinal)
                    && result.AllPassed
                    && result.Steps.Count == ExpectedObservations;
                report.Add(
                    digestHeld
                        ? ProbeOutcome.Pass(
                            result.Label + "-digest",
                            "digest=" + result.Digest
                            + "; expectedDigest=" + ExpectedDigest
                            + "; observations=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture))
                        : ProbeOutcome.Fail(
                            result.Label + "-digest",
                            "digest=" + result.Digest
                            + "; expectedDigest=" + ExpectedDigest
                            + "; observations=" + result.Steps.Count.ToString(CultureInfo.InvariantCulture)
                            + "; " + result.Describe()));
            }
            catch (Exception exception)
            {
                report.Add(
                    ProbeOutcome.Fail(
                        "catalog-coverage-scenario",
                        "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
        }

        /// <summary>The coverage sequence's label every observation name of this run is qualified with.</summary>
        public static string Label => CatalogCoverageScenario.Label;
    }
}
