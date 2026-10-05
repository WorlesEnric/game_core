// GameCore.Rules.Gameplay.Player - pure transitions of the player's authoritative state (P1.3, catalog row 3).
//
// The player's state is int32 slots owned by the player plugin (player.posX/Y/Z mm, player.yaw mrad, player.stamina,
// player.focus, plus the bookkeeping slots player.regionKey and player.regenDelayMs). A move command carries the
// displacement the presentation's collision resolver produced for this step; the rules clamp it to what the tuning
// allows (walk or run speed over the move window), charge or regenerate stamina, and pay for a jump. Nothing here
// depends on frame time: the same commands from the same state give the same slots, which is what replay relies on.
//
// P1.7a (A6): vertical motion is authoritative state too. The vertical speed (mm/s) and the grounded flag are player
// slots integrated here: a paid jump from the ground sets the jump speed, an airborne step applies gravity (clamped to
// the terminal speed), a grounded step rests at zero. The presentation's collision resolver only reports what it
// resolved: the step's displacement and, through AirborneFlag, that the character is not standing on anything. The
// resolver reads the committed vertical speed to propose the next step's vertical displacement, so a frame-time
// dependent integrator no longer lives in the presentation. A jump needs the ground (committed grounded flag).
// player.restoreStamina (P3.1 request) adds stamina clamped to the maximum and refuses an unchanged value.
#nullable enable
using System;

