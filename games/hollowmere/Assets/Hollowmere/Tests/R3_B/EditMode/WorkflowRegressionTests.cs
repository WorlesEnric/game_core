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
            string retained = Path.GetFullPath(Path.Combine(GatewayHarness.ProjectRoot, "../..", "artifacts/studio/workflows/P3.2/runs/voice2-20261005T112002Z/voice"));
            JObject repro = JObject.Parse(File.ReadAllText(Path.Combine(retained, "move-transcript.json")));
            Assert.That((string?)repro["final"], Is.Empty, "retained D22 symptom");
            byte[] pcm = VoiceFraming.WavToPcm16Mono24k(File.ReadAllBytes(Path.Combine(retained, "move.wav")));
            using EtosVoiceSession voice = new EtosVoiceSession(h.Client, h.Queue, new TakeSource(pcm), () => h.Gateway.Status, h.Log);
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
            int frameCount = (pcm.Length + 4799) / 4800;
            long[] takeSeqs = Enumerable.Range(0, frameCount).Select(n => (long)n).ToArray();
            Assert.That(h.Fake.VoiceSeqs, Is.EqualTo(takeSeqs.Concat(takeSeqs)));
            Assert.That(h.Fake.VoiceFrameBytes.All(n => n > 0 && n <= 24576 && n % 2 == 0), Is.True);
            Assert.That(h.Fake.VoiceFrameBytes.Sum(), Is.EqualTo(2 * pcm.Length));
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
            TaskTrayView tray = new TaskTrayView(ui);
            ui.Tasks.Changed += tray.Rebuild;
            h.Gateway.Start();
            yield return h.Until(() => h.Fake!.EventConnections == 1, 10, "event stream");
            string timeline = Path.GetFullPath(Path.Combine(GatewayHarness.ProjectRoot, "../..", "artifacts/studio/workflows/P3.2/runs/text2-20261005T061054Z/timeline.jsonl"));
            JObject retained = File.ReadLines(timeline).Select(JObject.Parse).First(r => (string?)r["requestId"] == "cs_01M45B93ZY07E94WSV7YZGHJPD" && (string?)r["state"] == "candidate");
            Assert.That((long)retained["visibleLagMs"]!, Is.GreaterThan(1000));
            List<double> latency = new List<double>();
            for (int transition = 0; transition < 20; transition++)
            {
                PreparedRequest request = ui.Requests.Build("Move the well one metre to the east.", h.EmptySelection());
                string id = request.ChangeSetId;
                ui.Tasks.AddSubmitted(request, "empty selection");
                // Queue a busy worker's progress ahead of a terminal transition, with a 50 ms Editor cadence.
                for (int n = 0; n < 30; n++) h.Fake!.Emit("task_progress", id, new JObject { ["status"] = "working" });
                long at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                JObject transitionData = (JObject)retained.DeepClone();
                transitionData["requestId"] = id;
                transitionData["changeSetId"] = id;
                transitionData["hasCandidate"] = false;
                transitionData["seq"] = 10000 + transition;
                transitionData["updatedAt"] = at;
                h.Fake!.Emit("request", id, transitionData);
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
                Assert.That(tray.Q("task-" + id).Q<Label>(className: "gcs-chip").text, Is.EqualTo("candidate"));
            }
            latency.Sort();
            TestContext.WriteLine("D13 tray latency ms n=20 p95=" + latency[18] + " max=" + latency[19]);
            Assert.That(latency[18], Is.LessThanOrEqualTo(1000));
        }

        [UnityTest]
        public IEnumerator D16_HelloWorkersReachPromptAndExactWorkerReachesCompanion()
        {
            using GatewayHarness h = GatewayHarness.WithFake(f => f.Hello["workers"] = new JArray("gc-designer", "gc-mechanic", "gc-specialist"));
            Task status = h.Gateway.RefreshStatusAsync();
            yield return h.Await(status);
            status.GetAwaiter().GetResult();
            using StudioUiContext ui = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            PromptBar bar = new PromptBar(ui);
            DropdownField selector = bar.Q<DropdownField>("prompt-worker");
            Assert.That(selector.choices, Is.EqualTo(new[] { "gc-designer", "gc-mechanic", "gc-specialist" }));
            string previous = StudioWorkerSettings.instance.Worker;
            try
            {
                bar.SelectedWorker = "gc-specialist";
                bar.Text = "Explain the current scene";
                Task<PromptSubmission?> submit = bar.SubmitAsync();
                yield return h.Await(submit);
                Assert.That(submit.Result!.Accepted, Is.True);
                JObject body = JObject.Parse(h.Fake!.Calls.Last(c => c.Method == "POST" && c.Path.EndsWith("/v1/requests", StringComparison.Ordinal)).Body);
                Assert.That((string?)body["worker"], Is.EqualTo("gc-specialist"));
                PromptBar reopened = new PromptBar(ui);
                Assert.That(reopened.Q<DropdownField>("prompt-worker").value, Is.EqualTo("gc-specialist"));
                Assert.That(File.Exists(Path.Combine(GatewayHarness.ProjectRoot, "UserSettings/GameCoreStudio.Worker.asset")), Is.True);
            }
            finally { StudioWorkerSettings.instance.Worker = previous; }
        }

        [UnityTest]
        public IEnumerator D13_DisconnectedStreamPollsAsFallback()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Gateway.Options.AutoImport = false;
            using StudioUiContext ui = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            PreparedRequest request = ui.Requests.Build("Explain the current scene", h.EmptySelection());
            Task<PromptSubmission> submit = ui.Submit(request);
            yield return h.Await(submit);
            Assert.That(submit.Result.Accepted, Is.True);
            Task cancel = h.Client.CancelAsync(request.ChangeSetId);
            yield return h.Await(cancel);
            cancel.GetAwaiter().GetResult();
            h.Gateway.Tick(); // Stream is stopped; polling must recover the terminal state.
            yield return h.Until(() => h.Gateway.Requests.Any(r => r.RequestId == request.ChangeSetId && r.TaskStatus == "cancelled"), 5, "fallback polling");
            ui.Tick();
            Assert.That(ui.Tasks.Find(request.ChangeSetId)!.etosStatus, Is.EqualTo("cancelled"));
        }

        private sealed class TakeSource : IPcmSource
        {
            private bool _read;
            private readonly byte[] _pcm;
            public TakeSource(byte[] pcm) { _pcm = pcm; }
            public string Name => "retained voice take frame pattern";
            public bool Finished => _read;
            public void Start() => _read = false;
            public void Stop() { }
            public byte[] Read() { if (_read) return Array.Empty<byte>(); _read = true; return _pcm; }
            public void Dispose() { }
        }
    }
}
