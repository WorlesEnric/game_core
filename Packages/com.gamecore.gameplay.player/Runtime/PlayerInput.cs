// GameCore.Gameplay.Player - input sampling and the player input adapter (P1.3).
//
// The authoritative-pose loop (determinism under replay):
//
//   frame N, CollectInput (before the host pump)
//     1. PlayerInputAdapter samples one PlayerIntent (Input System actions, or a scripted source in tests).
//     2. It reads the COMMITTED player pose (player.posX/Y/Z, yaw, stamina) of step N-1.
//     3. The motion resolver turns the intent into a displacement from that committed pose: PlayerLocomotion moves the
//        player view's CharacterController there (teleport to the committed pose, then Move with slopes, steps and
//        gravity) and reports where collision resolution ended; without a view (headless) the kinematic resolver
//        passes the horizontal displacement through.
//     4. The adapter submits ONE player.move{dx,dy,dz,yaw,flags} with the resolved displacement in millimetres.
//   host pump: step N commits; the player rules clamp the displacement (speed x move window, vertical limit), pay
//     stamina, and write the new pose slots.
//   Present: PlayerLocomotion puts the view at the committed pose.
//
// The slots therefore depend only on the sequence of committed commands: replaying the recorded player.move commands
// reproduces every pose and stamina value without physics, frame times or the CharacterController. Physics and frame
// time only decide which displacement gets requested; the kernel decides what is accepted.
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.App;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameCore.Gameplay.Player
{
    /// <summary>One frame of sampled player intent.</summary>
    public struct PlayerIntent
    {
        /// <summary>Move stick/keys, x = right, y = forward, magnitude &lt;= 1.</summary>
        public Vector2 Move;

        /// <summary>Look delta in input units (mouse pixels or stick).</summary>
        public Vector2 Look;

        public bool Run;

        public bool Jump;

        public bool Interact;

        public bool Pause;

        public bool Journal;

        public bool Inventory;
    }

    /// <summary>A source of player intent, sampled once per frame.</summary>
    public interface IPlayerIntentSource
    {
        PlayerIntent Sample();
    }

    /// <summary>Samples the Input System actions of an InputProfile (Move, Look, Jump, Interact, Pause, Journal, Inventory, Run).</summary>
    public sealed class InputSystemIntentSource : IPlayerIntentSource, IDisposable
    {
        private readonly InputActionMap? map;
        private readonly InputAction? move;
        private readonly InputAction? look;
        private readonly InputAction? jump;
        private readonly InputAction? interact;
        private readonly InputAction? pause;
        private readonly InputAction? journal;
        private readonly InputAction? inventory;
        private readonly InputAction? run;

        public InputSystemIntentSource(InputActionAsset? actions, string mapName)
        {
            map = actions != null ? actions.FindActionMap(string.IsNullOrEmpty(mapName) ? InputProfile.DefaultMap : mapName, false) : null;
            if (map == null)
            {
                return;
            }

            move = map.FindAction("Move", false);
            look = map.FindAction("Look", false);
            jump = map.FindAction("Jump", false);
            interact = map.FindAction("Interact", false);
            pause = map.FindAction("Pause", false);
            journal = map.FindAction("Journal", false);
            inventory = map.FindAction("Inventory", false);
            run = map.FindAction("Run", false);
            map.Enable();
        }

        /// <summary>False when the actions asset or map was not found (the source then samples nothing).</summary>
        public bool IsBound => map != null;

        public PlayerIntent Sample()
        {
            var intent = new PlayerIntent();
            if (map == null)
            {
                return intent;
            }

            intent.Move = move != null ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
            intent.Look = look != null ? look.ReadValue<Vector2>() : Vector2.zero;
            intent.Jump = jump != null && jump.WasPressedThisFrame();
            intent.Interact = interact != null && interact.WasPressedThisFrame();
            intent.Pause = pause != null && pause.WasPressedThisFrame();
            intent.Journal = journal != null && journal.WasPressedThisFrame();
            intent.Inventory = inventory != null && inventory.WasPressedThisFrame();
            intent.Run = run != null && run.IsPressed();
            return intent;
        }

        public void Dispose()
        {
            if (map != null)
            {
                map.Disable();
            }
        }
    }

    /// <summary>Turns a requested displacement into the displacement collision resolution allows.</summary>
    public interface IPlayerMotionResolver
    {
        /// <summary>False when the resolver cannot run (no view, headless); the adapter then uses the kinematic resolver.</summary>
        bool IsResolving { get; }

        /// <param name="from">The committed pose, metres.</param>
        /// <param name="horizontal">Requested horizontal displacement this frame, metres.</param>
        /// <param name="verticalSpeed">
        /// The committed vertical speed (player.verticalSpeed, m/s; the jump take-off speed when this move jumps). The
        /// kernel integrates gravity (P1.7a, A6); the resolver only moves by it and reports where collision stops it.
        /// </param>
        /// <param name="deltaTime">Frame time, seconds.</param>
        /// <param name="airborne">True when the resolver found nothing under the player (the move's AirborneFlag).</param>
        /// <returns>Where the player ends up, metres.</returns>
        Vector3 Resolve(Vector3 from, Vector3 horizontal, float verticalSpeed, float deltaTime, out bool airborne);
    }

    /// <summary>No collision: the horizontal displacement is passed through, the height is kept and the player is grounded.</summary>
    public sealed class KinematicMotionResolver : IPlayerMotionResolver
    {
        public bool IsResolving => true;

        public Vector3 Resolve(Vector3 from, Vector3 horizontal, float verticalSpeed, float deltaTime, out bool airborne)
        {
            airborne = false;
            return new Vector3(from.x + horizontal.x, from.y, from.z + horizontal.z);
        }
    }

    /// <summary>Submits the player's commands with the world's gameplay issuer.</summary>
    public sealed class PlayerCommands
    {
        private readonly GameplayWorld world;

        public PlayerCommands(GameplayWorld world, TargetId player)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            Player = player;
        }

        public TargetId Player { get; }

        public CommandAdmissionReceipt Move(int dx, int dy, int dz, int yaw, int flags) =>
            world.Commands.Submit(PlayerSlots.MoveRoute, Player, PlayerSlots.MoveCommand, MovePayload.Encode(dx, dy, dz, yaw, flags));

        public CommandAdmissionReceipt Interact() =>
            world.Commands.Submit(PlayerSlots.InteractRoute, Player, PlayerSlots.InteractCommand, PlayerValuePayload.Encode(0));

        public CommandAdmissionReceipt SetFocus(int key) =>
            world.Commands.Submit(PlayerSlots.SetFocusRoute, Player, PlayerSlots.SetFocusCommand, PlayerValuePayload.Encode(key));
    }

    /// <summary>
    /// The player's input source: samples intent once per frame and submits exactly one player.move (the world's step
    /// heartbeat while it runs), plus player.interact when Interact was pressed with a focus, and raises UI intents.
    /// </summary>
    public sealed class PlayerInputAdapter : IGameplayInputSource
    {
        private readonly KinematicMotionResolver kinematic = new KinematicMotionResolver();
        private readonly PlayerWorldExtension extension;

        public PlayerInputAdapter(PlayerWorldExtension extension, PlayerCommands commands, IPlayerIntentSource source)
        {
            this.extension = extension ?? throw new ArgumentNullException(nameof(extension));
            Commands = commands ?? throw new ArgumentNullException(nameof(commands));
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>When false the adapter submits nothing (a test drives the player through its own input source).</summary>
        public bool Enabled { get; set; } = true;

        public PlayerCommands Commands { get; }

        public IPlayerIntentSource Source { get; set; }

        /// <summary>The CharacterController resolver (PlayerLocomotion); null or inactive falls back to kinematic.</summary>
        public IPlayerMotionResolver? Resolver { get; set; }

        /// <summary>Camera heading in degrees around +Y, or null to move relative to the player's own heading.</summary>
        public Func<float?>? CameraYaw { get; set; }

        /// <summary>Frame time in seconds (defaults to Time.deltaTime); tests pin it.</summary>
        public Func<float> DeltaTime { get; set; } = () => Time.deltaTime;

        public IUiIntentSink UiIntents { get; set; } = new NullUiIntentSink();

        /// <summary>The last sampled intent (the camera reads Look from it).</summary>
        public PlayerIntent LastIntent { get; private set; }

        public int MovesSubmitted { get; private set; }

        public int InteractsSubmitted { get; private set; }

        public int Collect(GameplayWorld world)
        {
            PlayerModule? module = extension.Module;
            if (!Enabled || module == null || world.Root.State != GameApplicationState.Running)
            {
                return 0;
            }

            PlayerIntent intent = Source.Sample();
            LastIntent = intent;
            RaiseUi(intent);

            // P1.7a (A4): world.pos is the authoritative pose; (A6) the vertical speed and grounded flag are slots.
            TargetId player = extension.Player;
            int x = world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0);
            int y = world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int z = world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0);
            int yaw = world.Slots.ReadOrDefault(player, GameplaySlots.WorldOwner, GameplaySlots.Yaw, 0);
            int verticalSpeed = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerMotionSlots.VerticalSpeed, 0);
            bool grounded = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerMotionSlots.Grounded, 1) != 0;
            int stamina = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.Stamina, 0);
            int focus = world.Slots.ReadOrDefault(player, PlayerSlots.Owner, PlayerSlots.Focus, PlayerRules.NoFocus);

            float headingDegrees = CameraYaw?.Invoke() ?? (float)GameplayUnits.MilliradiansToDegrees(yaw);
            float heading = headingDegrees * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading));
            var right = new Vector3(Mathf.Cos(heading), 0f, -Mathf.Sin(heading));
            Vector2 stick = Vector2.ClampMagnitude(intent.Move, 1f);
            Vector3 direction = right * stick.x + forward * stick.y;
            bool running = intent.Run && stamina > 0;
            PlayerTuning tuning = module.Tuning;
            float speed = (running ? tuning.RunMillimetresPerSecond : tuning.WalkMillimetresPerSecond) / 1000f;
            float window = tuning.MoveWindowMilliseconds / 1000f;
            float dt = Mathf.Clamp(DeltaTime(), 0f, window);
            bool jump = intent.Jump && grounded && stamina >= tuning.JumpCost;
            int takeOff = jump ? tuning.JumpSpeedMillimetresPerSecond : verticalSpeed;

            var from = new Vector3((float)GameplayUnits.ToMetres(x), (float)GameplayUnits.ToMetres(y), (float)GameplayUnits.ToMetres(z));
            IPlayerMotionResolver resolver = Resolver != null && Resolver.IsResolving ? Resolver : kinematic;
            Vector3 to = resolver.Resolve(from, direction * (speed * dt), takeOff / 1000f, dt, out bool airborne);

            int dx = GameplayUnits.ToMillimetres(to.x) - x;
            int dy = GameplayUnits.ToMillimetres(to.y) - y;
            int dz = GameplayUnits.ToMillimetres(to.z) - z;
            int facing = dx != 0 || dz != 0 ? PlanarMath.YawTowards(dx, dz) : yaw;
            int flags = (running ? PlayerSlots.RunFlag : 0) | (jump ? PlayerSlots.JumpFlag : 0) | (airborne ? PlayerRules.AirborneFlag : 0);
            int submitted = 0;
            if (Commands.Move(dx, dy, dz, facing, flags).Admitted)
            {
                MovesSubmitted++;
                submitted++;
            }

            if (intent.Interact && focus != PlayerRules.NoFocus && Commands.Interact().Admitted)
            {
                InteractsSubmitted++;
                submitted++;
            }

            return submitted;
        }

        private void RaiseUi(PlayerIntent intent)
        {
            if (intent.Pause)
            {
                UiIntents.Raise(UiIntent.Pause);
            }

            if (intent.Journal)
            {
                UiIntents.Raise(UiIntent.Journal);
            }

            if (intent.Inventory)
            {
                UiIntents.Raise(UiIntent.Inventory);
            }
        }
    }
}
