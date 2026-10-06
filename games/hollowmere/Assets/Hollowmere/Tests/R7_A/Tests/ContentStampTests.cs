#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using Driver = Hollowmere.P3_2.Workflows.Workflows;

namespace Hollowmere.R7_A
{
    public sealed class ContentStampTests
    {
        [Test]
        public void R7_A_ReopenBakeRestoresAllBytesAfterRetainedNarrativeUndoRedoUndo()
        {
            string root = Path.Combine(Path.GetTempPath(), "gamecore-r7-stamps-" + Guid.NewGuid().ToString("N"));
            string? priorOutput = Environment.GetEnvironmentVariable("GCS_P32_OUT");
            SceneSetup[] scenes = EditorSceneManager.GetSceneManagerSetup();
            BakePaths bakePaths = BakePaths.ConventionFor(ReopenPreparation.WorldPath);
            var watched = Directory.GetFiles("Assets/Hollowmere", "*.asset", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/Hollowmere", "*.unity", SearchOption.AllDirectories))
                .Concat(new[] { bakePaths.DescriptionPath, bakePaths.ReportPath, bakePaths.GeneratedPath })
                .Distinct(StringComparer.Ordinal).ToArray();
            var original = watched.ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
            try
            {
                Environment.SetEnvironmentVariable("GCS_P32_OUT", Path.Combine(root, "workflow"));
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                JObject saved;
                using (StudioRuntime runtime = Open(root))
                    saved = ReopenPreparation.ApplyRetained(runtime, Path.Combine(root, "prepare"));

                // Discard the runtime and reload disk assets/journals, rather than using the session's Unity Undo stack.
                foreach (string asset in ReopenPreparation.AssetPaths)
                    AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Assert.That(JToken.DeepEquals(saved["after"], ReopenPreparation.Hashes()), Is.True,
                    "The saved applied assets survive an import and fresh runtime unchanged.");
                using (StudioRuntime runtime = Open(root))
                {
                    runtime.Index.Rebuild();
                    string[] ids = ReopenPreparation.Tags.Select(tag => (string)saved["applied"]![tag]!["changeSetId"]!).ToArray();
                    foreach (string id in ids)
                        Assert.That(runtime.Journal.Read(id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
                    foreach (string id in ids.Reverse()) Require(runtime.History.Undo(id));
                    foreach (string id in ids) Require(runtime.History.Redo(id));
                    foreach (string id in ids.Reverse()) Require(runtime.History.Undo(id));
                }
                AssetDatabase.SaveAssets();
                Assert.Throws<InvalidOperationException>(() => Driver.RequireByteConsistency(saved["before"], ReopenPreparation.Hashes()),
                    "Regression witness: history restored authored fields, but the intervening Play bake's metadata is stale.");

                Driver.For("reopen").Single(step => step.Name == "bake restored content").Run();
                AssetDatabase.SaveAssets();
                foreach (string asset in ReopenPreparation.AssetPaths)
                    Assert.That(File.ReadAllBytes(asset), Is.EqualTo(original[asset]), asset + " including every serialized field");
                Driver.RequireByteConsistency(saved["before"], ReopenPreparation.Hashes());

                // A second production bake must not manufacture a new stamp or new bytes.
                var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(ReopenPreparation.WorldPath);
                BakeResult repeat = Entry.Bake(world, bakePaths, false);
                Assert.That(repeat.Succeeded, Is.True, repeat.ToString());
                Assert.That(repeat.ChangedFiles, Is.Empty);
                foreach (string asset in ReopenPreparation.AssetPaths)
                    Assert.That(File.ReadAllBytes(asset), Is.EqualTo(original[asset]), asset + " after repeat bake");
            }
            finally
            {
                // Fixture cleanup is deliberately after the byte assertions, never part of the consistency proof.
                Undo.ClearAll();
                foreach (KeyValuePair<string, byte[]> file in original)
                {
                    if (File.ReadAllBytes(file.Key).SequenceEqual(file.Value)) continue;
                    File.WriteAllBytes(file.Key, file.Value);
                    AssetDatabase.ImportAsset(file.Key, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
                Environment.SetEnvironmentVariable("GCS_P32_OUT", priorOutput);
                if (scenes.Any(scene => scene.isLoaded && scene.isActive && !string.IsNullOrEmpty(scene.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(scenes);
                else
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static StudioRuntime Open(string root) => StudioRuntime.Create(new StudioRuntimeOptions
        {
            Paths = new StudioPaths(WorkflowRunner.ProjectRoot, Path.Combine(root, "state"), "r7-stamp-regression"),
            LoadIndexCache = false,
            Log = new MemoryStudioLog(),
        });

        private static void Require(HistoryResult result) => Assert.That(result.Ok, Is.True, string.Join(" | ", result.Diagnostics));
    }
}
