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
//
// Two stamps (P1.7a, A8 / SADR-012 studio):
//
//   content stamp      every authorable field. Change detection (Studio, stale bake, the content-stamp field of the
//                      asset); any edit changes it.
//   structural stamp   only the structural fields of the definition type: what a checkpoint's slot rows and the
//                      catalog recipe depend on. The recipe revision (ManifestDefinition.Revision, the catalog recipe
//                      implementation id, the bake report revision) derives from it, so a cosmetic or tuning edit keeps
//                      the revision and a save taken before the edit still restores.
//
// Until the authoring attributes carry a Structural marker (requested from P1.7b), the structural fields are the
// explicit per-type lists below; a type without a list is structural in every field (conservative: every edit
// changes its revision). A referenced authorable definition contributes its structural stamp to the structural
// canonical form, so a variant tint edit does not change the revision of the definition that lists the variant.
//
//   entity.definition   prefab, variants (membership, order and each variant's structural stamp), interactionKind,
//                       overridableFields. Not structural: defaultScaleMilli, startsVisible, startsAlive (seed defaults:
//                       restored slots carry their own values) and animatorBindings (presentation).
//   entity.variant      prefab. Not structural: tint.
//
// Validate (A10) refuses, with GP-ID-* diagnostics, a definition whose own authoring id is missing or malformed and any
// reference that is neither a persisted asset nor an authored object with a valid authoring id; the bake runs it
// before stamping, so an un-minted or unsaved reference never reaches a stamp. Nothing here mints ids or dirties assets.
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

        /// <summary>The object type id of entity definitions.</summary>
        public const string EntityDefinitionType = "entity.definition";

        /// <summary>The object type id of entity variants.</summary>
        public const string EntityVariantType = "entity.variant";

        /// <summary>The content stamp (lowercase hex SHA-256) of an authorable ScriptableObject.</summary>
        public static string ContentStamp(ScriptableObject asset)
        {
            return DefinitionHashing.HashHex(Canonical(asset, new HashSet<int>(), false));
        }

        /// <summary>The structural stamp (lowercase hex SHA-256 of the structural fields only): the recipe revision source.</summary>
        public static string StructuralStamp(ScriptableObject asset)
        {
            return DefinitionHashing.HashHex(Canonical(asset, new HashSet<int>(), true));
        }

        /// <summary>The canonical field set of an authorable ScriptableObject.</summary>
        public static CanonicalFields Canonical(ScriptableObject asset) => Canonical(asset, new HashSet<int>(), false);

        /// <summary>The structural canonical field set of an authorable ScriptableObject.</summary>
        public static CanonicalFields StructuralCanonical(ScriptableObject asset) => Canonical(asset, new HashSet<int>(), true);

        /// <summary>True when <paramref name="typeId"/> has an explicit structural field list.</summary>
        public static bool HasStructuralList(string typeId) =>
            string.Equals(typeId, EntityDefinitionType, StringComparison.Ordinal) || string.Equals(typeId, EntityVariantType, StringComparison.Ordinal);

        /// <summary>
        /// The explicit structural field names of a definition type (a fresh array, ordinal order), or an empty array when the
        /// type has no list (every field is then structural).
        /// </summary>
        public static string[] StructuralFieldNames(string typeId)
        {
            switch (typeId)
            {
                case EntityDefinitionType:
                    return new[] { "interactionKind", "overridableFields", "prefab", "variants" };
                case EntityVariantType:
                    return new[] { "prefab" };
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>True when field <paramref name="fieldName"/> of type <paramref name="typeId"/> takes part in the structural stamp.</summary>
        public static bool IsStructural(string typeId, string fieldName)
        {
            if (!HasStructuralList(typeId))
            {
                return true;
            }

            return Array.IndexOf(StructuralFieldNames(typeId), fieldName) >= 0;
        }

        /// <summary>
        /// The problems that refuse stamping <paramref name="asset"/> (A10): a missing or malformed own authoring id, and any
        /// reference (transitively through referenced authorable definitions) that is neither a persisted asset nor an
        /// authored object with a valid authoring id. Reads only; never mints or dirties.
        /// </summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(ScriptableObject asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var diagnostics = new List<GameplayDiagnostic>();
            ValidateDefinition(asset, new HashSet<int>(), diagnostics);
            return diagnostics;
        }

        private static void ValidateDefinition(ScriptableObject asset, HashSet<int> visited, List<GameplayDiagnostic> diagnostics)
        {
            if (!visited.Add(asset.GetInstanceID()))
            {
                return;
            }

            string subject = SubjectOf(asset);
            if (asset is IAuthoredObject authored)
            {
                CheckIdentity(authored.AuthoringId, asset.name, subject, diagnostics);
            }

            foreach (FieldInfo field in AuthorableFields(asset.GetType()))
            {
                ValidateValue(field.GetValue(asset), asset.name + "." + field.Name, subject, visited, diagnostics, 0);
            }
        }

        private static void ValidateValue(object? value, string where, string subject, HashSet<int> visited, List<GameplayDiagnostic> diagnostics, int depth)
        {
            if (value == null || depth > 16)
            {
                return;
            }

            switch (value)
            {
                case string _:
                case bool _:
                case int _:
                case long _:
                case uint _:
                case float _:
                case double _:
                case Vector2 _:
                case Vector3 _:
                case Color _:
                case Enum _:
                    return;
                case UnityEngine.Object reference:
                    ValidateReference(reference, where, subject, visited, diagnostics);
                    return;
                case IList list:
                    for (int i = 0; i < list.Count; i++)
                    {
                        ValidateValue(list[i], where + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", subject, visited, diagnostics, depth + 1);
                    }

                    return;
            }

            Type type = value.GetType();
            if (type.IsPrimitive)
            {
                return;
            }

            foreach (FieldInfo field in type.GetFields(Fields))
            {
                bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
                if (!serialized || field.IsStatic || field.IsDefined(typeof(NonSerializedAttribute), true))
                {
                    continue;
                }

                ValidateValue(field.GetValue(value), where + "." + field.Name, subject, visited, diagnostics, depth + 1);
            }
        }

        private static void ValidateReference(UnityEngine.Object reference, string where, string subject, HashSet<int> visited, List<GameplayDiagnostic> diagnostics)
        {
            // Unity's fake-null: a destroyed or missing reference stamps as null and is the validators' business.
            if (reference == null)
            {
                return;
            }

            if (reference is IAuthoredObject authored)
            {
                CheckIdentity(authored.AuthoringId, where + " -> " + reference.name, subject, diagnostics);
                if (reference is ScriptableObject definition && definition.GetType().IsDefined(typeof(AuthorableAttribute), false))
                {
                    ValidateDefinition(definition, visited, diagnostics);
                }

                return;
            }

            if (EditorUtility.IsPersistent(reference) || AssetDatabase.Contains(reference))
            {
                return;
            }

            diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, subject,
                where + " references " + reference.name + " (" + reference.GetType().Name
                + "), which is neither a saved asset nor an authored object; save it as an asset or reference an authored object"));
        }

        private static void CheckIdentity(string? id, string what, string subject, List<GameplayDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(id))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, subject,
                    what + " has no authoring id; open and save it (or run its authoring tool) before baking - the bake never mints ids"));
            }
            else if (!AuthoringIds.IsValid(id))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.InvalidAuthoringId, subject,
                    what + " has a non-canonical authoring id '" + id + "'"));
            }
        }

        private static string SubjectOf(ScriptableObject asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            return asset is IAuthoredObject authored && !string.IsNullOrEmpty(authored.AuthoringId) ? authored.AuthoringId : asset.name;
        }

        private static CanonicalFields Canonical(ScriptableObject asset, HashSet<int> visiting, bool structural)
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
                if (structural && !IsStructural(authorable.ObjectTypeId, field.Name))
                {
                    continue;
                }

                fields.Add(field.Name, Format(field.GetValue(asset), visiting, structural));
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

        private static string Format(object? value, HashSet<int> visiting, bool structural)
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
                    return FormatReference(reference, visiting, structural);
                case IList list:
                    var items = new StringBuilder("[");
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0)
                        {
                            items.Append(',');
                        }

                        items.Append(Format(list[i], visiting, structural));
                    }

                    return items.Append(']').ToString();
                default:
                    return FormatPlain(value, visiting, structural);
            }
        }

        private static string FormatReference(UnityEngine.Object reference, HashSet<int> visiting, bool structural)
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

                return "auth:" + authored.AuthoringId + "#" + DefinitionHashing.HashHex(Canonical(definition, visiting, structural));
            }

            if (reference is IAuthoredObject plain)
            {
                return "auth:" + plain.AuthoringId;
            }

            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(reference, out string guid, out long localId))
            {
                return "asset:" + guid + ":" + localId.ToString(CultureInfo.InvariantCulture);
            }

            // Neither authored nor a saved asset: Validate refuses it before any stamp is persisted (A10); the text stays as
            // it was, so the stamps of valid content are unchanged.
            return "object:" + reference.name;
        }

        private static string FormatPlain(object value, HashSet<int> visiting, bool structural)
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

                parts.Add(new KeyValuePair<string, string>(field.Name, Format(field.GetValue(value), visiting, structural)));
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
