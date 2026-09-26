// GameCore.ReferenceConformance — the oracle that compares one run's normalized trace against a transcribed 07 table.
//
// The oracle is the only thing that decides whether a conformance run passed, and it decides it field by field:
//
//   * every 07 row must have been executed (a row the run never reached is a missing observation, not a pass);
//   * every expectation of every row must be satisfied by the two values the run recorded for that field;
//   * a `RefusedKeepsAssembly` row additionally requires that the run reported the operation refused, and that every
//     field the row observes reads identically in both phases (P-014, P-028);
//   * a field the run recorded but no expectation mentions is *not* an error: the trace is meant to carry more than
//     the assertions, so a reviewer can see the state the assertions were made against (P-026);
//   * a field an expectation mentions but the run never recorded is an error: the run did not observe what the table
//     requires, and guessing it would be fabricating evidence.
//
// The verdict is data, not prose: a caller reports the failures it lists and digests the whole verdict, which is what
// makes one run comparable with another across catalogs and across hosts (P-008, TEST-022).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Rules.Narrative;

namespace GameCore.ReferenceConformance
{
    /// <summary>One unsatisfied requirement of one row: what the table demanded and what the run recorded.</summary>
    public readonly struct ConformanceFailure
    {
        public ConformanceFailure(string tableId, string rowId, string field, string expected, string observed)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Field = field ?? string.Empty;
            Expected = expected ?? string.Empty;
            Observed = observed ?? string.Empty;
        }

        /// <summary>The table the failure belongs to.</summary>
        public string TableId { get; }

        /// <summary>The 07 row (or precondition row) the failure belongs to.</summary>
        public string RowId { get; }

        /// <summary>The canonical field the failure is about; empty for a whole-row failure.</summary>
        public string Field { get; }

        /// <summary>What the table demanded.</summary>
        public string Expected { get; }

        /// <summary>What the run recorded.</summary>
        public string Observed { get; }

