// GameCore.Contracts - checkpoint codec seam and envelope error mapping (GC-018). Normative sources:
// docs/game-core/00-core-protocols.md P-054 (generated serializers replace reflection-based type construction; an
// unknown required field or schema version rejects) and P-052 (every error carries a stable code).
//
// The persistence path never constructs a record type by reflection and never names a CLR type on the wire: it
// holds one generated codec per record kind, keyed by the kind's stable ordinal, and each codec is a direct
// constructed reference supplied by the generated catalog (04 s8).
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>One codec's non-generic identity: the record kind it serves and the schema it accepts (P-054).</summary>
    public interface ICheckpointRecordCodec
    {
        /// <summary>The record kind this codec encodes; one kind has exactly one codec.</summary>
        CheckpointRecordKind Kind { get; }

        /// <summary>Exact schema and version this codec's documents declare.</summary>
        SchemaRef Schema { get; }

        /// <summary>
        /// Validates one document's envelope header, declared fields, required-feature gate and checksum without
        /// knowing its value type, so a reader can check a record it may decode later (05 s6).
        /// </summary>
        bool TryValidate(byte[] document, out EnvelopeError error);
    }

    /// <summary>Typed codec of one record kind: the shape a capture writes and a restore reads.</summary>
    /// <typeparam name="TValue">The record value type of this kind.</typeparam>
    public interface ICheckpointRecordCodec<TValue> : ICheckpointRecordCodec where TValue : struct
    {
        byte[] Encode(TValue value);

        bool TryDecode(byte[] document, out TValue value, out EnvelopeError error);
    }

    /// <summary>
    /// One generated codec per record kind. A missing kind is a value, never a fallback: a capture that needs a
    /// codec it does not have is refused before any bytes are written (P-054).
    /// </summary>
    public sealed class CheckpointCodecSet
    {
        private readonly ICheckpointRecordCodec?[] byKind = new ICheckpointRecordCodec?[CheckpointFormat.RecordKindCount];
        private readonly List<SchemaRef> schemas = new List<SchemaRef>();

        public CheckpointCodecSet(IReadOnlyList<ICheckpointRecordCodec>? codecs)
        {
            if (codecs == null)
            {
                return;
            }

            for (int i = 0; i < codecs.Count; i++)
            {
                ICheckpointRecordCodec codec = codecs[i];
                if (codec == null)
                {
                    throw new ArgumentException("A codec set cannot contain a null codec.", nameof(codecs));
                }

                if (!CheckpointFormat.IsDeclared(codec.Kind))
                {
                    throw new ArgumentException(
                        "A codec names an undeclared record kind " + codec.Kind + " (P-054).",
                        nameof(codecs));
                }

                int slot = (int)codec.Kind;
                if (byKind[slot] != null)
                {
                    // One kind has one codec; a second registration is a catalog defect, not a precedence question.
                    throw new ArgumentException(
                        "Two codecs are registered for record kind " + codec.Kind + " (P-054).",
                        nameof(codecs));
                }

                byKind[slot] = codec;
                schemas.Add(codec.Schema);
            }
        }

        /// <summary>Schemas this set accepts, in registration order; a reader compares them with a document's.</summary>
        public IReadOnlyList<SchemaRef> Schemas => schemas;

        /// <summary>Feature ids every document of this set declares; the codecs' own sets must agree (P-055).</summary>
        public IReadOnlyList<Id128> KnownFeatureIds => CheckpointFormat.KnownFeatureIds;

        /// <summary>The non-generic codec of one kind, or false when this set has none (P-054).</summary>
        public bool TryGet(CheckpointRecordKind kind, out ICheckpointRecordCodec? codec)
        {
            if (!CheckpointFormat.IsDeclared(kind))
            {
                codec = null;
                return false;
            }

            codec = byKind[(int)kind];
            return codec != null;
        }

        /// <summary>The typed codec of one kind; false when this set has none for that kind or value type.</summary>
        public bool TryGet<TValue>(CheckpointRecordKind kind, out ICheckpointRecordCodec<TValue>? codec)
            where TValue : struct
        {
            codec = null;
            if (!TryGet(kind, out ICheckpointRecordCodec? found) || found == null)
            {
                return false;
            }

            codec = found as ICheckpointRecordCodec<TValue>;
            return codec != null;
        }

        /// <summary>How many record kinds this set can encode and decode.</summary>
        public int CompleteKindCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < byKind.Length; i++)
                {
                    if (byKind[i] != null)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>True when every declared record kind has a codec, which a capture of a whole world requires.</summary>
        public bool IsComplete => CompleteKindCount == CheckpointFormat.RecordKindCount;

        /// <summary>Diagnostic list of the kinds this set is missing; empty when it is complete (P-052).</summary>
        public IReadOnlyList<CheckpointRecordKind> MissingKinds()
        {
            var missing = new List<CheckpointRecordKind>();
            for (int i = 0; i < byKind.Length; i++)
            {
                if (byKind[i] == null)
                {
                    missing.Add((CheckpointRecordKind)i);
                }
            }

            return missing;
        }
    }

    /// <summary>
    /// Maps an envelope codec error to the protocol's stable diagnostic code (P-052). The protocol enumerates no
    /// "corrupt save" code, so corruption maps to <see cref="DiagnosticCode.ResourceUnavailable"/>: the blob is
    /// present but unusable, which is the same class of outcome as a missing asset. A caller must not treat that
    /// code as retryable with unchanged input.
    /// </summary>
    public static class CheckpointErrors
    {
        public static DiagnosticCode CodeFor(EnvelopeError error)
        {
            switch (error)
            {
                case EnvelopeError.None:
                    return DiagnosticCode.None;

                // The document names a schema, version or feature this build does not implement (P-055).
                case EnvelopeError.BadMagic:
                case EnvelopeError.UnsupportedVersion:
                case EnvelopeError.UnknownRequiredFeature:
                case EnvelopeError.UnknownWireType:
                case EnvelopeError.InvalidUtf8:
                case EnvelopeError.InvalidBooleanValue:
                case EnvelopeError.FieldIdOutOfRange:
                    return DiagnosticCode.UnsupportedVersion;

                // A required element of the document is absent: the blob is structurally incomplete (05 s6).
                case EnvelopeError.Truncated:
                case EnvelopeError.MissingRequiredField:
                    return DiagnosticCode.MissingDependency;

                // A declared element appears twice; the document states two answers to one question (P-008).
                case EnvelopeError.DuplicateField:
                    return DiagnosticCode.OwnershipConflict;

                // A declared length or count exceeds a configured bound; nothing may be truncated (P-022).
                case EnvelopeError.DocumentTooLarge:
                case EnvelopeError.FieldTooLong:
                case EnvelopeError.StringTooLong:
                case EnvelopeError.ListCountExceeded:
                case EnvelopeError.ListTooLarge:
                    return DiagnosticCode.BudgetExceeded;

                // A shape or corruption mismatch, including the trailing checksum (05 s6).
                default:
                    return DiagnosticCode.ResourceUnavailable;
            }
        }

        /// <summary>Stable textual code for a diagnostic, without allocating a <see cref="Diagnostic"/>.</summary>
        public static string TextOf(DiagnosticCode code) => DiagnosticCodeText.Of(code);
    }
}
