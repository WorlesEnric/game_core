// GameCore.Gameplay.Integration - the reward bridge's destination port (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-034 (one declared owner per authoritative domain; the
// bridge never writes the card table's components), P-042 (cross-owner mutation requires an ordinary typed command;
// `Accepted` is not `Committed`) and P-045 (an irreversible output adapter uses an explicit external idempotency key,
// and a redelivery of one key applies the mutation once).
//
// THE SHAPE OF THE PORT
//
// The bridge owns no card state. It submits the card family's own `Transfer` command through the world's ordinary
// command ingress and reads the card family's own committed result back. Concretely, one delivery attempt is:
//
//   1. read the recipient's current hand (the card package's `CardTableAccess.ReadCards`);
//   2. if the reward card is already there, the mutation is already applied: report `AlreadyApplied`, which is what
//      makes a redelivery after acknowledgement loss a no-op (P-045);
//   3. otherwise submit `CardCommandKind.Transfer` from the holding seat to the recipient seat and pump the world;
//   4. read the destination's own `RequestResult` for that operation identity and translate it: `Committed` is
//      `Applied`, a rejection whose reason is transient is `Unavailable`, and a rejection whose reason is terminal is
//      `Refused`;
//   5. if the attempt was made under the *same* obligation identity as an earlier attempt whose operation identity
//      already committed a different mutation, report `Compensated` with the recorded reason, because the protocol
//      forbids pretending that the earlier committed mutation can be undone (P-003).
//
// Step 2 is a *reading* of the destination's authority, not a second copy of it: the hand is the card package's
// storage, read through the card package's own accessor (P-034). Step 4 is likewise read back from the kernel's
// request ledger, so the bridge never infers gameplay success from admission (P-042).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Cards;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Gameplay.Integration.RewardOutbox
{
    /// <summary>What one reward delivery attempt did, before it is mapped to a delivery outcome (P-042).</summary>
    public enum RewardAttemptStatus
    {
        /// <summary>The reward card is already in the recipient's hand; nothing was submitted.</summary>
        AlreadyPresent = 0,

        /// <summary>The command committed and the card moved.</summary>
        Committed = 1,

        /// <summary>The destination could not accept the command yet; the obligation stays open.</summary>
        Unavailable = 2,

        /// <summary>An earlier attempt under this obligation identity committed a different mutation.</summary>
        ConflictingMutationCommitted = 3,

        /// <summary>The destination terminally refused the reward.</summary>
        Refused = 4,

        /// <summary>The bridge could not observe the destination at all (no module, no seat, no table).</summary>
        DestinationMissing = 5,
    }

    /// <summary>Why one reward attempt ended the way it did, in the destination's own vocabulary (P-052).</summary>
    public readonly struct RewardAttemptReport
    {
        public RewardAttemptReport(
            RewardAttemptStatus status,
            DiagnosticCode code,
            string detail,
            CardSettlementStatus settlement,
            OperationId operation,
            int recipientCardsBefore,
            int recipientCardsAfter)
        {
            Status = status;
            Code = code;
            Detail = detail;
            Settlement = settlement;
            Operation = operation;
            RecipientCardsBefore = recipientCardsBefore;
            RecipientCardsAfter = recipientCardsAfter;
        }

        public readonly RewardAttemptStatus Status;

        public readonly DiagnosticCode Code;

        public readonly string Detail;

        /// <summary>The card package's own settlement status, so no second vocabulary is invented (P-052).</summary>
        public readonly CardSettlementStatus Settlement;

        /// <summary>The operation identity this attempt used; a retry of one obligation reuses it (P-050).</summary>
        public readonly OperationId Operation;

        public readonly int RecipientCardsBefore;

        public readonly int RecipientCardsAfter;

        /// <summary>True when the recipient's hand really gained the reward card in this attempt.</summary>
        public bool Applied =>
            Status == RewardAttemptStatus.Committed
            && RecipientCardsAfter == RecipientCardsBefore + 1;

        public override string ToString() =>
            "rewardAttempt(" + Status.ToString() + "," + Settlement.ToString() + ",cards="
            + RecipientCardsBefore.ToString(CultureInfo.InvariantCulture) + "->"
            + RecipientCardsAfter.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The reward bridge's destination port: a card table that receives reward transfers on one declared seat
    /// (P-034's single authority, expressed as a port rather than a second writer).
    /// </summary>
    public sealed class CardRewardDestination : IDestinationPort
    {
        private readonly UnityWorldHost host;
        private readonly WorldTimeDriver time;
        private readonly Id128 issuer;
        private readonly Dictionary<Id128, OperationId> operations = new Dictionary<Id128, OperationId>();
        private ulong nextSequence;

        public CardRewardDestination(
            UnityWorldHost host,
            WorldTimeDriver time,
            Id128 issuer,
            ulong firstSequence = 1UL)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.time = time ?? throw new ArgumentNullException(nameof(time));
            if (issuer.IsDefault)
            {
                throw new ArgumentException(
                    "The reward bridge is an issuer with its own stable identity (P-004).", nameof(issuer));
            }

            this.issuer = issuer;
            nextSequence = firstSequence == 0UL ? 1UL : firstSequence;
        }

        /// <summary>The card family's own command route identity is the delivery destination (P-004).</summary>
        public Id128 DestinationId => CardTableConstants.DestinationId;

        public SchemaRef CommandSchema => CardTableConstants.CommandSchema;

        /// <summary>Attempts that found the reward card already present (P-045).</summary>
        public int AlreadyPresentCount { get; private set; }

        /// <summary>Attempts that submitted a transfer command.</summary>
        public int SubmittedCount { get; private set; }

        /// <summary>Attempts whose committed result moved the card (the mutation really happened).</summary>
        public int CommittedCount { get; private set; }

        /// <summary>Attempts that could not reach the destination at all.</summary>
        public int MissingCount { get; private set; }

        /// <summary>Attempts that observed an earlier committed mutation under the same obligation (P-003).</summary>
        public int ConflictingMutationCount { get; private set; }

        /// <summary>The last attempt's report, so a scenario can assert the destination's own words (P-052).</summary>
        public RewardAttemptReport LastReport { get; private set; }

        /// <summary>
        /// Attempts one reward delivery. The attempt carries the obligation identity, so the port can reuse one
        /// operation identity for every attempt at that obligation — which is exactly what P-050 requires of a retry
        /// and what lets the kernel's own deduplication answer a retransmission with the recorded result.
        /// </summary>
        public DestinationOutcome TryApply(
            in DeliveryAttempt attempt,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            RewardAttemptReport report = ApplyReward(attempt);
            LastReport = report;
            code = report.Code;
            detail = report.Detail;

            switch (report.Status)
            {
                case RewardAttemptStatus.AlreadyPresent:
                    AlreadyPresentCount++;
                    return DestinationOutcome.AlreadyApplied;

                case RewardAttemptStatus.Committed:
                    CommittedCount++;
                    return DestinationOutcome.Applied;

                case RewardAttemptStatus.Unavailable:
                case RewardAttemptStatus.DestinationMissing:
                    MissingCount++;
                    return DestinationOutcome.Unavailable;

                case RewardAttemptStatus.ConflictingMutationCommitted:
                    ConflictingMutationCount++;
                    return DestinationOutcome.Compensated;

                default:
                    return DestinationOutcome.Refused;
            }
        }

        /// <summary>
        /// One reward attempt, decomposed so a test can read the destination's own reason for each outcome. It is the
        /// only place in this package that performs a card mutation, and it does so by submitting the card family's
        /// own declared command (P-042).
        /// </summary>
        public RewardAttemptReport ApplyReward(in DeliveryAttempt attempt)
        {
            DeliveryObligation obligation = attempt.Obligation;
            if (!TryDecodeReward(obligation, out RewardDefinition? definition, out string decodeDetail))
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Refused,
                    DiagnosticCode.UnsupportedVersion,
                    decodeDetail,
                    CardSettlementStatus.Committed,
                    default(OperationId),
                    0,
                    0);
            }

            RewardDefinition reward = definition!;

            if (!CardTableModule.TryGet(host.EntityWorld, out CardTableModule? module) || module == null)
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.DestinationMissing,
                    DiagnosticCode.MissingDependency,
                    "the card table module is not attached to world " + host.DiagnosticName
                    + "; the destination cannot be observed (P-034).",
                    CardSettlementStatus.Committed,
                    default(OperationId),
                    0,
                    0);
            }

            EntityManager entityManager = host.EntityWorld.EntityManager;
            if (!module.TrySeat(reward.RecipientSeat, out Entity recipient))
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.DestinationMissing,
                    DiagnosticCode.StaleHandle,
                    "seat " + reward.RecipientSeat.ToString(CultureInfo.InvariantCulture)
                    + " of world " + host.DiagnosticName + " is not bound; nothing was submitted (P-005).",
                    CardSettlementStatus.Committed,
                    default(OperationId),
                    0,
                    0);
            }

            IReadOnlyList<CardId> before = CardTableAccess.ReadCards(entityManager, recipient);
            if (Contains(before, reward.Card))
            {
                // The destination's own storage already carries the reward: this attempt owes it nothing. Reporting
                // AlreadyApplied is what makes a redelivery after acknowledgement loss a single mutation (P-045).
                return new RewardAttemptReport(
                    RewardAttemptStatus.AlreadyPresent,
                    DiagnosticCode.None,
                    "the reward card is already in seat "
                    + reward.RecipientSeat.ToString(CultureInfo.InvariantCulture)
                    + "'s hand; the mutation this obligation names is already applied (P-045).",
                    CardSettlementStatus.Committed,
                    default(OperationId),
                    before.Count,
                    before.Count);
            }

            if (!module.TrySeat(reward.HoldingSeat, out Entity holder))
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Refused,
                    DiagnosticCode.StaleHandle,
                    "the reward's holding seat " + reward.HoldingSeat.ToString(CultureInfo.InvariantCulture)
                    + " is not bound, so the reward card cannot be drawn from it (P-052).",
                    CardSettlementStatus.RejectedCardNotHeld,
                    default(OperationId),
                    before.Count,
                    before.Count);
            }

            IReadOnlyList<CardId> held = CardTableAccess.ReadCards(entityManager, holder);
            if (!Contains(held, reward.Card))
            {
                // Terminal by content: no future attempt draws a card the destination does not stock. Recorded as a
                // rejection with the card family's own status rather than retried forever (P-052).
                return new RewardAttemptReport(
                    RewardAttemptStatus.Refused,
                    DiagnosticCode.ResourceUnavailable,
                    "the reward card is not stocked at holding seat "
                    + reward.HoldingSeat.ToString(CultureInfo.InvariantCulture)
                    + "; content repair is required, so this is a terminal rejection (P-052).",
                    CardSettlementStatus.RejectedCardNotHeld,
                    default(OperationId),
                    before.Count,
                    before.Count);
            }

            CardTableState table = CardTableAccess.ReadTable(entityManager, module.TableEntity);
            OperationId operation = OperationFor(obligation.Key.OutboxId);
            // A transfer names one candidate card; the second and third slots are the card family's own "no card"
            // value (`CardId.IsNone` is `Value == 0UL`), which is how it encodes an unused slot.
            var noCard = new CardId(0UL);
            var command = new CardCommandPayload(
                CardCommandKind.Transfer,
                reward.HoldingSeat,
                reward.RecipientSeat,
                table.TableVersion,
                reward.Card,
                noCard,
                noCard);

            var envelope = new CommandEnvelope(
                operation,
                CardTableKeys.CommandRoute,
                CardIdentity.Target(CardVocabulary.TableOne),
                CardTableKeys.CommandSchema,
                null,
                CardPayloadCodec.WriteCommand(command));

            SubmittedCount++;
            CommandAdmissionReceipt receipt = host.Submit(envelope);
            if (!receipt.Admitted)
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Unavailable,
                    receipt.Result.Reason,
                    "the world did not admit the reward transfer: " + receipt.Result.Kind.ToString()
                    + "/" + DiagnosticCodeText.Of(receipt.Result.Reason)
                    + "; the obligation stays open (P-042).",
                    CardSettlementStatus.Committed,
                    operation,
                    before.Count,
                    before.Count);
            }

            // Nothing the bridge does advances the world by itself: the ordinary pump commits the step the admitted
            // command needs, exactly as it would for a player-submitted command (07 s5 step 3, P-036).
            TimeFrameReport frame = time.PumpFrame(RewardHostTicks);
            int after = CardTableAccess.ReadCards(entityManager, recipient).Count;

            WorldMessagePlane? plane = host.Messages;
            if (plane == null || !plane.Requests.TryGet(operation, out RequestRow? row) || row == null)
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Unavailable,
                    DiagnosticCode.ResourceUnavailable,
                    "the destination's result for this reward attempt is not readable yet; the obligation stays open "
                    + "(P-042).",
                    CardSettlementStatus.Committed,
                    operation,
                    before.Count,
                    after);
            }

            RequestResultKind kind = row.Outcome.Kind;
            if (kind == RequestResultKind.Committed && after == before.Count + 1)
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Committed,
                    DiagnosticCode.None,
                    "the reward transfer committed in " + frame.StepsCommitted.ToString(CultureInfo.InvariantCulture)
                    + " step(s); seat " + reward.RecipientSeat.ToString(CultureInfo.InvariantCulture)
                    + "'s hand grew from " + before.Count.ToString(CultureInfo.InvariantCulture) + " to "
                    + after.ToString(CultureInfo.InvariantCulture) + " (P-042).",
                    CardSettlementStatus.Committed,
                    operation,
                    before.Count,
                    after);
            }

            if (kind == RequestResultKind.Committed)
            {
                // Committed, yet the hand did not gain the card: another mutation under this identity already
                // happened. The protocol forbids pretending that a committed mutation can be undone (P-003), so the
                // adapter is told to record an explicit compensation instead of a false success.
                return new RewardAttemptReport(
                    RewardAttemptStatus.ConflictingMutationCommitted,
                    DiagnosticCode.IdempotencyConflict,
                    "operation " + operation.ToString() + " committed but seat "
                    + reward.RecipientSeat.ToString(CultureInfo.InvariantCulture) + "'s hand is "
                    + after.ToString(CultureInfo.InvariantCulture) + " card(s) rather than "
                    + (before.Count + 1).ToString(CultureInfo.InvariantCulture)
                    + "; a different mutation already committed under this identity, which cannot be undone (P-003).",
                    CardSettlementStatus.Committed,
                    operation,
                    before.Count,
                    after);
            }

            CardSettlementStatus settlement = SettlementOf(row, operation);
            DiagnosticCode refusal = row.Outcome.Reason == DiagnosticCode.None
                ? DiagnosticCode.OwnershipConflict
                : row.Outcome.Reason;

            if (IsTransient(settlement))
            {
                return new RewardAttemptReport(
                    RewardAttemptStatus.Unavailable,
                    refusal,
                    "the destination did not accept the reward transfer yet (" + settlement.ToString()
                    + "); the obligation stays open (P-042).",
                    settlement,
                    operation,
                    before.Count,
                    after);
            }

            return new RewardAttemptReport(
                RewardAttemptStatus.Refused,
                refusal,
                "the destination terminally refused the reward transfer (" + settlement.ToString()
                + "); the refusal is recorded rather than retried (P-052).",
                settlement,
                operation,
                before.Count,
                after);
        }

        /// <summary>
        /// The operation identity of one obligation. Every attempt at one obligation reuses its identity, which is
        /// P-050's "the same attempt retains its operation ID", and is what lets the kernel answer a retransmission
        /// from its recorded result instead of executing the command twice.
        /// </summary>
        public OperationId OperationFor(Id128 outboxId)
        {
            if (operations.TryGetValue(outboxId, out OperationId existing))
            {
                return existing;
            }

            var created = new OperationId(host.World, issuer, nextSequence);
            nextSequence++;
            operations.Add(outboxId, created);
            return created;
        }

        /// <summary>Host ticks each reward frame is pumped at; the value the card fixture already uses.</summary>
        public const ulong RewardHostTicks = 1_000_000UL;

        public override string ToString() =>
            "cardRewardDestination(" + DestinationId.ToString() + ",submitted="
            + SubmittedCount.ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>
        /// Reads the reward definition out of the obligation's recorded payload. The payload *is* the definition's
        /// stable key, so a restore reinstates an obligation that carries its own reward without a second lookup
        /// table; a payload that does not decode is a terminal refusal, never a defaulted reward (P-054).
        /// </summary>
        private static bool TryDecodeReward(
            DeliveryObligation obligation,
            out RewardDefinition? definition,
            out string detail)
        {
            definition = null;
            detail = string.Empty;
            byte[] bytes = obligation.PayloadBytes();
            if (bytes.Length != RewardPayloadCodec.PayloadBytes)
            {
                detail = "a reward obligation carries " + bytes.Length.ToString(CultureInfo.InvariantCulture)
                    + " byte(s) and the reward payload is " + RewardPayloadCodec.PayloadBytes.ToString(
                        CultureInfo.InvariantCulture) + " (P-054).";
                return false;
            }

            return RewardPayloadCodec.TryRead(bytes, out definition, out detail);
        }

        private static bool Contains(IReadOnlyList<CardId> cards, CardId card)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Equals(card))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The destination's own settlement status for an attempt, read from its committed result event when one was
        /// published and otherwise from the request ledger's own rejection reason. It is a reading of the card
        /// family's vocabulary, not a second one (P-034, P-052).
        /// </summary>
        private CardSettlementStatus SettlementOf(RequestRow row, OperationId operation)
        {
            WorldMessagePlane? plane = host.Messages;
            if (plane != null)
            {
                CommittedEventPage page = plane.ReadEvents(new EventCursor(host.World, EventSequence.Zero), 32);
                if (page.Outcome == CursorOutcome.Ok)
                {
                    for (int i = page.Events.Count - 1; i >= 0; i--)
                    {
                        CommittedEvent committed = page.Events[i];
                        if (!committed.Schema.Equals(CardTableConstants.ResultSchema)
                            || !committed.CausalRequest.Equals(operation))
                        {
                            continue;
                        }

                        if (CardResultCodec.TryRead(committed.Payload.Bytes, out CardResultPayload result))
                        {
                            return result.Status;
                        }
                    }
                }
            }

            return SettlementFromReason(row.Outcome.Reason);
        }

        /// <summary>
        /// The card family's status that corresponds to the kernel code a rejected request carries. The mapping is
        /// deliberately conservative: an unclassified reason becomes a terminal rejection, because retrying a refusal
        /// whose cause is unknown is exactly the silent loop P-052 forbids.
        /// </summary>
        private static CardSettlementStatus SettlementFromReason(DiagnosticCode reason)
        {
            switch (reason)
            {
                case DiagnosticCode.BudgetExceeded:
                    return CardSettlementStatus.RejectedBoundedOverflow;

                case DiagnosticCode.StalePlan:
                    return CardSettlementStatus.RejectedStaleTableVersion;

                case DiagnosticCode.MissingDependency:
                    return CardSettlementStatus.RejectedSeatFull;

                case DiagnosticCode.StaleHandle:
                    return CardSettlementStatus.RejectedUnknownSeat;

                default:
                    return CardSettlementStatus.RejectedEmptyCandidateSet;
            }
        }

        /// <summary>
        /// Which of the card family's refusals a later attempt could plausibly survive. Only a full seat and a table
        /// whose version moved are transient: the first is real backpressure that a later drain can clear, and the
        /// second is a fresh attempt against the current version. Everything else is content or ownership, so it is
        /// terminal and is recorded rather than retried (07 s5's "an invalid reward definition is a terminal rejected
        /// outcome requiring content repair").
        /// </summary>
        private static bool IsTransient(CardSettlementStatus settlement) =>
            settlement == CardSettlementStatus.RejectedSeatFull
            || settlement == CardSettlementStatus.RejectedStaleTableVersion;
    }
}
