// GameCore.Studio.Edit - data types of the edit engine: stage options, staged change sets, apply reports, the undo
// payload kept in outcome.undo.inverse, and fault injection for crash tests.
//
// undo.inverse shape (decided here, SADR-009):
//   { "operations": [Operation...],          inverse operations, applied in order by journal undo
//     "assetLevel": true,                    present when Unity Undo cannot revert the op (files on disk)
//     "after": [ { "ref": AuthoringRef, "stamp": "sha256:..." } ],   stamps right after apply (undo conflict check)
//     "replay": { ... } }                    hints that make a redo recreate the same identity (ids, paths)
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    /// <summary>How a change set is staged.</summary>
    public sealed class StageOptions
    {
        /// <summary>Candidate for change sets arriving from agents (03 s9 candidate mode); Journal otherwise.</summary>
        public ValidationMode Mode { get; set; } = ValidationMode.Journal;

        /// <summary>
        /// The tool catalog revision the change set was planned against (EditRequest.toolCatalogRevision). Required in
        /// Candidate mode: a missing or different revision is StaleContext.
        /// </summary>
        public string? ToolCatalogRevision { get; set; }

        /// <summary>The planner saw a bounded slice of the index (03 s3).</summary>
        public bool IndexIsSlice { get; set; }

        /// <summary>Build preview ghosts (default true).</summary>
        public bool Previews { get; set; } = true;

        /// <summary>Record a valid candidate in the journal as state Candidate (default true in Candidate mode).</summary>
        public bool JournalCandidate { get; set; } = true;
    }

    /// <summary>Where the fault hook is called (crash-recovery tests).</summary>
    public enum EngineFaultPoint
    {
        /// <summary>After the Interrupted entry is written, before the first write.</summary>
        AfterInterruptedWritten,
        BeforeOperation,
        AfterPrepared,
        AfterFileWrite,
        /// <summary>After an operation's outcome is checkpointed in the journal.</summary>
        AfterOperation,
        /// <summary>After all operations, before rollback or commit and the final journal write.</summary>
        BeforeFinalize,
    }

    /// <summary>Thrown by a fault hook to simulate the editor dying mid-apply: the engine does not finalize.</summary>
    public sealed class SimulatedCrashException : Exception
    {
        public SimulatedCrashException(string message)
            : base(message)
        {
        }
    }

    /// <summary>Engine switches.</summary>
    public sealed class EngineOptions
    {
        /// <summary>Called at each <see cref="EngineFaultPoint"/> with the op id (or null); may throw <see cref="SimulatedCrashException"/>.</summary>
        public Action<EngineFaultPoint, string?>? FaultHook { get; set; }

        /// <summary>Overrides "is the editor in Play mode" (tests drive the live path without entering Play).</summary>
        public Func<bool>? PlayModeProbe { get; set; }

        /// <summary>
        /// Rebase hook: re-plans a conflicting operation against the current stamp (e.g. by asking the agent). Consulted
        /// before the tool's own <see cref="IReplannableTool"/>; return null to leave the operation to the tool.
        /// </summary>
        public Func<StagedOperation, string?, Operation?>? Replan { get; set; }
    }

    /// <summary>One staged operation.</summary>
    public sealed class StagedOperation
    {
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        internal StagedOperation(Operation operation)
        {
            Operation = operation;
        }

        public Operation Operation { get; }

        public string OpId => Operation.OpId;

        public IStudioTool? Tool { get; internal set; }

        /// <summary>The resolved target at stage time (null for target-less tools, Location targets and deferred ops).</summary>
        public UnityEngine.Object? Target { get; internal set; }

        /// <summary>The target's stamp at stage time.</summary>
        public string? CurrentStamp { get; internal set; }

        /// <summary>The tool's dry run (null when staging stopped earlier).</summary>
        public ToolStageResult? StageResult { get; internal set; }

        /// <summary>The target does not resolve yet but the op depends on others that may create it; resolved at apply.</summary>
        public bool Deferred { get; internal set; }

        /// <summary>The op goes through the live world bridge (Play mode, Live tool, translator registered).</summary>
        public bool Live { get; internal set; }

        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

        public bool Blocked => _diagnostics.Count > 0;

        public JToken? Preview => StageResult?.Preview;

        /// <summary>The Conflict diagnostic at this op's target, or null.</summary>
        public Diagnostic? Conflict
        {
            get
            {
                foreach (Diagnostic diagnostic in _diagnostics)
                {
                    if (string.Equals(diagnostic.Code, DiagnosticCodes.Conflict, StringComparison.Ordinal))
                    {
                        return diagnostic;
                    }
                }

                return null;
            }
        }

        internal void Add(Diagnostic diagnostic) => _diagnostics.Add(StudioDiagnostics.Normalize(diagnostic));

        internal void AddRange(IEnumerable<Diagnostic> diagnostics)
        {
            foreach (Diagnostic diagnostic in diagnostics)
            {
                Add(diagnostic);
            }
        }

        internal void ClearStaleChecks()
        {
            _diagnostics.RemoveAll(diagnostic =>
                string.Equals(diagnostic.Code, DiagnosticCodes.Conflict, StringComparison.Ordinal)
                || string.Equals(diagnostic.Code, DiagnosticCodes.StaleTarget, StringComparison.Ordinal));
        }
    }

    /// <summary>A staged change set: findings, previews and the context it was staged against.</summary>
    public sealed class StagedChangeSet
    {
        internal StagedChangeSet(ChangeSet changeSet, StageOptions options, IReadOnlyList<StagedOperation> operations, IReadOnlyList<Diagnostic> diagnostics, long indexRevision, string catalogRevision, ulong? liveRevision, bool allowInternal)
        {
            ChangeSet = changeSet;
            Options = options;
            Operations = operations;
            Diagnostics = diagnostics;
            IndexRevision = indexRevision;
            CatalogRevision = catalogRevision;
            LiveRevision = liveRevision;
            AllowInternal = allowInternal;
        }

        public string Id => ChangeSet.Id;

        public ChangeSet ChangeSet { get; }

        /// <summary>Non-blocking scope inference evidence; never included in refusal diagnostics.</summary>
        public IReadOnlyList<Diagnostic> Inferences { get; internal set; } = Array.Empty<Diagnostic>();

        public StageOptions Options { get; }

        public IReadOnlyList<StagedOperation> Operations { get; }

        /// <summary>Change-set-wide findings (envelope, candidate mode, catalog revision, artifacts, base versions).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public long IndexRevision { get; }

        public string CatalogRevision { get; }

        /// <summary>The committed world revision at stage time (Play mode), the expected revision of live ops.</summary>
        public ulong? LiveRevision { get; }

        /// <summary>True once applied or discarded; a consumed stage cannot be applied again.</summary>
        public bool Consumed { get; internal set; }

        internal bool AllowInternal { get; }

        /// <summary>Every finding: change-set-wide first, then per operation in order.</summary>
        public IReadOnlyList<Diagnostic> AllDiagnostics
        {
            get
            {
                List<Diagnostic> all = new List<Diagnostic>(Diagnostics);
                foreach (StagedOperation operation in Operations)
                {
                    all.AddRange(operation.Diagnostics);
                }

                return all;
            }
        }

        /// <summary>True when nothing blocks applying every operation.</summary>
        public bool Ok
        {
            get
            {
                if (Diagnostics.Count > 0)
                {
                    return false;
                }

                foreach (StagedOperation operation in Operations)
                {
                    if (operation.Blocked)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Operations with a Conflict (candidates for rebase or skip).</summary>
        public IReadOnlyList<StagedOperation> Conflicts
        {
            get
            {
                List<StagedOperation> conflicts = new List<StagedOperation>();
                foreach (StagedOperation operation in Operations)
                {
                    if (operation.Conflict != null)
                    {
                        conflicts.Add(operation);
                    }
                }

                return conflicts;
            }
        }

        public StagedOperation? Find(string opId)
        {
            foreach (StagedOperation operation in Operations)
            {
                if (string.Equals(operation.OpId, opId, StringComparison.Ordinal))
                {
                    return operation;
                }
            }

            return null;
        }
    }

    /// <summary>The result of an apply: the journaled entry and every finding.</summary>
    public sealed class ApplyReport
    {
        internal ApplyReport(ChangeSet entry, IReadOnlyList<Diagnostic> diagnostics, bool rolledBack, bool journaled, double milliseconds)
        {
            Entry = entry;
            Diagnostics = diagnostics;
            RolledBack = rolledBack;
            Journaled = journaled;
            Milliseconds = milliseconds;
        }

        /// <summary>The change set as journaled (state, outcomes, links, timestamps).</summary>
        public ChangeSet Entry { get; }

        public ChangeSetState State => Entry.EffectiveState;

        public IReadOnlyList<OperationOutcome> Outcomes => Entry.Outcomes ?? (IReadOnlyList<OperationOutcome>)Array.Empty<OperationOutcome>();

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>True when an AllOrNothing apply wrote and then rolled back.</summary>
        public bool RolledBack { get; }

        /// <summary>False for refusals that left no journal entry (e.g. a busy engine) and for internal applies.</summary>
        public bool Journaled { get; }

        public double Milliseconds { get; }

        public bool Ok => State == ChangeSetState.Applied;

        public OperationOutcome? Outcome(string opId)
        {
            foreach (OperationOutcome outcome in Outcomes)
            {
                if (string.Equals(outcome.OpId, opId, StringComparison.Ordinal))
                {
                    return outcome;
                }
            }

            return null;
        }
    }

    /// <summary>A stamp recorded right after apply.</summary>
    public sealed class StampWitness
    {
        public StampWitness(AuthoringRef reference, string stamp)
        {
            Ref = reference;
            Stamp = stamp;
        }

        public AuthoringRef Ref { get; }

        public string Stamp { get; }
    }

    /// <summary>The <c>undo.inverse</c> payload of one outcome.</summary>
    public sealed class UndoPayload
    {
        public UndoPayload(IReadOnlyList<Operation> operations, bool assetLevel, IReadOnlyList<StampWitness> after, JObject? replay)
        {
            Operations = operations;
            AssetLevel = assetLevel;
            After = after;
            Replay = replay;
        }

        public IReadOnlyList<Operation> Operations { get; }

        public bool AssetLevel { get; }

        public IReadOnlyList<StampWitness> After { get; }

        public JObject? Replay { get; }

        public bool IsEmpty => Operations.Count == 0 && After.Count == 0 && (Replay == null || !Replay.HasValues);

        public JObject ToJson()
        {
            JArray operations = new JArray();
            foreach (Operation operation in Operations)
            {
                operations.Add(StudioJson.ToToken(operation));
            }

            JObject json = new JObject { ["operations"] = operations };
            if (AssetLevel)
            {
                json["assetLevel"] = true;
            }

            if (After.Count > 0)
            {
                JArray after = new JArray();
                foreach (StampWitness witness in After)
                {
                    after.Add(new JObject { ["ref"] = StudioJson.ToToken(witness.Ref), ["stamp"] = witness.Stamp });
                }

                json["after"] = after;
            }

            if (Replay != null && Replay.HasValues)
            {
                json["replay"] = Replay.DeepClone();
            }

            return json;
        }

        /// <summary>Parses an outcome's <c>undo.inverse</c>; null with a problem when malformed.</summary>
        public static UndoPayload? Parse(JObject? inverse, out string? problem)
        {
            problem = null;
            if (inverse == null)
            {
                return new UndoPayload(Array.Empty<Operation>(), false, Array.Empty<StampWitness>(), null);
            }

            try
            {
                List<Operation> operations = new List<Operation>();
                if (inverse["operations"] is JArray rows)
                {
                    foreach (JToken row in rows)
                    {
                        operations.Add(StudioJson.Deserialize<Operation>(row.ToString(Formatting.None)));
                    }
                }

                List<StampWitness> after = new List<StampWitness>();
                if (inverse["after"] is JArray witnesses)
                {
                    foreach (JToken witness in witnesses)
                    {
                        JToken? reference = witness["ref"];
                        string? stamp = (string?)witness["stamp"];
                        if (reference == null || stamp == null)
                        {
                            problem = "an 'after' witness lacks ref or stamp";
                            return null;
                        }

                        after.Add(new StampWitness(StudioJson.Deserialize<AuthoringRef>(reference.ToString(Formatting.None)), stamp));
                    }
                }

                bool assetLevel = inverse["assetLevel"]?.Type == JTokenType.Boolean && inverse["assetLevel"]!.Value<bool>();
                return new UndoPayload(operations, assetLevel, after, inverse["replay"] as JObject);
            }
            catch (JsonException error)
            {
                problem = error.Message;
                return null;
            }
        }
    }
}
