// GameCore.Studio.Views - an index contributor for the relationships the base projection cannot see (03 s3, SR-2.2).
//
// The semantic index projects the top-level [AuthorRef] members of an authored object as `references` edges. The
// gameplay packages keep many references one level down, inside [Serializable] list elements: quest rewards and
// objectives (rewards[i].target), vendor stock, dialogue option and branch conditions, rule inline conditions and
// actions; others are strings: P1.3's NpcDefinition.dialogueGraph names a graph by its npcGraphRef ("dialogue.maren"),
// and [AuthorField(Type = "authoringId")] strings name placed entities (speakerEntityId, targetEntityId). Region
// membership is a third gap: the region marker of a scene (P1.1's AuthoredRegion) is not [Authorable], so scene
// entities had no `contains` edge from their region. Without these edges "impact of deleting the lantern" misses the
// quest reward and the vendor stock, and Maren's neighbourhood misses her dialogue graph and region.
//
// This contributor adds, for every projected object (generic: any plugin's types, by attribute and member name):
//   * `references` edges for UnityEngine.Object values of [AuthorRef] fields nested in [Serializable] elements (up to
//     four levels), labelled with the field path (rewards[0].target);
//   * `references` edges to name references: a string [AuthorRef] value becomes an endpoint with path
//     "byname:<category>:<value>", resolved by IndexGraph against nodes of that category;
//   * `references` edges to entities named by authoringId-typed strings (endpoint = the authoring id);
//   * a second edge of a more specific kind where the member says so: `triggers` (member name contains "trigger"),
//     `spawns` (category asset.prefab or member name "prefab"), `bindsUi` (category starting with "ui.");
//   * `contains` edges from a scene's region to the scene's authored objects: the region is the definition of the
//     scene's root component implementing an interface named IAuthoredRegion (duck-typed, no gameplay dependency).
// Field paths are kept per contributor instance (Labels) for the views' edge labels.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Views
{
    /// <summary>Adds nested, by-name, entity-id and region-membership edges to the semantic index.</summary>
    public sealed class NestedReferenceContributor : IIndexContributor
    {
        public const int MaxDepth = 4;

        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly StudioRuntime _runtime;
        private readonly Dictionary<string, string> _labels = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();

        public NestedReferenceContributor(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>Field paths of the contributed edges, by <see cref="EdgeKinds.Key"/> of the raw endpoints.</summary>
        public IReadOnlyDictionary<string, string> Labels => _labels;

        public void Contribute(AuthoredObjectEntry entry, IndexNode node, IndexContributionSink sink)
        {
            AuthoringRef from = SemanticIndexService.EdgeRef(node.Ref);
            foreach (AuthorMemberInfo member in entry.Type.Members)
            {
                object? value;
                try
                {
                    value = member.GetValue(entry.Target);
                }
                catch (TargetInvocationException)
                {
                    continue;
                }

                if (value == null)
                {
                    continue;
                }

                if (member.IsReference)
                {
                    string? category = member.Reference!.Category;
                    foreach (object? item in member.IsCollection ? AuthoringIdentity.Items(value) : new[] { value })
                    {
                        if (item is string text)
                        {
                            AddByName(sink, from, category ?? string.Empty, text, member.Name);
                        }
                        else if (item is UnityEngine.Object target && target != null)
                        {
                            AddSpecific(sink, from, target, member.Name, category);
                        }
                    }

                    continue;
                }

                if (IsAuthoringId(member.Field) && value is string id)
                {
                    AddEntity(sink, from, id, member.Name);
                    continue;
                }

                if (IsComplex(member.ValueType))
                {
                    Walk(sink, from, value, member.Name, 0);
                }
            }

            if (entry.Location == AuthoredObjectLocation.Scene && entry.Target is Component component && !node.Provides("world.region"))
            {
                AuthoringRef? region = RegionOfScene(component.gameObject.scene);
                if (region != null && !string.Equals(region.IdentityKey, from.IdentityKey, StringComparison.Ordinal))
                {
                    sink.AddEdge(region, from, EdgeKind.Contains);
                }
            }
        }

        private void Walk(IndexContributionSink sink, AuthoringRef from, object value, string path, int depth)
        {
            if (depth >= MaxDepth)
            {
                return;
            }

            if (value is IList list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    object? item = list[i];
                    if (item != null && !(item is UnityEngine.Object) && IsComplex(item.GetType()))
                    {
                        Walk(sink, from, item, path + "[" + i + "]", depth + 1);
                    }
                }

                return;
            }

            foreach (FieldInfo field in SerializedFields(value.GetType()))
            {
                object? child = field.GetValue(value);
                if (child == null)
                {
                    continue;
                }

                string childPath = path + "." + field.Name;
                AuthorRefAttribute? reference = AuthoringMetadata.Reference(field);
                if (reference != null)
                {
                    foreach (object? item in child is IList items && !(child is string) ? AuthoringIdentity.Items(items) : new[] { child })
                    {
                        if (item is UnityEngine.Object target && target != null)
                        {
                            AddNested(sink, from, target, childPath, reference.Category);
                        }
                        else if (item is string text)
                        {
                            AddByName(sink, from, reference.Category ?? string.Empty, text, childPath);
                        }
                    }

                    continue;
                }

                if (IsAuthoringId(AuthoringMetadata.Field(field)) && child is string id)
                {
                    AddEntity(sink, from, id, childPath);
                    continue;
                }

                if (!(child is UnityEngine.Object) && IsComplex(field.FieldType))
                {
                    Walk(sink, from, child, childPath, depth + 1);
                }
            }
        }

        private void AddNested(IndexContributionSink sink, AuthoringRef from, UnityEngine.Object target, string path, string? category)
        {
            AuthoringRef? to = _runtime.Resolver.BuildRef(target, null, false);
            if (to == null)
            {
                return;
            }

            Add(sink, from, to, EdgeKind.References, path);
            EdgeKind? specific = Classify(path, category);
            if (specific.HasValue)
            {
                Add(sink, from, to, specific.Value, path);
            }
        }

        private void AddSpecific(IndexContributionSink sink, AuthoringRef from, UnityEngine.Object target, string member, string? category)
        {
            EdgeKind? specific = Classify(member, category);
            if (!specific.HasValue)
            {
                return;
            }

            AuthoringRef? to = _runtime.Resolver.BuildRef(target, null, false);
            if (to != null)
            {
                Add(sink, from, to, specific.Value, member);
            }
        }

        private void AddByName(IndexContributionSink sink, AuthoringRef from, string category, string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            AuthoringRef to = new AuthoringRef(AuthoringKind.Definition, path: IndexGraph.ByNamePrefix + category + ":" + value.Trim());
            Add(sink, from, to, EdgeKind.References, path);
        }

        private void AddEntity(IndexContributionSink sink, AuthoringRef from, string authoringId, string path)
        {
            if (string.IsNullOrWhiteSpace(authoringId))
            {
                return;
            }

            AuthoringRef to = new AuthoringRef(AuthoringKind.Entity, authoringId: authoringId.Trim());
            if (!string.Equals(to.IdentityKey, from.IdentityKey, StringComparison.Ordinal))
            {
                Add(sink, from, to, EdgeKind.References, path);
            }
        }

        private void Add(IndexContributionSink sink, AuthoringRef from, AuthoringRef to, EdgeKind kind, string path)
        {
            sink.AddEdge(from, to, kind);
            string key = EdgeKinds.Key(from.IdentityKey, to.IdentityKey, kind);
            if (!_labels.ContainsKey(key))
            {
                _labels.Add(key, path);
            }
        }

        private AuthoringRef? RegionOfScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null || !ImplementsRegion(behaviour.GetType()))
                    {
                        continue;
                    }

                    PropertyInfo? definition = behaviour.GetType().GetProperty("Definition", BindingFlags.Public | BindingFlags.Instance);
                    if (definition != null && definition.GetValue(behaviour) is UnityEngine.Object asset && asset != null)
                    {
                        AuthoringRef? reference = _runtime.Resolver.BuildRef(asset, null, false);
                        if (reference != null)
                        {
                            return SemanticIndexService.EdgeRef(reference);
                        }
                    }

                    PropertyInfo? id = behaviour.GetType().GetProperty("AuthoringId", BindingFlags.Public | BindingFlags.Instance);
                    if (id != null && id.GetValue(behaviour) is string authoringId && authoringId.Length > 0)
                    {
                        return new AuthoringRef(AuthoringKind.Region, authoringId: authoringId);
                    }
                }
            }

            return null;
        }

        private FieldInfo[] SerializedFields(Type type)
        {
            if (_fields.TryGetValue(type, out FieldInfo[]? known))
            {
                return known;
            }

            List<FieldInfo> fields = new List<FieldInfo>();
            for (Type? current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (FieldInfo field in current.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (AuthoringIdentity.IsUnitySerialized(field))
                    {
                        fields.Add(field);
                    }
                }
            }

            FieldInfo[] result = fields.ToArray();
            _fields[type] = result;
            return result;
        }

        internal static bool ImplementsRegion(Type type)
        {
            foreach (Type contract in type.GetInterfaces())
            {
                if (contract.Name == "IAuthoredRegion")
                {
                    return true;
                }
            }

            return false;
        }

        internal static EdgeKind? Classify(string member, string? category)
        {
            string name = member.ToLowerInvariant();
            int dot = name.LastIndexOf('.');
            string leaf = dot >= 0 ? name.Substring(dot + 1) : name;
            if (leaf.Contains("trigger"))
            {
                return EdgeKind.Triggers;
            }

            if (string.Equals(category, "asset.prefab", StringComparison.Ordinal) || leaf == "prefab")
            {
                return EdgeKind.Spawns;
            }

            if (category != null && category.StartsWith("ui.", StringComparison.Ordinal))
            {
                return EdgeKind.BindsUi;
            }

            return null;
        }

        private static bool IsAuthoringId(AuthorFieldAttribute? field) => field != null && string.Equals(field.Type, "authoringId", StringComparison.Ordinal);

        /// <summary>True for list types and [Serializable] classes/structs that can hold nested references.</summary>
        internal static bool IsComplex(Type type)
        {
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                return false;
            }

            if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal) && type.IsValueType)
            {
                return false;
            }

            if (type.IsArray)
            {
                Type? element = type.GetElementType();
                return element != null && IsComplex(element);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return IsComplex(type.GetGenericArguments()[0]);
            }

            return type.IsDefined(typeof(SerializableAttribute), false);
        }
    }

    /// <summary>Registers the views' index contributor on a runtime (once) and rebuilds the index so its edges exist.</summary>
    public static class ViewIndexContributors
    {
        /// <summary>The runtime's nested reference contributor, registering it (and rebuilding the index) when missing.</summary>
        public static NestedReferenceContributor Ensure(StudioRuntime runtime)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            foreach (IIndexContributor existing in runtime.Index.Contributors)
            {
                if (existing is NestedReferenceContributor known)
                {
                    return known;
                }
            }

            NestedReferenceContributor contributor = new NestedReferenceContributor(runtime);
            runtime.Index.Contributors.Add(contributor);
            runtime.Index.Rebuild();
            return contributor;
        }
    }
}
