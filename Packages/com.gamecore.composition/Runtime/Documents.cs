// GameCore.Composition — canonical document codec (GC-004).
//
// Every document this assembly puts on the wire (edit requests, config documents, config patches, service
// declarations, fingerprints) is a canonical envelope (docs/game-core/05-contracts-and-data-model.md s6):
// fixed magic/version header carrying a schema id, explicit (fieldId, wireType, byteLength) records in a
// fixed order, big-endian scalars, 128-bit ids as high/low words, and a trailing checksum. Field order is
// fixed by the writer and the reader rejects anything unexpected, so a document can never be interpreted two
// ways (P-054). Nested documents are embedded as a Bytes field holding a complete envelope document, and
// lists are a checked list header followed by one Bytes field per element.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// Stable schema identities of the composition host's own canonical documents. Ids are derived
    /// deterministically from a reverse-domain document name, so the same name always yields the same
    /// identity on every host and in every build (P-004, P-054). Generated catalog schemas never reuse these.
    /// </summary>
    public static class CompositionSchemas
    {
        /// <summary>One admitted composition edit request (O-02 to O-08 edit payload).</summary>
        public static SchemaRef EditRequest { get; } = Document("GameCore.Composition.EditRequest");

        /// <summary>One installation declaration inside a mount edit (05 s3 InstallationSpec).</summary>
        public static SchemaRef InstallationSpec { get; } = Document("GameCore.Composition.InstallationSpec");

        /// <summary>One explicit instance service selection (P-011).</summary>
        public static SchemaRef ServiceSelection { get; } = Document("GameCore.Composition.ServiceSelection");

        /// <summary>One named service/capability isolation set (P-016).</summary>
        public static SchemaRef IsolationSet { get; } = Document("GameCore.Composition.IsolationSet");

        /// <summary>One exclusion rule (P-016).</summary>
        public static SchemaRef ExclusionRule { get; } = Document("GameCore.Composition.ExclusionRule");

        /// <summary>One Conservative-mode capability grant of a scope (P-013).</summary>
        public static SchemaRef CapabilityGrant { get; } = Document("GameCore.Composition.CapabilityGrant");

        /// <summary>An immutable configuration document: a canonical, field-keyed value map (P-020).</summary>
        public static SchemaRef ConfigDocument { get; } = Document("GameCore.Composition.ConfigDocument");

        /// <summary>One field entry of a configuration document (P-020); nested inside <see cref="ConfigDocument"/>.</summary>
        public static SchemaRef ConfigEntry { get; } = Document("GameCore.Composition.ConfigEntry");

        /// <summary>Canonical semantic fingerprint of a desired composition definition (P-027 plan identity).</summary>
        public static SchemaRef DefinitionFingerprint { get; } = Document("GameCore.Composition.DefinitionFingerprint");

        /// <summary>
        /// Canonical input hash document of one cancellation request (O-18): the target operation identity plus
        /// the cancellation domain tag, so a cancellation id can never alias an edit request (P-050).
        /// </summary>
        public static SchemaRef CancellationRequest { get; } = Document("GameCore.Composition.CancellationRequest");

        /// <summary>Canonical schema version of every document in this table.</summary>
        public const uint CurrentVersion = 1U;

        /// <summary>
        /// Deterministic 128-bit identity of a document name: the first 16 bytes of the SHA-256 of its UTF-8
        /// spelling, read as a canonical big-endian id (05 s2, P-054). Never a platform-formatted Guid.
        /// </summary>
        public static Id128 NameId(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            byte[] digest = ContentHash.Compute(Encoding.UTF8.GetBytes(name)).ToArray();
            return Id128Codec.ReadBigEndian(digest, 0);
        }

        private static SchemaRef Document(string name) => new SchemaRef(new SchemaId(NameId(name)), CurrentVersion);
    }

    /// <summary>
    /// Read/write helpers for canonical composition documents. The codec is deliberately strict: an unknown
    /// schema id or a newer schema version is rejected before any field is used, and every field record must
    /// match the id and wire type the reader expects (P-054).
    /// </summary>
    public static class DocumentCodec
    {
        /// <summary>Copies a frozen payload into the byte array the envelope reader requires.</summary>
        public static byte[] ToBytes(FrozenPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            byte[] copy = new byte[payload.Length];
            IReadOnlyList<byte> source = payload.Bytes;
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        /// <summary>Copies a document byte array into host-owned frozen memory.</summary>
        public static FrozenPayload ToPayload(byte[] document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            return new FrozenPayload(document);
        }

        /// <summary>Writes one complete canonical document, checksum included.</summary>
        public static byte[] Write(SchemaRef schema, Action<EnvelopeWriter> body)
        {
            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            EnvelopeWriter writer = new EnvelopeWriter(schema);
            body(writer);
            writer.WriteChecksum();
            return writer.ToArray();
        }

        /// <summary>Writes one complete canonical document as a frozen payload.</summary>
        public static FrozenPayload WritePayload(SchemaRef schema, Action<EnvelopeWriter> body) =>
            ToPayload(Write(schema, body));

        /// <summary>
        /// Opens a document and positions the reader after its header. The returned reader is only valid when
        /// the returned code is <see cref="DiagnosticCode.None"/>; a foreign schema id or a newer schema version
        /// is <see cref="DiagnosticCode.UnsupportedVersion"/> and a structurally broken document keeps that code
        /// because the caller must not act on any part of it.
        /// </summary>
        public static DiagnosticCode Open(FrozenPayload payload, SchemaRef schema, out EnvelopeReader reader)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            reader = new EnvelopeReader(ToBytes(payload));
            if (!reader.TryReadHeader(out EnvelopeHeader header))
            {
                return DiagnosticCode.UnsupportedVersion;
            }

            if (!header.Schema.Id.Equals(schema.Id) || header.Schema.Version > schema.Version)
            {
                return DiagnosticCode.UnsupportedVersion;
            }

            return DiagnosticCode.None;
        }

        /// <summary>True when the record is exactly the expected field id and wire type.</summary>
        public static bool Expect(EnvelopeField field, int fieldId, WireType type) =>
            field.FieldId == fieldId && field.Type == type;

        /// <summary>
        /// Reads a list header plus its element documents. The declared payload byte length is re-checked
        /// against the bytes actually consumed, so a truncated or oversized list cannot be half-read.
        /// </summary>
        public static bool TryReadDocumentList(
            EnvelopeReader reader,
            EnvelopeField listField,
            int elementFieldId,
            out List<byte[]> elements)
        {
            elements = new List<byte[]>();
            if (listField.Type != WireType.List)
            {
                return false;
            }

            if (!reader.TryReadListCount(listField, out int count, out int declaredBytes))
            {
                return false;
            }

            int start = reader.Position;
            for (int i = 0; i < count; i++)
            {
                if (!reader.TryReadField(out EnvelopeField element))
                {
                    return false;
                }

                if (element.FieldId != elementFieldId || element.Type != WireType.Bytes)
                {
                    return false;
                }

                if (!reader.TryReadBytes(element, out byte[]? bytes) || bytes == null)
                {
                    return false;
                }

                elements.Add(bytes);
            }

            return reader.Position - start == declaredBytes;
        }

        /// <summary>Writes a checked list of element documents with a computed payload byte length.</summary>
        public static void WriteDocumentList(
            EnvelopeWriter writer,
            int headerFieldId,
            int elementFieldId,
            IReadOnlyList<byte[]> elements)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            if (elements == null)
            {
                throw new ArgumentNullException(nameof(elements));
            }

            int payloadBytes = 0;
            for (int i = 0; i < elements.Count; i++)
            {
                payloadBytes += EnvelopeFormat.FieldHeaderSize + elements[i].Length;
            }

            writer.WriteListHeader(headerFieldId, elements.Count, payloadBytes);
            for (int i = 0; i < elements.Count; i++)
            {
                writer.WriteBytesField(elementFieldId, elements[i]);
            }
        }

        /// <summary>Writes a checked list of 128-bit ids as Id128 element fields.</summary>
        public static void WriteIdList(EnvelopeWriter writer, int headerFieldId, int elementFieldId, IReadOnlyList<Id128>? ids)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            IReadOnlyList<Id128> list = ids ?? Array.Empty<Id128>();
            writer.WriteListHeader(
                headerFieldId,
                list.Count,
                list.Count * (EnvelopeFormat.FieldHeaderSize + Id128.SizeInBytes));
            for (int i = 0; i < list.Count; i++)
            {
                writer.WriteId128Field(elementFieldId, list[i]);
            }
        }

        /// <summary>Reads a checked list of Id128 element fields and re-checks the declared payload length.</summary>
        public static bool TryReadIdList(
            EnvelopeReader reader,
            EnvelopeField listField,
            int elementFieldId,
            out List<Id128> ids)
        {
            ids = new List<Id128>();
            if (listField.Type != WireType.List)
            {
                return false;
            }

            if (!reader.TryReadListCount(listField, out int count, out int declaredBytes))
            {
                return false;
            }

            int start = reader.Position;
            for (int i = 0; i < count; i++)
            {
                if (!reader.TryReadField(out EnvelopeField element) || element.FieldId != elementFieldId)
                {
                    return false;
                }

                if (!reader.TryReadId128(element, out Id128 value))
                {
                    return false;
                }

                ids.Add(value);
            }

            return reader.Position - start == declaredBytes;
        }

        /// <summary>
        /// Verifies the trailing checksum record of an open document. The reader must be positioned at that
        /// record, which must also be the last field of the document.
        /// </summary>
        public static bool TryClose(EnvelopeReader reader)
        {
            if (!reader.TryReadField(out EnvelopeField checksum))
            {
                return false;
            }

            return reader.TryVerifyChecksum(checksum, out ulong declared);
        }
    }

    /// <summary>
    /// Canonical ordering helpers. Every sort in this assembly uses a total order whose final tie breaker is a
    /// stable identity, so registration timing, dictionary enumeration and insertion order can never decide an
    /// outcome (P-008). List.Sort is not stable, which is exactly why the comparators must be total.
    /// </summary>
    public static class CanonicalOrder
    {
        /// <summary>Canonical big-endian identity order (high word, then low word).</summary>
        public static int Compare(Id128 left, Id128 right) => left.CompareTo(right);

        /// <summary>Returns a sorted frozen copy; null or empty yields an empty list.</summary>
        public static IReadOnlyList<T> Sort<T>(IReadOnlyList<T>? source, Comparison<T> comparison)
        {
            if (source == null || source.Count == 0)
            {
                return Array.Empty<T>();
            }

            List<T> copy = new List<T>(source);
            copy.Sort(comparison);
            return ContractCollections.Freeze(copy);
        }
    }
}
