// GameCore.Studio.Edit - journal-based undo/redo and crash recovery (SADR-009, 02 s5).
//
// Undo:  the latest Applied entry (or a named one). Every touched object is first checked against the stamps recorded
//        right after apply (undo.inverse.after): an object edited since is Conflict{expected, actual} and the undo is
//        refused unless forced. The inverse operations of the outcomes run in reverse op order as one internal apply
//        (one Undo group, asset-level inverses for files). The entry becomes Undone and is pushed on the redo stack.
// Redo:  the top of the redo stack (or a named Undone entry). The original operations run again with their replay
//        hints (minted ids, chosen paths) and their retained artifacts; requests (asset.generate, mechanism.propose)
//        are not repeated. The entry becomes Applied again with fresh outcomes (fresh inverses).
// Recovery: entries left Interrupted by a crash carry the outcomes checkpointed so far. Rollback runs the recorded
//        inverses of the applied ops and marks the entry Failed; Resume applies the ops that have no outcome yet and
//        finalizes the entry. Both are API only (the UI is P2.x).
// The redo stack lives in Library/GameCoreStudio/redo.json under the runtime's state root, so it survives domain
// reloads and editor restarts; a new apply clears it.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>The result of an undo, redo or recovery step.</summary>
    public sealed class HistoryResult
    {
        public HistoryResult(string? changeSetId, bool ok, ChangeSetState? state, IReadOnlyList<Diagnostic> diagnostics, ApplyReport? report)
        {
            ChangeSetId = changeSetId;
            Ok = ok;
            State = state;
            Diagnostics = diagnostics;
            Report = report;
        }

        public string? ChangeSetId { get; }

        public bool Ok { get; }

        /// <summary>The entry's state after the step.</summary>
        public ChangeSetState? State { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>The internal apply that ran the inverse (or replayed) operations.</summary>
        public ApplyReport? Report { get; }

        internal static HistoryResult Refused(string? id, string code, string message, string? hint = null)
        {
            return new HistoryResult(id, false, null, new[] { StudioDiagnostics.General(code, message, hint) }, null);
        }
    }

    /// <summary>Undo, redo and recovery over the journal.</summary>
    public sealed class HistoryService
    {
        /// <summary>Tools whose operations are requests to other systems; redo never repeats them.</summary>
        public static readonly IReadOnlyList<string> NotReplayed = new[] { BuiltInToolIds.AssetGenerate, BuiltInToolIds.MechanismPropose, BuiltInToolIds.ProjectBuild, BuiltInToolIds.ProjectLaunch };

        private readonly StudioRuntime _runtime;
        private List<string>? _redo;

        internal HistoryService(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public string RedoStackPath => Path.Combine(_runtime.Paths.LibraryRoot, "redo.json");

        /// <summary>Undone entry ids, most recent last.</summary>
        public IReadOnlyList<string> RedoStack => Redo_;

        /// <summary>The entry the next <see cref="Undo"/> reverts, or null.</summary>
        public string? NextUndo
        {
            get
            {
                IReadOnlyList<JournalRecord> applied = _runtime.Journal.List(ChangeSetState.Applied);
                string? latest = null;
                string? latestTime = null;
                foreach (JournalRecord record in applied)
                {
                    string time = record.Applied ?? string.Empty;
                    if (latest == null || string.CompareOrdinal(time, latestTime) > 0 || (time == latestTime && string.CompareOrdinal(record.Id, latest) > 0))
                    {
                        latest = record.Id;
                        latestTime = time;
                    }
                }

                return latest;
            }
        }

        /// <summary>The entry the next <see cref="Redo"/> re-applies, or null.</summary>
        public string? NextRedo => Redo_.Count == 0 ? null : Redo_[Redo_.Count - 1];

        /// <summary>Reverts the latest applied change set (or <paramref name="changeSetId"/>).</summary>
        public HistoryResult Undo(string? changeSetId = null, bool force = false)
        {
            string? id = changeSetId ?? NextUndo;
            if (id == null)
            {
                return HistoryResult.Refused(null, DiagnosticCodes.Refused, "Nothing to undo.");
            }

            ChangeSet? entry = _runtime.Journal.Read(id);
            if (entry == null || entry.EffectiveState != ChangeSetState.Applied)
            {
                return HistoryResult.Refused(id, DiagnosticCodes.Refused, "Change set " + id + " is not Applied (state " + (entry?.EffectiveState.ToString() ?? "missing") + ").");
            }

            List<Diagnostic> diagnostics = new List<Diagnostic>();
            List<Operation> inverses = new List<Operation>();
            IReadOnlyList<OperationOutcome> outcomes = entry.Outcomes ?? Array.Empty<OperationOutcome>();
            for (int i = outcomes.Count - 1; i >= 0; i--)
            {
                OperationOutcome outcome = outcomes[i];
                if (outcome.Status != OutcomeStatus.Applied || outcome.Undo == null)
                {
                    continue;
                }

                UndoPayload? payload = UndoPayload.Parse(outcome.Undo.Inverse, out string? problem);
                if (payload == null)
                {
                    return HistoryResult.Refused(id, DiagnosticCodes.CandidateInvalid, "The undo payload of " + outcome.OpId + " is malformed: " + problem);
                }

                if (!force)
                {
                    CheckAfter(payload, diagnostics);
                }

                inverses.AddRange(payload.Operations);
            }

            if (diagnostics.Count > 0)
            {
                return new HistoryResult(id, false, ChangeSetState.Applied, diagnostics, null);
            }

            ApplyReport? report = null;
            if (inverses.Count > 0)
            {
                report = _runtime.Engine.ApplyForHistory(InternalChangeSet(entry, "Undo: " + entry.Intent.Text, Renumber(inverses, "u")), null, true);
                if (!report.Ok)
                {
                    return new HistoryResult(id, false, ChangeSetState.Applied, report.Diagnostics, report);
                }
            }

            _runtime.Journal.Write(entry.WithState(ChangeSetState.Undone));
            Redo_.Remove(id);
            Redo_.Add(id);
            SaveRedo();
            _runtime.Log.Write(StudioLogLevel.Info, "history", "undid " + id + " (" + inverses.Count + " inverse op(s))");
            return new HistoryResult(id, true, ChangeSetState.Undone, report?.Diagnostics ?? Array.Empty<Diagnostic>(), report);
        }

        /// <summary>Re-applies the latest undone change set (or <paramref name="changeSetId"/>) with retained artifacts.</summary>
        public HistoryResult Redo(string? changeSetId = null)
        {
            string? id = changeSetId ?? NextRedo;
            if (id == null)
            {
                return HistoryResult.Refused(null, DiagnosticCodes.Refused, "Nothing to redo.");
            }

            ChangeSet? entry = _runtime.Journal.Read(id);
            if (entry == null || entry.EffectiveState != ChangeSetState.Undone)
            {
                return HistoryResult.Refused(id, DiagnosticCodes.Refused, "Change set " + id + " is not Undone (state " + (entry?.EffectiveState.ToString() ?? "missing") + ").");
            }

            Dictionary<string, JObject> replay = new Dictionary<string, JObject>(StringComparer.Ordinal);
            HashSet<string> wasApplied = new HashSet<string>(StringComparer.Ordinal);
            foreach (OperationOutcome outcome in entry.Outcomes ?? Array.Empty<OperationOutcome>())
            {
                if (outcome.Status != OutcomeStatus.Applied)
                {
                    continue;
                }

                wasApplied.Add(outcome.OpId);
                UndoPayload? payload = UndoPayload.Parse(outcome.Undo?.Inverse, out _);
                if (payload?.Replay != null)
                {
                    replay[outcome.OpId] = payload.Replay;
                }
            }

            List<Operation> operations = new List<Operation>();
            List<OperationOutcome> skipped = new List<OperationOutcome>();
            foreach (Operation operation in entry.Operations)
            {
                bool request = Contains(NotReplayed, operation.Tool);
                if (!wasApplied.Contains(operation.OpId) || request)
                {
                    skipped.Add(new OperationOutcome(operation.OpId, OutcomeStatus.Skipped, null, request ? "Requests are not repeated by redo." : "Not applied originally."));
                    continue;
                }

                operations.Add(new Operation(operation.OpId, operation.Tool, operation.Target == null ? null : StaleFree(operation.Target), operation.Args, Filter(operation.DependsOn, wasApplied), Preconditions.None, operation.ApplyRequirement));
            }

            ApplyReport? report = null;
            List<OperationOutcome> outcomes = new List<OperationOutcome>();
            if (operations.Count > 0)
            {
                report = _runtime.Engine.ApplyForHistory(InternalChangeSet(entry, "Redo: " + entry.Intent.Text, operations), replay, false);
                if (!report.Ok)
                {
                    return new HistoryResult(id, false, ChangeSetState.Undone, report.Diagnostics, report);
                }

                outcomes.AddRange(report.Outcomes);
            }

            outcomes.AddRange(skipped);
            List<OperationOutcome> ordered = new List<OperationOutcome>();
            foreach (Operation operation in entry.Operations)
            {
                foreach (OperationOutcome outcome in outcomes)
                {
                    if (outcome.OpId == operation.OpId)
                    {
                        ordered.Add(outcome);
                        break;
                    }
                }
            }

            Timestamps stamps = new Timestamps(entry.Timestamps?.Requested, entry.Timestamps?.Candidate, Journal.Now());
            _runtime.Journal.Write(entry.WithState(ChangeSetState.Applied).WithOutcomes(ordered).WithTimestamps(stamps));
            Redo_.Remove(id);
            SaveRedo();
            _runtime.Log.Write(StudioLogLevel.Info, "history", "redid " + id + " (" + operations.Count + " op(s))");
            return new HistoryResult(id, true, ChangeSetState.Applied, report?.Diagnostics ?? Array.Empty<Diagnostic>(), report);
        }

        /// <summary>Entries a crash left Interrupted (recovery candidates), oldest first.</summary>
        public IReadOnlyList<JournalRecord> Interrupted()
        {
            _runtime.Journal.Rescan();
            return _runtime.Journal.List(ChangeSetState.Interrupted);
        }

        /// <summary>Rolls an interrupted entry back: the checkpointed inverses of its applied ops run in reverse; the entry becomes Failed.</summary>
        public HistoryResult RollbackInterrupted(string changeSetId)
        {
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry == null || entry.EffectiveState != ChangeSetState.Interrupted)
            {
                return HistoryResult.Refused(changeSetId, DiagnosticCodes.Refused, "Change set " + changeSetId + " is not Interrupted.");
            }

            List<Operation> inverses = new List<Operation>();
            IReadOnlyList<OperationOutcome> outcomes = entry.Outcomes ?? Array.Empty<OperationOutcome>();
            for (int i = outcomes.Count - 1; i >= 0; i--)
            {
                if (outcomes[i].Status == OutcomeStatus.Applied && outcomes[i].Undo != null)
                {
                    UndoPayload? payload = UndoPayload.Parse(outcomes[i].Undo!.Inverse, out _);
                    if (payload != null)
                    {
                        inverses.AddRange(payload.Operations);
                    }
                }
            }

            ApplyReport? report = null;
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            if (inverses.Count > 0)
            {
                // Best effort: a crash lost unsaved scene edits, so some inverses find nothing to revert.
                ChangeSet rollback = InternalChangeSet(entry, "Recover (rollback): " + entry.Intent.Text, Renumber(inverses, "r"));
                report = _runtime.Engine.ApplyForHistory(new ChangeSet(rollback.Id, rollback.Schema, rollback.Intent, rollback.Operations, links: rollback.Links, policy: ApplyPolicy.BestEffort), null, true);
                diagnostics.AddRange(report.Diagnostics);
            }

            List<OperationOutcome> rolled = new List<OperationOutcome>();
            foreach (Operation operation in entry.Operations)
            {
                rolled.Add(new OperationOutcome(operation.OpId, OutcomeStatus.Skipped, null, "Rolled back after an interrupted apply."));
            }

            _runtime.Journal.Write(entry.WithState(ChangeSetState.Failed).WithOutcomes(rolled));
            return new HistoryResult(changeSetId, true, ChangeSetState.Failed, diagnostics, report);
        }

        /// <summary>Resumes an interrupted entry: the ops without an outcome are applied and the entry is finalized.</summary>
        public HistoryResult ResumeInterrupted(string changeSetId)
        {
            ChangeSet? entry = _runtime.Journal.Read(changeSetId);
            if (entry == null || entry.EffectiveState != ChangeSetState.Interrupted)
            {
                return HistoryResult.Refused(changeSetId, DiagnosticCodes.Refused, "Change set " + changeSetId + " is not Interrupted.");
            }

            Dictionary<string, OperationOutcome> done = new Dictionary<string, OperationOutcome>(StringComparer.Ordinal);
            foreach (OperationOutcome outcome in entry.Outcomes ?? Array.Empty<OperationOutcome>())
            {
                done[outcome.OpId] = outcome;
            }

            HashSet<string> applied = new HashSet<string>(StringComparer.Ordinal);
            foreach (OperationOutcome outcome in done.Values)
            {
                if (outcome.Status == OutcomeStatus.Applied)
                {
                    applied.Add(outcome.OpId);
                }
            }

            List<Operation> remaining = new List<Operation>();
            foreach (Operation operation in entry.Operations)
            {
                if (!done.ContainsKey(operation.OpId))
                {
                    remaining.Add(new Operation(operation.OpId, operation.Tool, operation.Target, operation.Args, Filter(operation.DependsOn, null, done.Keys), operation.Preconditions, operation.ApplyRequirement));
                }
            }

            ApplyReport? report = null;
            if (remaining.Count > 0)
            {
                ChangeSet resumed = InternalChangeSet(entry, entry.Intent.Text, remaining);
                report = _runtime.Engine.ApplyForHistory(new ChangeSet(resumed.Id, resumed.Schema, resumed.Intent, resumed.Operations, links: resumed.Links, policy: entry.EffectivePolicy), null, false);
                foreach (OperationOutcome outcome in report.Outcomes)
                {
                    done[outcome.OpId] = outcome;
                }
            }

            List<OperationOutcome> ordered = new List<OperationOutcome>();
            bool anyApplied = false;
            foreach (Operation operation in entry.Operations)
            {
                if (done.TryGetValue(operation.OpId, out OperationOutcome? outcome))
                {
                    ordered.Add(outcome);
                    anyApplied |= outcome.Status == OutcomeStatus.Applied;
                }
            }

            ChangeSetState state = anyApplied ? ChangeSetState.Applied : ChangeSetState.Failed;
            Timestamps stamps = new Timestamps(entry.Timestamps?.Requested, entry.Timestamps?.Candidate, state == ChangeSetState.Applied ? Journal.Now() : null);
            _runtime.Journal.Write(entry.WithState(state).WithOutcomes(ordered).WithTimestamps(stamps));
            if (state == ChangeSetState.Applied)
            {
                OnApplied(changeSetId);
            }

            return new HistoryResult(changeSetId, state == ChangeSetState.Applied, state, report?.Diagnostics ?? Array.Empty<Diagnostic>(), report);
        }

        /// <summary>A new change set was applied: the redo stack is cleared.</summary>
        internal void OnApplied(string changeSetId)
        {
            if (Redo_.Count == 0)
            {
                return;
            }

            Redo_.Clear();
            SaveRedo();
        }

        private void CheckAfter(UndoPayload payload, List<Diagnostic> diagnostics)
        {
            foreach (StampWitness witness in payload.After)
            {
                UnityEngine.Object? found = _runtime.Resolver.Find(witness.Ref);
                if (found == null)
                {
                    continue;
                }

                string? current = _runtime.Resolver.ComputeStamp(found);
                if (current != null && !string.Equals(current, witness.Stamp, StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic.ConflictAt(
                        witness.Ref.WithStamp(witness.Stamp),
                        witness.Stamp,
                        current,
                        "The object changed after this change set was applied; undoing would overwrite that edit.",
                        "Undo the later change first, or force the undo."));
                }
            }
        }

        private List<string> Redo_ => _redo ??= LoadRedo();

        private List<string> LoadRedo()
        {
            List<string> stack = new List<string>();
            if (!File.Exists(RedoStackPath))
            {
                return stack;
            }

            try
            {
                if (JObject.Parse(File.ReadAllText(RedoStackPath, Encoding.UTF8))["stack"] is JArray rows)
                {
                    foreach (JToken row in rows)
                    {
                        string? id = (string?)row;
                        if (IdDerivation.IsChangeSetId(id))
                        {
                            stack.Add(id!);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            return stack;
        }

        private void SaveRedo()
        {
            StudioPaths.WriteAllTextAtomic(RedoStackPath, new JObject { ["schema"] = "gamecore.studio.redo/1", ["stack"] = new JArray(Redo_.ToArray()) }.ToString(Formatting.None));
        }

        private static ChangeSet InternalChangeSet(ChangeSet entry, string text, IReadOnlyList<Operation> operations)
        {
            return new ChangeSet(
                IdDerivation.NewChangeSetId(),
                ChangeSet.SchemaId,
                new Intent(text.Length <= 200 ? text : text.Substring(0, 200), IntentOrigin.Replay),
                operations,
                links: new Links(null, entry.Id, null),
                policy: ApplyPolicy.AllOrNothing);
        }

        private static List<Operation> Renumber(List<Operation> operations, string prefix)
        {
            List<Operation> renumbered = new List<Operation>(operations.Count);
            for (int i = 0; i < operations.Count; i++)
            {
                Operation operation = operations[i];
                renumbered.Add(new Operation(prefix + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), operation.Tool, operation.Target, operation.Args, null, Preconditions.None, operation.ApplyRequirement));
            }

            return renumbered;
        }

        private static AuthoringRef StaleFree(AuthoringRef target) => target.WithStamp(null);

        private static IReadOnlyList<string>? Filter(IReadOnlyList<string>? dependsOn, HashSet<string>? keep, IEnumerable<string>? drop = null)
        {
            if (dependsOn == null)
            {
                return null;
            }

            HashSet<string> dropped = drop == null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(drop, StringComparer.Ordinal);
            List<string> kept = new List<string>();
            foreach (string id in dependsOn)
            {
                if ((keep == null || keep.Contains(id)) && !dropped.Contains(id))
                {
                    kept.Add(id);
                }
            }

            return kept.Count == 0 ? null : kept;
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string item in list)
            {
                if (string.Equals(item, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
