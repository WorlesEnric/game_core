#nullable enable
using System;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>A first-party adapter prepares identity and inverse before invoking a gameplay creator.
    /// Preparation must not mutate the world. Candidate extension code cannot register adapters.</summary>
    public interface IPreparedCreationAdapter
    {
        PreparedCreation? Prepare(EditContext context);
    }

    public sealed class PreparedCreation
    {
        public PreparedCreation(JObject replay, Operation[] inverse, bool assetLevel, Func<EditContext, OperationResult>? apply = null)
        { Replay = replay; Inverse = inverse; AssetLevel = assetLevel; Apply = apply; }
        public JObject Replay { get; }
        public Operation[] Inverse { get; }
        public bool AssetLevel { get; }
        public Func<EditContext, OperationResult>? Apply { get; }

        internal static PreparedCreation? For(EditContext context)
        {
            PreparedCreation? plan = Plan(context);
            if (plan == null) return null;
            bool creates = false;
            foreach (Operation inverse in plan.Inverse)
            {
                creates |= inverse.Tool == DeletePreparedCreationTool.Id || inverse.Tool == "history.deleteAsset";
                if (inverse.Tool != "history.deleteAsset") continue;
                string path = context.Runtime.Paths.Absolute((string)inverse.Args!["path"]!);
                if (File.Exists(path) || Directory.Exists(path))
                    return Refuse(plan.Replay, "The prepared creation path is already occupied; redo cannot overwrite it.");
            }
            if (!creates) return plan;
            var ids = new JArray();
            if (plan.Replay["authoringId"] != null) ids.Add(plan.Replay["authoringId"]!.DeepClone());
            foreach (JToken id in plan.Replay["ids"] as JArray ?? new JArray()) ids.Add(id.DeepClone());
            foreach (JToken id in ids)
            {
                string? value = id.Type == JTokenType.String ? (string?)id : null;
                if (string.IsNullOrEmpty(value) || context.Resolver.FindByAuthoringId(value!) != null)
                    return Refuse(plan.Replay, "A new object needs an unused authoring identity; existing identities cannot authorize a creation inverse.");
            }
            return plan;
        }

        private static PreparedCreation? PrepareGameplay(EditContext context, JObject replay, Type? adapter)
        {
            if (adapter == null || !typeof(IPreparedCreationAdapter).IsAssignableFrom(adapter))
                return new PreparedCreation(replay, Array.Empty<Operation>(), false,
                    _ => OperationResult.Refused(DiagnosticCodes.NotConfigured, "The trusted Studio gameplay creation adapter is unavailable."));
            return ((IPreparedCreationAdapter)Activator.CreateInstance(adapter)!).Prepare(context)
                ?? new PreparedCreation(replay, Array.Empty<Operation>(), false,
                    _ => OperationResult.Refused(DiagnosticCodes.NotConfigured, "The trusted Studio gameplay creation adapter did not prepare this operation."));
        }

        private static PreparedCreation Refuse(JObject replay, string detail) =>
            new PreparedCreation(replay, Array.Empty<Operation>(), false, _ => OperationResult.Refused(DiagnosticCodes.InvalidArgs, detail));

        private static PreparedCreation? Plan(EditContext context)
        {
            string tool = context.Operation.Tool;
            JObject replay = context.Replay != null ? (JObject)context.Replay.DeepClone() : new JObject();
            if (tool == "npc.addAt" || tool == "npc.setPatrol"
                || (tool == "create" && context.StringArg("type") == "dialogue.graph"))
            {
                // Fixed first-party assembly boundary; never discover candidate implementations.
                Type? adapter = Type.GetType("GameCore.Studio.Gameplay.NpcCreationAdapter, GameCore.Studio.Gameplay.Editor");
                return PrepareGameplay(context, replay, adapter);
            }
            if (tool == "create" || tool == "addComponent")
            {
                Type? type = context.Types.ResolveType(context.StringArg("type") ?? "");
                AuthoringTypeInfo? info = type == null ? null : context.Identity.Describe(type);
                if (info == null || info.IdField == null) return null;
                string id = (string?)context.Arg("fields")?["authoringId"] ?? context.ReplayOrArg("authoringId") ?? Guid.NewGuid().ToString("D");
                replay["authoringId"] = id;
                if (tool == "create" && info.IsScriptableObject)
                {
                    string name = context.StringArg("name") ?? info.Type.Name;
                    string destination = context.StringArg("path")!;
                    if (!destination.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) destination = destination.TrimEnd('/') + "/" + name + ".asset";
                    string path = context.ReplayOrArg("assetPath") ?? AssetDatabase.GenerateUniqueAssetPath(destination);
                    replay["assetPath"] = path;
                    return new PreparedCreation(replay, new[] { ToolSupport.InverseOp("history.deleteAsset", null, new JObject { ["path"] = path }) }, true);
                }
                if (!info.IsComponent) return null;
                return new PreparedCreation(replay, new[] { DeletePreparedCreationTool.Inverse(new AuthoringRef(AuthoringKind.Entity, id), tool == "addComponent") }, false);
            }
            if (tool == "duplicate" && context.Target is ScriptableObject asset && EditorUtility.IsPersistent(asset))
            {
                string name = context.StringArg("name") ?? asset.name + " Copy";
                string destination = context.ReplayOrArg("assetPath") ?? AssetDatabase.GenerateUniqueAssetPath(context.StringArg("path")
                    ?? Path.GetDirectoryName(AssetDatabase.GetAssetPath(asset))!.Replace('\\', '/') + "/" + name + ".asset");
                replay["assetPath"] = destination;
                replay["authoringId"] = context.ReplayOrArg("authoringId") ?? Guid.NewGuid().ToString("D");
                return new PreparedCreation(replay, new[] { ToolSupport.InverseOp("history.deleteAsset", null, new JObject { ["path"] = destination }) }, true);
            }
            if (tool == "replace")
            {
                GameObject? original = SceneTools.GameObjectOf(context.Target);
                if (original == null) return null;
                string? blob = ObjectBlob.Capture(context, original, out string? problem);
                if (blob == null) return new PreparedCreation(replay, Array.Empty<Operation>(), false,
                    _ => OperationResult.Failed(DiagnosticCodes.Refused, problem ?? "Cannot retain replacement preimage."));
                return new PreparedCreation(replay, new[] {
                    ToolSupport.InverseOp("delete", context.Operation.Target, null),
                    ToolSupport.InverseOp("history.restoreObject", null, SceneTools.RestoreArgs(context, original, blob)) }, false);
            }
            if (tool == "place" || (tool == "duplicate" && !EditorUtility.IsPersistent(context.Target)))
            {
                GameObject? source = tool == "place"
                    ? context.Codec.Refs.ReadRef(context.Arg("prefab")!, typeof(GameObject), out _) as GameObject
                    : SceneTools.GameObjectOf(context.Target);
                if (source == null) return null;
                var ids = new JArray();
                string? rootId = null;
                JArray? previous = replay["ids"] as JArray;
                foreach (MonoBehaviour component in source.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component == null || context.Identity.Describe(component)?.IdField == null) continue;
                    string id = previous != null && ids.Count < previous.Count ? (string)previous[ids.Count]! : Guid.NewGuid().ToString("D");
                    ids.Add(id);
                    if (component.gameObject == source && rootId == null) rootId = id;
                }
                if (rootId == null) return null;
                replay["ids"] = ids;
                return new PreparedCreation(replay, new[] { DeletePreparedCreationTool.Inverse(new AuthoringRef(AuthoringKind.Entity, rootId)) }, false,
                    tool == "duplicate" ? c => DuplicateScene(c, source) : (Func<EditContext, OperationResult>?)null);
            }
            return null;
        }

        private static OperationResult DuplicateScene(EditContext context, GameObject source)
        {
            // Instantiating the source preserves added-component overrides; instantiating its prefab and
            // copying only PropertyModifications loses those components (including AuthoredEntity).
            GameObject copy = UnityEngine.Object.Instantiate(source, source.transform.parent);
            if (copy.scene != source.scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(copy, source.scene);
            copy.name = context.StringArg("name") ?? source.name;
            copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            if (context.TryArg("offset", out Vector3 offset, out _)) copy.transform.position += offset;
            context.RegisterCreated(copy);
            JArray ids = ToolSupport.MintHierarchyIds(context, copy, context.Replay?["ids"] as JArray);
            AuthoringRef reference = SceneTools.OwnerRef(context, copy)!;
            return OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(reference) })
                .WithInverse(ToolSupport.InverseOp("delete", reference, null))
                .WithReplay(new JObject { ["ids"] = ids }).Touch(copy);
        }
    }
}
