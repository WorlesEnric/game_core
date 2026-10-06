// GameCore.Studio.Views - every edit a view makes is a change set of existing tool ids applied by the ChangeSetEngine
// (docs/studio/03 s5/s6, SR-3.4): manual edits in views land in the journal exactly like agent edits, with the same
// validation, conflict detection (the target refs carry the stamp the view read) and journal-based undo.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    /// <summary>Builds and applies the views' change sets.</summary>
    public sealed class ViewEdits
    {
        private readonly StudioRuntime _runtime;

        public ViewEdits(StudioRuntime runtime)
        {
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        /// <summary>The last report (views show its state and diagnostics).</summary>
        public ApplyReport? LastReport { get; private set; }

        /// <summary>Raised after every apply a view made (journaled or refused).</summary>
        public event Action<ApplyReport>? Reported;

        /// <summary>A manual change set of <paramref name="operations"/> (policy AllOrNothing unless stated).</summary>
        public static ChangeSet Build(string intent, IReadOnlyList<Operation> operations, ApplyPolicy policy = ApplyPolicy.AllOrNothing)
        {
            if (operations == null || operations.Count == 0)
            {
                throw new ArgumentException("A change set needs at least one operation.", nameof(operations));
            }

            return new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(intent, IntentOrigin.Manual), operations, policy: policy);
        }

        /// <remarks>
        /// A definition target without a scope gets <see cref="AuthorScope.Definition"/>: model tools declared
        /// Definition-only (dialogue.addLine, quest.addStage...) refuse an unscoped target, and the index strips scopes
        /// from the references the views start from.
        /// </remarks>
        public static Operation Op(string opId, string tool, AuthoringRef? target, JObject? args = null, IReadOnlyList<string>? dependsOn = null)
        {
            if (target != null && target.Scope == null && target.Kind == AuthoringKind.Definition)
            {
                target = target.WithScope(AuthorScope.Definition);
            }

            return new Operation(opId, tool, target, args, dependsOn);
        }

        /// <summary><c>set {field, value}</c>.</summary>
        public static Operation SetOp(string opId, AuthoringRef target, string field, JToken? value)
        {
            return Op(opId, BuiltInToolIdsExt.Set, target, new JObject { ["field"] = field, ["value"] = value?.DeepClone() ?? JValue.CreateNull() });
        }

        /// <summary><c>set {fields: {...}}</c> (one op for several fields of one object: a row commit).</summary>
        public static Operation SetFieldsOp(string opId, AuthoringRef target, JObject fields)
        {
            return Op(opId, BuiltInToolIdsExt.Set, target, new JObject { ["fields"] = fields.DeepClone() });
        }

        /// <summary>Stages and applies <paramref name="changeSet"/> through the engine (journaled).</summary>
        public ApplyReport Apply(ChangeSet changeSet)
        {
            ApplyReport report = _runtime.Engine.Apply(changeSet);
            LastReport = report;
            Reported?.Invoke(report);
            return report;
        }

        /// <summary>A one-operation manual change set, applied.</summary>
        public ApplyReport ApplyOne(string intent, Operation operation)
        {
            return Apply(Build(intent, new[] { operation }));
        }

        /// <summary>Explicit creator command; creates one durable authored candidate for exit Play.</summary>
        public bool ApplyToAuthored(string runtimeChangeSetId, out ChangeSet? authored, out Diagnostic? problem) =>
            _runtime.Engine.RuntimeMoves.TryApplyToAuthored(runtimeChangeSetId, out authored, out problem);

        /// <summary>Journal-based undo of a change set the views (or anyone) applied.</summary>
        public HistoryResult Undo(string? changeSetId = null) => _runtime.History.Undo(changeSetId);

        /// <summary>Journal-based redo.</summary>
        public HistoryResult Redo(string? changeSetId = null) => _runtime.History.Redo(changeSetId);

        /// <summary>A short human summary of a report: state plus the first diagnostic.</summary>
        public static string Describe(ApplyReport report)
        {
            string text = report.State + " " + report.Entry.Id;
            if (report.Diagnostics.Count > 0)
            {
                text += " - " + report.Diagnostics[0];
            }
            else
            {
                foreach (OperationOutcome outcome in report.Outcomes)
                {
                    if (outcome.Status != OutcomeStatus.Applied && outcome.Detail != null)
                    {
                        text += " - " + outcome.OpId + " " + outcome.Status + ": " + outcome.Detail;
                        break;
                    }
                }
            }

            return text;
        }
    }
}
