#nullable enable
// GameCore.Studio.Edit - continues admissions across the domain reload their compile causes (P2.4).
// An admission writes Library/GameCoreStudio/stage/pending-<id>.json before it recompiles; after the reload this
// [InitializeOnLoad] hook finds the record and runs StageAdmission.ResumePending (checkers, re-bake, catalog check,
// Applied or rollback; or the catalog check of an undo). Nothing happens when no record exists, so ordinary reloads
// (and test runs) never create a Studio runtime here.
using System.IO;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    [InitializeOnLoad]
    internal static class AdmissionResumer
    {
        static AdmissionResumer()
        {
            // A few idle seconds after the reload: the Editor imports the new package's assets right after it.
            StageAdmission.UnityDefer(3, Resume);
        }

        internal static string StateRoot => Path.Combine(StudioPaths.ForCurrentProject().LibraryRoot, "stage");

        private static void Resume()
        {
            string state = StateRoot;
            if (!Directory.Exists(state))
            {
                return;
            }

            bool pending = Directory.GetFiles(state, "pending-cs_*.json").Length > 0;
            bool commandLine = File.Exists(Path.Combine(state, StageCommandLine.RequestFile));
            if (!pending && !commandLine)
            {
                return;
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Resume;
                return;
            }

            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            StageCommandLine.Attach(admission);
            if (pending)
            {
                foreach (AdmissionResult result in admission.ResumePending())
                {
                    UnityEngine.Debug.Log("[GameCore Studio] stage: resumed " + result.ChangeSetId + " -> " + result.Outcome + ": " + result.Detail);
                }
            }
        }
    }
}
