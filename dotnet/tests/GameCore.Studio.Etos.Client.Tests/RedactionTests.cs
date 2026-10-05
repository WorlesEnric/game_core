// SADR-018 / W-ETOS-01: no key, ticket, proxy token or bearer value in anything the client writes or shows.
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
    public sealed class RedactionTests
    {
        [TestCase("key etk_abcdefghijklmnop0123 end", "key [redacted] end")]
        [TestCase("ticket=ett_0001fakeTicketabc", "ticket=[redacted]")]
        [TestCase("proxy etp_ZZZZ-yyyy.xxxx", "proxy [redacted]")]
        [TestCase("agent eta_token_value", "agent [redacted]")]
        [TestCase("Authorization: Bearer abc.def-ghi", "Authorization: Bearer [redacted]")]
        [TestCase("authorization: bearer etk_abcdefgh", "authorization: bearer [redacted]")]
        [TestCase("ws://h/p?after=3&etos_ticket=whatever123", "ws://h/p?after=3&etos_ticket=[redacted]")]
        [TestCase("nothing secret here: etk_ab", "nothing secret here: [redacted]")]
        public void Redact_RemovesEveryCredentialShape(string input, string expected)
        {
            Assert.That(EtosRedaction.Redact(input), Is.EqualTo(expected));
            Assert.That(EtosRedaction.ContainsSecret(EtosRedaction.Redact(input)), Is.False);
        }

        [Test]
        public void Describe_ShowsOnlyThePrefixAndTheLength()
        {
            string shown = EtosRedaction.Describe(FakeCompanion.AppKey);
            Assert.That(shown, Does.StartWith("etk_").And.Contain("[redacted]").And.Contain("(" + FakeCompanion.AppKey.Length + " chars)"));
            Assert.That(shown, Does.Not.Contain(FakeCompanion.AppKey.Substring(4, 8)));
            Assert.That(EtosRedaction.Describe(null), Is.EqualTo("(no key)"));
        }

        [Test]
        public void Credentials_ReadJsonAndPlainKeyFilesAndNeverPrintTheKey()
        {
            string dir = Path.Combine(Path.GetTempPath(), "gcstudio-cred-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string json = Path.Combine(dir, "app-key.json");
                File.WriteAllText(json, new JObject { ["url"] = "http://127.0.0.1:7410/", ["key"] = FakeCompanion.AppKey }.ToString());
                EtosCredentials fromJson = EtosCredentials.FromKeyFile(json);
                Assert.That(fromJson.NodeUrl, Is.EqualTo("http://127.0.0.1:7410"));
                Assert.That(fromJson.KeyLength, Is.EqualTo(FakeCompanion.AppKey.Length));
                Assert.That(fromJson.ToString(), Does.Not.Contain(FakeCompanion.AppKey));
                Assert.That(fromJson.Display, Does.Not.Contain(FakeCompanion.AppKey));
                Assert.That(fromJson.AppearsIn("x " + FakeCompanion.AppKey), Is.True);

                string plain = Path.Combine(dir, "key.txt");
                File.WriteAllText(plain, FakeCompanion.AppKey + "\n");
                Assert.That(EtosCredentials.FromKeyFile(plain).NodeUrl, Is.Null);

                EtosException missing = Assert.Throws<EtosException>(() => EtosCredentials.FromKeyFile(Path.Combine(dir, "absent.json")))!;
                Assert.That(missing.Code, Is.EqualTo(EtosCodes.NotConfigured));
                Assert.That(missing.Error.Hint, Does.Contain(EtosCredentials.KeyFileVariable));

                string broken = Path.Combine(dir, "broken.json");
                File.WriteAllText(broken, "{\"url\": \"x\"}");
                EtosException noKey = Assert.Throws<EtosException>(() => EtosCredentials.FromKeyFile(broken))!;
                Assert.That(noKey.Code, Is.EqualTo(EtosCodes.NotConfigured));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Test]
        public void Errors_RedactWhatTheServerEchoes()
        {
            EtosError error = EtosError.FromBody(401, "{\"code\":\"unauthorized\",\"message\":\"key " + FakeCompanion.AppKey + " is revoked\",\"hint\":\"Bearer " + FakeCompanion.AppKey + "\"}");
            Assert.That(error.Message, Does.Not.Contain(FakeCompanion.AppKey));
            Assert.That(error.Hint, Does.Not.Contain(FakeCompanion.AppKey));
            Assert.That(error.ToString(), Does.Not.Contain(FakeCompanion.AppKey));
        }

        [Test]
        public async Task ClientLogsAndExceptions_NeverCarryTheKeyOrATicket()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.FailNext("/v1/hello", 401, new JObject { ["code"] = "unauthorized", ["message"] = "key " + FakeCompanion.AppKey + " unknown" });
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => setup.Client.HelloAsync())!;
            Assert.That(refused.Message, Does.Not.Contain(FakeCompanion.AppKey));
            using (EventStream stream = new EventStream(setup.Client, new MemoryCursorStore()))
            {
                stream.Start();
                await Wait.Until(() => stream.State == EventStreamState.Connected, TimeSpan.FromSeconds(5), "connected");
                setup.Fake.DropEventConnections();
                await Wait.Until(() => stream.Connects >= 2, TimeSpan.FromSeconds(10), "reconnected");
            }

            string all;
            lock (setup.Lines)
            {
                all = string.Join("\n", setup.Lines);
            }

            Assert.That(setup.Lines, Is.Not.Empty);
            Assert.That(all, Does.Not.Contain(FakeCompanion.AppKey));
            Assert.That(EtosRedaction.ContainsSecret(all), Is.False);
            Assert.That(setup.Client.KeyDisplay, Does.Not.Contain(FakeCompanion.AppKey));
        }
    }
}
