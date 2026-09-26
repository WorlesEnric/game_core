// GameCore.Recovery.Fixtures — the small canonical JSON reader the fixture documents are read with (GC-027).
//
// WHY NOT `System.Text.Json`
//
// This assembly is compiled by Unity (its `Runtime/GameCore.Recovery.Fixtures.asmdef` is a local package assembly),
// and `System.Text.Json` is not part of Unity's netstandard2.1 profile. That is not a preference: the content
// compiler's own `JsonReader` states the same reason for its hand-written reader, and GC-023's telemetry trace is a
// hand-rolled JSON document for the same reason. A dependency on the NuGet package would have to be carried into the
// Editor and the player through `precompiledReferences`, which this repository does nowhere; so the fixture reader is
// a small, strict, dependency-free parser instead.
//
// WHAT IT IS, AND WHAT IT REFUSES
//
// The committed fixtures are written in the canonical form their README documents: UTF-8, LF, 2-space indentation,
// one property per line, no comments and no trailing commas. This reader parses a *general* JSON subset (objects,
// arrays, strings with the JSON escapes, integers, booleans, null — which is everything the fixtures use) and is
// strict about the things a lenient reader would hide:
//
//   * a syntax error anywhere is a refusal with the offset and the reason, never a partially built document;
//   * duplicate property names inside one object are refused, because the canonical form has none and a duplicate
//     would make "the value of this property" ambiguous (P-008's canonical-order rule);
//   * a fractional or out-of-range number is refused rather than truncated to an integer;
//   * unpaired surrogates and unterminated strings are refused rather than replaced.
//
// It does NOT build a mutable DOM, does not convert numbers to `double` for the caller, and never reports success
// for a document it only partly understood. The tree it produces is immutable, so two reads of one document cannot
// disagree (P-054).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Recovery.Fixtures
{
    /// <summary>The JSON kinds this reader produces: exactly the six the fixtures use (05 s6's explicit null marker).</summary>
    public enum RecoveryJsonKind
    {
        /// <summary>An all-zero value: the kind of a value that was never parsed.</summary>
        Undefined = 0,

        Object = 1,
        Array = 2,
        String = 3,
        Number = 4,
        True = 5,
        False = 6,
        Null = 7,
    }

    /// <summary>One immutable parsed JSON node. Internal: callers only ever hold a <see cref="RecoveryJsonValue"/>.</summary>
    internal sealed class RecoveryJsonNode
    {
        private readonly List<KeyValuePair<string, RecoveryJsonNode>>? members;
        private readonly List<RecoveryJsonNode>? items;

        private RecoveryJsonNode(
            RecoveryJsonKind kind,
            string? text,
            long number,
            List<KeyValuePair<string, RecoveryJsonNode>>? members,
            List<RecoveryJsonNode>? items)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            Number = number;
            this.members = members;
            this.items = items;
        }

        public RecoveryJsonKind Kind { get; }

        /// <summary>The string value of a `String` node; empty for every other kind.</summary>
        public string Text { get; }

        /// <summary>The integer value of a `Number` node; zero for every other kind.</summary>
        public long Number { get; }

        public int MemberCount => members == null ? 0 : members.Count;

        public int ItemCount => items == null ? 0 : items.Count;

        public static RecoveryJsonNode Object(List<KeyValuePair<string, RecoveryJsonNode>> members) =>
            new RecoveryJsonNode(RecoveryJsonKind.Object, string.Empty, 0L, members, null);

        public static RecoveryJsonNode Array(List<RecoveryJsonNode> items) =>
            new RecoveryJsonNode(RecoveryJsonKind.Array, string.Empty, 0L, null, items);

        public static RecoveryJsonNode String(string value) =>
            new RecoveryJsonNode(RecoveryJsonKind.String, value, 0L, null, null);

        public static RecoveryJsonNode Integer(long value) =>
            new RecoveryJsonNode(RecoveryJsonKind.Number, string.Empty, value, null, null);

        public static RecoveryJsonNode Scalar(RecoveryJsonKind kind) =>
            new RecoveryJsonNode(kind, string.Empty, 0L, null, null);

        /// <summary>One member of an object by name; a duplicate name is refused at parse time, so this is unique.</summary>
        public bool TryGetMember(string name, out RecoveryJsonNode value)
        {
            if (members != null)
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (string.Equals(members[i].Key, name, StringComparison.Ordinal))
                    {
                        value = members[i].Value;
                        return true;
                    }
                }
            }

            value = Scalar(RecoveryJsonKind.Undefined);
            return false;
        }

        /// <summary>One item of an array; false when the index is outside it.</summary>
        public bool TryGetItem(int index, out RecoveryJsonNode value)
        {
            if (items != null && index >= 0 && index < items.Count)
            {
                value = items[index];
                return true;
            }

            value = Scalar(RecoveryJsonKind.Undefined);
            return false;
        }
    }

    /// <summary>
    /// One parsed JSON value as a caller sees it: a kind, the six accessors the fixture reader needs, and the two
    /// lookups (`TryGetProperty`, `EnumerateArray`) that make walking a document a bounded loop rather than a
    /// recursive visitor. It is a readonly struct so `default` is a well-defined "never parsed" value and an
    /// `out` parameter is never null (P-054's explicit null semantics).
    /// </summary>
    public readonly struct RecoveryJsonValue
    {
        private readonly RecoveryJsonNode? node;

        internal RecoveryJsonValue(RecoveryJsonNode? node)
        {
            this.node = node;
        }

        /// <summary>The kind of this value; <see cref="RecoveryJsonKind.Undefined"/> for a never-parsed value.</summary>
        public RecoveryJsonKind ValueKind => node == null ? RecoveryJsonKind.Undefined : node.Kind;

        /// <summary>True when this value came from a parse rather than from `default`.</summary>
        public bool IsDefined => node != null;

        /// <summary>The number of items in an array, or zero for another kind.</summary>
        public int GetArrayLength() => node == null ? 0 : node.ItemCount;

        /// <summary>One property of an object; false when this value is not an object or the name is absent.</summary>
        public bool TryGetProperty(string name, out RecoveryJsonValue value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (node != null && node.TryGetMember(name, out RecoveryJsonNode member))
            {
                value = new RecoveryJsonValue(member);
                return true;
            }

            value = default(RecoveryJsonValue);
            return false;
        }

        /// <summary>The string value; the empty string when this value is not a string.</summary>
        public string GetString() => node == null ? string.Empty : node.Text;

        /// <summary>The integer value of a number, or false when this value is not an integer number.</summary>
        public bool TryGetInt32(out int value)
        {
            if (node != null
                && node.Kind == RecoveryJsonKind.Number
                && node.Number >= int.MinValue
                && node.Number <= int.MaxValue)
            {
                value = (int)node.Number;
                return true;
            }

            value = 0;
            return false;
        }

        /// <summary>True or false; false for every kind that is not `True` or `False`.</summary>
        public bool GetBoolean() => node != null && node.Kind == RecoveryJsonKind.True;

        /// <summary>
        /// The items of an array in document order. A value that is not an array enumerates nothing, which the
        /// caller reports as a reason rather than as an empty document.
        /// </summary>
        public IEnumerable<RecoveryJsonValue> EnumerateArray()
        {
            if (node == null || node.Kind != RecoveryJsonKind.Array)
            {
                yield break;
            }

            for (int i = 0; i < node.ItemCount; i++)
            {
                if (node.TryGetItem(i, out RecoveryJsonNode item))
                {
                    yield return new RecoveryJsonValue(item);
                }
            }
        }

        /// <summary>Diagnostic name of this value's kind, for a refusal reason (P-052).</summary>
        public override string ToString() => ValueKind.ToString();

        /// <summary>
        /// Parses one document. False and a reason for every documented refusal: a syntax error, a duplicate property
        /// name in one object, an empty input, a fractional number, or a document with content after its root value.
        /// </summary>
        public static bool TryParse(string? json, out RecoveryJsonValue root, out string reason)
        {
            root = default(RecoveryJsonValue);
            reason = string.Empty;
            if (json == null)
            {
                reason = "the document text is null; there is nothing to read.";
                return false;
            }

            var parser = new RecoveryJsonParser(json);
            if (!parser.TryParseDocument(out RecoveryJsonNode? parsed, out reason))
            {
                return false;
            }

            root = new RecoveryJsonValue(parsed);
            return true;
        }
    }

    /// <summary>
    /// The reader itself: one linear scan with an explicit offset, no recursion on the caller's stack beyond the
    /// document's own nesting, and every refusal naming the offset it was found at.
    /// </summary>
    internal sealed class RecoveryJsonParser
    {
        /// <summary>Nesting this reader accepts; a document deeper than this is refused rather than recursed into.</summary>
        private const int MaxDepth = 64;

        private readonly string text;
        private int offset;

        public RecoveryJsonParser(string text)
        {
            this.text = text;
        }

        public bool TryParseDocument(out RecoveryJsonNode? root, out string reason)
        {
            root = null;
            reason = string.Empty;
            SkipWhitespace();
            if (offset >= text.Length)
            {
                reason = "the document is empty; there is no JSON value to read.";
                return false;
            }

            if (!TryParseValue(0, out RecoveryJsonNode? value, out reason))
            {
                return false;
            }

            SkipWhitespace();
            if (offset != text.Length)
            {
                reason = "the document has content after its root value at offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            root = value;
            return true;
        }

        private bool TryParseValue(int depth, out RecoveryJsonNode? value, out string reason)
        {
            value = null;
            reason = string.Empty;
            if (depth > MaxDepth)
            {
                reason = "the document nests deeper than " + MaxDepth.ToString(CultureInfo.InvariantCulture)
                    + " levels, which this reader refuses rather than recursing into.";
                return false;
            }

            SkipWhitespace();
            if (offset >= text.Length)
            {
                reason = "the document ends where a value was expected (offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }

            char current = text[offset];
            switch (current)
            {
                case '{':
                    return TryParseObject(depth, out value, out reason);

                case '[':
                    return TryParseArray(depth, out value, out reason);

                case '"':
                    if (!TryParseString(out string parsedText, out reason))
                    {
                        return false;
                    }

                    value = RecoveryJsonNode.String(parsedText);
                    return true;

                case 't':
                    if (!TryReadLiteral("true", out reason))
                    {
                        return false;
                    }

                    value = RecoveryJsonNode.Scalar(RecoveryJsonKind.True);
                    return true;

                case 'f':
                    if (!TryReadLiteral("false", out reason))
                    {
                        return false;
                    }

                    value = RecoveryJsonNode.Scalar(RecoveryJsonKind.False);
                    return true;

                case 'n':
                    if (!TryReadLiteral("null", out reason))
                    {
                        return false;
                    }

                    value = RecoveryJsonNode.Scalar(RecoveryJsonKind.Null);
                    return true;

                default:
                    if (current == '-' || (current >= '0' && current <= '9'))
                    {
                        return TryParseNumber(out value, out reason);
                    }

                    reason = "the document has an unexpected character '" + current + "' at offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
            }
        }

        private bool TryParseObject(int depth, out RecoveryJsonNode? value, out string reason)
        {
            value = null;
            reason = string.Empty;
            offset++;
            var members = new List<KeyValuePair<string, RecoveryJsonNode>>();
            SkipWhitespace();
            if (offset < text.Length && text[offset] == '}')
            {
                offset++;
                value = RecoveryJsonNode.Object(members);
                return true;
            }

            while (true)
            {
                SkipWhitespace();
                if (offset >= text.Length || text[offset] != '"')
                {
                    reason = "an object member name must be a string (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                if (!TryParseString(out string name, out reason))
                {
                    return false;
                }

                // A duplicate name inside one object is refused: the canonical form has none, and a duplicate would
                // make "the value of this property" ambiguous, which P-008's canonical-order rule forbids.
                for (int i = 0; i < members.Count; i++)
                {
                    if (string.Equals(members[i].Key, name, StringComparison.Ordinal))
                    {
                        reason = "the object declares the property '" + name + "' more than once (offset "
                            + offset.ToString(CultureInfo.InvariantCulture) + ").";
                        return false;
                    }
                }

                SkipWhitespace();
                if (offset >= text.Length || text[offset] != ':')
                {
                    reason = "a property name must be followed by ':' (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                offset++;
                if (!TryParseValue(depth + 1, out RecoveryJsonNode? memberValue, out reason) || memberValue == null)
                {
                    return false;
                }

                // The member order of the document is preserved, because the canonical-form checks a consumer makes
                // are about the text and a reader that reordered them would hide a defect (P-008).
                members.Add(new KeyValuePair<string, RecoveryJsonNode>(name, memberValue));
                SkipWhitespace();
                if (offset < text.Length && text[offset] == ',')
                {
                    offset++;
                    continue;
                }

                if (offset < text.Length && text[offset] == '}')
                {
                    offset++;
                    value = RecoveryJsonNode.Object(members);
                    return true;
                }

                reason = "an object member must be followed by ',' or '}' (offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }
        }

        private bool TryParseArray(int depth, out RecoveryJsonNode? value, out string reason)
        {
            value = null;
            reason = string.Empty;
            offset++;
            var items = new List<RecoveryJsonNode>();
            SkipWhitespace();
            if (offset < text.Length && text[offset] == ']')
            {
                offset++;
                value = RecoveryJsonNode.Array(items);
                return true;
            }

            while (true)
            {
                if (!TryParseValue(depth + 1, out RecoveryJsonNode? item, out reason) || item == null)
                {
                    return false;
                }

                items.Add(item);
                SkipWhitespace();
                if (offset < text.Length && text[offset] == ',')
                {
                    offset++;
                    continue;
                }

                if (offset < text.Length && text[offset] == ']')
                {
                    offset++;
                    value = RecoveryJsonNode.Array(items);
                    return true;
                }

                reason = "an array item must be followed by ',' or ']' (offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }
        }

        private bool TryParseString(out string value, out string reason)
        {
            value = string.Empty;
            reason = string.Empty;
            offset++;
            var builder = new StringBuilder();
            while (true)
            {
                if (offset >= text.Length)
                {
                    reason = "a string is not terminated (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                char current = text[offset];
                if (current == '"')
                {
                    offset++;
                    value = builder.ToString();
                    return true;
                }

                if (current != '\\')
                {
                    if (current < ' ')
                    {
                        reason = "a string carries an unescaped control character at offset "
                            + offset.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    builder.Append(current);
                    offset++;
                    continue;
                }

                offset++;
                if (offset >= text.Length)
                {
                    reason = "a string escape is not terminated (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                char escape = text[offset];
                offset++;
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
                        if (!TryParseUnicodeEscape(out char escaped, out reason))
                        {
                            return false;
                        }

                        builder.Append(escaped);
                        break;
                    default:
                        reason = "a string carries the unknown escape '\\" + escape + "' at offset "
                            + (offset - 1).ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                }
            }
        }

        private bool TryParseUnicodeEscape(out char value, out string reason)
        {
            value = '\0';
            reason = string.Empty;
            if (offset + 4 > text.Length)
            {
                reason = "a \\u escape is shorter than four hex digits (offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }

            int digits = 0;
            for (int i = 0; i < 4; i++)
            {
                int digit = HexDigit(text[offset + i]);
                if (digit < 0)
                {
                    reason = "a \\u escape carries a non-hex character '" + text[offset + i] + "' at offset "
                        + (offset + i).ToString(CultureInfo.InvariantCulture) + ".";
                    return false;
                }

                digits = (digits << 4) | digit;
            }

            offset += 4;

            // A high surrogate must be followed by its low surrogate and vice versa: an unpaired surrogate is a
            // malformed document, and substituting a replacement character would hide it (P-054).
            if (digits >= 0xD800 && digits <= 0xDBFF)
            {
                if (offset + 6 > text.Length || text[offset] != '\\' || text[offset + 1] != 'u')
                {
                    reason = "a high surrogate is not followed by its low surrogate (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                int low = 0;
                for (int i = 0; i < 4; i++)
                {
                    int digit = HexDigit(text[offset + 2 + i]);
                    if (digit < 0)
                    {
                        reason = "a \\u escape carries a non-hex character at offset "
                            + (offset + 2 + i).ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }

                    low = (low << 4) | digit;
                }

                if (low < 0xDC00 || low > 0xDFFF)
                {
                    reason = "a high surrogate is followed by a value that is not a low surrogate (offset "
                        + offset.ToString(CultureInfo.InvariantCulture) + ").";
                    return false;
                }

                offset += 6;
                int combined = 0x10000 + (((digits - 0xD800) << 10) | (low - 0xDC00));
                value = char.ConvertFromUtf32(combined)[0];
                return true;
            }

            if (digits >= 0xDC00 && digits <= 0xDFFF)
            {
                reason = "a low surrogate appears without its high surrogate (offset "
                    + (offset - 4).ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }

            value = (char)digits;
            return true;
        }

        private bool TryParseNumber(out RecoveryJsonNode? value, out string reason)
        {
            value = null;
            reason = string.Empty;
            int start = offset;
            if (offset < text.Length && text[offset] == '-')
            {
                offset++;
            }

            int digits = 0;
            while (offset < text.Length && text[offset] >= '0' && text[offset] <= '9')
            {
                offset++;
                digits++;
            }

            if (digits == 0)
            {
                reason = "a number has no digits (offset " + start.ToString(CultureInfo.InvariantCulture) + ").";
                return false;
            }

            // The fixtures carry integers only. A fractional part or an exponent is refused rather than truncated,
            // because the reader hands the caller an integer and a silent truncation would be exactly the kind of
            // approximation P-054 forbids.
            if (offset < text.Length && (text[offset] == '.' || text[offset] == 'e' || text[offset] == 'E'))
            {
                reason = "the number at offset " + start.ToString(CultureInfo.InvariantCulture)
                    + " is not an integer; the fixtures carry integer scalars only and this reader refuses to "
                    + "truncate one.";
                return false;
            }

            string literal = text.Substring(start, offset - start);
            if (!long.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed))
            {
                reason = "the number '" + literal + "' at offset " + start.ToString(CultureInfo.InvariantCulture)
                    + " is outside the range this reader holds.";
                return false;
            }

            value = RecoveryJsonNode.Integer(parsed);
            return true;
        }

        private bool TryReadLiteral(string literal, out string reason)
        {
            reason = string.Empty;
            if (offset + literal.Length > text.Length
                || string.CompareOrdinal(text, offset, literal, 0, literal.Length) != 0)
            {
                reason = "expected the literal '" + literal + "' at offset "
                    + offset.ToString(CultureInfo.InvariantCulture) + ".";
                return false;
            }

            offset += literal.Length;
            return true;
        }

        private void SkipWhitespace()
        {
            while (offset < text.Length)
            {
                char current = text[offset];
                if (current == ' ' || current == '\t' || current == '\n' || current == '\r')
                {
                    offset++;
                    continue;
                }

                return;
            }
        }

        private static int HexDigit(char value)
        {
            if (value >= '0' && value <= '9')
            {
                return value - '0';
            }

            if (value >= 'a' && value <= 'f')
            {
                return 10 + (value - 'a');
            }

            if (value >= 'A' && value <= 'F')
            {
                return 10 + (value - 'A');
            }

            return -1;
        }
    }
}
