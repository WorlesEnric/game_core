// GameCore.Studio.Authoring - enumeration of authored objects (docs/studio/03-authoring-contracts.md s3).
// The semantic index is a projection built from an IAuthoringSource. The Editor's default source finds every object
// whose type carries [Authorable] in open scenes, the open prefab stage and project assets; other packages may supply
// sources of their own (for example generated content).
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Authoring
{
    /// <summary>Where an authored object lives.</summary>
    public enum AuthoredObjectLocation
    {
        /// <summary>A component on a GameObject of a loaded scene.</summary>
        Scene,
        /// <summary>A component inside a prefab asset (or the open prefab stage).</summary>
        Prefab,
        /// <summary>A ScriptableObject (or other) asset.</summary>
        Asset,
    }

    /// <summary>Which parts of the project a source enumeration covers.</summary>
    [Flags]
    public enum AuthoringSourceScope
    {
        OpenScenes = 1,
        PrefabStage = 2,
        Assets = 4,
        All = OpenScenes | PrefabStage | Assets,
    }

    /// <summary>One authored object found by a source.</summary>
    public sealed class AuthoredObjectEntry
    {
        public AuthoredObjectEntry(UnityEngine.Object target, AuthoringTypeInfo type, AuthoredObjectLocation location, string sourceKey, string? assetPath)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Location = location;
            SourceKey = sourceKey ?? throw new ArgumentNullException(nameof(sourceKey));
            AssetPath = assetPath;
        }

        public UnityEngine.Object Target { get; }

        public AuthoringTypeInfo Type { get; }

        public AuthoredObjectLocation Location { get; }

        /// <summary>
        /// The unit the index refreshes incrementally: <c>asset:&lt;path&gt;</c> for assets and prefab assets,
        /// <c>scene:&lt;path or handle&gt;</c> for scene content, <c>stage:&lt;path&gt;</c> for the prefab stage.
        /// </summary>
        public string SourceKey { get; }

        /// <summary>Asset path of the asset, prefab or scene the object is stored in; null for an unsaved scene.</summary>
        public string? AssetPath { get; }
    }

    /// <summary>Enumerates authored objects for the semantic index (03 s3).</summary>
    public interface IAuthoringSource
    {
        /// <summary>Every authored object in <paramref name="scope"/>.</summary>
        IEnumerable<AuthoredObjectEntry> Enumerate(AuthoringSourceScope scope);

        /// <summary>The authored objects stored in one asset (a ScriptableObject asset or a prefab); empty when none.</summary>
        IEnumerable<AuthoredObjectEntry> EnumerateAsset(string assetPath);

        /// <summary>The authored objects of one loaded scene.</summary>
        IEnumerable<AuthoredObjectEntry> EnumerateScene(Scene scene);

        /// <summary>The source key of a scene (stable for saved scenes, handle-based for unsaved ones).</summary>
        string SceneKey(Scene scene);
    }

    /// <summary>Optional cache authority: all current asset source keys and their imported-content fingerprints.</summary>
    public interface IAuthoringSourceFingerprints
    {
        IReadOnlyDictionary<string, string> AssetFingerprints();
    }

    /// <summary>Source-key helpers shared by sources and the index.</summary>
    public static class AuthoringSourceKeys
    {
        public const string AssetPrefix = "asset:";
        public const string ScenePrefix = "scene:";
        public const string StagePrefix = "stage:";

        public static string ForAsset(string assetPath) => AssetPrefix + assetPath;

        public static string ForScene(Scene scene)
        {
            return string.IsNullOrEmpty(scene.path)
                ? ScenePrefix + "unsaved/" + scene.handle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : ScenePrefix + scene.path;
        }

        public static string ForStage(string assetPath) => StagePrefix + assetPath;
    }
}
