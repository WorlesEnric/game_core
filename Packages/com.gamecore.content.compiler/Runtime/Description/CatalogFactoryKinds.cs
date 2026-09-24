// GameCore.Content.Compiler - factory kind names and code-fragment rules (GC-003).
// The factory kind names are exactly the members of GameCore.Contracts.FactoryKind, so a description cannot
// invent a registration category, and the code-fragment rules keep injected C# single-line and comment-free so
// generated output stays byte-reproducible and diffable.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Content.Compiler
{
    /// <summary>Name lookup for the generated registration categories (P-009).</summary>
    public static class CatalogFactoryKinds
    {
        /// <summary>Every accepted kind name, in <see cref="FactoryKind"/> declaration order.</summary>
        public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        {
            "PluginFactory", "SystemFactory", "Reducer", "StaticPredicate", "Serializer", "Migration",
            "ResourceFactory", "LayoutApply", "SchemaFactory", "StatePolicy", "Handler",
        });

        /// <summary>True when the name is one of the accepted kinds.</summary>
        public static bool IsKnown(string? name)
        {
            if (name == null)
            {
                return false;
            }

            for (int i = 0; i < Names.Count; i++)
            {
                if (string.Equals(Names[i], name, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Parses a validated kind name; throws when the name was not validated first.</summary>
        public static FactoryKind Parse(string name)
        {
            switch (name)
            {
                case "PluginFactory": return FactoryKind.PluginFactory;
                case "SystemFactory": return FactoryKind.SystemFactory;
                case "Reducer": return FactoryKind.Reducer;
                case "StaticPredicate": return FactoryKind.StaticPredicate;
                case "Serializer": return FactoryKind.Serializer;
                case "Migration": return FactoryKind.Migration;
                case "ResourceFactory": return FactoryKind.ResourceFactory;
                case "LayoutApply": return FactoryKind.LayoutApply;
                case "SchemaFactory": return FactoryKind.SchemaFactory;
                case "StatePolicy": return FactoryKind.StatePolicy;
                case "Handler": return FactoryKind.Handler;
                default:
                    throw new CatalogDescriptionException("unknown factory kind '" + name + "'");
            }
        }

        /// <summary>Comma-separated accepted names, for diagnostics and the package README.</summary>
        public static string DescribeSupported() => string.Join(", ", Names);
    }

    /// <summary>Shape rules for C# fragments injected into generated output.</summary>
    public static class CatalogCodeFragments
    {
        /// <summary>Maximum accepted fragment length in characters.</summary>
        public const int MaxLength = 512;

        /// <summary>
        /// Returns null when the fragment is acceptable, or a reason when it is not. Rules: one line, no comment
        /// introducer, no brace, no semicolon, balanced angle brackets, and either a dotted/qualified expression or
        /// (for statements and assembly attributes) an invocation. A fragment that produces invalid C# fails the
        /// consuming project's build, which is the intended type check; these rules only keep the emitted file
        /// deterministic and impossible to smuggle a comment or a second statement into.
        /// </summary>
        public static string? DescribeRejection(string fragment, bool allowStatementAttributes)
        {
            if (fragment.Length == 0)
            {
                return "the fragment is empty";
            }

            if (fragment.Length > MaxLength)
            {
                return "the fragment exceeds " + MaxLength.ToString(CultureInfo.InvariantCulture) +
                       " characters; split it into several fragments";
            }

            if (HasControlCharacter(fragment))
            {
                return "the fragment contains a control character";
            }

            if (Contains(fragment, "//") || Contains(fragment, "/*") || Contains(fragment, "*/"))
            {
                return "the fragment cannot contain a comment";
            }

            if (Contains(fragment, ";") || Contains(fragment, "{") || Contains(fragment, "}"))
            {
                return "the fragment cannot contain a statement terminator or a brace block";
            }

            if (!HasBalancedDelimiters(fragment))
            {
                return "the fragment has unbalanced '<', '>' or '(', ')' delimiters";
            }

            if (!HasInvocationOrMember(fragment))
            {
                return "the fragment must be a dotted expression or an invocation expression";
            }

            if (!allowStatementAttributes && !Contains(fragment, "("))
            {
                return "a statement fragment must be an expression, for example 'MyRoots.Track(default(My.Job<V>))'";
            }

            return null;
        }

        private static bool HasControlCharacter(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\t')
                {
                    continue;
                }

                if (c < 0x20 || c == 0x7F)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Contains(string text, string needle) =>
            text.IndexOf(needle, StringComparison.Ordinal) >= 0;

        private static bool HasBalancedDelimiters(string text)
        {
            int angles = 0;
            int parentheses = 0;
            for (int i = 0; i < text.Length; i++)
            {
                switch (text[i])
                {
                    case '<':
                        angles++;
                        break;
                    case '>':
                        angles--;
                        break;
                    case '(':
                        parentheses++;
                        break;
                    case ')':
                        parentheses--;
                        break;
                }

                if (angles < 0 || parentheses < 0)
                {
                    return false;
                }
            }

            return angles == 0 && parentheses == 0;
        }
        /// <summary>
        /// True when the fragment starts with an identifier and otherwise contains only identifier, generic,
        /// invocation, member-access and argument characters. A stray operator, string literal or keyword
        /// punctuation therefore rejects.
        /// </summary>
        private static bool HasInvocationOrMember(string text)
        {
            if (!IsIdentifierStart(text[0]))
            {
                return false;
            }

            for (int i = 1; i < text.Length; i++)
            {
                char c = text[i];
                bool allowed = IsIdentifierStart(c) || (c >= '0' && c <= '9') || c == '.' || c == '<' ||
                               c == '>' || c == '(' || c == ')' || c == ',' || c == ' ' || c == '?' ||
                               c == '[' || c == ']';
                if (!allowed)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdentifierStart(char c) =>
            (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }
}
