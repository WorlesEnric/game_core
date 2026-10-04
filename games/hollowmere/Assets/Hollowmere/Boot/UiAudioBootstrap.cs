// Hollowmere - the one call GameBoot makes to add the UI and audio (P1.5).
//
// GameBoot.Start builds the world with `new WorldBuildOptions { Name = "Hollowmere" }`. The integration hook wraps it:
//
//     WorldBuilder.Build(manifest, catalog, fingerprint, UiAudioBootstrap.Configure(new WorldBuildOptions { Name = "Hollowmere" }, gameObject));
//
// Configure loads the UI/audio content asset (Resources/Hollowmere/UiAudio) as an IGameplayWorldExtensionSource, so the
// boot assembly needs no reference to the UI or audio packages. Without the asset the options are returned unchanged
// and the game runs without UI and audio.
#nullable enable
using GameCore.Gameplay.World;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Adds the Hollowmere UI and audio extensions to a world build.</summary>
    public static class UiAudioBootstrap
    {
        public const string ResourcePath = "Hollowmere/UiAudio";

        /// <summary>Contributes the UI and audio to <paramref name="options"/> (built under <paramref name="host"/>); returns the options.</summary>
        public static WorldBuildOptions Configure(WorldBuildOptions options, GameObject? host)
        {
            if (Resources.Load<ScriptableObject>(ResourcePath) is IGameplayWorldExtensionSource source)
            {
                source.Contribute(options, host);
            }
            else
            {
                Debug.LogWarning("[Hollowmere] no UI/audio content at Resources/" + ResourcePath + "; running without UI and audio");
            }

            return options;
        }
    }
}
