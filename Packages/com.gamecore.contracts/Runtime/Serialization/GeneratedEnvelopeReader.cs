// GameCore.Contracts - generated-serializer runtime support (GC-003). Normative source:
// docs/game-core/05-contracts-and-data-model.md s6, docs/game-core/00-core-protocols.md P-054 and P-055.
//
// Why these types live in GameCore.Contracts: generated serializers are emitted at build time into a project
// assembly that must work in the player, and the contract assembly is the only runtime-safe, engine-free
// assembly generated code may depend on. The envelope codec itself already lives here, so the walk that
// validates a document against a generated field table belongs beside it. There is no reflection and no
// dynamic type construction anywhere in this path.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>One declared field of a generated schema: explicit field id, wire type and requiredness (05 s6).</summary>
    public readonly struct GeneratedFieldSlot
    {
        public readonly int FieldId;
        public readonly WireType Type;
        public readonly bool Required;

        public GeneratedFieldSlot(int fieldId, WireType type, bool required)
        {
            FieldId = fieldId;
            Type = type;
            Required = required;
        }

        public override string ToString() =>
            "#" + FieldId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Type +
            (Required ? " required" : " optional");
    }

    /// <summary>
    /// Reusable scratch storage for one validation walk: the record offset and decoded header of every present
    /// declared field, in document order. A generated serializer allocates one per read call; nothing is cached
    /// in the serializer instance, so a read is reentrant.
    /// </summary>
    public sealed class GeneratedFieldBuffer
    {
        private int[] recordOffsets;
        private EnvelopeField[] fields;

        public GeneratedFieldBuffer()
        {
            recordOffsets = Array.Empty<int>();
            fields = Array.Empty<EnvelopeField>();
        }

        /// <summary>Number of present declared fields recorded by the last walk.</summary>
        public int Count { get; private set; }

        /// <summary>Absolute document offset of the field record header at <paramref name="index"/>.</summary>
        public int RecordOffset(int index)
        {
            CheckIndex(index);
            return recordOffsets[index];
        }

        /// <summary>Decoded header of the field at <paramref name="index"/>.</summary>
        public EnvelopeField Field(int index)
        {
            CheckIndex(index);
            return fields[index];
        }

        /// <summary>True when a declared field id is present in the recorded set.</summary>
        public bool Contains(int fieldId)
        {
            for (int i = 0; i < Count; i++)
            {
                if (fields[i].FieldId == fieldId)
                {
                    return true;
                }
            }

            return false;
        }

        internal void Reset() => Count = 0;

        internal void Append(int recordOffset, EnvelopeField field)
        {
            if (Count == recordOffsets.Length)
            {
                int capacity = Count == 0 ? 8 : Count * 2;
                int[] grownOffsets = new int[capacity];
                EnvelopeField[] grownFields = new EnvelopeField[capacity];
                if (Count != 0)
                {
                    Array.Copy(recordOffsets, grownOffsets, Count);
                    Array.Copy(fields, grownFields, Count);
                }

                recordOffsets = grownOffsets;
                fields = grownFields;
            }

            recordOffsets[Count] = recordOffset;
            fields[Count] = field;
            Count++;
        }

        private void CheckIndex(int index)
        {
            if (index < 0 || index >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "No declared field was recorded at that index.");
            }
        }
    }

    /// <summary>
    /// One-pass structural validation of a canonical envelope document against a generated field table. It
    /// validates the fixed header, the schema identity and version, the required-feature gate, every field
    /// record's own length, the declared-field set (duplicate and missing required ids) and the trailing
    /// checksum, and records the declared fields so the generated reader can decode them without a second walk
    /// of the unknown-field space (05 s6, P-054, P-055).
    /// </summary>
    public static class GeneratedEnvelopeReader
    {
        /// <summary>
        /// Validates a document and records its declared fields. Unknown field ids are skipped by their declared
        /// length, which is how an explicitly optional extension field stays readable; a duplicate declared
        /// field, a missing required declared field, a mismatched schema, an unknown required feature, a
        /// corrupted checksum or trailing bytes after the checksum record all reject with the exact
        /// <see cref="EnvelopeError"/>.
        /// </summary>
        public static bool TryReadDeclaredFields(
            byte[] document,
            SchemaRef schema,
            IReadOnlyList<Id128> knownFeatureIds,
            IReadOnlyList<GeneratedFieldSlot> declaredSlots,
            GeneratedFieldBuffer buffer,
            out EnvelopeError error)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (knownFeatureIds == null)
            {
                throw new ArgumentNullException(nameof(knownFeatureIds));
            }

            if (declaredSlots == null)
            {
                throw new ArgumentNullException(nameof(declaredSlots));
            }

            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Reset();
            error = EnvelopeError.None;

            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(knownFeatureIds, out EnvelopeHeader header))
            {
                return Fail(reader.LastError, out error);
            }

            if (!header.Schema.Equals(schema))
            {
                return Fail(EnvelopeError.UnsupportedVersion, out error);
            }

            bool[] present = new bool[declaredSlots.Count];
            bool checksumSeen = false;

            while (true)
            {
                if (checksumSeen)
                {
                    if (reader.Position != document.Length)
                    {
                        // The document contains bytes after its checksum record; that is not a valid document.
                        return Fail(EnvelopeError.FieldLengthMismatch, out error);
                    }

                    for (int i = 0; i < declaredSlots.Count; i++)
                    {
                        if (declaredSlots[i].Required && !present[i])
                        {
                            return Fail(EnvelopeError.MissingRequiredField, out error);
                        }
                    }

                    return true;
                }

                int recordOffset = reader.Position;
                if (!reader.TryReadField(out EnvelopeField field))
                {
                    return Fail(reader.LastError, out error);
                }

                int slot = IndexOf(declaredSlots, field.FieldId);
                if (field.IsChecksum)
                {
                    if (!reader.TryVerifyChecksum(field, out ulong _))
                    {
                        return Fail(reader.LastError, out error);
                    }

                    checksumSeen = true;
                    continue;
                }

                if (slot < 0)
                {
                    // Not a declared field: skip by declared length. An undeclared field is optional by
                    // definition on the wire, so skipping it is the documented behaviour (05 s6).
                    if (!reader.Skip(field))
                    {
                        return Fail(reader.LastError, out error);
                    }

                    continue;
                }

                if (present[slot])
                {
                    return Fail(EnvelopeError.DuplicateField, out error);
                }

                if (!ReaderMatchesSlot(field, declaredSlots[slot]))
                {
                    return Fail(EnvelopeError.FieldLengthMismatch, out error);
                }

                if (!reader.Skip(field))
                {
                    return Fail(reader.LastError, out error);
                }

                present[slot] = true;
                buffer.Append(recordOffset, field);
            }
        }

        /// <summary>
        /// Checks that a field's actual wire type is the one the generated schema declares. An explicit null
        /// marker is accepted for a Utf8 or Bytes slot: that marker is how an absent or null value is written, so
        /// the field is present and its value is null.
        /// </summary>
        private static bool ReaderMatchesSlot(EnvelopeField field, GeneratedFieldSlot slot)
        {
            if (field.Type == slot.Type)
            {
                return true;
            }

            return field.Type == WireType.Null && (slot.Type == WireType.Utf8 || slot.Type == WireType.Bytes);
        }

        private static int IndexOf(IReadOnlyList<GeneratedFieldSlot> slots, int fieldId)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].FieldId == fieldId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool Fail(EnvelopeError source, out EnvelopeError error)
        {
            error = source;
            return false;
        }
    }
}
