// GameCore.Contracts - checkpoint document format (GC-018). Normative sources:
// docs/game-core/00-core-protocols.md P-004 (stable identities are what a save records), P-005 (no runtime handle
// is ever persisted), P-049 (recovery creates a new WorldId), P-053 (what a checkpoint contains), P-054 (schema
// ids, integer versions, explicit field ids, canonical byte order, length bounds, reference tables; generated
// serializers and registered directed migrations on an allocated target, never reflected construction) and
// docs/game-core/05-contracts-and-data-model.md s6 (the envelope every document below is written in).
//
// One checkpoint is one canonical envelope document (05 s6) whose payload is a stream of framed records:
//
//   field id 1   Bytes   the header record's own document  (exactly one, first)
//   field id 100 Bytes   one target record document
//   field id 101 Bytes   one scope record document
//   ...                  (field id = FirstRecordFieldId + CheckpointRecordKind ordinal)
//   field id 0   UInt64  the trailing checksum field of the envelope itself
//
// The field id names the record kind, so the stream is self-describing and its kind order is checkable; the
// record document's own envelope header names its schema id, schema version and required features. No field in
// this format carries a native `Entity` index, a `SystemHandle`, a Unity object id or a process-local lease id
// (P-005, P-054): every reference is a stable 128-bit identity.
//
// Each record document is produced and consumed by the generated serializer of its own schema (the GC-003
// content compiler's output for the checkpoint catalog), reached through ICheckpointCodecSet. Nothing here
// constructs a type by reflection, and nothing here allocates a live world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>One kind of framed checkpoint record; the ordinal fixes the record's field id (P-008).</summary>
    public enum CheckpointRecordKind
    {
        /// <summary>Exactly one per document; the document's own identity, counts and position (P-053).</summary>
        Header = 0,

        /// <summary>One stable scope identity with its parent, depth, mode and boundary counts (P-010, P-016).</summary>
        Scope = 1,

        /// <summary>One installation identity, configuration revision and lifecycle state (P-004, P-046).</summary>
        Install = 2,

        /// <summary>One instance-level service selection of an installation (P-011).</summary>
        Selection = 3,

        /// <summary>One target's stable identity, recipe and owning scope (P-004, P-015).</summary>
        Target = 4,

        /// <summary>One authoritative owner state slot, active or dormant (P-032, P-053).</summary>
        Slot = 5,

        /// <summary>One explicit import, opt-in, exclusion, provider override or isolation member (P-013, P-016).</summary>
        Grant = 6,

        /// <summary>One plugin clock declaration or one scheduled wake (P-038, P-053).</summary>
        Clock = 7,

        /// <summary>One admitted, still-unexecuted external command (P-037, P-053).</summary>
        Command = 8,

        /// <summary>One bounded next-step message retained across the boundary (P-043, P-053).</summary>
        Message = 9,

        /// <summary>One deterministic random stream's position (P-008, P-053).</summary>
        Rng = 10,

        /// <summary>One event cursor or per-issuer deduplication high-water mark (P-045, P-050, P-053).</summary>
        Cursor = 11,
    }

    /// <summary>What a capture does with external commands that are queued but not yet executed (P-053, 06 s7).</summary>
    public enum CheckpointQueuePolicy
    {
        /// <summary>Cancel every unexecuted external command before capture, recording the cutoff and the count.</summary>
        RejectQueued = 0,

        /// <summary>Record every unexecuted external command so the restored world re-admits it.</summary>
        IncludeQueued = 1,
    }

    /// <summary>
    /// Stable layout of the checkpoint document. Every constant here is part of the format, not an
    /// implementation detail (P-054): a reader can reproduce the ids without reading a writer.
    /// </summary>
    public static class CheckpointFormat
    {
        /// <summary>Diagnostic name of the format; never a runtime identity (P-004).</summary>
        public const string FormatName = "gamecore.checkpoint/1";

        /// <summary>Protocol major/minor the format is written for (P-055).</summary>
        public const byte ProtocolMajor = 1;

        public const byte ProtocolMinor = 0;

        /// <summary>Stable name of the required feature id every checkpoint document declares (P-055).</summary>
        public const string FeatureStableName = "gamecore.checkpoint.feature.v1";

        /// <summary>
        /// Required feature id of the V1 checkpoint format, derived from <see cref="FeatureStableName"/> by
        /// <see cref="StableNameKeyDerivation.Derive"/> (SHA-256 over the UTF-8 stable name, first 16 digest bytes
        /// as two big-endian 64-bit words). A reader that does not know it rejects the document before decoding.
        /// </summary>
        public static readonly Id128 RequiredFeatureId = new Id128(0x9138C1DCDA9A0A87UL, 0x2360071679D5859AUL);

        /// <summary>Feature ids this build knows; an unknown declared feature rejects (P-055).</summary>
        public static IReadOnlyList<Id128> KnownFeatureIds { get; } =
            Array.AsReadOnly(new[] { RequiredFeatureId });

        /// <summary>Stable name of the container document's schema; a diagnostic label only (P-004).</summary>
        public const string DocumentSchemaStableName = "gamecore.checkpoint.schema.document";

        /// <summary>
        /// Schema of the container document, derived from <see cref="DocumentSchemaStableName"/>; a document whose
        /// header names another schema is a different document.
        /// </summary>
        public static readonly SchemaRef DocumentSchema =
            new SchemaRef(new SchemaId(new Id128(0xA3B7BBB9B0B2F1ADUL, 0xB7918379774F0D08UL)), 1U);

        /// <summary>Field id of the one header record; it is the document's first field (P-053).</summary>
        public const int HeaderFieldId = 1;

        /// <summary>Field id of the first record kind; the kind's ordinal is added to it.</summary>
        public const int FirstRecordFieldId = 100;

        /// <summary>Number of declared record kinds.</summary>
        public const int RecordKindCount = 12;

        /// <summary>Upper bound on the records of one document; a larger document is refused, never truncated.</summary>
        public const int MaxRecordCount = 1 << 20;

        /// <summary>Upper bound on one checkpoint document's bytes (P-022's 128 MiB temporary-storage guardrail).</summary>
        public const int MaxDocumentBytes = 64 * 1024 * 1024;

        /// <summary>Upper bound on one framed record's document bytes.</summary>
        public const int MaxRecordBytes = 4 * 1024 * 1024;

        /// <summary>Bounded read/write limits of one checkpoint document (05 s6, P-054).</summary>
        public static SerializationLimits Limits { get; } = new SerializationLimits(
            MaxDocumentBytes,
            MaxRecordBytes,
            4096,
            MaxRecordCount,
            16 * 1024 * 1024);

        /// <summary>True for a declared kind ordinal; an out-of-range ordinal is a malformed document.</summary>
        public static bool IsDeclared(CheckpointRecordKind kind) =>
            (int)kind >= 0 && (int)kind < RecordKindCount;

        /// <summary>The kind a field id names, or false when the id is not a record field.</summary>
        public static bool TryKindOfField(int fieldId, out CheckpointRecordKind kind)
        {
            int ordinal = fieldId - FirstRecordFieldId;
            if (ordinal < 0 || ordinal >= RecordKindCount)
            {
                kind = default(CheckpointRecordKind);
                return false;
            }

            kind = (CheckpointRecordKind)ordinal;
            return true;
        }

        /// <summary>The field id a record of this kind is framed with.</summary>
        public static int FieldIdOf(CheckpointRecordKind kind) => FirstRecordFieldId + (int)kind;

        /// <summary>Stable stable name of one record kind's schema, for diagnostics and catalog lookup.</summary>
        public static string SchemaStableNameOf(CheckpointRecordKind kind)
        {
            RequireDeclared(kind);
            return "gamecore.checkpoint.schema." + TokenOf(kind);
        }

        /// <summary>One-line diagnostic description of the format; never an identity (P-004).</summary>
        public static string Describe() =>
            FormatName + " protocol=" + ProtocolMajor.ToString(CultureInfo.InvariantCulture) + "."
            + ProtocolMinor.ToString(CultureInfo.InvariantCulture)
            + " records=" + RecordKindCount.ToString(CultureInfo.InvariantCulture);

        private static string TokenOf(CheckpointRecordKind kind)
        {
            switch (kind)
            {
                case CheckpointRecordKind.Header: return "header";
                case CheckpointRecordKind.Scope: return "scope";
                case CheckpointRecordKind.Install: return "install";
                case CheckpointRecordKind.Selection: return "selection";
                case CheckpointRecordKind.Target: return "target";
                case CheckpointRecordKind.Slot: return "slot";
                case CheckpointRecordKind.Grant: return "grant";
                case CheckpointRecordKind.Clock: return "clock";
                case CheckpointRecordKind.Command: return "command";
                case CheckpointRecordKind.Message: return "message";
                case CheckpointRecordKind.Rng: return "rng";
                case CheckpointRecordKind.Cursor: return "cursor";
                default: return "unknown";
            }
        }

        private static void RequireDeclared(CheckpointRecordKind kind)
        {
            if (!IsDeclared(kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown checkpoint record kind.");
            }
        }
    }
}
