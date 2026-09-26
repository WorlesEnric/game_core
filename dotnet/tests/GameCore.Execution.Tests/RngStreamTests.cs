// GameCore.Execution.Tests - deterministic random-stream tests (GC-018, RngStreams.cs).
//
// The persistence layer's RNG streams exist so a checkpoint can save and resume "identical seed streams" (P-008) and
// so a replay can prove it consumed the same number of values (P-053). These tests pin the four properties that
// make the type usable by a capture: a seed fixes a sequence, a bounded draw never leaves its range, the draw count
// is an exact and non-wrapping counter, and a record round-trips the generator state verbatim.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>Deterministic random streams of a checkpoint (P-008, P-053).</summary>
    [TestFixture]
    public sealed class RngStreamTests
    {
        private static readonly Id128 StreamA = new Id128(0x4743303138524E47UL, 1UL);
        private static readonly Id128 StreamB = new Id128(0x4743303138524E47UL, 2UL);

        private const ulong StreamKey = 7UL;
        private const ulong Seed = 12345UL;
        private const ulong OtherSeed = 54321UL;

        /// <summary>Draws <paramref name="count"/> values, so two streams can be compared position by position.</summary>
        private static ulong[] Draw(RngStream stream, int count)
        {
            var values = new ulong[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = stream.Next();
            }

            return values;
        }

        private static RngStream NewStream() => new RngStream(StreamA, StreamKey, Seed);

        [Test]
        public void ASeedFixesTheStreamSequenceAndADifferentSeedDoesNot()
        {
            var first = NewStream();
            var second = NewStream();
            var other = new RngStream(StreamA, StreamKey, OtherSeed);

            ulong[] firstDraws = Draw(first, 8);
            Assert.That(Draw(second, 8), Is.EqualTo(firstDraws));
            Assert.That(Draw(other, 8), Is.Not.EqualTo(firstDraws));

            // The first value is pinned by the xorshift* arithmetic this generator documents, so a re-seeding
            // regression cannot pass by making two streams wrong in exactly the same way (P-008).
            Assert.That(firstDraws[0], Is.EqualTo(0x47EDFD1CD809B6DCUL));
            Assert.That(firstDraws[1], Is.EqualTo(0x34D004209D31C6BAUL));
        }

        [Test]
        public void AZeroBoundIsRefusedWithoutAdvancingAndBoundedDrawsStayInRange()
        {
            RngStream stream = NewStream();

            Assert.That(stream.TryNextBounded(0U, out uint refused), Is.False);
            Assert.That(refused, Is.Zero);
            Assert.That(stream.DrawCount, Is.Zero);

            for (uint bound = 1U; bound <= 64U; bound++)
            {
                Assert.That(stream.TryNextBounded(bound, out uint value), Is.True);
                Assert.That(value, Is.LessThan(bound), "a bounded draw must stay inside [0, bound).");
            }

            // Rejection sampling may consume more than one raw value per bounded draw, but never zero draws overall.
            Assert.That(stream.DrawCount, Is.GreaterThanOrEqualTo(64UL));
        }

        [Test]
        public void DrawCountAdvancesOncePerDrawAndSaturatesInsteadOfWrapping()
        {
            RngStream stream = NewStream();
            Assert.That(stream.DrawCount, Is.Zero);

            stream.Next();
            Assert.That(stream.DrawCount, Is.EqualTo(1UL));

            stream.Next();
            Assert.That(stream.DrawCount, Is.EqualTo(2UL));

            RngStream saturated = RngStream.Restore(
                new RngRecordValue(StreamA.High, StreamA.Low, stream.State, StreamKey, ulong.MaxValue));
            Assert.That(saturated.DrawCount, Is.EqualTo(ulong.MaxValue));

            saturated.Next();
            Assert.That(saturated.DrawCount, Is.EqualTo(ulong.MaxValue));
        }

        [Test]
        public void ARecordRoundTripPreservesStateAndDrawCountAndContinuesTheSequence()
        {
            RngStream stream = NewStream();
            Draw(stream, 5);

            RngStream restored = RngStream.Restore(stream.ToRecord());

            Assert.That(restored.StreamId, Is.EqualTo(stream.StreamId));
            Assert.That(restored.StreamKey, Is.EqualTo(stream.StreamKey));
            Assert.That(restored.State, Is.EqualTo(stream.State));
            Assert.That(restored.DrawCount, Is.EqualTo(stream.DrawCount));

            // A restore that re-seeded the generator would restart the sequence instead of continuing it (P-053).
            Assert.That(Draw(restored, 4), Is.EqualTo(Draw(stream, 4)));
        }

        [Test]
        public void AZeroStateSurvivesTheRoundTripRatherThanReseeding()
        {
            var record = new RngRecordValue(StreamA.High, StreamA.Low, 0UL, StreamKey, 9UL);

            RngStream restored = RngStream.Restore(record);

            Assert.That(restored.State, Is.Zero);
            Assert.That(restored.DrawCount, Is.EqualTo(9UL));
            Assert.That(restored.ToRecord().State, Is.Zero);
            Assert.That(restored.ToRecord().DrawCount, Is.EqualTo(9UL));
            Assert.That(RngStream.Restore(restored.ToRecord()).State, Is.Zero);
        }

        [Test]
        public void DeclarationRefusesZeroAndDuplicateIdentitiesAndLookupNeverCreatesAStream()
        {
            var table = new RngStreamTable();

            Assert.That(table.TryDeclare(default(Id128), StreamKey, Seed, out RngStream? zero, out string zeroDetail), Is.False);
            Assert.That(zero, Is.Null);
            Assert.That(zeroDetail, Is.Not.Empty);
            Assert.That(table.Count, Is.Zero);

            Assert.That(table.TryDeclare(StreamA, StreamKey, Seed, out RngStream? declared, out string firstDetail), Is.True);
            Assert.That(declared, Is.Not.Null);
            Assert.That(firstDetail, Is.Empty);
            Assert.That(table.Count, Is.EqualTo(1));

            Assert.That(table.TryDeclare(StreamA, StreamKey, OtherSeed, out RngStream? duplicate, out string duplicateDetail), Is.False);
            Assert.That(duplicate, Is.Null);
            Assert.That(duplicateDetail, Is.Not.Empty);
            Assert.That(table.Count, Is.EqualTo(1));

            Assert.That(table.TryGet(StreamB, out RngStream? unknown), Is.False);
            Assert.That(unknown, Is.Null);
            Assert.That(table.Count, Is.EqualTo(1));

            // A miss is a value and never an implicitly created stream (P-008).
            Assert.That(table.TryGet(StreamB, out RngStream? stillUnknown), Is.False);
            Assert.That(stillUnknown, Is.Null);
            Assert.That(table.Count, Is.EqualTo(1));
            Assert.That(table.StreamIds.Count, Is.EqualTo(1));
        }

        [Test]
        public void RecordsAreInCanonicalIdentityOrderWhateverTheDeclarationOrder()
        {
            var forward = new RngStreamTable();
            Assert.That(forward.TryDeclare(StreamA, StreamKey, Seed, out RngStream? _, out string _), Is.True);
            Assert.That(forward.TryDeclare(StreamB, StreamKey, OtherSeed, out RngStream? _, out string _), Is.True);

            var backward = new RngStreamTable();
            Assert.That(backward.TryDeclare(StreamB, StreamKey, OtherSeed, out RngStream? _, out string _), Is.True);
            Assert.That(backward.TryDeclare(StreamA, StreamKey, Seed, out RngStream? _, out string _), Is.True);

            IReadOnlyList<RngRecordValue> forwardRecords = forward.ToRecords();
            IReadOnlyList<RngRecordValue> backwardRecords = backward.ToRecords();

            Assert.That(forwardRecords.Count, Is.EqualTo(2));
            Assert.That(forwardRecords[0].StreamId, Is.EqualTo(StreamA));
            Assert.That(forwardRecords[1].StreamId, Is.EqualTo(StreamB));
            Assert.That(backwardRecords, Is.EqualTo(forwardRecords));

            Assert.That(RngStreamTable.TryRestore(forwardRecords, out RngStreamTable? restored, out string restoredDetail), Is.True);
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.Count, Is.EqualTo(2));
            Assert.That(restoredDetail, Is.Empty);

            var duplicated = new List<RngRecordValue> { forwardRecords[0], forwardRecords[0] };
            Assert.That(RngStreamTable.TryRestore(duplicated, out RngStreamTable? refused, out string duplicateDetail), Is.False);
            Assert.That(refused, Is.Null);
            Assert.That(duplicateDetail, Is.Not.Empty);
        }

        [Test]
        public void TheAllZeroStreamIdentityIsRejectedByConstruction()
        {
            Assert.Throws<ArgumentException>(() => { _ = new RngStream(default(Id128), StreamKey, Seed); });
            Assert.Throws<ArgumentException>(() => { _ = RngStream.Restore(new RngRecordValue(0UL, 0UL, 1UL, StreamKey, 0UL)); });
        }
    }
}
