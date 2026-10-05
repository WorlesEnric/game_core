// GameCore.Studio.Etos.Client - small JSON helpers. Answers are read leniently (a member that is absent or null reads
// as absent), because the client must keep working with companion builds that predate the 03 s9 null policy; what the
// client WRITES never carries null (requests are built without null members, and NullFinder lets callers refuse
// documents that must be null-free, such as candidate change sets).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Lenient readers and strict writers.</summary>
    public static class Json
    {
        public static string? Str(JObject? obj, string name)
        {
            JToken? token = obj?[name];
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return null;
            }

            return token.Type == JTokenType.String ? (string?)token : token.ToString(Formatting.None);
        }

        public static long? Long(JObject? obj, string name)
        {
            JToken? token = obj?[name];
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer)
            {
                return (long)token;
            }

            if (token.Type == JTokenType.Float)
            {
                return (long)(double)token;
            }

            if (token.Type == JTokenType.String && long.TryParse((string?)token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed))
            {
                return parsed;
            }

            return null;
        }

        public static double? Double(JObject? obj, string name)
        {
            JToken? token = obj?[name];
            if (token == null)
            {
                return null;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return (double)token;
            }

            return null;
        }

        public static bool? Bool(JObject? obj, string name)
        {
            JToken? token = obj?[name];
            return token != null && token.Type == JTokenType.Boolean ? (bool)token : (bool?)null;
        }

        public static JObject? Obj(JObject? obj, string name) => obj?[name] as JObject;

        public static JArray? Arr(JObject? obj, string name) => obj?[name] as JArray;

        public static List<string> Strings(JObject? obj, string name)
        {
            List<string> result = new List<string>();
            if (obj?[name] is JArray array)
            {
                foreach (JToken item in array)
                {
                    string? text = item.Type == JTokenType.String ? (string?)item : null;
                    if (text != null)
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        /// <summary>Parses an answer body as a JSON object (refusing anything else as <c>protocol</c>).</summary>
        public static JObject ParseObject(string body, string what)
        {
            try
            {
                JToken token = JToken.Parse(body);
                if (token is JObject obj)
                {
                    return obj;
                }
            }
            catch (JsonException error)
            {
                throw EtosException.Protocol(what + " is not JSON: " + error.Message, error);
            }

            throw EtosException.Protocol(what + " is not a JSON object.");
        }

        /// <summary>Compact JSON text without null members (null-valued properties are removed recursively).</summary>
        public static string Write(JToken token)
        {
            JToken copy = token.DeepClone();
            Prune(copy);
            return copy.ToString(Formatting.None);
        }

        /// <summary>The JSON pointer of the first null in <paramref name="token"/>, or null when there is none.</summary>
        public static string? FirstNull(JToken token)
        {
            return FirstNull(token, string.Empty);
        }

        /// <summary>Lowercase hex sha256 of <paramref name="bytes"/>.</summary>
        public static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(bytes));
            }
        }

        /// <summary>Lowercase hex sha256 of a file's content.</summary>
        public static string Sha256HexOfFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return Hex(sha.ComputeHash(stream));
            }
        }

        /// <summary>The bare lowercase hex form of <c>&lt;hex&gt;</c> or <c>sha256:&lt;hex&gt;</c>, or null when it is not a digest.</summary>
        public static string? NormalizeSha256(string? digest)
        {
            if (digest == null)
            {
                return null;
            }

            string hex = digest.StartsWith("sha256:", StringComparison.Ordinal) ? digest.Substring(7) : digest;
            if (hex.Length != 64)
            {
                return null;
            }

            StringBuilder lower = new StringBuilder(64);
            foreach (char c in hex)
            {
                char l = char.ToLowerInvariant(c);
                if (!((l >= '0' && l <= '9') || (l >= 'a' && l <= 'f')))
                {
                    return null;
                }

                lower.Append(l);
            }

            return lower.ToString();
        }

        public static string Hex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        private static void Prune(JToken token)
        {
            if (token is JObject obj)
            {
                List<string> remove = new List<string>();
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Value.Type == JTokenType.Null || property.Value.Type == JTokenType.Undefined)
                    {
                        remove.Add(property.Name);
                    }
                    else
                    {
                        Prune(property.Value);
                    }
                }

                foreach (string name in remove)
                {
                    obj.Remove(name);
                }
            }
            else if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    Prune(item);
                }
            }
        }

        private static string? FirstNull(JToken token, string pointer)
        {
            if (token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
            {
                return pointer.Length == 0 ? "/" : pointer;
            }

            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    string? found = FirstNull(property.Value, pointer + "/" + property.Name.Replace("~", "~0").Replace("/", "~1"));
                    if (found != null)
                    {
                        return found;
                    }
                }
            }
            else if (token is JArray array)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    string? found = FirstNull(array[i], pointer + "/" + i.ToString(CultureInfo.InvariantCulture));
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }
    }
}
