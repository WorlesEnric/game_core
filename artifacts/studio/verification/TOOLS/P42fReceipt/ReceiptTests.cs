#nullable enable
using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace P42f.Receipt
{
    public sealed class ReceiptTests
    {
        [Test]
        public async Task R2_09_13_P42f_InstalledSignedRecordBindsCurrentMain()
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
            Assert.That((string?)record["sourceRevision"], Is.EqualTo("4ac7ba858b91e73e2d5de9dc6f02852c13feec56"));
            Assert.That((string?)record["catalogDelta"]?["world"], Does.Match("^[0-9a-f]{64}$"));
            Assert.That((string?)record["catalogDelta"]?["predicted"], Does.Match("^[0-9a-f]{64}$"));
        }
    }
}
