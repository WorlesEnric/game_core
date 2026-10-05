#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Hollowmere.P2_2;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Hollowmere.R3_B
{
    public sealed class WorkflowRegressionTests
    {
        // Retained voice2/move-transcript.json: empty final after release, no request sent.
        [UnityTest]
        public IEnumerator D19_D22_TwoTakesDrainDelayedFinalsAndDisplayPartialsWithoutSubmitting()
        {
            using GatewayHarness h = GatewayHarness.WithFake(f =>
            {
                f.VoiceFinalDelay = TimeSpan.FromMilliseconds(150);
                f.VoiceTranscript.Clear();
                f.VoiceTranscript.Add("Move the well");
                f.VoiceTranscript.Add("Move the well one metre to the east.");
            });
            using StudioUiContext ui = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            PromptBar bar = new PromptBar(ui);
            using EtosVoiceSession voice = new EtosVoiceSession(h.Client, h.Queue, new TakeSource(), () => h.Gateway.Status, h.Log);
            voice.Transcript += bar.OnTranscript;
            for (int take = 0; take < 2; take++)
            {
                bar.Text = string.Empty;
                Task start = voice.StartAsync();
                yield return h.Await(start);
                start.GetAwaiter().GetResult();
                Assert.That(voice.IsCapturing, Is.True, "each take opens a new channel");
                voice.Tick();
                yield return h.Until(() => bar.PartialTranscript.Contains("Move the well"), 5, "partial transcript");
                Assert.That(bar.Text, Is.Empty, "partial revisions never enter the prompt");
                Task stop = voice.StopAsync();
                yield return h.Await(stop);
                stop.GetAwaiter().GetResult();
                Assert.That(bar.Text, Is.EqualTo("Move the well one metre to the east."));
                Assert.That(voice.CloseReason, Is.EqualTo("stopped"));
            }
            Assert.That(h.Fake!.VoiceStops, Is.EqualTo(2));
            Assert.That(h.Fake.VoiceSeqs, Is.EqualTo(new long[] { 0, 1, 2, 0, 1, 2 }));
            Assert.That(h.Fake.VoiceFrameBytes, Is.EqualTo(new[] { 4800, 4800, 400, 4800, 4800, 400 }));
            Assert.That(h.Gateway.Requests, Is.Empty);
            Assert.That(ui.Tasks.Rows, Is.Empty);
        }

        // Retained batch timeline: request candidate/done trails updatedAt by seconds.
        [UnityTest]
        public IEnumerator D13_EventBurstReachesTrayWithinOneSecond()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Gateway.Options.AutoImport = false;
            using StudioUiContext ui = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            h.Gateway.Start();
            yield return h.Until(() => h.Fake!.EventConnections == 1, 10, "event stream");
            List<double> latency = new List<double>();
            for (int transition = 0; transition < 20; transition++)
            {
                PreparedRequest request = ui.Requests.Build("Move the well one metre to the east.", h.EmptySelection());
                string id = request.ChangeSetId;
                ui.Tasks.AddSubmitted(request, "empty selection");
                // Queue a busy worker's progress ahead of a terminal transition, with a 50 ms Editor cadence.
                for (int n = 0; n < 30; n++) h.Fake!.Emit("task_progress", id, new JObject { ["status"] = "working" });
                long at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                h.Fake!.Emit("request", id, new JObject { ["requestId"] = id, ["changeSetId"] = id, ["state"] = "candidate", ["taskStatus"] = "done", ["hasCandidate"] = false, ["seq"] = 10000 + transition, ["updatedAt"] = at });
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (ui.Tasks.Find(id)?.etosStatus != "done" && DateTime.UtcNow < deadline)
                {
                    yield return new UnityEngine.WaitForSecondsRealtime(.05f);
                    h.Queue.Pump();
                    ui.Tick();
                }
                double ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - at;
                latency.Add(ms);
                Assert.That(ui.Tasks.Find(id)?.etosStatus, Is.EqualTo("done"));
            }
            latency.Sort();
            TestContext.WriteLine("D13 tray latency ms n=20 p95=" + latency[18] + " max=" + latency[19]);
            Assert.That(latency[18], Is.LessThanOrEqualTo(1000));
        }

        private sealed class TakeSource : IPcmSource
        {
            private bool _read;
            public string Name => "retained voice take frame pattern";
            public bool Finished => _read;
            public void Start() => _read = false;
            public void Stop() { }
            public byte[] Read() { if (_read) return Array.Empty<byte>(); _read = true; return new byte[10000]; }
            public void Dispose() { }
        }
    }
}
