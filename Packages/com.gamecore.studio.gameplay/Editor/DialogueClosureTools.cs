#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Gameplay
{
    /// <summary>Trusted dialogue creation and candidate closure checks, without a runtime gameplay dependency.</summary>
    public static class DialogueClosureTools
    {
        /// <summary>Trusted operations whose only mutation is to their supplied detached definition target.</summary>
        public static bool CanProjectDefinitionOperation(string tool)
        {
            switch (tool)
            {
                case "dialogue.addLine":
                case "dialogue.addChoice":
                case "dialogue.linkCondition":
                case "dialogue.setFactCondition":
                case "dialogue.setConsequence":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Preserves content-set validation diagnostics for a detached final copy without requiring an asset path on it.</summary>
        public static IReadOnlyList<Diagnostic> ValidateProjectedSet(ScriptableObject definition, string originalPath)
        {
            var diagnostics = new List<Diagnostic>();
            var results = (System.Collections.IEnumerable)ClosureMethod("ValidateProjectedSet")
                .Invoke(null, new object[] { definition, originalPath })!;
            foreach (object item in results)
            {
                Type type = item.GetType();
                string code = (string)type.GetProperty("Code")!.GetValue(item)!;
                string message = (string)type.GetProperty("Message")!.GetValue(item)!;
                string subjectId = (string)type.GetProperty("SubjectId")!.GetValue(item)!;
                AuthoringRef subject = Guid.TryParse(subjectId, out _)
                    ? new AuthoringRef(AuthoringKind.Definition, authoringId: subjectId, scope: AuthorScope.Definition)
                    : new AuthoringRef(AuthoringKind.Definition, path: originalPath, scope: AuthorScope.Definition);
                diagnostics.Add(new Diagnostic(code, message, null, DiagnosticWhere.At(subject),
                    subjectId.Length > 0 ? new JObject { ["subject"] = subjectId } : null));
            }
            return diagnostics;
        }

        [AuthorOperation("dialogue.createGraph", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(DialogueClosureValidator), Doc = "Creates a dialogue graph and enrolls it in the active region world's narrative content set, with durable undo.")]
        public static OperationResult CreateGraph(EditContext context,
            [AuthorArg] string name, [AuthorArg] string path, [AuthorArg(Required = false)] object? fields = null)
        {
            PreparedCreation plan = Prepare(context);
            context.PrepareInverse(plan.Inverse, plan.AssetLevel);
            return plan.Apply!(context);
        }

        public static PreparedCreation Prepare(EditContext context)
        {
            JObject replay = context.Replay != null ? (JObject)context.Replay.DeepClone() : new JObject();
            UnityEngine.Object set;
            try { set = ResolveSet(); }
            catch (ArgumentException error) { return Refuse(replay, error.Message); }
            Type? type = context.Types.ResolveType("dialogue.graph");
            AuthoringTypeInfo? info = type == null ? null : context.Identity.Describe(type);
            if (info == null || info.IdField == null) return Refuse(replay, "dialogue.graph is not registered.");
            string? requestedPath = context.StringArg("path");
            if (!ToolSupport.IsSafeAssetPath(requestedPath) || string.IsNullOrWhiteSpace(context.StringArg("name")))
                return Refuse(replay, "A graph name and a safe Assets path are required.");
            string destination = requestedPath!;
            if (!destination.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                destination = destination.TrimEnd('/') + "/" + context.StringArg("name") + ".asset";
            string path = context.ReplayOrArg("assetPath") ?? AssetDatabase.GenerateUniqueAssetPath(destination);
            if (!ToolSupport.IsSafeAssetPath(path)) return Refuse(replay, "The composed graph path must remain under Assets without traversal.");
            string id = (string?)context.Arg("fields")?["authoringId"] ?? context.ReplayOrArg("authoringId") ?? Guid.NewGuid().ToString("D");
            if (!Guid.TryParse(id, out _) || context.Resolver.FindByAuthoringId(id) != null || AssetDatabase.LoadMainAssetAtPath(path) != null
                || File.Exists(context.Runtime.Paths.Absolute(path)))
                return Refuse(replay, "A graph creation requires an unused authoring identity and asset path.");
            string setPath = AssetDatabase.GetAssetPath(set);
            if (replay["contentSetPath"] is JToken previous && (string?)previous != setPath)
                return Refuse(replay, "npc_dialogue_world_ambiguous: redo's owning content set differs from the active world's content set.");
            replay["contentSetPath"] = setPath;
            replay["assetPath"] = path;
            replay["authoringId"] = id;
            JObject members = ToolSupport.CaptureMembers(context, set, context.Identity.Describe(set)!);
            var inverse = new[] {
                ToolSupport.SetFieldsInverse(ToolSupport.RefOf(context, set)!, new JObject { ["definitions"] = members["definitions"]!.DeepClone() }),
                ToolSupport.InverseOp("history.deleteAsset", null, new JObject { ["path"] = path })
            };
            return new PreparedCreation(replay, inverse, true, c =>
            {
                ScriptableObject graph = ScriptableObject.CreateInstance(info.Type);
                graph.name = Path.GetFileNameWithoutExtension(path);
                ToolSupport.MintAuthoringId(graph, info, id, false);
                string? problem = ToolSupport.ApplyFields(c, graph, info, c.Arg("fields") as JObject, false);
                if (problem != null)
                {
                    UnityEngine.Object.DestroyImmediate(graph);
                    return OperationResult.Refused(DiagnosticCodes.InvalidArgs, problem);
                }
                c.PrepareInverse(inverse, true);
                c.OutsideAssetEditing(() =>
                {
                    ToolSupport.EnsureFolder(Path.GetDirectoryName(path)!.Replace('\\', '/'));
                    AssetDatabase.CreateAsset(graph, path);
                    AssetDatabase.SaveAssetIfDirty(graph);
                    ClosureMethod("Enroll").Invoke(null, new object[] { set, graph });
                });
                return OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(ToolSupport.RefOf(c, graph)), ["path"] = path })
                    .WithAssetLevelInverse(inverse).WithReplay(replay).Touch(graph).Touch(set);
            });
        }

        /// <summary>
        /// Validates final NPC bindings after all definition writes and graph enrollments have been projected.
        /// The projection maps originals to their final copies, leaves unchanged/copy objects alone, and returns null for deletions.
        /// </summary>
        public static IReadOnlyList<Diagnostic> ValidateProposed(StudioRuntime runtime,
            IReadOnlyList<ScriptableObject> definitions, Func<UnityEngine.Object, UnityEngine.Object?> projectReference)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (definitions == null) throw new ArgumentNullException(nameof(definitions));
            if (projectReference == null) throw new ArgumentNullException(nameof(projectReference));
            var diagnostics = new List<Diagnostic>();
            UnityEngine.Object? set = null;
            string setPath = string.Empty;
            string? setProblem = null;
            bool resolvedSet = false;
            MethodInfo? contains = null;
            foreach (ScriptableObject definition in definitions)
            {
                AuthoringTypeInfo? info = runtime.Identity.Describe(definition);
                if (info?.TypeId != "npc.definition") continue;
                UnityEngine.Object? graph = definition.GetType().GetProperty("Dialogue")?.GetValue(definition) as UnityEngine.Object;
                string legacy = graph == null
                    ? definition.GetType().GetProperty("LegacyDialogueGraph")?.GetValue(definition) as string ?? string.Empty
                    : string.Empty;
                if (graph == null && legacy.Length == 0) continue;
                string graphName = graph == null ? legacy : graph.name;
                if (graph == null)
                    graph = runtime.Resolver.Codec.Refs.ReadRef(new JValue(legacy), typeof(ScriptableObject), out _);
                if (!resolvedSet)
                {
                    resolvedSet = true;
                    try
                    {
                        UnityEngine.Object original = ResolveSet();
                        setPath = AssetDatabase.GetAssetPath(original);
                        set = projectReference(original);
                        if (set == null)
                            setProblem = "npc_dialogue_content_set_missing: the owning world's GameplayContentSet is absent from the final proposal.";
                    }
                    catch (ArgumentException error) { setProblem = error.Message; }
                }
                string? problem = setProblem;
                if (problem == null)
                {
                    contains ??= ClosureMethod("ContainsProposed");
                    bool enrolled = graph != null && (bool)contains.Invoke(null, new object[] { set!, graph, projectReference })!;
                    if (!enrolled)
                        problem = "npc_dialogue_not_enrolled: NPC dialogue " + graphName
                            + " is not in the owning world's final GameplayContentSet closure (" + setPath + ").";
                }
                if (problem == null) continue;
                AuthoringRef? subject = runtime.Resolver.BuildRef(definition, includeStamp: false);
                string? id = info.ReadId(definition);
                if (subject == null && !string.IsNullOrEmpty(id))
                    subject = new AuthoringRef(AuthoringKind.Definition, authoringId: id, scope: AuthorScope.Definition);
                diagnostics.Add(new Diagnostic(DiagnosticCodes.InvalidArgs, problem,
                    "Enroll the graph in that content set, or create it with dialogue.createGraph before binding the NPC.",
                    subject == null ? null : DiagnosticWhere.At(subject)));
            }
            return diagnostics;
        }

        /// <summary>Run before any candidate writes, including operations whose NPC or graph does not exist yet.</summary>
        public static IReadOnlyList<Diagnostic> ValidateCandidate(StudioRuntime runtime, ChangeSet changeSet)
        {
            var diagnostics = new List<Diagnostic>();
            foreach (Operation operation in changeSet.Operations)
            {
                UnityEngine.Object? target = operation.Target == null ? null : runtime.Resolver.Find(operation.Target);
                bool npc = (operation.Tool == "create" && (string?)operation.Args?["type"] == "npc.definition")
                    || (target != null && runtime.Identity.Describe(target)?.TypeId == "npc.definition");
                if (!npc) continue;
                JObject? fields = operation.Args?["fields"] as JObject;
                JToken? graphRef = fields?["dialogue"];
                bool supplied = fields?.Property("dialogue") != null;
                if ((operation.Tool == "assign" || operation.Tool == "set") && (string?)operation.Args?["field"] == "dialogue")
                { graphRef = operation.Args?["value"]; supplied = true; }
                if (!supplied && target != null)
                {
                    UnityEngine.Object? graph = target.GetType().GetProperty("Dialogue")?.GetValue(target) as UnityEngine.Object;
                    if (graph != null) graphRef = runtime.Resolver.Codec.Refs.WriteRef(graph);
                    else graphRef = JToken.FromObject(target.GetType().GetProperty("LegacyDialogueGraph")?.GetValue(target) as string ?? "");
                }
                if (graphRef == null || graphRef.Type == JTokenType.Null || (graphRef.Type == JTokenType.String && string.IsNullOrEmpty((string?)graphRef))) continue;
                UnityEngine.Object set;
                try { set = ResolveSet(); }
                catch (ArgumentException error)
                { diagnostics.Add(Diagnostic.AtOperation(DiagnosticCodes.InvalidArgs, operation.OpId, error.Message)); continue; }
                UnityEngine.Object? resolved = runtime.Resolver.Codec.Refs.ReadRef(graphRef, typeof(ScriptableObject), out _);
                bool enrolled = resolved != null && (bool)ClosureMethod("Contains").Invoke(null, new object[] { set, resolved })!;
                if (!enrolled)
                {
                    foreach (Operation producer in changeSet.Operations)
                    {
                        // Only the public enrolled creator is reachable today. Core's generic create needs the named adapter dispatch.
                        if (producer.Tool != "dialogue.createGraph" || !DependsOn(changeSet, operation, producer.OpId, new HashSet<string>(StringComparer.Ordinal))) continue;
                        string? path = (string?)producer.Args?["path"];
                        if (path != null && !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) path = path.TrimEnd('/') + "/" + (string?)producer.Args?["name"] + ".asset";
                        string? id = (string?)producer.Args?["fields"]?["authoringId"];
                        enrolled = (graphRef.Type == JTokenType.String && (string?)graphRef == path)
                            || (graphRef is JObject reference && ((id != null && (string?)reference["authoringId"] == id) || (path != null && (string?)reference["path"] == path)));
                        if (enrolled) break;
                    }
                }
                if (!enrolled) diagnostics.Add(Diagnostic.AtOperation(DiagnosticCodes.InvalidArgs, operation.OpId,
                    "npc_dialogue_not_enrolled: NPC dialogue " + graphRef.ToString(Newtonsoft.Json.Formatting.None)
                    + " is not in the owning world's GameplayContentSet closure (" + AssetDatabase.GetAssetPath(set) + ").",
                    "Enroll the graph in that content set, or create it with dialogue.createGraph before binding the NPC."));
            }
            return diagnostics;
        }

        private static bool DependsOn(ChangeSet changeSet, Operation consumer, string producer, HashSet<string> seen)
        {
            foreach (string dependency in consumer.DependsOn ?? Array.Empty<string>())
            {
                if (dependency == producer) return true;
                if (!seen.Add(dependency)) continue;
                foreach (Operation operation in changeSet.Operations)
                    if (operation.OpId == dependency && DependsOn(changeSet, operation, producer, seen)) return true;
            }
            return false;
        }

        public static UnityEngine.Object ResolveSet()
        {
            try { return (UnityEngine.Object)ClosureMethod("ResolveForActiveScene").Invoke(null, null)!; }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException argument) { throw argument; }
        }

        private static MethodInfo ClosureMethod(string name) => Type.GetType(
            "GameCore.Gameplay.Dialogue.Editor.DialogueContentClosure, GameCore.Gameplay.Dialogue.Editor", true)!.GetMethod(name)!;

        private static PreparedCreation Refuse(JObject replay, string detail) => new PreparedCreation(replay, Array.Empty<Operation>(), false,
            _ => OperationResult.Refused(DiagnosticCodes.InvalidArgs, detail));
    }

    public sealed class DialogueClosureValidator : IOperationValidator
    {
        public IEnumerable<Diagnostic> Validate(EditContext context)
        {
            var diagnostics = new List<Diagnostic>();
            if (!ToolSupport.IsSafeAssetPath(context.StringArg("path")) || string.IsNullOrWhiteSpace(context.StringArg("name")))
                diagnostics.Add(context.Problem(DiagnosticCodes.InvalidArgs, "A graph name and a safe Assets path are required."));
            try { DialogueClosureTools.ResolveSet(); }
            catch (ArgumentException error) { diagnostics.Add(context.Problem(DiagnosticCodes.InvalidArgs, error.Message)); }
            Type? type = context.Types.ResolveType("dialogue.graph");
            AuthoringTypeInfo? info = type == null ? null : context.Identity.Describe(type);
            if (info != null)
            {
                var staged = new ToolStageResult();
                ToolSupport.CheckFields(context, info, context.Arg("fields") as JObject, staged);
                diagnostics.AddRange(staged.Diagnostics);
            }
            return diagnostics;
        }
    }
}
