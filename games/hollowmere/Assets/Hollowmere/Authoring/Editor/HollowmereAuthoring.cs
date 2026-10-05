// Hollowmere - AuthorAll (P3.1): authors the complete reference game through the Studio edit engine and bakes it.
//
//   Menu:   GameCore/Hollowmere/Author All (P3.1)
//   Batch:  Unity -batchmode -projectPath games/hollowmere -executeMethod Hollowmere.Authoring.HollowmereAuthoring.AuthorAllBatch
//           [-p31Media portraits,voices] [-p31MediaMaxCalls N] [-p31Report <file.json>]
//
// Every content change is a journaled change set (StudioAuthor, intent "[P3.1:<step>] ..."); a step already in the
// journal is skipped, so a second run applies nothing. Scenes and assets are saved and the world is re-baked and
// verified at the end. Two project-file edits are not Studio tools (there is no tool for them): the build-settings
// scene list is checked (and only written when it differs) and the bake itself (Entry.Bake / Entry.Verify).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Hollowmere.Authoring.HollowmerePaths;

namespace Hollowmere.Authoring
{
    /// <summary>The outcome of one AuthorAll run.</summary>
    public sealed class AuthorAllReport
    {
        public bool Ok;
        public string? Error;
        public List<string> Applied = new List<string>();
        public List<string> Skipped = new List<string>();
        public Dictionary<string, int> ToolCounts = new Dictionary<string, int>();
        public int Operations;
        public bool BakeOk;
        public string Bake = string.Empty;
        public bool VerifyOk;
        public string Verify = string.Empty;
        public bool BuildSettingsWritten;
        public HollowmereMedia.Report? Media;
        public List<string> Log = new List<string>();
        public double Seconds;

        public override string ToString() =>
            "ok=" + Ok + " applied=" + Applied.Count + " skipped=" + Skipped.Count + " ops=" + Operations + " bake=" + BakeOk + " verify=" + VerifyOk
            + " seconds=" + Seconds.ToString("F1", CultureInfo.InvariantCulture) + (Error == null ? string.Empty : " error=" + Error);
    }

    /// <summary>The P3.1 authoring entry points.</summary>
    public static class HollowmereAuthoring
    {
        public static readonly string[] BuildScenes = { BootScene, VillageScene, MarshScene, BelfryScene };

        [MenuItem("GameCore/Hollowmere/Author All (P3.1)")]
        public static void AuthorAllMenu()
        {
            AuthorAllReport report = AuthorAll(null, 0);
            if (report.Ok)
            {
                Debug.Log("[Hollowmere P3.1] " + report);
            }
            else
            {
                Debug.LogError("[Hollowmere P3.1] " + report);
            }
        }

