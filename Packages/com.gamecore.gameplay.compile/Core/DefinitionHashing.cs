// GameCore.Gameplay.Compile - definition content hashing (P1.1).
//
// A definition's content hash is SHA-256 over the canonical text of its authorable fields - never over the asset's
// YAML, whose layout, line endings and serialization order are Unity's business. The editor reflects the fields marked
// [AuthorField]/[AuthorRef] and formats each value canonically (integers in invariant culture, floats round-trip,
// booleans as true/false, references by authoring id or asset GUID); this type only orders and hashes them:
//
//   gamecore.gameplay.definition/1 LF
//   type=<object type id> LF
//   <field name>=<escaped value> LF        (one line per field, ordinal field-name order)
//
// The definition revision is the first eight digest bytes read big-endian, so one field change changes exactly that
// definition's revision and nothing else (the zero revision is reserved and maps to one).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Ordered canonical field set of one definition.</summary>
    public sealed class CanonicalFields
    {
        private readonly SortedDictionary<string, string> fields = new SortedDictionary<string, string>(StringComparer.Ordinal);

        public CanonicalFields(string typeId)
        {
            TypeId = typeId ?? throw new ArgumentNullException(nameof(typeId));
        }

        public string TypeId { get; }

        public int Count => fields.Count;

        public CanonicalFields Add(string name, string value)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A canonical field needs a name.", nameof(name));
            }

            if (fields.ContainsKey(name))
            {
                throw new InvalidOperationException("Field '" + name + "' is added twice to definition type " + TypeId + ".");
            }

            fields.Add(name, value ?? string.Empty);
            return this;
        }

        public CanonicalFields Add(string name, int value) => Add(name, value.ToString(CultureInfo.InvariantCulture));

        public CanonicalFields Add(string name, bool value) => Add(name, value ? "true" : "false");

        public CanonicalFields Add(string name, double value) => Add(name, CanonicalValues.Float(value));

        /// <summary>The canonical text the hash is computed over.</summary>
        public string Text()
        {
            var builder = new StringBuilder();
            builder.Append(DefinitionHashing.Format).Append('\n');
            builder.Append("type=").Append(CanonicalValues.Escape(TypeId)).Append('\n');
            foreach (KeyValuePair<string, string> field in fields)
            {
                builder.Append(CanonicalValues.Escape(field.Key)).Append('=').Append(CanonicalValues.Escape(field.Value)).Append('\n');
            }

            return builder.ToString();
        }
    }

    /// <summary>Canonical value formatting shared by the editor reflection and the tests.</summary>
    public static class CanonicalValues
    {
        /// <summary>Round-trip invariant float text; negative zero is zero and NaN is "nan".</summary>
        public static string Float(double value)
        {
            if (double.IsNaN(value))
            {
                return "nan";
            }

            if (value == 0.0)
            {
                return "0";
            }

            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public static string Float(float value) => Float((double)value);

        /// <summary>Escapes backslash, newline, carriage return and '=' so every field is exactly one line.</summary>
        public static string Escape(string value)
        {
            if (value.IndexOfAny(new[] { '\\', '\n', '\r', '=' }) < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '=': builder.Append("\\e"); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }
    }

    /// <summary>Hash and revision of definitions.</summary>
    public static class DefinitionHashing
    {
        public const string Format = "gamecore.gameplay.definition/1";

        /// <summary>Lowercase hex SHA-256 of the canonical text.</summary>
        public static string HashHex(CanonicalFields fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            return Sha256Hex(fields.Text());
        }

        public static string Sha256Hex(string text)
        {
            byte[] digest;
            using (SHA256 sha = SHA256.Create())
            {
                digest = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
            }

            var builder = new StringBuilder(digest.Length * 2);
            for (int i = 0; i < digest.Length; i++)
            {
                builder.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        /// <summary>The definition revision of a content hash: its first eight bytes big-endian; never zero.</summary>
        public static ulong RevisionOf(string contentHashHex)
        {
            return GameCore.Gameplay.Contracts.AuthoringIds.RevisionOfContentStamp(contentHashHex);
        }

        /// <summary>The first 32 hex characters (16 bytes) of a content hash: a catalog implementation id.</summary>
        public static string ImplementationIdOf(string contentHashHex)
        {
            if (contentHashHex == null || contentHashHex.Length < 32)
            {
                throw new ArgumentException("A content hash is 64 lowercase hex characters.", nameof(contentHashHex));
            }

            return contentHashHex.Substring(0, 32);
        }
    }
}
