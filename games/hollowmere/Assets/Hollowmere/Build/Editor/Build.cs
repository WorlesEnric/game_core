// Hollowmere - the Linux graphical player build (P3.1, W-GAME-06; SADR-017 StandaloneLinux64 IL2CPP, URP, graphical).
//
//   Unity -batchmode -nographics -projectPath games/hollowmere -logFile <log> \
//         -executeMethod Hollowmere.Build.BuildLinuxPlayer -buildOutput <dir> [-buildRevision <sha>] [-development]
//
// studio/tools/build_game_player.sh runs it on the host under the shared Unity lock (studio/tools/unity-batch.sh).
// Scenes are the enabled EditorBuildSettings scenes (Boot.unity must be first). The scripting backend (IL2CPP) and
// managed stripping level (Low, link.xml honoured) are committed in ProjectSettings; the method only sets them when
// they differ (and restores them afterwards), so a normal build changes no project file. It writes
// <dir>/build-report.json, prints one "HOLLOWMERE-BUILD result=... size=... seconds=..." line and exits 0 on success,
// 1 otherwise.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hollowmere
{
    /// <summary>Builds the Hollowmere Linux player.</summary>
    public static class Build
    {
        public const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        public const string ExecutableName = "Hollowmere.x86_64";
        public const string ReportName = "build-report.json";

        /// <summary>The -executeMethod entry point; always exits the Editor.</summary>
        public static void BuildLinuxPlayer()
        {
            int code = 1;
            try
            {
                code = Run(Environment.GetCommandLineArgs()) ? 0 : 1;
            }
            catch (Exception error)
            {
                Debug.LogError("[Hollowmere.Build] " + error);
                Console.WriteLine("HOLLOWMERE-BUILD result=Exception size=0 seconds=0 error=" + error.GetType().Name + ": " + error.Message);
            }

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(code);
            }
        }

        /// <summary>Builds with the given command line; true on success.</summary>
        public static bool Run(IReadOnlyList<string> args)
        {
            string? output = Value(args, "-buildOutput");
            if (string.IsNullOrEmpty(output))
            {
                Console.WriteLine("HOLLOWMERE-BUILD result=Refused size=0 seconds=0 error=-buildOutput <dir> is required");
                return false;
            }

            string revision = Value(args, "-buildRevision") ?? "unknown";
            bool development = Has(args, "-development");
            string outputDir = Path.GetFullPath(output!);
            Directory.CreateDirectory(outputDir);

            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path))
                {
                    scenes.Add(scene.path);
                }
            }

            if (scenes.Count == 0 || !string.Equals(scenes[0], BootScene, StringComparison.Ordinal))
            {
                string first = scenes.Count == 0 ? "(none)" : scenes[0];
                Console.WriteLine("HOLLOWMERE-BUILD result=Refused size=0 seconds=0 error=the first enabled build scene must be " + BootScene + ", not " + first);
                return false;
            }

            NamedBuildTarget standalone = NamedBuildTarget.Standalone;
            ScriptingImplementation previousBackend = PlayerSettings.GetScriptingBackend(standalone);
            ManagedStrippingLevel previousStripping = PlayerSettings.GetManagedStrippingLevel(standalone);
            bool changedBackend = previousBackend != ScriptingImplementation.IL2CPP;
            bool changedStripping = previousStripping != ManagedStrippingLevel.Low;
            if (changedBackend)
            {
                PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.IL2CPP);
            }

            if (changedStripping)
            {
                PlayerSettings.SetManagedStrippingLevel(standalone, ManagedStrippingLevel.Low);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = Path.Combine(outputDir, ExecutableName),
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };

            Debug.Log("[Hollowmere.Build] building " + string.Join(", ", scenes) + " -> " + options.locationPathName + " (revision " + revision
                + ", IL2CPP, stripping Low, development=" + development + ")");
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            finally
            {
                if (changedBackend)
                {
                    PlayerSettings.SetScriptingBackend(standalone, previousBackend);
                }

                if (changedStripping)
                {
                    PlayerSettings.SetManagedStrippingLevel(standalone, previousStripping);
                }
            }

            BuildSummary summary = report.summary;
            bool ok = summary.result == BuildResult.Succeeded;
            string json = ReportJson(report, revision, development, scenes, changedBackend, changedStripping);
            File.WriteAllText(Path.Combine(outputDir, ReportName), json, new UTF8Encoding(false));
            Console.WriteLine("HOLLOWMERE-BUILD result=" + summary.result + " size=" + summary.totalSize.ToString(CultureInfo.InvariantCulture)
                + " seconds=" + summary.totalTime.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)
                + " errors=" + summary.totalErrors.ToString(CultureInfo.InvariantCulture)
                + " warnings=" + summary.totalWarnings.ToString(CultureInfo.InvariantCulture)
                + " output=" + options.locationPathName);
            return ok;
        }

        private static string ReportJson(BuildReport report, string revision, bool development, List<string> scenes, bool changedBackend, bool changedStripping)
        {
            BuildSummary summary = report.summary;
            var json = new StringBuilder();
            json.Append("{\n");
            Field(json, "result", summary.result.ToString());
            Field(json, "platform", summary.platform.ToString());
            Field(json, "scriptingBackend", ScriptingImplementation.IL2CPP.ToString());
            Field(json, "managedStripping", ManagedStrippingLevel.Low.ToString());
            Field(json, "unityVersion", Application.unityVersion);
            Field(json, "revision", revision);
            Field(json, "outputPath", summary.outputPath);
            json.Append("  \"development\": ").Append(development ? "true" : "false").Append(",\n");
            json.Append("  \"settingsChangedForBuild\": ").Append(changedBackend || changedStripping ? "true" : "false").Append(",\n");
            json.Append("  \"totalSize\": ").Append(summary.totalSize.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"totalTimeSeconds\": ").Append(summary.totalTime.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"errors\": ").Append(summary.totalErrors.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("  \"warnings\": ").Append(summary.totalWarnings.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            Field(json, "startedUtc", summary.buildStartedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            json.Append("  \"scenes\": [");
            for (int i = 0; i < scenes.Count; i++)
            {
                json.Append(i == 0 ? string.Empty : ", ").Append('"').Append(Escape(scenes[i])).Append('"');
            }

            json.Append("],\n  \"steps\": [");
            BuildStep[] steps = report.steps;
            for (int i = 0; i < steps.Length; i++)
            {
                json.Append(i == 0 ? "\n" : ",\n").Append("    {\"name\": \"").Append(Escape(steps[i].name)).Append("\", \"seconds\": ")
                    .Append(steps[i].duration.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)).Append(", \"depth\": ")
                    .Append(steps[i].depth.ToString(CultureInfo.InvariantCulture)).Append('}');
            }

            json.Append(steps.Length == 0 ? "],\n" : "\n  ],\n");
            json.Append("  \"messages\": [");
            int written = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                foreach (BuildStepMessage message in steps[i].messages)
                {
                    if (message.type != LogType.Error && message.type != LogType.Exception && message.type != LogType.Warning)
                    {
                        continue;
                    }

                    if (written >= 200)
                    {
                        break;
                    }

                    json.Append(written == 0 ? "\n" : ",\n").Append("    {\"type\": \"").Append(message.type.ToString()).Append("\", \"text\": \"")
                        .Append(Escape(message.content.Length > 400 ? message.content.Substring(0, 400) : message.content)).Append("\"}");
                    written++;
                }
            }

            json.Append(written == 0 ? "]\n" : "\n  ]\n");
            json.Append("}\n");
            return json.ToString();
        }

        private static void Field(StringBuilder json, string name, string value) =>
            json.Append("  \"").Append(name).Append("\": \"").Append(Escape(value)).Append("\",\n");

        private static string Escape(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var escaped = new StringBuilder(text!.Length + 8);
            foreach (char c in text)
            {
                switch (c)
                {
                    case '"':
                        escaped.Append("\\\"");
                        break;
                    case '\\':
                        escaped.Append("\\\\");
                        break;
                    case '\n':
                        escaped.Append("\\n");
                        break;
                    case '\r':
                        escaped.Append("\\r");
                        break;
                    case '\t':
                        escaped.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            escaped.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            escaped.Append(c);
                        }

                        break;
                }
            }

            return escaped.ToString();
        }

        private static string? Value(IReadOnlyList<string> args, string flag)
        {
            for (int i = 0; i + 1 < args.Count; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool Has(IReadOnlyList<string> args, string flag)
        {
            for (int i = 0; i < args.Count; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
