// GameCore.Studio.UI - the first-run guide: what the viewport modes do, how prompting, candidates and history work.
// Pages come from Editor/Resources/GameCoreStudio/FirstRun.uxml; "Don't show again" is a per-user EditorPrefs flag.
#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>The first-run guide window.</summary>
    public sealed class FirstRunWizardWindow : EditorWindow
    {
        public const string UxmlPath = "GameCoreStudio/FirstRun";

        private int _page;

        [MenuItem(StudioWindowIds.FirstRunMenu, false, 130)]
        public static void Open()
        {
            FirstRunWizardWindow window = GetWindow<FirstRunWizardWindow>(true, "GameCore Studio - First run", true);
            window.minSize = new Vector2(520f, 360f);
            window.maxSize = new Vector2(900f, 640f);
            window.ShowUtility();
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            StudioStyles.Apply(root);
            VisualTreeAsset? tree = Resources.Load<VisualTreeAsset>(UxmlPath);
            if (tree != null)
            {
                tree.CloneTree(root);
            }
            else
            {
                root.Add(new Label("GameCore Studio: the viewport has Play, Select and Inspect modes (Tab cycles). Select objects, type an intent, press Ctrl+Enter, review candidates, apply or reject, and undo from History."));
            }

            Button? back = root.Q<Button>("first-run-back");
            Button? next = root.Q<Button>("first-run-next");
            Button? done = root.Q<Button>("first-run-done");
            Toggle? dontShow = root.Q<Toggle>("first-run-dont-show");
            if (back != null)
            {
                back.clicked += () => ShowPage(_page - 1);
            }

            if (next != null)
            {
                next.clicked += () => ShowPage(_page + 1);
            }

            if (dontShow != null)
            {
                dontShow.value = StudioUiSettings.FirstRunDone;
            }

            if (done != null)
            {
                done.clicked += () =>
                {
                    StudioUiSettings.FirstRunDone = dontShow == null || dontShow.value;
                    Close();
                };
            }

            ShowPage(0);
        }

        private void ShowPage(int page)
        {
            UQueryBuilder<VisualElement> pages = rootVisualElement.Query<VisualElement>(className: "gcs-first-run__page");
            int count = 0;
            pages.ForEach(_ => count++);
            if (count == 0)
            {
                return;
            }

            _page = Mathf.Clamp(page, 0, count - 1);
            int index = 0;
            pages.ForEach(element =>
            {
                element.style.display = index == _page ? DisplayStyle.Flex : DisplayStyle.None;
                index++;
            });
            Label? counter = rootVisualElement.Q<Label>("first-run-counter");
            if (counter != null)
            {
                counter.text = (_page + 1) + " / " + count;
            }
        }
    }
}
