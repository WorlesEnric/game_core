// GameCore.Validation.ProbeHost — the GC-024 conformance player mode (`-probeConformance`).
//
// The IL2CPP player is the only surface where the conformance tables run under shipping-style compilation: High
// managed stripping, Burst, IL2CPP, no JIT and no Editor. This host runs every transcribed 07 table over the genre
// that owns it, runs the combined cross-family world, and writes the normalized traces and the genre audit beside
// the probe result, so the committed evidence is the player's own output.
//
// WHAT IT OBSERVES
//
//   * per table: every step the runner records plus the oracle's own verdict, so a passing run reports the
//     field-by-field comparison and not only its steps;
//   * the trace document itself, written to `<result-dir>/traces/<table>.txt` — the artifact the task asks for, and
//     the same bytes the EditMode suite recomputes from the fixture;
//   * the genre audit. A player has no project tree, so the loaded-assembly half is what it can prove and the
//     build-time half is asserted in EditMode and on the build host: this host reports which half it computed
//     instead of claiming a clean result it never looked at (P-060).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.ReferenceConformance;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>The `-probeConformance` player mode: every 07 table, the combined world and the genre audit.</summary>
    public static class ProbeConformance
    {
        /// <summary>Environment override of the directory the traces and the genre audit are written to.</summary>
        public const string ArtifactDirectoryVariable = "GC024_ARTIFACT_DIR";

        /// <summary>The step naming the tables this player carries and where its artifacts go.</summary>
        public const string RevisionStepName = "conformance/revision";

        /// <summary>The step naming how many tables ran, how many rows were compared and how many facts recorded.</summary>
        public const string CoverageStepName = "conformance/coverage";

        /// <summary>The step naming the combined world's outcome.</summary>
        public const string CrossStepName = "conformance/cross/verdict";

        /// <summary>The step naming the genre audit's outcome.</summary>
        public const string AuditStepName = "conformance/genre-audit";

        /// <summary>Runs every table over its genre, then the combined world, then the audit.</summary>
        public static void Run(ProbeReport report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            string artifactDirectory = ResolveArtifactDirectory();
            var results = new List<ConformanceTableResult>();

            report.Add(ProbeOutcome.Pass(
                RevisionStepName,
                "task=GC-024; tables=" + ReferenceTables.All().Count.ToString(CultureInfo.InvariantCulture)
                + "; artifactDirectory=" + artifactDirectory));

            RunTable(report, results, artifactDirectory, "cards", Gc013CardsHost.RunConformanceCards);
            RunTable(report, results, artifactDirectory, "narrative", Gc013NarrativeHost.RunConformanceNarrative);
            RunTable(report, results, artifactDirectory, "traversal", Gc020TraversalHost.RunConformanceTraversal);
            RunCross(report, results, artifactDirectory);

            int rows = 0;
            int facts = 0;
            bool allPassed = results.Count > 0;
            for (int i = 0; i < results.Count; i++)
            {
                rows += results[i].Verdict.RowsChecked;
                facts += results[i].Trace.Count;
                allPassed &= results[i].AllPassed;
            }

            string coverage = "tables=" + results.Count.ToString(CultureInfo.InvariantCulture)
                + "; rowsCompared=" + rows.ToString(CultureInfo.InvariantCulture)
                + "; facts=" + facts.ToString(CultureInfo.InvariantCulture)
                + "; allPassed=" + ConformanceValue.Bool(allPassed);
            report.Add(allPassed
                ? ProbeOutcome.Pass(CoverageStepName, coverage)
                : ProbeOutcome.Fail(CoverageStepName, coverage));

            RunGenreAudit(report, artifactDirectory);
        }

        /// <summary>Runs one table over its genre and records every step plus the oracle's verdict.</summary>
        private static void RunTable(
            ProbeReport report,
            List<ConformanceTableResult> results,
            string artifactDirectory,
            string tableId,
            Func<ConformanceTableResult> run)
        {
            ConformanceTableResult result;
            try
            {
                result = run();
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    ConformanceScenario.StepPrefix + tableId,
                    "the table could not run: " + exception.GetType().FullName + ": " + exception.Message));
                return;
            }

            results.Add(result);
            Record(report, result, artifactDirectory, tableId);
        }

        /// <summary>Runs the combined cross-family world and records its steps and its trace.</summary>
        private static void RunCross(
            ProbeReport report, List<ConformanceTableResult> results, string artifactDirectory)
        {
            ConformanceTableResult result;
            try
            {
                result = ConformanceCrossWorld.Run();
            }
            catch (Exception exception)
            {
                report.Add(ProbeOutcome.Fail(
                    CrossStepName,
                    "the combined world could not run: " + exception.GetType().FullName + ": " + exception.Message));
                return;
            }

            results.Add(result);
            Record(report, result, artifactDirectory, "cross");
        }

        /// <summary>Records one table's steps, its verdict and its trace document.</summary>
        private static void Record(
            ProbeReport report, ConformanceTableResult result, string artifactDirectory, string tableId)
        {
            for (int i = 0; i < result.Steps.Count; i++)
            {
                ConformanceStep step = result.Steps[i];
                report.Add(step.Passed
                    ? ProbeOutcome.Pass(step.Name, step.Detail)
                    : ProbeOutcome.Fail(step.Name, step.Detail));
            }

            string digestDetail = "digest=" + result.Trace.Digest()
                + "; facts=" + result.Trace.Count.ToString(CultureInfo.InvariantCulture)
                + "; verdict=" + result.Verdict.Describe();
            report.Add(result.AllPassed
                ? ProbeOutcome.Pass(ConformanceScenario.StepPrefix + tableId + "/trace-digest", digestDetail)
                : ProbeOutcome.Fail(ConformanceScenario.StepPrefix + tableId + "/trace-digest", digestDetail));

            WriteTrace(report, artifactDirectory, tableId, result.Document);
        }

        /// <summary>
        /// Runs the build-time genre audit when this host has the project tree, and reports the loaded-assembly half
        /// either way. A player has no tree, so it says which half it computed rather than claiming a clean result it
        /// never looked at (P-060).
        /// </summary>
        private static void RunGenreAudit(ProbeReport report, string artifactDirectory)
        {
            LoadedAssemblyReport loaded = KernelAssemblyAudit.AuditLoadedAssemblies();
            bool kernelClean = loaded.KernelAssemblies.Count > 0 && loaded.ForbiddenReferences.Count == 0;

            string? repositoryRoot = KernelAssemblyAudit.TryFindRepositoryRoot(Directory.GetCurrentDirectory());
            string treeHalf;
            bool treeClean;
            if (repositoryRoot == null)
            {
                treeHalf = "no project tree on this host, so the build-time half is asserted in EditMode and on the"
                    + " build host rather than claimed here";
                treeClean = true;
            }
            else
            {
                GenreAuditReport audit = AssemblyReferenceAudit.Audit(repositoryRoot);
                treeClean = audit.Clean;
                treeHalf = audit.Describe();
                WriteDocument(
                    report, artifactDirectory, GenreAuditDocument.ArtifactPath, GenreAuditDocument.Write(audit));
            }

            string detail = "loaded=" + loaded.Describe() + "; tree=" + treeHalf;
            report.Add(kernelClean && treeClean
                ? ProbeOutcome.Pass(AuditStepName, detail)
                : ProbeOutcome.Fail(AuditStepName, detail));
        }

        /// <summary>Writes one table's normalized trace beside the probe result, and reports the write.</summary>
        private static void WriteTrace(
            ProbeReport report, string artifactDirectory, string tableId, string document)
        {
            string path = Path.Combine(artifactDirectory, "traces", tableId + ".txt");
            if (!TryWrite(path, document, out string detail))
            {
                report.Add(ProbeOutcome.Fail(ConformanceScenario.StepPrefix + tableId + "/trace-write", detail));
                return;
            }

            report.Add(ProbeOutcome.Pass(
                ConformanceScenario.StepPrefix + tableId + "/trace-write",
                "wrote " + path + " (" + document.Length.ToString(CultureInfo.InvariantCulture) + " characters)"));
        }

        private static void WriteDocument(
            ProbeReport report, string artifactDirectory, string relativePath, string document)
        {
            string path = Path.Combine(artifactDirectory, relativePath);
            if (!TryWrite(path, document, out string detail))
            {
                report.Add(ProbeOutcome.Fail(AuditStepName + "/write", detail));
            }
        }

        private static bool TryWrite(string path, string document, out string detail)
        {
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory!);
                }

                File.WriteAllText(path, document);
                detail = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                detail = "writing " + path + " failed: " + exception.GetType().FullName + ": " + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// The directory the conformance artifacts go to: the explicit override when one is set, otherwise the
        /// directory holding the probe result. A run with neither still reports its steps and simply writes no files.
        /// </summary>
        private static string ResolveArtifactDirectory()
        {
            string configured = Environment.GetEnvironmentVariable(ArtifactDirectoryVariable) ?? string.Empty;
            if (configured.Length != 0)
            {
                return configured;
            }

            ProbeArguments arguments = ProbeArguments.Parse(Environment.GetCommandLineArgs());
            string resultPath = arguments.ResultPath ?? string.Empty;
            if (resultPath.Length != 0)
            {
                string? directory = Path.GetDirectoryName(resultPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    return directory!;
                }
            }

            return Directory.GetCurrentDirectory();
        }
    }
}
