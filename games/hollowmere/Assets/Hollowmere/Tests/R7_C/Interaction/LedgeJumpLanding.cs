#nullable enable
using System.Collections;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.R7_C.Interaction.Tests
{
    public sealed class LedgeJumpLanding
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const float FloorY = 20f;
        private const float LedgeY = 20.8f;
        private GameBoot? boot;
        private GameObject? course;

        private sealed class ScriptedIntents : IPlayerIntentSource
        {
            private readonly Queue<PlayerIntent> frames = new Queue<PlayerIntent>();

            public void Script(PlayerIntent intent, int count)
            {
                for (int i = 0; i < count; i++)
                    frames.Enqueue(intent);
            }

            public PlayerIntent Sample() => frames.Count == 0 ? default : frames.Dequeue();
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator WPLUG03_DeterministicJumpClearsSolidLedgeAndLands()
        {
            var first = new List<string>();
            yield return RunCourse(first);
            yield return Cleanup();
            var second = new List<string>();
            yield return RunCourse(second);
            CollectionAssert.AreEqual(first, second, "two fresh boots produce identical committed poses, vertical speeds and grounded transitions for the same inputs");
            Debug.Log("[R7-C] W-PLUG-03: walk blocked by 0.8m ledge; Jump crosses its lip airborne and lands grounded on top; two identical " + first.Count + "-frame traces");
        }

        private IEnumerator RunCourse(List<string> trace)
        {
#if UNITY_EDITOR
            AsyncOperation? load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BootScene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null);
            while (!load!.isDone)
                yield return null;
            boot = Object.FindAnyObjectByType<GameBoot>();
            Assert.That(boot, Is.Not.Null);
            for (int i = 0; i < 1200 && (boot!.World == null || boot.Player == null || !boot.World.Streamer.IsSettled); i++)
                yield return null;
            Assert.That(boot!.Failure, Is.Empty);
            Assert.That(boot.World, Is.Not.Null);
            Assert.That(boot.Player, Is.Not.Null);
            GameplayWorld world = boot.World!;
            PlayerSession player = boot.Player!;
            TargetId target = boot.PlayerExtension!.Player;
            var intents = new ScriptedIntents();
            player.Input.Enabled = false;
            player.Input.Source = intents;
            // Match the kernel's authored 20ms gravity step, not wall-clock time in the Editor.
            player.Input.DeltaTime = () => boot.PlayerExtension!.Module!.Tuning.StepMilliseconds / 1000f;
            player.Input.CameraYaw = () => 0f;
            Assert.That(player.Input.Resolver, Is.SameAs(player.Locomotion));
            Assert.That(player.Locomotion.IsResolving, Is.True, "use the real CharacterController, never the kinematic fallback");
            CharacterController controller = player.Locomotion.Rig!.GetComponent<CharacterController>();
            Assert.That(LedgeY - FloorY, Is.GreaterThan(controller.stepOffset + controller.skinWidth), "the ledge cannot be traversed by ordinary step climbing");

            // Isolated collision geometry above the streamed map: real solid colliders, no mocked resolver or pose writes during traversal.
            course = new GameObject("R7-C ledge course");
            AddBox("floor", new Vector3(100f, FloorY - 0.5f, 0f), new Vector3(8f, 1f, 16f));
            BoxCollider ledge = AddBox("ledge", new Vector3(100f, FloorY + 0.4f, 3f), new Vector3(8f, 0.8f, 6f));
            Physics.SyncTransforms();
            yield return PlaceAtStart(world, player, target);
            int ungroundedBefore = player.Locomotion.UngroundedResolutions;

            // Negative control: identical forward motion without Jump must stop against the solid lip.
            intents.Script(new PlayerIntent { Move = Vector2.up }, 40);
            for (int i = 0; i < 40; i++)
            {
                yield return null;
                trace.Add(Trace(world, target));
            }
            Vector3 blocked = Position(world, target);
            Assert.That(blocked.z, Is.GreaterThan(-1.1f), "the control really walked to the ledge");
            Assert.That(blocked.z, Is.LessThan(ledge.bounds.min.z), "walking alone cannot cross this lip");
            Assert.That(blocked.y, Is.EqualTo(FloorY).Within(0.05f));
            Assert.That(Motion(world, target, PlayerMotionSlots.Grounded), Is.EqualTo(1));

            yield return PlaceAtStart(world, player, target);
            intents.Script(new PlayerIntent { Move = Vector2.up, Jump = true }, 1);
            intents.Script(new PlayerIntent { Move = Vector2.up }, 59);
            intents.Script(default, 10);
            bool roseAirborne = false;
            bool crossedLipAirborne = false;
            bool descended = false;
            bool landedOnLedge = false;
            float peak = FloorY;
            for (int i = 0; i < 70; i++)
            {
                yield return null;
                Vector3 position = Position(world, target);
                int grounded = Motion(world, target, PlayerMotionSlots.Grounded);
                int verticalSpeed = Motion(world, target, PlayerMotionSlots.VerticalSpeed);
                trace.Add(Trace(world, target));
                peak = Mathf.Max(peak, position.y);
                roseAirborne |= grounded == 0 && position.y > FloorY + 0.1f && verticalSpeed > 0;
                crossedLipAirborne |= grounded == 0 && position.z >= ledge.bounds.min.z && position.y >= ledge.bounds.max.y - 0.025f;
                descended |= grounded == 0 && verticalSpeed < 0;
                landedOnLedge |= crossedLipAirborne && grounded == 1 && verticalSpeed == 0
                    && position.z > ledge.bounds.min.z + controller.radius
                    && position.z < ledge.bounds.max.z - controller.radius
                    && Mathf.Abs(position.y - ledge.bounds.max.y) < 0.05f;
            }

            Assert.That(roseAirborne, Is.True, "Jump must cause a committed ascent with grounded=0");
            Assert.That(peak, Is.GreaterThan(ledge.bounds.max.y), "the player's feet clear the solid ledge's top");
            Assert.That(crossedLipAirborne, Is.True, "the committed player crosses the lip while airborne and above its top");
            Assert.That(descended, Is.True, "the jump must transition to falling, not hover or teleport");
            Assert.That(landedOnLedge, Is.True, "after clearance the player must land inside the ledge's supporting surface");
            Assert.That(Motion(world, target, PlayerMotionSlots.Grounded), Is.EqualTo(1));
            Assert.That(Motion(world, target, PlayerMotionSlots.VerticalSpeed), Is.Zero);
            Assert.That(Position(world, target).y, Is.EqualTo(ledge.bounds.max.y).Within(0.05f));
            Assert.That(player.Locomotion.IsGrounded, Is.True, "the actual controller confirms contact after landing");
            Assert.That(player.Locomotion.UngroundedResolutions, Is.EqualTo(ungroundedBefore), "no off-map ground-probe fallback may simulate the crossing");
            Assert.That(world.Root.PumpCounter.Violations, Is.Zero);
#else
            Assert.Ignore("The ledge driver loads Boot.unity by path in Editor PlayMode");
            yield break;
#endif
        }

        private static IEnumerator PlaceAtStart(GameplayWorld world, PlayerSession player, TargetId target)
        {
            player.Input.Enabled = false;
            Assert.That(world.Commands.Place(target, 100000, 20000, -1200, 0).Admitted, Is.True);
            for (int i = 0; i < 120 && Position(world, target) != new Vector3(100f, FloorY, -1.2f); i++)
                yield return null;
            Assert.That(Position(world, target), Is.EqualTo(new Vector3(100f, FloorY, -1.2f)));
            player.Input.Enabled = true;
            for (int i = 0; i < 10; i++)
                yield return null;
            Assert.That(Motion(world, target, PlayerMotionSlots.Grounded), Is.EqualTo(1));
            Assert.That(Motion(world, target, PlayerMotionSlots.VerticalSpeed), Is.Zero);
        }

        private BoxCollider AddBox(string name, Vector3 position, Vector3 size)
        {
            var shape = new GameObject(name);
            shape.transform.SetParent(course!.transform);
            shape.transform.position = position;
            BoxCollider collider = shape.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private static int Motion(GameplayWorld world, TargetId target, SlotId slot) =>
            world.Slots.ReadOrDefault(target, PlayerSlots.Owner, slot, int.MinValue);

        private static Vector3 Position(GameplayWorld world, TargetId target) => new Vector3(
            world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosX, int.MinValue) / 1000f,
            world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosY, int.MinValue) / 1000f,
            world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue) / 1000f);

        private static string Trace(GameplayWorld world, TargetId target) =>
            world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosX, int.MinValue) + ","
            + world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosY, int.MinValue) + ","
            + world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, int.MinValue) + ","
            + Motion(world, target, PlayerMotionSlots.VerticalSpeed) + "," + Motion(world, target, PlayerMotionSlots.Grounded);

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (course != null)
                Object.Destroy(course);
            if (boot != null)
                Object.Destroy(boot.gameObject);
            course = null;
            boot = null;
            yield return null;
        }
    }
}