        /// <summary>Batch entry: exits 0 when authoring, bake and verify succeed, 1 otherwise.</summary>
        public static void AuthorAllBatch()
        {
            string[] args = Environment.GetCommandLineArgs();
            string? media = Arg(args, "-p31Media");
            int maxCalls = int.TryParse(Arg(args, "-p31MediaMaxCalls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 40;
            string? reportPath = Arg(args, "-p31Report");
            AuthorAllReport report;
            try
            {
                report = AuthorAll(media, maxCalls);
            }
            catch (Exception error)
            {
                report = new AuthorAllReport { Ok = false, Error = error.ToString() };
            }

            if (reportPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
                File.WriteAllText(reportPath, JsonConvert.SerializeObject(report, Formatting.Indented) + "\n");
            }

            Console.WriteLine("HOLLOWMERE-AUTHOR " + report);
            foreach (string line in report.Log)
            {
                Console.WriteLine("HOLLOWMERE-AUTHOR step " + line);
            }

            if (report.Media != null)
            {
                Console.WriteLine("HOLLOWMERE-AUTHOR media calls=" + report.Media.Calls + " imported=" + report.Media.Imported + " skipped=" + report.Media.Skipped
                    + " ceilingUsd=" + report.Media.CeilingUsd.ToString("F2", CultureInfo.InvariantCulture) + " blocked=" + (report.Media.Blocked ?? "no"));
                foreach (string failure in report.Media.Failures)
                {
                    Console.WriteLine("HOLLOWMERE-AUTHOR media-failure " + failure);
                }
            }

            EditorApplication.Exit(report.Ok ? 0 : 1);
        }

        private static string? Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        /// <summary>
        /// Authors everything (idempotent). <paramref name="media"/>: null = no network; "portraits", "voices" or both
        /// (comma separated) generate the missing files through the ETOS gateway first.
        /// </summary>
        public static AuthorAllReport AuthorAll(string? media, int mediaMaxCalls)
        {
            var report = new AuthorAllReport();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            StudioRuntime runtime = StudioServices.Runtime;
            OpenScenes();
            var author = new StudioAuthor(runtime);
            try
            {
                HollowmereStory.Author(author);
                HollowmereMedia.AuthorSfx(author);
                HollowmereWorldContent.Author(author);
                HollowmereDressing.Author(author);
                Closures(author);
                if (!string.IsNullOrEmpty(media))
                {
                    string[] kinds = media!.Split(',');
                    report.Media = HollowmereMedia.Generate(runtime, kinds.Contains("portraits"), kinds.Contains("voices"), mediaMaxCalls);
                    AssetDatabase.Refresh();
                }

                HollowmereMedia.AttachGenerated(author);
                SaveAll();
            }
            catch (Exception error)
            {
                report.Error = error.Message;
                Collect(author, report);
                SaveAll();
                report.Seconds = watch.Elapsed.TotalSeconds;
                return report;
            }

            Collect(author, report);
            report.BuildSettingsWritten = EnsureBuildSettings();
            WorldDefinition world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath)
                ?? throw new InvalidOperationException("P3.1: no world at " + WorldPath);
            BakeResult bake = Entry.Bake(world, BakePaths.ConventionFor(WorldPath), false);
            report.BakeOk = bake.Succeeded;
            report.Bake = bake.ToString();
            if (bake.Succeeded)
            {
                BakeResult verify = Entry.Verify(world, BakePaths.ConventionFor(WorldPath));
                report.VerifyOk = verify.Succeeded;
                report.Verify = verify.ToString();
            }

            AssetDatabase.SaveAssets();
            report.Ok = report.Error == null && report.BakeOk && report.VerifyOk;
            report.Seconds = watch.Elapsed.TotalSeconds;
            return report;
        }

        private static void Collect(StudioAuthor author, AuthorAllReport report)
        {
            report.Applied.AddRange(author.AppliedNow);
            report.Skipped.AddRange(author.Skipped);
            foreach (KeyValuePair<string, int> pair in author.ToolCounts)
            {
                report.ToolCounts[pair.Key] = pair.Value;
            }

            report.Operations = author.OperationsApplied;
            report.Log.AddRange(author.Log);
        }

        /// <summary>Boot.unity single, the three region scenes additive (the placement tools need them loaded).</summary>
        public static void OpenScenes()
        {
            Scene boot = SceneManager.GetSceneByPath(BootScene);
            if (!boot.IsValid() || !boot.isLoaded)
            {
                EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
            }

            foreach (string scene in new[] { VillageScene, MarshScene, BelfryScene })
            {
                Scene loaded = SceneManager.GetSceneByPath(scene);
                if (!loaded.IsValid() || !loaded.isLoaded)
                {
                    EditorSceneManager.OpenScene(scene, OpenSceneMode.Additive);
                }
            }

            SceneManager.SetActiveScene(SceneManager.GetSceneByPath(BootScene));
        }

        public static void SaveAll()
        {
            EditorSceneManager.SaveOpenScenes();
            AssetDatabase.SaveAssets();
        }

        /// <summary>Boot plus the three regions, in order (written only when the list differs).</summary>
        public static bool EnsureBuildSettings()
        {
            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            bool same = current.Length == BuildScenes.Length;
            for (int i = 0; same && i < BuildScenes.Length; i++)
            {
                same = current[i].enabled && current[i].path == BuildScenes[i];
            }

            if (same)
            {
                return false;
            }

            EditorBuildSettings.scenes = BuildScenes.Select(path => new EditorBuildSettingsScene(path, true)).ToArray();
            return true;
        }

        // ------------------------------------------------------------------ closures (P1.x left-open items)

        public const string RunActionId = "5b0e3f0c-3d2a-4a51-9a5e-7c1f2b8d6e41";

        private static void Closures(StudioAuthor a)
        {
            a.Step("closure.stamina-hud", "Bind the HUD stamina bar to the player's committed stamina slot (percent of 1000)", () => new List<Operation>
            {
                StudioAuthor.Call("bind", "ui.bind", a.Ref(HudDocument), new JObject
                {
                    ["element"] = "stamina-bar",
                    ["source"] = "slot:player.owner/player.stamina@focus",
                    ["property"] = "value",
                    ["format"] = "percent:1000",
                }),
            });

            var artifacts = new List<ArtifactRef>();
            a.Step("closure.run-action", "Add the Run action (left shift, left stick press) to the player input actions", () =>
            {
                string full = Path.Combine(Path.GetDirectoryName(Application.dataPath)!, InputActions);
                JObject asset = JObject.Parse(File.ReadAllText(full));
                var map = (JObject)((JArray)asset["maps"]!)[0];
                var actions = (JArray)map["actions"]!;
                if (actions.Any(action => (string?)action["name"] == "Run"))
                {
                    return new List<Operation>();
                }

                actions.Add(new JObject
                {
                    ["name"] = "Run",
                    ["type"] = "Button",
                    ["id"] = RunActionId,
                    ["expectedControlType"] = "Button",
                    ["processors"] = string.Empty,
                    ["interactions"] = string.Empty,
                    ["initialStateCheck"] = false,
                });
                var bindings = (JArray)map["bindings"]!;
                bindings.Add(Binding("9d3c1f6e-2b47-4c8a-8f0e-1a6b5c4d3e21", "<Keyboard>/leftShift", "Keyboard&Mouse"));
                bindings.Add(Binding("c4e8a2b1-7f36-4d59-b0a3-6e2d9f1c8b52", "<Gamepad>/leftStickPress", "Gamepad"));
                string text = asset.ToString(Formatting.Indented).Replace("  ", "    ").Replace("\r\n", "\n");
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                ArtifactRef artifact = a.Retain(bytes, "application/json", "Player.inputactions", "edit.inputActions", "inputActions");
                artifacts.Add(artifact);
                return new List<Operation> { StudioAuthor.Import("actions", InputActions, artifact) };
            }, artifacts);
        }

        private static JObject Binding(string id, string path, string group) => new JObject
        {
            ["name"] = string.Empty,
            ["id"] = id,
            ["path"] = path,
            ["interactions"] = string.Empty,
            ["processors"] = string.Empty,
            ["groups"] = group,
            ["action"] = "Run",
            ["isComposite"] = false,
            ["isPartOfComposite"] = false,
        };
    }
}
