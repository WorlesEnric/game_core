// GameCore.Studio.Edit - the edit engine (docs/studio/03-authoring-contracts.md s6, s7; 02 s5; SADR-009, SADR-011).
//
// Stage:  Resolve -> Precheck (catalog revision, envelope, stamps, existence, residency, scope) -> tool dry runs and
//         preview ghosts. Nothing is written (except a candidate's journal entry, state Candidate).
// Apply:  re-precheck -> journal `Interrupted` -> one Undo group + one AssetDatabase edit block -> per-op apply in
//         dependency order (live bridge in Play) with a journal checkpoint after each op -> AllOrNothing rollback
//         (Undo.RevertAllDownToGroup, then asset-level inverse ops in reverse) or commit -> journal final state.
// Conflicts: a target whose stamp changed is Conflict{expected, actual}; Rebase re-plans through a hook or the tool;
//         Skip drops operations. A gone target is StaleTarget. Applies are single-writer (ApplyQueue serializes).
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameCore.Composition;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Stages and applies change sets.</summary>
    public sealed class ChangeSetEngine
    {
        private readonly StudioRuntime _runtime;
        private int _assetEditingDepth;
        private bool _applying;

        internal ChangeSetEngine(StudioRuntime runtime, EngineOptions? options)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            Options = options ?? new EngineOptions();
        }

        public EngineOptions Options { get; }

        /// <summary>True in Play mode (or when the probe says so).</summary>
        public bool IsPlayMode => Options.PlayModeProbe?.Invoke() ?? EditorApplication.isPlaying;

        /// <summary>True while an apply runs (the single writer).</summary>
        public bool IsApplying => _applying;

        private readonly Dictionary<string, StagedChangeSet> _previews = new Dictionary<string, StagedChangeSet>(StringComparer.Ordinal);

        /// <summary>Change sets staged by <c>preview.stage</c>, by id (dropped when applied, discarded or re-staged).</summary>
        public IReadOnlyDictionary<string, StagedChangeSet> Previews => _previews;

        /// <summary>Keeps a staged change set for preview.compare (replacing and discarding an older stage of the same id).</summary>
        internal void RememberPreview(StagedChangeSet staged)
        {
            if (_previews.TryGetValue(staged.Id, out StagedChangeSet? previous) && !ReferenceEquals(previous, staged))
            {
                _runtime.Staging.Clear(previous.Id);
                previous.Consumed = true;
            }

            _previews[staged.Id] = staged;
        }

        /// <summary>Raised after every journaled apply.</summary>
        public event Action<ApplyReport>? Applied;

        /// <summary>Runs <paramref name="action"/> with the AssetDatabase edit block suspended (imports and asset creation need it).</summary>
        public void RunOutsideAssetEditing(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (_assetEditingDepth == 0)
            {
                action();
                return;
            }

            AssetDatabase.StopAssetEditing();
            try
            {
                action();
            }
            finally
            {
                AssetDatabase.StartAssetEditing();
            }
        }

        // ------------------------------------------------------------------------------------------------- stage

        /// <summary>Stages a change set: prechecks, dry runs and previews.</summary>
        public StagedChangeSet Stage(ChangeSet changeSet, StageOptions? options = null)
        {
            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            options ??= new StageOptions();
            StagedChangeSet staged = StageCore(changeSet, options, false, true);
            if (options.Mode == ValidationMode.Candidate && options.JournalCandidate && staged.Ok && !_runtime.Journal.Exists(changeSet.Id))
            {
                Timestamps stamps = new Timestamps(changeSet.Timestamps?.Requested, changeSet.Timestamps?.Candidate ?? Journal.Now(), null);
                _runtime.Journal.Write(changeSet.WithState(ChangeSetState.Candidate).WithTimestamps(stamps));
            }

            return staged;
        }

        /// <summary>Drops a staged change set's previews; with <paramref name="reject"/> a journaled candidate becomes Rejected.</summary>
        public void Discard(StagedChangeSet staged, bool reject = false)
        {
            if (staged == null)
            {
                throw new ArgumentNullException(nameof(staged));
            }

            _runtime.Staging.Clear(staged.Id);
            staged.Consumed = true;
            _previews.Remove(staged.Id);
            if (reject)
            {
                ChangeSet? entry = _runtime.Journal.Read(staged.Id);
                if (entry != null && entry.EffectiveState == ChangeSetState.Candidate)
                {
                    _runtime.Journal.Write(entry.WithState(ChangeSetState.Rejected));
                }
            }
        }

        /// <summary>
        /// Re-plans conflicting operations against their targets' current stamps (the Replan hook first, then the
        /// tool's <see cref="IReplannableTool"/>) and stages the result. Operations that cannot be re-planned keep their
        /// Conflict. <paramref name="opIds"/> limits the rebase (default: every conflicting op).
        /// </summary>
        public StagedChangeSet Rebase(StagedChangeSet staged, IEnumerable<string>? opIds = null)
        {
            if (staged == null)
            {
                throw new ArgumentNullException(nameof(staged));
            }

            HashSet<string>? only = opIds == null ? null : new HashSet<string>(opIds, StringComparer.Ordinal);
            List<Operation> operations = new List<Operation>();
            foreach (StagedOperation operation in staged.Operations)
            {
                Operation replanned = operation.Operation;
                if (operation.Conflict != null && (only == null || only.Contains(operation.OpId)) && operation.Target != null)
                {
                    string? current = _runtime.Resolver.ComputeStamp(operation.Target);
                    Operation? hooked = Options.Replan?.Invoke(operation, current);
                    if (hooked != null)
                    {
                        replanned = hooked;
                    }
                    else if (operation.Tool is IReplannableTool replannable)
                    {
                        EditContext context = new EditContext(_runtime, staged.ChangeSet, operation.Operation, operation.Target, true, null);
                        Operation? planned = replannable.Replan(context, current, out Diagnostic? problem);
                        if (planned != null)
                        {
                            replanned = planned;
                        }
                        else if (problem != null)
                        {
                            _runtime.Log.Write(StudioLogLevel.Info, "engine", "rebase of " + operation.OpId + " refused: " + problem.Message, problem);
                        }
                    }
                }

                operations.Add(replanned);
            }

            Discard(staged);
            ChangeSet rebased = With(staged.ChangeSet, operations);
            return StageCore(rebased, staged.Options, staged.AllowInternal, !staged.AllowInternal);
        }

        /// <summary>Drops operations (and every operation depending on them) and stages the rest.</summary>
        public StagedChangeSet Skip(StagedChangeSet staged, IEnumerable<string> opIds)
        {
            if (staged == null)
            {
                throw new ArgumentNullException(nameof(staged));
            }

            HashSet<string> dropped = new HashSet<string>(opIds ?? throw new ArgumentNullException(nameof(opIds)), StringComparer.Ordinal);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Operation operation in staged.ChangeSet.Operations)
                {
                    if (dropped.Contains(operation.OpId) || operation.DependsOn == null)
                    {
                        continue;
                    }

                    foreach (string dependency in operation.DependsOn)
                    {
                        if (dropped.Contains(dependency))
                        {
                            dropped.Add(operation.OpId);
                            grew = true;
                            break;
                        }
                    }
                }
            }

            List<Operation> kept = new List<Operation>();
            foreach (Operation operation in staged.ChangeSet.Operations)
            {
                if (!dropped.Contains(operation.OpId))
                {
                    kept.Add(operation);
                }
            }

            if (kept.Count == 0)
            {
                throw new InvalidOperationException("Skipping these operations leaves an empty change set; discard it instead.");
            }

            Discard(staged);
            return StageCore(With(staged.ChangeSet, kept), staged.Options, staged.AllowInternal, !staged.AllowInternal);
        }

        // ------------------------------------------------------------------------------------------------- apply

        /// <summary>Stages and applies.</summary>
        public ApplyReport Apply(ChangeSet changeSet, StageOptions? options = null)
        {
            return Apply(Stage(changeSet, options));
        }

        /// <summary>Applies a staged change set (single writer; refused while another apply runs).</summary>
        public ApplyReport Apply(StagedChangeSet staged)
        {
            if (staged == null)
            {
                throw new ArgumentNullException(nameof(staged));
            }

            if (_applying)
            {
                return Unjournaled(staged.ChangeSet, StudioDiagnostics.General(DiagnosticCodes.Refused, "Another change set is being applied; use ApplyAsync to queue."));
            }

            if (staged.Consumed)
            {
                return Unjournaled(staged.ChangeSet, StudioDiagnostics.General(DiagnosticCodes.StaleContext, "This staged change set was already applied or discarded; stage it again."));
            }

            ChangeSet? existing = _runtime.Journal.Read(staged.Id);
            if (existing != null && (existing.EffectiveState == ChangeSetState.Applied || existing.EffectiveState == ChangeSetState.Undone || existing.EffectiveState == ChangeSetState.Interrupted))
            {
                return Unjournaled(existing, StudioDiagnostics.General(DiagnosticCodes.LedgerConflict, "Change set " + staged.Id + " is already " + existing.EffectiveState + " in the journal."));
            }

            _applying = true;
            try
            {
                return ApplyCore(staged, null, true);
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>Queues an apply on the single-writer queue (runs on the editor update loop).</summary>
        public Task<ApplyReport> ApplyAsync(ChangeSet changeSet, StageOptions? options = null)
        {
            return _runtime.Queue.Enqueue(() => Apply(changeSet, options));
        }

        /// <summary>
        /// Applies operations for the journal (undo, redo, recovery): internal tools allowed, no catalog validation, no
        /// journal entry of its own. <paramref name="replay"/> maps op ids to redo hints.
        /// </summary>
        internal ApplyReport ApplyForHistory(ChangeSet changeSet, IReadOnlyDictionary<string, JObject>? replay, bool skipStageChecks = false)
        {
            if (_applying)
            {
                return Unjournaled(changeSet, StudioDiagnostics.General(DiagnosticCodes.Refused, "Another change set is being applied."));
            }

            _applying = true;
            try
            {
                StagedChangeSet staged = StageCore(changeSet, new StageOptions { Previews = false }, true, false);
                if (skipStageChecks)
                {
                    foreach (StagedOperation operation in staged.Operations)
                    {
                        operation.ClearStaleChecks();
                    }
                }

                return ApplyCore(staged, replay, false);
            }
            finally
            {
                _applying = false;
            }
        }

        private StagedChangeSet StageCore(ChangeSet changeSet, StageOptions options, bool allowInternal, bool validate)
        {
            List<Diagnostic> envelope = new List<Diagnostic>();
            ToolCatalog catalog = _runtime.Registry.Catalog;
            string catalogRevision = catalog.Revision ?? catalog.ComputeRevision();
            if (options.Mode == ValidationMode.Candidate)
            {
                if (options.ToolCatalogRevision == null)
                {
                    envelope.Add(StudioDiagnostics.General(DiagnosticCodes.StaleContext, "The candidate does not state the tool catalog revision it was planned against.", "Send toolCatalogRevision with the request."));
                }
                else if (!string.Equals(options.ToolCatalogRevision, catalogRevision, StringComparison.Ordinal))
                {
                    envelope.Add(new Diagnostic(
                        DiagnosticCodes.StaleContext,
                        "The candidate was planned against tool catalog " + options.ToolCatalogRevision + ", but the project's catalog is " + catalogRevision + ".",
                        "Re-plan against the current tool catalog.",
                        null,
                        new JObject { ["expected"] = catalogRevision, ["actual"] = options.ToolCatalogRevision }));
                }
            }

            _runtime.Index.Flush();
            Dictionary<string, List<Diagnostic>> byOperation = new Dictionary<string, List<Diagnostic>>(StringComparer.Ordinal);
            if (validate)
            {
                ChangeSetValidator validator = new ChangeSetValidator(catalog, _runtime.Index.Snapshot(), new ChangeSetValidationOptions
                {
                    Mode = options.Mode,
                    CheckStamps = false,
                    RequireTargetsInIndex = false,
                    IndexIsSlice = options.IndexIsSlice,
                });
                foreach (Diagnostic diagnostic in validator.Validate(changeSet))
                {
                    string? opId = OperationOf(changeSet, diagnostic);
                    if (opId == null)
                    {
                        envelope.Add(diagnostic);
                        continue;
                    }

                    if (!byOperation.TryGetValue(opId, out List<Diagnostic>? list))
                    {
                        list = new List<Diagnostic>();
                        byOperation[opId] = list;
                    }

                    list.Add(diagnostic);
                }
            }

            foreach (string missing in _runtime.Artifacts.Missing(changeSet))
            {
                envelope.Add(StudioDiagnostics.General(DiagnosticCodes.StageFailed, "Artifact sha256:" + missing + " is not retained in Studio/Artifacts.", "The companion must deliver the artifact before the candidate is staged."));
            }

            if (changeSet.BaseVersions != null)
            {
                foreach (BaseVersion version in changeSet.BaseVersions)
                {
                    ResolveResult resolved = _runtime.Resolver.Resolve(version.Ref.WithStamp(version.Stamp));
                    foreach (StaleEntry entry in resolved.Stale)
                    {
                        if (entry.Blocking)
                        {
                            envelope.Add(entry.Diagnostic);
                        }
                    }
                }
            }

            ulong? liveRevision = IsPlayMode && _runtime.Live.IsAvailable ? _runtime.Live.CommittedRevision : (ulong?)null;
            List<StagedOperation> operations = new List<StagedOperation>();
            _runtime.Staging.BeginOwner(changeSet.Id);
            try
            {
                foreach (Operation operation in changeSet.Operations)
                {
                    StagedOperation staged = new StagedOperation(operation);
                    operations.Add(staged);
                    if (byOperation.TryGetValue(operation.OpId, out List<Diagnostic>? found))
                    {
                        staged.AddRange(found);
                    }

                    IStudioTool? tool = _runtime.Registry.Find(operation.Tool);
                    if (tool == null || (tool.Internal && !allowInternal))
                    {
                        if (!Has(staged, DiagnosticCodes.UnknownTool))
                        {
                            staged.Add(StudioDiagnostics.Op(DiagnosticCodes.UnknownTool, operation.OpId, "Tool '" + operation.Tool + "' is not registered."));
                        }

                        continue;
                    }

                    staged.Tool = tool;
                    if (tool is IDirectTool && !tool.ReadOnly)
                    {
                        staged.Add(StudioDiagnostics.Op(DiagnosticCodes.Refused, operation.OpId, "Tool '" + operation.Tool + "' runs directly (ToolRegistry.Invoke), not inside a change set."));
                        continue;
                    }

                    Precheck(staged);
                    staged.Live = liveRevision.HasValue && tool.Entry.RuntimeApply == RuntimeApply.Live;
                    if (staged.Blocked || staged.Deferred)
                    {
                        continue;
                    }

                    EditContext context = new EditContext(_runtime, changeSet, operation, staged.Target, true, null);
                    try
                    {
                        ToolStageResult result = options.Previews ? tool.Stage(context) : StageWithoutPreview(tool, context);
                        staged.StageResult = result;
                        staged.AddRange(result.Diagnostics);
                    }
                    catch (Exception error) when (!(error is ExitGUIException))
                    {
                        staged.Add(StudioDiagnostics.Op(DiagnosticCodes.StageFailed, operation.OpId, "Staging '" + operation.Tool + "' threw " + error.GetType().Name + ": " + error.Message));
                        _runtime.Log.Write(StudioLogLevel.Error, "engine", "stage of " + operation.OpId + " threw: " + error);
                    }
                }
            }
            finally
            {
                _runtime.Staging.EndOwner();
            }

            return new StagedChangeSet(changeSet, options, operations, envelope, _runtime.Index.Revision, catalogRevision, liveRevision, allowInternal);
        }

        private ToolStageResult StageWithoutPreview(IStudioTool tool, EditContext context)
        {
            ToolStageResult result = tool.Stage(context);
            foreach (GameObject ghost in result.PreviewObjects)
            {
                if (ghost != null)
                {
                    UnityEngine.Object.DestroyImmediate(ghost);
                }
            }

            return result;
        }

        /// <summary>Target existence, residency and stamp checks (StaleTarget, Conflict).</summary>
        private void Precheck(StagedOperation staged)
        {
            Operation operation = staged.Operation;
            AuthoringRef? target = operation.Target;
            if (target == null)
            {
                if (staged.Tool != null && staged.Tool.Entry.TargetRequired && !Has(staged, DiagnosticCodes.InvalidArgs))
                {
                    staged.Add(StudioDiagnostics.Op(DiagnosticCodes.InvalidArgs, operation.OpId, "Tool '" + operation.Tool + "' needs a target."));
                }

                return;
            }

            if (target.Kind == AuthoringKind.Location)
            {
                return;
            }

            bool stampChecked = operation.EffectivePreconditions == Preconditions.Stamp;
            if (stampChecked && target.Stamp == null && !Has(staged, DiagnosticCodes.CandidateInvalid))
            {
                staged.Add(StudioDiagnostics.Op(
                    DiagnosticCodes.CandidateInvalid,
                    operation.OpId,
                    "Operation '" + operation.OpId + "' uses stamp preconditions but its target carries no stamp.",
                    "Copy the target ref (with its stamp) from the index slice, or declare preconditions \"none\"."));
            }

            ResolveResult resolved = _runtime.Resolver.Resolve(stampChecked ? target : target.WithStamp(null));
            if (resolved.Object == null)
            {
                bool unloaded = resolved.Stale.Count > 0 && resolved.Stale[0].Reason == StaleReason.RegionUnloaded;
                if (!unloaded && operation.DependsOn != null && operation.DependsOn.Count > 0)
                {
                    staged.Deferred = true;
                    return;
                }

                foreach (StaleEntry entry in resolved.Stale)
                {
                    staged.Add(entry.Diagnostic);
                }

                return;
            }

            staged.Target = resolved.Object;
            staged.CurrentStamp = resolved.CurrentStamp;
            foreach (StaleEntry entry in resolved.Stale)
            {
                if (entry.Blocking)
                {
                    staged.Add(entry.Diagnostic);
                }
            }
        }

        private ApplyReport ApplyCore(StagedChangeSet staged, IReadOnlyDictionary<string, JObject>? replay, bool journal)
        {
            Stopwatch watch = Stopwatch.StartNew();
            ChangeSet changeSet = staged.ChangeSet;
            ApplyPolicy policy = changeSet.EffectivePolicy;
            List<Diagnostic> diagnostics = new List<Diagnostic>(staged.Diagnostics);
            string requested = changeSet.Timestamps?.Requested ?? Journal.Now();
            Timestamps baseStamps = new Timestamps(requested, changeSet.Timestamps?.Candidate, null);

            if (staged.Diagnostics.Count > 0)
            {
                List<OperationOutcome> refused = new List<OperationOutcome>();
                Diagnostic first = staged.Diagnostics[0];
                foreach (Operation operation in changeSet.Operations)
                {
                    refused.Add(new OperationOutcome(operation.OpId, OutcomeStatus.Refused, first.Code, "Not applied: " + first.Message));
                }

                return Finish(staged, changeSet.WithState(ChangeSetState.Rejected).WithOutcomes(refused).WithTimestamps(baseStamps), diagnostics, false, journal, watch, Array.Empty<OperationResult>());
            }

            // Re-check targets right before writing: the project may have changed since staging.
            foreach (StagedOperation operation in staged.Operations)
            {
                if (operation.Tool != null && !operation.Deferred)
                {
                    operation.ClearStaleChecks();
                    Precheck(operation);
                }
            }

            List<StagedOperation> order = Order(staged.Operations);
            StagedOperation? firstBlocked = null;
            foreach (StagedOperation operation in order)
            {
                if (operation.Blocked)
                {
                    firstBlocked ??= operation;
                    diagnostics.AddRange(operation.Diagnostics);
                }
            }

            if (firstBlocked != null && policy == ApplyPolicy.AllOrNothing)
            {
                List<OperationOutcome> outcomes = new List<OperationOutcome>();
                foreach (StagedOperation operation in staged.Operations)
                {
                    outcomes.Add(operation.Blocked
                        ? new OperationOutcome(operation.OpId, OutcomeStatus.Refused, operation.Diagnostics[0].Code, operation.Diagnostics[0].Message)
                        : new OperationOutcome(operation.OpId, OutcomeStatus.Skipped, null, "Not applied: AllOrNothing and '" + firstBlocked.OpId + "' was refused."));
                }

                return Finish(staged, changeSet.WithState(ChangeSetState.Rejected).WithOutcomes(outcomes).WithTimestamps(baseStamps), diagnostics, false, journal, watch, Array.Empty<OperationResult>());
            }

            ChangeSet entry = changeSet.WithState(ChangeSetState.Interrupted).WithOutcomes(null).WithTimestamps(baseStamps);
            if (journal)
            {
                _runtime.Journal.Write(entry);
            }

            Fault(EngineFaultPoint.AfterInterruptedWritten, null);
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("GameCore Studio: " + Shorten(changeSet.Intent.Text));
            int group = Undo.GetCurrentGroup();
            Dictionary<string, OperationOutcome> outcomeById = new Dictionary<string, OperationOutcome>(StringComparer.Ordinal);
            List<OperationResult> applied = new List<OperationResult>();
            HashSet<string> notApplied = new HashSet<string>(StringComparer.Ordinal);
            List<string> gameCoreOps = new List<string>();
            bool failed = false;
            ulong? expectedRevision = staged.LiveRevision;
            bool assetEditing = !IsPlayMode;
            if (assetEditing)
            {
                AssetDatabase.StartAssetEditing();
                _assetEditingDepth++;
            }

            try
            {
                foreach (StagedOperation operation in order)
                {
                    OperationOutcome outcome;
                    if (failed && policy == ApplyPolicy.AllOrNothing)
                    {
                        outcome = new OperationOutcome(operation.OpId, OutcomeStatus.Skipped, null, "Not applied: an earlier operation failed (AllOrNothing).");
                    }
                    else if (operation.Blocked)
                    {
                        outcome = new OperationOutcome(operation.OpId, OutcomeStatus.Refused, operation.Diagnostics[0].Code, operation.Diagnostics[0].Message);
                    }
                    else if (DependencyMissing(operation.Operation, notApplied, out string? dependency))
                    {
                        outcome = new OperationOutcome(operation.OpId, OutcomeStatus.Skipped, null, "Not applied: dependency '" + dependency + "' did not apply.");
                    }
                    else
                    {
                        Fault(EngineFaultPoint.BeforeOperation, operation.OpId);
                        JObject? hints = replay != null && replay.TryGetValue(operation.OpId, out JObject? found) ? found : null;
                        OperationResult result = ApplyOne(staged, operation, hints, ref expectedRevision, diagnostics);
                        outcome = ToOutcome(operation.Operation, result);
                        if (result.Status == OutcomeStatus.Applied)
                        {
                            applied.Add(result);
                            gameCoreOps.AddRange(result.GameCoreOps);
                        }
                        else
                        {
                            failed = true;
                            diagnostics.Add(StudioDiagnostics.Op(result.Code ?? DiagnosticCodes.Refused, operation.OpId, result.Detail ?? result.Status.ToString()));
                        }
                    }

                    if (outcome.Status != OutcomeStatus.Applied)
                    {
                        notApplied.Add(operation.OpId);
                        failed |= policy == ApplyPolicy.AllOrNothing && outcome.Status != OutcomeStatus.Skipped;
                    }

                    outcomeById[operation.OpId] = outcome;
                    if (journal)
                    {
                        _runtime.Journal.Write(entry.WithOutcomes(InChangeSetOrder(changeSet, outcomeById)));
                    }

                    Fault(EngineFaultPoint.AfterOperation, operation.OpId);
                }
            }
            finally
            {
                if (assetEditing)
                {
                    _assetEditingDepth--;
                    AssetDatabase.StopAssetEditing();
                }
            }

            Fault(EngineFaultPoint.BeforeFinalize, null);
            bool rolledBack = false;
            ChangeSetState state;
            if (failed && policy == ApplyPolicy.AllOrNothing)
            {
                Rollback(staged.ChangeSet, group, applied, diagnostics);
                rolledBack = true;
                foreach (KeyValuePair<string, OperationOutcome> pair in new List<KeyValuePair<string, OperationOutcome>>(outcomeById))
                {
                    if (pair.Value.Status == OutcomeStatus.Applied)
                    {
                        outcomeById[pair.Key] = new OperationOutcome(pair.Key, OutcomeStatus.Skipped, null, "Rolled back: another operation failed (AllOrNothing).");
                    }
                }

                state = ChangeSetState.Failed;
                gameCoreOps.Clear();
            }
            else
            {
                Undo.CollapseUndoOperations(group);
                state = applied.Count > 0 ? ChangeSetState.Applied : (failed ? ChangeSetState.Failed : ChangeSetState.Rejected);
            }

            Links? links = changeSet.Links;
            if (gameCoreOps.Count > 0)
            {
                List<string> all = new List<string>(links?.GameCoreOps ?? Array.Empty<string>());
                all.AddRange(gameCoreOps);
                links = new Links(links?.EtosTasks, links?.Parent, all);
            }

            Timestamps finalStamps = new Timestamps(requested, changeSet.Timestamps?.Candidate, state == ChangeSetState.Applied ? Journal.Now() : null);
            ChangeSet final = changeSet.WithState(state).WithOutcomes(InChangeSetOrder(changeSet, outcomeById)).WithLinks(links).WithTimestamps(finalStamps);
            return Finish(staged, final, diagnostics, rolledBack, journal, watch, applied);
        }

        private OperationResult ApplyOne(StagedChangeSet staged, StagedOperation staging, JObject? replay, ref ulong? expectedRevision, List<Diagnostic> diagnostics)
        {
            Operation operation = staging.Operation;
            IStudioTool tool = staging.Tool!;
            UnityEngine.Object? target = staging.Target;
            if (operation.Target != null && operation.Target.Kind != AuthoringKind.Location && (target == null || staging.Deferred))
            {
                target = _runtime.Resolver.Find(operation.Target);
                if (target == null)
                {
                    return OperationResult.Refused(DiagnosticCodes.StaleTarget, "The target of '" + operation.OpId + "' does not exist.");
                }
            }

            EditContext context = new EditContext(_runtime, staged.ChangeSet, operation, target, false, replay);
            try
            {
                OperationResult result;
                ILiveOpTranslator? translator = staging.Live ? _runtime.Services.FindLiveTranslator(context) : null;
                if (translator != null)
                {
                    CompositionEditPayload? payload = translator.Translate(context, out Diagnostic? problem);
                    if (payload == null)
                    {
                        return OperationResult.Refused(problem?.Code ?? DiagnosticCodes.Refused, problem?.Message ?? "The live translator produced no edit.");
                    }

                    ulong expected = expectedRevision ?? _runtime.Live.CommittedRevision;
                    LiveSubmitResult live = _runtime.Live.Submit(payload, expected);
                    if (live.Status == LiveSubmitStatus.Stale)
                    {
                        if (operation.Target != null)
                        {
                            diagnostics.Add(Diagnostic.ConflictAt(
                                operation.Target,
                                "revision:" + live.ExpectedRevision,
                                "revision:" + live.ActualRevision,
                                "The world moved past the revision the change set was staged against (StalePlan).",
                                "Re-stage the change set against the current world."));
                        }

                        OperationResult stale = OperationResult.Refused(DiagnosticCodes.Conflict, "StalePlan: expected world revision " + live.ExpectedRevision + ", actual " + live.ActualRevision + ".");
                        if (live.OperationId != null)
                        {
                            stale.WithGameCoreOp(live.OperationId);
                        }

                        return stale;
                    }

                    if (live.Status != LiveSubmitStatus.Executed)
                    {
                        return OperationResult.Refused(DiagnosticCodes.Refused, live.Detail ?? live.Status.ToString());
                    }

                    expectedRevision = live.ActualRevision;
                    result = tool.Apply(context);
                    if (live.OperationId != null)
                    {
                        result.WithGameCoreOp(live.OperationId);
                    }
                }
                else
                {
                    result = tool.Apply(context);
                }

                return result;
            }
            catch (SimulatedCrashException)
            {
                throw;
            }
            catch (ExitGUIException)
            {
                throw;
            }
            catch (Exception error)
            {
                _runtime.Log.Write(StudioLogLevel.Error, "engine", "apply of " + operation.OpId + " (" + operation.Tool + ") threw: " + error);
                return OperationResult.Failed(DiagnosticCodes.Refused, operation.Tool + " threw " + error.GetType().Name + ": " + error.Message);
            }
        }

        private OperationOutcome ToOutcome(Operation operation, OperationResult result)
        {
            IReadOnlyList<string>? gameCoreOps = result.GameCoreOps.Count > 0 ? result.GameCoreOps : null;
            if (result.Status != OutcomeStatus.Applied)
            {
                return new OperationOutcome(operation.OpId, result.Status, result.Code, result.Detail, gameCoreOps);
            }

            List<StampWitness> after = new List<StampWitness>();
            foreach (UnityEngine.Object touched in result.Touched)
            {
                if (touched == null)
                {
                    continue;
                }

                AuthoringRef? reference = _runtime.Resolver.BuildRef(touched, null, true);
                if (reference?.Stamp != null)
                {
                    after.Add(new StampWitness(reference.WithStamp(null), reference.Stamp));
                }
            }

            UndoPayload payload = new UndoPayload(result.Inverse, result.AssetLevel, after, result.Replay);
            return new OperationOutcome(operation.OpId, OutcomeStatus.Applied, null, result.Detail, gameCoreOps, payload.IsEmpty ? null : new OperationUndo(payload.ToJson()));
        }

        /// <summary>AllOrNothing rollback: Unity Undo first, then the asset-level inverses in reverse order.</summary>
        private void Rollback(ChangeSet changeSet, int group, List<OperationResult> applied, List<Diagnostic> diagnostics)
        {
            Undo.RevertAllDownToGroup(group);
            for (int i = applied.Count - 1; i >= 0; i--)
            {
                OperationResult result = applied[i];
                if (!result.AssetLevel)
                {
                    continue;
                }

                foreach (Operation inverse in result.Inverse)
                {
                    IStudioTool? tool = _runtime.Registry.Find(inverse.Tool);
                    if (tool == null)
                    {
                        continue;
                    }

                    UnityEngine.Object? target = inverse.Target == null || inverse.Target.Kind == AuthoringKind.Location ? null : _runtime.Resolver.Find(inverse.Target);
                    if (inverse.Target != null && inverse.Target.Kind != AuthoringKind.Location && target == null)
                    {
                        // Undo already reverted the object this inverse would touch.
                        continue;
                    }

                    try
                    {
                        OperationResult reverted = tool.Apply(new EditContext(_runtime, changeSet, inverse, target, false, null));
                        if (reverted.Status != OutcomeStatus.Applied)
                        {
                            diagnostics.Add(StudioDiagnostics.General(DiagnosticCodes.Refused, "Rollback step " + inverse.Tool + " did not apply: " + reverted.Detail));
                        }
                    }
                    catch (Exception error) when (!(error is ExitGUIException))
                    {
                        diagnostics.Add(StudioDiagnostics.General(DiagnosticCodes.Refused, "Rollback step " + inverse.Tool + " threw: " + error.Message));
                    }
                }
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }

        private ApplyReport Finish(StagedChangeSet staged, ChangeSet final, List<Diagnostic> diagnostics, bool rolledBack, bool journal, Stopwatch watch, IReadOnlyList<OperationResult> applied)
        {
            if (journal)
            {
                _runtime.Journal.Write(final);
                if (final.EffectiveState == ChangeSetState.Applied)
                {
                    _runtime.History.OnApplied(final.Id);
                }
            }

            _runtime.Staging.Clear(staged.Id);
            staged.Consumed = true;
            if (_previews.TryGetValue(staged.Id, out StagedChangeSet? preview) && ReferenceEquals(preview, staged))
            {
                _previews.Remove(staged.Id);
            }

            NotifyIndex(applied);
            watch.Stop();
            ApplyReport report = new ApplyReport(final, diagnostics, rolledBack, journal, watch.Elapsed.TotalMilliseconds);
            _runtime.Log.Write(
                StudioLogLevel.Info,
                "engine",
                (journal ? "applied " : "history apply ") + final.Id + " -> " + final.EffectiveState + " (" + report.Outcomes.Count + " op(s), " + report.Milliseconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms)");
            if (journal)
            {
                Applied?.Invoke(report);
            }

            return report;
        }

        private void NotifyIndex(IReadOnlyList<OperationResult> applied)
        {
            if (applied.Count == 0)
            {
                return;
            }

            List<string> imported = new List<string>();
            List<string> deleted = new List<string>();
            foreach (OperationResult result in applied)
            {
                foreach (UnityEngine.Object touched in result.Touched)
                {
                    _runtime.Index.MarkObjectChanged(touched);
                }

                if (result.Output is JObject output && output["path"] is JValue value && value.Type == JTokenType.String)
                {
                    string path = value.Value<string>()!;
                    (File.Exists(_runtime.Paths.Absolute(path)) ? imported : deleted).Add(path);
                }

                foreach (Operation inverse in result.Inverse)
                {
                    string? path = inverse.Args?["path"]?.Type == JTokenType.String ? inverse.Args["path"]!.Value<string>() : null;
                    if (path != null)
                    {
                        (File.Exists(_runtime.Paths.Absolute(path)) ? imported : deleted).Add(path);
                    }
                }
            }

            _runtime.Index.MarkAssetsChanged(imported, deleted);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    _runtime.Index.MarkSceneChanged(scene);
                }
            }

            _runtime.Index.MarkPrefabStageChanged();
        }

        private ApplyReport Unjournaled(ChangeSet changeSet, Diagnostic diagnostic)
        {
            return new ApplyReport(changeSet, new[] { diagnostic }, false, false, 0);
        }

        private void Fault(EngineFaultPoint point, string? opId)
        {
            Options.FaultHook?.Invoke(point, opId);
        }

        private static bool Has(StagedOperation staged, string code)
        {
            foreach (Diagnostic diagnostic in staged.Diagnostics)
            {
                if (string.Equals(diagnostic.Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DependencyMissing(Operation operation, HashSet<string> notApplied, out string? dependency)
        {
            dependency = null;
            if (operation.DependsOn == null)
            {
                return false;
            }

            foreach (string id in operation.DependsOn)
            {
                if (notApplied.Contains(id))
                {
                    dependency = id;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Stable topological order: listed order, each op after its dependencies (cycles keep listed order).</summary>
        private static List<StagedOperation> Order(IReadOnlyList<StagedOperation> operations)
        {
            List<StagedOperation> ordered = new List<StagedOperation>();
            HashSet<string> placed = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
            foreach (StagedOperation operation in operations)
            {
                known.Add(operation.OpId);
            }

            List<StagedOperation> remaining = new List<StagedOperation>(operations);
            while (remaining.Count > 0)
            {
                int index = remaining.FindIndex(operation =>
                {
                    if (operation.Operation.DependsOn == null)
                    {
                        return true;
                    }

                    foreach (string dependency in operation.Operation.DependsOn)
                    {
                        if (known.Contains(dependency) && !placed.Contains(dependency))
                        {
                            return false;
                        }
                    }

                    return true;
                });
                if (index < 0)
                {
                    index = 0;
                }

                StagedOperation next = remaining[index];
                remaining.RemoveAt(index);
                ordered.Add(next);
                placed.Add(next.OpId);
            }

            return ordered;
        }

        private static List<OperationOutcome> InChangeSetOrder(ChangeSet changeSet, Dictionary<string, OperationOutcome> outcomes)
        {
            List<OperationOutcome> ordered = new List<OperationOutcome>();
            foreach (Operation operation in changeSet.Operations)
            {
                if (outcomes.TryGetValue(operation.OpId, out OperationOutcome? outcome))
                {
                    ordered.Add(outcome);
                }
            }

            return ordered;
        }

        private static string? OperationOf(ChangeSet changeSet, Diagnostic diagnostic)
        {
            if (diagnostic.Where == null)
            {
                return null;
            }

            if (diagnostic.Where.OpId != null)
            {
                return changeSet.FindOperation(diagnostic.Where.OpId) != null ? diagnostic.Where.OpId : null;
            }

            AuthoringRef? at = diagnostic.Where.Ref;
            foreach (Operation operation in changeSet.Operations)
            {
                if (at != null && operation.Target != null && operation.Target.SameTarget(at))
                {
                    return operation.OpId;
                }
            }

            return null;
        }

        private static ChangeSet With(ChangeSet changeSet, IReadOnlyList<Operation> operations)
        {
            return new ChangeSet(
                changeSet.Id,
                changeSet.Schema,
                changeSet.Intent,
                operations,
                changeSet.Selection,
                changeSet.BaseVersions,
                changeSet.Artifacts,
                changeSet.Validation,
                changeSet.Requirements,
                changeSet.Links,
                changeSet.State,
                changeSet.Outcomes,
                changeSet.Policy,
                changeSet.Timestamps);
        }

        private static string Shorten(string text) => text.Length <= 60 ? text : text.Substring(0, 57) + "...";
    }
}
