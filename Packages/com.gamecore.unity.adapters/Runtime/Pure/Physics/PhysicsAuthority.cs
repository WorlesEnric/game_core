// GameCore.Unity.Adapters.Physics — the physics-authority contract: one local physics scene, stepped exactly once per
// admitted action step (GC-020).
//
// Normative sources: 00 P-034 ("An engine-owned physical domain is declared as external authority; its ECS data is
// stamped observation, and ECS submits intent through that adapter"), P-041 (tracked work, explicit dependencies),
// P-045 (observation is immutable and stamped) and 04 s7's Physics row: "For the separate optional rigidbody mode,
// select Unity built-in 3D physics as the physical solver. The physics adapter owns a dedicated local `PhysicsScene`.
// The host calls that scene's `PhysicsScene.Simulate(fixedDelta)` exactly once per admitted action step at the
// plugin's declared stage. It does not also auto-simulate those bodies in the default scene. It applies validated
// intents before simulation, then copies pose/velocity/contact results into externally owned ECS observation
// components before downstream gameplay runs."
//
// This file is Unity-free on purpose: it holds the engine port, the keyed body table and the exact once-per-step
// protocol, so the protocol can be tested in the plain dotnet suite with a recording backend and only the real
// `PhysicsScene` binding has to live in a Unity assembly (04 s10's "pure rules ... EditMode tests without World").
//
// TWO AUTHORITIES, NEVER THREE. The traversal package owns the ECS-owned kinematic pose in `ECS`. In rigidbody mode
// the *engine* owns pose/velocity for the bodies it was given, ECS stores the last synchronized observation, and
// gameplay may not integrate or overwrite the same quantity: `IExternalAuthorityAdapter.SubmitIntent` is the only
// write path, and `ExternalAuthorityLedger.TryWriteFromGameplay` refuses the other one (07 s4.2, REF-A05).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Authority;

namespace GameCore.Unity.Adapters.Physics
{
    /// <summary>
    /// One three-component integer vector in the adapter's declared physical representation: millimetres for a
    /// position, thousandths of a metre per second for a velocity. Integer on purpose, so a physics observation can be
    /// compared exactly and a mismatch is a real defect rather than a tolerance question (P-008).
    /// </summary>
    public readonly struct PhysicsVector3i : IEquatable<PhysicsVector3i>
    {
        /// <summary>X component.</summary>
        public readonly int X;

        /// <summary>Y component.</summary>
        public readonly int Y;

        /// <summary>Z component.</summary>
        public readonly int Z;

        /// <summary>Builds one vector.</summary>
        public PhysicsVector3i(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>The zero vector.</summary>
        public static PhysicsVector3i Zero => new PhysicsVector3i(0, 0, 0);

        /// <inheritdoc />
        public bool Equals(PhysicsVector3i other) => X == other.X && Y == other.Y && Z == other.Z;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PhysicsVector3i other && Equals(other);

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

        /// <summary>Componentwise equality.</summary>
        public static bool operator ==(PhysicsVector3i left, PhysicsVector3i right) => left.Equals(right);

        /// <summary>Componentwise inequality.</summary>
        public static bool operator !=(PhysicsVector3i left, PhysicsVector3i right) => !left.Equals(right);

        /// <inheritdoc />
        public override string ToString() =>
            "(" + X.ToString(CultureInfo.InvariantCulture)
            + "," + Y.ToString(CultureInfo.InvariantCulture)
            + "," + Z.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>One body's physical pose and velocity, as the engine holds them (04 s7's "pose/velocity results").</summary>
    public readonly struct PhysicsPose
    {
        /// <summary>Position in millimetres.</summary>
        public readonly PhysicsVector3i Position;

        /// <summary>Linear velocity in thousandths of a metre per second.</summary>
        public readonly PhysicsVector3i Velocity;

        /// <summary>Builds one pose.</summary>
        public PhysicsPose(PhysicsVector3i position, PhysicsVector3i velocity)
        {
            Position = position;
            Velocity = velocity;
        }

        /// <summary>The origin at rest.</summary>
        public static PhysicsPose Zero => new PhysicsPose(PhysicsVector3i.Zero, PhysicsVector3i.Zero);

        /// <summary>True when position and velocity are both zero.</summary>
        public bool IsZero => Position == PhysicsVector3i.Zero && Velocity == PhysicsVector3i.Zero;

        /// <inheritdoc />
        public override string ToString() => Position.ToString() + "v" + Velocity.ToString();
    }

    /// <summary>
    /// The identity of one simulated body: one target of one declared physical domain. The stable target id is the
    /// body's domain identity; the engine's own `Rigidbody` instance is never an identity (P-004, P-054).
    /// </summary>
    public readonly struct PhysicsBodyKey : IEquatable<PhysicsBodyKey>
    {
        /// <summary>The stable target the body belongs to.</summary>
        public readonly TargetId Target;

        /// <summary>The declared physical domain of that target.</summary>
        public readonly Id128 Domain;

        /// <summary>Builds one body key.</summary>
        public PhysicsBodyKey(TargetId target, Id128 domain)
        {
            Target = target;
            Domain = domain;
        }

        /// <summary>True when the key names a target and a domain.</summary>
        public bool IsAllocated => !Target.IsDefault && !Domain.IsDefault;

        /// <inheritdoc />
        public bool Equals(PhysicsBodyKey other) => Target.Equals(other.Target) && Domain.Equals(other.Domain);

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is PhysicsBodyKey other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            unchecked
            {
                return (Target.Value.GetHashCode() * 397) ^ Domain.GetHashCode();
            }
        }

        /// <inheritdoc />
        public override string ToString() => Target.ToString() + "@" + Domain.ToString();
    }

