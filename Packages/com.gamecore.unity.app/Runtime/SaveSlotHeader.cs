// GameCore.Unity.App - the JSON header written next to every save slot (SADR-012 (studio), catalog row 12).
//
// A save slot is two files written atomically: `<slot>.gcc`, the checkpoint envelope the kernel's file store writes
// (P-053, its own checksums), and `<slot>.json`, this header. The header is what a save menu and a Studio inspection
// read without decoding the checkpoint: the game, the catalog fingerprint the slot was captured under, the slot schema
// versions, the region, the play time, the wall-clock timestamp, an optional thumbnail path, and the document hash
// that ties the header to exactly one checkpoint document.
//
// The file is deliberately Unity-free and dependency-free (System only), so it builds under plain dotnet and its
// parser is tested there. The JSON is written in one canonical form (fixed key order, invariant culture, no
// insignificant whitespace beyond the indentation below) and read by a small strict parser: an unknown key is
// ignored for forward compatibility, a missing required key or a malformed value refuses.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Unity.App
{
    /// <summary>One slot schema and the version the slot was written at (P-032, P-054).</summary>
    public readonly struct SaveSchemaVersion
    {
        public SaveSchemaVersion(string schema, uint version)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            Version = version;
        }

        /// <summary>The schema's stable name (a stable name such as `owner.slot-schema`).</summary>
        public string Schema { get; }

        public uint Version { get; }

        public override string ToString() => Schema + "@" + Version.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The player-facing name of a save slot, which is also its file stem (SADR-012).</summary>
    public static class SaveSlotNames
    {
        public const int MaxLength = 64;

        /// <summary>
        /// True for a lowercase ASCII name of letters, digits, '-' and '_' that starts with a letter or digit and is
        /// at most <see cref="MaxLength"/> characters: a name that is a safe file stem on every platform.
        /// </summary>
        public static bool IsValid(string? slot)
        {
            if (string.IsNullOrEmpty(slot) || slot!.Length > MaxLength)
            {
                return false;
            }

            for (int i = 0; i < slot.Length; i++)
            {
                char c = slot[i];
                bool alnum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                if (alnum)
                {
                    continue;
                }

                if (i == 0 || (c != '-' && c != '_'))
                {
                    return false;
                }
            }

            return true;
        }

        public static string Describe(string? slot) =>
            "slot names are 1-" + MaxLength.ToString(CultureInfo.InvariantCulture)
            + " characters of a-z, 0-9, '-' and '_', starting with a letter or digit; received '" + (slot ?? "null") + "'";
    }

    /// <summary>The JSON header of one save slot (SADR-012).</summary>
    public sealed class SaveSlotHeader
    {
        /// <summary>Header format this build writes; a newer format refuses rather than being half read.</summary>
        public const int CurrentFormat = 1;

        private readonly SaveSchemaVersion[] schemaVersions;

        public SaveSlotHeader(
            string slot,
            string gameId,
            string catalogFingerprint,
            IReadOnlyList<SaveSchemaVersion>? schemaVersions,
            string regionId,
            double playTimeSeconds,
            DateTime savedAtUtc,
            string? thumbnailPath,
            ulong logicalStep,
            string documentHash,
            long documentBytes,
            bool temporalContinuity,
            int format = CurrentFormat)
        {
            if (!SaveSlotNames.IsValid(slot))
            {
                throw new ArgumentException(SaveSlotNames.Describe(slot), nameof(slot));
            }

            if (string.IsNullOrEmpty(gameId))
            {
                throw new ArgumentException("A save header names its game.", nameof(gameId));
            }

            if (!IsHex(catalogFingerprint, 64))
            {
                throw new ArgumentException("The catalog fingerprint is 64 lowercase hex characters.", nameof(catalogFingerprint));
            }

            if (!IsHex(documentHash, 64))
            {
                throw new ArgumentException("The document hash is 64 lowercase hex characters.", nameof(documentHash));
            }

            if (double.IsNaN(playTimeSeconds) || double.IsInfinity(playTimeSeconds) || playTimeSeconds < 0.0)
            {
                throw new ArgumentOutOfRangeException(nameof(playTimeSeconds), "Play time is finite and non-negative.");
            }

            if (documentBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(documentBytes), "A save carries a non-empty document.");
            }

            if (format <= 0 || format > CurrentFormat)
            {
                throw new ArgumentOutOfRangeException(nameof(format), "Header format " + format.ToString(CultureInfo.InvariantCulture) + " is not readable by this build.");
            }

            Slot = slot;
            GameId = gameId;
            CatalogFingerprint = catalogFingerprint;
            this.schemaVersions = schemaVersions == null ? Array.Empty<SaveSchemaVersion>() : Copy(schemaVersions);
            RegionId = regionId ?? string.Empty;
            PlayTimeSeconds = playTimeSeconds;
            SavedAtUtc = savedAtUtc.Kind == DateTimeKind.Utc ? savedAtUtc : savedAtUtc.ToUniversalTime();
            ThumbnailPath = string.IsNullOrEmpty(thumbnailPath) ? null : thumbnailPath;
            LogicalStep = logicalStep;
            DocumentHash = documentHash;
            DocumentBytes = documentBytes;
            TemporalContinuity = temporalContinuity;
            Format = format;
        }

        public int Format { get; }

        public string Slot { get; }

        public string GameId { get; }

        /// <summary>Lowercase hex of the catalog fingerprint the slot was captured under (P-028).</summary>
        public string CatalogFingerprint { get; }

        public IReadOnlyList<SaveSchemaVersion> SchemaVersions => schemaVersions;

        public string RegionId { get; }

        public double PlayTimeSeconds { get; }

        public DateTime SavedAtUtc { get; }

        public string? ThumbnailPath { get; }

        /// <summary>The logical step the slot was captured at.</summary>
        public ulong LogicalStep { get; }

        /// <summary>Lowercase hex of the checkpoint document's content hash; ties this header to one document.</summary>
        public string DocumentHash { get; }

        public long DocumentBytes { get; }

        /// <summary>True when the document declares the SADR-012 temporal-continuity feature.</summary>
        public bool TemporalContinuity { get; }

        /// <summary>The header as canonical JSON.</summary>
        public string ToJson()
        {
            var builder = new StringBuilder(512);
            builder.Append("{\n");
            Field(builder, "format", Format.ToString(CultureInfo.InvariantCulture));
            Field(builder, "slot", Quote(Slot));
            Field(builder, "gameId", Quote(GameId));
            Field(builder, "catalogFingerprint", Quote(CatalogFingerprint));
            builder.Append("  \"schemaVersions\": [");
            for (int i = 0; i < schemaVersions.Length; i++)
            {
                builder.Append(i == 0 ? "\n" : ",\n");
                builder.Append("    { \"schema\": ").Append(Quote(schemaVersions[i].Schema))
                    .Append(", \"version\": ").Append(schemaVersions[i].Version.ToString(CultureInfo.InvariantCulture))
                    .Append(" }");
            }

            builder.Append(schemaVersions.Length == 0 ? "],\n" : "\n  ],\n");
            Field(builder, "regionId", Quote(RegionId));
            Field(builder, "playTimeSeconds", PlayTimeSeconds.ToString("R", CultureInfo.InvariantCulture));
            Field(builder, "savedAtUtc", Quote(SavedAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)));
            Field(builder, "thumbnailPath", ThumbnailPath == null ? "null" : Quote(ThumbnailPath));
            Field(builder, "logicalStep", LogicalStep.ToString(CultureInfo.InvariantCulture));
            Field(builder, "documentHash", Quote(DocumentHash));
            Field(builder, "documentBytes", DocumentBytes.ToString(CultureInfo.InvariantCulture));
            builder.Append("  \"temporalContinuity\": ").Append(TemporalContinuity ? "true" : "false").Append('\n');
            builder.Append("}\n");
            return builder.ToString();
        }

        /// <summary>Parses a header; false with a reason for malformed JSON, a missing key or an unreadable format.</summary>
        public static bool TryParse(string? json, out SaveSlotHeader? header, out string error)
        {
            header = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "the save header is empty";
                return false;
            }

            object? root;
            try
            {
                var parser = new JsonReader(json!);
                root = parser.ReadDocument();
            }
            catch (FormatException exception)
            {
                error = "the save header is not valid JSON: " + exception.Message;
                return false;
            }

            if (!(root is Dictionary<string, object?> map))
            {
                error = "the save header is not a JSON object";
                return false;
            }

            try
            {
                int format = (int)RequireInteger(map, "format", 1, int.MaxValue);
                if (format > CurrentFormat)
                {
                    error = "the save header has format " + format.ToString(CultureInfo.InvariantCulture)
                        + " and this build reads format " + CurrentFormat.ToString(CultureInfo.InvariantCulture)
                        + "; the save was written by a newer build";
                    return false;
                }

                var versions = new List<SaveSchemaVersion>();
                if (map.TryGetValue("schemaVersions", out object? rawVersions) && rawVersions != null)
                {
                    if (!(rawVersions is List<object?> list))
                    {
                        throw new FormatException("\"schemaVersions\" is not an array");
                    }

                    for (int i = 0; i < list.Count; i++)
                    {
                        if (!(list[i] is Dictionary<string, object?> entry))
                        {
                            throw new FormatException("schemaVersions[" + i.ToString(CultureInfo.InvariantCulture) + "] is not an object");
                        }

                        versions.Add(new SaveSchemaVersion(
                            RequireString(entry, "schema"),
                            (uint)RequireInteger(entry, "version", 0, uint.MaxValue)));
                    }
                }

                string saved = RequireString(map, "savedAtUtc");
                if (!DateTime.TryParse(
                        saved,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                        out DateTime savedAt))
                {
                    throw new FormatException("\"savedAtUtc\" is not an ISO-8601 timestamp: " + saved);
                }

                string? thumbnail = null;
                if (map.TryGetValue("thumbnailPath", out object? rawThumbnail) && rawThumbnail != null)
                {
                    thumbnail = rawThumbnail as string ?? throw new FormatException("\"thumbnailPath\" is not a string");
                }

                header = new SaveSlotHeader(
                    RequireString(map, "slot"),
                    RequireString(map, "gameId"),
                    RequireString(map, "catalogFingerprint"),
                    versions,
                    OptionalString(map, "regionId"),
                    RequireNumber(map, "playTimeSeconds"),
                    DateTime.SpecifyKind(savedAt, DateTimeKind.Utc),
                    thumbnail,
                    (ulong)RequireInteger(map, "logicalStep", 0, decimal.MaxValue),
                    RequireString(map, "documentHash"),
                    (long)RequireInteger(map, "documentBytes", 1, long.MaxValue),
                    RequireBool(map, "temporalContinuity"),
                    format);
                error = string.Empty;
                return true;
            }
            catch (FormatException exception)
            {
                error = "the save header is malformed: " + exception.Message;
                return false;
            }
            catch (ArgumentException exception)
            {
                error = "the save header is malformed: " + exception.Message;
                return false;
            }
        }

        public override string ToString() =>
            "save(" + Slot + "," + GameId + ",step=" + LogicalStep.ToString(CultureInfo.InvariantCulture)
            + ",at=" + SavedAtUtc.ToString("o", CultureInfo.InvariantCulture) + ")";

        private static SaveSchemaVersion[] Copy(IReadOnlyList<SaveSchemaVersion> source)
        {
            var copy = new SaveSchemaVersion[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        private static void Field(StringBuilder builder, string key, string value) =>
            builder.Append("  \"").Append(key).Append("\": ").Append(value).Append(",\n");

        private static string Quote(string value)
        {
            var builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(c);
                        }

                        break;
                }
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool IsHex(string? value, int length)
        {
            if (value == null || value.Length != length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                {
                    return false;
                }
            }

            return true;
        }

        private static string RequireString(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || !(value is string text))
            {
                throw new FormatException("required string \"" + key + "\" is missing");
            }

            return text;
        }

        private static string OptionalString(Dictionary<string, object?> map, string key) =>
            map.TryGetValue(key, out object? value) && value is string text ? text : string.Empty;

        private static bool RequireBool(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || !(value is bool flag))
            {
                throw new FormatException("required boolean \"" + key + "\" is missing");
            }

            return flag;
        }

        private static double RequireNumber(Dictionary<string, object?> map, string key)
        {
            if (!map.TryGetValue(key, out object? value) || !(value is JsonNumber number))
            {
                throw new FormatException("required number \"" + key + "\" is missing");
            }

            if (!double.TryParse(number.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                throw new FormatException("\"" + key + "\" is not a number");
            }

            return parsed;
        }

        private static decimal RequireInteger(Dictionary<string, object?> map, string key, decimal min, decimal max)
        {
            if (!map.TryGetValue(key, out object? value) || !(value is JsonNumber number))
            {
                throw new FormatException("required integer \"" + key + "\" is missing");
            }

            if (!decimal.TryParse(number.Text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal parsed)
                || parsed < min
                || parsed > max)
            {
                throw new FormatException("\"" + key + "\" is not an integer in [" + min.ToString(CultureInfo.InvariantCulture)
                    + ", " + max.ToString(CultureInfo.InvariantCulture) + "]");
            }

            return parsed;
        }

        /// <summary>A JSON number kept as its source text, so integers never lose precision through a double.</summary>
        private sealed class JsonNumber
        {
            public JsonNumber(string text)
            {
                Text = text;
            }

            public string Text { get; }
        }

        /// <summary>A strict, small JSON reader: objects, arrays, strings, numbers, true, false, null.</summary>
        private sealed class JsonReader
        {
            private const int MaxDepth = 32;
            private readonly string text;
            private int position;

            public JsonReader(string text)
            {
                this.text = text;
            }

            public object? ReadDocument()
            {
                object? value = ReadValue(0);
                SkipWhitespace();
                if (position != text.Length)
                {
                    throw new FormatException("unexpected content after the document at offset " + position.ToString(CultureInfo.InvariantCulture));
                }

                return value;
            }

            private object? ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw new FormatException("nesting deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture));
                }

                SkipWhitespace();
                if (position >= text.Length)
                {
                    throw new FormatException("unexpected end of input");
                }

                char c = text[position];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"': return ReadString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return ReadNumber();
                        }

                        throw new FormatException("unexpected character '" + c + "' at offset " + position.ToString(CultureInfo.InvariantCulture));
                }
            }

            private Dictionary<string, object?> ReadObject(int depth)
            {
                var map = new Dictionary<string, object?>(StringComparer.Ordinal);
                position++;
                SkipWhitespace();
                if (Peek() == '}')
                {
                    position++;
                    return map;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (Peek() != '"')
                    {
                        throw new FormatException("expected a key at offset " + position.ToString(CultureInfo.InvariantCulture));
                    }

                    string key = ReadString();
                    SkipWhitespace();
                    if (Peek() != ':')
                    {
                        throw new FormatException("expected ':' at offset " + position.ToString(CultureInfo.InvariantCulture));
                    }

                    position++;
                    object? value = ReadValue(depth + 1);
                    if (map.ContainsKey(key))
                    {
                        throw new FormatException("duplicate key \"" + key + "\"");
                    }

                    map.Add(key, value);
                    SkipWhitespace();
                    char next = Peek();
                    position++;
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == '}')
                    {
                        return map;
                    }

                    throw new FormatException("expected ',' or '}' at offset " + (position - 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            private List<object?> ReadArray(int depth)
            {
                var list = new List<object?>();
                position++;
                SkipWhitespace();
                if (Peek() == ']')
                {
                    position++;
                    return list;
                }

                while (true)
                {
                    list.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    char next = Peek();
                    position++;
                    if (next == ',')
                    {
                        continue;
                    }

                    if (next == ']')
                    {
                        return list;
                    }

                    throw new FormatException("expected ',' or ']' at offset " + (position - 1).ToString(CultureInfo.InvariantCulture));
                }
            }

            private string ReadString()
            {
                position++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (position >= text.Length)
                    {
                        throw new FormatException("unterminated string");
                    }

                    char c = text[position++];
                    if (c == '"')
                    {
                        return builder.ToString();
                    }

                    if (c < 0x20)
                    {
                        throw new FormatException("control character inside a string");
                    }

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (position >= text.Length)
                    {
                        throw new FormatException("unterminated escape");
                    }

                    char escape = text[position++];
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (position + 4 > text.Length
                                || !int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw new FormatException("malformed \\u escape");
                            }

                            builder.Append((char)code);
                            position += 4;
                            break;
                        default:
                            throw new FormatException("unknown escape '\\" + escape + "'");
                    }
                }
            }

            private JsonNumber ReadNumber()
            {
                int start = position;
                if (Peek() == '-')
                {
                    position++;
                }

                while (position < text.Length)
                {
                    char c = text[position];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-')
                    {
                        position++;
                        continue;
                    }

                    break;
                }

                string number = text.Substring(start, position - start);
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double _))
                {
                    throw new FormatException("malformed number '" + number + "'");
                }

                return new JsonNumber(number);
            }

            private void Expect(string literal)
            {
                if (position + literal.Length > text.Length
                    || string.CompareOrdinal(text, position, literal, 0, literal.Length) != 0)
                {
                    throw new FormatException("expected '" + literal + "' at offset " + position.ToString(CultureInfo.InvariantCulture));
                }

                position += literal.Length;
            }

            private char Peek() => position < text.Length ? text[position] : '\0';

            private void SkipWhitespace()
            {
                while (position < text.Length)
                {
                    char c = text[position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        position++;
                        continue;
                    }

                    break;
                }
            }
        }
    }
}
