// GameCore.Execution.Messages — typed ports without reflection (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-042 (a `Request` is a typed transient message routed to a
// named owner and consumer step) and docs/game-core/04-unity-integration.md s8 (IL2CPP forbids runtime type
// construction, so the executable type set and every payload reader are known at build time; a missing reader is
// reported, never replaced by reflection or a generic fallback).
//
// A generated registration supplies one reader per payload schema. The reader registry resolves a schema to the
// typed reader instance the generator emitted, so an owner decodes a bounded payload with no reflection, no
// assembly scan and no boxing of the payload itself.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>A generated reader for one payload schema of one route port.</summary>
    public interface ICommandPayloadReader<T>
    {
        /// <summary>The exact schema/version this reader decodes; a mismatch is refused before decoding (P-006).</summary>
        SchemaRef Schema { get; }

        /// <summary>Decodes one bounded payload. The reader never reads outside the supplied bytes.</summary>
        T Read(IReadOnlyList<byte> payload);
    }

    /// <summary>What a decode attempt produced.</summary>
    public enum PayloadDecodeOutcome
    {
        /// <summary>The value was decoded.</summary>
        Decoded = 0,

        /// <summary>No generated reader covers this schema/version (P-009, 04 s8).</summary>
        NoReader = 1,

        /// <summary>A reader exists for the schema id but not for this version.</summary>
        VersionMismatch = 2,

        /// <summary>The reader itself refused the bytes (malformed or truncated payload).</summary>
        Malformed = 3,
    }

    /// <summary>
    /// The generated payload-reader registry of one world. Lookup is by schema identity; the value is a typed reader
    /// instance, and a miss is an explicit refusal rather than a reflective attempt.
    /// </summary>
    public sealed class CommandPayloadReaders
    {
        private readonly Dictionary<Id128, Entry> readers = new Dictionary<Id128, Entry>();

        public int Count => readers.Count;

        /// <summary>Registers one generated reader. A duplicate schema/version is a registration defect (P-039).</summary>
        public bool TryBind<T>(ICommandPayloadReader<T> reader, out string failure)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (reader.Schema.Id.Value.IsDefault)
            {
                failure = "a default zero payload schema is not a generated reader identity (P-004)";
                return false;
            }

            if (readers.TryGetValue(reader.Schema.Id.Value, out Entry existing) && existing.Version == reader.Schema.Version)
            {
                failure = "payload schema " + reader.Schema.ToString()
                    + " already has a generated reader; one reader per schema/version (04 s8)";
                return false;
            }

            readers[reader.Schema.Id.Value] = new Entry(reader.Schema.Version, reader);
            failure = string.Empty;
            return true;
        }

        /// <summary>True when a generated reader covers this exact schema/version.</summary>
        public bool CanRead(SchemaRef schema)
            => readers.TryGetValue(schema.Id.Value, out Entry entry) && entry.Version == schema.Version;

        /// <summary>
        /// Decodes one payload with its generated reader. A missing reader, a version mismatch and a reader refusal
        /// stay distinguishable, so a caller never treats a decode failure as an empty payload (04 s8).
        /// </summary>
        public PayloadDecodeOutcome TryRead<T>(SchemaRef schema, IReadOnlyList<byte>? payload, out T value, out string detail)
        {
            value = default(T)!;
            if (!readers.TryGetValue(schema.Id.Value, out Entry entry))
            {
                detail = "no generated reader is registered for payload schema " + schema.ToString()
                    + "; a missing reader is reported, never substituted reflectively (04 s8)";
                return PayloadDecodeOutcome.NoReader;
            }

            if (entry.Version != schema.Version)
            {
                detail = "the generated reader for " + schema + " decodes version "
                    + entry.Version.ToString(CultureInfo.InvariantCulture) + ", not " + schema.Version.ToString(CultureInfo.InvariantCulture);
                return PayloadDecodeOutcome.VersionMismatch;
            }

            if (entry.Reader is not ICommandPayloadReader<T> typed)
            {
                detail = "the generated reader for " + schema.ToString()
                    + " returns another payload type; a route is typed, so a mismatch is refused (P-042)";
                return PayloadDecodeOutcome.VersionMismatch;
            }

            try
            {
                value = typed.Read(payload ?? Array.Empty<byte>());
                detail = string.Empty;
                return PayloadDecodeOutcome.Decoded;
            }
            catch (Exception exception)
            {
                detail = "the generated reader for " + schema.ToString() + " refused the payload: "
                    + exception.GetType().FullName + ": " + exception.Message;
                return PayloadDecodeOutcome.Malformed;
            }
        }

        private readonly struct Entry
        {
            internal readonly uint Version;
            internal readonly object Reader;

            internal Entry(uint version, object reader)
            {
                Version = version;
                Reader = reader;
            }
        }
    }

    /// <summary>
    /// Appends a little-endian integer field stream; the generated readers decode exactly what this writes, so a
    /// fixture can build a payload without a serializer assembly.
    /// </summary>
    public sealed class PayloadWriter
    {
        private readonly List<byte> bytes = new List<byte>();

        public int Length => bytes.Count;

        public PayloadWriter WriteInt32(int value)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
            return this;
        }

        public PayloadWriter WriteUInt64(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                bytes.Add((byte)(value >> (i * 8)));
            }

            return this;
        }

        public byte[] ToArray() => bytes.ToArray();

        public IReadOnlyList<byte> ToPayload() => bytes;
    }

    /// <summary>The matching bounded reader for <see cref="PayloadWriter"/>'s layout.</summary>
    public sealed class PayloadReader
    {
        private readonly IReadOnlyList<byte> bytes;

        public PayloadReader(IReadOnlyList<byte> bytes)
        {
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public int Length => bytes.Count;

        public int ReadInt32(int offset)
        {
            Require(offset, 4);
            return bytes[offset]
                | (bytes[offset + 1] << 8)
                | (bytes[offset + 2] << 16)
                | (bytes[offset + 3] << 24);
        }

        public ulong ReadUInt64(int offset)
        {
            Require(offset, 8);
            ulong value = 0UL;
            for (int i = 0; i < 8; i++)
            {
                value |= (ulong)bytes[offset + i] << (i * 8);
            }

            return value;
        }

        private void Require(int offset, int length)
        {
            if (offset < 0 || offset + length > bytes.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(offset),
                    "the payload holds " + bytes.Count.ToString(CultureInfo.InvariantCulture)
                    + " byte(s); " + length.ToString(CultureInfo.InvariantCulture)
                    + " byte(s) from offset " + offset.ToString(CultureInfo.InvariantCulture) + " were requested");
            }
        }
    }
}
