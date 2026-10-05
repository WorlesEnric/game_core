// GameCore.Studio.Views.Tests - W-VIEW-01: the index graph, neighbourhoods, impact, export, and the 2,000-node canvas
// budget (layout slices and refreshes stay under one 16 ms frame).
#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.Views.Tests
{
    public sealed class RelationshipsModelTests
    {
        private static readonly HashSet<EdgeKind> AllKinds = new HashSet<EdgeKind>(EdgeKinds.All);

        [Test]
        public void IndexGraph_ResolvesNameReferencesAndKeepsFieldLabels()
        {
            SyntheticIndex index = new SyntheticIndex();
            AuthoringRef graph = index.Node("g1", "dialogue.graph", "Maren", new Dictionary<string, IndexField> { ["npcGraphRef"] = SyntheticIndex.Text("dialogue.maren") });
            AuthoringRef npc = index.Node("npc1", "npc.definition", "Maren NPC");
            AuthoringRef byName = new AuthoringRef(AuthoringKind.Definition, path: IndexGraph.ByNamePrefix + "dialogue.graph:dialogue.maren");
            index.Edge(npc, byName);
            AuthoringRef item = index.Node("item1", "inventory.item", "Lantern");
            AuthoringRef quest = index.Node("q1", "quest.quest", "Drowned Bell");
            index.Edge(quest, item);
            Dictionary<string, string> labels = new Dictionary<string, string> { [EdgeKinds.Key(quest.IdentityKey, item.IdentityKey, EdgeKind.References)] = "rewards[0].target" };

            IndexGraph built = IndexGraph.Build(index.Build(), labels);

            Assert.That(built.Outgoing(npc.IdentityKey), Has.Some.Matches<GraphLink>(link => link.ToKey == graph.IdentityKey), "a string AuthorRef value resolves through the graph's npcGraphRef field");
            Assert.That(built.Incoming(item.IdentityKey)[0].Label, Is.EqualTo("rewards[0].target"));
            Assert.That(built.IsExternal(byName.IdentityKey), Is.False);
        }

        [Test]
        public void Neighbourhood_FollowsDepthAndEdgeKinds()
        {
            SemanticIndex index = SyntheticIndex.Tree(40, 3, out AuthoringRef root);
            IndexGraph graph = IndexGraph.Build(index);

            Neighbourhood one = RelationshipsModel.Build(graph, new[] { root.IdentityKey }, 1, AllKinds);
            Neighbourhood two = RelationshipsModel.Build(graph, new[] { root.IdentityKey }, 2, AllKinds);
            Neighbourhood referencesOnly = RelationshipsModel.Build(graph, new[] { root.IdentityKey }, 2, new HashSet<EdgeKind> { EdgeKind.References });

            Assert.That(one.Nodes.Count, Is.EqualTo(4), "root plus its three children");
            Assert.That(two.Nodes.Count, Is.EqualTo(13));
            Assert.That(referencesOnly.Nodes.Count, Is.LessThan(two.Nodes.Count), "contains edges filtered out");
            foreach (GraphLink link in referencesOnly.Links)
            {
                Assert.That(link.Kind, Is.EqualTo(EdgeKind.References));
            }
        }

        [Test]
        public void Impact_ListsReferrersTransitivelyWithCountsByType()
        {
            SyntheticIndex index = new SyntheticIndex();
            AuthoringRef lantern = index.Node("lantern", "inventory.item", "Lantern");
            AuthoringRef quest = index.Node("quest", "quest.quest", "Drowned Bell");
            AuthoringRef loot = index.Node("loot", "inventory.lootTable", "Marsh Loot");
            AuthoringRef content = index.Node("content", "logic.contentSet", "Content");
            AuthoringRef unrelated = index.Node("coin", "inventory.item", "Coin");
            index.Edge(quest, lantern);
            index.Edge(loot, lantern);
            index.Edge(content, quest);
            index.Edge(content, unrelated);
            IndexGraph graph = IndexGraph.Build(index.Build());

            ImpactAnalysis impact = ImpactAnalysis.Of(graph, lantern.IdentityKey);

            Assert.That(impact.Affects(quest.IdentityKey), Is.True);
            Assert.That(impact.Affects(loot.IdentityKey), Is.True);
            Assert.That(impact.Affects(content.IdentityKey), Is.True, "a referrer of a referrer is affected through it");
            Assert.That(impact.Affects(unrelated.IdentityKey), Is.False);
            Assert.That(impact.CountsByType["quest.quest"], Is.EqualTo(1));
            Assert.That(impact.CountsByType["inventory.lootTable"], Is.EqualTo(1));
        }

        [Test]
        public void Export_WritesJsonAndMermaid()
        {
            SemanticIndex index = SyntheticIndex.Tree(7, 2, out AuthoringRef root);
            Neighbourhood neighbourhood = RelationshipsModel.Build(IndexGraph.Build(index), new[] { root.IdentityKey }, 2, AllKinds);

            JObject json = RelationshipsModel.ToJson(neighbourhood);
            string mermaid = RelationshipsModel.ToMermaid(neighbourhood);

            Assert.That(json["schema"]!.ToString(), Is.EqualTo("gamecore.studio.views.subgraph/1"));
            Assert.That(((JArray)json["nodes"]!).Count, Is.EqualTo(neighbourhood.Nodes.Count));
            Assert.That(((JArray)json["links"]!).Count, Is.EqualTo(neighbourhood.Links.Count));
            Assert.That(mermaid, Does.StartWith("flowchart LR"));
            Assert.That(mermaid, Does.Contain("-->|references|"));
            Assert.That(mermaid, Does.Contain("style n0 stroke-width:3px"));
        }

        [Test]
        public void TwoThousandNodes_RenderWithinTheFrameBudget()
        {
            Stopwatch watch = Stopwatch.StartNew();
            SemanticIndex index = SyntheticIndex.Tree(2000, 13, out AuthoringRef root);
            IndexGraph graph = IndexGraph.Build(index);
            double buildGraph = watch.Elapsed.TotalMilliseconds;
            Neighbourhood neighbourhood = RelationshipsModel.Build(graph, new[] { root.IdentityKey }, 3, AllKinds);
            Assert.That(neighbourhood.Nodes.Count, Is.EqualTo(2000));

            GraphCanvas canvas = new GraphCanvas();
            canvas.SetViewportSize(new Vector2(1280f, 720f));
            watch.Restart();
            RelationshipsCanvas.Show(canvas, neighbourhood);
            double setGraph = watch.Elapsed.TotalMilliseconds;
            int frames = 1;
            while (canvas.LayoutPending && frames < 10000)
            {
                canvas.StepLayout(GraphCanvas.LayoutBudgetMs);
                frames++;
            }

            Assert.That(canvas.LayoutPending, Is.False);
            GraphLayout layout = canvas.Layout!;
            double maxRefresh = 0.0;
            for (int i = 0; i < 30; i++)
            {
                canvas.ZoomBy(i % 2 == 0 ? 1.25f : 0.8f);
                maxRefresh = System.Math.Max(maxRefresh, canvas.LastRefreshMilliseconds);
            }

            canvas.FrameAll();
            int compactCards = canvas.VisibleCardCount;
            canvas.Select(root.IdentityKey, true);
            int framedCards = canvas.VisibleCardCount;
            UnityEngine.Debug.Log("[P2.3] 2000-node relationships: index graph " + buildGraph.ToString("0.0") + " ms, neighbourhood " + neighbourhood.Milliseconds.ToString("0.0")
                + " ms, SetGraph " + setGraph.ToString("0.0") + " ms, layout " + layout.Steps + " slices max " + layout.MaxStepMilliseconds.ToString("0.00")
                + " ms total " + layout.TotalMilliseconds.ToString("0.0") + " ms, refresh max " + maxRefresh.ToString("0.00") + " ms, cards framed-all "
                + compactCards + " (compact " + canvas.IsCompact + "), cards at root " + framedCards);

            Assert.That(layout.MaxStepMilliseconds, Is.LessThan(16.0), "one layout slice per editor frame stays under 16 ms");
            Assert.That(maxRefresh, Is.LessThan(16.0), "a pan/zoom refresh stays under 16 ms");
            Assert.That(setGraph, Is.LessThan(16.0), "SetGraph builds the cards and runs one half-budget layout slice synchronously");
            Assert.That(framedCards, Is.LessThanOrEqualTo(GraphCanvas.MaxCards), "cards are virtualised");
        }
    }
}
