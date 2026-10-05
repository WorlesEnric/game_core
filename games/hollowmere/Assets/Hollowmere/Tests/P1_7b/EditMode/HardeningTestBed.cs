// Hollowmere.P1_7b.EditMode.Tests - shared fixture of the P1.7b gameplay-hardening tests: a Studio runtime over
// Assets/Hollowmere (temp state root), temp assets under Tests/P1_7b/Temp, engine round trips, and a guard that puts
// every Hollowmere asset an experiment touched back to its on-disk state (P3.1 owns that content; nothing is saved).
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    /// <summary>One Studio runtime plus temp assets; dispose restores everything.</summary>
    public sealed class HardeningTestBed : IDisposable
    {
        public const string HollowmereFolder = "Assets/Hollowmere";

        public const string TempFolder = "Assets/Hollowmere/Tests/P1_7b/Temp";

        private readonly string _stateRoot;
        private readonly int _undoGroup;
        private readonly List<string> _created = new List<string>();

        public HardeningTestBed(string label)
        {
            _stateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p17b-" + label + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_stateRoot);
            Undo.IncrementCurrentGroup();
            _undoGroup = Undo.GetCurrentGroup();
            Runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, _stateRoot, "p17b-" + label),
                Log = new MemoryStudioLog(),
                SearchFolders = new[] { HollowmereFolder },
                IndexScope = AuthoringSourceScope.Assets,
                LoadIndexCache = false,
            });
        }

        public StudioRuntime Runtime { get; }

        /// <summary>Creates a temp ScriptableObject asset (with an authoring id) under <see cref="TempFolder"/>.</summary>
        public T Create<T>(string name, Action<T>? configure = null) where T : ScriptableObject
        {
            EnsureTempFolder();
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = name;
            configure?.Invoke(asset);
            MintId(asset);
            string path = AssetDatabase.GenerateUniqueAssetPath(TempFolder + "/" + name + ".asset");
            AssetDatabase.CreateAsset(asset, path);
            _created.Add(path);
            return asset;
        }

        /// <summary>The single asset of type <typeparamref name="T"/> named <paramref name="name"/> under Assets/Hollowmere (temp folder excluded).</summary>
        public static T Load<T>(string name) where T : UnityEngine.Object
        {
            T? found = null;
            foreach (string guid in AssetDatabase.FindAssets(name + " t:" + typeof(T).Name, new[] { HollowmereFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(TempFolder, StringComparison.Ordinal) || !string.Equals(Path.GetFileNameWithoutExtension(path), name, StringComparison.Ordinal))
                {
                    continue;
                }

                T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    Assert.That(found, Is.Null, "more than one " + typeof(T).Name + " named " + name + " under " + HollowmereFolder);
                    found = asset;
                }
            }

            Assert.That(found, Is.Not.Null, "Hollowmere has no " + typeof(T).Name + " named " + name);
            return found!;
        }

        public AuthoringRef Ref(UnityEngine.Object target)
        {
            AuthoringRef? reference = Runtime.Resolver.BuildRef(target);
            Assert.That(reference, Is.Not.Null, "no authoring ref for " + target.name);
            return reference!;
        }

        public JToken RefToken(UnityEngine.Object target) => StudioJson.ToToken(Ref(target));

        /// <summary>
        /// Applies one tool operation as a change set through the engine (validation, ToolRegistry.Invoke, Undo,
        /// journal) and asserts it applied and was journaled.
        /// </summary>
        public ApplyReport Apply(string toolId, UnityEngine.Object? target, JObject? args)
        {
            var operation = new Operation("op1", toolId, target != null ? Ref(target) : null, args ?? new JObject());
            ChangeSet changeSet = StudioRuntime.Single("P1.7b " + toolId, IntentOrigin.Manual, operation);
            ApplyReport report = Runtime.Engine.Apply(changeSet);
            Assert.That(report.Ok, Is.True, toolId + ": " + Describe(report));
            Assert.That(report.Journaled, Is.True, toolId + " is journaled");
            ChangeSet? journaled = Runtime.Journal.Read(changeSet.Id);
            Assert.That(journaled, Is.Not.Null, toolId + " has a journal entry");
            Assert.That(journaled!.EffectiveState, Is.EqualTo(ChangeSetState.Applied), toolId);
            Assert.That(journaled.Operations[0].Tool, Is.EqualTo(toolId));
            return report;
        }

        public static string Describe(ApplyReport report)
        {
            var lines = new List<string> { "state " + report.State };
            foreach (Diagnostic diagnostic in report.Diagnostics)
            {
                lines.Add(diagnostic.Code + " " + diagnostic.Message);
            }

            foreach (OperationOutcome outcome in report.Outcomes)
            {
                lines.Add(outcome.OpId + " " + outcome.Status + " " + outcome.Code + " " + outcome.Detail);
            }

            return string.Join("; ", lines);
        }

        public static void MintId(ScriptableObject asset)
        {
            System.Reflection.MethodInfo? ensure = asset.GetType().GetMethod("EnsureAuthoringId", Type.EmptyTypes);
            ensure?.Invoke(asset, null);
        }

        public void Dispose()
        {
            Runtime.Dispose();
            Undo.RevertAllDownToGroup(_undoGroup);
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                AssetDatabase.DeleteAsset(_created[i]);
            }

            if (AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }

            RestoreHollowmereFromDisk();
            if (Directory.Exists(_stateRoot))
            {
                Directory.Delete(_stateRoot, true);
            }
        }

        /// <summary>Reloads every Hollowmere asset left dirty (by a tool, a migration dry run or OnAfterDeserialize) from its file.</summary>
        public static void RestoreHollowmereFromDisk()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { HollowmereFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UnityEngine.Object? asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset != null && EditorUtility.IsDirty(asset))
                {
                    EditorUtility.ClearDirty(asset);
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/Hollowmere/Tests/P1_7b", "Temp");
            }
        }
    }
}
