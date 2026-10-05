// GameCore.Gameplay.Player - the player plugin's kernel half: payloads, readers, the per-world module and the command
// system (P1.3; P-032, P-042, P-044).
//
//   player.move       dx, dy, dz (mm), yaw (mrad), flags   -> PlayerMoved (A..D = x, y, z, yaw)
//   player.interact   (zero)                                -> InteractRequested (A = focused key, B = actor key)
//   player.setFocus   key (or -1)                           -> FocusChanged (A = previous, B = new)
//
// Every step the system first checks whether the world moved the player (world.region differs from player.regionKey:
// a portal travel committed) and adopts the world's pose; then it applies the step's commands through the pure player
// rules. A step without a move command applies an idle move, so stamina regenerates per logical step whether or not
// the presentation sampled input. Refused focus/interact commands are rejected with no write; moves are clamped.
//
// P1.7a:
//   * Pose authority (A4): world.posX/Y/Z/yaw is the player's authoritative pose. Every decision reads it and every
//     move writes it in the same step; player.pos* is a mirror the system keeps equal to it. A mirror that differs from
//     world.pos at the start of a step means the world moved the player (a travel or a host/Studio world.place): the
//     pose is adopted (focus is cleared only when the region changed).
//   * Vertical motion (A6): player.verticalSpeed (mm/s) and player.grounded (0/1) are slots the pure rules integrate
//     (PlayerRules.Vertical); the presentation reads the committed speed and reports AirborneFlag.
//   * player.restoreStamina{amount[, request id]} (P3.1 request): clamped to the maximum, refused when unchanged
//     (player.stamina-unchanged), accepted from the host issuer or the narrative issuer (the logic delivery port). A
//     negative request id is an outbox obligation's: claimed and settled through the world's step tap, exactly once.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Player
{
    /// <summary>The P1.7a player slots, route and schemas (vertical motion and player.restoreStamina).</summary>
    public static class PlayerMotionSlots
    {
        /// <summary>player.verticalSpeed: vertical speed in mm/s (positive up).</summary>
        public static readonly SlotId VerticalSpeed = SlotNames.Of("player", "verticalSpeed");

        /// <summary>player.grounded: 1 while the player stands on something.</summary>
        public static readonly SlotId Grounded = SlotNames.Of("player", "grounded");

        public static readonly RouteId RestoreStaminaRoute = PlayerStaminaIds.RestoreRoute;

        /// <summary>player.restoreStamina: amount, optional request id.</summary>
        public static readonly SchemaRef RestoreStaminaCommand = PlayerStaminaIds.RestoreCommand;

        /// <summary>StaminaRestored: A = amount asked, B = stamina before, C = stamina after.</summary>
        public static readonly SchemaRef StaminaRestoredEvent = PlayerStaminaIds.RestoredEvent;
    }

    /// <summary>player.restoreStamina payload: amount, optionally followed by a request id.</summary>
    public readonly struct RestoreStaminaPayload
    {
        public const int Length = 4;

        public const int LengthWithRequest = 8;

        public RestoreStaminaPayload(int amount, int requestId)
        {
            Amount = amount;
            RequestId = requestId;
        }

        public int Amount { get; }

        /// <summary>0 when none; negative for an outbox obligation's id.</summary>
        public int RequestId { get; }

        public static FrozenPayload Encode(int amount) => new GameplayPayloadWriter().Int32(amount).Freeze();

        public static FrozenPayload Encode(int amount, int requestId) => new GameplayPayloadWriter().Int32(amount).Int32(requestId).Freeze();
    }

    public sealed class RestoreStaminaReader : ICommandPayloadReader<RestoreStaminaPayload>
    {
        public SchemaRef Schema => PlayerMotionSlots.RestoreStaminaCommand;

        public RestoreStaminaPayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (reader.HasLength(RestoreStaminaPayload.LengthWithRequest))
            {
                return new RestoreStaminaPayload(reader.Int32(), reader.Int32());
            }

            if (!reader.HasLength(RestoreStaminaPayload.Length))
            {
                throw new FormatException("a restoreStamina command is " + RestoreStaminaPayload.Length + " or " + RestoreStaminaPayload.LengthWithRequest + " bytes");
            }

            return new RestoreStaminaPayload(reader.Int32(), 0);
        }
    }

    /// <summary>player.move payload: dx, dy, dz (mm), yaw (mrad), flags.</summary>
    public readonly struct MovePayload
    {
        public const int Length = 20;

        public MovePayload(int dx, int dy, int dz, int yaw, int flags)
        {
            Dx = dx;
            Dy = dy;
            Dz = dz;
            Yaw = yaw;
            Flags = flags;
        }

        public int Dx { get; }

        public int Dy { get; }

        public int Dz { get; }

        public int Yaw { get; }

        public int Flags { get; }

        public static FrozenPayload Encode(int dx, int dy, int dz, int yaw, int flags) =>
            new GameplayPayloadWriter().Int32(dx).Int32(dy).Int32(dz).Int32(yaw).Int32(flags).Freeze();
    }

    /// <summary>player.interact / player.setFocus payload: one int32 (zero, or the key to focus).</summary>
    public readonly struct PlayerValuePayload
    {
        public const int Length = 4;

        public PlayerValuePayload(int value)
        {
            Value = value;
        }

        public int Value { get; }

        public static FrozenPayload Encode(int value) => new GameplayPayloadWriter().Int32(value).Freeze();
    }

    public sealed class MoveReader : ICommandPayloadReader<MovePayload>
    {
        public SchemaRef Schema => PlayerSlots.MoveCommand;

        public MovePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(MovePayload.Length))
            {
                throw new FormatException("a player move is exactly " + MovePayload.Length + " bytes");
            }

            return new MovePayload(reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32(), reader.Int32());
        }
    }

    public sealed class PlayerValueReader : ICommandPayloadReader<PlayerValuePayload>
    {
        public PlayerValueReader(SchemaRef schema)
        {
            Schema = schema;
        }

        public SchemaRef Schema { get; }

        public PlayerValuePayload Read(IReadOnlyList<byte> payload)
        {
            var reader = new GameplayPayloadReader(payload);
            if (!reader.HasLength(PlayerValuePayload.Length))
            {
                throw new FormatException("a player value command is exactly " + PlayerValuePayload.Length + " bytes");
            }

            return new PlayerValuePayload(reader.Int32());
        }
    }

    public static class PlayerReaders
    {
        public static void BindInto(CommandPayloadReaders readers)
        {
            if (readers == null)
            {
                throw new ArgumentNullException(nameof(readers));
            }

            Require(readers.TryBind(new MoveReader(), out string failure), failure);
            Require(readers.TryBind(new PlayerValueReader(PlayerSlots.InteractCommand), out failure), failure);
            Require(readers.TryBind(new PlayerValueReader(PlayerSlots.SetFocusCommand), out failure), failure);
            Require(readers.TryBind(new RestoreStaminaReader(), out failure), failure);
        }

        private static void Require(bool bound, string failure)
        {
            if (!bound)
            {
                throw new InvalidOperationException("player reader registration failed: " + failure);
            }
        }
    }

    /// <summary>The player plugin's state of one world. Instance state only.</summary>
    public sealed class PlayerModule
    {
        public PlayerModule(UnityWorldHost host, TargetRegistry registry, TargetId player, string playerAuthoringId, PlayerTuning tuning)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Player = player;
            PlayerAuthoringId = playerAuthoringId ?? string.Empty;
            PlayerKey = AuthoringIds.IsValid(playerAuthoringId) ? AuthoringIds.StableKey(playerAuthoringId!) : 0;
            Tuning = tuning;
        }

        public UnityWorldHost Host { get; }

        public TargetRegistry Registry { get; }

        /// <summary>The player's entity target.</summary>
        public TargetId Player { get; }

        public string PlayerAuthoringId { get; }

        /// <summary>The player's own stable key (the actor of its interactions).</summary>
        public int PlayerKey { get; }

        /// <summary>
        /// The integer tuning the rules apply. Settable between steps: the player session refines it with the authored
        /// vertical motion, and a Live numeric edit (SADR-013 studio, A9) retunes the running world without a rebuild.
        /// </summary>
        public PlayerTuning Tuning { get; set; }

        public int Moves { get; private set; }

        public int StaminaRestores { get; private set; }

        /// <summary>The stable refusal code of the last refused restoreStamina (empty before any).</summary>
        public string LastRefusalCode { get; private set; } = string.Empty;

        public int IdleSteps { get; private set; }

        public int Clamped { get; private set; }

        public int Adoptions { get; private set; }

        public int Refused { get; private set; }

        public int Malformed { get; private set; }

        public int FocusChanges { get; private set; }

        public int InteractRequests { get; private set; }

        internal void CountMove(bool clamped)
        {
            Moves++;
            if (clamped)
            {
                Clamped++;
            }
        }

        internal void CountIdle() => IdleSteps++;

        internal void CountAdoption() => Adoptions++;

        internal void CountRefused() => Refused++;

        internal void CountMalformed() => Malformed++;

        internal void CountFocus() => FocusChanges++;

        internal void CountInteract() => InteractRequests++;

        internal void CountRestore() => StaminaRestores++;

        internal void RefuseRestore(PlayerRefusal refusal)
        {
            Refused++;
            LastRefusalCode = PlayerRefusals.Code(refusal);
        }

        /// <summary>
        /// The player's state with its authoritative pose: world.pos* (A4), falling back to the player.pos* mirror only
        /// for a target that has no placement slots.
        /// </summary>
        public static PlayerState Read(EntityManager entityManager, Entity entity)
        {
            int mirrorX = SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosX, 0);
            int mirrorY = SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosY, 0);
            int mirrorZ = SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosZ, 0);
            int mirrorYaw = SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Yaw, 0);
            return new PlayerState(
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosX, mirrorX),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosY, mirrorY),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.PosZ, mirrorZ),
                SlotState.ReadOrDefault(entityManager, entity, GameplaySlots.WorldOwner, GameplaySlots.Yaw, mirrorYaw),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Stamina, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Focus, PlayerRules.NoFocus),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegionKey, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegenDelayMs, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerMotionSlots.VerticalSpeed, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerMotionSlots.Grounded, 1) != 0);
        }

        /// <summary>True when the player.pos* mirror differs from the authoritative world.pos* (the world moved the player).</summary>
        public static bool MirrorDiffers(EntityManager entityManager, Entity entity)
        {
            return Differs(entityManager, entity, PlayerSlots.PosX, GameplaySlots.PosX)
                || Differs(entityManager, entity, PlayerSlots.PosY, GameplaySlots.PosY)
                || Differs(entityManager, entity, PlayerSlots.PosZ, GameplaySlots.PosZ)
                || Differs(entityManager, entity, PlayerSlots.Yaw, GameplaySlots.Yaw);
        }

        /// <summary>Writes the state: the pose to world.pos* (authoritative) and to the player.pos* mirror.</summary>
        public static void Write(EntityManager entityManager, Entity entity, PlayerState state)
        {
            WorldModule.WritePose(entityManager, entity, state.PosX, state.PosY, state.PosZ, state.Yaw);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosX, state.PosX);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosY, state.PosY);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosZ, state.PosZ);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Yaw, state.Yaw);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Stamina, state.Stamina);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Focus, state.Focus);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegionKey, state.RegionKey);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegenDelayMs, state.RegenDelayMilliseconds);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerMotionSlots.VerticalSpeed, state.VerticalSpeed);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerMotionSlots.Grounded, state.Grounded ? 1 : 0);
        }

        private static bool Differs(EntityManager entityManager, Entity entity, SlotId mirror, SlotId authoritative)
        {
            return SlotState.TryRead(entityManager, entity, GameplaySlots.WorldOwner, authoritative, out int world)
                && SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, mirror, world) != world;
        }
    }

    /// <summary>The player command stage: travel adoption, moves (or an idle step), focus and interact requests.</summary>
    [DisableAutoCreation]
    public partial class PlayerCommandSystem : SystemBase
    {
        /// <summary>This world's module; set by the player world extension after boot. Until then the stage is idle.</summary>
        public PlayerModule? Module { get; set; }

        protected override void OnUpdate()
        {
            PlayerModule? module = Module;
            if (module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(PlayerDeclarations.Owner);
            bool resolved = module.Registry.TryResolveTarget(module.Player, out TargetHandle _, out Entity player) && entityManager.Exists(player);
            if (resolved)
            {
                AdoptWorldPose(module, entityManager, player);
            }

            bool moved = false;
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                if (!resolved || !message.Target.Equals(module.Player))
                {
                    module.CountRefused();
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    continue;
                }

                if (message.Route.Equals(PlayerSlots.MoveRoute))
                {
                    moved |= Move(module, plane, entityManager, player, message);
                }
                else if (message.Route.Equals(PlayerSlots.SetFocusRoute))
                {
                    SetFocus(module, plane, entityManager, player, message);
                }
                else if (message.Route.Equals(PlayerSlots.InteractRoute))
                {
                    Interact(module, plane, entityManager, player, message);
                }
                else if (message.Route.Equals(PlayerMotionSlots.RestoreStaminaRoute))
                {
                    RestoreStamina(module, plane, entityManager, player, message);
                }
                else
                {
                    module.CountRefused();
                    plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                }
            }

            if (resolved && !moved)
            {
                PlayerState state = PlayerModule.Read(entityManager, player);
                PlayerMoveResult idle = PlayerRules.Move(state, new PlayerMove(0, 0, 0, state.Yaw, false, false), module.Tuning);
                PlayerModule.Write(entityManager, player, idle.State);
                module.CountIdle();
            }

            plane.ReleaseConsumed(PlayerDeclarations.Owner);
        }

        private static void AdoptWorldPose(PlayerModule module, EntityManager entityManager, Entity player)
        {
            if (!SlotState.TryRead(entityManager, player, GameplaySlots.WorldOwner, GameplaySlots.Region, out int region))
            {
                return;
            }

            PlayerState state = PlayerModule.Read(entityManager, player);
            if (state.RegionKey != region)
            {
                // A committed travel (or a place across regions): the arrival pose is already the authoritative pose;
                // focus belongs to the old surroundings.
                PlayerModule.Write(entityManager, player, PlayerRules.Adopt(state, region, state.PosX, state.PosY, state.PosZ, state.Yaw).WithVertical(0, true));
                module.CountAdoption();
                return;
            }

            if (PlayerModule.MirrorDiffers(entityManager, player))
            {
                // A host/Studio world.place in the same region: adopt the pose (rest vertically), keep the focus.
                PlayerModule.Write(entityManager, player, state.WithVertical(0, true));
                module.CountAdoption();
            }
        }

        private static void RestoreStamina(PlayerModule module, WorldMessagePlane plane, EntityManager entityManager, Entity player, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<RestoreStaminaPayload>(message.PayloadSchema, payload, out RestoreStaminaPayload restore, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            WorldCommandSystem? worldSystem = entityManager.World.GetExistingSystemManaged<WorldCommandSystem>();
            WorldModule? worlds = worldSystem != null ? worldSystem.Module : null;
            Id128 issuer = message.Request.IssuerId;
            if (worlds == null || (!issuer.Equals(worlds.HostIssuer) && !issuer.Equals(GameplayIssuers.Narrative(worlds.WorldId))))
            {
                module.RefuseRestore(PlayerRefusal.IssuerNotAllowed);
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            IGameplayStepTap? tap = worlds.StepTap;
            if (GameplayObligations.Claim(tap, restore.RequestId) == ObligationClaim.AlreadyApplied)
            {
                module.RefuseRestore(PlayerRefusal.AlreadyApplied);
                plane.Reject(message, DiagnosticCode.IdempotencyConflict, plane.ExecutingStep);
                return;
            }

            PlayerState state = PlayerModule.Read(entityManager, player);
            PlayerTransition transition = PlayerRules.RestoreStamina(state, restore.Amount, module.Tuning);
            if (!transition.Accepted)
            {
                module.RefuseRestore(transition.Refusal);
                plane.Reject(message, transition.Refusal == PlayerRefusal.StaminaUnchanged ? DiagnosticCode.Ineligible : DiagnosticCode.UnsupportedVersion,
                    plane.ExecutingStep);
                return;
            }

            if (!plane.Commit(message, PlayerMotionSlots.StaminaRestoredEvent,
                    GameplayActorEvent.Encode(message.Target, restore.Amount, state.Stamina, transition.State.Stamina, 0), plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            PlayerModule.Write(entityManager, player, transition.State);
            GameplayObligations.Settle(tap, restore.RequestId);
            module.CountRestore();
        }

        private static bool Move(PlayerModule module, WorldMessagePlane plane, EntityManager entityManager, Entity player, StepMessage message)
        {
            byte[] payload = plane.PayloadOf(message);
            if (plane.Readers.TryRead<MovePayload>(message.PayloadSchema, payload, out MovePayload move, out string _)
                != PayloadDecodeOutcome.Decoded)
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return false;
            }

            PlayerState state = PlayerModule.Read(entityManager, player);
            PlayerMoveResult result = PlayerRules.Move(state, PlayerMove.FromFlags(move.Dx, move.Dy, move.Dz, move.Yaw, move.Flags), module.Tuning);
            if (!result.Accepted)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return false;
            }

            PlayerState next = result.State;
            if (!plane.Commit(message, PlayerSlots.MovedEvent,
                    GameplayActorEvent.Encode(message.Target, next.PosX, next.PosY, next.PosZ, next.Yaw), plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return false;
            }

            PlayerModule.Write(entityManager, player, next);
            module.CountMove(result.Clamped);
            return true;
        }

        private static void SetFocus(PlayerModule module, WorldMessagePlane plane, EntityManager entityManager, Entity player, StepMessage message)
        {
            if (!TryValue(plane, message, out int key))
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            PlayerState state = PlayerModule.Read(entityManager, player);
            PlayerTransition transition = PlayerRules.SetFocus(state, key);
            if (!transition.Accepted)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            if (!plane.Commit(message, PlayerSlots.FocusChangedEvent,
                    GameplayActorEvent.Encode(message.Target, state.Focus, key, 0, 0), plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            PlayerModule.Write(entityManager, player, transition.State);
            module.CountFocus();
        }

        private static void Interact(PlayerModule module, WorldMessagePlane plane, EntityManager entityManager, Entity player, StepMessage message)
        {
            if (!TryValue(plane, message, out int _))
            {
                module.CountMalformed();
                plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                return;
            }

            PlayerState state = PlayerModule.Read(entityManager, player);
            PlayerTransition transition = PlayerRules.Interact(state);
            if (!transition.Accepted)
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.Ineligible, plane.ExecutingStep);
                return;
            }

            if (!plane.Commit(message, PlayerSlots.InteractRequestedEvent,
                    GameplayActorEvent.Encode(message.Target, state.Focus, module.PlayerKey, 0, 0), plane.ExecutingStep, out string _))
            {
                module.CountRefused();
                plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                return;
            }

            module.CountInteract();
        }

        private static bool TryValue(WorldMessagePlane plane, StepMessage message, out int value)
        {
            byte[] payload = plane.PayloadOf(message);
            bool decoded = plane.Readers.TryRead<PlayerValuePayload>(message.PayloadSchema, payload, out PlayerValuePayload read, out string _)
                == PayloadDecodeOutcome.Decoded;
            value = decoded ? read.Value : 0;
            return decoded;
        }
    }
}
