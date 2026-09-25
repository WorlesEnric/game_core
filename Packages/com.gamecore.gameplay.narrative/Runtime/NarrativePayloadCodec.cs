// GameCore.Gameplay.Narrative — the narrative routes' canonical payload codec (05 section 6, P-042, P-054).
//
// A route's payload is a fixed-width big-endian integer stream: the same convention the reference fixtures use and
// the one 05 section 6 fixes for wire data. Writing it here, next to the generated readers that decode it, means a
// payload cannot be produced by one interpretation and read by another — and no payload carries a CLR type name, a
// pointer or a managed reference, so the same bytes are valid in a managed system, in a Burst job and in a player.
//
//   choice command    8 bytes:  int32 node ordinal, int32 choice ordinal
//   quest mutation    8 bytes:  int32 fact ordinal, int32 requested value
//   fact observed    12 bytes:  int32 fact ordinal, int32 value, int32 version
//   fact committed   12 bytes:  int32 fact ordinal, int32 value, int32 version
//   gate changed      8 bytes:  int32 decision, int32 evaluated fact version
//   choice committed  8 bytes:  int32 resulting node, int32 resulting status
//
// Every reader is total and bounded: a payload of another length is refused rather than partially decoded, which is
// what lets a stage treat "malformed" as a rejection instead of a default value.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Rules.Narrative;

namespace GameCore.Gameplay.Narrative
{
    /// <summary>One decoded quest-mutation request: which fact, and the value the choice asks it to take.</summary>
    public readonly struct NarrativeMutation
    {
        public readonly int FactOrdinal;
        public readonly int RequestedValue;

        public NarrativeMutation(int factOrdinal, int requestedValue)
        {
            FactOrdinal = factOrdinal;
            RequestedValue = requestedValue;
        }

        /// <summary>Bytes one encoded mutation occupies: two big-endian int32 scalars.</summary>
        public const int EncodedLength = 8;

        public override string ToString()
            => "mutation(fact=" + FactOrdinal + ", value=" + RequestedValue + ")";
    }

    /// <summary>One committed fact state carried by a step buffer or a committed event.</summary>
    public readonly struct NarrativeObservation
    {
        public readonly int FactOrdinal;
        public readonly int Value;
        public readonly int Version;

        public NarrativeObservation(int factOrdinal, int value, int version)
        {
            FactOrdinal = factOrdinal;
            Value = value;
            Version = version;
        }

        /// <summary>Bytes one encoded observation occupies: three big-endian int32 scalars.</summary>
        public const int EncodedLength = 12;

        public override string ToString()
            => "observation(fact=" + FactOrdinal + ", value=" + Value + ", version=" + Version + ")";
    }

    /// <summary>One committed gate decision carried by a committed event.</summary>
    public readonly struct NarrativeGateChange
    {
        public readonly int Decision;
        public readonly int EvaluatedFactVersion;

        public NarrativeGateChange(int decision, int evaluatedFactVersion)
        {
            Decision = decision;
            EvaluatedFactVersion = evaluatedFactVersion;
        }

        /// <summary>Bytes one encoded gate change occupies: two big-endian int32 scalars.</summary>
        public const int EncodedLength = 8;

        public override string ToString()
            => "gate(decision=" + Decision + ", evaluatedVersion=" + EvaluatedFactVersion + ")";
    }

    /// <summary>The canonical big-endian integer stream the narrative routes use (05 section 6).</summary>
    public static class NarrativePayloadCodec
    {
        /// <summary>Bytes one big-endian int32 scalar occupies.</summary>
        public const int Int32Bytes = 4;

        /// <summary>Encodes one choice: two big-endian int32 scalars in declared order.</summary>
        public static byte[] EncodeChoice(in NarrativeChoice choice)
        {
            var bytes = new byte[NarrativeChoice.EncodedLength];
            WriteInt32(bytes, 0, choice.NodeOrdinal);
            WriteInt32(bytes, Int32Bytes, choice.ChoiceOrdinal);
            return bytes;
        }

        /// <summary>Decodes one choice; a payload of another length is refused.</summary>
        public static bool TryDecodeChoice(IReadOnlyList<byte>? payload, out NarrativeChoice choice)
        {
            choice = default(NarrativeChoice);
            if (payload == null || payload.Count != NarrativeChoice.EncodedLength)
            {
                return false;
            }

            choice = new NarrativeChoice(ReadInt32(payload, 0), ReadInt32(payload, Int32Bytes));
            return true;
        }

