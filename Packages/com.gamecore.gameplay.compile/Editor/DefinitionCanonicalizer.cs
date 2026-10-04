// GameCore.Gameplay.Compile.Editor - canonical authorable fields of a definition asset, by reflection (P1.1).
//
// The content hash of a definition covers exactly its authorable surface: every field of the type marked
// [AuthorField] or [AuthorRef] (the gameplay mirror attributes), formatted canonically:
//
//   integers, booleans, strings     invariant text
//   floats, vectors, colours        round-trip invariant text, components joined by ','
//   lists and arrays                [item,item,...] in list order
//   serializable plain classes      {name=value;...} over their serialized fields in ordinal name order
//   authored definition references  auth:<authoring id>#<content hash of the referenced definition> (so a variant edit
//                                   changes its definition's revision)
//   other asset references          asset:<guid>:<local file id>; null is null
//
// The YAML of the asset is never read, so serialization layout, line endings and Unity version do not matter.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using GameCore.Gameplay.Contracts;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Computes canonical fields and content stamps of authorable definition assets.</summary>
    public static class DefinitionCanonicalizer
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>The content stamp (lowercase hex SHA-256) of an authorable ScriptableObject.</summary>
        public static string ContentStamp(ScriptableObject asset)
        {
            return DefinitionHashing.HashHex(Canonical(asset, new HashSet<int>()));
        }

        /// <summary>The canonical field set of an authorable ScriptableObject.</summary>
        public static CanonicalFields Canonical(ScriptableObject asset) => Canonical(asset, new HashSet<int>());

        private static CanonicalFields Canonical(ScriptableObject asset, HashSet<int> visiting)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            Type type = asset.GetType();
            AuthorableAttribute? authorable = type.GetCustomAttribute<AuthorableAttribute>(false);
            if (authorable == null)
            {
                throw new ArgumentException(type.FullName + " is not [Authorable]", nameof(asset));
            }

            visiting.Add(asset.GetInstanceID());
            var fields = new CanonicalFields(authorable.ObjectTypeId);
            foreach (FieldInfo field in AuthorableFields(type))
            {
                fields.Add(field.Name, Format(field.GetValue(asset), visiting));
            }

            visiting.Remove(asset.GetInstanceID());
            return fields;
        }

        /// <summary>The [AuthorField]/[AuthorRef] fields of a type, base classes included, in ordinal name order.</summary>
        public static IReadOnlyList<FieldInfo> AuthorableFields(Type type)
        {
            var found = new List<FieldInfo>();
            for (Type? current = type; current != null && current != typeof(ScriptableObject) && current != typeof(MonoBehaviour); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(Fields | BindingFlags.DeclaredOnly))
                {
                    if (field.IsDefined(typeof(AuthorFieldAttribute), true) || field.IsDefined(typeof(AuthorRefAttribute), true))
                    {
                        found.Add(field);
                    }
                }
            }

            found.Sort((l, r) => string.CompareOrdinal(l.Name, r.Name));
            return found;
        }

        private static string Format(object? value, HashSet<int> visiting)
        {
            switch (value)
            {
                case null:
                    return "null";
                case string text:
                    return text;
                case bool flag:
                    return flag ? "true" : "false";
                case int number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case long number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case uint number:
                    return number.ToString(CultureInfo.InvariantCulture);
                case float number:
                    return CanonicalValues.Float(number);
                case double number:
                    return CanonicalValues.Float(number);
                case Vector2 v:
                    return CanonicalValues.Float(v.x) + "," + CanonicalValues.Float(v.y);
                case Vector3 v:
                    return CanonicalValues.Float(v.x) + "," + CanonicalValues.Float(v.y) + "," + CanonicalValues.Float(v.z);
                case Color c:
                    return CanonicalValues.Float(c.r) + "," + CanonicalValues.Float(c.g) + "," + CanonicalValues.Float(c.b) + "," + CanonicalValues.Float(c.a);
                case Enum e:
                    return e.ToString();
                case UnityEngine.Object reference:
                    return FormatReference(reference, visiting);
                case IList list:
                    var items = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            items.Append(',');
                        }

                        items.Append(Format(list[i], visiting));
                    }

                    return items.Append(']').ToString();
                default:
                    return FormatPlain(value, visiting);
            }
        }

        private static string FormatReference(UnityEngine.Object reference, HashSet<int> visiting)
        {
            // Unity's fake-null: a destroyed or missing reference formats as null.
            if (reference == null)
            {
                return "null";
            }

            if (reference is IAuthoredObject authored && reference is ScriptableObject definition
                && definition.GetType().IsDefined(typeof(AuthorableAttribute), false))
            {
                if (visiting.Contains(definition.GetInstanceID()))
                {
                    return "auth:" + authored.AuthoringId + "#cycle";
                }

                return "auth:" + authored.AuthoringId + "#" + DefinitionHashing.HashHex(Canonical(definition, visiting));
            }

            if (reference is IAuthoredObject plain)
            {
                return "auth:" + plain.AuthoringId;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long localId))
            {
                return "asset:" + guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            }

            return "object:" + reference.name;
        }

        private static string FormatPlain(object value, HashSet<int> visiting)
        {
            Type type = value.GetType();
            var parts = new List<KeyValuePair<string, string>>();
            foreach (FieldInfo field in type.GetFields(Fields))
            {
                bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
                if (!serialized || field.IsStatic || field.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    continue;
                }

                parts.Add(new KeyValuePair<string, string>(field.Name, Format(field.GetValue(value), visiting)));
            }

            parts.Sort((l, r) => string.CompareOrdinal(l.Key, r.Key));
            var builder = new StringBuilder("{");
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(';');
                }

                builder.Append(parts[i].Key).Append('=').Append(parts[i].Value);
            }

            return builder.Append('}').ToString();
        }
    }
}
