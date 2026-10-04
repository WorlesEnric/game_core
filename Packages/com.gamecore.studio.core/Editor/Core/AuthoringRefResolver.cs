// GameCore.Studio.Edit - AuthoringRef building, resolution and content stamps (docs/studio/03-authoring-contracts.md
// s1, s2; 02 s6 identity map).
//
// Building: GlobalObjectId text, the asset GUID (the scene's GUID for scene objects), the path (asset path, plus
// "#/" + hierarchy path for objects inside prefabs and scenes), the duck-typed authoring id, the DefinitionRef read the
// IDefinitionAsset way, and the content stamp.
//
// Content stamp: sha256 over a canonical text of the object's serialized authorable fields, read with
// SerializedObject (properties and [NonSerialized] fields are transient and excluded). Object references contribute
// the referenced object's identity, never an instance id. Components also contribute their placement (local
// position/rotation/scale and parent name), so a concurrent move is a stamp change. Objects without [Authorable]:
// a main asset hashes its file bytes; anything else hashes all of its visible serialized properties.
//
// Resolving: GlobalObjectId first, then the authoring id (index, open scenes, prefab stage), then the asset GUID, then
// the path. Stale checks (03 s2): destroyed, region unloaded (IResidencyQuery, or the holding scene is closed), moved
// (path differs; not blocking) and stamp changed.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Looks authored things up in the semantic index (the index registers itself with the resolver).</summary>
    public interface IAuthoringLookup
    {
        /// <summary>The index node ref with this authoring id, or null.</summary>
        AuthoringRef? FindByAuthoringId(string authoringId);

        /// <summary>The index node ref with this DefinitionRef text, or null.</summary>
        AuthoringRef? FindByDefinition(string definition);
    }

    /// <summary>The Editor implementation of <see cref="IAuthoringRefResolver"/>.</summary>
    public sealed class AuthoringRefResolver : IAuthoringRefResolver
    {
        private readonly AuthoringIdentity _identity;
        private readonly IAuthoringSource _source;

        public AuthoringRefResolver(AuthoringIdentity identity, IAuthoringSource source, IResidencyQuery? residency = null)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Residency = residency ?? new AllResidentQuery();
            Codec = new ValueCodec(new ObjectRefCodec(this));
        }

        /// <summary>Region residency (P1.1's RegionStreamer adapter replaces the default "all resident").</summary>
        public IResidencyQuery Residency { get; set; }

        /// <summary>Index lookups by authoring id and definition; set by the semantic index service.</summary>
        public IAuthoringLookup? Lookup { get; set; }

        public AuthoringIdentity Identity => _identity;

        /// <summary>The value codec whose references go through this resolver.</summary>
        public ValueCodec Codec { get; }

        public AuthoringRef? BuildRef(UnityEngine.Object target, AuthorScope? scope = null, bool includeStamp = true)
        {
            if (target == null)
            {
                return null;
            }

            if (target is GameObject gameObject)
            {
                MonoBehaviour? authored = _identity.FindAuthoredComponent(gameObject);
                if (authored != null)
                {
                    target = authored;
                }
            }

            if (IsStudioInternal(target))
            {
                return null;
            }

            AuthoringTypeInfo? info = _identity.Describe(target);
            string? path = ComputePath(target, out string? guid);
            string? global = GlobalIdOf(target);
            AuthoringKind kind = KindOf(target, info);
            string? authoringId = kind == AuthoringKind.Asset ? null : info?.ReadId(target);
            string? definition = _identity.GetDefinitionRef(target);
            string? stamp = includeStamp ? ComputeStamp(target) : null;
            if (authoringId == null && global == null && guid == null && path == null)
            {
                return null;
            }

            return new AuthoringRef(kind, authoringId, global, guid, path, definition, scope, stamp);
        }

        /// <summary>Resolves a ref; <see cref="ResolveResult.Object"/> is null when destroyed or unloaded.</summary>
        public ResolveResult Resolve(AuthoringRef reference)
        {
            if (reference == null)
            {
                throw new ArgumentNullException(nameof(reference));
            }

            if (reference.Kind == AuthoringKind.Location)
            {
                return new ResolveResult(reference, null, Array.Empty<StaleEntry>(), null);
            }

            UnityEngine.Object? found = Find(reference);
            if (found == null)
            {
                StaleReason reason = IsUnloaded(reference) ? StaleReason.RegionUnloaded : StaleReason.Destroyed;
                return new ResolveResult(reference, null, new[] { StaleEntry.Create(reference, reason) }, null);
            }

            List<StaleEntry> stale = new List<StaleEntry>();
            string? currentPath = ComputePath(found, out _);
            if (reference.Path != null && currentPath != null && !string.Equals(reference.Path, currentPath, StringComparison.Ordinal))
            {
                stale.Add(StaleEntry.Create(reference, StaleReason.Moved, null, currentPath));
            }

            string? currentStamp = ComputeStamp(found);
            if (reference.Stamp != null && currentStamp != null && !string.Equals(reference.Stamp, currentStamp, StringComparison.Ordinal))
            {
                stale.Add(StaleEntry.Create(reference, reference.Kind == AuthoringKind.Asset ? StaleReason.AssetChanged : StaleReason.StampChanged, currentStamp));
            }

            return new ResolveResult(reference, found, stale, currentStamp);
        }

        /// <summary>The 03 s2 form: the object when the ref is current, else the stale report.</summary>
        public bool TryResolve(AuthoringRef reference, out UnityEngine.Object? target, out StaleReport report)
        {
            ResolveResult result = Resolve(reference);
            target = result.Object;
            report = result.Report;
            return result.Object != null && result.IsCurrent;
        }

        /// <summary>The object a ref points at, without stale checks (null when not found).</summary>
        public UnityEngine.Object? Find(AuthoringRef reference)
        {
            UnityEngine.Object? found = null;
            if (reference.Global != null && GlobalObjectId.TryParse(reference.Global, out GlobalObjectId id))
            {
                found = Adapt(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id), reference);
            }

            if (found != null && reference.AuthoringId != null && !string.Equals(_identity.GetAuthoringId(found), reference.AuthoringId, StringComparison.Ordinal))
            {
                found = null;
            }

            if (found == null && reference.AuthoringId != null)
            {
                found = FindByAuthoringId(reference.AuthoringId, reference.AssetGuid);
            }

            if (found == null && reference.AssetGuid != null && reference.AuthoringId == null)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(reference.AssetGuid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    found = Adapt(LoadFromPath(assetPath + Fragment(reference.Path)), reference);
                }
            }

            if (found == null && reference.Path != null && reference.AuthoringId == null)
            {
                found = Adapt(LoadFromPath(reference.Path), reference);
            }

            return found;
        }

        /// <summary>An authored object with the given id: index first, then open scenes, the prefab stage and the hinted asset.</summary>
        public UnityEngine.Object? FindByAuthoringId(string authoringId, string? assetGuidHint = null)
        {
            AuthoringRef? indexed = Lookup?.FindByAuthoringId(authoringId);
            if (indexed?.Global != null && GlobalObjectId.TryParse(indexed.Global, out GlobalObjectId id))
            {
                UnityEngine.Object? viaIndex = Adapt(GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id), indexed);
                if (viaIndex != null && string.Equals(_identity.GetAuthoringId(viaIndex), authoringId, StringComparison.Ordinal))
                {
                    return viaIndex;
                }
            }

            foreach (AuthoredObjectEntry entry in _source.Enumerate(AuthoringSourceScope.OpenScenes | AuthoringSourceScope.PrefabStage))
            {
                if (string.Equals(entry.Type.ReadId(entry.Target), authoringId, StringComparison.Ordinal))
                {
                    return entry.Target;
                }
            }

            string? hintPath = assetGuidHint == null ? null : AssetDatabase.GUIDToAssetPath(assetGuidHint);
            if (!string.IsNullOrEmpty(hintPath) && !hintPath!.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                foreach (AuthoredObjectEntry entry in _source.EnumerateAsset(hintPath))
                {
                    if (string.Equals(entry.Type.ReadId(entry.Target), authoringId, StringComparison.Ordinal))
                    {
                        return entry.Target;
                    }
                }
            }

            if (indexed?.AssetGuid != null && !string.Equals(indexed.AssetGuid, assetGuidHint, StringComparison.Ordinal))
            {
                string indexedPath = AssetDatabase.GUIDToAssetPath(indexed.AssetGuid);
                if (!string.IsNullOrEmpty(indexedPath) && !indexedPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (AuthoredObjectEntry entry in _source.EnumerateAsset(indexedPath))
                    {
                        if (string.Equals(entry.Type.ReadId(entry.Target), authoringId, StringComparison.Ordinal))
                        {
                            return entry.Target;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>The content stamp of an object (see the file header), or null for a destroyed object.</summary>
        public string? ComputeStamp(UnityEngine.Object target)
        {
            if (target == null)
            {
                return null;
            }

            AuthoringTypeInfo? info = _identity.Describe(target);
            StringBuilder text = new StringBuilder();
            if (info != null)
            {
                text.Append("type=").Append(info.TypeId).Append('\n');
                SerializedObject serialized = new SerializedObject(target);
                foreach (AuthorMemberInfo member in info.Members)
                {
                    if (!member.IsSerializedField)
                    {
                        continue;
                    }

                    SerializedProperty? property = serialized.FindProperty(member.Name);
                    if (property == null)
                    {
                        continue;
                    }

                    text.Append(member.Name).Append('=').Append(Codec.FromProperty(property, member.ValueType).ToString(Formatting.None)).Append('\n');
                }

                if (target is Component component)
                {
                    AppendPlacement(text, component.transform);
                }

                return ContentStamp.OfUtf8(text.ToString());
            }

            if (EditorUtility.IsPersistent(target) && AssetDatabase.IsMainAsset(target))
            {
                string assetPath = AssetDatabase.GetAssetPath(target);
                string full = Path.GetFullPath(assetPath);
                if (File.Exists(full) && !Directory.Exists(full))
                {
                    return ContentStamp.Of(File.ReadAllBytes(full));
                }
            }

            if (target is GameObject gameObject)
            {
                text.Append("gameObject=").Append(gameObject.name).Append('\n');
                AppendPlacement(text, gameObject.transform);
                foreach (Component component in gameObject.GetComponents<Component>())
                {
                    if (component != null && !(component is Transform))
                    {
                        AppendSerialized(text, component);
                    }
                }
            }
            else
            {
                AppendSerialized(text, target);
            }

            return ContentStamp.OfUtf8(text.ToString());
        }

        /// <summary>The ref path of an object (see the file header) and the GUID of the asset or scene holding it.</summary>
        public string? ComputePath(UnityEngine.Object target, out string? guid)
        {
            guid = null;
            GameObject? gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (EditorUtility.IsPersistent(target))
            {
                string assetPath = AssetDatabase.GetAssetPath(target);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return null;
                }

                guid = NullIfEmpty(AssetDatabase.AssetPathToGUID(assetPath));
                if (gameObject != null)
                {
                    return assetPath + "#/" + HierarchyPath(gameObject.transform, null);
                }

                return AssetDatabase.IsMainAsset(target) ? assetPath : assetPath + "#" + target.name;
            }

            if (gameObject == null)
            {
                return null;
            }

            PrefabStage? stage = PrefabStageUtility.GetPrefabStage(gameObject);
            if (stage != null)
            {
                guid = NullIfEmpty(AssetDatabase.AssetPathToGUID(stage.assetPath));
                return stage.assetPath + "#/" + HierarchyPath(gameObject.transform, null);
            }

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
            {
                return null;
            }

            guid = NullIfEmpty(AssetDatabase.AssetPathToGUID(scene.path));
            return scene.path + "#/" + HierarchyPath(gameObject.transform, null);
        }

        /// <summary>Names from the root to <paramref name="transform"/>, joined with '/'.</summary>
        public static string HierarchyPath(Transform transform, Transform? stopAt)
        {
            List<string> names = new List<string>();
            for (Transform? current = transform; current != null && current != stopAt; current = current.parent)
            {
                names.Insert(0, current.name);
            }

            return string.Join("/", names);
        }

        /// <summary>True for Studio staging/preview objects (never addressable).</summary>
        public static bool IsStudioInternal(UnityEngine.Object target)
        {
            GameObject? gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null)
            {
                return false;
            }

            return (gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave && !EditorUtility.IsPersistent(gameObject);
        }

        private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;

        private static string? GlobalIdOf(UnityEngine.Object target)
        {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
            return id.identifierType == 0 ? null : id.ToString();
        }

        private static AuthoringKind KindOf(UnityEngine.Object target, AuthoringTypeInfo? info)
        {
            if (info != null)
            {
                return target is Component ? AuthoringKind.Entity : AuthoringKind.Definition;
            }

            if (target is GameObject || target is Component)
            {
                return AuthoringKind.SceneObject;
            }

            return AuthoringKind.Asset;
        }

        private UnityEngine.Object? Adapt(UnityEngine.Object? found, AuthoringRef reference)
        {
            if (found == null)
            {
                return null;
            }

            if (reference.Kind == AuthoringKind.Entity || reference.AuthoringId != null)
            {
                GameObject? gameObject = found as GameObject ?? (found as Component)?.gameObject;
                if (gameObject != null && (found is GameObject || _identity.Describe(found) == null))
                {
                    if (reference.AuthoringId != null)
                    {
                        foreach (MonoBehaviour behaviour in gameObject.GetComponents<MonoBehaviour>())
                        {
                            if (behaviour != null && string.Equals(_identity.GetAuthoringId(behaviour), reference.AuthoringId, StringComparison.Ordinal))
                            {
                                return behaviour;
                            }
                        }
                    }

                    MonoBehaviour? authored = _identity.FindAuthoredComponent(gameObject);
                    if (authored != null)
                    {
                        return authored;
                    }
                }
            }

            if (reference.Kind == AuthoringKind.SceneObject && found is Component sceneComponent && _identity.Describe(found) == null)
            {
                return sceneComponent.gameObject;
            }

            return found;
        }

        private static string Fragment(string? path)
        {
            if (path == null)
            {
                return string.Empty;
            }

            int hash = path.IndexOf('#');
            return hash < 0 ? string.Empty : path.Substring(hash);
        }

        /// <summary>Loads <c>assetPath[#/hierarchy | #subAssetName]</c> from an asset or an open scene.</summary>
        public static UnityEngine.Object? LoadFromPath(string path)
        {
            int hash = path.IndexOf('#');
            string assetPath = hash < 0 ? path : path.Substring(0, hash);
            string? fragment = hash < 0 ? null : path.Substring(hash + 1);
            if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                Scene scene = SceneManager.GetSceneByPath(assetPath);
                if (!scene.IsValid() || !scene.isLoaded || fragment == null || !fragment.StartsWith("/", StringComparison.Ordinal))
                {
                    return null;
                }

                return FindInRoots(scene.GetRootGameObjects(), fragment.Substring(1));
            }

            PrefabStage? stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && string.Equals(stage.assetPath, assetPath, StringComparison.Ordinal) && fragment != null && fragment.StartsWith("/", StringComparison.Ordinal))
            {
                GameObject? inStage = FindInRoots(new[] { stage.prefabContentsRoot }, fragment.Substring(1));
                if (inStage != null)
                {
                    return inStage;
                }
            }

            if (fragment == null)
            {
                return AssetDatabase.LoadMainAssetAtPath(assetPath);
            }

            if (fragment.StartsWith("/", StringComparison.Ordinal))
            {
                GameObject? root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                return root == null ? null : FindInRoots(new[] { root }, fragment.Substring(1));
            }

            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (candidate != null && string.Equals(candidate.name, fragment, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static GameObject? FindInRoots(GameObject[] roots, string hierarchyPath)
        {
            string[] names = hierarchyPath.Split('/');
            foreach (GameObject root in roots)
            {
                if (root == null || !string.Equals(root.name, names[0], StringComparison.Ordinal))
                {
                    continue;
                }

                Transform current = root.transform;
                bool found = true;
                for (int i = 1; i < names.Length && found; i++)
                {
                    Transform? next = null;
                    for (int c = 0; c < current.childCount; c++)
                    {
                        if (string.Equals(current.GetChild(c).name, names[i], StringComparison.Ordinal))
                        {
                            next = current.GetChild(c);
                            break;
                        }
                    }

                    if (next == null)
                    {
                        found = false;
                    }
                    else
                    {
                        current = next;
                    }
                }

                if (found)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        private bool IsUnloaded(AuthoringRef reference)
        {
            RegionResidency residency = Residency.ResidencyOf(reference);
            if (residency == RegionResidency.Unloaded || residency == RegionResidency.Unloading)
            {
                return true;
            }

            string? scenePath = null;
            if (reference.Path != null)
            {
                int hash = reference.Path.IndexOf('#');
                string assetPath = hash < 0 ? reference.Path : reference.Path.Substring(0, hash);
                if (assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    scenePath = assetPath;
                }
            }

            if (scenePath == null && reference.AssetGuid != null)
            {
                string guidPath = AssetDatabase.GUIDToAssetPath(reference.AssetGuid);
                if (guidPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    scenePath = guidPath;
                }
            }

            if (scenePath == null || !File.Exists(Path.GetFullPath(scenePath)))
            {
                return false;
            }

            Scene scene = SceneManager.GetSceneByPath(scenePath);
            return !scene.IsValid() || !scene.isLoaded;
        }

        private void AppendPlacement(StringBuilder text, Transform transform)
        {
            text.Append("placement=")
                .Append(Codec.FromClr(transform.localPosition).ToString(Formatting.None))
                .Append(Codec.FromClr(transform.localRotation).ToString(Formatting.None))
                .Append(Codec.FromClr(transform.localScale).ToString(Formatting.None))
                .Append(transform.parent == null ? "/" : transform.parent.name)
                .Append('\n');
        }

        private void AppendSerialized(StringBuilder text, UnityEngine.Object target)
        {
            text.Append('[').Append(target.GetType().FullName).Append("]\n");
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (string.Equals(property.propertyPath, "m_Script", StringComparison.Ordinal))
                {
                    continue;
                }

                text.Append(property.propertyPath).Append('=').Append(Codec.FromProperty(property).ToString(Formatting.None)).Append('\n');
            }
        }

        /// <summary>References as AuthoringRef JSON (no stamp); reads AuthoringRef objects, authoring ids and name@revision.</summary>
        private sealed class ObjectRefCodec : IObjectRefCodec
        {
            private readonly AuthoringRefResolver _resolver;

            public ObjectRefCodec(AuthoringRefResolver resolver)
            {
                _resolver = resolver;
            }

            public JToken WriteRef(UnityEngine.Object? target)
            {
                if (target == null)
                {
                    return JValue.CreateNull();
                }

                AuthoringRef? reference = _resolver.BuildRef(target, null, false);
                return reference == null ? JValue.CreateNull() : StudioJson.ToToken(reference);
            }

            public UnityEngine.Object? ReadRef(JToken value, Type expected, out string? problem)
            {
                problem = null;
                UnityEngine.Object? found = null;
                if (value.Type == JTokenType.Object)
                {
                    AuthoringRef? reference;
                    try
                    {
                        reference = value.ToObject<AuthoringRef>(JsonSerializer.Create(StudioJson.CreateSettings()));
                    }
                    catch (JsonException error)
                    {
                        problem = "not an AuthoringRef: " + error.Message;
                        return null;
                    }

                    if (reference == null)
                    {
                        problem = "not an AuthoringRef";
                        return null;
                    }

                    found = _resolver.Find(reference);
                }
                else if (value.Type == JTokenType.String)
                {
                    string text = value.Value<string>() ?? string.Empty;
                    if (text.IndexOf('@') > 0)
                    {
                        AuthoringRef? definition = _resolver.Lookup?.FindByDefinition(text);
                        found = definition == null ? null : _resolver.Find(definition);
                    }
                    else if (text.StartsWith("Assets/", StringComparison.Ordinal) || text.StartsWith("Packages/", StringComparison.Ordinal))
                    {
                        found = LoadFromPath(text);
                    }
                    else if (text.Length > 0)
                    {
                        found = _resolver.FindByAuthoringId(text);
                    }
                }
                else
                {
                    problem = "expected a reference (AuthoringRef, authoring id, name@revision or asset path), got " + ValueCodec.Describe(value);
                    return null;
                }

                if (found == null)
                {
                    problem = "the reference " + value.ToString(Formatting.None) + " does not resolve";
                    return null;
                }

                UnityEngine.Object? adapted = AdaptTo(found, expected);
                if (adapted == null)
                {
                    problem = "the reference resolves to a " + found.GetType().Name + ", expected " + expected.Name;
                }

                return adapted;
            }

            private static UnityEngine.Object? AdaptTo(UnityEngine.Object found, Type expected)
            {
                if (expected.IsInstanceOfType(found))
                {
                    return found;
                }

                GameObject? gameObject = found as GameObject ?? (found as Component)?.gameObject;
                if (gameObject != null)
                {
                    if (expected == typeof(GameObject))
                    {
                        return gameObject;
                    }

                    if (typeof(Component).IsAssignableFrom(expected))
                    {
                        Component? component = gameObject.GetComponent(expected);
                        return component == null ? null : component;
                    }
                }

                return null;
            }
        }
    }
}
