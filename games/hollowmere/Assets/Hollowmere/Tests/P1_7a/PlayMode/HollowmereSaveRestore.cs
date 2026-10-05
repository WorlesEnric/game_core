// Hollowmere P1.7a PlayMode - HollowmereSaveRestore: the narrative layer, streaming and runtime targets survive
// SaveService.Restore (P1.7a review blockers), on the real PlayerLoop pump with an ImmediateSceneLoader.
//
//   W-PERSIST-01  The Drowned Bell played to the belfry (quest mid-way, Odd moved by world.place, one runtime-spawned
//                 target), the bell rung, and save.capture taken in the frame whose pump recorded the bell's deliveries
//                 (open outbox obligations). A FRESH boot restores it: the narrative layer re-attaches on the restored
//                 root's delivery owner (NarrativeComposer.AttachRestored), the canonical slot hash of the restored world
//                 equals the capture's, the streamer reconciles (Belfry resident, Village unloaded), the in-flight
//                 deliveries apply exactly once (bell_rung, quest stage 3, the bell rule fired once), the runtime target
//                 is registered again, dialogue.start works after travelling home. Then save.capture while a pickup
//                 (the square's three coins) is in flight, an in-place restore, and the pickup lands exactly once; a
//                 replay of the in-flight outbox rows on top changes nothing (AlreadyApplied). (P3.1 re-scripted the
//                 play on the reference game's story: Maren, a lantern, Hale's gate, the clapper, ending B.)
//                 W-PLUG-01 (moved NPC stays across a region cycle and a restore), W-PLUG-04 (an NPC in an unloaded
//                 region keeps its slots across save/load), W-PLUG-06 (facts persist) are asserted on the way.
//   W-PERSIST-02  A cosmetic edit (a definition whose content stamp changed, structural stamp kept) restores.
//   W-PERSIST-03  A structural edit (structural stamp changed) refuses with RecipeRevisionMismatch.
//   W-PLUG-08     200 grants get 200 distinct request ids (none deduplicated), and grants after a reload still apply.
//   Counts of old coins are relative to the player's starting purse (P3.1: two coins).
//   Determinism   Two boots fed the same frame-indexed commands commit identical slots.
//
// The save service uses the validation project's generated checkpoint codecs (Tests/P1_5/Checkpoint), as P1.5 does.
// Counts and durations are logged as [P1.7a] lines.
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

