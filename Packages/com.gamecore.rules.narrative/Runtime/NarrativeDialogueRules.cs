// GameCore.Rules.Narrative — the dialogue stage's pure validation (07 section 3.2, P-042, P-044).
//
// `narrative.dialogue` validates a choice and may submit a `QuestMutationRequest`; it never writes a fact and
// never writes a gate. Everything it decides from is content plus the *current* conversation state of the
// addressed target, so the decision is an integer/boolean function of declared inputs:
//
//   * a choice is legal only at the node the conversation currently sits on, and only while the conversation is
//     idle or active — a second choice against a conversation in another status is refused instead of reopening it;
//   * accepting the permit choice requests exactly one durable fact mutation; declining requests none, which is
//     why the validation result carries an explicit "no mutation" shape rather than a zero value that could be
//     mistaken for `false`;
//   * the accepted decision also names the conversation state the dialogue owner writes, so the authoritative
//     conversation slot and the requested fact stay two separate owner-local writes (P-034).
//
// Every refusal carries a stable code (P-052) and its prose stays a diagnostic field, so the canonical trace never
// depends on a sentence.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Rules.Narrative
{
    /// <summary>
    /// One decoded narrative choice command: the dialogue node the choice is answered at and the choice itself.
    /// Both are small ordinals of the chapter's declared graph, never free text.
    /// </summary>
    public readonly struct NarrativeChoice : IEquatable<NarrativeChoice>
    {
        public readonly int NodeOrdinal;
        public readonly int ChoiceOrdinal;

        public NarrativeChoice(int nodeOrdinal, int choiceOrdinal)
        {
            NodeOrdinal = nodeOrdinal;
            ChoiceOrdinal = choiceOrdinal;
        }

        /// <summary>Bytes one encoded choice occupies: two big-endian int32 scalars (05 section 6).</summary>
        public const int EncodedLength = 8;

        public bool Equals(NarrativeChoice other)
            => NodeOrdinal == other.NodeOrdinal && ChoiceOrdinal == other.ChoiceOrdinal;

        public override bool Equals(object? obj) => obj is NarrativeChoice other && Equals(other);

        public override int GetHashCode() => (NodeOrdinal * 397) ^ ChoiceOrdinal;

        public override string ToString()
            => "choice(node=" + NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                + ", choice=" + ChoiceOrdinal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Conversation status values of the dialogue domain; plain integers, one per declared state.</summary>
    public static class NarrativeConversationStatus
    {
        /// <summary>No conversation is in progress; the state a new choice is legal in.</summary>
        public const int Idle = 0;

        /// <summary>A choice was accepted for this target and is being acted on.</summary>
        public const int Requested = 1;

        /// <summary>The conversation is active under the selected chapter's graph.</summary>
        public const int Active = 2;

        /// <summary>The conversation was closed by a declared transition (a state disposition, P-032).</summary>
        public const int Closed = 3;
    }

    /// <summary>What one choice validation decided, with the exact numbers the owner must write.</summary>
    public readonly struct ChoiceValidation
    {
        public ChoiceValidation(
            bool accepted,
            int resultingNodeOrdinal,
            int resultingStatus,
            string factKey,
            int factValue,
            string refusalCode,
            string reason)
        {
            Accepted = accepted;
            ResultingNodeOrdinal = resultingNodeOrdinal;
            ResultingStatus = resultingStatus;
            FactKey = factKey ?? string.Empty;
            FactValue = factValue;
            RefusalCode = refusalCode ?? NarrativeRefusals.None;
            Reason = reason ?? string.Empty;
        }

        /// <summary>True when the choice is legal at the conversation's current node and status.</summary>
        public bool Accepted { get; }

        /// <summary>Dialogue node the conversation sits on after this choice.</summary>
        public int ResultingNodeOrdinal { get; }

        /// <summary>Conversation status the dialogue owner writes for this choice.</summary>
        public int ResultingStatus { get; }

        /// <summary>Durable fact key this choice requests a mutation of; empty when the choice mutates nothing.</summary>
        public string FactKey { get; }

        /// <summary>Requested fact value; meaningful only when <see cref="RequestsFactMutation"/> is true.</summary>
        public int FactValue { get; }

        /// <summary>Stable refusal code (P-052); <see cref="NarrativeRefusals.None"/> when the choice was accepted.</summary>
        public string RefusalCode { get; }

        /// <summary>True when the accepted choice requests exactly one durable fact mutation.</summary>
        public bool RequestsFactMutation => Accepted && FactKey.Length != 0;

        /// <summary>Why a refused choice was refused; empty for an accepted one.</summary>
        public string Reason { get; }

        public override string ToString()
            => Accepted
                ? "accepted(node=" + ResultingNodeOrdinal.ToString(CultureInfo.InvariantCulture)
                    + ", status=" + ResultingStatus.ToString(CultureInfo.InvariantCulture)
                    + (RequestsFactMutation
                        ? ", fact=" + NarrativeFacts.Describe(FactKey, FactValue, NarrativeFacts.InitialVersion)
                        : ", no fact")
                    + ")"
                : "refused(" + RefusalCode + ": " + Reason + ")";
    }

    /// <summary>The reference chapter graph's choice rules and the conversation lifecycle transitions.</summary>
    public static class NarrativeDialogueRules
    {
        /// <summary>The choice that accepts the chapter's offer and requests the permit fact (07 section 3.2).</summary>
        public const int PermitChoice = 1;

        /// <summary>The choice that declines: accepted, names a resulting node, and mutates no fact.</summary>
        public const int DeclineChoice = 2;

        /// <summary>Node the conversation sits on after accepting the permit choice.</summary>
        public const int PermitResultNode = 2;

        /// <summary>Node the conversation sits on after declining: unchanged.</summary>
        public const int DeclineResultNode = 1;

        /// <summary>True when the ordinal is one of the declared choices of the reference graph.</summary>
        public static bool IsDeclaredChoice(int choiceOrdinal)
            => choiceOrdinal == PermitChoice || choiceOrdinal == DeclineChoice;

        /// <summary>
        /// Validates one choice against one chapter and the addressed target's current conversation state. A refusal
        /// carries a stable code and never carries a fact mutation, so a caller cannot mistake "refused" for "mutate
        /// to the current value" (P-042: admission is not gameplay success).
        /// </summary>
        public static ChoiceValidation Validate(
            ChapterDefinition chapter,
            in NarrativeChoice choice,
            int currentNodeOrdinal,
            int currentStatus)
        {
            if (chapter == null)
            {
                throw new ArgumentNullException(nameof(chapter));
            }

            if (currentStatus != NarrativeConversationStatus.Idle
                && currentStatus != NarrativeConversationStatus.Active)
            {
                return Refuse(
                    NarrativeRefusals.ConversationNotIdle,
                    "the conversation is " + currentStatus.ToString(CultureInfo.InvariantCulture)
                    + " and accepts no choice");
            }

            if (choice.NodeOrdinal != currentNodeOrdinal)
            {
                return Refuse(
                    NarrativeRefusals.NodeMismatch,
                    "choice names node " + choice.NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " but the conversation sits on node " + currentNodeOrdinal.ToString(CultureInfo.InvariantCulture));
            }

            if (!IsDeclaredChoice(choice.ChoiceOrdinal))
            {
                return Refuse(
                    NarrativeRefusals.UndeclaredChoice,
                    "choice " + choice.ChoiceOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " is not declared by chapter '" + chapter.ChapterTag + "'");
            }

            if (choice.ChoiceOrdinal == DeclineChoice)
            {
                // Declining is a legal, committed outcome that requests no durable change: the fact rule is never
                // asked to transition, so no second fact event can appear (P-044).
                return new ChoiceValidation(
                    true,
                    DeclineResultNode,
                    NarrativeConversationStatus.Active,
                    string.Empty,
                    NarrativeFacts.False,
                    NarrativeRefusals.None,
                    string.Empty);
            }

            return new ChoiceValidation(
                true,
                PermitResultNode,
                NarrativeConversationStatus.Active,
                chapter.GateConditionFactKey,
                NarrativeFacts.True,
                NarrativeRefusals.None,
                string.Empty);
        }

        private static ChoiceValidation Refuse(string refusalCode, string reason)
            => new ChoiceValidation(
                false,
                0,
                NarrativeConversationStatus.Idle,
                string.Empty,
                NarrativeFacts.False,
                refusalCode,
                reason);

        /// The declared conversation status transition table, in one place: admitting a choice moves an idle
        /// conversation to `Requested`, an admitted choice activates it, and an in-progress conversation closes.
        /// Every other pair is refused with a stable code and leaves the status untouched (P-052).
        /// </summary>
        public static bool TryTransition(
            int currentStatus,
            int requestedStatus,
            out int nextStatus,
            out string refusalCode)
        {
            nextStatus = currentStatus;
            refusalCode = NarrativeRefusals.None;

            if (requestedStatus == NarrativeConversationStatus.Requested
                && currentStatus == NarrativeConversationStatus.Idle)
            {
                nextStatus = NarrativeConversationStatus.Requested;
                return true;
            }

            if (requestedStatus == NarrativeConversationStatus.Active
                && currentStatus == NarrativeConversationStatus.Requested)
            {
                nextStatus = NarrativeConversationStatus.Active;
                return true;
            }

            if (requestedStatus == NarrativeConversationStatus.Closed
                && (currentStatus == NarrativeConversationStatus.Requested
                    || currentStatus == NarrativeConversationStatus.Active))
            {
                nextStatus = NarrativeConversationStatus.Closed;
                return true;
            }

            refusalCode = NarrativeRefusals.ConversationTransitionIllegal;
            return false;
        }

        /// <summary>
        /// The declared conversation transition when a choice is admitted for a target whose conversation is idle.
        /// A live conversation is not silently reopened; the transition is refused and the caller keeps its state.
        /// </summary>
        public static bool TryRequest(int currentStatus, out int nextStatus)
            => TryTransition(currentStatus, NarrativeConversationStatus.Requested, out nextStatus, out string _);

        /// <summary>The declared conversation transition from `Requested` to `Active`.</summary>
        public static bool TryActivate(int currentStatus, out int nextStatus)
            => TryTransition(currentStatus, NarrativeConversationStatus.Active, out nextStatus, out string _);

        /// <summary>
        /// The declared `CloseConversation` transition (07 section 3.3): an in-progress conversation becomes closed.
        /// It is idempotent — closing a closed conversation is not a transition — and it never touches a fact.
        /// </summary>
        public static bool TryClose(int currentStatus, out int nextStatus)
            => TryTransition(currentStatus, NarrativeConversationStatus.Closed, out nextStatus, out string _);

        /// <summary>
        /// The conversation domain's registered schema migration: version 1 holds the node ordinal a target had
        /// before a chapter graph covered it (zero), and version 2 initializes an idle session at the chapter's
        /// opening node. A negative source is refused rather than migrated, so a corrupt value is reported instead of
        /// silently becoming a legal node (P-029, P-032).
        /// </summary>
        public static bool TryUpgradeNodeToVersionTwo(int sourceNodeOrdinal, out int migratedNodeOrdinal)
        {
            migratedNodeOrdinal = sourceNodeOrdinal;
            if (sourceNodeOrdinal < 0)
            {
                return false;
            }

            if (sourceNodeOrdinal == 0)
            {
                // The chapter's opening node is the declared initialization of the migrated slot.
                migratedNodeOrdinal = NarrativeChapters.Get(NarrativeChapters.ChapterOneTag).OpeningNodeOrdinal;
            }

            return true;
        }
    }
}
