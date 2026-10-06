#nullable enable
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Etos;
using GameCore.Studio.Hollowmere.P2_2;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.R5_B
{
    public sealed class VirtualVoiceSelfTest
    {
        [UnityTest]
        [Timeout(240000)]
        public IEnumerator Request6_VirtualMicrophoneBothRetainedLinesTranscribe()
        {
            if (Environment.GetEnvironmentVariable("GAMECORE_R5B_VOICE_SELF_TEST") != "1")
                Assert.Ignore("Requires explicit realtime-operation authorization and GAMECORE_R5B_VOICE_SELF_TEST=1; no paid calls in the offline packet.");
            string? source = Environment.GetEnvironmentVariable("GAMECORE_R4A_VIRTUAL_SOURCE");
            string? sink = Environment.GetEnvironmentVariable("GAMECORE_R4A_VIRTUAL_SINK");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(sink) || !Microphone.devices.Contains(source))
                Assert.Ignore("No named virtual microphone/sink: set GAMECORE_R4A_VIRTUAL_SOURCE and GAMECORE_R4A_VIRTUAL_SINK to an already configured PipeWire loopback.");
            if (!File.Exists("/usr/bin/pw-play")) Assert.Ignore("pw-play is unavailable; virtual fixture playback cannot run.");
            EtosAgentGateway? gateway = EtosStudioSession.Gateway;
            if (gateway == null) Assert.Ignore("An authenticated Studio session must already be running; this self-test never reads key files.");
            string folder = Path.GetFullPath(Path.Combine(GatewayHarness.ProjectRoot, "../..",
                "artifacts/studio/verification/W-VOICE-01/p42c-voice2-20261006T074412.994071Z/workflow/voice"));
            using var voice = new EtosVoiceSession(gateway!.Client, gateway.Queue, new MicrophoneCapture(source), () => gateway.Status, gateway.Runtime.Log);
            foreach (var line in new[] { (File: "move.wav", Word: "Move"), (File: "destructive.wav", Word: "Delete") })
            {
                string fixture = Path.Combine(folder, line.File);
                Assert.That(File.Exists(fixture), Is.True, "unchanged retained speech fixture");
                int requests = gateway.Requests.Count;
                int journal = gateway.Runtime.Journal.List().Count;
                Task start = voice.StartAsync();
                DateTime deadline = DateTime.UtcNow.AddSeconds(30);
                while (!start.IsCompleted && DateTime.UtcNow < deadline) { gateway.Queue.Pump(); yield return null; }
                Assert.That(start.IsCompleted, Is.True);
                start.GetAwaiter().GetResult();
                Assert.That(voice.IsCapturing, Is.True, "the source delivered actual samples before fixture playback");
                var info = new ProcessStartInfo("/usr/bin/pw-play") { UseShellExecute = false };
                // Arguments are quoted for ProcessStartInfo's argument parser; no shell is involved.
                info.Arguments = "--target \"" + sink!.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" \"" + fixture.Replace("\"", "\\\"") + "\"";
                using Process player = Process.Start(info)!;
                deadline = DateTime.UtcNow.AddSeconds(30);
                while (!player.HasExited && DateTime.UtcNow < deadline) { gateway.Queue.Pump(); yield return null; }
                if (!player.HasExited) { player.Kill(); Assert.Fail("virtual source fixture playback timed out"); }
                Assert.That(player.ExitCode, Is.Zero);
                yield return new WaitForSecondsRealtime(1.5f);
                Task stop = voice.StopAsync();
                deadline = DateTime.UtcNow.AddSeconds(20);
                while (!stop.IsCompleted && DateTime.UtcNow < deadline) { gateway.Queue.Pump(); yield return null; }
                Assert.That(stop.IsCompleted, Is.True);
                stop.GetAwaiter().GetResult();
                Assert.That(voice.FinalText, Does.Contain(line.Word).IgnoreCase, "real provider recognition, never a canned transcript");
                Assert.That(gateway.Requests.Count, Is.EqualTo(requests));
                Assert.That(gateway.Runtime.Journal.List().Count, Is.EqualTo(journal));
                Assert.That(voice.Finals.Count, Is.GreaterThan(0));
            }
        }
    }
}
