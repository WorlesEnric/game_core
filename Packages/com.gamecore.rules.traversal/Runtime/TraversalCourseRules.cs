// GameCore.Rules.Traversal — the pure checkpoint course rules (GC-020).
//
// Normative sources: 07 s4.2 ("`CheckpointRuntime` rejects duplicate `(runner, checkpoint, crossingSequence)`
// observations and requires the next checkpoint in the course definition. It then writes progress and emits
// `CheckpointPassed`. Multiple trigger callbacks do not produce repeated awards. The kernel neither understands
// checkpoint order nor inserts a combat arbitration stage.") and 07 s6's `REF-A04`: "Repeat a crossing observation
// and deliver an old activation callback | One progress increment; duplicate/stale diagnostics; no write into
// released output."
//
// The rules are pure: they take a course definition, the current progress and one observation, and answer what the
// next progress is and why. Nothing here touches ECS, and nothing here is a kernel rule — checkpoint order is this
// package's own gameplay policy (01 s3, level 4).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Traversal
{
    /// <summary>Why one checkpoint observation did or did not advance a run (07 s4.2).</summary>
    public enum CheckpointVerdict
    {
        /// <summary>The observation names the next checkpoint of the course, so progress advances once.</summary>
        Accepted = 0,

        /// <summary>The very same crossing (runner, checkpoint, sequence) was already recorded.</summary>
        DuplicateCrossing = 1,

        /// <summary>The observation names a checkpoint that is not the next one in the course definition.</summary>
        OutOfOrder = 2,

        /// <summary>The observation names a checkpoint the course definition does not contain.</summary>
        UnknownCheckpoint = 3,

        /// <summary>The observation is malformed: no runner, no checkpoint, or no crossing sequence (P-005).</summary>
        Malformed = 4,

        /// <summary>The run is already complete, so no further crossing is accepted.</summary>
        AlreadyComplete = 5,
    }

    /// <summary>One sealed spatial observation of one runner and one checkpoint volume (07 s4.2).</summary>
    public readonly struct CheckpointObservation
    {
        /// <summary>The runner the observation is about.</summary>
        public readonly TargetId Runner;

        /// <summary>The checkpoint volume that was entered.</summary>
        public readonly TargetId Checkpoint;

        /// <summary>The observation's own crossing ordinal, assigned by the observing step (P-042).</summary>
        public readonly uint CrossingSequence;

        /// <summary>The logical step the observation was sampled at; a stamp, never authority (TEST-019).</summary>
        public readonly ulong SampledStep;

        /// <summary>Builds one observation.</summary>
        public CheckpointObservation(TargetId runner, TargetId checkpoint, uint crossingSequence, ulong sampledStep)
        {
            Runner = runner;
            Checkpoint = checkpoint;
            CrossingSequence = crossingSequence;
            SampledStep = sampledStep;
        }

        /// <summary>True when the observation names a runner, a checkpoint and a nonzero crossing ordinal.</summary>
        public bool IsAllocated => !Runner.IsDefault && !Checkpoint.IsDefault && CrossingSequence != 0U;

        /// <inheritdoc />
        public override string ToString() =>
            Runner.ToString() + "->" + Checkpoint.ToString()
            + "#" + CrossingSequence.ToString(CultureInfo.InvariantCulture)
            + "@" + SampledStep.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The course definition: the ordered checkpoint volumes of one course (07 s4.2).</summary>
    public sealed class CheckpointCourse
    {
        private readonly List<TargetId> checkpoints;

        /// <summary>Builds a course from its ordered checkpoint volumes.</summary>
        public CheckpointCourse(IReadOnlyList<TargetId>? orderedCheckpoints)
        {
            checkpoints = new List<TargetId>(orderedCheckpoints?.Count ?? 0);
            if (orderedCheckpoints != null)
            {
                for (int i = 0; i < orderedCheckpoints.Count; i++)
                {
                    checkpoints.Add(orderedCheckpoints[i]);
                }
            }
        }

        /// <summary>The declared length of the course.</summary>
        public int Count => checkpoints.Count;

        /// <summary>One checkpoint volume by ordinal; false when the ordinal is outside the course.</summary>
        public bool TryCheckpoint(uint ordinal, out TargetId checkpoint)
        {
            if (ordinal >= (uint)checkpoints.Count)
            {
                checkpoint = default(TargetId);
                return false;
            }

            checkpoint = checkpoints[(int)ordinal];
            return true;
        }

        /// <summary>The ordinal of one checkpoint volume; false when the course does not contain it.</summary>
        public bool TryOrdinal(TargetId checkpoint, out uint ordinal)
        {
            for (int i = 0; i < checkpoints.Count; i++)
            {
                if (checkpoints[i].Equals(checkpoint))
                {
                    ordinal = (uint)i;
                    return true;
                }
            }

            ordinal = 0U;
            return false;
        }
    }

    /// <summary>
    /// One run's deduplicated ordered progress: the last checkpoint passed, how many were passed, and the last
    /// crossing identity that advanced it (07 s4.2's `RunProgress { LastCheckpoint, Count }`).
    /// </summary>
    public readonly struct RunProgress
    {
        /// <summary>Number of checkpoints passed, which is also the ordinal of the next expected checkpoint.</summary>
        public readonly uint Count;

        /// <summary>Whether any crossing has been recorded at all.</summary>
        public readonly byte Started;

        /// <summary>Crossing ordinal of the observation that advanced this progress most recently.</summary>
        public readonly uint LastCrossingSequence;

        /// <summary>The checkpoint the last accepted crossing passed, or a default when none was accepted.</summary>
        public readonly TargetId LastCheckpoint;

        /// <summary>Builds one progress record.</summary>
        public RunProgress(uint count, byte started, uint lastCrossingSequence, TargetId lastCheckpoint)
        {
            Count = count;
            Started = started;
            LastCrossingSequence = lastCrossingSequence;
            LastCheckpoint = lastCheckpoint;
        }

        /// <summary>The progress a fresh run starts from: nothing passed.</summary>
        public static RunProgress Initial => new RunProgress(0U, 0, 0U, default(TargetId));

        /// <summary>True when the run has passed at least one checkpoint.</summary>
        public bool HasStarted => Started != 0;

        /// <summary>True when the whole course has been passed.</summary>
        public bool IsComplete(int courseLength) => courseLength > 0 && Count >= (uint)courseLength;

        /// <summary>
        /// Whether one observation advanced this progress, and the progress it produced. Deduplication is by the
        /// declared crossing identity `(runner, checkpoint, crossing-sequence)`: a repeated trigger callback carries
        /// the same ordinal and is refused, while a genuinely new crossing of the same volume carries a new one
        /// and is refused as out of order (07 s4.2, REF-A04).
        /// </summary>
        public bool TryAdvance(CheckpointCourse course, in CheckpointObservation observation, out RunProgress next)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            next = this;
            if (!TryAdvance(course, in observation, out next, out CheckpointVerdict _))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// The full decision: whether the observation advances progress, the progress it produces, and the verdict
        /// that explains the outcome. A refusal always returns this progress unchanged.
        /// </summary>
        public bool TryAdvance(
            CheckpointCourse course,
            in CheckpointObservation observation,
            out RunProgress next,
            out CheckpointVerdict verdict)
        {
            if (course == null)
            {
                throw new ArgumentNullException(nameof(course));
            }

            next = this;
            if (!observation.IsAllocated)
            {
                verdict = CheckpointVerdict.Malformed;
                return false;
            }

            if (IsComplete(course.Count))
            {
                verdict = CheckpointVerdict.AlreadyComplete;
                return false;
            }

            if (Started != 0
                && observation.CrossingSequence == LastCrossingSequence
                && observation.Checkpoint.Equals(LastCheckpoint))
            {
                // The same crossing identity arrived twice: one progress increment only.
                verdict = CheckpointVerdict.DuplicateCrossing;
                return false;
            }

            if (!course.TryOrdinal(observation.Checkpoint, out uint ordinal))
            {
                verdict = CheckpointVerdict.UnknownCheckpoint;
                return false;
            }

            if (ordinal != Count)
            {
                verdict = CheckpointVerdict.OutOfOrder;
                return false;
            }

            next = new RunProgress(
                Count + 1U,
                1,
                observation.CrossingSequence,
                observation.Checkpoint);
            verdict = CheckpointVerdict.Accepted;
            return true;
        }
    }
}
