#nullable enable
using System;
using System.IO;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    [InitializeOnLoad]
    internal static class AdmissionResumer
    {
        static AdmissionResumer()
        {
            Wake();
            EditorApplication.playModeStateChanged += _ => Wake();
        }

        internal static string StateRoot => Path.Combine(StudioPaths.ForCurrentProject().StateRoot, "Studio", "Admission");

        internal static void Wake()
        {
            if (AdmissionSession.instance.ResumeScheduled) return;
            AdmissionSession.instance.ResumeScheduled = true;
            StageAdmission.UnityDefer(3, () =>
            {
                AdmissionSession.instance.ResumeScheduled = false;
                Resume();
            });
        }

        private static bool HasPending(string root) => Directory.Exists(root)
            && (Directory.GetFiles(root, "pending-cs_*.json").Length > 0 || Directory.GetFiles(root, "admit-after-play-cs_*.json").Length > 0);

        private static async void Resume()
        {
            if (AdmissionSession.instance.Resuming) { Wake(); return; }
            AdmissionSession.instance.Resuming = true;
            bool waiting = false;
            try
            {
                string legacy = Path.Combine(StudioPaths.ForCurrentProject().LibraryRoot, "stage");
                if (!HasPending(StateRoot) && !HasPending(legacy)) return;
                StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
                StageCommandLine.Attach(admission);
                admission.ResumeSmokePolling();
                try { await admission.RefreshPendingVerdicts(); }
                catch (Exception)
                {
                    // Additions wait for authenticated client rebinding; owned removals can still recover.
                }
                foreach (AdmissionResult result in admission.ResumePending())
                {
                    waiting |= result.Outcome == AdmissionOutcome.Pending;
                    UnityEngine.Debug.Log("[GameCore Studio] stage: resumed " + result.ChangeSetId + " -> " + result.Outcome + ": " + result.Detail);
                }
            }
            catch (Exception)
            {
                UnityEngine.Debug.LogWarning("[GameCore Studio] Admission recovery could not read its durable state; the record is retained for review.");
            }
            finally
            {
                AdmissionSession.instance.Resuming = false;
                if (waiting) Wake();
            }
        }
    }
}
