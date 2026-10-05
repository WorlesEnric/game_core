#nullable enable
using System;
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
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (activeSaveService == null) throw new ArgumentNullException(nameof(activeSaveService));
            if (sessionReady == null) throw new ArgumentNullException(nameof(sessionReady));
            if (smokeTest == null) throw new ArgumentNullException(nameof(smokeTest));
            WorldLiveOpTranslator.Register(runtime);
            AdmissionOptions options = StageAdmission.Of(runtime).Options;
            options.Capture = new SaveServiceAdmissionCapture(activeSaveService);
            options.SessionReady = sessionReady;
            options.SmokeTest = verdict => sessionReady() &&
                ReferenceEquals(StageAdmission.Of(runtime).VerdictOf(verdict.ChangeSetId), verdict) && smokeTest(verdict);
        }

        /// <summary>Resolve the exact verified proposal's smoke entry for a trusted game-owned dispatcher.
        /// The dispatcher boots/asserts against the active restored world and is idempotent by verdict digest.
        /// Candidate strings are data, never reflected method names or callback types.</summary>
        public static bool RunSmokeTest(StudioRuntime runtime, StageVerdict verdict,
            Func<string, string, int, bool> runLiveEntry)
        {
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (verdict == null) throw new ArgumentNullException(nameof(verdict));
            if (runLiveEntry == null) throw new ArgumentNullException(nameof(runLiveEntry));
            if (!ReferenceEquals(StageAdmission.Of(runtime).VerdictOf(verdict.ChangeSetId), verdict) ||
                !verdict.Artifacts.TryGetValue("proposal", out string digest)) return false;
            JObject proposal = JObject.Parse(Encoding.UTF8.GetString(runtime.Artifacts.Read(digest)));
            if (!(proposal["smokeTest"] is JObject smoke) || smoke["type"]?.Type != JTokenType.String) return false;
            string type = (string?)smoke["type"] ?? string.Empty;
            if (smoke["method"] != null && smoke["method"]!.Type != JTokenType.String) return false;
            if (smoke["steps"] != null && smoke["steps"]!.Type != JTokenType.Integer) return false;
            string method = (string?)smoke["method"] ?? "Begin";
            long steps = (long?)smoke["steps"] ?? 120;
            return type.Length > 0 && method.Length > 0 && steps > 0 && steps <= 10000 &&
                runLiveEntry(type, method, (int)steps);
        }
    }
}
