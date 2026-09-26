// GameCore.Unity.Adapters.Physics — the dedicated local physics scene (GC-020).
//
// Normative sources: 04 s7's Physics row, quoted in full: "For the separate optional rigidbody mode, select Unity
// built-in 3D physics as the physical solver. The physics adapter owns a dedicated local `PhysicsScene`. The host
// calls that scene's `PhysicsScene.Simulate(fixedDelta)` exactly once per admitted action step at the plugin's
// declared stage. It does not also auto-simulate those bodies in the default scene. It applies validated intents
// before simulation, then copies pose/velocity/contact results into externally owned ECS observation components
// before downstream gameplay runs." — plus P-034 (an engine-owned physical domain is declared external authority) and
// P-008 (physics observations are compared by recorded observation, never by bitwise determinism).
//
// THIS FILE IS THE ONLY PLACE A UNITY PHYSICS TYPE APPEARS. The protocol — which body belongs to which target, when
// exactly one simulation happens, that intents are applied before it — lives in the engine-free
// `PhysicsAuthorityGate`, so the plain-dotnet suite proves it with a recording backend and this file only has to be
// right about the engine calls.
//
// TWO THINGS THIS ADAPTER DOES NOT DO.
//   * It does not simulate the default scene's bodies. Its bodies live in a scene created with
//     `LocalPhysicsMode.Physics3D`, which Unity never auto-simulates; the adapter additionally sets the global
//     `Physics.simulationMode` to `Script` for its lifetime and restores the previous mode on dispose, so no
//     FixedUpdate in the process advances any body while this adapter owns the physical domain. The restore is a
//     property of dispose, not a promise: a process that disposes nothing keeps the scripted mode, which is why an
//     application that installs no adapter never sees this code run (P-059).
//   * It never writes gameplay state. It reads and writes ITS OWN bodies and reports them as observations; ECS
//     stores the stamped observation and gameplay reads it (P-034).
#nullable enable
using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using GameCore.Contracts;
using GameCore.Unity.Adapters.Authority;

namespace GameCore.Unity.Adapters.Physics
{
    /// <summary>
    /// The real Unity physical authority: one dedicated local scene, one engine body per declared
    /// <see cref="PhysicsBodyKey"/>, and exactly one `PhysicsScene.Simulate(fixedDelta)` per admitted step.
    /// </summary>
    public sealed class UnityPhysicsSceneBackend : IPhysicsSceneBackend, IDisposable
    {
        private readonly Scene scene;
        private readonly PhysicsScene physicsScene;
        private readonly Collider[] colliders;
        private readonly Rigidbody[] bodies;
        private readonly DiagnosticCode[] codes;
        private readonly string[] details;
        private readonly PhysicsBodyKey[] keys;
        private readonly bool[] occupied;
        private readonly bool ownsScene;

        private global::UnityEngine.SimulationMode previousGlobalMode;
        private bool globalModeCaptured;
        private bool disposed;

