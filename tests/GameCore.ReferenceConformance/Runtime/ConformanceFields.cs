// GameCore.ReferenceConformance — the canonical field vocabulary every 07 table is observed through.
//
// A trace records canonical tokens, so every observable a 07 table names needs one canonical field key and one
// reader. The vocabulary is declared here as data — the set of fields a table observes is checkable against the set
// the run recorded, so a field the table demands but the world never read is a reported gap rather than a silently
// missing row (P-026, P-060).
//
// Field keys are `<subject>.<property>`, lower case, hyphen-separated. A subject is a stable target name from the
// reference composition's own vocabulary (`seat-a`, `npc-mara`, `runner-a`, `table-1`, `quest-ledger`) or one of
// the two world-level subjects `world` and `outbox`.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.ReferenceConformance
{
    /// <summary>What kind of reading one canonical field demands, so a family can report what it cannot answer.</summary>
    public enum ConformanceFieldKind
    {
        /// <summary>A derived capability binding row: its effective integer value, or `none` with no active row.</summary>
        DerivedValue = 0,

        /// <summary>A derived capability binding row: the stable label of the installation that supports it.</summary>
        DerivedProvider = 1,

        /// <summary>An authoritative gameplay value the genre's own state owner wrote (P-034).</summary>
        OwnedState = 2,

        /// <summary>A pure projection of the values this table observed (07's "next set awards 12").</summary>
        Projection = 3,

        /// <summary>A world-level reading the runner owns: the committed assembly, the mode, the step.</summary>
        World = 4,

        /// <summary>A durable outbox reading of the cross-family combination (07 s5, P-045).</summary>
        Outbox = 5,
    }

    /// <summary>One canonical field of the conformance vocabulary.</summary>
    public readonly struct ConformanceField
    {
        public ConformanceField(string key, ConformanceFieldKind kind, string subject, string property, string note)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Kind = kind;
            Subject = subject ?? throw new ArgumentNullException(nameof(subject));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Note = note ?? string.Empty;
        }

        /// <summary>The canonical field key a trace entry uses.</summary>
        public string Key { get; }

        /// <summary>What kind of reading this field demands.</summary>
        public ConformanceFieldKind Kind { get; }

        /// <summary>The stable target name (or `world` / `outbox`) this field is about.</summary>
        public string Subject { get; }

        /// <summary>The property name inside the subject.</summary>
        public string Property { get; }

        /// <summary>What the reading means, and which 00/07 clause demands it.</summary>
        public string Note { get; }

        public override string ToString() => Key;
    }

    /// <summary>
    /// The closed field vocabulary of GC-024's conformance tables. A family answers exactly these keys; an unknown
    /// key is a reported failure, which is what keeps "the table observed something else" from passing.
    /// </summary>
    public static class ConformanceFields
    {
        // ------------------------------------------------------------------ world-level

        /// <summary>The world's published propagation mode: `automatic` or `conservative` (P-013).</summary>
        public const string WorldMode = "world.mode";

        /// <summary>The committed composition revision the world published (P-006).</summary>
        public const string WorldRevision = "world.revision";

        /// <summary>The committed assembly epoch the world published (P-006).</summary>
        public const string WorldEpoch = "world.epoch";

        /// <summary>The committed logical step (P-006).</summary>
        public const string WorldStep = "world.step";

        // ------------------------------------------------------------------ card market (07 s2)

        /// <summary>`EffectiveSetBonus { Value }` of one seat: the `cards.set-bonus` row's effective value (P-019).</summary>
        public static string SeatBonus(uint ordinal) => Seat(ordinal) + ".bonus";

        /// <summary>The installation supporting that row, so a provider change is observable (P-017, P-025).</summary>
        public static string SeatBonusProvider(uint ordinal) => Seat(ordinal) + ".bonus-provider";

        /// <summary>`SeatScore { Total }`: the accumulated score only a committed set changes (07 s2.2).</summary>
        public static string SeatTotal(uint ordinal) => Seat(ordinal) + ".total";

        /// <summary>The seat's held cards as a canonical ascending set of `c&lt;id&gt;` tokens (07 s2.2).</summary>
        public static string SeatHand(uint ordinal) => Seat(ordinal) + ".hand";

        /// <summary>How many cards the seat holds: the hand half of a settlement's conservation check (P-044).</summary>
        public static string SeatHandSize(uint ordinal) => Seat(ordinal) + ".hand-size";

        /// <summary>Whether the seat is seated and therefore accepts commands (07 s2.2).</summary>
        public static string SeatSeated(uint ordinal) => Seat(ordinal) + ".seated";

        /// <summary>
        /// The score the seat's next valid set would award: the base score plus the effective bonus, which is 07's
        /// "their next valid sets award 12" column read as the pure rule it is (07 s2.3, P-019).
        /// </summary>
        public static string SeatNextAward(uint ordinal) => Seat(ordinal) + ".next-award";

        /// <summary>`TableState.ActiveSeat`: the seat whose turn it is (07 s2.2).</summary>
        public const string TableActiveSeat = "table-1.active-seat";

        /// <summary>`TableState.TurnNumber`: advanced only by an accepted settlement (07 s2.2).</summary>
        public const string TableTurn = "table-1.turn";

        /// <summary>`TableState.Version`: the bounded commit version a stale request is refused against (07 s2.2).</summary>
        public const string TableVersion = "table-1.version";

        /// <summary>The stable target name of one market seat ordinal (07 s2.1).</summary>
        public static string Seat(uint ordinal)
        {
            switch (ordinal)
            {
                case 0U:
                    return "seat-a";
                case 1U:
                    return "seat-b";
                case 2U:
                    return "seat-c";
                case 3U:
                    return "seat-d";
                default:
                    return "seat-" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The practice seat: eligible, beneath the festival provider, and behind an isolation boundary (P-016).</summary>
        public const string PracticeSeatBonus = "practice-seat.bonus";

        /// <summary>The scoreboard view: no card rule selects its recipe, so it stays ineligible (P-015).</summary>
        public const string ScoreboardBonus = "scoreboard.bonus";

        // ------------------------------------------------------------------ chapter quest (07 s3)

        /// <summary>`DialogueBinding`: the chapter ordinal bound to one conversation target (07 s3.1, P-019).</summary>
        public static string DialogueBinding(string target) => target + ".dialogue-binding";

        /// <summary>`GateConditionBinding`: the chapter ordinal bound to one gate target (07 s3.1).</summary>
        public static string GateBinding(string target) => target + ".gate-binding";

        /// <summary>`EncounterHookBinding`: the chapter ordinal bound to one encounter target (07 s3.1).</summary>
        public static string EncounterBinding(string target) => target + ".encounter-binding";

        /// <summary>`QuestFact[] { Value }` of the durable bridge-permit fact (07 s3.2, P-032).</summary>
        public const string BridgePermit = "quest-ledger.bridge-permit";

        /// <summary>That fact's version: a stale gate evaluation is observable through it (07 s3.2, P-032).</summary>
        public const string BridgePermitVersion = "quest-ledger.bridge-permit-version";

        /// <summary>`ConversationState { Status }`: the current conversation's lifecycle (07 s3.2).</summary>
        public const string MaraConversationStatus = "npc-mara.conversation-status";

        /// <summary>`ConversationState { NodeId }`: the node the conversation sits on (07 s3.2).</summary>
        public const string MaraConversationNode = "npc-mara.conversation-node";

        /// <summary>`GateState { IsOpen }`: the decision derived from the last accepted evaluation (07 s3.2).</summary>
        public const string GateEastDecision = "gate-east.gate-decision";

        /// <summary>`EncounterState { Status }` of the grove encounter (07 s3.2).</summary>
        public const string EncounterOakStatus = "encounter-oak.encounter-status";

        // ------------------------------------------------------------------ traversal (07 s4)

        /// <summary>`EffectiveAcceleration.X`: the runner's additional x acceleration in thousandths (07 s4.1).</summary>
        public static string RunnerAccelerationX(string runner) => runner + ".acceleration.x";

        /// <summary>The installation supporting that modifier row (07 s4.1, P-017).</summary>
        public static string RunnerAccelerationProvider(string runner) => runner + ".acceleration-provider";

        /// <summary>`KinematicPose` as `(x,y,z)` in millimetres (07 s4.2).</summary>
        public static string RunnerPose(string runner) => runner + ".pose";

        /// <summary>`Velocity` as `(x,y,z)` in thousandths of a metre per second (07 s4.2).</summary>
        public static string RunnerVelocity(string runner) => runner + ".velocity";

        /// <summary>`JumpState`: whether the runner is airborne, and the step it last jumped on (07 s4.2).</summary>
        public static string RunnerJump(string runner) => runner + ".jump";

        /// <summary>`RunProgress.Count`: deduplicated ordered checkpoint progress (07 s4.2).</summary>
        public static string RunnerProgress(string runner) => runner + ".progress";

        /// <summary>
        /// The traversal course's complete-opt-in runner target (P-013): 07:244's row names it as the runner that
        /// keeps the modifier in `Conservative` while every automatically eligible runner loses it, so it is a
        /// subject of its own rather than one of the automatically eligible runners.
        /// </summary>
        public const string OptedInRunner = "gc020.traversal.opted-in-runner";

        // ------------------------------------------------------------------ cross-family (07 s5)

        /// <summary>Open obligations in the durable outbox: a reward admitted and not yet settled (P-045).</summary>
        public const string OutboxOpen = "outbox.open";

        /// <summary>Acknowledged obligations: a destination confirmed the effect (P-045).</summary>
        public const string OutboxAcknowledged = "outbox.acknowledged";

        /// <summary>Attempts answered from the external idempotency key, so they mutated nothing (P-045).</summary>
        public const string OutboxAlreadyApplied = "outbox.already-applied";

        /// <summary>Destination mutations that really happened: the exactly-once count of the combination (P-045).</summary>
        public const string OutboxMutations = "outbox.mutations";

        /// <summary>Committed events the bridge recognised as reward sources (07 s5 step 1).</summary>
        public const string OutboxRecognised = "outbox.recognised";

        /// <summary>The recipient seat's held cards after the delivery: the destination state (P-034).</summary>
        public const string RewardRecipientHand = "card-tent.seat-a.hand";

        /// <summary>The holding seat's held cards: the unrelated state a reward must not disturb (P-034).</summary>
        public const string RewardHolderHand = "card-tent.seat-b.hand";

        /// <summary>How many cards the recipient holds: the destination effect's size (P-044).</summary>
        public const string RewardRecipientHandSize = "card-tent.seat-a.hand-size";

        /// <summary>How many cards the holding seat holds: conserved by a transfer, disturbed by a bug (P-044).</summary>
        public const string RewardHolderHandSize = "card-tent.seat-b.hand-size";

        /// <summary>The recipient's accumulated score: unrelated authoritative state a reward must not touch (P-034).</summary>
        public const string RewardRecipientTotal = "card-tent.seat-a.total";

        /// <summary>The holding seat's accumulated score: unrelated authoritative state (P-034).</summary>
        public const string RewardHolderTotal = "card-tent.seat-b.total";

        /// <summary>The combined world's card table version: unrelated authoritative state (P-025).</summary>
        public const string RewardTableVersion = "card-tent.table-1.version";

        /// <summary>
        /// The one target whose compatibility descriptor declares the complete explicit opt-in (P-013). 07:103's
        /// mode row names it as the target that retains the derived contribution in `Conservative` while every
        /// automatically eligible descendant loses it, so it needs a field of its own: it is not one of the
        /// automatically eligible targets and must not be confused with one.
        /// </summary>
        public const string OptedInSeatBonus = "cards.opted-in-seat.bonus";

        /// <summary>The installation supporting the opted-in seat's row, so a provider change stays observable.</summary>
        public const string OptedInSeatBonusProvider = "cards.opted-in-seat.bonus-provider";

        /// <summary>The opted-in seat's projection, so its retained contribution is observed as a real award.</summary>

        /// <summary>Every field the card market's table observes, in canonical order.</summary>
        public static IReadOnlyList<ConformanceField> Cards()
        {
            return new List<ConformanceField>
            {
                Derived(SeatBonus(0U), Seat(0U), "bonus"),
                Derived(SeatBonus(1U), Seat(1U), "bonus"),
                Derived(SeatBonus(2U), Seat(2U), "bonus"),
                Derived(SeatBonus(3U), Seat(3U), "bonus"),
                Derived(PracticeSeatBonus, "practice-seat", "bonus"),
                Derived(ScoreboardBonus, "scoreboard", "bonus"),
                Provider(SeatBonusProvider(1U), Seat(1U), "bonus-provider"),
                Derived(OptedInSeatBonus, "cards.opted-in-seat", "bonus"),
                Provider(OptedInSeatBonusProvider, "cards.opted-in-seat", "bonus-provider"),
                Owned(SeatTotal(0U), Seat(0U), "total"),
                Owned(SeatTotal(1U), Seat(1U), "total"),
                Owned(SeatHand(0U), Seat(0U), "hand"),
                Owned(SeatHand(1U), Seat(1U), "hand"),
                Owned(SeatHandSize(0U), Seat(0U), "hand-size"),
                Owned(SeatHandSize(1U), Seat(1U), "hand-size"),
                Owned(SeatSeated(0U), Seat(0U), "seated"),
                Projection(SeatNextAward(0U), Seat(0U), "next-award",
                    "the base set score plus the seat's effective bonus (07 s2.3)"),
                Projection(SeatNextAward(1U), Seat(1U), "next-award",
                    "the base set score plus the seat's effective bonus (07 s2.3)"),
                Owned(TableActiveSeat, "table-1", "active-seat"),
                Owned(TableTurn, "table-1", "turn"),
                Projection(OptedInSeatNextAward, "cards.opted-in-seat", "next-award",
                    "the base set score plus the opted-in seat's effective bonus (07 s2.3, P-013)"),
                World(WorldMode, "world", "mode"),
            };
        }

        /// <summary>Every field the chapter quest's table observes, in canonical order.</summary>
        public static IReadOnlyList<ConformanceField> Narrative()
        {
            return new List<ConformanceField>
            {
                Derived(DialogueBinding("npc-mara"), "npc-mara", "dialogue-binding"),
                Derived(DialogueBinding("npc-display"), "npc-display", "dialogue-binding"),
                Derived(DialogueBinding("crowd-prop"), "crowd-prop", "dialogue-binding"),
                Derived(DialogueBinding("npc-newcomer"), "npc-newcomer", "dialogue-binding"),
                Derived(DialogueBinding("npc-sailor"), "npc-sailor", "dialogue-binding"),
                Derived(GateBinding("gate-east"), "gate-east", "gate-binding"),
                Derived(EncounterBinding("encounter-oak"), "encounter-oak", "encounter-binding"),
                Owned(BridgePermit, "quest-ledger", "bridge-permit"),
                Owned(BridgePermitVersion, "quest-ledger", "bridge-permit-version"),
                Owned(MaraConversationStatus, "npc-mara", "conversation-status"),
                Owned(MaraConversationNode, "npc-mara", "conversation-node"),
                Owned(GateEastDecision, "gate-east", "gate-decision"),
                Owned(EncounterOakStatus, "encounter-oak", "encounter-status"),
                World(WorldMode, "world", "mode"),
            };
        }

        /// <summary>Every field the traversal challenge's table observes, in canonical order.</summary>
        public static IReadOnlyList<ConformanceField> Traversal()
        {
            var fields = new List<ConformanceField>();
            string[] runners = { "runner-a", "runner-b", "runner-c", "runner-display", OptedInRunner };
            for (int i = 0; i < runners.Length; i++)
            {
                fields.Add(Derived(RunnerAccelerationX(runners[i]), runners[i], "acceleration.x"));
            }

            for (int i = 0; i < runners.Length; i++)
            {
                fields.Add(Provider(RunnerAccelerationProvider(runners[i]), runners[i], "acceleration-provider"));
            }

            fields.Add(Owned(RunnerPose("runner-a"), "runner-a", "pose"));
            fields.Add(Owned(RunnerVelocity("runner-a"), "runner-a", "velocity"));
            fields.Add(Owned(RunnerJump("runner-a"), "runner-a", "jump"));
            fields.Add(Owned(RunnerProgress("runner-a"), "runner-a", "progress"));
            fields.Add(Owned(RunnerPose("runner-b"), "runner-b", "pose"));
            fields.Add(Owned(RunnerVelocity("runner-b"), "runner-b", "velocity"));
            fields.Add(Owned(RunnerProgress("runner-b"), "runner-b", "progress"));
            fields.Add(World(WorldMode, "world", "mode"));
            return fields;
        }

        /// <summary>Every field the cross-family combination's table observes, in canonical order.</summary>
        public static IReadOnlyList<ConformanceField> Cross()
        {
            return new List<ConformanceField>
            {
                Owned(BridgePermit, "quest-ledger", "bridge-permit"),
                Owned(BridgePermitVersion, "quest-ledger", "bridge-permit-version"),
                Owned(MaraConversationStatus, "npc-mara", "conversation-status"),
                Outbox(OutboxRecognised, "outbox", "recognised"),
                Outbox(OutboxOpen, "outbox", "open"),
                Outbox(OutboxAcknowledged, "outbox", "acknowledged"),
                Outbox(OutboxAlreadyApplied, "outbox", "already-applied"),
                Outbox(OutboxMutations, "outbox", "mutations"),
                Owned(RewardRecipientHand, "card-tent", "seat-a.hand"),
                Owned(RewardHolderHand, "card-tent", "seat-b.hand"),
                Owned(RewardRecipientHandSize, "card-tent", "seat-a.hand-size"),
                Owned(RewardHolderHandSize, "card-tent", "seat-b.hand-size"),
                Owned(RewardRecipientTotal, "card-tent", "seat-a.total"),
                Owned(RewardHolderTotal, "card-tent", "seat-b.total"),
                Owned(RewardTableVersion, "card-tent", "table-1.version"),
                World(WorldStep, "world", "step"),
            };
        }

        /// <summary>Every field of one table id, or an empty list for an unknown id.</summary>
        public static IReadOnlyList<ConformanceField> Of(string tableId)
        {
            switch (tableId)
            {
                case "cards":
                    return Cards();
                case "narrative":
                    return Narrative();
                case "traversal":
                    return Traversal();
                case "cross":
                    return Cross();
                default:
                    return Array.Empty<ConformanceField>();
            }
        }

        private static ConformanceField Derived(string key, string subject, string property)
            => new ConformanceField(key, ConformanceFieldKind.DerivedValue, subject, property,
                "the effective value of the derived binding row this target publishes (P-019)");

        private static ConformanceField Provider(string key, string subject, string property)
            => new ConformanceField(key, ConformanceFieldKind.DerivedProvider, subject, property,
                "the installation supporting the derived row, so a provider change is observable (P-017)");

        private static ConformanceField Owned(string key, string subject, string property)
            => new ConformanceField(key, ConformanceFieldKind.OwnedState, subject, property,
                "authoritative gameplay state its single owner writes (P-034)");

        private static ConformanceField Projection(string key, string subject, string property, string note)
            => new ConformanceField(key, ConformanceFieldKind.Projection, subject, property, note);

        private static ConformanceField World(string key, string subject, string property)
            => new ConformanceField(key, ConformanceFieldKind.World, subject, property,
                "world-level committed state the runner reads (P-006)");

        private static ConformanceField Outbox(string key, string subject, string property)
            => new ConformanceField(key, ConformanceFieldKind.Outbox, subject, property,
                "the durable outbox's own counter or state (P-045)");
    }
}