namespace Hollowmere.P1_7a.PlayMode.Tests
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

        private readonly List<Game> games = new List<Game>();
        private readonly List<string> timings = new List<string>();
        private string saveDirectory = string.Empty;
        private RegionManifest? manifest;
        private GameplayContentManifest? content;
        private NpcRoster? npcRoster;
        private InteractionRoster? interactionRoster;

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "gamecore-p1_7a-saves-" + System.Guid.NewGuid().ToString("N"));
            timings.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = games.Count - 1; i >= 0; i--)
            {
                games[i].Close();
            }

            games.Clear();
            if (saveDirectory.Length > 0 && Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
        }

        // ------------------------------------------------------------------ W-PERSIST-01 (+ W-PLUG-01/04/06)

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator FreshBootRestore_ReattachesTheNarrativeLayer_AndDeliversInFlightWorkExactlyOnce()
        {
#if UNITY_EDITOR
            if (!LoadContent())
            {
                yield break;
            }

            var total = Stopwatch.StartNew();
            Game a = Boot(manifest!);
            yield return null;
            int frames = 0;

            // 1. Maren, the marsh, the key, the gate, the clapper (as P1.4's DrownedBellHeadless).
            var phase = Stopwatch.StartNew();
            yield return PlayToTheClapper(a, f => frames += f);
            Timing("play to the clapper", phase.ElapsedMilliseconds, frames);

            // W-PLUG-01: Odd (idle, no patrol, no schedule) is moved by world.place in his own region.
            TargetId odd = AuthoringIds.TargetIdFor(HollowmereNarrative.OddId);
            int oddX = Pos(a, odd, GameplaySlots.PosX) + 1500;
            int oddY = Pos(a, odd, GameplaySlots.PosY);
            int oddZ = Pos(a, odd, GameplaySlots.PosZ) + 1500;
            Assert.That(a.World.World.Commands.Place(odd, oddX, oddY, oddZ, 0).Admitted, Is.True);
            yield return Until(() => Pos(a, odd, GameplaySlots.PosX) == oddX && Pos(a, odd, GameplaySlots.PosZ) == oddZ, "Odd is placed", f => frames += f);

            // A runtime-spawned target in the marsh (its identity comes from world.spawnOrdinal, a committed slot).
            string definitionId = a.World.World.Manifest.Definitions[0].authoringId;
            int spawnX = oddX + 3000;
            Assert.That(a.World.World.Spawner.TrySpawn(definitionId, MarshId, spawnX, oddY, oddZ, 0, out TargetId spawned, out string spawnDetail), Is.True, spawnDetail);
            Assert.That(a.World.World.Spawner.CommittedOrdinal, Is.EqualTo(1));

            // 2. The belfry (the marsh and the village unload), the bell.
            phase.Restart();
            frames = 0;
            TargetId traveller = a.World.World.Focus;
            Assert.That(a.World.World.Commands.Travel(traveller, BelfryId).Admitted, Is.True);
            yield return Until(() => RegionOf(a, traveller) == AuthoringIds.StableKey(BelfryId) && a.World.World.Streamer.IsSettled, "arrival in the belfry", f => frames += f);
            Assert.That(a.World.World.Streamer.ResidencyOf(VillageId), Is.EqualTo(RegionResidency.Unloaded));
            Assert.That(Pos(a, odd, GameplaySlots.PosX), Is.EqualTo(oddX), "W-PLUG-01: Odd stays where he was put while the marsh is unloaded");
            Assert.That(Pos(a, odd, GameplaySlots.PosZ), Is.EqualTo(oddZ));

            TargetId bell = AuthoringIds.TargetIdFor(HollowmereNarrative.BellId);
            yield return StandBy(a, bell, f => frames += f);
            var interact = new InteractionCommands(a.World.World);
            Assert.That(interact.Use(traveller, a.World.Runtime.ActorKey, bell).Admitted, Is.True);
            for (int i = 0; i < MaxFrames && a.World.Delivery.Owner.Outbox.OpenCount == 0; i++)
            {
                frames++;
                yield return null;
            }

            int openAtCapture = a.World.Delivery.Owner.Outbox.OpenCount;
            Assert.That(openAtCapture, Is.GreaterThan(0), "the bell's deliveries are open obligations right after the committing pump");
            Assert.That(Fact(a, "bell_rung"), Is.EqualTo(0), "the bell's actions are still in flight");

            // 3. save.capture in that frame.
            SaveResult saved = a.Saves.Capture("slot-1");
            Assert.That(saved.Succeeded, Is.True, saved.ToString());
            int questKey = Key(a, HollowmereNarrative.Quest);
            TargetId quest = a.World.Runtime.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            int stageAtCapture = QuestSlot(a, quest, QuestIds.Stage);
            TargetId maren = AuthoringIds.TargetIdFor(HollowmereNarrative.MarenId);
            int[] marenAtCapture = NpcSlots(a, maren);
            Dictionary<TargetId, int> occupantsAtCapture = Occupants(a);
            Timing("belfry and capture (" + openAtCapture + " open)", phase.ElapsedMilliseconds, frames);
            a.Close();

            // 4. A fresh boot restores slot-1.
            phase.Restart();
            frames = 0;
            Game b = Boot(manifest!);
            yield return null;
            SaveResult restored = b.Saves.Restore("slot-1");
            Assert.That(restored.Succeeded, Is.True, restored.ToString());
            Assert.That(b.Reattachments, Is.EqualTo(1), "RootChanged re-attached the narrative layer");
            Assert.That(b.World.Root, Is.SameAs(b.Saves.ActiveRoot));
            Assert.That(b.World.Delivery.Owner, Is.SameAs(b.Saves.Modules.Delivery), "one delivery owner per world: the restored owner");
            Assert.That(b.World.Delivery.Owner.Outbox.OpenCount, Is.EqualTo(openAtCapture), "the open obligations were reinstated");
            Assert.That(QuestSlot(b, quest, QuestIds.Stage), Is.EqualTo(stageAtCapture));
            Assert.That(Fact(b, "heard_rumour"), Is.EqualTo(1), "W-PLUG-06: facts persist");
            Assert.That(Fact(b, "gate_open"), Is.EqualTo(1));
            Assert.That(NpcSlots(b, maren), Is.EqualTo(marenAtCapture), "W-PLUG-04: an NPC in an unloaded region keeps its slots across save/load");
            Assert.That(Pos(b, odd, GameplaySlots.PosX), Is.EqualTo(oddX), "W-PLUG-01: the moved NPC stays across a restore");
            Assert.That(Occupants(b), Is.EqualTo(occupantsAtCapture), "interact.occupants restored");
            Assert.That(b.World.World.RestoredRuntimeTargets, Is.EqualTo(1), "the runtime-spawned target is registered again");
            Assert.That(b.World.World.Entities.TryGet(spawned, out EntityRecord? spawnedRecord) && spawnedRecord != null, Is.True);
            Assert.That(b.World.World.Spawner.CommittedOrdinal, Is.EqualTo(1));

            SaveRoundTripReport roundTrip = b.Saves.TestRoundTrip();
            Assert.That(roundTrip.Refusal, Is.Null, roundTrip.ToString());
            Assert.That(roundTrip.SourceSlotHash, Is.EqualTo(saved.SlotHash), "the restored world's canonical slot hash equals the capture's");
            Assert.That(roundTrip.Equal, Is.True, roundTrip.Detail);
            Assert.That(b.Saves.FrameRebinds, Is.GreaterThanOrEqualTo(1), "A11: the round trip binds the shared presentation frame back to the kept root");

            yield return Until(() => b.World.World.Streamer.IsSettled, "the streamer reconciles", f => frames += f);
            Assert.That(b.World.World.Streamer.Reconciliations, Is.GreaterThan(0));
            Assert.That(b.World.World.Streamer.ResidencyOf(BelfryId), Is.EqualTo(RegionResidency.Resident));
            Assert.That(b.World.World.Streamer.ResidencyOf(VillageId), Is.EqualTo(RegionResidency.Unloaded));

            yield return Until(() => Fact(b, "bell_rung") == 1 && Fact(b, "ending_b") == 1 && b.World.Delivery.Owner.Outbox.OpenCount == 0,
                "the in-flight bell deliveries apply after the restore", f => frames += f);
            yield return Until(() => QuestSlot(b, quest, QuestIds.Status) == QuestIds.Completed, "the quest completes on ending B", f => frames += f);
            for (int i = 0; i < 30; i++)
            {
                frames++;
                yield return null;
            }

            Assert.That(RuleFired(b, "RingBellOnUse"), Is.EqualTo(1), "the bell rule fired exactly once");
            Assert.That(b.World.Delivery.Dropped, Is.Zero, b.World.Delivery.LastDropDetail);
            Timing("fresh boot restore and in-flight delivery", phase.ElapsedMilliseconds, frames);

            // 5. Home: dialogue.start works on the re-attached world (Maren's lines follow bell_rung); then the square's
            //    coins are picked up and save.capture is taken while the pickup is in flight.
            phase.Restart();
            frames = 0;
            traveller = b.World.World.Focus;
            Assert.That(b.World.World.Commands.Travel(traveller, VillageId).Admitted, Is.True);
            yield return Until(() => RegionOf(b, traveller) == AuthoringIds.StableKey(VillageId), "arrival home", f => frames += f);
            ConversationStart started = b.World.Conversations.TryStart(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef);
            Assert.That(started.Started, Is.True, started.Detail);
            yield return Until(() => Presented(b).Active && Presented(b).Kind == "line", "Maren's thanks line", f => frames += f);
            StringAssert.Contains("The bell rang", Presented(b).Text);
            Assert.That(b.Modules.Dialogue.Runner!.Advance(), Is.True);
            yield return Until(() => !Presented(b).Active, "the end of the conversation", f => frames += f);

            int coinsBefore = Held(b, HollowmereNarrative.OldCoin);
            TargetId coins = AuthoringIds.TargetIdFor(EntityId("Coins (square)"));
            yield return StandBy(b, coins, f => frames += f);
            Id128 pickupPort = NarrativeDelivery.DestinationOf("pickup");
            Assert.That(new InteractionCommands(b.World.World).Use(traveller, b.World.Runtime.ActorKey, coins).Admitted, Is.True);
            IReadOnlyList<OutboxRecordValue>? inFlight = null;
            SaveResult? pickupSaved = null;
            for (int i = 0; i < MaxFrames && pickupSaved == null; i++)
            {
                IReadOnlyList<OutboxRecordValue> rows = b.World.Delivery.Owner.ToRecords();
                if (OpenGrants(rows, pickupPort) >= 1)
                {
                    inFlight = rows;
                    pickupSaved = b.Saves.Capture("slot-2");
                    break;
                }

                frames++;
                yield return null;
            }

            Assert.That(pickupSaved, Is.Not.Null, "the pickup was seen in flight");
            Assert.That(pickupSaved!.Succeeded, Is.True, pickupSaved.ToString());
            yield return Until(() => Held(b, HollowmereNarrative.OldCoin) == coinsBefore + 3, "the coins before the restore", f => frames += f);

            // 6. In-place restore of slot-2: the pickup lands exactly once more (from the reinstated obligation).
            SaveResult pickupRestored = b.Saves.Restore("slot-2");
            Assert.That(pickupRestored.Succeeded, Is.True, pickupRestored.ToString());
            Assert.That(b.Reattachments, Is.EqualTo(2));
            Assert.That(Held(b, HollowmereNarrative.OldCoin), Is.EqualTo(coinsBefore), "the capture was taken before the pickup applied");
            yield return Until(() => OpenGrants(b.World.Delivery.Owner.ToRecords(), pickupPort) == 0 && Held(b, HollowmereNarrative.OldCoin) == coinsBefore + 3,
                "the in-flight pickup applies after the restore", f => frames += f);
            for (int i = 0; i < 30; i++)
            {
                frames++;
                yield return null;
            }

            Assert.That(Held(b, HollowmereNarrative.OldCoin), Is.EqualTo(coinsBefore + 3), "the in-flight pickup applies exactly once");
            Assert.That(QuestSlot(b, quest, QuestIds.Status), Is.EqualTo(QuestIds.Completed));

            // 7. A replay of the in-flight rows on the restored delivery changes nothing.
            int alreadyBefore = b.World.Delivery.Owner.AlreadyAppliedCount;
            Assert.That(b.World.Delivery.Reinstate(inFlight!, out string replayDetail), Is.True, replayDetail);
            yield return Until(() => OpenGrants(b.World.Delivery.Owner.ToRecords(), pickupPort) == 0, "the replayed pickup settles", f => frames += f);
            for (int i = 0; i < 10; i++)
            {
                frames++;
                yield return null;
            }

            Assert.That(Held(b, HollowmereNarrative.OldCoin), Is.EqualTo(coinsBefore + 3), "a replayed outbox after a restore picks up nothing twice");
            Assert.That(b.World.Delivery.Owner.AlreadyAppliedCount - alreadyBefore, Is.GreaterThanOrEqualTo(1), "the replayed pickup answered AlreadyApplied");
            Timing("pickup capture, restore and replay", phase.ElapsedMilliseconds, frames);

            GameApplicationRoot root = b.World.Root;
            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;
            for (int i = 0; i < 20; i++)
            {
                yield return null;
            }

            Assert.That(root.PumpCounter.SanctionedPumps - startPumps, Is.EqualTo(Time.frameCount - startFrame).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);
            Debug.Log("[P1.7a] W-PERSIST-01 total=" + total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms obligations="
                + b.World.Delivery.Described + " settled=" + b.World.Delivery.Settled + " claims=" + b.World.Delivery.Claims
                + " claimRefusals=" + b.World.Delivery.ClaimRefusals + " | " + string.Join(" | ", timings));
#else
            Assert.Ignore("HollowmereSaveRestore loads baked assets by path and runs in the Editor only");
            yield break;
#endif
        }

        // ------------------------------------------------------------------ W-PERSIST-02 / 03

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator CosmeticEditRestores_StructuralEditRefusesWithRecipeRevisionMismatch()
        {
#if UNITY_EDITOR
            if (!LoadContent())
            {
                yield break;
            }

            var total = Stopwatch.StartNew();
            Game a = Boot(manifest!);
            yield return null;
            int frames = 0;
            int purse = 0;
            yield return StartingPurse(a, value => purse = value, f => frames += f);
            InventoryCommands inventory = a.Modules.Inventory.Commands!;
            Assert.That(inventory.Grant(HollowmereNarrative.OldCoin, 2).Admitted, Is.True);
            yield return Until(() => Held(a, HollowmereNarrative.OldCoin) == purse + 2, "two coins", f => frames += f);
            SaveResult saved = a.Saves.Capture("slot-1");
            Assert.That(saved.Succeeded, Is.True, saved.ToString());
            a.Close();

            // Pick a definition the world actually instantiates (its recipe is on a captured target).
            int edited = UsedDefinitionIndex(manifest!);
            Assert.That(edited, Is.GreaterThanOrEqualTo(0), "the world instantiates at least one baked definition");

            // W-PERSIST-02: a cosmetic edit keeps the structural stamp (tint, tuning: the content stamp moves only).
            RegionManifest cosmetic = Object.Instantiate(manifest!);
            ManifestDefinition cosmeticDefinition = cosmetic.Definitions[edited];
            if (cosmeticDefinition.structuralStamp.Length != 64)
            {
                // A manifest baked before P1.7a: its revision came from the content stamp, which a structural-only bake keeps.
                cosmeticDefinition.structuralStamp = cosmeticDefinition.contentStamp;
            }

            ulong revision = manifest!.Definitions[edited].Revision;
            cosmeticDefinition.contentStamp = Flip(cosmeticDefinition.contentStamp);
            Assert.That(cosmeticDefinition.Revision, Is.EqualTo(revision), "a cosmetic edit keeps the recipe revision");
            Game cosmeticGame = Boot(cosmetic);
            yield return null;
            SaveResult cosmeticRestore = cosmeticGame.Saves.Restore("slot-1");
            Assert.That(cosmeticRestore.Succeeded, Is.True, cosmeticRestore.ToString());
            Assert.That(Held(cosmeticGame, HollowmereNarrative.OldCoin), Is.EqualTo(purse + 2));
            cosmeticGame.Close();
            Object.DestroyImmediate(cosmetic);

            // W-PERSIST-03: a structural edit moves the structural stamp: the restore refuses, naming the recipe.
            RegionManifest structural = Object.Instantiate(manifest!);
            ManifestDefinition structuralDefinition = structural.Definitions[edited];
            structuralDefinition.structuralStamp = Flip(structuralDefinition.structuralStamp.Length == 64 ? structuralDefinition.structuralStamp : structuralDefinition.contentStamp);
            Assert.That(structuralDefinition.Revision, Is.Not.EqualTo(revision));
            Game structuralGame = Boot(structural);
            yield return null;
            SaveResult structuralRestore = structuralGame.Saves.Restore("slot-1");
            Assert.That(structuralRestore.Succeeded, Is.False, "a structural edit refuses the old save");
            Assert.That(structuralRestore.Refusal!.Code, Is.EqualTo(SaveRefusalCode.CatalogMismatch), structuralRestore.ToString());
            Assert.That(structuralRestore.Build, Is.Not.Null);
            Assert.That(structuralRestore.Build!.Refusal, Is.EqualTo(ProductionRestoreRefusal.RecipeRevisionMismatch), structuralRestore.ToString());
            Assert.That(structuralGame.Saves.ActiveRoot, Is.SameAs(structuralGame.World.Root), "a refused restore leaves the running world");
            structuralGame.Close();
            Object.DestroyImmediate(structural);
            Debug.Log("[P1.7a] W-PERSIST-02/03 total=" + total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms refusal="
                + structuralRestore.Refusal.CodeId + " " + structuralRestore.Refusal.Detail);
#else
            Assert.Ignore("runs in the Editor only");
            yield break;
#endif
        }

        // ------------------------------------------------------------------ W-PLUG-08

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TwoHundredGrants_GetDistinctRequestIds_AndGrantsAfterAReloadStillApply()
        {
#if UNITY_EDITOR
            if (!LoadContent())
            {
                yield break;
            }

            var total = Stopwatch.StartNew();
            Game g = Boot(manifest!);
            yield return null;
            int frames = 0;
            int purse = 0;
            yield return StartingPurse(g, value => purse = value, f => frames += f);
            int submitted = 0;
            while (submitted < 200 && frames < MaxFrames)
            {
                InventoryCommands commands = g.Modules.Inventory.Commands!;
                for (int i = 0; i < 8 && submitted < 200; i++)
                {
                    if (commands.Grant(HollowmereNarrative.OldCoin, 1).Admitted)
                    {
                        submitted++;
                    }
                }

                frames++;
                yield return null;
            }

            Assert.That(submitted, Is.EqualTo(200));
            yield return Until(() => Held(g, HollowmereNarrative.OldCoin) == purse + 200, "200 single-coin grants, none deduplicated", f => frames += f);
            Assert.That(g.Modules.Inventory.Refused, Is.Zero, "no grant was refused as already applied");

            SaveResult saved = g.Saves.Capture("slot-1");
            Assert.That(saved.Succeeded, Is.True, saved.ToString());
            SaveResult restored = g.Saves.Restore("slot-1");
            Assert.That(restored.Succeeded, Is.True, restored.ToString());
            yield return null;
            for (int i = 0; i < 8; i++)
            {
                Assert.That(g.Modules.Inventory.Commands!.Grant(HollowmereNarrative.OldCoin, 1).Admitted, Is.True);
            }

            yield return Until(() => Held(g, HollowmereNarrative.OldCoin) == purse + 208, "grants after the reload apply (fresh ids, not the ring's)", f => frames += f);
            Debug.Log("[P1.7a] W-PLUG-08 frames=" + frames.ToString(CultureInfo.InvariantCulture) + " total=" + total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
#else
            Assert.Ignore("runs in the Editor only");
            yield break;
#endif
        }

        // ------------------------------------------------------------------ determinism

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator SameFrameIndexedCommands_GiveIdenticalSlots_AcrossTwoBoots()
        {
#if UNITY_EDITOR
            if (!LoadContent())
            {
                yield break;
            }

            var total = Stopwatch.StartNew();
            var hashes = new List<string>();
            var steps = new List<ulong>();
            for (int run = 0; run < 2; run++)
            {
                Game g = Boot(manifest!);
                yield return null;
                TargetId traveller = g.World.World.Focus;
                for (int frame = 0; frame < 150; frame++)
                {
                    switch (frame)
                    {
                        case 2:
                            g.Modules.Inventory.Commands!.Grant(HollowmereNarrative.OldCoin, 3);
                            break;
                        case 4:
                            g.World.Conversations.TryStart(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef);
                            break;
                        case 20:
                        case 40:
                            g.Modules.Dialogue.Runner!.Advance();
                            break;
                        case 30:
                            g.Modules.Dialogue.Runner!.Choose(0);
                            break;
                        case 60:
                            g.World.World.Commands.Travel(traveller, MarshId);
                            break;
                        case 80:
                            g.Modules.Inventory.Commands!.Buy(HollowmereNarrative.Vendor, HollowmereNarrative.GateKey, 1);
                            break;
                    }

                    yield return null;
                }

                SaveRoundTripReport report = g.Saves.TestRoundTrip();
                Assert.That(report.Refusal, Is.Null, report.ToString());
                hashes.Add(report.SourceSlotHash);
                steps.Add(report.SourceStep);
                g.Close();
            }

            Assert.That(hashes[1], Is.EqualTo(hashes[0]), "identical slots after the same commands (steps " + steps[0] + " / " + steps[1] + ")");
            Debug.Log("[P1.7a] determinism hash=" + hashes[0] + " steps=" + steps[0] + "/" + steps[1] + " total=" + total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms");
#else
            Assert.Ignore("runs in the Editor only");
            yield break;
#endif
        }

        // ------------------------------------------------------------------ portal condition (coordinator addition to A4)

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator APortalCondition_RefusesTravelWithAStableCode_UntilItHolds()
        {
#if UNITY_EDITOR
            if (!LoadContent())
            {
                yield break;
            }

            // Every portal of a copy of the manifest needs narrative.fact.heard_rumour (P1.7b's PortalDefinition field lands
            // in the baked ManifestPortal.conditionRef); Maren's conversation in the village satisfies it.
            RegionManifest gated = Object.Instantiate(manifest!);
            for (int i = 0; i < gated.Portals.Count; i++)
            {
                gated.Portals[i].conditionRef = "narrative.fact.heard_rumour";
            }

            Game g = Boot(gated);
            yield return null;
            int frames = 0;
            TargetId traveller = g.World.World.Focus;
            int refusals = g.World.World.Worlds.Refused;
            Assert.That(g.World.World.Commands.Travel(traveller, MarshId).Admitted, Is.True);
            yield return Until(() => g.World.World.Worlds.Refused > refusals, "the gated travel is refused", f => frames += f);
            Assert.That(g.World.World.Worlds.LastRefusalCode, Is.EqualTo(WorldRefusalCodes.TravelConditionFailed));
            IReadOnlyList<ExplainRecord> explained = g.World.World.Worlds.RecentRefusals(1);
            Assert.That(explained.Count, Is.EqualTo(1));
            StringAssert.Contains("narrative.fact.heard_rumour", explained[0].FailedCondition, "the explain record names the condition");
            Assert.That(RegionOf(g, traveller), Is.EqualTo(AuthoringIds.StableKey(VillageId)), "the traveller stays home");

            yield return Converse(g, HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef, new[] { 2 }, "Maren's rumour", f => frames += f);
            yield return Until(() => Fact(g, "heard_rumour") == 1, "heard_rumour", f => frames += f);
            Assert.That(g.World.World.Commands.Travel(traveller, MarshId).Admitted, Is.True);
            yield return Until(() => RegionOf(g, traveller) == AuthoringIds.StableKey(MarshId), "the travel passes once the condition holds", f => frames += f);
            Debug.Log("[P1.7a] portal condition frames=" + frames.ToString(CultureInfo.InvariantCulture) + " refusal=" + explained[0]);
            g.Close();
            Object.DestroyImmediate(gated);
#else
            Assert.Ignore("runs in the Editor only");
            yield break;
#endif
        }

        // ------------------------------------------------------------------ boot and re-attach

        /// <summary>One booted game: the narrative world with P1.3's NPC and interaction extensions and its save service.</summary>
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
            var options = new SaveServiceOptions("hollowmere.p1_7a", codecs!) { Directory = saveDirectory, RegionId = () => string.Empty };
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

        private IEnumerator PlayToTheClapper(Game g, System.Action<int> count)
        {
            NarrativeWorld w = g.World;
            int questKey = Key(g, HollowmereNarrative.Quest);
            TargetId quest = w.Runtime.Index.TargetOf(NarrativeTargetKind.Quest, questKey);
            TargetId traveller = w.World.Focus;
            yield return Converse(g, HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef, new[] { 2 }, "Maren's rumour", count);
            yield return Until(() => QuestSlot(g, quest, QuestIds.Stage) == 1, "quest stage 1", count);
            InventoryCommands inventory = g.Modules.Inventory.Commands!;
            Assert.That(inventory.Grant(HollowmereNarrative.Lantern, 1).Admitted, Is.True);
            yield return Until(() => Held(g, HollowmereNarrative.Lantern) == 1 && QuestSlot(g, quest, QuestIds.Stage) == 2, "a lantern and quest stage 2", count);

            Assert.That(w.World.Commands.Travel(traveller, MarshId).Admitted, Is.True);
            yield return Until(() => RegionOf(g, traveller) == AuthoringIds.StableKey(MarshId), "arrival in the marsh", count);
            yield return Converse(g, HollowmereNarrative.HaleId, HollowmereNarrative.HaleGraphRef, System.Array.Empty<int>(), "Hale opens the gate", count);
            yield return Until(() => Fact(g, "gate_open") == 1, "gate_open", count);
            TargetId gate = AuthoringIds.TargetIdFor(HollowmereNarrative.GateId);
            yield return StandBy(g, gate, count);
            var interact = new InteractionCommands(w.World);
            Assert.That(interact.Use(traveller, w.Runtime.ActorKey, gate).Admitted, Is.True);
            yield return Until(() => g.World.World.Slots.ReadOrDefault(gate, InteractionSlots.Owner, InteractionSlots.State, InteractableStates.Locked) != InteractableStates.Locked,
                "the gate unlocks", count);
            Assert.That(g.Modules.Inventory.WorldItems!.TryPickup(HollowmereNarrative.ClapperWorldItem), Is.True);
            yield return Until(() => Held(g, HollowmereNarrative.BellClapper) == 1, "the bell clapper", count);
        }

        /// <summary>Talks a conversation through: advances lines, picks the planned choices in order, until it ends.</summary>
        private IEnumerator Converse(Game g, string npcId, string graphRef, int[] choices, string what, System.Action<int> count)
        {
            ConversationStart started = g.World.Conversations.TryStart(npcId, graphRef);
            Assert.That(started.Started, Is.True, what + ": " + started.Detail);
            DialogueRunner talk = g.Modules.Dialogue.Runner!;
            var queue = new Queue<int>(choices);
            for (int steps = 0; steps < 40; steps++)
            {
                yield return Until(() => !Presented(g).Active || Presented(g).CanAdvance || Presented(g).Kind == "choice", what + ": a line or a choice", count);
                DialogueViewModel shown = Presented(g);
                if (!shown.Active)
                {
                    break;
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

                yield return Until(() => !Presented(g).Active || Presented(g).Node != node, what + ": the next node", count);
            }

            Assert.That(Presented(g).Active, Is.False, what + ": the conversation ended");
        }

        /// <summary>The player's starting purse of old coins once the boot settled (stable for ten frames).</summary>
        private IEnumerator StartingPurse(Game g, System.Action<int> purse, System.Action<int> count)
        {
            int value = Held(g, HollowmereNarrative.OldCoin);
            int stable = 0;
            int frames = 0;
            while (stable < 10 && frames < MaxFrames)
            {
                frames++;
                yield return null;
                int now = Held(g, HollowmereNarrative.OldCoin);
                stable = now == value ? stable + 1 : 0;
                value = now;
            }

            count(frames);
            purse(value);
        }

        /// <summary>The authoring id of the placed entity named <paramref name="name"/> in the baked manifest.</summary>
        private string EntityId(string name)
        {
            foreach (ManifestEntity entity in manifest!.Entities)
            {
                if (entity.name == name)
                {
                    return entity.authoringId;
                }
            }

            Assert.Fail("no placed entity named " + name);
            return string.Empty;
        }

        // ------------------------------------------------------------------ helpers

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

        private IEnumerator StandBy(Game g, TargetId interactable, System.Action<int> count)
        {
            GameplayWorld world = g.World.World;
            TargetId traveller = world.Focus;
            int x = world.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
            int y = world.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int z = world.Slots.ReadOrDefault(interactable, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0) - 1000;
            Assert.That(world.Commands.Place(traveller, x, y, z, 0).Admitted, Is.True);
            yield return Until(() => g.World.World.Slots.ReadOrDefault(traveller, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue) == z,
                "the traveller stands by the interactable", count);
        }

        private static DialogueViewModel Presented(Game g) => g.Modules.Dialogue.Presenter!.Last;

        private static int Key(Game g, string reference)
        {
            Assert.That(g.World.Runtime.Models.TryResolve(reference, out int key), Is.True, reference);
            return key;
        }

        private static int Fact(Game g, string name) =>
            g.World.Runtime.Models.TryGetFactByName(name, out FactModel? fact) && fact != null ? g.World.Runtime.State.Fact(fact.Key) : int.MinValue;

        private static int Held(Game g, string item) => g.World.Runtime.State.ItemCount(0, Key(g, item), g.World.Runtime.ActorKey);

        private static int QuestSlot(Game g, TargetId quest, SlotId slot) => g.World.World.Slots.ReadOrDefault(quest, QuestIds.Owner, slot, int.MinValue);

        private static int RegionOf(Game g, TargetId target) => g.World.World.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.Region, 0);

        private static int Pos(Game g, TargetId target, SlotId slot) => g.World.World.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, slot, int.MinValue);

        private static int RuleFired(Game g, string rule)
        {
            int key = Key(g, rule);
            TargetId target = g.World.Runtime.Index.TargetOf(NarrativeTargetKind.Rule, key);
            return g.World.World.Slots.ReadOrDefault(target, LogicIds.Owner, LogicIds.Fired, int.MinValue);
        }

        private static int[] NpcSlots(Game g, TargetId npc)
        {
            ICommittedSlotReader slots = g.World.World.Slots;
            return new[]
            {
                slots.ReadOrDefault(npc, GameCore.Gameplay.Contracts.NpcSlots.Owner, GameCore.Gameplay.Contracts.NpcSlots.State, int.MinValue),
                slots.ReadOrDefault(npc, GameCore.Gameplay.Contracts.NpcSlots.Owner, GameCore.Gameplay.Contracts.NpcSlots.PatrolIndex, int.MinValue),
                slots.ReadOrDefault(npc, GameplaySlots.WorldOwner, GameplaySlots.PosX, int.MinValue),
                slots.ReadOrDefault(npc, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue),
                slots.ReadOrDefault(npc, GameplaySlots.WorldOwner, GameplaySlots.Region, int.MinValue),
            };
        }

        private static Dictionary<TargetId, int> Occupants(Game g)
        {
            var occupants = new Dictionary<TargetId, int>();
            InteractionModule? module = g.Interactions.Module;
            if (module == null)
            {
                return occupants;
            }

            IReadOnlyList<InteractableRecord> records = module.Records;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsTrigger)
                {
                    occupants[records[i].Target] = g.World.World.Slots.ReadOrDefault(records[i].Target, InteractionSlots.Owner, InteractionSlots.Occupants, 0);
                }
            }

            return occupants;
        }

        private static int OpenGrants(IReadOnlyList<OutboxRecordValue> rows, Id128 port)
        {
            int open = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                OutboxRecordValue row = rows[i];
                bool live = row.RowKind == (uint)OutboxRowKind.Obligation
                    && (row.DeliveryState == (uint)OutboxDeliveryState.Pending || row.DeliveryState == (uint)OutboxDeliveryState.Delivered);
                if (live && row.DestinationHigh == port.High && row.DestinationLow == port.Low)
                {
                    open++;
                }
            }

            return open;
        }

        /// <summary>The index of a definition some authored entity of the world uses.</summary>
        private static int UsedDefinitionIndex(RegionManifest world)
        {
            var used = new HashSet<string>();
            for (int i = 0; i < world.Entities.Count; i++)
            {
                used.Add(world.Entities[i].definitionId);
            }

            for (int i = 0; i < world.Definitions.Count; i++)
            {
                if (used.Contains(world.Definitions[i].authoringId))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>A 64-hex stamp with its first character changed (a different revision).</summary>
        private static string Flip(string stamp)
        {
            string text = stamp.Length == 64 ? stamp : new string('0', 64);
            char first = text[0] == 'a' ? 'b' : 'a';
            return first + text.Substring(1);
        }

        private void Timing(string phase, long ms, int frames) =>
            timings.Add(phase + " " + ms.ToString(CultureInfo.InvariantCulture) + "ms/" + frames.ToString(CultureInfo.InvariantCulture) + "f");
    }
}