    /// <summary>
    /// The engine port of one dedicated physics scene. GC-020 supplies the real `PhysicsScene` binding and a
    /// recording backend for tests; both obey the same exact protocol, so "exactly one simulation per admitted step"
    /// is a property of the port's caller and is provable with either (04 s7, REF-A06).
    /// </summary>
    public interface IPhysicsSceneBackend
    {
        /// <summary>Stable identity of this backend's scene, for diagnostics (never a Unity instance id).</summary>
        string SceneName { get; }

        /// <summary>False when no scene could be obtained; every call then refuses as a value, never throws.</summary>
        bool IsAvailable { get; }

        /// <summary>Bodies this scene currently simulates.</summary>
        int BodyCount { get; }

        /// <summary>
        /// Explicit simulations this backend performed. It is the count that makes "exactly one per admitted step"
        /// observable rather than asserted: a caller must never let it exceed the world's committed step count.
        /// </summary>
        int SimulateCount { get; }

        /// <summary>True when this backend suppresses Unity's own automatic FixedUpdate simulation.</summary>
        bool AutomaticSimulationSuppressed { get; }

        /// <summary>Adds one body to the local scene at its declared initial pose; a duplicate key is refused.</summary>
        bool TryAddBody(PhysicsBodyKey key, PhysicsPose initial, out DiagnosticCode code, out string detail);

        /// <summary>Removes one body; false reports that the scene never held it.</summary>
        bool TryRemoveBody(PhysicsBodyKey key, out string detail);

        /// <summary>Reads the engine's current pose for one body (the authoritative physical quantity).</summary>
        bool TryReadPose(PhysicsBodyKey key, out PhysicsPose pose);

        /// <summary>
        /// Applies one already-validated intent. A teleport sets the pose outright; an impulse adds to the velocity.
        /// Applying is the *engine's* act, never a gameplay write (04 s7).
        /// </summary>
        bool TryApplyIntent(PhysicsBodyKey key, AuthorityIntentKind kind, FrozenPayload payload, out string detail);

        /// <summary>
        /// Advances this scene by exactly one fixed step. It is called exactly once per admitted action step and
        /// never once per host frame, so a 30/60/144 Hz presentation cannot double-advance physics (04 s7, REF-A06).
        /// </summary>
        bool TrySimulate(double fixedDeltaSeconds, out string detail);
    }

