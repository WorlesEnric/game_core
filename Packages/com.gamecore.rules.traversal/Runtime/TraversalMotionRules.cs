// GameCore.Rules.Traversal — the pure integer kinematics of one fixed step (GC-020).
//
// Normative sources: 07 s4.2 ("traversal.integrate schedules Burst-compatible jobs that write their owned pose and
// velocity directly. It consumes a pinned input/config snapshot for each 20 ms step") and 07 s4.3's numeric
// assertion ("A one-step numeric assertion uses zero baseline horizontal acceleration and zero horizontal input:
// from x velocity 1.00, Tailwind yields 1.04 after one 20 ms step; after a fenced reparent, Headwind yields 1.02
// after the next step, within the specified float tolerance").
//
// The rules are pure, allocation-free and totalling on the fixture's declared range. Every length is millimetres
// and every velocity/acceleration is thousandths of a metre per second, so one step is exact integer arithmetic:
//
//   applied   = derivedAcceleration + inputAcceleration + gravity        (this package's declared step policy)
//   velocity' = velocity + applied * stepMillis / milliScale             (exact for the fixture's values)
//   pose'     = pose     + velocity  * stepMillis / milliScale           (semi-implicit Euler: the velocity the
//                                                                         step began with carries the pose forward)
//
// Every division truncates toward zero. That is the declared numeric representation of THIS package's pure-motion
// fixture, not a claim about any other game: it exists so a replay of the same admitted input is exact and so a
// mismatch is a real defect rather than a floating-point tolerance question (P-008, TEST-022).
//
// Gravity, the jump impulse and the ground clamp are fixture configuration of this package's motion policy
// (01 s3 level 4: "Reference-template policy ... traversal step duration/checkpoints"), not a kernel rule. They are
// vertical only, so they never touch the horizontal assertion 07 s4.3 makes, and a world that declares a different
// motion policy simply does not call them.
#nullable enable
using System;
using System.Globalization;

