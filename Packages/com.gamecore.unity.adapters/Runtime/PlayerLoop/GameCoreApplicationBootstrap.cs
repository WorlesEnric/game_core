#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// What the bootstrap does when the application's composition root cannot produce its gameplay world (SADR-010).
    /// </summary>
    public enum GameCoreBootstrapFailurePolicy
    {
        /// <summary>
        /// Legacy behaviour, kept for the validation project and every application that registers no game root: a
        /// missing or failing root falls back to the infrastructure-only world and the failure is counted in
        /// <see cref="GameCoreApplicationBootstrap.FallbackCount"/>.
        /// </summary>
        InfrastructureFallback = 0,

        /// <summary>
        /// A game's application root (com.gamecore.unity.app): a missing root, an undeclared catalog hash or a failed
        /// world creation is a named, logged boot failure. No infrastructure-only world is created in its place, so a
        /// player with a broken catalog exits with a failure code instead of running an empty world.
        /// </summary>
        HardFailure = 1,
    }

    /// <summary>
    /// The single application <see cref="ICustomBootstrap"/>. It creates the designated gameplay world from the
    /// application composition root, assigns <c>World.DefaultGameObjectInjectionWorld</c>, installs exactly one
    /// application-owned PlayerLoop node and returns <c>true</c> to suppress Unity's default gameplay
    /// initialization. It never discovers systems by scanning loaded assemblies (04 s3).
    /// </summary>
    /// <remarks>
    /// SADR-010: <see cref="FailurePolicy"/>, <see cref="CatalogHash"/>, <see cref="Propagation"/> and
    /// <see cref="WorldCreated"/> are application configuration, set from the same
    /// <c>RuntimeInitializeOnLoadMethod(SubsystemRegistration)</c> registration that sets
    /// <see cref="GameCoreApplicationComposition.RootFactory"/>. Like the root factory they are pure functions of the
    /// application and are deliberately not cleared by the domain-reload reset (04 s9). Their defaults reproduce the
    /// W1 bootstrap exactly, which is what the validation project keeps using.
    /// </remarks>
    public sealed class GameCoreApplicationBootstrap : ICustomBootstrap
    {
        /// <summary>Bootstraps performed since process start; one per Play Mode session (TEST-018).</summary>
        public static int BootstrapCount { get; private set; }

        /// <summary>Bootstraps that fell back to the infrastructure-only world because creation failed.</summary>
        public static int FallbackCount { get; private set; }

        /// <summary>Bootstraps that failed under <see cref="GameCoreBootstrapFailurePolicy.HardFailure"/>.</summary>
        public static int HardFailureCount { get; private set; }

        public static string LastWorldName { get; private set; } = string.Empty;

        public static DiagnosticCode LastCode { get; private set; } = DiagnosticCode.None;

        public static string LastDetail { get; private set; } = string.Empty;

        /// <summary>True when the most recent bootstrap created no world under the hard-failure policy.</summary>
        public static bool LastBootFailed { get; private set; }

        /// <summary>The failure policy for a root that cannot produce its world (SADR-010); legacy fallback by default.</summary>
        public static GameCoreBootstrapFailurePolicy FailurePolicy { get; set; } =
            GameCoreBootstrapFailurePolicy.InfrastructureFallback;

        /// <summary>
        /// The catalog fingerprint the gameplay world is created with (SADR-010). Empty means "no catalog declared",
        /// which the legacy policy accepts and the hard-failure policy refuses.
        /// </summary>
        public static ContentHash CatalogHash { get; set; } = ContentHash.Empty;

        /// <summary>The world's propagation mode at creation (O-03).</summary>
        public static PropagationMode Propagation { get; set; } = PropagationMode.Automatic;

        /// <summary>
        /// Called once with the gameplay world after it was created from the registered root (not for the
        /// infrastructure-only fallback). The application root composes its lane, publisher and pipeline here. An
        /// exception is a boot failure of the policy in force; it is logged and never swallowed silently.
        /// </summary>
        public static Action<UnityWorldHost>? WorldCreated { get; set; }

        /// <summary>Raised with the failure code and detail of a hard boot failure, after it was logged.</summary>
        public static event Action<DiagnosticCode, string>? BootFailed;

        public bool Initialize(string defaultWorldName)
        {
            _ = defaultWorldName;

            GameCoreThreading.CaptureMainThread();
            BootstrapCount++;
            LastBootFailed = false;

            // One application-owned route: remove any node a previous session left behind, then install one.
            GameCorePlayerLoopInstaller.EnsureInstalled();
            // The application owns exactly one quit hook, beside the one pump node.
            GameCorePlayerLoopInstaller.InstallQuitHook();

            if (FailurePolicy == GameCoreBootstrapFailurePolicy.HardFailure)
            {
                return InitializeOrFail();
            }

            GameCoreApplicationCompositionRoot? registered = GameCoreApplicationComposition.TryCreateRoot();
            GameCoreApplicationCompositionRoot root = registered ?? GameCoreApplicationComposition.DefaultRoot();

            WorldId session = GameCoreApplicationComposition.ReserveSessionId();
            var operation = new OperationId(session, GameCoreApplicationComposition.BootstrapIssuerId, 1UL);

            bool created = TryCreateWorld(root, session, operation, out UnityWorldHost? host, out WorldCreateResult result);
            bool fromRegisteredRoot = created && registered != null;
            if (!created)
            {
                // Keep exactly one update path even on a composition defect: an infrastructure-only world is created
                // and the failure is reported, rather than returning false and letting Unity create a default world
                // populated with every auto-created system (04 s3).
                FallbackCount++;
                root = GameCoreApplicationComposition.DefaultRoot();
                created = TryCreateWorld(root, session, operation, out host, out result);
            }

            if (!created || host == null)
            {
                LastCode = result.Code;
                LastDetail = result.Detail;
            }
            else
            {
                LastWorldName = host.DiagnosticName;
                LastCode = DiagnosticCode.None;
                LastDetail = result.Detail;
                if (fromRegisteredRoot)
                {
                    NotifyWorldCreated(host);
                }
            }

            World.DefaultGameObjectInjectionWorld = host == null ? null : host.EntityWorld;
            return true;
        }

        /// <summary>
        /// SADR-010 hard-failure path: the registered root must exist, declare a catalog hash and create its world.
        /// Any failure is logged with its code and raised through <see cref="BootFailed"/>; no fallback world is
        /// created, and the bootstrap still returns <c>true</c> so Unity never builds its default world either.
        /// </summary>
        private static bool InitializeOrFail()
        {
            GameCoreApplicationCompositionRoot? root;
            try
            {
                root = GameCoreApplicationComposition.TryCreateRoot();
            }
            catch (Exception exception)
            {
                return Fail(DiagnosticCode.ProviderFailed,
                    "the application composition root factory threw: " + exception.GetType().Name + ": "
                    + exception.Message);
            }

            if (root == null)
            {
                return Fail(DiagnosticCode.MissingDependency,
                    "the hard-failure boot policy is set but no application composition root is registered (SADR-010)");
            }

            if (CatalogHash.IsEmpty)
            {
                return Fail(DiagnosticCode.MissingDependency,
                    "the application root declares no catalog hash; a gameplay world is created with the real catalog "
                    + "fingerprint, never with an empty one (SADR-010)");
            }

            WorldId session = GameCoreApplicationComposition.ReserveSessionId();
            var operation = new OperationId(session, GameCoreApplicationComposition.BootstrapIssuerId, 1UL);
            if (!TryCreateWorld(root, session, operation, out UnityWorldHost? host, out WorldCreateResult result)
                || host == null)
            {
                return Fail(result.Code == DiagnosticCode.None ? DiagnosticCode.ApplyFault : result.Code,
                    "the gameplay world could not be created: " + result.Detail);
            }

            LastWorldName = host.DiagnosticName;
            LastCode = DiagnosticCode.None;
            LastDetail = result.Detail;
            World.DefaultGameObjectInjectionWorld = host.EntityWorld;
            if (!NotifyWorldCreated(host))
            {
                // The hook failed and was reported as a hard failure; the half-composed world is not handed out.
                World.DefaultGameObjectInjectionWorld = null;
            }

            return true;
        }

        private static bool NotifyWorldCreated(UnityWorldHost host)
        {
            Action<UnityWorldHost>? hook = WorldCreated;
            if (hook == null)
            {
                return true;
            }

            try
            {
                hook(host);
                return true;
            }
            catch (Exception exception)
            {
                string detail = "the application root's world-created hook threw: " + exception.GetType().Name + ": "
                    + exception.Message;
                if (FailurePolicy == GameCoreBootstrapFailurePolicy.HardFailure)
                {
                    Fail(DiagnosticCode.ProviderFailed, detail);
                }
                else
                {
                    LastCode = DiagnosticCode.ProviderFailed;
                    LastDetail = detail;
                    UnityEngine.Debug.LogError("[GameCore] " + detail);
                }

                return false;
            }
        }

        private static bool Fail(DiagnosticCode code, string detail)
        {
            HardFailureCount++;
            LastBootFailed = true;
            LastCode = code;
            LastDetail = detail;
            World.DefaultGameObjectInjectionWorld = null;
            UnityEngine.Debug.LogError(
                "[GameCore] application boot failed (" + DiagnosticCodeText.Of(code) + "): " + detail);
            Action<DiagnosticCode, string>? failed = BootFailed;
            failed?.Invoke(code, detail);

            // True suppresses Unity's default world: a failed boot leaves no world at all, never an empty one (04 s3).
            return true;
        }

        private static bool TryCreateWorld(
            GameCoreApplicationCompositionRoot root,
            WorldId session,
            OperationId operation,
            out UnityWorldHost? host,
            out WorldCreateResult result)
        {
            // SADR-010: the world is created with the catalog fingerprint the application declared. The legacy
            // default is the empty hash, which means "no catalog declared", never a substituted default catalog.
            WorldCreateRequest request = root.CreateRequest(
                session,
                operation,
                Propagation,
                CatalogHash);

            return UnityWorldRegistry.TryCreate(request, root.Registration, out host, out result);
        }
    }
}
