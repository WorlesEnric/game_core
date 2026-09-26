// GameCore.Execution.Tests.Delivery - the durable outbox and destination idempotency tests (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-003 (acknowledging a delivery never reverses committed
// gameplay), P-008 (one canonical order; nothing here depends on dictionary enumeration), P-042 (`Accepted` is not
// `Committed`), P-043 (a bounded buffer refuses rather than dropping silently), P-045 ("irreversible output adapters
// consume only committed events, use explicit external idempotency keys, and persist an outbox when delivery must
// survive crashes") and P-053 (a checkpoint contains external outbox/dedup cursors when used) — plus this task's own
// acceptance: "Redelivery after acknowledgment loss applies the destination mutation once; source unload does not
// erase committed delivery obligation. Capacity exhaustion is explicit and cannot silently drop a reward; volatile
// delivery is clearly distinguishable from configured durable delivery."
//
// Every crash below is a scripted boundary, not a probability: one marker names one point, raises exactly once, and
// the test then recovers in a fresh adapter over the same journal. The journal file lives in a per-test temporary
// directory, so no two tests can share a frame stream.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using NUnit.Framework;

namespace GameCore.Execution.Tests.Delivery
{
    /// <summary>Stable identities and content of the delivery fixtures; one word per identity category.</summary>
    internal static class DeliveryTestIds
    {
        internal static readonly Id128 Owner = new Id128(0x47433231444C564FUL, 1UL);
        internal static readonly Id128 DestinationA = new Id128(0x4743323144455354UL, 1UL);
        internal static readonly Id128 DestinationB = new Id128(0x4743323144455354UL, 2UL);
        internal static readonly Id128 Issuer = new Id128(0x4743323149535355UL, 1UL);
        internal static readonly Id128 SchemaWord = new Id128(0x4743323153434845UL, 1UL);

        internal static readonly WorldId World = new WorldId(new Id128(0x47433231574F524CUL, 1UL));
        internal static readonly WorldId RestoredWorld = new WorldId(new Id128(0x47433231574F524CUL, 2UL));

        internal static readonly SchemaRef CommandSchema = new SchemaRef(new SchemaId(SchemaWord), 1U);
        internal static readonly SchemaRef OtherSchema = new SchemaRef(new SchemaId(SchemaWord), 2U);

        /// <summary>A destination command payload: three bytes, so a transposed read is visible.</summary>
        internal static byte[] Payload(byte seed) => new byte[] { seed, (byte)(seed + 1U), (byte)(seed + 2U) };

        internal static OperationId Operation(ulong sequence) => new OperationId(World, Issuer, sequence);

        /// <summary>The key of obligation number <paramref name="ordinal"/> addressed to one destination.</summary>
        internal static DeliveryKey Key(ulong ordinal, Id128 destination) =>
            DeliveryKey.Derive(World, new EventSequence(ordinal), destination, CommandSchema);

        /// <summary>A per-test temporary journal path inside a directory this test alone owns.</summary>
        internal static string TempJournalPath() => Path.Combine(
            Path.GetTempPath(),
            "gamecore-delivery-" + Guid.NewGuid().ToString("N"),
            "outbox.journal");

        internal static void Cleanup(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (directory != null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        /// <summary>A row with a distinct value in every field, so a transposed field is visible.</summary>
        internal static OutboxRecordValue Row(OutboxRowKind kind, Id128 destination, ulong ordinal) =>
            new OutboxRecordValue(
                (uint)kind,
                OutboxRecordValue.CurrentRecordVersion,
                Key(ordinal, destination).OutboxId.High,
                Key(ordinal, destination).OutboxId.Low,
                destination.High,
                destination.Low,
                Key(ordinal, destination).IdempotencyKey.High,
                Key(ordinal, destination).IdempotencyKey.Low,
                ordinal,
                ordinal + 100UL,
                ordinal + 200UL,
                Issuer.High,
                Issuer.Low,
                ordinal + 300UL,
                SchemaWord.High,
                SchemaWord.Low,
                1U,
                (uint)OutboxDeliveryState.Delivered,
                (uint)DiagnosticCode.None,
                2U,
                (uint)OutboxDurability.Durable,
                (uint)ordinal,
                0UL,
                0UL,
                0U,
                3U,
                Payload((byte)ordinal));
    }

    /// <summary>
    /// A destination port that records exactly what it was asked to do. It mutates only when the attempt's
    /// idempotency key has not been seen and reports `AlreadyApplied` otherwise, which is what a destination that
    /// honours P-045 does.
    /// </summary>
    internal sealed class RecordingDestination : IDestinationPort
    {
        private readonly HashSet<Id128> applied = new HashSet<Id128>();

        internal RecordingDestination(Id128 destinationId, DestinationOutcome outcome = DestinationOutcome.Applied)
        {
            DestinationId = destinationId;
            Outcome = outcome;
        }

        internal Id128 DestinationId { get; }

        internal SchemaRef CommandSchema => DeliveryTestIds.CommandSchema;

        /// <summary>What this port answers for a key it has not seen before.</summary>
        internal DestinationOutcome Outcome { get; set; }

        /// <summary>The diagnostic reason it reports with a non-applied outcome.</summary>
        internal DiagnosticCode Reason { get; set; } = DiagnosticCode.None;

        /// <summary>Every attempt this port was handed, in order.</summary>
        internal List<DeliveryAttempt> Attempts { get; } = new List<DeliveryAttempt>();

        /// <summary>Attempts that really mutated something; the count "applies the mutation once" is about (P-045).</summary>
        internal int MutationCount { get; private set; }

        internal int AlreadyAppliedCount { get; private set; }

        public DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail)
        {
            Attempts.Add(attempt);
            code = Reason;
            detail = "the recording destination saw attempt #"
                + attempt.AttemptOrdinal.ToString(CultureInfo.InvariantCulture) + " (P-042).";

            switch (Outcome)
            {
                case DestinationOutcome.Applied:
                    if (!applied.Add(attempt.IdempotencyKey))
                    {
                        AlreadyAppliedCount++;
                        return DestinationOutcome.AlreadyApplied;
                    }

                    MutationCount++;
                    return DestinationOutcome.Applied;

                case DestinationOutcome.AlreadyApplied:
                    AlreadyAppliedCount++;
                    return OutboxOutcome(Outcome);

                default:
                    return OutboxOutcome(Outcome);
            }
        }

        private static DestinationOutcome OutboxOutcome(DestinationOutcome outcome) => outcome;
    }

