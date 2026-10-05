// Hollowmere.P1_7b.EditMode.Tests - W-PLUG-12 batch entry for studio/tools/clean-clone-verify.sh: on a fresh clone,
// (1) Entry.Verify against the committed bake outputs (reported: stale outputs after a definition change are expected
// until the content owner re-bakes), (2) Entry.Bake without an asset refresh, (3) Entry.Verify again, which must pass:
// two bakes of the same content are byte-identical. Writes a JSON report next to the log and exits 0 only when (3)
// passes. Not a test; -executeMethod Hollowmere.P1_7b.EditMode.Tests.CleanCloneBakeCheck.Run.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_7b.EditMode.Tests
{
    public static class CleanCloneBakeCheck
    {
        /// <summary>Environment variable naming the JSON report path (default: Temp/p17b-clean-clone-verify.json).</summary>
        public const string ReportVariable = "P17B_BAKE_REPORT";

        public static void Run()
        {
            int code;
            try
            {
                code = Check();
            }
            catch (Exception error)
            {
                UnityEngine.Debug.LogError("[P1.7b] clean-clone bake check failed: " + error);
                code = 2;
            }

            EditorApplication.Exit(code);
        }

        private static int Check()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(WorldDefinition));
            if (guids.Length != 1)
            {
                UnityEngine.Debug.LogError("[P1.7b] expected one WorldDefinition, found " + guids.Length);
                return 2;
            }

            string worldPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(worldPath);
            BakePaths paths = BakePaths.ConventionFor(worldPath);
            DateTime start = DateTime.UtcNow;
            BakeResult committed = Entry.Verify(world, paths);
            BakeResult bake = Entry.Bake(world, paths, false);
            BakeResult rebaked = Entry.Verify(world, paths);
            double seconds = (DateTime.UtcNow - start).TotalSeconds;

            var json = new StringBuilder();
            json.Append("{\n");
            Field(json, "world", worldPath, true);
            Field(json, "committedVerify", committed.Succeeded ? "pass" : "fail", true);
            Field(json, "committedSummary", committed.ToString(), true);
            json.Append("  \"committedMismatches\": ").Append(Codes(committed.Diagnostics)).Append(",\n");
            Field(json, "bake", bake.Succeeded ? "pass" : "fail", true);
            Field(json, "bakeSummary", bake.ToString(), true);
            Field(json, "rebakedVerify", rebaked.Succeeded ? "pass" : "fail", true);
            Field(json, "rebakedSummary", rebaked.ToString(), true);
            json.Append("  \"seconds\": ").Append(seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            json.Append("}\n");
            string report = Environment.GetEnvironmentVariable(ReportVariable) ?? Path.Combine("Temp", "p17b-clean-clone-verify.json");
            File.WriteAllText(report, json.ToString(), new UTF8Encoding(false));
            UnityEngine.Debug.Log("[P1.7b] committed verify: " + committed + "\n[P1.7b] bake: " + bake + "\n[P1.7b] verify after bake: " + rebaked);
            return bake.Succeeded && rebaked.Succeeded ? 0 : 1;
        }

        private static string Codes(IReadOnlyList<GameplayDiagnostic> diagnostics)
        {
            var parts = new List<string>();
            for (int i = 0; i < diagnostics.Count && i < 200; i++)
            {
                parts.Add("\"" + Escape(diagnostics[i].ToString()) + "\"");
            }

            return "[" + string.Join(", ", parts) + "]";
        }

        private static void Field(StringBuilder json, string name, string value, bool comma)
        {
            json.Append("  \"").Append(name).Append("\": \"").Append(Escape(value)).Append('"').Append(comma ? ",\n" : "\n");
        }

        private static string Escape(string text) =>
            (text ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", string.Empty);
    }
}
