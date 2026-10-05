#nullable enable
// GameCore.Studio.Edit - batchmode entry points of the staging lane (P2.4, W-MECH-01 on the build host).
//
//   Unity -batchmode -nographics -projectPath games/hollowmere -logFile admit.log \
//     -executeMethod GameCore.Studio.Edit.StageCommandLine.Admit \
//     -gcCandidate <candidate dir> -gcVerdict <verdict.json> -gcResult <result.json> [-gcShared]
//   Unity ... -executeMethod GameCore.Studio.Edit.StageCommandLine.Undo -gcChangeSet <cs_id> -gcResult <result.json>
//
// No -quit: the admission recompiles, the domain reloads, AdmissionResumer finishes the admission in the new domain and
// this class writes the result JSON and exits (0 admitted / undone, 1 otherwise). Run under studio/tools/unity-batch.sh
// (the host-wide Unity lock and watchdog).
using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    public static class StageCommandLine
    {
        public const string RequestFile = "commandline.json";

        /// <summary>Admits a candidate directory with a verdict file.</summary>
        public static void Admit()
        {
            string? candidate = Argument("-gcCandidate");
            string? verdict = Argument("-gcVerdict");
            string? resultPath = Argument("-gcResult");
            if (candidate == null || verdict == null || resultPath == null)
            {
                Fail(resultPath, "usage: -gcCandidate <dir> -gcVerdict <verdict.json> -gcResult <result.json> [-gcShared]");
                return;
            }

            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            admission.Options.SharedPolicy = Flag("-gcShared");
            WriteRequest(admission, "admit", resultPath);
            Attach(admission);
            AdmissionResult result;
            try
            {
                result = admission.Admit(admission.RetainCandidate(candidate), File.ReadAllBytes(verdict));
            }
            catch (Exception error) when (error is IOException || error is ArtifactStoreException || error is ArgumentException || error is JsonException)
            {
                Fail(resultPath, "the candidate or verdict could not be read: " + error.Message);
                return;
            }

            UnityEngine.Debug.Log("[GameCore Studio] stage: admit " + result.ChangeSetId + " -> " + result.Outcome + ": " + result.Detail);
        }

        /// <summary>Undoes an admitted change set.</summary>
        public static void Undo()
        {
            string? id = Argument("-gcChangeSet");
            string? resultPath = Argument("-gcResult");
            if (id == null || resultPath == null)
            {
                Fail(resultPath, "usage: -gcChangeSet <cs_id> -gcResult <result.json>");
                return;
            }

            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            WriteRequest(admission, "undo", resultPath);
            Attach(admission);
            AdmissionResult result = admission.Undo(id);
            UnityEngine.Debug.Log("[GameCore Studio] stage: undo " + id + " -> " + result.Outcome + ": " + result.Detail);
        }

        /// <summary>Exits the editor with the result when a command-line admission or undo finishes.</summary>
        internal static void Attach(StageAdmission admission)
        {
            string path = Path.Combine(admission.StateRoot, RequestFile);
            if (!File.Exists(path))
            {
                return;
            }

            admission.Finished -= OnFinished;
            admission.Finished += OnFinished;
        }

        private static void OnFinished(AdmissionResult result)
        {
            StageAdmission admission = StageAdmission.Of(StudioServices.Runtime);
            string path = Path.Combine(admission.StateRoot, RequestFile);
            if (!File.Exists(path))
            {
                return;
            }

            JObject request = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
            string? resultPath = (string?)request["result"];
            string operation = (string?)request["operation"] ?? "admit";
            File.Delete(path);
            bool ok = operation == "undo" ? result.Outcome == AdmissionOutcome.Undone : result.Outcome == AdmissionOutcome.Admitted;
            JObject json = result.ToJson();
            json["operation"] = operation;
            json["ok"] = ok;
            if (resultPath != null)
            {
                StudioPaths.WriteAllTextAtomic(resultPath, json.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n");
            }

            UnityEngine.Debug.Log("[GameCore Studio] stage: " + operation + " finished: " + json.ToString(Formatting.None));
            EditorApplication.Exit(ok ? 0 : 1);
        }

        private static void WriteRequest(StageAdmission admission, string operation, string resultPath)
        {
            JObject request = new JObject { ["operation"] = operation, ["result"] = Path.GetFullPath(resultPath) };
            StudioPaths.WriteAllTextAtomic(Path.Combine(admission.StateRoot, RequestFile), request.ToString(Formatting.Indented) + "\n");
        }

        private static void Fail(string? resultPath, string message)
        {
            UnityEngine.Debug.LogError("[GameCore Studio] stage: " + message);
            if (resultPath != null)
            {
                JObject json = new JObject { ["ok"] = false, ["outcome"] = "Refused", ["detail"] = message };
                StudioPaths.WriteAllTextAtomic(Path.GetFullPath(resultPath), json.ToString(Formatting.Indented) + "\n");
            }

            EditorApplication.Exit(2);
        }

        private static string? Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
    }
}
