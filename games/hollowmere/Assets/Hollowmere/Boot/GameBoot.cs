// Hollowmere - GameBoot: boots the baked Hollowmere world through the application root (P1.1, SADR-010).
//
// Boot.unity holds one GameBoot. On Start it resolves the generated catalog the manifest was baked with, builds the
// gameplay application definition (WorldBuilder.Build), boots it with GameApplication.Boot, attaches the gameplay
// world (slots, modules, presentation frame), creates the view binders under its own transform and starts the world.
// Streaming and presentation then run inside the application's one pump; GameBoot itself has no Update.
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Boots the Hollowmere world from its baked region manifest.</summary>
    [DisallowMultipleComponent]
    public sealed class GameBoot : MonoBehaviour
    {
        [SerializeField] private RegionManifest? manifest;

        [Tooltip("Keep the neighbours of the focus region resident (overrides the world's setting when enabled).")]
        [SerializeField] private bool preloadNeighbours;

        /// <summary>The running gameplay world; null before Start or when the boot refused.</summary>
        public GameplayWorld? World { get; private set; }

        /// <summary>The refusal of the last boot attempt; empty when it booted.</summary>
        public string Failure { get; private set; } = string.Empty;

        public RegionManifest? Manifest => manifest;

        public void Configure(RegionManifest regionManifest, bool preload)
        {
            manifest = regionManifest;
            preloadNeighbours = preload;
        }

        private void Start()
        {
            if (manifest == null)
            {
                Failure = "GameBoot has no region manifest; run GameCore/Gameplay/Bake World";
                Debug.LogError("[Hollowmere] " + Failure);
                return;
            }

            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail) || catalog == null)
            {
                Failure = detail;
                Debug.LogError("[Hollowmere] " + Failure);
                return;
            }

            WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, new WorldBuildOptions { Name = "Hollowmere" });
            GameApplicationRoot root = GameApplication.Boot(plan.Definition, new GameApplicationBootOptions());
            GameplayWorld world = WorldBuilder.Attach(root, plan);
            if (preloadNeighbours)
            {
                world.Streamer.PreloadNeighbours = true;
            }

            world.Streamer.Observe(destroyCancellationToken);
            world.CreateViews(transform);
            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = world;
            root.Start();
            World = world;
        }

        private void OnDestroy()
        {
            GameplayWorld? world = World;
            World = null;
            if (world == null)
            {
                return;
            }

            world.Shutdown();
            if (world.Root.State != GameApplicationState.Stopped)
            {
                world.Root.Stop("GameBoot destroyed");
            }
        }
    }
}
