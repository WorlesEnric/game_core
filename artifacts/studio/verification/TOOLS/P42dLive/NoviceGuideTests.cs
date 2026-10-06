#nullable enable
using System;
using System.Collections;
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
