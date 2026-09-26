// GameCore.Validation.ProbeHost — the genre-free engine-physics surface of one GC-027 family.
//
// The orchestrator's GC-027 review asks for one thing the two ECS-owned genres cannot show: a traversal recovery must
// prove that the *engine's* physical state is NOT continued across a recovery while the authoritative ECS state is.
// `P-054` says engine-internal solver state is never a portable checkpoint contract, and 06 s7 says an adapter
// "restores declared authoritative pose/velocity or observation policy and records any restabilization limits". So
// the claim to make is not "the physics scene came back identical" — that is excluded — but:
//
//   * the restored world declares its own physical domain and seeds its bodies from the authoritative ECS pose the
//     checkpoint carried (the engine is re-derived, never restored);
//   * the engine's own counters start at zero for the new session, so `SimulateCount` on the recovered scene counts
//     only the recovered world's own admitted steps;
//   * an observation stamped with the OLD session is refused by the new world (P-004, P-005).
//
// This type is that surface, and it is deliberately genre-free: it names only `TargetId`s, integers and a step number,
// so the runner can drive a physics scene without referencing `UnityPhysicsSceneBackend`, `PhysicsAuthorityGate` or
// any traversal type. The traversal adapter is the one place that binds those real objects to it, which is why the
// runner stays genre-neutral (P-001) while the evidence is still the real engine's own counters.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Physics;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One body's authoritative-adjacent numerical pose: the integers the physics protocol compares exactly.</summary>
    public readonly struct Gc027PhysicsPose
    {
        public Gc027PhysicsPose(int positionX, int positionY, int positionZ, int velocityX, int velocityY, int velocityZ)
        {
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            VelocityX = velocityX;
            VelocityY = velocityY;
            VelocityZ = velocityZ;
        }

        /// <summary>Position X in millimetres, the adapter's declared physical representation.</summary>
        public int PositionX { get; }

        public int PositionY { get; }

        public int PositionZ { get; }

        /// <summary>Velocity X in thousandths of a metre per second.</summary>
        public int VelocityX { get; }

        public int VelocityY { get; }

        public int VelocityZ { get; }

        /// <summary>True when this pose is the origin at rest, i.e. a body that was never declared.</summary>
        public bool IsZero =>
            PositionX == 0 && PositionY == 0 && PositionZ == 0 && VelocityX == 0 && VelocityY == 0 && VelocityZ == 0;

        public override string ToString() =>
            "(" + PositionX.ToString(CultureInfo.InvariantCulture) + ","
            + PositionY.ToString(CultureInfo.InvariantCulture) + ","
            + PositionZ.ToString(CultureInfo.InvariantCulture) + ")v("
            + VelocityX.ToString(CultureInfo.InvariantCulture) + ","
            + VelocityY.ToString(CultureInfo.InvariantCulture) + ","
            + VelocityZ.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One family's engine-physics domain, as the GC-027 runner drives it. Every method is a thin, coded wrapper: a
    /// refusal is a value with a reason, never a throw, because a throw inside a recovery observation would fault the
    /// world the observation is about (P-031, P-052).
    /// </summary>
    public sealed class Gc027PhysicsDomain
    {
        private readonly Func<TargetId, Gc027PhysicsPose> readAuthoritativePose;
        private readonly Func<TargetId, Gc027PhysicsPose> readEnginePose;
        private readonly Func<TargetId, bool> declareBody;
        private readonly Func<ulong, double, PhysicsStepOutcome> stepOnce;
        private readonly Func<int> engineSimulations;

        public Gc027PhysicsDomain(
            string kind,
            bool dedicatedLocalScene,
            IReadOnlyList<TargetId> bodies,
            Func<TargetId, Gc027PhysicsPose> readAuthoritativePose,
            Func<TargetId, Gc027PhysicsPose> readEnginePose,
            Func<TargetId, bool> declareBody,
            Func<ulong, double, PhysicsStepOutcome> stepOnce,
            Func<int> engineSimulations)
        {
            Kind = kind ?? string.Empty;
            DedicatedLocalScene = dedicatedLocalScene;
            Bodies = bodies ?? Array.Empty<TargetId>();
            this.readAuthoritativePose = readAuthoritativePose
                ?? throw new ArgumentNullException(nameof(readAuthoritativePose));
            this.readEnginePose = readEnginePose ?? throw new ArgumentNullException(nameof(readEnginePose));
            this.declareBody = declareBody ?? throw new ArgumentNullException(nameof(declareBody));
            this.stepOnce = stepOnce ?? throw new ArgumentNullException(nameof(stepOnce));
            this.engineSimulations = engineSimulations ?? throw new ArgumentNullException(nameof(engineSimulations));
        }

        /// <summary>Diagnostic name of the runtime this domain wraps (never an identity; P-004).</summary>
        public string Kind { get; }

        /// <summary>True when the family declared a dedicated local scene rather than the global one (04 s7).</summary>
        public bool DedicatedLocalScene { get; }

        /// <summary>The bodies this domain owns, in canonical declaration order (P-008).</summary>
        public IReadOnlyList<TargetId> Bodies { get; }

        /// <summary>
        /// The authoritative pose the world's own storage holds: an ECS component read, which is what a checkpoint
        /// carries and what a recovery re-seeds from (P-054's "declared authoritative pose/velocity").
        /// </summary>
        public Gc027PhysicsPose AuthoritativePose(TargetId target) => readAuthoritativePose(target);

        /// <summary>The engine's own current pose for one body, or the zero pose when the engine has none.</summary>
        public Gc027PhysicsPose EnginePose(TargetId target) => readEnginePose(target);

        /// <summary>Declares one body at its authoritative pose; false when the engine refused the declaration.</summary>
        public bool TryDeclareBody(TargetId target) => declareBody(target);

        /// <summary>
        /// Simulates the local scene exactly once for one admitted logical step. The gate refuses a repeat of the same
        /// step as a value, which is the whole "one simulation per admitted step" obligation in one call (REF-A06).
        /// </summary>
        public PhysicsStepOutcome TryStepOnce(ulong admittedStep, double fixedDeltaSeconds) =>
            stepOnce(admittedStep, fixedDeltaSeconds);

        /// <summary>Explicit simulations the ENGINE performed: the counter a recovered scene must restart from zero.</summary>
        public int EngineSimulationCount() => engineSimulations();

        public override string ToString() =>
            "physicsDomain(" + Kind + ",bodies=" + Bodies.Count.ToString(CultureInfo.InvariantCulture)
            + ",dedicatedScene=" + (DedicatedLocalScene ? "1" : "0") + ")";
    }

    /// <summary>
    /// The outcome of one admissible physics step, reduced to the three cases the runner distinguishes: the scene
    /// stepped, the gate refused a repeated step, or the backend refused the step outright. It is a plain enum so no
    /// physics type from the adapters package escapes into the runner's own logic (P-001).
    /// </summary>
    public enum PhysicsStepOutcome
    {
        /// <summary>The scene simulated once for this admitted step.</summary>
        Stepped = 0,

        /// <summary>The same admitted step was already simulated, so the gate refused the repeat (REF-A06).</summary>
        DuplicateStepRefused = 1,

        /// <summary>The scene could not be obtained, or the step was degenerate.</summary>
        Refused = 2,
    }
}
