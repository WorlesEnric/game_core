// GameCore.Rules.Traversal.Tests — the pure traversal rules (GC-020).
//
// These are the engine-independent half of the traversal acceptance work: the fixed-step arithmetic of 07 s4.3's
// numeric assertion, the checkpoint course's deduplication and ordering rules of 07 s4.2/REF-A04, the canonical
// payload codec of 05 s6 and the registered `Additive` reducer of P-019. They run in the plain dotnet suite and in
// Unity's EditMode suite; neither needs a World, a GameObject or a PlayerLoop.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation;
using NUnit.Framework;

namespace GameCore.Rules.Traversal.Tests
{
    /// <summary>The fixed-step kinematics of the real-time reference (07 s4.2, s4.3).</summary>
    [TestFixture]
    public sealed class TraversalMotionRulesTests
    {
        private readonly TraversalVector3i zero = TraversalVector3i.Zero;

        /// <summary>REF-A01's assertion is this package's own rule, not a test constant (07 s4.3).</summary>
        [Test]
        public void TheReferenceVelocitySequenceIsExactIntegerArithmetic()
        {
            var tailwind = new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0);
            var headwind = new TraversalVector3i(TraversalVocabulary.HeadwindMilli, 0, 0);
            var seeded = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);

            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    seeded, tailwind, TraversalVocabulary.StepMilliseconds, out TraversalVector3i afterTailwind),
                Is.True);
            Assert.That(afterTailwind.X, Is.EqualTo(TraversalVocabulary.VelocityAfterTailwindMilli));
            Assert.That(afterTailwind.X, Is.EqualTo(1040), "1.00 m/s plus 2 m/s^2 for 20 ms is 1.04 m/s");

            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    afterTailwind, headwind, TraversalVocabulary.StepMilliseconds, out TraversalVector3i afterHeadwind),
                Is.True);
            Assert.That(afterHeadwind.X, Is.EqualTo(TraversalVocabulary.VelocityAfterHeadwindMilli));
            Assert.That(afterHeadwind.X, Is.EqualTo(1020), "1.04 m/s minus 1 m/s^2 for 20 ms is 1.02 m/s");
        }

        /// <summary>A modifier's retraction is not a velocity reset: the state it produced survives (07 s4.3).</summary>
        [Test]
        public void RetractingAModifierLeavesTheVelocityItProduced()
        {
            var tailwind = new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0);
            var seeded = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    seeded, tailwind, TraversalVocabulary.StepMilliseconds, out TraversalVector3i accelerated),
                Is.True);

            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    accelerated, zero, TraversalVocabulary.StepMilliseconds, out TraversalVector3i unmodified),
                Is.True);
            Assert.That(
                unmodified.X,
                Is.EqualTo(accelerated.X),
                "with no applicable modifier the velocity is unchanged, not reset");
        }

        /// <summary>Two admitted steps of one acceleration are two steps, never one doubled step.</summary>
        [Test]
        public void TwoStepsApplyTheAccelerationTwiceAndTheStepOnce()
        {
            var tailwind = new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0);
            var seeded = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    seeded, tailwind, TraversalVocabulary.StepMilliseconds, out TraversalVector3i once),
                Is.True);
            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(
                    once, tailwind, TraversalVocabulary.StepMilliseconds, out TraversalVector3i twice),
                Is.True);

            Assert.That(once.X - seeded.X, Is.EqualTo(40));
            Assert.That(twice.X - once.X, Is.EqualTo(40));
        }

        /// <summary>A step integrates pose from the velocity the step began with, and reports what it applied.</summary>
        [Test]
        public void OneStepAdvancesPoseByThePreStepVelocity()
        {
            var request = new TraversalBodyStepRequest(
                TraversalVector3i.Zero,
                new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0),
                new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0),
                0,
                0,
                1,
                TraversalVocabulary.StepMilliseconds);

            Assert.That(
                TraversalMotionRules.TryStep(in request, out TraversalBodyStepResult result, out string failure),
                Is.True,
                failure);
            Assert.That(result.Velocity.X, Is.EqualTo(1040));
            Assert.That(
                result.Pose.X,
                Is.EqualTo(20),
                "the pose advanced by the velocity the step began with: 1.00 m/s for 20 ms is 20 mm");
            Assert.That(result.AppliedAcceleration.X, Is.EqualTo(TraversalVocabulary.TailwindMilli));
            Assert.That(result.AppliedAcceleration.Y, Is.EqualTo(TraversalMotionRules.GravityMilli));
            Assert.That(result.Grounded, Is.EqualTo((byte)1));
        }

        /// <summary>A jump is accepted from the ground only, and it is owned by the step's own policy.</summary>
        [Test]
        public void AJumpIsAcceptedOnlyFromTheGround()
        {
            var grounded = new TraversalBodyStepRequest(
                TraversalVector3i.Zero,
                new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0),
                TraversalVector3i.Zero,
                0,
                1,
                1,
                TraversalVocabulary.StepMilliseconds);
            Assert.That(
                TraversalMotionRules.TryStep(in grounded, out TraversalBodyStepResult jumped, out string first),
                Is.True,
                first);
            Assert.That(jumped.Jumped, Is.EqualTo((byte)1));
            Assert.That(jumped.Velocity.Y, Is.EqualTo(TraversalMotionRules.JumpImpulseMilli));

            var airborne = new TraversalBodyStepRequest(
                jumped.Pose,
                jumped.Velocity,
                TraversalVector3i.Zero,
                0,
                1,
                0,
                TraversalVocabulary.StepMilliseconds);
            Assert.That(
                TraversalMotionRules.TryStep(in airborne, out TraversalBodyStepResult again, out string second),
                Is.True,
                second);
            Assert.That(again.Jumped, Is.EqualTo((byte)0), "a jump request in the air is refused");
            Assert.That(again.Velocity.Y, Is.LessThan(jumped.Velocity.Y), "gravity acts while airborne");
        }

        /// <summary>A degenerate step duration is refused with a reason, never applied as a zero-length step.</summary>
        [Test]
        public void ANonPositiveStepDurationIsRefusedWithAReason()
        {
            var request = new TraversalBodyStepRequest(
                TraversalVector3i.Zero, TraversalVector3i.Zero, TraversalVector3i.Zero, 0, 0, 1, 0);
            Assert.That(
                TraversalMotionRules.TryStep(in request, out TraversalBodyStepResult _, out string failure),
                Is.False);
            Assert.That(failure, Is.Not.Empty);
        }

        /// <summary>An overflowing acceleration is refused rather than wrapped into a plausible value.</summary>
        [Test]
        public void AnOverflowingStepIsRefusedInsteadOfWrapping()
        {
            var huge = new TraversalVector3i(int.MaxValue, 0, 0);
            Assert.That(
                TraversalMotionRules.TryVelocityAfterStep(huge, huge, TraversalVocabulary.StepMilliseconds, out _),
                Is.False);
        }

        /// <summary>The declared comparison policy compares componentwise and is never a magnitude heuristic.</summary>
        [Test]
        public void TheDeclaredToleranceComparesComponentwise()
        {
            var a = new TraversalVector3i(1000, 0, 0);
            var b = new TraversalVector3i(1004, 0, 0);
            Assert.That(a.IsWithin(b, 5), Is.True);
            Assert.That(b.IsWithin(a, 5), Is.True);
            Assert.That(a.IsWithin(b, 3), Is.False);
            Assert.That(new TraversalVector3i(1000, 0, 0).IsWithin(b, 5), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(delegate { a.IsWithin(b, -1); });
        }
    }

    /// <summary>The checkpoint course's deduplication and ordering rules (07 s4.2, REF-A04).</summary>
    [TestFixture]
    public sealed class TraversalCourseRulesTests
    {
        private static CheckpointCourse Course()
        {
            return new CheckpointCourse(new List<TargetId>
            {
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo),
            });
        }

        /// <summary>A run passes the volumes in the course definition's order, one increment per crossing.</summary>
        [Test]
        public void OneCrossingAdvancesProgressByExactlyOne()
        {
            CheckpointCourse course = Course();
            TargetId first = TraversalIdentity.Target(TraversalVocabulary.CheckpointOne);
            var observation = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                first,
                TraversalVocabulary.FirstCrossingSequence,
                7UL);

            Assert.That(
                RunProgress.Initial.TryAdvance(course, in observation, out RunProgress next, out CheckpointVerdict verdict),
                Is.True);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.Accepted));
            Assert.That(next.Count, Is.EqualTo(1U));
            Assert.That(next.HasStarted, Is.True);
            Assert.That(next.LastCheckpoint, Is.EqualTo(first));
        }

        /// <summary>Repeating a trigger callback must not award the same crossing twice (07 s4.2, REF-A04).</summary>
        [Test]
        public void ARepeatedCrossingIsDeduplicatedAndDiagnosedAsSuch()
        {
            CheckpointCourse course = Course();
            var observation = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                TraversalVocabulary.FirstCrossingSequence,
                7UL);

            Assert.That(
                RunProgress.Initial.TryAdvance(course, in observation, out RunProgress first, out CheckpointVerdict _),
                Is.True);
            Assert.That(
                first.TryAdvance(course, in observation, out RunProgress repeated, out CheckpointVerdict verdict),
                Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.DuplicateCrossing));
            Assert.That(repeated.Count, Is.EqualTo(first.Count), "a refusal returns the progress unchanged");
        }

        /// <summary>The course definition decides order: the second volume first is out of order (07 s4.2).</summary>
        [Test]
        public void ASkippedCheckpointIsOutOfOrder()
        {
            CheckpointCourse course = Course();
            var second = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo),
                TraversalVocabulary.FirstCrossingSequence,
                7UL);

            Assert.That(
                RunProgress.Initial.TryAdvance(course, in second, out RunProgress next, out CheckpointVerdict verdict),
                Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.OutOfOrder));
            Assert.That(next.Count, Is.EqualTo(0U));
        }

        /// <summary>A volume the course does not contain is an explicit unknown, not a silent skip.</summary>
        [Test]
        public void AVolumeOutsideTheCourseIsUnknown()
        {
            CheckpointCourse course = Course();
            var unknown = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target("traversal.checkpoint-not-in-this-course"),
                TraversalVocabulary.FirstCrossingSequence,
                7UL);

            Assert.That(
                RunProgress.Initial.TryAdvance(course, in unknown, out RunProgress _, out CheckpointVerdict verdict),
                Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.UnknownCheckpoint));
        }

        /// <summary>A malformed observation names no runner, no volume or no crossing ordinal (P-005).</summary>
        [Test]
        public void ADegenerateObservationIsRefusedAsMalformed()
        {
            CheckpointCourse course = Course();
            var degenerate = new CheckpointObservation(
                default(TargetId), TraversalIdentity.Target(TraversalVocabulary.CheckpointOne), 0U, 7UL);

            Assert.That(degenerate.IsAllocated, Is.False);
            Assert.That(
                RunProgress.Initial.TryAdvance(course, in degenerate, out RunProgress _, out CheckpointVerdict verdict),
                Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.Malformed));
        }

        /// <summary>A completed course accepts no further crossing, including a repeat of the last one.</summary>
        [Test]
        public void ACompletedCourseAcceptsNothingFurther()
        {
            CheckpointCourse course = Course();
            var first = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                1U,
                7UL);
            var second = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo),
                2U,
                8UL);

            Assert.That(RunProgress.Initial.TryAdvance(course, in first, out RunProgress one, out CheckpointVerdict _), Is.True);
            Assert.That(one.TryAdvance(course, in second, out RunProgress complete, out CheckpointVerdict _), Is.True);
            Assert.That(complete.IsComplete(course.Count), Is.True);

            var third = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                3U,
                9UL);
            Assert.That(
                complete.TryAdvance(course, in third, out RunProgress _, out CheckpointVerdict verdict),
                Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.AlreadyComplete));
        }

        /// <summary>A new crossing ordinal of an already-passed volume is out of order, not a duplicate.</summary>
        [Test]
        public void ANewCrossingOrdinalOfAPassedVolumeIsOutOfOrderNotADuplicate()
        {
            CheckpointCourse course = Course();
            var first = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                1U,
                7UL);
            Assert.That(RunProgress.Initial.TryAdvance(course, in first, out RunProgress one, out CheckpointVerdict _), Is.True);

            var again = new CheckpointObservation(
                TraversalIdentity.Target(TraversalVocabulary.RunnerA),
                TraversalIdentity.Target(TraversalVocabulary.CheckpointOne),
                2U,
                8UL);
            Assert.That(one.TryAdvance(course, in again, out RunProgress _, out CheckpointVerdict verdict), Is.False);
            Assert.That(verdict, Is.EqualTo(CheckpointVerdict.OutOfOrder));
        }

        /// <summary>The course reports the ordinal of a volume and refuses one it does not contain (07 s4.2).</summary>
        [Test]
        public void TheCourseResolvesOrdinalsBothWays()
        {
            CheckpointCourse course = Course();
            Assert.That(course.Count, Is.EqualTo(2));
            Assert.That(course.TryCheckpoint(1U, out TargetId second), Is.True);
            Assert.That(second, Is.EqualTo(TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo)));
            Assert.That(course.TryCheckpoint(2U, out TargetId _), Is.False);
            Assert.That(course.TryOrdinal(second, out uint ordinal), Is.True);
            Assert.That(ordinal, Is.EqualTo(1U));
        }
    }

    /// <summary>The canonical payload and the registered `Additive` reducer (05 s6, P-019).</summary>
    [TestFixture]
    public sealed class TraversalAccelerationTests
    {
        /// <summary>An acceleration slot value is one canonical big-endian int32 (05 s6).</summary>
        [Test]
        public void TheAccelerationPayloadIsOneCanonicalInt32()
        {
            FrozenPayload payload = TraversalPayloadCodec.WriteAcceleration(TraversalVocabulary.TailwindMilli);
            Assert.That(payload.Length, Is.EqualTo(TraversalPayloadCodec.AccelerationBytes));
            Assert.That(payload.Bytes[0], Is.EqualTo((byte)0));
            Assert.That(payload.Bytes[1], Is.EqualTo((byte)0));
            Assert.That(payload.Bytes[2], Is.EqualTo((byte)7));
            Assert.That(payload.Bytes[3], Is.EqualTo((byte)208));

            Assert.That(TraversalPayloadCodec.TryReadAcceleration(payload.Bytes, out int read), Is.True);
            Assert.That(read, Is.EqualTo(TraversalVocabulary.TailwindMilli));
        }

        /// <summary>A payload of the wrong length is refused rather than reinterpreted (P-054).</summary>
        [Test]
        public void APayloadOfTheWrongLengthIsRefused()
        {
            Assert.That(TraversalPayloadCodec.TryReadAcceleration(new byte[12], out int _), Is.False);
            Assert.That(TraversalPayloadCodec.TryReadAcceleration(null, out int _), Is.False);
        }

        /// <summary>The registered fold adds in the order it was given, never re-sorted (P-018, P-019).</summary>
        [Test]
        public void TheRegisteredFoldAddsInContributionOrder()
        {
            var reducer = new TraversalAccelerationReducer(TraversalVocabulary.AccelerationReducerKey);
            var contributions = new List<int>
            {
                TraversalVocabulary.TailwindMilli,
                TraversalVocabulary.HeadwindMilli,
            };

            Assert.That(reducer.TryReduce(contributions, out int effective, out string failure), Is.True, failure);
            Assert.That(effective, Is.EqualTo(TraversalVocabulary.TailwindMilli + TraversalVocabulary.HeadwindMilli));
            Assert.That(reducer.Key, Is.EqualTo(TraversalVocabulary.AccelerationReducerKey));
        }

        /// <summary>An overflow is a bounded failure with a reason, not a wrapped value (P-019).</summary>
        [Test]
        public void AnOverflowingFoldIsRefusedWithAReason()
        {
            var reducer = new TraversalAccelerationReducer(TraversalVocabulary.AccelerationReducerKey);
            var contributions = new List<int> { int.MaxValue, 1 };
            Assert.That(reducer.TryReduce(contributions, out int _, out string failure), Is.False);
            Assert.That(failure, Is.Not.Empty);
        }

        /// <summary>An unregistered reducer key stays a miss; there is no implicit identity fold (P-028).</summary>
        [Test]
        public void AnUnregisteredReducerKeyIsAMissAndAnotherKeyIsRejected()
        {
            TraversalDerivationValueSource source = TraversalDerivationValueSource.Default();
            Assert.That(source.IsReductionRegistered(TraversalVocabulary.AccelerationReducerKey), Is.True);
            Assert.That(source.IsPredicateRegistered(TraversalVocabulary.AlwaysPredicateKey), Is.True);

            var unknown = new FactoryKey(TraversalIdentity.Id("traversal.reducer.not-registered"), 1U);
            Assert.That(source.IsReductionRegistered(unknown), Is.False);

            var inputs = new List<FrozenPayload>
            {
                TraversalPayloadCodec.WriteAcceleration(TraversalVocabulary.TailwindMilli),
                TraversalPayloadCodec.WriteAcceleration(TraversalVocabulary.TailwindMilli),
            };
            Assert.That(source.TryReduce(unknown, inputs, out FrozenPayload? _), Is.False);

            Assert.That(
                source.TryReduce(TraversalVocabulary.AccelerationReducerKey, inputs, out FrozenPayload? sum),
                Is.True);
            Assert.That(sum, Is.Not.Null);
            Assert.That(
                TraversalPayloadCodec.TryReadAcceleration(sum!.Bytes, out int effective),
                Is.True);
            Assert.That(effective, Is.EqualTo(TraversalVocabulary.TailwindMilli * 2));
            Assert.That(source.ReductionCount, Is.EqualTo(1));
        }

        /// <summary>A contribution that is not one canonical scalar rejects the whole fold (P-019).</summary>
        [Test]
        public void AMalformedContributionRejectsTheWholeFold()
        {
            TraversalDerivationValueSource source = TraversalDerivationValueSource.Default();
            var inputs = new List<FrozenPayload> { new FrozenPayload(new byte[12]) };
            Assert.Throws<ReducerFailureException>(
                delegate { source.TryReduce(TraversalVocabulary.AccelerationReducerKey, inputs, out FrozenPayload? _); });
        }

        /// <summary>The registered predicate is always accepting, so no runner is skipped by accident (P-015).</summary>
        [Test]
        public void TheRegisteredPredicateIsAlwaysAccepting()
        {
            var predicate = new TraversalAlwaysPredicate(TraversalVocabulary.AlwaysPredicateKey);
            Assert.That(predicate.IsMatch(null), Is.True);
            Assert.That(predicate.IsMatch(new List<string>()), Is.True);
            Assert.That(predicate.Key, Is.EqualTo(TraversalVocabulary.AlwaysPredicateKey));
        }

        /// <summary>The declared numeric representation is the one the reference's numbers use (07 s4.3).</summary>
        [Test]
        public void TheDeclaredReferenceValuesAreTheVocabularyConstants()
        {
            Assert.That(TraversalVocabulary.StepMilliseconds, Is.EqualTo(20));
            Assert.That(TraversalVocabulary.StepDurationTicks, Is.EqualTo(200000UL));
            Assert.That(TraversalVocabulary.SeededVelocityMilli, Is.EqualTo(1000));
            Assert.That(TraversalVocabulary.VelocityAfterTailwindMilli, Is.EqualTo(1040));
            Assert.That(TraversalVocabulary.VelocityAfterHeadwindMilli, Is.EqualTo(1020));
        }

        /// <summary>One declared slot carries one int32, which is why the fixture's modifiers vary only X (07 s4.1).</summary>
        [Test]
        public void TheSlotPayloadIsTheStatedNumericRepresentation()
        {
            Assert.That(TraversalPayloadCodec.AccelerationBytes, Is.EqualTo(4));
            Assert.That(TraversalVocabulary.AccelerationSlot,
                Is.EqualTo(TraversalIdentity.Slot(TraversalVocabulary.Acceleration + ".slot-0")));
            Assert.That(TraversalVocabulary.AccelerationStratum, Is.EqualTo(0));
        }
    }
}
