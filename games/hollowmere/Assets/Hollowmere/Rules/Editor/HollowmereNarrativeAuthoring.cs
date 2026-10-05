// Hollowmere - authors "The Drowned Bell" (P1.4): facts, items, the player inventory, Odd's stall, two world items,
// condition and action sets, five conversations, the quest and four rules, all listed on one GameplayContentSet of the
// Hollowmere world, then re-bakes the world so the content manifest and the catalog registrations are written.
//
//   Assets/Hollowmere/Dialogue/Facts/*.asset      heard_rumour, maren_trusts_player, odd_paid, odd_persuaded, gate_open,
//                                                 bell_rung, belfry_echo_freed, maren_grateful, pip_asked (not persistent)
//   Assets/Hollowmere/Dialogue/Graphs/*.asset     Maren, Odd, Pip, Hale, Belfry Echo
//   Assets/Hollowmere/Items/*.asset               Lantern, Old Coin, Bell Clapper, Gate Key, the player inventory, Odd's
//                                                 stall (the gate key for three old coins), Old Coins x3 in the village,
//                                                 the Bell Clapper in the marsh, the marsh loot table
//   Assets/Hollowmere/Quests/DrownedBell.asset    four stages; stage 1 is passed by paying Odd or persuading him
//   Assets/Hollowmere/Rules/*.asset               condition sets, action sets, rules, the content set
//   Assets/Hollowmere/Rules/HollowmereContent.content.asset   bake output (content manifest)
//
// Every asset carries a fixed authoring id, so re-authoring after a delete reproduces the same keys. The script is
// idempotent: when the content set exists nothing is re-authored and only the bake runs (byte-identical when nothing
// changed). Graphs, the quest and the stock are authored through the P1.4 tools (dialogue.addLine/addChoice/
// linkCondition, quest.addStage/addObjective/linkReward, inventory.grantStarting/placeItem/setStock, logic.addRule).
//
// Hollowmere has no gate or NPC entities yet (P1.3 adds NPCs): the NPCs speak by name, and the marsh gate is the fixed
// subject id GateId that the gate interaction (P1.3's interactable, or the PlayMode test) passes to the evaluator.
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Compile;
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
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using Hollowmere.Narrative;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.NarrativeAuthoring
{
    /// <summary>Creates (when missing) the Drowned Bell content and re-bakes the Hollowmere world.</summary>
    public static class HollowmereNarrativeAuthoring
    {
        public const string WorldPath = "Assets/Hollowmere/World/Hollowmere.asset";
        public const string ContentSetPath = HollowmereNarrative.ContentSetPath;
        public const string ContentManifestPath = HollowmereNarrative.ContentManifestPath;
        public const string FactsDir = "Assets/Hollowmere/Dialogue/Facts";
        public const string GraphsDir = "Assets/Hollowmere/Dialogue/Graphs";
        public const string ItemsDir = "Assets/Hollowmere/Items";
        public const string QuestsDir = "Assets/Hollowmere/Quests";
        public const string RulesDir = "Assets/Hollowmere/Rules";

        public const string VillageId = "11e8dd95-6622-43d0-8b48-5e17b72f0bb8";
        public const string MarshId = "7f21b99a-8e74-412f-a1da-5f7d60843080";
        public const string BelfryId = "1c5a1ae9-bec2-4201-be5f-3ff8bf8e1d18";

        [MenuItem("GameCore/Hollowmere/Author and Bake Narrative (P1.4)")]
        public static void AuthorAndBakeMenu()
        {
            BakeResult result = AuthorAndBake();
            if (result.Succeeded)
            {
                Debug.Log("[Hollowmere] " + result);
            }
            else
            {
                Debug.LogError("[Hollowmere] " + result);
            }
        }

        /// <summary>Authors whatever is missing, then bakes the world (without importing the generated C#).</summary>
        public static BakeResult AuthorAndBake()
        {
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath)
                ?? throw new System.InvalidOperationException("the Hollowmere world is missing; run the P1.1 authoring first");
            EnsureContent(world);
            return Entry.Bake(world, BakePaths.ConventionFor(WorldPath), false);
        }

        /// <summary>The content set, authoring every asset when it does not exist yet.</summary>
        public static GameplayContentSet EnsureContent(WorldDefinition world)
        {
            GameplayContentSet? existing = AssetDatabase.LoadAssetAtPath<GameplayContentSet>(ContentSetPath);
            if (existing != null)
            {
                return existing;
            }

            NarrativeAuthoring.EnsureFolder(FactsDir);
            NarrativeAuthoring.EnsureFolder(GraphsDir);
            NarrativeAuthoring.EnsureFolder(ItemsDir);
            NarrativeAuthoring.EnsureFolder(QuestsDir);
            NarrativeAuthoring.EnsureFolder(RulesDir);
            GameplayContentSet set = ScriptableObject.CreateInstance<GameplayContentSet>();
            set.Configure(world, new List<ScriptableObject>());
            AssetDatabase.CreateAsset(set, ContentSetPath);

            var c = new Content(set, world);
            c.Author();
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            return set;
        }

        private sealed class Content
        {
            private readonly GameplayContentSet set;
            private readonly WorldDefinition world;

            public Content(GameplayContentSet contentSet, WorldDefinition worldDefinition)
            {
                set = contentSet;
                world = worldDefinition;
            }

            private FactDefinition heardRumour = null!;
            private FactDefinition marenTrusts = null!;
            private FactDefinition oddPaid = null!;
            private FactDefinition oddPersuaded = null!;
            private FactDefinition gateOpen = null!;
            private FactDefinition bellRung = null!;
            private FactDefinition echoFreed = null!;
            private FactDefinition marenGrateful = null!;
            private FactDefinition pipAsked = null!;
            private ItemDefinition lantern = null!;
            private ItemDefinition oldCoin = null!;
            private ItemDefinition clapper = null!;
            private ItemDefinition gateKey = null!;
            private VendorDefinition odd = null!;
            private QuestDefinition quest = null!;

            public void Author()
            {
                Facts();
                Items();
                ConditionSetDefinition hasGateKey = Conditions("HasGateKey", "3335bea3-1074-4a9e-bb5e-fbd62deea390",
                    ConditionEntry.Of(ConditionKind.ItemCount, gateKey, CompareOp.GreaterOrEqual, 1));
                ConditionSetDefinition hasClapper = Conditions("HasBellClapper", "ee1253a8-ef88-4fb0-a173-6af02114b21c",
                    ConditionEntry.Of(ConditionKind.ItemCount, clapper, CompareOp.GreaterOrEqual, 1));
                ConditionSetDefinition marenTrustsSet = Conditions("MarenTrustsPlayer", "1c70446b-7b7c-4fc7-8b5d-6dafb0de98c2",
                    ConditionEntry.Of(ConditionKind.Fact, marenTrusts, CompareOp.GreaterOrEqual, 1));
                ConditionSetDefinition bellRungSet = Conditions("BellRung", "131e9a71-1319-41a1-88c9-0b923510ae1b",
                    ConditionEntry.Of(ConditionKind.Fact, bellRung, CompareOp.GreaterOrEqual, 1));
                ConditionSetDefinition threeCoins = Conditions("HasThreeOldCoins", "39b2f636-d880-4a2f-bda0-2a6af23ec1ef",
                    ConditionEntry.Of(ConditionKind.ItemCount, oldCoin, CompareOp.GreaterOrEqual, 3));
                ConditionSetDefinition gateOpenSet = Conditions("GateOpen", "45e8c82c-389b-49fb-aa35-e526fffc6c4a",
                    ConditionEntry.Of(ConditionKind.Fact, gateOpen, CompareOp.GreaterOrEqual, 1));

                quest = Create<QuestDefinition>(QuestsDir + "/DrownedBell.asset", "DrownedBell", "0db698bb-d2d3-45b7-94cb-1bd81e90e6c2");
                ActionSetDefinition openGate = Actions("OpenGate", "42f181e3-ef95-4ce3-82e7-2c2d0b79d494",
                    ActionEntry.Of(ActionKind.SetFact, gateOpen, 1),
                    ActionEntry.Text(ActionKind.PlayAudio, "sfx.gate.creak"));
                Actions("RingBell", "0207a68e-bc01-457e-a2d4-69736a0658f0",
                    ActionEntry.Of(ActionKind.SetFact, bellRung, 1),
                    ActionEntry.Text(ActionKind.PlayAudio, "sfx.bell.toll"));
                ActionSetDefinition marenIntro = Actions("MarenIntro", "266d333d-2e6d-47e6-8e71-8004cf7f4aca",
                    ActionEntry.Of(ActionKind.SetFact, heardRumour, 1),
                    ActionEntry.Of(ActionKind.StartQuest, quest, 0));
                ActionSetDefinition marenTrust = Actions("MarenTrust", "c0b46d92-87ea-4b10-b523-3e28d5b338f2",
                    ActionEntry.Of(ActionKind.SetFact, marenTrusts, 1));
                ActionSetDefinition oddPersuade = Actions("OddPersuaded", "2beb7fe9-752d-4e57-a701-e3bf4a64de70",
                    ActionEntry.Of(ActionKind.SetFact, oddPersuaded, 1),
                    ActionEntry.Of(ActionKind.Grant, gateKey, 1));
                ActionSetDefinition echoFree = Actions("EchoFreed", "f8f82ca0-fc26-440b-87a3-75b5f13b721d",
                    ActionEntry.Of(ActionKind.SetFact, echoFreed, 1));
                ActionSetDefinition pipAsk = Actions("PipAsked", "ca415c6a-b951-4501-b70f-594d657bb5a9",
                    ActionEntry.Of(ActionKind.SetFact, pipAsked, 1));
                _ = openGate;
                _ = hasGateKey;
                _ = hasClapper;

                DialogueGraphDefinition maren = Maren(bellRungSet, marenIntro, marenTrust);
                Odd(threeCoins, marenTrustsSet, oddPersuade);
                Pip(pipAsk);
                Hale(gateOpenSet);
                Echo(bellRungSet, echoFree);
                Quest(maren);
                Rules();
            }

            private void Facts()
            {
                heardRumour = Fact("heard_rumour", "8508a66a-cbc1-4729-9be5-fb7befe04391", true);
                marenTrusts = Fact("maren_trusts_player", "deae77f6-2b47-4c20-a4ab-11e1b8496514", true);
                oddPaid = Fact("odd_paid", "0f06b03c-98f7-4b7a-b4c7-70950667b31a", true);
                oddPersuaded = Fact("odd_persuaded", "bd21363a-d9b9-479c-ba80-dde87c541ae8", true);
                gateOpen = Fact("gate_open", "d92de012-492f-42b1-8527-302c94471b73", true);
                bellRung = Fact("bell_rung", "e7b37913-f43a-4a88-ba31-59e248433998", true);
                echoFreed = Fact("belfry_echo_freed", "a1381dc8-8045-4ba2-a1dc-8a2f5f5d8509", true);
                marenGrateful = Fact("maren_grateful", "9ad3fb8b-f42d-45fa-9fef-5d5ffb061752", true);
                pipAsked = Fact("pip_asked", "09ad393c-e99a-4165-8745-aab9f23ac790", false);
            }

            private void Items()
            {
                lantern = Item("Lantern", "6bb88144-376c-4507-8429-e78247b14e66", "Lantern", 1, 900, 12, "tool");
                oldCoin = Item("OldCoin", "19942a74-dafe-4daf-a9d4-26bea7aad898", "Old Coin", 20, 10, 1, "currency");
                clapper = Item("BellClapper", "e51c9d68-54de-4f7d-96e4-23b8807194ee", "Bell Clapper", 1, 2500, 0, "quest");
                gateKey = Item("GateKey", "e1d08e53-755f-4d05-a23d-c84d98a29897", "Gate Key", 1, 50, 3, "key");

                InventoryDefinition player = Create<InventoryDefinition>(ItemsDir + "/PlayerInventory.asset", "PlayerInventory", "392e67f7-738f-403d-81f1-52fb78205528");
                player.Configure(12, 20000, 0, true, string.Empty);
                InventoryTools.GrantStarting(player, lantern, 0);

                odd = Create<VendorDefinition>(ItemsDir + "/OddsStall.asset", "OddsStall", "15ce0205-6068-4b27-9c55-3b2084b0ec81");
                odd.Configure("Odd's Stall", oldCoin, 4, string.Empty);
                InventoryTools.SetStock(odd, gateKey, 1, 3, 0, false);

                RegionDefinition? village = Region(VillageId);
                RegionDefinition? marsh = Region(MarshId);
                WorldItemDefinition coins = InventoryTools.PlaceItem(set, oldCoin, village!, new Vector3(4f, 0f, -3f), 3, string.Empty, "OldCoins_Village");
                Move(coins, ItemsDir + "/OldCoins_Village.asset", "16b73b9b-6003-42ad-982c-d85426d1dc64");
                WorldItemDefinition clapperItem = InventoryTools.PlaceItem(set, clapper, marsh!, new Vector3(-6f, 0f, 9f), 1, string.Empty, "BellClapper_Marsh");
                Move(clapperItem, ItemsDir + "/BellClapper_Marsh.asset", "43793255-287c-42c9-998b-fd8c45b448a2");
                string placedDir = RulesDir + "/WorldItems";
                if (AssetDatabase.IsValidFolder(placedDir) && AssetDatabase.FindAssets(string.Empty, new[] { placedDir }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(placedDir);
                }

                LootTableDefinition loot = Create<LootTableDefinition>(ItemsDir + "/MarshLoot.asset", "MarshLoot", "5b597ed8-0d3c-4e49-9b96-578a988de75b");
                loot.Configure(1, new[]
                {
                    new LootEntryDefinition { item = oldCoin, weight = 3, min = 1, max = 2 },
                    new LootEntryDefinition { item = lantern, weight = 1, min = 1, max = 1 },
                });
            }

            private DialogueGraphDefinition Maren(ConditionSetDefinition bellRungSet, ActionSetDefinition intro, ActionSetDefinition trust)
            {
                DialogueGraphDefinition g = Graph("Maren", "8e52f233-3797-4396-89c4-24f00fceef8a", "Maren");
                int branch = Node(g, DialogueNodeKind.Branch, string.Empty);
                DialogueTools.LinkCondition(g, branch, bellRungSet);
                int greet = DialogueTools.AddLine(g, "Traveller! The Drowned Bell has been silent since the flood took the old belfry.");
                g.Link(branch, DialoguePort.Else, 0, greet);
                int introNode = ActionNode(g, intro, greet);
                int choice = DialogueTools.AddChoice(g, new List<string> { "I'll find the clapper and ring it.", "Why should I help?" }, null, introNode,
                    "Will you help us?");
                int trustNode = ActionNode(g, trust, -1);
                g.Link(choice, DialoguePort.Option, 0, trustNode);
                int thanks = DialogueTools.AddLine(g, "Bless you. Odd keeps the marsh gate key, and he will want coin for it.", "", trustNode);
                _ = thanks;
                int why = DialogueTools.AddLine(g, "Because the marsh will not let anyone rest until that bell rings again.");
                g.Link(choice, DialoguePort.Option, 1, why);
                int rung = DialogueTools.AddLine(g, "You rang it! Hollowmere can sleep again. Take my lantern, you have earned it.");
                g.Link(branch, DialoguePort.Next, 0, rung);
                Save(g);
                return g;
            }

            private void Odd(ConditionSetDefinition threeCoins, ConditionSetDefinition marenTrustsSet, ActionSetDefinition persuade)
            {
                DialogueGraphDefinition g = Graph("Odd", "aa779457-076d-496c-84b5-72019a826681", "Odd");
                int offer = DialogueTools.AddLine(g, "The gate key? Three old coins, and no haggling.");
                int choice = DialogueTools.AddChoice(g, new List<string> { "Here are three coins.", "Maren sent me. She trusts me.", "Not now." }, null, offer);
                DialogueTools.LinkCondition(g, choice, threeCoins, 0);
                DialogueTools.LinkCondition(g, choice, marenTrustsSet, 1);
                g.Node(choice).options[1].hideWhenUnavailable = true;
                int paid = DialogueTools.AddLine(g, "Pleasure doing business. Mind the reeds.");
                g.Link(choice, DialoguePort.Option, 0, paid);
                int persuadeNode = ActionNode(g, persuade, -1);
                g.Link(choice, DialoguePort.Option, 1, persuadeNode);
                DialogueTools.AddLine(g, "For Maren, then. Take it, and do not drown.", "", persuadeNode);
                Save(g);
            }

            private void Pip(ActionSetDefinition ask)
            {
                DialogueGraphDefinition g = Graph("Pip", "dc88c507-3b24-44ea-8eb8-fcb35dcf733e", "Pip");
                int hello = DialogueTools.AddLine(g, "Did you ever hear the bell? I never have.");
                int choice = DialogueTools.AddChoice(g, new List<string> { "Where is the clapper?", "Bye, Pip." }, null, hello);
                int askNode = ActionNode(g, ask, -1);
                g.Link(choice, DialoguePort.Option, 0, askNode);
                DialogueTools.AddLine(g, "Gran says it fell in the marsh, by the dead willow.", "", askNode);
                Save(g);
            }

            private void Hale(ConditionSetDefinition gateOpenSet)
            {
                DialogueGraphDefinition g = Graph("Hale", "a82ac490-3bac-45c7-9e27-65a59d7cc6cf", "Hale");
                int branch = Node(g, DialogueNodeKind.Branch, string.Empty);
                DialogueTools.LinkCondition(g, branch, gateOpenSet);
                int shut = DialogueTools.AddLine(g, "The marsh gate stays shut. Odd has the key.");
                int open = DialogueTools.AddLine(g, "The gate's open. Watch your step out there.");
                g.Link(branch, DialoguePort.Else, 0, shut);
                g.Link(branch, DialoguePort.Next, 0, open);
                Save(g);
            }

            private void Echo(ConditionSetDefinition bellRungSet, ActionSetDefinition free)
            {
                DialogueGraphDefinition g = Graph("BelfryEcho", "b9b85886-492d-40a3-9edb-02c9d9a417b0", "Belfry Echo");
                int branch = Node(g, DialogueNodeKind.Branch, string.Empty);
                DialogueTools.LinkCondition(g, branch, bellRungSet);
                int silent = DialogueTools.AddLine(g, "...silent... the bell is silent...");
                g.Link(branch, DialoguePort.Else, 0, silent);
                int freeNode = ActionNode(g, free, -1);
                g.Link(branch, DialoguePort.Next, 0, freeNode);
                DialogueTools.AddLine(g, "The toll... I hear it... I am free.", "", freeNode);
                Save(g);
            }

            private void Quest(DialogueGraphDefinition maren)
            {
                quest.Configure("The Drowned Bell", null, new[] { "pay Odd", "persuade Odd" });
                RegionDefinition? belfry = Region(BelfryId);
                int s0 = QuestTools.AddStage(quest, "A silent bell", "Maren asked you to find the bell's clapper.");
                int s1 = QuestTools.AddStage(quest, "The marsh gate", "Get past the marsh gate: pay Odd for the key, or persuade him.");
                int s2 = QuestTools.AddStage(quest, "Ring the bell", "Find the clapper in Blackmere Marsh and ring the Drowned Bell.");
                int s3 = QuestTools.AddStage(quest, "Return to Maren", "Tell Maren the bell has rung.");
                QuestTools.AddObjective(quest, s0, ObjectiveKind.Fact, heardRumour, string.Empty, 1, 0, "Hear Maren out");
                QuestTools.AddObjective(quest, s1, ObjectiveKind.Fact, gateOpen, string.Empty, 1, 0, "Open the marsh gate");
                QuestTools.AddObjective(quest, s1, ObjectiveKind.Fact, oddPaid, string.Empty, 1, 1, "Pay Odd three old coins");
                QuestTools.AddObjective(quest, s1, ObjectiveKind.Fact, oddPersuaded, string.Empty, 1, 2, "Persuade Odd");
                QuestTools.AddObjective(quest, s2, ObjectiveKind.Collect, clapper, string.Empty, 1, 0, "Find the bell clapper");
                QuestTools.AddObjective(quest, s2, ObjectiveKind.Reach, belfry, string.Empty, 1, 0, "Reach the Drowned Belfry");
                QuestTools.AddObjective(quest, s2, ObjectiveKind.Fact, bellRung, string.Empty, 1, 0, "Ring the bell");
                QuestTools.AddObjective(quest, s3, ObjectiveKind.Talk, maren, string.Empty, 1, 0, "Talk to Maren");
                QuestTools.LinkReward(quest, RewardKind.Item, lantern, 1, 0);
                QuestTools.LinkReward(quest, RewardKind.Fact, marenGrateful, 1, 0);
                QuestTools.LinkReward(quest, RewardKind.Item, oldCoin, 3, 1);
                Save(quest);
            }

            private void Rules()
            {
                RuleDefinition gateStays = Create<RuleDefinition>(RulesDir + "/BellKeepsGateOpen.asset", "BellKeepsGateOpen", "12fd6b07-8dab-4d77-a1dc-ce30262983a5");
                gateStays.ConfigureTrigger(TriggerKind.FactSet, bellRung, string.Empty, false, 1);
                gateStays.SetActions(null, new[] { ActionEntry.Of(ActionKind.SetFact, gateOpen, 1) });
                gateStays.ConfigureLimits(true, 0, 0, 0);
                Save(gateStays);

                RuleDefinition ambience = Create<RuleDefinition>(RulesDir + "/EchoFreedAmbience.asset", "EchoFreedAmbience", "de03f0af-388d-4797-9d6e-3af2faa2a466");
                ambience.ConfigureTrigger(TriggerKind.FactSet, echoFreed, string.Empty, false, 1);
                ambience.SetActions(null, new[]
                {
                    ActionEntry.Text(ActionKind.PlayAudio, "ambience.belfry.freed"),
                    ActionEntry.Text(ActionKind.ShowMessage, "The belfry falls quiet. Something has left it."),
                });
                ambience.ConfigureLimits(true, 0, 0, 0);
                Save(ambience);

                RuleDefinition paid = Create<RuleDefinition>(RulesDir + "/OddPaidOnTrade.asset", "OddPaidOnTrade", "ebee54ff-745b-4c42-8f41-bb5bd5efadee");
                paid.ConfigureTrigger(TriggerKind.TradeDone, odd, string.Empty, false, AuthoringIds.StableKey(gateKey.AuthoringId));
                paid.SetActions(null, new[] { ActionEntry.Of(ActionKind.SetFact, oddPaid, 1) });
                paid.ConfigureLimits(true, 0, 0, 0);
                Save(paid);

                RuleDefinition back = Create<RuleDefinition>(RulesDir + "/ReturnToMaren.asset", "ReturnToMaren", "78bdc449-3fc2-4935-8847-8c732163dd22");
                back.ConfigureTrigger(TriggerKind.QuestCompleted, quest, string.Empty, true, 0);
                back.SetActions(null, new[]
                {
                    ActionEntry.Text(ActionKind.ShowMessage, "Maren presses her old lantern into your hands."),
                    ActionEntry.Text(ActionKind.PlayAudio, "sting.quest.complete"),
                });
                back.ConfigureLimits(true, 0, 0, 0);
                Save(back);
            }

            // ---- helpers ----

            private FactDefinition Fact(string name, string id, bool persistent)
            {
                FactDefinition fact = Create<FactDefinition>(FactsDir + "/" + name + ".asset", name, id);
                fact.Configure(name, 0, persistent);
                Save(fact);
                return fact;
            }

            private ItemDefinition Item(string asset, string id, string shown, int stack, int grams, int price, string tag)
            {
                ItemDefinition item = Create<ItemDefinition>(ItemsDir + "/" + asset + ".asset", asset, id);
                item.Configure(shown, stack, grams, price, new[] { tag }, null);
                Save(item);
                return item;
            }

            private ConditionSetDefinition Conditions(string name, string id, params ConditionEntry[] entries)
            {
                ConditionSetDefinition conditions = Create<ConditionSetDefinition>(RulesDir + "/Conditions/" + name + ".asset", name, id);
                conditions.Configure(ConditionMode.All, entries);
                Save(conditions);
                return conditions;
            }

            private ActionSetDefinition Actions(string name, string id, params ActionEntry[] entries)
            {
                ActionSetDefinition actions = Create<ActionSetDefinition>(RulesDir + "/Actions/" + name + ".asset", name, id);
                actions.Configure(entries);
                Save(actions);
                return actions;
            }

            private DialogueGraphDefinition Graph(string asset, string id, string speaker)
            {
                DialogueGraphDefinition graph = Create<DialogueGraphDefinition>(GraphsDir + "/" + asset + ".asset", asset, id);
                graph.Configure(speaker, string.Empty, 0);
                return graph;
            }

            private static int Node(DialogueGraphDefinition graph, DialogueNodeKind kind, string text) =>
                graph.AddNode(new DialogueNodeEntry { kind = kind, text = text });

            private static int ActionNode(DialogueGraphDefinition graph, ActionSetDefinition actions, int after)
            {
                int index = graph.AddNode(new DialogueNodeEntry { kind = DialogueNodeKind.Action, actions = actions });
                if (after >= 0)
                {
                    graph.Link(after, DialoguePort.Next, 0, index);
                }

                return index;
            }

            private T Create<T>(string path, string name, string id)
                where T : NarrativeDefinitionAsset
            {
                NarrativeAuthoring.EnsureFolder(System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/'));
                T asset = NarrativeAuthoring.CreateAsset<T>(set, name, string.Empty, path, "hollowmere.narrative");
                asset.SetAuthoringId(id);
                return asset;
            }

            private void Move(WorldItemDefinition placed, string path, string id)
            {
                string from = AssetDatabase.GetAssetPath(placed);
                if (from != path)
                {
                    string problem = AssetDatabase.MoveAsset(from, path);
                    if (problem.Length > 0)
                    {
                        throw new System.InvalidOperationException(problem);
                    }
                }

                placed.SetAuthoringId(id);
                Save(placed);
            }

            private RegionDefinition? Region(string authoringId)
            {
                for (int i = 0; i < world.Regions.Count; i++)
                {
                    if (world.Regions[i] != null && world.Regions[i].AuthoringId == authoringId)
                    {
                        return world.Regions[i];
                    }
                }

                throw new System.InvalidOperationException("Hollowmere has no region " + authoringId);
            }

            private static void Save(ScriptableObject asset) => EditorUtility.SetDirty(asset);
        }
    }
}
