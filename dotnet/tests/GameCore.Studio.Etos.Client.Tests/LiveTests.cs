// Live verification against the REAL etos node and companion on the Studio host (studio/tools/live-etos-tests.sh).
// Ignored unless GAMECORE_ETOS_LIVE=1; the app key file comes from GAMECORE_ETOS_KEY_FILE (never printed). Every
// transcript is redacted and checked for the key before it is written to GC_ETOS_EVIDENCE_DIR; sanitized answers are
// also written to GC_ETOS_FIXTURE_OUT for the replay fixtures. Paid calls: one tts (cached by the companion's
// idempotency key on repeats) and one task that is cancelled within seconds.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    [Category("Live")]
    [NonParallelizable]
    public sealed class LiveTests
    {
        private EtosCredentials? _credentials;
        private CompanionClient? _client;
        private readonly List<string> _log = new List<string>();
        private readonly List<ExchangeRecord> _exchanges = new List<ExchangeRecord>();

        private CompanionClient Client => _client!;

        [SetUp]
        public void SetUp()
        {
            if (Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE") != "1")
            {
                Assert.Ignore("live etos tests run only with GAMECORE_ETOS_LIVE=1 (studio/tools/live-etos-tests.sh)");
            }

            string? keyFile = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable) ?? EtosCredentials.DefaultKeyFile();
            _credentials = EtosCredentials.FromKeyFile(keyFile ?? string.Empty);
            EtosClientOptions options = new EtosClientOptions
            {
                NodeUrl = _credentials.NodeUrl ?? "http://127.0.0.1:7410",
                DefaultMaxCostUsd = 0.50,
                Log = line => { lock (_log) { _log.Add(line); } },
            };
            _client = new CompanionClient(options, _credentials);
            _client.Exchanged += record => { lock (_exchanges) { _exchanges.Add(record); } };
        }

        [TearDown]
        public void TearDown()
        {
            if (_client != null)
            {
                string name = TestContext.CurrentContext.Test.MethodName ?? "test";
                JArray exchanges;
                lock (_exchanges)
                {
                    exchanges = new JArray(_exchanges.Select(e => new JObject { ["method"] = e.Method, ["path"] = e.Path, ["status"] = e.Status, ["ms"] = Math.Round(e.Milliseconds, 1), ["code"] = e.Code }));
                }

                Evidence("exchanges-" + name, new JObject { ["exchanges"] = exchanges, ["log"] = new JArray(_log.ToArray()) });
                _client.Dispose();
            }
        }

        [Test]
        public async Task L01_HelloThroughTheProxyReportsProviderStates()
        {
            Stopwatch watch = Stopwatch.StartNew();
            HelloInfo hello = await Client.HelloAsync();
            double ms = watch.Elapsed.TotalMilliseconds;
            Assert.That(hello.Service, Is.EqualTo("gamecore-studio"));
            Assert.That(hello.Providers.Keys, Is.SupersetOf(new[] { "image", "tts", "voice", "3d", "describe" }));
            Evidence("a-hello", new JObject { ["ms"] = Math.Round(ms, 1), ["answer"] = hello.Raw });
            Fixture("hello", 200, hello.Raw);
        }

        [Test]
        public async Task L02_TheAppKeyReachesNothingButItsOwnAgent()
        {
            EtosError? otherAgent = await Client.ProbeAsync("/api/v1/agents/etos-other-agent/http/v1/hello");
            EtosError? agentRoute = await Client.ProbeAsync("/api/v1/tasks/t000000000000000000000000");
            Assert.That(otherAgent, Is.Not.Null, "another agent's route must be refused");
            Assert.That(agentRoute, Is.Not.Null, "an agent-key route must be refused for an app key");
            Evidence("w-etos-02-authority", new JObject
            {
                ["otherAgent"] = new JObject { ["status"] = otherAgent!.Status, ["code"] = otherAgent.Code, ["message"] = otherAgent.Message, ["hint"] = otherAgent.Hint },
                ["agentOnlyRoute"] = new JObject { ["status"] = agentRoute!.Status, ["code"] = agentRoute.Code, ["message"] = agentRoute.Message, ["hint"] = agentRoute.Hint },
            });
            Fixture("error-other-agent", otherAgent.Status, ErrorJson(otherAgent));
            Fixture("error-agent-only-route", agentRoute.Status, ErrorJson(agentRoute));
        }

        [Test]
        public async Task L03_EventsResumeAfterAForcedDisconnectAndCancelEndsTheTask()
        {
            MemoryCursorStore cursors = new MemoryCursorStore();
            List<EventFrame> frames = new List<EventFrame>();
            using EventStream stream = new EventStream(Client, cursors);
            stream.Received += frame => { lock (frames) { frames.Add(frame); } };
            stream.Start();
            await WaitFor(() => stream.State == EventStreamState.Connected, TimeSpan.FromSeconds(30), "connected");
            int seen = -1;
            for (int i = 0; i < 60 && seen != Count(frames); i++)
            {
                seen = Count(frames);
                await Task.Delay(1500);
            }

            long start = stream.Cursor;
            int replayed = Count(frames);

            HelloInfo hello = await Client.HelloAsync();
            string id = NewChangeSetId();
            JObject catalog = new JObject { ["schema"] = "gamecore.studio.toolcatalog/1", ["objectTypes"] = new JArray(), ["tools"] = new JArray() };
            string revision = Json.Sha256Hex(System.Text.Encoding.UTF8.GetBytes("{\"objectTypes\":[],\"schema\":\"gamecore.studio.toolcatalog/1\",\"tools\":[]}"));
            catalog["revision"] = revision;
            JObject selection = new JObject { ["id"] = "sel_0" + id.Substring(4), ["mode"] = "Edit", ["targets"] = new JArray(), ["indexRevision"] = 1 };
            JObject slice = new JObject { ["revision"] = 1, ["project"] = "hollowmere", ["nodes"] = new JArray() };
            EditRequestBody body = new EditRequestBody(id, "P2.2 live check: this task is cancelled immediately; do nothing.", "agent", selection, slice, revision);
            if (!hello.HoldsCatalog(revision))
            {
                body.ToolCatalog = catalog;
            }

            Stopwatch watch = Stopwatch.StartNew();
            SubmitResult submitted = await Client.SubmitAsync(body);
            double submitMs = watch.Elapsed.TotalMilliseconds;
            Fixture("request-submitted", 200, submitted.Raw);
            await WaitFor(() => Frames(frames).Any(f => f.RequestId == id), TimeSpan.FromSeconds(30), "the request's first event");
            double firstEventMs = watch.Elapsed.TotalMilliseconds;

            stream.ForceDisconnect();
            Stopwatch cancelWatch = Stopwatch.StartNew();
            RequestInfo cancelled = await Client.CancelAsync(id);
            double cancelMs = cancelWatch.Elapsed.TotalMilliseconds;
            Fixture("request-cancelled", 200, cancelled.Raw);
            await WaitFor(() => Frames(frames).Any(f => f.RequestId == id && f.Type == "request" && (string?)f.Data["state"] == RequestStates.Cancelled), TimeSpan.FromSeconds(60), "the cancelled event after the reconnect");
            await Task.Delay(3000);
            await stream.StopAsync();

            List<EventFrame> mine = Frames(frames).Where(f => f.Cursor > start).ToList();
            List<long> replay = new List<long>();
            using (EventStream fresh = new EventStream(Client, new MemoryCursorStore(start)))
            {
                List<EventFrame> again = new List<EventFrame>();
                fresh.Received += frame => { lock (again) { again.Add(frame); } };
                fresh.Start();
                await WaitFor(() => Frames(again).Any(f => f.Cursor >= mine.Last().Cursor), TimeSpan.FromSeconds(30), "the replay to reach the last cursor");
                await fresh.StopAsync();
                replay.AddRange(Frames(again).Where(f => f.Cursor <= mine.Last().Cursor).Select(f => f.Cursor));
            }

            RequestInfo final = await Client.GetRequestAsync(id);
            Evidence("g-h-events-resume-cancel", new JObject
            {
                ["requestId"] = id,
                ["taskId"] = submitted.TaskId,
                ["submitMs"] = Math.Round(submitMs, 1),
                ["firstEventMs"] = Math.Round(firstEventMs, 1),
                ["cancelAckMs"] = Math.Round(cancelMs, 1),
                ["replayedBeforeStart"] = replayed,
                ["startCursor"] = start,
                ["connects"] = stream.Connects,
                ["reconnectMs"] = Math.Round(stream.LastReconnectMilliseconds, 1),
                ["received"] = new JArray(mine.Select(f => new JObject { ["cursor"] = f.Cursor, ["type"] = f.Type, ["requestId"] = f.RequestId, ["state"] = f.Data["state"], ["taskStatus"] = f.Data["taskStatus"], ["latencyMs"] = f.ReceivedAt - f.At })),
                ["replayCursors"] = new JArray(replay),
                ["final"] = final.Raw,
            });
            foreach (EventFrame frame in mine.Where(f => f.RequestId == id).Take(4))
            {
                Fixture("event-" + frame.Type + "-" + frame.Cursor, 200, new JObject { ["cursor"] = frame.Cursor, ["at"] = frame.At, ["type"] = frame.Type, ["requestId"] = frame.RequestId, ["data"] = frame.Data });
            }

            Assert.That(mine.Select(f => f.Cursor), Is.EqualTo(replay), "the resumed stream saw exactly what the ledger holds: no gap, no duplicate");
            Assert.That(stream.Connects, Is.GreaterThanOrEqualTo(2));
            Assert.That(cancelled.State, Is.EqualTo(RequestStates.Cancelled).Or.EqualTo(RequestStates.Failed), cancelled.Raw.ToString(Formatting.None));
            Assert.That(final.State, Is.EqualTo(RequestStates.Cancelled), final.Raw.ToString(Formatting.None));
            Assert.That(final.HasCandidate, Is.False);
            Assert.That(final.Tasks, Has.Count.EqualTo(1), "no duplicate task");
        }

        [Test]
        public async Task L04_VoiceTranscribesARealSpokenPromptThroughTheNode()
        {
            byte[] wav = await PromptWav("Move this NPC two metres north.");
            byte[] pcm = VoiceFraming.WavToPcm16Mono24k(wav);
            byte[] silence = new byte[VoiceFraming.SampleRate * 2 * 2];
            List<JObject> received = new List<JObject>();
            Stopwatch watch = Stopwatch.StartNew();
            using VoiceChannel channel = new VoiceChannel(Client);
            channel.Transcript += t => { lock (received) { received.Add(new JObject { ["type"] = "transcript", ["itemId"] = t.ItemId, ["revision"] = t.Revision, ["text"] = t.Text, ["final"] = t.Final, ["ms"] = watch.ElapsedMilliseconds }); } };
            channel.SpeechStarted += item => { lock (received) { received.Add(new JObject { ["type"] = "speech_started", ["itemId"] = item, ["ms"] = watch.ElapsedMilliseconds }); } };
            channel.SpeechEnded += item => { lock (received) { received.Add(new JObject { ["type"] = "speech_ended", ["itemId"] = item, ["ms"] = watch.ElapsedMilliseconds }); } };
            channel.Error += e => { lock (received) { received.Add(new JObject { ["type"] = "error", ["code"] = e.Code, ["message"] = e.Message, ["ms"] = watch.ElapsedMilliseconds }); } };
            VoiceReady ready;
            try
            {
                ready = await channel.ConnectAsync();
            }
            catch (EtosException refused)
            {
                Evidence("j-voice", new JObject { ["status"] = "refused", ["code"] = refused.Code, ["message"] = refused.Error.Message });
                Assert.Fail("the voice session was refused: " + refused.Error);
                return;
            }

            double readyMs = watch.Elapsed.TotalMilliseconds;
            byte[] all = new byte[pcm.Length + silence.Length];
            Buffer.BlockCopy(pcm, 0, all, 0, pcm.Length);
            for (int offset = 0; offset < all.Length; offset += VoiceFraming.CaptureFrameBytes)
            {
                byte[] frame = new byte[Math.Min(VoiceFraming.CaptureFrameBytes, all.Length - offset)];
                Buffer.BlockCopy(all, offset, frame, 0, frame.Length);
                await channel.SendPcmAsync(frame);
                await Task.Delay(100);
            }

            double lastAudioMs = watch.Elapsed.TotalMilliseconds;
            await WaitFor(() => { lock (received) { return received.Any(r => (string?)r["type"] == "transcript" && (bool)r["final"]!); } }, TimeSpan.FromSeconds(20), "a final transcript", fail: false);
            string reason = await channel.StopAsync(TimeSpan.FromSeconds(10));
            JObject? final;
            lock (received)
            {
                final = received.LastOrDefault(r => (string?)r["type"] == "transcript" && (bool)r["final"]!);
            }

            Evidence("j-voice", new JObject
            {
                ["sessionId"] = ready.SessionId,
                ["promptText"] = "Move this NPC two metres north.",
                ["promptSha256"] = Json.Sha256Hex(wav),
                ["audioSeconds"] = Math.Round(pcm.Length / 2.0 / VoiceFraming.SampleRate, 2),
                ["framesSent"] = channel.FramesSent,
                ["readyMs"] = Math.Round(readyMs, 1),
                ["lastAudioMs"] = Math.Round(lastAudioMs, 1),
                ["finalTranscript"] = final?["text"],
                ["finalAfterLastAudioMs"] = final == null ? null : (JToken)Math.Round((long)final["ms"]! - lastAudioMs, 1),
                ["closeReason"] = reason,
                ["events"] = new JArray(received.ToArray()),
            });
            Assert.That(final, Is.Not.Null, "the realtime provider returned no final transcript");
            Assert.That(((string)final!["text"]!).ToLowerInvariant(), Does.Contain("north"));
        }

        [Test]
        public async Task L05_3dGenerationIsRefusedHonestlyAndTamperedBytesAreRefused()
        {
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => Client.GenerateAsync(new GenerateBody("generate.3d", new JObject { ["prompt"] = "a wooden well" })))!;
            Fixture("error-generate-3d", refused.Error.Status, ErrorJson(refused.Error));
            byte[] wav = await PromptWav("Move this NPC two metres north.");
            string sha = Json.Sha256Hex(wav);
            Client.Options.DownloadTamperHook = path =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                bytes[bytes.Length / 2] ^= 0x01;
                File.WriteAllBytes(path, bytes);
            };
            EtosException tampered = Assert.ThrowsAsync<EtosException>(() => Client.DownloadArtifactAsync(sha))!;
            Client.Options.DownloadTamperHook = null;
            VerifiedArtifact clean = await Client.DownloadArtifactAsync(sha);
            Evidence("f-i-refusals", new JObject
            {
                ["generate3d"] = ErrorJson(refused.Error),
                ["tamperedDownload"] = ErrorJson(tampered.Error),
                ["cleanDownloadSha256"] = clean.Sha256,
            });
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.NotConfigured).Or.EqualTo(ProviderStates.Blocked));
            Assert.That(tampered.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
            Assert.That(clean.Sha256, Is.EqualTo(sha));
        }

        // -------------------------------------------------------------------------------------------- helpers

        private async Task<byte[]> PromptWav(string text)
        {
            GenerateResult result = await Client.GenerateAsync(new GenerateBody("tts", new JObject { ["text"] = text }) { MaxCostUsd = 0.50 });
            Fixture("generate-tts", 200, result.Raw);
            StoredArtifactInfo artifact = result.Artifacts.First();
            VerifiedArtifact bytes = await Client.DownloadArtifactAsync(artifact.Sha256, artifact.Bytes);
            string? dir = Environment.GetEnvironmentVariable("GC_ETOS_EVIDENCE_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, "voice-prompt.wav"), bytes.Bytes);
            }

            return bytes.Bytes;
        }

        private static JObject ErrorJson(EtosError error)
        {
            JObject body = new JObject { ["status"] = error.Status, ["code"] = error.Code, ["message"] = error.Message };
            if (error.Hint != null)
            {
                body["hint"] = error.Hint;
            }

            return body;
        }

        private void Evidence(string name, JObject content)
        {
            Write("GC_ETOS_EVIDENCE_DIR", "dotnet-" + name + ".json", content);
        }

        private void Fixture(string name, int status, JObject body)
        {
            Write("GC_ETOS_FIXTURE_OUT", name + ".json", new JObject { ["kind"] = KindOf(name), ["status"] = status, ["recordedFrom"] = "live node via P2.2 LiveTests", ["recordedAt"] = DateTime.UtcNow.ToString("u"), ["body"] = body });
        }

        private static string KindOf(string name)
        {
            if (name.StartsWith("error", StringComparison.Ordinal))
            {
                return "error";
            }

            if (name.StartsWith("event", StringComparison.Ordinal))
            {
                return "event";
            }

            if (name.StartsWith("request", StringComparison.Ordinal))
            {
                return name == "request-submitted" ? "submit" : "request";
            }

            return name.StartsWith("generate", StringComparison.Ordinal) ? "generate" : name;
        }

        private void Write(string variable, string file, JObject content)
        {
            string? dir = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(dir))
            {
                return;
            }

            string text = EtosRedaction.Redact(content.ToString(Formatting.Indented));
            Assert.That(_credentials!.AppearsIn(text), Is.False, "evidence must never carry the key");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file), text + "\n");
        }

        private static string NewChangeSetId() => "cs_0" + Guid.NewGuid().ToString("N").ToUpperInvariant().Substring(0, 25);

        private static List<EventFrame> Frames(List<EventFrame> frames)
        {
            lock (frames)
            {
                return frames.ToList();
            }
        }

        private static int Count(List<EventFrame> frames)
        {
            lock (frames)
            {
                return frames.Count;
            }
        }

        private static async Task WaitFor(Func<bool> condition, TimeSpan within, string what, bool fail = true)
        {
            DateTime end = DateTime.UtcNow + within;
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    if (fail)
                    {
                        Assert.Fail("timed out waiting for " + what);
                    }

                    return;
                }

                await Task.Delay(50);
            }
        }
    }
}
