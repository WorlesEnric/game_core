// GameCore.Gameplay.Integration - the narrative-to-card reward bridge (GC-021).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5 (the cross-family composition: "the bridge
// explicitly depends on the committed quest output and the card command endpoint. It does not obtain permission to
// write the table's components." ... "`rewards.dispatch` is a managed post-publication observer, not an execution
// stage.") and docs/game-core/00-core-protocols.md P-001 (no hybrid kernel types: the bridge is an ordinary package
// that depends on two gameplay packages), P-003 (granting a card is committed gameplay; nothing here reverses it),
// P-042 (the card mutation crosses a typed command port, not a universal effect bus) and P-045 (the obligation is
// persisted by the delivery seam before it is delivered, so a crash cannot silently lose a reward).
//
// WHAT THIS TYPE IS
//
// The whole bridge, assembled from parts that already exist:
//
//   observe   `WorldDeliveryOwner.PollCommittedEvents` reads the world's committed events through the world's own
//             committed-event reader. This type is the `IDeliveryObligationSource` that claims
//             `narrative.schema.choice-committed` events and turns an accepted choice into a reward obligation.
//   deliver   `WorldDeliveryOwner.DispatchOpenObligations` hands each open obligation to
//             `CardRewardDestination`, which submits the card family's own `Transfer` command.
//   settle    the destination's result decides `Applied`, `AlreadyApplied`, `Unavailable`, `Refused` or
//             `Compensated`; the outbox records whichever it was.
//
// It owns no state of its own beyond the declared content (the reward catalog). Every obligation it commits is a row
// in the world's outbox, and every mutation it causes is a command the card family admitted and committed (P-034).
//
// NO NEW UNIVERSAL EFFECT API
//
// There is deliberately no `Apply`, `Undo`, `Effect` or `IEffect` here. The bridge has three verbs that name their
// own domain: `Poll` observes committed events, `Dispatch` hands an obligation to a destination, and the destination
// submits a card command. That is the whole surface, and it is what P-003 requires: a common reversible effect API
// would imply that a granted card, a retracted capability contribution and a released asset lease have the same
// semantics, and they do not.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Gameplay.Narrative;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Gameplay.Integration.RewardOutbox
{
    /// <summary>What one bridge pass observed and delivered, so a scenario can assert each half separately (P-052).</summary>
    public readonly struct RewardBridgePassReport
    {
        public RewardBridgePassReport(
            int eventsRead,
            int rewardsEnqueued,
            int rewardsRecognised,
            int dispatched,
            int acknowledged,
            int alreadyApplied,
            int unavailable,
            int rejected,
            int compensated,
            DiagnosticCode code,
            string detail)
        {
            EventsRead = eventsRead;
            RewardsEnqueued = rewardsEnqueued;
            RewardsRecognised = rewardsRecognised;
            Dispatched = dispatched;
            Acknowledged = acknowledged;
            AlreadyApplied = alreadyApplied;
            Unavailable = unavailable;
            Rejected = rejected;
            Compensated = compensated;
            Code = code;
            Detail = detail;
        }

        /// <summary>Committed events this pass read from the world's own committed-event reader (P-045).</summary>
        public readonly int EventsRead;

        /// <summary>Obligations this pass committed from an accepted choice (07 s5 step 2).</summary>
        public readonly int RewardsEnqueued;

        /// <summary>Committed choices this pass recognised as a reward, enqueued or already enqueued (P-050).</summary>
        public readonly int RewardsRecognised;

        /// <summary>Obligations this pass handed to the destination (07 s5 step 3).</summary>
        public readonly int Dispatched;

        public readonly int Acknowledged;

        public readonly int AlreadyApplied;

        public readonly int Unavailable;

        public readonly int Rejected;

        public readonly int Compensated;

        public readonly DiagnosticCode Code;

        public readonly string Detail;

        public override string ToString() =>
            "rewardBridgePass(events=" + EventsRead.ToString(CultureInfo.InvariantCulture)
            + ",enqueued=" + RewardsEnqueued.ToString(CultureInfo.InvariantCulture)
            + ",dispatched=" + Dispatched.ToString(CultureInfo.InvariantCulture)
            + ",acked=" + Acknowledged.ToString(CultureInfo.InvariantCulture)
            + ",already=" + AlreadyApplied.ToString(CultureInfo.InvariantCulture)
            + ",unavailable=" + Unavailable.ToString(CultureInfo.InvariantCulture)
            + ",rejected=" + Rejected.ToString(CultureInfo.InvariantCulture)
            + ",compensated=" + Compensated.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The narrative-to-card reward bridge: one content table, one delivery owner and one destination port, wired in
    /// one constructor so an application cannot assemble them inconsistently (GC-021, 07 s5).
    /// </summary>
    public sealed class NarrativeCardRewardBridge : IDeliveryObligationSource, IDisposable
    {
        private readonly RewardCatalog catalog;
        private readonly Id128 destinationId;

        public NarrativeCardRewardBridge(
            UnityWorldHost host,
            WorldTimeDriver time,
            Id128 ownerId,
            Id128 issuer,
            RewardCatalog catalog,
            int capacity,
            int terminalRetention,
            OutboxDurability durability,
            IDeliveryJournal? journal = null,
            IDeliveryStepHook? hook = null)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

            Owner = new WorldDeliveryOwner(host, ownerId, capacity, terminalRetention, durability, journal, hook);
            Destination = new CardRewardDestination(host, time, issuer);
            destinationId = Destination.DestinationId;

            if (!Owner.TryRegisterDestination(Destination, out string detail))
            {
                throw new ArgumentException(detail, nameof(catalog));
            }
        }

        public UnityWorldHost Host { get; }

        /// <summary>The world's delivery owner: the outbox, its journal and the per-owner event cursor (GC-021).</summary>
        public WorldDeliveryOwner Owner { get; }

        /// <summary>The card table as the reward destination; it owns every mutation this bridge causes (P-034).</summary>
        public CardRewardDestination Destination { get; }

        public RewardCatalog Catalog => catalog;

        /// <summary>Obligations committed from an accepted choice over this bridge's lifetime.</summary>
        public int EnqueuedCount { get; private set; }

        /// <summary>Recognised reward choices, duplicates included: one committed choice is one reward (P-050).</summary>
        public int RecognisedCount { get; private set; }

        /// <summary>Accepted choices the content table does not reward, counted rather than guessed at (P-015).</summary>
        public int UnrewardedChoiceCount { get; private set; }

        /// <summary>Passes run; each one is bounded work on a committed boundary (P-043).</summary>
        public int PassCount { get; private set; }

        /// <summary>The last pass's counts, so a scenario can assert both halves of one pass (P-052).</summary>
        public RewardBridgePassReport LastPass { get; private set; }

        /// <summary>Obligations reinstated from a checkpoint's outbox section (P-053).</summary>
        public int ReinstateCount { get; private set; }

        /// <summary>
        /// One bridge pass: observe the committed events this owner has not seen, commit one obligation per rewarded
        /// choice, then hand up to <paramref name="maxDispatches"/> open obligations to the card destination.
        ///
        /// The order is what 07 s5 describes and it is also the only safe one: an obligation exists before anything is
        /// handed over, so a crash between the two loses nothing (P-045).
        /// </summary>
        public RewardBridgePassReport Run(
            OperationId causal,
            int maxEvents,
            int maxDispatches)
        {
            PassCount++;
            int before = Owner.EnqueuedCount;
            int dispatchedBefore = Owner.DispatchAttemptCount;
            int ackedBefore = Owner.AcknowledgedCount;
            int appliedBefore = Destination.AlreadyPresentCount;
            int missingBefore = Destination.MissingCount;
            int rejectedBefore = Owner.RejectedCount;
            int compensatedBefore = Owner.CompensatedCount;

            int read = Owner.PollCommittedEvents(this, causal, maxEvents);
            EnqueuedCount = Owner.EnqueuedCount;

            int dispatched = Owner.DispatchOpenObligations(maxDispatches);

            var report = new RewardBridgePassReport(
                Owner.ObservedEventCount,
                Owner.EnqueuedCount - before,
                RecognisedCount,
                dispatched,
                Owner.AcknowledgedCount - ackedBefore,
                Destination.AlreadyPresentCount - appliedBefore,
                Destination.MissingCount - missingBefore,
                Owner.RejectedCount - rejectedBefore,
                Owner.CompensatedCount - compensatedBefore,
                DiagnosticCode.None,
                "bridge pass " + PassCount.ToString(CultureInfo.InvariantCulture) + ": read "
                + read.ToString(CultureInfo.InvariantCulture) + " event(s), dispatched "
                + (Owner.DispatchAttemptCount - dispatchedBefore).ToString(CultureInfo.InvariantCulture)
                + " obligation(s).");
            LastPass = report;
            return report;
        }

        /// <summary>
        /// Claims one committed choice event and describes the reward obligation it produces. An event whose schema is
        /// not the narrative family's committed-choice schema is not this source's, and is reported as unclaimed so a
        /// world with several sources can carry them all (P-045).
        /// </summary>
        public bool TryDescribe(in CommittedEvent committed, out DeliveryObligationRequest request)
        {
            request = default(DeliveryObligationRequest);
            if (!committed.Schema.Equals(NarrativeKeys.ChoiceCommittedSchema))
            {
                return false;
            }

            RecognisedCount++;
            if (!TryReadChoice(committed.Payload.Bytes, out int nodeOrdinal, out int status, out string detail))
            {
                LastDescribeDetail = detail;
                UnrewardedChoiceCount++;
                return false;
            }

            if (status != NarrativeConversationStatus.Active && status != NarrativeConversationStatus.Closed)
            {
                // A choice whose outcome left no active or closed conversation granted nothing. This is content
                // policy, not an error, so it is counted and reported rather than failed (P-015).
                LastDescribeDetail = "the committed choice reached status "
                    + status.ToString(CultureInfo.InvariantCulture) + ", which this bridge does not reward (P-015).";
                UnrewardedChoiceCount++;
                return false;
            }

            if (!catalog.TryGetDefinition(nodeOrdinal, out RewardDefinition? definition) || definition == null)
            {
                LastDescribeDetail = "no reward definition covers node "
                    + nodeOrdinal.ToString(CultureInfo.InvariantCulture)
                    + "; an unrewarded node is reported rather than guessed at (P-015).";
                UnrewardedChoiceCount++;
                return false;
            }

            LastDescribeDetail = string.Empty;
            request = new DeliveryObligationRequest(
                destinationId,
                CardTableConstants.CommandSchema,
                RewardPayloadCodec.Write(definition),
                catalog.RequiresDurability);
            return true;
        }

        /// <summary>The detail of the last refused description, so a scenario can report why a choice was unrewarded.</summary>
        public string LastDescribeDetail { get; private set; } = string.Empty;

        /// <summary>
        /// Reinstates the outbox rows a checkpoint carried, so a reward the source world committed before it was
        /// unloaded is still owed by the restored session (P-045, P-049, P-053).
        /// </summary>
        public bool TryReinstate(IReadOnlyList<OutboxRecordValue> rows, out string detail)
        {
            if (!Owner.TryReinstate(rows, out DiagnosticCode code, out detail))
            {
                return false;
            }

            ReinstateCount = Owner.ReinstateCount;
            return true;
        }

        /// <summary>The outbox rows a capture of this world records (P-053).</summary>
        public IReadOnlyList<OutboxRecordValue> ToRecords() => Owner.ToRecords();

        public void Dispose() => Owner.Dispose();

        public override string ToString() =>
            "rewardBridge(owner=" + Owner.OwnerId.ToString() + "," + Owner.Outbox.Durability.ToString() + ")";

        /// <summary>
        /// Reads the two big-endian words a committed choice carries: the resulting node and the conversation status.
        /// The narrative package encodes a choice outcome as exactly `NarrativeChoice.EncodedLength` bytes with the
        /// node at offset 0 and the status at offset 4, and this is a reading of that layout, not a second one (P-054).
        /// </summary>
        private static bool TryReadChoice(
            IReadOnlyList<byte>? payload,
            out int nodeOrdinal,
            out int status,
            out string detail)
        {
            nodeOrdinal = 0;
            status = 0;
            detail = string.Empty;
            if (payload == null || payload.Count != NarrativeChoice.EncodedLength)
            {
                detail = "a committed choice carries " + (payload == null ? 0 : payload.Count).ToString(
                    CultureInfo.InvariantCulture) + " byte(s) and a choice outcome is "
                    + NarrativeChoice.EncodedLength.ToString(CultureInfo.InvariantCulture) + " (P-054).";
                return false;
            }

            nodeOrdinal = NarrativePayloadCodec.ReadInt32(payload, 0);
            status = NarrativePayloadCodec.ReadInt32(payload, NarrativePayloadCodec.Int32Bytes);
            return true;
        }
    }
}
