#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using NUnit.Framework;

namespace GameCore.Studio.Etos.Client.Tests
{
    public sealed class R3RegressionTests
    {
        [Test]
        public async Task D22_StopDrainsDelayedFinalAfterAllAudioFrames()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.VoiceFinalDelay = TimeSpan.FromMilliseconds(250);
            setup.Fake.VoiceTranscript.Clear();
            setup.Fake.VoiceTranscript.Add("Move the well");
            setup.Fake.VoiceTranscript.Add("Move the well one metre to the east.");
            using VoiceChannel voice = new VoiceChannel(setup.Client);
            List<VoiceTranscript> updates = new List<VoiceTranscript>();
            voice.Transcript += t => updates.Add(t);
            await voice.ConnectAsync();
            await voice.SendPcmAsync(new byte[60000]);
            string reason = await voice.StopAsync(TimeSpan.FromSeconds(3));
            Assert.That(reason, Is.EqualTo("stopped"));
            Assert.That(updates[0].Final, Is.False);
            Assert.That(updates[updates.Count - 1].Text, Is.EqualTo("Move the well one metre to the east."));
            Assert.That(updates[updates.Count - 1].Final, Is.True);
            Assert.That(setup.Fake.VoiceSeqs, Is.EqualTo(new long[] { 0, 1, 2 }));
            Assert.That(setup.Fake.VoiceFrameBytes, Is.EqualTo(new[] { 24576, 24576, 10848 }));
        }

        [Test]
        public async Task D22_StopTimeoutPreservesPreciseReasonInsteadOfClientClosed()
        {
            using FakeSetup setup = new FakeSetup();
            setup.Fake.VoiceNeverCloses = true;
            using VoiceChannel voice = new VoiceChannel(setup.Client);
            string? closed = null;
            voice.Closed += reason => closed = reason;
            await voice.ConnectAsync();
            string result = await voice.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.That(result, Is.EqualTo("stop timed out"));
            Assert.That(closed, Is.EqualTo(result));
        }
    }
}
