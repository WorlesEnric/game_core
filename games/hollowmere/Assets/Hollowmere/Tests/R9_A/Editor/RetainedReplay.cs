#nullable enable
using System;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.World;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.P3_2.Workflows;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.R9_A
{
    /// <summary>Offline retained-candidate replay. SessionState survives the production Play reloads.</summary>
    [InitializeOnLoad]
    public static class RetainedReplay
    {
        public const string NpcWitness = "artifacts/studio/verification/W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json";
        public const string OddWitness = "artifacts/studio/verification/W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json";
        private const string Active = "R9_A.Replay.Active";
        private const string Phase = "R9_A.Replay.Phase";
        private const string World = "Assets/Hollowmere/World/Hollowmere.asset";
        private static string Mode => Environment.GetEnvironmentVariable("GAMECORE_R9A_MODE") ?? throw new InvalidOperationException("GAMECORE_R9A_MODE is required");
        private static string Output => Environment.GetEnvironmentVariable("GAMECORE_R9A_OUTPUT") ?? throw new InvalidOperationException("GAMECORE_R9A_OUTPUT is required");
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
        private static string Tag => Mode == "npc" ? "ferryman2" : "odd-line";
        private static StudioRuntime Runtime => S.Context.Runtime;
        private static string Id => (string)Manifest()["changeSetId"]!;
        private static JObject Manifest() => JObject.Parse(File.ReadAllText(Path.Combine(Output, "manifest.json")));

        static RetainedReplay()
        {
            if (SessionState.GetBool(Active, false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            try
            {
                WorkflowPlayChecks.Require(Application.isBatchMode, "Use the batch wrapper; interactive ETOS bootstrap is not permitted");
                WorkflowPlayChecks.Require(Mode == "npc" || Mode == "odd" || Mode == "odd-reopen", "Unknown replay mode");
                Directory.CreateDirectory(Output);
                Environment.SetEnvironmentVariable("GCS_P32_OUT", Output);
                P32State.instance.Reset("r9-a-retained");
                EditorSceneManager.OpenScene(S.VillageScene, OpenSceneMode.Single);
                if (Mode == "odd-reopen") Reopen();
                else Prepare();
                SessionState.SetInt(Phase, 0);
                SessionState.SetBool(Active, true);
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Prepare()
        {
            WorkflowPlayChecks.Require(!File.Exists(Path.Combine(Output, "manifest.json")), "Evidence directory already contains a replay; do not overwrite it");
            string witness = Mode == "npc" ? NpcWitness : OddWitness;
            byte[] bytes = File.ReadAllBytes(Path.Combine(Repo, witness));
            var candidate = StudioJson.Deserialize<ChangeSet>(System.Text.Encoding.UTF8.GetString(bytes));
            string requestPath = Path.Combine(Path.GetDirectoryName(witness)!, "request.json");
            byte[] requestBytes = File.ReadAllBytes(Path.Combine(Repo, requestPath));
            var request = JObject.Parse(System.Text.Encoding.UTF8.GetString(requestBytes));
            string requestId = (string?)request["changeSetId"] ?? string.Empty;
            string revision = (string?)request["toolCatalogRevision"] ?? string.Empty;
            WorkflowPlayChecks.Require(requestId == candidate.Id || requestId == candidate.Links?.Parent,
                "Retained request is neither candidate request nor clarification parent");
            const string historicalCatalogPath = "artifacts/studio/verification/W-DOC-02/p42i-lever-literal/tool-catalog.json";
            byte[] historicalBytes = File.ReadAllBytes(Path.Combine(Repo, historicalCatalogPath));
            var historical = StudioJson.Deserialize<ToolCatalog>(System.Text.Encoding.UTF8.GetString(historicalBytes));
            // The retained workflow had already installed the production Play action catalog.
            // Registering the adapter/tool specs does not execute a world action or mutate authored data.
            Type translator = Type.GetType("GameCore.Gameplay.World.Editor.WorldLiveOpTranslator, GameCore.Studio.Gameplay.Editor", true)!;
            var register = translator.GetMethod("Register", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(StudioRuntime) }, null)
                ?? throw new InvalidOperationException("Production runtime action catalog registration is unavailable");
            register.Invoke(null, new object[] { Runtime });
            ToolCatalog current = Runtime.Registry.Catalog;
            WorkflowPlayChecks.Require(historical.HasValidRevision() && historical.Revision == revision && current.HasValidRevision(),
                "Retained request catalog or current catalog does not have a valid matching revision");
            bool reviewed = Environment.GetEnvironmentVariable("GAMECORE_R9A_REVIEW_CURRENT_CATALOG") == "1";
            bool historicalCatalogMatched = revision == current.Revision;
            File.WriteAllBytes(Path.Combine(Output, "retained-request.json"), requestBytes);
            File.WriteAllBytes(Path.Combine(Output, "retained-catalog.json"), historicalBytes);
            Save("current-catalog.json", StudioJson.ToToken(current));
            Save("catalog-diff.json", new JObject
            {
                ["tools"] = CatalogDiff((JArray)StudioJson.ToToken(historical)["tools"]!, (JArray)StudioJson.ToToken(current)["tools"]!, "id"),
                ["objectTypes"] = CatalogDiff((JArray)StudioJson.ToToken(historical)["objectTypes"]!, (JArray)StudioJson.ToToken(current)["objectTypes"]!, "typeId"),
            });
            Save("catalog-context.json", new JObject { ["request"] = requestPath,
                ["requestSha256"] = ContentStamp.Sha256Hex(requestBytes), ["requestId"] = requestId,
                ["retainedRevision"] = revision, ["currentRevision"] = current.Revision,
                ["reviewKind"] = reviewed ? "ExplicitRetainedReplay" : "OriginalRequestContext",
                ["historicalCatalogMatched"] = historicalCatalogMatched,
                ["candidateSha256"] = ContentStamp.Sha256Hex(bytes),
                ["historicalCatalogSource"] = historicalCatalogPath });
            WorkflowPlayChecks.Require(historicalCatalogMatched || reviewed,
                "StaleContext: retained catalog revision differs; explicit creator re-review is required, not original-request continuation");
            Runtime.Index.Rebuild();
            var usedTools = candidate.Operations.Select(operation => operation.Tool).Distinct().ToArray();
            var usedTypes = candidate.Operations.Select(operation => operation.Tool == "create" ? (string?)operation.Args?["type"] : null)
                .Concat(usedTools.Select(tool => current.FindTool(tool)?.TargetType))
                .Concat(candidate.Operations.Where(operation => operation.Target != null)
                    .Select(operation => Runtime.Index.Snapshot().FindNode(operation.Target!)?.Type))
                .Where(type => !string.IsNullOrEmpty(type)).Cast<string>().Distinct().ToArray();
            var usedSpecDiff = new JArray();
            foreach (string tool in usedTools)
            {
                var before = historical.FindTool(tool); var after = current.FindTool(tool);
                if (before == null || after == null || !JToken.DeepEquals(StudioJson.ToToken(before), StudioJson.ToToken(after)))
                    usedSpecDiff.Add(new JObject { ["kind"] = "tool", ["id"] = tool,
                        ["before"] = before == null ? null : StudioJson.ToToken(before), ["after"] = after == null ? null : StudioJson.ToToken(after) });
            }
            foreach (string type in usedTypes)
            {
                var before = historical.FindObjectType(type); var after = current.FindObjectType(type);
                if (before == null || after == null || !JToken.DeepEquals(StudioJson.ToToken(before), StudioJson.ToToken(after)))
                    usedSpecDiff.Add(new JObject { ["kind"] = "objectType", ["id"] = type,
                        ["before"] = before == null ? null : StudioJson.ToToken(before), ["after"] = after == null ? null : StudioJson.ToToken(after) });
            }
            Save("used-catalog-specs.json", new JObject { ["tools"] = new JArray(usedTools), ["objectTypes"] = new JArray(usedTypes),
                ["unchanged"] = usedSpecDiff.Count == 0, ["differences"] = usedSpecDiff });
            WorkflowPlayChecks.Require(usedSpecDiff.Count == 0, "Retained replay used tool/object specifications changed; inspect used-catalog-specs.json before review");
            string reviewedRevision = reviewed ? current.Revision! : revision;
            CheckFailedResume(candidate, bytes, witness);
            BakeSave();
            var paths = candidate.Operations.Select(op => op.Target?.Path).Where(p => p != null && p.EndsWith(".asset", StringComparison.Ordinal))
                .Cast<string>().Concat(new[] { "Assets/Hollowmere/Rules/HollowmereContent.asset", S.VillageScene }).Distinct().Where(File.Exists).ToArray();
            var manifest = new JObject
            {
                ["witness"] = witness, ["witnessSha256"] = ContentStamp.Sha256Hex(bytes),
                ["changeSetId"] = candidate.Id, ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["reviewKind"] = reviewed ? "ExplicitRetainedReplay" : "OriginalRequestContext",
                ["historicalCatalogMatched"] = historicalCatalogMatched,
                ["retainedCatalogRevision"] = revision, ["reviewedCatalogRevision"] = reviewedRevision,
                ["before"] = Hashes(paths), ["paths"] = new JArray(paths),
                ["created"] = new JArray(candidate.Operations.Where(op => op.Tool == "create").Select(op => (string)op.Args!["path"]!)),
            };
            Save("manifest.json", manifest);
            File.WriteAllBytes(Path.Combine(Output, "candidate.json"), bytes);
            if (Mode == "npc") WorkflowPlayChecks.CaptureNpcBaseline(); else WorkflowPlayChecks.CaptureDialogueBaseline();
            Runtime.Index.Rebuild();
            var entry = S.Context.Candidates.Add(candidate.Id, candidate, reviewedRevision);
            P32State.instance.SetReq(Tag, new JObject { ["id"] = candidate.Id });
            var stage = S.Context.Candidates.Preview(entry);
            Save("stage.json", new JObject { ["ok"] = stage.Ok, ["diagnostics"] = new JArray(stage.AllDiagnostics.Select(StudioJson.ToToken)) });
            WorkflowPlayChecks.Require(stage.Ok, "Retained candidate refused: " + string.Join(" | ", stage.AllDiagnostics));
            WorkflowPlayChecks.Require(File.ReadAllBytes(Path.Combine(Repo, witness)).SequenceEqual(bytes)
                && File.ReadAllBytes(Path.Combine(Output, "candidate.json")).SequenceEqual(bytes), "Retained candidate bytes changed during review");
            var applied = S.Context.Candidates.Apply(entry);
            Save("apply.json", StudioJson.ToToken(applied.Entry));
            WorkflowPlayChecks.Require(applied.Ok, "Retained apply refused: " + string.Join(" | ", applied.Diagnostics));
            WorkflowPlayChecks.Require(Runtime.Journal.Read(candidate.Id)?.EffectiveState == ChangeSetState.Applied, "Applied journal missing");
        }

        private static JArray CatalogDiff(JArray historical, JArray current, string identity)
        {
            var before = historical.ToDictionary(item => (string)item[identity]!, item => item, StringComparer.Ordinal);
            var after = current.ToDictionary(item => (string)item[identity]!, item => item, StringComparer.Ordinal);
            var diff = new JArray();
            foreach (string id in before.Keys.Union(after.Keys).OrderBy(value => value, StringComparer.Ordinal))
            {
                before.TryGetValue(id, out JToken? oldSpec);
                after.TryGetValue(id, out JToken? newSpec);
                if (!JToken.DeepEquals(oldSpec, newSpec))
                    diff.Add(new JObject { ["id"] = id, ["before"] = oldSpec?.DeepClone(), ["after"] = newSpec?.DeepClone() });
            }
            return diff;
        }

        private static void CheckFailedResume(ChangeSet candidate, byte[] bytes, string witness)
        {
            ChangeSet? journal = Runtime.Journal.Read(candidate.Id);
            string? previous = Environment.GetEnvironmentVariable("GAMECORE_R9A_RESUME_FAILED");
            if (string.IsNullOrEmpty(previous))
            {
                WorkflowPlayChecks.Require(journal == null, "Retained candidate already has a journal; supply own failed-attempt evidence, never erase history");
                return;
            }
            previous = Path.GetFullPath(previous);
            string ownedRoot = Path.Combine(Repo, "artifacts/studio/verification", Mode == "npc" ? "W-AI-02" : "W-AI-03") + Path.DirectorySeparatorChar;
            WorkflowPlayChecks.Require(previous.StartsWith(ownedRoot, StringComparison.Ordinal)
                && Path.GetFileName(previous).StartsWith("r9-a-" + Mode + "-", StringComparison.Ordinal), "Resume evidence must be an owned same-lane R9-A attempt");
            JObject manifest = JObject.Parse(File.ReadAllText(Path.Combine(previous, "manifest.json")));
            JObject failure = JObject.Parse(File.ReadAllText(Path.Combine(previous, Mode + "-failure.json")));
            JObject stage = JObject.Parse(File.ReadAllText(Path.Combine(previous, "stage.json")));
            WorkflowPlayChecks.Require((string?)manifest["witness"] == witness && (string?)manifest["changeSetId"] == candidate.Id
                && (string?)manifest["witnessSha256"] == ContentStamp.Sha256Hex(bytes)
                && File.ReadAllBytes(Path.Combine(previous, "candidate.json")).SequenceEqual(bytes)
                && (string?)failure["status"] == "fail" && (bool?)stage["ok"] == false
                && !File.Exists(Path.Combine(previous, "apply.json")), "Previous attempt is not this unchanged candidate's failed pre-apply stage");
            var paths = ((JArray)manifest["paths"]!).Values<string>().Cast<string>().ToArray();
            AssertHashes(manifest["before"]!, Hashes(paths));
            foreach (string path in ((JArray)manifest["created"]!).Values<string>())
                WorkflowPlayChecks.Require(!File.Exists(path) && !File.Exists(path + ".meta"), "Previous attempt left created assets: " + path);
            if (journal != null)
            {
                WorkflowPlayChecks.Require((journal.EffectiveState == ChangeSetState.Candidate || journal.EffectiveState == ChangeSetState.Staged)
                    && (journal.Outcomes == null || journal.Outcomes.Count == 0)
                    && string.IsNullOrEmpty(journal.Timestamps?.Applied), "Resume refuses any potentially applied journal");
                var expected = (JObject)StudioJson.ToToken(candidate);
                var actual = (JObject)StudioJson.ToToken(journal);
                expected.Remove("state"); actual.Remove("state");
                expected.Remove("timestamps"); actual.Remove("timestamps");
                expected.Remove("outcomes"); actual.Remove("outcomes");
                WorkflowPlayChecks.Require(JToken.DeepEquals(expected, actual), "Existing pre-apply journal does not match retained candidate");
            }
            Save("resume-source.json", new JObject { ["previousAttempt"] = previous,
                ["manifestSha256"] = ContentStamp.Sha256Hex(File.ReadAllBytes(Path.Combine(previous, "manifest.json"))),
                ["journalState"] = journal?.EffectiveState.ToString(), ["unchangedBefore"] = Hashes(paths) });
        }

        private static void Tick()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            try
            {
                int phase = SessionState.GetInt(Phase, 0);
                if (Mode == "odd-reopen")
                {
                    History(true, "reopen-undo"); AssertRestored();
                    History(false, "reopen-redo"); BakeSave(); AssertHashes(Manifest()["after"]!, Hashes(Paths()));
                    History(true, "reopen-final-undo"); AssertRestored();
                    Finish("reopen-result.json"); return;
                }
                if (phase == 0)
                {
                    if (!WorkflowPlayChecks.Effect(Mode == "npc" ? "W-AI-02" : "W-AI-03", Tag).Run()) return;
                    var effect = JObject.Parse(File.ReadAllText(Path.Combine(Output, Tag, "play-effect.json")));
                    WorkflowPlayChecks.Require((string?)effect["status"] == "pass", "Real Play effect failed: " + effect);
                    SessionState.SetInt(Phase, 1);
                    return;
                }
                History(true, "undo"); AssertRestored();
                if (Mode == "odd")
                {
                    History(false, "redo-for-reopen"); BakeSave();
                    var manifest = Manifest(); manifest["after"] = Hashes(Paths()); Save("manifest.json", manifest);
                }
                Finish("result.json");
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Reopen()
        {
            var manifest = Manifest();
            WorkflowPlayChecks.Require((int)manifest["pid"]! != System.Diagnostics.Process.GetCurrentProcess().Id, "Reopen must use a different Editor process");
            WorkflowPlayChecks.Require(Runtime.Journal.Read(Id)?.EffectiveState == ChangeSetState.Applied, "Fresh Odd edit did not reopen with its Applied journal");
            AssertHashes(manifest["after"]!, Hashes(Paths()));
            Save("reopened.json", new JObject { ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id,
                ["journal"] = StudioJson.ToToken(Runtime.Journal.Read(Id)!), ["hashes"] = Hashes(Paths()) });
        }

        private static void History(bool undo, string name)
        {
            var result = undo ? Runtime.History.Undo(Id) : Runtime.History.Redo(Id);
            Save(name + ".json", new JObject { ["ok"] = result.Ok, ["state"] = result.State?.ToString(),
                ["diagnostics"] = new JArray(result.Diagnostics.Select(StudioJson.ToToken)),
                ["journal"] = StudioJson.ToToken(Runtime.Journal.Read(Id)!) });
            WorkflowPlayChecks.Require(result.Ok && result.State == (undo ? ChangeSetState.Undone : ChangeSetState.Applied), name + " refused");
        }

        private static string[] Paths() => ((JArray)Manifest()["paths"]!).Values<string>().Cast<string>().ToArray();
        private static JObject Hashes(string[] paths)
        {
            var result = new JObject();
            foreach (string path in paths) result[path] = ContentStamp.Sha256Hex(File.ReadAllBytes(path));
            return result;
        }
        private static void AssertHashes(JToken expected, JToken actual) => WorkflowPlayChecks.Require(JToken.DeepEquals(expected, actual), "Complete saved asset bytes differ: expected " + expected + "; actual " + actual);
        private static void AssertRestored()
        {
            BakeSave(); AssertHashes(Manifest()["before"]!, Hashes(Paths()));
            foreach (string path in ((JArray)Manifest()["created"]!).Values<string>())
                WorkflowPlayChecks.Require(!File.Exists(path) && !File.Exists(path + ".meta"), "Undo retained created asset " + path);
        }
        private static void BakeSave()
        {
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
            var result = Entry.Bake(AssetDatabase.LoadAssetAtPath<WorldDefinition>(World), BakePaths.ConventionFor(World), false);
            WorkflowPlayChecks.Require(result.Succeeded, "Production bake refused: " + result);
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveOpenScenes();
        }
        private static void Save(string path, JToken data) => File.WriteAllText(Path.Combine(Output, path), data.ToString() + "\n");
        private static void Finish(string path)
        {
            Save(path, new JObject { ["status"] = "pass", ["mode"] = Mode, ["changeSetId"] = Id,
                ["pid"] = System.Diagnostics.Process.GetCurrentProcess().Id });
            SessionState.SetBool(Active, false); EditorApplication.update -= Tick; EditorApplication.Exit(0);
        }
        private static void Fail(Exception error)
        {
            Directory.CreateDirectory(Output);
            Save(Mode + "-failure.json", new JObject { ["status"] = "fail", ["detail"] = error.ToString() });
            SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
            Debug.LogError(error); EditorApplication.Exit(1);
        }
    }
}
