// GameCore.Studio.Hollowmere.P2_2.Live - the Editor against the REAL etos node and companion on the Studio host
// (studio/tools/live-etos-tests.sh). Ignored unless GAMECORE_ETOS_LIVE=1; the key file comes from
// GAMECORE_ETOS_KEY_FILE and is never printed. Evidence (redacted, checked for the key) goes to GC_ETOS_EVIDENCE_DIR.
// Rows: (b) NPC request through to staging, (c) 256x256 icon PNG imported under Assets/Hollowmere/Generated/P2_2,
// (d) TTS line WAV, (e) describe, (f) 3D refusal, (i) tampered artifact refused before Put, (j) voice through
// EtosVoiceSession from a spoken WAV, and the Editor microphone fed by a PipeWire virtual source.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Studio.Hollowmere.P2_2.Live
{
    [Category("Live")]
    public sealed class EtosLiveTests
    {
        public const string GeneratedFolder = "Assets/Hollowmere/Generated/P2_2";
        public const string IconPath = GeneratedFolder + "/wooden_well_icon.png";
        public const string WelcomePath = GeneratedFolder + "/welcome_to_thornwick.wav";
        public const string TravellerPath = "Assets/Hollowmere/World/Definitions/Traveller.asset";

        private GatewayHarness? _harness;

        private GatewayHarness H => _harness!;

        [SetUp]
        public void SetUp()
        {
            if (Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE") != "1")
            {
                Assert.Ignore("live etos tests run only with GAMECORE_ETOS_LIVE=1 (studio/tools/live-etos-tests.sh)");
            }

            _harness = GatewayHarness.Live();
        }

        [TearDown]
        public void TearDown()
        {
            if (_harness != null)
            {
                Evidence("log-" + TestContext.CurrentContext.Test.MethodName, new JObject { ["log"] = new JArray(_harness.Log.Recent.Select(e => e.ToString()).ToArray()) });
                _harness.Dispose();
                _harness = null;
            }
        }

        [UnityTest]
        [Timeout(1500000)]
        public IEnumerator B_NpcRequest_ReachesStaging()
        {
            H.Runtime.Index.Rebuild();
            UnityEngine.Object traveller = AssetDatabase.LoadMainAssetAtPath(TravellerPath);
            Assert.That(traveller, Is.Not.Null, TravellerPath);
            CandidateImport? import = null;
            List<JObject> timeline = new List<JObject>();
            Stopwatch watch = Stopwatch.StartNew();
            string id = IdDerivation.NewChangeSetId();
            H.Gateway.RequestChanged += view =>
            {
                if (view.RequestId == id)
                {
                    timeline.Add(new JObject { ["ms"] = watch.ElapsedMilliseconds, ["state"] = view.State, ["taskStatus"] = view.TaskStatus, ["local"] = view.LocalState, ["progress"] = view.Progress });
                }
            };
            H.Gateway.CandidateStaged += result =>
            {
                if (result.RequestId == id)
                {
                    import = result;
                }
            };
            H.Gateway.Start();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");

            SelectionSnapshot selection = AgentRequestBuilder.SnapshotOf(H.Runtime, new[] { traveller });
            AgentRequest request = AgentRequestBuilder.Build(H.Runtime, selection, "Move this NPC two metres north.");
            request.ChangeSetId = id;
            Task<string> submit = H.Gateway.SubmitAsync(request, CancellationToken.None);
            yield return H.Await(submit, 120, "the submit");
            Assert.That(submit.Result, Is.EqualTo(id));
            double submitMs = watch.Elapsed.TotalMilliseconds;
            yield return H.Until(() => import != null || Settled(H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id)), 1200, "a candidate or a settled request", fail: false);
            RequestView? view = H.Gateway.Requests.FirstOrDefault(r => r.RequestId == id);
            JObject evidence = new JObject
            {
                ["requestId"] = id,
                ["selection"] = StudioJson.ToToken(selection),
                ["sliceBytes"] = StudioJson.Serialize(request.ContextSlice).Length,
                ["catalogRevision"] = request.ToolCatalogRevision,
                ["submitMs"] = Math.Round(submitMs, 1),
                ["totalMs"] = watch.ElapsedMilliseconds,
                ["timeline"] = new JArray(timeline.ToArray()),
                ["recoveredForeignRequests"] = H.Gateway.Requests.Count(r => !H.Gateway.IsOwn(r.RequestId)),
                ["stagedForeign"] = H.Gateway.Staged.Keys.Count(k => k != id),
                ["final"] = view == null ? null : new JObject { ["state"] = view.State, ["taskStatus"] = view.TaskStatus, ["tasks"] = new JArray(view.Tasks.ToArray()), ["outcome"] = view.Outcome?.DeepClone(), ["local"] = view.LocalState },
            };
            if (import != null)
            {
                evidence["staging"] = new JObject
                {
                    ["ok"] = import.Ok,
                    ["importMs"] = Math.Round(import.Milliseconds, 1),
                    ["diagnostics"] = new JArray(import.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()),
                    ["changeSet"] = import.Staged == null ? null : StudioJson.ToToken(import.Staged.ChangeSet),
                    ["journalState"] = H.Runtime.Journal.Read(id)?.EffectiveState.ToString(),
                };
                if (import.Staged != null)
                {
                    H.Gateway.Reject(id, "P2.2 live check: staged for review only, not applied");
                    evidence["rejectedAfter"] = H.Runtime.Journal.Read(id)?.EffectiveState.ToString();
                }
            }

            Evidence("b-npc-request", evidence);
            Assert.That(import, Is.Not.Null, "no candidate reached staging: " + (view == null ? "no request view" : view.State + " " + view.Outcome?.ToString(Formatting.None)));
            Assert.That(import!.Staged, Is.Not.Null, "the candidate was refused before staging: " + string.Join("; ", import.Diagnostics.Select(d => d.Code + ": " + d.Message)));
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator C_D_E_F_I_MediaOps()
        {
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");
            EtosMediaGenerator media = new EtosMediaGenerator(H.Gateway, H.Runtime, H.Queue);
            Stopwatch watch = Stopwatch.StartNew();
            Task<MediaImport> icon = media.GenerateImageAsync("A wooden well icon: a small stone-ringed village well with a wooden roof and bucket, game UI icon, centered, plain background.", IconPath, 256);
            yield return H.Await(icon, 400, "the image");
            double iconMs = watch.Elapsed.TotalMilliseconds;
            string iconFile = Path.Combine(GatewayHarness.ProjectRoot, IconPath);
            long iconBytes = File.Exists(iconFile) ? new FileInfo(iconFile).Length : -1;
            Texture2D? texture = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            JObject c = new JObject
            {
                ["ok"] = icon.Result.Ok,
                ["ms"] = Math.Round(iconMs, 1),
                ["provider"] = icon.Result.Result.Provider,
                ["providerSha256"] = icon.Result.Result.Sha256,
                ["providerBytes"] = icon.Result.Result.Bytes?.LongLength,
                ["importedSha256"] = icon.Result.Artifact?.Sha256,
                ["path"] = IconPath,
                ["fileBytes"] = iconBytes,
                ["textureSize"] = texture == null ? null : texture.width + "x" + texture.height,
                ["journal"] = icon.Result.Report?.Entry.Id,
                ["problem"] = icon.Result.Problem == null ? null : StudioJson.ToToken(icon.Result.Problem),
            };
            Evidence("c-image", c);

            watch.Restart();
            Task<MediaImport> speech = media.GenerateSpeechAsync("Welcome to Thornwick.", WelcomePath);
            yield return H.Await(speech, 300, "tts");
            string wavFile = Path.Combine(GatewayHarness.ProjectRoot, WelcomePath);
            Evidence("d-tts", new JObject
            {
                ["ok"] = speech.Result.Ok,
                ["ms"] = watch.ElapsedMilliseconds,
                ["provider"] = speech.Result.Result.Provider,
                ["sha256"] = speech.Result.Artifact?.Sha256,
                ["mediaType"] = speech.Result.Artifact?.MediaType,
                ["path"] = WelcomePath,
                ["fileBytes"] = File.Exists(wavFile) ? new FileInfo(wavFile).Length : -1,
                ["seconds"] = speech.Result.Result.Bytes == null ? null : (JToken)Math.Round(VoiceFraming.WavToPcm16Mono24k(speech.Result.Result.Bytes).Length / 2.0 / VoiceFraming.SampleRate, 2),
                ["problem"] = speech.Result.Problem == null ? null : StudioJson.ToToken(speech.Result.Problem),
            });

            OpResult? described = null;
            if (icon.Result.Result.Sha256 != null)
            {
                watch.Restart();
                Task<OpResult> describe = media.DescribeAsync(icon.Result.Result.Sha256, "Describe this game icon in one sentence.");
                yield return H.Await(describe, 300, "describe");
                described = describe.Result;
                Evidence("e-describe", new JObject { ["ok"] = described.Succeeded, ["ms"] = watch.ElapsedMilliseconds, ["provider"] = described.Provider, ["text"] = described.Text, ["problem"] = described.Refusal == null ? null : StudioJson.ToToken(described.Refusal) });
            }

            Task<MediaImport> mesh = media.Generate3dAsync("a wooden well", GeneratedFolder + "/wooden_well.glb");
            yield return H.Await(mesh, 120, "3d");
            Evidence("f-3d-refusal", new JObject { ["ok"] = mesh.Result.Ok, ["status"] = H.Gateway.Status.ThreeD.ToString(), ["problem"] = mesh.Result.Problem == null ? null : StudioJson.ToToken(mesh.Result.Problem) });

            JObject tamper = new JObject();
            if (icon.Result.Result.Sha256 != null)
            {
                string providerSha = icon.Result.Result.Sha256;
                ArtifactStore store = H.Runtime.Artifacts;
                bool before = store.Has(providerSha);
                int entries = store.Entries.Count;
                H.Client.Options.DownloadTamperHook = file =>
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    bytes[bytes.Length / 2] ^= 0x5a;
                    File.WriteAllBytes(file, bytes);
                };
                Task<byte[]> fetch = H.Gateway.FetchArtifactAsync(providerSha, CancellationToken.None);
                yield return H.Until(() => fetch.IsCompleted, 120, "the tampered fetch");
                H.Client.Options.DownloadTamperHook = null;
                EtosException? refused = fetch.Exception?.GetBaseException() as EtosException;
                tamper["refused"] = refused != null;
                tamper["code"] = refused?.Code;
                tamper["message"] = refused?.Error.Message;
                tamper["storeHadBefore"] = before;
                tamper["storeEntriesUnchanged"] = store.Entries.Count == entries;
                Evidence("i-tamper", tamper);
                Assert.That(refused?.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
                Assert.That(store.Entries.Count, Is.EqualTo(entries), "nothing was Put");
            }

            Assert.That(icon.Result.Ok, Is.True, "image: " + icon.Result.Problem?.Code + " " + icon.Result.Problem?.Message);
            Assert.That(iconBytes, Is.GreaterThan(0).And.LessThanOrEqualTo(200 * 1024));
            Assert.That(texture != null && texture.width == 256 && texture.height == 256, Is.True, "a 256x256 texture");
            Assert.That(speech.Result.Ok, Is.True, "tts: " + speech.Result.Problem?.Code + " " + speech.Result.Problem?.Message);
            Assert.That(described != null && described.Succeeded && !string.IsNullOrWhiteSpace(described.Text), Is.True, "describe: " + described?.Refusal?.Code);
            Assert.That(mesh.Result.Ok, Is.False);
            Assert.That(mesh.Result.Problem!.Code, Is.EqualTo(EtosCodes.NotConfigured).Or.EqualTo(DiagnosticCodes.Blocked).Or.EqualTo("blocked"));
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator J_Voice_FromASpokenWav()
        {
            Task<OpResult> prompt = H.Gateway.GenerateAsync(new OpRequest("tts", new JObject { ["text"] = "Move this NPC two metres north." }), CancellationToken.None);
            yield return H.Await(prompt, 300, "the spoken prompt");
            Assert.That(prompt.Result.Succeeded, Is.True, prompt.Result.Refusal?.Code);
            EtosVoiceSession voice = H.Gateway.CreateVoiceSession(new WavPcmSource(prompt.Result.Bytes!, 2.0));
            yield return RunVoice(voice, "j-voice-wav", 30);
            Assert.That(voice.FinalText, Is.Not.Null, "no final transcript; " + voice.LastError?.Code + " " + voice.LastError?.Message);
            Assert.That(voice.FinalText!.ToLowerInvariant(), Does.Contain("north"));
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator J_Voice_FromTheEditorMicrophone()
        {
            string[] devices = Microphone.devices;
            string? wanted = Environment.GetEnvironmentVariable("GC_ETOS_MIC_DEVICE");
            JObject probe = new JObject { ["devices"] = new JArray(devices), ["wanted"] = wanted, ["batchMode"] = Application.isBatchMode };
            if (devices.Length == 0)
            {
                probe["blocked"] = "W-VOICE-01: UnityEngine.Microphone.devices is empty in this Editor (" + (Application.isBatchMode ? "batchmode -nographics" : "interactive") + "); the PipeWire virtual source is not visible to Unity's audio backend";
                Evidence("j-voice-mic", probe);
                Assert.Inconclusive((string)probe["blocked"]!);
            }

            string device = devices.FirstOrDefault(d => wanted != null && d.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0) ?? devices[0];
            probe["device"] = device;
            MicrophoneCapture capture = new MicrophoneCapture(device);
            EtosVoiceSession voice = H.Gateway.CreateVoiceSession(capture);
            Task start = voice.StartAsync();
            yield return H.Until(() => start.IsCompleted, 60, "voice ready");
            if (start.IsFaulted)
            {
                probe["error"] = start.Exception?.GetBaseException().Message;
                Evidence("j-voice-mic", probe);
                Assert.Fail("voice did not start: " + probe["error"]);
            }

            string? marker = Marker();
            if (marker != null)
            {
                File.WriteAllText(marker, device + "\n");
            }

            float peak = 0;
            voice.Level += level => peak = Math.Max(peak, level);
            double seconds = double.TryParse(Environment.GetEnvironmentVariable("GC_ETOS_MIC_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : 10;
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                voice.Tick();
                H.Queue.Pump();
                yield return null;
            }

            yield return H.Await(voice.StopAsync(), 60, "voice stop");
            yield return H.Until(() => voice.CloseReason != null, 15, "close", fail: false);
            probe["deviceRate"] = capture.DeviceRate;
            probe["peakLevel"] = Math.Round(peak, 3);
            probe["framesSent"] = voice.FramesSent;
            probe["finalTranscript"] = voice.FinalText;
            probe["closeReason"] = voice.CloseReason;
            probe["error"] = voice.LastError == null ? null : StudioJson.ToToken(voice.LastError);
            Evidence("j-voice-mic", probe);
            voice.Dispose();
            Assert.That(voice.FramesSent, Is.GreaterThan(0));
            Assert.That(voice.FinalText, Is.Not.Null, "no final transcript from the microphone (peak level " + peak + ")");
            Assert.That(voice.FinalText!.ToLowerInvariant(), Does.Contain("north"));
        }

        // ----------------------------------------------------------------------------------- helpers

        private IEnumerator RunVoice(EtosVoiceSession voice, string evidenceName, double maxSeconds)
        {
            List<JObject> events = new List<JObject>();
            Stopwatch watch = Stopwatch.StartNew();
            voice.Transcript += t => events.Add(new JObject { ["ms"] = watch.ElapsedMilliseconds, ["item"] = t.ItemId, ["revision"] = t.Revision, ["final"] = t.Final, ["text"] = t.Text });
            voice.Error += d => events.Add(new JObject { ["ms"] = watch.ElapsedMilliseconds, ["error"] = d.Code, ["message"] = d.Message });
            Task start = voice.StartAsync();
            yield return H.Until(() => start.IsCompleted, 60, "voice ready");
            double readyMs = watch.Elapsed.TotalMilliseconds;
            if (!start.IsFaulted)
            {
                yield return H.Until(() => { voice.Tick(); return voice.Source.Finished; }, maxSeconds, "the audio to be streamed", fail: false);
                double lastAudioMs = watch.Elapsed.TotalMilliseconds;
                yield return H.Until(() => voice.Finals.Count > 0, 15, "a final transcript", fail: false);
                yield return H.Await(voice.StopAsync(), 60, "voice stop");
                yield return H.Until(() => voice.CloseReason != null, 15, "close", fail: false);
                TranscriptUpdate? final = voice.Finals.LastOrDefault();
                JToken? finalMs = null;
                foreach (JObject e in events)
                {
                    if ((bool?)e["final"] == true)
                    {
                        finalMs = e["ms"];
                    }
                }

                Evidence(evidenceName, new JObject
                {
                    ["source"] = voice.Source.Name,
                    ["sessionId"] = voice.Ready?.SessionId,
                    ["readyMs"] = Math.Round(readyMs, 1),
                    ["lastAudioMs"] = Math.Round(lastAudioMs, 1),
                    ["finalAtMs"] = finalMs,
                    ["framesSent"] = voice.FramesSent,
                    ["finalTranscript"] = final?.Text,
                    ["closeReason"] = voice.CloseReason,
                    ["events"] = new JArray(events.ToArray()),
                });
            }
            else
            {
                Evidence(evidenceName, new JObject { ["refused"] = start.Exception?.GetBaseException().Message, ["events"] = new JArray(events.ToArray()) });
            }

            voice.Dispose();
        }

        private static bool Settled(RequestView? view)
        {
            if (view == null)
            {
                return false;
            }

            return view.State == "failed" || view.State == "cancelled" || view.State == "needs_clarification" || view.State == "candidate_invalid" || view.State == "unresolved" || view.LocalState == "import_failed";
        }

        private static string? Marker()
        {
            string? dir = Environment.GetEnvironmentVariable("GC_ETOS_EVIDENCE_DIR");
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "mic-listening");
        }

        private void Evidence(string name, JObject content)
        {
            string? dir = Environment.GetEnvironmentVariable("GC_ETOS_EVIDENCE_DIR");
            if (string.IsNullOrEmpty(dir) || _harness == null)
            {
                return;
            }

            string text = EtosRedaction.Redact(content.ToString(Formatting.Indented));
            Assert.That(_harness.Credentials.AppearsIn(text), Is.False, "evidence must never carry the key");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "unity-" + name + ".json"), text + "\n");
        }
    }
}
