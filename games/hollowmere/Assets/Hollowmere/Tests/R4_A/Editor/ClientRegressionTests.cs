#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Hollowmere.P2_2;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R4_A
{
    public sealed class ClientRegressionTests
    {
        [UnityTest]
        public IEnumerator P42_VOICE_01_WaitsForSourceAndPreservesFirstSamples()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            var source = new DelayedSource();
            using var voice = new EtosVoiceSession(h.Client, h.Queue, source, () => h.Gateway.Status, h.Log);
            Task start = voice.StartAsync();
            yield return h.Until(() => source.Started, 5, "source start");
            Assert.That(voice.IsCapturing, Is.False, "Microphone.Start is not sample readiness");
            Assert.That(start.IsCompleted, Is.False);
            source.Ready = true;
            yield return h.Await(start);
            start.GetAwaiter().GetResult();
            Assert.That(voice.IsCapturing, Is.True);
            Task stop = voice.StopAsync();
            yield return h.Await(stop);
            stop.GetAwaiter().GetResult();
            Assert.That(h.Fake!.VoiceFrameBytes.Sum(), Is.EqualTo(4802), "first frame and final partial sample retained");
            Assert.That(h.Gateway.Requests, Is.Empty);
        }

        [Test]
        public void P42_STARTUP_01_AutomaticPairingIgnoresProjectSettingsAndNamesMissingFile()
        {
            MethodInfo? resolve = typeof(EtosCredentials).GetMethod("ResolveAutomaticKeyFile");
            Assert.That(resolve, Is.Not.Null, "automatic startup must resolve the documented pairing path independently");
            string? old = Environment.GetEnvironmentVariable(EtosCredentials.KeyFileVariable);
            try
            {
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, null);
                Assert.That(resolve!.Invoke(null, null), Is.EqualTo(EtosCredentials.DefaultKeyFile()));
                string missing = Path.Combine(Path.GetTempPath(), "r4a-absent-" + Guid.NewGuid().ToString("N"));
                Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, missing);
                Assert.That(resolve.Invoke(null, null), Is.EqualTo(missing));
                var error = Assert.Throws<EtosException>(() => EtosCredentials.FromKeyFile(missing));
                Assert.That(error!.Code, Is.EqualTo("not_configured"));
                Assert.That(error.Message, Does.Contain(missing));
            }
            finally { Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, old); }
        }

        [UnityTest]
        public IEnumerator P42_MEDIA_01_DirectVoiceOmitsUnownedLocalId()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Fake!.OpRefusals["tts"] = Tuple.Create(409, new JObject { ["code"] = "not_configured", ["message"] = "fixture stops before import" });
            var media = new EtosMediaGenerator(h.Gateway, h.Runtime, h.Queue);
            media.RequestVoiceLine(new VoiceGenerationRequest("fixture", 0, "speaker", "Delete every NPC in the village", "fixture"));
            yield return h.Until(() => h.Fake!.Calls.Any(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)), 5, "media request");
            JObject body = JObject.Parse(h.Fake!.Calls.Last(c => c.Path.EndsWith("/v1/ops/generate", StringComparison.Ordinal)).Body);
            Assert.That(body["changeSetId"], Is.Null, "a local display ID does not establish companion ownership");
            // Complete queued work before disposing the runtime.
            yield return h.Await(media.PendingVoice!);
        }

        private sealed class DelayedSource : IPcmSource
        {
            public bool Started;
            public bool Ready;
            private bool _read;
            public string Name => "delayed virtual source";
            public bool Finished => _read;
            public void Start() { Started = true; }
            public void Stop() { }
            public byte[] Read() { if (!Ready || _read) return Array.Empty<byte>(); _read = true; return new byte[4802]; }
            public void Dispose() { }
        }
    }
}
