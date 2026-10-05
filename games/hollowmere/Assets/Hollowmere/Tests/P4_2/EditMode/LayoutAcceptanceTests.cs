#nullable enable
using System;
using System.Collections;
using System.IO;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class LayoutAcceptanceTests
    {
        [UnityTest]
        [Explicit("Physical 1280x720 Studio workspace qualification")]
        public IEnumerator R2_30_1280By720WorkspaceContainsEveryStudioPanel()
        {
            if (!ViewportRenderer.CanRender) Assert.Ignore("P4.2 physical layout requires a graphical Editor; run the layout qualification command.");
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("evidence required");
            bool first = StudioUiSettings.FirstRunDone;
            StudioUiSettings.FirstRunDone = true;
            Rect bounds = new Rect(20, 40, 1280, 720);
            StudioMenu.OpenStudio(bounds, true);
            try
            {
                DateTime settle = DateTime.UtcNow.AddSeconds(1);
                while (DateTime.UtcNow < settle) yield return null;
                EditorWindow[] windows = { EditorWindow.GetWindow<StudioViewportWindow>(), EditorWindow.GetWindow<StudioContextWindow>(),
                    EditorWindow.GetWindow<StudioTasksWindow>(), EditorWindow.GetWindow<StudioCandidatesWindow>(), EditorWindow.GetWindow<StudioHistoryWindow>() };
                var rows = new JArray();
                bool contained = true;
                foreach (EditorWindow window in windows)
                {
                    Rect r = window.position;
                    bool inside = r.xMin >= bounds.xMin && r.yMin >= bounds.yMin && r.xMax <= bounds.xMax && r.yMax <= bounds.yMax;
                    contained &= inside;
                    rows.Add(new JObject { ["window"] = window.GetType().Name, ["x"] = r.x, ["y"] = r.y,
                        ["width"] = r.width, ["height"] = r.height, ["inside1280x720"] = inside });
                }
                File.WriteAllText(Path.Combine(output, "layout-1280x720.json"), rows.ToString());
                string? problem = UnityWindowCapture.CaptureStudio(Path.Combine(output, "layout-1280x720.png"), false);
                Assert.That(problem, Is.Null);
                Assert.That(contained, Is.True, "All five actual window rectangles must fit the requested 1280x720 workspace.");
            }
            finally
            {
                StudioUiSettings.FirstRunDone = first;
                EditorWindow.GetWindow<StudioViewportWindow>().Close();
                EditorWindow.GetWindow<StudioContextWindow>().Close();
                EditorWindow.GetWindow<StudioTasksWindow>().Close();
                EditorWindow.GetWindow<StudioCandidatesWindow>().Close();
                EditorWindow.GetWindow<StudioHistoryWindow>().Close();
            }
        }
    }
}
