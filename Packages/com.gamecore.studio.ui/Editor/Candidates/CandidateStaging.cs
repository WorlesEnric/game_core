// GameCore.Studio.UI - the staging lane as the candidate panel shows it (P2.4, docs/studio/03 s8): a change set that
// proposes or admits a mechanism needs a passing StageVerdict before it can reach the editor (validator
// RequiresStageVerdict). The panel reads the verdict state from the journal entry's `validation` field (scenarios
// stage.verdict, stage.admission, stage.undo with status pending|pass|fail), and Admit calls StageAdmission (enabled
// only on a passing verdict; the validator refuses anything else regardless).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.UI
{
    /// <summary>The staging state of one change set.</summary>
    public sealed class StageState
    {
        public StageState(ValidationScenario? verdict, ValidationScenario? admission, ValidationScenario? undo)
        {
            Verdict = verdict;
            Admission = admission;
            Undo = undo;
        }

        public ValidationScenario? Verdict { get; }

        public ValidationScenario? Admission { get; }

        public ValidationScenario? Undo { get; }

        public bool VerdictPassed => Verdict != null && Verdict.Status == ScenarioStatus.Pass;

        /// <summary>"verdict pending|pass|fail" (or "not staged"), then admission and undo when present.</summary>
        public string Label
        {
            get
            {
                string text = Verdict == null ? "not staged" : "verdict " + Wire(Verdict.Status);
                if (Admission != null)
                {
                    text += ", admission " + Wire(Admission.Status);
                }

                if (Undo != null)
                {
                    text += ", undo " + Wire(Undo.Status);
                }

                return text;
            }
        }

        public static string Wire(ScenarioStatus status)
        {
            switch (status)
            {
                case ScenarioStatus.Pass:
                    return "pass";
                case ScenarioStatus.Fail:
                    return "fail";
                default:
                    return "pending";
            }
        }
    }

    /// <summary>Staging-lane helpers.</summary>
    public static class CandidateStaging
    {
        /// <summary>True when a change set needs a stage verdict: a mechanism.propose / mechanism.admit op, or a tool whose entry declares RequiresStageVerdict.</summary>
        public static bool Requires(StudioRuntime runtime, ChangeSet changeSet)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Tool == BuiltInToolIds.MechanismPropose || operation.Tool == MechanismAdmission.AdmitTool)
                {
                    return true;
                }

                IReadOnlyList<ValidatorRef>? validators = runtime.Registry.Find(operation.Tool)?.Entry.Validators;
                if (validators != null)
                {
                    foreach (ValidatorRef validator in validators)
                    {
                        if (validator.Id == MechanismAdmission.ValidatorId)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>True when an entry admitted a mechanism (its undo must go through StageAdmission.Undo).</summary>
        public static bool IsAdmission(ChangeSet changeSet)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Tool == MechanismAdmission.AdmitTool)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The staging scenarios of the journal entry (else of <paramref name="fallback"/>).</summary>
        public static StageState StateOf(StudioRuntime runtime, string changeSetId, ChangeSet? fallback = null)
        {
            ChangeSet? source = runtime.Journal.Exists(changeSetId) ? runtime.Journal.Read(changeSetId) : null;
            IReadOnlyList<ValidationScenario>? validation = source?.Validation ?? fallback?.Validation;
            return new StageState(Find(validation, StageAdmission.VerdictScenario), Find(validation, StageAdmission.AdmissionScenario), Find(validation, StageAdmission.UndoScenario));
        }

        /// <summary>The package artifact reference of the propose/admit op (<c>sha256:...</c>), or null.</summary>
        public static string? PackageRef(ChangeSet changeSet)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if ((operation.Tool == BuiltInToolIds.MechanismPropose || operation.Tool == MechanismAdmission.AdmitTool) && operation.Args?["package"] is JToken package)
                {
                    return package.Type == JTokenType.Object ? (string?)package["artifact"] : (string?)package;
                }
            }

            return null;
        }

        /// <summary>Writes a stage.verdict scenario on the journal entry (a failed stage job carries no verdict bytes to record).</summary>
        public static void MarkVerdict(StudioRuntime runtime, string changeSetId, ScenarioStatus status, string detail)
        {
            ChangeSet? entry = runtime.Journal.Exists(changeSetId) ? runtime.Journal.Read(changeSetId) : null;
            if (entry != null)
            {
                runtime.Journal.Write(StageAdmission.WithScenario(entry, StageAdmission.VerdictScenario, status, detail));
            }
        }

        private static ValidationScenario? Find(IReadOnlyList<ValidationScenario>? validation, string scenario)
        {
            if (validation == null)
            {
                return null;
            }

            ValidationScenario? found = null;
            foreach (ValidationScenario item in validation)
            {
                if (item.Scenario == scenario)
                {
                    found = item;
                }
            }

            return found;
        }

        /// <summary>The verdict reference of a finished stage job (<c>verdict.verdictRef</c>), or null.</summary>
        public static string? VerdictRef(JObject job)
        {
            return job["verdict"] is JObject verdict ? (string?)verdict["verdictRef"] : null;
        }

        /// <summary>The failure of a stage job (<c>{code:"stage_failed", reason, message, hint}</c>) as a diagnostic, or null.</summary>
        public static Diagnostic? FailureOf(JObject job)
        {
            if (!(job["verdict"] is JObject verdict) || verdict["code"] == null)
            {
                return (string?)job["state"] == "failed" ? new Diagnostic(DiagnosticCodes.StageFailed, "The stage job failed without a verdict.") : null;
            }

            string reason = (string?)verdict["reason"] ?? "unknown";
            return new Diagnostic((string?)verdict["code"] ?? DiagnosticCodes.StageFailed, (string?)verdict["message"] ?? ("stage failed: " + reason), (string?)verdict["hint"], null, new JObject { ["reason"] = reason });
        }

        /// <summary>Strips the <c>sha256:</c> prefix.</summary>
        public static string Digest(string reference) => reference.StartsWith(ContentStamp.Prefix, StringComparison.Ordinal) ? reference.Substring(ContentStamp.Prefix.Length) : reference;
    }
}
