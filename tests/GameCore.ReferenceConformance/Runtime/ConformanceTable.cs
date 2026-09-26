// GameCore.ReferenceConformance — every before/after table of docs/game-core/07-reference-compositions.md as data.
//
// WHY THIS IS DATA AND NOT PROSE IN A TEST
//
// 07 s2.4, s3.3 and s4.3 each state one table per reference composition: an operation, the last published state
// before it, and the first published state after successful publication. The task that validates them must execute
// *every* row in a real Unity world and compare what the world published against the table, so the table has to be
// a value a runner and an oracle share — not two hand-written expectations that can drift from each other and from
// the document.
//
// Each row therefore carries the 07 anchor it was transcribed from (`TableAnchor` + `SourceRow`) and its
// expectations. An expectation is one canonical field with the value the before phase and the after phase must
// read. `Unchanged` and `Absent` exist because several rows say "remains", "stays" or "is not eligible": spelling
// those as two identical values would lose the distinction between "the operation preserved this" and "this row
// happens to read the same number twice".
//
// The `Before` value of a row is what the *previous* rows of the same table left published, so a table is an
// ordered script: executing row N requires rows 1..N-1 to have published already. That is exactly how 07 reads —
// the tables are cumulative, and the "After" column of one row is the "Before" column of the next whenever both
// name the same field.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>What one expectation demands of a run's two observations of one field.</summary>
    public enum ConformanceExpectationKind
    {
        /// <summary>The before phase must read <c>Before</c> and the after phase <c>After</c>.</summary>
        Require = 0,

        /// <summary>Both phases must read the same value: the operation preserved this state (P-025, P-032).</summary>
        Unchanged = 1,

        /// <summary>Both phases must read <c>none</c>: nothing derives here, in either phase (P-015, P-016).</summary>
        Absent = 2,

        /// <summary>
        /// Both phases must read the same value, but which value is not a 07 literal: the row's claim is "this
        /// state survived", and the run's own before reading is the only honest spelling of it (P-025, P-032).
        /// A row whose before value 07 does not state — a hand's card identities, a pose, a fact version — uses
        /// this, so the oracle checks preservation without inventing a fixture value the document never gave.
        /// </summary>
        Preserved = 3,
    }

    /// <summary>One canonical field of one 07 table row and the two values it must read.</summary>
    public readonly struct ConformanceExpectation
    {
        private ConformanceExpectation(
            string field, ConformanceExpectationKind kind, string? before, string? after)
        {
            Field = field ?? throw new ArgumentNullException(nameof(field));
            Kind = kind;
            Before = before ?? string.Empty;
            After = after ?? string.Empty;
        }

        /// <summary>The canonical field key this expectation is about.</summary>
        public string Field { get; }

        /// <summary>What the expectation demands.</summary>
        public ConformanceExpectationKind Kind { get; }

        /// <summary>The value the before phase must read; empty for <see cref="ConformanceExpectationKind.Absent"/>.</summary>
        public string Before { get; }

        /// <summary>The value the after phase must read; empty for <see cref="ConformanceExpectationKind.Absent"/>.</summary>
        public string After { get; }

        /// <summary>The before phase changes the value: 07's "Before" column names one state, "After" another.</summary>
        public static ConformanceExpectation Require(string field, string before, string after)
            => new ConformanceExpectation(field, ConformanceExpectationKind.Require, before, after);

        /// <summary>The operation preserves this field: 07's "remains", "stays", "survive" (P-025, P-032).</summary>
        public static ConformanceExpectation Unchanged(string field, string value)
            => new ConformanceExpectation(field, ConformanceExpectationKind.Unchanged, value, value);

        /// <summary>The field derives nothing in either phase: 07's "ineligible", "receives none" (P-015, P-016).</summary>
        public static ConformanceExpectation Absent(string field)
            => new ConformanceExpectation(field, ConformanceExpectationKind.Absent, null, null);

        /// <summary>
        /// The operation must leave this field exactly as it read it, without 07 having stated the value: the
        /// oracle compares the run's own two readings (P-025, P-032).
        /// </summary>
        public static ConformanceExpectation Preserved(string field)
            => new ConformanceExpectation(field, ConformanceExpectationKind.Preserved, null, null);

        /// <summary>The two phases must agree; for <see cref="ConformanceExpectationKind.Preserved"/> that is all.</summary>
        public bool RequiresEqualPhases =>
            Kind == ConformanceExpectationKind.Unchanged || Kind == ConformanceExpectationKind.Preserved;

        /// <summary>The value this expectation demands of one phase, or null when only equality is demanded.</summary>
        public string? ExpectedOrNull(ConformancePhase phase)
        {
            if (Kind == ConformanceExpectationKind.Absent)
            {
                return ConformanceValue.None;
            }

            return Kind == ConformanceExpectationKind.Preserved
                ? null
                : (phase == ConformancePhase.Before ? Before : After);
        }

        /// <summary>The value this expectation demands of one phase; a Preserved field demands its before reading.</summary>
        public string Expected(ConformancePhase phase)
            => ExpectedOrNull(phase) ?? Before;

        public override string ToString()
        {
            switch (Kind)
            {
                case ConformanceExpectationKind.Unchanged:
                    return Field + "=" + Before + " (unchanged)";
                case ConformanceExpectationKind.Absent:
                    return Field + "=" + ConformanceValue.None + " (in both phases)";
                default:
                    return Field + ": " + Before + " -> " + After;
            }
        }
    }

    /// <summary>Whether one row's assembly operation may reject, and what the run must then observe (P-014, P-028).</summary>
    public enum ConformanceRowOutcome
    {
        /// <summary>The operation publishes: the after phase is the row's "After successful publication" column.</summary>
        Published = 0,

        /// <summary>
        /// The operation is expected to be refused before any live mutation, and the published state must be
        /// byte-identical to the before phase (07 s2.4: "then leaves the entire old mode and assembly published").
        /// </summary>
        RefusedKeepsAssembly = 1,
    }

    /// <summary>One row of one 07 before/after table: the operation, its anchor, and what the world must publish.</summary>
    public sealed class ConformanceRow
    {
        private readonly List<ConformanceExpectation> expectations = new List<ConformanceExpectation>();

        public ConformanceRow(
            string rowId,
            string operation,
            string sourceRow,
            ConformanceRowOutcome outcome,
            IReadOnlyList<ConformanceExpectation> expectations)
        {
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            SourceRow = sourceRow ?? throw new ArgumentNullException(nameof(sourceRow));
            Outcome = outcome;
            if (expectations != null)
            {
                this.expectations.AddRange(expectations);
            }
        }

        /// <summary>Stable row identity inside its table, e.g. <c>reparent-seat-a</c>.</summary>
        public string RowId { get; }

        /// <summary>The 07 "Operation" cell, verbatim.</summary>
        public string Operation { get; }

        /// <summary>The 07 "Before"/"After successful publication" cells, verbatim and abridged to the observable.</summary>
        public string SourceRow { get; }

        /// <summary>What the operation is expected to do to the published assembly.</summary>
        public ConformanceRowOutcome Outcome { get; }

        /// <summary>Every field this row demands, in canonical field order.</summary>
        public IReadOnlyList<ConformanceExpectation> Expectations => expectations;

        public override string ToString() => RowId + ": " + Operation;
    }

    /// <summary>One 07 before/after table: its anchor, the rows in 07's order, and the fields they observe.</summary>
    public sealed class ConformanceTable
    {
        public ConformanceTable(
            string tableId,
            string title,
            string tableAnchor,
            string genre,
            IReadOnlyList<ConformanceRow> rows)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            TableAnchor = tableAnchor ?? throw new ArgumentNullException(nameof(tableAnchor));
            Genre = genre ?? throw new ArgumentNullException(nameof(genre));
            Rows = rows ?? throw new ArgumentNullException(nameof(rows));
        }

        /// <summary>Stable table identity, e.g. <c>cards</c>.</summary>
        public string TableId { get; }

        /// <summary>The table's title, as 07 heads it.</summary>
        public string Title { get; }

        /// <summary>The exact 07 anchor this table was transcribed from, so every row is traceable (P-060).</summary>
        public string TableAnchor { get; }

        /// <summary>The reference composition this table belongs to, e.g. <c>card-market</c>.</summary>
        public string Genre { get; }

        /// <summary>The rows, in 07's own order; a run executes them in exactly this order.</summary>
        public IReadOnlyList<ConformanceRow> Rows { get; }

        /// <summary>Every field the table observes, in canonical order and without repetition.</summary>
        public IReadOnlyList<string> Fields()
        {
            var fields = new List<string>();
            for (int r = 0; r < Rows.Count; r++)
            {
                IReadOnlyList<ConformanceExpectation> row = Rows[r].Expectations;
                for (int e = 0; e < row.Count; e++)
                {
                    bool present = false;
                    for (int i = 0; i < fields.Count; i++)
                    {
                        if (string.Equals(fields[i], row[e].Field, StringComparison.Ordinal))
                        {
                            present = true;
                            break;
                        }
                    }

                    if (!present)
                    {
                        fields.Add(row[e].Field);
                    }
                }
            }

            fields.Sort(StringComparer.Ordinal);
            return fields;
        }

        public override string ToString() => TableId + " (" + TableAnchor + ", "
            + Rows.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " rows)";
    }
}