    /// <summary>A journal that always refuses, so "persist, then apply" is observable on its refusal path (P-045).</summary>
    internal sealed class UnwritableJournal : IDeliveryJournal
    {
        private const string Refusal = "the journal is deliberately unwritable in this fixture (P-045).";

        public string Location => "unwritable://delivery-journal";

        public bool Exists => false;

        public int FrameCount => 0;

        public int DiscardedFrameCount => 0;

        public bool TryAppend(byte[] frame, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.ResourceUnavailable;
            detail = Refusal;
            return false;
        }

        public bool TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            frames = Array.Empty<byte[]>();
            code = DiagnosticCode.ResourceUnavailable;
            detail = Refusal;
            return false;
        }

        public bool TryReplace(IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.ResourceUnavailable;
            detail = Refusal;
            return false;
        }

        public bool TryRemove(out string detail)
        {
            detail = Refusal;
            return false;
        }
    }

    /// <summary>One committed event and its destination always derive the same three stable identities (P-004, P-045).</summary>
    [TestFixture]
    public sealed class DeliveryKeyTests
    {
        [Test]
        public void OneCommittedEventAndDestinationAlwaysDeriveTheSameThreeIdentities()
        {
            DeliveryKey first = DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationA);
            DeliveryKey again = DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationA);

