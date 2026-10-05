#nullable enable
using System;
using System.IO;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.P2_1.Evidence;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P4_2
{
    public static class EvidenceEntry
    {
        private const string StepKey = "GameCore.Studio.P21.Evidence.Step";
        private const string FailedKey = "GameCore.Studio.P21.Evidence.Failed";

        public static void RunUi()
        {
            string file = Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? string.Empty,
                ".config", "gamecore-studio", "app-key.json");
            Environment.SetEnvironmentVariable(EtosCredentials.KeyFileVariable, file);
            bool started = EtosStudioSession.Start();
            Debug.Log("[P4.2] Explicit paired UI startup: " + started + "; " + EtosStudioSession.Problem);
            EditorApplication.update -= Guard;
            EditorApplication.update += Guard;
            StudioUiEvidence.Run();
        }

        private static void Guard()
        {
            int step = SessionState.GetInt(StepKey, -1);
            if (step != 9 && step != 13) return;
            var context = StudioUiSession.Context;
            string id = step == 9 ? EvidenceClock.instance.CandidateId : EvidenceClock.instance.LiveId;
            try
            {
                if (step == 9)
                {
                    // A rejected synthetic candidate must never undo an unrelated creator entry.
                    var entry = context.Runtime.Journal.Read(id);
                    if (entry?.EffectiveState == ChangeSetState.Applied)
                    {
                        var undo = context.Runtime.History.Undo(id);
                        Note(step, "bounded-sample-undo", "Undo targets only this run's synthetic candidate.", !undo.Ok);
                    }
                    else Note(step, "bounded-sample-undo", "Synthetic candidate did not apply; no unrelated History entry was undone.", true);
                }
                else
                {
                    CandidateEntry? candidate = context.Candidates.Find(id);
                    if (candidate == null) throw new InvalidOperationException("No real candidate arrived; apply/undo/redo are unavailable.");
                    var preview = context.Candidates.Preview(candidate);
                    var report = context.Candidates.Apply(candidate);
                    var data = new JObject { ["id"] = id, ["previewOk"] = preview.Ok, ["apply"] = StudioJson.ToToken(report.Entry) };
                    bool ok = preview.Ok && report.State == ChangeSetState.Applied;
                    if (ok)
                    {
                        StudioHistoryWindow.Open();
                        var panel = EditorWindow.GetWindow<StudioHistoryWindow>().View;
                        var undo = panel != null ? panel.Undo(id) : context.Runtime.History.Undo(id);
                        data["undoOk"] = undo.Ok;
                        var redo = panel != null ? panel.Redo(id) : context.Runtime.History.Redo(id);
                        data["redoOk"] = redo.Ok;
                        var cleanup = panel != null ? panel.Undo(id) : context.Runtime.History.Undo(id);
                        data["cleanupUndoOk"] = cleanup.Ok;
                        ok = undo.Ok && redo.Ok && cleanup.Ok;
                    }
                    File.WriteAllText(Path.Combine(StudioUiEvidence.OutputDir, "live-apply-undo-redo.json"), data.ToString());
                    Note(step, "live-apply-undo-redo", "Real candidate preview/apply/undo/redo/final undo: " + ok, !ok);
                }
            }
            catch (Exception error)
            {
                Note(step, "bounded-history-error", error.Message, true);
            }
            SessionState.SetInt(StepKey, step + 1);
        }

        private static void Note(int step, string name, string caption, bool failed)
        {
            if (failed) SessionState.SetBool(FailedKey, true);
            string filename = step.ToString("00") + "-" + name + ".png";
            string? capture = UnityWindowCapture.CaptureStudio(Path.Combine(StudioUiEvidence.OutputDir, filename), false);
            var row = new JObject { ["step"] = step, ["name"] = name, ["caption"] = caption,
                ["utc"] = DateTime.UtcNow.ToString("o"), ["file"] = filename };
            if (failed || capture != null) row["problem"] = capture ?? caption;
            File.AppendAllText(Path.Combine(StudioUiEvidence.OutputDir, "evidence-log.jsonl"), row.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            Debug.Log("[P4.2] " + caption);
        }
    }
}
