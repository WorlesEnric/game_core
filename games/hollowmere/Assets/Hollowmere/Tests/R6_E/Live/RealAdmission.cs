#nullable enable
using System;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using Hollowmere.Boot;
using Hollowmere.Game;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.R6_E
{
    // An observer of the ordinary project runtime: only the initial fetch and creator Admit/Undo
    // are driven here. Product startup, authenticated refresh, game binding and recovery run unaided.
    [InitializeOnLoad]
    public sealed class RealAdmission : ScriptableSingleton<RealAdmission>
    {
        private const long AdmissionBudgetMs = 90000;
        private const long UndoBudgetMs = 180000;
        [NonSerialized] private StageAdmission? admission;
        [NonSerialized] private bool busy;
        [NonSerialized] private string domain = Guid.NewGuid().ToString("N");
        [NonSerialized] private bool refreshObserved;

        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        static RealAdmission()
        {
            if (Argument("-gcR6EContext").Length != 0) EditorApplication.update += PrepareTick;
            if (Argument("-gcR6EConfig").Length == 0) return;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        public static void Run() { }
        private static void Tick() => instance.Poll();
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;
        private static JObject Config => JObject.Parse(File.ReadAllText(Argument("-gcR6EConfig")));
        private static void Save(string evidence, JObject progress) =>
            StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "live-progress.json"), progress.ToString());

        public static void Prepare()
        {
            try
            {
                string output = Argument("-gcR6EContext");
                if (output.Length == 0 || File.Exists(output)) throw new InvalidOperationException("fresh context output is required");
                if (File.Exists(EtosSettings.PathFor(Project)))
                    throw new InvalidOperationException("scratch regression requires absent project ETOS settings; no settings are read or changed by this driver");
                StudioRuntime runtime = StudioServices.Runtime;
                StageAdmission stage = StageAdmission.Of(runtime);
                if (Directory.Exists(stage.StateRoot) && Directory.GetFiles(stage.StateRoot, "pending-*.json").Length != 0)
                    throw new InvalidOperationException("existing pending admission must be resolved by its owner before this regression");
                if (Directory.Exists(Path.Combine(Project, "Packages", HollowmereAdmittedSmoke.PressurePlatePackage)))
                    throw new InvalidOperationException("pressure-plate package is already installed");
                SessionState.SetBool("Hollowmere.R6_E.Preparing", true);
                EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                Debug.LogError(new SecretRedactor().Redact(error.ToString()));
                EditorApplication.Exit(1);
            }
        }

        private static void PrepareTick()
        {
            if (!SessionState.GetBool("Hollowmere.R6_E.Preparing", false) || !EditorApplication.isPlaying
                || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
            if (boot?.Saves == null || !boot.AdmissionReady(boot.Saves)) return;
            try
            {
                StudioRuntime runtime = StudioServices.Runtime;
                var context = new JObject {
                    ["changeSetId"] = IdDerivation.NewChangeSetId(),
                    ["projectId"] = EtosProjectContext.LoadProjectId(Project),
                    ["sourceProject"] = Project,
                    ["sourceRevision"] = EtosProjectContext.SourceRevision(Project),
                    ["catalogRevision"] = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision(),
                    ["graphics"] = GraphicsWitness(),
                };
                StudioPaths.WriteAllTextAtomic(Argument("-gcR6EContext"), context.ToString());
                SessionState.SetBool("Hollowmere.R6_E.Preparing", false);
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogError(new SecretRedactor().Redact(error.ToString()));
                EditorApplication.Exit(1);
            }
        }

        private static JObject GraphicsWitness()
        {
            string device = SystemInfo.graphicsDeviceName;
            string lower = device.ToLowerInvariant();
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
                || string.IsNullOrWhiteSpace(device) || lower.Contains("llvmpipe")
                || lower.Contains("lavapipe") || lower.Contains("swiftshader") || lower.Contains("software")
                || Environment.GetEnvironmentVariable("DISPLAY") != ":1")
                throw new InvalidOperationException("R6-E requires a real GPU graphical Editor on DISPLAY=:1");
            return new JObject {
                ["isBatchMode"] = Application.isBatchMode, ["display"] = Environment.GetEnvironmentVariable("DISPLAY"),
                ["deviceType"] = SystemInfo.graphicsDeviceType.ToString(), ["deviceName"] = device,
                ["deviceVendor"] = SystemInfo.graphicsDeviceVendor, ["deviceVersion"] = SystemInfo.graphicsDeviceVersion,
                ["isPlaying"] = EditorApplication.isPlaying, ["observedMs"] = Now,
            };
        }

        private static void BeforeReload()
        {
            JObject config = Config;
            string evidence = (string)config["evidence"]!;
            string progressPath = Path.Combine(evidence, "live-progress.json");
            if (!File.Exists(progressPath)) return;
            JObject progress = JObject.Parse(File.ReadAllText(progressPath));
            JArray reloads = progress["reloads"] as JArray ?? new JArray();
            JObject? pending = instance.admission?.ReadPending((string)config["request"]!["changeSetId"]!);
            reloads.Add(new JObject {
                ["phase"] = progress["phase"], ["pendingPhase"] = pending?["phase"],
                ["packageInstalled"] = Directory.Exists(Path.Combine(Project, "Packages", HollowmereAdmittedSmoke.PressurePlatePackage)),
                ["domain"] = instance.domain, ["atMs"] = Now,
            });
            progress["reloads"] = reloads;
            Save(evidence, progress);
        }

        // Read-only instrumentation of the game's already registered smoke. Never call Bind or
        // install a substitute callback: observe the actual production subscriber and its results.
        private static HollowmereAdmittedSmoke? ObservedSmoke(GameBoot boot)
        {
            AdmissionSmokeFrames? frames = boot.GetComponent<AdmissionSmokeFrames>();
            if (frames == null) return null;
            var subscribers = typeof(AdmissionSmokeFrames).GetField("Frame", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(frames) as Delegate;
            if (subscribers == null) return null;
            foreach (Delegate subscriber in subscribers.GetInvocationList())
                if (subscriber.Target is HollowmereAdmittedSmoke smoke && smoke.Transitions.Count > 0
                    && smoke.Transitions[smoke.Transitions.Count - 1] == "Passed") return smoke;
            return null;
        }

        private async void Poll()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            busy = true;
            try
            {
                JObject config = Config;
                string evidence = (string)config["evidence"]!;
                string progressPath = Path.Combine(evidence, "live-progress.json");
                JObject progress = File.Exists(progressPath) ? JObject.Parse(File.ReadAllText(progressPath))
                    : new JObject { ["phase"] = "start", ["startedMs"] = Now };
                string phase = (string)progress["phase"]!;
                if (phase == "complete") return;
                long limit = phase == "admission" ? AdmissionBudgetMs : phase == "undo" ? UndoBudgetMs : 300000;
                string timer = phase == "admission" ? "admissionStartedMs" : phase == "undo" ? "undoStartedMs" : "startedMs";
                if (Now - (long)progress[timer]! > limit)
                    throw new InvalidOperationException(phase + " exceeded " + limit + " milliseconds; durable recovery is retained");
                StageCandidateRequest expected = config["request"]!.ToObject<StageCandidateRequest>()!;
                // After an admission reload, do not even force runtime creation ahead of the
                // resumer: the normal product startup must recreate and bind the current runtime.
                if (phase != "start" && !StudioServices.HasRuntime) return;
                StudioRuntime runtime = StudioServices.Runtime;
                if (admission == null || !ReferenceEquals(admission.Runtime, runtime))
                {
                    if (admission != null) admission.Finished -= Finished;
                    admission = StageAdmission.Of(runtime);
                    admission.Finished += Finished;
                    JArray domains = progress["domains"] as JArray ?? new JArray();
                    domains.Add(new JObject { ["domain"] = domain, ["phase"] = phase, ["atMs"] = Now,
                        ["runtimeReloads"] = StudioServices.instance.DomainReloads });
                    progress["domains"] = domains;
                    Save(evidence, progress);
                }
                if (phase == "start")
                {
                    GraphicsWitness();
                    EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                    progress["phase"] = "play";
                    Save(evidence, progress);
                    EditorApplication.isPlaying = true;
                    return;
                }
                GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                if (phase == "play")
                {
                    if (!EditorApplication.isPlaying || boot?.Saves == null || !boot.AdmissionReady(boot.Saves)) return;
                    if (EtosStudioSession.Gateway == null || admission.Options.StageService == null) return;
                    if (!ReferenceEquals(EtosStudioSession.Gateway.Runtime, runtime)
                        || EtosStudioSession.Gateway.Client.Options.NodeUrl != (string)config["nodeUrl"]!
                        || admission.Options.ProjectId != expected.ProjectId
                        || admission.Options.SourceRevision?.Invoke() != expected.SourceRevision
                        || admission.Options.CatalogRevision?.Invoke() != expected.CatalogRevision)
                        throw new InvalidOperationException("automatic session context differs from fresh scratch stage: " + new JObject {
                            ["currentRuntime"] = ReferenceEquals(EtosStudioSession.Gateway.Runtime, runtime),
                            ["node"] = EtosStudioSession.Gateway.Client.NodeUrl, ["expectedNode"] = config["nodeUrl"],
                            ["project"] = admission.Options.ProjectId, ["expectedProject"] = expected.ProjectId,
                            ["source"] = admission.Options.SourceRevision?.Invoke(), ["expectedSource"] = expected.SourceRevision,
                            ["catalog"] = admission.Options.CatalogRevision?.Invoke(), ["expectedCatalog"] = expected.CatalogRevision,
                        });
                    HollowmereGame game = boot.GetComponent<HollowmereGame>();
                    if (progress["coins"] == null)
                    {
                        if (game.Director!.ItemCount("OldCoin") != 2 || !boot.Modules!.Inventory.Commands!.Grant("OldCoin", 7).Admitted)
                            throw new InvalidOperationException("fresh Boot/two coins/checkpoint mutation prerequisite failed");
                        progress["coins"] = 9;
                        Save(evidence, progress);
                        return;
                    }
                    if (game.Director!.ItemCount("OldCoin") != 9) return;
                    ChangeSet candidate = admission.RetainCandidate((string)config["candidate"]!);
                    await admission.FetchVerdict((string)config["jobId"]!, expected);
                    progress["initialVerifiedJobId"] = (string)config["jobId"]!;
                    progress["phase"] = "admission";
                    progress["admissionStartedMs"] = Now;
                    progress["admissionDomain"] = domain;
                    Save(evidence, progress);
                    AdmissionResult result = admission.Admit(candidate, captureAndStop: true);
                    if (result.Outcome != AdmissionOutcome.Pending) throw new InvalidOperationException(result.Detail);
                    return;
                }
                if (phase == "admission" || phase == "undo")
                {
                    JObject? pending = admission.ReadPending(expected.ChangeSetId);
                    if (pending != null)
                        StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "last-pending.json"), pending.ToString());
                    if (runtime.Journal.Read(expected.ChangeSetId)?.EffectiveState == ChangeSetState.Failed)
                        throw new InvalidOperationException("admission rolled back; inspect retained compile records");
                    if (phase == "admission" && domain != (string)progress["admissionDomain"]!
                        && admission.VerdictOf(expected.ChangeSetId) != null && !refreshObserved)
                    {
                        if (admission.Options.StageService == null || EtosStudioSession.Gateway == null
                            || !ReferenceEquals(EtosStudioSession.Gateway.Runtime, runtime))
                            throw new InvalidOperationException("post-reload authenticated verdict is not bound to the current session");
                        StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "automatic-refresh.json"), new JObject {
                            ["domain"] = domain, ["admissionDomain"] = progress["admissionDomain"], ["atMs"] = Now,
                            ["jobId"] = config["jobId"], ["sessionStarts"] = EtosStudioSession.instance.Starts,
                            ["stageService"] = admission.Options.StageService.GetType().FullName,
                            ["currentRuntimeBound"] = true, ["companionVerified"] = true,
                        }.ToString());
                        refreshObserved = true;
                    }
                    return;
                }
                if (phase == "admitted" && Now - (long)progress["admittedMs"]! >= 2000)
                {
                    progress["phase"] = "undo";
                    progress["undoStartedMs"] = Now;
                    Save(evidence, progress);
                    AdmissionResult result = admission.Undo(expected.ChangeSetId);
                    if (result.Outcome != AdmissionOutcome.Pending) throw new InvalidOperationException(result.Detail);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private void Finished(AdmissionResult result)
        {
            try
            {
                JObject config = Config;
                if (result.ChangeSetId != (string)config["request"]!["changeSetId"]!) return;
                string evidence = (string)config["evidence"]!;
                JObject progress = JObject.Parse(File.ReadAllText(Path.Combine(evidence, "live-progress.json")));
                if (result.Outcome == AdmissionOutcome.Admitted)
                {
                    GameBoot? boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                    HollowmereAdmittedSmoke? smoke = boot == null ? null : ObservedSmoke(boot);
                    long wallMs = Now - (long)progress["admissionStartedMs"]!;
                    if (!EditorApplication.isPlaying || boot?.Saves == null || !boot.AdmissionReady(boot.Saves)
                        || smoke == null || smoke.Steps != 120 || smoke.Transitions.Count != 2
                        || smoke.Transitions[0] != "Pending" || smoke.Transitions[1] != "Passed"
                        || boot.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin") != 9
                        || string.IsNullOrEmpty(result.Predicted) || result.Live != result.Predicted
                        || result.Confinement != "docker" || result.Milliseconds > AdmissionBudgetMs || wallMs > AdmissionBudgetMs
                        || domain == (string)progress["admissionDomain"]!
                        || !refreshObserved)
                        throw new InvalidOperationException("automatic authenticated reload / restored Play / nine coins / signed catalog / tri-state smoke / 90s witness failed");
                    JObject witness = result.ToJson();
                    witness["smokeSteps"] = smoke.Steps;
                    witness["smokeTransitions"] = new JArray(smoke.Transitions);
                    witness["smokeReport"] = smoke.LastReport;
                    witness["coins"] = 9;
                    witness["admissionWallMs"] = wallMs;
                    witness["admissionBudgetMs"] = AdmissionBudgetMs;
                    witness["resumedDomain"] = domain;
                    witness["graphics"] = GraphicsWitness();
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "live-admit.json"), witness.ToString());
                    ScreenCapture.CaptureScreenshot(Path.Combine(evidence, "admitted-play.png"));
                    boot.Saves.Delete(result.CaptureSlot!);
                    progress["phase"] = "admitted";
                    progress["admittedMs"] = Now;
                    Save(evidence, progress);
                }
                else if (result.Outcome == AdmissionOutcome.Undone)
                {
                    long wallMs = Now - (long)progress["undoStartedMs"]!;
                    if (string.IsNullOrEmpty(result.Before) || result.Before != result.Live || wallMs > UndoBudgetMs
                        || admission!.ReadPending(result.ChangeSetId) != null
                        || Directory.Exists(Path.Combine(Project, "Packages", HollowmereAdmittedSmoke.PressurePlatePackage)))
                        throw new InvalidOperationException("undo catalog/package/pending-state witness or 180s removal bound failed");
                    JObject witness = result.ToJson();
                    witness["undoWallMs"] = wallMs;
                    witness["undoBudgetMs"] = UndoBudgetMs;
                    witness["graphics"] = GraphicsWitness();
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "live-undo.json"), witness.ToString());
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
            string safe = new SecretRedactor().Redact(error.ToString());
            Debug.LogError(safe);
            try { StudioPaths.WriteAllTextAtomic(Path.Combine((string)Config["evidence"]!, "live-failure.txt"), safe); }
            finally { EditorApplication.Exit(1); }
        }
    }
}
