// GameCore.Studio.UI - GameCore/Studio/Open Studio: opens the viewport, context, tasks, candidates and history windows
// and tiles them over the main editor window (viewport left, context right, tasks/candidates/history below). Windows
// already docked by the user keep their place; only windows this call creates are positioned.
#nullable enable
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

            area = EnforceMinimum(area);
            float top = area.y;
            float height = area.height;
            float viewportWidth = Mathf.Max(640f, area.width * 0.64f);
            float sideWidth = Mathf.Max(300f, area.width - viewportWidth - 12f);
            float viewportHeight = Mathf.Max(480f, height * 0.68f);
            float bottomHeight = height - viewportHeight - 8f;
            float bottomWidth = viewportWidth / 3f;

            StudioViewportWindow viewport = StudioViewportWindow.Open();
            if (!viewportExisted)
            {
                viewport.position = new Rect(area.x + 4f, top, viewportWidth, viewportHeight);
            }

            if (!contextExisted)
            {
                StudioContextWindow.Open();
                EditorWindow.GetWindow<StudioContextWindow>().position = new Rect(area.x + viewportWidth + 8f, top, sideWidth, height);
            }

            if (!tasksExisted)
            {
                StudioTasksWindow.Open();
                EditorWindow.GetWindow<StudioTasksWindow>().position = new Rect(area.x + 4f, top + viewportHeight + 8f, bottomWidth - 4f, bottomHeight);
            }

            if (!candidatesExisted)
            {
                StudioCandidatesWindow.Open(null).position = new Rect(area.x + bottomWidth + 4f, top + viewportHeight + 8f, bottomWidth - 4f, bottomHeight);
            }

            if (!historyExisted)
            {
                StudioHistoryWindow.Open();
                EditorWindow.GetWindow<StudioHistoryWindow>().position = new Rect(area.x + (2f * bottomWidth) + 4f, top + viewportHeight + 8f, bottomWidth - 4f, bottomHeight);
            }

            viewport.Focus();
            if (!StudioUiSettings.FirstRunDone)
            {
                FirstRunWizardWindow.Open();
            }
        }

        public static Rect EnforceMinimum(Rect area) => new Rect(area.x, area.y, Mathf.Max(1280f, area.width), Mathf.Max(720f, area.height));

        private static bool HasOpenInstances<T>()
            where T : EditorWindow
        {
            return EditorWindow.HasOpenInstances<T>();
        }
    }
}
