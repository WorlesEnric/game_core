#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos;
using GameCore.Studio.Hollowmere.P2_2;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R5_B
{
    public sealed class VoiceTests
    {
        [UnityTest]
        public IEnumerator Request6_TwoTakesTraceFramesBytesReleaseAndFinalWithoutAudioOrText()
        {
            using GatewayHarness h = GatewayHarness.WithFake();
            h.Fake!.VoiceFinalDelay = TimeSpan.FromMilliseconds(50);
            h.Fake.VoiceTranscript.Clear();
            h.Fake.VoiceTranscript.Add("private spoken command");
            using var voice = new EtosVoiceSession(h.Client, h.Queue, new Source(), () => h.Gateway.Status, h.Log);
            for (int take = 0; take < 2; take++)
            {
                Task start = voice.StartAsync();
                yield return h.Await(start);
                start.GetAwaiter().GetResult();
                Task stop = voice.StopAsync();
                yield return h.Await(stop);
                stop.GetAwaiter().GetResult();
                Assert.That(voice.Finals.Count, Is.EqualTo(1));
                Assert.That(voice.FramesSent, Is.EqualTo(2));
            }
            string log = string.Join("\n", h.Log.Recent.Select(e => e.Message));
            Assert.That(log, Does.Contain("frame=audio-0 bytes=4800").And.Contain("frame=audio-1 bytes=2"));
            Assert.That(log, Does.Contain("release frames=2 bytes=4802").And.Contain("final=True"));
            Assert.That(log, Does.Not.Contain("private spoken command").And.Not.Contain("pcm16"));
            Assert.That(h.Fake.VoicePcm.SelectMany(b => b).Count(), Is.EqualTo(9604));
            Assert.That(h.Gateway.Requests, Is.Empty);
        }

        private sealed class Source : IPcmSource
        {
            private bool _read;
            public string Name => "r5-b-low-level";
            public bool IsRunning { get; private set; }
            public bool Finished => _read;
            public void Start() { IsRunning = true; _read = false; }
            public byte[] Read()
            {
                if (!IsRunning || _read) return Array.Empty<byte>();
                _read = true;
                byte[] bytes = new byte[4802];
                for (int i = 0; i < bytes.Length; i += 2) { bytes[i] = 0x84; bytes[i + 1] = 0x13; }
                return bytes;
            }
            public void Stop() { IsRunning = false; }
            public void Dispose() { Stop(); }
        }
    }
}
