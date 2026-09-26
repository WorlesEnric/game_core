// GameCore.Gameplay.Traversal — the declared observation trace of the fixed-step course (GC-020).
//
// Normative sources: P-008's determinism boundary and TEST-022's "For Unity-physics-dependent rules, record and
// replay the engine observations to test downstream rule repeatability separately. Cross-platform
// floating-point/physics lockstep is outside the claim." GC-020's acceptance says the same thing in one clause:
// "observation replay separates rule repeatability from native physics".
//
// WHAT THIS IS. One bounded record per composed step: the step and epoch, every runner's pose and velocity AFTER the
// step's integration, the effective acceleration that step applied, and the crossings that step committed. Nothing
// here is authority — it is a copy taken at the step's declared output boundary, so:
//
//   * the PURE-motion claim (this package's integer arithmetic) is repeatable: two runs of the same admitted input
//     produce byte-identical records, and `Difference` is empty;
//   * the PHYSICS-dependent claim is separate: an external rigidbody world's motion is an engine observation, and
//     comparing two of those records is a declared-tolerance comparison with recorded mismatches, never a bitwise
//     determinism claim.
//
// The trace is bounded (a ring of the most recent steps), so an idle or long-running world cannot grow it without
// bound (P-043's bounded work).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>One body's observed motion in one composed step.</summary>
    public readonly struct TraversalBodyTrace
    {
        /// <summary>The runner this record is about.</summary>
        public readonly TargetId Runner;

        /// <summary>Pose after the step, in millimetres.</summary>
        public readonly TraversalVector3i Pose;

        /// <summary>Velocity after the step, in thousandths of a metre per second.</summary>
        public readonly TraversalVector3i Velocity;

        /// <summary>The effective derived acceleration this step applied, in thousandths of a metre per second squared.</summary>
        public readonly TraversalVector3i AppliedAcceleration;

        /// <summary>1 when the step accepted a jump for this body.</summary>
        public readonly byte Jumped;

        /// <summary>Builds one body record.</summary>
        public TraversalBodyTrace(
            TargetId runner,
            TraversalVector3i pose,
            TraversalVector3i velocity,
            TraversalVector3i appliedAcceleration,
            byte jumped)
        {
            Runner = runner;
            Pose = pose;
            Velocity = velocity;
            AppliedAcceleration = appliedAcceleration;
            Jumped = jumped;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Runner.ToString() + " p" + Pose.ToString() + " v" + Velocity.ToString();
    }

    /// <summary>One committed crossing in one composed step.</summary>
    public readonly struct TraversalCrossingTrace
    {
        /// <summary>The runner that passed the checkpoint.</summary>
        public readonly TargetId Runner;

        /// <summary>The checkpoint volume that was passed.</summary>
        public readonly TargetId Checkpoint;

        /// <summary>The count after this crossing.</summary>
        public readonly uint CountAfter;

        /// <summary>Builds one crossing record.</summary>
        public TraversalCrossingTrace(TargetId runner, TargetId checkpoint, uint countAfter)
        {
            Runner = runner;
            Checkpoint = checkpoint;
            CountAfter = countAfter;
        }

        /// <inheritdoc />
        public override string ToString() =>
            Runner.ToString() + "->" + Checkpoint.ToString() + "=" + CountAfter.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One composed step's observation record.</summary>
    public sealed class TraversalStepTrace
    {
        /// <summary>Builds one step record.</summary>
        public TraversalStepTrace(ulong step, ulong epoch, int externallyOwned)
        {
            Step = step;
            Epoch = epoch;
            ExternallyOwned = externallyOwned;
        }

        /// <summary>Logical step this record was taken at.</summary>
        public ulong Step { get; }

        /// <summary>Assembly epoch this record was taken under.</summary>
        public ulong Epoch { get; }

        /// <summary>Runners this step left to an external authority instead of integrating (P-034).</summary>
        public int ExternallyOwned { get; }

        /// <summary>Bodies this step integrated, in canonical runner order.</summary>
        public List<TraversalBodyTrace> Bodies { get; } = new List<TraversalBodyTrace>();

        /// <summary>Crossings this step committed, in commit order.</summary>
        public List<TraversalCrossingTrace> Crossings { get; } = new List<TraversalCrossingTrace>();

        /// <summary>Integrations this step refused, with the reason the rules gave.</summary>
        public List<string> Refusals { get; } = new List<string>();

        /// <summary>True when this step recorded no motion at all, which is what an idle step must look like.</summary>
        public bool IsEmpty => Bodies.Count == 0 && Crossings.Count == 0 && Refusals.Count == 0;

        /// <summary>One-line diagnostic form; the trace's identity is its step, never its text (P-004).</summary>
        public override string ToString() =>
            "step=" + Step.ToString(CultureInfo.InvariantCulture)
            + ";bodies=" + Bodies.Count.ToString(CultureInfo.InvariantCulture)
            + ";crossings=" + Crossings.Count.ToString(CultureInfo.InvariantCulture)
            + ";refusals=" + Refusals.Count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The result of comparing two traces under a declared tolerance (P-008, TEST-022).</summary>
    public sealed class TraversalTraceComparison
    {
        /// <summary>Builds one comparison result.</summary>
        public TraversalTraceComparison(
            int comparedSteps,
            int comparedBodies,
            int mismatchedBodies,
            int mismatchedSteps,
            string detail)
        {
            ComparedSteps = comparedSteps;
            ComparedBodies = comparedBodies;
            MismatchedBodies = mismatchedBodies;
            MismatchedSteps = mismatchedSteps;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Steps present in both traces.</summary>
        public int ComparedSteps { get; }

        /// <summary>Body records compared.</summary>
        public int ComparedBodies { get; }

        /// <summary>Body records that differed beyond the declared tolerance.</summary>
        public int MismatchedBodies { get; }

        /// <summary>Steps whose record sets themselves differed (missing step, crossing or refusal count).</summary>
        public int MismatchedSteps { get; }

        /// <summary>First mismatch, verbatim, so a failure names the step it happened at.</summary>
        public string Detail { get; }

        /// <summary>True when the two traces agree under the tolerance.</summary>
        public bool Matches => MismatchedBodies == 0 && MismatchedSteps == 0;
    }

    /// <summary>
    /// The bounded per-step observation trace of one course world. It is written by the integrate and checkpoint
    /// stages through `TraversalStepTraceRecorder` and read by a scenario or a replay comparison.
    /// </summary>
    public sealed class TraversalTraceRecorder
    {
        private readonly List<TraversalStepTrace> steps = new List<TraversalStepTrace>();
        private readonly int capacity;

        private TraversalStepTrace? current;

        /// <summary>Builds a recorder bounded to the most recent <paramref name="capacity"/> steps.</summary>
        public TraversalTraceRecorder(int capacity = 256)
        {
            this.capacity = capacity < 1 ? 1 : capacity;
        }

        /// <summary>Steps recorded, oldest first (a bounded ring; an old step is dropped, never unbounded).</summary>
        public IReadOnlyList<TraversalStepTrace> Steps => steps;

        /// <summary>Steps dropped because the ring was full; a bounded trace says so instead of growing silently.</summary>
        public int DroppedStepCount { get; private set; }

        /// <summary>Opens the record of one step; a second call for the same step replaces an unfinished record.</summary>
        public void BeginStep(ulong step, ulong epoch, int externallyOwned)
        {
            current = new TraversalStepTrace(step, epoch, externallyOwned);
        }

        /// <summary>Records one integrated body in the open step.</summary>
        public void RecordBody(in TraversalBodyTrace body)
        {
            current?.Bodies.Add(body);
        }

        /// <summary>Records one refused integration in the open step.</summary>
        public void RecordRefusal(string reason)
        {
            current?.Refusals.Add(reason ?? string.Empty);
        }

        /// <summary>Records one committed crossing in the open step.</summary>
        public void RecordCrossing(in TraversalCrossingTrace crossing)
        {
            current?.Crossings.Add(crossing);
        }

        /// <summary>Closes the open step and appends it to the bounded ring.</summary>
        public void CompleteStep()
        {
            if (current == null)
            {
                return;
            }

            steps.Add(current);
            current = null;
            while (steps.Count > capacity)
            {
                steps.RemoveAt(0);
                DroppedStepCount++;
            }
        }

        /// <summary>The record of one step, or null when the ring no longer holds it.</summary>
        public TraversalStepTrace? Find(ulong step)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i].Step == step)
                {
                    return steps[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Compares this trace against another under a declared per-component tolerance, in millimetres (or
        /// thousandths of a metre per second). Zero tolerance is the pure-motion claim of this package's integer
        /// fixture; a non-zero tolerance is the declared comparison policy for a recorded engine-observation trace
        /// (P-008, TEST-022). A step present in only one trace is a mismatch, so a silently shorter replay fails.
        /// </summary>
        public TraversalTraceComparison CompareTo(TraversalTraceRecorder other, int tolerance)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            if (tolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance), "a tolerance is never negative");
            }

            int comparedSteps = 0;
            int comparedBodies = 0;
            int mismatchedBodies = 0;
            int mismatchedSteps = 0;
            string detail = "<none>";

            if (steps.Count != other.steps.Count)
            {
                mismatchedSteps++;
                detail = "the traces hold different step counts: "
                    + steps.Count.ToString(CultureInfo.InvariantCulture) + " vs "
                    + other.steps.Count.ToString(CultureInfo.InvariantCulture);
            }

            int shared = steps.Count < other.steps.Count ? steps.Count : other.steps.Count;
            for (int i = 0; i < shared; i++)
            {
                TraversalStepTrace left = steps[i];
                TraversalStepTrace right = other.steps[i];
                comparedSteps++;
                if (left.Step != right.Step
                    || left.Crossings.Count != right.Crossings.Count
                    || left.Refusals.Count != right.Refusals.Count
                    || left.Bodies.Count != right.Bodies.Count)
                {
                    mismatchedSteps++;
                    if (detail == "<none>")
                    {
                        detail = "step records differ at index " + i.ToString(CultureInfo.InvariantCulture)
                            + ": left[" + left.ToString() + "] right[" + right.ToString() + "]";
                    }

                    continue;
                }

                for (int b = 0; b < left.Bodies.Count; b++)
                {
                    comparedBodies++;
                    TraversalBodyTrace leftBody = left.Bodies[b];
                    TraversalBodyTrace rightBody = right.Bodies[b];
                    if (!leftBody.Runner.Equals(rightBody.Runner)
                        || !leftBody.Pose.IsWithin(rightBody.Pose, tolerance)
                        || !leftBody.Velocity.IsWithin(rightBody.Velocity, tolerance)
                        || leftBody.Jumped != rightBody.Jumped)
                    {
                        mismatchedBodies++;
                        if (detail == "<none>")
                        {
                            detail = "body mismatch at step "
                                + left.Step.ToString(CultureInfo.InvariantCulture) + ": left["
                                + leftBody.ToString() + "] right[" + rightBody.ToString() + "]";
                        }
                    }
                }
            }

            return new TraversalTraceComparison(
                comparedSteps, comparedBodies, mismatchedBodies, mismatchedSteps, detail);
        }

        /// <summary>
        /// The canonical textual form of this trace: one line per step, then one line per body and crossing, so two
        /// traces can be diffed as text and a digest can be computed over the same lines (P-008).
        /// </summary>
        public IReadOnlyList<string> ToLines()
        {
            var lines = new List<string>(steps.Count * 3);
            for (int i = 0; i < steps.Count; i++)
            {
                TraversalStepTrace step = steps[i];
                lines.Add("step=" + step.Step.ToString(CultureInfo.InvariantCulture)
                    + ";epoch=" + step.Epoch.ToString(CultureInfo.InvariantCulture)
                    + ";external=" + step.ExternallyOwned.ToString(CultureInfo.InvariantCulture));
                for (int b = 0; b < step.Bodies.Count; b++)
                {
                    lines.Add("body=" + step.Bodies[b].ToString());
                }

                for (int c = 0; c < step.Crossings.Count; c++)
                {
                    lines.Add("crossing=" + step.Crossings[c].ToString());
                }

                for (int r = 0; r < step.Refusals.Count; r++)
                {
                    lines.Add("refusal=" + step.Refusals[r]);
                }
            }

            return lines;
        }
    }

    /// <summary>
    /// The process-side registry of one course world's trace recorder, so a gameplay stage resolves the recorder of
    /// its own world without the adapter owning it (P-002). An unattached world records nothing.
    /// </summary>
    public static class TraversalStepTraceRegistry
    {
        private static readonly Dictionary<ulong, TraversalTraceRecorder> Recorders =
            new Dictionary<ulong, TraversalTraceRecorder>();

        /// <summary>Attaches one recorder to one world session (P-004: the session is the identity).</summary>
        public static TraversalTraceRecorder Attach(WorldId world, int capacity = 256)
        {
            var recorder = new TraversalTraceRecorder(capacity);
            Recorders[world.Session.Low] = recorder;
            return recorder;
        }

        /// <summary>The recorder of one module's world, or null when that world records no trace.</summary>
        public static TraversalTraceRecorder? Of(TraversalModule module) =>
            module != null && Recorders.TryGetValue(module.Host.World.Session.Low, out TraversalTraceRecorder recorder)
                ? recorder
                : null;

        /// <summary>Detaches one world's recorder; a retired incarnation keeps no trace (P-047).</summary>
        public static bool Detach(WorldId world) => Recorders.Remove(world.Session.Low);

        /// <summary>Detaches every recorder; a per-session reset calls this (04 s9).</summary>
        public static void ResetAll() => Recorders.Clear();

        /// <summary>Worlds with a live recorder, for diagnostics.</summary>
        public static int Count => Recorders.Count;
    }
}
