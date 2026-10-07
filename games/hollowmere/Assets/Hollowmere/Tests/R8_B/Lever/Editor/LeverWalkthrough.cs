#nullable enable
using System;
using System.IO;
using System.Reflection;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.UI;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using Hollowmere.Boot;
using Hollowmere.Game;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hollowmere.R8_B
{
    // An observer of the ordinary project runtime: only the initial fetch and creator Admit/Undo
    // are driven here. Product startup, authenticated refresh, game binding and recovery run unaided.
    [InitializeOnLoad]
    public sealed class LeverWalkthrough : ScriptableSingleton<LeverWalkthrough>
    {
        private const string LeverPackage = "com.hollowmere.mechanism.lever";
        private const long AdmissionBudgetMs = 90000;
        private const long UndoBudgetMs = 180000;
        [NonSerialized] private StageAdmission? admission;
        [NonSerialized] private bool busy;
        [NonSerialized] private string domain = Guid.NewGuid().ToString("N");
        [NonSerialized] private bool refreshObserved;
        [NonSerialized] private StudioCandidatesWindow? window;
        [NonSerialized] private string? lastPending;

        private static bool IsLever(JObject config) => (string?)config["mechanism"] != "pressure-plate";
        private static string Package(JObject config) => IsLever(config) ? LeverPackage : HollowmereAdmittedSmoke.PressurePlatePackage;

        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : string.Empty;
        }

        static LeverWalkthrough()
        {
            if (Argument("-gcR8CStage").Length != 0) EditorApplication.update += StageTick;
            if (Argument("-gcR8CConfig").Length == 0) return;
            EditorApplication.update += Tick;
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
        }

        public static void Run()
        {
            if (Argument("-gcR8CStage").Length != 0) StageTick();
            else Tick();
        }
        private static void Tick() => instance.Poll();
        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;
        private static JObject Config => JObject.Parse(File.ReadAllText(Argument("-gcR8CConfig")));
        private static void Save(string evidence, JObject progress) =>
            StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "live-progress.json"), progress.ToString());

        private static void StageTick() => instance.SubmitStage();

        private void SubmitStage()
        {
            if (busy || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EtosAgentGateway? gateway = EtosStudioSession.Gateway;
            if (gateway == null) return;
            busy = true;
            try
            {
                string path = Argument("-gcR8CStage");
                JObject config = JObject.Parse(File.ReadAllText(path));
                StudioRuntime runtime = StudioServices.Runtime;
                if (File.Exists(EtosSettings.PathFor(Project)))
                    throw new InvalidOperationException("Scratch walkthrough requires absent project ETOS settings");
                StageAdmission stage = StageAdmission.Of(runtime);
                if (Directory.Exists(stage.StateRoot) && Directory.GetFiles(stage.StateRoot, "pending-*.json").Length != 0)
                    throw new InvalidOperationException("Existing pending admission must be resolved by its owner");
                if (Directory.Exists(Path.Combine(Project, "Packages", Package(config))))
                    throw new InvalidOperationException("The candidate package is already installed");
                GraphicsWitness();
                EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
                if ((bool?)config["workerEditUndo"] == true)
                    Hollowmere.R10_A.SourceFreshness.Exercise(runtime, (string)config["evidence"]!);
                stage.PrepareStageWorldSnapshot();
                File.Copy(Path.Combine(Project, "Library", "GameCoreStudio", "StageWorldSnapshot.json"),
                    Path.Combine((string)config["evidence"]!, "source-world-snapshot.json"), false);
                string candidatePath = (string)config["candidate"]!;
                ChangeSet candidate = StageAdmission.Of(runtime).RetainCandidate(candidatePath);
                StageCandidateRequest request = stage.BuildStageRequest(candidate, Project);
                config["request"] = JObject.FromObject(request);
                StudioPaths.WriteAllTextAtomic(Path.Combine((string)config["evidence"]!, "tool-catalog.json"),
                    StudioJson.Serialize(runtime.Registry.Catalog));
                StudioPaths.WriteAllTextAtomic(path, config.ToString());
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
                throw new InvalidOperationException("R8-C requires a real GPU graphical Editor on DISPLAY=:1");
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
                ["packageInstalled"] = Directory.Exists(Path.Combine(Project, "Packages", Package(config))),
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
                    FrameGameView();
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
                    if (game.Rig == null) return;
                    if (progress["newGame"] == null)
                    {
                        if (!game.Rig.Ui.Dispatcher.Dispatch("newgame").Accepted)
                            throw new InvalidOperationException("New Game was refused");
                        progress["newGame"] = true;
                        Save(evidence, progress);
                        return;
                    }
                    if (game.Rig.Ui.Screen != GameCore.Rules.Gameplay.Ui.UiScreen.Hud) return;
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
                    string jobKey = "GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(runtime.Paths.ProjectRoot)) + "." + candidate.Id;
                    SessionState.SetString(jobKey, new JObject { ["jobId"] = config["jobId"], ["request"] = JObject.FromObject(expected) }.ToString());
                    StudioUiContext context = StudioUiSession.Context;
                    CandidateEntry entry = context.Candidates.Find(candidate.Id) ?? context.Candidates.Add(candidate.Id, candidate, expected.CatalogRevision);
                    StudioCandidatesWindow.Open(entry.Id);
                    Diagnostic? problem = await context.Candidates.RefreshStage(entry);
                    if (problem != null || !context.Candidates.CanAdmit(entry))
                        throw new InvalidOperationException("Creator review has no authenticated passing verdict: " + problem?.Message);
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "signed-verdict.json"), entry.VerifiedVerdict!.Document.ToString());
                    window = StudioCandidatesWindow.Open(entry.Id);
                    foreach (EditorWindow other in Resources.FindObjectsOfTypeAll<EditorWindow>())
                        if (other != window && other.GetType().Namespace == "GameCore.Studio.UI") other.Close();
                    window.position = new Rect(80, 80, 1100, 900);
                    window.Focus();
                    progress["initialVerifiedJobId"] = (string)config["jobId"]!;
                    progress["phase"] = "review";
                    progress["reviewStartedMs"] = Now;
                    Save(evidence, progress);
                    return;
                }
                if (phase == "review" && Now - (long)progress["reviewStartedMs"]! >= 1500)
                {
                    CandidateEntry entry = StudioUiSession.Context.Candidates.Find(expected.ChangeSetId)
                        ?? throw new InvalidOperationException("The creator candidate entry disappeared");
                    Button? button = window?.rootVisualElement.Q<Button>("candidate-admit");
                    Toggle? capture = window?.rootVisualElement.Q<Toggle>("candidate-admit-capture");
                    if (button == null || !button.enabledInHierarchy || button.panel == null
                        || capture == null || !capture.enabledInHierarchy || !capture.value
                        || !EditorApplication.isPlaying || boot?.GetComponent<HollowmereGame>().Director?.ItemCount("OldCoin") != 9
                        || !StudioUiSession.Context.Candidates.CanAdmit(entry))
                        throw new InvalidOperationException("The real creator Admit/capture controls are not ready in Play");
                    string? captureProblem = Hollowmere.P2_1.Evidence.UnityWindowCapture.CaptureStudio(
                        Path.Combine(evidence, "panel-admit-enabled.png"), false);
                    if (captureProblem != null) throw new InvalidOperationException(captureProblem);
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "panel-admit.json"), new JObject {
                        ["control"] = button.name, ["event"] = "NavigationSubmitEvent", ["enabled"] = true,
                        ["captureAndStop"] = capture.value, ["coins"] = 9, ["graphics"] = GraphicsWitness(),
                        ["jobId"] = entry.StageJobId, ["request"] = JObject.FromObject(expected),
                    }.ToString());
                    progress["phase"] = "admission";
                    progress["admissionStartedMs"] = Now;
                    progress["admissionDomain"] = domain;
                    Save(evidence, progress);
                    using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                    {
                        submit.target = button;
                        button.SendEvent(submit);
                    }
                    if (entry.Admission?.Outcome != AdmissionOutcome.Pending)
                        throw new InvalidOperationException("The creator Admit control did not start normal pending admission: " + entry.Admission?.Detail);
                    return;
                }
                if (phase == "admission" || phase == "undo")
                {
                    JObject? pending = admission.ReadPending(expected.ChangeSetId);
                    if (pending != null)
                    {
                        string serialized = pending.ToString(Newtonsoft.Json.Formatting.None);
                        StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "last-pending.json"), pending.ToString());
                        if (serialized != lastPending)
                        {
                            File.AppendAllText(Path.Combine(evidence, "pending-transitions.jsonl"), new JObject {
                                ["observedMs"] = Now, ["domain"] = domain, ["driverPhase"] = phase, ["pending"] = pending,
                            }.ToString(Newtonsoft.Json.Formatting.None) + "\n");
                            lastPending = serialized;
                        }
                    }
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
                    if (!IsLever(config))
                    {
                        BeginUndo(runtime, expected.ChangeSetId, evidence, progress);
                        return;
                    }
                    CaptureLever(evidence, "lever-off-before");
                    ActivateLever();
                    progress["phase"] = "lever-on";
                    progress["interactionStartedMs"] = Now;
                    Save(evidence, progress);
                }
                else if (phase == "lever-on" && ReadLever(boot!) == 1)
                {
                    CaptureLever(evidence, "lever-on");
                    progress["phase"] = "lever-on-visible";
                    progress["visibleMs"] = Now;
                    Save(evidence, progress);
                }
                else if (phase == "lever-on-visible" && Now - (long)progress["visibleMs"]! >= 2000)
                {
                    ActivateLever();
                    progress["phase"] = "lever-off";
                    Save(evidence, progress);
                }
                else if (phase == "lever-off" && ReadLever(boot!) == 0)
                {
                    CaptureLever(evidence, "lever-off-after");
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "interactive-lever.json"), new JObject {
                        ["states"] = new JArray(0, 1, 0), ["normalFrames"] = true,
                        ["control"] = "lever-toggle", ["coins"] = boot!.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin"),
                        ["graphics"] = GraphicsWitness(),
                    }.ToString());
                    BeginUndo(runtime, expected.ChangeSetId, evidence, progress);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { busy = false; }
        }

        private static void BeginUndo(StudioRuntime runtime, string changeSetId, string evidence, JObject progress)
        {
            progress["phase"] = "undo";
            progress["undoStartedMs"] = Now;
            Save(evidence, progress);
            var result = runtime.History.Undo(changeSetId);
            StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "undo-request.json"), new JObject {
                ["changeSetId"] = changeSetId, ["ok"] = result.Ok, ["requestedMs"] = progress["undoStartedMs"],
            }.ToString());
            if (!result.Ok) throw new InvalidOperationException("Normal History admission undo was refused");
        }

        private static void FrameGameView()
        {
            Type type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView", true)!;
            foreach (UnityEngine.Object existing in Resources.FindObjectsOfTypeAll(type))
                ((EditorWindow)existing).Close();
            EditorWindow view = (EditorWindow)ScriptableObject.CreateInstance(type);
            view.titleContent = new GUIContent("R8-C Play");
            view.minSize = new Vector2(1280, 720);
            view.ShowUtility();
            view.position = new Rect(40, 40, 1280, 760);
            PropertyInfo? size = type.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (size == null) throw new InvalidOperationException("Game View free-aspect selection is unavailable");
            size.SetValue(view, 0);
            view.Focus();
        }

        private static int ReadLever(GameBoot boot)
        {
            foreach (var extension in boot.World!.Extensions)
                if (extension.GetType().FullName == HollowmereExtensionRegistry.LeverExtensionType)
                    return (int)extension.GetType().GetProperty("State")!.GetValue(extension)!;
            throw new InvalidOperationException("The live lever extension is absent");
        }

        private static void ActivateLever()
        {
            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                Button? button = document.rootVisualElement.Q<Button>("lever-toggle");
                if (button == null || button.panel == null || !button.enabledInHierarchy) continue;
                Rect bounds = button.worldBound;
                if (bounds.width <= 0 || bounds.height <= 0 || bounds.xMin < 0 || bounds.yMin < 0
                    || bounds.xMax > Screen.width || bounds.yMax > Screen.height)
                    throw new InvalidOperationException("The actual lever control is outside the rendered Game View");
                using (NavigationSubmitEvent click = NavigationSubmitEvent.GetPooled())
                {
                    click.target = button;
                    button.SendEvent(click);
                }
                return;
            }
            throw new InvalidOperationException("The attached runtime lever control is unavailable");
        }

        private static void CaptureLever(string evidence, string name)
        {
            if (Screen.width < 640 || Screen.height < 360)
                throw new InvalidOperationException("The live Game View is too small for readable evidence");
            ScreenCapture.CaptureScreenshot(Path.Combine(evidence, name + "-play.png"));
            GameObject? lever = GameObject.Find("Admitted Lever (committed state)");
            if (lever == null) throw new InvalidOperationException("The actual lever presentation is absent");
            // A temporary evidence camera observes the actual playing scene; it never replaces or pumps the game root.
            var host = new GameObject("R8-C evidence camera");
            RenderTexture? target = null;
            Texture2D? image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                Camera camera = host.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = lever.transform.position + new Vector3(2.5f, 1.8f, -3f);
                camera.transform.LookAt(lever.transform.position + new Vector3(0f, 0.7f, 0f));
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.12f, 0.16f, 0.2f);
                target = new RenderTexture(960, 720, 24);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(960, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(evidence, name + "-world.png"), image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                UnityEngine.Object.DestroyImmediate(host);
            }
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
                        || (IsLever(config) && (smoke.MechanismStates.Count != 3 || smoke.MechanismStates[0] != 0
                            || smoke.MechanismStates[1] != 1 || smoke.MechanismStates[2] != 0))
                        || !smoke.CheckpointRoundTripsEqual || smoke.RestoredCheckpointSlotHash.Length != 64
                        || boot.GetComponent<HollowmereGame>().Director!.ItemCount("OldCoin") != 9
                        || string.IsNullOrEmpty(result.Predicted) || result.Live != result.Predicted
                        || boot.World?.Root.CatalogHash.ToHex() != result.Predicted
                        || result.Confinement != "docker" || result.Milliseconds > AdmissionBudgetMs || wallMs > AdmissionBudgetMs
                        || domain == (string)progress["admissionDomain"]!
                        || !refreshObserved)
                        throw new InvalidOperationException("automatic authenticated reload / restored Play / nine coins / signed catalog / tri-state smoke / 90s witness failed");
                    JObject witness = result.ToJson();
                    witness["smokeSteps"] = smoke.Steps;
                    witness["smokeTransitions"] = new JArray(smoke.Transitions);
                    witness["smokeReport"] = smoke.LastReport;
                    witness["mechanismStates"] = new JArray(smoke.MechanismStates);
                    witness["restoredCheckpointSlotHash"] = smoke.RestoredCheckpointSlotHash;
                    witness["initialSlotHash"] = smoke.InitialSlotHash;
                    witness["finalSlotHash"] = smoke.FinalSlotHash;
                    witness["checkpointRoundTripsEqual"] = smoke.CheckpointRoundTripsEqual;
                    witness["coins"] = 9;
                    witness["restoredRootCatalogHash"] = boot.World!.Root.CatalogHash.ToHex();
                    witness["admissionWallMs"] = wallMs;
                    witness["admissionBudgetMs"] = AdmissionBudgetMs;
                    witness["resumedDomain"] = domain;
                    witness["graphics"] = GraphicsWitness();
                    StudioPaths.WriteAllTextAtomic(Path.Combine(evidence, "live-admit.json"), witness.ToString());
                    FrameGameView();
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
                        || Directory.Exists(Path.Combine(Project, "Packages", Package(config))))
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
