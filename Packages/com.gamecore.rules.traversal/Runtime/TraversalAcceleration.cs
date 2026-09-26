// GameCore.Rules.Traversal — the acceleration slot's payload, its registered reducer and its predicate (GC-020).
//
// The derivation value source that binds these two seams to the derivation engine lives in the GAMEPLAY package
// (`GameCore.Gameplay.Traversal.TraversalDerivationValueSource`), because the rules layer of 04 s2 may reference only
// GameCore.Contracts. That is the same split the card package makes.
//
// Normative sources: 07 s4.1 ("`Tailwind` contributes `(+2, 0, 0)` m/s² and `Headwind` contributes `(-1, 0, 0)` m/s²
// to the `Additive` `traversal.Acceleration` slot. Two applicable modifiers add in canonical contribution order
// using this package's stated numeric representation."), P-019 (a reducer is pure, bounded, versioned and supplied by
// that contract's package) and P-009/P-028 (a generated catalog holds one keyed instance per registration and an
// unregistered key stays a miss).
//
// ONE SLOT, ONE INT32. 07 s4.1 declares one `traversal.Acceleration` output slot, and the V1 Unity bridge transfers
// exactly one 32-bit integer per derived output slot into a binding row
// (`GameCore.Unity.Runtime.Integration.IntegrationSlotValues`: "The one value a derived output slot transfers into a
// binding row: a single 32-bit integer ... a payload of another length or another shape is a refusal"). So this
// package's STATED NUMERIC REPRESENTATION is:
//
//   * the slot's value is the additional acceleration along X, in thousandths of a metre per second;
//   * both of the fixture's modifiers vary only X (07 s4.1: `(+2, 0, 0)` and `(-1, 0, 0)`), which is why one
//     integer is the fixture's complete effective configuration;
//   * the fold is the registered `Additive` int32 sum, so two applicable modifiers add and retracting one removes
//     exactly its own contribution (P-017-P-019);
//   * a package that needs a full three-component derived acceleration declares three output slots of its own
//     contract; this fixture deliberately does not, because 07 s4.1 declares one.
//
// The payload codec is the canonical fixed-width big-endian int32 of 05 s6 — the same bytes
// `IntegrationSlotValues` reads and writes — so a derived value and a binding row's `Value` cannot disagree.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Rules.Traversal
{
    /// <summary>Fixed-width big-endian codec of the traversal package's declared slot payloads (05 s6).</summary>
    public static class TraversalPayloadCodec
    {
        /// <summary>Bytes of one acceleration slot value: one canonical int32 scalar.</summary>
        public const int AccelerationBytes = 4;

        /// <summary>Writes one int32 as four big-endian bytes (the canonical scalar of 05 s6).</summary>
        public static void WriteInt32(byte[] target, int offset, int value)
        {
            uint raw = unchecked((uint)value);
            target[offset] = (byte)(raw >> 24);
            target[offset + 1] = (byte)(raw >> 16);
            target[offset + 2] = (byte)(raw >> 8);
            target[offset + 3] = (byte)raw;
        }

        /// <summary>Reads one big-endian int32; false when the buffer does not hold exactly one such scalar.</summary>
        public static bool TryReadInt32(IReadOnlyList<byte>? source, out int value)
        {
            value = 0;
            if (source == null || source.Count != AccelerationBytes)
            {
                return false;
            }

            uint raw = ((uint)source[0] << 24)
                | ((uint)source[1] << 16)
                | ((uint)source[2] << 8)
                | source[3];
            value = unchecked((int)raw);
            return true;
        }

        /// <summary>Encodes one acceleration slot value exactly as a derived slot value carries it.</summary>
        public static FrozenPayload WriteAcceleration(int accelerationXMilli)
        {
            var bytes = new byte[AccelerationBytes];
            WriteInt32(bytes, 0, accelerationXMilli);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one acceleration slot value; false for any other length (P-054's explicit shape).</summary>
        public static bool TryReadAcceleration(IReadOnlyList<byte>? payload, out int accelerationXMilli) =>
            TryReadInt32(payload, out accelerationXMilli);
    }

    /// <summary>The int32 acceleration-reducer seam a generated traversal catalog binds (P-019).</summary>
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
        bool TryReduce(IReadOnlyList<int>? contributions, out int effective, out string failure);
    }

    /// <summary>The registered `traversal.reducer.int32-sum`: an overflow-checked int32 fold over X acceleration.</summary>
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
        public bool TryReduce(IReadOnlyList<int>? contributions, out int effective, out string failure)
        {
            effective = 0;
            failure = string.Empty;
            if (contributions == null)
            {
                return true;
            }

            long sum = 0L;
            for (int i = 0; i < contributions.Count; i++)
            {
                sum += contributions[i];
                if (sum < int.MinValue || sum > int.MaxValue)
                {
                    failure = TraversalVocabulary.AccelerationReducer
                        + ": the int32 fold overflowed at contribution "
                        + i.ToString(CultureInfo.InvariantCulture);
                    effective = 0;
                    return false;
                }
            }

            effective = (int)sum;
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
}
