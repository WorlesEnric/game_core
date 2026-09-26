// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - outbox consistency (GC-027).
//
// Normative sources: 00 P-045 ("subscriber delivery is at-least-once within retention, using `(WorldId, event
// sequence)` deduplication... Irreversible output adapters consume only committed events, use explicit external
// idempotency keys, and persist an outbox when delivery must survive crashes"), P-053 (a checkpoint carries
// "external outbox/dedup cursors when used") and P-049 ("No hidden automatic replay of external side effects is
// allowed").
//
// WHAT THIS FILE IS
//
// The report a crash/restart recovery is judged by. GC-021 ships the outbox, its durable adapter and its checkpoint
// rows; GC-018 ships the planner that validates a checkpoint's outbox section; this task has to answer the third
// question, which is the one an operator actually asks: *after this recovery, is the delivery obligation set of the
// new session exactly the one the checkpoint carried, is its cursor still where the source left it, and did anything
// get delivered or settled that was not recorded?*
//
// It is a pure census over rows plus a live outbox. It never delivers, never settles and never mutates: a report
// that moved an obligation would be the hidden replay P-049 forbids.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Delivery;

namespace GameCore.Execution.Recovery
{
    /// <summary>
    /// One destination's delivery position as a report records it: how far acknowledgements reached, how many
    /// terminal rows the destination keeps, and how many retention already pruned (P-045, P-043's "no silent drop").
    /// </summary>
    public readonly struct OutboxCursorRow
    {
        public OutboxCursorRow(Id128 destinationId, Id128 newestAcknowledged, uint retainedTerminals, uint terminalTotal)
        {
            DestinationId = destinationId;
            NewestAcknowledged = newestAcknowledged;
            RetainedTerminals = retainedTerminals;
            TerminalTotal = terminalTotal;
        }

        public Id128 DestinationId { get; }

        /// <summary>The newest acknowledged obligation identity; default when the destination acknowledged nothing.</summary>
        public Id128 NewestAcknowledged { get; }

        /// <summary>Terminal rows this destination still carries.</summary>
        public uint RetainedTerminals { get; }

        /// <summary>Every terminal outcome this destination ever produced, pruned rows included.</summary>
        public uint TerminalTotal { get; }

        /// <summary>Terminal rows retention removed; recorded rather than derived, so it cannot hide (P-043).</summary>
        public uint Pruned => TerminalTotal - RetainedTerminals;

        public bool HasAcknowledged => !NewestAcknowledged.IsDefault;

        public override string ToString() =>
            "cursor(" + DestinationId.ToString() + ",ack="
            + (HasAcknowledged ? NewestAcknowledged.ToString() : "<none>") + ",retained="
            + RetainedTerminals.ToString(CultureInfo.InvariantCulture) + ",total="
            + TerminalTotal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The outbox consistency report of one recovery: what the checkpoint carried, what the recovered session holds,
    /// and every disagreement between the two. `Consistent` is the report's verdict; `Problems` names each
    /// discrepancy so a failure is actionable rather than a bare false (P-052).
    /// </summary>
    public sealed class OutboxConsistencyReport
    {
        private readonly List<string> problems = new List<string>();

        internal OutboxConsistencyReport(string label)
        {
            Label = label ?? string.Empty;
        }

        /// <summary>Which world or session this report describes, for an evidence file (P-004).</summary>
        public string Label { get; }

        /// <summary>Rows the checkpoint carries, i.e. what `OutboxRecordValue` projected.</summary>
        public int CarriedRows { get; internal set; }

        /// <summary>Obligation rows among them.</summary>
        public int CarriedObligations { get; internal set; }

        /// <summary>Terminal rows among them.</summary>
        public int CarriedTerminals { get; internal set; }

        /// <summary>Cursor rows among them.</summary>
        public int CarriedCursors { get; internal set; }

        /// <summary>Obligation rows still open (`Pending` or `Delivered`): work the destination still owes (P-045).</summary>
        public int CarriedOpenObligations { get; internal set; }

        /// <summary>Obligations the live outbox tracks, or -1 when no outbox was supplied.</summary>
        public int LiveTrackedCount { get; internal set; } = -1;

        /// <summary>Obligations the live outbox still owes, or -1 when no outbox was supplied.</summary>
        public int LiveOpenCount { get; internal set; } = -1;

        /// <summary>Terminal obligations the live outbox retains, or -1 when no outbox was supplied.</summary>
        public int LiveTerminalCount { get; internal set; } = -1;

        /// <summary>Terminal rows retention pruned in the live outbox, or -1 when no outbox was supplied.</summary>
        public int LivePrunedCount { get; internal set; } = -1;

        /// <summary>Terminal rows the live outbox adopted while rebuilding from the checkpoint rows.</summary>
        public int LiveTerminalAdoptionCount { get; internal set; } = -1;

        /// <summary>Destinations the rows carry a cursor for, in ascending stable-id order (P-008).</summary>
        public IReadOnlyList<OutboxCursorRow> Cursors { get; internal set; } = Array.Empty<OutboxCursorRow>();

        /// <summary>Every disagreement found, in discovery order; empty when the report is consistent.</summary>
        public IReadOnlyList<string> Problems => problems;

        /// <summary>True when nothing disagreed: the recovered obligation set and cursor match the checkpoint's.</summary>
        public bool Consistent => problems.Count == 0;

        internal void Problem(string detail)
        {
            if (!string.IsNullOrEmpty(detail))
            {
                problems.Add(detail);
            }
        }

        /// <summary>Canonical multi-line form for an evidence file: the counts, then one line per cursor.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("outbox-consistency label=").Append(Label)
                .Append(" carried=").Append(CarriedRows.ToString(CultureInfo.InvariantCulture))
                .Append("(open=").Append(CarriedOpenObligations.ToString(CultureInfo.InvariantCulture))
                .Append(",terminal=").Append(CarriedTerminals.ToString(CultureInfo.InvariantCulture))
                .Append(",cursor=").Append(CarriedCursors.ToString(CultureInfo.InvariantCulture))
                .Append(") live=(").Append(LiveTrackedCount.ToString(CultureInfo.InvariantCulture))
                .Append("/").Append(LiveOpenCount.ToString(CultureInfo.InvariantCulture))
                .Append("/").Append(LiveTerminalCount.ToString(CultureInfo.InvariantCulture))
                .Append("/pruned=").Append(LivePrunedCount.ToString(CultureInfo.InvariantCulture))
                .Append("/adopted=").Append(LiveTerminalAdoptionCount.ToString(CultureInfo.InvariantCulture))
                .Append(") cursors=").Append(Cursors.Count.ToString(CultureInfo.InvariantCulture))
                .Append(" consistent=").Append(Consistent ? "1" : "0");
            for (int i = 0; i < Cursors.Count; i++)
            {
                text.Append('\n').Append(Cursors[i].ToString());
            }

            for (int i = 0; i < problems.Count; i++)
            {
                text.Append('\n').Append("problem=").Append(problems[i]);
            }

            return text.ToString();
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Builds the consistency report of one recovery. The checks are the ones a delivered obligation's survival
    /// depends on, and each is a count or a named row rather than a heuristic.
    /// </summary>
    public static class OutboxConsistency
    {
        /// <summary>
        /// Verifies the checkpoint's outbox rows on their own, then - when the recovered session's outbox is
        /// supplied - verifies that the live outbox holds exactly those rows and the same cursors (P-045, P-053).
        /// Nothing here mutates the outbox: a report is a census.
        /// </summary>
        public static OutboxConsistencyReport Verify(
            IReadOnlyList<OutboxRecordValue>? rows,
            DurableOutbox? live,
            string label)
        {
            var report = new OutboxConsistencyReport(label);
            IReadOnlyList<OutboxRecordValue> safeRows = rows ?? Array.Empty<OutboxRecordValue>();
            report.CarriedRows = safeRows.Count;

            var obligations = new Dictionary<Id128, OutboxRecordValue>();
            var terminalIds = new HashSet<Id128>();
            var terminalPerDestination = new Dictionary<Id128, uint>();
            var cursorRows = new List<OutboxRecordValue>();
            var orphanTerminals = new List<Id128>();
            var duplicates = new List<Id128>();

            for (int i = 0; i < safeRows.Count; i++)
            {
                OutboxRecordValue row = safeRows[i];
                if (row.RecordVersion != OutboxRecordValue.CurrentRecordVersion)
                {
                    report.Problem("row " + i.ToString(CultureInfo.InvariantCulture) + " declares record version "
                        + row.RecordVersion.ToString(CultureInfo.InvariantCulture) + " and this build implements "
                        + OutboxRecordValue.CurrentRecordVersion.ToString(CultureInfo.InvariantCulture)
                        + " (P-054).");
                    continue;
                }

                switch (row.Row)
                {
                    case OutboxRowKind.Cursor:
                        cursorRows.Add(row);
                        report.CarriedCursors++;
                        continue;

                    case OutboxRowKind.Obligation:
                        if (!obligations.ContainsKey(row.OutboxId))
                        {
                            obligations.Add(row.OutboxId, row);
                            report.CarriedObligations++;
                            if (row.IsOpen)
                            {
                                report.CarriedOpenObligations++;
                            }
                        }
                        else if (row.State != OutboxDeliveryState.Pending
                            && row.State != OutboxDeliveryState.Delivered)
                        {
                            // The same identity may legitimately appear once per state transition in a journal frame
                            // set, but a *checkpoint projection* emits one obligation row per identity; a repeated
                            // one is a defect the report names (P-053).
                            duplicates.Add(row.OutboxId);
                        }

                        continue;

                    case OutboxRowKind.Terminal:
                        if (!obligations.ContainsKey(row.OutboxId))
                        {
                            orphanTerminals.Add(row.OutboxId);
                            continue;
                        }

                        terminalIds.Add(row.OutboxId);
                        report.CarriedTerminals++;
                        terminalPerDestination.TryGetValue(row.DestinationId, out uint count);
                        terminalPerDestination[row.DestinationId] = count + 1U;
                        continue;

                    default:
                        report.Problem("row " + i.ToString(CultureInfo.InvariantCulture) + " declares row kind "
                            + row.RowKind.ToString(CultureInfo.InvariantCulture)
                            + ", which names no outbox fact this build implements (P-054).");
                        continue;
                }
            }

            for (int i = 0; i < duplicates.Count; i++)
            {
                report.Problem("obligation " + duplicates[i].ToString()
                    + " appears more than once as an obligation row (P-053).");
            }

            for (int i = 0; i < orphanTerminals.Count; i++)
            {
                report.Problem("terminal row " + orphanTerminals[i].ToString()
                    + " closes an obligation the checkpoint does not carry (P-053).");
            }

            var cursors = new List<OutboxCursorRow>(cursorRows.Count);
            var seenDestinations = new HashSet<Id128>();
            for (int i = 0; i < cursorRows.Count; i++)
            {
                OutboxRecordValue row = cursorRows[i];
                if (!seenDestinations.Add(row.DestinationId))
                {
                    report.Problem("destination " + row.DestinationId.ToString()
                        + " carries more than one cursor row; one destination has one cursor (P-008).");
                    continue;
                }

                terminalPerDestination.TryGetValue(row.DestinationId, out uint terminalRows);
                uint declared = row.RetainedTerminalCount;
                if (declared != terminalRows)
                {
                    report.Problem("destination " + row.DestinationId.ToString() + " declares "
                        + declared.ToString(CultureInfo.InvariantCulture) + " retained terminal rows and the "
                        + "checkpoint carries " + terminalRows.ToString(CultureInfo.InvariantCulture)
                        + " (P-045, P-053).");
                }

                if (!row.Cursor.IsDefault)
                {
                    // A cursor that acknowledges an identity the checkpoint does not carry as a terminal row would
                    // claim an acknowledgement nothing recorded, which is exactly the "hidden state" P-049 excludes.
                    if (!terminalIds.Contains(row.Cursor))
                    {
                        report.Problem("destination " + row.DestinationId.ToString()
                            + " acknowledges obligation " + row.Cursor.ToString()
                            + " and the checkpoint carries no terminal row for it (P-045).");
                    }
                }

                cursors.Add(new OutboxCursorRow(
                    row.DestinationId,
                    row.Cursor,
                    row.RetainedTerminalCount,
                    row.TerminalTotal));
            }

            cursors.Sort(CompareCursors);
            report.Cursors = cursors;

            if (live != null)
            {
                report.LiveTrackedCount = live.Count;
                report.LiveOpenCount = live.OpenCount;
                report.LiveTerminalCount = live.TerminalCount;
                report.LivePrunedCount = live.PrunedTerminalCount;
                report.LiveTerminalAdoptionCount = live.TerminalAdoptionCount;

                if (live.OpenCount != report.CarriedOpenObligations)
                {
                    report.Problem("the recovered outbox owes " + live.OpenCount.ToString(CultureInfo.InvariantCulture)
                        + " open obligations and the checkpoint carried "
                        + report.CarriedOpenObligations.ToString(CultureInfo.InvariantCulture)
                        + " (P-045, P-053).");
                }

                if (live.TerminalCount != report.CarriedTerminals)
                {
                    report.Problem("the recovered outbox retains "
                        + live.TerminalCount.ToString(CultureInfo.InvariantCulture)
                        + " terminal obligations and the checkpoint carried "
                        + report.CarriedTerminals.ToString(CultureInfo.InvariantCulture) + " (P-045, P-053).");
                }

                IReadOnlyList<Id128> destinations = live.DestinationIds;
                if (destinations.Count != cursors.Count)
                {
                    report.Problem("the recovered outbox has " + destinations.Count.ToString(CultureInfo.InvariantCulture)
                        + " destinations and the checkpoint carried "
                        + cursors.Count.ToString(CultureInfo.InvariantCulture) + " cursors (P-045).");
                }
                else
                {
                    for (int i = 0; i < destinations.Count; i++)
                    {
                        if (!live.TryGetCursor(destinations[i], out DeliveryCursor cursor))
                        {
                            report.Problem("the recovered outbox reports destination " + destinations[i].ToString()
                                + " without a cursor (P-045).");
                            continue;
                        }

                        int carriedIndex = IndexOfCursor(cursors, destinations[i]);
                        if (carriedIndex < 0)
                        {
                            report.Problem("the recovered outbox holds a cursor for destination "
                                + destinations[i].ToString() + " that the checkpoint did not carry (P-045).");
                            continue;
                        }

                        OutboxCursorRow carried = cursors[carriedIndex];
                        if (!cursor.NewestAcknowledged.Equals(carried.NewestAcknowledged))
                        {
                            report.Problem("destination " + destinations[i].ToString()
                                + " acknowledges " + DescribeId(cursor.NewestAcknowledged)
                                + " in the recovered outbox and " + DescribeId(carried.NewestAcknowledged)
                                + " in the checkpoint; the acknowledgement position moved (P-045).");
                        }

                        if (cursor.TerminalTotal != carried.TerminalTotal)
                        {
                            report.Problem("destination " + destinations[i].ToString() + " records "
                                + cursor.TerminalTotal.ToString(CultureInfo.InvariantCulture)
                                + " terminal outcomes in the recovered outbox and "
                                + carried.TerminalTotal.ToString(CultureInfo.InvariantCulture)
                                + " in the checkpoint (P-045).");
                        }
                    }
                }
            }

            return report;
        }

        private static int IndexOfCursor(IReadOnlyList<OutboxCursorRow> cursors, Id128 destination)
        {
            for (int i = 0; i < cursors.Count; i++)
            {
                if (cursors[i].DestinationId.Equals(destination))
                {
                    return i;
                }
            }

            return -1;
        }

        private static int CompareCursors(OutboxCursorRow left, OutboxCursorRow right) =>
            left.DestinationId.CompareTo(right.DestinationId);

        private static string DescribeId(Id128 id) => id.IsDefault ? "<none>" : id.ToString();
    }
}
