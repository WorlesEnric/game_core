#nullable enable
using GameCore.Execution;
using GameCore.Unity.Runtime;
using UnityEngine;

namespace GameCore.Unity.Adapters
{
    /// <summary>
    /// Per-session reset path. Domain reload disabled does not reset static fields or event subscriptions, so a new
    /// Play Mode session must invalidate stale loop nodes, dispose surviving hosts and clear the application's
    /// static state. Immutable generated catalogs are never mutated here (04 s9).
    /// </summary>
    public static class GameCoreApplicationReset
    {
        /// <summary>Fully qualified method name, recorded so a log can name the entry point it ran.</summary>
        public const string ResetMethodName = "GameCore.Unity.Adapters.GameCoreApplicationReset.ResetForNewSession";

        /// <summary>Reset runs since process start.</summary>
        public static int RunCount { get; private set; }

        public static int LastRemovedNodeCount { get; private set; }

        public static int LastDisposedWorldCount { get; private set; }

        public static int LastResetGeneration { get; private set; }

        public static bool EditorHookInstalled { get; private set; }

#if UNITY_EDITOR
        private static bool editorHookInstalled;
#endif

        /// <summary>
        /// Runs before the first scene load of every session, i.e. before the Entities bootstrap performs world
        /// initialization in the same session.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => RunReset();

        /// <summary>
        /// The reset itself. It is public so the Play Mode smoke test can exercise exactly the code path the
        /// attribute invokes instead of asserting that an attribute exists (TEST-018).
        /// </summary>
        public static void RunReset()
        {
            RunCount++;

            // Invalidate any delegate a previous session installed, then remove its nodes structurally.
            GameCorePlayerLoopInstaller.BumpGeneration();
            LastResetGeneration = GameCorePlayerLoopInstaller.Generation;
            LastRemovedNodeCount = GameCorePlayerLoopInstaller.Remove();

            // Dispose surviving hosts: their Unity worlds may already have been destroyed by the Entities
            // shutdown path, so disposal is written to tolerate an already-uncreated world.
            LastDisposedWorldCount = UnityWorldRegistry.ResetAll();

            GameCoreApplicationPump.Reset();
            // Adapter frames belong to one world incarnation, so a surviving registration from the previous session
            // would present or ingest into a world that no longer exists (04 s9, P-004).
            AdapterFrameRegistry.Reset();
            GameCoreThreading.CaptureMainThread();

            InstallEditorHook();
        }

        /// <summary>Leaving Play Mode closes the loop route and disposes worlds before the next session starts.</summary>
        public static void ExitPlayModeCleanup()
        {
            GameCorePlayerLoopInstaller.Remove();
            UnityWorldRegistry.ResetAll();
            GameCoreApplicationPump.Reset();
            AdapterFrameRegistry.Reset();
        }

        private static void InstallEditorHook()
        {
#if UNITY_EDITOR
            if (editorHookInstalled)
            {
                return;
            }

            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            editorHookInstalled = true;
            EditorHookInstalled = true;
#else
            EditorHookInstalled = false;
#endif
        }

#if UNITY_EDITOR
        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            {
                ExitPlayModeCleanup();
            }
        }
#endif
    }
}
