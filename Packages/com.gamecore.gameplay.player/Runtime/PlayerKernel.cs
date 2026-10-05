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
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Player;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Player
{
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

        public PlayerTuning Tuning { get; }

        public int Moves { get; private set; }

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

        public static PlayerState Read(EntityManager entityManager, Entity entity) =>
            new PlayerState(
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosX, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosY, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosZ, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Yaw, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Stamina, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Focus, PlayerRules.NoFocus),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegionKey, 0),
                SlotState.ReadOrDefault(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegenDelayMs, 0));

        public static void Write(EntityManager entityManager, Entity entity, PlayerState state)
        {
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosX, state.PosX);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosY, state.PosY);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.PosZ, state.PosZ);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Yaw, state.Yaw);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Stamina, state.Stamina);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.Focus, state.Focus);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegionKey, state.RegionKey);
            SlotState.Write(entityManager, entity, PlayerSlots.Owner, PlayerSlots.RegenDelayMs, state.RegenDelayMilliseconds);
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
            if (state.RegionKey == region)
            {
                return;
            }

            PlayerState adopted = PlayerRules.Adopt(
                state,
                region,
                SlotState.ReadOrDefault(entityManager, player, GameplaySlots.WorldOwner, GameplaySlots.PosX, state.PosX),
                SlotState.ReadOrDefault(entityManager, player, GameplaySlots.WorldOwner, GameplaySlots.PosY, state.PosY),
                SlotState.ReadOrDefault(entityManager, player, GameplaySlots.WorldOwner, GameplaySlots.PosZ, state.PosZ),
                SlotState.ReadOrDefault(entityManager, player, GameplaySlots.WorldOwner, GameplaySlots.Yaw, state.Yaw));
            PlayerModule.Write(entityManager, player, adopted);
            module.CountAdoption();
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
