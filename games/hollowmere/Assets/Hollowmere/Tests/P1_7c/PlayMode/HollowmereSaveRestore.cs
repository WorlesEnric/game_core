// P1.7c extension of the P1.7a HollowmereSaveRestore fixture: in-flight feedback and terminal retention.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Rules.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Validation.ProbeHost;
using Hollowmere.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P1_7c.PlayMode.Tests
{
    public sealed class HollowmereSaveRestore
    {
        private const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        private const string NpcRosterPath = "Assets/Hollowmere/Npcs/NpcRoster.asset";
        private const string InteractionRosterPath = "Assets/Hollowmere/Interactables/InteractionRoster.asset";
        private const string VillageId = "11e8dd95-6622-43d0-8b48-5e17b72f0bb8";
        private const string MarshId = "7f21b99a-8e74-412f-a1da-5f7d60843080";
        private const string BelfryId = "1c5a1ae9-bec2-4201-be5f-3ff8bf8e1d18";
        private const int MaxFrames = 300;

        private readonly List<ScriptableObject> clones = new List<ScriptableObject>();
        private readonly List<Game> games = new List<Game>();
        private string saveDirectory = string.Empty;
        private RegionManifest? manifest;
        private GameplayContentManifest? content;
        private NpcRoster? npcRoster;
        private InteractionRoster? interactionRoster;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "gamecore-p1_7c-saves-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = games.Count - 1; i >= 0; i--)
            {
                games[i].Close();
            }

            games.Clear();
            foreach (ScriptableObject clone in clones) UnityEngine.Object.DestroyImmediate(clone);
            clones.Clear();
            if (saveDirectory.Length > 0 && Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
        }

        [UnityTest]
        public IEnumerator P17c_06_HollowmereSaveRestore_InFlightCueOnce_AndTerminalSurvivesFreshBoot()
        {
            Assert.That(LoadContent(), Is.True);
            Game a = Boot(manifest!);
            ActionSetModel? cueSet = null;
            int cueIndex = -1;
            foreach (ActionSetModel set in a.World.Runtime.Models.ActionSets)
            {
                for (int i = 0; i < set.Actions.Count; i++)
                    if (set.Actions[i].Kind == ActionKind.PlayAudio) { cueSet = set; cueIndex = i; break; }
                if (cueSet != null) break;
            }
            Assert.That(cueSet, Is.Not.Null, "Hollowmere has a bell sound action");
            a.World.Delivery.OnCommitted(LogicIds.ActionDueEvent,
                NarrativeEvent.Encode(a.World.Runtime.Index.HubTarget, cueSet!.Key, cueIndex, 0, 0, 0, 0), default(OperationId));
            Assert.That(a.Saves.Capture("inflight").Succeeded, Is.True);
            a.Close();
            Game b = Boot(manifest!);
            Assert.That(b.Saves.Restore("inflight").Succeeded, Is.True);
            var sink = new Feedback();
            b.World.Runtime.UseFeedback(sink);
            b.World.Delivery.Pump();
            Assert.That(sink.Count, Is.EqualTo(1));
            // The boundary immediately after dispatch must already contain the terminal disposition.
            Assert.That(b.Saves.Capture("presented").Succeeded, Is.True);
            b.Close();
            Game c = Boot(manifest!);
            Assert.That(c.Saves.Restore("presented").Succeeded, Is.True);
            c.World.Runtime.UseFeedback(sink);
            c.World.Delivery.Pump();
            c.World.Delivery.Pump();
            Assert.That(sink.Count, Is.EqualTo(1), "a fresh port must use retained outbox terminals");
            yield return null;
        }

        [UnityTest]
        public IEnumerator P17c_01_CompletionActionsSurviveFreshBoot() => TerminalRestore(false);

        [UnityTest]
        public IEnumerator P17c_01_FailureActionsSurviveFreshBoot() => TerminalRestore(true);

        private IEnumerator TerminalRestore(bool fail)
        {
            Assert.That(LoadContent(), Is.True);
            content = UnityEngine.Object.Instantiate(content!);
            clones.Add(content);
            QuestDefinition? quest = null;
            ActionSetDefinition audio = ScriptableObject.CreateInstance<ActionSetDefinition>();
            audio.SetAuthoringId(System.Guid.NewGuid().ToString());
            audio.Configure(new[] { new ActionEntry { kind = ActionKind.PlayAudio, text = "p17c.terminal" } });
            clones.Add(audio);
            foreach (ContentEntry entry in content.Entries)
            {
                if (quest == null && entry.asset is QuestDefinition original)
                {
                    quest = UnityEngine.Object.Instantiate(original);
                    clones.Add(quest);
                    entry.asset = quest;
                }
            }
            Assert.That(quest, Is.Not.Null);
            Assert.That(audio, Is.Not.Null);
            quest!.SetPrerequisites(null);
            quest.SetConsequences(audio, audio);
            Game a = Boot(manifest!);
            int key = NarrativeRefs.KeyOf(quest);
            TargetId target = a.World.Runtime.Index.TargetOf(NarrativeTargetKind.Quest, key);
            a.World.Runtime.Submitter.Submit(QuestIds.StartRoute, target, QuestIds.StartCommand, NarrativeCommands.QuestStart(1));
            yield return Until(() => QuestSlot(a, target, QuestIds.Status) == QuestIds.Active, "quest start", _ => { });
            a.World.Runtime.Submitter.Submit(fail ? QuestIds.FailRoute : QuestIds.CompleteRoute, target,
                fail ? QuestIds.FailCommand : QuestIds.CompleteCommand, NarrativeCommands.QuestComplete(2));
            yield return Until(() => QuestSlot(a, target, QuestIds.Status) == (fail ? QuestIds.Failed : QuestIds.Completed), "terminal state", _ => { });
            Assert.That(a.World.Delivery.Owner.Outbox.OpenCount, Is.GreaterThan(0));
            Assert.That(a.Saves.Capture("terminal").Succeeded, Is.True);
            a.Close();
            Game b = Boot(manifest!);
            Assert.That(b.Saves.Restore("terminal").Succeeded, Is.True);
            var sink = new Feedback { CueFilter = "p17c.terminal" };
            b.World.Runtime.UseFeedback(sink);
            yield return Until(() => sink.Count > 0, "restored terminal cue", _ => { });
            for (int i = 0; i < 20; i++) yield return null;
            Assert.That(sink.Count, Is.EqualTo(1));
            Assert.That(b.Saves.Capture("settled").Succeeded, Is.True);
            Assert.That(b.Saves.Restore("settled").Succeeded, Is.True);
            b.World.Runtime.UseFeedback(sink);
            for (int i = 0; i < 20; i++) yield return null;
            Assert.That(sink.Count, Is.EqualTo(1));
        }

        private sealed class Feedback : IFeedbackSink
        {
            public int Count;
            public string? CueFilter;
            public void OnFeedback(FeedbackCue cue) { if (CueFilter == null || cue.Cue == CueFilter) Count++; }
        }

        private sealed class Game
        {
            public Game(NarrativeWorld world, HollowmereNarrativeModules modules, InteractionWorldExtension interactions, GameplayContentManifest content)
            {
                World = world;
                Modules = modules;
                Interactions = interactions;
                Content = content;
            }

            public NarrativeWorld World { get; set; }

            public HollowmereNarrativeModules Modules { get; }

            public InteractionWorldExtension Interactions { get; }

            public GameplayContentManifest Content { get; }

            public SaveService Saves { get; set; } = null!;

            public int Reattachments { get; set; }

            private bool closed;

            /// <summary>The restore re-attach: base world, narrative layer on the restored delivery owner, P1.3 wiring.</summary>
            public void Reattach(GameApplicationRoot restored)
            {
                NarrativeWorld previous = World;
                previous.World.Shutdown();
                previous.Delivery.Dispose();
                GameplayWorld next = WorldBuilder.Attach(restored, previous.World.Plan, false);
                next.UseSceneLoader(new ImmediateSceneLoader());
                World = NarrativeComposer.AttachRestored(Saves, next, Content, Modules.All);
                HollowmereNarrative.Wire(World, Interactions, null, null);
                Reattachments++;
            }

            public void Close()
            {
                if (closed)
                {
                    return;
                }

                closed = true;
                if (World.Root.State == GameApplicationState.Stopped)
                {
                    World.Delivery.Dispose();
                    World.World.Shutdown();
                    return;
                }

                World.Shutdown();
            }
        }

        private Game Boot(RegionManifest worldManifest)
        {
            var modules = new HollowmereNarrativeModules();
            var npcs = new NpcWorldExtension(npcRoster);
            var interactions = new InteractionWorldExtension(interactionRoster);
            var build = new WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            build.Extensions.Add(npcs);
            build.Extensions.Add(interactions);
            Assert.That(NarrativeComposer.TryBoot(worldManifest, content!, modules.All, new GameApplicationBootOptions { AssignDefaultWorld = false }, build, false,
                out NarrativeWorld? booted, out string failure), Is.True, failure);
            NarrativeWorld world = booted!;
            HollowmereNarrative.Wire(world, interactions, null, null);
            world.World.UseSceneLoader(new ImmediateSceneLoader());
            var game = new Game(world, modules, interactions, content!);
            games.Add(game);

            Assert.That(Gc018CheckpointCodecs.TryBuild(out _, out CheckpointCodecSet? codecs, out string codecDetail), Is.True, codecDetail);
            var options = new SaveServiceOptions("hollowmere.p1_7c", codecs!) { Directory = saveDirectory, RegionId = () => string.Empty };
            NarrativeDelivery.Configure(options, world);
            game.Saves = new SaveService(world.Root, options);
            Assert.That(game.Saves.Modules.Delivery, Is.SameAs(world.Delivery.Owner), "the save service captures the narrative delivery's owner");
            game.Saves.RootChanged += (previous, restored) => game.Reattach(restored);
            Assert.That(world.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(world.Runtime.Models.Problems, Is.Empty, string.Join("\n", world.Runtime.Models.Problems));
            return game;
        }

        private bool LoadContent()
        {
#if UNITY_EDITOR
            manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            content = UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayContentManifest>(HollowmereNarrative.ContentManifestPath);
            npcRoster = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcRoster>(NpcRosterPath);
            interactionRoster = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractionRoster>(InteractionRosterPath);
#endif
            Assert.That(manifest, Is.Not.Null, "the Hollowmere world is baked");
            Assert.That(content, Is.Not.Null, "the Drowned Bell content is baked");
            Assert.That(npcRoster, Is.Not.Null, "P1.3's NPC roster");
            Assert.That(interactionRoster, Is.Not.Null, "P1.3's interaction roster");
            return true;
        }

        // ------------------------------------------------------------------ the quest up to the clapper

        private IEnumerator Until(System.Func<bool> condition, string what, System.Action<int> count)
        {
            int frames = 0;
            while (!condition() && frames < MaxFrames)
            {
                frames++;
                yield return null;
            }

            count(frames);
            Assert.That(condition(), Is.True, "not reached within " + MaxFrames + " frames: " + what);
        }

        private static int QuestSlot(Game g, TargetId quest, SlotId slot) => g.World.World.Slots.ReadOrDefault(quest, QuestIds.Owner, slot, int.MinValue);

    }
}