namespace GameCore.Rules.Gameplay.Player
{
    /// <summary>Integer tuning of one player (converted once from the authored PlayerDefinition).</summary>
    public readonly struct PlayerTuning
    {
        public PlayerTuning(
            int walkMillimetresPerSecond,
            int runMillimetresPerSecond,
            int stepMilliseconds,
            int moveWindowMilliseconds,
            int maxVerticalMillimetresPerStep,
            int staminaMax,
            int staminaDrainPerSecond,
            int staminaRegenPerSecond,
            int staminaRegenDelayMilliseconds,
            int jumpCost,
            int jumpSpeedMillimetresPerSecond = DefaultJumpSpeed,
            int gravityMillimetresPerSecondSquared = DefaultGravity,
            int terminalSpeedMillimetresPerSecond = DefaultTerminalSpeed)
        {
            WalkMillimetresPerSecond = walkMillimetresPerSecond < 0 ? 0 : walkMillimetresPerSecond;
            RunMillimetresPerSecond = runMillimetresPerSecond < WalkMillimetresPerSecond ? WalkMillimetresPerSecond : runMillimetresPerSecond;
            StepMilliseconds = stepMilliseconds < 1 ? 1 : stepMilliseconds;
            MoveWindowMilliseconds = moveWindowMilliseconds < StepMilliseconds ? StepMilliseconds : moveWindowMilliseconds;
            MaxVerticalMillimetresPerStep = maxVerticalMillimetresPerStep < 0 ? 0 : maxVerticalMillimetresPerStep;
            StaminaMax = staminaMax < 1 ? 1 : staminaMax;
            StaminaDrainPerSecond = staminaDrainPerSecond < 0 ? 0 : staminaDrainPerSecond;
            StaminaRegenPerSecond = staminaRegenPerSecond < 0 ? 0 : staminaRegenPerSecond;
            StaminaRegenDelayMilliseconds = staminaRegenDelayMilliseconds < 0 ? 0 : staminaRegenDelayMilliseconds;
            JumpCost = jumpCost < 0 ? 0 : jumpCost;
            JumpSpeedMillimetresPerSecond = jumpSpeedMillimetresPerSecond < 0 ? 0 : jumpSpeedMillimetresPerSecond;
            GravityMillimetresPerSecondSquared = gravityMillimetresPerSecondSquared < 0 ? 0 : gravityMillimetresPerSecondSquared;
            TerminalSpeedMillimetresPerSecond = terminalSpeedMillimetresPerSecond < 1 ? 1 : terminalSpeedMillimetresPerSecond;
        }

        /// <summary>Default jump take-off speed: 6 m/s (a 1 m jump under 18 m/s2, PlayerDefinition's defaults).</summary>
        public const int DefaultJumpSpeed = 6000;

        /// <summary>Default gravity: 18 m/s2 (PlayerDefinition's default).</summary>
        public const int DefaultGravity = 18000;

        /// <summary>Default terminal falling speed: 50 m/s.</summary>
        public const int DefaultTerminalSpeed = 50000;

        public int WalkMillimetresPerSecond { get; }

        public int RunMillimetresPerSecond { get; }

        /// <summary>The logical duration of one step (stamina accounting).</summary>
        public int StepMilliseconds { get; }

        /// <summary>
        /// The longest frame a single move may cover: a move is clamped to speed x this window, so a slow rendered frame
        /// does not lose distance while a forged command cannot teleport.
        /// </summary>
        public int MoveWindowMilliseconds { get; }

        public int MaxVerticalMillimetresPerStep { get; }

        public int StaminaMax { get; }

        public int StaminaDrainPerSecond { get; }

        public int StaminaRegenPerSecond { get; }

        public int StaminaRegenDelayMilliseconds { get; }

        public int JumpCost { get; }

        /// <summary>Vertical take-off speed of a jump (mm/s).</summary>
        public int JumpSpeedMillimetresPerSecond { get; }

        /// <summary>Gravity (mm/s2) applied to an airborne player.</summary>
        public int GravityMillimetresPerSecondSquared { get; }

        /// <summary>The fastest fall (mm/s).</summary>
        public int TerminalSpeedMillimetresPerSecond { get; }

        /// <summary>Speed lost to gravity in one step (mm/s; at least 1 when gravity is on).</summary>
        public int GravityPerStep => PerStep(GravityMillimetresPerSecondSquared);

        /// <summary>
        /// This tuning with the vertical motion of an authored gravity (m/s2) and jump height (m): the take-off speed is
        /// sqrt(2 g h), computed in integers.
        /// </summary>
        public PlayerTuning WithVertical(double gravityMetresPerSecondSquared, double jumpHeightMetres)
        {
            int gravity = (int)Math.Round(Math.Max(0.0, gravityMetresPerSecondSquared) * 1000.0, MidpointRounding.AwayFromZero);
            int height = (int)Math.Round(Math.Max(0.0, jumpHeightMetres) * 1000.0, MidpointRounding.AwayFromZero);
            return new PlayerTuning(
                WalkMillimetresPerSecond, RunMillimetresPerSecond, StepMilliseconds, MoveWindowMilliseconds, MaxVerticalMillimetresPerStep,
                StaminaMax, StaminaDrainPerSecond, StaminaRegenPerSecond, StaminaRegenDelayMilliseconds, JumpCost,
                PlayerRules.JumpSpeed(gravity, height), gravity, TerminalSpeedMillimetresPerSecond);
        }

        /// <summary>Stamina drained by one running step (at least 1 when draining is on).</summary>
        public int DrainPerStep => PerStep(StaminaDrainPerSecond);

        /// <summary>Stamina regenerated by one resting step (at least 1 when regeneration is on).</summary>
        public int RegenPerStep => PerStep(StaminaRegenPerSecond);

        /// <summary>The longest horizontal move of one step at walking or running speed.</summary>
        public int MoveLimit(bool running) =>
            PlanarMath.Travel(running ? RunMillimetresPerSecond : WalkMillimetresPerSecond, MoveWindowMilliseconds);

        /// <summary>Defaults: walk 2.5 m/s, run 5.5 m/s, 20 ms steps, 100 ms window, 0.4 m vertical, stamina 1000.</summary>
        public static PlayerTuning Default => new PlayerTuning(2500, 5500, 20, 100, 400, 1000, 200, 150, 800, 150);

        private int PerStep(int perSecond)
        {
            if (perSecond <= 0)
            {
                return 0;
            }

            long value = (long)perSecond * StepMilliseconds / 1000L;
            return value < 1L ? 1 : (int)value;
        }
    }

