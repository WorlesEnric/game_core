// GameCore.ReferenceConformance — the ordered execution script of every 07 table.
//
// A stage is one freshly built world. Inside a stage the steps run in list order: a `Row` step is the 07 row itself,
// and a step whose row id carries `/pre<n>` establishes the state that row's "Before" column names (a committed card
// set, the second provider, an opted-in target, a spawned descendant). Numbering is by list order, not by "before or
// after the row", so a stage can finish with a step that shows what the row's *next* state is — which is exactly how
// `07:247`'s velocity sequence `1000 -> 1040 -> 1020` is observed.
//
// Every step names an operation key from `ConformanceOperations`; the genre's own family maps it to a payload its
// package declares. Nothing here invents a composition edit, and nothing here names a genre-specific payload.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>The scripts that execute the four transcribed tables (07 s2.4, s3.3, s4.3, s5).</summary>
    public static class ReferenceScripts
    {

        /// <summary>The card market's script: four stages, one fresh world each (07 s2.4, 07:51, 07:108, 07:110).</summary>
        public static ConformanceScript Cards()
        {
            var stages = new List<ConformanceStage>
            {
                // Rows 07:98-07:100 are cumulative: the provider mounts, a descendant spawns, the same source is
                // reconfigured. One committed set between the spawn and the reconfigure is what makes 07:100's
                // "A has already scored under the old revision" true.
                new ConformanceStage(
                    "scoring-lifecycle",
                    "07:98-07:100 — mount, future descendant, reconfigure",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Row("cards", "mount-festival", ConformanceOperations.MountProvider, 0, "mount the festival provider"),
                        Row("cards", "spawn-seat-d", ConformanceOperations.SpawnFutureTarget, 0, "spawn seat D under LeagueA"),
                        Pre("reconfigure-festival", 1, ConformanceOperations.CommitCommand, 1,
                            "commit one valid set as seat A under the +2 revision (07:100's before state)",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatTotal(0U), "4", "16"),
                                ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatHandSize(0U), "4", "1"),
                                ConformanceExpectation.Require(ConformanceFields.TableTurn, "0", "1"),
                                ConformanceExpectation.Require(ConformanceFields.TableVersion, "1", "2"),
                            }),
                        Row("cards", "reconfigure-festival", ConformanceOperations.ReconfigureProvider, 4,
                            "reconfigure the festival contribution to +4"),
                    }),

                // 07:51's nested-retraction case (REF-C04): the nested provider adds +3 under the seat's own scope,
                // and retracting the ancestor's +2 removes only that source's entry.
                new ConformanceStage(
                    "nested-retraction",
                    "07:51 — a nested festival adds `+3` and the ancestor's `+2` is retracted (REF-C04)",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("nested-retraction", 1, ConformanceOperations.MountProvider, 0,
                            "mount the ancestor festival at LeagueA (+2)",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                            }),
                        Pre("nested-retraction", 2, ConformanceOperations.MountNestedProvider, 3,
                            "mount the nested festival under seat A's scope (+3), so both match",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.SeatBonus(0U), "2", "5"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatNextAward(0U), "12", "15"),
                            }),
                        Row("cards", "nested-retraction", ConformanceOperations.UnmountProvider, 0,
                            "retract the ancestor +2 entry and nothing else"),
                    }),

                // 07:101's provider loss: the seat has scored 16 under +2, then the provider leaves.
                new ConformanceStage(
                    "provider-loss",
                    "07:101 — the provider leaves after a committed set",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("unmount-festival", 1, ConformanceOperations.MountProvider, 0,
                            "mount the festival provider",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                            }),
                        Pre("unmount-festival", 2, ConformanceOperations.CommitCommand, 1,
                            "commit one valid set so the seat holds total 16",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.SeatTotal(0U), "4", "16"),
                            }),
                        Row("cards", "unmount-festival", ConformanceOperations.UnmountProvider, 0,
                            "unmount the festival provider while the committed total survives"),
                    }),

                // 07:102's subtree move: the moved seat must land on the quiet league's own provider.
                new ConformanceStage(
                    "subtree-move",
                    "07:102 — the seat's branch moves from LeagueA to LeagueB",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("reparent-seat-a", 1, ConformanceOperations.MountProvider, 0,
                            "mount the festival provider at LeagueA",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                            }),
                        Pre("reparent-seat-a", 2, ConformanceOperations.MountSecondProvider, 0,
                            "mount the quiet provider at LeagueB (+1)",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                            }),
                        Pre("reparent-seat-a", 3, ConformanceOperations.CommitCommand, 1,
                            "commit one valid set so the moved seat's score is 16 and its hand is seeded real state",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.SeatTotal(0U), "4", "16"),
                                ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                            }),
                        Row("cards", "reparent-seat-a", ConformanceOperations.ReparentMovedScope, 0,
                            "reparent seat A's scope from LeagueA to LeagueB"),
                    }),

                // 07:103-07:104's two mode directions. The complete explicit opt-in is a descriptor property of the
                // moved seat (P-015: a target's compatibility descriptor is immutable assembly input), so the stage
                // declares it at seeding time rather than publishing a later edit for it.
                new ConformanceStage(
                    "mode-directions",
                    "07:103-07:104 — both mode directions over existing and future descendants",
                    new[] { ConformanceOperations.SeedOptedInTarget },
                    new List<ConformanceStep>
                    {
                        Pre("mode-conservative", 1, ConformanceOperations.MountProvider, 0,
                            "mount the festival provider in Automatic",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                            }),
                        Pre("mode-conservative", 2, ConformanceOperations.SpawnFutureTarget, 0,
                            "spawn seat D in Automatic so the conservative switch has a future descendant to lose",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(3U), ConformanceValue.None, "2"),
                            }),
                        Row("cards", "mode-conservative", ConformanceOperations.ModeConservative, 0,
                            "switch the world to Conservative"),
                        Row("cards", "mode-automatic", ConformanceOperations.ModeAutomatic, 0,
                            "switch the world back to Automatic"),
                    }),

                // 07:108's refused switch: the pair is mounted while Conservative (where both are denied, so the
                // mounts publish cleanly) and the switch that would expose the conflict keeps the old assembly.
                new ConformanceStage(
                    "exclusive-conflict",
                    "07:108 — an unresolved exclusive pair refuses the switch and keeps the old assembly",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("exclusive-conflict-rejected", 1, ConformanceOperations.MountProvider, 0,
                            "mount the festival provider so the old assembly has state worth preserving",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(0U), ConformanceValue.None, "2"),
                            }),
                        Pre("exclusive-conflict-rejected", 2, ConformanceOperations.ModeConservative, 0,
                            "enter Conservative, where a descendant rule without export/import is denied",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(ConformanceFields.WorldMode, "conservative"),
                            }),
                        Pre("exclusive-conflict-rejected", 3, ConformanceOperations.MountConflictProvider, 0,
                            "mount the first exclusive draw-policy provider",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                            }),
                        Pre("exclusive-conflict-rejected", 4,
                            ConformanceOperations.MountConflictSecondProvider, 0,
                            "mount the second provider of the same exclusive capability",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(ConformanceFields.SeatBonus(0U), "2"),
                            }),
                        Row("cards", "exclusive-conflict-rejected", ConformanceOperations.ModeAutomatic, 0,
                            "attempt the switch that would expose the conflict"),
                    }),

                // 07:50/P-016's exclusion and P-046's suspend/resume of the same provider.
                new ConformanceStage(
                    "exclusion-and-suspension",
                    "07:50, 07:110, P-016, P-046 — exclusion, suspension and resume of one provider",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("exclude-seat-b", 1, ConformanceOperations.MountProvider, 0,
                            "mount the festival provider",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.SeatBonus(1U), ConformanceValue.None, "2"),
                            }),
                        Row("cards", "exclude-seat-b", ConformanceOperations.ApplyExclusion, 0,
                            "exclude the capability on seat B while the provider stays mounted"),
                        Row("cards", "suspend-festival", ConformanceOperations.SuspendProvider, 0,
                            "suspend the provider explicitly"),
                        Row("cards", "resume-festival", ConformanceOperations.ResumeProvider, 0,
                            "resume it and let it rederive"),
                    }),
            };

            return new ConformanceScript("cards", stages);
        }

        /// <summary>The chapter quest's script (07 s3.3, P-016, P-046).</summary>
        public static ConformanceScript Narrative()
        {
            var stages = new List<ConformanceStage>
            {
                new ConformanceStage(
                    "chapter-mount-and-future-descendant",
                    "07:172-07:173 — mount the chapter, then spawn a villager",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Row("narrative", "mount-chapter", ConformanceOperations.MountProvider, 0,
                            "mount the Chapter One provider"),
                        Row("narrative", "spawn-villager", ConformanceOperations.SpawnFutureTarget, 0,
                            "spawn a villager under Village"),
                    }),

                new ConformanceStage(
                    "unmount-with-live-session",
                    "07:174 — the chapter leaves while a conversation is in progress",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("unmount-chapter", 1, ConformanceOperations.MountProvider, 0,
                            "mount the Chapter One provider",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                            }),
                        Pre("unmount-chapter", 2, ConformanceOperations.CommitCommand, 1,
                            "commit the permit choice so the fact is true and the conversation is active",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.BridgePermit, "0", "1"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.BridgePermitVersion, "1", "2"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.GateEastDecision, "0", "1"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.MaraConversationStatus, "0", "2"),
                            }),
                        Row("narrative", "unmount-chapter", ConformanceOperations.UnmountProvider, 0,
                            "unmount the chapter and let the registered disposition close the session"),
                    }),

                new ConformanceStage(
                    "reparent-village",
                    "07:175 — Village moves under ChapterTwo",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("reparent-village", 1, ConformanceOperations.MountProvider, 0,
                            "mount Chapter One at ChapterOne",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                            }),
                        Pre("reparent-village", 2, ConformanceOperations.MountSecondProvider, 0,
                            "mount Chapter Two at ChapterTwo, the move's destination",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(
                                    ConformanceFields.DialogueBinding("npc-mara"), "1"),
                                ConformanceExpectation.Unchanged(
                                    ConformanceFields.DialogueBinding("npc-sailor"), "2"),
                            }),
                        Row("narrative", "reparent-village", ConformanceOperations.ReparentMovedScope, 0,
                            "reparent Village under ChapterTwo"),
                    }),

                new ConformanceStage(
                    "mode-directions",
                    "07:176-07:177 — both mode directions with the gate's complete opt-in. The gate's opt-in is a"
                    + " descriptor property (P-015), so this stage declares it at seeding time.",
                    new[] { ConformanceOperations.SeedOptedInTarget },
                    new List<ConformanceStep>
                    {
                        Pre("mode-conservative", 1, ConformanceOperations.MountProvider, 0,
                            "mount the Chapter One provider in Automatic",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                            }),
                        Row("narrative", "mode-conservative", ConformanceOperations.ModeConservative, 0,
                            "switch the world to Conservative"),
                        Row("narrative", "mode-automatic", ConformanceOperations.ModeAutomatic, 0,
                            "switch the world back to Automatic"),
                    }),

                new ConformanceStage(
                    "exclusion-and-suspension",
                    "07:139, P-016, P-046 — exclusion, suspension and resume of the chapter provider",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("exclude-mara", 1, ConformanceOperations.MountProvider, 0,
                            "mount the Chapter One provider",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.DialogueBinding("npc-mara"), ConformanceValue.None, "1"),
                            }),
                        Pre("exclude-mara", 2, ConformanceOperations.CommitCommand, 1,
                            "commit the permit choice, so the durable fact and the open gate the suspension row"
                            + " asserts are state this world really owns (07:181, 07:174)",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.BridgePermit, "0", "1"),
                                ConformanceExpectation.Require(ConformanceFields.BridgePermitVersion, "1", "2"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.GateEastDecision, "0", "1"),
                            }),
                        Row("narrative", "exclude-mara", ConformanceOperations.ApplyExclusion, 0,
                            "exclude the conversation capability on Mara"),
                        Row("narrative", "suspend-chapter", ConformanceOperations.SuspendProvider, 0,
                            "suspend the chapter provider"),
                        Row("narrative", "resume-chapter", ConformanceOperations.ResumeProvider, 0,
                            "resume it and let it rederive"),
                    }),
            };

            return new ConformanceScript("narrative", stages);
        }

        /// <summary>The traversal challenge's script, including 07:247's numeric sequence (REF-A01).</summary>
        public static ConformanceScript Traversal()
        {
            var stages = new List<ConformanceStage>
            {
                new ConformanceStage(
                    "modifier-lifecycle",
                    "07:240-07:241 — the modifier mounts, then a descendant spawns under it",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Row("traversal", "mount-tailwind", ConformanceOperations.MountProvider, 0,
                            "mount Tailwind in Valley"),
                        Row("traversal", "spawn-runner-c", ConformanceOperations.SpawnFutureTarget, 0,
                            "spawn the course's own future runner under Valley/Runners"),
                    }),

                new ConformanceStage(
                    "unmount-after-progress",
                    "07:242 — the modifier leaves after a committed crossing",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("unmount-tailwind", 1, ConformanceOperations.MountProvider, 0,
                            "mount Tailwind in Valley",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                            }),
                        Pre("unmount-tailwind", 2, ConformanceOperations.CommitCommand, 1,
                            "integrate one 20 ms step so the runner holds real motion: 1.00 -> 1.04 m/s, and the"
                            + " crossing of the course's first volume is committed",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerVelocity("runner-a"), "(1000,0,0)", "(1040,0,0)"),
                            }),
                        Row("traversal", "unmount-tailwind", ConformanceOperations.UnmountProvider, 0,
                            "unmount the modifier while pose, velocity and progress survive"),
                    }),

                new ConformanceStage(
                    "numeric-acceleration-sequence",
                    "07:243 and 07:247 — `1.00 -> 1.04 -> 1.02` m/s across a fenced reparent (REF-A01)",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("reparent-runner-subtree", 1, ConformanceOperations.MountProvider, 0,
                            "mount Tailwind in Valley",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                            }),
                        Pre("reparent-runner-subtree", 2, ConformanceOperations.CommitCommand, 1,
                            "one 20 ms step with zero horizontal input: 1.00 -> 1.04 m/s",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerVelocity("runner-a"), "(1000,0,0)", "(1040,0,0)"),
                                ConformanceExpectation.Unchanged(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), "2000"),
                            }),
                        Pre("reparent-runner-subtree", 3, ConformanceOperations.MountSecondProvider, 0,
                            "mount Headwind in Ridge; the sibling branch leaves runner A's contribution alone",
                            new[]
                            {
                                ConformanceExpectation.Unchanged(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), "2000"),
                            }),
                        Row("traversal", "reparent-runner-subtree", ConformanceOperations.ReparentMovedScope, 0,
                            "reparent the runner subtree into Ridge"),
                        Pre("reparent-runner-subtree", 4, ConformanceOperations.CommitCommand, 1,
                            "one more 20 ms step under -1 m/s²: 1.04 -> 1.02 m/s",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerVelocity("runner-a"), "(1040,0,0)", "(1020,0,0)"),
                                ConformanceExpectation.Unchanged(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), "-1000"),
                            }),
                    }),

                // The complete explicit opt-in is a descriptor property (P-015), so the stage declares it at seeding
                // time; the automatically eligible runners are the course's own valley runner and the runner spawned
                // under the provider in this stage.
                new ConformanceStage(
                    "mode-directions",
                    "07:244-07:245 — both mode directions over existing and future runners",
                    new[] { ConformanceOperations.SeedOptedInTarget },
                    new List<ConformanceStep>
                    {
                        Pre("mode-conservative", 1, ConformanceOperations.MountProvider, 0,
                            "mount Tailwind in Valley",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX(ConformanceFields.OptedInRunner),
                                    ConformanceValue.None,
                                    "2000"),
                            }),
                        Pre("mode-conservative", 2, ConformanceOperations.SpawnFutureTarget, 0,
                            "spawn the course's future runner so the conservative switch has a descendant to lose",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-c"), ConformanceValue.None, "2000"),
                            }),
                        Row("traversal", "mode-conservative", ConformanceOperations.ModeConservative, 0,
                            "switch the world to Conservative"),
                        Row("traversal", "mode-automatic", ConformanceOperations.ModeAutomatic, 0,
                            "switch the world back to Automatic"),
                    }),

                new ConformanceStage(
                    "exclusion-and-suspension",
                    "07:203, P-016, P-046 — exclusion, suspension and resume of one modifier",
                    Array.Empty<string>(),
                    new List<ConformanceStep>
                    {
                        Pre("exclude-runner-a", 1, ConformanceOperations.MountProvider, 0,
                            "mount Tailwind in Valley",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-a"), ConformanceValue.None, "2000"),
                            }),
                        Pre("exclude-runner-a", 2, ConformanceOperations.SpawnFutureTarget, 0,
                            "spawn the future runner so the exclusion's own sibling is observable",
                            new[]
                            {
                                ConformanceExpectation.Require(
                                    ConformanceFields.RunnerAccelerationX("runner-c"), ConformanceValue.None, "2000"),
                            }),
                        Row("traversal", "exclude-runner-a", ConformanceOperations.ApplyExclusion, 0,
                            "exclude the acceleration capability on runner A"),
                        Row("traversal", "suspend-tailwind", ConformanceOperations.SuspendProvider, 0,
                            "suspend the modifier"),
                        Row("traversal", "resume-tailwind", ConformanceOperations.ResumeProvider, 0,
                            "resume it and let it rederive"),
                    }),
            };

            return new ConformanceScript("traversal", stages);
        }

        /// <summary>The combined world's script: the reward flow of 07 s5 through the durable outbox (P-045).</summary>
        public static ConformanceScript Cross()
        {
            var stages = new List<ConformanceStage>
            {
                new ConformanceStage(
                    "reward-flow",
                    "07:267-07:276 — one committed choice becomes one durable, idempotent card grant, and the"
                    + " installation's unmount is gated by its pending work and preserves the completed outbox",
                    new[]
                    {
                        ConformanceOperations.MountProvider,
                        ConformanceOperations.MountRewardBridge,
                        ConformanceOperations.MountScoringProvider,
                    },
                    new List<ConformanceStep>
                    {
                        // 07:267's step 12 is the commit whose receipt the reward keys on, so the row is executed by
                        // submitting that one choice. Its expectations are the table's own — the fact, its version,
                        // the conversation, the recognised event and the open obligation, all in one publication —
                        // so nothing here restates what the transcription already demands.
                        Row("cross", "reward-enqueue", ConformanceOperations.CommitCommand, 1,
                            "commit the permit choice: the fact, its event and the pending reward publish together"),
                        Row("cross", "reward-unmount-pending", ConformanceOperations.UnmountRewardBridge, 0,
                            "attempt to unmount the installation while one reward is still pending"),
                        Row("cross", "reward-settle", ConformanceOperations.SettleReward, 0,
                            "dispatch the admitted reward and acknowledge it"),
                        Row("cross", "reward-drain-then-unmount", ConformanceOperations.SettleReward, 1,
                            "drain the pending work, then unmount: the completed outbox is preserved dormant"),
                        // The alternative 07:276 names for a pending unmount: an explicitly selected compatible owner
                        // takes the outbox. A fresh obligation is admitted first, so the transfer has work to carry.
                        Pre("reward-unmount-transfer", 1, ConformanceOperations.CommitCommand, 1,
                            "admit one more reward obligation, so the transfer has pending work to carry",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.OutboxOpen, "0", "1"),
                                ConformanceExpectation.Require(ConformanceFields.OutboxRows, "1", "2"),
                            }),
                        Pre("reward-unmount-transfer", 2, ConformanceOperations.SettleReward, 0,
                            "arm the new obligation's work lease, so the transfer carries armed work too",
                            new[]
                            {
                                ConformanceExpectation.Require(ConformanceFields.OutboxPendingWork, "0", "1"),
                            }),
                        Row("cross", "reward-unmount-transfer", ConformanceOperations.TransferRewardOutbox, 0,
                            "transfer the outbox to the explicitly selected compatible owner"),
                        // 07:272's claim is asserted at the owner that now holds the carried obligation, so the
                        // destination is asked for the same reward a second time under the same external key.
                        Row("cross", "reward-redelivery", ConformanceOperations.RedeliverReward, 0,
                            "hand the carried obligation to the card table again: it applies exactly once"),
                        Row("cross", "reward-scoring-unmount-keeps-card", ConformanceOperations.UnmountScoringProvider, 0,
                            "unmount the scoring provider: the issued card and the score survive"),
                    }),
            };

            return new ConformanceScript("cross", stages);
        }

        /// <summary>Every script, in the order a run executes them.</summary>
        public static IReadOnlyList<ConformanceScript> All()
        {
            return new List<ConformanceScript> { Cards(), Narrative(), Traversal(), Cross() };
        }

        /// <summary>The script of one table id, or null for an unknown id.</summary>
        public static ConformanceScript? ById(string tableId)
        {
            IReadOnlyList<ConformanceScript> scripts = All();
            for (int i = 0; i < scripts.Count; i++)
            {
                if (string.Equals(scripts[i].TableId, tableId, StringComparison.Ordinal))
                {
                    return scripts[i];
                }
            }

            return null;
        }

        /// <summary>
        /// A step that executes one transcribed 07 row: its expectations and its outcome come from the row its own
        /// table declares, so a row can never be asserted twice with two different sets of values. Row ids are
        /// unique per table, not across tables (`mode-conservative` is a row of three of them), so the script
        /// resolves the id inside the table it is executing and a name that exists in another table is a fixture
        /// defect that throws rather than a silently wrong expectation set (P-026, P-060).
        /// </summary>
        private static ConformanceStep Row(
            string tableId, string rowId, string operation, int operand, string note)
        {
            ConformanceTable table = ReferenceTables.ById(tableId)
                ?? throw new InvalidOperationException(
                    "no transcribed table carries the id '" + tableId + "'; the script and the tables disagree.");
            ConformanceRow row = FindRow(table, rowId);
            return new ConformanceStep(
                row.RowId,
                ConformanceStepKind.Row,
                operation,
                operand,
                note,
                row.Expectations,
                row.Outcome);
        }

        /// <summary>The row of this table with this id, or a fixture defect that throws (P-026, P-060).</summary>
        private static ConformanceRow FindRow(ConformanceTable table, string rowId)
        {
            for (int r = 0; r < table.Rows.Count; r++)
            {
                if (string.Equals(table.Rows[r].RowId, rowId, StringComparison.Ordinal))
                {
                    return table.Rows[r];
                }
            }

            throw new InvalidOperationException(
                "the table '" + table.TableId + "' carries no 07 row '" + rowId
                + "'; the script and the tables disagree.");
        }

        private static ConformanceStep Pre(
            string rowId,
            int ordinal,
            string operation,
            int operand,
            string note,
            IReadOnlyList<ConformanceExpectation> expectations)
        {
            return new ConformanceStep(
                rowId + "/pre" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ConformanceStepKind.Precondition,
                operation,
                operand,
                note,
                expectations);
        }

        /// <summary>Convenience overload for a precondition whose state needs no expectation of its own.</summary>
        private static ConformanceStep Pre(string rowId, int ordinal, string operation, int operand, string note)
            => Pre(rowId, ordinal, operation, operand, note, Array.Empty<ConformanceExpectation>());
    }
}