    /// <summary>How one body-table operation resolved; recorded so a refusal is a value, not an exception.</summary>
    public enum PhysicsBodyOutcome
    {
        /// <summary>Applied.</summary>
        Applied = 0,

        /// <summary>The backend cannot work (no scene, disposed).</summary>
        RefusedUnavailable = 1,

        /// <summary>The key is degenerate (P-005).</summary>
        RefusedMalformed = 2,

        /// <summary>The scene already holds that key, or does not hold it.</summary>
        RefusedUnknownBody = 3,

        /// <summary>The intent's payload is not the declared shape.</summary>
        RefusedMalformedIntent = 4,
    }

    /// <summary>
    /// The keyed body table plus the once-per-admitted-step gate. It is the whole protocol of the physics stage and it
    /// is engine-free: a caller declares the bodies it owns, hands intents to the authoritative backend, and calls
    /// <see cref="TrySimulateExactlyOnce"/> once per admitted step at the plugin's declared stage.
    ///
    /// The gate is the point of this type. <see cref="TrySimulateExactlyOnce"/> refuses a second call for the SAME
    /// admitted step, whatever the host frame rate is, so 30, 60 and 144 presentations of one 50 Hz simulation all
    /// produce one engine step per admitted step and never more (04 s7, REF-A06). It also refuses a step ordinal that
    /// is not newer than the last admitted one, because going backwards would mean the caller is not following the
    /// world's own step sequence.
    /// </summary>
    public sealed class PhysicsAuthorityGate
    {
        private readonly IPhysicsSceneBackend backend;
        private readonly List<PhysicsBodyKey> bodies = new List<PhysicsBodyKey>();

        private ulong lastSimulatedStep;
        private byte hasSimulated;

        /// <summary>Builds the gate over one authoritative backend.</summary>
        public PhysicsAuthorityGate(IPhysicsSceneBackend backend)
        {
            this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        /// <summary>The authoritative backend this gate drives.</summary>
        public IPhysicsSceneBackend Backend => backend;

        /// <summary>Bodies the gate declared, in declaration order (canonical, never dictionary order; P-008).</summary>
        public IReadOnlyList<PhysicsBodyKey> Bodies => bodies;

        /// <summary>Admitted steps this gate stepped exactly once, which is what the REF-A06 comparison checks.</summary>
        public int SteppedStepCount { get; private set; }

        /// <summary>Calls refused because the same admitted step was already simulated.</summary>
        public int DuplicateStepRefusalCount { get; private set; }

        /// <summary>Calls refused because the requested step was not newer than the last simulated one.</summary>
        public int RegressingStepRefusalCount { get; private set; }

        /// <summary>Calls refused because the backend was unavailable or the step was degenerate.</summary>
        public int RefusedSimulationCount { get; private set; }

        /// <summary>The logical step of the last simulation, or zero when none happened.</summary>
        public ulong LastSimulatedStep => lastSimulatedStep;

        /// <summary>Declares one body of this scene at its initial pose.</summary>
        public PhysicsBodyOutcome TryDeclareBody(PhysicsBodyKey key, PhysicsPose initial, out DiagnosticCode code, out string detail)
        {
            if (!key.IsAllocated)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "a simulated body must name a target and a domain (P-005)";
                return PhysicsBodyOutcome.RefusedMalformed;
            }

            for (int i = 0; i < bodies.Count; i++)
            {
                if (bodies[i].Equals(key))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "the physics scene already holds " + key.ToString() + " (P-034)";
                    return PhysicsBodyOutcome.RefusedUnknownBody;
                }
            }

            if (!backend.IsAvailable)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the physics scene backend is unavailable";
                return PhysicsBodyOutcome.RefusedUnavailable;
            }

            if (!backend.TryAddBody(key, initial, out code, out detail))
            {
                return PhysicsBodyOutcome.RefusedUnavailable;
            }

