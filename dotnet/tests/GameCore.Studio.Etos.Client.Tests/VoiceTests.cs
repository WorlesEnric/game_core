// W-VOICE-01 at the protocol layer: PCM16 conversion, the 24 KiB frame cap, gapless seq, the 100 ms capture framing,
// WAV conversion, level, and a session against the fake companion (ready, partial revisions never final, the final on
// stop, assistant transcripts dropped, refusals with the code preserved).
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Etos.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class VoiceFramingTests
    {
        [Test]
        public void ToPcm16_ClampsAndWritesLittleEndian()
        {
            byte[] pcm = VoiceFraming.ToPcm16(new[] { 0f, 1f, -1f, 2f, -2f, 0.5f }, 0, 6);
            short[] values = Enumerable.Range(0, 6).Select(i => (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8))).ToArray();
            Assert.That(values, Is.EqualTo(new short[] { 0, 32767, -32767, 32767, -32767, 16384 }));
        }

        [Test]
        public void Frames_RespectTheCapAndNumberGaplessly()
        {
            long seq = 5;
            List<string> frames = VoiceFraming.Frames(new byte[60000], ref seq);
            Assert.That(seq, Is.EqualTo(8));
            List<JObject> parsed = frames.Select(JObject.Parse).ToList();
            Assert.That(parsed.Select(f => (long)f["seq"]!), Is.EqualTo(new long[] { 5, 6, 7 }));
            Assert.That(parsed.Select(f => Convert.FromBase64String((string)f["pcm16"]!).Length), Is.EqualTo(new[] { 24576, 24576, 10848 }));
            Assert.That(parsed.All(f => ((string)f["pcm16"]!).Length <= 32 * 1024), Is.True, "≤ 32 KiB of base64");
            Assert.That(parsed.All(f => (string?)f["type"] == "audio"), Is.True);
            long s = 0;
            Assert.Throws<ArgumentException>(() => VoiceFraming.Frames(new byte[3], ref s));
            Assert.That(VoiceFraming.StopFrame, Is.EqualTo("{\"type\":\"stop\"}"));
        }

        [Test]
        public void Accumulator_EmitsHundredMillisecondFramesAndFlushesTheRest()
        {
            PcmFrameAccumulator accumulator = new PcmFrameAccumulator();
            Assert.That(accumulator.FrameBytes, Is.EqualTo(4800));
            List<byte[]> frames = accumulator.Add(new byte[10000], 0, 10000);
            Assert.That(frames.Select(f => f.Length), Is.EqualTo(new[] { 4800, 4800 }));
            Assert.That(accumulator.Pending, Is.EqualTo(400));
            Assert.That(accumulator.Flush()!.Length, Is.EqualTo(400));
            Assert.That(accumulator.Flush(), Is.Null);
            Assert.Throws<ArgumentOutOfRangeException>(() => new PcmFrameAccumulator(30000));
        }

        [Test]
        public void Wav_IsDownmixedAndResampledTo24kMono()
        {
            byte[] stereo48 = new byte[48000 * 2 * 2];
            byte[] wav = FakeMedia.Wav(stereo48, 48000, 2);
            byte[] pcm = VoiceFraming.WavToPcm16Mono24k(wav);
            Assert.That(pcm.Length, Is.EqualTo(24000 * 2));
            byte[] same = VoiceFraming.WavToPcm16Mono24k(FakeMedia.Wav(new byte[4800], 24000, 1));
            Assert.That(same.Length, Is.EqualTo(4800));
        }

        [Test]
        public void Level_IsZeroForSilenceAndAboutPointTwoForATone()
        {
            Assert.That(VoiceFraming.Level(new byte[4800], 0, 4800), Is.EqualTo(0f));
            byte[] tone = FakeMedia.Tone(0.1, 24000, 440);
            Assert.That(VoiceFraming.Level(tone, 0, tone.Length), Is.InRange(0.19f, 0.23f));
        }
    }

    public sealed class VoiceSessionTests
    {
        [Test]
        public async Task Session_StreamsGaplessFramesAndOnlyTheStopRevisionIsFinal()
        {
            using FakeSetup setup = new FakeSetup();
            List<VoiceTranscript> transcripts = new List<VoiceTranscript>();
            using VoiceChannel channel = new VoiceChannel(setup.Client);
            channel.Transcript += t => { lock (transcripts) { transcripts.Add(t); } };
            VoiceReady ready = await channel.ConnectAsync();
            Assert.That(ready.SampleRateHz, Is.EqualTo(24000));
            Assert.That(ready.MaxChunkBytes, Is.EqualTo(24576));

            byte[] tone = FakeMedia.Tone(1.0, 24000, 300);
            for (int offset = 0; offset < tone.Length; offset += VoiceFraming.CaptureFrameBytes)
            {
                byte[] frame = new byte[Math.Min(VoiceFraming.CaptureFrameBytes, tone.Length - offset)];
                Buffer.BlockCopy(tone, offset, frame, 0, frame.Length);
                await channel.SendPcmAsync(frame);
            }

            await Wait.Until(() => { lock (transcripts) { return transcripts.Count >= 2; } }, TimeSpan.FromSeconds(10), "partial revisions");
            lock (transcripts)
            {
                Assert.That(transcripts.All(t => !t.Final), Is.True, "partials are never final");
            }

            string reason = await channel.StopAsync(TimeSpan.FromSeconds(5));
            Assert.That(reason, Is.EqualTo("stopped"));
            VoiceTranscript last = transcripts.Last();
            Assert.That(last.Final, Is.True);
            Assert.That(last.Text, Is.EqualTo("move this NPC two metres north"));
            Assert.That(transcripts.All(t => t.Role == "user"), Is.True, "assistant transcripts are dropped");
            Assert.That(transcripts.Count(t => t.Final), Is.EqualTo(1));
            Assert.That(setup.Fake.VoiceSeqs, Is.EqualTo(Enumerable.Range(0, 10).Select(i => (long)i).ToList()), "gapless seq from 0");
            Assert.That(setup.Fake.VoiceFrameBytes.All(b => b <= 24576), Is.True);
            Assert.That(setup.Fake.VoiceStops, Is.EqualTo(1));
            Assert.That(channel.FramesSent, Is.EqualTo(10));
        }

        [Test]
        public async Task Session_ALargeBufferIsSplitUnderTheCap()
        {
            using FakeSetup setup = new FakeSetup();
            using VoiceChannel channel = new VoiceChannel(setup.Client);
            await channel.ConnectAsync();
            await channel.SendPcmAsync(new byte[60000]);
            await channel.StopAsync(TimeSpan.FromSeconds(5));
            Assert.That(setup.Fake.VoiceFrameBytes, Is.EqualTo(new[] { 24576, 24576, 10848 }));
        }

        [Test]
        public async Task Session_ARefusalKeepsItsCode()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.VoiceRefusal = new JObject { ["code"] = "not_configured", ["message"] = "no realtime adapter is registered for this provider" };
            using VoiceChannel channel = new VoiceChannel(setup.Client);
            List<EtosError> errors = new List<EtosError>();
            channel.Error += e => errors.Add(e);
            EtosException refused = Assert.ThrowsAsync<EtosException>(() => channel.ConnectAsync())!;
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.NotConfigured));
            Assert.That(errors.Select(e => e.Code), Does.Contain("not_configured"));
            Assert.That(await channel.Completion, Is.EqualTo("refused"));
        }
    }
}
