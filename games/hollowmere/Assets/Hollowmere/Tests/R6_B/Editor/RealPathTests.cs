#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using System.IO;
using System.Linq;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Hollowmere.P2_2;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R6_B
{
    public sealed class RealPathTests
    {
        [Test]
        public void R6_Request6_ActualEngineStageRejectsUnmodifiedFerrymanBeforeAnyWrite()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Runtime.Index.Rebuild();
            const string odd = "Assets/Hollowmere/Dialogue/Graphs/Odd.asset";
            byte[] before = File.ReadAllBytes(odd);
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(DialogueCandidateTests.Witness().ToString());
            var staged = h.Runtime.Engine.Stage(candidate, new StageOptions
            {
                Mode = ValidationMode.Candidate, ToolCatalogRevision = h.Runtime.Registry.Catalog.Revision,
            });
            Assert.That(staged.Ok, Is.False);
            Assert.That(staged.AllDiagnostics.Any(d => DialogueCandidateTests.IsRule(d, "GP-DLG-005")), Is.True,
                string.Join("; ", staged.AllDiagnostics.Select(d => d.ToString())));
            var refusal = staged.AllDiagnostics.Single(d => DialogueCandidateTests.IsRule(d, "GP-DLG-005"));
            Assert.That(refusal.Data!["unreachable"]!.Values<int>(), Is.EqualTo(Enumerable.Range(0, 8)));
            Assert.That(File.ReadAllBytes(odd), Is.EqualTo(before));
            Assert.That(h.Runtime.Journal.List(ChangeSetState.Applied), Is.Empty);
        }

        [Test]
        public void R6_Request6_ActualEngineAcceptsExplicitRelinkWithoutWriting()
        {
            var scenes = UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup();
            try
            {
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity");
                using GatewayHarness h = GatewayHarness.WithFake();
                h.Runtime.Index.Rebuild();
                JObject json = DialogueCandidateTests.Witness();
                ChangeSet original = StudioJson.Deserialize<ChangeSet>(json.ToString());
                var graph = h.Runtime.Index.Snapshot().FindNode(original.Operations[1].Target!)!;
                JArray edges = (JArray)graph.Fields!["edges"].Value!.DeepClone();
                edges.Add(new JObject { ["from"] = 8, ["port"] = "Next", ["option"] = 0, ["to"] = 0 });
                json["operations"]![2]!["args"] = new JObject { ["fields"] = new JObject { ["entry"] = 8, ["edges"] = edges } };
                var staged = h.Runtime.Engine.Stage(StudioJson.Deserialize<ChangeSet>(json.ToString()), new StageOptions
                {
                    Mode = ValidationMode.Candidate, ToolCatalogRevision = h.Runtime.Registry.Catalog.Revision,
                });
                Assert.That(staged.Ok, Is.True, string.Join("; ", staged.AllDiagnostics.Select(d => d.ToString())));
                Assert.That(h.Runtime.Journal.List(ChangeSetState.Applied), Is.Empty);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(scenes);
            }
        }

        [UnityTest]
        public IEnumerator R6_Request3_RealSessionDryTransportWaitsForSamplesAndStopFinalNeverSubmits()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Fake!.VoiceFinalDelay = TimeSpan.FromMilliseconds(100);
            h.Fake.VoiceTranscript.Clear();
            h.Fake.VoiceTranscript.Add("Delete all NPCs in this region.");
            using var context = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime),
                new TaskLedger(new MemoryTaskRowStore()), false);
            var prompt = new PromptBar(context);
            var source = new Source();
            using var session = new EtosVoiceSession(h.Client, h.Queue, source, () => h.Gateway.Status, h.Log);
            using var take = new VoiceTakeDriver(session, () => session.IsCapturing && session.Ready != null, prompt.OnTranscript);
            int plays = 0;
            int journals = h.Runtime.Journal.List().Count;
            DateTime end = DateTime.UtcNow.AddSeconds(20);
            while (session.Ready == null)
            {
                Assert.That(DateTime.UtcNow, Is.LessThan(end));
                h.Queue.Pump();
                take.Tick(UnityEditor.EditorApplication.timeSinceStartup, false, () => plays++);
                yield return null;
            }
            Assert.That(plays, Is.Zero);
            Assert.That(session.IsCapturing, Is.False, "provider ready precedes source samples");
            source.Samples = true;
            bool done = false;
            while (!done)
            {
                Assert.That(DateTime.UtcNow, Is.LessThan(end));
                h.Queue.Pump();
                done = take.Tick(UnityEditor.EditorApplication.timeSinceStartup, plays > 0, () => plays++);
                context.Tick();
                yield return null;
            }
            Assert.That(plays, Is.EqualTo(1));
            Assert.That(h.Fake.VoiceStops, Is.EqualTo(1));
            Assert.That(prompt.Text, Is.EqualTo("Delete all NPCs in this region."));
            Assert.That(take.Error, Is.Null);
            Assert.That(session.IsCapturing, Is.False);
            Assert.That(h.Gateway.Requests, Is.Empty);
            Assert.That(h.Runtime.Journal.List().Count, Is.EqualTo(journals));
            Assert.That(prompt.LastRequest, Is.Null);
        }

        [UnityTest]
        public IEnumerator R6_Request4_RefusedAcknowledgmentIsBeforeCaptureAndNeverRetried()
        {
            using GatewayHarness h = GatewayHarness.WithFake(f => f.VoiceRefusal = new JObject
            {
                ["code"] = "bad_response", ["message"] = "upstream session.updated was not acknowledged; reconnect required",
            });
            var source = new Source { Samples = true };
            using var session = new EtosVoiceSession(h.Client, h.Queue, source, () => h.Gateway.Status, h.Log);
            using var take = new VoiceTakeDriver(session, () => session.Ready != null && session.IsCapturing, _ => Assert.Fail("no transcript"));
            DateTime end = DateTime.UtcNow.AddSeconds(15);
            while (!take.Tick(UnityEditor.EditorApplication.timeSinceStartup, false, () => Assert.Fail("no playback")))
            {
                Assert.That(DateTime.UtcNow, Is.LessThan(end));
                h.Queue.Pump();
                yield return null;
            }
            Assert.That(take.Error, Does.Contain("bad_response"));
            Assert.That(source.Starts, Is.Zero);
            Assert.That(h.Fake!.VoicePcm, Is.Empty);
            string log = string.Join("\n", h.Log.Recent.Select(e => e.Message));
            Assert.That(log, Does.Contain("setup failed phase=awaiting_companion_ready frames=0 code=bad_response"));
            Assert.That(h.Gateway.Requests, Is.Empty);
        }

        [UnityTest]
        public IEnumerator R6_Request3_ActualVoiceTakeDoesNotPlayOnElapsedTimerAndDrains()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Fake!.VoiceFinalDelay = TimeSpan.FromMilliseconds(100);
            h.Fake.VoiceTranscript.Clear();
            h.Fake.VoiceTranscript.Add("Delete all NPCs in this region.");
            var source = new Source();
            using var session = new EtosVoiceSession(h.Client, h.Queue, source, () => h.Gateway.Status, h.Log);
            var gateway = new VoiceGateway(h, session);
            using var context = new StudioUiContext(h.Runtime, () => gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            var window = UnityEngine.ScriptableObject.CreateInstance<StudioViewportWindow>();
            window.UseContext(context);
            window.EnsureGui();
            var state = P32State.instance;
            JObject before = state.Data;
            bool recording = state.Recording;
            MethodInfo method = typeof(Hollowmere.P3_2.Workflows.Workflows).GetMethod("VoiceTake", BindingFlags.NonPublic | BindingFlags.Static)!;
            string dir = Path.Combine(WorkflowRunner.OutputDir, "voice");
            string play = Path.Combine(dir, "play-r6-dry");
            string played = Path.Combine(dir, "played-r6-dry");
            try
            {
                state.Set("voice.r6-dry", new JObject());
                Directory.CreateDirectory(dir);
                File.Delete(play);
                File.Delete(played);
                Assert.That((bool)method.Invoke(null, new object[] { "r6-dry", "fixture.wav" })!, Is.False);
                JObject take = (JObject)state.Get("voice.r6-dry")!;
                take["pressedEditor"] = UnityEditor.EditorApplication.timeSinceStartup - 4;
                state.Set("voice.r6-dry", take);
                method.Invoke(null, new object[] { "r6-dry", "fixture.wav" });
                Assert.That(File.Exists(play), Is.False, "Elapsed three-second timer is not capture/provider readiness");
                source.Samples = true;
                DateTime end = DateTime.UtcNow.AddSeconds(20);
                bool done = false;
                while (!done)
                {
                    Assert.That(DateTime.UtcNow, Is.LessThan(end));
                    h.Queue.Pump();
                    done = (bool)method.Invoke(null, new object[] { "r6-dry", "fixture.wav" })!;
                    if (File.Exists(play)) File.WriteAllText(played, "done");
                    yield return null;
                }
                Assert.That(window.Prompt!.Text, Is.EqualTo("Delete all NPCs in this region."));
                Assert.That(h.Fake.VoiceStops, Is.EqualTo(1));
                Assert.That(gateway.Submits, Is.Zero);
                Assert.That(h.Runtime.Journal.List(), Is.Empty);
            }
            finally
            {
                state.VoiceTake?.Dispose();
                state.VoiceTake = null;
                typeof(Hollowmere.P3_2.Workflows.Workflows).GetMethod("StopWatch", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { "r6-dry" });
                state.Data = before;
                state.Recording = recording;
                UnityEngine.Object.DestroyImmediate(window);
                File.Delete(play);
                File.Delete(played);
            }
        }

        private sealed class VoiceGateway : IAgentGateway
        {
            private readonly GatewayHarness harness;
            private readonly EtosVoiceSession session;
            public VoiceGateway(GatewayHarness harness, EtosVoiceSession session) { this.harness = harness; this.session = session; }
            public int Submits;
            public ProviderStatus Status => harness.Gateway.Status;
            public IReadOnlyList<RequestView> Requests => harness.Gateway.Requests;
            public event Action<ProviderStatus>? StatusChanged { add { } remove { } }
            public event Action<RequestView>? RequestChanged { add { } remove { } }
            public event Action<CandidateNotice>? CandidateReady { add { } remove { } }
            public Task<string> SubmitAsync(AgentRequest req, CancellationToken ct) { Submits++; throw new InvalidOperationException("Voice must not submit"); }
            public Task CancelAsync(string id, CancellationToken ct) => throw new InvalidOperationException();
            public Task<ChangeSet> FetchCandidateAsync(string id, CancellationToken ct) => throw new InvalidOperationException();
            public Task<byte[]> FetchArtifactAsync(string id, CancellationToken ct) => throw new InvalidOperationException();
            public Task<OpResult> GenerateAsync(OpRequest req, CancellationToken ct) => throw new InvalidOperationException();
            public IVoiceSession CreateVoiceSession() => session;
        }

        private sealed class Source : IPcmSource
        {
            public bool Samples;
            public int Starts;
            public string Name => "r6-b-dry";
            public bool IsRunning { get; private set; }
            public bool Finished => false;
            public void Start() { Starts++; IsRunning = true; }
            public byte[] Read() => Samples && IsRunning ? new byte[4800] : Array.Empty<byte>();
            public void Stop() { IsRunning = false; }
            public void Dispose() => Stop();
        }
    }
}
