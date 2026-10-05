// Every companion route through the fake node proxy: headers, shapes, idempotency, catalog handling, verified artifact
// downloads, ops with max_cost_usd, staging, and etos error pass-through with codes preserved.
#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class RoutesTests
    {
        [Test]
        public async Task Hello_GoesThroughTheProxyWithTheAppKeyAndReadsProviders()
        {
            using FakeSetup setup = new FakeSetup();
            HelloInfo hello = await setup.Client.HelloAsync();

            Assert.That(hello.Service, Is.EqualTo("gamecore-studio"));
            Assert.That(hello.Provider("image"), Is.EqualTo(ProviderStates.Live));
            Assert.That(hello.Provider("3d"), Is.EqualTo(ProviderStates.NotConfigured));
            Assert.That(hello.Provider("voice"), Is.EqualTo(ProviderStates.Unknown));
            Assert.That(hello.Provider("unheard-of"), Is.EqualTo(ProviderStates.Unknown));
            FakeCall call = setup.Fake.Calls.Last();
            Assert.That(call.Path, Is.EqualTo("/api/v1/agents/gamecore-studio/http/v1/hello"));
            Assert.That(call.Authorized, Is.True);
            Assert.That(call.App, Is.EqualTo("gamecore-unity"));
        }

        [Test]
        public async Task Hello_ToleratesNullMembersFromOlderCompanionBuilds()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.Hello["indexRevision"] = JValue.CreateNull();
            setup.Fake.Hello["providersCheckedAt"] = 1791154721705L;
            HelloInfo hello = await setup.Client.HelloAsync();
            Assert.That(hello.IndexRevision, Is.Null);
            Assert.That(hello.ProvidersCheckedAt, Is.EqualTo(1791154721705L));
        }

        [Test]
        public async Task Submit_SendsTheCatalogOnlyWhenTheCompanionLacksItAndIsIdempotent()
        {
            using FakeSetup setup = new FakeSetup();
            string id = Samples.ChangeSetId();

            EtosException stale = Assert.ThrowsAsync<EtosException>(() => setup.Client.SubmitAsync(Samples.Request(id, withCatalog: false)))!;
            Assert.That(stale.Code, Is.EqualTo(EtosCodes.StaleContext));
            Assert.That(stale.Error.Status, Is.EqualTo(409));
            Assert.That(stale.Error.Hint, Does.Contain("toolCatalog"));

            SubmitResult first = await setup.Client.SubmitAsync(Samples.Request(id));
            Assert.That(first.RequestId, Is.EqualTo(id));
            Assert.That(first.TaskId, Is.Not.Null.And.Not.Empty);
            Assert.That(first.State, Is.EqualTo(RequestStates.Running));

            HelloInfo hello = await setup.Client.HelloAsync();
            Assert.That(hello.HoldsCatalog(Samples.CatalogRevision), Is.True);
            Assert.That(hello.HoldsCatalog("sha256:" + Samples.CatalogRevision), Is.True);

            SubmitResult again = await setup.Client.SubmitAsync(Samples.Request(id, withCatalog: false));
            Assert.That(again.TaskId, Is.EqualTo(first.TaskId), "same change-set id, same task: no duplicate");

            EditRequestBody different = new EditRequestBody(id, "something else entirely", "agent", Samples.Selection(), Samples.Slice(), Samples.CatalogRevision);
            EtosException conflict = Assert.ThrowsAsync<EtosException>(() => setup.Client.SubmitAsync(different))!;
            Assert.That(conflict.Code, Is.EqualTo(EtosCodes.LedgerConflict));
        }

        [Test]
        public async Task Submit_WritesNoNullMembers()
        {
            using FakeSetup setup = new FakeSetup();
            EditRequestBody body = Samples.Request(Samples.ChangeSetId());
            body.Attachments.Add(new AttachmentBody("frame.png", "image/png", FakeMedia.TinyPng(), "frame"));
            await setup.Client.SubmitAsync(body);
            FakeCall call = setup.Fake.Calls.Last(c => c.Path.EndsWith("/v1/requests", StringComparison.Ordinal));
            JObject sent = JObject.Parse(call.Body);
            Assert.That(Json.FirstNull(sent), Is.Null);
            Assert.That(sent["intent"]!["voiceTranscriptId"], Is.Null, "absent optional members are omitted");
            Assert.That((string?)sent["attachments"]![0]!["sha256"], Is.EqualTo(Json.Sha256Hex(FakeMedia.TinyPng())));
        }

        [Test]
        public void Submit_RefusesBadIdsAndOversizedAttachmentsBeforeSending()
        {
            using FakeSetup setup = new FakeSetup();
            EtosException badId = Assert.ThrowsAsync<EtosException>(() => setup.Client.SubmitAsync(Samples.Request("cs_not-a-ulid")))!;
            Assert.That(badId.Code, Is.EqualTo(EtosCodes.BadRequest));

            EditRequestBody big = Samples.Request(Samples.ChangeSetId());
            big.Attachments.Add(new AttachmentBody("huge.bin", "application/octet-stream", new byte[(16 * 1024 * 1024) + 1]));
            EtosException tooLarge = Assert.ThrowsAsync<EtosException>(() => setup.Client.SubmitAsync(big))!;
            Assert.That(tooLarge.Code, Is.EqualTo(EtosCodes.TooLarge));
            Assert.That(setup.Fake.Calls.Count(c => c.Path.EndsWith("/v1/requests", StringComparison.Ordinal)), Is.EqualTo(0));
        }

        [Test]
        public async Task Requests_GetListAndCancel()
        {
            using FakeSetup setup = new FakeSetup();
            string id = Samples.ChangeSetId();
            await setup.Client.SubmitAsync(Samples.Request(id));

            RequestInfo running = await setup.Client.GetRequestAsync(id);
            Assert.That(running.State, Is.EqualTo(RequestStates.Running));
            Assert.That(running.Topic, Does.StartWith("#agent/gamecore-studio/cs-"));

            RequestPage page = await setup.Client.ListRequestsAsync(0);
            Assert.That(page.Requests.Select(r => r.RequestId), Does.Contain(id));
            Assert.That(page.Next, Is.EqualTo(running.Seq));
            RequestPage empty = await setup.Client.ListRequestsAsync(page.Next);
            Assert.That(empty.Requests, Is.Empty);

            RequestInfo cancelled = await setup.Client.CancelAsync(id);
            Assert.That(cancelled.State, Is.EqualTo(RequestStates.Cancelled));
            Assert.That(cancelled.TaskStatus, Is.EqualTo("cancelled"));
            Assert.That(cancelled.IsTerminal, Is.True);
            RequestInfo again = await setup.Client.CancelAsync(id);
            Assert.That(again.Seq, Is.EqualTo(cancelled.Seq), "a cancel after the end is a no-op");

            EtosException missing = Assert.ThrowsAsync<EtosException>(() => setup.Client.GetRequestAsync(Samples.ChangeSetId()))!;
            Assert.That(missing.Code, Is.EqualTo(EtosCodes.NotFound));
        }

        [Test]
        public async Task Candidate_AndItsArtifactAreFetchedAndVerified()
        {
            using FakeSetup setup = new FakeSetup();
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng(), "texture");
            setup.Fake.Worker = body => new FakeCandidate(Samples.ChangeSet((string)body["changeSetId"]!, png.Sha256, png.Bytes.Length), new[] { png });
            string id = Samples.ChangeSetId();
            await setup.Client.SubmitAsync(Samples.Request(id));
            RequestInfo done = await Wait.ForState(setup.Client, id, RequestStates.Candidate, TimeSpan.FromSeconds(10));
            Assert.That(done.HasCandidate, Is.True);
            Assert.That(done.OutcomeCode, Is.EqualTo("candidate"));

            CandidateInfo candidate = await setup.Client.GetCandidateAsync(id);
            Assert.That(candidate.ChangeSetId, Is.EqualTo(id));
            Assert.That(candidate.ToolCatalogRevision, Is.EqualTo(Samples.CatalogRevision));
            Assert.That((string?)candidate.ChangeSet["id"], Is.EqualTo(id));
            StoredArtifactInfo? stored = candidate.FindArtifact(png.Sha256);
            Assert.That(stored, Is.Not.Null);
            Assert.That(stored!.Bytes, Is.EqualTo(png.Bytes.Length));

            VerifiedArtifact bytes = await setup.Client.DownloadArtifactAsync("sha256:" + png.Sha256, png.Bytes.Length);
            Assert.That(bytes.Bytes, Is.EqualTo(png.Bytes));
            Assert.That(bytes.MediaType, Is.EqualTo("image/png"));
            Assert.That(Directory.GetFiles(setup.TempDirectory), Is.Empty, "the temp file is removed");
        }

        [Test]
        public async Task Artifact_TamperedBytesAreRefusedBeforeTheyAreHandedOver()
        {
            using FakeSetup setup = new FakeSetup();
            FakeArtifact png = new FakeArtifact("well.png", "image/png", FakeMedia.TinyPng());
            await setup.Client.GenerateAsync(new GenerateBody("image", new JObject { ["prompt"] = "x" }));
            string sha = setup.Fake.ImageOutput.Sha256;

            setup.Fake.TamperArtifacts = true;
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => setup.Client.DownloadArtifactAsync(sha))!;
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
            Assert.That(Directory.GetFiles(setup.TempDirectory), Is.Empty);

            setup.Fake.TamperArtifacts = false;
            VerifiedArtifact good = await setup.Client.DownloadArtifactAsync(sha);
            Assert.That(good.Sha256, Is.EqualTo(sha));
            Assert.That(png.Sha256, Is.EqualTo(sha));
        }

        [Test]
        public async Task Artifact_ATestHookFlippingAByteInTheTempFileIsRefused()
        {
            int hooked = 0;
            using FakeSetup setup = new FakeSetup(options => options.DownloadTamperHook = path =>
            {
                hooked++;
                byte[] bytes = File.ReadAllBytes(path);
                bytes[0] ^= 0x01;
                File.WriteAllBytes(path, bytes);
            });
            await setup.Client.GenerateAsync(new GenerateBody("image", new JObject { ["prompt"] = "x" }));
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => setup.Client.DownloadArtifactAsync(setup.Fake.ImageOutput.Sha256))!;
            Assert.That(hooked, Is.EqualTo(1));
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
        }

        [Test]
        public async Task Artifact_ASizeOtherThanDeclaredIsRefused()
        {
            using FakeSetup setup = new FakeSetup();
            await setup.Client.GenerateAsync(new GenerateBody("image", new JObject { ["prompt"] = "x" }));
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => setup.Client.DownloadArtifactAsync(setup.Fake.ImageOutput.Sha256, 1))!;
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.ArtifactSizeMismatch));
            EtosException notDigest = Assert.ThrowsAsync<EtosException>(() => setup.Client.DownloadArtifactAsync("abc"))!;
            Assert.That(notDigest.Code, Is.EqualTo(EtosCodes.BadRequest));
        }

        [Test]
        public async Task IndexDelta_IsAcknowledged()
        {
            using FakeSetup setup = new FakeSetup();
            JObject delta = new JObject { ["project"] = "hollowmere", ["revision"] = 7, ["nodes"] = new JArray(new JObject { ["ref"] = new JObject { ["kind"] = "Entity", ["authoringId"] = "e1" }, ["type"] = "entity.definition" }), ["edges"] = new JArray(), ["removals"] = new JArray() };
            IndexDeltaAck ack = await setup.Client.PostIndexDeltaAsync(delta);
            Assert.That(ack.Revision, Is.EqualTo(7));
            Assert.That(ack.Queued, Is.EqualTo(1));
        }

        [Test]
        public async Task Generate_AlwaysSendsACostCeilingAndPassesRefusalsThrough()
        {
            using FakeSetup setup = new FakeSetup(options => options.DefaultMaxCostUsd = 0.5);
            GenerateResult image = await setup.Client.GenerateAsync(new GenerateBody("generate.image", new JObject { ["prompt"] = "wooden well icon", ["size"] = "256x256" }));
            JObject sent = JObject.Parse(setup.Fake.Calls.Last(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body);
            Assert.That((string?)sent["op"], Is.EqualTo("image"), "etos op names map to the companion's");
            Assert.That((double)sent["max_cost_usd"]!, Is.EqualTo(0.5));
            Assert.That(image.EtosOp, Is.EqualTo("generate.image"));
            Assert.That(image.Artifacts, Has.Count.EqualTo(1));
            Assert.That(image.MaxCostUsd, Is.EqualTo(0.5));

            await setup.Client.GenerateAsync(new GenerateBody("tts", new JObject { ["text"] = "Welcome to Thornwick" }) { MaxCostUsd = 0.1 });
            sent = JObject.Parse(setup.Fake.Calls.Last(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body);
            Assert.That((double)sent["max_cost_usd"]!, Is.EqualTo(0.1));

            GenerateResult describe = await setup.Client.GenerateAsync(new GenerateBody("describe", new JObject { ["artifact"] = image.Artifacts[0].Sha256 }));
            Assert.That(describe.Text, Does.Contain("well"));
            sent = JObject.Parse(setup.Fake.Calls.Last(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body);
            Assert.That(sent["max_cost_usd"], Is.Not.Null, "describe carries the ceiling too");

            EtosException blocked = Assert.ThrowsAsync<EtosException>(() => setup.Client.GenerateAsync(new GenerateBody("generate.3d", new JObject { ["prompt"] = "a well" })))!;
            Assert.That(blocked.Code, Is.EqualTo(EtosCodes.NotConfigured));
            Assert.That(blocked.Error.Status, Is.EqualTo(503));
            Assert.That(blocked.Error.Hint, Does.Contain("ops.toml"));
        }

        [Test]
        public async Task Stage_PostsAndReadsTheJob()
        {
            using FakeSetup setup = new FakeSetup();
            string id = Samples.ChangeSetId();
            StageJobInfo job = await setup.Client.StageAsync(id, setup.Options.ProjectId, new string('b', 40), Samples.CatalogRevision);
            Assert.That(job.State, Is.EqualTo("queued"));
            StageJobInfo read = await setup.Client.GetStageAsync(job.JobId);
            Assert.That(read.ChangeSetId, Is.EqualTo(id));
        }

        [Test]
        public void Errors_KeepTheirEtosCodeStatusAndHint()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.FailNext("/v1/hello", 503, new JObject { ["code"] = "agent_starting", ["message"] = "the companion has not been welcomed by the node yet", ["hint"] = "try again in a few seconds" });
            EtosException starting = Assert.ThrowsAsync<EtosException>(() => setup.Client.HelloAsync())!;
            Assert.That(starting.Code, Is.EqualTo(EtosCodes.AgentStarting));
            Assert.That(starting.Error.Status, Is.EqualTo(503));
            Assert.That(starting.Error.Hint, Is.EqualTo("try again in a few seconds"));

            setup.Fake.FailNext("/v1/ops/generate", 402, new JObject { ["code"] = "budget_exhausted", ["message"] = "the agent's budget is spent" });
            EtosException budget = Assert.ThrowsAsync<EtosException>(() => setup.Client.GenerateAsync(new GenerateBody("image", new JObject { ["prompt"] = "x" })))!;
            Assert.That(budget.Code, Is.EqualTo(EtosCodes.BudgetExhausted));
            Assert.That(EtosCodes.IsBlocked(budget.Code), Is.True);

            setup.Fake.FailNext("/v1/requests", 400, new JObject { ["code"] = "bad_request", ["message"] = "the request does not fit the contract schemas (1 finding(s))", ["diagnostics"] = new JArray(new JObject { ["code"] = "InvalidArgs", ["message"] = "selection: /mode" }) });
            EtosException schema = Assert.ThrowsAsync<EtosException>(() => setup.Client.SubmitAsync(Samples.Request(Samples.ChangeSetId())))!;
            Assert.That(schema.Error.Diagnostics, Has.Count.EqualTo(1));
            Assert.That((string?)schema.Error.Diagnostics![0]!["code"], Is.EqualTo("InvalidArgs"));
        }

        [Test]
        public void Errors_AnotherAgentsRouteIsForbiddenAndAWrongKeyUnauthorized()
        {
            using FakeSetup other = new FakeSetup(options => options.AgentName = "some-other-agent");
            EtosException forbidden = Assert.ThrowsAsync<EtosException>(() => other.Client.HelloAsync())!;
            Assert.That(forbidden.Code, Is.EqualTo(EtosCodes.Forbidden));
            Assert.That(forbidden.Error.Status, Is.EqualTo(403));

            using FakeSetup wrongKey = new FakeSetup(key: "etk_wrongwrongwrongwrongwrongwrong");
            EtosException unauthorized = Assert.ThrowsAsync<EtosException>(() => wrongKey.Client.HelloAsync())!;
            Assert.That(unauthorized.Code, Is.EqualTo(EtosCodes.Unauthorized));
        }

        [Test]
        public void Errors_AnUnreachableNodeIsTransport()
        {
            EtosClientOptions options = new EtosClientOptions { ProjectId = new string('a', 64), NodeUrl = "http://127.0.0.1:9", RequestTimeout = TimeSpan.FromSeconds(5) };
            using CompanionClient client = new CompanionClient(options, new EtosCredentials(FakeCompanion.AppKey, null, "fixture"));
            EtosException down = Assert.ThrowsAsync<EtosException>(() => client.HelloAsync())!;
            Assert.That(down.Code, Is.EqualTo(EtosCodes.Transport).Or.EqualTo(EtosCodes.Timeout));
            Assert.That(down.Message, Does.Not.Contain(FakeCompanion.AppKey));
        }

        [Test]
        public void Errors_NonEtosBodiesGetACodeFromTheStatus()
        {
            EtosError plain = EtosError.FromBody(502, "Bad Gateway");
            Assert.That(plain.Code, Is.EqualTo(EtosCodes.Transport));
            Assert.That(EtosError.FromBody(413, string.Empty).Code, Is.EqualTo(EtosCodes.TooLarge));
            Assert.That(EtosError.FromBody(503, "{\"error\":{\"code\":\"not_configured\",\"message\":\"m\"}}").Code, Is.EqualTo(EtosCodes.NotConfigured));
        }
    }
}
