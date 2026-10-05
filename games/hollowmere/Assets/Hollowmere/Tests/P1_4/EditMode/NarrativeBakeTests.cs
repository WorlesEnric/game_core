// Hollowmere P1.4 EditMode - the Drowned Bell content: authoring, bake, verify, preview and simulate.
//
//   AuthorsAndBakes_IncludesTheNarrativeDefinitions   the bake writes the content manifest (every definition, every
//                                                     fact) and the catalog registrations of the four plugins; a second
//                                                     bake changes nothing and Entry.Verify passes
//   MarenPreview_DependsOnBellRung                    dialogue.preview differs for bell_rung 0 and 1
//   DrownedBell_CompletesAlongBothBranches            quest.simulate completes on each of P3.1's three endings (A let
//                                                     it sleep, B ring, C free the echo), maren_grateful only on B and C
//
// P3.1 replaced the P1.4 story with the reference game's (25 facts, six conversations, the Rumour -> Lantern -> Crossing
// -> Belfry quest with three endings and a failure condition, two vendors, six world items, 31 rules); the expected
// counts and lines below are P3.1's. The test names are kept for history.
//   RuntimeModels_ConvertWithoutProblems              the baked content converts with every package converter
#nullable enable
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Dialogue.Editor;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Logic.Editor;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.Quest.Editor;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Dialogue;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.Quest;
using Hollowmere.Narrative;
using Hollowmere.NarrativeEditor;
using NUnit.Framework;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P1_4.EditMode.Tests
{
    public sealed class NarrativeBakeTests
    {
        private const string BelfryId = HollowmereNarrativeAuthoring.BelfryId;

        [Test, Order(0)]
        public void AuthorsAndBakes_IncludesTheNarrativeDefinitions()
        {
            WorldDefinition world = RequireWorld();
            var clock = Stopwatch.StartNew();
            BakeResult first = HollowmereNarrativeAuthoring.AuthorAndBake();
            long authorMs = clock.ElapsedMilliseconds;
            Assert.That(first.Succeeded, Is.True, first.ToString());

            GameplayContentManifest? content = AssetDatabase.LoadAssetAtPath<GameplayContentManifest>(HollowmereNarrativeAuthoring.ContentManifestPath);
            Assert.That(content, Is.Not.Null, "the bake writes the content manifest beside the content set");
            Assert.That(content!.FormatId, Is.EqualTo(GameplayContentManifest.Format));
            Assert.That(content.WorldId, Is.EqualTo(world.AuthoringId));
            Assert.That(content.ContentHash.Length, Is.EqualTo(64));
            Assert.That(content.Facts.Count, Is.EqualTo(25));
            Assert.That(content.OfKind(NarrativeKinds.Graph).Count, Is.EqualTo(6));
            Assert.That(content.OfKind(NarrativeKinds.Quest).Count, Is.EqualTo(1));
            Assert.That(content.OfKind(NarrativeKinds.Item).Count, Is.EqualTo(6));
            Assert.That(content.OfKind(NarrativeKinds.Vendor).Count, Is.EqualTo(2));
            Assert.That(content.OfKind(NarrativeKinds.WorldItem).Count, Is.EqualTo(6));
            Assert.That(content.OfKind(NarrativeKinds.Rule).Count, Is.EqualTo(31));
            for (int i = 0; i < content.Entries.Count; i++)
            {
                ContentEntry entry = content.Entries[i];
                Assert.That(entry.asset, Is.Not.Null, entry.name);
                Assert.That(((INarrativeDefinition)entry.asset!).ContentStamp, Is.EqualTo(entry.contentStamp), entry.name + " carries its bake stamp");
                Assert.That(entry.contentStamp, Is.EqualTo(DefinitionCanonicalizer.ContentStamp(entry.asset!)), entry.name);
            }

            BakePaths paths = BakePaths.ConventionFor(HollowmereNarrativeAuthoring.WorldPath);
            string description = File.ReadAllText(paths.DescriptionPath);
            foreach (string plugin in new[] { "logic.plugin", "inventory.plugin", "quest.plugin", "dialogue.plugin" })
            {
                Assert.That(description, Does.Contain(plugin), "the catalog description registers " + plugin);
            }

            Assert.That(description, Does.Contain("dialogue.serializer.domain-state"), "the dialogue state domain is registered");

            clock.Restart();
            BakeResult second = Entry.Bake(world, paths, false);
            long rebakeMs = clock.ElapsedMilliseconds;
            Assert.That(second.Succeeded, Is.True, second.ToString());
            Assert.That(second.ChangedFiles, Is.Empty, "unchanged content bakes to the same bytes: " + string.Join(", ", second.ChangedFiles));

            clock.Restart();
            BakeResult verify = Entry.Verify(world, paths);
            Assert.That(verify.Succeeded, Is.True, verify.ToString());
            Debug.Log("[P1.4] author+bake " + authorMs + " ms; rebake " + rebakeMs + " ms; verify " + clock.ElapsedMilliseconds
                + " ms; entries " + content.Entries.Count + "; facts " + content.Facts.Count + "; hash " + content.ContentHash);
        }

        [Test, Order(1)]
        public void MarenPreview_DependsOnBellRung()
        {
            DialogueGraphDefinition maren = Load<DialogueGraphDefinition>(HollowmereNarrativeAuthoring.GraphsDir + "/Maren.asset");
            string silent = DialogueTools.Preview(maren, "bell_rung=0");
            string rung = DialogueTools.Preview(maren, "bell_rung=1");
            Debug.Log("[P1.4] Maren preview bell_rung=0:\n" + silent + "\n[P1.4] Maren preview bell_rung=1:\n" + rung);
            Assert.That(silent, Is.Not.EqualTo(rung));
            Assert.That(silent, Does.Contain("has been silent"));
            Assert.That(silent, Does.Not.Contain("The bell rang"));
            Assert.That(rung, Does.Contain("The bell rang"));
            Assert.That(DialogueTools.Preview(maren, string.Empty), Is.EqualTo(silent), "facts start at their initial values");
        }

        [Test, Order(2)]
        public void DrownedBell_CompletesAlongBothBranches()
        {
            QuestDefinition quest = Load<QuestDefinition>(HollowmereNarrativeAuthoring.QuestsDir + "/DrownedBell.asset");
            string path = "fact:heard_rumour=1; collect:Lantern=1; fact:gate_open=1; reach:" + BelfryId;
            var endings = new[] { ("ending_a", 1, 0), ("ending_b", 2, 1), ("ending_c", 3, 1) };
            foreach ((string fact, int branch, int rewards) in endings)
            {
                SimulationResult result = QuestTools.SimulateResult(quest, path + "; fact:" + fact + "=1");
                Debug.Log("[P1.4/P3.1] simulate " + fact + ":\n" + result.Text);
                Assert.That(result.Completed, Is.True, result.Text);
                Assert.That(result.State.Branch, Is.EqualTo(branch), fact + " is branch " + branch);
                Assert.That(result.Rewards.Count, Is.EqualTo(rewards), fact + ": maren_grateful only when the bell rang");
            }

            SimulationResult stuck = QuestTools.SimulateResult(quest, "fact:heard_rumour=1; collect:Lantern=1; fact:gate_open=1");
            Assert.That(stuck.Completed, Is.False, "the gate alone does not reach the belfry");
            Assert.That(stuck.State.Stage, Is.EqualTo(2));
        }

        [Test, Order(3)]
        public void RuntimeModels_ConvertWithoutProblems()
        {
            GameplayContentManifest content = Load<GameplayContentManifest>(HollowmereNarrativeAuthoring.ContentManifestPath);
            var modules = new HollowmereNarrativeModules();
            var converters = new List<INarrativeContentConverter>();
            for (int i = 0; i < modules.All.Count; i++)
            {
                converters.Add(modules.All[i].Converter!);
            }

            NarrativeModelSet models = NarrativeContent.Build(content, converters);
            Assert.That(models.Problems, Is.Empty, string.Join("\n", models.Problems));
            Assert.That(models.FactCount, Is.EqualTo(25));
            Assert.That(models.GraphCount, Is.EqualTo(6));
            Assert.That(models.QuestCount, Is.EqualTo(1));
            Assert.That(models.RuleCount, Is.EqualTo(31));
            foreach (string npcGraphRef in new[] { HollowmereNarrative.MarenGraphRef, HollowmereNarrative.OddGraphRef, HollowmereNarrative.PipGraphRef,
                HollowmereNarrative.HaleGraphRef, HollowmereNarrative.EchoGraphRef })
            {
                Assert.That(models.TryResolve(npcGraphRef, out int graphKey) && models.TryGetGraph(graphKey, out DialogueGraphModel? _), Is.True,
                    "P1.3's NpcDefinition.dialogueGraph " + npcGraphRef + " resolves to a baked graph");
            }
            Assert.That(models.WorldItemCount, Is.EqualTo(6));
            Assert.That(models.PlayerInventory, Is.Not.Null);
            Assert.That(models.TryResolve(HollowmereNarrative.BellCondition, out int _), Is.True);
            Assert.That(models.TryResolve(HollowmereNarrative.ClapperWorldItem, out int _), Is.True);
            Assert.That(models.TryGetFactByName("pip_asked", out FactModel? pip) && pip != null && !pip.Persistent, Is.True);
        }

        private static WorldDefinition RequireWorld()
        {
            WorldDefinition? world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(HollowmereNarrativeAuthoring.WorldPath);
            if (world == null)
            {
                Assert.Ignore("the Hollowmere world is not authored (run the P1.1 authoring first)");
            }

            return world!;
        }

        private static T Load<T>(string path)
            where T : UnityEngine.Object
        {
            T? asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Assert.Ignore(path + " is missing (AuthorsAndBakes runs first)");
            }

            return asset!;
        }
    }
}
