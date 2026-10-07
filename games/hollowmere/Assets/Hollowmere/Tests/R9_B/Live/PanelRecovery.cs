#nullable enable
using System;
using System.IO;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hollowmere.R9_B
{
    public sealed class PanelRecovery : ScriptableSingleton<PanelRecovery>
    {
        [NonSerialized] private CandidateEntry? entry;
        [NonSerialized] private StudioCandidatesWindow? window;
        [NonSerialized] private JObject? config;
        [NonSerialized] private bool activated;
        [NonSerialized] private int settledFrames;
        [NonSerialized] private DateTime started;
        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == flag) return args[i + 1];
            throw new ArgumentException(flag);
        }
        public static void Run()
        {
            instance.started = DateTime.UtcNow;
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
            if (Argument("-gcR9BMode") == "prepare")
            {
                StudioRuntime runtime = StudioServices.Runtime;
                StudioPaths.WriteAllTextAtomic(Argument("-gcR9BConfig"), new JObject {
                    ["changeSetId"] = IdDerivation.NewChangeSetId(),
                    ["projectId"] = EtosProjectContext.LoadProjectId(runtime.Paths.ProjectRoot),
                    ["sourceProject"] = runtime.Paths.ProjectRoot,
                    ["sourceRevision"] = EtosProjectContext.SourceRevision(runtime.Paths.ProjectRoot),
                    ["catalogRevision"] = runtime.Registry.Catalog.Revision ?? runtime.Registry.Catalog.ComputeRevision(),
                }.ToString());
                EditorApplication.Exit(0);
                return;
            }
            instance.config = JObject.Parse(File.ReadAllText(Argument("-gcR9BConfig")));
            EtosStudioSession.Start();
            EditorApplication.update += instance.Tick;
        }
        private void Save(string name, JObject value) => StudioPaths.WriteAllTextAtomic(Path.Combine((string)config!["evidence"]!, name + ".json"), value.ToString());
        private void Tick()
        {
            try
            {
                if ((DateTime.UtcNow - started).TotalMinutes > 5) throw new InvalidOperationException("panel deadline");
                StudioUiContext context = StudioUiSession.Context;
                if (!context.Gateway.Status.AgentReady) return;
                CandidateCoordinator candidates = context.Candidates;
                string mode = Argument("-gcR9BMode");
                if (!activated)
                {
                    StageAdmission admission = StageAdmission.Of(context.Runtime);
                    ChangeSet candidate = admission.RetainCandidate((string)config!["candidate"]!);
                    entry = candidates.Add(candidate.Id, candidate, context.Runtime.Registry.Catalog.Revision);
                    if (mode == "review")
                    {
                        JObject binding = JObject.Parse(File.ReadAllText(Path.Combine((string)config["evidence"]!, "panel-binding.json")));
                        Save("binding-before-recovery", new JObject {
                            ["entryJob"] = entry.StageJobId, ["entryRequest"] = entry.StageRequest == null ? null : JObject.FromObject(entry.StageRequest),
                            ["saved"] = binding, ["current"] = JObject.FromObject(admission.BuildStageRequest(entry.ChangeSet, context.Runtime.Paths.ProjectRoot))
                        });
                        string key = "GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(context.Runtime.Paths.ProjectRoot)) + "." + entry.Id;
                        SessionState.SetString(key, binding.ToString());
                        // The panel already owns this entry before the persisted stage binding arrives.
                        if (!ReferenceEquals(candidates.Add(candidate.Id, candidate), entry)) throw new InvalidOperationException("duplicate recovery entry");
                    }
                    window = StudioCandidatesWindow.Open(entry.Id);
                    activated = true;
                    return;
                }
                if (entry == null || window == null) throw new InvalidOperationException("entry missing");
                string buttonName = mode == "submit" ? "candidate-stage" : "candidate-refresh-verdict";
                if (!entry.Staging && entry.VerifiedVerdict == null && entry.Problems.Count == 0)
                {
                    Button button = window.rootVisualElement.Q<Button>(buttonName);
                    if (button == null || !button.enabledInHierarchy) throw new InvalidOperationException("panel control disabled: " + buttonName);
                    using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled()) { submit.target = button; button.SendEvent(submit); }
                    return;
                }
                if (mode == "submit" && entry.StageJobId != null)
                {
                    Save("panel-binding", new JObject { ["jobId"] = entry.StageJobId, ["request"] = JObject.FromObject(entry.StageRequest!) });
                    EditorApplication.Exit(0);
                    return;
                }
                if (entry.Staging) return;
                bool enabled = candidates.CanAdmit(entry) && window.rootVisualElement.Q<Button>("candidate-admit").enabledInHierarchy;
                if (enabled && ++settledFrames < 12) return;
                if (enabled)
                {
                    string? captureProblem = Hollowmere.P2_1.Evidence.UnityWindowCapture.CaptureStudio(Path.Combine((string)config!["evidence"]!, "panel-admit-enabled.png"), true);
                    if (captureProblem != null) throw new InvalidOperationException(captureProblem);
                }
                Save("panel-result", new JObject {
                    ["canAdmit"] = candidates.CanAdmit(entry), ["admitButtonEnabled"] = enabled,
                    ["jobId"] = entry.StageJobId, ["request"] = entry.StageRequest == null ? null : JObject.FromObject(entry.StageRequest),
                    ["signedVerdict"] = entry.VerifiedVerdict?.Document,
                    ["problems"] = JArray.FromObject(entry.Problems), ["batchMode"] = Application.isBatchMode,
                });
                EditorApplication.Exit(enabled ? 0 : 1);
            }
            catch (Exception error)
            {
                string safe = new SecretRedactor().Redact(error.ToString());
                if (config != null) Save("panel-error", new JObject { ["error"] = safe });
                Debug.LogError(safe);
                EditorApplication.Exit(1);
            }
        }
    }
}