namespace GameCore.Rules.Traversal
{
    /// <summary>
    /// One three-component integer vector: millimetres for a pose, thousandths of a metre per second for a velocity
    /// or an acceleration. Blittable and Burst-compatible by construction.
    /// </summary>
    public readonly struct TraversalVector3i : IEquatable<TraversalVector3i>
    {
        /// <summary>X component (right).</summary>
        public readonly int X;

        /// <summary>Y component (up).</summary>
        public readonly int Y;

        /// <summary>Z component (forward).</summary>
        public readonly int Z;

        /// <summary>Builds one vector from its three components.</summary>
        public TraversalVector3i(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The origin: a pose with no displacement and a velocity with no speed.</summary>
        public static TraversalVector3i Zero => new TraversalVector3i(0, 0, 0);

        /// <summary>True when every component is zero.</summary>
        public bool IsZero => X == 0 && Y == 0 && Z == 0;

        /// <summary>Componentwise addition, refusing an overflow instead of wrapping (P-019's bounded arithmetic).</summary>
        public static bool TryAdd(TraversalVector3i left, TraversalVector3i right, out TraversalVector3i sum)
        {
            if (!TryAddComponent(left.X, right.X, out int x)
                || !TryAddComponent(left.Y, right.Y, out int y)
                || !TryAddComponent(left.Z, right.Z, out int z))
            {
                sum = Zero;
                return false;
            }

            sum = new TraversalVector3i(x, y, z);
            return true;
        }

        /// <summary>Componentwise subtraction, refusing an overflow instead of wrapping.</summary>
        public static bool TrySubtract(TraversalVector3i left, TraversalVector3i right, out TraversalVector3i difference)
        {
            if (!TrySubtractComponent(left.X, right.X, out int x)
                || !TrySubtractComponent(left.Y, right.Y, out int y)
                || !TrySubtractComponent(left.Z, right.Z, out int z))
            {
                difference = Zero;
                return false;
            }

            difference = new TraversalVector3i(x, y, z);
            return true;
        }

        /// <summary>One component's per-step contribution: `value * stepMillis / milliScale`, truncating toward zero.</summary>
        public static bool TryPerStep(TraversalVector3i value, int stepMillis, out TraversalVector3i step)
        {
            step = Zero;
            if (!TryPerStepComponent(value.X, stepMillis, out int x)
                || !TryPerStepComponent(value.Y, stepMillis, out int y)
                || !TryPerStepComponent(value.Z, stepMillis, out int z))
            {
                return false;
            }

            step = new TraversalVector3i(x, y, z);
            return true;
        }

        /// <summary>
        /// True when every component of this vector is within <paramref name="tolerance"/> (inclusive) of the
        /// expected one. The integer fixture matches exactly with a tolerance of zero; a recorded engine-observation
        /// trace is compared with the package's declared tolerance (P-008, TEST-022).
        /// </summary>
        public bool IsWithin(TraversalVector3i expected, int tolerance)
        {
            if (tolerance < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(tolerance), "a tolerance is never negative");
            }

            return Within(X, expected.X, tolerance)
                && Within(Y, expected.Y, tolerance)
                && Within(Z, expected.Z, tolerance);
        }

        /// <inheritdoc />
        public bool Equals(TraversalVector3i other) => X == other.X && Y == other.Y && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is TraversalVector3i other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X;
                hash = (hash * 397) ^ Y;
                hash = (hash * 397) ^ Z;
                return hash;
            }
        }

        /// <summary>Componentwise equality; no rank or magnitude is implied by this comparison (P-008).</summary>
        public static bool operator ==(TraversalVector3i left, TraversalVector3i right) => left.Equals(right);

        /// <summary>Componentwise inequality.</summary>
        public static bool operator !=(TraversalVector3i left, TraversalVector3i right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() =>
            "(" + X.ToString(CultureInfo.InvariantCulture)
            + "," + Y.ToString(CultureInfo.InvariantCulture)
            + "," + Z.ToString(CultureInfo.InvariantCulture) + ")";

        private static bool Within(int value, int expected, int tolerance)
        {
            long difference = (long)value - expected;
            if (difference < 0)
            {
                difference = -difference;
            }

            return difference <= tolerance;
        }

        private static bool TryAddComponent(int left, int right, out int sum)
        {
            long value = (long)left + right;
            if (value < int.MinValue || value > int.MaxValue)
            {
                sum = 0;
                return false;
            }

            sum = (int)value;
            return true;
        }

        private static bool TrySubtractComponent(int left, int right, out int difference)
        {
            long value = (long)left - right;
            if (value < int.MinValue || value > int.MaxValue)
            {
                difference = 0;
                return false;
            }

            difference = (int)value;
            return true;
        }

        private static bool TryPerStepComponent(int value, int stepMillis, out int step)
        {
            long result = ((long)value * stepMillis) / TraversalVocabulary.MilliScale;
            if (result < int.MinValue || result > int.MaxValue)
            {
                step = 0;
                return false;
            }

            step = (int)result;
            return true;
        }
    }

    /// <summary>
    /// One body's inputs for exactly one fixed step: its owned state, the effective derived acceleration the
    /// assembly resolved, the captured movement input and the world's fixed step duration (07 s4.2).
    /// </summary>
    public readonly struct TraversalBodyStepRequest
    {
        /// <summary>Pose the step begins from, in millimetres.</summary>
        public readonly TraversalVector3i Pose;

        /// <summary>Velocity the step begins from, in thousandths of a metre per second.</summary>
        public readonly TraversalVector3i Velocity;

        /// <summary>Effective `traversal.acceleration` the published binding row carries, or zero when unbound.</summary>
        public readonly TraversalVector3i DerivedAcceleration;

        /// <summary>The captured input's horizontal request, in thousandths of a metre per second squared.</summary>
        public readonly int HorizontalInputMilli;

        /// <summary>1 when the captured input asked for a jump in this step.</summary>
        public readonly byte JumpPressed;

        /// <summary>1 while the body is on the ground, so a jump is only accepted from the ground.</summary>
        public readonly byte Grounded;

        /// <summary>Duration of the fixed step in milliseconds; the fixture's configured value is 20 (07 s4.1).</summary>
        public readonly int StepMillis;

        /// <summary>Builds one step request.</summary>
        public TraversalBodyStepRequest(
            TraversalVector3i pose,
            TraversalVector3i velocity,
            TraversalVector3i derivedAcceleration,
            int horizontalInputMilli,
            byte jumpPressed,
            byte grounded,
            int stepMillis)
        {
            Pose = pose;
            Velocity = velocity;
            DerivedAcceleration = derivedAcceleration;
            HorizontalInputMilli = horizontalInputMilli;
            JumpPressed = jumpPressed;
            Grounded = grounded;
            StepMillis = stepMillis;
        }
    }

    /// <summary>The state one integrated step produced, plus what the step applied (P-044's auditability).</summary>
    public readonly struct TraversalBodyStepResult
    {
        /// <summary>Pose after the step, in millimetres.</summary>
        public readonly TraversalVector3i Pose;

        /// <summary>Velocity after the step, in thousandths of a metre per second.</summary>
        public readonly TraversalVector3i Velocity;

        /// <summary>1 when the body rests on the ground after the step.</summary>
        public readonly byte Grounded;

        /// <summary>The acceleration this step actually applied: derived + input + gravity.</summary>
        public readonly TraversalVector3i AppliedAcceleration;

        /// <summary>1 when this step accepted a jump request.</summary>
        public readonly byte Jumped;

        /// <summary>Builds one step result.</summary>
        public TraversalBodyStepResult(
            TraversalVector3i pose,
            TraversalVector3i velocity,
            byte grounded,
            TraversalVector3i appliedAcceleration,
            byte jumped)
        {
            Pose = pose;
            Velocity = velocity;
            Grounded = grounded;
            AppliedAcceleration = appliedAcceleration;
            Jumped = jumped;
        }
    }

    /// <summary>
    /// The pure kinematics of one fixed step: the exact integer arithmetic the ECS-owned kinematic mode applies, and
    /// the same arithmetic an observation replay replays (07 s4.2, P-008, P-034, P-041).
    /// </summary>
    public static class TraversalMotionRules
    {
        /// <summary>Gravity of this package's motion policy: -10 m/s², vertical only (see the header).</summary>
        public const int GravityMilli = -10000;

        /// <summary>The vertical velocity a jump sets: +3 m/s, vertical only (see the header).</summary>
        public const int JumpImpulseMilli = 3000;

        /// <summary>
        /// Advances one body by exactly one fixed step under this package's declared motion policy. False reports a
        /// refusal (an overflow of the declared integer range) with a non-empty <paramref name="failure"/> and
        /// leaves the result at zero, so a caller never applies half of an integration.
        /// </summary>
        public static bool TryStep(
            in TraversalBodyStepRequest request,
            out TraversalBodyStepResult result,
            out string failure)
        {
            result = default(TraversalBodyStepResult);
            failure = string.Empty;
            if (request.StepMillis <= 0)
            {
                failure = "a fixed step duration must be positive (P-036)";
                return false;
            }

            // 1. applied = derived + input + gravity. The derived contribution is the assembly's effective value;
            //    the input and gravity are this package's declared step policy (see the header).
            var input = new TraversalVector3i(request.HorizontalInputMilli, 0, 0);
            var gravity = new TraversalVector3i(0, GravityMilli, 0);
            if (!TraversalVector3i.TryAdd(request.DerivedAcceleration, input, out TraversalVector3i combined)
                || !TraversalVector3i.TryAdd(combined, gravity, out TraversalVector3i applied))
            {
                failure = "the applied acceleration overflowed the declared integer range";
                return false;
            }

            // 2. velocity' = velocity + applied * stepMillis / milliScale.
            if (!TraversalVector3i.TryPerStep(applied, request.StepMillis, out TraversalVector3i accelerationStep)
                || !TraversalVector3i.TryAdd(request.Velocity, accelerationStep, out TraversalVector3i velocity))
            {
                failure = "the velocity step overflowed the declared integer range";
                return false;
            }

            // 3. A jump is accepted only from the ground, and it sets the vertical velocity outright.
            byte jumped = 0;
            if (request.JumpPressed != 0 && request.Grounded != 0)
            {
                velocity = new TraversalVector3i(velocity.X, JumpImpulseMilli, velocity.Z);
                jumped = 1;
            }

            // 4. pose' = pose + velocity * stepMillis / milliScale, using the velocity this step began with.
            if (!TraversalVector3i.TryPerStep(request.Velocity, request.StepMillis, out TraversalVector3i poseStep)
                || !TraversalVector3i.TryAdd(request.Pose, poseStep, out TraversalVector3i pose))
            {
                failure = "the pose step overflowed the declared integer range";
                return false;
            }

            // 5. The declared ground plane: a body at or below it rests on it. The clamp is SKIPPED on the step that
            //    accepted a jump, because this step's pose is integrated from the velocity the step began with, so a
            //    grounded body's pose is still exactly on the plane and clamping here would erase the impulse the
            //    jump just set — leaving the body unable to ever leave the ground (07 s4.2's `JumpState`).
            byte grounded = 0;
            if (jumped == 0 && pose.Y <= 0)
            {
                if (pose.Y < 0)
                {
                    pose = new TraversalVector3i(pose.X, 0, pose.Z);
                }

                velocity = new TraversalVector3i(velocity.X, 0, velocity.Z);
                grounded = 1;
            }

            result = new TraversalBodyStepResult(pose, velocity, grounded, applied, jumped);
            return true;
        }

        /// <summary>
        /// The velocity one body reaches after one fixed step under one acceleration with no input and standing on
        /// the ground. This is the reference's own numeric assertion (`1.00 -&gt; 1.04`) expressed as a rule rather
        /// than as a test constant, so a scenario compares two numbers this package computed (07 s4.3).
        /// </summary>
        public static bool TryVelocityAfterStep(
            TraversalVector3i velocity,
            TraversalVector3i acceleration,
            int stepMillis,
            out TraversalVector3i nextVelocity)
        {
            nextVelocity = TraversalVector3i.Zero;
            var request = new TraversalBodyStepRequest(
                TraversalVector3i.Zero, velocity, acceleration, 0, 0, 1, stepMillis);
            if (!TryStep(in request, out TraversalBodyStepResult result, out string _))
            {
                return false;
            }

            nextVelocity = result.Velocity;
            return true;
        }
    }
}
