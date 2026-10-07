#nullable enable
using System;
using System.IO;
using System.Text;
using GameCore.Studio.Edit;
using Hollowmere.Authoring;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.R8_B
{
    // This probes the trusted game's dispatch boundary, not verdict authenticity or admission.
    // No candidate code is imported and no parsed record is attached as trusted authority.
    public sealed class LeverAdmissionProbe : EditorWindow
    {
        private const string LeverPackage = "com.hollowmere.mechanism.lever";
        private const string LeverType = "Hollowmere.Mechanism.Lever.LeverSmoke";
        [SerializeField] private string quitSignal = string.Empty;
        [SerializeField] private string report = string.Empty;

        public static void Run()
        {
            JObject result = Probe();
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-gcR8BReport");
            if (index < 0 || index + 1 == args.Length)
            {
                throw new ArgumentException("-gcR8BReport requires an output path");
            }

            string path = Path.GetFullPath(args[index + 1]);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, result.ToString());
            LeverAdmissionProbe window = CreateInstance<LeverAdmissionProbe>();
            window.quitSignal = path + ".quit";
            window.titleContent = new GUIContent("R8-B lever prerequisite");
            window.report = result.ToString();
            window.minSize = new Vector2(1000, 650);
            window.ShowUtility();
            window.position = new Rect(140, 140, 1000, 650);
            Debug.Log("[R8-B] " + result);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("W-DOC-02 remains FAIL: new lever has no trusted game smoke registration. This is a prerequisite probe, not a signed stage or an admission.", MessageType.Warning);
            EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
        }

        private void Update()
        {
            if (!string.IsNullOrEmpty(quitSignal) && File.Exists(quitSignal))
            {
                EditorApplication.Exit(0);
            }
        }

        private static JObject Probe()
        {
            // Dispatch checks precede access to boot/service. Null dependencies deliberately
            // demonstrate that the observed refusal is the registration gate, not world readiness.
            var smoke = new HollowmereAdmittedSmoke(null!, null!);
            var document = new JObject
            {
                ["schema"] = StageVerdict.SchemaId,
                ["package"] = LeverPackage,
                ["pass"] = true,
            };
            StageVerdict verdict = StageVerdict.Parse(Encoding.UTF8.GetBytes(document.ToString()), out string? problem)
                ?? throw new InvalidOperationException(problem);
            AdmissionSmokeStatus status = smoke.RunAdmittedSmokeEntry(verdict, LeverType, "Begin", 120);
            string leverReport = smoke.LastReport;
            AdmissionSmokeStatus aliasStatus = smoke.RunAdmittedSmokeEntry(verdict, HollowmereAdmittedSmoke.PressurePlateType, "Begin", 120);
            return new JObject
            {
                ["row"] = "W-DOC-02",
                ["status"] = "FAIL",
                ["probeOnly"] = true,
                ["signedVerdict"] = false,
                ["admitAttempted"] = false,
                ["package"] = LeverPackage,
                ["type"] = LeverType,
                ["smokeStatus"] = status.ToString(),
                ["smokeReport"] = leverReport,
                ["aliasStatus"] = aliasStatus.ToString(),
                ["aliasReport"] = smoke.LastReport,
                ["registrations"] = smoke.Registrations,
                ["steps"] = smoke.Steps,
                ["display"] = Environment.GetEnvironmentVariable("DISPLAY") ?? string.Empty,
                ["graphics"] = SystemInfo.graphicsDeviceName,
                ["isBatchMode"] = Application.isBatchMode,
            };
        }

        [Test]
        public static void SR_12_2_NewLeverSmokeIsBlockedBeforeWorldAccess()
        {
            JObject result = Probe();
            Assert.That((string?)result["smokeStatus"], Is.EqualTo("Failed"));
            Assert.That((string?)result["smokeReport"], Does.Contain("no trusted smoke entry"));
            Assert.That((string?)result["aliasStatus"], Is.EqualTo("Failed"));
            Assert.That((string?)result["aliasReport"], Does.Contain("the entry belongs to"));
            Assert.That((int?)result["registrations"], Is.Zero);
            Assert.That((int?)result["steps"], Is.Zero);
        }
    }
}
