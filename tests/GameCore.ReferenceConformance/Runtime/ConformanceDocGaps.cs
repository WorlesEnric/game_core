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
        /// `07:276`: *"`NarrativeCardRewards` declares `PreserveDormant` for its completed outbox, with a
        /// scratch-migration precondition that no pending work remains; alternatively an explicitly selected
        /// compatible `TransferTo` owner may take the outbox. Unmounting with pending work therefore rejects until it
        /// drains or transfers."*
        ///
        /// The row is in the transcribed table and in the script, so the *attempt* is part of the run; what this
        /// revision cannot do is perform it, because the bridge is an ordinary caller-owned object rather than a
        /// mounted installation: there is no installation to unmount and therefore no place for the declared
        /// `PreserveDormant` policy to live. `artifacts/gc-021/HANDOFF.md` §7 item 5 recorded exactly this absence
        /// when GC-021 shipped the bridge.
        /// </summary>
        public const string RewardBridgeRemovalId = "gc024.gap.reward-bridge-removal";

        /// <summary>Every gap this revision records.</summary>
        public static IReadOnlyList<ConformanceDocGap> All { get; } = new[]
        {
            new ConformanceDocGap(
                RewardBridgeRemovalId,
                "cross",
                "reward-bridge-removal",
                "07:276 (\"Unmounting with pending work therefore rejects until it drains or transfers\") and 00 P-032's"
                + " last-support-loss policy set (\"Last-support loss MUST declare one of: RemoveDerived,"
                + " PreserveDormant, or TransferTo a named available owner\")",
                "a mounted installation for the reward bridge: `NarrativeCardRewards` is constructed as an ordinary"
                + " caller-owned object (the bridge owns its outbox and its destination port directly), so this"
                + " revision has no installation identity, no plugin manifest and therefore no slot policy to declare"
                + " or to enforce on removal",
                "artifacts/gc-021/HANDOFF.md §7 item 5 (\"PRESERVE for the completed outbox is not declared ... the"
                + " declaration has nowhere to live until a task mounts the bridge as a plugin\")",
                "mount the bridge as a declared plugin instance in the combination's own package (a manifest with the"
                + " bridge's state slots, an O-03 mount payload and the PreserveDormant policy on its last-support"
                + " loss), which makes the unmount attempt a lane operation this row can then observe; the row's"
                + " expectations need no change, since 07 states them in terms of the installation's policy rather"
                + " than of any API"),
        };

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
