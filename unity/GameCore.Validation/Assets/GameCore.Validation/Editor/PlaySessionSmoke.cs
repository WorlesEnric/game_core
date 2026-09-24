#nullable enable
using System;
using System.IO;
using GameCore.Contracts;
using GameCore.Unity.Adapters;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using UnityEditor;
using UnityEngine;

namespace GameCore.Validation.Editor
{
    /// <summary>TEST-018: ten actual enter/exit cycles with each domain reload setting.</summary>
    [InitializeOnLoad]
    public static class PlaySessionSmoke
    {
        private const string Prefix = "GameCore.PlaySessionSmoke.";
        private static UnityWorldHost? leavingHost;
        private static string ResultPath => Path.GetFullPath("../../artifacts/gc-005/play-session-smoke.jsonl");

        static PlaySessionSmoke()
        {
            EditorApplication.playModeStateChanged += OnStateChanged;
            EditorApplication.update += OnUpdate;
        }

        public static void Run()
        {
            SessionState.SetBool(Prefix + "active", true);
            SessionState.SetInt(Prefix + "cycle", 0);
            SessionState.SetBool(Prefix + "originalEnabled", EditorSettings.enterPlayModeOptionsEnabled);
            SessionState.SetInt(Prefix + "originalOptions", (int)EditorSettings.enterPlayModeOptions);
            File.WriteAllText(ResultPath, string.Empty);
            BeginCycle();
        }

        private static void BeginCycle()
        {
            int cycle = SessionState.GetInt(Prefix + "cycle", 0);
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = cycle < 10 ? EnterPlayModeOptions.None : EnterPlayModeOptions.DisableDomainReload;
            SessionState.SetInt(Prefix + "frames", 0);
            EditorApplication.isPlaying = true;
        }

        private static void OnStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Prefix + "active", false)) return;
            try
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    Require(UnityWorldRegistry.Count == 1, "exactly one bootstrap world");
                    Require(GameCorePlayerLoopInstaller.CountInstalledNodes() == 1, "exactly one loop route");
                    Require(GameCoreApplicationBootstrap.FallbackCount == 0, "no bootstrap fallback");
                    UnityWorldHost host = UnityWorldRegistry.Hosts[0];
                    Require(host.Lifecycle == WorldLifecycleState.Running, "running bootstrap world");
                    Require(host.World.Session.ToString() != SessionState.GetString(Prefix + "previous", ""), "fresh incarnation");
                    SessionState.SetString(Prefix + "previous", host.World.Session.ToString());
                    host.NotifyCommandAdmitted(1U);
                    leavingHost = host;
                }
                else if (state == PlayModeStateChange.EnteredEditMode)
                {
                    Require(UnityWorldRegistry.Count == 0, "no surviving registry entries");
                    Require(GameCorePlayerLoopInstaller.CountInstalledNodes() == 0, "no surviving loop route");
                    if (leavingHost != null)
                    {
                        Require(leavingHost.Lifecycle == WorldLifecycleState.Disposed, "host disposed");
                        Require(!leavingHost.IsEntityWorldCreated, "ECS storage disposed");
                        Require(leavingHost.Ledger.OutstandingJobCount == 0, "jobs settled before disposal");
                    }
                    int cycle = SessionState.GetInt(Prefix + "cycle", 0);
                    File.AppendAllText(ResultPath, "{\"cycle\":" + (cycle + 1) + ",\"domainReload\":" + (cycle < 10 ? "true" : "false") + ",\"status\":\"Pass\",\"world\":\"" + SessionState.GetString(Prefix + "previous", "") + "\"}\n");
                    SessionState.SetInt(Prefix + "cycle", ++cycle);
                    leavingHost = null;
                    if (cycle == 20) Finish(0);
                    else EditorApplication.delayCall += BeginCycle;
                }
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(Prefix + "active", false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            int frames = SessionState.GetInt(Prefix + "frames", 0) + 1;
            SessionState.SetInt(Prefix + "frames", frames);
            if (frames != 20) return;
            try
            {
                Require(UnityWorldRegistry.Count == 1, "one world after routed frames");
                UnityWorldHost host = UnityWorldRegistry.Hosts[0];
                Require(host.CurrentStep.Value == 1UL, "one command, exactly one step");
                Require(FixtureWorldState.TryReadTrail(host.EntityWorld.EntityManager, out FixtureTrail trail), "seeded fixture");
                Require(trail.AcceptCount == 1 && trail.ProjectCount == 1, "no system double update");
                Require(host.PumpCount > 1 && trail.IngressCount == host.PumpCount && trail.OutputCount == host.PumpCount, "idle routing continues");
                EditorApplication.isPlaying = false;
            }
            catch (Exception exception) { Fail(exception); }
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }

        private static void Fail(Exception exception)
        {
            Debug.LogException(exception);
            File.AppendAllText(ResultPath, "{\"status\":\"Fail\",\"cycle\":" + (SessionState.GetInt(Prefix + "cycle", 0) + 1) + "}\n");
            Finish(1);
        }

        private static void Finish(int code)
        {
            SessionState.SetBool(Prefix + "active", false);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(Prefix + "originalEnabled", false);
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(Prefix + "originalOptions", 0);
            EditorApplication.Exit(code);
        }
    }
}