        public override string ToString() => TableId + "/" + RowId
            + (Field.Length == 0 ? string.Empty : "/" + Field)
            + ": expected " + Expected + ", observed " + Observed;
    }

    /// <summary>The verdict of one table's comparison, plus the digest a caller records as evidence.</summary>
    public sealed class ConformanceVerdict
    {
        private readonly List<ConformanceFailure> failures = new List<ConformanceFailure>();

        public ConformanceVerdict(string tableId, int rowsChecked, int fieldsChecked)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            RowsChecked = rowsChecked;
            FieldsChecked = fieldsChecked;
        }

        /// <summary>The table this verdict is about.</summary>
        public string TableId { get; }

        /// <summary>How many rows of the table (and its preconditions) were compared.</summary>
        public int RowsChecked { get; }

        /// <summary>How many recorded values were compared against an expectation.</summary>
        public int FieldsChecked { get; }

        /// <summary>Every unsatisfied requirement, in the order it was found.</summary>
        public IReadOnlyList<ConformanceFailure> Failures => failures;

        /// <summary>True when every row was executed and every expectation was satisfied.</summary>
        public bool Passed => failures.Count == 0 && RowsChecked > 0;

        /// <summary>Records one unsatisfied requirement.</summary>
        public void Fail(string rowId, string field, string expected, string observed)
            => failures.Add(new ConformanceFailure(TableId, rowId, field, expected, observed));

        /// <summary>Appends another verdict's failures, so a script's verdict can join the table's.</summary>
        internal void Adopt(IReadOnlyList<ConformanceFailure> other)
        {
            for (int i = 0; i < other.Count; i++)
            {
                failures.Add(other[i]);
            }
        }

        /// <summary>
        /// The digest of this verdict: the canonical `table|row|field|expected|observed` lines in the order they were
        /// found, hashed with the same function every other GameCore recording uses. Two runs of the same table that
        /// disagree produce different digests; a run that agrees once and again produces the same one (P-008).
        /// </summary>
        public string Digest()
        {
            var lines = new List<string>(failures.Count);
            for (int i = 0; i < failures.Count; i++)
            {
                ConformanceFailure failure = failures[i];
                lines.Add(failure.TableId + "|" + failure.RowId + "|" + failure.Field
                    + "|" + failure.Expected + "|" + failure.Observed);
            }

            return NarrativeDigest.OfLines(lines);
        }

        /// <summary>One line naming the counts and the first few failures, for a report or a log.</summary>
        public string Describe()
        {
            if (Passed)
            {
                return TableId + ": passed " + RowsChecked.ToString(CultureInfo.InvariantCulture)
                    + " row(s) over " + FieldsChecked.ToString(CultureInfo.InvariantCulture)
                    + " observation(s); digest=" + Digest();
            }

            var text = new StringBuilder();
            text.Append(TableId).Append(": failed ")
                .Append(failures.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" of ").Append(RowsChecked.ToString(CultureInfo.InvariantCulture))
                .Append(" row(s) (").Append(FieldsChecked.ToString(CultureInfo.InvariantCulture))
                .Append(" observation(s)); digest=").Append(Digest());
            int shown = failures.Count < 8 ? failures.Count : 8;
            for (int i = 0; i < shown; i++)
            {
                text.Append("; ").Append(failures[i].ToString());
            }

            if (failures.Count > shown)
            {
                text.Append("; (+").Append((failures.Count - shown).ToString(CultureInfo.InvariantCulture))
                    .Append(" more)");
            }

            return text.ToString();
        }

        public override string ToString() => Describe();
    }

    /// <summary>The oracle: compare a run's trace against the transcribed table it claims to have executed.</summary>
    public static class ConformanceOracle
    {
        /// <summary>
        /// One step's outcome as the run reported it: whether the operation published, and, when it did not, the
        /// diagnostic the run recorded. The runner supplies this from its own operation results, so the refusal half
        /// of a `RefusedKeepsAssembly` step is a real observed outcome rather than an assumption.
        /// </summary>
        public readonly struct RowOutcomeReport
        {
            public RowOutcomeReport(string rowId, bool published, string refusal)
            {
                RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
                Published = published;
                Refusal = refusal ?? string.Empty;
            }

            /// <summary>The step this outcome belongs to: a 07 row id or a precondition row id.</summary>
            public string RowId { get; }

            /// <summary>True when the world published the operation's assembly.</summary>
            public bool Published { get; }

            /// <summary>The refusal's diagnostic text, empty when the operation published.</summary>
            public string Refusal { get; }
        }

        /// <summary>
        /// Compares one table's expectations against one run's trace. A row the run never reported is a failure,
        /// because a row that was not executed cannot be claimed as observed.
        /// </summary>
        public static ConformanceVerdict Compare(
            ConformanceTable table,
            ConformanceTrace trace,
            IReadOnlyList<RowOutcomeReport> outcomes)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            var verdict = new ConformanceVerdict(table.TableId, 0, 0);
            int rowsChecked = 0;
            int fieldsChecked = 0;
            for (int r = 0; r < table.Rows.Count; r++)
            {
                ConformanceRow row = table.Rows[r];
                RowOutcomeReport? outcome = Find(outcomes, row.RowId);
                if (outcome == null)
                {
                    verdict.Fail(row.RowId, string.Empty, "the row to be executed", "not executed");
                    continue;
                }

                rowsChecked++;
                RowOutcomeReport reported = outcome.Value;
                if (row.Outcome == ConformanceRowOutcome.RefusedKeepsAssembly && reported.Published)
                {
                    verdict.Fail(
                        row.RowId,
                        string.Empty,
                        "the operation to be refused",
                        "published" + (reported.Refusal.Length == 0 ? string.Empty : " (" + reported.Refusal + ")"));
                    continue;
                }

                if (row.Outcome == ConformanceRowOutcome.Published && !reported.Published)
                {
                    verdict.Fail(
                        row.RowId, string.Empty, "the operation to publish", "refused (" + reported.Refusal + ")");
                    continue;
                }

                fieldsChecked += Compare(
                    verdict, table.TableId, row.RowId, row.Expectations, trace);
            }

            return new ConformanceVerdict(table.TableId, rowsChecked, fieldsChecked);
        }

        /// <summary>
        /// Compares one table against a whole script's trace: the table's own rows plus, for each precondition step,
        /// the expectations that step declared. A precondition that carries no expectations still has to be reported
        /// as executed, because its operation is what the following row's before state depends on.
        /// </summary>
        public static ConformanceVerdict CompareScript(
            ConformanceScript script,
            ConformanceTrace trace,
            IReadOnlyList<RowOutcomeReport> outcomes)
        {
            if (script == null)
            {
                throw new ArgumentNullException(nameof(script));
            }

            ConformanceTable? table = ReferenceTables.ById(script.TableId);
            if (table == null)
            {
                throw new InvalidOperationException(
                    "the script names the table '" + script.TableId + "', which no transcribed 07 table carries.");
            }

            ConformanceVerdict tableVerdict = Compare(table, trace, outcomes);
            var verdict = new ConformanceVerdict(script.TableId, tableVerdict.RowsChecked, tableVerdict.FieldsChecked);
            verdict.Adopt(tableVerdict.Failures);

            IReadOnlyList<ConformanceStep> steps = script.Steps();
            int rowsChecked = verdict.RowsChecked;
            int fieldsChecked = verdict.FieldsChecked;
            for (int s = 0; s < steps.Count; s++)
            {
                ConformanceStep step = steps[s];
                if (step.Kind != ConformanceStepKind.Precondition)
                {
                    continue;
                }

                RowOutcomeReport? outcome = Find(outcomes, step.RowId);
                if (outcome == null)
                {
                    verdict.Fail(step.RowId, string.Empty, "the precondition to be executed", "not executed");
                    continue;
                }

                if (step.Outcome == ConformanceRowOutcome.RefusedKeepsAssembly && outcome.Value.Published)
                {
                    verdict.Fail(step.RowId, string.Empty, "the operation to be refused", "published");
                }

                rowsChecked++;
                fieldsChecked += Compare(verdict, script.TableId, step.RowId, step.Expectations, trace);
            }

            var merged = new ConformanceVerdict(script.TableId, rowsChecked, fieldsChecked);
            merged.Adopt(verdict.Failures);
            return merged;
        }

        /// <summary>
        /// Compares one row's expectations against the trace and records every failure on the verdict. Returns how
        /// many values were compared, so the caller can report the size of the comparison rather than of the table.
        /// </summary>
        private static int Compare(
            ConformanceVerdict verdict,
            string tableId,
            string rowId,
            IReadOnlyList<ConformanceExpectation> expectations,
            ConformanceTrace trace)
        {
            int compared = 0;
            for (int e = 0; e < expectations.Count; e++)
            {
                ConformanceExpectation expectation = expectations[e];
                string? before = trace.ValueOf(tableId, rowId, ConformancePhase.Before, expectation.Field);
                string? after = trace.ValueOf(tableId, rowId, ConformancePhase.After, expectation.Field);
                compared += 2;

                if (before == null || after == null)
                {
                    verdict.Fail(
                        rowId,
                        expectation.Field,
                        expectation.Expected(ConformancePhase.Before) + " -> " + expectation.Expected(ConformancePhase.After),
                        before == null && after == null
                            ? "not observed"
                            : (before == null ? "no before reading" : "no after reading"));
                    continue;
                }

                string? demandedBefore = expectation.ExpectedOrNull(ConformancePhase.Before);
                if (demandedBefore != null && !string.Equals(before, demandedBefore, StringComparison.Ordinal))
                {
                    verdict.Fail(rowId, expectation.Field, demandedBefore, before);
                }

                string demandedAfter = expectation.RequiresEqualPhases
                    ? before
                    : expectation.Expected(ConformancePhase.After);
                if (!string.Equals(after, demandedAfter, StringComparison.Ordinal))
                {
                    verdict.Fail(rowId, expectation.Field, demandedAfter, after);
                }
            }

            return compared;
        }

        private static RowOutcomeReport? Find(IReadOnlyList<RowOutcomeReport> outcomes, string rowId)
        {
            if (outcomes == null)
            {
                return null;
            }

            for (int i = 0; i < outcomes.Count; i++)
            {
                if (string.Equals(outcomes[i].RowId, rowId, StringComparison.Ordinal))
                {
                    return outcomes[i];
                }
            }

            return null;
        }
    }
}
