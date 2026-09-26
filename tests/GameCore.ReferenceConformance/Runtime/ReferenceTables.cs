// GameCore.ReferenceConformance — the four before/after tables of GC-024, transcribed from 07.
//
// Every row below carries the exact 07 anchor it was transcribed from. Two kinds of row appear, and they are kept
// visually distinct because they carry different authority:
//
//   * rows taken from a 07 *table* — "Before" and "After successful publication" are both the document's;
//   * rows taken from 07 *prose* inside the same section — the document states the before/after observables in a
//     sentence rather than a table cell (`07:51`'s nested-festival retraction, `07:108`'s refused mode switch,
//     `07:110`'s dormant table executor, `07:247`'s numeric acceleration sequence, `07:276`'s bridge removal with
//     pending work). The task requires every one of those observables, so they are rows too, with `SourceRow`
//     naming the sentence.
//
// A value a 07 cell states as illustrative rather than normative — a hand's card identities, a fixture's first
// three card values — is transcribed as `Preserved`: the document's claim there is "this survives the operation",
// and inventing the fixture's identity as if 07 had stated it would be fabricating an expectation.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>The 07 anchors every transcribed row names, so evidence is traceable to the document (P-060).</summary>
    public static class ReferenceAnchors
    {
        /// <summary>07 s2.4's card before/after table.</summary>
        public const string CardsTable = "07-reference-compositions.md s2.4 (Before/after assembly operations)";

        /// <summary>07 s3.3's narrative before/after table.</summary>
        public const string NarrativeTable = "07-reference-compositions.md s3.3 (Before/after assembly operations)";

        /// <summary>07 s4.3's traversal before/after table.</summary>
        public const string TraversalTable = "07-reference-compositions.md s4.3 (Before/after assembly operations)";

        /// <summary>07 s5's cross-family flow.</summary>
        public const string CrossTable = "07-reference-compositions.md s5 (Cross-family composition: a chapter with a card reward)";
    }

    /// <summary>The four transcribed tables, built once and immutable (P-008: no dictionary order decides anything).</summary>
    public static class ReferenceTables
    {
        /// <summary>The card market's table plus the rows its own prose states (07 s2).</summary>
        public static ConformanceTable Cards()
        {
            const string anchor = ReferenceAnchors.CardsTable;
            var rows = new List<ConformanceRow>
            {
                new ConformanceRow(
                    "mount-festival",
                    "Mount `FestivalScoring` at `LeagueA`",
                    "07:98 — before: seats A/B have bonus 0 and existing totals; after: both have bonus +2, their"
                    + " totals remain, and their next valid sets award 12. The scoreboard is ineligible and the"
                    + " practice seat is isolated (07:49, 07:106).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(1U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "4"),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatTotal(1U)),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(0U), "10", "12"),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(1U), "10", "12"),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                        ConformanceExpectation.Absent(ConformanceFields.ScoreboardBonus),
                    }),

                new ConformanceRow(
                    "spawn-seat-d",
                    "Spawn seat D below `LeagueA`",
                    "07:99 — before: recipe available and the provider already active; after: D obtains the same +2"
                    + " derived contribution before its first executable step, with no local import.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(3U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(3U), "10", "12"),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                        ConformanceExpectation.Absent(ConformanceFields.ScoreboardBonus),
                    }),

                new ConformanceRow(
                    "reconfigure-festival",
                    "Reconfigure festival bonus from `+2` to `+4`",
                    "07:100 — before: A has already scored under the old revision; after: the same source"
                    + " contribution resolves to +4, the existing total is preserved, and later sets award 14.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "2", "4"),
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(1U), "2", "4"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "16"),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(0U), "12", "14"),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatBonusProvider(0U)),
                    }),

                new ConformanceRow(
                    "nested-retraction",
                    "Mount nested `+3`, then retract the ancestor `+2`",
                    "07:51 — a nested festival with +3 produces +5 where both providers match, and retraction"
                    + " removes only the departing source's +2 entry (REF-C04).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "5", "3"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatNextAward(0U), "13"),
                    }),

                new ConformanceRow(
                    "unmount-festival",
                    "Unmount `FestivalScoring`",
                    "07:101 — before: A has total 16 after a committed set and bonus +2; after: the derived bonus"
                    + " entry is removed, the total stays 16, and the next set awards 10.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "2", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "16"),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(0U), "12", "10"),
                        ConformanceExpectation.Absent(ConformanceFields.SeatBonusProvider(0U)),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                    }),

                new ConformanceRow(
                    "reparent-seat-a",
                    "Reparent `SeatA` from `LeagueA` to `LeagueB`",
                    "07:102 — before: A has bonus +2, a hand and score 16; after: A has bonus +1, the same"
                    + " `TargetId`, hand, score and table membership, and no card ownership transfer is implied by"
                    + " scope movement.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "2", "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "16"),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHand(0U)),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHandSize(0U)),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHand(1U)),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHandSize(1U)),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatSeated(0U)),
                        ConformanceExpectation.Unchanged(ConformanceFields.TableActiveSeat, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.TableVersion, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonusProvider(0U), "cards.festival-scoring", "cards.quiet-scoring"),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                        ConformanceExpectation.Absent(ConformanceFields.ScoreboardBonus),
                    }),

                // 07:103-07:104's two mode rows. The target that keeps the contribution is the one whose descriptor
                // declares the complete explicit opt-in (P-013's "A has a complete target opt-in"), which the card
                // slice declares as its own opted-in seat; the seats the festival provider reaches by reach alone —
                // seat A and seat B — lose it in Conservative, and the seat spawned in Automatic loses it too.
                new ConformanceRow(
                    "mode-conservative",
                    "`Automatic` -> `Conservative`",
                    "07:103 — before: the eligible seats derive the provider's contribution and only the opted-in"
                    + " seat has an explicit opt-in naming the festival provider and capability; after: it retains it,"
                    + " the automatically eligible seats lose it, and all of them retain hands and score.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "automatic", "conservative"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OptedInSeatBonus, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(0U), "2", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(1U), "2", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(3U), "2", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "4"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(1U), "4"),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHand(0U)),
                        ConformanceExpectation.Preserved(ConformanceFields.SeatHand(1U)),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                        ConformanceExpectation.Absent(ConformanceFields.ScoreboardBonus),
                    }),

                new ConformanceRow(
                    "mode-automatic",
                    "`Conservative` -> `Automatic`",
                    "07:104 — before: only the opted-in seat participates; after: every eligible non-isolated"
                    + " descendant, the spawned seat D included, derives the contribution again, and the isolated"
                    + " practice seat and the ineligible scoreboard do not.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "conservative", "automatic"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OptedInSeatBonus, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(1U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(3U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.OptedInSeatNextAward, "12", "12"),
                        ConformanceExpectation.Absent(ConformanceFields.PracticeSeatBonus),
                        ConformanceExpectation.Absent(ConformanceFields.ScoreboardBonus),
                    }),

                new ConformanceRow(
                    "exclusive-conflict-rejected",
                    "Switch to `Automatic` with two applicable `cards.DrawPolicy` providers and no declared winner",
                    "07:108 — the mode switch that would expose the conflict leaves the entire old mode and assembly"
                    + " published, and no policy is silently selected (P-014, P-019).",
                    ConformanceRowOutcome.RefusedKeepsAssembly,
                    new[]
                    {
                        ConformanceExpectation.Unchanged(ConformanceFields.WorldMode, "conservative"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(1U), ConformanceValue.None),
                    }),

                new ConformanceRow(
                    "exclude-seat-b",
                    "Exclude `cards.set-bonus` on `SeatB` while the festival provider stays mounted",
                    "07:50, P-016 — a target exclusion is effective in both modes and denial along the propagation"
                    + " path wins, while unrelated capabilities remain available.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(1U), "2", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(1U), "4"),
                    }),

                new ConformanceRow(
                    "suspend-festival",
                    "Suspend `FestivalScoring` (O-06)",
                    "P-046, 07:110 — an explicit suspension stops the installation's ingress/execution and retracts"
                    + " its active contributions while retaining installation and configuration; committed totals stay.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "2", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "4"),
                    }),

                new ConformanceRow(
                    "resume-festival",
                    "Resume `FestivalScoring` (O-04)",
                    "P-046, 07:104 — resume rederives the suspended instance against the current ancestry (the"
                    + " automatic inheritance 07:104's `Conservative` -> `Automatic` row names), so its contribution"
                    + " returns without a reconfiguration: the eligible seat's bonus is +2 again and its next valid"
                    + " set awards 12.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                        ConformanceExpectation.Unchanged(ConformanceFields.SeatTotal(0U), "4"),
                        ConformanceExpectation.Require(ConformanceFields.SeatNextAward(0U), "10", "12"),
                    }),
            };

            return new ConformanceTable("cards", "Card market before/after operations", anchor, "card-market", rows);
        }

        /// <summary>The chapter quest's table plus the rows its own prose states (07 s3).</summary>
        public static ConformanceTable Narrative()
        {
            const string anchor = ReferenceAnchors.NarrativeTable;
            var rows = new List<ConformanceRow>
            {
                new ConformanceRow(
                    "mount-chapter",
                    "Mount chapter plugin",
                    "07:172 — before: Mara and gate east exist with compatible recipes but no chapter binding;"
                    + " after: Mara receives Chapter One dialogue, gate east receives the permit condition, and the"
                    + " crowd prop remains unchanged.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Require(
                            ConformanceFields.GateBinding("gate-east"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Require(
                            ConformanceFields.EncounterBinding("encounter-oak"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("crowd-prop")),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-sailor")),
                    }),

                new ConformanceRow(
                    "spawn-villager",
                    "Spawn a villager under `Village`",
                    "07:173 — before: the chapter plugin is active; after: the new villager receives Chapter One"
                    + " dialogue before execution, without instance-specific imports.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-newcomer"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("crowd-prop")),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                    }),

                new ConformanceRow(
                    "unmount-chapter",
                    "Unmount chapter plugin while a conversation is in progress",
                    "07:174 — before: the permit fact is true and Mara has a conversation in progress; after: a"
                    + " registered state disposition closes the session at publication, derived bindings retract, and"
                    + " the permit fact remains true.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), "1", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.GateBinding("gate-east"), "1", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.EncounterBinding("encounter-oak"), "1", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermitVersion, "2"),
                        ConformanceExpectation.Require(ConformanceFields.MaraConversationStatus, "2", "3"),
                    }),

                new ConformanceRow(
                    "reparent-village",
                    "Reparent `Village` under `ChapterTwo`",
                    "07:175 — before: Mara points to the Chapter One graph and gate east to the Chapter One condition;"
                    + " after: the new bindings select Chapter Two definitions, stable target IDs and the quest ledger"
                    + " persist, and no historical Chapter One fact is renamed or deleted.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), "1", "2"),
                        ConformanceExpectation.Require(
                            ConformanceFields.GateBinding("gate-east"), "1", "2"),
                        ConformanceExpectation.Unchanged(
                            ConformanceFields.DialogueBinding("npc-sailor"), "2"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermitVersion, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.MaraConversationStatus, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.MaraConversationNode, "7"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                    }),

                new ConformanceRow(
                    "mode-conservative",
                    "`Automatic` -> `Conservative`",
                    "07:176 — before: all compatible chapter descendants inherit bindings and only the gate has"
                    + " complete target opt-ins; after: the gate retains its binding, Mara and encounter oak lose"
                    + " theirs, and durable facts remain.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "automatic", "conservative"),
                        ConformanceExpectation.Unchanged(ConformanceFields.GateBinding("gate-east"), "1"),
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), "1", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.EncounterBinding("encounter-oak"), "1", ConformanceValue.None),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "0"),
                    }),

                new ConformanceRow(
                    "mode-automatic",
                    "`Conservative` -> `Automatic`",
                    "07:177 — before: only the explicitly opted-in gate participates; after: eligible Mara and"
                    + " encounter oak regain bindings and the museum target stays isolated.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "conservative", "automatic"),
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Require(
                            ConformanceFields.EncounterBinding("encounter-oak"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.GateBinding("gate-east"), "1"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                    }),

                new ConformanceRow(
                    "exclude-mara",
                    "Exclude `narrative.ConversationTarget` on `npc-mara` while the chapter stays mounted",
                    "07:139, P-016 — an exclusion on one target is effective while the boundary keeps"
                    + " `npc-display` isolated and unrelated bindings stay available.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), "1", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.GateBinding("gate-east"), "1"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-display")),
                    }),

                new ConformanceRow(
                    "suspend-chapter",
                    "Suspend `ChapterNarrative` (O-06)",
                    "P-046, 07:181 — suspension retracts the active contributions and disables the"
                    + " binding-dependent evaluator while the durable facts and the installation survive.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.DialogueBinding("npc-mara"), "1", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.GateBinding("gate-east"), "1", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.GateEastDecision, "1"),
                    }),

                new ConformanceRow(
                    "resume-chapter",
                    "Resume `ChapterNarrative` (O-04)",
                    "P-046, 07:177 — resume rederives the instance against the current ancestry (the automatic"
                    + " inheritance 07:177's `Conservative` -> `Automatic` row names), so its bindings return. The"
                    + " exclusion this stage applied to `npc-mara` is still in force, and P-016's \"denial along"
                    + " the propagation path wins over imports, opt-ins and selection overrides in both modes\" is"
                    + " exactly why Mara stays unbound while the unexcluded gate target gets its binding back, so the"
                    + " row reads both and a resume that bypassed the exclusion would fail here.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.GateBinding("gate-east"), ConformanceValue.None, "1"),
                        ConformanceExpectation.Absent(ConformanceFields.DialogueBinding("npc-mara")),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                    }),
            };

            return new ConformanceTable(
                "narrative", "Chapter quest before/after operations", anchor, "chapter-quest", rows);
        }

        /// <summary>The traversal challenge's table plus the rows its own prose states (07 s4).</summary>
        public static ConformanceTable Traversal()
        {
            const string anchor = ReferenceAnchors.TraversalTable;
            var rows = new List<ConformanceRow>
            {
                new ConformanceRow(
                    "mount-tailwind",
                    "Mount `Tailwind` in `Valley`",
                    "07:240 — before: the runner's additional x acceleration is 0 and its pose and velocity are"
                    + " `(p, v)`; after: the additional x acceleration is +2 and `(p, v)` stays unchanged until the"
                    + " next step. The checkpoint volume declares only the sensor contract, so no modifier selects it,"
                    + " and the sibling branch under `Ridge` inherits nothing from `Valley` (07:203, 07:200).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerPose("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerVelocity("runner-a")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("runner-b")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("checkpoint-1")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX(ConformanceFields.OptedInRunner)),
                    }),

                new ConformanceRow(
                    "spawn-runner-c",
                    "Spawn a runner under `Valley/Runners` (07's future descendant)",
                    "07:241 — before: the modifier is already active; after: the new runner begins with the same"
                    + " additional +2 before its first executable step, and the checkpoint stays ineligible. The"
                    + " course's own future runner is the target this row spawns.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-c"), ConformanceValue.None, "2000"),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("checkpoint-1")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("runner-b")),
                    }),

                new ConformanceRow(
                    "unmount-tailwind",
                    "Unmount `Tailwind` after a committed crossing",
                    "07:242 — before: the runner acquired speed during prior steps and passed checkpoint 1; after:"
                    + " future additional x acceleration returns to 0 and velocity, pose and committed progress"
                    + " remain.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), "2000", ConformanceValue.None),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationProvider("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerVelocity("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerPose("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerProgress("runner-a")),
                    }),

                new ConformanceRow(
                    "reparent-runner-subtree",
                    "Reparent the runner subtree into `Ridge`",
                    "07:243 and 07:247 — before: the additional x acceleration is +2; after: it is -1 and the same"
                    + " pose, velocity, jump state and progress continue. The numeric sequence with zero input is"
                    + " `1.00 -> 1.04 -> 1.02` m/s, i.e. 1000 -> 1040 -> 1020 thousandths (REF-A01).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), "2000", "-1000"),
                        ConformanceExpectation.Unchanged(
                            ConformanceFields.RunnerVelocity("runner-a"), "(1040,0,0)"),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerPose("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerJump("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerProgress("runner-a")),
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationProvider("runner-a"),
                            "traversal.tailwind",
                            "traversal.headwind"),
                    }),

                new ConformanceRow(
                    "mode-conservative",
                    "`Automatic` -> `Conservative`",
                    "07:244 — before: every eligible runner inherits and only the complete-opt-in runner is opted in;"
                    + " after: that runner retains the modifier, the automatically eligible runners lose it, and none"
                    + " is teleported or has its velocity reset.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "automatic", "conservative"),
                        ConformanceExpectation.Unchanged(
                            ConformanceFields.RunnerAccelerationX(ConformanceFields.OptedInRunner), "2000"),
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), "2000", ConformanceValue.None),
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-c"), "2000", ConformanceValue.None),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerPose("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerVelocity("runner-a")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("runner-display")),
                    }),

                new ConformanceRow(
                    "mode-automatic",
                    "`Conservative` -> `Automatic`",
                    "07:245 — before: only the opted-in runner participates; after: the automatically eligible"
                    + " runners receive the applicable modifier again, while the isolated showcase stays isolated.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.WorldMode, "conservative", "automatic"),
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-c"), ConformanceValue.None, "2000"),
                        ConformanceExpectation.Unchanged(
                            ConformanceFields.RunnerAccelerationX(ConformanceFields.OptedInRunner), "2000"),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("runner-display")),
                    }),

                new ConformanceRow(
                    "exclude-runner-a",
                    "Exclude `traversal.Acceleration` on `runner-a` while `Tailwind` stays mounted",
                    "07:203, P-016 — an exclusion on one target blocks that runner's contribution while its sibling"
                    + " keeps the provider's reach, and the isolated showcase and ineligible checkpoint stay clear.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), "2000", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(
                            ConformanceFields.RunnerAccelerationX("runner-c"), "2000"),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("runner-display")),
                        ConformanceExpectation.Absent(ConformanceFields.RunnerAccelerationX("checkpoint-1")),
                    }),

                new ConformanceRow(
                    "suspend-tailwind",
                    "Suspend `Tailwind` (O-06)",
                    "P-046, 07:205 — the modifiers do not own pose or velocity, so suspension retracts only the"
                    + " configuration contribution and leaves displacement and speed already produced.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), "2000", ConformanceValue.None),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerPose("runner-a")),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerVelocity("runner-a")),
                    }),

                new ConformanceRow(
                    "resume-tailwind",
                    "Resume `Tailwind` (O-04)",
                    "P-046, 07:245 — resume rederives the modifier against the current ancestry (the automatic"
                    + " inheritance 07:245's `Conservative` -> `Automatic` row names), so the contribution returns"
                    + " without resetting motion state.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                        ConformanceExpectation.Preserved(ConformanceFields.RunnerVelocity("runner-a")),
                    }),
            };

            return new ConformanceTable(
                "traversal", "Traversal challenge before/after operations", anchor, "traversal-challenge", rows);
        }

        /// <summary>The cross-family combination's table (07 s5), which is what one combined world must observe.</summary>
        public static ConformanceTable Cross()
        {
            const string anchor = ReferenceAnchors.CrossTable;
            var rows = new List<ConformanceRow>
            {
                new ConformanceRow(
                    "reward-enqueue",
                    "Commit the Chapter One permit choice: the fact, its event and the pending reward publish together",
                    "07:267 — in step 12 `narrative.quest` writes the fact and seals a tentative receipt; an ordered"
                    + " `rewards.enqueue` stage writes an ECS outbox item, and a successful `PublishStep` exposes the"
                    + " fact, its event and the pending reward together.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.BridgePermit, "0", "1"),
                        ConformanceExpectation.Require(ConformanceFields.BridgePermitVersion, "1", "2"),
                        ConformanceExpectation.Require(ConformanceFields.MaraConversationStatus, "0", "2"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxRecognised, "0", "1"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxOpen, "0", "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxAcknowledged, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxMutations, "0"),
                        ConformanceExpectation.Preserved(ConformanceFields.RewardRecipientHandSize),
                        ConformanceExpectation.Preserved(ConformanceFields.RewardHolderHandSize),
                    }),

                new ConformanceRow(
                    "reward-settle",
                    "Dispatch the admitted reward and acknowledge it: grant and acknowledgement publish together",
                    "07:269-270 — a managed `rewards.dispatch` observer submits `GrantCards` through the ordinary"
                    + " command port, the card table applies the grant and its durable receipt, and the ordered"
                    + " `rewards.ack` stage marks the outbox complete in the same `PublishStep`. The grant reaches the"
                    + " hand the only way this card package can express it, an owner-committed transfer (GC-021 s6"
                    + " item 7), so the table version advances with the transfer; the row's own claim is that the"
                    + " destination effect commits exactly once and unrelated state is preserved.",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(ConformanceFields.OutboxOpen, "1", "0"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxAcknowledged, "0", "1"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxMutations, "0", "1"),
                        ConformanceExpectation.Require(
                            ConformanceFields.RewardRecipientHandSize, "4", "5"),
                        ConformanceExpectation.Require(ConformanceFields.RewardHolderHandSize, "4", "3"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardRecipientTotal, "4"),
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardHolderTotal, "4"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermitVersion, "2"),
                    }),

                new ConformanceRow(
                    "reward-redelivery",
                    "Deliver the same reward obligation again after its acknowledgement was lost",
                    "07:272 — a duplicate source receipt or retry uses the same durable `RewardId` and returns the"
                    + " existing result without granting another card, even though a restored world has a new session"
                    + " identity (P-045, REF-X01).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        // Read from the same destination installation as the transfer row: it holds the carried
                        // obligation, hands it to the card table again under the same external idempotency key, and
                        // the destination reports AlreadyApplied while mutating nothing (P-045, REF-X01).
                        ConformanceExpectation.Require(
                            ConformanceFields.OutboxAlreadyApplied, "0", "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxMutations, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxOpen, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxRows, "2"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                    }),

                // 07:276's four claims. Each is a row of its own because each has a different observable and a
                // different generic mechanism carrying it (see the HANDOFF's clause-to-mechanism table):
                //   * the pending unmount is carried by a fenced resource lease the installation still holds, so the
                //     teardown cannot settle and the lane reports `TeardownBlocked` while the assembly stands;
                //   * the drain-then-unmount row reads the dormant outbox slot `PreserveDormant` leaves behind;
                //   * the transfer row moves the durable rows to an explicitly selected compatible owner;
                //   * the scoring unmount row is the "removal never reverses gameplay" claim (P-003, P-032).
                new ConformanceRow(
                    "reward-unmount-pending",
                    "Unmount `NarrativeCardRewards` while a reward is still pending",
                    "07:276 — \"Unmounting with pending work therefore rejects until it drains or transfers.\" The"
                    + " installation holds a fenced resource lease for its pending work, so the teardown cannot settle:"
                    + " the unmount is refused with `TeardownBlocked`, the installation stays mounted, and the pending"
                    + " obligation and every unrelated field are exactly as they were (P-047, P-048, REF-X02).",
                    ConformanceRowOutcome.RefusedKeepsAssembly,
                    new[]
                    {
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardsInstallationState, "mounted"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxOpen, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxPendingWork, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxMutations, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxRows, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                        ConformanceExpectation.Preserved(ConformanceFields.RewardRecipientHandSize),
                        ConformanceExpectation.Preserved(ConformanceFields.RewardHolderHandSize),
                    }),

                new ConformanceRow(
                    "reward-drain-then-unmount",
                    "Drain the reward (grant and acknowledge), then unmount: the completed outbox is preserved dormant",
                    "07:276 — \"declares `PreserveDormant` for its completed outbox, with a scratch-migration"
                    + " precondition that no pending work remains.\" With the work drained the state-policy pass's"
                    + " migration accepts its copied value, the slot's last-support loss retains the completed rows"
                    + " with no active writer, and the unmount settles (P-029, P-032).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        // The settled counters are this row's before state: the drain rewrites only what it owns —
                        // the slot's pending count and the work lease — and then unmounts the drained installation.
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxOpen, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxAcknowledged, "1"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxPendingWork, "1", "0"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxSlotDormant, "false", "true"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxRetainedLeases, "1", "0"),
                        ConformanceExpectation.Require(
                            ConformanceFields.RewardsInstallationState, "mounted", "dormant"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxRows, "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                    }),

                new ConformanceRow(
                    "reward-unmount-transfer",
                    "Unmount with pending work by transferring the outbox to an explicitly selected compatible owner",
                    "07:276 — \"alternatively an explicitly selected compatible `TransferTo` owner may take the"
                    + " outbox.\" The transfer resolves through the declared owner-transfer policy against the"
                    + " revision's own owner set, the rows move to the named destination, and nothing is lost"
                    + " (P-025, P-032, REF-X02).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        // Read from the DESTINATION installation: it starts empty and receives the rows, so "nothing
                        // was lost" is the destination's own count rather than an inference from the source. The
                        // source's release is asserted in the step's detail, and `outbox.mutations` stays 0 because a
                        // transfer hands work over — it applies nothing at a destination.
                        ConformanceExpectation.Require(ConformanceFields.OutboxRows, "0", "2"),
                        ConformanceExpectation.Require(ConformanceFields.OutboxOpen, "0", "1"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxMutations, "0"),
                        ConformanceExpectation.Unchanged(ConformanceFields.BridgePermit, "1"),
                    }),

                new ConformanceRow(
                    "reward-scoring-unmount-keeps-card",
                    "Unmount the scoring provider after a reward was granted: the card and the score survive",
                    "07:276 — \"Unmounting `FestivalScoring` does not undo an issued card or a score.\" Retracting a"
                    + " capability contribution is not a gameplay effect and cannot reverse committed state"
                    + " (P-003, P-032).",
                    ConformanceRowOutcome.Published,
                    new[]
                    {
                        ConformanceExpectation.Require(
                            ConformanceFields.SeatBonus(0U), "2", ConformanceValue.None),
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardRecipientHandSize, "5"),
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardHolderHandSize, "3"),
                        ConformanceExpectation.Unchanged(ConformanceFields.RewardRecipientTotal, "4"),
                        ConformanceExpectation.Unchanged(ConformanceFields.OutboxAcknowledged, "1"),
                    }),
            };
            return new ConformanceTable(
                "cross", "Cross-family combination before/after operations", anchor, "cross-family", rows);
        }

        /// <summary>Every table, in the order a run executes them.</summary>
        public static IReadOnlyList<ConformanceTable> All()
        {
            return new List<ConformanceTable> { Cards(), Narrative(), Traversal(), Cross() };
        }

        /// <summary>The table with this id, or null for an unknown id.</summary>
        public static ConformanceTable? ById(string tableId)
        {
            IReadOnlyList<ConformanceTable> tables = All();
            for (int i = 0; i < tables.Count; i++)
            {
                if (string.Equals(tables[i].TableId, tableId, StringComparison.Ordinal))
                {
                    return tables[i];
                }
            }

            return null;
        }
    }
}
