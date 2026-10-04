// Replays answers recorded on the REAL node (Fixtures/*.json, written by LiveTests with GC_ETOS_FIXTURE_OUT and
// redacted): every recorded body parses with the client's shapes, carries no credential, and the recorded hello and
// refusals behave identically when the fake serves them.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class FixtureReplayTests
    {
        private static string FixtureDirectory => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures");

        public static IEnumerable<string> FixtureFiles()
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            return Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").Select(Path.GetFileName).Where(n => n != null).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal) : Enumerable.Empty<string>();
        }

        [Test]
        public void Fixtures_ArePresent()
        {
            Assert.That(FixtureFiles().ToList(), Is.Not.Empty, "record fixtures with studio/tools/live-etos-tests.sh");
        }

        [TestCaseSource(nameof(FixtureFiles))]
        public void Fixture_ParsesWithTheClientShapesAndHoldsNoSecret(string file)
        {
            string text = File.ReadAllText(Path.Combine(FixtureDirectory, file));
            Assert.That(EtosRedaction.ContainsSecret(text), Is.False, file + " carries a credential");
            JObject fixture = JObject.Parse(text);
            string kind = (string?)fixture["kind"] ?? string.Empty;
            int status = (int?)fixture["status"] ?? 0;
            JObject body = (JObject)fixture["body"]!;
            switch (kind)
            {
                case "hello":
                    HelloInfo hello = new HelloInfo(body);
                    Assert.That(hello.Service, Is.EqualTo("gamecore-studio"));
                    Assert.That(hello.Providers.Keys, Is.SupersetOf(new[] { "image", "tts", "3d", "describe", "voice" }));
                    break;
                case "submit":
                    SubmitResult submit = new SubmitResult(body);
                    Assert.That(submit.RequestId, Does.StartWith("cs_"));
                    Assert.That(submit.Request.State, Is.Not.Empty);
                    break;
                case "request":
                    RequestInfo request = new RequestInfo(body);
                    Assert.That(request.RequestId, Does.StartWith("cs_"));
                    Assert.That(request.Tasks, Is.Not.Null);
                    break;
                case "generate":
                    GenerateResult generate = new GenerateResult(body);
                    Assert.That(generate.Artifacts, Is.Not.Empty);
                    Assert.That(generate.Artifacts.All(a => Json.NormalizeSha256(a.Sha256) != null), Is.True);
                    Assert.That(generate.MaxCostUsd, Is.Not.Null);
                    break;
                case "event":
                    EventFrame? frame = EventFrame.Parse(body.ToString());
                    Assert.That(frame, Is.Not.Null);
                    Assert.That(frame!.Cursor, Is.GreaterThan(0));
                    break;
                case "error":
                    EtosError error = new EtosError(status, (string?)body["code"] ?? string.Empty, (string?)body["message"] ?? string.Empty, (string?)body["hint"]);
                    Assert.That(error.Code, Is.Not.Empty);
                    Assert.That(status, Is.GreaterThanOrEqualTo(400));
                    break;
                default:
                    Assert.Fail("unknown fixture kind '" + kind + "' in " + file);
                    break;
            }
        }

        [Test]
        public async Task RecordedHello_ServedByTheFake_ReadsTheSame()
        {
            string path = Path.Combine(FixtureDirectory, "hello.json");
            Assume.That(File.Exists(path), "no recorded hello yet");
            JObject recorded = (JObject)JObject.Parse(File.ReadAllText(path))["body"]!;
            using FakeSetup setup = new FakeSetup();
            setup.Fake.Hello = (JObject)recorded.DeepClone();
            HelloInfo served = await setup.Client.HelloAsync();
            HelloInfo direct = new HelloInfo(recorded);
            Assert.That(served.Providers, Is.EquivalentTo(direct.Providers));
            Assert.That(served.Version, Is.EqualTo(direct.Version));
        }

        [Test]
        public void RecordedRefusals_ServedByTheFake_KeepTheirCode()
        {
            string path = Path.Combine(FixtureDirectory, "error-generate-3d.json");
            Assume.That(File.Exists(path), "no recorded 3d refusal yet");
            JObject fixture = JObject.Parse(File.ReadAllText(path));
            JObject body = (JObject)fixture["body"]!;
            int status = (int)fixture["status"]!;
            using FakeSetup setup = new FakeSetup();
            JObject served = new JObject { ["code"] = body["code"], ["message"] = body["message"] };
            if (body["hint"] != null)
            {
                served["hint"] = body["hint"];
            }

            setup.Fake.OpRefusals["3d"] = Tuple.Create(status, served);
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => setup.Client.GenerateAsync(new GenerateBody("generate.3d", new JObject { ["prompt"] = "a well" })))!;
            Assert.That(refused.Code, Is.EqualTo((string?)body["code"]));
            Assert.That(refused.Error.Status, Is.EqualTo(status));
        }
    }
}
