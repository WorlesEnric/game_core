// Hollowmere - registers the running game's SaveService as the Studio admission checkpoint (P2.4 open item 2, P3.1).
//
// When Play Mode starts, StageAdmission.Of(StudioServices.Runtime).Options.Capture becomes a SaveServiceAdmissionCapture
// over HollowmereGame.Saves, so admitting a staged mechanism from Play Mode captures "admit-<id>" before Play Mode stops
// instead of refusing with capture_failed. A capture hook someone else registered is left alone.
#nullable enable
using GameCore.Studio.Edit;
using GameCore.Unity.App;
using Hollowmere.Game;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.Authoring
{
    /// <summary>Installs the Hollowmere admission capture hook on entering Play Mode.</summary>
    [InitializeOnLoad]
    public static class HollowmereAdmissionCapture
    {
        static HollowmereAdmissionCapture()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>The running game's save service (null outside Play Mode or before the game booted).</summary>
        public static SaveService? RunningSaves()
        {
            HollowmereGame? game = Object.FindAnyObjectByType<HollowmereGame>();
            return game != null ? game.Saves : null;
        }

        /// <summary>Registers the hook on the Studio runtime's admission service (idempotent); true when ours is installed.</summary>
        public static bool Register()
        {
            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            if (admission.Options.Capture == null || admission.Options.Capture is Hook)
            {
                admission.Options.Capture = new Hook();
                return true;
            }

            return false;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode)
            {
                Register();
            }
        }

        /// <summary>The SaveService capture over whatever HollowmereGame is running (a type of its own so it is recognisable).</summary>
        private sealed class Hook : IAdmissionCapture
        {
            private readonly SaveServiceAdmissionCapture inner = new SaveServiceAdmissionCapture(RunningSaves);

            public bool TryCapture(string slot, out string? problem) => inner.TryCapture(slot, out problem);

            public bool TryRestore(string slot, out string? problem) => inner.TryRestore(slot, out problem);
        }
    }
}
