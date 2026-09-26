// GameCore.Rules.Traversal — the acceleration slot's payload, its registered reducer and the derivation value
// source of the traversal catalog (GC-020).
//
// Normative sources: 07 s4.1 ("`Tailwind` contributes `(+2, 0, 0)` m/s² and `Headwind` contributes `(-1, 0, 0)` m/s²
// to the `Additive` `traversal.Acceleration` slot. Two applicable modifiers add in canonical contribution order
// using this package's stated numeric representation."), P-019 (a reducer is pure, bounded, versioned and supplied
// by that contract's package) and P-009/P-028 (a generated catalog holds one keyed instance per registration and an
// unregistered key stays a miss).
//
// The payload is three fixed-width big-endian int32 components (05 s6's canonical scalar, repeated), which is what
// `GameCore.IntegrationSlotValues` reads and what a binding row's `Value` field mirrors. A `Additive` fold over a
// triple cannot be expressed by the single-int reducer of the card package, which is exactly why the contract's own
// package supplies its own registered componentwise reducer (P-019, 02 s6).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Derivation;

namespace GameCore.Rules.Traversal
{
    /// <summary>Fixed-width big-endian codec of the traversal package's declared payloads (05 s6).</summary>
    public static class TraversalPayloadCodec
    {
        /// <summary>Bytes of one componentwise int32-triple payload: three canonical int32 scalars.</summary>
        public const int AccelerationBytes = 12;

        /// <summary>Bytes of one captured movement input: two int32 components and one flag word.</summary>
        public const int MovementInputBytes = 12;

        /// <summary>Bytes of one committed crossing record: one int32 ordinal, one int32 count, one uint64 sequence.</summary>
        public const int CheckpointPassedBytes = 16;

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

