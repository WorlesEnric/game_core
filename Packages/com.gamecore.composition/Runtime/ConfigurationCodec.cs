// GameCore.Composition — canonical codec for configuration documents (P-020, 05 s6).
//
// A configuration document is a canonical map: entries in ascending field-key order, each entry a nested
// envelope document carrying its explicit value kind. Because the kind travels with the value, a reader can
// never interpret a field as the wrong representation, and two writers always produce identical bytes for
// identical content.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Encodes and decodes <see cref="ConfigDocument"/> values as canonical envelopes.</summary>
    public static class ConfigDocumentCodec
    {
        private const int FieldEntries = 1;
        private const int EntryElement = 1;

        private const int EntryKey = 1;
        private const int EntryKind = 2;
        private const int EntryValue = 3;
        private const int EntryIdSet = 4;
        private const int EntryOrderedIds = 5;
        private const int EntryNull = 6;

        /// <summary>Encodes a document. Entries are already in canonical order, so the bytes are canonical.</summary>
        public static FrozenPayload Encode(ConfigDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            List<byte[]> entries = new List<byte[]>(document.Count);
            for (int i = 0; i < document.Count; i++)
            {
                entries.Add(EncodeEntry(document.Fields[i]));
            }

            return DocumentCodec.WritePayload(
                CompositionSchemas.ConfigDocument,
                writer => DocumentCodec.WriteDocumentList(writer, FieldEntries, EntryElement, entries));
        }

        /// <summary>
        /// Canonical content hash of a configuration document: the SHA-256 of its canonical encoding. A caller
        /// that declares a configuration revision also declares this hash, and the applier re-derives it rather
        /// than trusting the declaration (P-020, 05 s4).
        /// </summary>
        public static ContentHash HashOf(ConfigDocument document) => ContentHash.Compute(DocumentCodec.ToBytes(Encode(document)));

        /// <summary>Decodes a document; a structurally invalid document is rejected, never partially applied.</summary>
        public static bool TryDecode(FrozenPayload payload, out ConfigDocument? document)
        {
            document = null;
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (DocumentCodec.Open(payload, CompositionSchemas.ConfigDocument, out EnvelopeReader reader) != DiagnosticCode.None)
            {
                return false;
            }

            if (!reader.TryReadField(out EnvelopeField listField) ||
                !DocumentCodec.Expect(listField, FieldEntries, WireType.List) ||
                !DocumentCodec.TryReadDocumentList(reader, listField, EntryElement, out List<byte[]> entries) ||
                !DocumentCodec.TryClose(reader))
            {
                return false;
            }

            List<ConfigField> fields = new List<ConfigField>(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!TryDecodeEntry(entries[i], out ConfigField field))
                {
                    return false;
                }

                fields.Add(field);
            }

            try
            {
                document = new ConfigDocument(fields);
            }
            catch (ArgumentException)
            {
                // Duplicate keys: the encoding is malformed, not merely surprising.
                return false;
            }

            return true;
        }

        private static byte[] EncodeEntry(ConfigField field)
        {
            ConfigFieldValue value = field.Value;
            return DocumentCodec.Write(
                CompositionSchemas.ConfigEntry,
                writer =>
                {
                    writer.WriteId128Field(EntryKey, field.Key);
                    writer.WriteUInt32Field(EntryKind, (uint)value.Kind);
                    switch (value.Kind)
                    {
                        case ConfigValueKind.Null:
                            writer.WriteNullField(EntryNull);
                            break;
                        case ConfigValueKind.UInt32:
                            writer.WriteUInt32Field(EntryValue, value.AsUInt32);
                            break;
                        case ConfigValueKind.UInt64:
                            writer.WriteUInt64Field(EntryValue, value.AsUInt64);
                            break;
                        case ConfigValueKind.Int32:
                            writer.WriteInt32Field(EntryValue, value.AsInt32);
                            break;
                        case ConfigValueKind.Int64:
                            writer.WriteInt64Field(EntryValue, value.AsInt64);
                            break;
                        case ConfigValueKind.Bool:
                            writer.WriteBoolField(EntryValue, value.AsBool);
                            break;
                        case ConfigValueKind.Id:
                            writer.WriteId128Field(EntryValue, value.AsId);
                            break;
                        case ConfigValueKind.Bytes:
                            writer.WriteBytesField(EntryValue, DocumentCodec.ToBytes(value.AsBytes!));
                            break;
                        case ConfigValueKind.Utf8:
                            writer.WriteUtf8Field(EntryValue, value.AsText);
                            break;
                        case ConfigValueKind.IdSet:
                            DocumentCodec.WriteIdList(writer, EntryIdSet, EntryIdSet, value.AsIds);
                            break;
                        default:
                            DocumentCodec.WriteIdList(writer, EntryOrderedIds, EntryOrderedIds, value.AsIds);
                            break;
                    }
                });
        }

        private static bool TryDecodeEntry(byte[] document, out ConfigField field)
        {
            field = default(ConfigField);
            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(out EnvelopeHeader header) || !header.Schema.Id.Equals(CompositionSchemas.ConfigEntry.Id))
            {
                return false;
            }

            if (!reader.TryReadField(out EnvelopeField keyField) ||
                !DocumentCodec.Expect(keyField, EntryKey, WireType.Id128) ||
                !reader.TryReadId128(keyField, out Id128 key))
            {
                return false;
            }

            if (!reader.TryReadField(out EnvelopeField kindField) ||
                !DocumentCodec.Expect(kindField, EntryKind, WireType.UInt32) ||
                !reader.TryReadUInt32(kindField, out uint rawKind) ||
                rawKind > (uint)ConfigValueKind.OrderedIds)
            {
                return false;
            }

            ConfigValueKind kind = (ConfigValueKind)rawKind;
            if (!reader.TryReadField(out EnvelopeField valueField))
            {
                return false;
            }

            ConfigFieldValue value;
            switch (kind)
            {
                case ConfigValueKind.Null:
                    if (valueField.FieldId != EntryNull || !reader.TryReadNull(valueField))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.Null;
                    break;
                case ConfigValueKind.UInt32:
                    if (valueField.FieldId != EntryValue || !reader.TryReadUInt32(valueField, out uint u32))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfUInt32(u32);
                    break;
                case ConfigValueKind.UInt64:
                    if (valueField.FieldId != EntryValue || !reader.TryReadUInt64(valueField, out ulong u64))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfUInt64(u64);
                    break;
                case ConfigValueKind.Int32:
                    if (valueField.FieldId != EntryValue || !reader.TryReadInt32(valueField, out int i32))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfInt32(i32);
                    break;
                case ConfigValueKind.Int64:
                    if (valueField.FieldId != EntryValue || !reader.TryReadInt64(valueField, out long i64))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfInt64(i64);
                    break;
                case ConfigValueKind.Bool:
                    if (valueField.FieldId != EntryValue || !reader.TryReadBool(valueField, out bool boolean))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfBool(boolean);
                    break;
                case ConfigValueKind.Id:
                    if (valueField.FieldId != EntryValue || !reader.TryReadId128(valueField, out Id128 id))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfId(id);
                    break;
                case ConfigValueKind.Bytes:
                    if (valueField.FieldId != EntryValue || !reader.TryReadBytes(valueField, out byte[]? bytes) || bytes == null)
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfBytes(new FrozenPayload(bytes));
                    break;
                case ConfigValueKind.Utf8:
                    if (valueField.FieldId != EntryValue || !reader.TryReadUtf8(valueField, out string? text) || text == null)
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfText(text);
                    break;
                case ConfigValueKind.IdSet:
                    if (valueField.FieldId != EntryIdSet ||
                        !DocumentCodec.TryReadIdList(reader, valueField, EntryIdSet, out List<Id128> set))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfIdSet(set);
                    break;
                default:
                    if (valueField.FieldId != EntryOrderedIds ||
                        !DocumentCodec.TryReadIdList(reader, valueField, EntryOrderedIds, out List<Id128> ordered))
                    {
                        return false;
                    }

                    value = ConfigFieldValue.OfOrderedIds(ordered);
                    break;
            }

            if (!DocumentCodec.TryClose(reader))
            {
                return false;
            }

            field = new ConfigField(key, value);
            return true;
        }
    }
}
