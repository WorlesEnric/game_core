#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using Hollowmere.Boot;
using Hollowmere.Game;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.R6_A
{
    // The executeMethod driver survives genuine script reloads; test-runner compilation locks
    // would prevent the real admission compiler from running inside a UnityTest iterator.
    [InitializeOnLoad]
    public sealed class RealAdmission : ScriptableSingleton<RealAdmission>
    {
        [NonSerialized] private StudioRuntime? runtime;
        [NonSerialized] private CompanionClient? client;
        [NonSerialized] private StageAdmission? admission;
        [NonSerialized] private bool busy;
        [NonSerialized] private HollowmereAdmittedSmoke? smoke;
        [NonSerialized] private GameBoot? boundBoot;
        private static string ConfigPath
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-gcR6Config");
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
            }
        }
        static RealAdmission()
        {
            if (ConfigPath.Length > 0) EditorApplication.update += Tick;
        }
        public static void Run() { }
        private static void Tick() => instance.Poll();
        private async void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            busy = true;
            try
            {
                JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                string evidence = (string)config["evidence"]!;
                string progressPath = Path.Combine(evidence, "live-progress.json");
                JObject progress = File.Exists(progressPath) ? JObject.Parse(File.ReadAllText(progressPath)) : new JObject { ["phase"] = "start" };
                if (progress["startedMs"] == null) progress["startedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)progress["startedMs"]! > 300000)
                    throw new InvalidOperationException("real admission lifecycle exceeded 300 seconds");
                StageCandidateRequest expected = config["request"]!.ToObject<StageCandidateRequest>()!;
                if (runtime == null)
                {
                    string project = Directory.GetParent(Application.dataPath)!.FullName;
                    runtime = StudioRuntime.Create(new StudioRuntimeOptions {
                        Paths = new StudioPaths(project, Path.Combine(evidence, "live-state"), "r6-real"), LoadIndexCache = false });
                    string url = (string)config["nodeUrl"]!;
                    client = new CompanionClient(new EtosClientOptions { NodeUrl = url, ProjectId = expected.ProjectId },
                        new EtosCredentials("etk_app_key_for_tests_0001", url, "synthetic scratch node"));
                    admission = StageAdmission.Configure(runtime, new AdmissionOptions {
                        StageService = new CompanionStageService(client), ProjectId = expected.ProjectId,
                        SourceRevision = () => expected.SourceRevision, CatalogRevision = () => expected.CatalogRevision });
                    admission.Finished += Finished;
                    await admission.FetchVerdict((string)config["jobId"]!, expected);
                }
                string phase = (string)progress["phase"]!;
                if (phase == "start")
                {
                    EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                    progress["phase"] = "play";
                    File.WriteAllText(progressPath, progress.ToString());
                    EditorApplication.isPlaying = true;
                    return;
                }
                GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                if (EditorApplication.isPlaying && boot?.Saves != null && boot.AdmissionReady(boot.Saves) && boundBoot != boot)
                {
                    boundBoot = boot;
                    smoke = HollowmereStudioAdmission.Bind(runtime, boot, boot.Saves);
                }
                if (phase == "play")
                {
                    if (!EditorApplication.isPlaying || boot?.Saves == null || !boot.AdmissionReady(boot.Saves)) return;
                    if (progress["coins"] == null)
                    {
                        HollowmereGame game = boot.GetComponent<HollowmereGame>();
                        progress["coins"] = game.Director!.ItemCount("OldCoin") + 7;
                        if (!boot.Modules!.Inventory.Commands!.Grant("OldCoin", 7).Admitted) throw new InvalidOperationException("coin grant refused");
                        File.WriteAllText(progressPath, progress.ToString());
                        return;
                    }
                    if (boot.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin") != (int)progress["coins"]!) return;
                    ChangeSet candidate = admission!.RetainCandidate((string)config["candidate"]!);
                    progress["phase"] = "admission";
                    File.WriteAllText(progressPath, progress.ToString());
                    AdmissionResult result = admission.Admit(candidate, captureAndStop: true);
                    if (result.Outcome != AdmissionOutcome.Pending) throw new InvalidOperationException(result.Detail);
                    return;
                }
                if (phase == "admission" || phase == "undo")
                {
                    JObject? pending = admission!.ReadPending(expected.ChangeSetId);
                    if (pending != null)
                    {
                        File.WriteAllText(Path.Combine(evidence, "last-pending.json"), pending.ToString());
                        AdmissionResult result = admission.Resume(expected.ChangeSetId);
                        if (result.Outcome == AdmissionOutcome.UndoFailed) throw new InvalidOperationException(result.Detail);
                    }
                    else if (runtime.Journal.Read(expected.ChangeSetId)?.EffectiveState == ChangeSetState.Failed)
                        throw new InvalidOperationException("admission rolled back; inspect the retained compile record");
                    return;
                }
                if (phase == "admitted")
                {
                    progress["phase"] = "undo";
                    File.WriteAllText(progressPath, progress.ToString());
                    admission!.Undo(expected.ChangeSetId);
                }
            }
            catch (Exception error)
            {
                JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                File.WriteAllText(Path.Combine((string)config["evidence"]!, "live-failure.txt"), error.ToString());
                EditorApplication.Exit(1);
            }
            finally { busy = false; }
        }
        private void Finished(AdmissionResult result)
        {
            JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
            string evidence = (string)config["evidence"]!;
            string progressPath = Path.Combine(evidence, "live-progress.json");
            JObject progress = JObject.Parse(File.ReadAllText(progressPath));
            if (result.Outcome == AdmissionOutcome.Admitted)
            {
                GameBoot boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                if (smoke == null || smoke.Steps != 120 || smoke.Transitions.Count != 2
                    || smoke.Transitions[0] != "Pending" || smoke.Transitions[1] != "Passed"
                    || boot.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin") != (int)progress["coins"]!
                    || result.Live != result.Predicted || result.Milliseconds > 90000)
                    throw new InvalidOperationException("real restore / signed catalog / tri-state smoke witness failed");
                JObject witness = result.ToJson();
                witness["smokeSteps"] = smoke.Steps;
                witness["smokeTransitions"] = new JArray(smoke.Transitions);
                File.WriteAllText(Path.Combine(evidence, "live-admit.json"), witness.ToString());
                boot.Saves!.Delete(result.CaptureSlot!);
                progress["phase"] = "admitted";
                File.WriteAllText(progressPath, progress.ToString());
            }
            else if (result.Outcome == AdmissionOutcome.Undone)
            {
                if (result.Before != result.Live) throw new InvalidOperationException("undo catalog mismatch");
                File.WriteAllText(Path.Combine(evidence, "live-undo.json"), result.ToJson().ToString());
                EditorApplication.Exit(0);
            }
            else
            {
                File.WriteAllText(Path.Combine(evidence, "live-failure.txt"), result.Outcome + ": " + result.Reason + " " + result.Detail);
                EditorApplication.Exit(1);
            }
        }
    }
}