    /// <summary>The player's authoritative slots.</summary>
    public readonly struct PlayerState
    {
        public PlayerState(int posX, int posY, int posZ, int yaw, int stamina, int focus, int regionKey, int regenDelayMilliseconds)
            : this(posX, posY, posZ, yaw, stamina, focus, regionKey, regenDelayMilliseconds, 0, true)
        {
        }

        public PlayerState(int posX, int posY, int posZ, int yaw, int stamina, int focus, int regionKey, int regenDelayMilliseconds, int verticalSpeed, bool grounded)
        {
            PosX = posX;
            PosY = posY;
            PosZ = posZ;
            Yaw = yaw;
            Stamina = stamina;
            Focus = focus;
            RegionKey = regionKey;
            RegenDelayMilliseconds = regenDelayMilliseconds;
            VerticalSpeed = verticalSpeed;
            Grounded = grounded;
        }

        public int PosX { get; }

        public int PosY { get; }

        public int PosZ { get; }

        /// <summary>Heading in milliradians, [0, 6283).</summary>
        public int Yaw { get; }

        public int Stamina { get; }

        /// <summary>Stable key of the focused interactable or NPC, or <see cref="PlayerRules.NoFocus"/>.</summary>
        public int Focus { get; }

        /// <summary>Region key the pose belongs to (mirrors world.region; a change means a travel was committed).</summary>
        public int RegionKey { get; }

        public int RegenDelayMilliseconds { get; }

        /// <summary>Vertical speed in mm/s (positive up; P1.7a slot player.verticalSpeed).</summary>
        public int VerticalSpeed { get; }

        /// <summary>True while the player stands on something (P1.7a slot player.grounded, 0/1).</summary>
        public bool Grounded { get; }

        public bool HasFocus => Focus != PlayerRules.NoFocus;

        public PlayerState WithPose(int x, int y, int z, int yaw) => new PlayerState(x, y, z, yaw, Stamina, Focus, RegionKey, RegenDelayMilliseconds, VerticalSpeed, Grounded);

        public PlayerState WithStamina(int stamina, int regenDelay) => new PlayerState(PosX, PosY, PosZ, Yaw, stamina, Focus, RegionKey, regenDelay, VerticalSpeed, Grounded);

        public PlayerState WithFocus(int focus) => new PlayerState(PosX, PosY, PosZ, Yaw, Stamina, focus, RegionKey, RegenDelayMilliseconds, VerticalSpeed, Grounded);

        public PlayerState WithRegion(int regionKey) => new PlayerState(PosX, PosY, PosZ, Yaw, Stamina, Focus, regionKey, RegenDelayMilliseconds, VerticalSpeed, Grounded);

        public PlayerState WithVertical(int verticalSpeed, bool grounded) => new PlayerState(PosX, PosY, PosZ, Yaw, Stamina, Focus, RegionKey, RegenDelayMilliseconds, verticalSpeed, grounded);

        public override string ToString() =>
            "player(pos=" + PosX + "," + PosY + "," + PosZ + " yaw=" + Yaw + " stamina=" + Stamina + " focus=" + Focus
            + " region=" + RegionKey + " vy=" + VerticalSpeed + (Grounded ? " grounded" : " airborne") + ")";
    }

    /// <summary>One sampled move: the step's displacement (mm), the facing (mrad) and the run/jump flags.</summary>
    public readonly struct PlayerMove
    {
        public PlayerMove(int dx, int dy, int dz, int yaw, bool run, bool jump)
        {
            Dx = dx;
            Dy = dy;
            Dz = dz;
            Yaw = yaw;
            Run = run;
            Jump = jump;
            Airborne = false;
            ExtraFlags = 0;
        }

        private PlayerMove(int dx, int dy, int dz, int yaw, int flags)
        {
            Dx = dx;
            Dy = dy;
            Dz = dz;
            Yaw = yaw;
            Run = (flags & PlayerRules.RunFlag) != 0;
            Jump = (flags & PlayerRules.JumpFlag) != 0;
            Airborne = (flags & PlayerRules.AirborneFlag) != 0;
            ExtraFlags = flags & ~PlayerRules.KnownFlags;
        }

        public int Dx { get; }

        public int Dy { get; }

        public int Dz { get; }

        public int Yaw { get; }

        public bool Run { get; }

        public bool Jump { get; }

        /// <summary>The collision resolver found nothing under the player this step (P1.7a; absent = grounded).</summary>
        public bool Airborne { get; }

        /// <summary>Flag bits the rules do not know (a newer writer); a move carrying any is refused.</summary>
        public int ExtraFlags { get; }

        /// <summary>Flag bits as carried in the command payload.</summary>
        public int Flags => (Run ? PlayerRules.RunFlag : 0) | (Jump ? PlayerRules.JumpFlag : 0) | (Airborne ? PlayerRules.AirborneFlag : 0) | ExtraFlags;

        public static PlayerMove FromFlags(int dx, int dy, int dz, int yaw, int flags) => new PlayerMove(dx, dy, dz, yaw, flags);
    }

