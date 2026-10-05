// GameCore.Studio.Edit - the tool contract of the edit engine (docs/studio/03-authoring-contracts.md s4, s5, s6).
//
// A tool stages (dry run: argument checks, validators, previews; nothing is written) and applies (writes through
// Undo-recorded edits). Applying returns an OperationResult with the per-op outcome and the inverse operations the
// journal keeps for undo (SADR-009). Inverse operations are plain Operation data, so undo works across sessions. An
// inverse that Unity Undo cannot revert (files written or deleted on disk) is marked asset-level: the engine runs it
// when an AllOrNothing apply rolls back.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.Edit
{
    /// <summary>The outcome of applying one operation.</summary>
    public sealed class OperationResult
    {
        private readonly List<Operation> _inverse = new List<Operation>();
        private readonly List<UnityEngine.Object> _touched = new List<UnityEngine.Object>();
        private readonly List<string> _gameCoreOps = new List<string>();

        public OperationResult(OutcomeStatus status, string? code = null, string? detail = null)
        {
            Status = status;
            Code = code == null ? null : StudioDiagnostics.Registered(code);
            Detail = detail == null ? null : new SecretRedactor().Redact(detail);
        }

        public OutcomeStatus Status { get; }

        /// <summary>A registered diagnostic code for Refused/Failed.</summary>
        public string? Code { get; }

        public string? Detail { get; private set; }

        /// <summary>Operations that undo this one (applied in order).</summary>
        public IReadOnlyList<Operation> Inverse => _inverse;

        /// <summary>True when the inverse must run on rollback because Unity Undo cannot revert the change (disk files).</summary>
        public bool AssetLevel { get; private set; }

        /// <summary>Result payload of read-only tools (inspect/query/preview) and created refs of compose tools.</summary>
        public JToken? Output { get; private set; }

        /// <summary>Hints a redo passes back to the tool (minted ids, chosen asset paths) so a replay recreates the same identity.</summary>
        public JObject? Replay { get; private set; }

        /// <summary>Objects this operation changed (their post-apply stamps are recorded for undo conflict checks).</summary>
        public IReadOnlyList<UnityEngine.Object> Touched => _touched;

        /// <summary>GameCore OperationIds of world edits made by this operation (Play mode).</summary>
        public IReadOnlyList<string> GameCoreOps => _gameCoreOps;

        public static OperationResult Applied(JToken? output = null)
        {
            OperationResult result = new OperationResult(OutcomeStatus.Applied);
            result.Output = output;
            return result;
        }

        public static OperationResult Refused(string code, string detail) => new OperationResult(OutcomeStatus.Refused, code, detail);

        public static OperationResult Failed(string code, string detail) => new OperationResult(OutcomeStatus.Failed, code, detail);

        public static OperationResult Skipped(string detail) => new OperationResult(OutcomeStatus.Skipped, null, detail);

        public OperationResult WithInverse(params Operation[] operations)
        {
            _inverse.AddRange(operations);
            return this;
        }

        public OperationResult WithAssetLevelInverse(params Operation[] operations)
        {
            _inverse.AddRange(operations);
            AssetLevel = true;
            return this;
        }

        public OperationResult WithOutput(JToken? output)
        {
            Output = output;
            return this;
        }

        public OperationResult WithReplay(JObject? replay)
        {
            Replay = replay;
            return this;
        }

        public OperationResult WithDetail(string? detail)
        {
            Detail = detail == null ? null : new SecretRedactor().Redact(detail);
            return this;
        }

        public OperationResult Touch(UnityEngine.Object? target)
        {
            if (target != null && !_touched.Contains(target))
            {
                _touched.Add(target);
            }

            return this;
        }

        public OperationResult WithGameCoreOp(string operationId)
        {
            _gameCoreOps.Add(operationId);
            return this;
        }
    }

    /// <summary>The result of staging one operation: dry-run findings and preview data.</summary>
    public sealed class ToolStageResult
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();
        private readonly List<GameObject> _previewObjects = new List<GameObject>();

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        /// <summary>Predicted values or impact, shown in the candidate strip and compared by <c>preview.compare</c>.</summary>
        public JToken? Preview { get; set; }

        /// <summary>Ghost objects under the staging root.</summary>
        public IReadOnlyList<GameObject> PreviewObjects => _previewObjects;

        public bool Ok => _diagnostics.Count == 0;

        public ToolStageResult Add(Diagnostic diagnostic)
        {
            _diagnostics.Add(StudioDiagnostics.Normalize(diagnostic));
            return this;
        }

        public ToolStageResult AddPreviewObject(GameObject ghost)
        {
            _previewObjects.Add(ghost);
            return this;
        }
    }

    /// <summary>A tool of the registry: a built-in or an [AuthorOperation] method.</summary>
    public interface IStudioTool
    {
        /// <summary>The catalog entry (03 s5).</summary>
        ToolEntry Entry { get; }

        /// <summary>Internal tools (journal inverses) are kept out of the exported catalog.</summary>
        bool Internal { get; }

        /// <summary>Read-only tools (inspect/query/preview) change nothing and need no journal entry when invoked directly.</summary>
        bool ReadOnly { get; }

        /// <summary>Dry run: argument and target checks, validators, previews. Must not write project state.</summary>
        ToolStageResult Stage(EditContext context);

        /// <summary>Applies the operation (Undo-recorded) and returns its outcome and inverse.</summary>
        OperationResult Apply(EditContext context);
    }

    /// <summary>
    /// A tool that runs directly (ToolRegistry.Invoke), never inside a change set: history (undo/redo operate on the
    /// journal itself), preview and project lifecycle tools. The engine refuses it in a change set.
    /// </summary>
    public interface IDirectTool
    {
    }

    /// <summary>A tool that can re-plan an operation against the current stamp of its target (03 s7 rebase).</summary>
    public interface IReplannableTool
    {
        /// <summary>The re-planned operation, or null with a problem when the operation cannot be rebased.</summary>
        Operation? Replan(EditContext context, string? currentStamp, out Diagnostic? problem);
    }

    /// <summary>A validator named by <c>[AuthorOperation(Validator = typeof(...))]</c>; runs while staging.</summary>
    public interface IOperationValidator
    {
        IEnumerable<Diagnostic> Validate(EditContext context);
    }

    /// <summary>
    /// Translates a Live operation on a world target into a GameCore composition edit (Play mode, SADR-011). Gameplay
    /// packages register translators; an operation without one changes only the authored data.
    /// </summary>
    public interface ILiveOpTranslator
    {
        bool CanTranslate(EditContext context);

        /// <summary>The composition edit, or null with a problem (reported as Refused).</summary>
        CompositionEditPayload? Translate(EditContext context, out Diagnostic? problem);
    }

    /// <summary>Everything a tool sees while staging or applying one operation.</summary>
    public sealed class EditContext
    {
        internal EditContext(StudioRuntime runtime, ChangeSet changeSet, Operation operation, UnityEngine.Object? target, bool dryRun, JObject? replay)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Target = target;
            IsDryRun = dryRun;
            Replay = replay;
        }

        public StudioRuntime Runtime { get; }

        public ChangeSet ChangeSet { get; }

        public Operation Operation { get; }

        /// <summary>The resolved target (null for target-less tools and Location targets).</summary>
        public UnityEngine.Object? Target { get; }

        /// <summary>True while staging.</summary>
        public bool IsDryRun { get; }

        /// <summary>Hints recorded by the first apply, passed back on redo.</summary>
        public JObject? Replay { get; }

        /// <summary>True when the operation applies in Play mode.</summary>
        public bool IsPlayMode => Runtime.Engine.IsPlayMode;

        public JObject Args => Operation.Args ?? new JObject();

        public AuthoringRefResolver Resolver => Runtime.Resolver;

        public ValueCodec Codec => Runtime.Resolver.Codec;

        public AuthoringIdentity Identity => Runtime.Identity;

        public AuthorableTypeRegistry Types => Runtime.Types;

        public SemanticIndexService Index => Runtime.Index;

        public ArtifactStore Artifacts => Runtime.Artifacts;

        public IStudioLog Log => Runtime.Log;

        public PreviewStaging Staging => Runtime.Staging;

        public StudioServiceRegistry Services => Runtime.Services;

        /// <summary>The Undo label of this operation.</summary>
        public string UndoLabel => "GameCore Studio: " + Operation.Tool;

        /// <summary>The raw argument, or null when absent or JSON null.</summary>
        public JToken? Arg(string name)
        {
            JToken? value = Args[name];
            return value == null || value.Type == JTokenType.Null ? null : value;
        }

        public string? StringArg(string name)
        {
            JToken? value = Arg(name);
            return value != null && value.Type == JTokenType.String ? value.Value<string>() : null;
        }

        public bool BoolArg(string name, bool fallback = false)
        {
            JToken? value = Arg(name);
            return value != null && value.Type == JTokenType.Boolean ? value.Value<bool>() : fallback;
        }

        /// <summary>Converts an argument to <typeparamref name="T"/>; false with a problem when absent or invalid.</summary>
        public bool TryArg<T>(string name, out T value, out string? problem)
        {
            value = default!;
            JToken? raw = Arg(name);
            if (raw == null)
            {
                problem = "argument '" + name + "' is missing";
                return false;
            }

            if (!Codec.TryToClr(raw, typeof(T), out object? converted, out problem))
            {
                problem = "argument '" + name + "': " + problem;
                return false;
            }

            value = (T)converted!;
            return true;
        }

        /// <summary>A replay hint (redo) or, failing that, an argument.</summary>
        public string? ReplayOrArg(string name)
        {
            JToken? replayed = Replay?[name];
            if (replayed != null && replayed.Type == JTokenType.String)
            {
                return replayed.Value<string>();
            }

            return StringArg(name);
        }

        /// <summary>Durably retain inverse operations before a tool's first side effect. Safe to call more than once.</summary>
        public void PrepareInverse(Operation[] inverse, bool assetLevel = false) => Runtime.Engine.PrepareInverse(inverse, assetLevel);

        public void RecordUndo(UnityEngine.Object target)
        {
            Undo.RecordObject(target, UndoLabel);
        }

        public void RegisterCreated(UnityEngine.Object created)
        {
            Undo.RegisterCreatedObjectUndo(created, UndoLabel);
        }

        /// <summary>Runs <paramref name="action"/> outside the engine's AssetDatabase edit block (imports need it).</summary>
        public void OutsideAssetEditing(Action action)
        {
            Runtime.Engine.RunOutsideAssetEditing(action);
        }

        /// <summary>A diagnostic about this operation.</summary>
        public Diagnostic Problem(string code, string message, string? hint = null) =>
            StudioDiagnostics.Op(code, Operation.OpId, message, hint);
    }
}
