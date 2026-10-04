// GameCore.Gameplay.World - booting a baked world: catalog resolution and the one-call boot (P1.1).
//
// The generated catalog class is produced by the bake into the game's own (non-auto-referenced) generated assembly, so
// gameplay code never references it by type: GameplayCatalog resolves it by the full name the manifest records and calls
// its generated BuildCatalog(). GameplayBoot is the whole boot path of a gameplay game: resolve the catalog, build the
// application definition (WorldBuilder.Build), boot it (GameApplication.TryBoot), attach the world (WorldBuilder.Attach)
// and start it.
#nullable enable
using System;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Unity.App;

namespace GameCore.Gameplay.World
{
    /// <summary>Resolves the generated catalog a manifest was baked with.</summary>
    public static class GameplayCatalog
    {
        /// <summary>Finds the generated catalog class by full name in the loaded assemblies and builds the catalog.</summary>
        public static bool TryBuild(string catalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail)
        {
            catalog = null;
            fingerprint = ContentHash.Empty;
            if (string.IsNullOrEmpty(catalogTypeName))
            {
                detail = GameplayDiagnosticCodes.BakeStale + ": the manifest names no generated catalog; run GameCore.Gameplay.Compile.Entry.Bake";
                return false;
            }

            Type? type = Find(catalogTypeName);
            if (type == null)
            {
                detail = GameplayDiagnosticCodes.CatalogStale + ": the generated catalog " + catalogTypeName + " is not compiled into the game";
                return false;
            }

            MethodInfo? build = type.GetMethod("BuildCatalog", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (build == null || build.ReturnType != typeof(CatalogBuildResult))
            {
                detail = GameplayDiagnosticCodes.CatalogStale + ": " + catalogTypeName + " has no generated BuildCatalog()";
                return false;
            }

            var result = (CatalogBuildResult?)build.Invoke(null, null);
            if (result == null || result.Catalog == null)
            {
                detail = GameplayDiagnosticCodes.CatalogStale + ": the generated catalog " + catalogTypeName + " refused to build";
                return false;
            }

            catalog = result.Catalog;
            fingerprint = result.Catalog.Fingerprint;
            detail = string.Empty;
            return true;
        }

        private static Type? Find(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type? type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }

    /// <summary>The one-call boot of a baked gameplay world.</summary>
    public static class GameplayBoot
    {
        /// <summary>
        /// Boots <paramref name="manifest"/>: catalog, definition, root, attach. The world is left Ready (paused) unless
        /// <paramref name="start"/> is true. Throws with a GP diagnostic code when the bake is stale or the boot refuses.
        /// </summary>
        public static GameplayWorld Boot(RegionManifest manifest, GameApplicationBootOptions? options, WorldBuildOptions? build, bool start)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail) || catalog == null)
            {
                throw new InvalidOperationException(detail);
            }

            WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, build);
            GameApplicationBootOptions effective = options ?? new GameApplicationBootOptions();
            bool startLater = effective.StartImmediately;
            effective.StartImmediately = false;
            if (!GameApplication.TryBoot(plan.Definition, effective, out GameApplicationRoot? root, out GameApplicationBootFailed? failure) || root == null)
            {
                throw new InvalidOperationException("the gameplay world refused to boot: " + (failure != null ? failure.ToString() : "no root"));
            }

            GameplayWorld world;
            try
            {
                world = WorldBuilder.Attach(root, plan);
            }
            catch (Exception)
            {
                root.Stop("gameplay attach failed");
                throw;
            }

            if (start || startLater)
            {
                OperationResult started = root.Start();
                if (started.Outcome == Outcome.Rejected)
                {
                    root.Stop("gameplay start refused");
                    throw new InvalidOperationException("the gameplay world refused to start: " + started.Code);
                }
            }

            return world;
        }
    }
}
