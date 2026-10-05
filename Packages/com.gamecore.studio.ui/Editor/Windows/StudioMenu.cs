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
            StudioAgentGateways.EnsureSessionStarted();
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
            float side = Mathf.Floor(Mathf.Max(420f, area.width * .34f));
            float left = area.width - side - gap;
            float tasks = Mathf.Floor(Mathf.Max(160f, area.height * .22f));
            float extra = area.height - 716f;
            float context = Mathf.Floor(240f + extra * .34f);
            float candidates = Mathf.Floor(260f + extra * .33f);
            float right = area.x + left + gap;
            return new[]
            {
                new Rect(area.x, area.y, left, area.height - tasks - gap),
                new Rect(right, area.y, side, context),
                new Rect(area.x, area.yMax - tasks, left, tasks),
                new Rect(right, area.y + context + gap, side, candidates),
                new Rect(right, area.y + context + candidates + 2 * gap, side, area.height - context - candidates - 2 * gap),
            };
        }

        public static Rect EnforceMinimum(Rect area) => new Rect(area.x, area.y, Mathf.Max(1280f, area.width), Mathf.Max(720f, area.height));

        private static bool HasOpenInstances<T>()
            where T : EditorWindow
        {
            return EditorWindow.HasOpenInstances<T>();
        }
    }

    // Show and X11 ConfigureNotify can race across several updates. Stop once placement is stable,
    // or after five seconds / 120 placement attempts, so a constrained WM cannot keep fighting the creator.
    internal sealed class StudioDeferredLayout : ScriptableSingleton<StudioDeferredLayout>
    {
        public const double TimeoutSeconds = 5;
        public const double StableSeconds = 0.5;
        private readonly Dictionary<EditorWindow, Placement> _pending = new Dictionary<EditorWindow, Placement>();

        private sealed class Placement
        {
            public Rect Rect;
            public double Started;
            public double StableSince;
            public int StableUpdates;
            public int Attempts;
            public bool StartedUpdating;
            public double LastWriteAt = double.NegativeInfinity;
        }

        public int PendingCount => _pending.Count;

        public void Place(EditorWindow window, Rect rect)
        {
            double now = EditorApplication.timeSinceStartup;
            window.position = rect;
            _pending[window] = new Placement { Rect = rect, StableSince = now, Attempts = 1 };
            EditorApplication.update -= Apply;
            EditorApplication.update += Apply;
        }

        private void Apply() => Advance(EditorApplication.timeSinceStartup);

        // Explicit clock keeps deadline/stability regressions deterministic without sleeping an Editor.
        public void Advance(double now)
        {
            var completed = new List<EditorWindow>();
            foreach (KeyValuePair<EditorWindow, Placement> item in _pending)
            {
                EditorWindow window = item.Key;
                Placement placement = item.Value;
                if (window == null) { completed.Add(window); continue; }
                if (!placement.StartedUpdating)
                {
                    // Opening the other Studio windows can block in GTK. The retry deadline starts
                    // when the first scheduled update can actually work, not while Show is blocking.
                    placement.StartedUpdating = true;
                    placement.Started = now;
                    placement.StableSince = now;
                }
                if (window.position == placement.Rect)
                {
                    placement.StableUpdates++;
                    if (placement.StableUpdates >= 3 && now - placement.StableSince >= StableSeconds)
                    {
                        completed.Add(window);
                        continue;
                    }
                }
                else
                {
                    placement.StableUpdates = 0;
                    placement.StableSince = now;
                }

                if (now - placement.Started >= TimeoutSeconds || placement.Attempts >= 120)
                {
                    Debug.LogWarning("GameCore Studio [layout_timeout]: " + window.GetType().Name
                        + " requested " + placement.Rect + ", observed " + window.position
                        + "; placement did not settle within 5 seconds / 120 placement attempts. Move the window manually or reopen Studio.");
                    completed.Add(window);
                }
                else if (window.position != placement.Rect)
                {
                    // Give ConfigureNotify time to acknowledge the last request before issuing
                    // a correction. EditorWindow.position initially returns its optimistic cache.
                    if (now - placement.LastWriteAt < 0.25) continue;
                    placement.LastWriteAt = now;
                    Rect command = placement.Rect;
                    placement.Attempts++;
                    window.position = command;
                }
            }
            foreach (EditorWindow window in completed) _pending.Remove(window);
            if (_pending.Count == 0) EditorApplication.update -= Apply;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Apply;
            _pending.Clear();
        }
    }
}