    /// <summary>Why a player command was refused (moves are clamped, not refused, unless malformed).</summary>
    public enum PlayerRefusal
    {
        None = 0,
        InvalidFocus = 1,
        FocusUnchanged = 2,
        NoFocus = 3,
        UnknownFlags = 4,

        /// <summary>player.restoreStamina would leave stamina where it is (already full).</summary>
        StaminaUnchanged = 5,

        /// <summary>player.restoreStamina with an amount that is not positive.</summary>
        InvalidAmount = 6,

        /// <summary>player.restoreStamina from an issuer other than the host or the narrative delivery.</summary>
        IssuerNotAllowed = 7,

        /// <summary>player.restoreStamina carrying an obligation that was already applied.</summary>
        AlreadyApplied = 8,
    }

    /// <summary>Stable refusal codes of the player rules.</summary>
    public static class PlayerRefusals
    {
        public const string InvalidFocus = "player.invalid-focus";
        public const string FocusUnchanged = "player.focus-unchanged";
        public const string NoFocus = "player.no-focus";
        public const string UnknownFlags = "player.unknown-flags";
        public const string StaminaUnchanged = "player.stamina-unchanged";
        public const string InvalidAmount = "player.invalid-amount";
        public const string IssuerNotAllowed = "player.issuer-not-allowed";
        public const string AlreadyApplied = "player.already-applied";

        public static string Code(PlayerRefusal refusal)
        {
            switch (refusal)
            {
                case PlayerRefusal.InvalidFocus: return InvalidFocus;
                case PlayerRefusal.FocusUnchanged: return FocusUnchanged;
                case PlayerRefusal.NoFocus: return NoFocus;
                case PlayerRefusal.UnknownFlags: return UnknownFlags;
                case PlayerRefusal.StaminaUnchanged: return StaminaUnchanged;
                case PlayerRefusal.InvalidAmount: return InvalidAmount;
                case PlayerRefusal.IssuerNotAllowed: return IssuerNotAllowed;
                case PlayerRefusal.AlreadyApplied: return AlreadyApplied;
                default: return string.Empty;
            }
        }
    }

    /// <summary>The outcome of one move.</summary>
    public readonly struct PlayerMoveResult
    {
        public PlayerMoveResult(PlayerState state, PlayerRefusal refusal, bool clamped, bool ran, bool jumped, bool jumpRefused)
        {
            State = state;
            Refusal = refusal;
            Clamped = clamped;
            Ran = ran;
            Jumped = jumped;
            JumpRefused = jumpRefused;
        }

        public PlayerState State { get; }

        public PlayerRefusal Refusal { get; }

        public bool Accepted => Refusal == PlayerRefusal.None;

        /// <summary>The displacement was longer than the tuning allows and was shortened.</summary>
        public bool Clamped { get; }

        public bool Ran { get; }

        public bool Jumped { get; }

        /// <summary>A jump was asked for without the stamina to pay for it; upward motion was dropped.</summary>
        public bool JumpRefused { get; }
    }

    /// <summary>One step of vertical motion: the next vertical speed (mm/s) and grounded flag.</summary>
    public readonly struct VerticalMotion
    {
        public VerticalMotion(int speed, bool grounded)
        {
            Speed = speed;
            Grounded = grounded;
        }

        public int Speed { get; }

        public bool Grounded { get; }
    }

    /// <summary>The outcome of a focus or interact command.</summary>
    public readonly struct PlayerTransition
    {
        private PlayerTransition(PlayerRefusal refusal, PlayerState state)
        {
            Refusal = refusal;
            State = state;
        }

        public PlayerRefusal Refusal { get; }

        public PlayerState State { get; }

        public bool Accepted => Refusal == PlayerRefusal.None;

        public static PlayerTransition Accept(PlayerState state) => new PlayerTransition(PlayerRefusal.None, state);

        public static PlayerTransition Refuse(PlayerRefusal refusal, PlayerState state) => new PlayerTransition(refusal, state);
    }

    /// <summary>Pure transitions of the player.</summary>
    public static class PlayerRules
    {
        public const int NoFocus = -1;

