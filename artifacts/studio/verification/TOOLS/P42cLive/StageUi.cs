#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.Boot;
using Hollowmere.Game;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace P42c.Live
{
    public sealed class StageUi : ScriptableSingleton<StageUi>
    {
        private Task<Diagnostic?>? pending;
        private CandidateEntry? entry;
        private const string Prefix = "P42c.Stage.";
        private static StudioUiContext Context => StudioUiSession.Context;
        private static string Output => SessionState.GetString(Prefix + "out", "");
        private static string Candidate => Environment.GetEnvironmentVariable("GAMECORE_P42C_CANDIDATE")!;
        private static int Phase { get => SessionState.GetInt(Prefix + "phase", 0); set => SessionState.SetInt(Prefix + "phase", value); }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        public static void Submit()
        {
            SessionState.SetString(Prefix + "out", Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!);
            SessionState.SetString(Prefix + "mode", "submit"); Phase = 0;
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.ToString("o"));
            Hook();
        }

        public static void Review()
        {
            SessionState.SetString(Prefix + "out", Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE")!);
            SessionState.SetString(Prefix + "mode", "review"); Phase = 0;
            SessionState.SetString(Prefix + "start", DateTime.UtcNow.ToString("o"));
            Hook();
        }

        private static void Write(string name, JObject data) => File.WriteAllText(Path.Combine(Output, name + ".json"), data.ToString());
        private static void Shot(string name) => UnityWindowCapture.CaptureStudio(Path.Combine(Output, name + ".png"), false);
        private static void Stop(bool ok, string reason)
        {
            Write("outcome", new JObject { ["ok"] = ok, ["reason"] = reason, ["phase"] = Phase, ["utc"] = DateTime.UtcNow.ToString("o") });
            SessionState.SetString(Prefix + "mode", "");
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static void Tick()
        {
            string mode = SessionState.GetString(Prefix + "mode", "");
            if (mode.Length == 0 || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try { instance.Advance(mode); }
            catch (Exception ex) { Write("error", new JObject { ["message"] = ex.ToString() }); Stop(false, ex.Message); }
        }

        private void Advance(string mode)
        {
            if ((DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "start", ""))).TotalMinutes > 25)
            { Stop(false, "bounded UI timeout"); return; }
            if (Phase == 0)
            {
                EtosStudioSession.Start();
                EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                EditorApplication.ExecuteMenuItem("GameCore/Studio/Open Studio");
                Phase = 1; return;
            }
            if (Phase == 1)
            {
                if (!Context.Gateway.Status.AgentReady) return;
                StageAdmission admission = StageAdmission.Of(Context.Runtime);
                ChangeSet candidate = admission.RetainCandidate(Candidate);
                SessionState.SetString(Prefix + "id", candidate.Id);
                if (mode == "review")
                {
                    JObject saved = ReadBinding();
                    string key = "GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(Context.Runtime.Paths.ProjectRoot)) + "." + candidate.Id;
                    SessionState.SetString(key, saved.ToString());
                }
                entry = Context.Candidates.Add(candidate.Id, candidate, Context.Runtime.Registry.Catalog.Revision);
                StudioCandidatesWindow.Open(entry.Id);
                pending = mode == "submit" ? Context.Candidates.RequestStage(entry) : Context.Candidates.RefreshStage(entry);
                Phase = 2; return;
            }
            if (Phase == 2)
            {
                if (entry == null || pending == null) throw new InvalidOperationException("UI request state lost");
                if (mode == "submit" && !string.IsNullOrEmpty(entry.StageJobId))
                {
                    Write("stage-request", new JObject { ["jobId"] = entry.StageJobId, ["request"] = JObject.FromObject(entry.StageRequest!), ["candidate"] = Candidate });
                    // Closing the creator Editor frees this packet's only Editor before sandbox Unity starts.
                    Stop(true, "app-origin Stage submitted through panel; Editor closed for sandbox allocation"); return;
                }
                if (!pending.IsCompleted) return;
                Diagnostic? error = pending.GetAwaiter().GetResult();
                bool can = Context.Candidates.CanAdmit(entry);
                Write("panel-verdict", new JObject { ["jobId"] = entry.StageJobId, ["verified"] = entry.VerifiedVerdict != null,
                    ["canAdmit"] = can, ["label"] = Context.Candidates.StageStateOf(entry).Label,
                    ["problem"] = error == null ? "" : error.Code + ": " + error.Message,
                    ["verdict"] = entry.VerifiedVerdict?.Document ?? new JObject() });
                Shot("panel-verdict");
                if (can && Context.Candidates.StageStateOf(entry).Label == "not staged")
                    throw new InvalidOperationException("R5-B verified app-origin badge still says not staged");
                if (!can) { Stop(Environment.GetEnvironmentVariable("GAMECORE_P42C_NEGATIVE") == "1", "Admit disabled: no verified passing verdict"); return; }
                if (Environment.GetEnvironmentVariable("GAMECORE_P42C_REVIEW_ONLY") == "1") { Stop(true, "verified verdict displayed; admission reserved for warm run"); return; }
                Phase = 3; EditorApplication.EnterPlaymode(); return;
            }
            string id = SessionState.GetString(Prefix + "id", "");
            if (Phase == 3)
            {
                if (!EditorApplication.isPlaying) return;
                GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
                if (boot?.Saves == null || game?.Director == null || !boot.AdmissionReady(boot.Saves)) return;
                int coins = game.Director.ItemCount("OldCoin");
                SessionState.SetInt(Prefix + "coins", coins + 7);
                if (!boot.Modules!.Inventory.Commands!.Grant("OldCoin", 7).Admitted) throw new InvalidOperationException("coin setup refused");
                Phase = 4; return;
            }
            if (Phase == 4)
            {
                HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
                if (game?.Director?.ItemCount("OldCoin") != SessionState.GetInt(Prefix + "coins", -1)) return;
                if (!Context.Gateway.Status.AgentReady || !Context.Candidates.CanRequestStage) return;
                // Rebuild the panel after Play's real domain reload; re-fetch and verify through the service.
                Phase = 5;
                RestoreEntry(); pending = Context.Candidates.RefreshStage(entry!); return;
            }
            if (Phase == 5)
            {
                if (pending == null || !pending.IsCompleted) return;
                Diagnostic? problem = pending.GetAwaiter().GetResult();
                bool canAdmit = Context.Candidates.CanAdmit(entry!);
                Write("play-verification", new JObject { ["problem"] = problem == null ? "" : problem.Code + ": " + problem.Message,
                    ["canAdmit"] = canAdmit, ["verified"] = entry!.VerifiedVerdict != null,
                    ["entryStage"] = entry.Stage.ToString(), ["staging"] = entry.Staging,
                    ["expected"] = JObject.FromObject(entry.StageRequest!),
                    ["current"] = JObject.FromObject(StageAdmission.Of(Context.Runtime).BuildStageRequest(entry.ChangeSet, Context.Runtime.Paths.ProjectRoot)) });
                if (problem != null || !canAdmit) throw new InvalidOperationException("Play verdict verification failed: " + (problem?.Message ?? "CanAdmit false"));
                string? liveHash = StageAdmission.Of(Context.Runtime).LiveHash(null, out string? catalogProblem);
                Write("catalog-preflight", new JObject { ["liveHash"] = liveHash ?? "", ["problem"] = catalogProblem ?? "", ["isPlaying"] = EditorApplication.isPlaying });
                if (Environment.GetEnvironmentVariable("GAMECORE_P42C_CATALOG_PROBE") == "1") { Stop(liveHash != null, catalogProblem ?? "catalog available"); return; }
                SessionState.SetString(Prefix + "admitStart", DateTime.UtcNow.ToString("o")); Phase = 6;
                AdmissionResult requested = Context.Candidates.Admit(entry!, true);
                Write("admit", requested.ToJson());
                if (requested.Outcome != AdmissionOutcome.Pending && requested.Outcome != AdmissionOutcome.Admitted)
                    Stop(false, requested.Reason + ": " + requested.Detail);
                return;
            }
            StageAdmission a = StageAdmission.Of(Context.Runtime);
            if (Phase == 6 || Phase == 9)
            {
                JObject? state = a.ReadPending(id);
                string serial = state?.ToString(Newtonsoft.Json.Formatting.None) ?? "none";
                if (serial != SessionState.GetString(Prefix + "last", ""))
                {
                    File.AppendAllText(Path.Combine(Output, "transitions.jsonl"), new JObject { ["utc"] = DateTime.UtcNow.ToString("o"), ["phase"] = Phase, ["pending"] = state ?? new JObject() }.ToString(Newtonsoft.Json.Formatting.None) + "\n");
                    SessionState.SetString(Prefix + "last", serial);
                }
                StageState stage = CandidateStaging.StateOf(Context.Runtime, id);
                ValidationScenario? scenario = Phase == 6 ? stage.Admission : stage.Undo;
                if (scenario == null || scenario.Status == ScenarioStatus.Pending) return;
                Write(Phase == 6 ? "admission" : "undo", new JObject { ["scenario"] = StudioJson.ToToken(scenario), ["journal"] = StudioJson.ToToken(Context.Runtime.Journal.Read(id)!) });
                if (scenario.Status != ScenarioStatus.Pass) { Stop(false, scenario.Detail ?? "admission or undo failed"); return; }
                if (Phase == 9) { Stop(true, "verified stage, Play capture/restore/smoke and undo completed"); return; }
                HollowmereGame? game = UnityEngine.Object.FindAnyObjectByType<HollowmereGame>();
                int actual = game?.Director?.ItemCount("OldCoin") ?? -1;
                Write("restored-world", new JObject { ["coinsExpected"] = SessionState.GetInt(Prefix + "coins", -1), ["coinsActual"] = actual,
                    ["isPlaying"] = EditorApplication.isPlaying, ["admitMs"] = (DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Prefix + "admitStart", ""))).TotalMilliseconds });
                if (actual != SessionState.GetInt(Prefix + "coins", -1)) { Stop(false, "captured inventory was not restored"); return; }
                Shot("resumed-world"); Phase = 8; EditorApplication.ExitPlaymode(); return;
            }
            if (Phase == 8 && !EditorApplication.isPlaying)
            {
                Phase = 9; Write("undo-request", new JObject { ["ok"] = Context.Runtime.History.Undo(id).Ok });
            }
        }

        private static JObject ReadBinding()
        {
            JObject record = JObject.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("GAMECORE_P42C_INPUT")!));
            string path = (string?)record["request"]?["sourceProject"] ?? string.Empty;
            if (path.StartsWith("~/", StringComparison.Ordinal))
                record["request"]!["sourceProject"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
            return record;
        }

        private void RestoreEntry()
        {
            JObject saved = ReadBinding();
            ChangeSet candidate = StageAdmission.Of(Context.Runtime).RetainCandidate(Candidate);
            string key = "GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(Context.Runtime.Paths.ProjectRoot)) + "." + candidate.Id;
            SessionState.SetString(key, saved.ToString());
            entry = Context.Candidates.Find(candidate.Id) ?? Context.Candidates.Add(candidate.Id, candidate, Context.Runtime.Registry.Catalog.Revision);
        }
    }
}
