#nullable enable
using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class R4RegressionTests
    {
        [Test]
        public async Task P42_STAGE_01_AppIntakeSignsExactPayloadAndRejectsTampering()
        {
            using var setup = new FakeSetup();
            MethodInfo? method = typeof(CompanionClient).GetMethod("StageAppCandidateAsync");
            Assert.That(method, Is.Not.Null, "retained W-MECH-01 app-origin intake is missing");
            string id = Samples.ChangeSetId();
            byte[] bytes = Encoding.UTF8.GetBytes("retained package fixture");
            JObject candidate = Samples.ChangeSet(id, Json.Sha256Hex(bytes), bytes.Length);
            candidate["operations"]![0]!["tool"] = "mechanism.propose";
            object[] args = { id, setup.Options.ProjectId, "revision", Samples.CatalogRevision, candidate, Samples.Catalog(), new[] { bytes }, CancellationToken.None };
            var job = await (Task<StageJobInfo>)method!.Invoke(setup.Client, args)!;
            Assert.That(job.JobId, Is.Not.Empty);
            var call = setup.Fake.Calls.Last();
            Assert.That(call.Path, Does.EndWith("/v1/stage/app-candidate"));
            JObject envelope = JObject.Parse(call.Body);
            JObject payload = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String((string)envelope["payloadBase64"]!)));
            Assert.That(JToken.DeepEquals(payload["changeSet"], candidate), Is.True);
            Assert.That(JToken.DeepEquals(payload["toolCatalog"], Samples.Catalog()), Is.True);
            Assert.That(Convert.FromBase64String((string)payload["files"]![0]!["bytesBase64"]!), Is.EqualTo(bytes));
            Assert.That(Json.FirstNull(payload), Is.Null);
            payload["request"]!["sourceRevision"] = "tampered";
            envelope["payloadBase64"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(Json.Write(payload)));
            using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
            using var request = new HttpRequestMessage(HttpMethod.Post, setup.Fake.NodeUrl + call.Path);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + FakeCompanion.AppKey);
            request.Headers.TryAddWithoutValidation("X-Etos-App", "gamecore-unity");
            request.Headers.TryAddWithoutValidation("X-GameCore-Project", setup.Options.ProjectId);
            request.Headers.TryAddWithoutValidation("X-GameCore-Stage-Key", FakeCompanion.AppKey);
            request.Content = new StringContent(Json.Write(envelope), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(request);
            Assert.That((int)response.StatusCode, Is.EqualTo(403));
            Assert.That((string?)JObject.Parse(await response.Content.ReadAsStringAsync())["code"], Is.EqualTo("forbidden"));
            Assert.That(setup.Lines.Any(setup.Credentials.AppearsIn), Is.False);
        }
    }
}