            Assert.That(again, Is.EqualTo(first), "the derivation is a pure function of committed data (P-004)");
            Assert.That(first.OutboxId.IsDefault, Is.False);
            Assert.That(first.DestinationId, Is.EqualTo(DeliveryTestIds.DestinationA));
            Assert.That(
                first.IdempotencyKey.IsDefault,
                Is.False,
                "an irreversible output adapter always has an explicit external idempotency key (P-045)");
            Assert.That(
                first.IdempotencyKey,
                Is.Not.EqualTo(first.OutboxId),
                "the external key is derived under its own label, never a copy of the obligation identity");
        }

        [Test]
        public void ADifferentEventDestinationSchemaOrSessionDerivesADifferentObligation()
        {
            DeliveryKey baseline = DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationA);

            Assert.That(
                DeliveryTestIds.Key(8UL, DeliveryTestIds.DestinationA).OutboxId,
                Is.Not.EqualTo(baseline.OutboxId),
                "another committed event is another obligation (P-045)");
            Assert.That(
                DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationB).OutboxId,
                Is.Not.EqualTo(baseline.OutboxId),
                "another destination is another obligation (P-034)");
            Assert.That(
                DeliveryKey.Derive(
                    DeliveryTestIds.World,
                    new EventSequence(7UL),
                    DeliveryTestIds.DestinationA,
                    DeliveryTestIds.OtherSchema).OutboxId,
                Is.Not.EqualTo(baseline.OutboxId),
                "a payload schema change is a different obligation, not a reinterpretation of bytes (P-054)");
            Assert.That(
                DeliveryKey.Derive(
                    DeliveryTestIds.RestoredWorld,
                    new EventSequence(7UL),
                    DeliveryTestIds.DestinationA,
                    DeliveryTestIds.CommandSchema).OutboxId,
                Is.Not.EqualTo(baseline.OutboxId),
                "the source session is part of the derivation, so two sessions never collide (P-004)");
        }

        [Test]
        public void TwoDestinationsNeverShareAnExternalIdempotencyKey()
        {
            DeliveryKey first = DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationA);
            DeliveryKey second = DeliveryTestIds.Key(7UL, DeliveryTestIds.DestinationB);

            Assert.That(
                first.IdempotencyKey,
                Is.Not.EqualTo(second.IdempotencyKey),
                "one destination's deduplication must never suppress another destination's mutation");
        }

        [Test]
        public void ADefaultIdentityIsRefusedRatherThanAccepted()
        {
            Assert.Throws<ArgumentException>(
                () => new DeliveryKey(default(Id128), DeliveryTestIds.DestinationA, DeliveryTestIds.Owner));
            Assert.Throws<ArgumentException>(
                () => new DeliveryKey(DeliveryTestIds.Owner, default(Id128), DeliveryTestIds.Owner));
            Assert.Throws<ArgumentException>(
                () => new DeliveryKey(DeliveryTestIds.Owner, DeliveryTestIds.DestinationA, default(Id128)));
            Assert.Throws<ArgumentException>(
                () => DeliveryKey.Derive(
                    DeliveryTestIds.World,
                    EventSequence.Zero,
                    DeliveryTestIds.DestinationA,
                    DeliveryTestIds.CommandSchema));
            Assert.Throws<ArgumentException>(
                () => DeliveryKey.Derive(
                    default(WorldId),
                    EventSequence.First,
                    DeliveryTestIds.DestinationA,
                    DeliveryTestIds.CommandSchema));
            Assert.Throws<ArgumentException>(() => new DurableOutbox(default(Id128), 4, 4, OutboxDurability.Durable));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DurableOutbox(DeliveryTestIds.Owner, 0, 4, OutboxDurability.Durable));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new DurableOutbox(DeliveryTestIds.Owner, 4, -1, OutboxDurability.Durable));
        }
    }

    /// <summary>The outbox's own bookkeeping: duplicates, capacity, endurance, terminal retention and cursors.</summary>
    [TestFixture]
    public sealed class DurableOutboxTests
    {
        [Test]
        public void CommittingOneObligationTwiceTracksItOnce()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);

            Assert.That(Commit(outbox, key, 1UL, out DeliveryObligation? obligation, out DiagnosticCode code, out string detail), Is.EqualTo(OutboxAdmission.Accepted), detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(obligation, Is.Not.Null);
            Assert.That(outbox.OpenCount, Is.EqualTo(1));
            Assert.That(outbox.CommitCount, Is.EqualTo(1));
            Assert.That(obligation!.PayloadBytes(), Is.EqualTo(DeliveryTestIds.Payload(1)));

            Assert.That(
                Commit(outbox, key, 1UL, out DeliveryObligation? again, out code, out detail),
                Is.EqualTo(OutboxAdmission.Duplicate),
                "one committed event is one obligation (P-050)");
            Assert.That(again, Is.Not.Null);
            Assert.That(again!.Key.OutboxId, Is.EqualTo(obligation.Key.OutboxId));
            Assert.That(outbox.OpenCount, Is.EqualTo(1), "a duplicate adds nothing and loses nothing");
            Assert.That(outbox.CommitCount, Is.EqualTo(1));
            Assert.That(outbox.DuplicateCount, Is.EqualTo(1), detail);
        }

        [Test]
        public void CapacityExhaustionIsExplicitAndTheRefusedObligationIsNotTracked()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 2, 4, OutboxDurability.Durable);
            Assert.That(Commit(outbox, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), 1UL, out _, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
            Assert.That(Commit(outbox, DeliveryTestIds.Key(2UL, DeliveryTestIds.DestinationA), 2UL, out _, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));

            Assert.That(
                Commit(outbox, DeliveryTestIds.Key(3UL, DeliveryTestIds.DestinationA), 3UL, out DeliveryObligation? refused, out DiagnosticCode code, out string detail),
                Is.EqualTo(OutboxAdmission.AtCapacity));
            Assert.That(code, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(detail, Does.Contain("capacity"), "capacity exhaustion says why (P-052)");
            Assert.That(refused, Is.Null);
            Assert.That(outbox.OpenCount, Is.EqualTo(2));
            Assert.That(outbox.CapacityRefusalCount, Is.EqualTo(1));
            Assert.That(
                outbox.TryGet(DeliveryTestIds.Key(3UL, DeliveryTestIds.DestinationA).OutboxId, out DeliveryObligation? probe) && probe != null,
                Is.False,
                "a refused obligation is never silently tracked: capacity exhaustion cannot become a silent drop (P-043)");
        }

        [Test]
        public void AVolatileOutboxRefusesAnObligationThatRequiresDurability()
        {
            var volatileOutbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Volatile);
            var durableOutbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);

            Assert.That(volatileOutbox.IsDurable, Is.False);
            Assert.That(durableOutbox.IsDurable, Is.True);
            Assert.That(
                volatileOutbox.Durability,
                Is.EqualTo(OutboxDurability.Volatile),
                "volatile delivery is distinguishable from configured durable delivery in the type itself");

            Assert.That(
                volatileOutbox.TryCommit(
                    DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA),
                    DeliveryTestIds.CommandSchema,
                    DeliveryTestIds.Payload(1),
                    new EventSequence(1UL),
                    LogicalStepId.First,
                    AssemblyEpoch.First,
                    DeliveryTestIds.Operation(1UL),
                    true,
                    out DeliveryObligation? refused,
                    out DiagnosticCode code,
                    out string detail),
                Is.EqualTo(OutboxAdmission.DurabilityUnavailable));
            Assert.That(refused, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(detail, Does.Contain("durability"));
            Assert.That(volatileOutbox.DurabilityRefusalCount, Is.EqualTo(1));
            Assert.That(
                volatileOutbox.Count,
                Is.Zero,
                "an obligation the outbox cannot keep is refused, never accepted and then lost (P-045)");

            Assert.That(
                Commit(durableOutbox, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), 1UL, out _, out _, out string accepted),
                Is.EqualTo(OutboxAdmission.Accepted),
                accepted);
        }

        [Test]
        public void ATerminalObligationIsRetainedAndRedeliveryIsRefusedNotRepeated()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            Commit(outbox, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), 1UL, out DeliveryObligation? obligation, out _, out _);
            Id128 id = obligation!.Key.OutboxId;

            Assert.That(outbox.TryBeginDelivery(id, out _, out string delivered), Is.EqualTo(DeliveryOutcome.Delivered), delivered);
            Assert.That(outbox.TryAcknowledge(id, out _, out string acked), Is.EqualTo(DeliveryOutcome.Acknowledged), acked);
            Assert.That(outbox.OpenCount, Is.Zero);
            Assert.That(outbox.TerminalCount, Is.EqualTo(1), "a terminal record is retained, not deleted (P-045)");

            Assert.That(
                outbox.TryBeginDelivery(id, out _, out _),
                Is.EqualTo(DeliveryOutcome.AlreadyTerminal),
                "an acknowledged obligation is never handed over again");
            Assert.That(outbox.TerminalAttemptCount, Is.EqualTo(1));
            Assert.That(outbox.DeliveryCount, Is.EqualTo(1), "the destination was asked exactly once");
            Assert.That(outbox.RedeliveryCount, Is.Zero);
            Assert.That(
                outbox.TryAcknowledge(id, out _, out _),
                Is.EqualTo(DeliveryOutcome.AlreadyTerminal),
                "an acknowledgement is idempotent, not a second mutation");
            Assert.That(outbox.AcknowledgeCount, Is.EqualTo(1));
        }

        [Test]
        public void AnUnacknowledgedObligationIsOfferedAgainAndCounted()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            Commit(outbox, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), 1UL, out DeliveryObligation? obligation, out _, out _);
            Id128 id = obligation!.Key.OutboxId;

            Assert.That(outbox.TryBeginDelivery(id, out _, out _), Is.EqualTo(DeliveryOutcome.Delivered));
            Assert.That(
                outbox.TryBeginDelivery(id, out _, out string detail),
                Is.EqualTo(DeliveryOutcome.Delivered),
                "an unacknowledged obligation is offered again — at-least-once by contract (P-045)");
            Assert.That(outbox.RedeliveryCount, Is.EqualTo(1), detail);
            Assert.That(outbox.DeliveryCount, Is.EqualTo(2));
            Assert.That(obligation!.Attempts, Is.EqualTo(2U), "attempts are counted, not merged");
            Assert.That(obligation.IsOpen, Is.True, "it is still owed until the destination confirms it");
        }

        [Test]
        public void TerminalRetentionPrunesAndCountsRatherThanGrowingWithoutBound()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 8, 2, OutboxDurability.Durable);

            for (ulong i = 1UL; i <= 4UL; i++)
            {
                Commit(outbox, DeliveryTestIds.Key(i, DeliveryTestIds.DestinationA), i, out DeliveryObligation? obligation, out _, out _);
                Id128 id = obligation!.Key.OutboxId;
                outbox.TryBeginDelivery(id, out _, out _);
                outbox.TryAcknowledge(id, out _, out _);
            }

            Assert.That(outbox.PrunedTerminalCount, Is.EqualTo(2), "the two oldest terminal records were pruned");
            Assert.That(outbox.TerminalCount, Is.EqualTo(2), "two are retained, per the declared bound (P-045)");
            Assert.That(outbox.TryGetCursor(DeliveryTestIds.DestinationA, out DeliveryCursor cursor), Is.True);
            Assert.That(cursor.RetainedTerminals, Is.EqualTo(2U));
            Assert.That(cursor.TerminalTotal, Is.EqualTo(4U), "pruning is counted, never a silent drop (P-043)");
            Assert.That(cursor.PrunedTerminals, Is.EqualTo(2U));
            Assert.That(
                cursor.NewestAcknowledged,
                Is.EqualTo(DeliveryTestIds.Key(4UL, DeliveryTestIds.DestinationA).OutboxId),
                "the cursor reach is the newest acknowledged obligation (P-045)");
            Assert.That(
                outbox.TryGet(DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA).OutboxId, out DeliveryObligation? pruned) && pruned != null,
                Is.False,
                "a pruned terminal record really is gone");
        }

        [Test]
        public void RejectionAndCompensationAreTerminalAndKeepTheirReason()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            Commit(outbox, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), 1UL, out DeliveryObligation? rejected, out _, out _);
            Commit(outbox, DeliveryTestIds.Key(2UL, DeliveryTestIds.DestinationA), 2UL, out DeliveryObligation? compensated, out _, out _);

            Assert.That(
                outbox.TryReject(rejected!.Key.OutboxId, DiagnosticCode.ResourceUnavailable, out _, out _),
                Is.EqualTo(DeliveryOutcome.Rejected));
            Assert.That(
                outbox.TryCompensate(compensated!.Key.OutboxId, DiagnosticCode.OwnershipConflict, out _, out _),
                Is.EqualTo(DeliveryOutcome.Compensated));

            Assert.That(outbox.RejectCount, Is.EqualTo(1));
            Assert.That(outbox.CompensateCount, Is.EqualTo(1));
            Assert.That(outbox.OpenCount, Is.Zero);
            Assert.That(
                outbox.TryGet(rejected.Key.OutboxId, out DeliveryObligation? refusedRow) && refusedRow != null
                    ? refusedRow.Reason
                    : DiagnosticCode.None,
                Is.EqualTo(DiagnosticCode.ResourceUnavailable),
                "a terminal refusal keeps its reason, so the refusal stays observable (P-052)");
            Assert.That(
                outbox.TryGet(compensated.Key.OutboxId, out DeliveryObligation? compensatedRow) && compensatedRow != null
                    ? compensatedRow.State
                    : OutboxDeliveryState.Pending,
                Is.EqualTo(OutboxDeliveryState.Compensated),
                "compensation is its own terminal state, never a success (P-003)");
            Assert.That(
                outbox.TryGetCursor(DeliveryTestIds.DestinationA, out DeliveryCursor cursor) && cursor.HasAcknowledged,
                Is.False,
                "a refusal or a compensation never advances the acknowledgement cursor (P-045)");
        }

        [Test]
        public void AnUnknownObligationIsReportedRatherThanAssumed()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            Id128 unknown = DeliveryTestIds.Key(9UL, DeliveryTestIds.DestinationA).OutboxId;

            Assert.That(outbox.TryBeginDelivery(unknown, out DiagnosticCode code, out string detail), Is.EqualTo(DeliveryOutcome.NoObligation));
            Assert.That(code, Is.EqualTo(DiagnosticCode.StaleHandle), detail);
            Assert.That(outbox.TryAcknowledge(unknown, out _, out _), Is.EqualTo(DeliveryOutcome.NoObligation));
            Assert.That(outbox.TryReject(unknown, DiagnosticCode.None, out _, out _), Is.EqualTo(DeliveryOutcome.NoObligation));
            Assert.That(outbox.TryCompensate(unknown, DiagnosticCode.None, out _, out _), Is.EqualTo(DeliveryOutcome.NoObligation));
            Assert.That(outbox.UnknownObligationCount, Is.EqualTo(4));
        }

        private static OutboxAdmission Commit(
            DurableOutbox outbox,
            DeliveryKey key,
            ulong ordinal,
            out DeliveryObligation? obligation,
            out DiagnosticCode code,
            out string detail) =>
            outbox.TryCommit(
                key,
                DeliveryTestIds.CommandSchema,
                DeliveryTestIds.Payload((byte)ordinal),
                new EventSequence(ordinal),
                new LogicalStepId(ordinal),
                AssemblyEpoch.First,
                DeliveryTestIds.Operation(ordinal),
                false,
                out obligation,
                out code,
                out detail);
    }

    /// <summary>The row codec and the file-backed journal: framing, refusal and torn-tail behavior (P-054).</summary>
    [TestFixture]
    public sealed class DeliveryJournalTests
    {
        [Test]
        public void ACanonicalRowFrameRoundTripsEveryField()
        {
            var codec = new CanonicalOutboxRowCodec();
            OutboxRecordValue row = DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 5UL);

            byte[] frame = codec.Encode(row);
            Assert.That(codec.TryDecode(frame, out OutboxRecordValue decoded, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(decoded.Row, Is.EqualTo(row.Row));
            Assert.That(decoded.RecordVersion, Is.EqualTo(OutboxRecordValue.CurrentRecordVersion));
            Assert.That(decoded.OutboxId, Is.EqualTo(row.OutboxId));
            Assert.That(decoded.DestinationId, Is.EqualTo(row.DestinationId));
            Assert.That(decoded.IdempotencyKey, Is.EqualTo(row.IdempotencyKey));
            Assert.That(decoded.SourceEvent.Value, Is.EqualTo(row.SourceEvent.Value));
            Assert.That(decoded.Step.Value, Is.EqualTo(row.Step.Value));
            Assert.That(decoded.Epoch.Value, Is.EqualTo(row.Epoch.Value));
            Assert.That(decoded.CausalIssuerId, Is.EqualTo(row.CausalIssuerId));
            Assert.That(decoded.CausalIssuerOrdinal, Is.EqualTo(row.CausalIssuerOrdinal));
            Assert.That(decoded.PayloadSchema, Is.EqualTo(row.PayloadSchema));
            Assert.That(decoded.State, Is.EqualTo(row.State));
            Assert.That(decoded.Reason, Is.EqualTo(row.Reason));
            Assert.That(decoded.Attempts, Is.EqualTo(row.Attempts));
            Assert.That(decoded.DurabilityClass, Is.EqualTo(row.DurabilityClass));
            Assert.That(decoded.Order, Is.EqualTo(row.Order));
            Assert.That(decoded.PrunedTerminals, Is.EqualTo(row.PrunedTerminals));
            Assert.That(decoded.PayloadBytes(), Is.EqualTo(row.PayloadBytes()), "the recorded command bytes survive the frame");
        }

        [Test]
        public void ARowFrameWithATamperedByteOrAnotherVersionIsRefusedRatherThanDecoded()
        {
            var codec = new CanonicalOutboxRowCodec();
            byte[] tampered = codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL));
            tampered[tampered.Length / 2] ^= 0xFF;
            Assert.That(codec.TryDecode(tampered, out _, out DiagnosticCode tamperedCode, out string tamperedDetail), Is.False);
            Assert.That(tamperedCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable), tamperedDetail);

            byte[] future = codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL));
            future[6] = 9;
            Assert.That(codec.TryDecode(future, out _, out DiagnosticCode futureCode, out string futureDetail), Is.False);
            Assert.That(futureCode, Is.EqualTo(DiagnosticCode.UnsupportedVersion), futureDetail);

            byte[] wrongFieldCount = codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL));
            wrongFieldCount[10] = 26;
            Assert.That(codec.TryDecode(wrongFieldCount, out _, out DiagnosticCode countCode, out string countDetail), Is.False);
            Assert.That(countCode, Is.EqualTo(DiagnosticCode.UnsupportedVersion), countDetail);
        }

        [Test]
        public void AFileJournalKeepsCompleteFramesAndDiscardsATornTail()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var codec = new CanonicalOutboxRowCodec();
                var journal = new FileDeliveryJournal(path);
                Assert.That(
                    journal.TryAppend(codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL)), out _, out string first),
                    Is.True,
                    first);
                Assert.That(
                    journal.TryAppend(codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Terminal, DeliveryTestIds.DestinationA, 1UL)), out _, out string second),
                    Is.True,
                    second);
                Assert.That(journal.FrameCount, Is.EqualTo(2));
                Assert.That(journal.Exists, Is.True);
                Assert.That(new FileInfo(path).Length, Is.GreaterThan(0));

                // A torn write: the process died partway through a third frame.
                byte[] torn = codec.Encode(DeliveryTestIds.Row(OutboxRowKind.Terminal, DeliveryTestIds.DestinationA, 2UL));
                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(torn, 0, torn.Length / 2);
                }

                var reopened = new FileDeliveryJournal(path);
                Assert.That(reopened.TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail), Is.True, detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(frames.Count, Is.EqualTo(2), "the two complete frames remain readable");
                Assert.That(reopened.FrameCount, Is.EqualTo(2));
                Assert.That(reopened.DiscardedFrameCount, Is.EqualTo(1), "the torn tail is counted, never decoded");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void AnEmptyOrMissingJournalReadsAsNoFramesRatherThanAnError()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var missing = new FileDeliveryJournal(path);
                Assert.That(missing.Exists, Is.False);
                Assert.That(missing.TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail), Is.True, detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(frames, Is.Empty);

                Assert.That(missing.TryAppend(Array.Empty<byte>(), out DiagnosticCode emptyCode, out _), Is.False);
                Assert.That(emptyCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
                Assert.That(missing.FrameCount, Is.Zero);
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }
    }

    /// <summary>
    /// The adapter's ordering and its deterministic crash points: before/after append, before/after delivery and
    /// before/after acknowledgement — GC-021's "persistence boundaries are covered by deterministic crash-point
    /// tests".
    /// </summary>
    [TestFixture]
    public sealed class DurableDeliveryAdapterTests
    {
        [Test]
        public void ACrashBeforeTheAppendLeavesNothingCommitted()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var adapter = new DurableDeliveryAdapter(outbox, new FileDeliveryJournal(path), new DeliveryCrashMarker(DeliveryCrashPoint.BeforeAppend));

                Assert.Throws<DeliveryCrashException>(
                    () => CommitThroughAdapter(adapter, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), out _, out _, out _));

                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);
                Assert.That(
                    recovered.OpenCount,
                    Is.Zero,
                    "a crash before the append means the caller was never told the commit happened");
                Assert.That(recovered.CommitCount, Is.Zero);
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void ACrashAfterTheAppendLeavesACommittedObligationThatOutlivesTheSourceWorld()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var adapter = new DurableDeliveryAdapter(outbox, new FileDeliveryJournal(path), new DeliveryCrashMarker(DeliveryCrashPoint.AfterAppend));

                Assert.Throws<DeliveryCrashException>(
                    () => CommitThroughAdapter(adapter, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), out _, out _, out _));

                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);
                Assert.That(
                    recovered.OpenCount,
                    Is.EqualTo(1),
                    "an obligation appended before the crash survives the process that committed it (P-045)");
                Assert.That(recovered.CommitCount, Is.EqualTo(1));
                Assert.That(
                    recovered.TryGet(DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA).OutboxId, out DeliveryObligation? obligation) && obligation != null
                        ? obligation.IsOpen
                        : false,
                    Is.True,
                    "source unload does not erase a committed delivery obligation");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void RedeliveryAfterAcknowledgmentLossAppliesTheDestinationMutationOnce()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);
                var destination = new RecordingDestination(DeliveryTestIds.DestinationA);

                // First life: commit, hand the obligation over, and die between the destination's mutation and the
                // local record of it — exactly the acknowledgement-loss window P-045 names.
                var firstOutbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var first = new DurableDeliveryAdapter(firstOutbox, new FileDeliveryJournal(path), new DeliveryCrashMarker(DeliveryCrashPoint.AfterDelivery));
                Assert.That(CommitThroughAdapter(first, key, out DeliveryObligation? obligation, out _, out string commitDetail), Is.EqualTo(OutboxAdmission.Accepted), commitDetail);

                Assert.Throws<DeliveryCrashException>(
                    () => first.TryDeliver(obligation!.Key.OutboxId, destination, out _, out _));
                Assert.That(destination.MutationCount, Is.EqualTo(1), "the destination did mutate before the crash");
                Assert.That(destination.Attempts.Count, Is.EqualTo(1));

                // Second life: a fresh adapter over the same journal, carrying no in-memory state across.
                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);
                Assert.That(recovered.OpenCount, Is.EqualTo(1), "the obligation is still owed after recovery");
                Assert.That(
                    recovered.TryGet(key.OutboxId, out DeliveryObligation? reinstated) && reinstated != null
                        ? reinstated.Key.IdempotencyKey
                        : default(Id128),
                    Is.EqualTo(key.IdempotencyKey),
                    "the reinstated obligation carries the same external key, which is what makes redelivery safe");

                var second = new DurableDeliveryAdapter(recovered, new FileDeliveryJournal(path));
                DeliveryOutcome outcome = second.TryDeliver(key.OutboxId, destination, out DiagnosticCode deliveryCode, out string deliveryDetail);

                Assert.That(outcome, Is.EqualTo(DeliveryOutcome.Acknowledged), deliveryDetail);
                Assert.That(deliveryCode, Is.EqualTo(DiagnosticCode.None));
                Assert.That(
                    destination.MutationCount,
                    Is.EqualTo(1),
                    "redelivery after acknowledgement loss applies the destination mutation exactly once (P-045)");
                Assert.That(destination.AlreadyAppliedCount, Is.EqualTo(1));
                Assert.That(destination.Attempts.Count, Is.EqualTo(2), "the destination really was asked twice");
                Assert.That(second.AlreadyAppliedCount, Is.EqualTo(1));
                Assert.That(recovered.OpenCount, Is.Zero, "the redelivery settled the obligation");

                // Third life: the acknowledgement is now durable, so a further redelivery reaches no destination.
                DurableOutbox settled = Recover(path, out _, out _);
                Assert.That(settled.OpenCount, Is.Zero);
                Assert.That(settled.AcknowledgeCount, Is.EqualTo(1));
                var third = new DurableDeliveryAdapter(settled, new FileDeliveryJournal(path));
                Assert.That(
                    third.TryDeliver(key.OutboxId, destination, out _, out _),
                    Is.EqualTo(DeliveryOutcome.AlreadyTerminal));
                Assert.That(destination.Attempts.Count, Is.EqualTo(2), "a settled obligation is answered from the record");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void ACrashBeforeTheAcknowledgementLeavesTheObligationRedeliverable()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);
                var destination = new RecordingDestination(DeliveryTestIds.DestinationA);
                var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var adapter = new DurableDeliveryAdapter(outbox, new FileDeliveryJournal(path), new DeliveryCrashMarker(DeliveryCrashPoint.BeforeAcknowledge));

                Assert.That(CommitThroughAdapter(adapter, key, out DeliveryObligation? obligation, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
                Assert.That(adapter.TryDeliver(obligation!.Key.OutboxId, destination, out _, out string deliveryDetail), Is.EqualTo(DeliveryOutcome.Delivered), deliveryDetail);
                Assert.Throws<DeliveryCrashException>(() => adapter.TryAcknowledge(obligation.Key.OutboxId, out _, out _));

                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);
                Assert.That(recovered.OpenCount, Is.EqualTo(1), "the acknowledgement never became durable");
                Assert.That(recovered.AcknowledgeCount, Is.Zero);
                Assert.That(
                    recovered.TryGet(key.OutboxId, out DeliveryObligation? reinstated) && reinstated != null
                        ? reinstated.State
                        : OutboxDeliveryState.Acknowledged,
                    Is.EqualTo(OutboxDeliveryState.Delivered),
                    "the delivery was recorded but not acknowledged, so the obligation is still owed");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void ACrashAfterTheAcknowledgementLeavesItSettledAndARedeliveryDoingNothing()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);
                var destination = new RecordingDestination(DeliveryTestIds.DestinationA);
                var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var adapter = new DurableDeliveryAdapter(outbox, new FileDeliveryJournal(path), new DeliveryCrashMarker(DeliveryCrashPoint.AfterAcknowledge));

                Assert.That(CommitThroughAdapter(adapter, key, out DeliveryObligation? obligation, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
                Assert.That(adapter.TryDeliver(obligation!.Key.OutboxId, destination, out _, out string deliveryDetail), Is.EqualTo(DeliveryOutcome.Delivered), deliveryDetail);
                Assert.Throws<DeliveryCrashException>(() => adapter.TryAcknowledge(obligation.Key.OutboxId, out _, out _));

                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);
                Assert.That(
                    recovered.OpenCount,
                    Is.Zero,
                    "the acknowledgement was persisted before the in-memory state moved, so recovery finds it settled");
                Assert.That(recovered.AcknowledgeCount, Is.EqualTo(1));
                Assert.That(recovered.TryGetCursor(DeliveryTestIds.DestinationA, out DeliveryCursor cursor), Is.True);
                Assert.That(cursor.HasAcknowledged, Is.True);
                Assert.That(cursor.NewestAcknowledged, Is.EqualTo(key.OutboxId), "the cursor row was carried too");

                var third = new DurableDeliveryAdapter(recovered, new FileDeliveryJournal(path));
                Assert.That(third.TryDeliver(key.OutboxId, destination, out _, out string redelivery), Is.EqualTo(DeliveryOutcome.AlreadyTerminal), redelivery);
                Assert.That(destination.Attempts.Count, Is.EqualTo(1), "only the first life ever reached the destination");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void ADurableAdapterDeclaresDurabilityAndAVolatileOneRefusesToPromiseIt()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var durableOutbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Unspecified);
                var durable = new DurableDeliveryAdapter(durableOutbox, new FileDeliveryJournal(path));
                Assert.That(durable.IsDurable, Is.True);
                Assert.That(durable.Durability, Is.EqualTo(OutboxDurability.Durable));
                Assert.That(durable.Journal, Is.Not.Null);
                Assert.That(durableOutbox.IsDurable, Is.True, "the journal makes the outbox declare durable");

                var volatileOutbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Unspecified);
                var volatileAdapter = new DurableDeliveryAdapter(volatileOutbox);
                Assert.That(volatileAdapter.IsDurable, Is.False);
                Assert.That(volatileAdapter.Journal, Is.Null);
                Assert.That(volatileAdapter.Durability, Is.EqualTo(OutboxDurability.Volatile));
                Assert.That(
                    volatileAdapter.TryRecover(out DiagnosticCode code, out string detail),
                    Is.False,
                    "an in-memory obligation is not recoverable, and the adapter says so instead of pretending");
                Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
                Assert.That(detail, Does.Contain("volatile"));

                Assert.That(CommitThroughAdapter(durable, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), out _, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
                IReadOnlyList<OutboxRecordValue> rows = durableOutbox.ToRecords();
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].DurabilityClass, Is.EqualTo(OutboxDurability.Durable), "the row records the durability the world had");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void AnAttemptForAnotherDestinationIsRefusedBeforeTheDestinationIsCalled()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            var adapter = new DurableDeliveryAdapter(outbox);
            var wrongDestination = new RecordingDestination(DeliveryTestIds.DestinationB);

            Assert.That(CommitThroughAdapter(adapter, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), out DeliveryObligation? obligation, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));

            Assert.That(
                adapter.TryDeliver(obligation!.Key.OutboxId, wrongDestination, out DiagnosticCode code, out string detail),
                Is.EqualTo(DeliveryOutcome.UnsupportedAttempt));
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict), detail);
            Assert.That(wrongDestination.Attempts, Is.Empty, "a delivery is never re-targeted (P-034)");
            Assert.That(obligation.IsOpen, Is.True, "the refused attempt left the obligation where it was");
        }

        [Test]
        public void AJournalRefusalPreventsTheCommitFromBeingRecordedInMemory()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            var adapter = new DurableDeliveryAdapter(outbox, new UnwritableJournal());
            var destination = new RecordingDestination(DeliveryTestIds.DestinationA);

            Assert.That(
                CommitThroughAdapter(adapter, DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA), out DeliveryObligation? obligation, out DiagnosticCode code, out string detail),
                Is.EqualTo(OutboxAdmission.JournalRefused),
                detail);
            Assert.That(obligation, Is.Null);
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(outbox.Count, Is.Zero, "the outbox never holds an obligation the journal does not (P-045)");
            Assert.That(adapter.AppendRefusalCount, Is.EqualTo(1));
            Assert.That(
                adapter.TryDeliver(DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA).OutboxId, destination, out _, out _),
                Is.EqualTo(DeliveryOutcome.NoObligation));
            Assert.That(destination.Attempts, Is.Empty);
        }

        [Test]
        public void AnUnavailableDestinationLeavesTheObligationOpenAndAnUnsupportedStateIsExplicit()
        {
            var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            var adapter = new DurableDeliveryAdapter(outbox);
            DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);
            Assert.That(CommitThroughAdapter(adapter, key, out DeliveryObligation? obligation, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));

            var unavailable = new RecordingDestination(DeliveryTestIds.DestinationA, DestinationOutcome.Unavailable)
            {
                Reason = DiagnosticCode.MissingDependency,
            };
            Assert.That(
                adapter.TryDeliver(obligation!.Key.OutboxId, unavailable, out DiagnosticCode openCode, out _),
                Is.EqualTo(DeliveryOutcome.Delivered));
            Assert.That(openCode, Is.EqualTo(DiagnosticCode.MissingDependency), "the destination's own reason travels");
            Assert.That(adapter.UnavailableCount, Is.EqualTo(1));
            Assert.That(obligation.IsOpen, Is.True, "an unavailable destination never settles the obligation (P-012)");

            var unsupported = new RecordingDestination(DeliveryTestIds.DestinationA, DestinationOutcome.UnsupportedState)
            {
                Reason = DiagnosticCode.OwnershipConflict,
            };
            Assert.That(
                adapter.TryDeliver(obligation.Key.OutboxId, unsupported, out DiagnosticCode refusalCode, out string refusalDetail),
                Is.EqualTo(DeliveryOutcome.Rejected));
            Assert.That(refusalCode, Is.EqualTo(DiagnosticCode.OwnershipConflict), refusalDetail);
            Assert.That(obligation.State, Is.EqualTo(OutboxDeliveryState.Rejected), "unsupported destination state is an explicit rejection");
            Assert.That(outbox.RejectCount, Is.EqualTo(1));

            var compensated = new RecordingDestination(DeliveryTestIds.DestinationA);
            DeliveryKey second = DeliveryTestIds.Key(2UL, DeliveryTestIds.DestinationA);
            Assert.That(CommitThroughAdapter(adapter, second, out DeliveryObligation? other, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
            compensated.Outcome = DestinationOutcome.Compensated;
            compensated.Reason = DiagnosticCode.OwnershipConflict;
            Assert.That(
                adapter.TryDeliver(other!.Key.OutboxId, compensated, out _, out _),
                Is.EqualTo(DeliveryOutcome.Compensated));
            Assert.That(other.State, Is.EqualTo(OutboxDeliveryState.Compensated));
            Assert.That(outbox.CompensateCount, Is.EqualTo(1));
        }

        [Test]
        public void ARestoredOutboxProjectsRowsThatSurviveAJournalRoundTrip()
        {
            string path = DeliveryTestIds.TempJournalPath();
            try
            {
                var outbox = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
                var adapter = new DurableDeliveryAdapter(outbox, new FileDeliveryJournal(path));
                var destination = new RecordingDestination(DeliveryTestIds.DestinationA);
                DeliveryKey key = DeliveryTestIds.Key(1UL, DeliveryTestIds.DestinationA);
                Assert.That(CommitThroughAdapter(adapter, key, out DeliveryObligation? obligation, out _, out _), Is.EqualTo(OutboxAdmission.Accepted));
                Assert.That(adapter.TryDeliver(obligation!.Key.OutboxId, destination, out _, out _), Is.EqualTo(DeliveryOutcome.Delivered));

                DurableOutbox recovered = Recover(path, out DiagnosticCode code, out string detail);
                Assert.That(code, Is.EqualTo(DiagnosticCode.None), detail);

                IReadOnlyList<OutboxRecordValue> rows = recovered.ToRecords();
                Assert.That(rows.Count, Is.EqualTo(1), "one open obligation, one row");
                Assert.That(rows[0].Row, Is.EqualTo(OutboxRowKind.Obligation));
                Assert.That(rows[0].IsOpen, Is.True, "the obligation a checkpoint carries is still owed (P-045)");
                Assert.That(rows[0].RecordVersion, Is.EqualTo(OutboxRecordValue.CurrentRecordVersion));
                Assert.That(rows[0].HasPayload, Is.True, "the destination command's bytes travel with the obligation");
                Assert.That(rows[0].Attempts, Is.EqualTo(1U), "the attempt survives the crash too");

                Assert.That(
                    DurableOutbox.TryRestore(
                        rows,
                        DeliveryTestIds.Owner,
                        4,
                        4,
                        OutboxDurability.Durable,
                        out DurableOutbox? rebuilt,
                        out string rebuildDetail),
                    Is.True,
                    rebuildDetail);
                Assert.That(rebuilt, Is.Not.Null);
                Assert.That(rebuilt!.OpenCount, Is.EqualTo(1));
                Assert.That(rebuilt.CommitCount, Is.EqualTo(1), "a rebuilt outbox reports what it reinstated");
                Assert.That(rebuilt.TerminalAdoptionCount, Is.Zero);
                Assert.That(rebuilt.NextOrder, Is.EqualTo(1U), "the canonical order continues where it left off (P-008)");
                Assert.That(
                    rebuilt.TryGet(rows[0].OutboxId, out DeliveryObligation? reinstated) && reinstated != null
                        ? reinstated.PayloadBytes()
                        : Array.Empty<byte>(),
                    Is.EqualTo(rows[0].PayloadBytes()),
                    "the reinstated obligation carries its own recorded command");
            }
            finally
            {
                DeliveryTestIds.Cleanup(path);
            }
        }

        [Test]
        public void ARestoreRefusesARowFromAnUnknownRecordVersionOrOfAnUnknownKind()
        {
            OutboxRecordValue good = DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL);

            Assert.That(
                DurableOutbox.TryRestore(
                    new[] { With(good, recordVersion: OutboxRecordValue.CurrentRecordVersion + 1U, rowKind: good.RowKind) },
                    DeliveryTestIds.Owner,
                    4,
                    4,
                    OutboxDurability.Durable,
                    out DurableOutbox? future,
                    out string futureDetail),
                Is.False);
            Assert.That(future, Is.Null);
            Assert.That(futureDetail, Does.Contain("record version"));

            Assert.That(
                DurableOutbox.TryRestore(
                    new[] { With(good, recordVersion: good.RecordVersion, rowKind: (OutboxRowKind)9) },
                    DeliveryTestIds.Owner,
                    4,
                    4,
                    OutboxDurability.Durable,
                    out DurableOutbox? unknown,
                    out string unknownDetail),
                Is.False);
            Assert.That(unknown, Is.Null);
            Assert.That(unknownDetail, Does.Contain("row kind"));
        }

        [Test]
        public void ACursorRowIsReinstatedWithItsReachRetentionAndPrunedCounts()
        {
            // The *planner* is what refuses an inconsistent outbox section (the pure suite covers that); this case
            // proves the outbox honours exactly what a consistent document says, including the reach and the counts
            // a checkpoint cursor carries (P-045, P-053).
            OutboxRecordValue obligation = DeliveryTestIds.Row(OutboxRowKind.Obligation, DeliveryTestIds.DestinationA, 1UL);
            OutboxRecordValue cursor = CursorRow(DeliveryTestIds.DestinationA, retainedTerminals: 3U, pruned: 0U);

            var rows = new List<OutboxRecordValue> { obligation, cursor };
            Assert.That(
                DurableOutbox.TryRestore(rows, DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable, out DurableOutbox? outbox, out string detail),
                Is.True,
                detail);
            Assert.That(outbox, Is.Not.Null);
            Assert.That(
                outbox!.TryGetCursor(DeliveryTestIds.DestinationA, out DeliveryCursor restoredCursor),
                Is.True);
            Assert.That(
                restoredCursor.RetainedTerminals,
                Is.EqualTo(3U),
                "the outbox reinstates the reach the document records, verbatim");
            Assert.That(restoredCursor.PrunedTerminals, Is.Zero);
        }

        private static OutboxRecordValue With(OutboxRecordValue source, uint recordVersion, OutboxRowKind rowKind) =>
            new OutboxRecordValue(
                (uint)rowKind,
                recordVersion,
                source.OutboxHigh,
                source.OutboxLow,
                source.DestinationHigh,
                source.DestinationLow,
                source.IdempotencyHigh,
                source.IdempotencyLow,
                source.SourceEventSequence,
                source.SourceStep,
                source.SourceEpoch,
                source.CausalIssuerHigh,
                source.CausalIssuerLow,
                source.CausalIssuerSequence,
                source.PayloadSchemaHigh,
                source.PayloadSchemaLow,
                source.PayloadSchemaVersion,
                source.DeliveryState,
                source.ReasonCode,
                source.AttemptCount,
                source.Durability,
                source.OrderOrdinal,
                source.CursorHigh,
                source.CursorLow,
                source.CursorCount,
                source.PrunedCount,
                source.Payload);

        private static OutboxRecordValue CursorRow(Id128 destination, uint retainedTerminals, uint pruned) =>
            new OutboxRecordValue(
                (uint)OutboxRowKind.Cursor,
                OutboxRecordValue.CurrentRecordVersion,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                (uint)OutboxDeliveryState.Acknowledged,
                (uint)DiagnosticCode.None,
                0U,
                (uint)OutboxDurability.Durable,
                0U,
                destination.High,
                destination.Low,
                retainedTerminals,
                pruned,
                null);

        /// <summary>A fresh outbox over the same journal, carrying no in-memory state across (P-045).</summary>
        internal static DurableOutbox Recover(string path, out DiagnosticCode code, out string detail)
        {
            var recovered = new DurableOutbox(DeliveryTestIds.Owner, 4, 4, OutboxDurability.Durable);
            var adapter = new DurableDeliveryAdapter(recovered, new FileDeliveryJournal(path));
            Assert.That(adapter.TryRecover(out code, out detail), Is.True, detail);
            return recovered;
        }

        /// <summary>One commit through the adapter, with the durability requirement a durable caller states (P-045).</summary>
        internal static OutboxAdmission CommitThroughAdapter(
            DurableDeliveryAdapter adapter,
            DeliveryKey key,
            out DeliveryObligation? obligation,
            out DiagnosticCode code,
            out string detail) =>
            adapter.TryCommit(
                key,
                DeliveryTestIds.CommandSchema,
                DeliveryTestIds.Payload(1),
                new EventSequence(1UL),
                LogicalStepId.First,
                AssemblyEpoch.First,
                DeliveryTestIds.Operation(1UL),
                true,
                out obligation,
                out code,
                out detail);
    }
}
