// Hollowmere P3.1 EditMode (Play Mode entered from the Editor) -
//   AdmissionFromPlayModeCaptures: with Boot.unity playing, the game's R2-G binding (HollowmereStudioAdmission) is in
//     place (capture, session readiness, smoke) and the bound capture adapter checkpoints the
//     running game through HollowmereGame's SaveService and reuses the checkpoint on a retry (P2.4 open item 2; R2-B's
//     capture contract). An unverified StageAdmission.Admit(captureAndStop) refuses as verdict_missing - never
//     capture_failed - since R2-B captures only after an authenticated companion verdict (Stopping Play Mode is stubbed).
//   TenPlayEditCycles (W-GAME-08, explicit): ten Play/Edit cycles of Boot.unity; after each exit the managed and native
//     memory (Profiler.GetTotalAllocatedMemoryLong / GetTotalReservedMemoryLong) is recorded and compared with cycle 1;
//     the series is written to artifacts/studio/evidence/P3.1/memory-cycles.json.
#nullable enable
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using Hollowmere.Game;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31PlayModeHooksTests
    {
        private const string MemoryKey = "Hollowmere.P3_1.MemoryCycles";

        private static string SlotFile(HollowmereGame game, string slot) => game.Saves!.DocumentPath(slot);

        private static IEnumerator WaitForGame(double seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
                if (game != null && game.Saves != null)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("HollowmereGame did not boot its SaveService within " + seconds + " s");
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator AdmissionFromPlayModeCaptures()
        {
            EditorSceneManager.OpenScene(HollowmerePaths.BootScene, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            yield return WaitForGame(60);

            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            HollowmereStudioAdmission.AttachAll();
            Assert.That(admission.Options.Capture, Is.InstanceOf<SaveServiceAdmissionCapture>(), "R2-G: the game bound its SaveService capture");
            Assert.That(admission.Options.SessionReady, Is.Not.Null, "R2-G: the game bound its session readiness");
            Assert.That(admission.Options.SessionReady!(), Is.True, "the running game's session is ready");
            Assert.That(admission.Options.PollSmokeTest, Is.Not.Null, "R2-G2: the game bound its tri-state live smoke poll");
            Assert.That(admission.Options.SmokeTestFrameBudget, Is.GreaterThanOrEqualTo(HollowmereAdmittedSmoke.FrameBudget), "the poll budget covers the entry's steps");
            IAdmissionCapture capture = admission.Options.Capture!;
            HollowmereGame game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>()!;

            // R2-B: a capture happens only after an authenticated companion verdict; without one Admit refuses as a missing
            // verdict (not capture_failed) and writes no checkpoint.
            Func<bool>? probe = admission.Options.PlayModeProbe;
            Action? stop = admission.Options.StopPlayMode;
            admission.Options.StopPlayMode = () => { };
            AdmissionResult result;
            try
            {
                var candidate = new ChangeSet(
                    IdDerivation.NewChangeSetId(),
                    ChangeSet.SchemaId,
                    new Intent("P3.1 admission capture probe", IntentOrigin.Manual),
                    new[] { new Operation("op1", "set", null, new JObject { ["field"] = "displayName", ["value"] = "probe" }) });
                result = admission.Admit(candidate, null, captureAndStop: true);
            }
            finally
            {
                admission.Options.PlayModeProbe = probe;
                admission.Options.StopPlayMode = stop;
            }

            Debug.Log("[P3.1] unverified admission from Play Mode: " + result.Outcome + " (" + result.Reason + ")");
            Assert.That(result.Reason, Is.EqualTo(VerdictReasons.Missing), result.Detail);
            Assert.That(result.Reason, Is.Not.EqualTo("capture_failed"), result.Detail);

            // The capture adapter itself (what a verified admission calls): it checkpoints the running game through
            // HollowmereGame's SaveService, and a retry reuses the checkpoint instead of overwriting it.
            string slot = "admit-p31-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            Assert.That(capture.TryCapture(slot, out string? problem), Is.True, problem);
            Assert.That(game.Saves!.Exists(slot), Is.True, "the capture slot was written");
            byte[] first = File.ReadAllBytes(SlotFile(game, slot));
            Assert.That(capture.TryCapture(slot, out problem), Is.True, "a retry reuses the checkpoint: " + problem);
            Assert.That(File.ReadAllBytes(SlotFile(game, slot)), Is.EqualTo(first), "the retry did not overwrite the checkpoint");
            Debug.Log("[P3.1] admission capture adapter wrote " + slot + " (" + first.Length + " bytes); the retry reused it");
            game.Saves.Delete(slot);
            yield return new ExitPlayMode();
        }

        [UnityTest]
        [Explicit("W-GAME-08 memory series; run with --filter TenPlayEditCycles")]
        [Timeout(1800000)]
        public IEnumerator TenPlayEditCycles()
        {
            // Entering and leaving Play Mode reloads the domain: the test's locals do not survive it, so the cycle number,
            // the cycle start and the in-play sample live in SessionState.
            SessionState.EraseString(MemoryKey);
            SessionState.SetInt(MemoryKey + ".cycle", 1);
            EditorSceneManager.OpenScene(HollowmerePaths.BootScene, OpenSceneMode.Single);
            while (SessionState.GetInt(MemoryKey + ".cycle", 11) <= 10)
            {
                SessionState.SetString(MemoryKey + ".start", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
                yield return new EnterPlayMode();
                yield return WaitForGame(90);
                for (int i = 0; i < 60; i++)
                {
                    yield return null;
                }

                SessionState.SetString(MemoryKey + ".play", Profiler.GetTotalAllocatedMemoryLong().ToString(CultureInfo.InvariantCulture));
                yield return new ExitPlayMode();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                EditorUtility.UnloadUnusedAssetsImmediate();
                yield return null;
                int cycle = SessionState.GetInt(MemoryKey + ".cycle", 1);
                long started = long.Parse(SessionState.GetString(MemoryKey + ".start", "0"), CultureInfo.InvariantCulture);
                var sample = new JObject
                {
                    ["cycle"] = cycle,
                    ["playAllocated"] = long.Parse(SessionState.GetString(MemoryKey + ".play", "0"), CultureInfo.InvariantCulture),
                    ["allocated"] = Profiler.GetTotalAllocatedMemoryLong(),
                    ["reserved"] = Profiler.GetTotalReservedMemoryLong(),
                    ["managed"] = GC.GetTotalMemory(false),
                    ["ms"] = (long)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - started).TotalMilliseconds,
                };
                JArray series = JArray.Parse(SessionState.GetString(MemoryKey, "[]"));
                series.Add(sample);
                SessionState.SetString(MemoryKey, series.ToString(Formatting.None));
                SessionState.SetInt(MemoryKey + ".cycle", cycle + 1);
            }

            JArray all = JArray.Parse(SessionState.GetString(MemoryKey, "[]"));
            Assert.That(all.Count, Is.EqualTo(10));
            double allocated1 = all[0].Value<double>("allocated");
            double reserved1 = all[0].Value<double>("reserved");
            foreach (JToken sample in all)
            {
                sample["allocatedGrowthPct"] = Math.Round((sample.Value<double>("allocated") - allocated1) * 100.0 / allocated1, 2);
                sample["reservedGrowthPct"] = Math.Round((sample.Value<double>("reserved") - reserved1) * 100.0 / reserved1, 2);
            }

            double lastAllocated = all[9].Value<double>("allocatedGrowthPct");
            double lastReserved = all[9].Value<double>("reservedGrowthPct");
            var report = new JObject
            {
                ["what"] = "W-GAME-08: Boot.unity Play/Edit cycles in the Editor; memory sampled after each exit (GC + UnloadUnusedAssetsImmediate)",
                ["revision"] = HollowmereGame.Revision(),
                ["unity"] = Application.unityVersion,
                ["cycles"] = all,
                ["allocatedGrowthPctCycle10"] = lastAllocated,
                ["reservedGrowthPctCycle10"] = lastReserved,
            };
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string output = Path.Combine(repo, "artifacts", "studio", "evidence", "P3.1", "memory-cycles.json");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, report.ToString(Formatting.Indented) + "\n");
            Debug.Log("[P3.1] memory-cycles.json " + report.ToString(Formatting.None));
            Debug.Log("[P3.1] memory cycles: allocated +" + lastAllocated.ToString(CultureInfo.InvariantCulture) + "%, reserved +"
                + lastReserved.ToString(CultureInfo.InvariantCulture) + "% at cycle 10 (" + output + ")");
            SessionState.EraseString(MemoryKey);
            Assert.That(lastAllocated, Is.LessThan(25.0), "allocated memory grows by less than 25% over ten cycles");
        }
    }
}
