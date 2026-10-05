#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Studio.Authoring.Agent;
using System.Threading.Tasks;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Hollowmere.P2_2;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R4_A
{
    public sealed class ClientRegressionTests
    {
        [UnityTest]
        public IEnumerator P42_VOICE_01_WaitsForSourceAndPreservesFirstSamples()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            var source = new DelayedSource();
            using var voice = new EtosVoiceSession(h.Client, h.Queue, source, () => h.Gateway.Status, h.Log);
            Task start = voice.StartAsync();
            yield return h.Until(() => source.Started, 5, "source start");
            Assert.That(voice.IsCapturing, Is.False, "Microphone.Start is not sample readiness");
            Assert.That(start.IsCompleted, Is.False);
            source.Ready = true;
            yield return h.Await(start);
            start.GetAwaiter().GetResult();
            Assert.That(voice.IsCapturing, Is.True);
            Task stop = voice.StopAsync();
            yield return h.Await(stop);
            stop.GetAwaiter().GetResult();
            Assert.That(h.Fake!.VoiceFrameBytes.Sum(), Is.EqualTo(4802), "first frame and final partial sample retained");
            Assert.That(h.Fake.VoicePcm.SelectMany(b => b), Is.EqualTo(Enumerable.Range(0, 4802).Select(n => (byte)(n % 251))));
            Assert.That(h.Gateway.Requests, Is.Empty);
        }

        [Test]
        public void P42_STARTUP_01_AutomaticPairingIgnoresProjectSettingsAndNamesMissingFile()
        {
            MethodInfo? resolve = typeof(EtosCredentials).GetMethod("ResolveAutomaticKeyFile");
            Assert.That(resolve, Is.Not.Null, "automatic startup must resolve the documented pairing path independently");
            string? old = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
            try
            {
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, null);
                Assert.That(resolve!.Invoke(null, null), Is.EqualTo(EtosCredentials.DefaultKeyFile()));
                string missing = Path.Combine(Path.GetTempPath(), "r4a-absent-" + Guid.NewGuid().ToString("N"));
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, missing);
                Assert.That(resolve.Invoke(null, null), Is.EqualTo(missing));
                var error = Assert.Throws<EtosException>(() => EtosCredentials.FromKeyFile(missing));
                Assert.That(error!.Code, Is.EqualTo("not_configured"));
                Assert.That(error.Message, Does.Contain(missing));
            }
            finally { Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, old); }
        }

        [UnityTest]
        public IEnumerator P42_MEDIA_01_DirectVoiceOmitsUnownedLocalId()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Fake!.OpRefusals["tts"] = Tuple.Create(409, new JObject { ["code"] = "not_configured", ["message"] = "fixture stops before import" });
            var media = new EtosMediaGenerator(h.Gateway, h.Runtime, h.Queue);
            media.RequestVoiceLine(new VoiceGenerationRequest("fixture", 0, "speaker", "Delete every NPC in the village", "fixture"));
            yield return h.Until(() => h.Fake!.Calls.Any(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)), 5, "media request");
            JObject body = JObject.Parse(h.Fake!.Calls.Last(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body);
            Assert.That(body["changeSetId"], Is.Null, "a local display ID does not establish companion ownership");
            // Complete queued work before disposing the runtime.
            yield return h.Await(media.PendingVoice!);
        }

        [UnityTest]
        public IEnumerator P42_STAGE_01_RetainedAppAndOwnedAgentUseSeparateRoutes()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            EtosProjectContext.Bind(h.Runtime, h.Client);
            string repo = Path.GetFullPath(Path.Combine(GatewayHarness.ProjectRoot, "../.."));
            string folder = Path.Combine(repo, "samples/mechanisms/pressure-plate/candidate");
            string file = Directory.GetFiles(folder, "*.json").First(f => Path.GetFileName(f) == "change-set.json");
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(File.ReadAllText(file));
            foreach (ArtifactRef artifact in candidate.Artifacts!)
                h.Runtime.Artifacts.Put(File.ReadAllBytes(Path.Combine(folder, "artifacts", artifact.Name!)), artifact);
            h.Runtime.Journal.Write(candidate);
            var service = (CompanionStageService)StageAdmission.Of(h.Runtime).Options.StageService!;
            StageCandidateRequest request = StageAdmission.Of(h.Runtime).BuildStageRequest(candidate, GatewayHarness.ProjectRoot);
            Task<StageJobInfo> app = service.RequestStageJobAsync(request);
            yield return h.Await(app);
            Assert.That((string?)app.Result.Raw["origin"], Is.EqualTo("app"));
            JObject signed = JObject.Parse(h.Fake!.Calls.Last(c => c.Path.EndsWith("/v1/stage/app-candidate", StringComparison.Ordinal)).Body);
            JObject payload = JObject.Parse(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String((string)signed["payloadBase64"]!)));
            Assert.That(JToken.DeepEquals(payload["changeSet"], JObject.Parse(StudioJson.Serialize(candidate))), Is.True);
            Assert.That(((JArray)payload["files"]!).Count, Is.EqualTo(candidate.Artifacts!.Count));
            // A real owned request in the fake ledger selects the unchanged ledger-stage contract.
            var body = new EditRequestBody(candidate.Id, "mechanism", "agent", new JObject(), new JObject(), request.CatalogRevision)
                { ToolCatalog = (JObject)payload["toolCatalog"]! };
            Task submit = h.Client.SubmitAsync(body);
            yield return h.Await(submit);
            submit.GetAwaiter().GetResult();
            Task<StageJobInfo> agent = service.RequestStageJobAsync(request);
            yield return h.Await(agent);
            Assert.That((string?)agent.Result.Raw["origin"], Is.EqualTo("agent"));
            Assert.That(h.Fake.Calls.Last().Path, Does.EndWith("/v1/stage"));
        }

        [Test]
        public void P42_STARTUP_01_AutomaticSessionBindsHostPairingAfterReload()
        {
            string root = Path.Combine(Path.GetTempPath(), "gc-r4a-startup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            File.WriteAllText(Path.Combine(root, "ProjectSettings/ProjectSettings.asset"), "productGUID: " + new string('b', 32));
            string? previous = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
            var session = EtosStudioSession.instance;
            using var fake = new GameCore.Studio.Etos.Testing.FakeCompanion();
            fake.Start();
            string fixture = Path.Combine(root, "fixture-pairing.json");
            File.WriteAllText(fixture, new JObject { ["key"] = GameCore.Studio.Etos.Testing.FakeCompanion.AppKey, ["url"] = fake.NodeUrl }.ToString());
            using var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(root, Path.Combine(root, "state")), Log = new GameCore.Studio.Authoring.MemoryStudioLog(), LoadIndexCache = false, SearchFolders = Array.Empty<string>() });
            MethodInfo start = typeof(EtosStudioSession).GetMethod("StartCore", BindingFlags.Instance | BindingFlags.NonPublic)!;
            MethodInfo reload = typeof(EtosStudioSession).GetMethod("OnBeforeReload", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, fixture);
                for (int take = 0; take < 2; take++)
                {
                    Assert.That(start.Invoke(session, new object[] { runtime }), Is.True);
                    Assert.That(runtime.Services.AgentGateway, Is.SameAs(EtosStudioSession.Gateway));
                    Assert.That(EtosStudioSession.Gateway!.Client.NodeUrl, Is.EqualTo(fake.NodeUrl));
                    reload.Invoke(session, null);
                    Assert.That(EtosStudioSession.Gateway, Is.Null);
                }
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, Path.Combine(root, "missing-pairing.json"));
                Assert.That(start.Invoke(session, new object[] { runtime }), Is.False);
                Assert.That(EtosStudioSession.Problem!.Code, Is.EqualTo("not_configured"));
                Assert.That(EtosStudioSession.Problem.Message, Does.Contain("missing-pairing.json"));
            }
            finally
            {
                reload.Invoke(session, null);
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, previous);
                Directory.Delete(root, true);
            }
        }

        [UnityTest]
        public IEnumerator P42_STAGE_01_WaitsForIssuedVerdictAndPreservesOrigin()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            Task<StageJobInfo> submit = h.Client.StageAsync(IdDerivation.NewChangeSetId(), h.Client.Options.ProjectId, "source", new string('c', 64));
            yield return h.Await(submit);
            string jobId = submit.Result.JobId;
            var service = new CompanionStageService(h.Client);
            Task<SignedVerdict> pending = service.GetVerdict(jobId);
            yield return h.Until(() => h.Fake!.Calls.Any(c => c.Method == "GET" && c.Path.EndsWith("/v1/stage/" + jobId, StringComparison.Ordinal)), 5, "stage poll");
            Assert.That(pending.IsCompleted, Is.False);
            var signed = new JObject { ["jobId"] = jobId, ["signature"] = "fixture-signature", ["origin"] = "app" };
            h.Fake!.CompleteStage(jobId, "done", signed);
            yield return h.Await(pending);
            Assert.That(JToken.DeepEquals(pending.Result.Verdict, signed), Is.True);
            h.Fake.CompleteStage(jobId, "failed");
            Task<SignedVerdict> refused = service.GetVerdict(jobId);
            yield return h.Await(refused);
            Assert.That(refused.IsFaulted, Is.True);
            Assert.That(refused.Exception!.GetBaseException(), Is.TypeOf<EtosException>());
        }

        [Test]
        public void P42_STAGE_01_ResolvesUnpreviewedUiCandidateOnlyForItsRuntime()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            using GatewayHarness other = GatewayHarness.WithFake();
            using var context = new StudioUiContext(h.Runtime, () => h.Gateway, new SelectionModel(h.Runtime), new TaskLedger(new MemoryTaskRowStore()), false);
            var candidate = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("app sample", IntentOrigin.Agent),
                new[] { new Operation("op1", "mechanism.propose", null, new JObject()) });
            context.Candidates.Add(candidate.Id, candidate);
            FieldInfo field = typeof(StudioUiSession).GetField("_context", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? previous = field.GetValue(StudioUiSession.instance);
            try
            {
                field.SetValue(StudioUiSession.instance, context);
                Assert.That(EtosProjectContext.OpenCandidate(h.Runtime, candidate.Id), Is.SameAs(candidate));
                Assert.That(EtosProjectContext.OpenCandidate(other.Runtime, candidate.Id), Is.Null);
                Assert.That(h.Runtime.Journal.Exists(candidate.Id), Is.False, "retained P4.2 sample arrives before preview/journaling");
            }
            finally { field.SetValue(StudioUiSession.instance, previous); }
        }

        private sealed class DelayedSource : IPcmSource
        {
            public bool Started;
            public bool Ready;
            private bool _read;
            public string Name => "delayed virtual source";
            public bool Finished => _read;
            public void Start() { Started = true; }
            public void Stop() { }
            public byte[] Read() { if (!Ready || _read) return Array.Empty<byte>(); _read = true; return Enumerable.Range(0, 4802).Select(n => (byte)(n % 251)).ToArray(); }
            public void Dispose() { }
        }
    }
}