        public const int RunFlag = 1;

        public const int JumpFlag = 2;

        /// <summary>The resolver found no ground under the player this step (P1.7a).</summary>
        public const int AirborneFlag = 4;

        public const int KnownFlags = RunFlag | JumpFlag | AirborneFlag;

        /// <summary>Displacements shorter than this (mm) count as standing still for stamina.</summary>
        public const int StillThreshold = 1;

        /// <summary>The state of a player placed at a pose with full stamina and no focus.</summary>
        public static PlayerState Spawned(int x, int y, int z, int yaw, int regionKey, PlayerTuning tuning) =>
            new PlayerState(x, y, z, PlanarMath.NormalizeYaw(yaw), tuning.StaminaMax, NoFocus, regionKey, 0);

        /// <summary>
        /// Applies one sampled move: clamps the displacement, drains stamina while running, pays for a jump, regenerates
        /// stamina after the regeneration delay, and turns the player to the move's facing.
        /// </summary>
        public static PlayerMoveResult Move(PlayerState state, PlayerMove move, PlayerTuning tuning)
        {
            if (move.ExtraFlags != 0)
            {
                return new PlayerMoveResult(state, PlayerRefusal.UnknownFlags, false, false, false, false);
            }

            int dx = move.Dx;
            int dz = move.Dz;
            bool canRun = move.Run && state.Stamina > 0;
            bool clamped = PlanarMath.ClampLength(ref dx, ref dz, tuning.MoveLimit(canRun));
            int dy = move.Dy;
            int maxVertical = tuning.MaxVerticalMillimetresPerStep;
            if (dy > maxVertical)
            {
                dy = maxVertical;
                clamped = true;
            }
            else if (dy < -maxVertical)
            {
                dy = -maxVertical;
                clamped = true;
            }

            bool moving = PlanarMath.Distance(dx, dz) >= StillThreshold;
            bool ran = canRun && moving;
            int stamina = state.Stamina;
            int regenDelay = state.RegenDelayMilliseconds;
            bool jumped = false;
            bool jumpRefused = false;

            if (move.Jump)
            {
                if (!state.Grounded)
                {
                    jumpRefused = true;
                    if (dy > 0 && state.VerticalSpeed <= 0)
                    {
                        dy = 0;
                    }
                }
                else if (stamina >= tuning.JumpCost)
                {
                    stamina -= tuning.JumpCost;
                    jumped = true;
                    regenDelay = tuning.StaminaRegenDelayMilliseconds;
                }
                else
                {
                    jumpRefused = true;
                    if (dy > 0)
                    {
                        dy = 0;
                    }
                }
            }

            if (ran)
            {
                stamina -= tuning.DrainPerStep;
                regenDelay = tuning.StaminaRegenDelayMilliseconds;
            }
            else if (!jumped)
            {
                if (regenDelay > 0)
                {
                    regenDelay -= tuning.StepMilliseconds;
                    if (regenDelay < 0)
                    {
                        regenDelay = 0;
                    }
                }
                else
                {
                    stamina += tuning.RegenPerStep;
                }
            }

            stamina = ClampStamina(stamina, tuning);
            VerticalMotion vertical = Vertical(state.VerticalSpeed, !move.Airborne, jumped, tuning);
            PlayerState next = state
                .WithPose(PlanarMath.Add(state.PosX, dx), PlanarMath.Add(state.PosY, dy), PlanarMath.Add(state.PosZ, dz), PlanarMath.NormalizeYaw(move.Yaw))
                .WithStamina(stamina, regenDelay)
                .WithVertical(vertical.Speed, vertical.Grounded);
            return new PlayerMoveResult(next, PlayerRefusal.None, clamped, ran, jumped, jumpRefused);
        }

        /// <summary>
        /// One step of vertical motion (P1.7a, A6): a paid jump takes off at the jump speed and leaves the ground; a
        /// grounded step rests at zero; an airborne step loses <see cref="PlayerTuning.GravityPerStep"/>, never falling
        /// faster than the terminal speed.
        /// </summary>
        public static VerticalMotion Vertical(int speed, bool groundedByResolver, bool jumped, PlayerTuning tuning)
        {
            if (jumped)
            {
                return new VerticalMotion(tuning.JumpSpeedMillimetresPerSecond, false);
            }

            if (groundedByResolver && speed <= 0)
            {
                return new VerticalMotion(0, true);
            }

            long next = (long)speed - tuning.GravityPerStep;
            if (next < -tuning.TerminalSpeedMillimetresPerSecond)
            {
                next = -tuning.TerminalSpeedMillimetresPerSecond;
            }

            return new VerticalMotion((int)next, false);
        }

