#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace P42i.Receipt
{
    public sealed class MediaReceiptTests
    {
        private static CompanionClient Client()
        {
            var credentials = EtosCredentials.FromKeyFile(Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable)!);
            return new CompanionClient(new EtosClientOptions { ProjectId = Environment.GetEnvironmentVariable("GAMECORE_ETOS_PROJECT_ID")!, NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410" }, credentials);
        }
        private static string Digest => Environment.GetEnvironmentVariable("GAMECORE_P42I_ARTIFACT")!;
        private static void Write(string name, JObject value) => File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!, name + ".json"), value.ToString());

        [Test]
        public async Task R2_38_CurrentGeneratedTextureTamperRefusedWithoutRegeneration()
        {
            Assert.That(Digest, Does.Match("^[0-9a-f]{64}$"));
            using var client = Client();
            VerifiedArtifact original = await client.DownloadArtifactAsync(Digest);
            Assert.That(original.Sha256, Is.EqualTo(Digest));
            client.Options.DownloadTamperHook = path =>
            {
                byte[] bytes = File.ReadAllBytes(path);
                bytes[bytes.Length / 2] ^= 0x5a;
                File.WriteAllBytes(path, bytes);
            };
            EtosException refusal = Assert.ThrowsAsync<EtosException>(() => client.DownloadArtifactAsync(Digest))!;
            client.Options.DownloadTamperHook = null;
            Assert.That(refusal.Code, Is.EqualTo(EtosCodes.ArtifactDigestMismatch));
            VerifiedArtifact unchanged = await client.DownloadArtifactAsync(Digest);
            Assert.That(unchanged.Bytes, Is.EqualTo(original.Bytes));
            Write("tamper", new JObject { ["status"] = "PASS", ["sha256"] = Digest, ["refusalCode"] = refusal.Code,
                ["originalBytesUnchanged"] = true, ["generationCalls"] = 0 });
        }

        [Test]
        public async Task R2_38_DescribeCurrentOwnedTextureUnderBoundTariff()
        {
            Assert.That(Environment.GetEnvironmentVariable("GAMECORE_P42I_DESCRIBE_RESERVED"), Is.EqualTo("1"));
            using var client = Client();
            var result = await client.GenerateAsync(new GenerateBody("describe", new JObject { ["artifact"] = Digest }) { MaxCostUsd = 0.01 });
            Write("describe", result.Raw);
            Assert.That(result.Text, Is.Not.Null.And.Not.Empty);
            Assert.That((string?)result.Raw["charge"]?["tariff"]?["kind"], Is.EqualTo("operator"));
            Assert.That((double?)result.Raw["charge"]?["costUsd"], Is.EqualTo(0.01));
        }
    }
}
