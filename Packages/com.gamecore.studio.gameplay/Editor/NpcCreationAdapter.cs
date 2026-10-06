#nullable enable
using System;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Gameplay
{
    /// <summary>First-party prepare/inverse bridge; gameplay itself remains independent of Studio.</summary>
    public sealed class NpcCreationAdapter : IPreparedCreationAdapter
    {
        public PreparedCreation? Prepare(EditContext context)
        {
            if ((context.Operation.Tool == "create" && context.StringArg("type") == "dialogue.graph")
                || context.Operation.Tool == "dialogue.createGraph") return DialogueClosureTools.Prepare(context);
            if (context.Target == null || context.Identity.Describe(context.Target)?.TypeId != "npc.definition") return null;
            UnityEngine.Object npc = context.Target;
            Type tools = Type.GetType("GameCore.Gameplay.Npc.Editor.NpcTools, GameCore.Gameplay.Npc.Editor", true)!;
            JObject replay = context.Replay != null ? (JObject)context.Replay.DeepClone() : new JObject();
            if (context.Operation.Tool == "npc.addAt")
            {
                string id = (string?)replay["authoringId"] ?? Guid.NewGuid().ToString("D");
                replay["authoringId"] = id;
                var inverse = new[] { ToolSupport.InverseOp("history.deleteCreated", null,
                    new JObject { ["created"] = StudioJson.ToToken(new AuthoringRef(AuthoringKind.Entity, id)) }) };
                return new PreparedCreation(replay, inverse, false, c =>
                {
                    c.TryArg("location", out Vector3 location, out _);
                    c.TryArg("yaw", out float yaw, out _);
                    UnityEngine.Object entity = (UnityEngine.Object)tools.GetMethod("AddAtIn")!.Invoke(null,
                        new object[] { SceneManager.GetActiveScene(), npc, location, yaw, c.StringArg("name") ?? "", id })!;
                    return OperationResult.Applied(new JObject { ["ref"] = ToolSupport.RefJson(ToolSupport.RefOf(c, entity)) })
                        .WithInverse(inverse).WithReplay(replay).Touch(entity);
                });
            }
            if (context.Operation.Tool != "npc.setPatrol") return null;
            Operation restoreNpc = ToolSupport.SetFieldsInverse(context.Operation.Target!,
                ToolSupport.CaptureMembers(context, npc, context.Identity.Describe(npc)!));
            UnityEngine.Object? existing = npc.GetType().GetProperty("Behaviour")!.GetValue(npc) as UnityEngine.Object;
            bool creates = existing == null;
            string path = creates ? (string?)replay["assetPath"] ?? AssetDatabase.GenerateUniqueAssetPath(
                (Path.GetDirectoryName(AssetDatabase.GetAssetPath(npc)) ?? "Assets").Replace('\\', '/') + "/" + npc.name + "Behaviour.asset")
                : AssetDatabase.GetAssetPath(existing);
            string behaviourId = creates ? (string?)replay["authoringId"] ?? Guid.NewGuid().ToString("D") : (string)existing!.GetType().GetProperty("AuthoringId")!.GetValue(existing)!;
            Operation[] inverses;
            if (creates)
            {
                replay["assetPath"] = path;
                replay["authoringId"] = behaviourId;
                inverses = new[] { restoreNpc, ToolSupport.InverseOp("history.deleteAsset", null, new JObject { ["path"] = path }) };
            }
            else inverses = new[] { ToolSupport.SetFieldsInverse(ToolSupport.RefOf(context, existing)!,
                ToolSupport.CaptureMembers(context, existing!, context.Identity.Describe(existing!)!)) };
            return new PreparedCreation(replay, inverses, creates, c =>
            {
                c.TryArg("points", out Vector3[] points, out _);
                float wait = c.TryArg("waitSeconds", out float value, out _) ? value : 1.5f;
                UnityEngine.Object? behaviour = null;
                c.OutsideAssetEditing(() => behaviour = (UnityEngine.Object)tools.GetMethod("SetPatrolPrepared")!.Invoke(null, new object[] { npc, points, wait, path, behaviourId })!);
                OperationResult result = OperationResult.Applied().WithReplay(replay).Touch(npc).Touch(behaviour);
                return creates ? result.WithAssetLevelInverse(inverses) : result.WithInverse(inverses);
            });
        }
    }
}
