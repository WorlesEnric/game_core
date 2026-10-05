#nullable enable
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using GameCore.Studio.Edit;
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
        [Test]
        public void P42_UI_02_LayoutUsesIntegralNativeWindowRects()
        {
            Rect area = new Rect(20, 100, 1600, 900);
            Rect[] rects = StudioMenu.Layout(area);
            foreach (Rect rect in rects)
            {
                Assert.That(rect.x, Is.EqualTo(Mathf.Floor(rect.x)));
                Assert.That(rect.y, Is.EqualTo(Mathf.Floor(rect.y)));
                Assert.That(rect.width, Is.EqualTo(Mathf.Floor(rect.width)));
                Assert.That(rect.height, Is.EqualTo(Mathf.Floor(rect.height)));
            }
            Assert.That(rects[0], Is.EqualTo(new Rect(20, 100, 1048, 694)));
            Assert.That(rects[2].yMax, Is.EqualTo(area.yMax));
            Assert.That(rects[4].yMax, Is.EqualTo(area.yMax), "pixel rounding must retain the entire tiled area");
        }

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

        [UnityTest]
        public IEnumerator P42_UI_02_GraphicalClampedOriginReportsTimeout()
        {
            if (!ViewportRenderer.CanRender) Assert.Ignore("P42-UI-02: native clamping requires the graphical :1 lane.");
            using UiTestBed bed = new UiTestBed();
            bool wizard = StudioUiSettings.FirstRunDone;
            StudioUiSettings.FirstRunDone = true;
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            System.Type scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout")!;
            object owner = scheduler.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            PropertyInfo pending = scheduler.GetProperty("PendingCount")!;
            Rect requested = new Rect(20, 40, 1048, 772);
            try
            {
                window.UseContext(bed.Context); window.Show(); window.EnsureGui();
                yield return null;
                LogAssert.Expect(LogType.Warning, new Regex(@"\[layout_timeout\]: StudioViewportWindow requested \(x:20\.00, y:40\.00, width:1048\.00, height:772\.00\), observed"));
                scheduler.GetMethod("Place")!.Invoke(owner, new object[] { window, requested });
                double deadline = EditorApplication.timeSinceStartup + 6;
                while ((int)pending.GetValue(owner)! != 0 && EditorApplication.timeSinceStartup < deadline) yield return null;
                Assert.That(pending.GetValue(owner), Is.EqualTo(0), "a native clamp must terminate retries");
                Assert.That(window.position.x, Is.EqualTo(requested.x));
                Assert.That(window.position.size, Is.EqualTo(requested.size));
                Assert.That(window.position.y, Is.GreaterThan(requested.y), "retained :1 case is clamped above the desktop panel");
                Debug.Log("[R4-B clamped origin] requested=" + requested + " observed=" + window.position + " code=layout_timeout");
            }
            finally { window.Close(); StudioUiSettings.FirstRunDone = wizard; }
        }

        [Test]
        public void P42_UI_02_ClampedReadbackRetriesTheRequestedRectWithoutDrifting()
        {
            System.Type scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout")!;
            object owner = scheduler.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo advance = scheduler.GetMethod("Advance")!;
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            Rect expected = new Rect(20, 40, 1048, 772);
            Rect displaced = new Rect(20, 69, 1048, 772);
            double now = EditorApplication.timeSinceStartup;
            try
            {
                scheduler.GetMethod("Place")!.Invoke(owner, new object[] { window, expected });
                window.position = displaced;
                advance.Invoke(owner, new object[] { now }); // unadjusted probe
                window.position = displaced;
                advance.Invoke(owner, new object[] { now + 0.3 });
                Assert.That(window.position, Is.EqualTo(expected), "a clamped readback must not change the requested origin");
                window.position = displaced; // repeated clamping must not accumulate offset
                advance.Invoke(owner, new object[] { now + 0.6 });
                Assert.That(window.position, Is.EqualTo(expected), "retry with an unadjusted probe");
                window.position = displaced;
                advance.Invoke(owner, new object[] { now + 0.9 });
                Assert.That(window.position, Is.EqualTo(expected));
                window.position = expected; // the WM acknowledges the request
                advance.Invoke(owner, new object[] { now + 1.0 });
                advance.Invoke(owner, new object[] { now + 1.5 });
                advance.Invoke(owner, new object[] { now + 1.6 });
                Assert.That(scheduler.GetProperty("PendingCount")!.GetValue(owner), Is.EqualTo(0));
                Assert.That(window.position, Is.EqualTo(expected));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void P42_UI_02_StableObservationsDoNotSpendRetryBudget()
        {
            System.Type scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout")!;
            object owner = scheduler.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo advance = scheduler.GetMethod("Advance")!;
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            double now = EditorApplication.timeSinceStartup;
            try
            {
                scheduler.GetMethod("Place")!.Invoke(owner, new object[] { window, new Rect(20, 40, 1048, 772) });
                for (int i = 0; i < 130; i++) advance.Invoke(owner, new object[] { now });
                Assert.That(scheduler.GetProperty("PendingCount")!.GetValue(owner), Is.EqualTo(1), "wait for stability time without exhausting placement attempts");
                advance.Invoke(owner, new object[] { now + 0.6 });
                Assert.That(scheduler.GetProperty("PendingCount")!.GetValue(owner), Is.EqualTo(0));
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void P42_UI_02_TimeoutDiagnosesAndUnsubscribes()
        {
            System.Type scheduler = typeof(StudioMenu).Assembly.GetType("GameCore.Studio.UI.StudioDeferredLayout")!;
            object owner = scheduler.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo? advance = scheduler.GetMethod("Advance");
            Assert.That(advance, Is.Not.Null, "P42-UI-02: no bounded settlement loop");
            StudioViewportWindow window = ScriptableObject.CreateInstance<StudioViewportWindow>();
            try
            {
                scheduler.GetMethod("Place")!.Invoke(owner, new object[] { window, new Rect(20, 40, 1048, 772) });
                advance!.Invoke(owner, new object[] { EditorApplication.timeSinceStartup });
                window.position = new Rect(100, 100, 800, 700);
                LogAssert.Expect(LogType.Warning, new Regex(@"\[layout_timeout\].*requested.*observed"));
                advance!.Invoke(owner, new object[] { EditorApplication.timeSinceStartup + 6 });
                Assert.That(scheduler.GetProperty("PendingCount")!.GetValue(owner), Is.EqualTo(0));
                advance.Invoke(owner, new object[] { EditorApplication.timeSinceStartup + 7 });
                Assert.That(window.position, Is.EqualTo(new Rect(100, 100, 800, 700)), "timeout must release window placement");
            }
            finally { Object.DestroyImmediate(window); }
        }

        // Optional-package contracts exercised without starting ETOS or opening any credential file.
        private sealed class IdempotentSession
        {
            public static bool EnsureStarted() => true;
            public static bool Start() => throw new System.InvalidOperationException("legacy restart must not be used");
        }

        private sealed class LegacySession
        {
            public static bool Start() => true;
        }

        private sealed class RefusedSession
        {
            public static bool Start() => false;
        }

        [Test]
        public void P42_STARTUP_01_OpenUsesOptionalIdempotentSessionAndPreservesGateway()
        {
            using UiTestBed bed = new UiTestBed();
            MethodInfo? start = typeof(StudioAgentGateways).GetMethod("StartSession", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(start, Is.Not.Null, "P42-STARTUP-01: OpenStudio must start the optional session");
            bed.Runtime.Services.AgentGateway = null;
            Assert.That(start!.Invoke(null, new object[] { bed.Runtime, typeof(IdempotentSession) }), Is.True);
            Assert.That(start.Invoke(null, new object[] { bed.Runtime, typeof(LegacySession) }), Is.True);
            Assert.That(start.Invoke(null, new object[] { bed.Runtime, typeof(RefusedSession) }), Is.False);
            bed.Runtime.Services.AgentGateway = bed.Gateway;
            Assert.That(start.Invoke(null, new object[] { bed.Runtime, typeof(RefusedSession) }), Is.True,
                "registered gateway must not be restarted");
            Assert.That(bed.Runtime.Services.AgentGateway, Is.SameAs(bed.Gateway));
        }

        [Test]
        public void P42_STARTUP_01_NullGatewayPreservesOptionalSessionProblem()
        {
            System.Type? session = System.Type.GetType("GameCore.Studio.Etos.EtosStudioSession, GameCore.Studio.Etos", false);
            if (session == null) Assert.Ignore("Optional ETOS package is absent; seam covered by fake-session tests.");
            object owner = session!.BaseType!.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            FieldInfo problem = session.GetField("_problem", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? previous = problem.GetValue(owner);
            try
            {
                problem.SetValue(owner, new Diagnostic("key_file_missing", "Key file is missing; pair the Editor again."));
                Assert.That(NullAgentGateway.Instance.Status.Problem!.Code, Is.EqualTo("key_file_missing"));
                Assert.That(NullAgentGateway.Refusal().Message, Does.Contain("pair the Editor again"));
            }
            finally { problem.SetValue(owner, previous); }
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