        /// <summary>Encodes one componentwise int32 triple exactly as a derived slot value carries it.</summary>
        public static FrozenPayload WriteAcceleration(TraversalVector3i acceleration)
        {
            var bytes = new byte[AccelerationBytes];
            WriteInt32(bytes, 0, acceleration.X);
            WriteInt32(bytes, 4, acceleration.Y);
            WriteInt32(bytes, 8, acceleration.Z);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one componentwise int32 triple; false for any other length (P-054's explicit shape).</summary>
        public static bool TryReadAcceleration(IReadOnlyList<byte>? payload, out TraversalVector3i acceleration)
        {
            acceleration = TraversalVector3i.Zero;
            if (payload == null || payload.Count != AccelerationBytes)
            {
                return false;
            }

            if (!TryReadInt32(payload, 0, out int x)
                || !TryReadInt32(payload, 4, out int y)
                || !TryReadInt32(payload, 8, out int z))
            {
                return false;
            }

            acceleration = new TraversalVector3i(x, y, z);
            return true;
        }

        /// <summary>
        /// Encodes one captured movement input: the requested horizontal acceleration in thousandths, the requested
        /// vertical acceleration, and the jump flag.
        /// </summary>
        public static FrozenPayload WriteMovementInput(TraversalMovementInput input)
        {
            var bytes = new byte[MovementInputBytes];
            WriteInt32(bytes, 0, input.HorizontalMilli);
            WriteInt32(bytes, 4, input.VerticalMilli);
            WriteInt32(bytes, 8, input.JumpPressed != 0 ? 1 : 0);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one captured movement input; false for any other length.</summary>
        public static bool TryReadMovementInput(IReadOnlyList<byte>? payload, out TraversalMovementInput input)
        {
            input = default(TraversalMovementInput);
            if (payload == null || payload.Count != MovementInputBytes)
            {
                return false;
            }

            if (!TryReadInt32(payload, 0, out int horizontal)
                || !TryReadInt32(payload, 4, out int vertical)
                || !TryReadInt32(payload, 8, out int jump))
            {
                return false;
            }

            input = new TraversalMovementInput(horizontal, vertical, jump != 0 ? (byte)1 : (byte)0);
            return true;
        }

        /// <summary>Encodes one committed crossing record: the checkpoint ordinal, the new count and the sequence.</summary>
        public static FrozenPayload WriteCheckpointPassed(uint ordinal, uint count, ulong crossingSequence)
        {
            var bytes = new byte[CheckpointPassedBytes];
            WriteInt32(bytes, 0, unchecked((int)ordinal));
            WriteInt32(bytes, 4, unchecked((int)count));
            uint high = (uint)(crossingSequence >> 32);
            uint low = (uint)crossingSequence;
            WriteInt32(bytes, 8, unchecked((int)high));
            WriteInt32(bytes, 12, unchecked((int)low));
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one committed crossing record; false for any other length.</summary>
        public static bool TryReadCheckpointPassed(
            IReadOnlyList<byte>? payload,
            out uint ordinal,
            out uint count,
            out ulong crossingSequence)
        {
            ordinal = 0U;
            count = 0U;
            crossingSequence = 0UL;
            if (payload == null || payload.Count != CheckpointPassedBytes)
            {
                return false;
            }

            if (!TryReadInt32(payload, 0, out int ordinalRaw)
                || !TryReadInt32(payload, 4, out int countRaw)
                || !TryReadInt32(payload, 8, out int highRaw)
                || !TryReadInt32(payload, 12, out int lowRaw))
            {
                return false;
            }

            ordinal = unchecked((uint)ordinalRaw);
            count = unchecked((uint)countRaw);
            crossingSequence = ((ulong)unchecked((uint)highRaw) << 32) | unchecked((uint)lowRaw);
            return true;
        }
    }

    /// <summary>One captured movement input, immutable for the whole step it was sealed in (07 s4.2).</summary>
    public readonly struct TraversalMovementInput
    {
        /// <summary>Requested horizontal acceleration in thousandths of a metre per second squared.</summary>
        public readonly int HorizontalMilli;

        /// <summary>Requested vertical acceleration in thousandths (unused by the fixture's policy, kept declared).</summary>
        public readonly int VerticalMilli;

        /// <summary>1 when the sample asked for a jump.</summary>
        public readonly byte JumpPressed;

        /// <summary>Builds one movement input.</summary>
        public TraversalMovementInput(int horizontalMilli, int verticalMilli, byte jumpPressed)
        {
            HorizontalMilli = horizontalMilli;
            VerticalMilli = verticalMilli;
            JumpPressed = jumpPressed;
        }

        /// <summary>The idle input: no request at all, which is what a step with no admitted command uses.</summary>
        public static TraversalMovementInput Idle => new TraversalMovementInput(0, 0, 0);

        /// <summary>True when the sample asks for nothing.</summary>
        public bool IsIdle => HorizontalMilli == 0 && VerticalMilli == 0 && JumpPressed == 0;

        /// <inheritdoc />
        public override string ToString() =>
            "h" + HorizontalMilli.ToString(CultureInfo.InvariantCulture)
            + "/v" + VerticalMilli.ToString(CultureInfo.InvariantCulture)
            + (JumpPressed != 0 ? "/jump" : string.Empty);
    }

    /// <summary>The componentwise int32-triple reducer seam a generated traversal catalog binds (P-019).</summary>
    public interface ITraversalAccelerationReducer
    {
        /// <summary>The generated registration key this reducer is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>
        /// Folds the contributions into one effective acceleration, in the order it was given (never re-sorted).
        /// False reports a bounded failure with a non-empty <paramref name="failure"/> instead of a wrapped value.
        /// </summary>
        bool TryReduce(IReadOnlyList<TraversalVector3i>? contributions, out TraversalVector3i effective, out string failure);
    }

    /// <summary>The registered `traversal.reducer.vec3i-sum`: a componentwise, overflow-checked int32 fold.</summary>
    public sealed class TraversalAccelerationReducer : ITraversalAccelerationReducer
    {
        /// <summary>Binds the reducer to its generated registration key.</summary>
        public TraversalAccelerationReducer(FactoryKey key)
        {
            Key = key;
        }

        /// <summary>The generated registration key the catalog holds this instance under.</summary>
        public FactoryKey Key { get; }

        /// <summary>Always <see cref="TraversalVocabulary.AccelerationReducer"/>.</summary>
        public string StableName => TraversalVocabulary.AccelerationReducer;

        /// <inheritdoc />
        public bool TryReduce(
            IReadOnlyList<TraversalVector3i>? contributions,
            out TraversalVector3i effective,
            out string failure)
        {
            effective = TraversalVector3i.Zero;
            failure = string.Empty;
            if (contributions == null)
            {
                return true;
            }

            TraversalVector3i sum = TraversalVector3i.Zero;
            for (int i = 0; i < contributions.Count; i++)
            {
                if (!TraversalVector3i.TryAdd(sum, contributions[i], out sum))
                {
                    failure = TraversalVocabulary.AccelerationReducer
                        + ": the componentwise int32 fold overflowed at contribution "
                        + i.ToString(CultureInfo.InvariantCulture);
                    effective = TraversalVector3i.Zero;
                    return false;
                }
            }

            effective = sum;
            return true;
        }
    }

    /// <summary>The target-selector predicate seam a generated traversal catalog binds for every modifier rule.</summary>
    public interface ITraversalTargetPredicate
    {
        /// <summary>The generated registration key this predicate is bound under.</summary>
        FactoryKey Key { get; }

        /// <summary>The registration's stable name, for diagnostics and fingerprints.</summary>
        string StableName { get; }

        /// <summary>True when the target's declared tags satisfy this predicate.</summary>
        bool IsMatch(IReadOnlyList<string>? targetTags);
    }

    /// <summary>The registered `traversal.predicate.always`: the always-accepting predicate of the traversal fixture.</summary>
    public sealed class TraversalAlwaysPredicate : ITraversalTargetPredicate
    {
        /// <summary>Binds the predicate to its generated registration key.</summary>
        public TraversalAlwaysPredicate(FactoryKey key)
        {
            Key = key;
        }

        /// <summary>The generated registration key the catalog holds this instance under.</summary>
        public FactoryKey Key { get; }

        /// <summary>Always <see cref="TraversalVocabulary.AlwaysPredicate"/>.</summary>
        public string StableName => TraversalVocabulary.AlwaysPredicate;

        /// <summary>True for every target; a null tag list is accepted too, so no runner is skipped by accident.</summary>
        public bool IsMatch(IReadOnlyList<string>? targetTags) => true;
    }

    /// <summary>
    /// The traversal catalog's registered reducer and static predicate: the acceleration triple fold and the
    /// always-accepting target predicate (07 s4.1, P-019, P-028).
    /// </summary>
    public sealed class TraversalDerivationValueSource : IDerivationValueSource
    {
        private readonly TraversalAccelerationReducer reducer;
        private readonly TraversalAlwaysPredicate predicate;

        /// <summary>Builds the source over the two generated traversal registrations.</summary>
        public TraversalDerivationValueSource(TraversalAccelerationReducer reducer, TraversalAlwaysPredicate predicate)
        {
            this.reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
            this.predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        /// <summary>The source over the traversal catalog's own keys.</summary>
        public static TraversalDerivationValueSource Default() =>
            new TraversalDerivationValueSource(
                new TraversalAccelerationReducer(TraversalVocabulary.AccelerationReducerKey),
                new TraversalAlwaysPredicate(TraversalVocabulary.AlwaysPredicateKey));

        /// <summary>Reductions this source resolved, so a scenario can assert the registered path ran.</summary>
        public int ReductionCount { get; private set; }

        /// <summary>Predicate evaluations this source resolved.</summary>
        public int EvaluationCount { get; private set; }

        /// <inheritdoc />
        public bool IsReductionRegistered(FactoryKey reducerKey) => reducerKey.Equals(reducer.Key);

        /// <inheritdoc />
        public bool IsPredicateRegistered(FactoryKey predicateKey) => predicateKey.Equals(predicate.Key);

        /// <inheritdoc />
        public bool TryReduce(FactoryKey reducerKey, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
        {
            result = null;
            if (!reducerKey.Equals(reducer.Key))
            {
                return false;
            }

            // The Additive fold runs over the canonical contribution order the engine supplies, which is the
            // precedence order of P-018; this method never re-sorts what it was given.
            var values = new List<TraversalVector3i>(inputs.Count);
            for (int i = 0; i < inputs.Count; i++)
            {
                if (!TraversalPayloadCodec.TryReadAcceleration(inputs[i].Bytes, out TraversalVector3i value))
                {
                    throw new ReducerFailureException(
                        reducerKey,
                        "contribution " + i.ToString(CultureInfo.InvariantCulture)
                        + " is not one canonical int32 triple (05 s6)");
                }

                values.Add(value);
            }

            if (!reducer.TryReduce(values, out TraversalVector3i effective, out string failure))
            {
                throw new ReducerFailureException(reducerKey, failure);
            }

            ReductionCount++;
            result = TraversalPayloadCodec.WriteAcceleration(effective);
            return true;
        }

        /// <inheritdoc />
        public bool TryEvaluate(FactoryKey predicateKey, DerivationPredicateContext context, out bool result)
        {
            result = false;
            if (!predicateKey.Equals(predicate.Key))
            {
                return false;
            }

            // Eligibility reads declared descriptors only: a target's tags are immutable assembly data, never live
            // mutable ECS state or wall time (P-015).
            var tags = new List<string>(context.Tags.Count);
            for (int i = 0; i < context.Tags.Count; i++)
            {
                tags.Add(context.Tags[i].ToString());
            }

            EvaluationCount++;
            result = predicate.IsMatch(tags);
            return true;
        }
    }
}
