// GameCore.Gameplay.Integration - the narrative-to-card reward rule (GC-021).
//
// Normative sources: docs/game-core/07-reference-compositions.md s5 ("One `CommandDriven` world contains the chapter
// quest packages, card table packages, and a small `NarrativeCardRewards` bridge plugin. There are no special hybrid
// game kernel types." ... "an ECS outbox item keyed by `(QuestTransitionReceiptId, RewardDefinitionRevision)` with
// `RewardId` derived from that pair ... A duplicate source receipt or retry uses the same durable `RewardId` and
// returns the existing result without granting another card") and docs/game-core/00-core-protocols.md P-001 (the
// kernel requires no reward schema; a plugin introduces a domain concept through registered contracts), P-003 (the
// three kinds of change are distinct and no common reversible `Effect` API exists: granting a card is committed
// gameplay) and P-054 (content is versioned: a reward definition revision is part of the reward's identity).
//
// WHAT A REWARD IS
//
// A reward is *content policy*: "reaching this node of this chapter grants this card to this seat". It is a pure
// function of a committed choice, so it needs no protocol addition and is data in this package.
//
// THE REWARD'S IDENTITY
//
// 07 s5 keys the outbox item on `(receipt, reward definition revision)`, and the obligation identity the delivery
// seam derives is `(source world, committed event sequence, destination, command schema)`. The two agree in the way
// that matters: one committed event plus one definition revision is one reward, derived on any host without a
// counter, and a repeated observation of that event produces the *same* obligation rather than a second one. The
// content revision is carried inside the reward payload (see `RewardPayloadCodec`), so a definition change is a
// different reward rather than a silent reinterpretation of the bytes already recorded (P-054).
//
// THE SOURCE RECEIPT: A RECORDED DECISION
//
// 07 s5 names a `QuestTransitionReceipt` as the receipt. The shipped narrative package publishes exactly one
// committed event for an accepted choice — `narrative.schema.choice-committed`, carrying the resulting node and the
// conversation status — and publishes no quest-transition event. Reading 00 as authoritative (00 wins over 05, 05
// over 09), the simplest reading is that the committed choice event *is* the receipt, and the reward keys on the node
// that event records. Nothing is invented and no narrative package is edited to add a second event.
//
// THE DESTINATION MUTATION: THE CARD FAMILY'S OWN COMMAND
//
// 07 s5 shows `GrantCards(RewardId, seat-a, [reward-card])` and a card rule that "admits this system-generated reward
// independently of the player's turn". The shipped card package declares exactly three command kinds — `SubmitSet`,
// `Transfer`, `Contest` — and its commit system refuses any decision that removes nothing, always advancing the table
// version and the turn number. The reward therefore reaches a hand the only way the card family can express it: a
// `Transfer` of the reward card from a holding seat to the rewarded seat. Recorded as a gap in this task's handoff
// rather than silently reinterpreted as a grant.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Integration.RewardOutbox
{
    /// <summary>
    /// One reward definition: reaching <see cref="NodeOrdinal"/> grants <see cref="Card"/> to
    /// <see cref="RecipientSeat"/> out of <see cref="HoldingSeat"/>. Immutable content, keyed by its own revision so a
    /// content change is a different reward rather than a silent reinterpretation of an existing one (P-054).
    /// </summary>
    public sealed class RewardDefinition
    {
        public RewardDefinition(
            int nodeOrdinal,
            CardId card,
            uint recipientSeat,
            uint holdingSeat,
            uint revision)
        {
            if (card.IsNone)
            {
                throw new ArgumentException(
                    "A reward grants a card; an all-zero card identity is not one (P-004).", nameof(card));
            }

            if (revision == 0U)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision), revision, "A reward definition carries a non-zero content revision (P-054).");
            }

            NodeOrdinal = nodeOrdinal;
            Card = card;
            RecipientSeat = recipientSeat;
            HoldingSeat = holdingSeat;
            Revision = revision;
        }

        /// <summary>The node the accepted choice landed on: the reward's key, as the committed event records it.</summary>
        public int NodeOrdinal { get; }

        public CardId Card { get; }

        /// <summary>Seat the card is granted to.</summary>
        public uint RecipientSeat { get; }

        /// <summary>Seat the reward card is drawn from; where the reward cards were stocked.</summary>
        public uint HoldingSeat { get; }

        /// <summary>Immutable definition revision; part of the reward's identity (07 s5, P-054).</summary>
        public uint Revision { get; }

        /// <summary>Stable name of this definition, for diagnostics; never an identity (P-004).</summary>
        public string StableName =>
            "gamecore.reward.definition|node=" + NodeOrdinal.ToString(CultureInfo.InvariantCulture)
            + "|card=" + Card.Value.ToString(CultureInfo.InvariantCulture)
            + "|to=" + RecipientSeat.ToString(CultureInfo.InvariantCulture)
            + "|from=" + HoldingSeat.ToString(CultureInfo.InvariantCulture)
            + "|rev=" + Revision.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => "rewardDefinition(" + StableName + ")";
    }

    /// <summary>
    /// The reward content of one bridge: a bounded, immutable table of definitions keyed by the node a choice landed
    /// on. A rewarded node has one definition: a second declaration for one node is refused rather than resolved by
    /// declaration order (P-008). A node no definition covers is not rewarded, which is the honest answer for a choice
    /// the content does not reward (P-015: unrecognized cases are reported, not guessed at).
    /// </summary>
    public sealed class RewardCatalog
    {
        private readonly List<RewardDefinition> definitions = new List<RewardDefinition>();
        private readonly Dictionary<int, RewardDefinition> byNode = new Dictionary<int, RewardDefinition>();

        public RewardCatalog(bool requiresDurability)
        {
            RequiresDurability = requiresDurability;
        }

        /// <summary>
        /// Whether the content this table describes requires its rewards to survive a crash (P-045). It is a declared
        /// policy of the content, kept out of the reward *payload* on purpose: the payload describes what the reward
        /// is, and a durability requirement is a claim about this deployment, not about the reward's identity.
        /// </summary>
        public bool RequiresDurability { get; }

        /// <summary>Declares one definition; a duplicate node is refused with the reason (P-008).</summary>
        public bool TryDeclare(RewardDefinition definition, out string detail)
        {
            detail = string.Empty;
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (byNode.ContainsKey(definition.NodeOrdinal))
            {
                detail = "node " + definition.NodeOrdinal.ToString(CultureInfo.InvariantCulture)
                    + " already has a reward definition; one node has one reward (P-008).";
                return false;
            }

            byNode.Add(definition.NodeOrdinal, definition);
            definitions.Add(definition);
            return true;
        }

        public int Count => definitions.Count;

        /// <summary>Definitions in declaration order, which is canonical and independent of hashing (P-008).</summary>
        public IReadOnlyList<RewardDefinition> Definitions => definitions;

        /// <summary>The reward an accepted choice grants, or false when this node is not rewarded.</summary>
        public bool TryGetDefinition(int nodeOrdinal, out RewardDefinition? definition) =>
            byNode.TryGetValue(nodeOrdinal, out definition);

        /// <summary>Definition revisions in canonical ascending order, so a versioned fixture can compare them.</summary>
        public IReadOnlyList<uint> Revisions()
        {
            var revisions = new List<uint>(definitions.Count);
            for (int i = 0; i < definitions.Count; i++)
            {
                revisions.Add(definitions[i].Revision);
            }

            return revisions;
        }

        /// <summary>
        /// The reward content the two families' fixtures use together: the narrative fixture's first accepted choice
        /// lands on node 1, and it grants one card to the card fixture's seat A, stocked at seat B. Both seats and the
        /// card identity are the card package's own declared values, so the bridge declares no card concept (P-001).
        /// </summary>
        public static RewardCatalog Default() => Default(requiresDurability: true);

        /// <summary>The default content, with the durability requirement stated explicitly by the caller.</summary>
        public static RewardCatalog Default(bool requiresDurability)
        {
            var catalog = new RewardCatalog(requiresDurability);
            catalog.TryDeclare(
                new RewardDefinition(
                    1,
                    new CardId(1UL),
                    CardTableKeys.SeatAOrdinal,
                    CardTableKeys.SeatBOrdinal,
                    1U),
                out string _);
            return catalog;
        }

        public override string ToString() => "rewardCatalog(definitions="
            + definitions.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The card-table facts this bridge depends on, named once so the dependency is visible rather than scattered.
    /// Every value is the card package's own declared identity or ordinal: nothing here re-declares a card concept
    /// (P-001), and nothing here is a second authority over the card table (P-034).
    /// </summary>
    public static class CardTableConstants
    {
        /// <summary>
        /// Stable identity of the delivery destination: the card family's own declared command route. The route *is*
        /// the endpoint an obligation is addressed to, so using its identity avoids inventing a parallel one (P-004).
        /// </summary>
        public static Id128 DestinationId => CardTableKeys.CommandRoute.Value;

        /// <summary>The payload schema the destination accepts; a mismatch is refused before the port runs (P-054).</summary>
        public static SchemaRef CommandSchema => CardTableKeys.CommandSchema;

        /// <summary>The schema the destination publishes its outcome under; used to read its own verdict (P-042).</summary>
        public static SchemaRef ResultSchema => CardTableKeys.ResultSchema;

        public override string ToString() => "cardTableConstants(destination=" + DestinationId.ToString() + ")";
    }
}
