#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Content.Compiler;
using GameCore.Validation.Probe;
using UnityEditor;
using UnityEngine;

namespace GameCore.Validation.Editor
{
    /// <summary>
    /// Deterministic build-time catalog generation for the GC-001 qualification player. Since GC-003 this is a
    /// thin bridge: the catalog description document is the input, the production content compiler
    /// (<c>GameCore.Content.Compiler</c>) validates it and emits the generated C# source, and this class only
    /// resolves Unity project paths and verifies that the probe's literal keys still match the documented
    /// derivation.
    ///
    /// Callable from batchmode with
    /// <c>-executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog</c>.
    /// Output is byte-reproducible because the compiler guarantees it: canonical declaration order, LF line
    /// endings, no timestamps and no machine paths.
    /// </summary>
    public static class ProbeCatalogGenerator
    {
        /// <summary>Project-relative path of the generated catalog consumed by the probe runtime.</summary>
        public const string RelativeOutputPath = "Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs";

        /// <summary>
        /// Project-relative path of the catalog description document. It lives outside <c>Assets/</c> so Unity
        /// does not import it as an asset and no generated <c>.meta</c> file is needed for it.
        /// </summary>
        public const string RelativeDescriptionPath = "Catalogs/ProbeCatalog.catalog.json";

        /// <summary>
        /// Batchmode entry point. Throws on any inconsistency so a failing generation produces a nonzero Editor
        /// exit code instead of a silently stale catalog.
        /// </summary>
        public static void GenerateCatalog()
        {
            if (!ProbeKeys.DerivationHolds())
            {
                throw new InvalidOperationException(
                    "ProbeKeys literals do not match the documented derivation "
                    + "StableNameKeyDerivation.Derive(stable name); refusing to generate a catalog with "
                    + "unverifiable keys.");
            }

            string projectRoot = ProjectRoot();
            string descriptionPath = Path.Combine(projectRoot, RelativeDescriptionPath);
            string outputPath = Path.Combine(projectRoot, RelativeOutputPath);

            if (!File.Exists(descriptionPath))
            {
                throw new FileNotFoundException(
                    "the catalog description document is missing; expected " + RelativeDescriptionPath,
                    descriptionPath);
            }

            CatalogGenerationReport report = CatalogGenerator.GenerateFromFiles(descriptionPath, outputPath);
            if (!report.Succeeded)
            {
                throw new InvalidOperationException("catalog generation failed: " + report.Summary);
            }

            Debug.Log(
                "[GC003] " + report.Summary
                + "; bytes=" + report.BytesWritten.ToString(CultureInfo.InvariantCulture)
                + "; catalogFileHash=" + (report.CatalogFileHash ?? "missing")
                + "; catalogFingerprint=" + (report.CatalogFingerprint ?? "missing"));

            AssetDatabase.Refresh();
        }

        /// <summary>Verification-only entry point: fails when the committed generated catalog is stale.</summary>
        public static void VerifyCatalog()
        {
            string projectRoot = ProjectRoot();
            string outputPath = Path.Combine(projectRoot, RelativeOutputPath);
            CatalogGenerationReport report = CatalogGenerator.Verify(outputPath);
            if (!report.Succeeded)
            {
                throw new InvalidOperationException("generated catalog verification failed: " + report.Summary);
            }

            Debug.Log("[GC003] verified " + RelativeOutputPath + "; catalogFileHash=" + report.CatalogFileHash
                + "; catalogFingerprint=" + report.CatalogFingerprint);
        }

        /// <summary>
        /// Absolute Unity project root. <c>Application.dataPath</c> ends in <c>/Assets</c>, which is reliable in
        /// batchmode; the process working directory is not guaranteed to be the project.
        /// </summary>
        private static string ProjectRoot()
        {
            string dataPath = Application.dataPath.TrimEnd('/', '\\');
            string? root = Path.GetDirectoryName(dataPath);
            if (string.IsNullOrEmpty(root))
            {
                throw new InvalidOperationException(
                    "cannot derive the project root from Application.dataPath=" + Application.dataPath);
            }

            return root;
        }
    }
}
