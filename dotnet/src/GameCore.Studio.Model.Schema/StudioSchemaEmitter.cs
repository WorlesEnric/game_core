// GameCore.Studio.Model.Schema - a small reflective JSON Schema (draft 2020-12) emitter for the Studio shapes.
// It reads exactly what the serializer reads: [JsonObject(OptIn)] types, [JsonProperty] names and Required, enum
// names or [EnumMember] values, and [SchemaHint] facts. Two model types with the same simple name would share a
// $defs entry, so that is refused rather than silently merged. Output is deterministic: keys sorted ordinally at every
// level, two-space indentation, '\n' line endings, one trailing newline.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model.Schema
{
    /// <summary>One emitted schema document.</summary>
    public sealed class SchemaDocument
    {
        public SchemaDocument(string fileName, Type root, string json)
        {
            FileName = fileName;
            Root = root;
            Json = json;
        }

        public string FileName { get; }

        public Type Root { get; }

        public string Json { get; }
    }

    /// <summary>Emits the Studio schemas from the model types.</summary>
    public static class StudioSchemaEmitter
    {
        public const string Draft = "https://json-schema.org/draft/2020-12/schema";

        /// <summary>Root types and their schema file stems, in emission order.</summary>
        public static readonly IReadOnlyList<KeyValuePair<string, Type>> Roots = new[]
        {
            new KeyValuePair<string, Type>("authoring-ref", typeof(AuthoringRef)),
            new KeyValuePair<string, Type>("selection-snapshot", typeof(SelectionSnapshot)),
            new KeyValuePair<string, Type>("semantic-index", typeof(SemanticIndex)),
            new KeyValuePair<string, Type>("tool-catalog", typeof(ToolCatalog)),
            new KeyValuePair<string, Type>("change-set", typeof(ChangeSet)),
            new KeyValuePair<string, Type>("diagnostic", typeof(Diagnostic)),
        };

        /// <summary>Every schema document, deterministic.</summary>
        public static IReadOnlyList<SchemaDocument> EmitAll()
        {
            List<SchemaDocument> documents = new List<SchemaDocument>();
            foreach (KeyValuePair<string, Type> root in Roots)
            {
                string fileName = root.Key + ".schema.json";
                documents.Add(new SchemaDocument(fileName, root.Value, Write(BuildSchema(root.Key, root.Value))));
            }

            return documents;
        }

        /// <summary>Writes every schema document into <paramref name="directory"/>.</summary>
        public static void WriteAll(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (SchemaDocument document in EmitAll())
            {
                File.WriteAllText(Path.Combine(directory, document.FileName), document.Json, new System.Text.UTF8Encoding(false));
            }
        }

        /// <summary>The schema tree of one root type (unsorted; <see cref="Write"/> sorts).</summary>
        public static JObject BuildSchema(string stem, Type root)
        {
            Definitions definitions = new Definitions();
            definitions.Owners.Add(root.Name, root);
            JObject schema = ObjectSchema(root, definitions);
            schema["$schema"] = Draft;
            schema["$id"] = "urn:gamecore:studio:schema:" + stem;
            schema["title"] = root.Name;
            definitions.Schemas.Remove(root.Name);
            if (definitions.Schemas.Count > 0)
            {
                JObject defs = new JObject();
                foreach (KeyValuePair<string, JObject> definition in definitions.Schemas)
                {
                    defs[definition.Key] = definition.Value;
                }

                schema["$defs"] = defs;
            }

            return schema;
        }

        private static JObject ObjectSchema(Type type, Definitions definitions)
        {
            JObject properties = new JObject();
            JArray required = new JArray();
            List<KeyValuePair<string, PropertyInfo>> members = new List<KeyValuePair<string, PropertyInfo>>();
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                JsonPropertyAttribute? json = property.GetCustomAttribute<JsonPropertyAttribute>(true);
                if (json == null || json.PropertyName == null)
                {
                    continue;
                }

                members.Add(new KeyValuePair<string, PropertyInfo>(json.PropertyName, property));
            }

            members.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            foreach (KeyValuePair<string, PropertyInfo> member in members)
            {
                JsonPropertyAttribute json = member.Value.GetCustomAttribute<JsonPropertyAttribute>(true)!;
                SchemaHintAttribute? hint = member.Value.GetCustomAttribute<SchemaHintAttribute>(true);
                properties[member.Key] = ValueSchema(member.Value.PropertyType, hint, definitions);
                if (json.Required == Required.Always)
                {
                    required.Add(member.Key);
                }
            }

            JObject schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["additionalProperties"] = false,
            };
            if (required.Count > 0)
            {
                schema["required"] = required;
            }

            return schema;
        }

        private static JObject ValueSchema(Type type, SchemaHintAttribute? hint, Definitions definitions)
        {
            Type? underlying = Nullable.GetUnderlyingType(type);
            if (underlying != null)
            {
                type = underlying;
            }

            if (type == typeof(string))
            {
                JObject text = new JObject { ["type"] = "string" };
                if (hint != null)
                {
                    if (hint.Const != null)
                    {
                        text["const"] = hint.Const;
                    }

                    if (hint.Pattern != null)
                    {
                        text["pattern"] = hint.Pattern;
                    }

                    if (hint.MinLength >= 0)
                    {
                        text["minLength"] = hint.MinLength;
                    }
                }

                return text;
            }

            if (type == typeof(bool))
            {
                return new JObject { ["type"] = "boolean" };
            }

            if (type == typeof(int) || type == typeof(long))
            {
                return WithMinimum(new JObject { ["type"] = "integer" }, hint);
            }

            if (type == typeof(double) || type == typeof(float))
            {
                return WithMinimum(new JObject { ["type"] = "number" }, hint);
            }

            if (type == typeof(JObject))
            {
                return new JObject { ["type"] = "object" };
            }

            if (type == typeof(JToken))
            {
                return new JObject();
            }

            if (type == typeof(DiagnosticWhere))
            {
                Define(typeof(AuthoringRef), definitions);
                return new JObject
                {
                    ["oneOf"] = new JArray
                    {
                        new JObject { ["type"] = "string", ["minLength"] = 1 },
                        new JObject { ["$ref"] = "#/$defs/" + nameof(AuthoringRef) },
                    },
                };
            }

            if (type.IsEnum)
            {
                Define(type, definitions);
                return new JObject { ["$ref"] = "#/$defs/" + type.Name };
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                Type element = type.GetGenericArguments()[0];
                JObject items = ValueSchema(element, null, definitions);
                if (hint != null && hint.ItemPattern != null)
                {
                    items["pattern"] = hint.ItemPattern;
                }

                JObject array = new JObject { ["type"] = "array", ["items"] = items };
                if (hint != null && hint.MinItems >= 0)
                {
                    array["minItems"] = hint.MinItems;
                }

                if (hint != null && hint.MaxItems >= 0)
                {
                    array["maxItems"] = hint.MaxItems;
                }

                return array;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
            {
                Type[] arguments = type.GetGenericArguments();
                if (arguments[0] != typeof(string))
                {
                    throw new InvalidOperationException("Only string-keyed maps are representable in JSON: " + type);
                }

                return new JObject { ["type"] = "object", ["additionalProperties"] = ValueSchema(arguments[1], null, definitions) };
            }

            if (type.IsClass && type.GetCustomAttribute<JsonObjectAttribute>(true) != null)
            {
                Define(type, definitions);
                return new JObject { ["$ref"] = "#/$defs/" + type.Name };
            }

            throw new InvalidOperationException("No schema mapping for " + type.FullName + ".");
        }

        private static JObject WithMinimum(JObject schema, SchemaHintAttribute? hint)
        {
            if (hint != null && !double.IsNaN(hint.Minimum))
            {
                double minimum = hint.Minimum;
                schema["minimum"] = Math.Floor(minimum) == minimum ? new JValue((long)minimum) : new JValue(minimum);
            }

            return schema;
        }

        private static void Define(Type type, Definitions definitions)
        {
            if (definitions.Owners.TryGetValue(type.Name, out Type? owner))
            {
                if (owner != type)
                {
                    throw new InvalidOperationException(
                        "$defs name collision: '" + type.Name + "' is both " + owner.FullName + " and " + type.FullName + ".");
                }

                return;
            }

            definitions.Owners.Add(type.Name, type);

            if (type.IsEnum)
            {
                JArray values = new JArray();
                FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                Array.Sort(fields, (left, right) => left.MetadataToken.CompareTo(right.MetadataToken));
                foreach (FieldInfo field in fields)
                {
                    EnumMemberAttribute? member = field.GetCustomAttribute<EnumMemberAttribute>(false);
                    values.Add(member?.Value ?? field.Name);
                }

                definitions.Schemas[type.Name] = new JObject { ["type"] = "string", ["enum"] = values };
                return;
            }

            // The owner entry above already reserves the name, so recursive shapes terminate.
            definitions.Schemas[type.Name] = ObjectSchema(type, definitions);
        }

        /// <summary>The $defs of one schema under construction, and which CLR type owns each name.</summary>
        private sealed class Definitions
        {
            public readonly SortedDictionary<string, JObject> Schemas = new SortedDictionary<string, JObject>(StringComparer.Ordinal);

            public readonly Dictionary<string, Type> Owners = new Dictionary<string, Type>(StringComparer.Ordinal);
        }

        /// <summary>Serializes with ordinally sorted keys, two-space indentation, '\n' newlines and a trailing newline.</summary>
        public static string Write(JToken schema)
        {
            JToken sorted = Sort(schema);
            using (StringWriter text = new StringWriter(CultureInfo.InvariantCulture))
            {
                text.NewLine = "\n";
                using (JsonTextWriter writer = new JsonTextWriter(text))
                {
                    writer.Formatting = Formatting.Indented;
                    writer.Indentation = 2;
                    writer.IndentChar = ' ';
                    sorted.WriteTo(writer);
                }

                return text.ToString() + "\n";
            }
        }

        private static JToken Sort(JToken token)
        {
            if (token is JObject value)
            {
                List<JProperty> properties = new List<JProperty>(value.Properties());
                properties.Sort((left, right) => string.CompareOrdinal(left.Name, right.Name));
                JObject result = new JObject();
                foreach (JProperty property in properties)
                {
                    result[property.Name] = Sort(property.Value);
                }

                return result;
            }

            if (token is JArray array)
            {
                JArray result = new JArray();
                foreach (JToken item in array)
                {
                    result.Add(Sort(item));
                }

                return result;
            }

            return token.DeepClone();
        }
    }
}
