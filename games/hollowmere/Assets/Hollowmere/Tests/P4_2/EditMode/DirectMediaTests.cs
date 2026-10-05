#nullable enable
using System;
using System.Collections;
using System.IO;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class DirectMediaTests
    {
        [UnityTest]
        [Explicit("One direct image request against the installed operator pricing policy")]
        [Timeout(360000)]
        public IEnumerator R2_38_DirectImagePriceRefusalIsRecorded()
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("evidence required");
            Assert.That(EtosStudioSession.Start(), Is.True, EtosStudioSession.Problem?.ToString());
            EtosAgentGateway gateway = EtosStudioSession.Gateway!;
            var media = new EtosMediaGenerator(gateway, gateway.Runtime, gateway.Queue);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var task = media.GenerateImageAsync("A plain green cloth texture for a village healer robe.", "Assets/Hollowmere/Generated/P4_2/robe.png", 256, 0.25);
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 300) yield return null;
            Assert.That(task.IsCompleted && !task.IsFaulted, Is.True, task.Exception?.GetBaseException().Message);
            var imported = task.Result;
            var report = new JObject { ["ms"] = watch.ElapsedMilliseconds, ["ok"] = imported.Ok,
                ["code"] = imported.Problem?.Code, ["message"] = imported.Problem?.Message,
                ["artifactSha256"] = imported.Artifact?.Sha256, ["maxCostUsd"] = 0.25 };
            File.WriteAllText(Path.Combine(output, "image-price-refusal.json"), report.ToString());
            Assert.That(imported.Ok, Is.False, "no unverifiably priced generation may run");
            Assert.That(imported.Problem?.Code, Is.EqualTo("budget_unpriced"));
        }

        [UnityTest]
        [Explicit("Installed companion direct app-owned media; one priced TTS, no ledger request id invented")]
        [Timeout(360000)]
        public IEnumerator R2_38_TtsImportsVerifiedBytesThroughEngineAndUndoes()
        {
            Assert.That(Environment.GetEnvironmentVariable("GAMECORE_ETOS_LIVE"), Is.EqualTo("1"));
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ?? throw new InvalidOperationException("evidence required");
            Assert.That(EtosStudioSession.Start(), Is.True, EtosStudioSession.Problem?.ToString());
            EtosAgentGateway gateway = EtosStudioSession.Gateway!;
            var media = new EtosMediaGenerator(gateway, gateway.Runtime, gateway.Queue);
            string asset = "Assets/Hollowmere/Generated/P4_2/verification.wav";
            var watch = System.Diagnostics.Stopwatch.StartNew();
            // The paired app owns direct media. A changeSetId is supplied only for a companion-owned request.
            var task = media.GenerateSpeechAsync("Move this NPC two metres north.", asset, maxCostUsd: 0.50);
            while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 300) yield return null;
            Assert.That(task.IsCompleted, Is.True, "300 s op budget");
            Assert.That(task.IsFaulted, Is.False, task.Exception?.GetBaseException().Message);
            MediaImport imported = task.Result;
            string file = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, asset);
            var record = new JObject { ["ms"] = watch.ElapsedMilliseconds, ["ok"] = imported.Ok,
                ["providerSha256"] = imported.Result.Sha256, ["importedSha256"] = imported.Artifact?.Sha256,
                ["problem"] = imported.Problem?.ToString(), ["fileExists"] = File.Exists(file) };
            if (imported.Result.Bytes != null) File.WriteAllBytes(Path.Combine(output, "provider.wav"), imported.Result.Bytes);
            if (File.Exists(file))
            {
                byte[] bytes = File.ReadAllBytes(file);
                record["diskSha256"] = Json.Sha256Hex(bytes);
                File.WriteAllBytes(Path.Combine(output, "imported.wav"), bytes);
            }
            if (imported.Report != null)
            {
                record["journalId"] = imported.Report.Entry.Id;
                record["journalState"] = imported.Report.State.ToString();
                var undo = gateway.Runtime.History.Undo(imported.Report.Entry.Id);
                record["undoOk"] = undo.Ok;
            }
            File.WriteAllText(Path.Combine(output, "tts-import.json"), record.ToString());
            Assert.That(imported.Ok, Is.True, imported.Problem?.ToString());
            Assert.That((string?)record["diskSha256"], Is.EqualTo(imported.Artifact!.Sha256));
            Assert.That((bool?)record["undoOk"], Is.True);
        }
    }
}
