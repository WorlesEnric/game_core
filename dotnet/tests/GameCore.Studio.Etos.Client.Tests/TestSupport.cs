// Shared helpers: a started fake companion with a client configured for it, waiting helpers and change-set samples.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    internal sealed class FakeSetup : IDisposable
    {
        public FakeSetup(Action<EtosClientOptions>? configure = null, string? key = null)
        {
            Fake = new FakeCompanion();
            Fake.Start();
            TempDirectory = Path.Combine(Path.GetTempPath(), "gcstudio-etos-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(TempDirectory);
            Options = new EtosClientOptions
            {
                NodeUrl = Fake.NodeUrl,
                ProjectId = new string('a', 64),
                TempDirectory = TempDirectory,
                RequestTimeout = TimeSpan.FromSeconds(15),
                Log = line =>
                {
                    lock (Lines)
                    {
                        Lines.Add(line);
                    }
                },
            };
            configure?.Invoke(Options);
            Credentials = new EtosCredentials(key ?? FakeCompanion.AppKey, Fake.NodeUrl, "fixture");
            Client = new CompanionClient(Options, Credentials);
        }

        public FakeCompanion Fake { get; }

        public EtosClientOptions Options { get; }

        public EtosCredentials Credentials { get; }

        public CompanionClient Client { get; }

        public string TempDirectory { get; }

        public List<string> Lines { get; } = new List<string>();

        public void Dispose()
        {
            Client.Dispose();
            Fake.Dispose();
            try
            {
                Directory.Delete(TempDirectory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    internal static class Samples
    {
        /// <summary>A fresh valid change-set id (cs_ + 0 + 25 hex digits: hex digits are Crockford base32 digits).</summary>
        public static string ChangeSetId()
        {
            return "cs_0" + Guid.NewGuid().ToString("N").ToUpperInvariant().Substring(0, 25);
        }

        public const string CatalogRevision = "5030be2d2a0b9a7c6f4e1d8b3c2a19087f6e5d4c3b2a19087f6e5d4c3b2a1908";

        public static JObject Selection() => new JObject
        {
            ["id"] = "sel_01J9ZQ00000000000000000001",
            ["mode"] = "Edit",
            ["targets"] = new JArray(new JObject { ["kind"] = "Entity", ["authoringId"] = "e-traveller" }),
            ["indexRevision"] = 3,
        };

        public static JObject Slice() => new JObject { ["revision"] = 3, ["project"] = "hollowmere", ["nodes"] = new JArray() };

        public static JObject Catalog() => new JObject
        {
            ["schema"] = "gamecore.studio.toolcatalog/1",
            ["objectTypes"] = new JArray(),
            ["tools"] = new JArray(),
            ["revision"] = CatalogRevision,
        };

        public static EditRequestBody Request(string id, bool withCatalog = true)
        {
            EditRequestBody body = new EditRequestBody(id, "move this NPC two metres north", "agent", Selection(), Slice(), CatalogRevision);
            if (withCatalog)
            {
                body.ToolCatalog = Catalog();
            }

            return body;
        }

        public static JObject ChangeSet(string id, string? artifactSha = null, long bytes = 0) => new JObject
        {
            ["id"] = id,
            ["schema"] = "gamecore.studio.changeset/1",
            ["intent"] = new JObject { ["text"] = "move this NPC two metres north", ["origin"] = "agent" },
            ["operations"] = new JArray(new JObject { ["opId"] = "op1", ["tool"] = "asset.import", ["args"] = new JObject { ["path"] = "Assets/Generated/a.png", ["artifact"] = new JObject { ["artifact"] = "sha256:" + (artifactSha ?? new string('0', 64)) } } }),
            ["artifacts"] = artifactSha == null ? new JArray() : new JArray(new JObject { ["sha256"] = artifactSha, ["mediaType"] = "image/png", ["bytes"] = bytes, ["name"] = "a.png" }),
            ["requirements"] = new JObject { ["max"] = "Live" },
        };
    }

    internal static class Wait
    {
        public static async Task Until(Func<bool> condition, TimeSpan within, string what)
        {
            DateTime end = DateTime.UtcNow + within;
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    Assert.Fail("timed out waiting for " + what);
                }

                await Task.Delay(20).ConfigureAwait(false);
            }
        }

        public static async Task<RequestInfo> ForState(CompanionClient client, string id, string state, TimeSpan within)
        {
            DateTime end = DateTime.UtcNow + within;
            while (true)
            {
                RequestInfo info = await client.GetRequestAsync(id).ConfigureAwait(false);
                if (info.State == state)
                {
                    return info;
                }

                if (DateTime.UtcNow > end)
                {
                    Assert.Fail(id + " did not reach " + state + " (last " + info.State + ")");
                }

                await Task.Delay(20).ConfigureAwait(false);
            }
        }
    }
}
