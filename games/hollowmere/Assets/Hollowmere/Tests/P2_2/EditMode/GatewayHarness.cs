// GameCore.Studio.Hollowmere.P2_2 - a Studio runtime over the Hollowmere project (temporary state root: journal and
// artifacts never touch Studio/), an EtosAgentGateway over either the fake companion or the live node, and coroutine
// helpers that pump the gateway's main-thread queue while waiting.
#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using GameCore.Studio.Model;
using NUnit.Framework;
using UnityEngine;

namespace GameCore.Studio.Hollowmere.P2_2
{
    /// <summary>One gateway under test.</summary>
    public sealed class GatewayHarness : IDisposable
    {
        public const string HollowmereFolder = "Assets/Hollowmere";

        private GatewayHarness(EtosCredentials credentials, string nodeUrl, FakeCompanion? fake, string name)
        {
            Fake = fake;
            StateRoot = Path.Combine(Path.GetTempPath(), "gcstudio-p22-" + name + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(StateRoot);
            Log = new MemoryStudioLog();
            Runtime = StudioRuntime.Create(new StudioRuntimeOptions
            {
                Paths = new StudioPaths(ProjectRoot, StateRoot, "p22-" + name),
                Log = Log,
                SearchFolders = new[] { HollowmereFolder },
                IndexScope = AuthoringSourceScope.Assets,
                LoadIndexCache = false,
            });
            Credentials = credentials;
            Client = new CompanionClient(new EtosClientOptions { NodeUrl = nodeUrl, DefaultMaxCostUsd = 0.50, Log = line => Log.Write(StudioLogLevel.Debug, "etos.client", EtosRedaction.Redact(line)) }, credentials);
            Queue = new MainThreadQueue(Log);
            Cursors = new MemoryCursorStore();
            Gateway = new EtosAgentGateway(Client, Runtime, Queue, Cursors, new EtosGatewayOptions { Backoff = new BackoffPolicy(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2)) }, Log);
            Runtime.Services.AgentGateway = Gateway;
        }

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public FakeCompanion? Fake { get; }

        public string StateRoot { get; }

        public MemoryStudioLog Log { get; }

        public StudioRuntime Runtime { get; }

        public EtosCredentials Credentials { get; }

        public CompanionClient Client { get; }

        public MainThreadQueue Queue { get; }

        public MemoryCursorStore Cursors { get; }

        public EtosAgentGateway Gateway { get; }

        public static GatewayHarness WithFake(Action<FakeCompanion>? configure = null)
        {
            FakeCompanion fake = new FakeCompanion();
            configure?.Invoke(fake);
            fake.Start();
            return new GatewayHarness(new EtosCredentials(FakeCompanion.AppKey, fake.NodeUrl, "fake"), fake.NodeUrl, fake, "fake");
        }

        /// <summary>The live node (key file from GAMECORE_ETOS_KEY_FILE or the pairing default).</summary>
        public static GatewayHarness Live()
        {
            string? keyFile = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable) ?? EtosCredentials.DefaultKeyFile();
            EtosCredentials credentials = EtosCredentials.FromKeyFile(keyFile ?? string.Empty);
            return new GatewayHarness(credentials, credentials.NodeUrl ?? "http://127.0.0.1:7410", null, "live");
        }

        /// <summary>Pumps the queue until <paramref name="task"/> completes (fails the test on timeout).</summary>
        public IEnumerator Await(Task task, double seconds = 30, string what = "the task")
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (!task.IsCompleted)
            {
                Queue.Pump();
                if (DateTime.UtcNow > end)
                {
                    Assert.Fail("timed out after " + seconds + " s waiting for " + what);
                }

                yield return null;
            }

            Queue.Pump();
        }

        /// <summary>Pumps the queue until <paramref name="condition"/> holds.</summary>
        public IEnumerator Until(Func<bool> condition, double seconds, string what, bool fail = true)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (true)
            {
                Queue.Pump();
                if (condition())
                {
                    yield break;
                }

                if (DateTime.UtcNow > end)
                {
                    if (fail)
                    {
                        Assert.Fail("timed out after " + seconds + " s waiting for " + what);
                    }

                    yield break;
                }

                yield return null;
            }
        }

        /// <summary>An empty Edit-mode selection at the runtime's index revision.</summary>
        public SelectionSnapshot EmptySelection()
        {
            return new SelectionSnapshot(IdDerivation.NewSelectionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance), SelectionMode.Edit, Array.Empty<AuthoringRef>(), Runtime.Index.Revision);
        }

        public void Dispose()
        {
            Gateway.Dispose();
            Client.Dispose();
            Runtime.Dispose();
            Fake?.Dispose();
            try
            {
                if (Directory.Exists(StateRoot))
                {
                    Directory.Delete(StateRoot, true);
                }
            }
            catch (IOException)
            {
                // A file still held by the OS; the temp folder is reclaimed later.
            }
        }
    }
}
