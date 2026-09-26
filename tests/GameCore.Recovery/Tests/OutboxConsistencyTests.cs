// GC-027's outbox-consistency suite (P-045, P-053, TEST-014).
//
// A recovered session only survives a crash if its delivery obligations and per-destination cursors are exactly the
// ones the checkpoint carried; `OutboxConsistency.Verify` is the census that decides it, and `Consistent` is the
// verdict a recovery transcript and an evidence file record. The rows the cases below feed it are built either by a
// real `DurableOutbox` (the happy path) or by hand in one named field (each disagreement), so every problem the
// report names is reached by a document a writer could actually have produced.
//
// The suite also proves the census does not mutate what it inspects: "Verify mutates nothing" is a contract, not a
// hope, because a report that advanced a cursor would itself be the hidden state P-049 forbids.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Recovery;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>The consistency census of a recovered outbox against the rows a checkpoint carried.</summary>
    [TestFixture]
    public sealed class OutboxConsistencyTests
    {
        private static readonly Id128 Owner = new Id128(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL);
        private static readonly Id128 Destination = new Id128(0x1111111111111111UL, 0x2222222222222222UL);
        private static readonly Id128 Obligation = new Id128(0xAAAAAAAAAAAAAAAAUL, 0xBBBBBBBBBBBBBBBBUL);
        private static readonly Id128 Unrecorded = new Id128(0xCCCCCCCCCCCCCCCCUL, 0xDDDDDDDDDDDDDDDDUL);

        [Test]
        public void ACheckpointThatMatchesItsRecoveredOutboxIsConsistent()
        {
            DurableOutbox outbox = OneObligationOutbox();
            IReadOnlyList<OutboxRecordValue> rows = outbox.ToRecords();
            Assert.That(rows.Count, Is.EqualTo(1), "one open obligation projects one obligation row and no cursor yet.");

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, outbox, "world-of-record");

            Assert.That(report.Consistent, Is.True, report.Describe());
            Assert.That(report.Problems, Is.Empty);
            Assert.That(report.Label, Is.EqualTo("world-of-record"));
            Assert.That(report.CarriedRows, Is.EqualTo(1));
            Assert.That(report.CarriedObligations, Is.EqualTo(1));
            Assert.That(report.CarriedOpenObligations, Is.EqualTo(1));
            Assert.That(report.CarriedTerminals, Is.EqualTo(0));
            Assert.That(report.CarriedCursors, Is.EqualTo(0));
            Assert.That(report.Cursors, Is.Empty);
            Assert.That(report.LiveTrackedCount, Is.EqualTo(1));
            Assert.That(report.LiveOpenCount, Is.EqualTo(1));
            Assert.That(report.LiveTerminalCount, Is.EqualTo(0));
            Assert.That(report.LivePrunedCount, Is.EqualTo(0));
            Assert.That(report.LiveTerminalAdoptionCount, Is.EqualTo(0));
            Assert.That(report.Describe(), Does.Contain("consistent=1"));
            Assert.That(report.Describe(), Does.Contain("carried=1(open=1,terminal=0,cursor=0)"));
            Assert.That(report.ToString(), Is.EqualTo(report.Describe()));
        }

        [Test]
        public void AnEmptyCheckpointAndAnEmptyOutboxAreConsistent()
        {
            var outbox = new DurableOutbox(Owner, 4, 2, OutboxDurability.Durable);
            OutboxConsistencyReport report = OutboxConsistency.Verify(
                Array.Empty<OutboxRecordValue>(), outbox, "empty");

            Assert.That(report.Consistent, Is.True, report.Describe());
            Assert.That(report.CarriedRows, Is.EqualTo(0));
            Assert.That(report.LiveTrackedCount, Is.EqualTo(0));
            Assert.That(report.LiveOpenCount, Is.EqualTo(0));
            Assert.That(report.LiveTerminalCount, Is.EqualTo(0));
            Assert.That(report.Describe(), Does.Contain("consistent=1"));

            OutboxConsistencyReport nullRows = OutboxConsistency.Verify(null, outbox, "null-rows");
            Assert.That(nullRows.Consistent, Is.True, nullRows.Describe());
            Assert.That(nullRows.CarriedRows, Is.EqualTo(0), "absent rows are read as no rows, never as a default row.");
        }

        [Test]
        public void ATerminalRowWithoutItsObligationRowIsInconsistent()
        {
            var rows = new List<OutboxRecordValue> { OutboxRow(OutboxRowKind.Terminal, Obligation, Destination, OutboxDeliveryState.Acknowledged) };

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, null, "orphan-terminal");

            Assert.That(report.Consistent, Is.False);
            Assert.That(report.CarriedTerminals, Is.EqualTo(0), "a terminal row nothing closed is not a terminal outcome.");
            Assert.That(report.CarriedObligations, Is.EqualTo(0));
            Assert.That(ProblemsContain(report, Obligation.ToString()), Is.True, report.Describe());
            Assert.That(ProblemsContain(report, "closes an obligation the checkpoint does not carry"), Is.True, report.Describe());
            Assert.That(report.Describe(), Does.Contain("consistent=0"));
        }

        [Test]
        public void TwoCursorRowsForOneDestinationAreInconsistent()
        {
            var rows = new List<OutboxRecordValue>
            {
                OutboxRow(OutboxRowKind.Obligation, Obligation, Destination, OutboxDeliveryState.Pending),
                CursorRow(Destination, default(Id128), 0U, 0U),
                CursorRow(Destination, default(Id128), 0U, 0U),
            };

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, null, "double-cursor");

            Assert.That(report.Consistent, Is.False);
            Assert.That(report.CarriedCursors, Is.EqualTo(2));
            Assert.That(report.Cursors.Count, Is.EqualTo(1), "one destination has one cursor (P-008).");
            Assert.That(ProblemsContain(report, "more than one cursor row"), Is.True, report.Describe());
        }

        [Test]
        public void ACursorThatDisagreesWithItsTerminalRowsIsInconsistent()
        {
            var rows = new List<OutboxRecordValue>
            {
                OutboxRow(OutboxRowKind.Obligation, Obligation, Destination, OutboxDeliveryState.Acknowledged),
                OutboxRow(OutboxRowKind.Terminal, Obligation, Destination, OutboxDeliveryState.Acknowledged),
                CursorRow(Destination, default(Id128), 5U, 0U),
            };

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, null, "cursor-disagreement");

            Assert.That(report.Consistent, Is.False);
            Assert.That(report.CarriedTerminals, Is.EqualTo(1));
            Assert.That(report.Cursors.Count, Is.EqualTo(1));
            Assert.That(report.Cursors[0].RetainedTerminals, Is.EqualTo(5U));
            Assert.That(report.Cursors[0].TerminalTotal, Is.EqualTo(5U));
            Assert.That(ProblemsContain(report, "declares 5 retained terminal rows and the checkpoint carries 1"), Is.True,
                report.Describe());
            Assert.That(ProblemsContain(report, Destination.ToString()), Is.True, report.Describe());
        }

        [Test]
        public void ACursorThatAcknowledgesAnUnrecordedIdentityIsInconsistent()
        {
            var rows = new List<OutboxRecordValue>
            {
                OutboxRow(OutboxRowKind.Obligation, Obligation, Destination, OutboxDeliveryState.Acknowledged),
                OutboxRow(OutboxRowKind.Terminal, Obligation, Destination, OutboxDeliveryState.Acknowledged),
                CursorRow(Destination, Unrecorded, 1U, 0U),
            };

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, null, "cursor-acknowledgement");

            Assert.That(report.Consistent, Is.False);
            Assert.That(report.Cursors.Count, Is.EqualTo(1));
            Assert.That(report.Cursors[0].HasAcknowledged, Is.True);
            Assert.That(ProblemsContain(report, Unrecorded.ToString()), Is.True, report.Describe());
            Assert.That(ProblemsContain(report, "carries no terminal row for it"), Is.True, report.Describe());
        }

        [Test]
        public void AnOutboxThatOwesFewerObligationsThanTheCheckpointCarriedIsInconsistent()
        {
            var rows = new List<OutboxRecordValue>
            {
                OutboxRow(OutboxRowKind.Obligation, Obligation, Destination, OutboxDeliveryState.Pending),
            };
            var recovered = new DurableOutbox(Owner, 4, 2, OutboxDurability.Durable);

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, recovered, "lost-obligation");

            Assert.That(report.Consistent, Is.False);
            Assert.That(report.CarriedOpenObligations, Is.EqualTo(1));
            Assert.That(report.LiveOpenCount, Is.EqualTo(0));
            Assert.That(ProblemsContain(report, "owes 0 open obligations and the checkpoint carried 1"), Is.True,
                report.Describe());
        }

        [Test]
        public void RecoveredRowSetRoundTripsThroughTheOutboxAndStaysConsistent()
        {
            DurableOutbox outbox = OneObligationOutbox();
            IReadOnlyList<OutboxRecordValue> rows = outbox.ToRecords();

            Assert.That(DurableOutbox.TryRestore(rows, Owner, 8, 4, OutboxDurability.Durable, out DurableOutbox? restored, out string detail),
                Is.True, detail);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.OpenCount, Is.EqualTo(1));

            OutboxConsistencyReport report = OutboxConsistency.Verify(rows, restored, "restored");
            Assert.That(report.Consistent, Is.True, report.Describe());
            Assert.That(restored.ToRecords().Count, Is.EqualTo(rows.Count));
        }

        [Test]
        public void VerifyMutatesNothing()
        {
            DurableOutbox outbox = OneObligationOutbox();
            int count = outbox.Count;
            int open = outbox.OpenCount;
            int terminal = outbox.TerminalCount;
            int commits = outbox.CommitCount;
            int deliveries = outbox.DeliveryCount;
            int acknowledgements = outbox.AcknowledgeCount;
            IReadOnlyList<OutboxRecordValue> rows = outbox.ToRecords();

            OutboxConsistency.Verify(rows, outbox, "census");
            OutboxConsistency.Verify(rows, outbox, "census");
            OutboxConsistency.Verify(Array.Empty<OutboxRecordValue>(), outbox, "census");
            OutboxConsistency.Verify(null, outbox, "census");

            Assert.That(outbox.Count, Is.EqualTo(count), "a census does not add, remove or settle an obligation.");
            Assert.That(outbox.OpenCount, Is.EqualTo(open));
            Assert.That(outbox.TerminalCount, Is.EqualTo(terminal));
            Assert.That(outbox.CommitCount, Is.EqualTo(commits));
            Assert.That(outbox.DeliveryCount, Is.EqualTo(deliveries));
            Assert.That(outbox.AcknowledgeCount, Is.EqualTo(acknowledgements));
            Assert.That(outbox.ToRecords().Count, Is.EqualTo(rows.Count));
        }

        /// <summary>A real outbox holding one committed, still-open obligation (P-045's durable append).</summary>
        private static DurableOutbox OneObligationOutbox()
        {
            var outbox = new DurableOutbox(Owner, 8, 4, OutboxDurability.Durable);
            var key = new DeliveryKey(
                Obligation,
                Destination,
                new Id128(0xEEEEEEEEEEEEEEEEUL, 0xFFFFFFFFFFFFFFFFUL));
            var schema = new SchemaRef(new SchemaId(new Id128(0x0102030405060708UL, 0x090A0B0C0D0E0F10UL)), 1U);
            var world = new WorldId(Owner);
            var operation = new OperationId(world, Owner, 1UL);

            OutboxAdmission admission = outbox.TryCommit(
                key,
                schema,
                new byte[] { 1, 2, 3 },
                new EventSequence(1UL),
                new LogicalStepId(1UL),
                new AssemblyEpoch(1UL),
                operation,
                requiresDurability: true,
                out DeliveryObligation? obligation,
                out DiagnosticCode code,
                out string detail);

            Assert.That(admission, Is.EqualTo(OutboxAdmission.Accepted), detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(obligation, Is.Not.Null);
            Assert.That(obligation!.IsOpen, Is.True);
            Assert.That(obligation.Key.IdempotencyKey, Is.Not.EqualTo(obligation.Key.OutboxId),
                "the external idempotency key is never the obligation identity (P-045).");
            return outbox;
        }

        /// <summary>
        /// One obligation or terminal row with every slot named: the row-kind and state under test, and otherwise a
        /// value a writer could have produced.
        /// </summary>
        private static OutboxRecordValue OutboxRow(
            OutboxRowKind kind,
            Id128 obligationId,
            Id128 destinationId,
            OutboxDeliveryState state)
        {
            return new OutboxRecordValue(
                (uint)kind,
                OutboxRecordValue.CurrentRecordVersion,
                obligationId.High,
                obligationId.Low,
                destinationId.High,
                destinationId.Low,
                destinationId.High ^ 0x5555555555555555UL,
                destinationId.Low ^ 0xAAAAAAAAAAAAAAAAUL,
                1UL,
                1UL,
                1UL,
                0UL,
                0UL,
                0UL,
                0x0102030405060708UL,
                0x090A0B0C0D0E0F10UL,
                1U,
                (uint)state,
                (uint)DiagnosticCode.None,
                0U,
                (uint)OutboxDurability.Durable,
                0U,
                0UL,
                0UL,
                0U,
                0U,
                null);
        }

        /// <summary>One cursor row: the newest acknowledged identity and the retained/pruned terminal counts.</summary>
        private static OutboxRecordValue CursorRow(Id128 destinationId, Id128 acknowledged, uint retainedTerminals, uint prunedTerminals)
        {
            return new OutboxRecordValue(
                (uint)OutboxRowKind.Cursor,
                OutboxRecordValue.CurrentRecordVersion,
                destinationId.High,
                destinationId.Low,
                destinationId.High,
                destinationId.Low,
                destinationId.High,
                destinationId.Low,
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
                (uint)OutboxDurability.Unspecified,
                0U,
                acknowledged.High,
                acknowledged.Low,
                retainedTerminals,
                prunedTerminals,
                null);
        }

        private static bool ProblemsContain(OutboxConsistencyReport report, string expected)
        {
            for (int i = 0; i < report.Problems.Count; i++)
            {
                if (report.Problems[i].IndexOf(expected, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
