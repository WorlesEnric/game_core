// Hollowmere P1.3 PlayMode - PlayerWalkAndInteract: the real boot scene, real region scenes, the real player loop.
//
// Boots Boot/Boot.unity, replaces the Input System source with a synthetic one (frame time pinned to 0.1 s), and:
//   1. drives the same scripted inputs for N frames in two fresh boots and requires identical player slots per frame
//      (the CharacterController resolves each move from the committed pose, so the slots repeat exactly);
//   2. walks to the village well with the real locomotion until the focus picks it, presses Interact, and requires a
//      committed InteractionSucceeded (and the well's prompt on the presenter);
//   3. sees Maren patrol and arrive (a committed NpcArrived);
//   4. runs to the village->marsh portal; the portal probe travels the player (committed world.travel, adopted pose);
//   5. uses the locked causeway gate and requires InteractionRefused with interaction.locked;
//   6. requires one sanctioned pump per frame and no pump violation.
// Timings are logged as B-FRAME lines (informational; batchmode -nographics has no vsync or rendering, so frame times
// are not representative of a player).
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.P1_3.PlayMode.Tests
{
    /// <summary>A synthetic intent source: a scripted list first, then a walker steering toward a target.</summary>
    internal sealed class SyntheticIntents : IPlayerIntentSource
    {
        private readonly List<PlayerIntent> script = new List<PlayerIntent>();
        private int next;

        public GameplayWorld? World { get; set; }

        public TargetId Player { get; set; }

        /// <summary>World-space target (m) of the walker, or null to stand still once the script ran out.</summary>
        public Vector3? Target { get; set; }

        public bool Run { get; set; }

        public bool InteractOnce { get; set; }

        public float ArriveDistance { get; set; } = 0.3f;

        public void Script(PlayerIntent intent, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                script.Add(intent);
            }
        }

        public bool ScriptDone => next >= script.Count;

        public PlayerIntent Sample()
        {
            if (next < script.Count)
            {
                return script[next++];
            }

            var intent = new PlayerIntent();
            if (InteractOnce)
            {
                intent.Interact = true;
                InteractOnce = false;
            }

            if (Target == null || World == null)
            {
                return intent;
            }

            int x = World.Slots.ReadOrDefault(Player, PlayerSlots.Owner, PlayerSlots.PosX, 0);
            int z = World.Slots.ReadOrDefault(Player, PlayerSlots.Owner, PlayerSlots.PosZ, 0);
            int yaw = World.Slots.ReadOrDefault(Player, PlayerSlots.Owner, PlayerSlots.Yaw, 0);
            Vector3 target = Target.Value;
            var toTarget = new Vector2(target.x - x / 1000f, target.z - z / 1000f);
            if (toTarget.magnitude <= ArriveDistance)
            {
                return intent;
            }

            // The adapter moves relative to the player's heading when there is no camera (headless).
            float heading = yaw / 1000f;
            var forward = new Vector2(Mathf.Sin(heading), Mathf.Cos(heading));
            var right = new Vector2(Mathf.Cos(heading), -Mathf.Sin(heading));
            Vector2 direction = toTarget.normalized * Mathf.Min(1f, toTarget.magnitude / 0.5f + 0.2f);
            intent.Move = new Vector2(Vector2.Dot(direction, right), Vector2.Dot(direction, forward));
            intent.Run = Run;
            return intent;
        }
    }

    public sealed class PlayerWalkAndInteract
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const int ScriptFrames = 90;

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator WalksInteractsTalksPastNpcsAndTravels()
        {
#if UNITY_EDITOR
            // 1. Same inputs, same slots: two fresh boots.
            var firstRun = new List<string>();
            yield return BootAndRun(firstRun, null);
            var secondRun = new List<string>();
            var session = new Session();
            yield return BootAndRun(secondRun, session);
            Assert.That(secondRun.Count, Is.EqualTo(ScriptFrames));
            for (int i = 0; i < ScriptFrames; i++)
            {
                Assert.That(secondRun[i], Is.EqualTo(firstRun[i]), "frame " + i + " of the scripted run");
            }

            Assert.That(firstRun[ScriptFrames - 1], Is.Not.EqualTo(firstRun[0]), "the scripted run moved the player");

            GameBoot boot = session.Boot!;
            GameplayWorld world = boot.World!;
            GameApplicationRoot root = world.Root;
            SyntheticIntents intents = session.Intents!;
            PlayerWorldExtension player = boot.PlayerExtension!;
            var cursor = new GameplayEventCursor();
            var events = new List<CommittedEvent>();
            cursor.ReadInto(world, events, 4096);
            events.Clear();
            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;
            var clock = Stopwatch.StartNew();

            // 2. Walk to the well; the focus picks it; Interact commits InteractionSucceeded.
            InteractableRecord well = Interactable(boot, "Village Well");
            InteractableRecord gate = Interactable(boot, "Causeway Gate");
            NpcRecord maren = Npc(boot, "Maren");
            bool marenArrived = false;
            Vector3 wellPoint = Position(world, well.Target);
            intents.Target = new Vector3(wellPoint.x, 0f, wellPoint.z - 0.9f);
            int frames = 0;
            while (Slot(world, player.Player, PlayerSlots.Focus) != well.Key && frames++ < 600)
            {
                yield return null;
                marenArrived |= Saw(world, cursor, events, NpcSlots.ArrivedEvent, maren.Target);
            }

            Assert.That(Slot(world, player.Player, PlayerSlots.Focus), Is.EqualTo(well.Key), "the focus picks the well");
            Log("walk-to-well", clock.ElapsedMilliseconds, frames);
            yield return null;
            Assert.That(((NullPromptPresenter)boot.Player!.Focus.Prompts).Current?.Text, Is.EqualTo("Examine the well"));
            intents.Target = null;
            intents.InteractOnce = true;
            bool succeeded = false;
            for (frames = 0; frames < 10 && !succeeded; frames++)
            {
                yield return null;
                succeeded = Saw(world, cursor, events, InteractionSlots.SucceededEvent, well.Target, maren.Target, out bool arrivedNow);
                marenArrived |= arrivedNow;
            }

            Assert.That(succeeded, Is.True, "interact on the focused well commits InteractionSucceeded");
            Assert.That(Slot(world, well.Target, InteractionSlots.Uses, InteractionSlots.Owner), Is.EqualTo(1));

            // 3. Maren patrols and arrives.
            for (frames = 0; frames < 400 && !marenArrived; frames++)
            {
                yield return null;
                marenArrived |= Saw(world, cursor, events, NpcSlots.ArrivedEvent, maren.Target);
            }

            Assert.That(marenArrived, Is.True, "Maren reaches a patrol point (NpcArrived)");

            // 4. Run to the village->marsh portal with the real locomotion; the probe travels the player.
            RegionRecord marsh = Region(world, "Blackmere Marsh");
            RegionPortal? portal = null;
            foreach (RegionPortal candidate in Object.FindObjectsByType<RegionPortal>(FindObjectsSortMode.None))
            {
                if (candidate.Portal != null && candidate.gameObject.scene.path.Contains("ThornwickVillage")
                    && (Is(candidate.Portal.RegionA, marsh.AuthoringId) || Is(candidate.Portal.RegionB, marsh.AuthoringId)))
                {
                    portal = candidate;
                }
            }

            Assert.That(portal, Is.Not.Null, "the village has a portal to the marsh");
            clock.Restart();
            intents.Run = true;
            intents.ArriveDistance = 0.05f;
            intents.Target = portal!.transform.position;
            for (frames = 0; frames < 900 && Slot(world, player.Player, PlayerSlots.RegionKey) != marsh.Key; frames++)
            {
                yield return null;
            }

            Log("run-to-portal-and-travel", clock.ElapsedMilliseconds, frames);
            Assert.That(Slot(world, player.Player, PlayerSlots.RegionKey), Is.EqualTo(marsh.Key), "the player travelled to the marsh");
            Assert.That(boot.Player.Portals.Requested, Is.GreaterThanOrEqualTo(1));
            Assert.That(boot.Player.Locomotion.Resolutions, Is.GreaterThan(0), "moves were resolved by the CharacterController");
            intents.Target = null;
            intents.Run = false;
            for (frames = 0; frames < 600 && !(world.Streamer.IsSettled && world.Streamer.ResidencyOf(marsh.AuthoringId) == RegionResidency.Resident); frames++)
            {
                yield return null;
            }

            // 5. The locked gate refuses with its code.
            events.Clear();
            cursor.ReadInto(world, events, 4096);
            events.Clear();
            Assert.That(boot.Interactions!.Commands.Use(player.Player, player.PlayerKey, gate.Target).Admitted, Is.True);
            string refusal = string.Empty;
            for (frames = 0; frames < 10 && refusal.Length == 0; frames++)
            {
                yield return null;
                events.Clear();
                cursor.ReadInto(world, events, 4096);
                foreach (CommittedEvent committed in events)
                {
                    if (committed.Schema.Equals(InteractionSlots.RefusedEvent) && GameplayActorEvent.TryDecode(committed.Payload, out GameplayActorEvent e)
                        && e.Target.Equals(gate.Target))
                    {
                        refusal = InteractionSlots.RefusalCode(e.B);
                    }
                }
            }

            Assert.That(refusal, Is.EqualTo("interaction.locked"), "the gate is locked by narrative.fact.gate_open");

            // 6. One pump per frame.
            int elapsedFrames = Time.frameCount - startFrame;
            int pumps = root.PumpCounter.SanctionedPumps - startPumps;
            UnityEngine.Debug.Log("[B-FRAME] P1.3 loop frames=" + elapsedFrames.ToString(CultureInfo.InvariantCulture)
                + " sanctionedPumps=" + pumps.ToString(CultureInfo.InvariantCulture) + " resolutions=" + boot.Player.Locomotion.Resolutions
                + " ungrounded=" + boot.Player.Locomotion.UngroundedResolutions + " " + root.PumpCounter);
            Assert.That(pumps, Is.EqualTo(elapsedFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);
            Assert.That(world.Streamer.LoadFailures, Is.EqualTo(0));

            Object.Destroy(boot.gameObject);
            yield return null;
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Stopped));
#else
            Assert.Ignore("PlayerWalkAndInteract loads scenes by path and runs in the Editor only");
            yield break;