            bodies.Add(key);
            bodies.Sort(CompareBodies);
            code = DiagnosticCode.None;
            detail = string.Empty;
            return PhysicsBodyOutcome.Applied;
        }

        /// <summary>Withdraws one body; the scene must not keep simulating a body nobody owns.</summary>
        public PhysicsBodyOutcome TryWithdrawBody(PhysicsBodyKey key, out string detail)
        {
            for (int i = 0; i < bodies.Count; i++)
            {
                if (!bodies[i].Equals(key))
                {
                    continue;
                }

                if (!backend.TryRemoveBody(key, out detail))
                {
                    return PhysicsBodyOutcome.RefusedUnknownBody;
                }

                bodies.RemoveAt(i);
                detail = string.Empty;
                return PhysicsBodyOutcome.Applied;
            }

            detail = "the physics scene does not hold " + key.ToString();
            return PhysicsBodyOutcome.RefusedUnknownBody;
        }

        /// <summary>Applies one already-validated intent to the authoritative backend.</summary>
        public PhysicsBodyOutcome TryApplyIntent(
            PhysicsBodyKey key,
            AuthorityIntentKind kind,
            FrozenPayload payload,
            out string detail)
        {
            if (payload == null)
            {
                detail = "an intent carries a payload (P-042)";
                return PhysicsBodyOutcome.RefusedMalformedIntent;
            }

            if (!backend.IsAvailable)
            {
                detail = "the physics scene backend is unavailable";
                return PhysicsBodyOutcome.RefusedUnavailable;
            }

            if (!backend.TryApplyIntent(key, kind, payload, out detail))
            {
                return PhysicsBodyOutcome.RefusedUnknownBody;
            }

            return PhysicsBodyOutcome.Applied;
        }

        /// <summary>The engine's current authoritative pose for one body.</summary>
        public bool TryReadPose(PhysicsBodyKey key, out PhysicsPose pose)
        {
            pose = PhysicsPose.Zero;
            return backend.IsAvailable && backend.TryReadPose(key, out pose);
        }

        /// <summary>
        /// Simulates the local scene exactly once for one admitted logical step. A repeated call for the same step, a
        /// regressing step, a degenerate (zero) step or an unavailable backend is refused as a value: the gate never
        /// simulates twice for one admitted step and never throws, because a throw here would fault the world
        /// (P-031, 04 s7).
        /// </summary>
        public bool TrySimulateExactlyOnce(ulong admittedStep, double fixedDeltaSeconds, out string detail)
        {
            detail = string.Empty;
            if (admittedStep == 0UL || fixedDeltaSeconds <= 0d)
            {
                RefusedSimulationCount++;
                detail = "a simulation needs a positive admitted step and a positive fixed delta (P-036)";
                return false;
            }

            if (hasSimulated != 0)
            {
                if (admittedStep == lastSimulatedStep)
                {
                    DuplicateStepRefusalCount++;
                    detail = "step " + admittedStep.ToString(CultureInfo.InvariantCulture)
                        + " was already simulated once; a presentation rate never advances authority twice (04 s7)";
                    return false;
                }

                if (admittedStep < lastSimulatedStep)
                {
                    RegressingStepRefusalCount++;
                    detail = "step " + admittedStep.ToString(CultureInfo.InvariantCulture)
                        + " is not newer than the last simulated step "
                        + lastSimulatedStep.ToString(CultureInfo.InvariantCulture) + " (P-036)";
                    return false;
                }
            }

            if (!backend.IsAvailable)
            {
                RefusedSimulationCount++;
                detail = "the physics scene backend is unavailable";
                return false;
            }

            if (!backend.TrySimulate(fixedDeltaSeconds, out detail))
            {
                RefusedSimulationCount++;
                return false;
            }

            lastSimulatedStep = admittedStep;
            hasSimulated = 1;
            SteppedStepCount++;
            return true;
        }

        /// <summary>True when the given admitted step has already been simulated exactly once.</summary>
        public bool HasSimulatedStep(ulong admittedStep) => hasSimulated != 0 && lastSimulatedStep == admittedStep;

        private static int CompareBodies(PhysicsBodyKey left, PhysicsBodyKey right)
        {
            int byTarget = left.Target.Value.CompareTo(right.Target.Value);
            return byTarget != 0 ? byTarget : left.Domain.CompareTo(right.Domain);
        }
    }
}
