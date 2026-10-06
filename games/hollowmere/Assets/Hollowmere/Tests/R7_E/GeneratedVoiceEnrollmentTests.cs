#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Dialogue;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.R7_E
{
    public sealed class GeneratedVoiceEnrollmentTests
    {
        private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, string> objects = new Dictionary<string, string>();
        private StudioRuntime? runtime;
        private SceneSetup[] scenes = Array.Empty<SceneSetup>();
        private string state = string.Empty;

        [SetUp]
        public void SetUp()
        {
            scenes = EditorSceneManager.GetSceneManagerSetup();
            // AttachGenerated owns fixed production paths. Preserve these committed fixtures, including
            // their exact disk bytes, rather than redirecting or mocking the authoring operation.
            foreach (string path in HollowmereDialogues.All.Select(HollowmerePaths.Graph)
                .Concat(new[] { HollowmerePaths.Director, HollowmerePaths.AudioBank }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                Assert.That(asset, Is.Not.Null, "Required Hollowmere fixture: " + path);
                Assert.That(EditorUtility.IsDirty(asset), Is.False, "Do not overwrite unsaved fixture edits: " + path);
                files.Add(path, File.ReadAllBytes(path));
                objects.Add(path, EditorJsonUtility.ToJson(asset));
            }
            state = Path.Combine(Path.GetTempPath(), "r7-e-voices-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            runtime?.Dispose();
            runtime = null;
            Undo.ClearAll();
            foreach (var snapshot in files)
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(snapshot.Key);
                if (asset != null)
                {
                    EditorJsonUtility.FromJsonOverwrite(objects[snapshot.Key], asset);
                    EditorUtility.ClearDirty(asset);
                }
                File.WriteAllBytes(snapshot.Key, snapshot.Value);
                AssetDatabase.ImportAsset(snapshot.Key, ImportAssetOptions.ForceUpdate);
            }
            files.Clear();
            objects.Clear();
            if (scenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(scenes);
            if (Directory.Exists(state)) Directory.Delete(state, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("R7_E")]
        public void AttachGenerated_EnrollsVoiceForDialogue_AndHistoryUndoRestoresBankPreimage(bool existingEntry)
        {
            // Existing imported generated media is enough: never call Generate or a paid service.
            string key = HollowmereDialogues.Build("Maren", false).Lines[0].Key;
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(GraphBuilder.VoicePath("Maren", key));
            Assert.That(clip, Is.Not.Null, "The committed Maren generated voice fixture must be imported.");
            var bank = AssetDatabase.LoadAssetAtPath<AudioBankDefinition>(HollowmerePaths.AudioBank);
            var graph = AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(HollowmerePaths.Graph("Maren"));
            AudioClip other = bank.Entries.First(entry => entry.Group == AudioGroup.Sfx && entry.Clip != null).Clip!;
            if (existingEntry)
            {
                bank.Assign(clip.name, other, AudioGroup.Sfx, 0.25f, true, true);
            }
            else
            {
                var serialized = new SerializedObject(bank);
                SerializedProperty entries = serialized.FindProperty("entries");
                for (int i = entries.arraySize - 1; i >= 0; i--)
                {
                    if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue == clip.name)
                        entries.DeleteArrayElementAtIndex(i);
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(bank.TryGet(clip.name, out _), Is.False);
            }
            // Also prove the graph attachment happens, rather than relying on its committed voice refs.
            foreach (var node in graph.Nodes) node.voiceClip = null;
            EditorUtility.SetDirty(graph);
            EditorUtility.SetDirty(bank);
            string bankBefore = EditorJsonUtility.ToJson(bank);
            string graphBefore = EditorJsonUtility.ToJson(graph);
            runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state, "r7-e-voices"),
                LoadIndexCache = false,
                Log = new MemoryStudioLog(),
            });
            runtime.Index.Rebuild();
            var author = new StudioAuthor(runtime);
            HollowmereMedia.AttachGenerated(author);

            Assert.That(graph.Nodes.Any(node => node.voiceClip == clip), Is.True, "Generated voice must be attached to its dialogue line.");
            Assert.That(bank.TryGet(clip.name, out AudioBankEntry? voice), Is.True,
                "A dialogue clip name must resolve through the playback bank, not only the graph.");
            Assert.That(voice!.Clip, Is.SameAs(clip));
            Assert.That(voice.Group, Is.EqualTo(AudioGroup.Voice));
            Assert.That(voice.Volume, Is.EqualTo(1f));
            Assert.That(voice.Loop, Is.False);
            Assert.That(voice.Spatial, Is.False);

            // Normal History, not Unity Undo or a forced restore. Clearing Unity's stack ensures the
            // durable reflected-tool inverse is responsible for the restored whole-bank preimage.
            Undo.ClearAll();
            string bankStep = runtime.Journal.List().Single(record => record.Intent.StartsWith("[P3.1:media.voices-bank-", StringComparison.Ordinal)).Id;
            HistoryResult undo = runtime.History.Undo(bankStep);
            Assert.That(undo.Ok, Is.True, string.Join("; ", undo.Diagnostics));
            Assert.That(EditorJsonUtility.ToJson(bank), Is.EqualTo(bankBefore), "Restore inserted/replaced entries and preserve unrelated SFX and voices.");
            Assert.That(graph.Nodes.Any(node => node.voiceClip == clip), Is.True, "Undoing bank enrollment must not undo a different graph step.");

            string graphStep = runtime.Journal.List().Single(record => record.Intent.StartsWith("[P3.1:media.voices-", StringComparison.Ordinal)
                && record.Intent.Contains(".Maren]")).Id;
            HistoryResult graphUndo = runtime.History.Undo(graphStep);
            Assert.That(graphUndo.Ok, Is.True, string.Join("; ", graphUndo.Diagnostics));
            Assert.That(EditorJsonUtility.ToJson(graph), Is.EqualTo(graphBefore));
        }
    }
}
