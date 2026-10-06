#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Driver = Hollowmere.P3_2.Workflows.Workflows;

namespace Hollowmere.R7_A
{
    /// <summary>Offline replay of retained, unchanged narrative candidates before a separate Editor reopen.</summary>
    public static class ReopenPreparation
    {
        public const string WorldPath = "Assets/Hollowmere/World/Hollowmere.asset";
        public static readonly IReadOnlyList<string> Tags = Array.AsReadOnly(new[] { "odd-line", "hud", "quest" });
        public static readonly IReadOnlyList<string> AssetPaths = Array.AsReadOnly(new[]
        {
            "Assets/Hollowmere/Dialogue/Graphs/Odd.asset",
            "Assets/Hollowmere/UI/Definitions/Doc_Hud.asset",
            "Assets/Hollowmere/Quests/DrownedBell.asset",
            "Assets/Hollowmere/Items/Lantern.asset",
        });
        private const string Witness = "artifacts/studio/verification/W-AI-03/p42e-narrative-20261006T151348.703137Z/workflow";

        public static void Prepare()
        {
            string output = Environment.GetEnvironmentVariable("GAMECORE_R7_EVIDENCE")
                ?? throw new InvalidOperationException("Set GAMECORE_R7_EVIDENCE to a fresh evidence directory.");
            string shared = Path.Combine(WorkflowRunner.ProjectRoot, "Library/P3_2/narrative.json");
            if (File.Exists(shared))
                throw new InvalidOperationException("Preserve the prior Library/P3_2/narrative.json before preparing another reopen exercise.");
            EditorSceneManager.OpenScene(S.VillageScene);
            JObject saved = ApplyRetained(StudioServices.Runtime, output);
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            File.WriteAllText(shared, saved.ToString());
            File.WriteAllText(Path.Combine(output, "saved.json"), saved.ToString());
            Debug.Log("R7-A retained narrative preparation saved; close this Editor and run the reopen workflow in another process.");
        }

        public static JObject ApplyRetained(StudioRuntime runtime, string output)
        {
            Directory.CreateDirectory(output);
            string source = Path.GetFullPath(Path.Combine(WorkflowRunner.ProjectRoot, "../..", Witness));
            JObject before = Hashes();
            JObject witness = JObject.Parse(File.ReadAllText(Path.Combine(source, "narrative/saved.json")));
            Driver.RequireByteConsistency(witness["before"], before);
            var candidates = new List<ChangeSet>();
            foreach (string tag in Tags)
            {
                string json = File.ReadAllText(Path.Combine(source, tag, "candidate.json"));
                ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(json);
                if (runtime.Journal.Read(candidate.Id) != null)
                    throw new InvalidOperationException("Retained candidate already exists in History: " + candidate.Id);
                candidates.Add(candidate);
                File.WriteAllText(Path.Combine(output, tag + "-candidate.json"), json);
            }
            Capture(output, "before");
            runtime.Index.Rebuild();
            var applied = new JObject();
            for (int i = 0; i < candidates.Count; i++)
            {
                ChangeSet candidate = candidates[i];
                ApplyReport report = runtime.Engine.Apply(candidate);
                File.WriteAllText(Path.Combine(output, Tags[i] + "-journal.json"), StudioJson.Serialize(report.Entry));
                if (!report.Ok)
                    throw new InvalidOperationException(Tags[i] + " retained candidate refused: " + string.Join(" | ", report.Diagnostics));
                applied[Tags[i]] = new JObject
                {
                    ["changeSetId"] = candidate.Id,
                    ["state"] = runtime.Journal.Read(candidate.Id)!.EffectiveState.ToString(),
                    ["applied"] = true,
                };
            }
            var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath);
            BakeResult bake = Entry.Bake(world, BakePaths.ConventionFor(WorldPath), false);
            File.WriteAllText(Path.Combine(output, "applied-bake.json"), new JObject
            {
                ["succeeded"] = bake.Succeeded,
                ["changedFiles"] = new JArray(bake.ChangedFiles),
                ["detail"] = bake.ToString(),
            }.ToString());
            if (!bake.Succeeded) throw new InvalidOperationException("Retained candidate bake failed: " + bake);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveOpenScenes();
            Capture(output, "saved");
            return new JObject
            {
                ["session"] = "retained-narrative",
                ["source"] = Witness,
                ["tags"] = new JArray(Tags),
                ["paths"] = new JArray(AssetPaths),
                ["applied"] = applied,
                ["before"] = before,
                ["after"] = Hashes(),
                ["preparationPid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
            };
        }

        public static JObject Hashes()
        {
            var paths = new string[AssetPaths.Count];
            for (int i = 0; i < paths.Length; i++) paths[i] = AssetPaths[i];
            return S.Hashes(paths);
        }

        private static void Capture(string output, string phase)
        {
            foreach (string asset in AssetPaths)
            {
                string destination = Path.Combine(output, phase, asset);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(asset, destination, false);
            }
        }
    }
}
