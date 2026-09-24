// GameCore.Content.Compiler.Editor - Unity Editor entry point for catalog generation (GC-003).
// Thin by design: it resolves paths, calls the Unity-free generator core and turns a failure into a nonzero
// batchmode exit. All catalogue rules, emission and verification live in GameCore.Content.Compiler so the same
// code runs under plain dotnet tests (docs/game-core/04-unity-integration.md section 8).
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Content.Compiler;
using UnityEditor;
using UnityEngine;

namespace GameCore.Content.Compiler.Editor
{
    /// <summary>Editor menu and batchmode entry point of the content compiler.</summary>
    public static class CatalogGeneratorMenu
    {
        /// <summary>Project-relative default of the description document.</summary>
        public const string DefaultDescriptionPath = "Assets/GameCore/Catalogs/GameCoreCatalog.catalog.json";

        /// <summary>Project-relative default of the generated catalog file.</summary>
        public const string DefaultOutputPath = "Assets/GameCore/Generated/GameCoreCatalog.g.cs";

        /// <summary>Exit code used for a failed batchmode generation, distinct from a thrown Editor exception.</summary>
        public const int GenerationFailureExitCode = 1;

        private const string MenuPath = "GameCore/Content/Generate Catalog";

        [MenuItem(MenuPath, false, 100)]
        private static void GenerateFromMenu()
        {
            string projectRoot = ProjectRoot();
            string description = Path.Combine(projectRoot, DefaultDescriptionPath);
            string output = Path.Combine(projectRoot, DefaultOutputPath);

            if (!File.Exists(description))
            {
                Debug.LogError(
                    "[GameCore.Content.Compiler] no catalog description at " + DefaultDescriptionPath +
                    "; create it or invoke the batchmode entry point with -catalogDescription <path>.");
                return;
            }

            CatalogGenerationReport report = CatalogGenerator.GenerateFromFiles(description, output);
            LogReport(report);
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Batchmode entry point. Exits 0 only after the generated file was written (or already current) and
        /// re-verified; otherwise writes the diagnostics and exits with a nonzero code so a build script fails.
        /// </summary>
        public static void GenerateFromCommandLine()
        {
            CatalogGeneratorArguments arguments = CatalogGeneratorArguments.Parse(Environment.GetCommandLineArgs());

            if (arguments.PrintHelp)
            {
                Debug.Log(CatalogGeneratorArguments.Usage);
                return;
            }

            if (!arguments.IsValid)
            {
                for (int i = 0; i < arguments.Errors.Count; i++)
                {
                    Debug.LogError("[GameCore.Content.Compiler] " + arguments.Errors[i]);
                }

                Debug.LogError(CatalogGeneratorArguments.Usage);
                Exit(GenerationFailureExitCode);
                return;
            }

            CatalogGenerationReport report = CatalogGenerator.GenerateFromFiles(
                arguments.DescriptionPath!,
                arguments.OutputPath!);
            LogReport(report);

            if (!report.Succeeded)
            {
                Exit(GenerationFailureExitCode);
                return;
            }

            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Verification-only entry point: re-reads a generated file and fails when its recorded hash or
        /// fingerprint no longer matches its content, which is how a stale committed catalog is detected.
        /// </summary>
        public static void VerifyFromCommandLine()
        {
            CatalogGeneratorArguments arguments = CatalogGeneratorArguments.Parse(Environment.GetCommandLineArgs());
            if (string.IsNullOrEmpty(arguments.OutputPath))
            {
                Debug.LogError("[GameCore.Content.Compiler] -catalogOutput <path> is required for verification.");
                Debug.LogError(CatalogGeneratorArguments.Usage);
                Exit(GenerationFailureExitCode);
                return;
            }

            CatalogGenerationReport report = CatalogGenerator.Verify(arguments.OutputPath!);
            LogReport(report);
            if (!report.Succeeded)
            {
                Exit(GenerationFailureExitCode);
            }
        }

        private static void LogReport(CatalogGenerationReport report)
        {
            if (report.Succeeded)
            {
                Debug.Log(
                    "[GameCore.Content.Compiler] " + report.Summary +
                    "; bytesWritten=" + report.BytesWritten.ToString(CultureInfo.InvariantCulture) +
                    "; catalogFileHash=" + (report.CatalogFileHash ?? "missing") +
                    "; catalogFingerprint=" + (report.CatalogFingerprint ?? "missing"));
                return;
            }

            Debug.LogError("[GameCore.Content.Compiler] catalog generation failed: " + report.Summary);
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

        private static void Exit(int code)
        {
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
                return;
            }

            throw new InvalidOperationException(
                "catalog generation failed with exit code " + code.ToString(CultureInfo.InvariantCulture) +
                "; see the Console for diagnostics.");
        }
    }
}
