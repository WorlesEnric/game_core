#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Trusted adapter to the game's typed placement command and committed pose reader. Never writes Unity transforms.</summary>
    public interface IRuntimeMoveGateway
    {
        OperationResult Move(AuthoringRef target, Vector3 position, float yaw);
        bool IsAt(AuthoringRef target, Vector3 position, float yaw, string operationId);
    }

    /// <summary>Runtime actions remain non-undoable. Promotion creates a distinct, stamp-checked authored move after Play.</summary>
    public sealed class RuntimeMovePromotion
    {
        public const string ToolId = "runtime.move";
        private readonly StudioRuntime _runtime;
        internal RuntimeMovePromotion(StudioRuntime runtime) => _runtime = runtime;
        public IRuntimeMoveGateway? Gateway { get; set; }
        private string BaselinePath => Path.Combine(_runtime.Paths.LibraryRoot, "runtime-move-baseline.json");
        private string PendingPath => Path.Combine(_runtime.Paths.LibraryRoot, "runtime-move-promotions.json");

        /// <summary>Captured before scene/domain reload, never from a runtime transform. Unsaved scenes cannot be promoted.</summary>
        public void CaptureAuthoredState()
        {
            if (_runtime.Engine.IsPlayMode) throw new InvalidOperationException("Capture authored state before entering Play.");
            var refs = new JObject();
            foreach (AuthoredObjectEntry entry in _runtime.Source.Enumerate(AuthoringSourceScope.OpenScenes))
            {
                GameObject? go = SceneTools.GameObjectOf(entry.Target);
                if (go == null || string.IsNullOrEmpty(go.scene.path) || go.scene.isDirty) continue;
                AuthoringRef? reference = _runtime.Resolver.BuildRef(entry.Target, AuthorScope.Instance, true);
                if (reference?.AuthoringId == null || reference.Stamp == null) continue;
                // Duplicate ids are not a promotable mapping.
                if (refs.Property(reference.AuthoringId) != null) refs[reference.AuthoringId] = JValue.CreateNull();
                else refs[reference.AuthoringId] = StudioJson.ToToken(reference);
            }
            StudioPaths.WriteAllTextAtomic(BaselinePath, refs.ToString());
        }

        public bool TryApplyToAuthored(string runtimeChangeSetId, out ChangeSet? authored, out Diagnostic? problem)
        {
            authored = null;
            problem = null;
            ChangeSet? source = _runtime.Journal.Read(runtimeChangeSetId);
            if (source == null || source.EffectiveState != ChangeSetState.Applied || source.Operations.Count != 1)
                return Refuse("Only an applied single runtime move can be promoted.", out problem);
            Operation op = source.Operations[0];
            OperationOutcome? outcome = source.Outcomes?.Count == 1 ? source.Outcomes[0] : null;
            if (op.Tool != ToolId || op.Target?.AuthoringId == null || outcome?.Status != OutcomeStatus.Applied
                || outcome.Undo != null || outcome.GameCoreOps == null || outcome.GameCoreOps.Count != 1)
                return Refuse("This action has no promotable runtime move receipt.", out problem);
            JObject pending = Read(PendingPath);
            if (pending[runtimeChangeSetId] is JObject existing)
            {
                authored = StudioJson.Deserialize<ChangeSet>(existing["changeSet"]!.ToString());
                return true;
            }
            if (!_runtime.Engine.IsPlayMode || Gateway == null)
                return Refuse("Apply to authored requires the same running Play world and its committed pose.", out problem);
            Vector3 position = Position(op);
            float yaw = (float)op.Args!["yaw"]!;
            if (!Gateway.IsAt(op.Target, position, yaw, outcome.GameCoreOps[0]))
                return Refuse("The accepted move is not the current committed pose; no authored change was requested.", out problem);
            JObject baseline = Read(BaselinePath);
            if (!(baseline[op.Target.AuthoringId] is JObject saved))
                return Refuse("No unique saved pre-Play authored target exists. Save its scene before entering Play.", out problem);
            AuthoringRef target = StudioJson.Deserialize<AuthoringRef>(saved.ToString());
            var rotation = Quaternion.Euler(0, yaw, 0);
            authored = StudioRuntime.Single("Apply runtime move to authored: " + source.Id, IntentOrigin.Manual,
                new Operation("move", BuiltInToolIdsExt.Move, target, new JObject
                {
                    ["position"] = SceneTools.Vector(position),
                    ["rotation"] = new JArray(rotation.x, rotation.y, rotation.z, rotation.w),
                })).WithState(ChangeSetState.Candidate);
            // Persist the creator decision before publishing it to History. A repeated click returns this same id.
            pending[runtimeChangeSetId] = new JObject { ["changeSet"] = StudioJson.ToToken(authored), ["completed"] = false };
            StudioPaths.WriteAllTextAtomic(PendingPath, pending.ToString());
            _runtime.Journal.Write(authored);
            return true;
        }

        /// <summary>Called after exit/reload. Conflict and StaleTarget remain ordinary journaled engine refusals.</summary>
        public IReadOnlyList<ApplyReport> ApplyPending()
        {
            var reports = new List<ApplyReport>();
            if (_runtime.Engine.IsPlayMode || EditorApplication.isPlayingOrWillChangePlaymode) return reports;
            JObject requests = Read(PendingPath);
            foreach (JProperty pending in requests.Properties())
            {
                JObject request = (JObject)pending.Value;
                if ((bool?)request["completed"] == true) continue;
                ChangeSet change = StudioJson.Deserialize<ChangeSet>(request["changeSet"]!.ToString());
                ChangeSet? previous = _runtime.Journal.Read(change.Id);
                if (previous != null && previous.EffectiveState != ChangeSetState.Candidate && previous.EffectiveState != ChangeSetState.Applied)
                {
                    request["completed"] = true;
                    StudioPaths.WriteAllTextAtomic(PendingPath, requests.ToString());
                    continue;
                }
                AuthoringRef target = change.Operations[0].Target!;
                string path = target.AssetGuid == null ? string.Empty : AssetDatabase.GUIDToAssetPath(target.AssetGuid);
                Scene scene = SceneManager.GetSceneByPath(path);
                bool opened = false;
                if (!scene.IsValid() && path.EndsWith(".unity", StringComparison.Ordinal) && File.Exists(_runtime.Paths.Absolute(path)))
                {
                    scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    opened = true;
                }
                try
                {
                    ApplyReport? report = previous?.EffectiveState == ChangeSetState.Applied ? null : _runtime.Engine.Apply(change);
                    if (report != null) reports.Add(report);
                    if ((report?.State ?? previous!.EffectiveState) == ChangeSetState.Applied)
                    {
                        GameObject? authoredObject = SceneTools.GameObjectOf(_runtime.Resolver.Find(target));
                        if (authoredObject == null || authoredObject.transform.position != Position(change.Operations[0]))
                            throw new IOException("An applied promotion has no matching authored postimage; use History recovery before saving: " + change.Id);
                        if (!scene.IsValid() || !EditorSceneManager.SaveScene(scene))
                            throw new IOException("Authored move applied but scene save failed: " + path);
                    }
                    request["completed"] = true;
                    StudioPaths.WriteAllTextAtomic(PendingPath, requests.ToString());
                }
                finally
                {
                    if (opened && !scene.isDirty) EditorSceneManager.CloseScene(scene, true);
                }
            }
            return reports;
        }

        internal static Vector3 Position(Operation operation)
        {
            JArray values = (JArray)operation.Args!["position"]!;
            return new Vector3((float)values[0], (float)values[1], (float)values[2]);
        }
        private static JObject Read(string path) => File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
        private static bool Refuse(string message, out Diagnostic? problem)
        {
            problem = StudioDiagnostics.General(DiagnosticCodes.Refused, message);
            return false;
        }
    }

    internal sealed class RuntimeMoveTool : IStudioTool
    {
        public ToolEntry Entry { get; } = new ToolEntry(RuntimeMovePromotion.ToolId, ToolTier.Compose, RuntimeApply.Live, true,
            new[] { new ArgSpec("position", ValueTypes.Vector3, true), new ArgSpec("yaw", ValueTypes.Float, true) },
            scopes: new[] { AuthorScope.Instance }, runtimeOnly: true);
        public bool Internal => false;
        public bool ReadOnly => false;
        public ToolStageResult Stage(EditContext context)
        {
            var result = new ToolStageResult();
            if (context.Operation.Target?.AuthoringId == null || SceneTools.GameObjectOf(context.Target) == null)
                result.Add(context.Problem(DiagnosticCodes.Refused, "A runtime move needs an authored scene entity."));
            if (context.Runtime.Engine.RuntimeMoves.Gateway == null)
                result.Add(context.Problem(DiagnosticCodes.NotConfigured, "No typed runtime placement adapter is available."));
            return result;
        }
        public OperationResult Apply(EditContext context) => context.Runtime.Engine.RuntimeMoves.Gateway!.Move(
            context.Operation.Target!, RuntimeMovePromotion.Position(context.Operation), (float)context.Operation.Args!["yaw"]!);
    }

    [InitializeOnLoad]
    internal static class RuntimeMoveLifecycle
    {
        static RuntimeMoveLifecycle()
        {
            EditorApplication.playModeStateChanged += Changed;
            EditorApplication.delayCall += Resume;
        }
        private static void Resume()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && File.Exists(Path.Combine(StudioPaths.ForCurrentProject().LibraryRoot, "runtime-move-promotions.json")))
                StudioServices.Runtime.Engine.RuntimeMoves.ApplyPending();
        }
        private static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode) StudioServices.Runtime.Engine.RuntimeMoves.CaptureAuthoredState();
            else if (state == PlayModeStateChange.EnteredEditMode) StudioServices.Runtime.Engine.RuntimeMoves.ApplyPending();
        }
    }
}