        public static byte[] EncodeMutation(in NarrativeMutation mutation)
        {
            var bytes = new byte[NarrativeMutation.EncodedLength];
            WriteInt32(bytes, 0, mutation.FactOrdinal);
            WriteInt32(bytes, Int32Bytes, mutation.RequestedValue);
            return bytes;
        }

        public static bool TryDecodeMutation(IReadOnlyList<byte>? payload, out NarrativeMutation mutation)
        {
            mutation = default(NarrativeMutation);
            if (payload == null || payload.Count != NarrativeMutation.EncodedLength)
            {
                return false;
            }

            mutation = new NarrativeMutation(ReadInt32(payload, 0), ReadInt32(payload, Int32Bytes));
            return true;
        }

        public static byte[] EncodeObservation(in NarrativeObservation observation)
        {
            var bytes = new byte[NarrativeObservation.EncodedLength];
            WriteInt32(bytes, 0, observation.FactOrdinal);
            WriteInt32(bytes, Int32Bytes, observation.Value);
            WriteInt32(bytes, Int32Bytes * 2, observation.Version);
            return bytes;
        }

        public static bool TryDecodeObservation(IReadOnlyList<byte>? payload, out NarrativeObservation observation)
        {
            observation = default(NarrativeObservation);
            if (payload == null || payload.Count != NarrativeObservation.EncodedLength)
            {
                return false;
            }

            observation = new NarrativeObservation(
                ReadInt32(payload, 0),
                ReadInt32(payload, Int32Bytes),
                ReadInt32(payload, Int32Bytes * 2));
            return true;
        }

        /// <summary>Encodes one committed gate decision: the decision and the fact version it was taken from.</summary>
        public static byte[] EncodeGateChange(in NarrativeGateChange change)
        {
            var bytes = new byte[NarrativeGateChange.EncodedLength];
            WriteInt32(bytes, 0, change.Decision);
            WriteInt32(bytes, Int32Bytes, change.EvaluatedFactVersion);
            return bytes;
        }

        /// <summary>Encodes the outcome of one choice: the resulting node and conversation status.</summary>
        public static byte[] EncodeChoiceOutcome(int nodeOrdinal, int status)
        {
            var bytes = new byte[NarrativeChoice.EncodedLength];
            WriteInt32(bytes, 0, nodeOrdinal);
            WriteInt32(bytes, Int32Bytes, status);
            return bytes;
        }

        /// <summary>Encodes one canonical int32 scalar; the shape the reference fixtures' slot values use too.</summary>
        public static byte[] EncodeInt32(int value)
        {
            var bytes = new byte[Int32Bytes];
            WriteInt32(bytes, 0, value);
            return bytes;
        }

        /// <summary>Writes one big-endian int32 at an offset inside a caller-owned buffer.</summary>
        public static void WriteInt32(byte[] destination, int offset, int value)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (offset < 0 || offset + Int32Bytes > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            uint raw = unchecked((uint)value);
            destination[offset] = (byte)(raw >> 24);
            destination[offset + 1] = (byte)(raw >> 16);
            destination[offset + 2] = (byte)(raw >> 8);
            destination[offset + 3] = (byte)raw;
        }

        /// <summary>Reads one big-endian int32 at an offset; the caller has already length-checked the payload.</summary>
        public static int ReadInt32(IReadOnlyList<byte> payload, int offset)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (offset < 0 || offset + Int32Bytes > payload.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            uint raw = ((uint)payload[offset] << 24)
                | ((uint)payload[offset + 1] << 16)
                | ((uint)payload[offset + 2] << 8)
                | payload[offset + 3];
            return unchecked((int)raw);
        }

        /// <summary>Canonical hex of one payload, for diagnostics and evidence only (P-054: never an identity).</summary>
        public static string Describe(IReadOnlyList<byte>? payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return "<none>";
            }

            var text = new System.Text.StringBuilder(payload.Count * 2);
            for (int i = 0; i < payload.Count; i++)
            {
                text.Append(HexDigit(payload[i] >> 4)).Append(HexDigit(payload[i] & 0x0F));
            }

            return text.ToString();
        }

        private static char HexDigit(int value) => (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
    }
}
