// GameCore.Studio.Hollowmere.P2_2 - the etos gateway inside the Editor against the fake companion (no network beyond
// loopback, no provider): submit → events → candidate → verified artifacts → ArtifactStore → Stage(Candidate) →
// journal → apply; tampered bytes refused before they are retained; null in a candidate refused; stale catalog resent
// once; provider status and every gateway event on the main thread; media ops imported through asset.import; 3D
// refused with its code; voice transcripts; settings and logs never carry the key.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Studio.Hollowmere.P2_2.Tests
{
    public sealed class EtosGatewayTests
    {
        private const string TempFolder = "Assets/Hollowmere/Tests/P2_2/Temp";

        private GatewayHarness? _harness;
        private readonly List<string> _assets = new List<string>();
        private int _mainThread;

        private GatewayHarness H => _harness!;

        [SetUp]
        public void SetUp()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
            foreach (string asset in _assets)
            {
                AssetDatabase.DeleteAsset(asset);
            }

            _assets.Clear();
            if (AssetDatabase.IsValidFolder(TempFolder) && AssetDatabase.FindAssets(string.Empty, new[] { TempFolder }).Length == 0)
            {
                AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        [UnityTest]
        public IEnumerator Submit_CandidateIsVerifiedRetainedStagedAppliedAndJournaled()
        {
            string path = TempPath(".png");
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng(), "texture");
            _harness = GatewayHarness.WithFake(fake => fake.Worker = body => new FakeCandidate(ImportCandidate((string)body["changeSetId"]!, path, png), new[] { png }));
            List<int> threads = new List<int>();
            List<string> states = new List<string>();
            CandidateImport? import = null;
            H.Gateway.RequestChanged += view => { threads.Add(Thread.CurrentThread.ManagedThreadId); states.Add(view.State + "/" + (view.LocalState ?? "-")); };
            H.Gateway.CandidateStaged += result => { threads.Add(Thread.CurrentThread.ManagedThreadId); import = result; };
            H.Gateway.Start();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 30, "hello");

            AgentRequest request = AgentRequestBuilder.Build(H.Runtime, H.EmptySelection(), "Put a wooden well icon in the project.");
            Task<string> submit = H.Gateway.SubmitAsync(request, CancellationToken.None);
            yield return H.Await(submit, 30, "the submit");
            string id = submit.Result;
            yield return H.Until(() => import != null, 30, "the candidate to be staged");

            Assert.That(import!.RequestId, Is.EqualTo(id));
            Assert.That(import.Ok, Is.True, Describe(import.Diagnostics));
            Assert.That(H.Runtime.Artifacts.Has(png.Sha256), Is.True, "the verified bytes are retained");
            ChangeSet? journaled = H.Runtime.Journal.Read(id);
            Assert.That(journaled, Is.Not.Null, "a valid candidate is journaled");
            Assert.That(journaled!.EffectiveState, Is.EqualTo(ChangeSetState.Candidate));
            Assert.That(journaled.Links?.EtosTasks, Is.Not.Null.And.Not.Empty, "the etos task is linked");
            RequestView view = H.Gateway.Requests.Single(r => r.RequestId == id);
            Assert.That(view.LocalState, Is.EqualTo("staged"));
            Assert.That(states, Has.Some.StartsWith("running"));
            Assert.That(threads, Is.Not.Empty.And.All.EqualTo(_mainThread), "every gateway event is raised on the main thread");
            Assert.That(File.Exists(Path.Combine(GatewayHarness.ProjectRoot, path)), Is.False, "staging writes no asset");

            ApplyReport report = H.Gateway.Apply(id);
            _assets.Add(path);
            Assert.That(report.Ok, Is.True, Describe(report.Diagnostics));
            Assert.That(File.Exists(Path.Combine(GatewayHarness.ProjectRoot, path)), Is.True);
            Assert.That(H.Runtime.Journal.Read(id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(H.Fake!.Calls.Where(c => c.Path.StartsWith("/api/v1/agents/gamecore-studio/http/v1/artifacts/", StringComparison.Ordinal)).Select(c => c.Authorized), Is.All.True);
        }

        [UnityTest]
        public IEnumerator TamperedArtifact_IsRefusedBeforeItIsRetained()
        {
            string path = TempPath(".png");
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng(), "texture");
            _harness = GatewayHarness.WithFake(fake =>
            {
                fake.TamperArtifacts = true;
                fake.Worker = body => new FakeCandidate(ImportCandidate((string)body["changeSetId"]!, path, png), new[] { png });
            });
            CandidateImport? import = null;
            H.Gateway.CandidateStaged += result => import = result;
            H.Gateway.Start();
            int writes = H.Runtime.Artifacts.Entries.Count;
            Task<string> submit = H.Gateway.SubmitAsync(AgentRequestBuilder.Build(H.Runtime, H.EmptySelection(), "Import a well icon."), CancellationToken.None);
            yield return H.Await(submit, 30, "the submit");
            yield return H.Until(() => import != null, 30, "the import result");

            Assert.That(import!.Ok, Is.False);
            Assert.That(import.Staged, Is.Null, "nothing reaches staging");
            Assert.That(import.Diagnostics.Select(d => d.Code), Has.Member(EtosCodes.ArtifactDigestMismatch));
            Assert.That(H.Runtime.Artifacts.Has(png.Sha256), Is.False, "tampered bytes are never retained");
            Assert.That(H.Runtime.Artifacts.Entries.Count, Is.EqualTo(writes));
            Assert.That(H.Runtime.Journal.Exists(submit.Result), Is.False);
            Assert.That(H.Gateway.Requests.Single(r => r.RequestId == submit.Result).LocalState, Is.EqualTo("import_failed"));
        }

        [UnityTest]
        public IEnumerator CandidateCarryingNull_IsRefusedAsCandidateInvalid()
        {
            string path = TempPath(".png");
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng(), "texture");
            _harness = GatewayHarness.WithFake(fake => fake.Worker = body =>
            {
                JObject changeSet = ImportCandidate((string)body["changeSetId"]!, path, png);
                changeSet["selection"] = JValue.CreateNull();
                return new FakeCandidate(changeSet, new[] { png });
            });
            CandidateImport? import = null;
            H.Gateway.CandidateStaged += result => import = result;
            H.Gateway.Start();
            Task<string> submit = H.Gateway.SubmitAsync(AgentRequestBuilder.Build(H.Runtime, H.EmptySelection(), "Import a well icon."), CancellationToken.None);
            yield return H.Await(submit, 30, "the submit");
            yield return H.Until(() => import != null, 30, "the import result");

            Assert.That(import!.Ok, Is.False);
            Assert.That(import.Diagnostics.Single().Code, Is.EqualTo(EtosCodes.CandidateInvalid));
            Assert.That(import.Diagnostics.Single().Message, Does.Contain("/selection"));
            Assert.That(H.Runtime.Artifacts.Has(png.Sha256), Is.False);
        }

        [UnityTest]
        public IEnumerator ForeignCandidate_IsListedButNeverStagedOnItsOwn()
        {
            string path = TempPath(".png");
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng(), "texture");
            _harness = GatewayHarness.WithFake(fake => fake.Worker = body => new FakeCandidate(ImportCandidate((string)body["changeSetId"]!, path, png), new[] { png }));
            bool staged = false;
            H.Gateway.CandidateStaged += _ => staged = true;
            H.Gateway.Start();
            string foreign = IdDerivation.NewChangeSetId();
            AgentRequest request = AgentRequestBuilder.Build(H.Runtime, H.EmptySelection(), "Another client's request.");
            EditRequestBody body = AgentRequestBuilder.ToBody(request, foreign, "gc-designer", "gc-mechanic", H.Runtime.Registry.Catalog);
            Task<SubmitResult> other = H.Client.SubmitAsync(body);
            yield return H.Await(other, 30, "the foreign submit");
            yield return H.Until(() => H.Gateway.Requests.Any(r => r.RequestId == foreign && r.HasCandidate), 30, "the foreign candidate event");
            yield return H.Until(() => staged, 2, "an import that must not happen", fail: false);

            Assert.That(staged, Is.False, "someone else's candidate is listed, not imported");
            Assert.That(H.Gateway.IsOwn(foreign), Is.False);
            Assert.That(H.Runtime.Artifacts.Has(png.Sha256), Is.False);
            Assert.That(H.Fake!.Calls.Any(c => c.Path.EndsWith("/v1/candidates/" + foreign, StringComparison.Ordinal)), Is.False);
        }

        [UnityTest]
        public IEnumerator StaleCatalog_IsResentOnceWithTheFullCatalog()
        {
            _harness = GatewayHarness.WithFake();
            string revision = H.Runtime.Registry.Catalog.Revision!;
            H.Fake!.HeldCatalogs.Add(revision);
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 30, "hello");
            Assert.That(H.Gateway.Hello!.HoldsCatalog(revision), Is.True);
            H.Fake.HeldCatalogs.Clear();

            Task<string> submit = H.Gateway.SubmitAsync(AgentRequestBuilder.Build(H.Runtime, H.EmptySelection(), "Anything."), CancellationToken.None);
            yield return H.Await(submit, 30, "the submit");

            List<FakeCall> posts = H.Fake.Calls.Where(c => c.Method == "POST" && c.Path.EndsWith("/v1/requests", StringComparison.Ordinal)).ToList();
            Assert.That(posts, Has.Count.EqualTo(2));
            Assert.That(JObject.Parse(posts[0].Body)["toolCatalog"], Is.Null, "the catalog is left out while the companion claims the revision");
            Assert.That(JObject.Parse(posts[1].Body)["toolCatalog"], Is.Not.Null, "stale_context → resent once with the catalog");
            Assert.That(JObject.Parse(posts[1].Body)["contextSlice"], Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Status_ComesFromHelloOnTheMainThread()
        {
            _harness = GatewayHarness.WithFake();
            ProviderStatus? seen = null;
            int thread = 0;
            H.Gateway.StatusChanged += status => { seen = status; thread = Thread.CurrentThread.ManagedThreadId; };
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 30, "hello");

            Assert.That(seen, Is.Not.Null);
            Assert.That(thread, Is.EqualTo(_mainThread));
            Assert.That(seen!.NodeReachable && seen.AgentReady, Is.True);
            Assert.That(seen.Image, Is.EqualTo(ProviderState.Live));
            Assert.That(seen.ThreeD, Is.EqualTo(ProviderState.NotConfigured));
            Assert.That(seen.Voice, Is.EqualTo(ProviderState.Unknown));
            Assert.That(AgentGatewayLookup.From(H.Runtime.Services.AgentGateway), Is.SameAs(H.Gateway), "registered into StudioServiceRegistry.AgentGateway");
        }

        [UnityTest]
        public IEnumerator UnreachableNode_IsReportedNotFaked()
        {
            _harness = GatewayHarness.WithFake();
            H.Fake!.Dispose();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 60, "hello");
            Assert.That(H.Gateway.Status.NodeReachable, Is.False);
            Assert.That(H.Gateway.Status.Problem!.Code, Is.EqualTo(EtosCodes.Transport));
        }

        [UnityTest]
        public IEnumerator AssetGenerate_ImageBecomesAStagedImportCandidate_3dIsRefusedWithItsCode()
        {
            _harness = GatewayHarness.WithFake();
            yield return H.Await(H.Gateway.RefreshStatusAsync(), 30, "hello");
            ServiceResult mesh = H.Gateway.GenerateAsset(new AgentAssetRequest(IdDerivation.NewChangeSetId(), "op1", "mesh", "a wooden well", null, null));
            Assert.That(mesh.Status, Is.EqualTo(ServiceRequestStatus.NotConfigured));
            Assert.That(mesh.Diagnostic!.Code, Is.EqualTo(EtosCodes.NotConfigured));

            CandidateImport? import = null;
            H.Gateway.CandidateStaged += result => import = result;
            ServiceResult image = H.Gateway.GenerateAsset(new AgentAssetRequest(IdDerivation.NewChangeSetId(), "op2", "icon", "a wooden well icon", null, null));
            Assert.That(image.Status, Is.EqualTo(ServiceRequestStatus.Accepted));
            yield return H.Until(() => import != null, 30, "the generated asset candidate");
            Assert.That(import!.Ok, Is.True, Describe(import.Diagnostics));
            Operation op = import.Staged!.ChangeSet.Operations.Single();
            Assert.That(op.Tool, Is.EqualTo(BuiltInToolIds.AssetImport));
            Assert.That((string?)op.Args?["path"], Does.StartWith("Assets/Generated/Studio/a_wooden_well_icon_op2"));
            Assert.That(H.Fake!.Calls.Single(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body, Does.Contain("\"max_cost_usd\":0.5"));
        }

        [UnityTest]
        public IEnumerator MediaGenerator_ImportsThroughAJournaledAssetImport()
        {
            _harness = GatewayHarness.WithFake();
            EtosMediaGenerator media = new EtosMediaGenerator(H.Gateway, H.Runtime, H.Queue);
            string path = TempPath(".png");
            Task<MediaImport> image = media.GenerateImageAsync("a wooden well icon", path, 1);
            yield return H.Await(image, 30, "the image");
            _assets.Add(path);
            Assert.That(image.Result.Ok, Is.True, Describe(image.Result.Diagnostics));
            Assert.That(File.Exists(Path.Combine(GatewayHarness.ProjectRoot, path)), Is.True);
            Assert.That(H.Runtime.Journal.Read(image.Result.Report!.Entry.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));

            Task<OpResult> describe = media.DescribeAsync(H.Fake!.ImageOutput.Sha256);
            yield return H.Await(describe, 30, "describe");
            Assert.That(describe.Result.Succeeded && !string.IsNullOrEmpty(describe.Result.Text), Is.True);

            Task<MediaImport> mesh = media.Generate3dAsync("a wooden well", TempPath(".glb"));
            yield return H.Await(mesh, 30, "3d");
            Assert.That(mesh.Result.Ok, Is.False);
            Assert.That(mesh.Result.Problem!.Code, Is.EqualTo(EtosCodes.NotConfigured));
            Assert.That(mesh.Result.Problem.Hint, Does.Contain("3d"));

            Task<MediaImport> sfx = media.GenerateSoundEffectAsync("splash", TempPath(".wav"));
            yield return H.Await(sfx, 5, "sfx");
            Assert.That(sfx.Result.Problem!.Code, Is.EqualTo(EtosCodes.NotConfigured));
        }

        [UnityTest]
        public IEnumerator LongOp_TransportTimeout_IsReissuedWithTheSameBody_RefusalIsNot()
        {
            _harness = GatewayHarness.WithFake();
            JObject timeout = new JObject { ["code"] = "transport", ["message"] = "POST /ops/generate.image timed out after 30000 ms", ["hint"] = "the etos node is unreachable from the companion; it retries" };
            H.Fake!.FailNext("/v1/ops/generate", 502, timeout);
            H.Fake.FailNext("/v1/ops/generate", 502, timeout);
            Task<OpResult> image = H.Gateway.GenerateAsync(new OpRequest("generate.image", new JObject { ["prompt"] = "a well" }), CancellationToken.None);
            yield return H.Await(image, 30, "the image");
            List<FakeCall> posts = H.Fake.Calls.Where(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).ToList();
            Assert.That(image.Result.Succeeded, Is.True, image.Result.Refusal?.Code);
            Assert.That(posts, Has.Count.EqualTo(3));
            Assert.That(posts.Select(c => c.Body).Distinct().Count(), Is.EqualTo(1), "identical bodies: the companion derives the same effect key");

            H.Fake.FailNext("/v1/ops/generate", 402, new JObject { ["code"] = "budget_exhausted", ["message"] = "the agent's budget is spent" });
            Task<OpResult> refused = H.Gateway.GenerateAsync(new OpRequest("tts", new JObject { ["text"] = "hello" }), CancellationToken.None);
            yield return H.Await(refused, 30, "the refusal");
            Assert.That(refused.Result.Refusal!.Code, Is.EqualTo(EtosCodes.BudgetExhausted));
            Assert.That(H.Fake.Calls.Count(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)), Is.EqualTo(4), "a refusal is never re-issued");
        }

        [UnityTest]
        public IEnumerator Voice_StreamsFramesAndRaisesTranscriptsOnTheMainThread()
        {
            _harness = GatewayHarness.WithFake(fake => fake.VoiceFramesPerRevision = 3);
            EtosVoiceSession voice = H.Gateway.CreateVoiceSession(new WavPcmSource(FakeMedia.Wav(FakeMedia.Tone(1.0, 48000, 440), 48000, 1), 0.2, 4.0));
            List<TranscriptUpdate> updates = new List<TranscriptUpdate>();
            List<float> levels = new List<float>();
            List<int> threads = new List<int>();
            voice.Transcript += t => { updates.Add(t); threads.Add(Thread.CurrentThread.ManagedThreadId); };
            voice.Level += level => levels.Add(level);
            yield return H.Await(voice.StartAsync(), 30, "voice ready");
            yield return H.Until(() => { voice.Tick(); return voice.Source.Finished; }, 30, "the audio to be sent");
            yield return H.Await(voice.StopAsync(), 30, "voice stop");
            yield return H.Until(() => voice.CloseReason != null, 10, "close");
            voice.Dispose();

            Assert.That(voice.FinalText, Is.EqualTo("move this NPC two metres north"));
            Assert.That(updates.Where(u => !u.Final), Is.Not.Empty, "partials arrive while speaking");
            Assert.That(updates.All(u => u.Role == "user"), Is.True);
            Assert.That(threads, Is.All.EqualTo(_mainThread));
            Assert.That(levels.Max(), Is.GreaterThan(0.1f));
            Assert.That(H.Fake!.VoiceFrameBytes, Is.All.LessThanOrEqualTo(VoiceFraming.MaxChunkBytes));
            Assert.That(H.Fake.VoiceSeqs, Is.EqualTo(Enumerable.Range(0, H.Fake.VoiceSeqs.Count).Select(i => (long)i)), "gapless seq");
            Assert.That(voice.CloseReason, Is.EqualTo("stopped"));
        }

        [UnityTest]
        public IEnumerator Voice_RefusalKeepsTheEtosCode()
        {
            _harness = GatewayHarness.WithFake(fake => fake.VoiceRefusal = new JObject { ["code"] = "not_configured", ["message"] = "no realtime provider" });
            EtosVoiceSession voice = H.Gateway.CreateVoiceSession(new WavPcmSource(FakeMedia.Wav(FakeMedia.Tone(0.2, 24000, 440), 24000, 1)));
            Diagnostic? error = null;
            voice.Error += d => error = d;
            Task start = voice.StartAsync();
            yield return H.Until(() => start.IsCompleted, 30, "the refusal");
            yield return H.Until(() => error != null, 5, "the error event");
            voice.Dispose();
            Assert.That(start.IsFaulted, Is.True);
            Assert.That(error!.Code, Is.EqualTo(EtosCodes.NotConfigured));
        }

        [Test]
        public void MainThreadQueue_RunsWorkOnlyWhenPumpedOnTheMainThread()
        {
            MainThreadQueue queue = new MainThreadQueue();
            int ranOn = 0;
            Task.Run(() => queue.Post(() => ranOn = Thread.CurrentThread.ManagedThreadId)).Wait();
            Assert.That(ranOn, Is.EqualTo(0));
            Assert.That(queue.Pump(), Is.EqualTo(1));
            Assert.That(ranOn, Is.EqualTo(_mainThread));
            Assert.That(queue.IsMainThread, Is.True);
        }

        [Test]
        public void Settings_HoldOnlyThePath_AndLogsNeverCarryTheKey()
        {
            string root = Path.Combine(Path.GetTempPath(), "gcstudio-p22-settings-" + Guid.NewGuid().ToString("N"));
            string keyFile = Path.Combine(root, "app-key.json");
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(keyFile, new JObject { ["url"] = "http://127.0.0.1:1", ["key"] = FakeCompanion.AppKey }.ToString());
                EtosSettings settings = new EtosSettings { KeyFile = keyFile };
                settings.Save(root);
                string stored = File.ReadAllText(EtosSettings.PathFor(root));
                Assert.That(stored, Does.Contain("app-key.json"));
                Assert.That(stored, Does.Not.Contain(FakeCompanion.AppKey));
                EtosSettings loaded = EtosSettings.Load(root);
                string? environment = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
                if (string.IsNullOrEmpty(environment))
                {
                    Assert.That(loaded.EffectiveKeyFile(), Is.EqualTo(keyFile));
                    Assert.That(loaded.KeyStatus(), Does.StartWith("etk_").And.Not.Contain(FakeCompanion.AppKey.Substring(8)));
                }

                EtosSettings leaking = new EtosSettings { KeyFile = FakeCompanion.AppKey };
                Assert.Throws<InvalidOperationException>(() => leaking.Save(root));

                MemoryStudioLog inner = new MemoryStudioLog();
                RedactingStudioLog log = new RedactingStudioLog(inner);
                log.Write(StudioLogLevel.Warning, "etos", "Authorization: Bearer " + FakeCompanion.AppKey + " ticket ett_abcdef0123456789 ws://x/v1/events?etos_ticket=ett_secretticket1&after=3", new Diagnostic("forbidden", "key " + FakeCompanion.AppKey, "eta_agentkey12345"));
                StudioLogEntry entry = inner.Recent.Single();
                string all = entry.Message + entry.Diagnostic!.Message + entry.Diagnostic.Hint;
                Assert.That(all, Does.Not.Contain(FakeCompanion.AppKey).And.Not.Contain("ett_abcdef").And.Not.Contain("ett_secret").And.Not.Contain("eta_agentkey"));
                Assert.That(entry.Diagnostic.Code, Is.EqualTo("forbidden"), "codes are preserved");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // ------------------------------------------------------------------------------------- helpers

        private string TempPath(string extension)
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
            {
                AssetDatabase.CreateFolder("Assets/Hollowmere/Tests/P2_2", "Temp");
            }

            string path = TempFolder + "/p22_" + Guid.NewGuid().ToString("N").Substring(0, 8) + extension;
            _assets.Add(path);
            return path;
        }

        private static JObject ImportCandidate(string id, string path, FakeArtifact artifact)
        {
            ArtifactRef reference = new ArtifactRef(artifact.Sha256, artifact.MediaType, artifact.Bytes.LongLength, artifact.Name, null, artifact.Role);
            Operation import = new Operation("op1", BuiltInToolIds.AssetImport, null, new JObject { ["path"] = path, ["artifact"] = new JObject { ["artifact"] = reference.Reference } });
            ChangeSet changeSet = new ChangeSet(id, ChangeSet.SchemaId, new Intent("Import the wooden well icon.", IntentOrigin.Agent), new[] { import }, artifacts: new[] { reference }, requirements: new Requirements(RuntimeApply.Live, false, false, false));
            return (JObject)StudioJson.ToToken(changeSet);
        }

        private static string Describe(IEnumerable<Diagnostic> diagnostics)
        {
            return string.Join("; ", diagnostics.Select(d => d.Code + ": " + d.Message));
        }
    }
}
