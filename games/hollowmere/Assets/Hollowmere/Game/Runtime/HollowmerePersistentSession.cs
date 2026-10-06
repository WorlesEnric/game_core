// Hollowmere - the player session that outlives scene reloads (P3.1).
//
// New Game and Restart reload Boot.unity (SceneReloadSessionActions), which destroys GameBoot and HollowmereGame. A
// frame log must keep one file across the whole run and an autoplay script must keep its place, so both live on one
// DontDestroyOnLoad object, created only when the command line asks for -frameLog, -autoplay or -quitAfterFrames. Each
// HollowmereGame attaches itself on Start; the session is the autoplay runner's host and forwards every call to the
// attached game (not Ready while none is attached or the world is not running). It also marks region transitions in
// the frame log ("region:<from>-><to>") and records the region of every row.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Keeps the frame log and the autoplay runner alive across scene reloads; the runner's host.</summary>
    [DisallowMultipleComponent]
    public sealed class HollowmerePersistentSession : MonoBehaviour, IAutoplayHost
    {
        private HollowmereGame? game;
        private string region = string.Empty;
        private int frames;
        private int quitAfterFrames;
        private bool quitting;

        public FrameLogRecorder? FrameLog { get; private set; }

        public AutoplayRunner? Autoplay { get; private set; }

        public HollowmereGame? Game => game;

        /// <summary>How many HollowmereGame instances attached (1 + scene reloads).</summary>
        public int Attaches { get; private set; }

        /// <summary>The session of this run, created when the command line needs one; null otherwise.</summary>
        public static HollowmerePersistentSession? Ensure(HollowmereCommandLine commandLine)
        {
            HollowmerePersistentSession? existing = FindAnyObjectByType<HollowmerePersistentSession>();
            if (existing != null)
            {
                return existing;
            }

            if (commandLine.FrameLogPath == null && !commandLine.Autoplay && commandLine.QuitAfterFrames <= 0)
            {
                return null;
            }

            var host = new GameObject("Hollowmere Session");
            DontDestroyOnLoad(host);
            HollowmerePersistentSession session = host.AddComponent<HollowmerePersistentSession>();
            session.Begin(commandLine);
            return session;
        }

        private void Begin(HollowmereCommandLine commandLine)
        {
            FrameProfile.Configure(gameObject);
            for (int i = 0; i < commandLine.Problems.Count; i++)
            {
                Debug.LogWarning("[Hollowmere] command line: " + commandLine.Problems[i]);
            }

            if (commandLine.FrameLogPath != null)
            {
                FrameLog = gameObject.AddComponent<FrameLogRecorder>();
                FrameLog.Begin(commandLine.FrameLogPath, HollowmereGame.Revision());
                Debug.Log("[Hollowmere] frame log -> " + commandLine.FrameLogPath);
            }

            if (commandLine.AutoplayPath != null)
            {
                string text;
                try
                {
                    text = File.ReadAllText(commandLine.AutoplayPath);
                }
                catch (Exception problem) when (problem is IOException || problem is UnauthorizedAccessException)
                {
                    Debug.LogError("[autoplay] FAILED: cannot read " + commandLine.AutoplayPath + ": " + problem.Message);
                    Quit(3);
                    return;
                }

                if (!AutoplayScript.TryParse(text, out AutoplayScript? script, out string error) || script == null)
                {
                    Debug.LogError("[autoplay] FAILED: " + error);
                    Quit(3);
                    return;
                }

                Autoplay = gameObject.AddComponent<AutoplayRunner>();
                Autoplay.Run(script, this, commandLine.QuitAfterFrames);
            }
            else
            {
                quitAfterFrames = commandLine.QuitAfterFrames;
            }
        }

        /// <summary>Called by every HollowmereGame on Start (also after New Game / Restart reloads).</summary>
        public void Attach(HollowmereGame attached)
        {
            game = attached;
            Attaches++;
            Mark(Attaches == 1 ? "ready" : "reload");
        }

        private void LateUpdate()
        {
            HollowmereGame? current = game;
            if (current != null && current.World != null)
            {
                ManifestRegion? now = current.CurrentRegion();
                string name = now != null ? now.name : string.Empty;
                if (!string.Equals(name, region, StringComparison.Ordinal))
                {
                    if (region.Length > 0 && name.Length > 0)
                    {
                        Mark("region:" + region + "->" + name);
                    }

                    region = name;
                    FrameLog?.SetRegion(name);
                }
            }

            frames++;
            if (quitAfterFrames > 0 && frames >= quitAfterFrames && !quitting)
            {
                Log("[Hollowmere] quitAfterFrames " + quitAfterFrames.ToString(CultureInfo.InvariantCulture) + " reached");
                Quit(0);
            }
        }

        private void Quit(int code)
        {
            quitting = true;
            FrameLog?.Flush();
#if !UNITY_EDITOR
            Application.Quit(code);
#endif
        }

        // ------------------------------------------------------------------ IAutoplayHost

        public bool Ready
        {
            get
            {
                HollowmereGame? current = game;
                return current != null && current.World != null && current.World.Root.State == GameApplicationState.Running && current.Rig != null;
            }
        }

        public Vector3 PlayerPosition => game != null ? game.PlayerPosition() : Vector3.zero;

        public string ProbeAhead(Vector3 direction) => game != null ? game.ProbeAhead(direction) : "no game";

        public bool TryEntityPosition(string name, out Vector3 position)
        {
            position = Vector3.zero;
            return game != null && game.TryEntityPosition(name, out position);
        }

        public void SetMove(Vector3 worldDirection, bool run)
        {
            AutoplayIntentSource? intents = game != null ? game.Intents : null;
            if (intents == null)
            {
                return;
            }

            if (worldDirection.sqrMagnitude < 1e-6f)
            {
                intents.SetMove(Vector3.zero, false);
                return;
            }

            intents.SetMove(worldDirection, run);
        }

        public void SetFacing(float yawDegrees) => game?.Intents?.SetFacing(yawDegrees);

        public void PressInteract() => game?.Intents?.PressInteract();

        public bool Dispatch(string uiCommand, out string detail)
        {
            HollowmereGame? current = game;
            if (current == null || current.Rig == null)
            {
                detail = "no game";
                return false;
            }

            var result = current.Rig.Dispatch(uiCommand);
            detail = result.ToString();
            return result.Accepted;
        }

        public bool Advance()
        {
            HollowmereGame? current = game;
            return current != null && current.Boot != null && current.Boot.Modules != null && current.Boot.Modules.Dialogue.Runner != null
                && current.Boot.Modules.Dialogue.Runner.Advance();
        }

        public bool Choose(int option)
        {
            HollowmereGame? current = game;
            return current != null && current.Boot != null && current.Boot.Modules != null && current.Boot.Modules.Dialogue.Runner != null
                && current.Boot.Modules.Dialogue.Runner.Choose(option);
        }

        public bool Check(string condition, out string detail)
        {
            HollowmereGame? current = game;
            if (current == null)
            {
                detail = "no game";
                return false;
            }

            return HollowmereConditions.Check(current, condition, out detail);
        }

        public void Mark(string label)
        {
            FrameLog?.Mark(label);
        }

        public void Log(string line)
        {
            Debug.Log(line);
        }

        private void OnApplicationQuit()
        {
            FrameLog?.Flush();
        }
    }
}