        /// <summary>The vertical displacement (mm) one step at <paramref name="speed"/> mm/s covers (truncated toward zero).</summary>
        public static int VerticalStepMillimetres(int speed, PlayerTuning tuning) =>
            (int)((long)speed * tuning.StepMilliseconds / 1000L);

        /// <summary>Take-off speed (mm/s) reaching <paramref name="heightMillimetres"/> under <paramref name="gravityMillimetres"/> mm/s2: sqrt(2 g h).</summary>
        public static int JumpSpeed(int gravityMillimetres, int heightMillimetres)
        {
            if (gravityMillimetres <= 0 || heightMillimetres <= 0)
            {
                return 0;
            }

            // g [mm/s2] * h [mm] = mm2/s2; the root is mm/s. Integer square root, rounded down.
            ulong value = 2UL * (ulong)gravityMillimetres * (ulong)heightMillimetres;
            ulong root = (ulong)Math.Sqrt(value);
            while (root * root > value)
            {
                root--;
            }

            while ((root + 1UL) * (root + 1UL) <= value)
            {
                root++;
            }

            return root > int.MaxValue ? int.MaxValue : (int)root;
        }

        /// <summary>
        /// player.restoreStamina: adds <paramref name="amount"/> stamina, clamped to the maximum. Refused (no write) when the
        /// amount is not positive or stamina would not change (already full).
        /// </summary>
        public static PlayerTransition RestoreStamina(PlayerState state, int amount, PlayerTuning tuning)
        {
            if (amount <= 0)
            {
                return PlayerTransition.Refuse(PlayerRefusal.InvalidAmount, state);
            }

            int current = ClampStamina(state.Stamina, tuning);
            long raised = (long)current + amount;
            int next = raised >= tuning.StaminaMax ? tuning.StaminaMax : (int)raised;
            if (next == state.Stamina)
            {
                return PlayerTransition.Refuse(PlayerRefusal.StaminaUnchanged, state);
            }

            return PlayerTransition.Accept(state.WithStamina(next, state.RegenDelayMilliseconds));
        }

        /// <summary>
        /// Adopts a pose written by the world (portal travel arrival or a world.place): the region key and pose are
        /// replaced and stamina is kept; focus is cleared because the candidates belong to the old surroundings.
        /// </summary>
        public static PlayerState Adopt(PlayerState state, int regionKey, int x, int y, int z, int yaw) =>
            state.WithPose(x, y, z, PlanarMath.NormalizeYaw(yaw)).WithRegion(regionKey).WithFocus(NoFocus);

        /// <summary>Sets the focused key (a positive stable key or <see cref="NoFocus"/>).</summary>
        public static PlayerTransition SetFocus(PlayerState state, int key)
        {
            if (key != NoFocus && key <= 0)
            {
                return PlayerTransition.Refuse(PlayerRefusal.InvalidFocus, state);
            }

            if (state.Focus == key)
            {
                return PlayerTransition.Refuse(PlayerRefusal.FocusUnchanged, state);
            }

            return PlayerTransition.Accept(state.WithFocus(key));
        }

        /// <summary>An interact request needs a focus; the state is unchanged.</summary>
        public static PlayerTransition Interact(PlayerState state) =>
            state.HasFocus ? PlayerTransition.Accept(state) : PlayerTransition.Refuse(PlayerRefusal.NoFocus, state);

        /// <summary>Stamina clamped to [0, max].</summary>
        public static int ClampStamina(int stamina, PlayerTuning tuning) =>
            stamina < 0 ? 0 : (stamina > tuning.StaminaMax ? tuning.StaminaMax : stamina);

        /// <summary>Stamina as thousandths of the maximum (for HUD bars).</summary>
        public static int StaminaMilli(PlayerState state, PlayerTuning tuning) =>
            (int)((long)ClampStamina(state.Stamina, tuning) * 1000L / tuning.StaminaMax);
    }
}
