// GameCore.Studio.Edit - where the Studio keeps its files (docs/studio/03-authoring-contracts.md s3, s6).
//   <project>/Studio/History/YYYY/MM/<id>.json        journal (tracked)
//   <project>/Studio/Artifacts/sha256/<aa>/<hash>       retained artifacts + manifest.json (tracked)
//   <project>/Library/GameCoreStudio/index.json        semantic index cache (untracked)
//   <project>/Library/GameCoreStudio/tool-catalog.json exported tool catalog (untracked)
// Tests point a runtime at a temporary state root so they never write into the project's history.
#nullable enable
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>File locations of one Studio runtime.</summary>
    public sealed class StudioPaths
    {
        public StudioPaths(string projectRoot, string? stateRoot = null, string? projectName = null)
        {
            if (string.IsNullOrEmpty(projectRoot))
            {
                throw new ArgumentException("A project root is required.", nameof(projectRoot));
            }

            ProjectRoot = Path.GetFullPath(projectRoot);
            StateRoot = Path.GetFullPath(stateRoot ?? projectRoot);
            ProjectName = string.IsNullOrEmpty(projectName) ? "project" : projectName!;
        }

        /// <summary>The Unity project directory (parent of Assets/).</summary>
        public string ProjectRoot { get; }

        /// <summary>Root of Studio/ and Library/GameCoreStudio/ (the project root unless a test redirects it).</summary>
        public string StateRoot { get; }

        /// <summary>Project id used in the semantic index (03 s3 <c>project</c>).</summary>
        public string ProjectName { get; }

        public string HistoryRoot => Path.Combine(StateRoot, "Studio", "History");

        public string ArtifactsRoot => Path.Combine(StateRoot, "Studio", "Artifacts");

        public string LibraryRoot => Path.Combine(StateRoot, "Library", "GameCoreStudio");

        public string IndexCachePath => Path.Combine(LibraryRoot, "index.json");

        public string IndexSourcesPath => Path.Combine(LibraryRoot, "index.sources.json");

        public string CatalogPath => Path.Combine(LibraryRoot, "tool-catalog.json");

        public string HistoryIndexPath => Path.Combine(LibraryRoot, "history-index.json");

        /// <summary>Absolute path of a project-relative asset path (<c>Assets/...</c>).</summary>
        public string Absolute(string assetPath) => Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));

        /// <summary>The paths of the open project: project root, the product name as project id.</summary>
        public static StudioPaths ForCurrentProject()
        {
            string root = Directory.GetParent(Application.dataPath)!.FullName;
            return new StudioPaths(root, null, ProjectIdOf(PlayerSettings.productName));
        }

        /// <summary>A lowercase id: letters and digits kept, every other run of characters becomes '-'.</summary>
        public static string ProjectIdOf(string? name)
        {
            StringBuilder id = new StringBuilder();
            bool dash = false;
            foreach (char character in (name ?? string.Empty).ToLowerInvariant())
            {
                if ((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9'))
                {
                    id.Append(character);
                    dash = false;
                }
                else if (!dash && id.Length > 0)
                {
                    id.Append('-');
                    dash = true;
                }
            }

            string text = id.ToString().Trim('-');
            return text.Length == 0 ? "project" : text;
        }

        /// <summary>Writes text atomically (temp file + replace) with '\n' newlines, creating directories.</summary>
        public static void WriteAllTextAtomic(string path, string text)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
