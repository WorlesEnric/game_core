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
