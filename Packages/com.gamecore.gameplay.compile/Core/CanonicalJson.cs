// GameCore.Gameplay.Compile - a minimal deterministic JSON text writer.
//
// Output is byte-stable: LF line endings, two-space indentation, members in the order they are written, invariant
// number formatting and a fixed string escape set. Nothing here depends on culture, platform newline or dictionary
// order, which is what makes "bake twice, compare bytes" a meaningful check.
#nullable enable
using System.Globalization;
using System.Text;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Streaming writer of pretty-printed, canonical JSON.</summary>
    public sealed class CanonicalJson
    {
        private readonly StringBuilder builder = new StringBuilder(4096);
        private int depth;
        private bool needsComma;

        public CanonicalJson BeginObject()
        {
            Separate();
            builder.Append('{');
            depth++;
            needsComma = false;
            return this;
        }

        public CanonicalJson BeginObject(string name)
        {
            Name(name);
            builder.Append('{');
            depth++;
            needsComma = false;
            return this;
        }

        public CanonicalJson EndObject()
        {
            depth--;
            NewLine();
            builder.Append('}');
            needsComma = true;
            return this;
        }

        public CanonicalJson BeginArray(string name)
        {
            Name(name);
            builder.Append('[');
            depth++;
            needsComma = false;
            return this;
        }

        public CanonicalJson EndArray()
        {
            depth--;
            NewLine();
            builder.Append(']');
            needsComma = true;
            return this;
        }

        /// <summary>An empty array member on one line.</summary>
        public CanonicalJson EmptyArray(string name)
        {
            Name(name);
            builder.Append("[]");
            needsComma = true;
            return this;
        }

        public CanonicalJson String(string name, string value)
        {
            Name(name);
            Quote(value);
            needsComma = true;
            return this;
        }

        /// <summary>A string element of an array.</summary>
        public CanonicalJson StringItem(string value)
        {
            Separate();
            Quote(value);
            needsComma = true;
            return this;
        }

        public CanonicalJson Number(string name, long value)
        {
            Name(name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            needsComma = true;
            return this;
        }

        public CanonicalJson Number(string name, ulong value)
        {
            Name(name);
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
            needsComma = true;
            return this;
        }

        public CanonicalJson Bool(string name, bool value)
        {
            Name(name);
            builder.Append(value ? "true" : "false");
            needsComma = true;
            return this;
        }

        /// <summary>The finished document, terminated by one LF.</summary>
        public override string ToString() => builder.ToString() + "\n";

        private void Name(string name)
        {
            Separate();
            Quote(name);
            builder.Append(": ");
        }

        private void Separate()
        {
            if (needsComma)
            {
                builder.Append(',');
            }

            if (builder.Length > 0)
            {
                NewLine();
            }

            needsComma = false;
        }

        private void NewLine()
        {
            builder.Append('\n');
            builder.Append(' ', depth * 2);
        }

        private void Quote(string value)
        {
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
        }
    }
}
