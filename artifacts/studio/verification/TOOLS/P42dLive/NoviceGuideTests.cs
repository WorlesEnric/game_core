#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Studio.Views;
using GameCore.Rules.Gameplay.Dialogue;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace P42d.Live
{
    public sealed class NoviceGuideTests
    {
        private static readonly string[] AuthoredPaths =
        {
            "Assets/Hollowmere/Dialogue/Graphs/Maren.asset",
            "Assets/Hollowmere/Rules/HollowmereContent.asset",
            "Assets/Hollowmere/Regions/ThornwickVillage.unity",
        };
        private readonly Dictionary<string, byte[]> preimages = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private SceneSetup[] scenes = Array.Empty<SceneSetup>();
        private bool restoreAuthoredFiles;

        [SetUp]
        public void SetUp()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("P4.2 novice guide acceptance requires a graphics-enabled Editor (omit -nographics); run artifacts/studio/verification/TOOLS/rows-p42l.py guide.");
            }
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")))
            {
                Assert.Ignore("P4.2 novice guide acceptance requires an explicit GAMECORE_P42_EVIDENCE directory; run artifacts/studio/verification/TOOLS/rows-p42l.py guide.");
            }
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                Assert.That(UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty, Is.False,
                    "guide acceptance requires saved scenes and must not discard user edits");
            }
            foreach (string path in AuthoredPaths)
            {
                Assert.That(EditorUtility.IsDirty(AssetDatabase.LoadMainAssetAtPath(path)), Is.False,
                    "guide acceptance requires saved authored assets: " + path);
                preimages[path] = File.ReadAllBytes(path);
                preimages[path + ".meta"] = File.ReadAllBytes(path + ".meta");
            }
            scenes = EditorSceneManager.GetSceneManagerSetup();
            restoreAuthoredFiles = true;
            Directory.CreateDirectory(Output);
        }

        [TearDown]
        public void TearDown()
        {
            if (!restoreAuthoredFiles) return;
            var failures = new List<Exception>();
            try
            {
                // Discard live test objects before restoring scene bytes; never save them over the preimage.
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                foreach (string path in AuthoredPaths.Where(path => !path.EndsWith(".unity", StringComparison.Ordinal)))
                {
                    try { AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(path)); }
                    catch (Exception error) { failures.Add(error); }
                }
            }
            finally
            {
                // Safety cleanup runs after every original acceptance assertion, including failed/aborted yields.
                // Restore complete authored bytes and metadata, not normalized fields or claimed undo receipts.
                foreach (KeyValuePair<string, byte[]> file in preimages)
                {
                    try { File.WriteAllBytes(file.Key, file.Value); }
                    catch (Exception error) { failures.Add(error); }
                }
                foreach (string path in AuthoredPaths)
                {
                    try { AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate); }
                    catch (Exception error) { failures.Add(error); }
                }
                try
                {
                    if (scenes.Any(scene => scene.isLoaded && scene.isActive && !string.IsNullOrEmpty(scene.path)))
                        EditorSceneManager.RestoreSceneManagerSetup(scenes);
                    else
                        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                }
                finally
                {
                    restoreAuthoredFiles = false;
                    preimages.Clear();
                    scenes = Array.Empty<SceneSetup>();
                }
            }
            if (failures.Count != 0) throw new AggregateException("Novice guide authored-file restoration failed", failures);
        }

        [UnityTest]
        public IEnumerator R2_38_Guide_NpcAndDialogueContextToolsUndo()
        {
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
            Assert.That(EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio"), Is.True);
            var context = StudioUiSession.Context;
            var runtime = context.Runtime;
            runtime.Index.Rebuild();
            var npc = AssetDatabase.LoadAssetAtPath<NpcDefinition>("Assets/Hollowmere/Npcs/Definitions/Maren.asset");
            var graph = AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>("Assets/Hollowmere/Dialogue/Graphs/Maren.asset");
            Assert.That(npc, Is.Not.Null); Assert.That(graph, Is.Not.Null);
            Assert.That(npc.Dialogue, Is.EqualTo(graph), "the placed definition already binds this dialogue graph");
            int baseline = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length;
            int lines = graph.Nodes.Count;
            var panel = new ContextPanelView(context);
            var receipt = new JObject { ["guide"] = "docs/studio/08-creator-guide.md: NPC and dialogue authoring boundary", ["rosterBefore"] = baseline, ["linesBefore"] = lines };
            var npcRef = runtime.Resolver.BuildRef(npc, AuthorScope.Definition, true);
            context.Selection.Set(new[] { npcRef }, SelectionOp.Replace);
            var add = ContextTools.For(runtime, npcRef).Single(t => t.Entry.Id == "npc.addAt");
            var well = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Single(g => g.name == "Village Well");
            Vector3 p = well.transform.position + new Vector3(4, 0, 3);
            var placed = panel.RunDirect(add, npcRef, new JObject { ["location"] = new JArray(p.x, p.y, p.z), ["name"] = "Guide NPC" }, npc.name);
            receipt["placement"] = StudioJson.ToToken(placed.Entry);
            Assert.That(placed.State, Is.EqualTo(ChangeSetState.Applied), string.Join(";", placed.Diagnostics));
            Assert.That(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length, Is.EqualTo(baseline + 1));
            var graphRef = runtime.Resolver.BuildRef(graph, AuthorScope.Definition, true);
            context.Selection.Set(new[] { graphRef }, SelectionOp.Replace);
            Assert.That(EditorApplication.ExecuteMenuItem("GameCore/Studio/Dialogue"), Is.True);
            using var viewContext = StudioViewContext.ForProject();
            var dialogue = new DialogueView(viewContext);
            dialogue.ShowGraph(graphRef);
            int terminal = Enumerable.Range(0, graph.Nodes.Count).First(i => graph.Nodes[i].kind == DialogueNodeKind.Line && graph.Target(i, DialoguePort.Next, 0) < 0);
            dialogue.SelectNode(terminal);
            var line = dialogue.AddLine("The Drowned Bell guides travellers home.", "Maren")!;
            receipt["dialogue"] = StudioJson.ToToken(line.Entry);
            Assert.That(line.State, Is.EqualTo(ChangeSetState.Applied), string.Join(";", line.Diagnostics));
            Assert.That(graph.Nodes.Count, Is.EqualTo(lines + 1));
            Assert.That(graph.Nodes.Last().text, Does.Contain("Drowned Bell"));
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            UnityWindowCapture.CaptureStudio(Path.Combine(Output, "guide-applied.png"), false);
            yield return null;
            var undoLine = runtime.History.Undo(line.Entry.Id);
            var undoNpc = runtime.History.Undo(placed.Entry.Id);
            receipt["dialogueUndoOk"] = undoLine.Ok; receipt["npcUndoOk"] = undoNpc.Ok;
            receipt["rosterAfterUndo"] = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Length;
            receipt["linesAfterUndo"] = graph.Nodes.Count;
            File.WriteAllText(Path.Combine(Output, "guide.json"), receipt.ToString());
            Assert.That(undoLine.Ok && undoNpc.Ok, Is.True);
            Assert.That(graph.Nodes.Count, Is.EqualTo(lines));
            Assert.That((int)receipt["rosterAfterUndo"]!, Is.EqualTo(baseline));
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
        }
        private static string Output => Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!;
    }
}
