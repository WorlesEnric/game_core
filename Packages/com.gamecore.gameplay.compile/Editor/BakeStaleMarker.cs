// GameCore.Gameplay.Compile.Editor - stale-marks the world bake when authored content is saved (P1.1).
//
// Saving a region scene or a gameplay definition asset marks the bake stale (SessionState, so no static mutable state
// and nothing survives an editor restart that a Verify would not catch anyway). Baking never runs inside the save
// callback: a bake opens and closes scenes and saves assets, which must not happen re-entrantly in OnWillSaveAssets.
// When the opt-in auto-bake preference is on AND the last measured bake took at most AutoBakeBudgetMilliseconds, a
// bake is scheduled for after the save (EditorApplication.delayCall); a slower bake only stale-marks and leaves it to
// the menu item, a test, or CI (Entry.Bake / Entry.Verify).
#nullable enable
using System;
using System.Diagnostics;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using UnityEditor;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Stale-marks the gameplay bake when region scenes or definitions are saved.</summary>
    public sealed class BakeStaleMarker : AssetModificationProcessor
    {
        /// <summary>A bake slower than this is never scheduled automatically after a save.</summary>
        public const long AutoBakeBudgetMilliseconds = 1000;

        public const string AutoBakePreference = "GameCore.Gameplay.AutoBake";

        private const string StaleKey = "GameCore.Gameplay.BakeStale";
        private const string LastBakeKey = "GameCore.Gameplay.LastBakeMilliseconds";
        private const string BakingKey = "GameCore.Gameplay.Baking";
        private const string LastMarkKey = "GameCore.Gameplay.LastStaleMarkMicroseconds";

        /// <summary>True when authored content was saved after the last successful bake in this editor session.</summary>
        public static bool IsStale => SessionState.GetBool(StaleKey, false);

        /// <summary>Duration of the last bake in this session; -1 when none ran.</summary>
        public static long LastBakeMilliseconds => SessionState.GetInt(LastBakeKey, -1);

        /// <summary>Duration of the last stale-mark check (inside the save callback), in microseconds.</summary>
        public static long LastStaleMarkMicroseconds => SessionState.GetInt(LastMarkKey, -1);

        public static bool AutoBake
        {
            get => EditorPrefs.GetBool(AutoBakePreference, false);
            set => EditorPrefs.SetBool(AutoBakePreference, value);
        }

        internal static void Clear() => SessionState.SetBool(StaleKey, false);

        internal static void BeginBake() => SessionState.SetBool(BakingKey, true);

        internal static void EndBake(long elapsedMilliseconds)
        {
            SessionState.SetBool(BakingKey, false);
            SessionState.SetInt(LastBakeKey, (int)Math.Min(int.MaxValue, Math.Max(0L, elapsedMilliseconds)));
        }

        /// <summary>Called by Unity before assets are saved; never bakes here.</summary>
        public static string[] OnWillSaveAssets(string[] paths)
        {
            if (paths == null || paths.Length == 0 || SessionState.GetBool(BakingKey, false))
            {
                return paths ?? Array.Empty<string>();
            }

            Stopwatch clock = Stopwatch.StartNew();
            bool relevant = false;
            for (int i = 0; i < paths.Length && !relevant; i++)
            {
                relevant = IsAuthoredContent(paths[i]);
            }

            if (relevant)
            {
                SessionState.SetBool(StaleKey, true);
                long last = LastBakeMilliseconds;
                if (AutoBake && last >= 0 && last <= AutoBakeBudgetMilliseconds)
                {
                    EditorApplication.delayCall += BakeAfterSave;
                }
            }

            SessionState.SetInt(LastMarkKey, (int)Math.Min(int.MaxValue, clock.ElapsedTicks * 1000000L / Stopwatch.Frequency));
            return paths;
        }

        /// <summary>True for a region scene of a world or a gameplay definition asset.</summary>
        public static bool IsAuthoredContent(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                string[] worlds = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition));
                for (int w = 0; w < worlds.Length; w++)
                {
                    WorldDefinition? world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(AssetDatabase.GUIDToAssetPath(worlds[w]));
                    if (world == null)
                    {
                        continue;
                    }

                    for (int r = 0; r < world.Regions.Count; r++)
                    {
                        if (world.Regions[r] != null && string.Equals(world.Regions[r].ScenePath, path, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            Type? type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type == typeof(EntityDefinition) || type == typeof(VariantDefinition) || type == typeof(RegionDefinition)
                || type == typeof(PortalDefinition) || type == typeof(WorldDefinition);
        }

        private static void BakeAfterSave()
        {
            if (!IsStale || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            BakeResult result = Entry.Bake();
            if (!result.Succeeded)
            {
                UnityEngine.Debug.LogWarning("[GameCore] automatic bake after save failed; the bake stays stale:\n" + result);
            }
        }
    }
}
