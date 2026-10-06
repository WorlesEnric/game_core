#nullable enable
using System;
using System.IO;
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
using UnityEngine.Rendering;

namespace Hollowmere.R6_D
{
    // Like R6_A's real driver, this runs outside the test runner so actual package compilation
    // can reload the domain. Only JSON progress and the production admission journal survive.
    [InitializeOnLoad]
    public sealed class RealAdmission : ScriptableSingleton<RealAdmission>
    {
        private const long AdmissionBudgetMs = 90000;
        private const long UndoBudgetMs = 180000;
        [NonSerialized] private StudioRuntime? runtime;
        [NonSerialized] private StageAdmission? admission;
        [NonSerialized] private HollowmereAdmittedSmoke? smoke;
        [NonSerialized] private GameBoot? boundBoot;
        [NonSerialized] private bool busy;

        private static string ConfigPath
        {
            get
            {
                string[] args = Environment.GetCommandLineArgs();
                int index = Array.IndexOf(args, "-gcR6DConfig");
                return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
            }
        }

        static RealAdmission()
        {
            if (ConfigPath.Length > 0) EditorApplication.update += Tick;
        }

        public static void Run() { }
        private static void Tick() => instance.Poll();
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static void Save(string evidence, JObject progress) =>
            File.WriteAllText(Path.Combine(evidence, "live-progress.json"), progress.ToString());

        private static JObject GraphicsWitness()
        {
            string device = SystemInfo.graphicsDeviceName;
            string lower = device.ToLowerInvariant();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || string.IsNullOrWhiteSpace(device) || lower.Contains("llvmpipe")
                || lower.Contains("lavapipe") || lower.Contains("swiftshader") || lower.Contains("software")
                || Environment.GetEnvironmentVariable("DISPLAY") != ":1")
                throw new InvalidOperationException("R6-D requires a real graphical GPU Editor on DISPLAY=:1, not batchmode or a software renderer");
            return new JObject {
                ["isBatchMode"] = Application.isBatchMode,
                ["display"] = Environment.GetEnvironmentVariable("DISPLAY"),
                ["deviceType"] = SystemInfo.graphicsDeviceType.ToString(),
                ["deviceName"] = device,
                ["deviceVendor"] = SystemInfo.graphicsDeviceVendor,
                ["deviceId"] = SystemInfo.graphicsDeviceID,
                ["deviceVersion"] = SystemInfo.graphicsDeviceVersion,
                ["deviceMemoryMb"] = SystemInfo.graphicsMemorySize,
                ["isPlaying"] = EditorApplication.isPlaying,
                ["observedMs"] = Now,
            };
        }

