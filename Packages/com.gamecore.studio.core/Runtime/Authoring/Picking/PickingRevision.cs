#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Studio.Authoring
{
    // Authoring's asmdef is Editor-only. Keep event ownership in a singleton, never in
    // individual services: opening/closing viewports must not retain their scenes.
    internal sealed class PickingRevision : ScriptableSingleton<PickingRevision>
    {
        public long Frame { get; private set; }
        public long Content { get; private set; }

        private void OnEnable()
        {
            EditorApplication.update += UpdateFrame;
            EditorApplication.hierarchyChanged += Changed;
            EditorApplication.projectChanged += Changed;
            Undo.undoRedoPerformed += Changed;
            ObjectChangeEvents.changesPublished += ObjectsChanged;
            EditorSceneManager.sceneOpened += SceneOpened;
            EditorSceneManager.sceneClosed += SceneClosed;
            EditorSceneManager.sceneSaved += SceneClosed;
            EditorApplication.playModeStateChanged += PlayChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= UpdateFrame;
            EditorApplication.hierarchyChanged -= Changed;
            EditorApplication.projectChanged -= Changed;
            Undo.undoRedoPerformed -= Changed;
            ObjectChangeEvents.changesPublished -= ObjectsChanged;
            EditorSceneManager.sceneOpened -= SceneOpened;
            EditorSceneManager.sceneClosed -= SceneClosed;
            EditorSceneManager.sceneSaved -= SceneClosed;
            EditorApplication.playModeStateChanged -= PlayChanged;
        }

        private void UpdateFrame()
        {
            Frame++;
            // Runtime components may change without Editor object-change notifications.
            if (Application.isPlaying) Content++;
        }

        private void Changed() { Content++; }
        private void ObjectsChanged(ref ObjectChangeEventStream stream) { Changed(); }
        private void SceneOpened(Scene scene, OpenSceneMode mode) { Changed(); }
        private void SceneClosed(Scene scene) { Changed(); }
        private void PlayChanged(PlayModeStateChange state) { Changed(); }
    }
}
