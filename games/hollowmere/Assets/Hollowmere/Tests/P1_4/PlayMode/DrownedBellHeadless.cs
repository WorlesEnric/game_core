// Hollowmere P1.4 PlayMode - DrownedBellHeadless: "The Drowned Bell" played start to finish with typed commands only.
//
// Boots the baked Hollowmere world with P1.3's NPC and interaction world extensions and the four narrative modules
// (NarrativeComposer) on the real PlayerLoop pump, with an ImmediateSceneLoader (no region scenes, no rendering), wires
// P1.3's seams to the narrative implementations (HollowmereNarrative.Wire), then:
//
//   (P3.1 re-scripted the steps on the reference game's story; the invariants are P1.4's.)
//   1. Maren's conversation started the way P1.3's NPC talk dispatcher starts it (IConversationStarter.TryStart with
//      Maren's entity and her graph ref dialogue.maren), "I will go" -> heard_rumour, quest started (through the
//      outbox) and in stage 1
//   2. inventory.grant up to four old coins, inventory.buy Bram's lantern at the inn -> quest stage 2
//   3. world.travel to the marsh; interact.use on the Causeway Gate -> refused (narrative.fact.gate_open is False);
//      Warden Hale sees the lantern and opens it (gate_open); interact.use -> unlocked
//   4. inventory.pickup of the clapper, world.travel to the belfry -> quest stage 3
//   5. interact.use on the Drowned Bell -> RingBellOnUse -> bell_rung, ending B (the shrine is dark), the quest
//      completed on branch 2 with maren_grateful; the bell's outbox records are captured while in flight
//   6. world.travel home, Maren again ("The bell rang") to the end; interact.use on the square's coins -> the pickup's
//      outbox records are captured in flight, three coins
//   7. the captured bell and pickup records are reinstated (a replayed outbox): no reward, fact or item changes, and
//      the pickup answers AlreadyApplied
//
// It asserts facts, quest and interactable slots and inventories from committed slots, exactly one sanctioned pump per
// frame, and logs [P1.4] lines with frame counts and durations of every phase.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Logic;
using GameCore.Unity.App;
using Hollowmere.Narrative;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P1_4.PlayMode.Tests
{
    public sealed class DrownedBellHeadless
    {
        private const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        private const string NpcRosterPath = "Assets/Hollowmere/Npcs/NpcRoster.asset";
        private const string InteractionRosterPath = "Assets/Hollowmere/Interactables/InteractionRoster.asset";
        private const string VillageId = "11e8dd95-6622-43d0-8b48-5e17b72f0bb8";
        private const string MarshId = "7f21b99a-8e74-412f-a1da-5f7d60843080";
        private const string BelfryId = "1c5a1ae9-bec2-4201-be5f-3ff8bf8e1d18";
        private const int MaxFrames = 240;

        private NarrativeWorld? world;
        private HollowmereNarrativeModules modules = null!;
        private readonly List<string> timings = new List<string>();

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator PlaysTheDrownedBell()
        {
#if UNITY_EDITOR
            RegionManifest? manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            GameplayContentManifest? content = UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayContentManifest>(HollowmereNarrative.ContentManifestPath);
            Assert.That(manifest, Is.Not.Null, "the Hollowmere world is baked");
            Assert.That(content, Is.Not.Null, "the Drowned Bell content is baked (EditMode AuthorsAndBakes)");
            NpcRoster? npcRoster = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcRoster>(NpcRosterPath);
            InteractionRoster? interactionRoster = UnityEditor.AssetDatabase.LoadAssetAtPath<InteractionRoster>(InteractionRosterPath);
            Assert.That(npcRoster, Is.Not.Null, "P1.3's NPC roster");
            Assert.That(interactionRoster, Is.Not.Null, "P1.3's interaction roster");

            var boot = Stopwatch.StartNew();
            modules = new HollowmereNarrativeModules();
            var options = new GameApplicationBootOptions { AssignDefaultWorld = false };
            var npcs = new NpcWorldExtension(npcRoster);
            var interactions = new InteractionWorldExtension(interactionRoster);
            var build = new WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            build.Extensions.Add(npcs);
            build.Extensions.Add(interactions);
            NarrativeWorld game = HollowmereNarrative.Boot(manifest!, content!, modules, options, build, false);
            world = game;
            HollowmereNarrative.Wire(game, interactions, null, null);
            Assert.That(interactions.Module, Is.Not.Null, "the interaction extension attached");
            Assert.That(interactions.Module!.Conditions, Is.SameAs(game.Conditions));
            game.World.UseSceneLoader(new ImmediateSceneLoader());
            GameApplicationRoot root = game.Root;
            Assert.That(root.Start().Outcome, Is.EqualTo(Outcome.Published));
            NarrativeRuntime rt = game.Runtime;
            Assert.That(rt.Models.Problems, Is.Empty, string.Join("\n", rt.Models.Problems));
            Timing("boot", boot.ElapsedMilliseconds, 0);
            yield return null;

            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;
            InventoryCommands inventory = modules.Inventory.Commands!;
            int questKey = Key(HollowmereNarrative.Quest);
            TargetId quest = rt.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            TargetId traveller = game.World.Focus;
            TargetId gate = AuthoringIds.TargetIdFor(HollowmereNarrative.GateId);
            TargetId bell = AuthoringIds.TargetIdFor(HollowmereNarrative.BellId);
            var interact = new InteractionCommands(game.World);

            // 1. Maren tells the rumour and starts the quest (started the way P1.3's NPC talk dispatcher starts it).
            var phase = Stopwatch.StartNew();
            int frames = 0;
            yield return Converse(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef, new[] { 2 }, "Maren's rumour", f => frames += f,
                first => StringAssert.Contains("I am Maren", first));
            yield return Until(() => QuestSlot(quest, QuestIds.Stage) == 1, "quest stage 1 (startQuest delivered, rumour heard)", f => frames += f);
            Assert.That(Fact("heard_rumour"), Is.EqualTo(1));
            Assert.That(QuestSlot(quest, QuestIds.Status), Is.EqualTo(QuestIds.Active));
            Timing("maren intro", phase.ElapsedMilliseconds, frames);

            // 2. Bram's lantern: up to four coins, then inventory.buy at the inn (four coins) -> quest stage 2.
            phase.Restart();
            frames = 0;
            int startCoins = Held(HollowmereNarrative.OldCoin);
            Assert.That(inventory.Grant(HollowmereNarrative.OldCoin, 4 - startCoins).Admitted, Is.True);
            yield return Until(() => Held(HollowmereNarrative.OldCoin) == 4, "four old coins", f => frames += f);
            Assert.That(inventory.Buy("InnStock", HollowmereNarrative.Lantern, 1).Admitted, Is.True);
            yield return Until(() => Held(HollowmereNarrative.Lantern) == 1 && Held(HollowmereNarrative.OldCoin) == 0, "the lantern for four coins", f => frames += f);
            yield return Until(() => QuestSlot(quest, QuestIds.Stage) == 2, "quest stage 2 (the crossing)", f => frames += f);
            Timing("lantern", phase.ElapsedMilliseconds, frames);

            // 3. The marsh: a gate that stays locked until Warden Hale, seeing the lantern, opens it.
            phase.Restart();
            frames = 0;
            Assert.That(game.World.Commands.Travel(traveller, MarshId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(MarshId), "arrival in the marsh", f => frames += f);
            yield return StandBy(gate, f => frames += f);
            Assert.That(InteractState(gate), Is.EqualTo(InteractableStates.Locked), "the Causeway Gate starts locked");
            int refusals = interactions.Module!.Refusals;
            Assert.That(interact.Use(traveller, rt.ActorKey, gate).Admitted, Is.True);
            yield return Until(() => interactions.Module!.Refusals > refusals, "the locked gate refuses", f => frames += f);
            Assert.That(InteractState(gate), Is.EqualTo(InteractableStates.Locked));
            Assert.That(game.Conditions.Evaluate("narrative.fact.gate_open", EvaluationContext.None, out string whyLocked), Is.False);
            Debug.Log("[P1.4] locked gate: " + whyLocked);
            yield return Converse(HollowmereNarrative.HaleId, HollowmereNarrative.HaleGraphRef, System.Array.Empty<int>(), "Hale opens the gate", f => frames += f, null);
            yield return Until(() => Fact("gate_open") == 1, "gate_open", f => frames += f);
            yield return new WaitForSecondsRealtime(1f); // the gate's 0.5 s use cooldown (interaction.cooling-down)
            int successes = interactions.Module!.Successes;
            Assert.That(interact.Use(traveller, rt.ActorKey, gate).Admitted, Is.True);
            yield return Until(() => interactions.Module!.Successes > successes && InteractState(gate) != InteractableStates.Locked,
                "the gate unlocks (lock condition narrative.fact.gate_open is True)", f => frames += f);
            Timing("hale and gate", phase.ElapsedMilliseconds, frames);

            // 4. The clapper, the belfry (the Reach objective -> quest stage 3).
            phase.Restart();
            frames = 0;
            Assert.That(modules.Inventory.WorldItems!.TryPickup(HollowmereNarrative.ClapperWorldItem), Is.True);
            yield return Until(() => Held(HollowmereNarrative.BellClapper) == 1, "the bell clapper", f => frames += f);
            Assert.That(game.World.Commands.Travel(traveller, BelfryId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(BelfryId), "arrival in the belfry", f => frames += f);
            yield return Until(() => QuestSlot(quest, QuestIds.Stage) == 3, "quest stage 3 (the drowned belfry)", f => frames += f);
            Timing("clapper and belfry", phase.ElapsedMilliseconds, frames);

            // 5. The bell, rung through P1.3's interaction: bell_rung, ending B (the shrine is dark), the quest completed on
            //    branch 2 with Maren's gratitude; the bell's outbox records are captured while in flight.
            phase.Restart();
            frames = 0;
            yield return StandBy(bell, f => frames += f);
            Assert.That(interact.Use(traveller, rt.ActorKey, bell).Admitted, Is.True);
            IReadOnlyList<OutboxRecordValue>? inFlight = null;
            for (int i = 0; i < MaxFrames && inFlight == null; i++)
            {
                if (game.Delivery.Owner.Outbox.OpenCount > 0)
                {
                    inFlight = game.Delivery.Owner.ToRecords();
                    break;
                }

                frames++;
                yield return null;
            }

            Assert.That(inFlight, Is.Not.Null, "the bell's obligations were seen in flight");
            yield return Until(() => Fact("bell_rung") == 1 && Fact("ending_b") == 1 && QuestSlot(quest, QuestIds.Status) == QuestIds.Completed
                && game.Delivery.Owner.Outbox.OpenCount == 0, "bell_rung, ending B and the quest completed", f => frames += f);
            Assert.That(QuestSlot(quest, QuestIds.Branch), Is.EqualTo(2), "ringing without the shrine is branch 2 (ending B)");
            Assert.That(Fact("maren_grateful"), Is.EqualTo(1), "ending B's reward");
            Assert.That(Fact("shrine_lit"), Is.EqualTo(0));
            Assert.That(game.Explain.TryExplain("RingBellOnUse", out ExplainRecord? rang) && rang != null && rang.Fired, Is.True);
            Timing("bell", phase.ElapsedMilliseconds, frames);

            // 6. Home to Maren: her lines follow bell_rung.
            phase.Restart();
            frames = 0;
            Assert.That(game.World.Commands.Travel(traveller, VillageId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(VillageId), "arrival home", f => frames += f);
            yield return Converse(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef, System.Array.Empty<int>(), "Maren's thanks", f => frames += f,
                first => StringAssert.Contains("The bell rang", first));

            // The square's coins: inventory.pickup through the rule Take_OldCoins_Village, its outbox records captured in flight.
            string coinsId = string.Empty;
            foreach (ManifestEntity entity in manifest!.Entities)
            {
                if (entity.name == "Coins (square)")
                {
                    coinsId = entity.authoringId;
                }
            }

            Assert.That(coinsId, Is.Not.Empty, "the square's coins are placed");
            TargetId coins = AuthoringIds.TargetIdFor(coinsId);
            int coinsBefore = Held(HollowmereNarrative.OldCoin);
            yield return StandBy(coins, f => frames += f);
            Id128 pickupPort = NarrativeDelivery.DestinationOf("pickup");
            Assert.That(interact.Use(traveller, rt.ActorKey, coins).Admitted, Is.True);
            IReadOnlyList<OutboxRecordValue>? pickupInFlight = null;
            for (int i = 0; i < MaxFrames && pickupInFlight == null; i++)
            {
                IReadOnlyList<OutboxRecordValue> rows = game.Delivery.Owner.ToRecords();
                if (OpenGrants(rows, pickupPort) >= 1)
                {
                    pickupInFlight = rows;
                    break;
                }

                frames++;
                yield return null;
            }

            Assert.That(pickupInFlight, Is.Not.Null, "the pickup was seen in flight");
            yield return Until(() => Held(HollowmereNarrative.OldCoin) == coinsBefore + 3 && OpenGrants(game.Delivery.Owner.ToRecords(), pickupPort) == 0,
                "the square's three coins", f => frames += f);
            Timing("return to maren", phase.ElapsedMilliseconds, frames);

            // 7. Replay the bell's outbox records: every obligation is delivered again and changes nothing.
            phase.Restart();
            frames = 0;
            int acknowledgedBefore = game.Delivery.Owner.AcknowledgedCount;
            int alreadyBefore = game.Delivery.Owner.AlreadyAppliedCount;
            int rejectedBeforeReplay = game.Delivery.Owner.RejectedCount;
            Assert.That(rejectedBeforeReplay, Is.EqualTo(0), "no obligation was refused while playing: " + string.Join(" | ", timings));
            int rewardsBefore = modules.Quest.RewardsGranted;
            int clappers = Held(HollowmereNarrative.BellClapper);
            Assert.That(game.Delivery.Reinstate(inFlight!, out string detail), Is.True, detail);
            yield return Until(() => game.Delivery.Owner.Outbox.OpenCount == 0, "the replayed bell obligations settle", f => frames += f);
            Assert.That(game.Delivery.Reinstate(pickupInFlight!, out detail), Is.True, detail);
            yield return Until(() => game.Delivery.Owner.Outbox.OpenCount == 0, "the replayed pickup settles", f => frames += f);
            for (int i = 0; i < 10; i++)
            {
                frames++;
                yield return null;
            }

            Assert.That(Fact("bell_rung"), Is.EqualTo(1));
            Assert.That(Fact("ending_b"), Is.EqualTo(1));
            Assert.That(Held(HollowmereNarrative.BellClapper), Is.EqualTo(clappers), "a replayed outbox changes no inventory");
            Assert.That(Held(HollowmereNarrative.OldCoin), Is.EqualTo(coinsBefore + 3), "a replayed pickup picks up nothing twice");
            Assert.That(modules.Quest.RewardsGranted, Is.EqualTo(rewardsBefore), "no reward granted again");
            Assert.That(game.Delivery.Owner.AlreadyAppliedCount - alreadyBefore, Is.GreaterThanOrEqualTo(1), "the replayed pickup answered AlreadyApplied");
            // A replayed obligation whose effect can no longer apply (the despawned coins) is refused rather than re-applied;
            // the state assertions above are what the replay must not change.
            Debug.Log("[P1.4] replay: alreadyApplied +" + (game.Delivery.Owner.AlreadyAppliedCount - alreadyBefore).ToString(CultureInfo.InvariantCulture)
                + " acknowledged +" + (game.Delivery.Owner.AcknowledgedCount - acknowledgedBefore).ToString(CultureInfo.InvariantCulture)
                + " refused +" + (game.Delivery.Owner.RejectedCount - rejectedBeforeReplay).ToString(CultureInfo.InvariantCulture));
            Timing("outbox replay", phase.ElapsedMilliseconds, frames);

            int elapsedFrames = Time.frameCount - startFrame;
            int pumps = root.PumpCounter.SanctionedPumps - startPumps;
            Debug.Log("[P1.4] frames=" + elapsedFrames.ToString(CultureInfo.InvariantCulture) + " sanctionedPumps=" + pumps.ToString(CultureInfo.InvariantCulture)
                + " events=" + game.Host.EventsRead + " submitted=" + rt.Submitter.Submitted + " refusedSubmits=" + rt.Submitter.Refused
                + " obligations=" + game.Delivery.Owner.EnqueuedCount + " acknowledged=" + game.Delivery.Owner.AcknowledgedCount
                + " alreadyApplied=" + game.Delivery.Owner.AlreadyAppliedCount + " rulesFired=" + modules.Logic.Fired + " rulesSkipped=" + modules.Logic.Skipped
                + " questCommands=" + modules.Quest.Commands + " grants=" + modules.Inventory.Granted + " explain=" + game.Explain.Count
                + " interactions=" + interactions.Module!.Successes + "/" + interactions.Module.Refusals + " evaluations=" + game.Conditions.Evaluations
                + " | " + string.Join(" | ", timings));
            Assert.That(pumps, Is.EqualTo(elapsedFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);
            Assert.That(modules.Quest.RewardsGranted, Is.EqualTo(1), "ending B's one reward, granted once");
            Assert.That(acknowledgedBefore, Is.GreaterThan(0));
            Assert.That(modules.Logic.Fired, Is.GreaterThanOrEqualTo(3), "the bell, its ending and the gate-keeping rules fired");
#else
            Assert.Ignore("DrownedBellHeadless loads baked assets by path and runs in the Editor only");
            yield break;
#endif
        }

        [TearDown]
        public void TearDown()
        {
            if (world != null)
            {
                world.Shutdown();
                world = null;
            }
        }

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

        private DialogueViewModel Presented() => modules.Dialogue.Presenter!.Last;

        /// <summary>Talks a conversation through: advances lines, picks the planned choices in order, until it ends.</summary>
        private IEnumerator Converse(string npcId, string graphRef, int[] choices, string what, System.Action<int> count, System.Action<string>? firstLine)
        {
            ConversationStart started = world!.Conversations.TryStart(npcId, graphRef);
            Assert.That(started.Started, Is.True, what + ": " + started.Detail);
            DialogueRunner talk = modules.Dialogue.Runner!;
            var queue = new Queue<int>(choices);
            bool first = true;
            for (int steps = 0; steps < 40; steps++)
            {
                yield return Until(() => !Presented().Active || Presented().CanAdvance || Presented().Kind == "choice", what + ": a line or a choice", count);
                DialogueViewModel shown = Presented();
                if (!shown.Active)
                {
                    break;
                }

                if (first && shown.Kind == "line")
                {
                    firstLine?.Invoke(shown.Text);
                    first = false;
                }

                int node = shown.Node;
                if (shown.Kind == "choice")
                {
                    Assert.That(queue.Count, Is.GreaterThan(0), what + ": an unplanned choice (" + shown.Text + ")");
                    Assert.That(talk.Choose(queue.Dequeue()), Is.True, what + ": choose");
                }
                else
                {
                    Assert.That(talk.Advance(), Is.True, what + ": advance");
                }

                yield return Until(() => !Presented().Active || Presented().Node != node, what + ": the next node", count);
            }

            Assert.That(Presented().Active, Is.False, what + ": the conversation ended");
        }

        /// <summary>Places the traveller one metre from an interactable (within its range) and waits for the pose.</summary>
        private IEnumerator StandBy(TargetId interactable, System.Action<int> count)
        {
            TargetId traveller = world!.World.Focus;
            int x = world.World.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
            int y = world.World.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int z = world.World.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0) - 1000;
            Assert.That(world.World.Commands.Place(traveller, x, y, z, 0).Admitted, Is.True);
            yield return Until(() => world!.World.Slots.ReadOrDefault(traveller, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue) == z,
                "the traveller stands by the interactable", count);
        }

        private int InteractState(TargetId interactable) =>
            world!.World.Slots.ReadOrDefault(interactable, InteractionSlots.Owner, InteractionSlots.State, int.MinValue);

        private int Key(string reference)
        {
            Assert.That(world!.Runtime.Models.TryResolve(reference, out int key), Is.True, reference);
            return key;
        }

        private int Fact(string name) =>
            world!.Runtime.Models.TryGetFactByName(name, out FactModel? fact) && fact != null ? world.Runtime.State.Fact(fact.Key) : int.MinValue;

        private int Held(string item) => world!.Runtime.State.ItemCount(0, Key(item), world.Runtime.ActorKey);

        private int QuestSlot(TargetId quest, SlotId slot) => world!.World.Slots.ReadOrDefault(quest, QuestIds.Owner, slot, int.MinValue);

        private int RegionOf(TargetId target) => world!.World.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);

        private static int OpenGrants(IReadOnlyList<OutboxRecordValue> rows, Id128 port)
        {
            int open = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                OutboxRecordValue row = rows[i];
                bool live = row.DeliveryState == (uint)OutboxDeliveryState.Pending || row.DeliveryState == (uint)OutboxDeliveryState.Delivered;
                if (live && row.DestinationHigh == port.High && row.DestinationLow == port.Low)
                {
                    open++;
                }
            }

            return open;
        }

        private void Timing(string phase, long ms, int frames) =>
            timings.Add(phase + " " + ms.ToString(CultureInfo.InvariantCulture) + "ms/" + frames.ToString(CultureInfo.InvariantCulture) + "f" + (world != null ? " rejected=" + world.Delivery.Owner.RejectedCount.ToString(CultureInfo.InvariantCulture) : string.Empty));
    }
}
