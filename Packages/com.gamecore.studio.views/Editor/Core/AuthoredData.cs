// GameCore.Studio.Views - reading authored member values as JSON (the same codec the edit engine writes with).
// Values come from the serialized properties (object references as AuthoringRef JSON), so a value read here and
// written back through `set` round-trips; the views never mutate the object themselves.
#nullable enable
using System;
using System.Globalization;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Views
{
    public static class AuthoredData
    {
        /// <summary>The JSON value of one authorable member of <paramref name="target"/>, or null when it has none.</summary>
        public static JToken? Read(StudioRuntime runtime, UnityEngine.Object target, string member)
        {
            AuthoringTypeInfo? info = runtime.Identity.Describe(target);
            AuthorMemberInfo? found = info?.FindMember(member);
            return found == null ? null : runtime.Resolver.Codec.ReadMember(target, found);
        }

        /// <summary>The authorable type id of <paramref name="target"/>, or null.</summary>
        public static string? TypeOf(StudioRuntime runtime, UnityEngine.Object target) => runtime.Identity.Describe(target)?.TypeId;

        /// <summary>The object behind a ref with a fresh stamped ref (null when it does not resolve).</summary>
        public static UnityEngine.Object? Resolve(StudioRuntime runtime, AuthoringRef reference, out AuthoringRef? current)
        {
            current = null;
            UnityEngine.Object? found = reference.Kind == AuthoringKind.Location ? null : runtime.Resolver.Find(reference);
            if (found != null)
            {
                current = runtime.Resolver.BuildRef(found, null, true);
            }

            return found;
        }

        public static int Int(JToken? token, int fallback = 0)
        {
            if (token == null)
            {
                return fallback;
            }

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                return token.Value<int>();
            }

            return token.Type == JTokenType.String && int.TryParse(token.Value<string>(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
        }

        public static float Float(JToken? token, float fallback = 0f)
        {
            if (token == null)
            {
                return fallback;
            }

            return token.Type == JTokenType.Integer || token.Type == JTokenType.Float ? token.Value<float>() : fallback;
        }

        public static bool Bool(JToken? token, bool fallback = false)
        {
            return token != null && token.Type == JTokenType.Boolean ? token.Value<bool>() : fallback;
        }

        public static string Text(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            return token.Type == JTokenType.String ? token.Value<string>() ?? string.Empty : token.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>An AuthoringRef read from a JSON value written by the codec (an object), or null.</summary>
        public static AuthoringRef? Ref(JToken? token)
        {
            if (!(token is JObject value))
            {
                return null;
            }

            try
            {
                return value.ToObject<AuthoringRef>(Newtonsoft.Json.JsonSerializer.Create(StudioJson.CreateSettings()));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>Display text of a value: a ref by its leaf name, scalars as text, vectors as (x, y, z).</summary>
        public static string Display(JToken? token, IndexGraph? graph = null)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                return string.Empty;
            }

            AuthoringRef? reference = Ref(token);
            if (reference != null)
            {
                string key = reference.IdentityKey;
                return graph != null && graph.Contains(key) ? graph.NameOf(key) : IndexGraph.LeafOf(reference);
            }

            if (token is JArray array)
            {
                bool numeric = array.Count > 0 && array.Count <= 4;
                foreach (JToken item in array)
                {
                    numeric &= item.Type == JTokenType.Float || item.Type == JTokenType.Integer;
                }

                if (numeric)
                {
                    string[] parts = new string[array.Count];
                    for (int i = 0; i < array.Count; i++)
                    {
                        parts[i] = array[i].Value<double>().ToString("0.###", CultureInfo.InvariantCulture);
                    }

                    return "(" + string.Join(", ", parts) + ")";
                }

                return "[" + array.Count + "]";
            }

            if (token.Type == JTokenType.Float)
            {
                return token.Value<double>().ToString("0.###", CultureInfo.InvariantCulture);
            }

            return Text(token);
        }

        /// <summary>A short id for logs and labels.</summary>
        public static string Short(string text, int max = 48)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= max)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, Math.Max(1, max - 1)) + "…";
        }

        internal static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString(hex, out Color color) ? color : Color.gray;
        }
    }
}
