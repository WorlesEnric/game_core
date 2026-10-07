#nullable enable
using System;
using System.IO;
using GameCore.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.R9_A
{
    public sealed class FinalDialogueWorkflowTests
    {
        [Test]
        public void Dialogue_AddLineConnectRename_OneFinalValidChangeSetAndUndoRestores()
        {
            string folder = "Assets/R9_A_" + Guid.NewGuid().ToString("N");
            string state = Path.Combine(Path.GetTempPath(), "r9-a-views-" + Guid.NewGuid().ToString("N"));
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            try
            {
                var graph = ScriptableObject.CreateInstance<DialogueGraphDefinition>();
                graph.name = "R9ViewsGraph";
                var choice = new DialogueNodeEntry { kind = DialogueNodeKind.Choice, text = "Help?" };
                choice.options.Add(new DialogueOptionEntry { text = "Yes" });
                choice.options.Add(new DialogueOptionEntry { text = "No" });
                graph.AddNode(choice);
                graph.Link(0, DialoguePort.Option, 0, -1);
                graph.Link(0, DialoguePort.Option, 1, -1);
                string path = folder + "/Graph.asset";
                AssetDatabase.CreateAsset(graph, path);
                AssetDatabase.SaveAssets();
                byte[] before = File.ReadAllBytes(path);
                using var runtime = StudioRuntime.Create(new StudioRuntimeOptions
                {
                    Paths = new StudioPaths(Directory.GetParent(Application.dataPath)!.FullName, state, "r9-a-views"),
                    SearchFolders = new[] { folder }, LoadIndexCache = false,
                });
                runtime.Index.Rebuild();
                using var context = new StudioViewContext(runtime, new ListSelectionBridge(), new ReflectionGameplayBridge(null, () => false), false);
                using var view = new DialogueView(context);
                view.ShowGraph(runtime.Resolver.BuildRef(graph, AuthorScope.Definition, false)!);
                var document = view.Document!;
                Operation add = DialogueEdits.AddLine(document, "Thank you.", "Maren");
                Operation connect = DialogueEdits.Connect(document, 0, DialogueDocument.PortOption, 0, 1);
                // Build the rename from the final document shape, so its partial-node array keeps the new line.
                var nodes = (JArray)document.RawNodes.DeepClone();
                nodes.Add(new JObject { ["kind"] = "Line", ["speaker"] = "Maren", ["text"] = "Thank you." });
                var finalDocument = DialogueDocument.FromData(document.Ref, document.Name, 0, nodes, (JArray)document.RawEdges.DeepClone());
                Operation rename = DialogueEdits.RenameOption(finalDocument, 0, 1, "Not now");
                ChangeSet edit = ViewEdits.Build("Add line, connect it, rename the other choice", new[]
                {
                    ViewEdits.Op("add", add.Tool, add.Target, add.Args),
                    ViewEdits.Op("connect", connect.Tool, connect.Target, connect.Args, new[] { "add" }),
                    ViewEdits.Op("rename", rename.Tool, rename.Target, rename.Args, new[] { "connect" }),
                });
                ApplyReport applied = context.Edits.Apply(edit);
                Assert.That(applied.Ok, Is.True, ViewEdits.Describe(applied));
                view.Refresh();
                Assert.That(view.Document!.Nodes.Count, Is.EqualTo(2));
                Assert.That(view.Document.Find(0, DialogueDocument.PortOption, 0)!.To, Is.EqualTo(1));
                Assert.That(view.Document.Nodes[1].Text, Is.EqualTo("Thank you."));
                Assert.That(view.Document.Nodes[0].Options[1].Text, Is.EqualTo("Not now"));
                Assert.That(runtime.Journal.Read(edit.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
                Assert.That(runtime.Journal.Read(edit.Id)!.Intent.Origin, Is.EqualTo(IntentOrigin.Manual));
                var undone = runtime.History.Undo(edit.Id);
                Assert.That(undone.Ok, Is.True, string.Join(" | ", undone.Diagnostics));
                view.Refresh();
                Assert.That(view.Document!.Nodes.Count, Is.EqualTo(1));
                Assert.That(view.Document.Nodes[0].Options[1].Text, Is.EqualTo("No"));
                Assert.That(view.Document.Find(0, DialogueDocument.PortOption, 0)!.To, Is.EqualTo(-1));
                Assert.That(runtime.Journal.Read(edit.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Undone));
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(path), Is.EqualTo(before), "one journal undo restores complete fixture bytes");
            }
            finally
            {
                AssetDatabase.DeleteAsset(folder);
                if (Directory.Exists(state)) Directory.Delete(state, true);
            }
        }
    }
}
