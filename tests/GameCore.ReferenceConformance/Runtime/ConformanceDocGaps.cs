// GameCore.ReferenceConformance — the documentation gaps this revision's conformance run records.
//
// GC-024's definition of done says: "Any exposed generic gap is resolved in 00/05/tests before acceptance". A gap that
// cannot be resolved inside this task has exactly two honest outcomes: name it, with the normative clause it offends
// and the mechanism the revision lacks, or pretend a row passed. This file is the first outcome. It exists so that
//
//   * a run that cannot perform a 07 row reports it as `ConformanceStepStatus.RecordedGap` rather than as a pass —
//     `ConformanceTableResult.AllPassed` stays false while a gap is open, so no gate can report the requirement
//     satisfied;
//   * the gap is a value a test can assert, so a *new* gap cannot appear silently: the pure suite requires every gap
//     identifier the run reports to be declared here, and requires every declaration to name its clause and its
//     evidence;
//   * the reader of this change set sees the whole remaining distance between 07 and this revision in one list.
//
// NOTHING HERE IS A SECOND NORMATIVE SOURCE. The clause strings quote the document; the resolution is a proposal.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>One recorded gap: the 07 row it blocks, the clause it offends, and what the revision lacks.</summary>
    public sealed class ConformanceDocGap
    {
        public ConformanceDocGap(
            string gapId,
            string tableId,
            string rowId,
            string clause,
            string missing,
            string evidence,
            string resolution)
        {
            GapId = gapId ?? throw new ArgumentNullException(nameof(gapId));
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Clause = clause ?? throw new ArgumentNullException(nameof(clause));
            Missing = missing ?? throw new ArgumentNullException(nameof(missing));
            Evidence = evidence ?? throw new ArgumentNullException(nameof(evidence));
            Resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
        }

        /// <summary>Stable identifier the run reports, e.g. <c>gc024.gap.reward-bridge-removal</c>.</summary>
        public string GapId { get; }

        /// <summary>The table whose row this gap blocks.</summary>
        public string TableId { get; }

        /// <summary>The row that cannot be performed.</summary>
        public string RowId { get; }

        /// <summary>The normative sentence or table cell the row serves, quoted from 07.</summary>
        public string Clause { get; }

        /// <summary>The mechanism this revision does not have, stated as the thing that is absent.</summary>
        public string Missing { get; }

        /// <summary>Where the same absence was already recorded, so the gap is not a new discovery.</summary>
        public string Evidence { get; }

        /// <summary>What would close it, as a proposal for the task that owns the mechanism.</summary>
        public string Resolution { get; }

        public override string ToString() => GapId + " (" + TableId + "/" + RowId + "): " + Missing;
    }

    /// <summary>The recorded gaps of this revision, and the lookup a run and its test share.</summary>
    public static class ConformanceDocGaps
    {
        /// <summary>
        /// No gap is recorded for this revision. `07:276`'s four claims are now carried by shipped generic
        /// mechanisms and asserted by four real rows of the `cross` table (`reward-unmount-pending`,
        /// `reward-drain-then-unmount`, `reward-unmount-transfer`, `reward-scoring-unmount-keeps-card`), so the
        /// registry is deliberately empty: a declared gap would be a false statement about the revision, and this
        /// type's `All` list is what a suite asserts the run's own gaps against, in both directions.
        /// </summary>
        public static IReadOnlyList<ConformanceDocGap> All { get; } = Array.Empty<ConformanceDocGap>();

        /// <summary>The gap with this identifier, or null.</summary>
        public static ConformanceDocGap? ById(string gapId)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].GapId, gapId, StringComparison.Ordinal))
                {
                    return All[i];
                }
            }

            return null;
        }

        /// <summary>The gaps that block one table's rows, in declaration order.</summary>
        public static IReadOnlyList<ConformanceDocGap> Of(string tableId)
        {
            var gaps = new List<ConformanceDocGap>();
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i].TableId, tableId, StringComparison.Ordinal))
                {
                    gaps.Add(All[i]);
                }
            }

            return gaps;
        }

        /// <summary>One canonical line per gap, for evidence (P-060).</summary>
        public static IReadOnlyList<string> CanonicalLines()
        {
            var lines = new List<string>(All.Count);
            for (int i = 0; i < All.Count; i++)
            {
                ConformanceDocGap gap = All[i];
                lines.Add(gap.GapId + "|" + gap.TableId + "|" + gap.RowId + "|" + gap.Clause);
            }

            return lines;
        }
    }
}
