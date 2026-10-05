#nullable enable
// GameCore Studio staging harness: a minimal JSON object writer (no null members are ever written; absent values are
// omitted), so the result files follow the Studio null policy (docs/studio/03 s9) without a JSON dependency.
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GameCore.Stage.Harness
{
    /// <summary>Builds one flat-or-nested JSON object.</summary>
    public sealed class HarnessJson
    {
        private readonly List<string> members = new List<string>();

        public HarnessJson Str(string name, string? value)
        {
            if (value != null)
            {
                members.Add(Quote(name) + ":" + Quote(value));
            }

            return this;
        }

        public HarnessJson Num(string name, long value)
        {
            members.Add(Quote(name) + ":" + value.ToString(CultureInfo.InvariantCulture));
            return this;
        }

        public HarnessJson Bool(string name, bool value)
        {
            members.Add(Quote(name) + ":" + (value ? "true" : "false"));
            return this;
        }

        public HarnessJson Obj(string name, HarnessJson value)
        {
            members.Add(Quote(name) + ":" + value);
            return this;
        }

        public HarnessJson Arr(string name, IEnumerable<HarnessJson> values)
        {
            var items = new List<string>();
            foreach (HarnessJson value in values)
            {
                items.Add(value.ToString());
            }

            members.Add(Quote(name) + ":[" + string.Join(",", items) + "]");
            return this;
        }

        public override string ToString() => "{" + string.Join(",", members) + "}";

        public static string Quote(string text)
        {
            var builder = new StringBuilder(text.Length + 2);
            builder.Append('"');
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
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
    }
}