#endif
        }

        private sealed class Session
        {
            public GameBoot? Boot;

            public SyntheticIntents? Intents;
        }

        /// <summary>Boots Boot.unity, runs the scripted inputs, records the player slots per frame; keeps the boot when asked.</summary>
        private static IEnumerator BootAndRun(List<string> trace, Session? keep)
        {
#if UNITY_EDITOR
            var bootClock = Stopwatch.StartNew();
            AsyncOperation? load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BootScene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null, "Boot.unity must exist");
            while (!load!.isDone)
            {
                yield return null;
            }

            GameBoot? boot = Object.FindAnyObjectByType<GameBoot>();
            Assert.That(boot, Is.Not.Null);
            int waited = 0;
            while (boot!.World == null && boot.Failure.Length == 0 && waited++ < 300)
            {
                yield return null;
            }

            Assert.That(boot.Failure, Is.Empty);
            GameplayWorld world = boot.World!;
            PlayerSession player = boot.Player!;
            var intents = new SyntheticIntents { World = world, Player = boot.PlayerExtension!.Player };
            player.Input.Source = intents;
            player.Input.DeltaTime = () => 0.1f;
            player.Input.CameraYaw = null;
            player.Input.Enabled = false;
            RegionRecord village = Region(world, "Thornwick Village");
            int frames = 0;
            while (!(world.Streamer.IsSettled && world.Streamer.ResidencyOf(village.AuthoringId) == RegionResidency.Resident) && frames++ < 1200)
            {
                yield return null;
            }

            Log("boot", bootClock.ElapsedMilliseconds, frames);
            intents.Script(new PlayerIntent { Move = new Vector2(0f, 1f) }, 30);
            intents.Script(new PlayerIntent { Move = new Vector2(0.6f, 0.8f), Run = true }, 20);
            intents.Script(new PlayerIntent { Move = new Vector2(0f, 1f), Jump = true }, 1);
            intents.Script(new PlayerIntent { Move = new Vector2(-1f, 0f) }, 19);
            intents.Script(new PlayerIntent(), 20);
            player.Input.Enabled = true;
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < ScriptFrames; i++)
            {
                yield return null;
                trace.Add(PlayerTrace(world, boot.PlayerExtension.Player));
            }

            Log("scripted-" + ScriptFrames, clock.ElapsedMilliseconds, ScriptFrames);
            if (keep != null)
            {
                keep.Boot = boot;
                keep.Intents = intents;
            }
            else
            {
                Object.Destroy(boot.gameObject);
                yield return null;
            }
