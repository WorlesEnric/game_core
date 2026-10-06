#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using Hollowmere.Game;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31dLogWriterTests
    {
        [Test]
        public void P31d_LOG_AutomaticFlushDoesNotWaitForStorage()
        {
            string path = Path.Combine(Path.GetTempPath(), "p31d-log-" + Guid.NewGuid().ToString("N") + ".csv");
            var host = new GameObject("frame log");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            string written = string.Empty;
            int appendThread = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            FrameLogRecorder log = host.AddComponent<FrameLogRecorder>();
            try
            {
                log.Begin(path, "test", new FrameLogWriter(text =>
                {
                    appendThread = Thread.CurrentThread.ManagedThreadId;
                    entered.Set();
                    if (!release.Wait(TimeSpan.FromSeconds(5))) throw new IOException("automatic flush waited for storage");
                    written += text;
                }));
                MethodInfo update = typeof(FrameLogRecorder).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.DoesNotThrow(() =>
                {
                    for (int i = 0; i < FrameLogRecorder.FlushEveryFrames; i++) update.Invoke(log, null);
                }, "the frame callback must return while storage is blocked");
                Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(written, Is.Empty);
                release.Set();
                log.Flush();
                Assert.That(appendThread, Is.Not.EqualTo(mainThread));
                Assert.That(written.Split('\n').Length - 1, Is.EqualTo(FrameLogRecorder.FlushEveryFrames));
            }
            finally
            {
                release.Set();
                Object.DestroyImmediate(host);
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Test]
        public void P31d_LOG_WritesStayOrderedAndFailuresSurfaceAtDrain()
        {
            var rows = new List<string>();
            var writer = new FrameLogWriter(text => rows.Add(text));
            writer.Append("first");
            writer.Append("second");
            writer.Drain();
            Assert.That(rows, Is.EqualTo(new[] { "first", "second" }));
            var failing = new FrameLogWriter(_ => throw new IOException("storage failed"));
            failing.Append("not lost silently");
            Assert.Throws<IOException>(() => failing.Drain());
        }
    }
}
