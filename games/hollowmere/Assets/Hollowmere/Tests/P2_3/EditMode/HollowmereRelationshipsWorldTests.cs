// GameCore.Studio.Views.Hollowmere.Tests - W-VIEW-01 and W-VIEW-04 over Hollowmere: Maren's neighbourhood, impact of
// deleting the lantern (and the gate key, the item Odd's stall actually stocks), the world's three regions and portal
// triangle, and the world view's change sets.
#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Views.Hollowmere.Tests
{
    public sealed class HollowmereRelationshipsWorldTests : HollowmereViewsFixture
    {
        private static readonly HashSet<EdgeKind> AllKinds = new HashSet<EdgeKind>(EdgeKinds.All);

        [Test]
        public void Maren_ShowsHerDialogueGraphPatrolPointsAndRegion()
        {
            IndexNode maren = NodeAt(Root + "/Npcs/Definitions/Maren.asset");
            IndexNode graph = NodeAt(Root + "/Dialogue/Graphs/Maren.asset");
            IndexNode behaviour = NodeAt(Root + "/Npcs/Definitions/MarenBehaviour.asset");
            IndexNode region = NodeAt(Root + "/World/Regions/ThornwickVillage.asset");
            Stopwatch watch = Stopwatch.StartNew();
            RelationshipsView view = new RelationshipsView(Context);
            view.Depth = 2;
            view.SetRoots(new[] { maren.Ref.IdentityKey });
            Neighbourhood neighbourhood = view.Current!;
            Log("Maren neighbourhood depth 2: " + neighbourhood.Nodes.Count + " nodes, " + neighbourhood.Links.Count + " links, view refresh " + watch.ElapsedMilliseconds + " ms");

            Assert.That(neighbourhood.Find(graph.Ref.IdentityKey), Is.Not.Null, "NpcDefinition.dialogueGraph = dialogue.maren resolves to the graph");
            NeighbourNode? patrol = neighbourhood.Find(behaviour.Ref.IdentityKey);
            Assert.That(patrol, Is.Not.Null, "her behaviour");
            Assert.That(patrol!.Details, Has.Some.Contains("patrol points"), "the behaviour card shows the patrol points");
            bool inRegion = false;
            foreach (NeighbourNode node in neighbourhood.Nodes)
            {
                inRegion |= node.RegionKey == region.Ref.IdentityKey || node.Key == region.Ref.IdentityKey;
            }

            Assert.That(inRegion, Is.True, "her placed entity lies in ThornwickVillage (the region marker's contains edge)");
            Assert.That(view.Canvas.Find(graph.Ref.IdentityKey), Is.Not.Null, "the graph has a card");
            view.Dispose();
        }

        [Test]
        public void LanternImpact_ListsTheQuestRewardAndTheLootTable_AndGateKeyImpactListsTheVendorStock()
        {
            IndexNode lantern = NodeAt(Root + "/Items/Lantern.asset");
            IndexNode quest = NodeAt(Root + "/Quests/DrownedBell.asset");
            IndexNode loot = NodeAt(Root + "/Items/MarshLoot.asset");
            IndexNode gateKey = NodeAt(Root + "/Items/GateKey.asset");
            IndexNode stall = NodeAt(Root + "/Items/OddsStall.asset");
            RelationshipsView view = new RelationshipsView(Context) { ImpactMode = true };
            view.SetRoots(new[] { lantern.Ref.IdentityKey });
            ImpactAnalysis impact = view.Impact!;
            Log("lantern impact: " + string.Join("; ", Describe(impact)));

            Assert.That(impact.Affects(quest.Ref.IdentityKey), Is.True, "the quest reward (rewards[n].target, a nested reference)");
            ImpactRow? reward = null;
            foreach (ImpactRow row in impact.OfType("quest.quest"))
            {
                reward = row;
            }

            Assert.That(reward!.Field, Does.StartWith("rewards["), "the row names the reward field");
            Assert.That(impact.Affects(loot.Ref.IdentityKey), Is.True, "the marsh loot table entry");
            Assert.That(impact.CountsByType.ContainsKey("quest.quest"), Is.True);

            view.SetRoots(new[] { gateKey.Ref.IdentityKey });
            Log("gate key impact: " + string.Join("; ", Describe(view.Impact!)));
            Assert.That(view.Impact!.Affects(stall.Ref.IdentityKey), Is.True, "Odd's stall stocks the gate key (stock[0].item)");
            view.Dispose();
        }

        [Test]
        public void World_ListsThreeRegionsAndThePortalTriangle()
        {
            WorldView view = new WorldView(Context);
            view.Refresh();
            WorldDocument world = view.Document!;
            Log("world " + world.Name + ": " + world.Regions.Count + " regions, " + world.Portals.Count + " portals, schedules " + world.Schedules.Count);

            Assert.That(world.Regions.Count, Is.EqualTo(3));
            Assert.That(world.Portals.Count, Is.EqualTo(3));
            for (int a = 0; a < 3; a++)
            {
                for (int b = a + 1; b < 3; b++)
                {
                    Assert.That(world.Between(world.Regions[a].Key, world.Regions[b].Key), Is.Not.Null, world.Regions[a].Name + " - " + world.Regions[b].Name);
                }
            }

            WorldRegion thornwick = First(world.Regions, region => region.ScenePath == Thornwick);
            Assert.That(thornwick.Residency, Is.EqualTo("Resident"), "its scene is open");
            Assert.That(thornwick.EntityCount, Is.GreaterThan(0), "the open region's objects are indexed under it");
            Assert.That(thornwick.Spawn.HasValue, Is.True, "the open region's marker gives the spawn point");
            Assert.That(view.Canvas.Edges.Count, Is.EqualTo(6), "two directed edges per portal");
            view.Dispose();
        }

        [Test]
        public void World_AddPortalAndConnectRegionsMakeChangeSets()
        {
            WorldView view = new WorldView(Context);
            view.Refresh();
            WorldDocument world = view.Document!;
            WorldRegion thornwick = First(world.Regions, region => region.ScenePath == Thornwick);
            WorldPortal portal = First(world.Portals, item => item.RegionA == thornwick.Key || item.RegionB == thornwick.Key);

            Operation add = WorldEdits.AddPortal(portal, thornwick.Spawn!.Value + new Vector3(3f, 0f, 0f), 90f);
            Assert.That(add.Tool, Is.EqualTo(WorldEdits.AddPortalTool));
            Assert.That(add.Target!.IdentityKey, Is.EqualTo(portal.Ref.IdentityKey), "world.addPortal's engine target is the portal");
            ApplyReport added = Context.Edits.Apply(ViewEdits.Build("P2.3 test: add portal end", new[] { add }));
            IReadOnlyList<string> unbound = ToolBinding.UnboundParameters(WorldEdits.AddPortalTool);
            Log("world.addPortal: " + ViewEdits.Describe(added) + "; unbound parameters: " + string.Join(", ", unbound));
            Assert.That(Runtime.Journal.Exists(added.Entry.Id) || !added.Journaled, Is.True);
            if (unbound.Count == 0)
            {
                Assert.That(added.Ok, Is.True, "a bindable world.addPortal applies");
            }
            else
            {
                Assert.That(added.Ok, Is.False, "world.addPortal cannot receive its region through the engine (PACKET.md, left open)");
            }

            WorldRegion belfry = First(world.Regions, region => region.Key != thornwick.Key && world.Between(region.Key, thornwick.Key) != null);
            IReadOnlyList<Operation> connect = WorldEdits.ConnectRegions(Runtime, world, thornwick, belfry);
            Assert.That(connect.Count, Is.EqualTo(ToolBinding.IsBindable(WorldEdits.ConnectRegionsTool) ? 1 : 2));
            ApplyReport connected = Context.Edits.Apply(ViewEdits.Build("P2.3 test: connect regions", connect));
            Log("connect regions (" + connect.Count + " ops): " + ViewEdits.Describe(connected));
            Assert.That(connected.Ok, Is.True, ViewEdits.Describe(connected));
            view.Refresh();
            Assert.That(view.Document!.Portals.Count, Is.EqualTo(4));

            HistoryResult undo = Context.Edits.Undo(connected.Entry.Id);
            Log("undo connect: " + undo.Ok + " " + undo.State);
            Assert.That(undo.Ok, Is.True, string.Join("; ", undo.Diagnostics));
            Runtime.Index.Flush();
            view.Refresh();
            Assert.That(view.Document!.Portals.Count, Is.EqualTo(3));
            view.Dispose();
        }

        private static IEnumerable<string> Describe(ImpactAnalysis impact)
        {
            foreach (ImpactRow row in impact.Rows)
            {
                yield return row.ToString();
            }
        }
    }
}