#else
            yield break;
#endif
        }

        private static bool Is(RegionDefinition? region, string authoringId) => region != null && region.AuthoringId == authoringId;

        private static string PlayerTrace(GameplayWorld world, TargetId player) =>
            Slot(world, player, PlayerSlots.PosX) + "," + Slot(world, player, PlayerSlots.PosY) + "," + Slot(world, player, PlayerSlots.PosZ) + ","
            + Slot(world, player, PlayerSlots.Yaw) + "," + Slot(world, player, PlayerSlots.Stamina) + "," + Slot(world, player, PlayerSlots.RegenDelayMs);

        private static bool Saw(GameplayWorld world, GameplayEventCursor cursor, List<CommittedEvent> events, SchemaRef schema, TargetId target) =>
            Saw(world, cursor, events, schema, target, default(TargetId), out bool _);

        /// <summary>Reads new events; true when one matches; <paramref name="arrived"/> reports NpcArrived of <paramref name="arriving"/>.</summary>
        private static bool Saw(GameplayWorld world, GameplayEventCursor cursor, List<CommittedEvent> events, SchemaRef schema, TargetId target, TargetId arriving, out bool arrived)
        {
            events.Clear();
            cursor.ReadInto(world, events, 4096);
            bool seen = false;
            arrived = false;
            foreach (CommittedEvent committed in events)
            {
                if (!GameplayActorEvent.TryDecode(committed.Payload, out GameplayActorEvent e))
                {
                    continue;
                }

                seen |= committed.Schema.Equals(schema) && e.Target.Equals(target);
                arrived |= committed.Schema.Equals(NpcSlots.ArrivedEvent) && e.Target.Equals(arriving);
            }

            return seen;
        }

        private static int Slot(GameplayWorld world, TargetId target, SlotId slot) =>
            world.Slots.ReadOrDefault(target, PlayerSlots.Owner, slot, int.MinValue);

        private static int Slot(GameplayWorld world, TargetId target, SlotId slot, OwnerId owner) => world.Slots.ReadOrDefault(target, owner, slot, int.MinValue);

        private static Vector3 Position(GameplayWorld world, TargetId target) =>
            new Vector3(
                Slot(world, target, GameplaySlots.PosX, GameplaySlots.WorldOwner) / 1000f,
                Slot(world, target, GameplaySlots.PosY, GameplaySlots.WorldOwner) / 1000f,
                Slot(world, target, GameplaySlots.PosZ, GameplaySlots.WorldOwner) / 1000f);

        private static RegionRecord Region(GameplayWorld world, string name)
        {
            for (int i = 0; i < world.Worlds.Regions.Count; i++)
            {
                if (world.Worlds.Regions[i].Name == name)
                {
                    return world.Worlds.Regions[i];
                }
            }

            Assert.Fail("no region " + name);
            return null!;
        }

        private static InteractableRecord Interactable(GameBoot boot, string name)
        {
            foreach (InteractableRecord record in boot.InteractionExtension!.Records)
            {
                if (record.Name == name)
                {
                    return record;
                }
            }

            Assert.Fail("no interactable " + name);
            return null!;
        }

        private static NpcRecord Npc(GameBoot boot, string name)
        {
            foreach (NpcRecord record in boot.NpcExtension!.Records)
            {
                if (record.Name == name)
                {
                    return record;
                }
            }

            Assert.Fail("no NPC " + name);
            return null!;
        }

        private static void Log(string what, long milliseconds, int frames) =>
            UnityEngine.Debug.Log("[B-FRAME] " + what + " ms=" + milliseconds.ToString(CultureInfo.InvariantCulture)
                + " frames=" + frames.ToString(CultureInfo.InvariantCulture)
                + (frames > 0 ? " msPerFrame=" + (milliseconds / (double)frames).ToString("0.00", CultureInfo.InvariantCulture) : string.Empty));
    }
}
