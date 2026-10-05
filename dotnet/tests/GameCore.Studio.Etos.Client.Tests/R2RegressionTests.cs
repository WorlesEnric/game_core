#nullable enable
using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class R2RegressionTests
    {
        [Test]
        public async Task R2_13_StageUsesExactOwnedContractAndVerifiesUnmodifiedRecord()
        {
            using var setup = new FakeSetup();
            var request = new StageCandidateRequest(Samples.ChangeSetId(), setup.Options.ProjectId, "/trusted/project",
                new string('b', 40), Samples.CatalogRevision, new string('c', 64), new string('d', 64), Array.Empty<string>());
            IStageService service = new CompanionStageService(setup.Client);
            string job = await service.RequestStage(request);
            JObject sent = JObject.Parse(setup.Fake.Calls.Last().Body);
            Assert.That(sent.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "changeSetId", "projectId", "sourceRevision", "catalogRevision" }));
            setup.Fake.SignedStageVerdict = new JObject
            {
                ["jobId"] = job, ["signature"] = "opaque-companion-signature", ["projectId"] = request.ProjectId,
                ["changeSetId"] = request.ChangeSetId, ["sourceRevision"] = request.SourceRevision,
                ["catalogRevision"] = request.CatalogRevision, ["packageDigest"] = request.PackageDigest,
                ["proposalDigest"] = request.ProposalDigest, ["verdictRef"] = new string('e', 64),
                ["artifacts"] = new JArray(new JObject { ["role"] = "package", ["sha256"] = request.PackageDigest },
                    new JObject { ["role"] = "proposal", ["sha256"] = request.ProposalDigest }),
            };
            SignedVerdict signed = await service.GetVerdict(job);
            Assert.That(signed.Verdict["verdictRef"]!.Value<string>(), Is.EqualTo(new string('e', 64)));
            Assert.That((await service.VerifyVerdict(job, new StageVerificationRequest(signed, request))).Verified, Is.True);
            Assert.That(JToken.DeepEquals(JObject.Parse(setup.Fake.Calls.Last().Body), setup.Fake.SignedStageVerdict), Is.True);
            setup.Fake.VerifyStageVerdict = false;
            Assert.That((await service.VerifyVerdict(job, new StageVerificationRequest(signed, request))).Verified, Is.False);
            setup.Fake.VerifyStageVerdict = true;
            var changed = new StageCandidateRequest(request.ChangeSetId, request.ProjectId, request.SourceProject,
                "changed", request.CatalogRevision, request.PackageDigest, request.ProposalDigest, Array.Empty<string>());
            Assert.That((await service.VerifyVerdict(job, new StageVerificationRequest(signed, changed))).Verified, Is.False);
            var inputs = new StageCandidateRequest(request.ChangeSetId, request.ProjectId, request.SourceProject,
                request.SourceRevision, request.CatalogRevision, request.PackageDigest, request.ProposalDigest, new[] { "Assets/input.json" });
            Assert.That((await service.VerifyVerdict(job, new StageVerificationRequest(signed, inputs))).Verified, Is.False);
            Assert.That(setup.Fake.Calls.All(c => c.ProjectId == request.ProjectId), Is.True);
            setup.Options.ProjectId = new string('f', 64);
            Assert.That(Assert.ThrowsAsync<EtosException>(() => service.GetVerdict(job))!.Error.Status, Is.EqualTo(404));
            Assert.That(Assert.ThrowsAsync<EtosException>(() => service.VerifyVerdict(job, new StageVerificationRequest(signed, request)))!.Error.Status, Is.EqualTo(404));
        }

        [Test]
        public async Task R2_20_UnhandledEventIsReplayedAfterStopAndThrowDoesNotAcknowledge()
        {
            using var setup = new FakeSetup();
            var cursors = new MemoryCursorStore();
            setup.Fake.Emit("stage", "cs_test", new JObject());
            var queued = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var stream = new EventStream(setup.Client, cursors))
            {
                stream.HandleAsync = async (frame, token) =>
                {
                    queued.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, token);
                };
                stream.Start();
                await queued.Task;
                Assert.That(cursors.Load(), Is.Zero);
                await stream.StopAsync();
                Assert.That(cursors.Load(), Is.Zero);
            }
            using (var stream = new EventStream(setup.Client, cursors, new BackoffPolicy(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(50))))
            {
                stream.HandleAsync = (frame, token) => throw new Exception("ett_private-ticket");
                stream.Start();
                await Wait.Until(() => stream.LastError != null, TimeSpan.FromSeconds(5), "handler diagnostic");
                Assert.That(stream.LastError!.Code, Is.EqualTo("event_handler_failed"));
                Assert.That(stream.LastError.ToString(), Does.Not.Contain("private-ticket"));
                Assert.That(cursors.Load(), Is.Zero);
                stream.HandleAsync = (frame, token) => Task.CompletedTask;
                await Wait.Until(() => cursors.Load() == 1, TimeSpan.FromSeconds(5), "replayed acknowledgment");
            }
        }

        [Test]
        public async Task R2_21_CancelTicketedConnectAndRepeatedReloadLeaveNoConnections()
        {
            using var setup = new FakeSetup();
            setup.Fake.StallWebSocketUpgrade = true;
            for (int i = 0; i < 3; i++)
            {
                using var cancel = new CancellationTokenSource();
                Task<ClientWebSocket> connect = setup.Client.ConnectWebSocketAsync("/v1/events", "after=0", cancel.Token);
                await Wait.Until(() => setup.Fake.PendingUpgrades == 1, TimeSpan.FromSeconds(5), "pending handshake");
                cancel.Cancel();
                try { using ClientWebSocket unexpected = await connect; Assert.Fail("connect must cancel"); }
                catch (OperationCanceledException) { }
                await Wait.Until(() => setup.Fake.PendingUpgrades == 0, TimeSpan.FromSeconds(5), "closed handshake");
            }
            setup.Fake.StallWebSocketUpgrade = false;
            for (int i = 0; i < 3; i++)
            {
                using var stream = new EventStream(setup.Client, new MemoryCursorStore());
                stream.Start();
                await Wait.Until(() => setup.Fake.EventConnections == 1, TimeSpan.FromSeconds(5), "one connection");
                await stream.StopAsync();
                await Wait.Until(() => setup.Fake.EventConnections == 0, TimeSpan.FromSeconds(5), "reload closed connection");
            }
            Assert.That(setup.Fake.Calls.Where(c => c.Path.EndsWith("/v1/events")).All(c => c.ProjectId == setup.Options.ProjectId), Is.True);
        }

        [Test]
        public void R2_22_23_TimeoutRetainsRedactedStructuredDataWithoutRawException()
        {
            using var setup = new FakeSetup();
            setup.Fake.FailNext("/v1/ops/generate", 504, new JObject
            {
                ["code"] = "transport", ["message"] = "ett_hidden-ticket", ["hint"] = "retry same request",
                ["data"] = new JObject { ["key"] = "effect-private", ["op"] = "tts", ["nested"] = new JObject { ["apiToken"] = "private-value" } },
                ["diagnostics"] = new JArray(new JObject { ["detail"] = "sk-private", ["secret"] = new JObject { ["value"] = "private-object" } }),
            });
            var error = Assert.ThrowsAsync<EtosException>(() => setup.Client.GenerateAsync(new GenerateBody("tts", new JObject { ["text"] = "hello" })))!;
            Assert.That(error.Error.Status, Is.EqualTo(504));
            Assert.That(error.Code, Is.EqualTo("transport"));
            Assert.That(error.Error.Hint, Is.EqualTo("retry same request"));
            Assert.That(error.Error.Data!["op"]!.Value<string>(), Is.EqualTo("tts"));
            Assert.That(error.Error.Data["key"]!.Value<string>(), Is.EqualTo("[redacted]"));
            string diagnostics = error.Error.Diagnostics!.ToString() + error.Error.Data.ToString();
            Assert.That(diagnostics, Does.Not.Contain("private"));
            var wrapped = EtosException.Transport("failed", new Exception("Bearer raw-secret"));
            Assert.That(wrapped.InnerException, Is.Null);
            Assert.That(wrapped.ToString(), Does.Not.Contain("raw-secret"));
        }
    }
}
