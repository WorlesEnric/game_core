#nullable enable
using System;
using System.IO;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Unity.App;
using Newtonsoft.Json.Linq;

namespace GameCore.Gameplay.World.Editor
{
    /// <summary>Trusted game bootstrap bindings. Call again after domain reload and world replacement.</summary>
    public static class StudioAdmissionServices
    {
        public static void BindAdmission(StudioRuntime runtime, Func<SaveService?> activeSaveService,
            Func<bool> sessionReady, Func<StageVerdict, bool> smokeTest)
        {
            if (smokeTest == null) throw new ArgumentNullException(nameof(smokeTest));
            AdmissionOptions options = BindServices(runtime, activeSaveService, sessionReady);
            options.SmokeTest = verdict => IsReady(runtime, sessionReady, verdict) && smokeTest(verdict);
            options.PollSmokeTest = null;
            // An immediate assertion cannot complete a previously asynchronous admission.
            RecoverPendingSmoke(runtime);
        }

        /// <summary>Bind a game-owned smoke poller. The game advances its registered entry on normal
        /// world frames; polling only observes Pending/Passed/Failed and never pumps the world.
        /// Rebind after reload; the dispatcher must key progress by verdict digest and active root.</summary>
        public static void BindAdmission(StudioRuntime runtime, Func<SaveService?> activeSaveService,
            Func<bool> sessionReady, Func<StageVerdict, AdmissionSmokeStatus> pollSmokeTest)
        {
            if (pollSmokeTest == null) throw new ArgumentNullException(nameof(pollSmokeTest));
            AdmissionOptions options = BindServices(runtime, activeSaveService, sessionReady);
            options.SmokeTest = null;
            options.PollSmokeTest = verdict =>
            {
                try
                {
                    if (!IsReady(runtime, sessionReady, verdict)) return AdmissionSmokeStatus.Failed;
                    AdmissionSmokeStatus status = pollSmokeTest(verdict);
                    return IsReady(runtime, sessionReady, verdict) &&
                        (status == AdmissionSmokeStatus.Pending || status == AdmissionSmokeStatus.Passed)
                        ? status : AdmissionSmokeStatus.Failed;
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    return AdmissionSmokeStatus.Failed;
                }
            };
            RecoverPendingSmoke(runtime);
        }

        private static AdmissionOptions BindServices(StudioRuntime runtime, Func<SaveService?> activeSaveService,
            Func<bool> sessionReady)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (activeSaveService == null) throw new ArgumentNullException(nameof(activeSaveService));
            if (sessionReady == null) throw new ArgumentNullException(nameof(sessionReady));
            WorldLiveOpTranslator.Register(runtime);
            AdmissionOptions options = StageAdmission.Of(runtime).Options;
            options.Capture = new SaveServiceAdmissionCapture(activeSaveService);
            options.SessionReady = sessionReady;
            return options;
        }

        private static bool IsReady(StudioRuntime runtime, Func<bool> sessionReady, StageVerdict verdict) =>
            sessionReady() && ReferenceEquals(StageAdmission.Of(runtime).VerdictOf(verdict.ChangeSetId), verdict);

        /// <summary>Re-register pending polling from durable admission state without resetting its budget.
        /// No game registration survives a domain reload: absence fails closed, even with a bool assertion.
        /// Companion verdict refresh and rollback remain owned by StageAdmission.</summary>
        public static void RecoverPendingSmoke(StudioRuntime runtime)
        {
            StageAdmission admission = StageAdmission.Of(runtime);
            if (!Directory.Exists(admission.StateRoot)) return;
            foreach (string path in Directory.GetFiles(admission.StateRoot, "pending-cs_*.json"))
            {
                string id = Path.GetFileNameWithoutExtension(path).Substring("pending-".Length);
                if ((string?)admission.ReadPending(id)?["phase"] != "smoke-pending") continue;
                admission.Options.PollSmokeTest ??= _ => AdmissionSmokeStatus.Failed;
                admission.Resume(id);
            }
        }

        /// <summary>Resolve the exact verified proposal's smoke entry for immediate trusted assertions.</summary>
        public static bool RunSmokeTest(StudioRuntime runtime, StageVerdict verdict,
            Func<string, string, int, bool> runLiveEntry)
        {
            if (runLiveEntry == null) throw new ArgumentNullException(nameof(runLiveEntry));
            return RunSmokeTest(runtime, verdict, (type, method, steps) =>
                runLiveEntry(type, method, steps) ? AdmissionSmokeStatus.Passed : AdmissionSmokeStatus.Failed)
                == AdmissionSmokeStatus.Passed;
        }

        /// <summary>Resolve the digest-bound descriptor for a trusted game-owned per-frame dispatcher.
        /// Candidate strings are data, never reflected method names or callback types. Pending is not success.</summary>
        public static AdmissionSmokeStatus RunSmokeTest(StudioRuntime runtime, StageVerdict verdict,
            Func<string, string, int, AdmissionSmokeStatus> pollLiveEntry)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (verdict == null) throw new ArgumentNullException(nameof(verdict));
            if (pollLiveEntry == null) throw new ArgumentNullException(nameof(pollLiveEntry));
            try
            {
                if (!ReferenceEquals(StageAdmission.Of(runtime).VerdictOf(verdict.ChangeSetId), verdict) ||
                    !verdict.Artifacts.TryGetValue("proposal", out string digest)) return AdmissionSmokeStatus.Failed;
                JObject proposal = JObject.Parse(Encoding.UTF8.GetString(runtime.Artifacts.Read(digest)));
                if (!(proposal["smokeTest"] is JObject smoke) || smoke["type"]?.Type != JTokenType.String)
                    return AdmissionSmokeStatus.Failed;
                string type = (string?)smoke["type"] ?? string.Empty;
                if (smoke["method"] != null && smoke["method"]!.Type != JTokenType.String) return AdmissionSmokeStatus.Failed;
                if (smoke["steps"] != null && smoke["steps"]!.Type != JTokenType.Integer) return AdmissionSmokeStatus.Failed;
                string method = (string?)smoke["method"] ?? "Begin";
                long steps = (long?)smoke["steps"] ?? 120;
                if (type.Length == 0 || method.Length == 0 || steps <= 0 || steps > 10000) return AdmissionSmokeStatus.Failed;
                AdmissionSmokeStatus status = pollLiveEntry(type, method, (int)steps);
                return status == AdmissionSmokeStatus.Pending || status == AdmissionSmokeStatus.Passed
                    ? status : AdmissionSmokeStatus.Failed;
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                return AdmissionSmokeStatus.Failed;
            }
        }
    }
}
