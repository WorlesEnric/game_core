// Hollowmere.P1_7b.EditMode.Tests - B7: every mutating tool added by P1.7b (B3 plus the authoring tools of the new B2
// fields) round-trips as a change set through the Studio engine (ChangeSetValidator -> ToolRegistry.Invoke -> Undo ->
// journal) and lands in the journal as Applied; its effect is checked on temp assets under Tests/P1_7b/Temp. The pure
// ones (interaction.explain, logic.whyNot, quest.inspectRuntime) are ReadOnly and run through ToolRegistry.Invoke.
#nullable enable
using System.Linq;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public sealed class ToolJournalTests
    {
        private static readonly string[] NewToolIds =
        {
            "npc.setDialogue", "npc.setAppearance", "interaction.setActions", "interaction.explain", "dialogue.setConsequence",
            "quest.setBranch", "quest.setConsequence", "quest.inspectRuntime", "quest.setPrerequisites", "inventory.setPrice",
            "inventory.bindUse", "logic.whyNot", "world.configurePortal", "world.setRegionBounds", "authoring.migrateRefs",
        };

        private HardeningTestBed? _bed;
        private Fixture? _f;

        private HardeningTestBed Bed => _bed!;

        private Fixture F => _f!;

        [SetUp]
        public void SetUp()
        {
            _bed = new HardeningTestBed("tools");
            _f = new Fixture(_bed);
            _bed.Runtime.Index.Rebuild();
        }

        [TearDown]
        public void TearDown()
        {
            _bed?.Dispose();
            _bed = null;
            _f = null;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void Catalog_ExportsEveryNewTool_AndTheMediaToolsAreComposeToolsThatRequireAgentMedia()
        {
            ToolCatalog catalog = Bed.Runtime.Registry.Catalog;
            foreach (string id in NewToolIds)
            {
                Assert.That(catalog.Tools.Any(t => t.Id == id), Is.True, id + " is in the catalog; problems: " + string.Join("; ", Bed.Runtime.Registry.Problems));
            }

            foreach (string id in new[] { "dialogue.generateVoice", "audio.generateVoice", "audio.generateSfx" })
            {
                ToolEntry tool = catalog.Tools.Single(t => t.Id == id);
                Assert.That(tool.Tier, Is.EqualTo(GameCore.Studio.Model.ToolTier.Compose), id);
                Assert.That(tool.Prerequisites?.Any(p => p.Requires == "agent.media") ?? false, Is.False, id);
            }

            Assert.That(catalog.Tools.Any(t => t.Id == "dialogue.graphView" || t.Id == "world.flowView"), Is.False, "graph and flow views are P2.3 views, not tools");
        }

        [Test]
        public void NpcTools_SetDialogueAndAppearance_AreJournaled()
        {
            string scenePath = HardeningTestBed.TempFolder + "/P17bNpc.unity";
            F.RegionA.Configure("NPC Region", scenePath);
            F.RegionA.SetBounds(Vector3.zero, new Vector3(60f, 20f, 60f));
            WorldDefinition world = Bed.Create<WorldDefinition>("P17bNpcWorld", w =>
            {
                w.AddRegion(F.RegionA);
                w.SetStartRegion(F.RegionA);
            });
            F.Content.Configure(world, F.Content.Definitions);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AuthoredRegion marker = new GameObject("NPC Region").AddComponent<AuthoredRegion>();
            marker.Configure(F.RegionA, null);
            marker.Bounds.Configure(F.RegionA.Bounds.center, F.RegionA.Bounds.size);
            Assert.That(EditorSceneManager.SaveScene(scene, scenePath), Is.True);
            Bed.Runtime.Index.Rebuild();
            Bed.Apply("npc.setDialogue", F.Npc, new JObject { ["graph"] = Bed.RefToken(F.Graph) });
            Assert.That(F.Npc.Dialogue, Is.SameAs(F.Graph));
            Assert.That(F.Npc.DialogueGraph, Is.EqualTo(F.Graph.AuthoringId), "the conversation starter gets the graph's authoring id");
            Bed.Apply("npc.setAppearance", F.Npc, new JObject { ["variant"] = Bed.RefToken(F.Variant) });
            Assert.That(F.Npc.Appearance, Is.SameAs(F.Variant));
            Assert.That(F.Npc.AppearanceVariant, Is.EqualTo(1));
        }

        [Test]
        public void InteractionTools_LinkConditionSetActionsExplain_AreJournaled()
        {
            Bed.Apply("interaction.linkCondition", F.Door, new JObject { ["condition"] = Bed.RefToken(F.Condition) });
            Assert.That(F.Door.Condition, Is.SameAs(F.Condition));
            Assert.That(F.Door.ConditionRef, Is.EqualTo(F.Condition.AuthoringId));
            Bed.Apply("interaction.setActions", F.Door, new JObject { ["actions"] = Bed.RefToken(F.Actions) });
            Assert.That(F.Door.Actions, Is.SameAs(F.Actions));
            Assert.That(F.Door.ActionRef, Is.EqualTo(F.Actions.AuthoringId));
            Bed.Invoke("interaction.explain", F.Door, new JObject { ["state"] = string.Empty });
        }

        [Test]
        public void DialogueTool_SetConsequence_IsJournaled()
        {
            Bed.Apply("dialogue.setConsequence", F.Graph, new JObject { ["node"] = 1, ["actions"] = Bed.RefToken(F.Actions) });
            Assert.That(F.Graph.Node(1).actions, Is.SameAs(F.Actions));
        }

        [Test]
        public void QuestTools_BranchConsequencePrerequisitesInspect_AreJournaled()
        {
            Bed.Apply("quest.setBranch", F.Quest, new JObject { ["objective"] = 1, ["branch"] = 1, ["branchName"] = "the long way" });
            Assert.That(F.Quest.Objectives[1].branch, Is.EqualTo(1));
            Assert.That(F.Quest.BranchNames[0], Is.EqualTo("the long way"));
            Bed.Apply("quest.setConsequence", F.Quest, new JObject { ["onComplete"] = Bed.RefToken(F.Actions) });
            Assert.That(F.Quest.CompletionActions, Is.SameAs(F.Actions));
            Bed.Apply("quest.setPrerequisites", F.Dependent, new JObject { ["prerequisites"] = new JArray(Bed.RefToken(F.Quest)) });
            Assert.That(F.Dependent.Prerequisites, Is.EquivalentTo(new[] { F.Quest }));
            Bed.Invoke("quest.inspectRuntime", F.Quest, new JObject { ["state"] = string.Empty });
        }

        [Test]
        public void InventoryTools_SetPriceAndBindUse_AreJournaled()
        {
            Bed.Apply("inventory.setPrice", F.Item, new JObject { ["price"] = 7 });
            Assert.That(F.Item.Price, Is.EqualTo(7));
            Bed.Apply("inventory.setPrice", F.Item, new JObject { ["price"] = 4, ["vendor"] = Bed.RefToken(F.Vendor) });
            Assert.That(F.Vendor.Stock.Single(s => s.item == F.Item).buyPrice, Is.EqualTo(4));
            Bed.Apply("inventory.bindUse", F.Item, new JObject { ["actions"] = Bed.RefToken(F.Actions) });
            Assert.That(F.Item.UseActions, Is.SameAs(F.Actions));
        }

        [Test]
        public void LogicTools_WhyNotAndMigrateRefs_AreJournaled()
        {
            InteractionToolsLink();
            Bed.Invoke("logic.whyNot", F.Content, new JObject { ["subject"] = Bed.RefToken(F.Door), ["state"] = string.Empty });
            Bed.Apply("authoring.migrateRefs", null, new JObject { ["folders"] = new JArray(HardeningTestBed.TempFolder), ["apply"] = false });
        }

        [Test]
        public void WorldTools_ConfigurePortalAndRegionBounds_AreJournaled()
        {
            Bed.Apply("world.configurePortal", F.Portal, new JObject { ["condition"] = Bed.RefToken(F.Condition), ["spawnPointA"] = "jetty" });
            Assert.That(F.Portal.Condition, Is.SameAs(F.Condition));
            Assert.That(F.Portal.SpawnPointIn(F.RegionA), Is.EqualTo("jetty"));
            Assert.That(F.Portal.ConditionRef, Is.EqualTo(F.Condition.AuthoringId));
            Bed.Apply("world.setRegionBounds", F.RegionA, new JObject { ["center"] = new JArray(0, 1, 0), ["size"] = new JArray(40, 10, 40) });
            Assert.That(F.RegionA.HasBounds, Is.True);
        }

        private void InteractionToolsLink()
        {
            Undo.RecordObject(F.Door, "p17b");
            F.Door.SetCondition(F.Condition, null, CompareOp.NotEqual, 0);
        }

        /// <summary>Temp definitions every tool test works on.</summary>
        private sealed class Fixture
        {
            public Fixture(HardeningTestBed bed)
            {
                Variant = bed.Create<VariantDefinition>("P17bVariant");
                Entity = bed.Create<EntityDefinition>("P17bEntity", e => e.SetVariants(new[] { Variant }));
                Npc = bed.Create<NpcDefinition>("P17bNpc", n => n.Configure(Entity, "Tmp", 1.5f, string.Empty, string.Empty));
                Fact = bed.Create<FactDefinition>("P17bFlag", f => f.Configure("p17b_flag", 0, true));
                Condition = bed.Create<ConditionSetDefinition>("P17bCondition", c => c.Configure(ConditionMode.All, new[]
                {
                    ConditionEntry.Of(ConditionKind.Fact, Fact, CompareOp.NotEqual, 0),
                }));
                Actions = bed.Create<ActionSetDefinition>("P17bActions", a => a.Configure(new[] { ActionEntry.Of(ActionKind.SetFact, Fact, 1) }));
                Graph = bed.Create<DialogueGraphDefinition>("P17bGraph", g =>
                {
                    g.Configure("Tmp", string.Empty, 0);
                    int line = g.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Line, text = "Hello." });
                    int action = g.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Action });
                    g.Link(line, DialoguePort.Next, 0, action);
                });
                Item = bed.Create<ItemDefinition>("P17bItem", i => i.Configure("Tmp item", 1, 0, 2, null, null));
                Vendor = bed.Create<VendorDefinition>("P17bVendor", v =>
                {
                    v.Configure("Tmp vendor", null, 4, string.Empty);
                    v.SetStock(Item, 1, -1, -1, false);
                });
                Quest = bed.Create<QuestDefinition>("P17bQuest", q => Stage(q, Fact));
                Dependent = bed.Create<QuestDefinition>("P17bDependent", q => Stage(q, Fact));
                Door = bed.Create<InteractableDefinition>("P17bDoor", d => d.Configure(Entity, InteractableKind.Door, 2f, 0.5f, 0));
                RegionA = bed.Create<RegionDefinition>("P17bRegionA", r => r.SetSpawnPoint("jetty", new Vector3(1f, 0f, 1f), 90f));
                RegionB = bed.Create<RegionDefinition>("P17bRegionB");
                Portal = bed.Create<PortalDefinition>("P17bPortal", p => p.Connect(RegionA, RegionB));
                WorldDefinition world = World();
                Content = bed.Create<GameplayContentSet>("P17bContent", c => c.Configure(world, new ScriptableObject[] { Fact, Condition, Actions, Graph, Item, Vendor, Quest, Dependent }));
            }

            public VariantDefinition Variant { get; }

            public EntityDefinition Entity { get; }

            public NpcDefinition Npc { get; }

            public FactDefinition Fact { get; }

            public ConditionSetDefinition Condition { get; }

            public ActionSetDefinition Actions { get; }

            public DialogueGraphDefinition Graph { get; }

            public ItemDefinition Item { get; }

            public VendorDefinition Vendor { get; }

            public QuestDefinition Quest { get; }

            public QuestDefinition Dependent { get; }

            public InteractableDefinition Door { get; }

            public RegionDefinition RegionA { get; }

            public RegionDefinition RegionB { get; }

            public PortalDefinition Portal { get; }

            public GameplayContentSet Content { get; }

            private static void Stage(QuestDefinition quest, FactDefinition fact)
            {
                quest.AddStage(new QuestStageEntry { title = "Only stage" });
                quest.AddObjective(new ObjectiveDefinition { stage = 0, kind = ObjectiveKind.Fact, target = fact, required = 1, text = "Raise the flag" });
                quest.AddObjective(new ObjectiveDefinition { stage = 0, kind = ObjectiveKind.Fact, target = fact, required = 1, text = "Or raise it again" });
            }

            private static WorldDefinition World()
            {
                string[] guids = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition), new[] { HardeningTestBed.HollowmereFolder });
                Assert.That(guids.Length, Is.EqualTo(1), "one Hollowmere world");
                return AssetDatabase.LoadAssetAtPath<WorldDefinition>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
        }
    }
}
