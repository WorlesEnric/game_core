#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    // One detached object graph for the entire proposal. Never registers assets or resolves a
    // proposed reference through the live AssetDatabase after its producer has been projected.
    internal sealed class DefinitionProjection : IObjectRefCodec, IDisposable
    {
        private readonly StudioRuntime _runtime;
        private readonly Dictionary<UnityEngine.Object, ScriptableObject> _copies = new Dictionary<UnityEngine.Object, ScriptableObject>();
        private readonly Dictionary<string, ScriptableObject> _created = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
        private readonly Dictionary<ScriptableObject, JToken> _references = new Dictionary<ScriptableObject, JToken>();
        public readonly List<ScriptableObject> Definitions = new List<ScriptableObject>();
        public ValueCodec Codec { get; }

        public DefinitionProjection(StudioRuntime runtime)
        {
            _runtime = runtime;
            Codec = new ValueCodec(this);
        }

        public UnityEngine.Object? ProjectReference(UnityEngine.Object original)
            => _copies.TryGetValue(original, out ScriptableObject? copy) ? copy : original;

        public ScriptableObject Copy(ScriptableObject original)
        {
            if (_copies.TryGetValue(original, out ScriptableObject? copy)) return copy;
            copy = UnityEngine.Object.Instantiate(original);
            copy.name = original.name;
            copy.hideFlags = HideFlags.HideAndDontSave;
            _copies.Add(original, copy);
            _references.Add(copy, _runtime.Resolver.Codec.Refs.WriteRef(original));
            Definitions.Add(copy);
            return copy;
        }

        public ScriptableObject? Target(StagedOperation staged)
        {
            if (staged.Target is ScriptableObject original) return Copy(original);
            if (staged.Operation.Target == null) return null;
            return ReadRef(JToken.FromObject(staged.Operation.Target), typeof(ScriptableObject), out _) as ScriptableObject;
        }

        public ScriptableObject? Create(StagedOperation staged)
        {
            Operation operation = staged.Operation;
            string? typeId = operation.Tool == "dialogue.createGraph" ? "dialogue.graph"
                : operation.Tool == "create" ? (string?)operation.Args?["type"] : null;
            Type? type = typeId == null ? null : _runtime.Types.ResolveType(typeId);
            if (type == null || !typeof(ScriptableObject).IsAssignableFrom(type)) return null;
            string name = (string?)operation.Args?["name"] ?? type.Name;
            string path = (string?)operation.Args?["path"] ?? throw new InvalidOperationException("A definition path is required.");
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) path = path.TrimEnd('/') + "/" + name + ".asset";
            if (_created.ContainsKey(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new InvalidOperationException("A definition already exists at " + path + ".");
            ScriptableObject copy = ScriptableObject.CreateInstance(type);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            copy.hideFlags = HideFlags.HideAndDontSave;
            Definitions.Add(copy);
            _created.Add(path, copy);
            string? id = (string?)operation.Args?["fields"]?["authoringId"] ?? (string?)operation.Args?["authoringId"];
            if (!string.IsNullOrEmpty(id)) _created.Add(id!, copy);
            ToolSupport.MintAuthoringId(copy, _runtime.Identity.Describe(copy)!, id ?? AuthoringIdentity.NewAuthoringId(), false);
            _references.Add(copy, new JObject { ["kind"] = "Definition", ["path"] = path });
            if (operation.Args?["fields"] is JObject fields) WriteFields(copy, fields);
            if (typeId == "dialogue.graph")
            {
                Type adapter = Type.GetType("GameCore.Studio.Gameplay.DialogueClosureTools, GameCore.Studio.Gameplay.Editor", true)!;
                var set = (ScriptableObject)adapter.GetMethod("ResolveSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!;
                ScriptableObject proposedSet = Copy(set);
                using SerializedObject serialized = new SerializedObject(proposedSet);
                SerializedProperty definitions = serialized.FindProperty("definitions");
                definitions.GetArrayElementAtIndex(definitions.arraySize++).objectReferenceValue = copy;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            return copy;
        }

        public void WriteFields(ScriptableObject copy, JObject fields)
        {
            AuthoringTypeInfo info = _runtime.Identity.Describe(copy)!;
            using SerializedObject serialized = new SerializedObject(copy);
            foreach (JProperty field in fields.Properties())
            {
                AuthorMemberInfo member = info.FindMember(field.Name) ?? throw new InvalidOperationException("Unknown field " + field.Name + ".");
                if (!Codec.WriteMember(copy, member, field.Value, serialized, out string? problem))
                    throw new InvalidOperationException(field.Name + ": " + problem);
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static bool CanInvoke(string tool)
        {
            Type? adapter = Type.GetType("GameCore.Studio.Gameplay.DialogueClosureTools, GameCore.Studio.Gameplay.Editor");
            return adapter?.GetMethod("CanProjectDefinitionOperation")?.Invoke(null, new object[] { tool }) is true;
        }

        public void Invoke(ReflectedTool tool, Operation operation, ScriptableObject target)
        {
            MethodInfo method = tool.Method;
            ParameterInfo[] parameters = method.GetParameters();
            object?[] values = new object?[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                AuthorArgAttribute? arg = AuthoringMetadata.Arg(parameter);
                if (arg == null)
                {
                    if (parameter.ParameterType.IsInstanceOfType(target)) values[i] = target;
                    else throw new InvalidOperationException("Definition projection cannot supply " + parameter.ParameterType.Name + ".");
                }
                else
                {
                    JToken? value = operation.Args?[arg.Name ?? parameter.Name!];
                    if (value == null) values[i] = parameter.HasDefaultValue ? parameter.DefaultValue
                        : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
                    else if (Codec.TryToClr(value, parameter.ParameterType, out object? converted, out string? problem)) values[i] = converted;
                    else throw new InvalidOperationException(parameter.Name + ": " + problem);
                }
            }
            method.Invoke(method.IsStatic ? null : target, values);
        }

        public void RemapReferences()
        {
            // Discover paths through unchanged intermediaries, then clone only ancestors
            // of edited objects. Unrelated authored subgraphs remain shared and read-only.
            var parents = new Dictionary<ScriptableObject, List<ScriptableObject>>();
            var visited = new HashSet<ScriptableObject>();
            var pending = new Stack<ScriptableObject>(Definitions);
            while (pending.Count > 0)
            {
                ScriptableObject parent = pending.Pop();
                if (!visited.Add(parent)) continue;
                using SerializedObject serialized = new SerializedObject(parent);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || !(property.objectReferenceValue is ScriptableObject child)
                        || _runtime.Identity.Describe(child) == null) continue;
                    if (!parents.TryGetValue(child, out List<ScriptableObject>? ancestors))
                        parents.Add(child, ancestors = new List<ScriptableObject>());
                    ancestors.Add(parent);
                    if (!_references.ContainsKey(child) && _copies.TryGetValue(child, out ScriptableObject? final))
                        pending.Push(final);
                    else pending.Push(child);
                }
            }
            pending.Clear();
            foreach (UnityEngine.Object original in _copies.Keys) pending.Push((ScriptableObject)original);
            visited.Clear();
            while (pending.Count > 0)
            {
                ScriptableObject child = pending.Pop();
                if (!visited.Add(child) || !parents.TryGetValue(child, out List<ScriptableObject>? ancestors)) continue;
                foreach (ScriptableObject ancestor in ancestors)
                {
                    if (_references.ContainsKey(ancestor)) continue;
                    Copy(ancestor);
                    pending.Push(ancestor);
                }
            }
            foreach (ScriptableObject copy in Definitions)
            {
                using SerializedObject serialized = new SerializedObject(copy);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null)
                        property.objectReferenceValue = ProjectReference(property.objectReferenceValue);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public JToken WriteRef(UnityEngine.Object? target)
            => target is ScriptableObject copy && _references.TryGetValue(copy, out JToken? reference)
                ? reference.DeepClone() : _runtime.Resolver.Codec.Refs.WriteRef(target);

        public UnityEngine.Object? ReadRef(JToken value, Type expected, out string? problem)
        {
            string? key = value.Type == JTokenType.String ? (string?)value : (string?)value["path"] ?? (string?)value["authoringId"];
            if (key != null && _created.TryGetValue(key, out ScriptableObject? created))
            {
                problem = expected.IsInstanceOfType(created) ? null : "Proposed reference has the wrong type.";
                return problem == null ? created : null;
            }
            UnityEngine.Object? original = _runtime.Resolver.Codec.Refs.ReadRef(value, expected, out problem);
            return original == null ? null : ProjectReference(original);
        }

        public void Dispose()
        {
            foreach (ScriptableObject copy in Definitions) UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
