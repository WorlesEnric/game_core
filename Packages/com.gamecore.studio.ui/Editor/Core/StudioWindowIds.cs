// GameCore.Studio.UI - window ids and menu paths (stable API for P2.2/P2.3/P3.x and the evidence scripts).
#nullable enable

namespace GameCore.Studio.UI
{
    /// <summary>Menu paths and window titles of the Studio windows.</summary>
    public static class StudioWindowIds
    {
        public const string Root = "GameCore/Studio/";
        public const string OpenStudioMenu = Root + "Open Studio";
        public const string ViewportMenu = Root + "Viewport";
        public const string ContextMenu = Root + "Context";
        public const string TasksMenu = Root + "Tasks";
        public const string CandidatesMenu = Root + "Candidates";
        public const string HistoryMenu = Root + "History";
        public const string SettingsMenu = Root + "Settings";
        public const string FirstRunMenu = Root + "First-Run Guide";

        public const string ViewportTitle = "Studio Viewport";
        public const string ContextTitle = "Studio Context";
        public const string TasksTitle = "Studio Tasks";
        public const string CandidatesTitle = "Studio Candidates";
        public const string HistoryTitle = "Studio History";
    }
}
