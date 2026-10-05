#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace GameCore.Studio.Edit
{
    public sealed partial class StageAdmission
    {
        private readonly Dictionary<string, EditorApplication.CallbackFunction> _smokeUpdates =
            new Dictionary<string, EditorApplication.CallbackFunction>(StringComparer.Ordinal);

        internal static Diagnostic PendingDiagnostic(string detail) => StudioDiagnostics.Normalize(
            new Diagnostic(DiagnosticCodes.Refused, detail, data: new JObject { ["reason"] = "admission_pending" }));

        // Called before the resumer awaits the companion: transport failure or a missing game
        // adapter must not suspend the durable smoke deadline after a domain reload.
        internal void ResumeSmokePolling()
        {
            if (!System.IO.Directory.Exists(StateRoot)) return;
            foreach (string file in System.IO.Directory.GetFiles(StateRoot, "pending-cs_*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(file).Substring("pending-".Length);
                if ((string?)ReadPending(id)?["phase"] == "smoke-pending") ScheduleSmokePolling(id);
            }
        }

        private void ScheduleSmokePolling(string id)
        {
            if (_smokeUpdates.ContainsKey(id)) return;
            EditorApplication.CallbackFunction tick = () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                StopSmokePolling(id);
                if (!AdmissionSession.instance.Instances.TryGetValue(_runtime, out StageAdmission current)
                    || !ReferenceEquals(current, this) || ReadPending(id) == null) return;
                Resume(id, true);
            };
            _smokeUpdates.Add(id, tick);
            EditorApplication.update += tick;
        }

        private void StopSmokePolling(string id)
        {
            if (!_smokeUpdates.TryGetValue(id, out EditorApplication.CallbackFunction tick)) return;
            EditorApplication.update -= tick;
            _smokeUpdates.Remove(id);
        }

        private void StopSmokePolling()
        {
            foreach (EditorApplication.CallbackFunction tick in _smokeUpdates.Values)
                EditorApplication.update -= tick;
            _smokeUpdates.Clear();
        }

        private static bool SmokeTimeExhausted(JObject p)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long started = (long?)p["smokeStartedMs"] ?? 0;
            long timeout = (long?)p["smokeTimeoutMs"] ?? 0;
            // A missing/invalid record or backwards clock is a closed failure, never a fresh budget.
            return started <= 0 || timeout <= 0 || now < started || now - started >= timeout;
        }

        private static bool SmokeBudgetExhausted(JObject p) => SmokeTimeExhausted(p)
            || (int?)p["smokeFrames"] == null || (int?)p["smokeFrames"] < 0
            || ((int?)p["smokeFrames"] ?? 0) >= ((int?)p["smokeFrameBudget"] ?? 0);

        private AdmissionResult SmokeWaiting(string id, JObject p, string detail)
        {
            if ((string?)p["phase"] == "smoke-pending" && SmokeBudgetExhausted(p))
                return FailSmoke(id, p, "smoke_budget_exhausted");
            return Waiting(id, detail);
        }

        private AdmissionResult FailSmoke(string id, JObject p, string reason)
        {
            p["smokeStatus"] = AdmissionSmokeStatus.Failed.ToString();
            return BeginRollback(id, p, reason);
        }
    }
}
