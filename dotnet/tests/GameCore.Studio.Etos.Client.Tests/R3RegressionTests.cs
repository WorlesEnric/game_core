#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class R3RegressionTests
    {
        [Test]
        public async Task D13_BufferedFailureReplaysWithoutAcknowledgingLaterEvents()
        {
            using FakeSetup setup = new FakeSetup();
            MemoryCursorStore cursors = new MemoryCursorStore();
            using EventStream stream = new EventStream(setup.Client, cursors, new BackoffPolicy(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100)));
            stream.MaxPendingEvents = 16;
            int fail = 1;
            ConcurrentQueue<long> received = new ConcurrentQueue<long>();
            stream.HandleAsync = (frame, token) => frame.Cursor == 2 && Volatile.Read(ref fail) == 1
                ? Task.FromException(new InvalidOperationException("fixture handler failure")) : Task.CompletedTask;
            stream.Received += frame => received.Enqueue(frame.Cursor);
            for (int n = 0; n < 3; n++) setup.Fake.Emit("request", "cs_failure", new JObject());
            stream.Start();
            await Wait.Until(() => stream.LastError?.Code == "event_handler_failed", TimeSpan.FromSeconds(10), "failed buffered handler");
            Assert.That(cursors.Load(), Is.EqualTo(1));
            Volatile.Write(ref fail, 0);
            await Wait.Until(() => cursors.Load() == 3, TimeSpan.FromSeconds(10), "replayed buffered events");
            Assert.That(received.ToArray(), Is.EqualTo(new long[] { 1, 2, 3 }));
        }

        [Test]
        public async Task D13_ReadAheadIsBoundedAndNeverAcknowledgesQueuedWork()
        {
            using FakeSetup setup = new FakeSetup();
            MemoryCursorStore cursors = new MemoryCursorStore();
            using EventStream stream = new EventStream(setup.Client, cursors);
            typeof(EventStream).GetProperty("MaxPendingEvents")?.SetValue(stream, 16);
            ConcurrentQueue<TaskCompletionSource<bool>> handled = new ConcurrentQueue<TaskCompletionSource<bool>>();
            stream.HandleAsync = (frame, token) =>
            {
                var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                token.Register(() => done.TrySetCanceled());
                handled.Enqueue(done);
                return done.Task;
            };
            for (int n = 0; n < 40; n++) setup.Fake.Emit("task_progress", "cs_burst", new JObject());
            stream.Start();
            await Wait.Until(() => handled.Count > 0, TimeSpan.FromSeconds(10), "first event queued");
            await Task.Delay(500);
            Assert.That(handled.Count, Is.EqualTo(16), "read-ahead batches FIFO main-thread work without an update per frame");
            Assert.That(cursors.Load(), Is.Zero, "queueing is not acknowledgment");
            Assert.That(handled.TryDequeue(out TaskCompletionSource<bool>? first), Is.True);
            first!.SetResult(true);
            await Wait.Until(() => cursors.Load() == 1, TimeSpan.FromSeconds(5), "first acknowledgment");
            await stream.StopAsync();
            Assert.That(cursors.Load(), Is.EqualTo(1), "reload leaves all queued work replayable");
        }

        [Test]
        public async Task D22_StopDrainsDelayedFinalAfterAllAudioFrames()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.VoiceFinalDelay = TimeSpan.FromMilliseconds(250);
            setup.Fake.VoiceTranscript.Clear();
            setup.Fake.VoiceTranscript.Add("Move the well");
            setup.Fake.VoiceTranscript.Add("Move the well one metre to the east.");
            using VoiceChannel voice = new VoiceChannel(setup.Client);
            List<VoiceTranscript> updates = new List<VoiceTranscript>();
            voice.Transcript += t => updates.Add(t);
            await voice.ConnectAsync();
            await voice.SendPcmAsync(new byte[60000]);
            string reason = await voice.StopAsync(TimeSpan.FromSeconds(3));
            Assert.That(reason, Is.EqualTo("stopped"));
            Assert.That(updates[0].Final, Is.False);
            Assert.That(updates[updates.Count - 1].Text, Is.EqualTo("Move the well one metre to the east."));
            Assert.That(updates[updates.Count - 1].Final, Is.True);
            Assert.That(setup.Fake.VoiceSeqs, Is.EqualTo(new long[] { 0, 1, 2 }));
            Assert.That(setup.Fake.VoiceFrameBytes, Is.EqualTo(new[] { 24576, 24576, 10848 }));
        }

        [Test]
        public async Task D22_StopTimeoutPreservesPreciseReasonInsteadOfClientClosed()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.VoiceNeverCloses = true;
            using VoiceChannel voice = new VoiceChannel(setup.Client);
            string? closed = null;
            voice.Closed += reason => closed = reason;
            await voice.ConnectAsync();
            string result = await voice.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(result, Is.EqualTo("stop timed out"));
            Assert.That(closed, Is.EqualTo(result));
        }
    }
}
