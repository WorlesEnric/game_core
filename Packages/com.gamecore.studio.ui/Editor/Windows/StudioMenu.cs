// GameCore.Studio.UI - GameCore/Studio/Open Studio: opens the viewport, context, tasks, candidates and history windows
// and tiles them over the main editor window (viewport/tasks left, context/candidates/history right). Windows
// already docked by the user keep their place; only windows this call creates are positioned.
#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The Studio layout command.</summary>
    public static class StudioMenu
    {
        [MenuItem(StudioWindowIds.OpenStudioMenu, false, 100)]
        public static void OpenStudio()
        {
            OpenStudio(EditorGUIUtility.GetMainWindowPosition());
        }

        /// <summary>
        /// Opens and tiles the Studio windows inside <paramref name="area"/> (screen rectangle). Windows that were already
        /// open keep their place unless <paramref name="reposition"/> is set.
        /// </summary>
        public static void OpenStudio(Rect area, bool reposition = false)
        {
            bool viewportExisted = !reposition && HasOpenInstances<StudioViewportWindow>();
            bool contextExisted = !reposition && HasOpenInstances<StudioContextWindow>();
            bool tasksExisted = !reposition && HasOpenInstances<StudioTasksWindow>();
            bool candidatesExisted = !reposition && HasOpenInstances<StudioCandidatesWindow>();
            bool historyExisted = !reposition && HasOpenInstances<StudioHistoryWindow>();

            Rect[] layout = Layout(area);

            StudioViewportWindow viewport = StudioViewportWindow.Open();
            if (!viewportExisted)
            {
                StudioDeferredLayout.instance.Place(viewport, layout[0]);
            }

            if (!contextExisted)
            {
                StudioContextWindow.Open();
                StudioDeferredLayout.instance.Place(EditorWindow.GetWindow<StudioContextWindow>(), layout[1]);
            }

            if (!tasksExisted)
            {
                StudioTasksWindow.Open();
                StudioDeferredLayout.instance.Place(EditorWindow.GetWindow<StudioTasksWindow>(), layout[2]);
            }

            if (!candidatesExisted)
            {
                StudioDeferredLayout.instance.Place(StudioCandidatesWindow.Open(null), layout[3]);
            }

            if (!historyExisted)
            {
                StudioHistoryWindow.Open();
                StudioDeferredLayout.instance.Place(EditorWindow.GetWindow<StudioHistoryWindow>(), layout[4]);
            }

            viewport.Focus();
            if (!StudioUiSettings.FirstRunDone)
            {
                FirstRunWizardWindow.Open();
            }
        }

        /// <summary>Viewport/tasks on the left; context/candidates/history on the right. No window is tiled below its minimum.</summary>
        public static Rect[] Layout(Rect requested)
        {
            Rect area = EnforceMinimum(requested);
            const float gap = 8f;
            float side = Mathf.Max(420f, area.width * .34f);
            float left = area.width - side - gap;
            float tasks = Mathf.Max(160f, area.height * .22f);
            float extra = area.height - 716f;
            float context = 240f + extra * .34f;
            float candidates = 260f + extra * .33f;
            float right = area.x + left + gap;
            return new[]
            {
                new Rect(area.x, area.y, left, area.height - tasks - gap),
                new Rect(right, area.y, side, context),
                new Rect(area.x, area.yMax - tasks, left, tasks),
                new Rect(right, area.y + context + gap, side, candidates),
                new Rect(right, area.y + context + candidates + 2 * gap, side, 200f + extra * .33f),
            };
        }

        public static Rect EnforceMinimum(Rect area) => new Rect(area.x, area.y, Mathf.Max(1280f, area.width), Mathf.Max(720f, area.height));

        private static bool HasOpenInstances<T>()
            where T : EditorWindow
        {
            return EditorWindow.HasOpenInstances<T>();
        }
    }

    // The window manager can override placement during Show. Reapply once on the next Editor update.
    internal sealed class StudioDeferredLayout : ScriptableSingleton<StudioDeferredLayout>
    {
        private readonly Dictionary<EditorWindow, Rect> _pending = new Dictionary<EditorWindow, Rect>();

        public void Place(EditorWindow window, Rect rect)
        {
            window.position = rect;
            _pending[window] = rect;
            EditorApplication.update -= Apply;
            EditorApplication.update += Apply;
        }

        private void Apply()
        {
            EditorApplication.update -= Apply;
            foreach (KeyValuePair<EditorWindow, Rect> item in _pending)
                if (item.Key != null && item.Key.position != item.Value) item.Key.position = item.Value;
            _pending.Clear();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Apply;
            _pending.Clear();
        }
    }

}
