// GameCore.Studio.Views.Tests - the change sets the dialogue, quest and table views build (shape only; the Hollowmere
// tests apply them through the engine), quest.simulate path derivation and parsing, and the package checker.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Views.Tests
{
    public sealed class EditBuilderTests
    {
        private static DialogueDocument Graph()
        {
            JArray nodes = new JArray
            {
                new JObject { ["kind"] = "Line", ["text"] = "Hello", ["speaker"] = "Maren", ["options"] = new JArray() },
                new JObject { ["kind"] = "Choice", ["text"] = "Help?", ["options"] = new JArray(new JObject { ["text"] = "Yes" }, new JObject { ["text"] = "No" }) },
                new JObject { ["kind"] = "Line", ["text"] = "Thanks", ["options"] = new JArray() },
                new JObject { ["kind"] = "End", ["text"] = string.Empty, ["options"] = new JArray() },
            };
            JArray edges = new JArray
            {
                new JObject { ["from"] = 0, ["port"] = "Next", ["option"] = 0, ["to"] = 1 },
                new JObject { ["from"] = 1, ["port"] = "Option", ["option"] = 0, ["to"] = 2 },
                new JObject { ["from"] = 1, ["port"] = "Option", ["option"] = 1, ["to"] = -1 },
                new JObject { ["from"] = 2, ["port"] = "Next", ["option"] = 0, ["to"] = 3 },
            };
            return DialogueDocument.FromData(new AuthoringRef(AuthoringKind.Definition, authoringId: "graph-1", stamp: "sha256:aa"), "Test", 0, nodes, edges);
        }

        [Test]
        public void Dialogue_ConnectReplacesThePortAndSetsTheEdgeList()
        {
            DialogueDocument graph = Graph();
            graph.DefaultPort(1, out string port, out int option);
            Assert.That(port, Is.EqualTo(DialogueDocument.PortOption));
            Assert.That(option, Is.EqualTo(1), "the first option without a target");

            Operation connect = DialogueEdits.Connect(graph, 1, port, option, 3);
            Assert.That(connect.Tool, Is.EqualTo(BuiltInToolIdsExt.Set));
            Assert.That(connect.Target!.Stamp, Is.EqualTo("sha256:aa"), "edits carry the stamp the view read");
            JArray edges = (JArray)connect.Args!["value"]!;
            Assert.That(connect.Args!["field"]!.ToString(), Is.EqualTo("edges"));
            Assert.That(edges.Count, Is.EqualTo(4), "re-targeted in place");
            Assert.That(edges[2]!["to"]!.Value<int>(), Is.EqualTo(3));

            Operation append = DialogueEdits.Connect(graph, 3, DialogueDocument.PortNext, 0, -1);
            Assert.That(((JArray)append.Args!["value"]!).Count, Is.EqualTo(5));
        }

        [Test]
        public void Dialogue_TextAndRenameArePartialElementSets()
        {
            DialogueDocument graph = Graph();
            Operation text = DialogueEdits.SetText(graph, 2, "Thank you");
            JArray nodes = (JArray)text.Args!["value"]!;
            Assert.That(nodes.Count, Is.EqualTo(4));
            Assert.That(((JObject)nodes[0]).Count, Is.EqualTo(0), "untouched nodes are {}");
            Assert.That(nodes[2]!["text"]!.ToString(), Is.EqualTo("Thank you"));

            Operation rename = DialogueEdits.RenameOption(graph, 1, 1, "Not now");
            JArray options = (JArray)((JArray)rename.Args!["value"]!)[1]!["options"]!;
            Assert.That(options.Count, Is.EqualTo(2));
            Assert.That(options[1]!["text"]!.ToString(), Is.EqualTo("Not now"));
        }

        [Test]
        public void Dialogue_RemoveNodeShiftsNodesAndReindexesEdges()
        {
            Operation remove = DialogueEdits.RemoveNode(Graph(), 2);
            JObject fields = (JObject)remove.Args!["fields"]!;
            JArray nodes = (JArray)fields["nodes"]!;
            JArray edges = (JArray)fields["edges"]!;
            Assert.That(nodes.Count, Is.EqualTo(3));
            Assert.That(nodes[2]!["kind"]!.ToString(), Is.EqualTo("End"));
            Assert.That(edges.Count, Is.EqualTo(3), "the removed node's outgoing edge is dropped");
            Assert.That(edges[1]!["to"]!.Value<int>(), Is.EqualTo(-1), "an edge into the removed node ends the conversation");
        }

        [Test]
        public void Dialogue_AddLineUsesTheToolWithAfter()
        {
            Operation add = DialogueEdits.AddLine(Graph(), "New", "Maren", 2);
            Assert.That(add.Tool, Is.EqualTo(DialogueEdits.AddLineTool));
            Assert.That(add.Args!["after"]!.Value<int>(), Is.EqualTo(2));
            Assert.That(DialogueDocument.NodesNamedIn("[0] Hello\n  [1] Help?\n    > Yes [2] Thanks"), Is.EquivalentTo(new[] { 0, 1, 2 }));
        }

        private static QuestDocument Quest()
        {
            AuthoringRef fact = new AuthoringRef(AuthoringKind.Definition, authoringId: "fact-paid");
            AuthoringRef clapper = new AuthoringRef(AuthoringKind.Definition, authoringId: "item-clapper");
            AuthoringRef belfry = new AuthoringRef(AuthoringKind.Definition, authoringId: "region-belfry");
            AuthoringRef persuaded = new AuthoringRef(AuthoringKind.Definition, authoringId: "fact-persuaded");
            return QuestDocument.FromData(
                new AuthoringRef(AuthoringKind.Definition, authoringId: "quest-1"),
                "Bell",
                new[] { new QuestStage(0, "Gate", string.Empty, -1), new QuestStage(1, "Bell", string.Empty, -1) },
                new[]
                {
                    new QuestObjective(0, 0, "Fact", fact, string.Empty, 1, 1, "Pay"),
                    new QuestObjective(1, 0, "Fact", persuaded, string.Empty, 1, 2, "Persuade"),
                    new QuestObjective(2, 1, "Collect", clapper, string.Empty, 1, 0, "Clapper"),
                    new QuestObjective(3, 1, "Reach", belfry, string.Empty, 1, 0, "Belfry"),
                    new QuestObjective(4, 1, "Interact", null, "entity-bell", 1, 0, "Ring"),
                },
                new[] { new QuestReward(0, "Item", clapper, 1, 0) },
                new[] { "pay", "persuade" });
        }

        [Test]
        public void Quest_PathForABranchSatisfiesAlwaysAndBranchObjectives()
        {
            QuestDocument quest = Quest();
            IndexGraph graph = IndexGraph.Build(new SemanticIndex(1, "q", new List<IndexNode>
            {
                new IndexNode(new AuthoringRef(AuthoringKind.Definition, authoringId: "fact-paid"), "narrative.fact", "odd_paid", new Dictionary<string, IndexField> { ["factName"] = SyntheticIndex.Text("odd_paid") }),
            }));

            string pay = QuestEdits.PathFor(quest, 1, graph);
            string persuade = QuestEdits.PathFor(quest, 2, graph);

            Assert.That(pay, Is.EqualTo("fact:odd_paid=1; collect:item-clapper=1; reach:region-belfry; interact:entity-bell"));
            Assert.That(persuade, Does.StartWith("fact:fact-persuaded=1;"), "a fact without an indexed name falls back to its authoring id");
            Assert.That(quest.BranchCount, Is.EqualTo(2));
        }

        [Test]
        public void Quest_SimulationParsesStatusFactsInventoryAndRewards()
        {
            string text = "> Fact fact odd_paid=1 = 1\n  ObjectiveDone ...\n  RewardGranted item Lantern x1\n  RewardGranted fact bell_rung = 1\n= completed at stage 3 via pay";
            QuestSimulation simulation = QuestEdits.Parse("fact:odd_paid=1; collect:BellClapper=1", 1, new ToolInvocation(true, new JValue(text), null, null));

            Assert.That(simulation.Completed, Is.True);
            Assert.That(simulation.Stage, Is.EqualTo(3));
            Assert.That(simulation.BranchName, Is.EqualTo("pay"));
            Assert.That(simulation.Facts["odd_paid"], Is.EqualTo(1));
            Assert.That(simulation.Facts["bell_rung"], Is.EqualTo(1));
            Assert.That(simulation.Inventory["BellClapper"], Is.EqualTo(1));
            Assert.That(simulation.Inventory["Lantern"], Is.EqualTo(1));
            Assert.That(simulation.Rewards.Count, Is.EqualTo(2));
        }

        [Test]
        public void Table_ParsesByFieldSpecAndBuildsRowAndBulkChangeSets()
        {
            FieldSpec speed = new FieldSpec("speed", ValueTypes.Float, false, "m/s", 0.0, 10.0);
            FieldSpec mood = new FieldSpec("mood", ValueTypes.Enum, false, enumValues: new[] { "Calm", "Angry" });
            Assert.That(TableModel.TryParse(speed, "2.5", out JToken value, out _), Is.True);
            Assert.That(value.Value<double>(), Is.EqualTo(2.5));
            Assert.That(TableModel.TryParse(speed, "fast", out _, out string problem), Is.False);
            Assert.That(problem, Does.Contain("number"));
            Assert.That(TableModel.TryParse(speed, "42", out _, out _), Is.False, "the engine's range check applies");
            Assert.That(TableModel.TryParse(mood, "angry", out JToken enumValue, out _), Is.True);
            Assert.That(enumValue.ToString(), Is.EqualTo("Angry"));

            List<TableRow> rows = new List<TableRow>();
            for (int i = 0; i < 3; i++)
            {
                rows.Add(new TableRow("auth:r" + i, new AuthoringRef(AuthoringKind.Definition, authoringId: "r" + i), "Row " + i));
            }

            ChangeSet row = TableModel.CommitRow(rows[0], new JObject { ["speed"] = 2.5, ["mood"] = "Angry" }, "NPCs");
            Assert.That(row.Operations.Count, Is.EqualTo(1));
            Assert.That(row.Operations[0].Tool, Is.EqualTo(BuiltInToolIdsExt.Set));
            Assert.That(((JObject)row.Operations[0].Args!["fields"]!).Count, Is.EqualTo(2));
            Assert.That(row.EffectivePolicy, Is.EqualTo(ApplyPolicy.AllOrNothing));
            Assert.That(row.Intent.Origin, Is.EqualTo(IntentOrigin.Manual));

            ChangeSet bulk = TableModel.ApplyToRows(rows, "speed", new JValue(3.0), "NPCs");
            Assert.That(bulk.Operations.Count, Is.EqualTo(3), "one change set, one op per row");
        }

        [Test]
        public void Table_CsvQuotesPerRfc4180()
        {
            TableRow row = new TableRow("k", null, "a,b");
            row.Texts[TableModel.NameColumn] = "a,b";
            row.Texts["text"] = "say \"hi\"";
            TableModel model = new TableModel("t", null, new[] { new TableColumn(TableModel.NameColumn, null, false), new TableColumn("text", null, false) }, new List<TableRow> { row });
            Assert.That(model.ToCsv(), Is.EqualTo("name,text\r\n\"a,b\",\"say \"\"hi\"\"\"\r\n"));
        }

        [Test]
        public void PackageGraph_ReportsDependencyAndKernelProblems()
        {
            string root = Path.Combine(Path.GetTempPath(), "gc-p23-packages-" + Guid.NewGuid().ToString("N"));
            try
            {
                Write(root, "com.gamecore.contracts", "{\"com.gamecore.studio.core\":\"1.0.0\"}", "GameCore.Contracts", "[]");
                Write(root, "com.gamecore.studio.core", "{}", "GameCore.Studio.Core", "[\"GameCore.Contracts\"]");
                PackageGraph graph = PackageGraph.FromDirectories(new[] { Path.Combine(root, "com.gamecore.contracts"), Path.Combine(root, "com.gamecore.studio.core") });

                PackageNode kernel = graph.Find("com.gamecore.contracts")!;
                PackageNode studio = graph.Find("com.gamecore.studio.core")!;
                Assert.That(kernel.Problems, Has.Some.Contains("kernel package depends on com.gamecore.studio.core"));
                Assert.That(kernel.Problems, Has.Some.Contains("extra dependency com.gamecore.studio.core"));
                Assert.That(studio.Problems, Has.Some.Contains("missing dependency com.gamecore.contracts"));
                Assert.That(kernel.Layer, Is.EqualTo(0));
                Assert.That(studio.Layer, Is.EqualTo(4));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void Write(string root, string name, string dependencies, string assembly, string references)
        {
            string directory = Path.Combine(root, name);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "package.json"), "{\"name\":\"" + name + "\",\"version\":\"1.0.0\",\"unity\":\"6000.0\",\"displayName\":\"X\",\"description\":\"Y\",\"dependencies\":" + dependencies + "}");
            File.WriteAllText(Path.Combine(directory, assembly + ".asmdef"), "{\"name\":\"" + assembly + "\",\"references\":" + references + "}");
        }
    }
}