        private async void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            busy = true;
            try
            {
                JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                string evidence = (string)config["evidence"]!;
                string progressPath = Path.Combine(evidence, "live-progress.json");
                JObject progress = File.Exists(progressPath) ? JObject.Parse(File.ReadAllText(progressPath))
                    : new JObject { ["phase"] = "start", ["startedMs"] = Now };
                string phase = (string)progress["phase"]!;
                if (phase == "complete") throw new InvalidOperationException("use a fresh evidence directory for a new graphical run");
                long limit = phase == "admission" ? AdmissionBudgetMs : phase == "undo" ? UndoBudgetMs : 300000;
                string timer = phase == "admission" ? "admissionStartedMs" : phase == "undo" ? "undoStartedMs" : "startedMs";
                if (Now - (long)progress[timer]! > limit)
                    throw new InvalidOperationException(phase + " exceeded " + limit + " milliseconds");
                StageCandidateRequest expected = config["request"]!.ToObject<StageCandidateRequest>()!;
                if (runtime == null)
                {
                    Save(evidence, progress);
                    File.WriteAllText(Path.Combine(evidence, "graphics.json"), GraphicsWitness().ToString());
                    string project = Directory.GetParent(Application.dataPath)!.FullName;
                    runtime = StudioRuntime.Create(new StudioRuntimeOptions {
                        Paths = new StudioPaths(project, Path.Combine(evidence, "live-state"), "r6-d-real"), LoadIndexCache = false });
                    string url = (string)config["nodeUrl"]!;
                    var client = new CompanionClient(new EtosClientOptions { NodeUrl = url, ProjectId = expected.ProjectId },
                        new EtosCredentials("etk_app_key_for_tests_0001", url, "synthetic scratch node"));
                    admission = StageAdmission.Configure(runtime, new AdmissionOptions {
                        StageService = new CompanionStageService(client), ProjectId = expected.ProjectId,
                        SourceRevision = () => expected.SourceRevision, CatalogRevision = () => expected.CatalogRevision });
                    admission.Finished += Finished;
                    // FetchVerdict performs authenticated fetch AND service verification of the signed
                    // job and all expected revision/digest bindings; no local verdict is injected.
                    await admission.FetchVerdict((string)config["jobId"]!, expected);
                    progress["verifiedJobId"] = (string)config["jobId"]!;
                    progress["verificationCount"] = ((int?)progress["verificationCount"] ?? 0) + 1;
                    Save(evidence, progress);
                }
                if (phase == "start")
                {
                    EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                    progress["phase"] = "play";
                    Save(evidence, progress);
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
                    HollowmereGame game = boot.GetComponent<HollowmereGame>();
                    if (progress["coins"] == null)
                    {
                        if (game.Director!.ItemCount("OldCoin") != 2)
                            throw new InvalidOperationException("fresh Boot must start with two OldCoin before the checkpoint mutation");
                        if (!boot.Modules!.Inventory.Commands!.Grant("OldCoin", 7).Admitted)
                            throw new InvalidOperationException("checkpoint coin grant refused");
                        progress["coins"] = 9;
                        Save(evidence, progress);
                        return;
                    }
                    if (game.Director!.ItemCount("OldCoin") != 9) return;
                    ChangeSet candidate = admission!.RetainCandidate((string)config["candidate"]!);
                    progress["phase"] = "admission";
                    progress["admissionStartedMs"] = Now;
                    Save(evidence, progress);
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
                    progress["undoStartedMs"] = Now;
                    Save(evidence, progress);
                    admission!.Undo(expected.ChangeSetId);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private void Finished(AdmissionResult result)
        {
            try
            {
                JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                string evidence = (string)config["evidence"]!;
                JObject progress = JObject.Parse(File.ReadAllText(Path.Combine(evidence, "live-progress.json")));
                if (result.Outcome == AdmissionOutcome.Admitted)
                {
                    GameBoot boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                    long wallMs = Now - (long)progress["admissionStartedMs"]!;
                    if (!EditorApplication.isPlaying || boot == null || boot.Saves == null || !boot.AdmissionReady(boot.Saves)
                        || smoke == null || smoke.Steps != 120 || smoke.Transitions.Count != 2
                        || smoke.Transitions[0] != "Pending" || smoke.Transitions[1] != "Passed"
                        || boot.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin") != 9
                        || string.IsNullOrEmpty(result.Predicted) || result.Live != result.Predicted
                        || result.Confinement != "docker" || result.Milliseconds > AdmissionBudgetMs || wallMs > AdmissionBudgetMs)
                        throw new InvalidOperationException("graphical restored Play / nine coins / signed catalog / 120-frame tri-state smoke / 90s admission witness failed");
                    JObject witness = result.ToJson();
                    witness["smokeSteps"] = smoke.Steps;
                    witness["smokeTransitions"] = new JArray(smoke.Transitions);
                    witness["coins"] = 9;
                    witness["admissionWallMs"] = wallMs;
                    witness["admissionBudgetMs"] = AdmissionBudgetMs;
                    witness["graphics"] = GraphicsWitness();
                    File.WriteAllText(Path.Combine(evidence, "live-admit.json"), witness.ToString());
                    boot.Saves.Delete(result.CaptureSlot!);
                    progress["phase"] = "admitted";
                    progress["admittedMs"] = Now;
                    Save(evidence, progress);
                }
                else if (result.Outcome == AdmissionOutcome.Undone)
                {
                    long wallMs = Now - (long)progress["undoStartedMs"]!;
                    if (string.IsNullOrEmpty(result.Before) || result.Before != result.Live || wallMs > UndoBudgetMs)
                        throw new InvalidOperationException("undo catalog mismatch or 180s removal bound exceeded");
                    JObject witness = result.ToJson();
                    witness["undoWallMs"] = wallMs;
                    witness["undoBudgetMs"] = UndoBudgetMs;
                    witness["graphics"] = GraphicsWitness();
                    File.WriteAllText(Path.Combine(evidence, "live-undo.json"), witness.ToString());
                    progress["phase"] = "complete";
                    progress["completedMs"] = Now;
                    Save(evidence, progress);
                    EditorApplication.Exit(0);
                }
                else throw new InvalidOperationException(result.Outcome + ": " + result.Reason + " " + result.Detail);
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Fail(Exception error)
        {
            Debug.LogException(error);
            try
            {
                JObject config = JObject.Parse(File.ReadAllText(ConfigPath));
                File.WriteAllText(Path.Combine((string)config["evidence"]!, "live-failure.txt"), error.ToString());
            }
            finally { EditorApplication.Exit(1); }
        }
    }
}
