// Hollowmere P1.4 PlayMode - DrownedBellHeadless: "The Drowned Bell" played start to finish with typed commands only.
//
// Boots the baked Hollowmere world with the four narrative modules (NarrativeComposer) on the real PlayerLoop pump,
// with an ImmediateSceneLoader (no region scenes, no rendering), then:
//
//   1. dialogue.start Maren, advance, choose "I'll find the clapper", advance to the end -> heard_rumour,
//      maren_trusts_player, quest started and in stage 1 (the dialogue's startQuest went through the outbox)
//   2. inventory.grant 3 old coins, inventory.buy the gate key from Odd -> odd_paid (rule on TradeDone)
//   3. the gate interaction (condition HasGateKey, actions OpenGate) -> gate_open, quest stage 2 on the pay branch
//   4. world.travel to the marsh, inventory.pickup of the clapper, world.travel to the belfry
//   5. the bell interaction (HasBellClapper, RingBell) -> bell_rung, quest stage 3, gate_open kept by the rule
//   6. world.travel home, dialogue.start Maren ("You rang it!") to the end -> quest completed, rewards delivered once
//   7. the outbox records captured while the rewards were in flight are reinstated (a replayed outbox): every reward
//      obligation is delivered again and answers AlreadyApplied - no second lantern, no extra coins
//
// It asserts facts, quest slots and inventories from committed slots, exactly one sanctioned pump per frame, and logs
// [P1.4] lines with frame counts and durations of every phase.
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
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
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

            var boot = Stopwatch.StartNew();
            modules = new HollowmereNarrativeModules();
            var options = new GameApplicationBootOptions { AssignDefaultWorld = false };
            NarrativeWorld game = HollowmereNarrative.Boot(manifest!, content!, modules, options, false);
            world = game;
            game.World.UseSceneLoader(new ImmediateSceneLoader());
            GameApplicationRoot root = game.Root;
            Assert.That(root.Start().Outcome, Is.EqualTo(Outcome.Published));
            NarrativeRuntime rt = game.Runtime;
            Assert.That(rt.Models.Problems, Is.Empty, string.Join("\n", rt.Models.Problems));
            Timing("boot", boot.ElapsedMilliseconds, 0);
            yield return null;

            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;
            DialogueRunner talk = modules.Dialogue.Runner!;
            InventoryCommands inventory = modules.Inventory.Commands!;
            int questKey = Key(HollowmereNarrative.Quest);
            TargetId quest = rt.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            TargetId traveller = game.World.Focus;

            // 1. Maren tells the rumour and starts the quest.
            var phase = Stopwatch.StartNew();
            int frames = 0;
            Assert.That(talk.TryStart(string.Empty, HollowmereNarrative.MarenGraph), Is.True);
            yield return Until(() => Presented().Active && Presented().Kind == "line", "Maren's first line", f => frames += f);
            StringAssert.Contains("silent since the flood", Presented().Text);
            Assert.That(talk.Advance(), Is.True);
            yield return Until(() => Presented().Kind == "choice", "Maren's question", f => frames += f);
            Assert.That(Presented().Choices.Count, Is.EqualTo(2));
            Assert.That(talk.Choose(0), Is.True);
            yield return Until(() => Presented().Text.StartsWith("Bless you", System.StringComparison.Ordinal), "Maren's thanks", f => frames += f);
            Assert.That(talk.Advance(), Is.True);
            yield return Until(() => !Presented().Active, "the end of the conversation", f => frames += f);
            yield return Until(() => QuestSlot(quest, QuestIds.Stage) == 1, "quest stage 1 (startQuest delivered, rumour heard)", f => frames += f);
            Assert.That(Fact("heard_rumour"), Is.EqualTo(1));
            Assert.That(Fact("maren_trusts_player"), Is.EqualTo(1));
            Assert.That(QuestSlot(quest, QuestIds.Status), Is.EqualTo(QuestIds.Active));
            Timing("maren intro", phase.ElapsedMilliseconds, frames);

            // 2. Coins, and Odd's gate key.
            phase.Restart();
            frames = 0;
            Assert.That(inventory.Grant(HollowmereNarrative.OldCoin, 3).Admitted, Is.True);
            yield return Until(() => Held(HollowmereNarrative.OldCoin) == 3, "three old coins", f => frames += f);
            Assert.That(inventory.Buy(HollowmereNarrative.Vendor, HollowmereNarrative.GateKey, 1).Admitted, Is.True);
            yield return Until(() => Held(HollowmereNarrative.GateKey) == 1 && Fact("odd_paid") == 1, "the gate key, odd_paid", f => frames += f);
            Assert.That(Held(HollowmereNarrative.OldCoin), Is.EqualTo(0), "the key cost three old coins");
            Timing("buy key", phase.ElapsedMilliseconds, frames);

            // 3. The marsh gate.
            phase.Restart();
            frames = 0;
            Assert.That(NarrativeInteractions.Use(game, HollowmereNarrative.GateId, HollowmereNarrative.GateCondition, HollowmereNarrative.GateActions, out string failed),
                Is.True, failed);
            yield return Until(() => Fact("gate_open") == 1 && QuestSlot(quest, QuestIds.Stage) == 2, "gate_open and quest stage 2", f => frames += f);
            Assert.That(QuestSlot(quest, QuestIds.Branch), Is.EqualTo(1), "the quest passed the gate on the pay branch");
            Timing("gate", phase.ElapsedMilliseconds, frames);

            // 4. The marsh, the clapper, the belfry.
            phase.Restart();
            frames = 0;
            Assert.That(game.World.Commands.Travel(traveller, MarshId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(MarshId), "arrival in the marsh", f => frames += f);
            Assert.That(modules.Inventory.WorldItems!.TryPickup(HollowmereNarrative.ClapperWorldItem), Is.True);
            yield return Until(() => Held(HollowmereNarrative.BellClapper) == 1, "the bell clapper", f => frames += f);
            Assert.That(game.World.Commands.Travel(traveller, BelfryId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(BelfryId), "arrival in the belfry", f => frames += f);
            Timing("marsh and belfry", phase.ElapsedMilliseconds, frames);

            // 5. The bell.
            phase.Restart();
            frames = 0;
            Assert.That(NarrativeInteractions.Use(game, HollowmereNarrative.BellId, HollowmereNarrative.BellCondition, HollowmereNarrative.BellActions, out failed),
                Is.True, failed);
            yield return Until(() => Fact("bell_rung") == 1 && QuestSlot(quest, QuestIds.Stage) == 3, "bell_rung and quest stage 3", f => frames += f);
            Assert.That(Fact("gate_open"), Is.EqualTo(1), "the gate stays open after the bell");
            Timing("bell", phase.ElapsedMilliseconds, frames);

            // 6. Home to Maren.
            phase.Restart();
            frames = 0;
            Assert.That(game.World.Commands.Travel(traveller, VillageId).Admitted, Is.True);
            yield return Until(() => RegionOf(traveller) == AuthoringIds.StableKey(VillageId), "arrival home", f => frames += f);
            Assert.That(talk.TryStart(string.Empty, HollowmereNarrative.MarenGraph), Is.True);
            yield return Until(() => Presented().Active && Presented().Kind == "line", "Maren's thanks line", f => frames += f);
            StringAssert.Contains("You rang it", Presented().Text);
            Assert.That(talk.Advance(), Is.True);
            IReadOnlyList<OutboxRecordValue>? inFlight = null;
            Id128 grantPort = NarrativeDelivery.DestinationOf("grant");
            for (int i = 0; i < MaxFrames && !(QuestSlot(quest, QuestIds.Status) == QuestIds.Completed && Held(HollowmereNarrative.Lantern) == 1
                && Held(HollowmereNarrative.OldCoin) == 3 && Fact("maren_grateful") == 1 && OpenGrants(game.Delivery.Owner.ToRecords(), grantPort) == 0); i++)
            {
                IReadOnlyList<OutboxRecordValue> rows = game.Delivery.Owner.ToRecords();
                if (inFlight == null && OpenGrants(rows, grantPort) >= 2)
                {
                    inFlight = rows;
                }

                frames++;
                yield return null;
            }

            Assert.That(QuestSlot(quest, QuestIds.Status), Is.EqualTo(QuestIds.Completed));
            Assert.That(Held(HollowmereNarrative.Lantern), Is.EqualTo(1), "the lantern reward");
            Assert.That(Held(HollowmereNarrative.OldCoin), Is.EqualTo(3), "the pay-branch coins");
            Assert.That(Fact("maren_grateful"), Is.EqualTo(1));
            Assert.That(inFlight, Is.Not.Null, "the reward grants were seen in flight");
            Timing("return to maren", phase.ElapsedMilliseconds, frames);

            // 7. Replay the outbox: the reward obligations are delivered again and change nothing.
            phase.Restart();
            frames = 0;
            int acknowledgedBefore = game.Delivery.Owner.AcknowledgedCount;
            int alreadyBefore = game.Delivery.Owner.AlreadyAppliedCount;
            int grantedBefore = modules.Inventory.Granted;
            Assert.That(game.Delivery.Reinstate(inFlight!, out string detail), Is.True, detail);
            Assert.That(OpenGrants(game.Delivery.Owner.ToRecords(), grantPort), Is.GreaterThanOrEqualTo(2), "the replayed grants are open again");
            yield return Until(() => OpenGrants(game.Delivery.Owner.ToRecords(), grantPort) == 0, "the replayed grants settle", f => frames += f);
            for (int i = 0; i < 10; i++)
            {
                frames++;
                yield return null;
            }

            Assert.That(Held(HollowmereNarrative.Lantern), Is.EqualTo(1), "a replayed outbox grants the lantern exactly once");
            Assert.That(Held(HollowmereNarrative.OldCoin), Is.EqualTo(3), "a replayed outbox grants the coins exactly once");
            Assert.That(modules.Inventory.Granted, Is.EqualTo(grantedBefore), "no grant applied again");
            Assert.That(game.Delivery.Owner.AlreadyAppliedCount - alreadyBefore, Is.GreaterThanOrEqualTo(2), "the replayed obligations answered AlreadyApplied");
            Timing("outbox replay", phase.ElapsedMilliseconds, frames);

            int elapsedFrames = Time.frameCount - startFrame;
            int pumps = root.PumpCounter.SanctionedPumps - startPumps;
            Debug.Log("[P1.4] frames=" + elapsedFrames.ToString(CultureInfo.InvariantCulture) + " sanctionedPumps=" + pumps.ToString(CultureInfo.InvariantCulture)
                + " events=" + game.Host.EventsRead + " submitted=" + rt.Submitter.Submitted + " refusedSubmits=" + rt.Submitter.Refused
                + " obligations=" + game.Delivery.Owner.EnqueuedCount + " acknowledged=" + game.Delivery.Owner.AcknowledgedCount
                + " alreadyApplied=" + game.Delivery.Owner.AlreadyAppliedCount + " rulesFired=" + modules.Logic.Fired + " rulesSkipped=" + modules.Logic.Skipped
                + " questCommands=" + modules.Quest.Commands + " grants=" + modules.Inventory.Granted + " explain=" + game.Explain.Count
                + " | " + string.Join(" | ", timings));
            Assert.That(pumps, Is.EqualTo(elapsedFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);
            Assert.That(modules.Quest.RewardsGranted, Is.EqualTo(3), "three rewards, granted once");
            Assert.That(game.Delivery.Owner.RejectedCount, Is.EqualTo(0), "no obligation was refused");
            Assert.That(acknowledgedBefore, Is.GreaterThan(0));
            Assert.That(modules.Logic.Fired, Is.GreaterThanOrEqualTo(3), "odd_paid, the gate-keeping rule and return-to-Maren fired");
            Assert.That(game.Explain.TryExplain("OddPaidOnTrade", out ExplainRecord? explained) && explained != null && explained.Fired, Is.True);
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
            timings.Add(phase + " " + ms.ToString(CultureInfo.InvariantCulture) + "ms/" + frames.ToString(CultureInfo.InvariantCulture) + "f");
    }
}
