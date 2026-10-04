// Test support: sample loading, repository discovery, order-insensitive JSON equivalence and a minimal JSON Schema
// (draft 2020-12 subset) checker covering exactly the keywords the Studio schema emitter produces.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GameCore.Studio.Model;
using GameCore.Studio.Model.Schema;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Model.Tests
{
    internal static class Samples
    {
        public static string PathOf(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Samples", name);

        public static string Read(string name) => File.ReadAllText(PathOf(name));

        public static JToken Token(string name) => StudioJson.ParseToken(Read(name));

        public static JObject Object(string name) => (JObject)Token(name);

        public static ToolCatalog Catalog(Action<JObject>? edit = null)
        {
            JObject catalog = Object("tool-catalog.json");
            edit?.Invoke(catalog);
            return StudioJson.Deserialize<ToolCatalog>(catalog.ToString());
        }

        public static SemanticIndex Index(Action<JObject>? edit = null)
        {
            JObject index = Object("semantic-index.json");
            edit?.Invoke(index);
            return StudioJson.Deserialize<SemanticIndex>(index.ToString());
        }

        public static ChangeSet ChangeSet(Action<JObject>? edit = null)
        {
            JObject changeSet = Object("change-set.json");
            edit?.Invoke(changeSet);
            return StudioJson.Deserialize<ChangeSet>(changeSet.ToString());
        }

        public static JObject Tool(JObject catalog, string id)
        {
            foreach (JToken tool in (JArray)catalog["tools"]!)
            {
                if ((string?)tool["id"] == id)
                {
                    return (JObject)tool;
                }
            }

            throw new InvalidOperationException("no tool " + id);
        }

        public static JObject Operation(JObject changeSet, string opId)
        {
            foreach (JToken operation in (JArray)changeSet["operations"]!)
            {
                if ((string?)operation["opId"] == opId)
                {
                    return (JObject)operation;
                }
            }

            throw new InvalidOperationException("no operation " + opId);
        }
    }

    internal static class RepositoryRoot
    {
        public static string Find()
        {
            DirectoryInfo? directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "dotnet", "GameCore.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Repository root (dotnet/GameCore.sln) not found above " + TestContext.CurrentContext.TestDirectory);
        }
    }

    internal static class JsonEquivalence
    {
        /// <summary>Null when equal modulo object key order and int/float spelling of the same number; else the first difference.</summary>
        public static string? Difference(JToken expected, JToken actual, string path = "$")
        {
            bool expectedNumber = expected.Type == JTokenType.Integer || expected.Type == JTokenType.Float;
            bool actualNumber = actual.Type == JTokenType.Integer || actual.Type == JTokenType.Float;
            if (expectedNumber && actualNumber)
            {
                double left = expected.Value<double>();
                double right = actual.Value<double>();
                return left.Equals(right) ? null : path + ": " + left.ToString("R", CultureInfo.InvariantCulture) + " != " + right.ToString("R", CultureInfo.InvariantCulture);
            }

            if (expected.Type != actual.Type)
            {
                return path + ": type " + expected.Type + " != " + actual.Type;
            }

            if (expected is JObject expectedObject)
            {
                JObject actualObject = (JObject)actual;
                foreach (JProperty property in expectedObject.Properties())
                {
                    JToken? other = actualObject[property.Name];
                    if (other == null)
                    {
                        return path + "." + property.Name + ": missing";
                    }

                    string? inner = Difference(property.Value, other, path + "." + property.Name);
                    if (inner != null)
                    {
                        return inner;
                    }
                }

                foreach (JProperty property in actualObject.Properties())
                {
                    if (expectedObject[property.Name] == null)
                    {
                        return path + "." + property.Name + ": unexpected";
                    }
                }

                return null;
            }

            if (expected is JArray expectedArray)
            {
                JArray actualArray = (JArray)actual;
                if (expectedArray.Count != actualArray.Count)
                {
                    return path + ": length " + expectedArray.Count + " != " + actualArray.Count;
                }

                for (int i = 0; i < expectedArray.Count; i++)
                {
                    string? inner = Difference(expectedArray[i], actualArray[i], path + "[" + i + "]");
                    if (inner != null)
                    {
                        return inner;
                    }
                }

                return null;
            }

            return JToken.DeepEquals(expected, actual) ? null : path + ": " + expected + " != " + actual;
        }
    }

    /// <summary>Checks an instance against an emitted schema: $ref/$defs, oneOf, type, enum, const, pattern, minLength,
    /// properties/required/additionalProperties, items/minItems/maxItems.</summary>
    internal sealed class MiniSchemaValidator
    {
        private readonly JObject _root;

        public MiniSchemaValidator(JObject root)
        {
            _root = root;
        }

        public static MiniSchemaValidator For(Type rootType)
        {
            foreach (KeyValuePair<string, Type> root in StudioSchemaEmitter.Roots)
            {
                if (root.Value == rootType)
                {
                    return new MiniSchemaValidator(StudioSchemaEmitter.BuildSchema(root.Key, root.Value));
                }
            }

            throw new InvalidOperationException("no schema root for " + rootType);
        }

        public IReadOnlyList<string> Validate(JToken instance)
        {
            List<string> errors = new List<string>();
            Check(_root, instance, "$", errors);
            return errors;
        }

        private void Check(JObject schema, JToken value, string path, List<string> errors)
        {
            JToken? reference = schema["$ref"];
            if (reference != null)
            {
                string target = (string?)reference ?? string.Empty;
                const string prefix = "#/$defs/";
                Assert.That(target.StartsWith(prefix, StringComparison.Ordinal), "unsupported $ref " + target);
                JObject definition = (JObject)_root["$defs"]![target.Substring(prefix.Length)]!;
                Check(definition, value, path, errors);
                return;
            }

            if (schema["oneOf"] is JArray oneOf)
            {
                int matches = 0;
                foreach (JToken option in oneOf)
                {
                    List<string> inner = new List<string>();
                    Check((JObject)option, value, path, inner);
                    if (inner.Count == 0)
                    {
                        matches++;
                    }
                }

                if (matches != 1)
                {
                    errors.Add(path + ": matches " + matches + " oneOf branches");
                }

                return;
            }

            string? type = (string?)schema["type"];
            if (type != null && !TypeMatches(type, value))
            {
                errors.Add(path + ": expected " + type + ", got " + value.Type);
                return;
            }

            if (schema["enum"] is JArray values)
            {
                bool found = false;
                foreach (JToken candidate in values)
                {
                    found |= JToken.DeepEquals(candidate, value);
                }

                if (!found)
                {
                    errors.Add(path + ": " + value + " not in enum");
                }
            }

            if (schema["const"] != null && !JToken.DeepEquals(schema["const"], value))
            {
                errors.Add(path + ": expected const " + schema["const"]);
            }

            if (value.Type == JTokenType.String)
            {
                string text = value.Value<string>() ?? string.Empty;
                string? pattern = (string?)schema["pattern"];
                if (pattern != null && !Regex.IsMatch(text, pattern))
                {
                    errors.Add(path + ": '" + text + "' does not match " + schema["pattern"]);
                }

                if (schema["minLength"] != null && text.Length < (int)schema["minLength"]!)
                {
                    errors.Add(path + ": shorter than " + schema["minLength"]);
                }
            }

            if (schema["minimum"] != null && (value.Type == JTokenType.Integer || value.Type == JTokenType.Float)
                && value.Value<double>() < (double)schema["minimum"]!)
            {
                errors.Add(path + ": " + value + " is below the minimum " + schema["minimum"]);
            }

            if (value is JObject instance)
            {
                JObject properties = schema["properties"] as JObject ?? new JObject();
                if (schema["required"] is JArray required)
                {
                    foreach (JToken name in required)
                    {
                        if (instance[(string?)name ?? string.Empty] == null)
                        {
                            errors.Add(path + ": missing required '" + name + "'");
                        }
                    }
                }

                foreach (JProperty property in instance.Properties())
                {
                    JToken? propertySchema = properties[property.Name];
                    if (propertySchema != null)
                    {
                        Check((JObject)propertySchema, property.Value, path + "." + property.Name, errors);
                        continue;
                    }

                    JToken? additional = schema["additionalProperties"];
                    if (additional is JObject additionalSchema)
                    {
                        Check(additionalSchema, property.Value, path + "." + property.Name, errors);
                    }
                    else if (additional != null && additional.Type == JTokenType.Boolean && !(bool)additional)
                    {
                        errors.Add(path + ": unexpected property '" + property.Name + "'");
                    }
                }
            }

            if (value is JArray array)
            {
                if (schema["minItems"] != null && array.Count < (int)schema["minItems"]!)
                {
                    errors.Add(path + ": fewer than " + schema["minItems"] + " items");
                }

                if (schema["maxItems"] != null && array.Count > (int)schema["maxItems"]!)
                {
                    errors.Add(path + ": more than " + schema["maxItems"] + " items");
                }

                if (schema["items"] is JObject items)
                {
                    for (int i = 0; i < array.Count; i++)
                    {
                        Check(items, array[i], path + "[" + i + "]", errors);
                    }
                }
            }
        }

        private static bool TypeMatches(string type, JToken value)
        {
            switch (type)
            {
                case "object":
                    return value.Type == JTokenType.Object;
                case "array":
                    return value.Type == JTokenType.Array;
                case "string":
                    return value.Type == JTokenType.String;
                case "boolean":
                    return value.Type == JTokenType.Boolean;
                case "integer":
                    return value.Type == JTokenType.Integer
                        || (value.Type == JTokenType.Float && Math.Floor(value.Value<double>()) == value.Value<double>());
                case "number":
                    return value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
                default:
                    throw new InvalidOperationException("unsupported schema type " + type);
            }
        }
    }
}
