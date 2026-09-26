// GameCore.Replay — engine observation replay, separated from native-physics comparison (GC-023, TEST-022).
//
// TEST-022's last paragraph is the requirement this file implements: "For Unity-physics-dependent rules, record and
// replay the engine observations to test downstream rule repeatability separately. Cross-platform
// floating-point/physics lockstep is outside the claim."
//
// The split has to be structural, not a matter of wording:
//
//   * **Rule repeatability** is claimed only over *recorded* engine observations. `ObservationReplay` feeds the
//     recorded stamped samples into the rule pipeline and hashes the rule output; two replays of the same recording
//     must be bit-identical, and that claim needs no physics engine at all.
//   * **Native-physics comparison** is a *separate* result. `NativePhysicsComparison` compares two runs'
//     observations against each other and reports equality, a maximum per-component delta and the first diverging
//     step. It never claims bit identity, and it is not an input to any rule hash: a physics divergence is reported
//     as a physics divergence, while the rule half of the same trace still compares equal.
//
// An observation is a stamped, integer-scaled sample: a step, an epoch, a sampling ordinal and a small vector of
// fixed-point components. Integers are deliberate — a floating-point sample would make "the observation replay is
// identical" a statement about a platform's rounding mode, which is exactly what TEST-022 excludes from the claim.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Replay
{
    /// <summary>One recorded engine observation: a stamped, integer-scaled sample of external authority.</summary>
    public readonly struct RecordedObservation
    {
        public RecordedObservation(
            int step,
            AssemblyEpoch epoch,
            int samplingOrdinal,
            int positionX,
            int positionY,
            int velocityX,
            int velocityY)
        {
            Step = step;
            Epoch = epoch;
            SamplingOrdinal = samplingOrdinal;
            PositionX = positionX;
            PositionY = positionY;
            VelocityX = velocityX;
            VelocityY = velocityY;
        }

        /// <summary>Logical step the sample was taken in; the sample is an observation of that step's boundary.</summary>
        public int Step { get; }

        /// <summary>Assembly epoch the sample was stamped with, so a stale observation is detectable (P-034).</summary>
        public AssemblyEpoch Epoch { get; }

        /// <summary>Ordinal within the step's sampling sequence (a step may sample more than once).</summary>
        public int SamplingOrdinal { get; }

        /// <summary>Fixed-point position/velocity components, in thousandths of a unit.</summary>
        public int PositionX { get; }

        public int PositionY { get; }

        public int VelocityX { get; }

        public int VelocityY { get; }

        /// <summary>Canonical text of this sample, the only form a rule hash reads.</summary>
        public string CanonicalText() =>
            "observation=step" + Step.ToString(CultureInfo.InvariantCulture)
            + "/epoch" + Epoch.Value.ToString(CultureInfo.InvariantCulture)
            + "/sample" + SamplingOrdinal.ToString(CultureInfo.InvariantCulture)
            + "/p" + PositionX.ToString(CultureInfo.InvariantCulture) + "," + PositionY.ToString(CultureInfo.InvariantCulture)
            + "/v" + VelocityX.ToString(CultureInfo.InvariantCulture) + "," + VelocityY.ToString(CultureInfo.InvariantCulture);

        public override string ToString() => CanonicalText();
    }

    /// <summary>
    /// The rule half of observation replay: recorded observations in, rule decisions and integer state out. The
    /// rule below is the fixture's own (an integral predictor), and it is deterministic over integers only, so
    /// "replaying the recording reproduces the decisions" is a claim about this fixture's rules and nothing else.
    /// </summary>
    public static class ObservationReplay
    {
        /// <summary>
        /// Replays one recording: per observation, applies the fixture's integral predictor to the *recorded*
        /// sample and accumulates the rule state. Returns the canonical rule hash and the per-step decisions.
        /// </summary>
        public static ObservationReplayResult Replay(IReadOnlyList<RecordedObservation>? observations)
        {
            var decisions = new List<string>();
            int state = 0;
            for (int i = 0; i < (observations?.Count ?? 0); i++)
            {
                RecordedObservation observation = observations![i];
                // An integral predictor: the rule reads the recorded sample and nothing else. It has no wall clock,
                // no floating point and no engine handle, so it is exactly as repeatable as its input record.
                int predictedX = observation.PositionX + (observation.VelocityX / 4);
                int predictedY = observation.PositionY + (observation.VelocityY / 4);
                state += predictedX + predictedY;
                decisions.Add(
                    "rule=step" + observation.Step.ToString(CultureInfo.InvariantCulture)
                    + "/x" + predictedX.ToString(CultureInfo.InvariantCulture)
                    + "/y" + predictedY.ToString(CultureInfo.InvariantCulture));
            }

            ContentHash ruleHash = ReplayStateHash.HashOf(Join(decisions));
            ContentHash observationHash = ReplayStateHash.HashOf(Join(Canonical(observations)));
            return new ObservationReplayResult(observations?.Count ?? 0, state, ruleHash, observationHash, decisions);
        }

        /// <summary>True when two replays of the same recording produced identical rule decisions.</summary>
        public static bool RuleReplaysMatch(ObservationReplayResult first, ObservationReplayResult second) =>
            first != null && second != null
            && first.RuleHash.Equals(second.RuleHash)
            && first.ObservationHash.Equals(second.ObservationHash)
            && first.FinalState == second.FinalState;

        private static List<string> Canonical(IReadOnlyList<RecordedObservation>? observations)
        {
            var text = new List<string>();
            for (int i = 0; i < (observations?.Count ?? 0); i++)
            {
                text.Add(observations![i].CanonicalText());
            }

            return text;
        }

        private static string Join(IReadOnlyList<string> lines)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                builder.Append(lines[i]).Append('\n');
            }

            return builder.ToString();
        }
    }

    /// <summary>The rule half's committed result: hashes and decisions over the recorded observations.</summary>
    public sealed class ObservationReplayResult
    {
        public ObservationReplayResult(
            int observationCount,
            int finalState,
            ContentHash ruleHash,
            ContentHash observationHash,
            IReadOnlyList<string>? decisions)
        {
            ObservationCount = observationCount;
            FinalState = finalState;
            RuleHash = ruleHash;
            ObservationHash = observationHash;
            Decisions = decisions == null ? Array.Empty<string>() : decisions;
        }

        /// <summary>Observations replayed.</summary>
        public int ObservationCount { get; }

        /// <summary>The fixture rule's accumulated integer state over the recording.</summary>
        public int FinalState { get; }

        /// <summary>Canonical hash of the rule decisions: the rule-repeatability claim.</summary>
        public ContentHash RuleHash { get; }

        /// <summary>Canonical hash of the recorded observations themselves.</summary>
        public ContentHash ObservationHash { get; }

        /// <summary>Per-observation decision text, kept for a failing assertion.</summary>
        public IReadOnlyList<string> Decisions { get; }

        public string Describe() =>
            "observation-replay{observations=" + ObservationCount.ToString(CultureInfo.InvariantCulture)
            + ";state=" + FinalState.ToString(CultureInfo.InvariantCulture)
            + ";rule=" + RuleHash.ToHex()
            + ";observationsHash=" + ObservationHash.ToHex() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// The native-physics half: a comparison of two runs' recorded observations that reports divergence instead of
    /// promising identity. It is deliberately *not* part of any rule hash (TEST-022's "separately").
    /// </summary>
    public sealed class NativePhysicsComparison
    {
        private NativePhysicsComparison(
            int compared,
            int firstDivergence,
            int maxComponentDelta,
            bool identical,
            string detail)
        {
            ComparedCount = compared;
            FirstDivergence = firstDivergence;
            MaxComponentDelta = maxComponentDelta;
            Identical = identical;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Observations compared, i.e. the length of the shorter recording.</summary>
        public int ComparedCount { get; }

        /// <summary>Index of the first observation whose components differ, or -1.</summary>
        public int FirstDivergence { get; }

        /// <summary>Largest absolute per-component difference seen across the whole comparison.</summary>
        public int MaxComponentDelta { get; }

        /// <summary>True when the two recordings agree component for component.</summary>
        public bool Identical { get; }

        public string Detail { get; }

        /// <summary>
        /// Compares two recordings. A length difference is reported as a divergence at the shorter length; nothing
        /// here asserts that either recording is the "correct" physics result, and nothing here is allowed to feed a
        /// rule hash.
        /// </summary>
        public static NativePhysicsComparison Compare(
            IReadOnlyList<RecordedObservation>? first,
            IReadOnlyList<RecordedObservation>? second)
        {
            int count = Math.Min(first?.Count ?? 0, second?.Count ?? 0);
            int firstDivergence = -1;
            int maxDelta = 0;
            for (int i = 0; i < count; i++)
            {
                RecordedObservation left = first![i];
                RecordedObservation right = second![i];
                int delta = Math.Max(
                    Math.Max(Math.Abs(left.PositionX - right.PositionX), Math.Abs(left.PositionY - right.PositionY)),
                    Math.Max(Math.Abs(left.VelocityX - right.VelocityX), Math.Abs(left.VelocityY - right.VelocityY)));
                if (delta > maxDelta)
                {
                    maxDelta = delta;
                }

                if (delta != 0 && firstDivergence < 0)
                {
                    firstDivergence = i;
                }
            }

            bool lengthsDiffer = (first?.Count ?? 0) != (second?.Count ?? 0);
            bool identical = firstDivergence < 0 && !lengthsDiffer;
            string detail = identical
                ? "the two recordings agree component for component; this is a comparison result, not a lockstep claim"
                : "the recordings differ: firstDivergence=" + firstDivergence.ToString(CultureInfo.InvariantCulture)
                  + ";maxComponentDelta=" + maxDelta.ToString(CultureInfo.InvariantCulture)
                  + ";lengthsDiffer=" + (lengthsDiffer ? "1" : "0");
            return new NativePhysicsComparison(count, lengthsDiffer ? count : firstDivergence, maxDelta, identical, detail);
        }

        public string Describe() =>
            "native-physics{compared=" + ComparedCount.ToString(CultureInfo.InvariantCulture)
            + ";identical=" + (Identical ? "1" : "0")
            + ";firstDivergence=" + FirstDivergence.ToString(CultureInfo.InvariantCulture)
            + ";maxDelta=" + MaxComponentDelta.ToString(CultureInfo.InvariantCulture)
            + ";" + Detail + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Generates one recording of engine observations for a trace: an integral, seeded stand-in for a physics
    /// solver's stamped samples. Two generators with the same seed produce the same recording; a different seed is a
    /// *different* physics result, which is how a test demonstrates that a physics divergence does not disturb the
    /// rule half.
    /// </summary>
    public static class ObservationRecorder
    {
        /// <summary>Records one observation per step for every step of the trace shape.</summary>
        public static IReadOnlyList<RecordedObservation> Record(ReplayTrace trace, uint seed)
        {
            if (trace == null)
            {
                throw new ArgumentNullException(nameof(trace));
            }

            var observations = new RecordedObservation[trace.Steps.Count];
            Lcg random = new Lcg(seed + 104729U);
            int positionX = 0;
            int positionY = 0;
            for (int i = 0; i < trace.Steps.Count; i++)
            {
                int velocityX = random.NextInclusive(-4, 4);
                int velocityY = random.NextInclusive(-4, 4);
                positionX += velocityX;
                positionY += velocityY;
                observations[i] = new RecordedObservation(
                    trace.Steps[i].Step,
                    AssemblyEpoch.First,
                    samplingOrdinal: 0,
                    positionX,
                    positionY,
                    velocityX,
                    velocityY);
            }

            return Array.AsReadOnly(observations);
        }
    }
}
