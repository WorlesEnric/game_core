#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Driver = Hollowmere.P3_2.Workflows.Workflows;

namespace Hollowmere.R6_B
{
    public sealed class DriverTests
    {
        [Test]
        public void R6_Request3_DryReadinessThenExplicitStopDrainsFinalBeforeCompletion()
        {
            var session = new Voice();
            string text = "";
            bool capturing = false;
            int playback = 0;
            using var take = new VoiceTakeDriver(session, () => capturing, t => { if (t.Final) text = t.Text; });
            Assert.That(take.Tick(0, false, () => playback++), Is.False);
            take.Tick(4, false, () => playback++);
            Assert.That(playback, Is.Zero, "the old three-second timer must not trigger playback");
            session.Started.SetResult(true);
            take.Tick(5, false, () => playback++);
            Assert.That(playback, Is.Zero, "provider-ready without capture is insufficient");
            capturing = true;
            take.Tick(6, false, () => playback++);
            Assert.That(playback, Is.EqualTo(1));
            Assert.That(take.Tick(7, true, () => playback++), Is.False);
            Assert.That(session.Stops, Is.EqualTo(1), "Stop must not wait for a final that may only arrive after commit");
            Assert.That(take.Tick(8, true, () => playback++), Is.False);
            Assert.That(text, Is.Empty);
            session.Final("Delete all NPCs in this region.");
            session.Drained.SetResult(true);
            Assert.That(take.Tick(9, true, () => playback++), Is.True);
            Assert.That(text, Is.EqualTo("Delete all NPCs in this region."));
            Assert.That(session.Starts, Is.EqualTo(1));
            Assert.That(take.Error, Is.Null);
            var steps = Driver.For("voice2").Select(s => s.Name).ToList();
            Assert.That(steps.IndexOf("send transcript"), Is.GreaterThan(steps.IndexOf("voice move")));
            Assert.That(steps.Any(s => s.Contains("destructive") && s.Contains("send")), Is.False);
        }

        [Test]
        public void R6_Request3_FailedStartNeverPlaysOrTogglesIntoAnotherStart()
        {
            var session = new Voice();
            using var take = new VoiceTakeDriver(session, () => true, _ => Assert.Fail("no transcript expected"));
            take.Tick(0, false, () => Assert.Fail("not ready"));
            session.Started.SetException(new InvalidOperationException("upstream session.updated was not acknowledged"));
            take.Tick(4, false, () => Assert.Fail("failed start cannot play"));
            Assert.That(session.Stops, Is.EqualTo(1));
            session.Drained.SetResult(true);
            Assert.That(take.Tick(5, false, () => Assert.Fail("no retry")), Is.True);
            Assert.That(session.Starts, Is.EqualTo(1));
            Assert.That(take.Error, Does.Contain("session.updated"));
        }

        [Test]
        public void R6_Request5_RetainedReopenMismatchFailsExactByteContract()
        {
            JObject witness = JObject.Parse(File.ReadAllText(Path.Combine(DialogueCandidateTests.Repo,
                "artifacts/studio/verification/W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/final.json")));
            Assert.That((bool)witness["backToBefore"]!, Is.False);
            Assert.Throws<InvalidOperationException>(() => Driver.RequireByteConsistency(witness["before"], (JObject)witness["now"]!));
            Assert.DoesNotThrow(() => Driver.RequireByteConsistency(witness["before"], (JObject)witness["before"]!.DeepClone()));
        }

        [Test]
        public void R6_Request5_RealReopenFinalStepFailsRatherThanOnlyRecordingFalse()
        {
            JObject state = P32State.instance.Data;
            int shots = P32State.instance.Shots;
            int frames = P32State.instance.Frames;
            try
            {
                P32State.instance.Set("saved", new JObject { ["before"] = new JObject { ["missing-baseline"] = new string('0', 64) } });
                var step = Driver.For("reopen").Single(s => s.Name == "final consistency");
                Assert.Throws<InvalidOperationException>(() => step.Run());
                JObject receipt = JObject.Parse(File.ReadAllText(Path.Combine(WorkflowRunner.OutputDir, "reopen/final.json")));
                Assert.That((bool)receipt["backToBefore"]!, Is.False);
            }
            finally
            {
                P32State.instance.Data = state;
                P32State.instance.Shots = shots;
                P32State.instance.Frames = frames;
            }
        }

        [TestCase("contentStamp")]
        [TestCase("contentHash")]
        [TestCase("text")]
        public void R6_Request5_NoBakeOrAuthoredFieldIsDropped(string field)
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, field + ": before\n");
                JObject before = S.Hashes(path);
                File.WriteAllText(path, field + ": after\n");
                Assert.Throws<InvalidOperationException>(() => Driver.RequireByteConsistency(before, S.Hashes(path)));
            }
            finally { File.Delete(path); }
        }

        [Test]
        public void R6_Request5_MissingFilesCannotPassAsEqual()
        {
            var absent = new JObject { ["asset"] = "missing" };
            Assert.Throws<InvalidOperationException>(() => Driver.RequireByteConsistency(absent, absent));
        }

        private sealed class Voice : IVoiceSession
        {
            public readonly TaskCompletionSource<bool> Started = new TaskCompletionSource<bool>();
            public readonly TaskCompletionSource<bool> Drained = new TaskCompletionSource<bool>();
            public int Starts;
            public int Stops;
            public event Action<TranscriptUpdate>? Transcript;
            public event Action<float>? Level { add { } remove { } }
            public event Action<Diagnostic>? Error { add { } remove { } }
            public Task StartAsync() { Starts++; return Started.Task; }
            public Task StopAsync() { Stops++; return Drained.Task; }
            public void Final(string text) => Transcript?.Invoke(new TranscriptUpdate("r6-take", 1, text, true));
        }
    }
}
