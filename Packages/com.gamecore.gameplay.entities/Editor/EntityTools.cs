// GameCore.Gameplay.Entities.Editor - entity authoring operations (P1.1, Studio 03 s4/s5).
//
// Inspector-free static tools, each one [AuthorOperation] with a stable tool id. Every tool records Undo, marks the
// edited scene dirty and validates its arguments before touching anything; a refused tool changes nothing and throws
// an ArgumentException whose message starts with the GP-* code.
//
//   entity.place             place a definition in the active scene at a pose
//   entity.duplicate         copy a placed entity with a fresh authoring id
//   entity.replaceDefinition swap a placed entity's definition, keeping id, pose, variant (clamped) and overrides
//   entity.setVariant        select a variant
//   entity.applyOverride     set (or clear, with an empty value) one overridable field
//   entity.layoutRing        place N copies of a definition on a ring, facing its centre
//   entity.layoutLine        place N copies of a definition evenly along a segment
//
// AuthoredAssetLookup (P1.7b) is the shared Editor helper the packages' authoring.migrateRefs migrations and
// validators use to find authored assets by [Authorable] type id and to resolve legacy string references.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Entities.Editor
{
    /// <summary>The entity.* authoring operations.</summary>
    public static class EntityTools
    {
        [AuthorOperation("entity.place", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Requires = "world.region", Validator = typeof(EntityValidator),
            Doc = "Places a definition in the active region scene at a position (m) and yaw (deg).")]
        public static AuthoredEntity Place(
            EntityDefinition definition,
            [AuthorArg(Unit = "m", Doc = "World position.")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Object name; defaults to the definition name.")] string name = "")
        {
            return PlaceIn(SceneManager.GetActiveScene(), definition, position, yaw, name);
        }

        /// <summary><see cref="Place"/> into an explicit scene (authoring scripts and tests).</summary>
        public static AuthoredEntity PlaceIn(Scene scene, EntityDefinition definition, Vector3 position, float yaw, string name)
        {
            if (definition == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityMissingDefinition + ": a definition is required");
            }

            if (definition.Prefab == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.DefinitionMissingPrefab + ": definition " + definition.name + " has no prefab");
            }

            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new ArgumentException("GP-ENT-002: the target scene is not loaded");
            }

            definition.EnsureAuthoringId();
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, scene);
            instance.name = string.IsNullOrEmpty(name) ? definition.name : name;
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            AuthoredEntity entity = instance.GetComponent<AuthoredEntity>();
            if (entity == null)
            {
                entity = instance.AddComponent<AuthoredEntity>();
            }

            entity.SetDefinition(definition);
            entity.SetVariant(0);
            if (!AuthoringIds.IsValid(entity.AuthoringId))
            {
                entity.EnsureAuthoringId();
            }

            Undo.RegisterCreatedObjectUndo(instance, "entity.place");
            Touch(entity);
            return entity;
        }

        [AuthorOperation("entity.duplicate", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(EntityValidator), Doc = "Copies a placed entity (definition, variant, overrides) with a fresh authoring id.")]
        public static AuthoredEntity Duplicate(
            AuthoredEntity entity,
            [AuthorArg(Unit = "m", Required = false, Doc = "Offset of the copy.")] Vector3 offset = default)
        {
            Require(entity);
            EntityDefinition definition = entity.Definition!;
            Transform source = entity.transform;
            AuthoredEntity copy = PlaceIn(
                entity.gameObject.scene,
                definition,
                source.position + offset,
                source.eulerAngles.y,
                entity.gameObject.name + " (copy)");
            copy.SetVariant(entity.Variant);
            for (int i = 0; i < entity.Overrides.Entries.Count; i++)
            {
                OverrideEntry entry = entity.Overrides.Entries[i];
                copy.Overrides.Set(entry.Field, entry.Value);
            }

            if (string.Equals(copy.AuthoringId, entity.AuthoringId, StringComparison.Ordinal))
            {
                copy.RemintAuthoringId();
            }

            Touch(copy);
            return copy;
        }

        [AuthorOperation("entity.replaceDefinition", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(EntityValidator),
            Doc = "Replaces a placed entity's definition; id, pose and overrides are kept and the variant is clamped.")]
        public static AuthoredEntity ReplaceDefinition(AuthoredEntity entity, [AuthorArg(Category = "entity.definition")] EntityDefinition definition)
        {
            if (entity == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.MissingAuthoringId + ": an entity is required");
            }

            if (definition == null || definition.Prefab == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.DefinitionMissingPrefab + ": the replacement needs a prefab");
            }

            Transform source = entity.transform;
            AuthoredEntity replaced = PlaceIn(entity.gameObject.scene, definition, source.position, source.eulerAngles.y, entity.gameObject.name);
            replaced.transform.SetParent(source.parent, true);
            CopyIdentity(entity, replaced);
            replaced.SetVariant(Math.Min(entity.Variant, definition.VariantCount - 1));
            for (int i = 0; i < entity.Overrides.Entries.Count; i++)
            {
                OverrideEntry entry = entity.Overrides.Entries[i];
                if (definition.IsOverridable(entry.Field))
                {
                    replaced.Overrides.Set(entry.Field, entry.Value);
                }
            }

            Undo.DestroyObjectImmediate(entity.gameObject);
            Touch(replaced);
            return replaced;
        }

        [AuthorOperation("entity.setVariant", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live,
            Validator = typeof(EntityValidator), Doc = "Selects variant 0..n of the entity's definition.")]
        public static void SetVariant(AuthoredEntity entity, [AuthorArg(Min = 0, Doc = "Variant index.")] int variant)
        {
            Require(entity);
            int count = entity.Definition!.VariantCount;
            if (variant < 0 || variant >= count)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityVariantOutOfRange + ": variant " + variant + " of " + count);
            }

            Undo.RecordObject(entity, "entity.setVariant");
            entity.SetVariant(variant);
            Touch(entity);
        }

        [AuthorOperation("entity.applyOverride", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(EntityValidator), Doc = "Sets one overridable field (scaleMilli, visible, alive, tint); an empty value clears it.")]
        public static void ApplyOverride(
            AuthoredEntity entity,
            [AuthorArg(Doc = "Overridable field name.")] string field,
            [AuthorArg(Required = false, Doc = "Canonical value text; empty clears the override.")] string value)
        {
            Require(entity);
            if (!entity.Definition!.IsOverridable(field))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityUnknownOverride + ": '" + field + "' is not overridable on " + entity.Definition.name);
            }

            if (!string.IsNullOrEmpty(value) && !OverrideFields.IsValidValue(field, value, out string error))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityUnknownOverride + ": " + error);
            }

            Undo.RecordObject(entity, "entity.applyOverride");
            entity.Overrides.Set(field, value ?? string.Empty);
            Touch(entity);
        }

        [AuthorOperation("entity.layoutRing", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Requires = "world.region", Doc = "Places count copies of a definition on a ring around a centre, each facing it.")]
        public static IReadOnlyList<AuthoredEntity> LayoutRing(
            EntityDefinition definition,
            [AuthorArg(Unit = "m")] Vector3 center,
            [AuthorArg(Unit = "m", Min = 0.1)] float radius,
            [AuthorArg(Min = 1, Max = 256)] int count,
            [AuthorArg(Unit = "deg", Required = false)] float startAngle = 0f)
        {
            return LayoutRingIn(SceneManager.GetActiveScene(), definition, center, radius, count, startAngle);
        }

        public static IReadOnlyList<AuthoredEntity> LayoutRingIn(Scene scene, EntityDefinition definition, Vector3 center, float radius, int count, float startAngle)
        {
            if (count < 1 || count > 256 || radius < 0.1f)
            {
                throw new ArgumentException("GP-ENT-005: a ring needs 1..256 copies and a radius of at least 0.1 m");
            }

            var placed = new List<AuthoredEntity>(count);
            for (int i = 0; i < count; i++)
            {
                float angle = startAngle + 360f * i / count;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 position = center + new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * radius;
                float facing = angle + 180f;
                placed.Add(PlaceIn(scene, definition, position, facing, definition.name + " " + (i + 1)));
            }

            return placed;
        }

        [AuthorOperation("entity.layoutLine", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Requires = "world.region", Doc = "Places count copies of a definition evenly from start to end, facing along the line.")]
        public static IReadOnlyList<AuthoredEntity> LayoutLine(
            EntityDefinition definition,
            [AuthorArg(Unit = "m")] Vector3 start,
            [AuthorArg(Unit = "m")] Vector3 end,
            [AuthorArg(Min = 1, Max = 256)] int count)
        {
            return LayoutLineIn(SceneManager.GetActiveScene(), definition, start, end, count);
        }

        public static IReadOnlyList<AuthoredEntity> LayoutLineIn(Scene scene, EntityDefinition definition, Vector3 start, Vector3 end, int count)
        {
            if (count < 1 || count > 256)
            {
                throw new ArgumentException("GP-ENT-005: a line needs 1..256 copies");
            }

            Vector3 direction = end - start;
            float facing = direction.sqrMagnitude > 0f ? Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg : 0f;
            var placed = new List<AuthoredEntity>(count);
            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                placed.Add(PlaceIn(scene, definition, Vector3.Lerp(start, end, t), facing, definition.name + " " + (i + 1)));
            }

            return placed;
        }

        [AuthorOperation("entity.setMaterialTexture", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Scope = AuthorScope.Definition, Validator = typeof(EntityValidator),
            Doc = "Binds an imported Texture2D to a renderer material slot on every view of this definition. Omit texture to clear the binding. Changes content stamp, not recipe revision; rebuild views to apply.")]
        public static void SetMaterialTexture(
            EntityDefinition definition,
            [AuthorArg(Category = "texture.texture2d", Required = false, Doc = "Imported texture AuthoringRef; omit to clear.")] Texture2D? texture,
            [AuthorArg(Min = 0, Required = false, Doc = "Renderer index in prefab depth-first order, including inactive children.")] int renderer = 0,
            [AuthorArg(Min = 0, Required = false, Doc = "Renderer material slot.")] int slot = 0,
            [AuthorArg(Required = false, Doc = "Shader Texture2D property, for example _BaseMap or _MainTex.")] string property = "_BaseMap")
        {
            if (definition == null) throw new ArgumentException(MaterialTextureBinding.InvalidCode + ": an entity definition is required");
            var binding = new MaterialTextureBinding { renderer = renderer, slot = slot, property = property, texture = texture };
            string problem = MaterialTextureBinding.Validate(definition, binding);
            if (problem.Length > 0) throw new ArgumentException(MaterialTextureBinding.InvalidCode + ": " + problem);
            if (texture != null && !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(texture)) is TextureImporter))
                throw new ArgumentException(MaterialTextureBinding.InvalidCode + ": texture must be an imported image asset");
            Undo.RecordObject(definition, "entity.setMaterialTexture");
            definition.SetMaterialTexture(binding);
            EditorUtility.SetDirty(definition);
        }

        private static void Require(AuthoredEntity entity)
        {
            if (entity == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.MissingAuthoringId + ": an entity is required");
            }

            if (entity.Definition == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityMissingDefinition + ": " + entity.name + " has no definition");
            }
        }

        private static void CopyIdentity(AuthoredEntity from, AuthoredEntity to)
        {
            var serialized = new SerializedObject(to);
            SerializedProperty id = serialized.FindProperty("authoringId");
            id.stringValue = from.AuthoringId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Touch(AuthoredEntity entity)
        {
            EditorUtility.SetDirty(entity);
            if (PrefabUtility.IsPartOfPrefabInstance(entity))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(entity);
            }

            if (entity.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(entity.gameObject.scene);
            }
        }
    }

    /// <summary>Finds authored assets by [Authorable] type id and resolves legacy string references to them (P1.7b).</summary>
    public static class AuthoredAssetLookup
    {
        /// <summary>The [Authorable] type id of <paramref name="value"/>'s type, or empty.</summary>
        public static string TypeIdOf(UnityEngine.Object? value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            object[] attributes = value.GetType().GetCustomAttributes(typeof(AuthorableAttribute), false);
            return attributes.Length == 1 ? ((AuthorableAttribute)attributes[0]).ObjectTypeId : string.Empty;
        }

        /// <summary>True when <paramref name="value"/> is null or its type's [Authorable] id is <paramref name="typeId"/>.</summary>
        public static bool IsNullOrOfType(UnityEngine.Object? value, string typeId) =>
            value == null || string.Equals(TypeIdOf(value), typeId, StringComparison.Ordinal);

        /// <summary>Project-relative asset folders: empty or null means the whole Assets folder.</summary>
        public static string[] Folders(IReadOnlyList<string>? folders)
        {
            if (folders == null || folders.Count == 0)
            {
                return new[] { "Assets" };
            }

            var list = new List<string>();
            for (int i = 0; i < folders.Count; i++)
            {
                if (!string.IsNullOrEmpty(folders[i]) && AssetDatabase.IsValidFolder(folders[i]))
                {
                    list.Add(folders[i]);
                }
            }

            return list.Count > 0 ? list.ToArray() : new[] { "Assets" };
        }

        /// <summary>Every asset of type <typeparamref name="T"/> under <paramref name="folders"/>, in path order.</summary>
        public static IReadOnlyList<T> AssetsOf<T>(IReadOnlyList<string>? folders) where T : UnityEngine.Object
        {
            var result = new List<T>();
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, Folders(folders));
            var paths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            paths.Sort(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < paths.Count; i++)
            {
                if (!seen.Add(paths[i]))
                {
                    continue;
                }

                UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(paths[i]);
                for (int o = 0; o < objects.Length; o++)
                {
                    if (objects[o] is T typed)
                    {
                        result.Add(typed);
                    }
                }
            }

            return result;
        }

        /// <summary>Every ScriptableObject asset whose type carries [Authorable] <paramref name="typeId"/>.</summary>
        public static IReadOnlyList<ScriptableObject> AssetsOfType(string typeId, IReadOnlyList<string>? folders)
        {
            var result = new List<ScriptableObject>();
            var types = new List<Type>();
            foreach (Type type in TypeCache.GetTypesWithAttribute<AuthorableAttribute>())
            {
                object[] attributes = type.GetCustomAttributes(typeof(AuthorableAttribute), false);
                if (attributes.Length == 1 && typeof(ScriptableObject).IsAssignableFrom(type)
                    && string.Equals(((AuthorableAttribute)attributes[0]).ObjectTypeId, typeId, StringComparison.Ordinal))
                {
                    types.Add(type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int t = 0; t < types.Count; t++)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + types[t].Name, Folders(folders));
                Array.Sort(guids, StringComparer.Ordinal);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    UnityEngine.Object[] objects = AssetDatabase.LoadAllAssetsAtPath(path);
                    for (int o = 0; o < objects.Length; o++)
                    {
                        if (objects[o] is ScriptableObject asset && types[t].IsInstanceOfType(asset)
                            && seen.Add(path + "#" + asset.name + "#" + types[t].FullName))
                        {
                            result.Add(asset);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resolves a legacy string reference among <paramref name="candidates"/>: an exact authoring id first, then an
        /// alias (<see cref="IAuthoringAlias"/>), then the definition name or asset name. Null when nothing matches or
        /// the name is ambiguous (<paramref name="ambiguous"/> true).
        /// </summary>
        public static ScriptableObject? Resolve(string text, IReadOnlyList<ScriptableObject> candidates, out bool ambiguous)
        {
            ambiguous = false;
            if (string.IsNullOrEmpty(text) || candidates == null)
            {
                return null;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] is IAuthoredObject authored && string.Equals(authored.AuthoringId, text, StringComparison.Ordinal))
                {
                    return candidates[i];
                }
            }

            ScriptableObject? match = Unique(text, candidates, c => c is IAuthoringAlias alias ? alias.AuthoringAlias : string.Empty, out ambiguous);
            if (match != null || ambiguous)
            {
                return match;
            }

            match = Unique(text, candidates, c => c is IDefinitionAsset definition ? definition.DefinitionName : string.Empty, out ambiguous);
            if (match != null || ambiguous)
            {
                return match;
            }

            return Unique(text, candidates, c => c.name, out ambiguous);
        }

        /// <summary>The asset path and object name of <paramref name="value"/> for migration reports.</summary>
        public static string Describe(UnityEngine.Object value)
        {
            string path = AssetDatabase.GetAssetPath(value);
            return (string.IsNullOrEmpty(path) ? "(unsaved)" : path) + (AssetDatabase.IsSubAsset(value) ? "#" + value.name : string.Empty);
        }

        private static ScriptableObject? Unique(string text, IReadOnlyList<ScriptableObject> candidates, Func<ScriptableObject, string> key, out bool ambiguous)
        {
            ambiguous = false;
            ScriptableObject? found = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] != null && string.Equals(key(candidates[i]), text, StringComparison.Ordinal))
                {
                    if (found != null && found != candidates[i])
                    {
                        ambiguous = true;
                        return null;
                    }

                    found = candidates[i];
                }
            }

            return found;
        }
    }
}
