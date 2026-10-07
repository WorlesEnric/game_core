#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace P42i.Receipt
{
    public sealed class ReceiptTests
    {
        [Test]
        public async Task R2_09_13_P42i_InstalledSignedRecordBindsCurrentMain()
        {
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            using var client = new CompanionClient(new EtosClientOptions { ProjectId = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
            string job = Environment.GetEnvironmentVariable("GAMECORE_P42E_STAGE_JOB")!;
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!;
            JObject record = await client.FetchTrustedVerdictAsync(job);
            using (var file = File.Create(Path.Combine(output, "signed-record-original.json.gz")))
            using (var gzip = new GZipStream(file, CompressionMode.Compress))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(record.ToString());
                gzip.Write(bytes, 0, bytes.Length);
            }
            bool verified = await client.VerifyVerdictAsync(job, record);
            File.WriteAllText(Path.Combine(output, "verified.json"), new JObject { ["jobId"] = job, ["verified"] = verified, ["sourceRevision"] = record["sourceRevision"] }.ToString());
            Assert.That(verified, Is.True);
            Assert.That((string?)record["sourceRevision"], Is.EqualTo("cb5e2aa20263209df2dea4ee17aa23c50daec0e0"));
            Assert.That((string?)record["catalogDelta"]?["world"], Does.Match("^[0-9a-f]{64}$"));
            Assert.That((string?)record["catalogDelta"]?["predicted"], Does.Match("^[0-9a-f]{64}$"));
        }

        [Test]
        public async Task R2_38_P42i_CancelledStageRemainsUnauthorizableAfterCompanionRestart()
        {
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            using var client = new CompanionClient(new EtosClientOptions { ProjectId = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
            string job = Environment.GetEnvironmentVariable("GAMECORE_P42E_STAGE_JOB")!;
            var status = await client.GetStageAsync(job);
            Assert.That(status.State, Is.EqualTo("cancelled"));
            var refusal = Assert.ThrowsAsync<EtosException>(() => client.FetchTrustedVerdictAsync(job));
            Assert.That(refusal!.Error.Status, Is.EqualTo(404));
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "cancelled-after-restart.json"),
                new JObject { ["jobId"] = job, ["state"] = status.State, ["verdictHttpStatus"] = refusal.Error.Status, ["status"] = "PASS" }.ToString());
        }

        [Test]
        public async Task R2_38_P42i_DiscardTerminalOwnedSlotsPreservesDurableJobs()
        {
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            string project = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!;
            using var client = new CompanionClient(new EtosClientOptions { ProjectId = project, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
            var requests = JArray.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("GAMECORE_P42I_DISCARD_REQUESTS")!));
            var receipts = new JArray();
            foreach (JToken request in requests)
            {
                Assert.That((string?)request["request"]?["projectId"], Is.EqualTo(project));
                string job = (string)request["jobId"]!;
                var before = await client.GetStageAsync(job);
                Assert.That(before.State, Is.AnyOf("done", "failed", "cancelled"));
                JObject? signed = null;
                try { signed = await client.FetchTrustedVerdictAsync(job); }
                catch (EtosException refusal) { Assert.That(refusal.Error.Status, Is.EqualTo(404)); }
                var result = await client.DiscardStageAsync((string)request["request"]!["changeSetId"]!);
                var after = await client.GetStageAsync(job);
                Assert.That(after.State, Is.EqualTo(before.State));
                if (signed != null) Assert.That(await client.VerifyVerdictAsync(job, signed), Is.True);
                receipts.Add(new JObject { ["jobId"] = job, ["state"] = after.State, ["discard"] = result, ["issuedVerdictStillVerifies"] = signed != null });
            }
            File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, "discarded-terminal-slots.json"), receipts.ToString());
        }
    }
}
