#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hollowmere.R7_A
{
    /// <summary>Real Editor open/play/walk/pick acceptance; never submits an agent request.</summary>
    [InitializeOnLoad]
    public static class OpenPlay
    {
        private const string Key = "Hollowmere.R7_A.OpenPlay.";
        static OpenPlay()
        {
            if (SessionState.GetBool(Key + "active", false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            Directory.CreateDirectory(Output);
            Environment.SetEnvironmentVariable("GCS_EVIDENCE_DIR", Output);
            Directory.CreateDirectory(StudioUiEvidence.OutputDir);
            SessionState.SetBool(Key + "active", true);
            SessionState.SetInt(Key + "step", 0);
            SessionState.SetFloat(Key + "next", 0);
            SessionState.SetFloat(Key + "deadline", (float)EditorApplication.timeSinceStartup + 300);
            SessionState.SetBool(Key + "firstRun", StudioUiSettings.FirstRunDone);
            StudioUiSettings.FirstRunDone = true;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static string Output => Path.GetFullPath(Path.Combine(Application.dataPath, "../../../artifacts/studio/verification/W-UI-01/r7-a/open-play"));

        private static object? Evidence(string method, params object?[] args) =>
            typeof(StudioUiEvidence).GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "next", 0)) return;
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "deadline", 0)) throw new TimeoutException("Open/play acceptance did not complete.");
                int step = SessionState.GetInt(Key + "step", 0);
                if (!Advance(step)) return;
                SessionState.SetInt(Key + "step", step + 1);
                SessionState.SetFloat(Key + "next", (float)EditorApplication.timeSinceStartup + 3);
            }
            catch (Exception error)
            {
                Write("failure", new JObject { ["error"] = StudioStyles.Safe((error.InnerException ?? error).Message) });
                Finish(1);
            }
        }

        private static bool Advance(int step)
        {
            if (step == 0) { Evidence("RunStep", 0); return true; }
            StudioViewportWindow viewport = StudioViewportWindow.Open();
            viewport.EnsureGui();
            if (step == 1)
            {
                // Ordinary Open Studio must discover the already-installed pairing by itself.
                var status = viewport.Context.Gateway.Status;
                if (!status.NodeReachable || !status.AgentReady) return false;
                Write("automatic-gateway", new JObject { ["gateway"] = viewport.Context.Gateway.GetType().Name, ["nodeReachable"] = status.NodeReachable, ["agentReady"] = status.AgentReady });
                Capture("01-open");
                return true;
            }
            if (step == 2) return (bool)Evidence("RunStep", 14)!;
            if (step == 3) return (bool)Evidence("RunStep", 15)!;
            if (step == 4)
            {
                Evidence("RunStep", 16);
                Capture("02-walk");
                return true;
            }
            if (step == 5)
            {
                viewport.SetMode(ViewportMode.Select);
                GameObject target = (GameObject?)Evidence("FindByName", "Maren") ?? throw new InvalidOperationException("Maren view is missing.");
                Evidence("FrameOn", viewport, target, null);
                Rect rect = viewport.ScreenRectOf(target) ?? throw new InvalidOperationException("Maren is offscreen.");
                string chosen = (string)Evidence("SelectTarget", viewport, target, rect.center, SelectionOp.Replace)!;
                viewport.Overlap?.Hide();
                var selected = viewport.Context.Selection.Targets;
                if (selected.Count != 1) throw new InvalidOperationException("NPC click did not select one authored target: " + chosen);
                Write("npc-click", new JObject { ["selection"] = selected[0].ToString(), ["choice"] = chosen });
                StudioContextWindow.Open();
                return true;
            }
            if (step == 6)
            {
                StudioContextWindow context = EditorWindow.GetWindow<StudioContextWindow>();
                string text = string.Join("\n", context.rootVisualElement.Query<Label>().ToList().Select(label => label.text));
                if (!text.Contains("Maren") || text.IndexOf("definition", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("NPC card lacks Maren/definition: " + text);
                Write("npc-card", new JObject { ["text"] = StudioStyles.Safe(text) });
                Capture("03-npc-definition");
                EditorApplication.ExitPlaymode();
                return true;
            }
            if (step == 7)
            {
                if (EditorApplication.isPlaying) return false;
                Write("result", new JObject { ["passed"] = true, ["paidRequests"] = 0 });
                Finish(0);
            }
            return true;
        }

        private static void Capture(string name)
        {
            string? problem = UnityWindowCapture.CaptureStudio(Path.Combine(Output, name + ".png"), false);
            if (problem != null) throw new InvalidOperationException(problem);
        }

        private static void Write(string name, JObject result)
        {
            result["utc"] = DateTime.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(Output, name + ".json"), result.ToString());
            Debug.Log("[R7-A] " + name + ": " + result.ToString(Newtonsoft.Json.Formatting.None));
        }

        private static void Finish(int result)
        {
            SessionState.SetBool(Key + "active", false);
            StudioUiSettings.FirstRunDone = SessionState.GetBool(Key + "firstRun", false);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(result);
        }
    }
}
