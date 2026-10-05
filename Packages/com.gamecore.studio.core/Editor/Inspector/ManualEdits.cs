// GameCore.Studio.Edit - manual edits as change sets (docs/studio/03-authoring-contracts.md s4: inspector commits and
// gizmo drags go through the same tools agents use, so they are validated, journaled and undoable the same way).
//   ManualEditCommitter  inspector field commits -> one `set` (values) or `assign` (references) change set
//   MoveChangeSets       one `move` change set for a target pose; shared by the gizmo and the typed path (W-EDIT-05)
//   GizmoMoveController  a drag moves the object live without Undo; mouse-up discards the preview and applies one
//                        `move` change set, so a drag is one journal entry identical to typing the final position
#nullable enable
using System;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>Receives inspector commits.</summary>
    public interface IManualEditSink
    {
        /// <summary>Checks a value against the member's catalog spec; empty when valid (shown inline, nothing applied).</summary>
        System.Collections.Generic.IReadOnlyList<string> Check(AuthorMemberInfo member, JToken? value);

        /// <summary>Commits one member value as a change set; null when nothing was applied (invalid value, unresolvable target).</summary>
        ApplyReport? Commit(UnityEngine.Object target, AuthorMemberInfo member, JToken? value);
    }

    /// <summary>Turns inspector commits into <c>set</c>/<c>assign</c> change sets applied by the engine.</summary>
    public sealed class ManualEditCommitter : IManualEditSink
    {
        private readonly StudioRuntime _runtime;

        public ManualEditCommitter(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>The last report (inspectors show its diagnostics).</summary>
        public ApplyReport? LastReport { get; private set; }

        public System.Collections.Generic.IReadOnlyList<string> Check(AuthorMemberInfo member, JToken? value)
        {
            if (member.IsReference)
            {
                return Array.Empty<string>();
            }

            return FieldValueChecker.Check(member.Spec, value);
        }

        /// <summary>The change set a commit would apply (null when the target has no ref).</summary>
        public ChangeSet? Build(UnityEngine.Object target, AuthorMemberInfo member, JToken? value)
        {
            AuthoringRef? reference = _runtime.Resolver.BuildRef(target, null, true);
            if (reference == null)
            {
                return null;
            }

            string tool = member.IsReference ? BuiltInToolIdsExt.Assign : BuiltInToolIdsExt.Set;
            JObject args = new JObject { ["field"] = member.Name, ["value"] = value?.DeepClone() ?? JValue.CreateNull() };
            Operation operation = new Operation("op1", tool, reference, args);
            return StudioRuntime.Single("Edit " + target.name + "." + member.Name, IntentOrigin.Manual, operation);
        }

        public ApplyReport? Commit(UnityEngine.Object target, AuthorMemberInfo member, JToken? value)
        {
            if (Check(member, value).Count > 0)
            {
                return null;
            }

            ChangeSet? changeSet = Build(target, member, value);
            if (changeSet == null)
            {
                return null;
            }

            LastReport = _runtime.Engine.Apply(changeSet);
            return LastReport;
        }
    }

    /// <summary>Builds the one-operation <c>move</c> change set used by both the gizmo and typed values.</summary>
    public static class MoveChangeSets
    {
        /// <summary>
        /// A move of <paramref name="target"/>'s logical owner to the given pose (any subset). The target ref carries the
        /// stamp of the object's current state, so a concurrent edit is a Conflict.
        /// </summary>
        public static ChangeSet? Build(StudioRuntime runtime, GameObject target, Vector3? position, Quaternion? rotation = null, Vector3? scale = null)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            MonoBehaviour? owner = runtime.Identity.FindAuthoredComponent(target);
            AuthoringRef? reference = runtime.Resolver.BuildRef(owner != null ? (UnityEngine.Object)owner : target, null, true);
            if (reference == null)
            {
                return null;
            }

            JObject args = new JObject();
            if (position.HasValue)
            {
                args["position"] = Vector(position.Value);
            }

            if (rotation.HasValue)
            {
                Quaternion value = rotation.Value;
                args["rotation"] = new JArray(ValueCodec.Widen(value.x), ValueCodec.Widen(value.y), ValueCodec.Widen(value.z), ValueCodec.Widen(value.w));
            }

            if (scale.HasValue)
            {
                args["scale"] = Vector(scale.Value);
            }

            if (!args.HasValues)
            {
                return null;
            }

            Operation operation = new Operation("op1", BuiltInToolIdsExt.Move, reference, args);
            return StudioRuntime.Single("Move " + target.name, IntentOrigin.Manual, operation);
        }

        private static JArray Vector(Vector3 value) => new JArray(ValueCodec.Widen(value.x), ValueCodec.Widen(value.y), ValueCodec.Widen(value.z));
    }

    /// <summary>Drag state of the Studio move gizmo (UI-free, so tests drive it directly).</summary>
    public sealed class GizmoMoveController
    {
        private readonly StudioRuntime _runtime;
        private GameObject? _target;
        private Vector3 _startPosition;
        private Vector3 _current;
        private GameObject? _ghost;
        private AuthoringRef? _startRef;

        public Transform? PreviewTransform => _ghost == null ? null : _ghost.transform;

        public GizmoMoveController(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public bool Dragging => _target != null;

        public GameObject? Target => _target;

        /// <summary>Starts a drag (mouse-down on the handle).</summary>
        public void Begin(GameObject target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            Cancel();
            _target = target;
            _startPosition = target.transform.position;
            _current = _startPosition;
            _startRef = MoveChangeSets.Build(_runtime, target, _startPosition)?.Operations[0].Target;
            _ghost = new GameObject("Studio move preview") { hideFlags = HideFlags.HideAndDontSave };
            _ghost.transform.position = _startPosition;
        }

        /// <summary>Moves only the hidden, unsaved preview transform.</summary>
        public void DragTo(Vector3 position)
        {
            if (_target == null)
            {
                return;
            }

            _current = position;
            if (_ghost != null) _ghost.transform.position = position;
        }

        /// <summary>Ends the drag (mouse-up): discards the preview and applies one <c>move</c> change set.</summary>
        public ApplyReport? End()
        {
            if (_target == null)
            {
                return null;
            }

            GameObject target = _target;
            Vector3 final = _current;
            Restore();
            if ((final - _startPosition).sqrMagnitude <= 0f)
            {
                return null;
            }

            ChangeSet? changeSet = _startRef == null ? null : StudioRuntime.Single("Move " + target.name, IntentOrigin.Manual,
                new Operation("op1", BuiltInToolIdsExt.Move, _startRef, new JObject { ["position"] = new JArray(ValueCodec.Widen(final.x), ValueCodec.Widen(final.y), ValueCodec.Widen(final.z)) }));
            return changeSet == null ? null : _runtime.Engine.Apply(changeSet);
        }

        /// <summary>Abandons the drag (Escape, selection change): the start pose comes back, nothing is applied.</summary>
        public void Cancel()
        {
            if (_target != null)
            {
                Restore();
            }
        }

        private void Restore()
        {
            if (_ghost != null) UnityEngine.Object.DestroyImmediate(_ghost);
            _ghost = null;
            _target = null;
        }
    }
}
