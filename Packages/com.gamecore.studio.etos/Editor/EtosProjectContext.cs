#nullable enable
using System;
using System.IO;
using System.Reflection;
using GameCore.Studio.Model;
using System.Text;
using System.Text.RegularExpressions;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos
{
    /// <summary>Trusted local identity. No candidate or transport field supplies a filesystem root.</summary>
    public static class EtosProjectContext
    {
        public static string LoadProjectId(string projectRoot)
        {
            string root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar);
            string file = Path.Combine(root, "UserSettings", "GameCoreStudio.Project.json");
            if (File.Exists(file))
            {
                JObject stored = JObject.Parse(File.ReadAllText(file));
                if ((string?)stored["path"] == root && Json.NormalizeSha256((string?)stored["projectId"] ?? "") is string id) return id;
            }
            string settings = File.ReadAllText(Path.Combine(root, "ProjectSettings", "ProjectSettings.asset"));
            Match match = Regex.Match(settings, @"(?m)^\s*productGUID:\s*([a-fA-F0-9]{32})\s*$");
            if (!match.Success) throw new InvalidOperationException("project_guid_missing");
            string projectId = Json.Sha256Hex(Encoding.UTF8.GetBytes(match.Groups[1].Value.ToLowerInvariant() + "\n" + root));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, new JObject { ["path"] = root, ["projectId"] = projectId }.ToString());
            return projectId;
        }

        public static string SourceRevision(string projectRoot)
        {
            // Trusted git metadata only; no subprocess and no candidate-controlled path.
            DirectoryInfo? directory = new DirectoryInfo(projectRoot);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, ".git"))) directory = directory.Parent;
            if (directory == null) throw new InvalidOperationException("source_revision_unavailable");
            string git = Path.Combine(directory.FullName, ".git");
            string head = File.ReadAllText(Path.Combine(git, "HEAD")).Trim();
            if (Regex.IsMatch(head, "^[a-f0-9]{40,64}$")) return head;
            if (!head.StartsWith("ref: refs/", StringComparison.Ordinal) || head.Contains("..")) throw new InvalidOperationException("source_revision_unavailable");
            string reference = head.Substring(5);
            string loose = Path.Combine(git, reference);
            if (File.Exists(loose)) return File.ReadAllText(loose).Trim();
            foreach (string line in File.ReadLines(Path.Combine(git, "packed-refs")))
                if (line.EndsWith(" " + reference, StringComparison.Ordinal)) return line.Split(' ')[0];
            throw new InvalidOperationException("source_revision_unavailable");
        }

        /// <summary>Optional UI seam, read on the Editor thread. ETOS does not require the UI package.
        /// Only the existing context for this exact runtime can supply an unpreviewed retained candidate.</summary>
        public static ChangeSet? OpenCandidate(StudioRuntime runtime, string id)
        {
            Type? session = Type.GetType("GameCore.Studio.UI.StudioUiSession, GameCore.Studio.UI.Editor");
            object? context = session?.GetProperty("ContextIfCreated", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (context == null || !ReferenceEquals(context.GetType().GetProperty("Runtime")?.GetValue(context), runtime)) return null;
            object? candidates = context.GetType().GetProperty("Candidates")?.GetValue(context);
            object? entry = candidates?.GetType().GetMethod("Find", new[] { typeof(string) })?.Invoke(candidates, new object[] { id });
            return entry?.GetType().GetProperty("ChangeSet")?.GetValue(entry) as ChangeSet;
        }

        public static CompanionStageService CreateStageService(CompanionClient client, StudioRuntime runtime) =>
            new CompanionStageService(client, id => runtime.Engine.Previews.TryGetValue(id, out StagedChangeSet staged)
                ? staged.ChangeSet : EtosProjectContext.OpenCandidate(runtime, id) ?? runtime.Journal.Read(id), () => runtime.Registry.Catalog, runtime.Artifacts.Read);

        public static void Bind(StudioRuntime runtime, CompanionClient client)
        {
            var options = StageAdmission.Of(runtime).Options;
            options.StageService = CreateStageService(client, runtime);
            options.ProjectId = client.Options.ProjectId;
            options.SourceRevision = () => SourceRevision(runtime.Paths.ProjectRoot);
            options.CatalogRevision = () => runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision();
        }
    }
}
