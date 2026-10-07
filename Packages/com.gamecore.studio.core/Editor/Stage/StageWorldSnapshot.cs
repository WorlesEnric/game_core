#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    public sealed partial class StageAdmission
    {
        /// <summary>
        /// Explicit Stage saves authored edits through project.save, then exports the production bake's
        /// read-only computation. It never writes bake outputs or stamps, and never saves a Play world.
        /// </summary>
        public void PrepareStageWorldSnapshot()
        {
            string project = _runtime.Paths.ProjectRoot;
            string packages = Path.Combine(RepositoryRoot ?? throw new InvalidOperationException(
                "bake_stale: the registered source has no repository Packages directory"), "Packages");
            string revision = Options.SourceRevision?.Invoke() ?? string.Empty;
            if (EditorApplication.isPlaying)
            {
                StageWorldSnapshot.VerifySaved(project, packages, revision);
                return;
            }
            List<string> saved = StageWorldSnapshot.DirtySourcePaths();
            if (saved.Count > 0)
            {
                OperationResult save = _runtime.Registry.Invoke(BuiltInToolIds.ProjectSave, null, null);
                if (save.Status != OutcomeStatus.Applied)
                    throw new InvalidOperationException("bake_stale: project.save could not save authored inputs: " + save.Detail);
            }
            StageWorldSnapshot.Publish(project, packages, revision, saved);
        }

        internal void PrepareSavedStageWorldSnapshot()
        {
            if (RepositoryRoot == null || Options.SourceRevision == null) return;
            string previous = StageDataPaths.ContainedFile(_runtime.Paths.ProjectRoot, StageWorldSnapshot.RelativePath);
            if (File.Exists(previous)) File.Delete(previous);
            StageWorldSnapshot.Publish(_runtime.Paths.ProjectRoot, Path.Combine(RepositoryRoot, "Packages"),
                Options.SourceRevision(), Array.Empty<string>());
        }
    }

    [InitializeOnLoad]
    internal static class StageWorldSnapshotBeforePlay
    {
        static StageWorldSnapshotBeforePlay()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode || AssetDatabase.FindAssets("t:WorldDefinition").Length == 0) return;
                try { StageAdmission.Of(StudioServices.Runtime).PrepareSavedStageWorldSnapshot(); }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    // Never save implicitly or prevent Play. An explicit Stage reports the precise refusal.
                    Debug.LogWarning("[GameCore Studio] " + error.Message);
                }
            };
        }
    }

    /// <summary>Trusted Editor source projection; the companion independently rehashes its complete source inventory.</summary>
    public static class StageWorldSnapshot
    {
        public const string RelativePath = "Library/GameCoreStudio/StageWorldSnapshot.json";
        private const string Schema = "gamecore.studio.stage-world-snapshot/1";

        public static List<string> DirtySourcePaths(bool includeScenes = true)
        {
            var paths = new SortedSet<string>(StringComparer.Ordinal);
            foreach (UnityEngine.Object asset in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
            {
                if (!EditorUtility.IsPersistent(asset) || !EditorUtility.IsDirty(asset)) continue;
                string path = AssetDatabase.GetAssetPath(asset);
                if (path.StartsWith("Assets/", StringComparison.Ordinal)) paths.Add(path);
            }
            if (includeScenes)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.isDirty) paths.Add(string.IsNullOrEmpty(scene.path) ? "<untitled scene>" : scene.path);
                }
            }
            return new List<string>(paths);
        }

        public static void Publish(string project, string packages, string revision, IReadOnlyList<string> savedSourcePaths)
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw Stale("save and Stage in an idle Edit mode Editor");
            if (!string.Equals(Path.GetFullPath(project).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetDirectoryName(Application.dataPath), StringComparison.Ordinal))
                throw Stale("the snapshot must come from the registered open project");
            List<string> dirty = DirtySourcePaths();
            if (dirty.Count != 0) throw Stale("unsaved authored inputs: " + string.Join(", ", dirty));
            if (string.IsNullOrEmpty(revision)) throw Stale("the source revision is unavailable");

            JArray before = SourceInputs(project, packages);
            string[] worlds = AssetDatabase.FindAssets("t:WorldDefinition");
            if (worlds.Length != 1) throw Stale("expected one WorldDefinition; found " + worlds.Length);
            string worldPath = AssetDatabase.GUIDToAssetPath(worlds[0]);
            UnityEngine.Object world = AssetDatabase.LoadMainAssetAtPath(worldPath);
            if (world == null) throw Stale(worldPath + " does not load");
            Type? entry = ReflectionAdmissionCatalog.FindType(ReflectionAdmissionCatalog.EntryType);
            Type? pathsType = ReflectionAdmissionCatalog.FindType("GameCore.Gameplay.Compile.BakePaths");
            Type? resultType = ReflectionAdmissionCatalog.FindType("GameCore.Gameplay.Compile.BakeResult");
            MethodInfo? convention = pathsType?.GetMethod("ConventionFor", BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(string) }, null);
            // Use the exact production planner shared by Bake and Verify. No parallel bake algorithm or
            // candidate fingerprint is accepted. A public read-only description API is an upstream request.
            MethodInfo? compute = pathsType == null || resultType == null ? null : entry?.GetMethod("Compute",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { world.GetType(), pathsType, resultType.MakeByRefType() }, null);
            if (convention == null || compute == null) throw Stale(worldPath + ": production read-only bake adapter unavailable");
            object? paths = convention.Invoke(null, new object[] { worldPath });
            object?[] args = { world, paths, null };
            object? outputs;
            try { outputs = compute.Invoke(null, args); }
            catch (TargetInvocationException error) { throw Stale(worldPath + ": " + (error.InnerException ?? error).Message); }
            if (outputs == null) throw Stale(worldPath + ": " + args[2]);
            string? description = outputs.GetType().GetProperty("Description")?.GetValue(outputs) as string;
            string? sourcePath = pathsType!.GetProperty("DescriptionPath")?.GetValue(paths) as string;
            if (string.IsNullOrEmpty(description) || string.IsNullOrEmpty(sourcePath))
                throw Stale(worldPath + ": production planner supplied no catalog description");
            VerifyRuntimeBake(project, worldPath, paths!, outputs);
            JArray after = SourceInputs(project, packages);
            if (!JToken.DeepEquals(before, after) || DirtySourcePaths().Count != 0)
                throw Stale(worldPath + ": authored inputs changed while computing the snapshot; Stage again");
            var snapshot = new JObject
            {
                ["schema"] = Schema, ["sourceRevision"] = revision, ["worldPath"] = worldPath,
                ["sourcePath"] = sourcePath, ["description"] = description,
                ["sha256"] = ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(description)),
                ["inputs"] = after, ["savedSourcePaths"] = new JArray(savedSourcePaths),
            };
            string target = StageDataPaths.ContainedFile(project, RelativePath);
            StudioPaths.WriteAllTextAtomic(target, snapshot.ToString(Formatting.Indented) + "\n");
        }

        private static void VerifyRuntimeBake(string project, string worldPath, object paths, object outputs)
        {
            Type pathType = paths.GetType();
            Type outputType = outputs.GetType();
            string? generatedPath = pathType.GetProperty("GeneratedPath")?.GetValue(paths) as string;
            string? manifestPath = pathType.GetProperty("ManifestPath")?.GetValue(paths) as string;
            string? generated = outputType.GetProperty("GeneratedCode")?.GetValue(outputs) as string;
            string? fingerprint = outputType.GetProperty("Fingerprint")?.GetValue(outputs) as string;
            string? catalogType = outputType.GetProperty("CatalogTypeName")?.GetValue(outputs) as string;
            if (generatedPath == null || manifestPath == null || generated == null || fingerprint == null || catalogType == null)
                throw Stale(worldPath + ": production runtime bake adapter unavailable");
            string file = StageDataPaths.ContainedFile(project, generatedPath);
            UnityEngine.Object manifest = AssetDatabase.LoadMainAssetAtPath(manifestPath);
            string? baked = manifest == null ? null : manifest.GetType().GetProperty("CatalogFingerprint")?.GetValue(manifest) as string;
            string? loaded = new ReflectionAdmissionCatalog().MechanismFingerprint(catalogType, out string? problem);
            if (!File.Exists(file) || !string.Equals(File.ReadAllText(file), generated, StringComparison.Ordinal)
                || baked != fingerprint || loaded != fingerprint)
                throw Stale(worldPath + ": runtime bake is not current; rebake " + generatedPath + " and " + manifestPath
                    + " with GameCore/Gameplay/Bake World before staging" + (problem == null ? string.Empty : ": " + problem));
        }

        public static void VerifySaved(string project, string packages, string revision)
        {
            if (DirtySourcePaths(false).Count != 0) throw Stale("unsaved authored assets; stop Play and Stage again");
            string path = StageDataPaths.ContainedFile(project, RelativePath);
            if (!File.Exists(path)) throw Stale("no saved Edit-mode world snapshot; Stage once before entering Play");
            JObject snapshot = JObject.Parse(File.ReadAllText(path));
            string? description = (string?)snapshot["description"];
            if ((string?)snapshot["schema"] != Schema || (string?)snapshot["sourceRevision"] != revision
                || description == null || (string?)snapshot["sha256"] != ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(description))
                || !JToken.DeepEquals(snapshot["inputs"], SourceInputs(project, packages)))
                throw Stale((string?)snapshot["worldPath"] + ": source changed since Edit-mode export; stop Play and Stage again");
        }

        private static JArray SourceInputs(string project, string packages)
        {
            var files = new SortedDictionary<string, JObject>(StringComparer.Ordinal);
            void Visit(string root, string directory, string scope)
            {
                if (!Directory.Exists(directory)) return;
                foreach (string child in Directory.EnumerateFileSystemEntries(directory))
                {
                    string name = Path.GetFileName(child);
                    if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith("~", StringComparison.Ordinal)
                        || PrivateSource(name)) continue;
                    string relative = Path.GetRelativePath(root, child).Replace('\\', '/');
                    string path = StageDataPaths.ContainedFile(root, relative);
                    if (Directory.Exists(path))
                    {
                        switch (Path.GetFileName(path))
                        {
                            case "Library": case "Temp": case "Logs": case "obj": case "bin": case "node_modules": case "UserSettings": continue;
                        }
                        Visit(root, path, scope);
                    }
                    else
                    {
                        string digest;
                        using (SHA256 sha = SHA256.Create())
                        using (FileStream stream = File.OpenRead(path))
                            digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                        files.Add(scope + "/" + relative, new JObject { ["scope"] = scope, ["path"] = relative, ["sha256"] = digest });
                    }
                }
            }
            foreach (string name in new[] { "Assets", "ProjectSettings", "Packages" })
                Visit(project, StageDataPaths.ContainedFile(project, name), "project");
            Visit(packages, packages, "packages");
            return new JArray(files.Values);
        }

        private static bool PrivateSource(string name) => name.Equals("auth.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("providers.env", StringComparison.OrdinalIgnoreCase)
            || name.Equals("GameCoreStudio.json", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".key", StringComparison.OrdinalIgnoreCase);

        private static InvalidOperationException Stale(string detail) => new InvalidOperationException("bake_stale: " + detail);
    }
}
