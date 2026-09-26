#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Content.Compiler;
using UnityEditor;
using UnityEngine;

namespace GameCore.Validation.Editor
{
    /// <summary>
    /// Deterministic build-time generation of the traversal catalog (GC-025). The third catalog of the
    /// qualification project, alongside the probe/narrative catalog and the card catalog, and the one GC-020
    /// deliberately deferred: until this task the traversal genre had a single hand-written generated-style table
    /// (<c>TraversalCatalogTable</c>) and its gate reported <c>generatedCatalog=absent</c>.
    ///
    /// This is a thin bridge in the same shape as <see cref="CardCatalogGenerator"/>: the description document is the
    /// input, the production content compiler validates it and emits the generated C#, and this class resolves Unity
    /// project paths. The generated catalog declares the same nine registrations and the same one schema as the
    /// hand-written table, so the two catalogs must produce the same
    /// <c>CatalogFingerprint</c> — which is what <c>-probeCatalogCoverage</c> asserts in the built player (P-028).
    ///
    /// Callable from batchmode with
    /// <c>-executeMethod GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog</c>.
    /// </summary>
    public static class TraversalCatalogGenerator
    {
        /// <summary>Project-relative path of the generated traversal catalog the traversal runtime consumes.</summary>
        public const string RelativeOutputPath = "Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs";

        /// <summary>
        /// Project-relative path of the catalog description document. It lives outside <c>Assets/</c> so Unity does
        /// not import it as an asset and no generated <c>.meta</c> file is needed for it.
        /// </summary>
        public const string RelativeDescriptionPath = "Catalogs/TraversalCatalog.catalog.json";

        /// <summary>
        /// Batchmode entry point. Throws on any inconsistency so a failing generation produces a nonzero Editor exit
        /// code instead of a silently stale catalog.
        /// </summary>
        public static void GenerateCatalog()
        {
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
                "[GC025] " + report.Summary
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

            Debug.Log("[GC025] verified " + RelativeOutputPath + "; catalogFileHash=" + report.CatalogFileHash
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
