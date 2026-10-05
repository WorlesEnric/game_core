#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Audio.Editor;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameCore.Studio.Etos.Tests
{
    public sealed class R2EtosTests
    {
        [Test]
        public void R2_13_ProjectIdentityPersistsAndTrustedContextRebinds()
        {
            string root = Path.Combine(Path.GetTempPath(), "r2-d-context-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            try
            {
                File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectSettings.asset"), "productGUID: " + new string('a', 32));
                string id = EtosProjectContext.LoadProjectId(root);
                Assert.That(id, Is.EqualTo(Json.Sha256Hex(Encoding.UTF8.GetBytes(new string('a', 32) + "\n" + root))));
                Assert.That(EtosProjectContext.LoadProjectId(root), Is.EqualTo(id));
                Assert.That(File.Exists(Path.Combine(root, "UserSettings", "GameCoreStudio.Project.json")), Is.True);
                using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
                for (int reload = 0; reload < 2; reload++)
                {
                    StageAdmission.Of(h.Runtime).Options.StageService = null;
                    EtosProjectContext.Bind(h.Runtime, h.Client);
                    var options = StageAdmission.Of(h.Runtime).Options;
                    Assert.That(options.StageService, Is.TypeOf<CompanionStageService>());
                    Assert.That(options.ProjectId, Is.EqualTo(h.Client.Options.ProjectId));
                    Assert.That(options.SourceRevision!(), Does.Match("^[a-f0-9]{40}$"));
                    Assert.That(options.CatalogRevision!(), Is.EqualTo(h.Runtime.Registry.Catalog.Revision));
                    Assert.That(h.Gateway, Is.InstanceOf<ICandidateStageGateway>());
                }
            }
            finally { Directory.Delete(root, true); }
        }

        [UnityTest]
        public IEnumerator R2_20_MainThreadQueueMustHandleBeforeCursorSaveAndReloadReplays()
        {
            using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
            h.Fake!.Emit("task_progress", "cs_test", new JObject());
            int received = 0;
            var handler = h.Gateway.Events.HandleAsync!;
            h.Gateway.Events.HandleAsync = (frame, token) => { Interlocked.Increment(ref received); return handler(frame, token); };
            h.Gateway.Events.Start();
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref received) == 0 && DateTime.UtcNow < deadline) yield return null;
            Assert.That(Volatile.Read(ref received), Is.EqualTo(1));
            // Do not pump: domain teardown must cancel the queued acknowledgment without deadlocking.
            Assert.That(h.Cursors.Load(), Is.Zero);
            h.Gateway.Events.Dispose();
            h.Queue.Pump();
            Assert.That(h.Cursors.Load(), Is.Zero);
            using var resumed = new EventStream(h.Client, h.Cursors);
            bool handled = false;
            resumed.HandleAsync = (frame, token) => h.Queue.Run(() => handled = true, token);
            resumed.Start();
            yield return h.Until(() => h.Cursors.Load() == 1, 10, "acknowledged replay");
            Assert.That(handled, Is.True);
            resumed.Dispose();
            yield return h.Until(() => h.Fake.EventConnections == 0, 10, "disposed connections");
        }

        [UnityTest]
        public IEnumerator R2_21_CancelDuringTicketedConnectDisposesConnection()
        {
            using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
            h.Fake!.StallWebSocketUpgrade = true;
            for (int i = 0; i < 3; i++)
            {
                using var cancel = new CancellationTokenSource();
                var connect = h.Client.ConnectWebSocketAsync("/v1/events", "after=0", cancel.Token);
                yield return h.Until(() => h.Fake.PendingUpgrades == 1, 10, "pending upgrade");
                cancel.Cancel();
                yield return h.Await(connect);
                Assert.That(connect.IsCanceled, Is.True);
                yield return h.Until(() => h.Fake.PendingUpgrades == 0, 10, "cancelled connection closed");
            }
        }

        [Test]
        public void R2_22_23_DiagnosticRetainsSanitizedStructuredTimeout()
        {
            EtosError error = EtosError.FromBody(504, new JObject
            {
                ["code"] = "transport", ["message"] = "etp_private", ["hint"] = "retry same body",
                ["data"] = new JObject { ["op"] = "tts", ["key"] = "private-effect" },
                ["diagnostics"] = new JArray(new JObject { ["secret"] = "hidden", ["message"] = "sk-private" }),
            }.ToString());
            Diagnostic diagnostic = EtosAgentGateway.DiagnosticOf(error);
            Assert.That(diagnostic.Code, Is.EqualTo("transport"));
            Assert.That(diagnostic.Hint, Is.EqualTo("retry same body"));
            Assert.That((int)diagnostic.Data!["status"]!, Is.EqualTo(504));
            Assert.That((string?)diagnostic.Data["op"], Is.EqualTo("tts"));
            Assert.That(StudioJson.Serialize(diagnostic), Does.Not.Contain("private").And.Not.Contain("hidden"));
        }

        [UnityTest]
        public IEnumerator R2_22_EventProgressAndNestedDiagnosticsAreSanitizedBeforeUI()
        {
            using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
            RequestView? view = null;
            h.Gateway.RequestChanged += update => view = update;
            h.Fake!.Emit("task_progress", "cs_test", new JObject { ["text"] = "ett_private" });
            h.Fake.Emit("candidate_invalid", "cs_test", new JObject { ["diagnostics"] = new JArray(new JObject
            {
                ["code"] = "candidate_invalid", ["message"] = "invalid", ["hint"] = "eta_private",
                ["data"] = new JObject { ["accessToken"] = "hidden", ["detail"] = "sk-private" },
            }) });
            h.Gateway.Events.Start();
            yield return h.Until(() => h.Cursors.Load() == 2, 10, "sanitized UI updates");
            Assert.That(view, Is.Not.Null);
            Assert.That(view!.Progress, Does.Not.Contain("private"));
            Assert.That(StudioJson.Serialize(view.Diagnostics[0]), Does.Not.Contain("private").And.Not.Contain("hidden"));
        }

        [Test]
        public void R2_24_SceneContextBoundsObjectsBytesAndRedactsNames()
        {
            using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
            var obj = new GameObject("etk_private " + new string('x', 2000));
            try
            {
                UnityEngine.Object[] selection = Enumerable.Repeat<UnityEngine.Object>(obj, 100000).ToArray();
                Attachment attachment = AgentRequestBuilder.SceneContext(h.Runtime, selection)!;
                Assert.That(attachment.Data.Length, Is.LessThanOrEqualTo(AgentRequestBuilder.SceneContextBytes));
                string text = Encoding.UTF8.GetString(attachment.Data);
                JObject context = JObject.Parse(text);
                Assert.That((bool)context["truncated"]!, Is.True);
                Assert.That(((JArray)context["objects"]!).Count, Is.LessThanOrEqualTo(AgentRequestBuilder.SceneContextObjects));
                Assert.That(text, Does.Not.Contain("etk_private"));
            }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
        }

        [UnityTest]
        public IEnumerator R2_41_AudioToolDiscoversGatewayAndImportsVerifiedVoiceThroughEngine()
        {
            using var h = Hollowmere.P2_2.GatewayHarness.WithFake();
            h.Gateway.Options.GeneratedFolder = "Assets/Hollowmere/Tests/P2_2/Temp";
            h.Gateway.Options.MaxCostUsd = 0.07;
            var field = typeof(EtosStudioSession).GetField("_gateway", BindingFlags.Instance | BindingFlags.NonPublic)!;
            object? old = field.GetValue(EtosStudioSession.instance);
            field.SetValue(EtosStudioSession.instance, h.Gateway);
            var bank = ScriptableObject.CreateInstance<AudioBankDefinition>();
            string? path = null;
            try
            {
                var providers = Resources.FindObjectsOfTypeAll<ScriptableObject>().OfType<IMediaGenerationGatewayProvider>().ToList();
                string seen = "[R2_41] media gateway lookup saw " + providers.Count + " provider(s): " + string.Join(", ", providers.Select(p => p.GetType().FullName + " -> " + (p.MediaGateway?.GetType().FullName ?? "null")))
                    + "; EtosStudioSession implements the provider: " + typeof(IMediaGenerationGatewayProvider).IsAssignableFrom(typeof(EtosStudioSession));
                Debug.Log(seen);
                Assert.That(MediaGateways.Resolve(), Is.TypeOf<EtosMediaGenerator>(), seen);
                MediaGenerationResult request = AudioTools.GenerateVoice(bank, "greeting", "Welcome", "voice", "speaker");
                Assert.That(request.Status, Is.EqualTo(MediaGenerationStatus.Requested));
                path = h.Gateway.Options.GeneratedFolder + "/voice-" + request.RequestId + ".wav";
                yield return h.Until(() => File.Exists(Path.Combine(h.Runtime.Paths.ProjectRoot, path)), 30, "voice asset.import");
                JObject sent = JObject.Parse(h.Fake!.Calls.Single(c => c.Path.EndsWith("/v1/ops/generate")).Body);
                Assert.That((double)sent["max_cost_usd"]!, Is.EqualTo(0.07));
                Assert.That(AssetDatabase.LoadAssetAtPath<AudioClip>(path), Is.Not.Null);
                ChangeSet entry = h.Runtime.Journal.Read(h.Runtime.Journal.List().Single().Id)!;
                Assert.That(entry.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
                Assert.That(entry.Operations.Single().Tool, Is.EqualTo(BuiltInToolIds.AssetImport));
                MediaGenerationResult sfx = AudioTools.GenerateSfx(bank, "splash", "water");
                Assert.That(sfx.Status, Is.EqualTo(MediaGenerationStatus.NotConfigured));
                Assert.That(h.Fake.Calls.Count(c => c.Path.EndsWith("/v1/ops/generate")), Is.EqualTo(1));
                h.Fake.TamperArtifacts = true;
                var adapter = new EtosMediaGenerator(h.Gateway, h.Runtime, h.Queue);
                var bad = adapter.GenerateSpeechAsync("tampered", path + ".bad.wav");
                yield return h.Await(bad);
                Assert.That(bad.Result.Problem!.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
                Assert.That(File.Exists(Path.Combine(h.Runtime.Paths.ProjectRoot, path + ".bad.wav")), Is.False);
            }
            finally
            {
                field.SetValue(EtosStudioSession.instance, old);
                UnityEngine.Object.DestroyImmediate(bank);
                if (path != null) AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
