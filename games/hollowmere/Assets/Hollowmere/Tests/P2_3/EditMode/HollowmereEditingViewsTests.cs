// GameCore.Studio.Views.Hollowmere.Tests - W-VIEW-02/03/05/06 over Hollowmere: dialogue edits as journaled change sets
// with undo, preview by facts, quest simulation on both branches, table row and multi-row commits, and the Changes
// view rendering a conflicting candidate.
#nullable enable
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Views.Hollowmere.Tests
{
    public sealed class HollowmereEditingViewsTests : HollowmereViewsFixture
    {
        private const string GraphPath = TempFolder + "/ViewsTestGraph.asset";

        [Test]
        public void Dialogue_AddLineConnectRename_AreJournaledChangeSetsAndUndoRestores()
        {
            RequireAuthoringIdValueType();
            using DialogueView view = new DialogueView(Context);
            ApplyReport created = Expect(view.CreateGraph(GraphPath), BuiltInToolIdsExt.Create);
            Assert.That(view.Document!.Entry, Is.EqualTo(0));
            Assert.That(view.Document.Nodes.Count, Is.EqualTo(1));
            AssetDatabase.SaveAssets();
            byte[] initial = File.ReadAllBytes(GraphPath);
            view.ShowGraph(view.Document.Ref);
            Assert.That(view.AddLine("No selection", string.Empty), Is.Null);
            Assert.That(view.AddChoice(new[] { "Yes" }, "No selection"), Is.Null);
            Assert.That(File.ReadAllBytes(GraphPath), Is.EqualTo(initial));
            List<byte[]> before = new List<byte[]>();
            List<ApplyReport> reports = new List<ApplyReport>();

            before.Add(File.ReadAllBytes(GraphPath));
            view.SelectNode(0);
            reports.Add(Expect(view.AddLine("Traveller, a word.", "Maren"), DialogueEdits.AddLineTool));
            Assert.That(view.Document!.Find(0, DialogueDocument.PortNext, 0)!.To, Is.EqualTo(1));
            AssetDatabase.SaveAssets();
            byte[] connected = File.ReadAllBytes(GraphPath);
            Assert.That(view.AddLine("Would orphan the previous continuation", string.Empty), Is.Null);
            Assert.That(view.AddChoice(new[] { "Yes" }, "Occupied continuation"), Is.Null);
            Assert.That(File.ReadAllBytes(GraphPath), Is.EqualTo(connected));

            before.Add(File.ReadAllBytes(GraphPath));
            reports.Add(Expect(Context.Edits.Apply(ViewEdits.Build("rename line", new[] { DialogueEdits.SetText(view.Document, 1, "Traveller!") })), BuiltInToolIdsExt.Set));
            view.Refresh();
            Assert.That(view.Document!.Nodes[1].Text, Is.EqualTo("Traveller!"));
            AssetDatabase.SaveAssets();

            before.Add(File.ReadAllBytes(GraphPath));
            view.SelectNode(1);
            reports.Add(Expect(view.AddChoice(new[] { "Yes", "No" }, "Help?"), DialogueEdits.AddChoiceTool));
            Assert.That(view.Document!.Find(1, DialogueDocument.PortNext, 0)!.To, Is.EqualTo(2));
            AssetDatabase.SaveAssets();

            before.Add(File.ReadAllBytes(GraphPath));
            reports.Add(Expect(Context.Edits.Apply(ViewEdits.Build("rename option", new[] { DialogueEdits.RenameOption(view.Document, 2, 1, "Not now") })), BuiltInToolIdsExt.Set));
            view.Refresh();
            Assert.That(view.Document!.Nodes[2].Options[1].Text, Is.EqualTo("Not now"));
            AssetDatabase.SaveAssets();

            before.Add(File.ReadAllBytes(GraphPath));
            view.SelectNode(2);
            reports.Add(Expect(view.AddLine("Thank you.", string.Empty), DialogueEdits.AddLineTool, 2));
            Assert.That(view.Document!.Find(2, DialogueDocument.PortOption, 0)!.To, Is.EqualTo(3));
            AssetDatabase.SaveAssets();

            before.Add(File.ReadAllBytes(GraphPath));
            reports.Add(Expect(view.Connect(2, DialogueDocument.PortOption, 1, 3), BuiltInToolIdsExt.Set));
            Assert.That(view.Document!.Find(2, DialogueDocument.PortOption, 1)!.To, Is.EqualTo(3));
            AssetDatabase.SaveAssets();
            foreach (ApplyReport report in reports)
            {
                ChangeSet? entry = Runtime.Journal.Read(report.Entry.Id);
                Assert.That(entry, Is.Not.Null, "journaled");
                Assert.That(entry!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
                Assert.That(entry.Intent.Origin, Is.EqualTo(IntentOrigin.Manual));
            }

            for (int i = reports.Count - 1; i >= 0; i--)
            {
                HistoryResult undo = Runtime.History.Undo(reports[i].Entry.Id);
                Assert.That(undo.Ok, Is.True, "undo " + reports[i].Entry.Intent.Text + ": " + string.Join("; ", undo.Diagnostics));
                AssetDatabase.SaveAssets();
                Assert.That(File.ReadAllBytes(GraphPath), Is.EqualTo(before[i]), "undo restores complete bytes at each creator step");
                Assert.That(Runtime.Journal.Read(reports[i].Entry.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Undone));
            }

            view.Refresh();
            Assert.That(view.Document!.Nodes.Count, Is.EqualTo(1), "every edit undone to the valid entry graph");
            Assert.That(view.Document.Edges.Count, Is.EqualTo(0));
            Assert.That(Runtime.Journal.Read(created.Entry.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(Runtime.History.Undo(created.Entry.Id).Ok, Is.True);
            Assert.That(File.Exists(GraphPath), Is.False, "undo creation removes the graph");
        }

        [Test]
        public void R9C_AddChoiceFromChoice_PreservesBothOptionsAndUndoBytes()
        {
            ApplyReport created = Context.Edits.Apply(ViewEdits.Build("Choice entry", new[]
            {
                ViewEdits.Op("op1", BuiltInToolIdsExt.Create, null, new JObject
                {
                    ["type"] = DialogueDocument.Type, ["name"] = "ViewsTestGraph", ["path"] = GraphPath,
                    ["fields"] = new JObject
                    {
                        ["entry"] = 0,
                        ["nodes"] = new JArray(new JObject
                        {
                            ["kind"] = "Choice", ["text"] = "Help?",
                            ["options"] = new JArray(new JObject { ["text"] = "Yes" }, new JObject { ["text"] = "No" }),
                        }),
                        ["edges"] = new JArray(),
                    },
                }),
            }));
            Assert.That(created.Ok, Is.True, ViewEdits.Describe(created));
            AssetDatabase.SaveAssets();
            byte[] before = File.ReadAllBytes(GraphPath);
            using DialogueView view = new DialogueView(Context);
            view.ShowGraph(Runtime.Resolver.BuildRef(AssetDatabase.LoadMainAssetAtPath(GraphPath), null, false)!);
            view.SelectNode(0);
            ApplyReport added = view.AddChoice(new[] { "Now", "Later" }, "When?")!;
            Assert.That(added, Is.Not.Null);
            Assert.That(added.Ok, Is.True, ViewEdits.Describe(added));
            Assert.That(added.Journaled, Is.True);
            Assert.That(view.Document!.Find(0, DialogueDocument.PortOption, 0)?.To, Is.EqualTo(1), "the selected choice must reach the new choice through an Option, not an unused Next edge");
            Assert.That(view.Document.Find(1, DialogueDocument.PortOption, 0)!.To, Is.EqualTo(-1));
            Assert.That(view.Document.Find(1, DialogueDocument.PortOption, 1)!.To, Is.EqualTo(-1));
            Assert.That(view.Document.Nodes[1].Options[1].Text, Is.EqualTo("Later"));
            Assert.That(Runtime.History.Undo(added.Entry.Id).Ok, Is.True);
            AssetDatabase.SaveAssets();
            Assert.That(File.ReadAllBytes(GraphPath), Is.EqualTo(before));
        }

        [Test]
        public void Dialogue_UndoOnMarenKeepsTheConditionReferences()
        {
            RequireAuthoringIdValueType();
            IndexNode maren = NodeAt(Root + "/Dialogue/Graphs/Maren.asset");
            DialogueView view = new DialogueView(Context);
            view.ShowGraph(maren.Ref);
            int nodes = view.Document!.Nodes.Count;
            AuthoringRef? condition = view.Document.Nodes[0].Condition;
            Assert.That(condition, Is.Not.Null, "Maren's entry branch has a condition");
            ApplyReport text = Context.Edits.Apply(ViewEdits.Build("text", new[] { DialogueEdits.SetText(view.Document, nodes - 1, "You rang it!") }));
            Assert.That(text.Ok, Is.True, ViewEdits.Describe(text));
            HistoryResult undo = UndoOrInconclusive(text.Entry.Id);
            Log("Maren set-text undo: " + undo.Ok + " " + undo.State + " " + string.Join("; ", undo.Diagnostics));
            Assert.That(undo.Ok, Is.True, string.Join("; ", undo.Diagnostics));
            view.Refresh();
            Assert.That(view.Document!.Nodes.Count, Is.EqualTo(nodes));
            Assert.That(view.Document.Nodes[0].Condition?.IdentityKey, Is.EqualTo(condition!.IdentityKey), "the branch condition survives the undo");
            view.Dispose();
        }

        [Test]
        public void Dialogue_PreviewDiffersByFact()
        {
            RequireAuthoringIdValueType();
            IndexNode maren = NodeAt(Root + "/Dialogue/Graphs/Maren.asset");
            DialogueView view = new DialogueView(Context);
            view.ShowGraph(maren.Ref);
            ToolInvocation before = view.RunPreview()!;
            view.SetFact("bell_rung", 1);
            ToolInvocation after = view.RunPreview()!;
            Log("preview default:\n" + before.Text + "\npreview bell_rung=1:\n" + after.Text);

            Assert.That(before.Ok, Is.True, before.Text);
            Assert.That(after.Ok, Is.True, after.Text);
            Assert.That(after.Text, Is.Not.EqualTo(before.Text), "the branch on bell_rung changes the reachable lines");
            Assert.That(view.Canvas.Nodes, Has.Some.Matches<Canvas.CanvasNode>(node => node.Highlighted), "the preview path is highlighted");
            view.Dispose();
        }

        [Test]
        public void Quest_SimulatePassesOnBothBranches()
        {
            RequireAuthoringIdValueType();
            IndexNode quest = NodeAt(Root + "/Quests/DrownedBell.asset");
            QuestsView view = new QuestsView(Context);
            view.ShowQuest(quest.Ref);
            // P3.1's Drowned Bell: three endings (1 let it sleep, 2 ring the bell, 3 free the echo); Maren's gratitude is the
            // reward of the two endings that ring the bell.
            Assert.That(view.Document!.BranchCount, Is.EqualTo(3));
            foreach (int branch in new[] { 1, 2, 3 })
            {
                QuestSimulation simulation = view.RunSimulation(branch)!;
                Log("simulate branch " + branch + " (" + view.Document.BranchName(branch) + "): " + simulation.Path + "\n" + QuestEdits.Describe(simulation));
                Assert.That(simulation.Ok, Is.True, simulation.Text);
                Assert.That(simulation.Completed, Is.True, simulation.Text);
                if (branch == 1)
                {
                    Assert.That(simulation.Rewards.Count, Is.EqualTo(0), "letting the bell sleep earns nothing");
                }
                else
                {
                    Assert.That(simulation.Rewards.Count, Is.GreaterThan(0), "the branch's rewards are granted");
                }
            }

            Assert.That(view.RuleKeys.Count, Is.GreaterThanOrEqualTo(0));
            view.Dispose();
        }

        [Test]
        public void Table_InlineEditIsOneSetOp_MultiRowIsNOpsInOneChangeSet()
        {
            TablesView view = new TablesView(Context);
            view.ShowTab(TablesView.ItemsTab);
            TableModel model = view.Model!;
            TableRow lantern = First(model.AllRows, row => row.Name == "Lantern");
            string originalPrice = lantern.Text("price");
            Assert.That(view.Stage(lantern, "price", "15"), Is.True, view.StatusText);
            Assert.That(view.Stage(lantern, "weight", "950"), Is.True, view.StatusText);
            ApplyReport row = view.CommitRow(lantern)!;
            Assert.That(row.Ok, Is.True, ViewEdits.Describe(row));
            Assert.That(row.Entry.Operations.Count, Is.EqualTo(1), "one row commit, one set op");
            Assert.That(row.Entry.Operations[0].Tool, Is.EqualTo(BuiltInToolIdsExt.Set));
            Assert.That(((JObject)row.Entry.Operations[0].Args!["fields"]!).Count, Is.EqualTo(2));
            Assert.That(row.Entry.EffectivePolicy, Is.EqualTo(ApplyPolicy.AllOrNothing));

            model = view.Model!;
            List<TableRow> rows = new List<TableRow>();
            foreach (string name in new[] { "Lantern", "OldCoin", "GateKey" })
            {
                rows.Add(First(model.AllRows, item => item.Name == name));
            }

            ApplyReport bulk = view.ApplyToSelection("price", "7", rows)!;
            Assert.That(bulk.Ok, Is.True, ViewEdits.Describe(bulk));
            Assert.That(bulk.Entry.Operations.Count, Is.EqualTo(3), "N rows, N ops, one change set");
            view.Refresh();
            foreach (TableRow item in view.Model!.AllRows)
            {
                if (item.Name == "Lantern")
                {
                    Assert.That(item.Text("price"), Is.EqualTo("7"));
                    Assert.That(item.Text("weight"), Does.StartWith("950"));
                }
            }

            Assert.That(Context.Edits.Undo(bulk.Entry.Id).Ok, Is.True);
            Assert.That(Context.Edits.Undo(row.Entry.Id).Ok, Is.True);
            view.Refresh();
            Assert.That(First(view.Model!.AllRows, item => item.Name == "Lantern").Text("price"), Is.EqualTo(originalPrice), "undo restores the price");
            Log("table csv:\n" + view.Model.ToCsv());
            view.Dispose();
        }

        [Test]
        public void Changes_RendersACandidateWithAConflictDiagnostic()
        {
            UnityEngine.Object lantern = AssetDatabase.LoadMainAssetAtPath(Root + "/Items/Lantern.asset");
            UnityEngine.Object coin = AssetDatabase.LoadMainAssetAtPath(Root + "/Items/OldCoin.asset");
            string otherStamp = Runtime.Resolver.ComputeStamp(coin)!;
            AuthoringRef stale = Runtime.Resolver.BuildRef(lantern, null, true)!.WithStamp(otherStamp);
            ChangeSet candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Agent: cheaper lantern", IntentOrigin.Agent), new[]
            {
                ViewEdits.SetOp("op1", stale, "price", 3),
                ViewEdits.Op("op2", BuiltInToolIdsExt.Set, Runtime.Resolver.BuildRef(coin, null, true), new JObject { ["field"] = "price", ["value"] = 2 }, new[] { "op1" }),
            });

            ChangesView view = new ChangesView(Context);
            view.Refresh();
            ChangeSetInspection inspection = view.Inspect(candidate);
            string rendered = view.RenderedOpTree;
            Log("changes view inspection: ok=" + inspection.Ok + " conflicts=" + inspection.ConflictCount + "\n" + rendered);

            Assert.That(inspection.ConflictCount, Is.GreaterThanOrEqualTo(1));
            DiagnosticRow conflict = First(inspection.Diagnostics, row => row.IsConflict);
            Assert.That(conflict.Expected, Is.EqualTo(otherStamp), "data.expected is the stamp the candidate was planned on");
            Assert.That(conflict.Actual, Is.EqualTo(Runtime.Resolver.ComputeStamp(lantern)), "data.actual is the current stamp");
            Assert.That(conflict.Where, Is.Not.Null, "the diagnostic navigates to its target");
            Assert.That(rendered, Does.Contain("Conflict"));
            Assert.That(rendered, Does.Contain("expected"));
            Assert.That(view.DependsOnCanvas.Edges.Count, Is.EqualTo(1), "op2 dependsOn op1");
            Assert.That(Runtime.Journal.Exists(candidate.Id), Is.False, "inspection writes nothing");
            Assert.That(view.Diagnostics, Has.Some.Matches<DiagnosticRow>(row => row.IsConflict), "the console aggregates it");
            view.Dispose();
        }

        private static ApplyReport Expect(ApplyReport? report, string tool, int operations = 1)
        {
            Assert.That(report, Is.Not.Null);
            Assert.That(report!.Ok, Is.True, ViewEdits.Describe(report));
            Assert.That(report.Entry.Operations.Count, Is.EqualTo(operations));
            Assert.That(report.Entry.Operations[0].Tool, Is.EqualTo(tool));
            Assert.That(report.Journaled, Is.True);
            return report;
        }
    }
}
