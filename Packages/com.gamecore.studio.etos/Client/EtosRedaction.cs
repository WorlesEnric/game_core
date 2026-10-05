// GameCore.Studio.Etos.Client - credential redaction for every line the client writes or shows (SADR-018, 04 s8).
// etos keys and tokens carry recognisable prefixes: etk_ (app/agent keys), ett_ (WebSocket tickets), etp_ (proxy
// tokens), eta_ (agent tokens). Bearer values are redacted whatever they look like, and so is the etos_ticket query
// parameter. The same rules as the companion's redact.rs, so a token never appears in a Studio log, an error shown in
// the settings page, an evidence transcript or a test failure message.
#nullable enable
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Removes etos credentials from text.</summary>
    public static class EtosRedaction
    {
        /// <summary>The replacement for a secret value.</summary>
        public const string Mask = "[redacted]";

        private static readonly Regex PrefixedToken = new Regex(@"(etk_|ett_|etp_|eta_|sk-)[A-Za-z0-9_\-\.~\+/=]+", RegexOptions.CultureInvariant);

        private static readonly Regex BearerValue = new Regex(@"(?i)\b(bearer\s+)[A-Za-z0-9_\-\.~\+/=]+", RegexOptions.CultureInvariant);

        private static readonly Regex TicketQuery = new Regex(@"(?i)\b(etos_ticket=)[^&\s""']+", RegexOptions.CultureInvariant);

        /// <summary><paramref name="text"/> with every etos credential, bearer value and ticket replaced by <see cref="Mask"/>.</summary>
        public static string Redact(string? text)
        {
            // Normalize transport URL/token syntax before the shared policy (core also covers JSON and sk-).
            string value = TicketQuery.Replace(text ?? string.Empty, "$1" + Mask);
            value = PrefixedToken.Replace(value, Mask);
            return new GameCore.Studio.Authoring.SecretRedactor().Redact(value);
        }

        /// <summary>Transport token normalization followed by the core's recursive secret-key policy.</summary>
        public static JToken RedactJson(JToken value)
        {
            return new GameCore.Studio.Authoring.SecretRedactor().RedactJson(NormalizeTransport(value));
        }

        private static JToken NormalizeTransport(JToken value)
        {
            if (value is JObject obj)
            {
                var copy = new JObject();
                foreach (JProperty property in obj.Properties()) copy[property.Name] = NormalizeTransport(property.Value);
                return copy;
            }
            if (value is JArray array)
            {
                var copy = new JArray();
                foreach (JToken item in array) copy.Add(NormalizeTransport(item));
                return copy;
            }
            return value.Type == JTokenType.String ? new JValue(Redact(value.Value<string>())) : value.DeepClone();
        }

        /// <summary>True when <paramref name="text"/> still holds something that looks like an etos credential.</summary>
        public static bool ContainsSecret(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            return PrefixedToken.IsMatch(text!) || HasBearerValue(text!) || TicketQuery.Match(text!).Success && !text!.Contains("etos_ticket=" + Mask);
        }

        /// <summary>A display form of a key: its prefix, the mask and its length (<c>etk_…[redacted]… (68 chars)</c>).</summary>
        public static string Describe(string? key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "(no key)";
            }

            int underscore = key!.IndexOf('_');
            string prefix = underscore > 0 && underscore <= 4 ? key.Substring(0, underscore + 1) : string.Empty;
            StringBuilder text = new StringBuilder();
            text.Append(prefix).Append("…").Append(Mask).Append("… (").Append(key.Length).Append(" chars)");
            return text.ToString();
        }

        private static bool HasBearerValue(string text)
        {
            foreach (Match match in BearerValue.Matches(text))
            {
                string value = match.Value.Substring(match.Groups[1].Length);
                if (value != Mask)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
