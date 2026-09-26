// GameCore.Gameplay.Traversal — the course's ECS storage and its bounded wire payloads (GC-020).
//
// Normative sources: 07 s4.2's schema table (`MovementInput`, `KinematicPose`, `Velocity`, `JumpState`,
// `EffectiveAcceleration`, `CheckpointDefinition`, `CheckpointObservation[]`, `RunProgress`, `CheckpointPassed`),
// 05 s6 (declared schema fields, fixed-width big-endian scalars, no reflection) and 04 s8 (a payload is decoded by a
// hand-written reader bound to its schema; a missing reader is reported, never substituted).
//
// Every component is blittable and holds no managed reference, so it is safe under Burst and IL2CPP. The payload
// encoding is FIXED-WIDTH BIG-ENDIAN, the scalar convention of 05 s6 that `IntegrationSlotValues` writes for derived
// values, so a command's payload, a binding row's value and a committed event's payload agree byte for byte.
//
// TWO PLACEMENTS. Runner-owned motion and input live on the runner entity, because one runner's pose is that
// runner's state. Progress, sealed observations, crossing output and the committed image live on the course entity,
// because `CheckpointRuntime` owns them for the whole course and one bounded decision settles every crossing of a
// step (P-034, P-044). The rules package owns the pure arithmetic (`TraversalMotionRules`), the checkpoint course
// rules and the acceleration payload; this file owns only the ECS and wire shapes around them.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Rules.Traversal;
using Unity.Entities;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>
    /// `KinematicPose` (07 s4.2): the runner's ECS-owned pose, in millimetres. In the Core-owned kinematic mode this
    /// component is the pose authority; in the optional external rigidbody mode gameplay must not write it and the
    /// synchronized engine observation carries the pose gameplay reads (P-034, 07 s4.2).
    /// </summary>
    public struct TraversalPose : IComponentData
    {
        /// <summary>Position x, in millimetres.</summary>
        public int X;

        /// <summary>Position y, in millimetres.</summary>
        public int Y;

        /// <summary>Position z, in millimetres.</summary>
        public int Z;

        /// <summary>1 while the body rests on the declared ground plane.</summary>
        public byte Grounded;

        /// <summary>True while the body rests on the ground.</summary>
        public bool IsGrounded => Grounded != 0;

        /// <summary>The component's value as the rules package's vector.</summary>
        public TraversalVector3i Vector => new TraversalVector3i(X, Y, Z);

        /// <summary>Builds one pose component from the rules package's vector.</summary>
        public static TraversalPose Of(TraversalVector3i position, byte grounded)
        {
            return new TraversalPose
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Grounded = grounded,
            };
        }
    }

    /// <summary>`Velocity` (07 s4.2): the runner's ECS-owned velocity, in thousandths of a metre per second.</summary>
    public struct TraversalVelocity : IComponentData
    {
        /// <summary>Velocity x, in thousandths of a metre per second.</summary>
        public int X;

        /// <summary>Velocity y.</summary>
        public int Y;

        /// <summary>Velocity z.</summary>
        public int Z;

        /// <summary>The component's value as the rules package's vector.</summary>
        public TraversalVector3i Vector => new TraversalVector3i(X, Y, Z);

        /// <summary>Builds one velocity component from the rules package's vector.</summary>
        public static TraversalVelocity Of(TraversalVector3i velocity)
        {
            return new TraversalVelocity { X = velocity.X, Y = velocity.Y, Z = velocity.Z };
        }
    }

    /// <summary>`JumpState` (07 s4.2): the runner's jump bookkeeping, separate from pose and velocity.</summary>
    public struct TraversalJumpState : IComponentData
    {
        /// <summary>The logical step of the last accepted jump, or zero when the run has not jumped.</summary>
        public ulong LastJumpStep;

        /// <summary>Jumps this run has performed, so a refused jump is distinguishable from an accepted one.</summary>
        public uint JumpCount;

        /// <summary>1 while the body is airborne.</summary>
        public byte Airborne;
    }

    /// <summary>
    /// `MovementInput` (07 s4.2): the runner's immutable step input after capture, written once per sealed step by
    /// the input adapter's stage. `Captured` distinguishes "this step captured an idle input" from "no input was
    /// captured for this step at all", so an absent capture is never silently read as a request for nothing.
    /// </summary>
    public struct TraversalMovementInput : IComponentData
    {
        /// <summary>Requested horizontal acceleration in thousandths of a metre per second squared.</summary>
        public int HorizontalMilli;

        /// <summary>Requested vertical acceleration in thousandths (declared, unused by the fixture's policy).</summary>
        public int VerticalMilli;

        /// <summary>1 when the captured sample asked for a jump.</summary>
        public byte JumpPressed;

        /// <summary>1 when this step captured input for this runner.</summary>
        public byte Captured;

        /// <summary>The logical step this input was captured for (P-037's sealed prefix).</summary>
        public ulong CapturedStep;

        /// <summary>True when a jump was requested in this step's input.</summary>
        public bool WantsJump => JumpPressed != 0 && Captured != 0;
    }

    /// <summary>
    /// `CheckpointDefinition` (07 s4.2): one checkpoint volume as content data, not a collider. The fixture's sensor
    /// tests this sphere from pure data, which is why the minimal traversal world needs no physics scene at all
    /// (07 s4.2, P-059).
    /// </summary>
    public struct TraversalCheckpointVolume : IComponentData
    {
        /// <summary>Ordinal of this volume in the course definition, ascending from zero.</summary>
        public uint Ordinal;

        /// <summary>Volume centre x, in millimetres.</summary>
        public int CenterX;

        /// <summary>Volume centre y, in millimetres.</summary>
        public int CenterY;

        /// <summary>Volume centre z, in millimetres.</summary>
        public int CenterZ;

        /// <summary>Volume radius, in millimetres.</summary>
        public int Radius;

        /// <summary>1 while the volume accepts crossings; a closed volume observes nothing.</summary>
        public byte Open;

        /// <summary>True when this volume accepts crossings.</summary>
        public bool IsOpen => Open != 0;

        /// <summary>
        /// The declared pure-data membership test: whether a point lies inside this volume. Integer arithmetic only,
        /// so the minimal fixture's sensing is exactly repeatable (P-008, 07 s4.2).
        /// </summary>
        public bool Contains(in TraversalVector3i point)
        {
            long dx = (long)point.X - CenterX;
            long dy = (long)point.Y - CenterY;
            long dz = (long)point.Z - CenterZ;
            long radius = Radius;
            return (dx * dx) + (dy * dy) + (dz * dz) <= radius * radius;
        }
    }

    /// <summary>
    /// One step's sealed spatial observation of one runner and one checkpoint volume (07 s4.2's
    /// `CheckpointObservation[]`). It is a producer row, never a progress mutation: the checkpoint owner decides
    /// whether it advances anything, and it is discarded with its step.
    /// </summary>
    public struct TraversalObservationRow : IBufferElementData
    {
        /// <summary>The runner the observation is about (a stable target id, P-004).</summary>
        public TargetId Runner;

        /// <summary>The checkpoint volume that was entered.</summary>
        public TargetId Checkpoint;

        /// <summary>Ordinal of the volume in the course definition, carried for canonical ordering.</summary>
        public uint CheckpointOrdinal;

        /// <summary>The observation's crossing ordinal within its sampled step.</summary>
        public uint CrossingSequence;

        /// <summary>The logical step the observation was sampled at (a stamp, never authority; TEST-019).</summary>
        public ulong SampledStep;

        /// <summary>The assembly epoch the observation was sampled under.</summary>
        public ulong SampledEpoch;

        /// <summary>1 when the observation came from the adapter's external feed rather than the data-defined test.</summary>
        public byte FromExternalFeed;
    }

    /// <summary>
    /// `RunProgress { LastCheckpoint, Count }` (07 s4.2), one row per runner on the course entity. It is
    /// `CheckpointRuntime`'s authoritative state and no other owner writes it.
    /// </summary>
    public struct TraversalProgressRow : IBufferElementData
    {
        /// <summary>The runner this progress belongs to.</summary>
        public TargetId Runner;

        /// <summary>Checkpoints passed, which is also the ordinal of the next expected checkpoint.</summary>
        public uint Count;

        /// <summary>1 once any crossing has advanced this run.</summary>
        public byte Started;

        /// <summary>Crossing ordinal of the observation that advanced this run most recently.</summary>
        public uint LastCrossingSequence;

        /// <summary>The checkpoint target the last accepted crossing passed.</summary>
        public TargetId LastCheckpoint;

        /// <summary>The logical step of the last accepted crossing.</summary>
        public ulong LastCrossingStep;
    }

    /// <summary>
    /// `CheckpointPassed` (07 s4.2): the committed output one accepted crossing prepared. It names the crossing
    /// identity, the resulting count and the causal step, so an observer reads one coherent record rather than
    /// predicting progress from its own model (P-045).
    /// </summary>
    public struct TraversalCrossingRow : IBufferElementData
    {
        /// <summary>The runner that passed the checkpoint.</summary>
        public TargetId Runner;

        /// <summary>The checkpoint volume that was passed.</summary>
        public TargetId Checkpoint;

        /// <summary>Ordinal of that volume in the course definition.</summary>
        public uint CheckpointOrdinal;

        /// <summary>Progress count after this crossing.</summary>
        public uint CountAfter;

        /// <summary>The crossing ordinal this award consumed.</summary>
        public uint CrossingSequence;

        /// <summary>The logical step this crossing committed in.</summary>
        public ulong Step;

        /// <summary>The assembly epoch this crossing committed under.</summary>
        public ulong Epoch;
    }

    /// <summary>
    /// The course's committed image (07 s4.2: `traversal.output` "prepares a coherent snapshot"): one row per
    /// committed step, so a reader compares the authority's own statement with the live storage instead of
    /// re-deriving it.
    /// </summary>
    public struct TraversalCourseSnapshot : IComponentData
    {
        /// <summary>Logical step of this image.</summary>
        public ulong Step;

        /// <summary>Assembly epoch of this image.</summary>
        public ulong Epoch;

        /// <summary>Runners integrated in this step.</summary>
        public int IntegratedCount;

        /// <summary>Sealed observations this step produced.</summary>
        public int ObservationCount;

        /// <summary>Crossings this step committed.</summary>
        public int CrossingCount;

        /// <summary>Observations the checkpoint owner refused as stale or out of order.</summary>
        public int RefusedObservationCount;

        /// <summary>Runners whose motion was skipped because an external authority owns their pose (P-034).</summary>
        public int ExternallyOwnedCount;
    }

    /// <summary>
    /// One captured movement input as the wire carries it, before it becomes a component (07 s4.2). It is this
    /// package's own payload schema, so a route is typed rather than generic.
    /// </summary>
    public readonly struct TraversalMovementInputValue
    {
        /// <summary>Requested horizontal acceleration in thousandths of a metre per second squared.</summary>
        public readonly int HorizontalMilli;

        /// <summary>Requested vertical acceleration in thousandths.</summary>
        public readonly int VerticalMilli;

        /// <summary>1 when the sample asked for a jump.</summary>
        public readonly byte JumpPressed;

        /// <summary>Builds one captured input value.</summary>
        public TraversalMovementInputValue(int horizontalMilli, int verticalMilli, byte jumpPressed)
        {
            HorizontalMilli = horizontalMilli;
            VerticalMilli = verticalMilli;
            JumpPressed = jumpPressed;
        }

        /// <summary>True when the sample requests nothing.</summary>
        public bool IsIdle => HorizontalMilli == 0 && VerticalMilli == 0 && JumpPressed == 0;

        /// <summary>Diagnostic form; never an identity (P-004).</summary>
        public override string ToString() =>
            "h" + HorizontalMilli.ToString(CultureInfo.InvariantCulture)
            + "/v" + VerticalMilli.ToString(CultureInfo.InvariantCulture)
            + (JumpPressed != 0 ? "/jump" : string.Empty);
    }

    /// <summary>Hand-written reader of the movement-input payload, bound to its schema (04 s8).</summary>
    public sealed class TraversalMovementInputReader : ICommandPayloadReader<TraversalMovementInputValue>
    {
        /// <inheritdoc />
        public SchemaRef Schema => TraversalKeys.CommandSchema;

        /// <inheritdoc />
        public TraversalMovementInputValue Read(IReadOnlyList<byte> payload)
        {
            if (!TraversalCommandCodec.TryReadInput(payload, out TraversalMovementInputValue input))
            {
                throw new ArgumentException(
                    "the movement input payload is exactly "
                    + TraversalCommandCodec.CommandBytes.ToString(CultureInfo.InvariantCulture)
                    + " canonical big-endian bytes.",
                    nameof(payload));
            }

            return input;
        }
    }

    /// <summary>
    /// Canonical codec of this package's payloads: fixed-width big-endian scalars, the 05 s6 convention, so an event
    /// payload is decoded by the same rule that wrote it.
    /// </summary>
    public static class TraversalCommandCodec
    {
        /// <summary>Bytes of one captured movement input payload: two int32 components and one flag word.</summary>
        public const int CommandBytes = 12;

        /// <summary>Bytes of one committed crossing event payload: four ordinals and one 64-bit step.</summary>
        public const int CrossingBytes = 24;

        /// <summary>Encodes one captured movement input as a canonical frozen payload.</summary>
        public static FrozenPayload WriteInput(int horizontalMilli, int verticalMilli, byte jumpPressed)
        {
            var bytes = new byte[CommandBytes];
            WriteInt32(bytes, 0, horizontalMilli);
            WriteInt32(bytes, 4, verticalMilli);
            WriteInt32(bytes, 8, jumpPressed != 0 ? 1 : 0);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one captured movement input payload; false for any other length (P-054).</summary>
        public static bool TryReadInput(IReadOnlyList<byte>? payload, out TraversalMovementInputValue input)
        {
            input = default(TraversalMovementInputValue);
            if (payload == null || payload.Count != CommandBytes)
            {
                return false;
            }

            if (!TryReadInt32(payload, 0, out int horizontal)
                || !TryReadInt32(payload, 4, out int vertical)
                || !TryReadInt32(payload, 8, out int jump))
            {
                return false;
            }

            input = new TraversalMovementInputValue(horizontal, vertical, jump != 0 ? (byte)1 : (byte)0);
            return true;
        }

        /// <summary>
        /// Encodes one committed crossing record: the checkpoint ordinal, the count it produced, the crossing ordinal
        /// it consumed, and the 64-bit logical step. `CrossingPassed` is what an observer replays (07 s4.2, P-045).
        /// </summary>
        public static FrozenPayload WriteCrossing(in TraversalCrossingRow crossing)
        {
            var bytes = new byte[CrossingBytes];
            WriteInt32(bytes, 0, unchecked((int)crossing.CheckpointOrdinal));
            WriteInt32(bytes, 4, unchecked((int)crossing.CountAfter));
            WriteInt32(bytes, 8, unchecked((int)crossing.CrossingSequence));
            WriteInt32(bytes, 12, unchecked((int)(crossing.Step >> 32)));
            WriteInt32(bytes, 16, unchecked((int)crossing.Step));
            WriteInt32(bytes, 20, unchecked((int)crossing.Epoch));
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one committed crossing record; false for any other length.</summary>
        public static bool TryReadCrossing(
            IReadOnlyList<byte>? payload,
            out uint checkpointOrdinal,
            out uint countAfter,
            out uint crossingSequence,
            out ulong step,
            out ulong epoch)
        {
            checkpointOrdinal = 0U;
            countAfter = 0U;
            crossingSequence = 0U;
            step = 0UL;
            epoch = 0UL;
            if (payload == null || payload.Count != CrossingBytes)
            {
                return false;
            }

            if (!TryReadInt32(payload, 0, out int ordinal)
                || !TryReadInt32(payload, 4, out int count)
                || !TryReadInt32(payload, 8, out int sequence)
                || !TryReadInt32(payload, 12, out int stepHigh)
                || !TryReadInt32(payload, 16, out int stepLow)
                || !TryReadInt32(payload, 20, out int epochRaw))
            {
                return false;
            }

            checkpointOrdinal = unchecked((uint)ordinal);
            countAfter = unchecked((uint)count);
            crossingSequence = unchecked((uint)sequence);
            step = ((ulong)unchecked((uint)stepHigh) << 32) | unchecked((uint)stepLow);
            epoch = unchecked((uint)epochRaw);
            return true;
        }

        /// <summary>Writes one int32 as four big-endian bytes (the canonical scalar of 05 s6).</summary>
        public static void WriteInt32(byte[] target, int offset, int value)
        {
            uint raw = unchecked((uint)value);
            target[offset] = (byte)(raw >> 24);
            target[offset + 1] = (byte)(raw >> 16);
            target[offset + 2] = (byte)(raw >> 8);
            target[offset + 3] = (byte)raw;
        }

        /// <summary>Reads one big-endian int32; false when the buffer is too short.</summary>
        public static bool TryReadInt32(IReadOnlyList<byte>? source, int offset, out int value)
        {
            value = 0;
            if (source == null || offset < 0 || offset + 4 > source.Count)
            {
                return false;
            }

            uint raw = ((uint)source[offset] << 24)
                | ((uint)source[offset + 1] << 16)
                | ((uint)source[offset + 2] << 8)
                | source[offset + 3];
            value = unchecked((int)raw);
            return true;
        }
    }
}
