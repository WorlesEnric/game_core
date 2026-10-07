#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Hollowmere.Game;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.R10_B.Tests
{
    public sealed class FrameStallTests
    {
        [Test]
        public void R10_B_LogDoesNotEnterSynchronousUnitySinkUntilExplicitFlush()
        {
            string path = Path.Combine(Path.GetTempPath(), "r10-b-" + Guid.NewGuid().ToString("N") + ".csv");
            var host = new GameObject("frame recorder regression");
            var sink = new CapturingHandler();
            ILogHandler original = Debug.unityLogger.logHandler;
            try
            {
                var session = host.AddComponent<HollowmerePersistentSession>();
                typeof(HollowmerePersistentSession).GetMethod("Begin", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(session, new object[] { HollowmereCommandLine.Parse(new[] { "-frameLog", path }) });
                Debug.unityLogger.logHandler = sink;
                // Any synchronous log handler may block on storage. No handler invocation is permitted here.
                session.Log("[autoplay] first frame=7096");
                session.Log("[autoplay] second frame=109373");
                Assert.That(sink.Messages, Is.Empty, "diagnostic IO must not execute inside a measured frame");
                session.FrameLog!.Flush();
                string expected = "[autoplay] first frame=7096\n[autoplay] second frame=109373\n";
                Assert.That(File.ReadAllText(path + ".events.log"), Is.EqualTo(expected));
                Assert.That(sink.Messages, Is.EqualTo(new[] { expected }));
                session.FrameLog.Flush();
                Assert.That(sink.Messages.Count, Is.EqualTo(1), "quit plus destroy must not duplicate evidence");
            }
            finally
            {
                Debug.unityLogger.logHandler = original;
                UnityEngine.Object.DestroyImmediate(host);
                foreach (string suffix in new[] { "", ".events.log", ".stalls.csv" })
                    if (File.Exists(path + suffix)) File.Delete(path + suffix);
            }
        }

        [Test]
        public void R10_B_AttributionUsesWorkFrameAndSpecificNestedOwner()
        {
            var attribution = new FrameAttribution();
            attribution.Record(7096, FrameSubsystem.Autoplay, 380);
            attribution.Record(7096, FrameSubsystem.AutoplayLog, 374);
            attribution.Record(7097, FrameSubsystem.Presentation, 1);
            Assert.That(attribution.Describe(7096), Is.EqualTo("autoplay.log,380.000,374.000,0.000,0.000"));
            Assert.That(attribution.Describe(7097), Is.EqualTo("unattributed,0.000,0.000,1.000,0.000"));
            attribution.Record(7099, FrameSubsystem.Autoplay, 2);
            Assert.That(attribution.Describe(7096), Is.EqualTo("unattributed,0.000,0.000,0.000,0.000"), "ring reuse cannot invent stale attribution");
        }

        [Test]
        public void R10_B_SmallScopesDoNotInventAnOwnerForAnExternalStall()
        {
            var attribution = new FrameAttribution();
            attribution.Record(12, FrameSubsystem.Autoplay, 70);
            attribution.Record(12, FrameSubsystem.AutoplayLog, 60);
            Assert.That(attribution.Describe(12), Does.StartWith("unattributed,"), "nested timings are not additive");
        }

        private sealed class CapturingHandler : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args) =>
                Messages.Add(string.Format(format, args));
            public void LogException(Exception exception, UnityEngine.Object context) => throw exception;
        }
    }
}
