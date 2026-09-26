// GC-027's recovery-transcript suite (P-043, P-049, P-052, P-053, TEST-016).
//
// The transcript is the executed evidence GC-027's definition of done asks for: every phase a recovery reached, in
// order, with the injection point recorded at the line where the fault fired, and the data-loss class every line was
// exposed to. The digest is the one value an evidence file pins, so this suite proves it changes when, and only
// when, the recovery did something different, and that the data-loss boundary is *derived* from the recorded lines
// (`RecoveryTranscript.DataLossBoundary`) rather than asserted in prose.
#nullable enable
using System;
using System.Text;
using GameCore.Contracts;
using GameCore.Execution.Recovery;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>Ordering, digest stability, the data-loss boundary and the bounded-overflow rule of a transcript.</summary>
    [TestFixture]
    public sealed class RecoveryTranscriptTests
    {
        [Test]
        public void LinesAreRecordedInOrderWithAssignedOrdinals()
        {
            var transcript = new RecoveryTranscript(8);
            RecoveryTranscriptLine first = transcript.Add(
                RecoveryPhase.SourceObserved, "faulted world observed", RecoveryDataLossClass.None);
            RecoveryTranscriptLine second = transcript.Add(
                RecoveryPhase.Capture, "committed boundary copied", RecoveryDataLossClass.None);
            RecoveryTranscriptLine third = transcript.Add(
                RecoveryPhase.Publish, "document published", RecoveryDataLossClass.None);

            Assert.That(transcript.Capacity, Is.EqualTo(8));
            Assert.That(first.Ordinal, Is.EqualTo(0));
            Assert.That(second.Ordinal, Is.EqualTo(1));
            Assert.That(third.Ordinal, Is.EqualTo(2));
            Assert.That(transcript.Count, Is.EqualTo(3));
            Assert.That(transcript.OverflowCount, Is.EqualTo(0));
            Assert.That(transcript.Lines.Count, Is.EqualTo(3));
            Assert.That(transcript.Lines[0].Ordinal, Is.EqualTo(0));
            Assert.That(transcript.Lines[1].Ordinal, Is.EqualTo(1));
            Assert.That(transcript.Lines[2].Ordinal, Is.EqualTo(2));
            Assert.That(transcript.Lines[0].Phase, Is.EqualTo(RecoveryPhase.SourceObserved));
            Assert.That(transcript.Lines[1].Phase, Is.EqualTo(RecoveryPhase.Capture));
            Assert.That(transcript.Lines[2].Phase, Is.EqualTo(RecoveryPhase.Publish));
            Assert.That(transcript.Lines[1].Detail, Is.EqualTo("committed boundary copied"));
            Assert.That(transcript.Lines[0].DataLoss, Is.EqualTo(RecoveryDataLossClass.None));
            Assert.That(transcript.Lines[0].FaultPointId, Is.Empty, "no fault fired on that line.");
            Assert.That(transcript.FaultCount, Is.EqualTo(0));

            Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryTranscript(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecoveryTranscript(-1));
        }

        [Test]
        public void DescribeIsTheLinesJoinedByLineFeeds()
        {
            RecoveryTranscript transcript = Recorded();

            string expected = transcript.Lines[0].ToLine()
                + "\n" + transcript.Lines[1].ToLine()
                + "\n" + transcript.Lines[2].ToLine();
            Assert.That(transcript.Describe(), Is.EqualTo(expected));
            Assert.That(transcript.Describe(), Does.Not.Contain("\r"), "the canonical form is LF-only.");
            Assert.That(transcript.Describe(), Does.Contain("000 SourceObserved loss=None faulted world observed"));

            var empty = new RecoveryTranscript(1);
            Assert.That(empty.Describe(), Is.EqualTo("<empty>"));
            Assert.That(empty.DigestHex(), Is.EqualTo(ContentHash.Compute(Encoding.UTF8.GetBytes("<empty>")).ToHex()));

            Assert.That(transcript.Summary(), Does.Contain("transcript(lines=3/8"));
            Assert.That(transcript.Summary(), Does.Contain("faults=1"));
            Assert.That(transcript.Summary(), Does.Contain("digest=" + transcript.DigestHex()));
            Assert.That(transcript.ToString(), Is.EqualTo(transcript.Summary()));
        }

        [Test]
        public void IdenticalLinesProduceEqualDigestsAndDifferentLinesDoNot()
        {
            RecoveryTranscript left = Recorded();
            RecoveryTranscript right = Recorded();

            Assert.That(left.Describe(), Is.EqualTo(right.Describe()));
            Assert.That(left.DigestHex(), Is.EqualTo(right.DigestHex()));
            Assert.That(left.Digest(), Is.EqualTo(right.Digest()));
            Assert.That(left.Digest(), Is.EqualTo(ContentHash.Compute(Encoding.UTF8.GetBytes(left.Describe()))),
                "the digest is the hash of the canonical transcript text and nothing else.");
            Assert.That(left.DigestHex(), Is.EqualTo(left.Digest().ToHex()));
            Assert.That(left.Digest().IsEmpty, Is.False);

            RecoveryTranscript other = Recorded();
            other.Add(RecoveryPhase.Refused, "refused with ResourceUnavailable", RecoveryDataLossClass.None);
            Assert.That(other.DigestHex(), Is.Not.EqualTo(left.DigestHex()),
                "one further line is a different recovery and must not digest the same.");

            var changed = new RecoveryTranscript(8);
            changed.Add(RecoveryPhase.SourceObserved, "faulted world observed differently", RecoveryDataLossClass.None);
            changed.Add(RecoveryPhase.Capture, "committed boundary copied", RecoveryDataLossClass.None);
            changed.AddFault(RecoveryFaultPoints.CheckpointPublication, "publication latch fired", RecoveryDataLossClass.None);
            Assert.That(changed.Count, Is.EqualTo(left.Count));
            Assert.That(changed.DigestHex(), Is.Not.EqualTo(left.DigestHex()),
                "a line with the same position but another detail is another recovery.");
        }

        [Test]
        public void OverflowIsCountedAndNeverGrowsTheLineCount()
        {
            var transcript = new RecoveryTranscript(2);
            transcript.Add(RecoveryPhase.SourceObserved, "faulted world observed", RecoveryDataLossClass.None);
            transcript.Add(RecoveryPhase.Capture, "committed boundary copied", RecoveryDataLossClass.None);

            RecoveryTranscriptLine refused = transcript.Add(
                RecoveryPhase.Refused, "one line too many", RecoveryDataLossClass.UncommittedSinceCheckpoint);
            Assert.That(refused.Ordinal, Is.EqualTo(-1), "a refused line is reported as unrecorded.");
            Assert.That(refused.Phase, Is.EqualTo(RecoveryPhase.Refused));
            Assert.That(transcript.Count, Is.EqualTo(2), "the transcript never grows past its capacity (P-043).");
            Assert.That(transcript.Lines.Count, Is.EqualTo(2));
            Assert.That(transcript.OverflowCount, Is.EqualTo(1), "the refused line is counted, never silently dropped.");
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.None),
                "a line that was not recorded cannot widen the data-loss boundary.");

            transcript.AddFault(RecoveryFaultPoints.Restart, "one fault too many", RecoveryDataLossClass.None);
            Assert.That(transcript.OverflowCount, Is.EqualTo(2));
            Assert.That(transcript.Count, Is.EqualTo(2));
            Assert.That(transcript.FaultCount, Is.EqualTo(0));
        }

        [Test]
        public void DataLossBoundaryIsTheMostSevereRecordedClass()
        {
            var transcript = new RecoveryTranscript(8);
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.None),
                "an empty transcript lost nothing.");

            transcript.Add(RecoveryPhase.Restore, "state restored into the staged world", RecoveryDataLossClass.None);
            transcript.Add(RecoveryPhase.OutboxReinstate, "obligations reinstated", RecoveryDataLossClass.UncommittedAttemptWork);
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.UncommittedAttemptWork));

            transcript.Add(RecoveryPhase.Refused, "the obligation was never durable", RecoveryDataLossClass.UnpersistedObligation);
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.UnpersistedObligation));

            transcript.Add(RecoveryPhase.Restart, "restarted from the store alone", RecoveryDataLossClass.UncommittedSinceCheckpoint);
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.UncommittedSinceCheckpoint));

            transcript.Add(RecoveryPhase.PublishNewWorld, "a new session was published", RecoveryDataLossClass.None);
            Assert.That(transcript.DataLossBoundary(), Is.EqualTo(RecoveryDataLossClass.UncommittedSinceCheckpoint),
                "a later harmless line does not lower the boundary the recovery was already exposed to.");

            Assert.That(
                (int)RecoveryDataLossClass.UncommittedSinceCheckpoint,
                Is.GreaterThan((int)RecoveryDataLossClass.UnpersistedObligation),
                "the enum's own order is the severity order the boundary is derived from.");
            Assert.That(
                (int)RecoveryDataLossClass.UnpersistedObligation,
                Is.GreaterThan((int)RecoveryDataLossClass.UncommittedAttemptWork));
        }

        [Test]
        public void AddFaultRecordsThePhaseAndTheFaultPoint()
        {
            var transcript = new RecoveryTranscript(4);
            RecoveryTranscriptLine line = transcript.AddFault(
                RecoveryFaultPoints.OutboxDelivery,
                "the delivery hook fired after the destination was asked",
                RecoveryDataLossClass.UncommittedAttemptWork);

            Assert.That(line.Phase, Is.EqualTo(RecoveryPhase.FaultPointReached));
            Assert.That(line.FaultPointId, Is.EqualTo(RecoveryFaultPoints.OutboxDelivery));
            Assert.That(line.DataLoss, Is.EqualTo(RecoveryDataLossClass.UncommittedAttemptWork));
            Assert.That(transcript.FaultCount, Is.EqualTo(1));
            Assert.That(line.ToLine(), Does.Contain("FaultPointReached"));
            Assert.That(line.ToLine(), Does.Contain("loss=UncommittedAttemptWork"));
            Assert.That(line.ToLine(), Does.Contain("at=" + RecoveryFaultPoints.OutboxDelivery));

            Assert.That(RecoveryFaultPoints.TryGet(line.FaultPointId, out RecoveryFaultPoint point), Is.True,
                "a faulted line must name an injection point the production table declares.");
            Assert.That(point.Mechanism, Is.EqualTo(RecoveryInjectionMechanism.DeliveryHook));

            transcript.Add(RecoveryPhase.SourceStopped, "the faulted world was stopped", RecoveryDataLossClass.UncommittedAttemptWork);
            Assert.That(transcript.FaultCount, Is.EqualTo(1), "only the fault line is a fault.");
            Assert.That(transcript.Count, Is.EqualTo(2));
        }

        [Test]
        public void ARestartPointReportsANewSessionOnlyWhenItDiffersFromTheOldOne()
        {
            var previous = new WorldId(new Id128(0x11UL, 0x22UL));
            ContentHash hash = ContentHash.Compute(new byte[] { 1, 2, 3 });

            var noDocument = new RecoveryRestartPoint(
                "memory://gc027", false, ContentHash.Empty, previous, default(WorldId),
                DiagnosticCode.ResourceUnavailable, "no document was present");
            Assert.That(noDocument.ProducedNewSession, Is.False, "a restart that produced no world produced no session.");
            Assert.That(noDocument.DocumentPresent, Is.False);
            Assert.That(noDocument.DocumentHash, Is.EqualTo(ContentHash.Empty));
            Assert.That(noDocument.ToLine(), Does.Contain("present=0"));
            Assert.That(noDocument.ToLine(), Does.Contain("now=<none>"));
            Assert.That(noDocument.ToLine(), Does.Contain("code=ResourceUnavailable"));

            var sameSession = new RecoveryRestartPoint(
                "memory://gc027", true, hash, previous, previous, DiagnosticCode.None, "the document named the faulted session");
            Assert.That(sameSession.ProducedNewSession, Is.False,
                "the faulted incarnation is never resumed, not even by a document that names it (P-049).");
            Assert.That(sameSession.ToLine(), Does.Contain("present=1"));

            var newSession = new RecoveryRestartPoint(
                "memory://gc027", true, hash, previous, new WorldId(new Id128(0x33UL, 0x44UL)),
                DiagnosticCode.None, "published a new session");
            Assert.That(newSession.ProducedNewSession, Is.True);
            Assert.That(newSession.ToLine(), Does.Contain("document=" + hash.ToHex()));
            Assert.That(newSession.ToLine(), Does.Contain("was=" + previous.Session.ToString()));
            Assert.That(newSession.ToLine(), Does.Contain("code=None"));

            var firstSession = new RecoveryRestartPoint(
                "memory://gc027", true, hash, default(WorldId), new WorldId(new Id128(0x55UL, 0x66UL)),
                DiagnosticCode.None, "no previous session existed");
            Assert.That(firstSession.ProducedNewSession, Is.True, "a restart that starts the first world did produce one.");
        }

        /// <summary>One recovery recorded twice, so two independent transcripts can be compared.</summary>
        private static RecoveryTranscript Recorded()
        {
            var transcript = new RecoveryTranscript(8);
            transcript.Add(RecoveryPhase.SourceObserved, "faulted world observed", RecoveryDataLossClass.None);
            transcript.Add(RecoveryPhase.Capture, "committed boundary copied", RecoveryDataLossClass.None);
            transcript.AddFault(RecoveryFaultPoints.CheckpointPublication, "publication latch fired", RecoveryDataLossClass.None);
            return transcript;
        }
    }
}
