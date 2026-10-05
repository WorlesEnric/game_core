#nullable enable
using System.Collections;
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
            selector.value = "gc-mechanic";
            PromptBar reopened = new PromptBar(bed.Context);
            Assert.That(reopened.Q<DropdownField>("prompt-worker").value, Is.EqualTo("gc-mechanic"));
            bar.Text = "Add a pressure plate mechanism";
            bar.SubmitAsync().GetAwaiter().GetResult();
            Assert.That(bed.Gateway.Submitted[0].Mode, Is.EqualTo("gc-mechanic"));
            selector.value = "gc-designer";
        }

        [UnityTest]
        public IEnumerator D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce()
        {
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
