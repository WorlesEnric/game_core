// Hollowmere P3.1 PlayMode - FullQuestHeadless: "The Drowned Bell" played through the real Boot.unity (GameBoot +
// HollowmereGame: narrative, director, saves) with typed commands only - conversations started the way the NPC talk
// dispatcher starts them, interactions through InteractionCommands next to the target, travel through world.travel.
// One test per path:
//
//   EndingC_FreedEcho   Maren -> barn lantern -> Hale opens the gate -> shrine lanterns west, middle, east -> clapper ->
//                       Odd's favour and ferry -> ring the bell with the shrine alight -> quest completed on branch 3
//   EndingB_Toll        Maren -> coins -> Bram sells a lantern -> Hale -> clapper -> oil flask -> mend and pole the punt
//                       -> ring the bell -> branch 2
//   EndingA_Silence     Maren -> barn lantern -> Hale -> oil flask -> punt -> the Echo: "let it sleep" -> branch 1
//   Failure_LanternLost Maren -> barn lantern -> Hale -> the glinting sinkhole takes the only lantern -> quest failed
//   SaveRestoreMidQuest after Hale: capture a slot, round-trip (canonical slot hash equal), restore it into the running
//                       game (HollowmereGame re-attaches narrative and sessions), then finish on ending C
//
// Each asserts facts, items and quest state from committed state and logs [P3.1] lines with frames and durations.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Boot;
using Hollowmere.Game;
using Hollowmere.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Hollowmere.P3_1.PlayMode.Tests
{
    public sealed partial class FullQuestHeadless
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const string VillageId = "11e8dd95-6622-43d0-8b48-5e17b72f0bb8";
        private const string MarshId = "7f21b99a-8e74-412f-a1da-5f7d60843080";
        private const string BelfryId = "1c5a1ae9-bec2-4201-be5f-3ff8bf8e1d18";
        private const int MaxFrames = 900;

        private HollowmereGame game = null!;
        private readonly List<string> timings = new List<string>();
        private readonly Stopwatch clock = new Stopwatch();
        private int frames;

        // ------------------------------------------------------------------ paths

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EndingC_FreedEcho()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Lantern (barn)", () => Item("Lantern") >= 1, "the barn lantern");
            yield return Gate();
            yield return Interact("Shrine Lantern West", () => Fact("shrine_post_1") == 1, "the west shrine lantern");
            yield return Interact("Shrine Lantern Middle", () => Fact("shrine_post_2") == 1, "the middle shrine lantern");
            yield return Interact("Shrine Lantern East", () => Fact("shrine_lit") == 1, "the shrine alight");
            yield return Interact("Bell Clapper", () => Item("BellClapper") == 1, "the clapper");
            yield return Ferry();
            yield return Ring(() => Fact("ending_c") == 1, "ending C");
            yield return Ended(3);
            Assert.That(Fact("belfry_echo_freed"), Is.EqualTo(1));
            Assert.That(Fact("odd_favour"), Is.EqualTo(1));
            Report("ending C");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EndingB_Toll()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Coins (square)", () => Item("OldCoin") == 5, "the square's coins (2 + 3)");
            yield return Converse(EntityId("Bram"), "Bram", new[] { 0 }, () => Item("Lantern") == 1, "Bram's lantern");
            Assert.That(Item("OldCoin"), Is.EqualTo(1), "the lantern cost four coins");
            yield return Gate();
            yield return Interact("Bell Clapper", () => Item("BellClapper") == 1, "the clapper");
            yield return Interact("Oil Flask (marsh)", () => Item("OilFlask") == 1, "the oil flask");
            yield return Punt();
            yield return Ring(() => Fact("ending_b") == 1, "ending B");
            yield return Ended(2);
            Assert.That(Fact("shrine_lit"), Is.EqualTo(0));
            Report("ending B");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator EndingA_Silence()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Lantern (barn)", () => Item("Lantern") >= 1, "the barn lantern");
            yield return Gate();
            yield return Interact("Oil Flask (marsh)", () => Item("OilFlask") == 1, "the oil flask");
            yield return Punt();
            yield return Converse(HollowmereNarrative.EchoId, HollowmereNarrative.EchoGraphRef, new[] { 1 }, () => Fact("ending_a") == 1, "the Echo, left to sleep");
            yield return Ended(1);
            Assert.That(Fact("bell_rung"), Is.EqualTo(0));
            Report("ending A");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator Failure_LanternLost()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Lantern (barn)", () => Item("Lantern") >= 1, "the barn lantern");
            yield return Gate();
            yield return Interact("Glinting Sinkhole", () => Fact("lantern_lost") == 1, "the sinkhole takes the lantern");
            Assert.That(Item("Lantern"), Is.EqualTo(0));
            yield return Until(() => game.Director!.QuestStatus() == QuestIds.Failed, "the quest fails (LanternLostNoSpare)");
            yield return Until(() => game.Director!.Outcome == -1, "the failure ending");
            Report("failure");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator SaveRestoreMidQuest()
        {
            yield return Boot();
            yield return Rumour();
            yield return Interact("Lantern (barn)", () => Item("Lantern") >= 1, "the barn lantern");
            yield return Gate();
            yield return Interact("Shrine Lantern West", () => Fact("shrine_post_1") == 1, "the west shrine lantern");
            for (int i = 0; i < 5; i++)
            {
                yield return null;
            }

            SaveService saves = game.Saves!;
            SaveRoundTripReport trip = saves.TestRoundTrip();
            Debug.Log("[P3.1] round trip: " + trip.Detail + (trip.Refusal == null ? string.Empty : " refused " + trip.Refusal));
            Assert.That(trip.Refusal, Is.Null);
            Assert.That(trip.Equal, Is.True, trip.Detail);
            SaveResult captured = saves.Capture("p31-mid");
            Assert.That(captured.Succeeded, Is.True, captured.ToString());
            int restoresBefore = game.Restores;
            SaveResult restored = saves.Restore("p31-mid");
            Assert.That(restored.Succeeded, Is.True, restored.ToString());
            Debug.Log("[P3.1] capture slot hash " + captured.SlotHash + ", restore slot hash " + restored.SlotHash);
            if (restored.SlotHash.Length > 0)
            {
                Assert.That(restored.SlotHash, Is.EqualTo(captured.SlotHash), "the restored slot rows hash like the captured ones");
            }

            yield return Until(() => game.Restores > restoresBefore, "HollowmereGame re-attached the restored world");
            Assert.That(game.RestoreFailure, Is.Empty);
            Assert.That(Fact("shrine_post_1"), Is.EqualTo(1), "the restored world keeps the lit lantern");
            Assert.That(Fact("gate_open"), Is.EqualTo(1));
            Assert.That(Item("Lantern"), Is.GreaterThanOrEqualTo(1));
            yield return Interact("Shrine Lantern Middle", () => Fact("shrine_post_2") == 1, "the middle shrine lantern (after restore)");
            yield return Interact("Shrine Lantern East", () => Fact("shrine_lit") == 1, "the shrine alight (after restore)");
            yield return Interact("Bell Clapper", () => Item("BellClapper") == 1, "the clapper (after restore)");
            yield return Ferry();
            yield return Ring(() => Fact("ending_c") == 1, "ending C (after restore)");
            yield return Ended(3);
            Report("save/restore");
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator R7C_WPLUG08_RepeatedBarnLanternPickupAndReload_LeavesExactlyOneLantern()
        {
            yield return Boot();
            Assert.That(Item("Lantern"), Is.Zero, "a fresh game has no lantern");
            string lanternId = EntityId("Lantern (barn)");
            TargetId lantern = AuthoringIds.TargetIdFor(lanternId);
            yield return StandBy(lanternId);
            yield return LanternUseBurst(lantern, 1);
            yield return Until(() => Item("Lantern") == 1, "the actual barn pickup delivers its lantern");
            yield return Until(() => game.World!.Slots.ReadOrDefault(lantern, InteractionSlots.Owner, InteractionSlots.CooldownMs, -1) == 0, "the pickup cooldown expires");
            yield return LanternUseBurst(lantern, 0);
            Assert.That(Item("Lantern"), Is.EqualTo(1), "repeated pickup after cooldown does not grant another lantern");

            const string slot = "r7c-lantern-spam";
            SaveService saves = game.Saves!;
            try
            {
                SaveResult captured = saves.Capture(slot);
                Assert.That(captured.Succeeded, Is.True, captured.ToString());
                GameplayWorld original = game.World!;
                int restoresBefore = game.Restores;
                SaveResult restored = saves.Restore(slot);
                Assert.That(restored.Succeeded, Is.True, restored.ToString());
                Assert.That(restored.SlotHash, Is.EqualTo(captured.SlotHash), "production restore preserves all captured slot rows");
                yield return Until(() => game.Restores > restoresBefore, "the restored world and narrative sessions attach");
                Assert.That(game.RestoreFailure, Is.Empty);
                Assert.That(game.World, Is.Not.SameAs(original), "the test continues in the production-restored world");
                Assert.That(Item("Lantern"), Is.EqualTo(1), "the save contains exactly the barn's single lantern");
                yield return StandBy(lanternId);
                yield return LanternUseBurst(lantern, 0);
                yield return LanternUseBurst(lantern, 0);
                Assert.That(Item("Lantern"), Is.EqualTo(1), "take spam after reload still leaves exactly one lantern");
                Assert.That(game.World!.Slots.ReadOrDefault(lantern, InteractionSlots.Owner, InteractionSlots.Uses, -1), Is.EqualTo(1));
                Assert.That(game.Narrative!.Delivery.Dropped, Is.Zero, game.Narrative.Delivery.LastDropDetail);
                Assert.That(game.World.Root.PumpCounter.Violations, Is.Zero);
                Report("R7-C W-PLUG-08: 32 real barn uses, one successful pickup, one lantern across save/reload");
            }
            finally
            {
                SaveResult deleted = saves.Delete(slot);
                Assert.That(deleted.Succeeded, Is.True, deleted.ToString());
            }
        }

        private IEnumerator LanternUseBurst(TargetId lantern, int expectedSuccesses)
        {
            const int attempts = 8;
            GameplayWorld world = game.World!;
            var cursor = new GameplayEventCursor();
            var events = new List<CommittedEvent>();
            cursor.ReadInto(world, events, 4096);
            var commands = new InteractionCommands(world);
            for (int i = 0; i < attempts; i++)
            {
                Assert.That(commands.Use(world.Focus, game.Narrative!.Runtime.ActorKey, lantern).Admitted, Is.True, "real interact.use attempt " + i);
            }

            int succeeded = 0;
            int refused = 0;
            for (int waited = 0; waited < MaxFrames && succeeded + refused < attempts; waited++)
            {
                yield return null;
                events.Clear();
                cursor.ReadInto(world, events, 4096);
                foreach (CommittedEvent committed in events)
                {
                    if (!GameplayActorEvent.TryDecode(committed.Payload, out GameplayActorEvent payload) || !payload.Target.Equals(lantern))
                        continue;
                    if (committed.Schema.Equals(InteractionSlots.SucceededEvent))
                        succeeded++;
                    else if (committed.Schema.Equals(InteractionSlots.RefusedEvent))
                    {
                        refused++;
                        Assert.That(new[] { "interaction.cooling-down", "interaction.uses-exhausted", "interaction.already-used" }, Does.Contain(InteractionSlots.RefusalCode(payload.B)), "repeat uses must be refused because this lantern was already taken, not due to range or broken wiring");
                    }
                }
            }

            Assert.That(succeeded + refused, Is.EqualTo(attempts), "every admitted pickup attempt reaches a committed outcome");
            Assert.That(succeeded, Is.EqualTo(expectedSuccesses));
            Assert.That(refused, Is.EqualTo(attempts - expectedSuccesses));
            // Give the real action/outbox/inventory stages time to expose any duplicated grant.
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.That(Item("Lantern"), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ phases

        private IEnumerator Boot()
        {
#if UNITY_EDITOR
            clock.Restart();
            AsyncOperation? load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BootScene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null, "Boot.unity must exist");
            while (!load!.isDone)
            {
                yield return null;
            }

            HollowmereGame? found = null;
            for (int i = 0; i < MaxFrames && (found == null || found.Narrative == null || found.Director == null || found.Saves == null || found.Rig == null); i++)
            {
                found = Object.FindAnyObjectByType<HollowmereGame>();
                yield return null;
            }

            Assert.That(found, Is.Not.Null, "Boot.unity carries HollowmereGame");
            game = found!;
            Assert.That(game.Failure, Is.Empty);
            Assert.That(game.Director, Is.Not.Null, "the director attached");
            Assert.That(game.Rig!.Ui.Dispatcher.Dispatch("newgame").Accepted, Is.True);
            yield return Until(() => game.Rig!.Ui.Screen == GameCore.Rules.Gameplay.Ui.UiScreen.Hud, "the HUD");
            Timing("boot");
#else
            Assert.Ignore("FullQuestHeadless loads Boot.unity by path and runs in the Editor only");
            yield break;
#endif
        }

        private IEnumerator Rumour()
        {
            yield return Converse(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef, new[] { 2 }, () => Fact("heard_rumour") == 1, "Maren's rumour");
            yield return Until(() => game.Director!.QuestStatus() == QuestIds.Active, "the quest started");
            yield return Until(() => game.Director!.QuestStage() >= 1, "past the rumour stage");
        }

        private IEnumerator Gate()
        {
            yield return Travel(MarshId, "the marsh");
            yield return Converse(HollowmereNarrative.HaleId, HollowmereNarrative.HaleGraphRef, Array.Empty<int>(), () => Fact("gate_open") == 1, "Hale opens the gate");
            yield return Until(() => game.Director!.QuestStage() >= 2, "the crossing stage");
        }

        private IEnumerator Ferry()
        {
            yield return Converse(EntityId("Odd"), HollowmereNarrative.OddGraphRef, new[] { 0 }, () => Region() == AuthoringIds.StableKey(BelfryId), "Odd ferries to the belfry");
            Timing("ferry");
        }

        private IEnumerator Punt()
        {
            yield return Interact("Old Punt", () => Fact("punt_repaired") == 1, "the punt sealed with pitch");
            Assert.That(Item("OilFlask"), Is.EqualTo(0), "the pitch used the oil flask");
            yield return new WaitForSecondsRealtime(1f); // the punt's 0.5 s use cooldown (interaction.cooling-down)
            yield return Interact("Old Punt", () => Region() == AuthoringIds.StableKey(BelfryId), "the punt crosses to the belfry");
        }

        private IEnumerator Ring(Func<bool> ending, string what)
        {
            yield return Until(() => game.Director!.QuestStage() >= 3, "the belfry stage");
            yield return InteractWith(HollowmereNarrative.BellId, () => Fact("bell_rung") == 1, "the bell rings");
            yield return Until(ending, what);
        }

        private IEnumerator Ended(int branch)
        {
            yield return Until(() => game.Director!.QuestStatus() == QuestIds.Completed, "the quest completed");
            Assert.That(game.Director!.QuestBranch(), Is.EqualTo(branch), "completed on branch " + branch);
            yield return Until(() => game.Director!.Outcome == branch, "the ending of branch " + branch);
        }

        // ------------------------------------------------------------------ verbs

        private IEnumerator Travel(string regionId, string what)
        {
            GameplayWorld world = game.World!;
            Assert.That(world.Commands.Travel(world.Focus, regionId).Admitted, Is.True, "travel to " + what);
            yield return Until(() => Region() == AuthoringIds.StableKey(regionId), "arrival in " + what);
        }

        private IEnumerator Converse(string entityId, string graph, int[] choices, Func<bool> done, string what)
        {
            yield return StandBy(entityId);
            NarrativeWorld narrative = game.Narrative!;
            ConversationStart started = narrative.Conversations.TryStart(entityId, graph);
            Assert.That(started.Started, Is.True, what + ": " + started.Detail);
            GameBoot boot = game.GetComponent<GameBoot>();
            var queue = new Queue<int>(choices);
            int steps = 0;
            while (steps++ < 40)
            {
                yield return Until(() => !Presented(boot).Active || Presented(boot).CanAdvance || Presented(boot).Kind == "choice", what + ": a line or a choice");
                DialogueViewModel shown = Presented(boot);
                if (!shown.Active)
                {
                    break;
                }

                int node = shown.Node;
                if (shown.Kind == "choice")
                {
                    Assert.That(queue.Count, Is.GreaterThan(0), what + ": an unplanned choice at node " + node + " (" + shown.Text + ")");
                    Assert.That(boot.Modules!.Dialogue.Runner!.Choose(queue.Dequeue()), Is.True, what + ": choose");
                }
                else
                {
                    Assert.That(boot.Modules!.Dialogue.Runner!.Advance(), Is.True, what + ": advance at node " + node);
                }

                yield return Until(() => !Presented(boot).Active || Presented(boot).Node != node, what + ": the next node");
            }

            yield return Until(done, what);
            Timing(what);
        }

        private IEnumerator Interact(string entityName, Func<bool> done, string what)
        {
            yield return InteractWith(EntityId(entityName), done, what);
        }

        private IEnumerator InteractWith(string entityId, Func<bool> done, string what)
        {
            yield return StandBy(entityId);
            GameplayWorld world = game.World!;
            var commands = new InteractionCommands(world);
            Assert.That(commands.Use(world.Focus, game.Narrative!.Runtime.ActorKey, AuthoringIds.TargetIdFor(entityId)).Admitted, Is.True, what + ": interact.use admitted");
            yield return Until(done, what);
            Timing(what);
        }

        /// <summary>Places the traveller one metre south of an entity (its committed pose) and waits for the pose.</summary>
        private IEnumerator StandBy(string entityId)
        {
            GameplayWorld world = game.World!;
            TargetId target = AuthoringIds.TargetIdFor(entityId);
            TargetId traveller = world.Focus;
            yield return Until(() => world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.Region, 0) == Region(), "the entity is in the traveller's region");
            int x = world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
            int y = world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int z = world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0) - 1000;
            Assert.That(world.Commands.Place(traveller, x, y, z, 0).Admitted, Is.True);
            yield return Until(() => world.Slots.ReadOrDefault(traveller, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue) == z, "the traveller stands by the entity");
        }

        private IEnumerator Until(Func<bool> condition, string what)
        {
            int waited = 0;
            while (!condition() && waited < MaxFrames)
            {
                waited++;
                frames++;
                yield return null;
            }

            Assert.That(condition(), Is.True, "not reached within " + MaxFrames + " frames: " + what + " (" + State() + ")");
        }

        // ------------------------------------------------------------------ state

        private static DialogueViewModel Presented(GameBoot boot) => boot.Modules!.Dialogue.Presenter!.Last;

        private int Fact(string name) => game.Director!.Fact(name);

        private int Item(string name) => game.Director!.ItemCount(name);

        private int Region()
        {
            GameplayWorld world = game.World!;
            return world.Slots.ReadOrDefault(world.Focus, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);
        }

        /// <summary>The authoring id of a placed entity by name (the baked world manifest).</summary>
        private string EntityId(string name)
        {
            foreach (ManifestEntity entity in game.World!.Manifest.Entities)
            {
                if (entity.name == name)
                {
                    return entity.authoringId;
                }
            }

            Assert.Fail("no placed entity named '" + name + "' in the baked manifest");
            return string.Empty;
        }

        private string State() =>
            "region " + Region().ToString(CultureInfo.InvariantCulture) + ", stage " + game.Director!.QuestStage().ToString(CultureInfo.InvariantCulture)
            + ", status " + game.Director.QuestStatus().ToString(CultureInfo.InvariantCulture) + ", outcome " + game.Director.Outcome.ToString(CultureInfo.InvariantCulture)
            + ", lantern " + Item("Lantern").ToString(CultureInfo.InvariantCulture) + ", coins " + Item("OldCoin").ToString(CultureInfo.InvariantCulture)
            + ", oil " + Item("OilFlask").ToString(CultureInfo.InvariantCulture) + ", clapper " + Item("BellClapper").ToString(CultureInfo.InvariantCulture)
            + ", interact ok/refused " + game.GetComponent<GameBoot>().InteractionExtension!.Module!.Successes.ToString(CultureInfo.InvariantCulture)
            + "/" + game.GetComponent<GameBoot>().InteractionExtension!.Module!.Refusals.ToString(CultureInfo.InvariantCulture)
            + " last " + game.Interactions!.Dispatcher.LastRefusalCode
            + ", pickups " + game.GetComponent<GameBoot>().Modules!.Inventory.Pickups.ToString(CultureInfo.InvariantCulture)
            + " refused " + game.GetComponent<GameBoot>().Modules!.Inventory.Refused.ToString(CultureInfo.InvariantCulture)
            + ", delivery dropped " + game.Narrative!.Delivery.Dropped.ToString(CultureInfo.InvariantCulture) + " " + game.Narrative.Delivery.LastDropDetail
            + ", problem " + game.Director.LastProblem;

        private void Timing(string phase)
        {
            timings.Add(phase + " " + clock.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms/" + frames.ToString(CultureInfo.InvariantCulture) + "f");
            clock.Restart();
            frames = 0;
        }

        private void Report(string path) => Debug.Log("[P3.1] " + path + ": " + string.Join(" | ", timings));

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            HollowmereGame? running = Object.FindAnyObjectByType<HollowmereGame>();
            if (running != null)
            {
                Object.Destroy(running.gameObject);
                yield return null;
            }

            timings.Clear();
        }
    }
}
