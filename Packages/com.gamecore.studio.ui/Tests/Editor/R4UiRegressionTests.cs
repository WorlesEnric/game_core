#nullable enable
using System.Collections;
using System.Reflection;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI.Tests
{
    public sealed class R4UiRegressionTests
    {
        [UnityTest]
        public IEnumerator P42_UI_02_LatePlacementIsRetriedUntilStable()
        {
            System.Type scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout")!;
            object owner = scheduler.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            Rect expected = new Rect(20, 40, 1048, 772);
            try
            {
                scheduler.GetMethod("Place")!.Invoke(owner, new object[] { window, expected });
                for (int i = 0; i < 4; i++)
                {
                    yield return null;
                    window.position = new Rect(100, 100, 800, 700);
                }
                double deadline = EditorApplication.timeSinceStartup + 6;
                while (window.position != expected && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(window.position, Is.EqualTo(expected), "P42-UI-02: late placement must be retried");
                // Allow the documented stability interval to elapse, then prove user placement is no longer overwritten.
                double settled = EditorApplication.timeSinceStartup + 1;
                while (EditorApplication.timeSinceStartup < settled) yield return null;
                Rect user = new Rect(60, 60, 900, 700);
                window.position = user;
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(window.position, Is.EqualTo(user));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [TestCase("not_configured", "Not paired: configure the Studio companion.")]
        [TestCase("key_file_missing", "App key file is missing; pair the Editor again.")]
        public void P42_STARTUP_01_TrayShowsPreciseSessionProblem(string code, string message)
        {
            using UiTestBed bed = new UiTestBed();
            bed.Gateway.Status = new ProviderStatus(ProviderState.NotConfigured, ProviderState.NotConfigured,
                ProviderState.NotConfigured, ProviderState.NotConfigured, ProviderState.NotConfigured,
                false, false, null, new Diagnostic(code, message));
            TaskTrayView tray = new TaskTrayView(bed.Context);
            Label? label = tray.Q<Label>("tasks-connection");
            Assert.That(label, Is.Not.Null, "P42-STARTUP-01: missing tray connection diagnostic");
            Assert.That(label!.text, Does.Contain(code).And.Contain(message));
            bed.Gateway.Status = new ProviderStatus(ProviderState.Live, ProviderState.Live, ProviderState.Live,
                ProviderState.NotConfigured, ProviderState.Live, true, true, "test");
            tray.Rebuild();
            Assert.That(label.text, Does.Not.Contain(message));
        }
    }
}
