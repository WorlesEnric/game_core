#nullable enable
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// The single application <see cref="ICustomBootstrap"/>. It creates the designated gameplay world from the
    /// application composition root, assigns <c>World.DefaultGameObjectInjectionWorld</c>, installs exactly one
    /// application-owned PlayerLoop node and returns <c>true</c> to suppress Unity's default gameplay
    /// initialization. It never discovers systems by scanning loaded assemblies (04 s3).
    /// </summary>
    public sealed class GameCoreApplicationBootstrap : ICustomBootstrap
    {
        /// <summary>Bootstraps performed since process start; one per Play Mode session (TEST-018).</summary>
        public static int BootstrapCount { get; private set; }

        /// <summary>Bootstraps that fell back to the infrastructure-only world because creation failed.</summary>
        public static int FallbackCount { get; private set; }

        public static string LastWorldName { get; private set; } = string.Empty;

        public static DiagnosticCode LastCode { get; private set; } = DiagnosticCode.None;

        public static string LastDetail { get; private set; } = string.Empty;

        public bool Initialize(string defaultWorldName)
        {
            _ = defaultWorldName;

            GameCoreThreading.CaptureMainThread();
            BootstrapCount++;

            // One application-owned route: remove any node a previous session left behind, then install one.
            GameCorePlayerLoopInstaller.EnsureInstalled();

            GameCoreApplicationCompositionRoot root =
                GameCoreApplicationComposition.TryCreateRoot() ?? GameCoreApplicationComposition.DefaultRoot();

            WorldId session = GameCoreApplicationComposition.ReserveSessionId();
            var operation = new OperationId(session, GameCoreApplicationComposition.BootstrapIssuerId, 1UL);

            bool created = TryCreateWorld(root, session, operation, out UnityWorldHost? host, out WorldCreateResult result);
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
            }

            World.DefaultGameObjectInjectionWorld = host == null ? null : host.EntityWorld;
            return true;
        }

        private static bool TryCreateWorld(
            GameCoreApplicationCompositionRoot root,
            WorldId session,
            OperationId operation,
            out UnityWorldHost? host,
            out WorldCreateResult result)
        {
            // W1 has no catalog hash yet: the generated catalog and its fingerprint belong to the content compiler
            // (GC-003/GC-006). The empty hash means "no catalog declared", never a substituted default catalog.
            WorldCreateRequest request = root.CreateRequest(
                session,
                operation,
                PropagationMode.Automatic,
                ContentHash.Empty);

            return UnityWorldRegistry.TryCreate(request, root.Registration, out host, out result);
        }
    }
}
