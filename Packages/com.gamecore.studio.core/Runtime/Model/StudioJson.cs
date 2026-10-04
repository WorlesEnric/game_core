// GameCore.Studio.Model - the one JSON configuration for the Studio interchange shapes (03, SADR-008).
// Newtonsoft.Json only (Unity ships it as com.unity.nuget.newtonsoft-json); System.Text.Json is not used.
// Every model type is [JsonObject(MemberSerialization.OptIn)] with explicit camelCase [JsonProperty] names, so a
// default JsonConvert call and these settings produce the same keys; the settings add the strict read rules.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>Serializer settings and helpers for every Studio interchange document.</summary>
    public static class StudioJson
    {
        /// <summary>
        /// Fresh settings: nulls omitted, unknown members refused, no date sniffing inside free-form payloads,
        /// floating point read as double, invariant culture, trailing content refused.
        /// </summary>
        public static JsonSerializerSettings CreateSettings()
        {
            return new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Error,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                Culture = CultureInfo.InvariantCulture,
                TypeNameHandling = TypeNameHandling.None,
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                CheckAdditionalContent = true,
                MaxDepth = 64,
            };
        }

        /// <summary>Serializes with '\n' line endings (indented) so journal files are byte-stable across hosts.</summary>
        public static string Serialize(object value, bool indented = true)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            JsonSerializerSettings settings = CreateSettings();
            settings.Formatting = indented ? Formatting.Indented : Formatting.None;
            JsonSerializer serializer = JsonSerializer.Create(settings);
            using (StringWriter text = new StringWriter(CultureInfo.InvariantCulture))
            {
                text.NewLine = "\n";
                using (JsonTextWriter writer = new JsonTextWriter(text))
                {
                    writer.Formatting = settings.Formatting;
                    writer.Indentation = 2;
                    writer.IndentChar = ' ';
                    serializer.Serialize(writer, value);
                }

                return text.ToString();
            }
        }

        /// <summary>Deserializes one document; throws <see cref="JsonException"/> on any contract violation.</summary>
        public static T Deserialize<T>(string json)
            where T : class
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            T? value;
            try
            {
                value = JsonConvert.DeserializeObject<T>(json, CreateSettings());
            }
            catch (ArgumentException error)
            {
                // A model constructor refused a null or empty required member before Newtonsoft's own Required check
                // ran; report it as the contract violation it is.
                throw new JsonSerializationException("Invalid " + typeof(T).Name + ": " + error.Message, error);
            }

            if (value == null)
            {
                throw new JsonSerializationException("The document is JSON null; a " + typeof(T).Name + " was expected.");
            }

            return value;
        }

        /// <summary>Converts a model object to a JSON tree with the same rules as <see cref="Serialize"/>.</summary>
        public static JToken ToToken(object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            return JToken.FromObject(value, JsonSerializer.Create(CreateSettings()));
        }

        /// <summary>Parses JSON text into a tree without date sniffing (free-form payloads keep their strings).</summary>
        public static JToken ParseToken(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            using (JsonTextReader reader = new JsonTextReader(new StringReader(json)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                reader.FloatParseHandling = FloatParseHandling.Double;
                JToken token = JToken.ReadFrom(reader);
                if (reader.Read() && reader.TokenType != JsonToken.Comment)
                {
                    throw new JsonReaderException("Additional content after the JSON document.");
                }

                return token;
            }
        }
    }

    /// <summary>
    /// String enum converter that accepts only the exact documented spelling (the member name, or its [EnumMember]
    /// value): integers, other casings and comma-combined flags are refused. Writing is the base converter's.
    /// </summary>
    public sealed class StrictStringEnumConverter : StringEnumConverter
    {
        public StrictStringEnumConverter()
        {
            AllowIntegerValues = false;
        }

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            Type? underlying = Nullable.GetUnderlyingType(objectType);
            Type enumType = underlying ?? objectType;
            if (reader.TokenType == JsonToken.Null)
            {
                if (underlying != null)
                {
                    return null;
                }

                throw new JsonSerializationException("Null is not a " + enumType.Name + " value.");
            }

            if (reader.TokenType != JsonToken.String)
            {
                throw new JsonSerializationException("A " + enumType.Name + " is written as its name string, not " + reader.TokenType + ".");
            }

            string text = reader.Value as string ?? string.Empty;
            foreach (FieldInfo field in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                EnumMemberAttribute? member = field.GetCustomAttribute<EnumMemberAttribute>(false);
                if (string.Equals(member?.Value ?? field.Name, text, StringComparison.Ordinal))
                {
                    return field.GetValue(null);
                }
            }

            throw new JsonSerializationException("'" + text + "' is not a " + enumType.Name + " value.");
        }
    }

    /// <summary>
    /// JSON Schema facts that a CLR type cannot express (string patterns, constants, fixed array lengths). Read by the
    /// schema emitter (tools/studio/emit_studio_schemas.py); ignored by the serializer.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
    public sealed class SchemaHintAttribute : Attribute
    {
        /// <summary>ECMA-262 regular expression a string value must match.</summary>
        public string? Pattern { get; set; }

        /// <summary>The only value a string property may take.</summary>
        public string? Const { get; set; }

        /// <summary>Minimum string length; negative means unset.</summary>
        public int MinLength { get; set; } = -1;

        /// <summary>Minimum array length; negative means unset.</summary>
        public int MinItems { get; set; } = -1;

        /// <summary>Maximum array length; negative means unset.</summary>
        public int MaxItems { get; set; } = -1;

        /// <summary>Pattern of each string item of an array property.</summary>
        public string? ItemPattern { get; set; }
    }

    /// <summary>Shared patterns of the Studio documents.</summary>
    public static class StudioPatterns
    {
        /// <summary>Content stamp: <c>sha256:</c> plus 64 lowercase hex digits.</summary>
        public const string Stamp = "^sha256:[0-9a-f]{64}$";

        /// <summary>Bare SHA-256 digest: 64 lowercase hex digits (artifact entries).</summary>
        public const string Sha256Hex = "^[0-9a-f]{64}$";

        /// <summary>Change-set id: <c>cs_</c> plus a 26-character Crockford base32 ULID.</summary>
        public const string ChangeSetId = "^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$";

        /// <summary>Selection id: <c>sel_</c> plus a 26-character Crockford base32 ULID.</summary>
        public const string SelectionId = "^sel_[0-7][0-9A-HJKMNP-TV-Z]{25}$";

        /// <summary>DefinitionRef text form <c>name@revision</c>.</summary>
        public const string DefinitionRef = "^[a-z0-9][a-z0-9._-]*@[0-9A-Za-z]+$";
    }

    internal static class ModelLists
    {
        internal static IReadOnlyList<T> Required<T>(IEnumerable<T>? items, string name)
        {
            if (items == null)
            {
                throw new ArgumentNullException(name);
            }

            List<T> copy = new List<T>(items);
            for (int i = 0; i < copy.Count; i++)
            {
                if (copy[i] == null)
                {
                    throw new ArgumentException(name + "[" + i.ToString(CultureInfo.InvariantCulture) + "] is null.", name);
                }
            }

            return copy.AsReadOnly();
        }

        internal static IReadOnlyList<T>? Optional<T>(IEnumerable<T>? items, string name)
        {
            return items == null ? null : Required(items, name);
        }

        internal static IReadOnlyDictionary<string, T>? OptionalMap<T>(IReadOnlyDictionary<string, T>? items, string name)
        {
            if (items == null)
            {
                return null;
            }

            SortedDictionary<string, T> copy = new SortedDictionary<string, T>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, T> pair in items)
            {
                if (pair.Value == null)
                {
                    throw new ArgumentException(name + "['" + pair.Key + "'] is null.", name);
                }

                copy.Add(pair.Key, pair.Value);
            }

            return copy;
        }

        internal static T NotNull<T>(T? value, string name)
            where T : class
        {
            if (value == null)
            {
                throw new ArgumentNullException(name);
            }

            return value;
        }

        internal static string NotEmpty(string? value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(name + " must be a non-empty string.", name);
            }

            return value!;
        }

        internal static JObject? CopyObject(JObject? value)
        {
            return value == null ? null : (JObject)value.DeepClone();
        }
    }
}
