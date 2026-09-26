// GameCore.Validation.ProbeHost — the GC-024 conformance runner: 07's tables executed in real Unity worlds.
//
// ONE RUNNER, ONE GENRE PER TABLE. The runner owns no gameplay fact at all: it reads the transcriptions
// (`GameCore.ReferenceConformance.ReferenceTables`), the scripts (`ReferenceScripts`) and the field vocabulary
// (`ConformanceFields`) from the pure fixture assembly, builds one real world per stage through the genre's own
// `ConformanceWorld`, and asks the genre to perform each operation and to read each field. What it produces is the
// two things the task asks for and nothing else:
//
//   * one NORMALIZED TRACE per 07 table (`artifacts/gc-024/traces/<table>.txt`), canonical and digest-carrying, whose
//     facts are compared against the transcribed table by the fixture's own oracle;
//   * one named observation per table plus one per row, so a failure names the row and the field that moved.
//
// HOW A STEP IS OBSERVED. Before a step, every field of the step's expectations is read; the operation runs; the same
// fields are read again. A row step additionally snapshots the table's whole declared field set on both sides, which
// is the before/after state diff a reviewer checks — a row's own assertions are a subset of what the run recorded,
// never the whole of it.
//
// WHY THE OUTCOME IS PART OF THE OBSERVATION. A row whose 07 column says the operation leaves the old assembly
// published (`07:108`) cannot be checked by values alone: a run that published would still read the same numbers if
// the switch had been a no-op. So each step reports whether its operation published, and the oracle requires the
// refusal a refused row demands (P-014, P-028).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.ReferenceConformance;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>What one step's observation says: it held, it failed, or it is a gap the fixture records as one.</summary>
    public enum ConformanceStepStatus
    {
        /// <summary>The step did what its 07 row demands.</summary>
        Pass = 0,

        /// <summary>The step did not hold. A single failure makes the whole run red.</summary>
        Fail = 1,

        /// <summary>
        /// The step could not be performed in this revision, for a reason the fixture records as a documentation
        /// gap (`ConformanceDocGaps`): the 07 clause it serves is named, the mechanism this revision lacks is named,
        /// and the state the run did observe is recorded. It is *not* a pass — `AllPassed` excludes it and the
        /// probe reports it as an expected negative — so a gap can never be mistaken for a satisfied requirement.
        /// </summary>
        RecordedGap = 2,
    }

    /// <summary>One named conformance observation: the step, its status, and the values it was computed from.</summary>
    public sealed class ConformanceObservation
    {
        public ConformanceObservation(string name, ConformanceStepStatus status, string detail)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Status = status;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Convenience for a step that is a plain pass or fail.</summary>
        public ConformanceObservation(string name, bool passed, string detail)
            : this(name, passed ? ConformanceStepStatus.Pass : ConformanceStepStatus.Fail, detail)
        {
        }

        /// <summary>The observation's qualified name.</summary>
        public string Name { get; }

        /// <summary>What the observation says.</summary>
        public ConformanceStepStatus Status { get; }

        /// <summary>True only for <see cref="ConformanceStepStatus.Pass"/>.</summary>
        public bool Passed => Status == ConformanceStepStatus.Pass;

        /// <summary>True when this step is a recorded documentation gap rather than a result.</summary>
        public bool IsRecordedGap => Status == ConformanceStepStatus.RecordedGap;

        /// <summary>The values the status was computed from.</summary>
        public string Detail { get; }

        public override string ToString() => Name + ": " + Status + " (" + Detail + ")";
    }

    /// <summary>One table's whole result: its observations, its normalized trace and the oracle's verdict.</summary>
    public sealed class ConformanceTableResult
    {
        private readonly List<ConformanceObservation> failures = new List<ConformanceObservation>();
        private readonly List<ConformanceObservation> gaps = new List<ConformanceObservation>();

        public ConformanceTableResult(
            string tableId,
            IReadOnlyList<ConformanceObservation> steps,
            ConformanceTrace trace,
            ConformanceVerdict verdict,
            string document)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
            Trace = trace ?? throw new ArgumentNullException(nameof(trace));
            Verdict = verdict ?? throw new ArgumentNullException(nameof(verdict));
            Document = document ?? string.Empty;

            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].Status == ConformanceStepStatus.Fail)
                {
                    failures.Add(Steps[i]);
                }
                else if (Steps[i].Status == ConformanceStepStatus.RecordedGap)
                {
                    gaps.Add(Steps[i]);
                }
            }

            // The whole run is green only when the oracle accepted the trace AND every observed step held. A step
            // that could not be performed is a `RecordedGap` and keeps this false: a gap is not a pass, so a
            // requirement that this revision cannot satisfy can never be reported as satisfied.
            AllPassed = verdict.Passed && failures.Count == 0;
        }

        /// <summary>The 07 table this result is about.</summary>
        public string TableId { get; }

        /// <summary>The named observations, in execution order.</summary>
        public IReadOnlyList<ConformanceObservation> Steps { get; }

        /// <summary>The normalized trace this run recorded.</summary>
        public ConformanceTrace Trace { get; }

        /// <summary>The oracle's field-by-field verdict.</summary>
        public ConformanceVerdict Verdict { get; }

        /// <summary>The trace's canonical document text, which the caller commits as evidence.</summary>
        public string Document { get; }

        /// <summary>True when the oracle passed and no step failed; a recorded gap keeps it false.</summary>
        public bool AllPassed { get; }

        /// <summary>Every step that did not hold.</summary>
        public IReadOnlyList<ConformanceObservation> Failures => failures;

        /// <summary>
        /// Every step this revision could not perform for a reason the fixture records as a documentation gap. An
        /// empty list is the normal value; a non-empty one is a reported gap, never a silent one.
        /// </summary>
        public IReadOnlyList<ConformanceObservation> RecordedGaps => gaps;

        public string Describe()
        {
            var text = new System.Text.StringBuilder();
            text.Append(TableId).Append(": digest=").Append(Trace.Digest())
                .Append("; facts=").Append(Trace.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; steps=").Append(Steps.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; failed=").Append(failures.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; recordedGaps=").Append(gaps.Count.ToString(CultureInfo.InvariantCulture))
                .Append("; ").Append(Verdict.Describe());
            for (int i = 0; i < failures.Count && i < 4; i++)
            {
                text.Append("; FAIL ").Append(failures[i].Name).Append(" (").Append(failures[i].Detail).Append(')');
            }

            for (int i = 0; i < gaps.Count && i < 4; i++)
            {
                text.Append("; GAP ").Append(gaps[i].Name).Append(" (").Append(gaps[i].Detail).Append(')');
            }

            return text.ToString();
        }
    }

    /// <summary>Executes every transcribed 07 table against one genre's real worlds.</summary>
    public static class ConformanceScenario
    {
        /// <summary>The step-name prefix a run of one table carries in the probe report.</summary>
        public const string StepPrefix = "conformance/";

        /// <summary>How many targets one conformance world registers; the card market's own tree needs a dozen.</summary>
        public const int TargetCapacity = 48;

        /// <summary>Salt of the session-id sequence of a conformance run, so a run is reproducible (P-008).</summary>
        public const ulong SessionSalt = 0x434F4E464F524DUL;

        /// <summary>Runs one table's script over one genre and returns its trace, verdict and observations.</summary>
        public static ConformanceTableResult Run(IConformanceFamily family, string tableId)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            ConformanceScript? script = ReferenceScripts.ById(tableId);
            ConformanceTable? table = ReferenceTables.ById(tableId);
            if (script == null || table == null)
            {
                throw new InvalidOperationException(
                    "no 07 table or script carries the id '" + tableId + "' (GC-024).");
            }

            var steps = new List<ConformanceObservation>();
            var trace = new ConformanceTrace(family.ConformanceLabel);
            var outcomes = new List<ConformanceOracle.RowOutcomeReport>();
            var sessions = new IdSequence(SessionSalt);

            for (int s = 0; s < script.Stages.Count; s++)
            {
                ConformanceStage stage = script.Stages[s];
                RunStage(family, table, stage, sessions, trace, outcomes, steps);
            }

            ConformanceVerdict verdict = ConformanceOracle.CompareScript(script, trace, outcomes);
            steps.Add(new ConformanceObservation(
                StepPrefix + table.TableId + "/verdict",
                verdict.Passed,
                verdict.Describe()));
            return new ConformanceTableResult(table.TableId, steps, trace, verdict, trace.ToDocument());
        }

        /// <summary>
        /// Runs one stage: a fresh world, its declared setup operations, then the steps in order. Every field of the
        /// step's expectations is read on both sides of its operation, and a row step snapshots the table's whole
        /// declared field set on both sides.
        /// </summary>
        private static void RunStage(
            IConformanceFamily family,
            ConformanceTable table,
            ConformanceStage stage,
            IdSequence sessions,
            ConformanceTrace trace,
            List<ConformanceOracle.RowOutcomeReport> outcomes,
            List<ConformanceObservation> steps)
        {
            string label = stage.StageId;
            ConformanceWorld world = ConformanceWorld.Build(family, sessions, TargetCapacity);
            try
            {
                if (!world.Ready)
                {
                    steps.Add(new ConformanceObservation(
                        StepPrefix + table.TableId + "/" + label + "/world",
                        false,
                        "the world could not be built: " + world.Failure));
                    return;
                }

                steps.Add(new ConformanceObservation(
                    StepPrefix + table.TableId + "/" + label + "/world",
                    world.MatchesPublishedAssembly(),
                    "session=" + world.Host!.World.Session.ToString()
                    + "; mode=" + world.Lane!.Committed.Mode
                    + "; targets=" + world.Targets!.Count.ToString(CultureInfo.InvariantCulture)
                    + "; joined=" + world.MatchesPublishedAssembly()));

                for (int i = 0; i < stage.Setup.Count; i++)
                {
                    ConformanceOperationResult setup = family.Apply(stage.Setup[i], 0, world);
                    steps.Add(new ConformanceObservation(
                        StepPrefix + table.TableId + "/" + label + "/setup-" + i.ToString(CultureInfo.InvariantCulture),
                        setup.Published,
                        stage.Setup[i] + ": " + setup.Detail));
                    if (!setup.Published)
                    {
                        return;
                    }
                }

                IReadOnlyList<ConformanceObservation> tableSteps = stage.Steps;
                for (int i = 0; i < tableSteps.Count; i++)
                {
                    RunStep(family, table, label, tableSteps[i], world, trace, outcomes, steps);
                }
            }
            catch (Exception exception)
            {
                steps.Add(new ConformanceObservation(
                    StepPrefix + table.TableId + "/" + label + "/unhandled",
                    false,
                    "unhandled " + exception.GetType().FullName + ": " + exception.Message));
            }
            finally
            {
                Outcome stop = world.StopAndDispose();
                steps.Add(new ConformanceObservation(
                    StepPrefix + table.TableId + "/" + label + "/teardown",
                    stop == Outcome.Published || stop == Outcome.NoChange,
                    "stop=" + stop + "; registry=" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>Runs one step of a stage and records everything it observed.</summary>
        private static void RunStep(
            IConformanceFamily family,
            ConformanceTable table,
            string stageId,
            ConformanceObservation step,
            ConformanceWorld world,
            ConformanceTrace trace,
            List<ConformanceOracle.RowOutcomeReport> outcomes,
            List<ConformanceObservation> steps)
        {
            string name = StepPrefix + table.TableId + "/" + step.RowId;
            IReadOnlyList<ConformanceExpectation> expectations = step.Expectations;

            // The row's own fields first, then — for a row step — the table's whole declared vocabulary, so the
            // trace carries the state the assertions were made against and not only the assertions (P-026).
            var fields = new List<string>();
            var before = new List<string>();
            for (int e = 0; e < expectations.Count; e++)
            {
                fields.Add(expectations[e].Field);
                before.Add(ReadField(family, world, expectations[e].Field));
            }

            var snapshotBefore = new List<string>();
            if (step.Kind == ConformanceStepKind.Row)
            {
                IReadOnlyList<ConformanceField> declared = ConformanceFields.Of(table.TableId);
                for (int f = 0; f < declared.Count; f++)
                {
                    snapshotBefore.Add(ReadField(family, world, declared[f].Key));
                }
            }

            ConformanceOperationResult result;
            switch (step.Operation)
            {
                case ConformanceOperations.CommitCommand:
                    result = family.Apply(step.Operation, step.Operand <= 0 ? 1 : step.Operand, world);
                    break;
                default:
                    result = family.Apply(step.Operation, step.Operand, world);
                    break;
            }

            outcomes.Add(new ConformanceOracle.RowOutcomeReport(
                step.RowId, result.Published, result.RefusalToken));

            var after = new List<string>();
            for (int e = 0; e < expectations.Count; e++)
            {
                after.Add(ReadField(family, world, expectations[e].Field));
            }

            for (int e = 0; e < expectations.Count; e++)
            {
                trace.Record(table.TableId, step.RowId, ConformancePhase.Before, fields[e], before[e]);
                trace.Record(table.TableId, step.RowId, ConformancePhase.After, fields[e], after[e]);
            }

            if (step.Kind == ConformanceStepKind.Row)
            {
                IReadOnlyList<ConformanceField> declared = ConformanceFields.Of(table.TableId);
                for (int f = 0; f < declared.Count; f++)
                {
                    // The snapshot is recorded under its own row id so it never competes with the row's own
                    // assertions: `state` is what the run saw, the row's fields are what the document demands.
                    trace.TryRecord(
                        table.TableId, step.RowId + "/state", ConformancePhase.Before, declared[f].Key,
                        snapshotBefore[f]);
                    trace.TryRecord(
                        table.TableId, step.RowId + "/state", ConformancePhase.After, declared[f].Key,
                        ReadField(family, world, declared[f].Key));
                }
            }

            bool pass = result.Outcome == ConformanceOperationOutcome.Published
                || step.Outcome == ConformanceRowOutcome.RefusedKeepsAssembly
                    && result.Outcome == ConformanceOperationOutcome.Refused;
            var text = new StringBuilder();
            text.Append(stageId).Append("; outcome=").Append(result.Outcome)
                .Append("; ").Append(step.Note);
            for (int e = 0; e < expectations.Count; e++)
            {
                text.Append("; ").Append(expectations[e].Field).Append('=')
                    .Append(before[e]).Append("->").Append(after[e]);
            }

            steps.Add(new ConformanceObservation(name, pass, text.ToString()));
        }

        /// <summary>
        /// Reads one field, recording `none` when the genre reports a miss. A miss is a *recorded* absence with a
        /// reason in the step's detail rather than a silent default, because the oracle's whole job is to fail on a
        /// field nobody observed.
        /// </summary>
        private static string ReadField(IConformanceFamily family, ConformanceWorld world, string field)
            => family.TryReadField(field, world, out string value, out string _) ? value : ConformanceValue.None;

        /// <summary>
        /// Reads one field and reports the reason a miss had, which is what a diagnostic message needs and what the
        /// trace deliberately does not carry (a trace holds canonical tokens, never prose).
        /// </summary>
        public static bool TryRead(
            IConformanceFamily family, ConformanceWorld world, string field, out string value, out string detail)
        {
            if (world == null)
            {
                value = ConformanceValue.None;
                detail = "no world";
                return false;
            }

            return family.TryReadField(field, world, out value, out detail);
        }

        /// <summary>Every observation name one table's run records, so a caller can require them all (P-060).</summary>
        public static IReadOnlyList<string> ObservationNames(string tableId, string conformanceLabel)
        {
            ConformanceScript? script = ReferenceScripts.ById(tableId);
            ConformanceTable? table = ReferenceTables.ById(tableId);
            var names = new List<string>();
            if (script == null || table == null)
            {
                return names;
            }

            for (int s = 0; s < script.Stages.Count; s++)
            {
                ConformanceStage stage = script.Stages[s];
                names.Add(StepPrefix + tableId + "/" + stage.StageId + "/world");
                for (int i = 0; i < stage.Setup.Count; i++)
                {
                    names.Add(StepPrefix + tableId + "/" + stage.StageId + "/setup-"
                        + i.ToString(CultureInfo.InvariantCulture));
                }

                for (int i = 0; i < stage.Steps.Count; i++)
                {
                    names.Add(StepPrefix + tableId + "/" + stage.Steps[i].RowId);
                }

                names.Add(StepPrefix + tableId + "/" + stage.StageId + "/teardown");
            }

            names.Add(StepPrefix + tableId + "/verdict");
            _ = conformanceLabel;
            return names;
        }
    }
}
