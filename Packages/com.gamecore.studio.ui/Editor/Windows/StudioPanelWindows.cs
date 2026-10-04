// GameCore.Studio.UI - the dockable Studio windows around the viewport: Context, Tasks, Candidates and History. Each hosts
// one panel view over the project's UI context (StudioUiSession) and rebuilds it after a domain reload.
#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>GameCore/Studio/Context.</summary>
    public sealed class StudioContextWindow : EditorWindow
    {
        public ContextPanelView? View { get; private set; }

        [MenuItem(StudioWindowIds.ContextMenu, false, 102)]
        public static void Open()
        {
            StudioContextWindow window = GetWindow<StudioContextWindow>(StudioWindowIds.ContextTitle);
            window.minSize = new Vector2(280f, 240f);
            window.Show();
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(StudioWindowIds.ContextTitle);
            rootVisualElement.Clear();
            StudioStyles.Apply(rootVisualElement);
            View = new ContextPanelView(StudioUiSession.Context);
            View.AddToClassList("gcs-grow");
            rootVisualElement.Add(View);
        }
    }

    /// <summary>GameCore/Studio/Tasks (the task tray).</summary>
    public sealed class StudioTasksWindow : EditorWindow
    {
        public TaskTrayView? View { get; private set; }

        [MenuItem(StudioWindowIds.TasksMenu, false, 103)]
        public static void Open()
        {
            StudioTasksWindow window = GetWindow<StudioTasksWindow>(StudioWindowIds.TasksTitle);
            window.minSize = new Vector2(320f, 160f);
            window.Show();
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(StudioWindowIds.TasksTitle);
            rootVisualElement.Clear();
            StudioStyles.Apply(rootVisualElement);
            View = new TaskTrayView(StudioUiSession.Context);
            View.AddToClassList("gcs-grow");
            rootVisualElement.Add(View);
        }
    }

    /// <summary>GameCore/Studio/Candidates (the full candidate panel).</summary>
    public sealed class StudioCandidatesWindow : EditorWindow
    {
        public CandidatePanelView? View { get; private set; }

        [MenuItem(StudioWindowIds.CandidatesMenu, false, 104)]
        public static void OpenMenu() => Open(null);

        /// <summary>Opens the panel, selecting <paramref name="changeSetId"/> when given.</summary>
        public static StudioCandidatesWindow Open(string? changeSetId)
        {
            StudioCandidatesWindow window = GetWindow<StudioCandidatesWindow>(StudioWindowIds.CandidatesTitle);
            window.minSize = new Vector2(420f, 260f);
            window.Show();
            if (changeSetId != null)
            {
                window.View?.Select(changeSetId);
            }

            return window;
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(StudioWindowIds.CandidatesTitle);
            rootVisualElement.Clear();
            StudioStyles.Apply(rootVisualElement);
            View = new CandidatePanelView(StudioUiSession.Context);
            View.AddToClassList("gcs-grow");
            rootVisualElement.Add(View);
        }
    }

    /// <summary>GameCore/Studio/History.</summary>
    public sealed class StudioHistoryWindow : EditorWindow
    {
        public HistoryPanelView? View { get; private set; }

        [MenuItem(StudioWindowIds.HistoryMenu, false, 105)]
        public static void Open()
        {
            StudioHistoryWindow window = GetWindow<StudioHistoryWindow>(StudioWindowIds.HistoryTitle);
            window.minSize = new Vector2(320f, 200f);
            window.Show();
        }

        private void CreateGUI()
        {
            titleContent = new GUIContent(StudioWindowIds.HistoryTitle);
            rootVisualElement.Clear();
            StudioStyles.Apply(rootVisualElement);
            View = new HistoryPanelView(StudioUiSession.Context);
            View.AddToClassList("gcs-grow");
            rootVisualElement.Add(View);
        }
    }
}
