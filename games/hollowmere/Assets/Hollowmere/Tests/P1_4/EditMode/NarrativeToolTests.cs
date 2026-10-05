// Hollowmere P1.4 EditMode - the narrative authoring operations round-trip through assets, validation and Undo.
//
// Every tool runs on scratch assets under Assets/Hollowmere/Tests/P1_4/Scratch (deleted afterwards): what a tool writes
// reads back from the asset, converts to the model the runtime would bake, refuses invalid input with its GP code, and
// undoes with one Undo step.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Inventory.Editor;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.Quest.Editor;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using Hollowmere.NarrativeAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_4.EditMode.Tests
{
    public sealed class NarrativeToolTests
    {
        private const string Scratch = "Assets/Hollowmere/Tests/P1_4/Scratch";

        private GameplayContentSet set = null!;

        [SetUp]
        public void SetUp()
        {
            NarrativeAuthoring.EnsureFolder(Scratch);
            set = ScriptableObject.CreateInstance<GameplayContentSet>();
            set.Configure(null, new List<ScriptableObject>());
            AssetDatabase.CreateAsset(set, Scratch + "/ScratchContent.asset");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Scratch);
            AssetDatabase.SaveAssets();
        }

        [Test]
        public void DialogueTools_RoundTrip()
        {
            FactDefinition met = DialogueTools.SetFact(set, "scratch_met", 0, true);
            Assert.That(DialogueTools.SetFact(set, "scratch_met", 2, false), Is.SameAs(met), "setFact updates an existing fact");
            Assert.That(met.InitialValue, Is.EqualTo(2));
            Assert.That(met.Persistent, Is.False);
            Assert.Throws<ArgumentException>(() => DialogueTools.SetFact(set, "Not A Fact", 0, true));

            ConditionSetDefinition hasMet = NarrativeAuthoring.CreateAsset<ConditionSetDefinition>(set, "HasMet", string.Empty, Scratch + "/HasMet.asset", "test");
            hasMet.Configure(ConditionMode.All, new[] { ConditionEntry.Of(ConditionKind.Fact, met, CompareOp.GreaterOrEqual, 3) });
            DialogueGraphDefinition graph = NarrativeAuthoring.CreateAsset<DialogueGraphDefinition>(set, "ScratchGraph", string.Empty, Scratch + "/ScratchGraph.asset", "test");
            graph.Configure("Scratch", string.Empty, 0);
            int hello = DialogueTools.AddLine(graph, "Hello there.");
            int choice = DialogueTools.AddChoice(graph, new List<string> { "Again", "Bye" }, new List<int> { hello, -1 }, hello, "Well?");
            DialogueTools.LinkCondition(graph, choice, hasMet, 0);
            Assert.That(graph.Nodes.Count, Is.EqualTo(2));
            Assert.That(graph.Target(hello, DialoguePort.Next, 0), Is.EqualTo(choice));
            Assert.That(graph.Target(choice, DialoguePort.Option, 0), Is.EqualTo(hello));
            Assert.That(graph.Node(choice).options[0].condition, Is.SameAs(hasMet));
            Assert.Throws<ArgumentException>(() => DialogueTools.LinkCondition(graph, hello, hasMet));
            Assert.Throws<ArgumentException>(() => DialogueTools.AddLine(graph, "dangling", "", 9));

            NarrativeModelSet models = NarrativeAuthoring.Models(string.Empty, graph);
            Assert.That(models.Problems, Is.Empty, string.Join("\n", models.Problems));
            Assert.That(models.TryGetGraph(NarrativeRefs.KeyOf(graph), out DialogueGraphModel? model) && model != null, Is.True);
            Assert.That(model!.Nodes[choice].Options.Count, Is.EqualTo(2));
            Assert.That(model.Nodes[choice].Options[0].Condition, Is.Not.Null);

            string locked = DialogueTools.Preview(graph, string.Empty);
            string open = DialogueTools.Preview(graph, "scratch_met=3");
            Assert.That(locked, Is.Not.EqualTo(open), "the option condition shows in the preview");

            Undo.IncrementCurrentGroup();
            DialogueTools.AddLine(graph, "Undo me.");
            Assert.That(graph.Nodes.Count, Is.EqualTo(3));
            Undo.PerformUndo();
            Assert.That(graph.Nodes.Count, Is.EqualTo(2), "one Undo removes the added line");

            MediaGenerationResult voice = DialogueTools.GenerateVoice(graph, hello);
            Assert.That(voice.Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
        }

        [Test]
        public void QuestTools_RoundTrip()
        {
            FactDefinition done = DialogueTools.SetFact(set, "scratch_done", 0, true);
            ItemDefinition coin = NarrativeAuthoring.CreateAsset<ItemDefinition>(set, "ScratchCoin", string.Empty, Scratch + "/ScratchCoin.asset", "test");
            coin.Configure("Scratch Coin", 10, 1, 1, null, null);
            QuestDefinition quest = NarrativeAuthoring.CreateAsset<QuestDefinition>(set, "ScratchQuest", string.Empty, Scratch + "/ScratchQuest.asset", "test");
            int s0 = QuestTools.AddStage(quest, "Do it", "Set the fact.");
            int s1 = QuestTools.AddStage(quest, "Collect", "Hold two coins.");
            QuestTools.AddObjective(quest, s0, ObjectiveKind.Fact, done, string.Empty, 1, 0, "Done");
            QuestTools.AddObjective(quest, s1, ObjectiveKind.Collect, coin, string.Empty, 2, 0, "Coins");
            QuestTools.LinkReward(quest, RewardKind.Fact, done, 5, 0);
            Assert.Throws<ArgumentException>(() => QuestTools.AddObjective(quest, 7, ObjectiveKind.Fact, done));
            Assert.Throws<ArgumentException>(() => QuestTools.LinkReward(quest, RewardKind.Item, done, 1, 0));
            Assert.That(quest.Stages.Count, Is.EqualTo(2));
            Assert.That(quest.Objectives[1].target, Is.SameAs(coin));

            SimulationResult result = QuestTools.SimulateResult(quest, "fact:scratch_done=1; collect:ScratchCoin=2");
            Assert.That(result.Completed, Is.True, result.Text);
            Assert.That(result.Rewards.Count, Is.EqualTo(1));
            Assert.That(QuestTools.Simulate(quest, "fact:scratch_done=1"), Does.Contain("Collect").Or.Contain("stage 1"));
        }

        [Test]
        public void InventoryAndLogicTools_RoundTrip()
        {
            WorldDefinition? world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereNarrativeAuthoring.WorldPath);
            if (world == null)
            {
                Assert.Ignore("the Hollowmere world is not authored");
            }

            ItemDefinition coin = NarrativeAuthoring.CreateAsset<ItemDefinition>(set, "ScratchCoin", string.Empty, Scratch + "/ScratchCoin.asset", "test");
            coin.Configure("Scratch Coin", 10, 1, 2, null, null);
            InventoryDefinition bag = NarrativeAuthoring.CreateAsset<InventoryDefinition>(set, "ScratchBag", string.Empty, Scratch + "/ScratchBag.asset", "test");
            bag.Configure(2, 0, 0, true, string.Empty);
            InventoryTools.GrantStarting(bag, coin, 15);
            Assert.That(bag.Starting.Count, Is.EqualTo(1));
            Assert.That(bag.Starting[0].count, Is.EqualTo(15));
            Assert.Throws<ArgumentException>(() => InventoryTools.GrantStarting(bag, coin, 50), "50 coins do not fit two stacks of 10");
            InventoryTools.GrantStarting(bag, coin, 0);
            Assert.That(bag.Starting.Count, Is.EqualTo(0));

            VendorDefinition vendor = NarrativeAuthoring.CreateAsset<VendorDefinition>(set, "ScratchVendor", string.Empty, Scratch + "/ScratchVendor.asset", "test");
            InventoryTools.SetStock(vendor, coin, 4, 3, 1, false);
            InventoryTools.SetStock(vendor, coin, 6, 3, 1, false);
            Assert.That(vendor.Stock.Count, Is.EqualTo(1));
            Assert.That(vendor.Stock[0].stock, Is.EqualTo(6));
            NarrativeModelSet vendorModels = NarrativeAuthoring.Models(string.Empty, vendor);
            Assert.That(vendorModels.TryGetVendor(NarrativeRefs.KeyOf(vendor), out VendorModel? vendorModel) && vendorModel != null, Is.True);
            Assert.That(vendorModel!.Entries[0].BuyPrice, Is.EqualTo(3));

            set.Configure(world, set.Definitions);
            RegionDefinition marsh = world!.Regions[0];
            WorldItemDefinition placed = InventoryTools.PlaceItem(set, coin, marsh, new Vector3(1f, 0f, 2f), 3);
            Assert.That(placed.Region, Is.SameAs(marsh));
            Assert.That(placed.Count, Is.EqualTo(3));
            Assert.That(set.Definitions, Does.Contain(placed));
            set.Configure(null, set.Definitions);

            FactDefinition flag = DialogueTools.SetFact(set, "scratch_flag", 0, true);
            ActionSetDefinition actions = NarrativeAuthoring.CreateAsset<ActionSetDefinition>(set, "ScratchActions", string.Empty, Scratch + "/ScratchActions.asset", "test");
            actions.Configure(new[] { ActionEntry.Of(ActionKind.Grant, coin, 1) });
            ConditionSetDefinition flagged = NarrativeAuthoring.CreateAsset<ConditionSetDefinition>(set, "Flagged", string.Empty, Scratch + "/Flagged.asset", "test");
            flagged.Configure(ConditionMode.All, new[] { ConditionEntry.Of(ConditionKind.Fact, flag, CompareOp.Equal, 1) });
            RuleDefinition rule = LogicTools.AddRule(set, "ScratchRule", TriggerKind.FactSet, flag, 1, false, flagged, actions, true, 0, string.Empty,
                Scratch + "/ScratchRule.asset");
            Assert.That(rule.Trigger, Is.EqualTo(TriggerKind.FactSet));
            Assert.That(rule.ActionSet, Is.SameAs(actions));

            string skips = LogicTools.Test(rule, "fact.scratch_flag=0");
            string fires = LogicTools.Test(rule, "fact.scratch_flag=1");
            string once = LogicTools.Test(rule, "fact.scratch_flag=1; fired=1");
            Debug.Log("[P1.4] logic.test:\n" + skips + "\n" + fires + "\n" + once);
            Assert.That(skips, Does.Contain("skips"));
            Assert.That(fires, Does.Contain("FIRES"));
            Assert.That(once, Does.Contain("skips"));
            Assert.That(LogicTools.Explain(rule), Does.Contain("scratch_flag").Or.Contain("Flagged"));
            Assert.That(LogicValidator.Validate(rule), Is.Empty);
        }
    }
}
