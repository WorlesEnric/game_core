// GameCore.Studio.Views - W-VIEW-06 model, part 1: inspecting pending change sets.
//
// A change set is inspected by staging it in Journal mode without previews (nothing is written: no journal entry, no
// preview objects) and reading the StagedChangeSet: per operation the tool, the resolved target, the current stamp,
// the diagnostics (with their `where` and `data {expected, actual}` for conflicts) and the dependsOn edges. A change
// set that is already staged as a preview (engine.Previews, e.g. a candidate the Studio shows) is read as it is and
// never discarded by the inspector.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    /// <summary>One diagnostic as the Changes view lists it.</summary>
    public sealed class DiagnosticRow
    {
        public DiagnosticRow(string source, Diagnostic diagnostic, string? opId)
        {
            Source = source;
            Diagnostic = diagnostic;
            OpId = opId ?? diagnostic.Where?.OpId;
        }

        /// <summary>stage, validator id, journal...</summary>
        public string Source { get; }

        public Diagnostic Diagnostic { get; }

        public string Code => Diagnostic.Code;

        public string Message => Diagnostic.Message;

        public string? OpId { get; }

        /// <summary>The ref to navigate to (the diagnostic's where, else the op's target).</summary>
        public AuthoringRef? Where { get; set; }

        public string? Expected => Diagnostic.Data?["expected"]?.ToString();

        public string? Actual => Diagnostic.Data?["actual"]?.ToString();

        public bool IsConflict => string.Equals(Diagnostic.Code, DiagnosticCodes.Conflict, StringComparison.Ordinal) || Diagnostic.Data?["expected"] != null;

        public override string ToString() => Code + ": " + Message + (IsConflict ? " {expected " + Expected + ", actual " + Actual + "}" : string.Empty);
    }

    public sealed class InspectedOperation
    {
        public InspectedOperation(Operation operation, string targetName, string? currentStamp, IReadOnlyList<DiagnosticRow> diagnostics, JToken? preview)
        {
            Operation = operation;
            TargetName = targetName;
            CurrentStamp = currentStamp;
            Diagnostics = diagnostics;
            Preview = preview;
        }

        public Operation Operation { get; }

        public string OpId => Operation.OpId;

        public string Tool => Operation.Tool;

        public string TargetName { get; }

        public string? CurrentStamp { get; }

        public IReadOnlyList<DiagnosticRow> Diagnostics { get; }

        public JToken? Preview { get; }

        public IReadOnlyList<string> DependsOn => Operation.DependsOn ?? (IReadOnlyList<string>)Array.Empty<string>();

        public bool HasConflict
        {
            get
            {
                foreach (DiagnosticRow row in Diagnostics)
                {
                    if (row.IsConflict)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    public sealed class ChangeSetInspection
    {
        public ChangeSetInspection(ChangeSet changeSet, IReadOnlyList<InspectedOperation> operations, IReadOnlyList<DiagnosticRow> diagnostics, bool ok, bool fromPreview)
        {
            ChangeSet = changeSet;
            Operations = operations;
            Diagnostics = diagnostics;
            Ok = ok;
            FromPreview = fromPreview;
        }

        public ChangeSet ChangeSet { get; }

        public IReadOnlyList<InspectedOperation> Operations { get; }

        /// <summary>Change-set level diagnostics plus every operation's.</summary>
        public IReadOnlyList<DiagnosticRow> Diagnostics { get; }

        public bool Ok { get; }

        public bool FromPreview { get; }

        public int ConflictCount
        {
            get
            {
                int count = 0;
                foreach (DiagnosticRow row in Diagnostics)
                {
                    count += row.IsConflict ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>Stages <paramref name="changeSet"/> for inspection (see the file header).</summary>
        public static ChangeSetInspection Inspect(StudioRuntime runtime, ChangeSet changeSet)
        {
            if (runtime.Engine.Previews.TryGetValue(changeSet.Id, out StagedChangeSet? existing))
            {
                return FromStaged(runtime, existing, true);
            }

            StagedChangeSet staged = runtime.Engine.Stage(changeSet, new StageOptions { Mode = ValidationMode.Journal, Previews = false, JournalCandidate = false });
            try
            {
                return FromStaged(runtime, staged, false);
            }
            finally
            {
                if (!runtime.Engine.Previews.ContainsKey(changeSet.Id) || ReferenceEquals(runtime.Engine.Previews[changeSet.Id], staged))
                {
                    runtime.Engine.Discard(staged, false);
                }
            }
        }

        public static ChangeSetInspection FromStaged(StudioRuntime runtime, StagedChangeSet staged, bool fromPreview)
        {
            List<InspectedOperation> operations = new List<InspectedOperation>();
            List<DiagnosticRow> all = new List<DiagnosticRow>();
            foreach (Diagnostic diagnostic in staged.Diagnostics)
            {
                DiagnosticRow row = new DiagnosticRow("change set", diagnostic, null) { Where = diagnostic.Where?.Ref };
                all.Add(row);
            }

            foreach (StagedOperation operation in staged.Operations)
            {
                List<DiagnosticRow> rows = new List<DiagnosticRow>();
                foreach (Diagnostic diagnostic in operation.Diagnostics)
                {
                    DiagnosticRow row = new DiagnosticRow("stage", diagnostic, operation.OpId) { Where = diagnostic.Where?.Ref ?? operation.Operation.Target };
                    rows.Add(row);
                    all.Add(row);
                }

                string name = operation.Target != null ? operation.Target.name : (operation.Operation.Target != null ? IndexGraph.LeafOf(operation.Operation.Target) : "(no target)");
                operations.Add(new InspectedOperation(operation.Operation, name, operation.CurrentStamp, rows, operation.Preview));
            }

            return new ChangeSetInspection(staged.ChangeSet, operations, all, staged.Ok, fromPreview);
        }

        /// <summary>The pending change sets: staged previews, then journaled candidates not staged.</summary>
        public static IReadOnlyList<ChangeSet> Pending(StudioRuntime runtime)
        {
            List<ChangeSet> pending = new List<ChangeSet>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, StagedChangeSet> preview in runtime.Engine.Previews)
            {
                if (seen.Add(preview.Key))
                {
                    pending.Add(preview.Value.ChangeSet);
                }
            }

            foreach (JournalRecord record in runtime.Journal.List(ChangeSetState.Candidate))
            {
                if (seen.Add(record.Id))
                {
                    ChangeSet? entry = runtime.Journal.Read(record.Id);
                    if (entry != null)
                    {
                        pending.Add(entry);
                    }
                }
            }

            return pending;
        }
    }
}