        /// <summary>
        /// Creates the adapter's own scene with a local 3D physics solver. <paramref name="sceneName"/> is the
        /// adapter's own identity for diagnostics; it is never a Unity instance id (P-054).
        /// </summary>
        public UnityPhysicsSceneBackend(string sceneName, int capacity = 16, bool suppressGlobalAutomaticSimulation = true)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "a physics scene needs room for one body");
            }

            SceneName = string.IsNullOrEmpty(sceneName) ? "GameCorePhysicsScene" : sceneName;
            colliders = new Collider[capacity];
            bodies = new Rigidbody[capacity];
            codes = new DiagnosticCode[capacity];
            details = new string[capacity];
            keys = new PhysicsBodyKey[capacity];
            occupied = new bool[capacity];

            var parameters = new CreateSceneParameters(LocalPhysicsMode.Physics3D);
            scene = SceneManager.CreateScene(SceneName, parameters);
            physicsScene = scene.GetPhysicsScene();
            ownsScene = true;

            if (suppressGlobalAutomaticSimulation)
            {
                previousGlobalMode = global::UnityEngine.Physics.simulationMode;
                globalModeCaptured = true;
                global::UnityEngine.Physics.simulationMode = global::UnityEngine.SimulationMode.Script;
            }
        }

        /// <inheritdoc />
        public string SceneName { get; }

        /// <inheritdoc />
        public bool IsAvailable => !disposed && scene.IsValid() && physicsScene.IsValid();

        /// <inheritdoc />
        public int BodyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < occupied.Length; i++)
                {
                    if (occupied[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <inheritdoc />
        public int SimulateCount { get; private set; }

        /// <summary>
        /// True when this adapter stops Unity's own automatic simulation: its bodies are in a local scene Unity never
        /// auto-simulates, and the global mode is scripted for as long as this adapter is alive (04 s7).
        /// </summary>
        public bool AutomaticSimulationSuppressed => ownsScene && (!globalModeCaptured || global::UnityEngine.Physics.simulationMode == global::UnityEngine.SimulationMode.Script);

        /// <summary>The default scene's physics scene, so a caller can prove the adapter's bodies are not in it.</summary>
        public PhysicsScene DefaultPhysicsScene => SceneManager.GetActiveScene().GetPhysicsScene();

        /// <summary>The local scene this adapter owns, for diagnostics.</summary>
        public Scene Scene => scene;

        /// <summary>True when the adapter's local scene is a different scene from the active one (04 s7).</summary>
        public bool IsDedicatedLocalScene => scene.IsValid() && !scene.Equals(SceneManager.GetActiveScene());

        /// <summary>The last refusal's diagnostic code, so a caller can report why an engine call failed.</summary>
        public DiagnosticCode LastCode { get; private set; }

        /// <summary>The last refusal's detail, verbatim from the engine or this adapter.</summary>
        public string LastDetail { get; private set; } = string.Empty;

        /// <inheritdoc />
        public bool TryAddBody(PhysicsBodyKey key, PhysicsPose initial, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!IsAvailable)
            {
                return Fail(DiagnosticCode.ResourceUnavailable, "the local physics scene is not available", out code, out detail);
            }

            for (int i = 0; i < occupied.Length; i++)
            {
                if (occupied[i] && keys[i].Equals(key))
                {
                    return Fail(DiagnosticCode.OwnershipConflict, "the scene already holds " + key.ToString(), out code, out detail);
                }
            }

            int slot = -1;
            for (int i = 0; i < occupied.Length; i++)
            {
                if (!occupied[i])
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                return Fail(DiagnosticCode.BudgetExceeded, "the local physics scene is at its declared capacity", out code, out detail);
            }

            var bodyObject = new GameObject("GameCorePhysicsBody");
            SceneManager.MoveGameObjectToScene(bodyObject, scene);
            var collider = bodyObject.AddComponent<SphereCollider>();
            collider.radius = BodyRadiusMeters;
            var body = bodyObject.AddComponent<Rigidbody>();
            body.useGravity = GravityEnabled;
            body.isKinematic = false;
            body.position = ToMeters(initial.Position);
            body.linearVelocity = ToMetersPerSecond(initial.Velocity);

            colliders[slot] = collider;
            bodies[slot] = body;
            codes[slot] = DiagnosticCode.None;
            details[slot] = string.Empty;
            keys[slot] = key;
            occupied[slot] = true;
            return true;
        }

        /// <inheritdoc />
        public bool TryRemoveBody(PhysicsBodyKey key, out string detail)
        {
            detail = string.Empty;
            for (int i = 0; i < occupied.Length; i++)
            {
                if (!occupied[i] || !keys[i].Equals(key))
                {
                    continue;
                }

                if (bodies[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(bodies[i].gameObject);
                }

                bodies[i] = null!;
                colliders[i] = null!;
                occupied[i] = false;
                keys[i] = default(PhysicsBodyKey);
                return true;
            }

            detail = "the local physics scene does not hold " + key.ToString();
            return false;
        }

        /// <inheritdoc />
        public bool TryReadPose(PhysicsBodyKey key, out PhysicsPose pose)
        {
            pose = PhysicsPose.Zero;
            if (!IsAvailable)
            {
                return false;
            }

            for (int i = 0; i < occupied.Length; i++)
            {
                if (!occupied[i] || !keys[i].Equals(key) || bodies[i] == null)
                {
                    continue;
                }

                Rigidbody body = bodies[i];
                pose = new PhysicsPose(
                    ToMillimetres(body.position),
                    ToMillimetresPerSecond(body.linearVelocity));
                return true;
            }

            return false;
        }

        /// <summary>
        /// Applies one validated intent to the engine body. A teleport sets the pose outright; an impulse adds to the
        /// velocity. This is the ONLY write path to an externally owned pose: gameplay submits an intent here instead
        /// of writing the quantity itself (P-034, 04 s7).
        /// </summary>
        public bool TryApplyIntent(PhysicsBodyKey key, AuthorityIntentKind kind, FrozenPayload payload, out string detail)
        {
            detail = string.Empty;
            if (!IsAvailable)
            {
                detail = "the local physics scene is not available";
                return false;
            }

            if (!PhysicsIntentCodec.TryRead(payload.Bytes, out PhysicsVector3i value))
            {
                detail = "a physics intent payload is exactly " + PhysicsIntentCodec.IntentBytes.ToString() + " bytes";
                return false;
            }

            for (int i = 0; i < occupied.Length; i++)
            {
                if (!occupied[i] || !keys[i].Equals(key) || bodies[i] == null)
                {
                    continue;
                }

                Rigidbody body = bodies[i];
                switch (kind)
                {
                    case AuthorityIntentKind.Teleport:
                        body.position = ToMeters(value);
                        break;
                    case AuthorityIntentKind.Impulse:
                        body.linearVelocity = body.linearVelocity + ToMetersPerSecond(value);
                        break;
                    default:
                        detail = "unknown intent kind " + kind;
                        return false;
                }

                return true;
            }

            detail = "the local physics scene does not hold " + key.ToString();
            return false;
        }

        /// <summary>
        /// Advances this local scene by exactly one fixed step. The gate above it guarantees this is called once per
        /// admitted action step, so a 30/60/144 Hz presentation cannot double-advance the solver (04 s7, REF-A06).
        /// </summary>
        public bool TrySimulate(double fixedDeltaSeconds, out string detail)
        {
            detail = string.Empty;
            if (!IsAvailable)
            {
                detail = "the local physics scene is not available";
                return false;
            }

            if (fixedDeltaSeconds <= 0d)
            {
                detail = "a simulation needs a positive fixed delta (P-036)";
                return false;
            }

            physicsScene.Simulate((float)fixedDeltaSeconds);
            SimulateCount++;
            return true;
        }

        /// <summary>
        /// Restores the process's automatic-simulation mode and destroys the adapter's scene. Dispose is idempotent, so
        /// a repeated teardown is a no-op rather than a second scene destruction (P-048).
        /// </summary>
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (globalModeCaptured)
            {
                global::UnityEngine.Physics.simulationMode = previousGlobalMode;
                globalModeCaptured = false;
            }

            if (scene.IsValid() && ownsScene)
            {
                SceneManager.UnloadSceneAsync(scene);
            }
        }

        /// <summary>Body radius of the fixture's physical bodies, in metres.</summary>
        private const float BodyRadiusMeters = 0.25f;

        /// <summary>Whether the engine's gravity acts on these bodies; the traversal fixture drives its own policy.</summary>
        private const bool GravityEnabled = false;

        private static Vector3 ToMeters(PhysicsVector3i millimetres) =>
            new Vector3(millimetres.X * 0.001f, millimetres.Y * 0.001f, millimetres.Z * 0.001f);

        private static Vector3 ToMetersPerSecond(PhysicsVector3i thousandths) =>
            new Vector3(thousandths.X * 0.001f, thousandths.Y * 0.001f, thousandths.Z * 0.001f);

        private static PhysicsVector3i ToMillimetres(Vector3 metres) =>
            new PhysicsVector3i(
                (int)Math.Round(metres.x * 1000d),
                (int)Math.Round(metres.y * 1000d),
                (int)Math.Round(metres.z * 1000d));

        private static PhysicsVector3i ToMillimetresPerSecond(Vector3 metresPerSecond) =>
            new PhysicsVector3i(
                (int)Math.Round(metresPerSecond.x * 1000d),
                (int)Math.Round(metresPerSecond.y * 1000d),
                (int)Math.Round(metresPerSecond.z * 1000d));

        private bool Fail(DiagnosticCode failureCode, string failureDetail, out DiagnosticCode code, out string detail)
        {
            code = failureCode;
            detail = failureDetail;
            LastCode = failureCode;
            LastDetail = failureDetail;
            return false;
        }
    }
}
