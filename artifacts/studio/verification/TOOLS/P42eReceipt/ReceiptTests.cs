#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace P42e.Receipt
{
    public sealed class ReceiptTests
    {
        private static CompanionClient Client()
        {
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            return new CompanionClient(new EtosClientOptions { ProjectId = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
        }
        private static void Write(string name, JObject value) => File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, name + ".json"), value.ToString());
        [Test]
        public async Task R2_38_P42e_HelloOwnerDescribeAnd3dRefusal()
        {
            using var client = Client();
            var hello = await client.HelloAsync(); Write("hello", hello.Raw);
            var price = hello.Raw["tariffs"]!.Single(p => (string?)p["op"] == "describe")["tariff"]!;
            Assert.That((string?)price["kind"], Is.EqualTo("operator"));
            Assert.That((double)price["perUnitUsd"]!, Is.EqualTo(0.01));
            var error = Assert.ThrowsAsync<EtosException>(() => client.GenerateAsync(new GenerateBody("3d", new JObject { ["prompt"] = "a wooden well" }) { MaxCostUsd = 0 }));
            Write("3d-refusal", new JObject { ["code"] = error!.Code, ["message"] = error.Message });
            Assert.That(error.Code, Is.EqualTo("not_configured"));
        }
        [Test]
        public async Task R2_38_P42e_OnePricedImageThenOneDescribe()
        {
            Assert.That(Environment.GetEnvironmentVariable("GAMECORE_P42E_PAID"), Is.EqualTo("1"));
            using var client = Client();
            var hello = await client.HelloAsync(); Write("paid-hello", hello.Raw);
            var price = hello.Raw["tariffs"]!.Single(p => (string?)p["op"] == "describe")["tariff"]!;
            Assert.That((string?)price["kind"], Is.EqualTo("operator"));
            Assert.That((double)price["perUnitUsd"]!, Is.EqualTo(0.01));
            var image = await client.GenerateAsync(new GenerateBody("image", new JObject { ["prompt"] = "A simple small brass lantern inventory icon on a plain dark background." }) { MaxCostUsd = 0.20 });
            Write("image", image.Raw); Assert.That(image.Artifacts.Count, Is.EqualTo(1));
            var bytes = await client.DownloadArtifactAsync(image.Artifacts[0].Sha256);
            File.WriteAllBytes(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "describe-input.png"), bytes.Bytes);
            var result = await client.GenerateAsync(new GenerateBody("describe", new JObject { ["artifact"] = image.Artifacts[0].Sha256 }) { MaxCostUsd = 0.01 });
            Write("describe", result.Raw);
            Assert.That(result.Text, Is.Not.Null.And.Not.Empty);
            Assert.That((string?)result.Raw["charge"]?["tariff"]?["kind"], Is.EqualTo("operator"));
            Assert.That((double?)result.Raw["charge"]?["costUsd"], Is.EqualTo(0.01));
        }
        [Test]
        public async Task R2_09_13_P42e_InstalledSignedRecordHasWorldAndPredicted()
        {
            using var client = Client();
            string job = Environment.GetEnvironmentVariable("GAMECORE_P42E_STAGE_JOB")!;
            JObject record = await client.FetchTrustedVerdictAsync(job);
            Write("signed-record", record);
            using (var output = File.Create(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "signed-record-original.json.gz")))
            using (var gzip = new GZipStream(output, CompressionMode.Compress))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(record.ToString());
                gzip.Write(bytes, 0, bytes.Length);
            }
            bool verified = await client.VerifyVerdictAsync(job, record);
            Write("verified", new JObject { ["jobId"] = job, ["verified"] = verified });
            Assert.That(verified, Is.True);
            Assert.That((string?)record["sourceRevision"], Is.EqualTo("d140f7487092a40cbead927c5546127e0cb6fe14"));
            Assert.That((string?)record["catalogDelta"]?["world"], Does.Match("^[0-9a-f]{64}$"));
            Assert.That((string?)record["catalogDelta"]?["predicted"], Does.Match("^[0-9a-f]{64}$"));
        }
    }
}
