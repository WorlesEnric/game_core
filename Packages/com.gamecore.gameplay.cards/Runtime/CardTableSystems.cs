// GameCore.Gameplay.Cards — the card table's runtime module and its four settlement systems (GC-011).
//
// Normative sources: 07 s2.3 (the settlement plan and the example "seat A owns cards {c1,c2,c3}, its score is 4,
// and the active bonus is +2. `SubmitSet(c1,c2,c3)` removes exactly those cards, advances the turn, and changes
// the score to 16 in the same commit stage"), P-034 (one owner per domain; `cards.commit` is the sole writer of
// table, affected hand and score), P-042/P-044 (an owner validates gameplay and commits a bounded multi-entity
// decision) and P-036 (a command-driven world advances only for an admitted command).
//
// The four systems are ordinary gameplay systems and the ECS storage they write is this package's own. The kernel
// services they use are the real ones: `WorldMessagePlane` for the bounded command lane and the committed events,
// the published assembly for the inherited scoring configuration, and the driver's own step boundary for "every
// write of one command happens in one step, or none does".
//
// Atomicity is the logical step, not an ECB: `cards.commit` rechecks the live table version, computes every value
// of the settlement into locals, and only then applies them; a refusal before that point has written nothing, and
// an unexpected failure after it faults the world through the driver's own latch rather than retrying a half-step.
// Nothing here claims database transactions (P-044, P-031).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Cards
{
    /// <summary>
    /// The fixture-side state of one card-table world: the entities the table runtime owns and the counters the
    /// scenario asserts on. It owns no gameplay rule.
    /// </summary>
    public sealed class CardTableModule : IDisposable
    {
        private static readonly List<CardTableModule> Modules = new List<CardTableModule>();

        private readonly Dictionary<uint, Entity> seatsByOrdinal = new Dictionary<uint, Entity>();
        private bool disposed;

        private CardTableModule(UnityWorldHost host)
        {
            Host = host;
        }

        /// <summary>The world this card table runs in; the same host class the narrative slice uses (P-002).</summary>
        public UnityWorldHost Host { get; }

        /// <summary>The table entity: `TableState`, the market and deck rows, and the table-wide drafts.</summary>
        public Entity TableEntity { get; private set; } = Entity.Null;

        /// <summary>Seats the module knows, keyed by their declared ordinal (07 s2.2).</summary>
        public int SeatCount => seatsByOrdinal.Count;

        /// <summary>Ordinary commands the input stage decoded into the command draft.</summary>
        public int DecodedCommandCount { get; private set; }

        /// <summary>Batch envelopes the input stage decoded.</summary>
        public int DecodedBatchCount { get; private set; }

        /// <summary>Commands the input stage refused before they reached the draft (a bounded-work rejection).</summary>
        public int InputRejectionCount { get; private set; }

        /// <summary>Decisions the validate stage built.</summary>
        public int DecisionCount { get; private set; }

        /// <summary>Decisions that carried no writes, i.e. the rules' verdicts with their reasons.</summary>
        public int RejectedDecisionCount { get; private set; }

        /// <summary>Settlements `cards.commit` applied to live storage.</summary>
        public int CommittedSettlementCount { get; private set; }

        /// <summary>Decisions the commit stage refused after rechecking the live table version (P-044).</summary>
        public int StaleCommitRejectionCount { get; private set; }

        /// <summary>Individual state writes applied by `cards.commit`; the bounded write set's size in total.</summary>
        public int AppliedWriteCount { get; private set; }

        /// <summary>Committed result records the output stage prepared.</summary>
        public int OutputRowCount { get; private set; }

        /// <summary>Seat ordinal of the last resolved contest, or -1 when no contest has been resolved.</summary>
        public int LastContestWinnerSeat { get; private set; } = -1;

        /// <summary>Winning bid sequence of the last resolved contest; zero when none was resolved.</summary>
        public ulong LastContestWinnerSequence { get; private set; }

        /// <summary>The last effective `cards.set-bonus` value a settled command read from the assembly (P-015).</summary>
        public int LastEffectiveBonus { get; private set; }

        /// <summary>
        /// Settlements whose seat carried no active scoring row, so the honest effective bonus was zero. A
        /// non-zero value means the modifier never reached a seat that scored.
        /// </summary>
        public int BonusMissCount { get; private set; }

        /// <summary>Creates and registers the module of one owned world.</summary>
        public static CardTableModule Attach(UnityWorldHost host)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            var module = new CardTableModule(host);
            Modules.Add(module);
            return module;
        }

        /// <summary>Resolves the module of one ECS world; systems reach the host through it.</summary>
        public static bool TryGet(World world, out CardTableModule? module)
        {
            for (int i = 0; i < Modules.Count; i++)
            {
                if (Modules[i].Host.EntityWorld == world)
                {
                    module = Modules[i];
                    return true;
                }
            }

            module = null;
            return false;
        }

        /// <summary>Disposes every attached module; the scenario calls this after its world is torn down.</summary>
        public static void DetachAll()
        {
            for (int i = Modules.Count - 1; i >= 0; i--)
            {
                Modules[i].Dispose();
            }

            Modules.Clear();
        }

        /// <summary>Records the table entity the seeding step created.</summary>
        public void BindTable(Entity table) => TableEntity = table;

        /// <summary>Records one seat entity and its declared ordinal.</summary>
        public void BindSeat(uint ordinal, Entity seat) => seatsByOrdinal[ordinal] = seat;

        /// <summary>Resolves one seat ordinal to its entity; false when no such seat exists (P-005).</summary>
        public bool TrySeat(uint ordinal, out Entity seat) => seatsByOrdinal.TryGetValue(ordinal, out seat);

        /// <summary>Seat ordinals in ascending order, so a caller reads a deterministic list (P-008).</summary>
        public IReadOnlyList<uint> SeatOrdinals()
        {
            var ordinals = new List<uint>(seatsByOrdinal.Count);
            foreach (KeyValuePair<uint, Entity> pair in seatsByOrdinal)
            {
                ordinals.Add(pair.Key);
            }

            ordinals.Sort();
            return ordinals;
        }

        internal void RecordInput(bool isBatch, bool rejected)
        {
            if (rejected)
            {
                InputRejectionCount++;
                return;
            }

            if (isBatch)
            {
                DecodedBatchCount++;
            }
            else
            {
                DecodedCommandCount++;
            }
        }

        internal void RecordDecision(bool committed)
        {
            DecisionCount++;
            if (!committed)
            {
                RejectedDecisionCount++;
            }
        }

        internal void RecordCommit(int writes, int effectiveBonus, bool bonusMiss)
        {
            CommittedSettlementCount++;
            AppliedWriteCount += writes;
            LastEffectiveBonus = effectiveBonus;
            if (bonusMiss)
            {
                BonusMissCount++;
            }
        }

        internal void RecordStaleCommit() => StaleCommitRejectionCount++;

        internal void RecordOutput() => OutputRowCount++;

        internal void RecordContest(SeatOrdinal winner, ulong sequence)
        {
            LastContestWinnerSeat = (int)winner.Value;
            LastContestWinnerSequence = sequence;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            seatsByOrdinal.Clear();
            Modules.Remove(this);
        }
    }

    /// <summary>
    /// Helpers the four systems share: reading a bounded hand as the pure rules see it, reading the inherited
    /// scoring value from the published assembly, and installing the card storage of one entity.
    /// </summary>
    public static class CardTableAccess
    {
        /// <summary>Reads one seat's hand as a bounded copy, never as a live view the rules could mutate.</summary>
        public static CardHand ReadHand(EntityManager entityManager, Entity seat, uint ordinal)
        {
            if (!entityManager.Exists(seat) || !entityManager.HasBuffer<CardHandRow>(seat))
            {
                return new CardHand(new SeatOrdinal(ordinal), null);
            }

            DynamicBuffer<CardHandRow> rows = entityManager.GetBuffer<CardHandRow>(seat);
            var cards = new List<CardId>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                cards.Add(new CardId(rows[i].Card));
            }

            return new CardHand(new SeatOrdinal(ordinal), cards);
        }

        /// <summary>
        /// The effective `cards.set-bonus` value of one seat, read from its published binding row (07 s2.2:
        /// "EffectiveSetBonus { Value } — Derived target binding; assembly bridge; gameplay systems read it").
        /// A seat with no active scoring row reports zero and <paramref name="missing"/> true, so a caller never
        /// mistakes "no published configuration" for "a bonus of zero" (P-009, P-015).
        /// </summary>
        public static int EffectiveBonus(EntityManager entityManager, Entity seat, out bool missing)
        {
            missing = true;
            if (!entityManager.Exists(seat) || !entityManager.HasBuffer<CapabilityBinding>(seat))
            {
                return 0;
            }

            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(seat);
            if (!AssemblyStorage.TryFindBinding(bindings, CardVocabulary.SetBonusCapability, 0U, out CapabilityBinding row)
                || !row.IsActive)
            {
                return 0;
            }

            missing = false;
            return row.Value;
        }

        /// <summary>Installs the card storage of one table entity at its declared initial state.</summary>
        public static void InstallTableStorage(EntityManager entityManager, Entity table, uint tableVersion)
        {
            entityManager.AddComponentData(table, new CardTableState
            {
                ActiveSeat = CardTableKeys.SeededActiveSeat,
                TurnNumber = 0U,
                TableVersion = tableVersion,
                Closed = 0,
            });
            entityManager.AddComponentData(table, default(CardTableSnapshot));
            entityManager.AddBuffer<CardCommandRow>(table);
            entityManager.AddBuffer<CardDecisionRow>(table);
            entityManager.AddBuffer<CardCommittedRow>(table);
            entityManager.AddBuffer<CardMarketRow>(table);
            entityManager.AddBuffer<CardDeckRow>(table);
        }

        /// <summary>Installs the card storage of one seat entity and seeds its ordinal and score.</summary>
        public static void InstallSeatStorage(EntityManager entityManager, Entity seat, uint ordinal, int score)
        {
            entityManager.AddComponentData(seat, new CardSeatState
            {
                Ordinal = ordinal,
                Score = score,
                Seated = 1,
            });
            entityManager.AddBuffer<CardHandRow>(seat);
        }

        /// <summary>Appends one held card row to a seat, refusing beyond the declared hand capacity (07 s2.3).</summary>
        public static bool TryAddHeldCard(EntityManager entityManager, Entity seat, CardId card)
        {
            if (!entityManager.Exists(seat) || !entityManager.HasBuffer<CardHandRow>(seat))
            {
                return false;
            }

            DynamicBuffer<CardHandRow> hand = entityManager.GetBuffer<CardHandRow>(seat);
            if (hand.Length >= CardSetRules.MaxHandCards)
            {
                return false;
            }

            hand.Add(new CardHandRow { Card = card.Value });
            return true;
        }

        /// <summary>Reads a seat's hand rows as card identities, in stored order (a copy).</summary>
        public static IReadOnlyList<CardId> ReadCards(EntityManager entityManager, Entity seat)
        {
            var cards = new List<CardId>();
            if (!entityManager.Exists(seat) || !entityManager.HasBuffer<CardHandRow>(seat))
            {
                return cards;
            }

            DynamicBuffer<CardHandRow> hand = entityManager.GetBuffer<CardHandRow>(seat);
            for (int i = 0; i < hand.Length; i++)
            {
                cards.Add(new CardId(hand[i].Card));
            }

            return cards;
        }

        /// <summary>The table state one table entity currently holds, or a default when it has none.</summary>
        public static CardTableState ReadTable(EntityManager entityManager, Entity table) =>
            entityManager.Exists(table) && entityManager.HasComponent<CardTableState>(table)
                ? entityManager.GetComponentData<CardTableState>(table)
                : default(CardTableState);
    }

    /// <summary>
    /// `cards.input` (07 s2.3): it consumes the admitted command envelopes through the owner's bounded lane,
    /// decodes each payload with the reader bound to its schema, and appends one row to the owner's draft. A
    /// payload no reader covers and a draft at capacity are both refusals with an observable result, so nothing is
    /// silently dropped (P-042, P-043).
    /// </summary>
    [DisableAutoCreation]
    public partial class CardInputSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!CardTableModule.TryGet(World, out CardTableModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            EntityManager entityManager = EntityManager;
            if (plane == null || !entityManager.Exists(module.TableEntity))
            {
                return;
            }

            // A step's draft is step-scoped: its producer clears it first, so a command-free step leaves it empty
            // instead of exposing the previous step's rows (P-043, lifetime `Step`).
            DynamicBuffer<CardCommandRow> draft = entityManager.GetBuffer<CardCommandRow>(module.TableEntity);
            draft.Clear();

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(CardTableKeys.TableOwner);
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);
                bool isBatch = message.PayloadSchema.Id.Value.Equals(CardTableKeys.BatchSchema.Id.Value);

                if (draft.Length >= CardTableKeys.DraftCapacity)
                {
                    // Declared bounded work: a full draft refuses the command before any mutation (P-043). The bound is
                    // the declared draft capacity, not the buffer's own allocation capacity, which the ECS grows as it
                    // likes and which is zero for an element this large before its first growth.
                    plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                    module.RecordInput(isBatch, true);
                    continue;
                }

                var row = new CardCommandRow { Message = message, IsBatch = isBatch ? (byte)1 : (byte)0 };
                if (isBatch)
                {
                    if (plane.Readers.TryRead(
                            message.PayloadSchema, payload, out CardBatchPayload decodedBatch, out string _)
                        != PayloadDecodeOutcome.Decoded)
                    {
                        plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                        module.RecordInput(true, true);
                        continue;
                    }

                    row.Batch = decodedBatch;
                }
                else
                {
                    if (plane.Readers.TryRead(
                            message.PayloadSchema, payload, out CardCommandPayload decodedCommand, out string _)
                        != PayloadDecodeOutcome.Decoded)
                    {
                        plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                        module.RecordInput(false, true);
                        continue;
                    }

                    row.Command = decodedCommand;
                }

                draft.Add(row);
                module.RecordInput(isBatch, false);
            }
        }
    }

    /// <summary>
    /// `cards.validate` (07 s2.3): it reads the table, the hands and the current published scoring configuration,
    /// and builds one bounded draft. It rejects a wrong-turn, duplicate-card or unheld-card submission and writes
    /// nothing authoritative, so a rejected command is a rejection rather than a partially applied step.
    /// </summary>
    [DisableAutoCreation]
    public partial class CardValidateSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!CardTableModule.TryGet(World, out CardTableModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.TableEntity))
            {
                return;
            }

            DynamicBuffer<CardCommandRow> commands = entityManager.GetBuffer<CardCommandRow>(module.TableEntity);
            DynamicBuffer<CardDecisionRow> decisions = entityManager.GetBuffer<CardDecisionRow>(module.TableEntity);
            decisions.Clear();
            if (commands.Length == 0)
            {
                return;
            }

            CardTableState table = entityManager.GetComponentData<CardTableState>(module.TableEntity);
            for (int i = 0; i < commands.Length; i++)
            {
                CardCommandRow command = commands[i];
                var decision = new CardDecisionRow
                {
                    Message = command.Message,
                    Kind = command.IsBatchEnvelope ? CardCommandKind.Contest : command.Command.Kind,
                    Status = CardSettlementStatus.RejectedEmptyCandidateSet,
                };

                if (command.IsBatchEnvelope)
                {
                    ValidateContest(module, entityManager, table, command, ref decision);
                }
                else
                {
                    ValidateCommand(module, entityManager, table, command, ref decision);
                }

                decisions.Add(decision);
                module.RecordDecision(decision.Status == CardSettlementStatus.Committed);
            }
        }

        private static void ValidateCommand(
            CardTableModule module,
            EntityManager entityManager,
            in CardTableState table,
            in CardCommandRow command,
            ref CardDecisionRow decision)
        {
            CardCommandPayload payload = command.Command;
            if (!module.TrySeat(payload.Seat, out Entity seat))
            {
                decision.Status = CardSettlementStatus.RejectedUnknownSeat;
                return;
            }

            CardHand hand = CardTableAccess.ReadHand(entityManager, seat, payload.Seat);
            CardHand counterparty = default(CardHand);
            if (payload.Kind == CardCommandKind.Transfer)
            {
                if (payload.Counterparty == payload.Seat
                    || !module.TrySeat(payload.Counterparty, out Entity receiving))
                {
                    decision.Status = CardSettlementStatus.RejectedUnknownSeat;
                    return;
                }

                counterparty = CardTableAccess.ReadHand(entityManager, receiving, payload.Counterparty);
            }

            if (payload.Kind == CardCommandKind.Contest)
            {
                // A contest is resolved by the batch envelope's own resolver, never by one hand's settlement
                // (07 s2.3: "Two commands cannot both consume the same card").
                decision.Status = CardSettlementStatus.RejectedEmptyCandidateSet;
                return;
            }

            int bonus = CardTableAccess.EffectiveBonus(entityManager, seat, out bool _);
            var request = new CardSettlementRequest(
                payload.Kind,
                command.Message.Order.Admitted.Value,
                new SeatOrdinal(payload.Seat),
                new SeatOrdinal(payload.Counterparty),
                payload.ExpectedTableVersion,
                payload.Candidates());

            if (!CardSetRules.TryBuildSettlement(
                    request,
                    table.ActiveSeat,
                    table.TableVersion,
                    hand,
                    counterparty,
                    bonus,
                    out CardWriteSet writes,
                    out CardSettlementStatus status))
            {
                decision.Status = status;
                return;
            }

            decision.Status = CardSettlementStatus.Committed;
            decision.Counterparty = payload.Counterparty;
            RecordWrites(ref decision, writes);
        }

        private static void ValidateContest(
            CardTableModule module,
            EntityManager entityManager,
            in CardTableState table,
            in CardCommandRow command,
            ref CardDecisionRow decision)
        {
            CardBatchPayload payload = command.Batch;
            if (!module.TrySeat(payload.Holder, out Entity holder))
            {
                decision.Status = CardSettlementStatus.RejectedUnknownSeat;
                return;
            }

            CardHand holderHand = CardTableAccess.ReadHand(entityManager, holder, payload.Holder);
            if (!CardSetRules.TryResolveContest(
                    payload.ToContestRequest(),
                    table.TableVersion,
                    holderHand,
                    out CardContestResolution resolution,
                    out CardSettlementStatus status))
            {
                decision.Status = status;
                return;
            }

            // The winner's bid is the one accepted use of the contested card: the holder's hand loses that card
            // and the winning seat gains it, so the two seats and the table move together or not at all.
            if (resolution.Winner.Value == payload.Holder
                || !module.TrySeat(resolution.Winner.Value, out Entity winner))
            {
                decision.Status = CardSettlementStatus.RejectedUnknownSeat;
                return;
            }

            CardHand winnerHand = CardTableAccess.ReadHand(entityManager, winner, resolution.Winner.Value);
            var request = new CardSettlementRequest(
                CardCommandKind.Transfer,
                command.Message.Order.Admitted.Value,
                new SeatOrdinal(payload.Holder),
                resolution.Winner,
                payload.ExpectedTableVersion,
                new[] { payload.ContestedCard });

            if (!CardSetRules.TryBuildSettlement(
                    request,
                    table.ActiveSeat,
                    table.TableVersion,
                    holderHand,
                    winnerHand,
                    0,
                    out CardWriteSet writes,
                    out CardSettlementStatus transferStatus))
            {
                decision.Status = transferStatus;
                return;
            }

            // The resolved winner is observable, so a caller never has to re-derive the contest to see which bid
            // won; the resolution is the pure resolver's own output (P-037).
            module.RecordContest(resolution.Winner, resolution.WinningSequence);

            decision.Status = CardSettlementStatus.Committed;
            decision.Counterparty = resolution.Winner.Value;
            RecordWrites(ref decision, writes);
        }

        /// <summary>
        /// Copies the bounded write set into the decision row. Only <see cref="CardDecisionRow.WriteCount"/> entries
        /// are meaningful, so a decision whose effect exceeds the declared slots rejects instead of truncating.
        /// </summary>
        private static void RecordWrites(ref CardDecisionRow decision, CardWriteSet writes)
        {
            decision.WriteCount = (byte)writes.Count;
            for (int w = 0; w < writes.Count; w++)
            {
                if (!decision.TrySetWrite(w, writes.Write(w)))
                {
                    decision.Status = CardSettlementStatus.RejectedBoundedOverflow;
                    decision.WriteCount = 0;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// `cards.commit` (07 s2.3): the sole writer of the table, the affected hands and the score. It rechecks the
    /// live table version, computes every value of the settlement into locals, and only then applies them under its
    /// single owner, before the committed result is staged. A command that fails a recheck changes nothing and is
    /// reported as a rejected result; a genuine failure in the apply step is a postwrite fault of the world, never a
    /// retried half-step (P-044, P-031).
    /// </summary>
    [DisableAutoCreation]
    public partial class CardCommitSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!CardTableModule.TryGet(World, out CardTableModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            EntityManager entityManager = EntityManager;
            if (plane == null || !entityManager.Exists(module.TableEntity))
            {
                return;
            }

            DynamicBuffer<CardDecisionRow> decisions = entityManager.GetBuffer<CardDecisionRow>(module.TableEntity);
            DynamicBuffer<CardCommittedRow> output = entityManager.GetBuffer<CardCommittedRow>(module.TableEntity);
            output.Clear();

            for (int i = 0; i < decisions.Length; i++)
            {
                CardDecisionRow decision = decisions[i];
                if (decision.Status != CardSettlementStatus.Committed)
                {
                    plane.Reject(decision.Message, ReasonOf(decision.Status), plane.ExecutingStep);
                    continue;
                }

                bool applied = TryApply(
                    module,
                    entityManager,
                    plane,
                    ref decision,
                    out int writes,
                    out int effectiveBonus,
                    out bool bonusMiss,
                    out DiagnosticCode refusal);

                if (!applied)
                {
                    if (refusal == DiagnosticCode.StalePlan)
                    {
                        module.RecordStaleCommit();
                    }

                    plane.Reject(decision.Message, refusal, plane.ExecutingStep);
                    continue;
                }

                module.RecordCommit(writes, effectiveBonus, bonusMiss);
            }
        }

        /// <summary>
        /// Plans and applies one committed decision. Returns false with the refusal code when nothing was written,
        /// so a false result always means "the live state is exactly as it was" (P-044).
        /// </summary>
        private static bool TryApply(
            CardTableModule module,
            EntityManager entityManager,
            WorldMessagePlane plane,
            ref CardDecisionRow decision,
            out int writes,
            out int effectiveBonus,
            out bool bonusMiss,
            out DiagnosticCode refusal)
        {
            writes = 0;
            effectiveBonus = 0;
            bonusMiss = false;
            refusal = DiagnosticCode.None;

            Entity tableEntity = module.TableEntity;
            CardTableState table = CardTableAccess.ReadTable(entityManager, tableEntity);
            if (table.IsClosed)
            {
                refusal = DiagnosticCode.Ineligible;
                return false;
            }

            // Phase 1: validate every write against live state and compute the new values. Nothing is written, so
            // a refusal here leaves the table and every hand untouched.
            var removals = new List<KeyValuePair<uint, CardId>>(CardSetRules.SetCardCount);
            var additions = new List<KeyValuePair<uint, CardId>>(1);
            bool hasScore = false;
            bool hasAdvance = false;
            uint scoreSeat = 0U;
            int scoreDelta = 0;
            int scoreAfter = 0;

            for (int w = 0; w < decision.WriteCount; w++)
            {
                if (!decision.TryWrite(w, out CardWrite write))
                {
                    break;
                }

                switch (write.Kind)
                {
                    case CardWriteKind.RemoveCard:
                        if (!module.TrySeat(write.Seat.Value, out Entity from))
                        {
                            refusal = DiagnosticCode.StaleHandle;
                            return false;
                        }

                        if (!CardTableAccess.ReadHand(entityManager, from, write.Seat.Value).Holds(write.Card))
                        {
                            refusal = DiagnosticCode.Ineligible;
                            return false;
                        }

                        removals.Add(new KeyValuePair<uint, CardId>(write.Seat.Value, write.Card));
                        break;

                    case CardWriteKind.AddCard:
                        if (!module.TrySeat(write.Seat.Value, out Entity to))
                        {
                            refusal = DiagnosticCode.StaleHandle;
                            return false;
                        }

                        if (entityManager.GetBuffer<CardHandRow>(to).Length >= CardSetRules.MaxHandCards)
                        {
                            refusal = DiagnosticCode.Ineligible;
                            return false;
                        }

                        additions.Add(new KeyValuePair<uint, CardId>(write.Seat.Value, write.Card));
                        break;

                    case CardWriteKind.AddScore:
                        if (!module.TrySeat(write.Seat.Value, out Entity scoring))
                        {
                            refusal = DiagnosticCode.StaleHandle;
                            return false;
                        }

                        scoreSeat = write.Seat.Value;
                        scoreDelta = write.Amount;
                        scoreAfter = entityManager.GetComponentData<CardSeatState>(scoring).Score + write.Amount;
                        effectiveBonus = CardTableAccess.EffectiveBonus(entityManager, scoring, out bool missing);
                        bonusMiss = missing;
                        hasScore = true;
                        break;

                    default:
                        hasAdvance = true;
                        break;
                }
            }

            // A settlement must move at least one card and advance the table: a decision that scored without a
            // card, or advanced without moving one, would publish a table version that does not describe the state
            // it committed, so the whole effect is refused instead of half-applied (P-044).
            if (!hasAdvance || removals.Count == 0)
            {
                refusal = DiagnosticCode.Ineligible;
                return false;
            }

            uint nextVersion = table.TableVersion + 1U;
            uint nextTurn = table.TurnNumber + 1U;

            // Phase 2: apply. Every value is already computed, so this copy has no fallible work in it.
            for (int r = 0; r < removals.Count; r++)
            {
                if (RemoveCard(entityManager, module, removals[r].Key, removals[r].Value))
                {
                    writes++;
                }
            }

            for (int a = 0; a < additions.Count; a++)
            {
                if (module.TrySeat(additions[a].Key, out Entity to)
                    && CardTableAccess.TryAddHeldCard(entityManager, to, additions[a].Value))
                {
                    writes++;
                }
            }

            if (hasScore && module.TrySeat(scoreSeat, out Entity scored))
            {
                CardSeatState seat = entityManager.GetComponentData<CardSeatState>(scored);
                seat.Score = scoreAfter;
                entityManager.SetComponentData(scored, seat);
                writes++;
            }

            table.TableVersion = nextVersion;
            table.TurnNumber = nextTurn;
            entityManager.SetComponentData(tableEntity, table);
            writes++;

            // The committed record carries the values this step actually committed; it becomes a `CommittedEvent`
            // only at the step's own publication (P-044, P-045).
            var result = new CardResultPayload(
                decision.Kind,
                hasScore ? scoreSeat : removals[0].Key,
                decision.Counterparty,
                CardSettlementStatus.Committed,
                hasScore ? scoreDelta : 0,
                hasScore ? scoreAfter : 0,
                nextVersion,
                (byte)Math.Min(removals.Count, 3),
                CardAt(removals, 0),
                CardAt(removals, 1),
                CardAt(removals, 2));

            entityManager.GetBuffer<CardCommittedRow>(tableEntity).Add(new CardCommittedRow
            {
                Message = decision.Message,
                Kind = decision.Kind,
                ScoreDelta = result.ScoreDelta,
                ScoreAfter = result.ScoreAfter,
                TableVersion = nextVersion,
                CardCount = result.CardCount,
                Card0 = result.Card0.Value,
                Card1 = result.Card1.Value,
                Card2 = result.Card2.Value,
            });

            if (!plane.Commit(
                    decision.Message,
                    CardTableKeys.ResultSchema,
                    CardResultCodec.Write(result),
                    plane.ExecutingStep,
                    out string _))
            {
                // The step's event budget or the request row refused the commit; the refusal is recorded against
                // the decision, and the live writes stand as the bounded write set they were.
                decision.Status = CardSettlementStatus.RejectedBoundedOverflow;
            }

            return true;
        }

        private static CardId CardAt(List<KeyValuePair<uint, CardId>> cards, int index) =>
            index < cards.Count ? cards[index].Value : default(CardId);

        private static bool RemoveCard(
            EntityManager entityManager,
            CardTableModule module,
            uint seatOrdinal,
            CardId card)
        {
            if (!module.TrySeat(seatOrdinal, out Entity seat) || !entityManager.HasBuffer<CardHandRow>(seat))
            {
                return false;
            }

            DynamicBuffer<CardHandRow> hand = entityManager.GetBuffer<CardHandRow>(seat);
            for (int i = 0; i < hand.Length; i++)
            {
                if (hand[i].Card == card.Value)
                {
                    hand.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>The diagnostic code a rules verdict maps to; a refusal is never an invented code (P-009).</summary>
        private static DiagnosticCode ReasonOf(CardSettlementStatus status)
        {
            switch (status)
            {
                case CardSettlementStatus.RejectedUnknownSeat:
                    return DiagnosticCode.StaleHandle;
                case CardSettlementStatus.RejectedStaleTableVersion:
                    return DiagnosticCode.StalePlan;
                case CardSettlementStatus.RejectedBoundedOverflow:
                    return DiagnosticCode.BudgetExceeded;
                default:
                    return DiagnosticCode.Ineligible;
            }
        }
    }

    /// <summary>
    /// `cards.output` (07 s2.3): it reads the completed owner state and the decisions, prepares the coherent
    /// snapshot for the step's publication, and releases the consumed lane rows. It writes no score and no card, so
    /// no prediction of this stage can become authority.
    /// </summary>
    [DisableAutoCreation]
    public partial class CardOutputSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!CardTableModule.TryGet(World, out CardTableModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.TableEntity))
            {
                return;
            }

            CardTableState table = entityManager.GetComponentData<CardTableState>(module.TableEntity);
            DynamicBuffer<CardDecisionRow> decisions = entityManager.GetBuffer<CardDecisionRow>(module.TableEntity);
            DynamicBuffer<CardCommittedRow> committed = entityManager.GetBuffer<CardCommittedRow>(module.TableEntity);

            int totalScore = 0;
            int totalCards = 0;
            IReadOnlyList<uint> ordinals = module.SeatOrdinals();
            for (int i = 0; i < ordinals.Count; i++)
            {
                if (!module.TrySeat(ordinals[i], out Entity seat))
                {
                    continue;
                }

                totalScore += entityManager.GetComponentData<CardSeatState>(seat).Score;
                totalCards += entityManager.GetBuffer<CardHandRow>(seat).Length;
            }

            totalCards += entityManager.GetBuffer<CardMarketRow>(module.TableEntity).Length;
            totalCards += entityManager.GetBuffer<CardDeckRow>(module.TableEntity).Length;

            int rejected = 0;
            for (int i = 0; i < decisions.Length; i++)
            {
                if (decisions[i].Status != CardSettlementStatus.Committed)
                {
                    rejected++;
                }
            }

            entityManager.SetComponentData(module.TableEntity, new CardTableSnapshot
            {
                Step = module.Host.CurrentStep.Value,
                Epoch = module.Host.CurrentEpoch.Value,
                TableVersion = table.TableVersion,
                TurnNumber = table.TurnNumber,
                ActiveSeat = table.ActiveSeat,
                TotalScore = totalScore,
                TotalCards = totalCards,
                CommittedCount = committed.Length,
                RejectedCount = rejected,
            });

            for (int i = 0; i < committed.Length; i++)
            {
                module.RecordOutput();
            }

            // The step's drafts are step-scoped, so they are cleared by the last consumer that read them, and the
            // lane's consumed rows are released only now: a payload must never be read after release (P-043).
            entityManager.GetBuffer<CardCommandRow>(module.TableEntity).Clear();
            decisions.Clear();
            WorldMessagePlane? plane = module.Host.Messages;
            if (plane != null)
            {
                plane.ReleaseConsumed(CardTableKeys.TableOwner);
            }
        }
    }
}
