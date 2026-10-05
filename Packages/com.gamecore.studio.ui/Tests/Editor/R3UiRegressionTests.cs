#nullable enable
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI.Tests
{
    public sealed class R3UiRegressionTests
    {
        [Test]
        public void D16_PromptListsAdvertisedWorkersAndPreservesSelection()
        {
            using UiTestBed bed = new UiTestBed();
            PromptBar bar = new PromptBar(bed.Context);
            DropdownField? selector = bar.Q<DropdownField>("prompt-worker");
            Assert.That(selector, Is.Not.Null, "P3.2 D16: worker selector missing");
            Assert.That(selector!.choices, Is.EqualTo(new[] { "gc-designer", "gc-mechanic" }));
            bar.SelectedWorker = "gc-mechanic";
            PromptBar reopened = new PromptBar(bed.Context);
            Assert.That(reopened.Q<DropdownField>("prompt-worker").value, Is.EqualTo("gc-mechanic"));
            bar.Text = "Add a pressure plate mechanism";
            bar.SubmitAsync().GetAwaiter().GetResult();
            Assert.That(bed.Gateway.Submitted[0].Mode, Is.EqualTo("gc-mechanic"));
            bar.SelectedWorker = "gc-designer";
        }

        [UnityTest]
        public IEnumerator D12_DeferredPlacementRunsOnceWithoutOpeningGraphics()
        {
            System.Type? scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout");
            Assert.That(scheduler, Is.Not.Null, "D12: OpenStudio has no deferred placement scheduler");
            object owner = scheduler!.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo place = scheduler.GetMethod("Place")!;
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            Rect expected = StudioMenu.Layout(new Rect(20, 40, 1600, 1000))[0];
            try
            {
                place.Invoke(owner, new object[] { window, expected });
                place.Invoke(owner, new object[] { window, expected });
                window.position = new Rect(100, 100, 800, 700);
                yield return null;
                yield return null;
                Assert.That(window.position, Is.EqualTo(expected));
                Rect user = new Rect(60, 60, 900, 700);
                window.position = user;
                yield return null;
                Assert.That(window.position, Is.EqualTo(user));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce()
        {
            if (!ViewportRenderer.CanRender) Assert.Ignore("D12 physical window-manager placement requires a graphical Editor; the separate scheduler regression runs headless.");
            bool wizard = StudioUiSettings.FirstRunDone;
            StudioUiSettings.FirstRunDone = true;
            Rect area = new Rect(20, 40, 1600, 1000);
            try
            {
                StudioMenu.OpenStudio(area, true);
                StudioViewportWindow viewport = EditorWindow.GetWindow<StudioViewportWindow>();
                viewport.position = new Rect(100, 100, 800, 700); // simulated late WM placement from :1
                yield return null;
                yield return null;
                Assert.That(viewport.position, Is.EqualTo(StudioMenu.Layout(area)[0]));
                Rect user = new Rect(60, 60, 900, 700);
                viewport.position = user;
                yield return null;
                Assert.That(viewport.position, Is.EqualTo(user), "deferred layout must unsubscribe after one update");
            }
            finally
            {
                EditorWindow.GetWindow<StudioViewportWindow>().Close();
                EditorWindow.GetWindow<StudioContextWindow>().Close();
                EditorWindow.GetWindow<StudioTasksWindow>().Close();
                EditorWindow.GetWindow<StudioCandidatesWindow>().Close();
                EditorWindow.GetWindow<StudioHistoryWindow>().Close();
                StudioUiSettings.FirstRunDone = wizard;
            }
        }
    }
}
