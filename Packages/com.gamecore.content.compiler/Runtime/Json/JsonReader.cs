// GameCore.Content.Compiler - strict minimal JSON reader for catalog description documents (GC-003).
// Unity-free and dependency-free on purpose: the Editor-only compiler assembly cannot use
// System.Text.Json in Unity's netstandard2.1 profile, and generated output must be byte-reproducible, so the
// reader is strict (no comments, no trailing commas, no duplicate member names, bounded depth) and rejects
// anything whose interpretation could differ between hosts.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Content.Compiler.Json
{
    /// <summary>JSON value kind of a parsed catalog description node.</summary>
    public enum JsonKind
    {
        Null = 0,
        Boolean = 1,
        Number = 2,
        String = 3,
        Array = 4,
        Object = 5,
    }

    /// <summary>
    /// One parsed JSON value. Object members keep document order so a diagnostic can name the first offending
    /// member; order never influences emitted output, which is canonically sorted by the emitter.
    /// </summary>
    public sealed class JsonValue
    {
        private readonly List<KeyValuePair<string, JsonValue>> members;
        private readonly List<JsonValue> items;

        internal JsonValue(JsonKind kind)
        {
            Kind = kind;
            members = new List<KeyValuePair<string, JsonValue>>();
            items = new List<JsonValue>();
            Text = string.Empty;
        }

        internal JsonValue(JsonKind kind, string text)
            : this(kind)
        {
            Text = text;
        }

        public JsonKind Kind { get; }

        /// <summary>Verbatim text of a string member or the verbatim token of a number.</summary>
        public string Text { get; }

        /// <summary>Object members in document order.</summary>
        public IReadOnlyList<KeyValuePair<string, JsonValue>> Members => members;

        /// <summary>Array items in document order.</summary>
        public IReadOnlyList<JsonValue> Items => items;

        public bool IsNull => Kind == JsonKind.Null;

        internal void AddMember(string name, JsonValue value) => members.Add(new KeyValuePair<string, JsonValue>(name, value));

        internal void AddItem(JsonValue value) => items.Add(value);

        /// <summary>First member with the given name, or null when absent.</summary>
        public JsonValue? Member(string name)
        {
            for (int i = 0; i < members.Count; i++)
            {
                if (string.Equals(members[i].Key, name, StringComparison.Ordinal))
                {
                    return members[i].Value;
                }
            }

            return null;
        }

        /// <summary>Every member name in document order; used to report unknown members.</summary>
        public IReadOnlyList<string> MemberNames()
        {
            string[] names = new string[members.Count];
            for (int i = 0; i < members.Count; i++)
            {
                names[i] = members[i].Key;
            }

            return names;
        }

        /// <summary>Plain-text description used by diagnostics; never throws.</summary>
        public string Describe()
        {
            switch (Kind)
            {
                case JsonKind.Null:
                    return "null";
                case JsonKind.Boolean:
                    return Text;
                case JsonKind.Number:
                    return "number " + Text;
                case JsonKind.String:
                    return "string \"" + Text + "\"";
                case JsonKind.Array:
                    return "array of " + items.Count.ToString(CultureInfo.InvariantCulture) + " item(s)";
                default:
                    return "object with " + members.Count.ToString(CultureInfo.InvariantCulture) + " member(s)";
            }
        }
    }

    /// <summary>Thrown by <see cref="JsonReader"/> for a malformed document, with an exact character offset.</summary>
    public sealed class JsonFormatException : Exception
    {
        public JsonFormatException(string message, int offset)
            : base(message + " (at character " + offset.ToString(CultureInfo.InvariantCulture) + ")")
        {
            Offset = offset;
        }

        public int Offset { get; }
    }

    /// <summary>
    /// Strict JSON reader for catalog description documents. Accepted syntax is exactly RFC 8259 with these
    /// deliberate restrictions: duplicate object member names reject, comments reject, trailing commas reject,
    /// leading zeros and leading '+' in numbers reject, and nesting is bounded.
    /// </summary>
    public static class JsonReader
    {
        /// <summary>Maximum nesting depth; deeper input is rejected instead of risking a stack overflow.</summary>
        public const int MaxDepth = 64;

        /// <summary>Maximum accepted document size in characters.</summary>
        public const int MaxDocumentLength = 4 * 1024 * 1024;

        public static JsonValue Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (text.Length > MaxDocumentLength)
            {
                throw new JsonFormatException("the document exceeds the accepted size limit", MaxDocumentLength);
            }

            Parser parser = new Parser(text);
            parser.SkipWhitespace();
            JsonValue value = parser.ReadValue(0);
            parser.SkipWhitespace();
            if (!parser.AtEnd)
            {
                throw new JsonFormatException("unexpected trailing content", parser.Position);
            }

            return value;
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            internal Parser(string text)
            {
                this.text = text;
            }

            internal int Position => index;

            internal bool AtEnd => index >= text.Length;

            internal void SkipWhitespace()
            {
                while (index < text.Length)
                {
                    char c = text[index];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    {
                        index++;
                        continue;
                    }

                    break;
                }
            }

            internal JsonValue ReadValue(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw new JsonFormatException("the document nests deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels", index);
                }

                if (AtEnd)
                {
                    throw new JsonFormatException("the document ends before a value is complete", index);
                }

                char c = text[index];
                switch (c)
                {
                    case '{':
                        return ReadObject(depth);
                    case '[':
                        return ReadArray(depth);
                    case '"':
                        return new JsonValue(JsonKind.String, ReadString());
                    case 't':
                        ReadLiteral("true");
                        return new JsonValue(JsonKind.Boolean, "true");
                    case 'f':
                        ReadLiteral("false");
                        return new JsonValue(JsonKind.Boolean, "false");
                    case 'n':
                        ReadLiteral("null");
                        return new JsonValue(JsonKind.Null, "null");
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return new JsonValue(JsonKind.Number, ReadNumber());
                        }

                        throw new JsonFormatException("unexpected character '" + c + "' where a value was expected", index);
                }
            }

            private JsonValue ReadObject(int depth)
            {
                JsonValue value = new JsonValue(JsonKind.Object);
                index++;
                SkipWhitespace();
                if (!AtEnd && text[index] == '}')
                {
                    index++;
                    return value;
                }

                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[index] != '"')
                    {
                        throw new JsonFormatException("an object member name must be a string", index);
                    }

                    int nameOffset = index;
                    string name = ReadString();
                    if (!seen.Add(name))
                    {
                        throw new JsonFormatException("duplicate object member name \"" + name + "\"", nameOffset);
                    }

                    SkipWhitespace();
                    if (AtEnd || text[index] != ':')
                    {
                        throw new JsonFormatException("expected ':' after the member name \"" + name + "\"", index);
                    }

                    index++;
                    SkipWhitespace();
                    value.AddMember(name, ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new JsonFormatException("the object is not terminated", index);
                    }

                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (text[index] == '}')
                    {
                        index++;
                        return value;
                    }

                    throw new JsonFormatException("expected ',' or '}' in the object", index);
                }
            }

            private JsonValue ReadArray(int depth)
            {
                JsonValue value = new JsonValue(JsonKind.Array);
                index++;
                SkipWhitespace();
                if (!AtEnd && text[index] == ']')
                {
                    index++;
                    return value;
                }

                while (true)
                {
                    SkipWhitespace();
                    value.AddItem(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw new JsonFormatException("the array is not terminated", index);
                    }

                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    if (text[index] == ']')
                    {
                        index++;
                        return value;
                    }

                    throw new JsonFormatException("expected ',' or ']' in the array", index);
                }
            }

            private void ReadLiteral(string literal)
            {
                if (index + literal.Length > text.Length ||
                    string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                {
                    throw new JsonFormatException("expected the literal '" + literal + "'", index);
                }

                index += literal.Length;
            }

            private string ReadNumber()
            {
                int start = index;
                if (!AtEnd && text[index] == '-')
                {
                    index++;
                }

                int digitsStart = index;
                while (!AtEnd && text[index] >= '0' && text[index] <= '9')
                {
                    index++;
                }

                if (index == digitsStart)
                {
                    throw new JsonFormatException("a number requires at least one digit", index);
                }

                string integerPart = text.Substring(digitsStart, index - digitsStart);
                if (integerPart.Length > 1 && integerPart[0] == '0')
                {
                    throw new JsonFormatException("a number may not have a leading zero", digitsStart);
                }

                if (!AtEnd && text[index] == '.')
                {
                    index++;
                    int fractionStart = index;
                    while (!AtEnd && text[index] >= '0' && text[index] <= '9')
                    {
                        index++;
                    }

                    if (index == fractionStart)
                    {
                        throw new JsonFormatException("a fraction requires at least one digit", index);
                    }
                }

                if (!AtEnd && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    if (!AtEnd && (text[index] == '+' || text[index] == '-'))
                    {
                        index++;
                    }

                    int exponentStart = index;
                    while (!AtEnd && text[index] >= '0' && text[index] <= '9')
                    {
                        index++;
                    }

                    if (index == exponentStart)
                    {
                        throw new JsonFormatException("an exponent requires at least one digit", index);
                    }
                }

                return text.Substring(start, index - start);
            }

            private string ReadString()
            {
                index++;
                StringBuilder builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw new JsonFormatException("the string is not terminated", index);
                    }

                    char c = text[index];
                    if (c == '"')
                    {
                        index++;
                        return builder.ToString();
                    }

                    if (c == '\\')
                    {
                        index++;
                        if (AtEnd)
                        {
                            throw new JsonFormatException("the string ends after an escape character", index);
                        }

                        char escape = text[index++];
                        switch (escape)
                        {
                            case '"':
                                builder.Append('"');
                                break;
                            case '\\':
                                builder.Append('\\');
                                break;
                            case '/':
                                builder.Append('/');
                                break;
                            case 'b':
                                builder.Append('\b');
                                break;
                            case 'f':
                                builder.Append('\f');
                                break;
                            case 'n':
                                builder.Append('\n');
                                break;
                            case 'r':
                                builder.Append('\r');
                                break;
                            case 't':
                                builder.Append('\t');
                                break;
                            case 'u':
                                builder.Append(ReadUnicodeEscape());
                                break;
                            default:
                                throw new JsonFormatException("unknown escape sequence '\\" + escape + "'", index - 1);
                        }

                        continue;
                    }

                    if (c < 0x20)
                    {
                        throw new JsonFormatException("a control character must be escaped inside a string", index);
                    }

                    builder.Append(c);
                    index++;
                }
            }

            private char ReadUnicodeEscape()
            {
                if (index + 4 > text.Length)
                {
                    throw new JsonFormatException("a '\\u' escape requires four hexadecimal digits", index);
                }

                int code = 0;
                for (int i = 0; i < 4; i++)
                {
                    int digit = HexDigit(text[index + i]);
                    if (digit < 0)
                    {
                        throw new JsonFormatException("a '\\u' escape requires four hexadecimal digits", index + i);
                    }

                    code = (code << 4) | digit;
                }

                index += 4;
                if (code >= 0xD800 && code <= 0xDFFF)
                {
                    throw new JsonFormatException(
                        "a surrogate escape is not accepted; write the character directly as UTF-8",
                        index - 4);
                }

                return (char)code;
            }

            private static int HexDigit(char c)
            {
                if (c >= '0' && c <= '9')
                {
                    return c - '0';
                }

                if (c >= 'a' && c <= 'f')
                {
                    return (c - 'a') + 10;
                }

                if (c >= 'A' && c <= 'F')
                {
                    return (c - 'A') + 10;
                }

                return -1;
            }
        }
    }
}
