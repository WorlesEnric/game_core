// GameCore.Studio.Edit - the default IAuthoringSource (docs/studio/03-authoring-contracts.md s3).
// Finds every object whose type carries [Authorable] (identity is duck-typed, see AuthoringIdentity) in loaded scenes,
// the open prefab stage, ScriptableObject assets of authorable types and prefab assets under the search folders.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    /// <summary>Enumerates authored objects from the AssetDatabase and the loaded scenes.</summary>
    public sealed class DefaultAuthoringSource : IAuthoringSource
    {
        private readonly AuthoringIdentity _identity;
        private readonly AuthorableTypeRegistry _types;

        public DefaultAuthoringSource(AuthoringIdentity identity, AuthorableTypeRegistry types, IReadOnlyList<string>? searchFolders = null)
        {
            _identity = identity ?? throw new ArgumentNullException(nameof(identity));
            _types = types ?? throw new ArgumentNullException(nameof(types));
            SearchFolders = searchFolders ?? new[] { "Assets" };
        }

        /// <summary>Folders scanned for assets (default: Assets).</summary>
        public IReadOnlyList<string> SearchFolders { get; set; }

        public IEnumerable<AuthoredObjectEntry> Enumerate(AuthoringSourceScope scope)
        {
            List<AuthoredObjectEntry> entries = new List<AuthoredObjectEntry>();
            if ((scope & AuthoringSourceScope.OpenScenes) != 0)
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded)
                    {
                        entries.AddRange(EnumerateScene(scene));
                    }
                }
            }

            if ((scope & AuthoringSourceScope.PrefabStage) != 0)
            {
                PrefabStage? stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.prefabContentsRoot != null)
                {
                    AddHierarchy(entries, stage.prefabContentsRoot, AuthoredObjectLocation.Prefab, AuthoringSourceKeys.ForStage(stage.assetPath), stage.assetPath);
                }
            }

            if ((scope & AuthoringSourceScope.Assets) != 0)
            {
                foreach (string path in AssetPaths())
                {
                    entries.AddRange(EnumerateAsset(path));
                }
            }

            return entries;
        }

        public IEnumerable<AuthoredObjectEntry> EnumerateAsset(string assetPath)
        {
            List<AuthoredObjectEntry> entries = new List<AuthoredObjectEntry>();
            if (string.IsNullOrEmpty(assetPath) || assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(assetPath))
            {
                return entries;
            }

            string key = AuthoringSourceKeys.ForAsset(assetPath);
            if (assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                GameObject? root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (root != null)
                {
                    AddHierarchy(entries, root, AuthoredObjectLocation.Prefab, key, assetPath);
                }

                return entries;
            }

            if (!assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            {
                return entries;
            }

            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (candidate == null || candidate is Component || candidate is GameObject)
                {
                    continue;
                }

                AuthoringTypeInfo? info = _identity.Describe(candidate);
                if (info != null)
                {
                    entries.Add(new AuthoredObjectEntry(candidate, info, AuthoredObjectLocation.Asset, key, assetPath));
                }
            }

            return entries;
        }

        public IEnumerable<AuthoredObjectEntry> EnumerateScene(Scene scene)
        {
            List<AuthoredObjectEntry> entries = new List<AuthoredObjectEntry>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return entries;
            }

            string key = SceneKey(scene);
            string? path = string.IsNullOrEmpty(scene.path) ? null : scene.path;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                AddHierarchy(entries, root, AuthoredObjectLocation.Scene, key, path);
            }

            return entries;
        }

        public string SceneKey(Scene scene) => AuthoringSourceKeys.ForScene(scene);

        /// <summary>Asset paths that may hold authored objects: authorable ScriptableObject types and prefabs.</summary>
        public IReadOnlyList<string> AssetPaths()
        {
            SortedSet<string> paths = new SortedSet<string>(StringComparer.Ordinal);
            string[] folders = ValidFolders();
            if (folders.Length == 0)
            {
                return new List<string>();
            }

            foreach (AuthoringTypeInfo info in _types.Types)
            {
                if (!info.IsScriptableObject)
                {
                    continue;
                }

                foreach (string guid in AssetDatabase.FindAssets("t:" + info.Type.Name, folders))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrEmpty(path))
                    {
                        paths.Add(path);
                    }
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                {
                    paths.Add(path);
                }
            }

            return new List<string>(paths);
        }

        private string[] ValidFolders()
        {
            List<string> folders = new List<string>();
            foreach (string folder in SearchFolders)
            {
                if (AssetDatabase.IsValidFolder(folder))
                {
                    folders.Add(folder);
                }
            }

            return folders.ToArray();
        }

        private void AddHierarchy(List<AuthoredObjectEntry> entries, GameObject root, AuthoredObjectLocation location, string key, string? assetPath)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || AuthoringRefResolver.IsStudioInternal(behaviour))
                {
                    continue;
                }

                AuthoringTypeInfo? info = _identity.Describe(behaviour.GetType());
                if (info != null)
                {
                    entries.Add(new AuthoredObjectEntry(behaviour, info, location, key, assetPath));
                }
            }
        }
    }
}
