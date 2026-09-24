// Independent pure oracle support (GC-002). Locates the repository root so fixtures and result documents
// are read from and written to the committed tree instead of a build-output copy.
#nullable enable
using System;
using System.IO;

namespace GameCore.ProtocolFixtures
{
    /// <summary>Repository-relative path discovery for fixture data and evidence documents.</summary>
    public static class RepoLayout
    {
        /// <summary>Environment override, useful on a build host with an out-of-tree working directory.</summary>
        public const string RootEnvironmentVariable = "GAMECORE_REPO_ROOT";

        /// <summary>Marker file that identifies the repository root.</summary>
        public const string RootMarker = "docs/game-core/traceability.json";

        public static string FixtureDataDirectory => "tests/GameCore.ProtocolFixtures/Data";

        public static string FixtureCaseDirectory => "tests/GameCore.ProtocolFixtures/Data/cases";

        public static string ResultSchemaPath => "tests/GameCore.ProtocolFixtures/Data/result-schema.json";

        public static string ApiSnapshotPath => "tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt";

        /// <summary>Finds the repository root, or throws with the searched locations listed.</summary>
        public static string FindRoot()
        {
            if (TryFindRoot(out string root))
            {
                return root;
            }

            throw new InvalidOperationException(
                "Repository root not found. Set " + RootEnvironmentVariable +
                " or run the tests from inside the repository. Searched upwards from: " +
                AppContext.BaseDirectory + ", " + Directory.GetCurrentDirectory() +
                " for marker '" + RootMarker + "'.");
        }

        public static bool TryFindRoot(out string root)
        {
            string? fromEnvironment = Environment.GetEnvironmentVariable(RootEnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnvironment) && IsRoot(fromEnvironment))
            {
                root = Path.GetFullPath(fromEnvironment);
                return true;
            }

            if (SearchUpwards(AppContext.BaseDirectory, out root))
            {
                return true;
            }

            return SearchUpwards(Directory.GetCurrentDirectory(), out root);
        }

        /// <summary>Resolves a repository-relative path with the platform separator.</summary>
        public static string Resolve(string root, string relativePath)
        {
            if (string.IsNullOrEmpty(root))
            {
                throw new ArgumentException("A repository root is required.", nameof(root));
            }

            if (string.IsNullOrEmpty(relativePath))
            {
                throw new ArgumentException("A relative path is required.", nameof(relativePath));
            }

            return Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static bool SearchUpwards(string startDirectory, out string root)
        {
            root = string.Empty;
            if (string.IsNullOrEmpty(startDirectory))
            {
                return false;
            }

            DirectoryInfo? current = new DirectoryInfo(Path.GetFullPath(startDirectory));
            while (current != null)
            {
                if (IsRoot(current.FullName))
                {
                    root = current.FullName;
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        private static bool IsRoot(string directory) => File.Exists(Resolve(directory, RootMarker));
    }
}
