#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
namespace P42d.Receipt
{
    public sealed class ReceiptTests
    {
        [Test]
        public async Task R2_09_13_P42d_InstalledSignedRecordVerifiesAndNamesMissingWorldDelta()
        {
            string project = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!;
            string job = Environment.GetEnvironmentVariable("GAMECORE_P42D_STAGE_JOB")!;
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            using var client = new CompanionClient(new EtosClientOptions { ProjectId = project, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
            JObject record = await client.FetchTrustedVerdictAsync(job);
            Assert.That(await client.VerifyVerdictAsync(job, record), Is.True);
            Assert.That((string?)record["projectId"], Is.EqualTo(project));
            Assert.That((string?)record["sourceRevision"], Is.EqualTo("40fb91fa672c292d830afbbdca97bc4a99a2a6b6"));
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!;
            File.WriteAllText(Path.Combine(output, "signed-record.json"), record.ToString());
            File.WriteAllText(Path.Combine(output, "verified.json"), new JObject { ["jobId"] = job, ["verified"] = true,
                ["worldPresent"] = record["catalogDelta"]?["world"] != null, ["predictedPresent"] = record["catalogDelta"]?["predicted"] != null }.ToString());
        }
    }
}
