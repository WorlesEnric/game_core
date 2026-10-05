// Hollowmere P1.3 EditMode - the player, npc and interaction plugins on the kernel, without scenes: rules parity (the
// committed slots equal the pure rules applied to the same commands), the talk path, interactions and the P1.2 seam
// (an attach without seeding leaves the slots alone).
//
// The world is the baked Hollowmere manifest booted through GameplayBoot with the three world extensions, a test-driven
// frame clock and an ImmediateSceneLoader; frames go through the one sanctioned application pump.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Interaction;
using GameCore.Rules.Gameplay.Npc;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using Hollowmere.GameplayAuthoring;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_3.EditMode.Tests
{
    /// <summary>A scripted intent source: queued intents one per frame, then the held intent.</summary>
    internal sealed class QueuedIntents : IPlayerIntentSource
    {
        private readonly Queue<PlayerIntent> queue = new Queue<PlayerIntent>();

        /// <summary>The intent sampled while the queue is empty.</summary>
        public PlayerIntent Held { get; set; }

        public void Enqueue(PlayerIntent intent, int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                queue.Enqueue(intent);
            }
        }

        public void Clear() => queue.Clear();

        public PlayerIntent Sample() => queue.Count > 0 ? queue.Dequeue() : Held;
    }

    /// <summary>One booted Hollowmere world with the P1.3 extensions and sessions.</summary>
    internal sealed class P13World : IDisposable
    {
        private long frame = 90000L;

        public GameplayWorld World { get; private set; } = null!;

        public PlayerWorldExtension Player { get; private set; } = null!;

        public NpcWorldExtension Npcs { get; private set; } = null!;

        public InteractionWorldExtension Interactions { get; private set; } = null!;

        public PlayerSession PlayerSession { get; private set; } = null!;

        public NpcSession NpcSession { get; private set; } = null!;

        public InteractionSession InteractionSession { get; private set; } = null!;

        public QueuedIntents Intents { get; } = new QueuedIntents();

        public ImmediateSceneLoader Loader { get; } = new ImmediateSceneLoader();

        public GameplayEventCursor Events { get; } = new GameplayEventCursor();

        public static P13World Boot()
        {
            RegionManifest manifest = P13ContentTests.RequireManifest();
            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? _, out ContentHash _, out string detail))
            {
                Assert.Ignore("the generated catalog is not compiled yet: " + detail);
            }

            PlayerDefinition? playerDefinition = AssetDatabase.LoadAssetAtPath<PlayerDefinition>(HollowmereGameplayAuthoring.PlayerDefinitionPath);
            NpcRoster? roster = AssetDatabase.LoadAssetAtPath<NpcRoster>(HollowmereGameplayAuthoring.NpcRosterPath);
            InteractionRoster? interactions = AssetDatabase.LoadAssetAtPath<InteractionRoster>(HollowmereGameplayAuthoring.InteractionRosterPath);
            if (playerDefinition == null || roster == null || interactions == null)
            {
                Assert.Ignore("the P1.3 content is not authored yet (run P13ContentTests first)");
            }

            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
            var harness = new P13World
            {
                Player = new PlayerWorldExtension(playerDefinition),
                Npcs = new NpcWorldExtension(roster),
                Interactions = new InteractionWorldExtension(interactions),
            };
            var options = new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => harness.frame,
            };
            var build = new WorldBuildOptions { Name = "HollowmereP13", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            build.Extensions.Add(harness.Player);
            build.Extensions.Add(harness.Npcs);
            build.Extensions.Add(harness.Interactions);
            harness.World = GameplayBoot.Boot(manifest, options, build, false);
            harness.World.UseSceneLoader(harness.Loader);
            harness.PlayerSession = PlayerSession.Install(harness.World, harness.Player, null, harness.Intents);
            harness.PlayerSession.Input.DeltaTime = () => 0.1f;
            harness.PlayerSession.Portals.Enabled = false;
            harness.NpcSession = NpcSession.Install(harness.World, harness.Npcs);
            harness.InteractionSession = InteractionSession.Install(harness.World, harness.Interactions, harness.Player.Player, harness.Player.PlayerKey);
            harness.PlayerSession.Focus.AddSource(harness.NpcSession.Candidates);
            harness.PlayerSession.Focus.AddSource(harness.InteractionSession.Candidates);
            Assert.That(harness.World.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            return harness;
        }

        public void Pump(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                frame++;
                GameCoreApplicationPump.PumpFrame();
            }
        }

        public int PumpUntil(Func<bool> condition, string what, int maxFrames = 200)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return i;
                }

                Pump(1);
            }

            Assert.Fail("not reached within " + maxFrames + " frames: " + what);
            return maxFrames;
        }

        public int Slot(TargetId target, OwnerId owner, SlotId slot) => World.Slots.ReadOrDefault(target, owner, slot, int.MinValue);

        public PlayerState PlayerState() =>
            new PlayerState(
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.PosX),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.PosY),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.PosZ),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.Yaw),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.Stamina),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.Focus),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.RegionKey),
                Slot(Player.Player, PlayerSlots.Owner, PlayerSlots.RegenDelayMs));

        public NpcRecord Npc(string name)
        {
            for (int i = 0; i < Npcs.Records.Count; i++)
            {
                if (Npcs.Records[i].Name == name)
                {
                    return Npcs.Records[i];
                }
            }

            Assert.Fail("no NPC " + name);
            return null!;
        }

        public InteractableRecord Interactable(string name)
        {
            for (int i = 0; i < Interactions.Records.Count; i++)
            {
                if (Interactions.Records[i].Name == name)
                {
                    return Interactions.Records[i];
                }
            }

            Assert.Fail("no interactable " + name);
            return null!;
        }

        public ulong ExecutingStep => World.Root.Host.CurrentStep.Value;

        /// <summary>Committed events since the last call, decoded, with their schema.</summary>
        public List<(SchemaRef Schema, GameplayActorEvent Event)> ReadEvents()
        {
            var raw = new List<CommittedEvent>();
            Events.ReadInto(World, raw, 1024);
            var decoded = new List<(SchemaRef, GameplayActorEvent)>();
            for (int i = 0; i < raw.Count; i++)
            {
                if (GameplayActorEvent.TryDecode(raw[i].Payload, out GameplayActorEvent e))
                {
                    decoded.Add((raw[i].Schema, e));
                }
            }

            return decoded;
        }

        public void Dispose()
        {
            PlayerSession?.Dispose();
            if (World != null)
            {
                World.Shutdown();
                if (World.Root.State != GameApplicationState.Stopped)
                {
                    World.Root.Stop("test end");
                }
            }
        }
    }

    [TestFixture]
    public sealed class P13KernelTests
    {
        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp() => pumpWasEnabled = GameCoreApplicationPump.IsEnabled;

        [TearDown]
        public void TearDown()
        {
            GameApplicationRoot? current = GameApplication.Current;
            if (current != null && current.State != GameApplicationState.Stopped)
            {
                current.Stop("test teardown");
            }

            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }
        [Test]
        public void Boot_SeedsThePlayerFromTheTravellerPlacement_AndEveryNpcAndInteractable()
        {
            using P13World w = P13World.Boot();
            RegionManifest manifest = w.World.Manifest;
            ManifestEntity traveller = manifest.FindEntity(manifest.FocusEntityId)!;
            PlayerState player = w.PlayerState();
            Assert.That(player.PosX, Is.EqualTo(traveller.x));
            Assert.That(player.PosZ, Is.EqualTo(traveller.z));
            Assert.That(player.Stamina, Is.EqualTo(w.Player.Tuning.StaminaMax));
            Assert.That(player.Focus, Is.EqualTo(PlayerRules.NoFocus));
            Assert.That(player.RegionKey, Is.EqualTo(manifest.FindRegion(traveller.regionId)!.key));
            Assert.That(w.Npcs.Records.Count, Is.EqualTo(5));
            Assert.That(w.Interactions.Records.Count, Is.EqualTo(3));
            InteractableRecord gate = w.Interactable(HollowmereGameplayAuthoring.GateName);
            Assert.That(w.Slot(gate.Target, InteractionSlots.Owner, InteractionSlots.State), Is.EqualTo(InteractableStates.Locked));
            NpcRecord pip = w.Npc("Pip");
            Assert.That(w.Slot(pip.Target, NpcSlots.Owner, NpcSlots.SchedulePhase), Is.EqualTo(-1), "a scheduled NPC enters its phase on the first update");
        }

        [Test]
        public void PlayerMoves_CommitExactlyWhatThePureRulesCompute()
        {
            using P13World w = P13World.Boot();
            w.PlayerSession.Input.Enabled = false;
            w.PlayerSession.Focus.Enabled = false;
            w.Pump(2);
            PlayerState expected = w.PlayerState();
            PlayerTuning tuning = w.Player.Tuning;
            var moves = new[]
            {
                new PlayerMove(0, 0, 250, 0, false, false),
                new PlayerMove(200, 0, 150, 927, false, false),
                new PlayerMove(0, 0, 900, 0, false, false),
                new PlayerMove(500, 0, 0, 1571, true, false),
                new PlayerMove(0, 0, 0, 1571, false, true),
                new PlayerMove(-3, 4, 0, 1571, false, false),
            };
            for (int round = 0; round < 3; round++)
            {
                for (int i = 0; i < moves.Length; i++)
                {
                    PlayerMove move = moves[i];
                    Assert.That(w.PlayerSession.Commands.Move(move.Dx, move.Dy, move.Dz, move.Yaw, move.Flags).Admitted, Is.True);
                    ulong before = w.ExecutingStep;
                    w.Pump(1);
                    Assert.That(w.ExecutingStep, Is.EqualTo(before + 1UL), "one step per frame with one command");
                    expected = PlayerRules.Move(expected, move, tuning).State;
                    PlayerState committed = w.PlayerState();
                    Assert.That(committed.ToString(), Is.EqualTo(expected.ToString()), "round " + round + " move " + i);
                }
            }

            Assert.That(w.Player.Module!.Clamped, Is.GreaterThan(0), "the oversized move was clamped");
        }

        [Test]
        public void NpcPatrol_CommitsWhatTheLogicalMoverComputes_AndRaisesNpcArrived()
        {
            using P13World w = P13World.Boot();
            RegionRecord village = w.World.Worlds.Regions[0];
            for (int i = 0; i < w.World.Worlds.Regions.Count; i++)
            {
                if (w.World.Worlds.Regions[i].Name == "Thornwick Village")
                {
                    village = w.World.Worlds.Regions[i];
                }
            }

            w.PlayerSession.Input.Enabled = false;
            w.PumpUntil(() => w.World.Streamer.IsSettled && w.World.Streamer.ResidencyOf(village.AuthoringId) == RegionResidency.Resident, "village resident");
            NpcRecord maren = w.Npc("Maren");
            w.ReadEvents();
            NpcSnapshot expected = Snapshot(w, maren);
            bool arrived = false;
            for (int frame = 0; frame < 160 && !arrived; frame++)
            {
                Assert.That(w.PlayerSession.Commands.Move(0, 0, 0, 0, 0).Admitted, Is.True, "a heartbeat command per frame");
                ulong step = w.ExecutingStep + 1UL;
                w.Pump(1);
                expected = NpcLogicalMover.Advance(expected, maren, step, w.Npcs.Module!.StepMilliseconds, true, w.Npcs.Module.UnloadedStride).After;
                Assert.That(Snapshot(w, maren).ToString(), Is.EqualTo(expected.ToString()), "frame " + frame);
                foreach ((SchemaRef schema, GameplayActorEvent e) in w.ReadEvents())
                {
                    arrived |= schema.Equals(NpcSlots.ArrivedEvent) && e.Target.Equals(maren.Target);
                }
            }

            Assert.That(arrived, Is.True, "Maren reaches her first patrol point (NpcArrived)");
            Assert.That(w.Slot(maren.Target, NpcSlots.Owner, NpcSlots.State), Is.EqualTo(NpcSlots.Patrol));
        }

        [Test]
        public void UnloadedNpcs_StillMove_AtTheStrideRate()
        {
            using P13World w = P13World.Boot();
            w.PlayerSession.Input.Enabled = false;
            NpcRecord pip = w.Npc("Pip");
            int startUpdates = w.Npcs.Module!.UnloadedUpdates;
            for (int frame = 0; frame < 48; frame++)
            {
                w.PlayerSession.Commands.Move(0, 0, 0, 0, 0);
                w.Pump(1);
            }

            Assert.That(w.Npcs.Module.UnloadedUpdates, Is.GreaterThan(startUpdates), "NPCs of unloaded regions are updated");
            Assert.That(w.Slot(pip.Target, NpcSlots.Owner, NpcSlots.SchedulePhase), Is.EqualTo(0), "Pip entered the day phase");
        }

        [Test]
        public void TalkingToAFocusedNpc_HoldsItInConverse_WithTheNullConversationStarter()
        {
            using P13World w = P13World.Boot();
            w.PlayerSession.Input.Enabled = false;
            w.PlayerSession.Focus.Enabled = false;
            var diagnostics = new List<string>();
            w.NpcSession.Conversations.Diagnostics = diagnostics.Add;
            NpcRecord odd = w.Npc("Odd");
            Assert.That(w.PlayerSession.Commands.SetFocus(odd.Key).Admitted, Is.True);
            w.Pump(1);
            Assert.That(w.PlayerState().Focus, Is.EqualTo(odd.Key));
            Assert.That(w.PlayerSession.Commands.Interact().Admitted, Is.True);
            w.PumpUntil(() => w.Slot(odd.Target, NpcSlots.Owner, NpcSlots.State) == NpcSlots.Converse, "Odd converses", 10);
            Assert.That(w.NpcSession.Conversations.Talks, Is.EqualTo(1));
            Assert.That(diagnostics.Count, Is.EqualTo(1));
            Assert.That(diagnostics[0], Does.StartWith(PlayerNpcInteractionCodes.NpcNoConversationSystem));
            Assert.That(w.Slot(odd.Target, NpcSlots.Owner, NpcSlots.TimerMs), Is.GreaterThan(0), "the null starter holds converse briefly");
        }

        [Test]
        public void UsingTheWellSucceeds_AndTheLockedGateRefusesWithItsCode()
        {
            using P13World w = P13World.Boot();
            InteractableRecord well = w.Interactable(HollowmereGameplayAuthoring.WellName);
            InteractableRecord gate = w.Interactable(HollowmereGameplayAuthoring.GateName);
            InteractionCommands commands = w.InteractionSession.Commands;
            w.ReadEvents();

            // Far away: refused out of range, nothing changes.
            Assert.That(commands.Use(w.Player.Player, w.Player.PlayerKey, well.Target).Admitted, Is.True);
            w.Pump(1);
            Assert.That(LastRefusal(w), Is.EqualTo("interaction.out-of-range"));

            // Walk to the well with the input adapter (kinematic in edit mode) until the focus picks it.
            int wellZ = w.Slot(well.Target, GameplaySlots.WorldOwner, GameplaySlots.PosZ);
            w.Intents.Held = new PlayerIntent { Move = new Vector2(0f, 1f) };
            w.PumpUntil(() => w.PlayerState().PosZ >= wellZ - 900, "the player reaches the well", 200);
            w.Intents.Held = default(PlayerIntent);
            w.PumpUntil(() => w.PlayerState().Focus == well.Key, "the focus picks the well", 20);
            Assert.That(((NullPromptPresenter)w.PlayerSession.Focus.Prompts).Current?.Text, Is.EqualTo("Examine the well"));
            w.ReadEvents();
            w.Intents.Enqueue(new PlayerIntent { Interact = true });
            bool succeeded = false;
            for (int i = 0; i < 6 && !succeeded; i++)
            {
                w.Pump(1);
                foreach ((SchemaRef schema, GameplayActorEvent e) in w.ReadEvents())
                {
                    succeeded |= schema.Equals(InteractionSlots.SucceededEvent) && e.Target.Equals(well.Target);
                }
            }

            Assert.That(succeeded, Is.True, "player.interact on the focused well commits InteractionSucceeded");
            Assert.That(w.Slot(well.Target, InteractionSlots.Owner, InteractionSlots.Uses), Is.EqualTo(1));
            w.Pump(1);
            Assert.That(w.InteractionSession.Dispatcher.Succeeded, Is.EqualTo(1), "the dispatcher reads the committed success on the next frame");

            // Travel to the marsh (arrival 2.5 m from the gate's portal), then the gate refuses: locked.
            RegionRecord? marsh = null;
            for (int i = 0; i < w.World.Worlds.Regions.Count; i++)
            {
                if (w.World.Worlds.Regions[i].Name == "Blackmere Marsh")
                {
                    marsh = w.World.Worlds.Regions[i];
                }
            }

            Assert.That(w.World.Commands.Travel(w.Player.Player, marsh!.AuthoringId).Admitted, Is.True);
            w.PumpUntil(() => w.PlayerState().RegionKey == marsh.Key, "the player adopts the marsh arrival pose", 20);
            w.ReadEvents();
            Assert.That(commands.Use(w.Player.Player, w.Player.PlayerKey, gate.Target).Admitted, Is.True);
            w.Pump(1);
            Assert.That(LastRefusal(w), Is.EqualTo("interaction.locked"));
            Assert.That(w.Slot(gate.Target, InteractionSlots.Owner, InteractionSlots.State), Is.EqualTo(InteractableStates.Locked));
            Assert.That(w.Slot(gate.Target, InteractionSlots.Owner, InteractionSlots.Uses), Is.EqualTo(0));
        }

        [Test]
        public void AnAttachWithoutSeeding_LeavesThePlayerSlotsToTheRestore()
        {
            using P13World w = P13World.Boot();
            w.PlayerSession.Input.Enabled = false;
            Assert.That(w.PlayerSession.Commands.Move(0, 0, 250, 0, 0).Admitted, Is.True);
            w.Pump(1);
            PlayerState moved = w.PlayerState();

            // Re-attaching the extension the way SaveService.RootChanged does (seedSlots: false) must not reseed.
            w.Player.Attach(w.World, false);
            Assert.That(w.PlayerState().ToString(), Is.EqualTo(moved.ToString()));
            w.Player.Attach(w.World, true);
            Assert.That(w.PlayerState().PosZ, Is.Not.EqualTo(moved.PosZ), "seeding returns to the baked placement");
        }

        private static string LastRefusal(P13World w)
        {
            string code = string.Empty;
            foreach ((SchemaRef schema, GameplayActorEvent e) in w.ReadEvents())
            {
                if (schema.Equals(InteractionSlots.RefusedEvent))
                {
                    code = InteractionSlots.RefusalCode(e.B);
                }
            }

            return code;
        }

        private static NpcSnapshot Snapshot(P13World w, NpcRecord npc) =>
            new NpcSnapshot(
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.State),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.Behaviour),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.PatrolIndex),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.Mood),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.SchedulePhase),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.TargetX),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.TargetZ),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.PosX),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.PosZ),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.Yaw),
                w.Slot(npc.Target, NpcSlots.Owner, NpcSlots.TimerMs));
    }
}
