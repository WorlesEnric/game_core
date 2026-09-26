// GameCore.ReferenceConformance — one ordered step of a 07 table's execution script.
//
// A 07 table's rows are independent scenarios, not one cumulative run: `07:101` states "A has total 16 after a
// committed set" while `07:104` states "A has bonus +2" again after `07:101` had unmounted the provider. So the
// script that executes a table is a list of *stages*: one freshly built world each, and inside a stage the ordered
// steps whose `Before` columns really are the previous step's `After` column. A step is either one 07 row or a
// precondition that establishes the state a 07 row's `Before` column names (a committed card set, an exclusions
// edit, a second provider mount).
//
// Every step names an *operation key* rather than a payload: the genre's own family maps the key to the payload its
// package declares, so this fixture never invents a composition edit and the same normalized trace is comparable
// across catalogs (P-002, P-042). A precondition's `RowId` is `<row>/pre<n>`, which keeps every recorded fact
// attributable to the 07 row it exists for while leaving the table's own rows exactly the document's rows.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>What one script step is: one 07 row, or the state one 07 row's `Before` column names.</summary>
    public enum ConformanceStepKind
    {
        /// <summary>The step is one row of the 07 table; its expectations are the row's own.</summary>
        Row = 0,

        /// <summary>The step establishes the state the following row's `Before` column names.</summary>
        Precondition = 1,
    }

    /// <summary>
    /// One executable step. <see cref="Operation"/> is the key the genre's family maps to one of its own declared
    /// payloads; <see cref="Operand"/> is the one scalar such an operation sometimes needs (a reconfigured value, a
    /// repeated command count), which keeps the vocabulary small and explicit.
    /// </summary>
    public sealed class ConformanceStep
    {
        public ConformanceStep(
            string rowId,
            ConformanceStepKind kind,
            string operation,
            int operand,
            string note,
            IReadOnlyList<ConformanceExpectation>? expectations = null,
            ConformanceRowOutcome outcome = ConformanceRowOutcome.Published)
        {
            RowId = rowId ?? throw new ArgumentNullException(nameof(rowId));
            Kind = kind;
            Operation = operation ?? throw new ArgumentNullException(nameof(operation));
            Operand = operand;
            Note = note ?? string.Empty;
            Expectations = expectations ?? Array.Empty<ConformanceExpectation>();
            Outcome = outcome;
        }

        /// <summary>The 07 row this step belongs to, or that row's id plus <c>/pre&lt;n&gt;</c> for a precondition.</summary>
        public string RowId { get; }

        /// <summary>Whether this step is the row itself or a precondition for it.</summary>
        public ConformanceStepKind Kind { get; }

        /// <summary>The operation key the family maps to its own declared composition edit or command.</summary>
        public string Operation { get; }

        /// <summary>One scalar an operation needs; 0 for the operations that need none.</summary>
        public int Operand { get; }

        /// <summary>What this step establishes, in one line, so a failing run reports why the step exists.</summary>
        public string Note { get; }

        /// <summary>Every field this step demands of the two phases.</summary>
        public IReadOnlyList<ConformanceExpectation> Expectations { get; }

        /// <summary>Whether this step must publish or must be refused with the old assembly kept.</summary>
        public ConformanceRowOutcome Outcome { get; }

        public override string ToString() => RowId + " [" + Operation
            + (Operand != 0 ? "(" + Operand.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")" : string.Empty)
            + "]";
    }

    /// <summary>One freshly built world and the ordered steps executed in it.</summary>
    public sealed class ConformanceStage
    {
        public ConformanceStage(string stageId, string title, IReadOnlyList<string> setup, IReadOnlyList<ConformanceStep> steps)
        {
            StageId = stageId ?? throw new ArgumentNullException(nameof(stageId));
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Setup = setup ?? Array.Empty<string>();
            Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        }

        /// <summary>Stable stage identity inside its table.</summary>
        public string StageId { get; }

        /// <summary>The 07 sentence or rows this stage exists for.</summary>
        public string Title { get; }

        /// <summary>
        /// Operation keys the world must perform after seeding and before the first step, in order: the declared
        /// targets a stage needs that the family's default seeding does not create (a spawned seat, a seeded
        /// opted-in target).
        /// </summary>
        public IReadOnlyList<string> Setup { get; }

        /// <summary>The steps, in execution order.</summary>
        public IReadOnlyList<ConformanceStep> Steps { get; }
    }

    /// <summary>One table's whole script: its stages, in the order a run executes them.</summary>
    public sealed class ConformanceScript
    {
        public ConformanceScript(string tableId, IReadOnlyList<ConformanceStage> stages)
        {
            TableId = tableId ?? throw new ArgumentNullException(nameof(tableId));
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
        }

        /// <summary>The 07 table this script executes.</summary>
        public string TableId { get; }

        /// <summary>The stages, in execution order.</summary>
        public IReadOnlyList<ConformanceStage> Stages { get; }

        /// <summary>Every step of every stage, in execution order.</summary>
        public IReadOnlyList<ConformanceStep> Steps()
        {
            var steps = new List<ConformanceStep>();
            for (int s = 0; s < Stages.Count; s++)
            {
                for (int i = 0; i < Stages[s].Steps.Count; i++)
                {
                    steps.Add(Stages[s].Steps[i]);
                }
            }

            return steps;
        }

        /// <summary>
        /// Every operation key this script names, in first-use order. A family answers for exactly these keys, and a
        /// key the script names but the family cannot perform is a reported failure rather than a silent skip.
        /// </summary>
        public IReadOnlyList<string> Operations()
        {
            var operations = new List<string>();
            for (int s = 0; s < Stages.Count; s++)
            {
                for (int i = 0; i < Stages[s].Setup.Count; i++)
                {
                    if (!operations.Contains(Stages[s].Setup[i]))
                    {
                        operations.Add(Stages[s].Setup[i]);
                    }
                }

                for (int i = 0; i < Stages[s].Steps.Count; i++)
                {
                    if (!operations.Contains(Stages[s].Steps[i].Operation))
                    {
                        operations.Add(Stages[s].Steps[i].Operation);
                    }
                }
            }

            return operations;
        }
    }

    /// <summary>The stable operation keys every reference composition's script uses (P-042, P-002).</summary>
    public static class ConformanceOperations
    {
        /// <summary>O-03: mount the family's first declared provider at its own scope.</summary>
        public const string MountProvider = "mount-provider";

        /// <summary>O-03: mount the family's second declared provider at its sibling branch.</summary>
        public const string MountSecondProvider = "mount-second-provider";

        /// <summary>O-03: mount the family's secondary provider (the nested festival, the headwind).</summary>
        public const string MountNestedProvider = "mount-nested-provider";

        /// <summary>O-03: mount the first provider of the family's overlapping exclusive pair.</summary>
        public const string MountConflictProvider = "mount-conflict-provider";

        /// <summary>O-03: mount the second provider of that pair.</summary>
        public const string MountConflictSecondProvider = "mount-conflict-second-provider";

        /// <summary>O-07: unmount the family's first declared provider, disposing its derived contribution.</summary>
        public const string UnmountProvider = "unmount-provider";

        /// <summary>O-07: unmount the family's second declared provider.</summary>
        public const string UnmountSecondProvider = "unmount-second-provider";

        /// <summary>O-05: reconfigure the family's first provider to this step's operand value.</summary>
        public const string ReconfigureProvider = "reconfigure-provider";

        /// <summary>O-02: move the family's declared branch under its declared new parent.</summary>
        public const string ReparentMovedScope = "reparent-moved-scope";

        /// <summary>O-08: switch this world's propagation mode to `Automatic`.</summary>
        public const string ModeAutomatic = "mode-automatic";

        /// <summary>O-08: switch this world's propagation mode to `Conservative`.</summary>
        public const string ModeConservative = "mode-conservative";

        /// <summary>O-06/O-04: suspend, then resume, the family's first provider (P-046).</summary>
        public const string SuspendProvider = "suspend-provider";

        /// <summary>O-04: resume the suspended provider.</summary>
        public const string ResumeProvider = "resume-provider";

        /// <summary>P-024: publish one spawn of the family's declared future target, fully assembled.</summary>
        public const string SpawnFutureTarget = "spawn-future-target";


        /// <summary>
        /// 07 s5: mount the combined world's reward bridge provider, so its outbox obligations are the durable
        /// seam the narrative-to-card combination crosses (P-045).
        /// </summary>
        public const string MountRewardBridge = "mount-reward-bridge";

        /// <summary>07 s5: unmount the reward bridge while its outbox may still hold pending work (REF-X02).</summary>
        public const string UnmountRewardBridge = "unmount-reward-bridge";

        /// <summary>07 s5 step 3-4: dispatch the admitted reward and acknowledge it at the destination.</summary>
        public const string SettleReward = "settle-reward";

        /// <summary>07 s5, P-045: hand the same obligation to the destination a second time.</summary>
        public const string RedeliverReward = "redeliver-reward";

        /// <summary>P-016: apply the family's declared exclusion to its declared excluded target and republish.</summary>
        public const string ApplyExclusion = "apply-exclusion";

        /// <summary>Submit this step's `operand` copies of the family's own declared command.</summary>
        public const string CommitCommand = "commit-command";

        /// <summary>Submit the family's own declared command, expecting it to be refused with no live write.</summary>
        public const string CommitCommandRejected = "commit-command-rejected";
    }
}
