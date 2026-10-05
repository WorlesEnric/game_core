#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using GameCore.Studio.Etos;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class VoiceAcceptanceTests
    {
        [UnityTest]
        [Explicit("P4.2 real PipeWire microphone and installed companion; reuses recorded WAV")]
        [Timeout(180000)]
        public IEnumerator R2_38_W_VOICE_01_DestructiveSpeechNeverSubmits()
        {
            Assert.That(Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE"), Is.EqualTo("1"));
            Assert.That(ViewportRenderer.CanRender, Is.True);
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("evidence directory required");
            Directory.CreateDirectory(output);
            EditorSceneManager.OpenScene("Assets/Hollowmere/Regions/ThornwickVillage.unity", OpenSceneMode.Single);
            StudioViewportWindow viewport = EditorWindow.GetWindow<StudioViewportWindow>();
            viewport.position = new Rect(30, 50, 1220, 650);
            viewport.Show();
            viewport.EnsureGui();
            StudioUiContext context = viewport.Context;
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!context.Gateway.Status.AgentReady && DateTime.UtcNow < deadline) yield return null;
            Assert.That(context.Gateway.Status.AgentReady, Is.True, "authenticated hello");
            PromptBar prompt = viewport.Prompt!;
            int beforeRequests = context.Gateway.Requests.Count;
            int beforeJournal = context.Runtime.Journal.List().Count;
            int beforeTray = context.Tasks.Rows.Count;
            var revisions = new JArray();
            prompt.Text = string.Empty;
            prompt.ToggleVoice();
            EtosVoiceSession? voice = typeof(PromptBar).GetField("_voice", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(prompt) as EtosVoiceSession;
            Assert.That(voice, Is.Not.Null, "real ETOS session backing the prompt");
            var providerRevisions = new JArray();
            float peakLevel = 0;
            voice!.Level += value => peakLevel = Math.Max(peakLevel, value);
            voice.Transcript += update => providerRevisions.Add(new JObject { ["text"] = update.Text, ["final"] = update.Final, ["revision"] = update.Revision });
            try
            {
                deadline = DateTime.UtcNow.AddSeconds(60);
                while (!voice.IsCapturing && DateTime.UtcNow < deadline) yield return null;
                Assert.That(voice.IsCapturing, Is.True, "provider ready and microphone actually capturing before playing fixture");
                File.WriteAllText(Path.Combine(output, "play-destructive"), "destructive.wav\n");
                deadline = DateTime.UtcNow.AddSeconds(75);
                string previous = string.Empty;
                while (DateTime.UtcNow < deadline && string.IsNullOrWhiteSpace(prompt.Text))
                {
                    string partial = prompt.PartialTranscript;
                    if (partial.Length > 0 && partial != "listening..." && partial != previous)
                    {
                        revisions.Add(new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["text"] = partial });
                        previous = partial;
                    }
                    yield return null;
                }
                if (prompt.VoiceActive) prompt.ToggleVoice();
                var report = new JObject
                {
                    ["utc"] = DateTime.UtcNow.ToString("o"), ["devices"] = new JArray(Microphone.devices),
                    ["final"] = prompt.Text, ["revisions"] = revisions, ["providerRevisions"] = providerRevisions, ["framesSent"] = voice.FramesSent, ["peakLevel"] = peakLevel, ["played"] = File.Exists(Path.Combine(output, "played-destructive")),
                    ["requestsBefore"] = beforeRequests, ["requestsAfter"] = context.Gateway.Requests.Count,
                    ["journalBefore"] = beforeJournal, ["journalAfter"] = context.Runtime.Journal.List().Count,
                    ["trayBefore"] = beforeTray, ["trayAfter"] = context.Tasks.Rows.Count,
                    ["voiceActiveAfterRelease"] = prompt.VoiceActive,
                };
                File.WriteAllText(Path.Combine(output, "voice-result.json"), report.ToString());
                string? capture = UnityWindowCapture.CaptureStudio(Path.Combine(output, "voice-final.png"), false);
                Assert.That(capture, Is.Null, "Studio-only screenshot");
                Assert.That(report.Value<bool>("played"), Is.True);
                Assert.That(voice.FramesSent, Is.GreaterThan(0));
                Assert.That(peakLevel, Is.GreaterThan(0.001f), "recorded speech reached the actual microphone source");
                Assert.That(prompt.Text.ToLowerInvariant(), Does.Contain("delete"));
                Assert.That(revisions.Count, Is.GreaterThan(0), "partial transcript visible before final");
                Assert.That(context.Gateway.Requests.Count, Is.EqualTo(beforeRequests));
                Assert.That(context.Runtime.Journal.List().Count, Is.EqualTo(beforeJournal));
                Assert.That(context.Tasks.Rows.Count, Is.EqualTo(beforeTray));
            }
            finally
            {
                if (prompt.VoiceActive) prompt.ToggleVoice();
                viewport.Close();
            }
        }
    }
}
