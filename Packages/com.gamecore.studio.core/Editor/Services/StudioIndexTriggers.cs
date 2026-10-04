// GameCore.Studio.Edit - incremental index triggers (docs/studio/03-authoring-contracts.md s3: the index is updated on
// asset import, scene save, prefab stage changes and Undo-recorded edits). Triggers only mark sources dirty; the index
// re-projects lazily on the next read (Flush), so a burst of edits costs one projection. Triggers do nothing until the
// project's runtime exists, because an index that was never built has nothing to keep current (it builds on first
// read). Before a domain reload or quit the index cache is saved and the session state persisted.
#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Edit
{
    [InitializeOnLoad]
    internal static class StudioIndexTriggers
    {
        static StudioIndexTriggers()
        {
            EditorSceneManager.sceneOpened += (scene, _) => WithIndex(index => index.MarkSceneChanged(scene));
            EditorSceneManager.sceneSaved += scene => WithIndex(index => index.MarkSceneChanged(scene));
            EditorSceneManager.sceneClosed += scene => WithIndex(index => index.MarkSceneClosed(scene));
            EditorSceneManager.sceneDirtied += scene => WithIndex(index => index.MarkSceneChanged(scene));
            PrefabStage.prefabStageOpened += _ => WithIndex(index => index.MarkPrefabStageChanged());
            PrefabStage.prefabStageClosing += _ => WithIndex(index => index.MarkPrefabStageChanged());
            EditorApplication.hierarchyChanged += () => WithIndex(MarkLoadedScenes);
            Undo.undoRedoPerformed += () => WithIndex(index =>
            {
                MarkLoadedScenes(index);
                index.MarkPrefabStageChanged();
            });
            Undo.postprocessModifications += modifications =>
            {
                WithIndex(index =>
                {
                    foreach (UndoPropertyModification modification in modifications)
                    {
                        index.MarkObjectChanged(modification.currentValue?.target);
                    }
                });
                return modifications;
            };
            AssemblyReloadEvents.beforeAssemblyReload += () => StudioServices.instance.Persist(true);
            EditorApplication.quitting += () => StudioServices.instance.Persist(false);
        }

        internal static void WithIndex(System.Action<SemanticIndexService> action)
        {
            if (StudioServices.HasRuntime)
            {
                action(StudioServices.Runtime.Index);
            }
        }

        private static void MarkLoadedScenes(SemanticIndexService index)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    index.MarkSceneChanged(scene);
                }
            }
        }
    }

    /// <summary>Asset import, delete and move notifications for the project's index.</summary>
    internal sealed class StudioIndexAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            StudioIndexTriggers.WithIndex(index => index.MarkAssetsChanged(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths));
            if (StudioServices.HasRuntime && (importedAssets.Length > 0 || deletedAssets.Length > 0))
            {
                foreach (string path in importedAssets)
                {
                    if (path.EndsWith(".cs", System.StringComparison.OrdinalIgnoreCase) || path.EndsWith(".asmdef", System.StringComparison.OrdinalIgnoreCase))
                    {
                        StudioServices.Runtime.InvalidateCode();
                        break;
                    }
                }
            }
        }
    }
}
