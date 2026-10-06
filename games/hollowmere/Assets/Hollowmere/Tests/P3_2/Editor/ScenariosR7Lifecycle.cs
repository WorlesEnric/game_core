// R7-B: real Play -> authored apply -> delete -> stale apply, and a two-Editor prefab rename/save restore.
// Invoke EditLifecycle, PersistPrepare, then (in a NEW Editor) PersistReopen. Required: -r7Evidence <row/run>.
// Persistence also requires -saveDir <row/run/saves> in BOTH processes. Do not pass -quit: this driver exits.
#nullable enable
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Npc;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.Boot;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using AuthorScope = GameCore.Studio.Model.AuthorScope;
using Object = UnityEngine.Object;

namespace Hollowmere.P3_2.Workflows
{
    /// <summary>Offline acceptance; production boot, command, save, resolver, engine and History paths only.</summary>
    [InitializeOnLoad]
    public static class ScenariosR7Lifecycle
    {
        private const string StateKey = "Hollowmere.R7B.Lifecycle";
        private const string Village = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const string Slot = "slot-9";
        private static StudioRuntime Runtime => StudioServices.Runtime;
        private static JObject State
        {
            get => JObject.Parse(SessionState.GetString(StateKey, "{}"));
            set => SessionState.SetString(StateKey, value.ToString(Formatting.None));
        }

        static ScenariosR7Lifecycle()
        {
            if ((bool?)State["active"] == true) EditorApplication.update += Pump;
        }

        public static void EditLifecycle() => Begin("edit");
        public static void PersistPrepare() => Begin("prepare");
        public static void PersistReopen() => Begin("reopen");

        // Explicit recovery entry for an interrupted prepare/reopen; never emits a passing acceptance receipt.
        public static void RecoverPrefab()
        {
            string output = Argument("-r7Evidence");
            var state = JObject.Parse(File.ReadAllText(Path.Combine(output, "handoff.json")));
            state["output"] = output;
            RestorePrefab(state);
            Write(state, "recovery.json", new JObject { ["status"] = "recovered-not-acceptance" });
            EditorApplication.Exit(0);
        }

        private static void Begin(string mode)
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "entry requires Edit Mode");
            string output = Path.GetFullPath(Argument("-r7Evidence"));
            Directory.CreateDirectory(output);
            Require(!File.Exists(Path.Combine(output, mode + "-result.json")), "retain attempts: choose a fresh evidence directory");
            JObject state = mode == "reopen"
                ? JObject.Parse(File.ReadAllText(Path.Combine(output, "handoff.json"))) : new JObject();
            state["active"] = true;
            state["mode"] = mode;
            state["output"] = output;
            state["phase"] = "setup";
            state["deadline"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 300000;
            state["process"] = System.Diagnostics.Process.GetCurrentProcess().Id;
            state["originalScene"] = SceneManager.GetActiveScene().path;
            state["events"] = new JArray();
            State = state;
            EditorApplication.update -= Pump;
            EditorApplication.update += Pump;
        }

        private static void Pump()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            JObject state = R7LifecycleState.instance.Receipt ?? State;
            if ((bool?)state["active"] != true) return;
            try
            {
                string phase = Text(state, "phase");
                if (phase != "failed")
                    Require(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < (long)state["deadline"]!, "lifecycle phase timed out: " + phase);
                if (phase == "setup") Setup(state);
                else if (phase == "play")
                {
                    if (!EditorApplication.isPlaying) return;
                    var owner = R7LifecycleState.instance;
                    owner.Receipt = state;
                    owner.Probe ??= new ProbeExecution(Observe(state));
                    if (owner.Probe.MoveNext()) { State = state; return; }
                    owner.Probe = null;
                    owner.Receipt = null;
                    state["phase"] = "after-play";
                    State = state;
                    EditorApplication.ExitPlaymode();
                    return;
                }
                else if (phase == "after-play")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    if (Text(state, "mode") == "edit") ApplyDeleteApply(state);
                    else if (Text(state, "mode") == "prepare") RenameAndHandoff(state);
                    else RestorePrefab(state);
                    Finish(state, Text(state, "mode") == "prepare" ? "prepared" : "pass");
                    return;
                }
                else if (phase == "failed")
                {
                    if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                    Cleanup(state);
                    Finish(state, "fail");
                    return;
                }
                State = state;
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                R7LifecycleState.instance.Probe = null;
                R7LifecycleState.instance.Receipt = null;
                state["error"] = error.ToString();
                state["phase"] = "failed";
                // Cleanup has its own failure receipt instead of an endless update-loop retry.
                if (Text(State, "phase") == "failed")
                {
                    state["cleanupError"] = error.ToString();
                    Finish(state, "fail");
                    return;
                }
                State = state;
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }

        private static void Setup(JObject state)
        {
            if (Text(state, "mode") == "reopen")
            {
                Require((int)state["prepareProcess"]! != (int)state["process"]!, "reopen must be a separate Editor process");
                Require(Text(state, "prepareStatus") == "prepared", "prepare did not finish");
                CheckRenamedPrefab(state);
                Event(state, "separate-editor-reopened", new JObject { ["prepareProcess"] = state["prepareProcess"], ["reopenProcess"] = state["process"] });
            }
            else
            {
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                Runtime.Index.Rebuild();
                var roster = AssetDatabase.LoadAssetAtPath<NpcRoster>("Assets/Hollowmere/Npcs/NpcRoster.asset");
                Require(roster != null, "NPC roster missing");
                // Bram is a shipped Village NPC with a non-default authored costume tint.
                var npc = Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)
                    .Single(e => e.AuthoringId == "2825db71-11a1-4155-b1db-9e7734111d9e");
                Require(npc.Definition != null && npc.Definition.Prefab != null, "NPC prefab is missing");
                state["npcId"] = npc.AuthoringId;
                state["authoredRef"] = StudioJson.ToToken(Runtime.Resolver.BuildRef(npc, AuthorScope.Instance, true)!);
                state["definitionPath"] = AssetDatabase.GetAssetPath(npc.Definition);
                state["prefabOriginalPath"] = AssetDatabase.GetAssetPath(npc.Definition.Prefab);
                state["prefabGuid"] = AssetDatabase.AssetPathToGUID(Text(state, "prefabOriginalPath"));
                state["sceneHash"] = ContentStamp.Sha256Hex(File.ReadAllBytes(Village));
                state["cosmeticOverrides"] = JToken.FromObject(npc.Overrides.Entries);
                Require(npc.Overrides.Entries.Any(e => e.Field == "tint" && !string.IsNullOrEmpty(e.Value)), "NPC must have a real authored costume tint");
                if (Text(state, "mode") == "prepare")
                {
                    string path = Text(state, "prefabOriginalPath");
                    File.Copy(path, Path.Combine(Text(state, "output"), "prefab-before.bytes"), false);
                    File.Copy(path + ".meta", Path.Combine(Text(state, "output"), "prefab-before.meta"), false);
                    state["prefabBeforeHash"] = ContentStamp.Sha256Hex(File.ReadAllBytes(path));
                    state["prefabMetaBeforeHash"] = ContentStamp.Sha256Hex(File.ReadAllBytes(path + ".meta"));
                }
                Event(state, "authored-npc-baseline", new JObject { ["npcId"] = npc.AuthoringId, ["reference"] = state["authoredRef"] });
            }
            EditorSceneManager.OpenScene(BootScene, OpenSceneMode.Single);
            state["phase"] = "play";
            State = state;
            EditorApplication.EnterPlaymode();
        }

        private static IEnumerator Observe(JObject state)
        {
            yield return WorkflowPlayChecks.Until(() => Object.FindFirstObjectByType<GameBoot>()?.Saves != null, "real Hollowmere SaveService boot");
            yield return WorkflowPlayChecks.StartGame();
            GameBoot boot = Object.FindFirstObjectByType<GameBoot>();
            Require(boot != null && boot.World != null && boot.NpcExtension != null && boot.Saves != null, "game did not boot");
            var npc = boot.NpcExtension!.Records.Single(n => n.AuthoringId == Text(state, "npcId"));
            Require(Read(boot, npc.Target, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 1, "NPC is not alive");
            Event(state, "real-play-npc", new JObject { ["npcId"] = npc.AuthoringId, ["targetId"] = npc.Target.ToString(), ["world"] = boot.World!.Root.World.ToString(), ["isPlaying"] = Application.isPlaying });
            if (Text(state, "mode") == "reopen")
            {
                Require(Path.GetFullPath(boot.Saves!.Directory) == Path.Combine(Text(state, "output"), "saves"), "pass the same isolated -saveDir in both Editors");
                Require(ContentStamp.Sha256Hex(File.ReadAllBytes(boot.Saves.DocumentPath(Slot))) == Text(state, "saveDocumentHash"), "saved checkpoint bytes changed across Editor restart");
                var restored = boot.Saves.Restore(Slot);
                Require(restored.Succeeded, "production restore refused: " + restored);
                Require(restored.SlotHash == Text(state, "savedSlotHash"), "restored canonical slot hash differs from captured save");
                Require(!restored.SourceWorld.Equals(restored.RestoredWorld), "restore did not build a fresh world");
                JObject after = NpcState(boot, npc.Target);
                Require(JToken.DeepEquals(after, state["savedNpcState"]), "NPC state differs immediately after production restore");
                CheckRenamedPrefab(state);
                yield return WorkflowPlayChecks.Until(() => Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(e => e.AuthoringId == Text(state, "npcId")), "restored NPC authored costume content");
                var authored = Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(e => e.AuthoringId == Text(state, "npcId"));
                Require(JToken.DeepEquals(JToken.FromObject(authored.Overrides.Entries), state["cosmeticOverrides"]), "NPC authored costume content changed after rename/reopen");
                Event(state, "checkpoint-restored", new JObject { ["slotHash"] = restored.SlotHash, ["npcState"] = after, ["cosmeticOverrides"] = state["cosmeticOverrides"], ["newWorld"] = restored.RestoredWorld.ToString() });
                yield break;
            }
            var commands = new NpcCommands(boot.World!);
            int mood = Read(boot, npc.Target, NpcSlots.Owner, NpcSlots.Mood) == 1 ? 2 : 1;
            Require(commands.SetMood(npc.Target, mood).Admitted, "live NPC mood command refused");
            yield return WorkflowPlayChecks.Until(() => Read(boot, npc.Target, NpcSlots.Owner, NpcSlots.Mood) == mood, "running NPC edit committed");
            Event(state, "running-npc-edited", new JObject { ["mood"] = mood });
            if (Text(state, "mode") == "edit")
            {
                yield return WorkflowPlayChecks.Until(() => Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(e => e.AuthoringId == npc.AuthoringId), "running NPC authored proxy");
                var authored = Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(e => e.AuthoringId == npc.AuthoringId);
                var target = Runtime.Resolver.BuildRef(authored, AuthorScope.Instance, true)!;
                Require(target.AuthoringId == npc.AuthoringId && IdDerivation.TargetIdFor(target.AuthoringId).Equals(npc.Target), "live NPC identity differs from the authored selection");
                var candidate = Override(target, "#336699");
                Runtime.Index.Rebuild();
                var staged = Runtime.Engine.Stage(candidate);
                Write(state, "staged-in-play-diagnostics.json", JToken.FromObject(staged.AllDiagnostics));
                Require(staged.Ok, "real NPC edit did not stage in Play: " + string.Join("; ", staged.AllDiagnostics.Select(d => d.Code + ": " + d.Message)));
                candidate = staged.ChangeSet;
                state["candidate"] = StudioJson.ToToken(candidate);
                Write(state, "planned-in-play.json", StudioJson.ToToken(candidate));
                Event(state, "authored-edit-planned-in-play", new JObject { ["changeSetId"] = candidate.Id, ["reference"] = StudioJson.ToToken(target) });
                yield break;
            }
            Require(Path.GetFullPath(boot.Saves!.Directory) == Path.Combine(Text(state, "output"), "saves"), "persistence requires isolated -saveDir <evidence>/saves");
            Require(!boot.Saves.Exists(Slot), "isolated save slot already exists; do not overwrite previous evidence");
            // Visibility is cosmetic slot state, changed by the production entity command path, not direct slot writes.
            Require(boot.World!.Commands.Despawn(npc.Target).Admitted, "NPC despawn refused");
            yield return WorkflowPlayChecks.Until(() => Read(boot, npc.Target, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 0, "NPC despawn committed");
            Require(boot.World.Commands.Spawn(npc.Target, false).Admitted, "NPC invisible respawn refused");
            yield return WorkflowPlayChecks.Until(() => Read(boot, npc.Target, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 1
                && Read(boot, npc.Target, GameplaySlots.EntityOwner, GameplaySlots.Visible) == 0, "cosmetic visibility committed");
            JObject savedNpc = NpcState(boot, npc.Target);
            var captured = boot.Saves.Capture(Slot);
            Require(captured.Succeeded, "production save refused: " + captured);
            state["savedNpcState"] = savedNpc;
            state["savedSlotHash"] = captured.SlotHash;
            state["saveDocumentHash"] = ContentStamp.Sha256Hex(File.ReadAllBytes(boot.Saves.DocumentPath(Slot)));
            Event(state, "checkpoint-captured-before-rename", new JObject { ["slotHash"] = captured.SlotHash, ["npcState"] = savedNpc, ["documentHash"] = state["saveDocumentHash"] });
        }

        private static void ApplyDeleteApply(JObject state)
        {
            Require(!Application.isPlaying, "authored apply must follow exit Play");
            Event(state, "exited-play-before-apply", new JObject { ["isPlaying"] = Application.isPlaying });
            EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
            Runtime.Index.Rebuild();
            var candidate = StudioJson.Deserialize<ChangeSet>(state["candidate"]!.ToString());
            var applied = Runtime.Engine.Apply(candidate);
            RecordApply(state, "after-exit-apply", applied);
            Require(applied.Ok && applied.Journaled, "candidate selected in Play did not apply after exit");
            state["editId"] = applied.Entry.Id;
            State = state;
            var entity = Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Single(e => e.AuthoringId == Text(state, "npcId"));
            Require(entity.Overrides.Entries.Any(e => e.Field == "tint" && e.Value == "#336699"), "first apply did not change NPC costume");
            var pending = Runtime.Engine.Stage(Override(Runtime.Resolver.BuildRef(entity, AuthorScope.Instance, true)!, "#996633"));
            Require(pending.Ok, "second NPC edit must be valid before deletion");
            var deletion = Runtime.Engine.Apply(StudioRuntime.Single("R7 W-EDIT-04 delete selected NPC", IntentOrigin.Manual,
                new Operation("delete-npc", "delete", Runtime.Resolver.BuildRef(entity, AuthorScope.Instance, true))));
            RecordApply(state, "delete", deletion);
            Require(deletion.Ok && deletion.Journaled, "NPC delete did not apply");
            state["deleteId"] = deletion.Entry.Id;
            State = state;
            Require(!Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Any(e => e.AuthoringId == Text(state, "npcId")), "deleted NPC still exists");
            var refused = Runtime.Engine.Apply(pending);
            RecordApply(state, "after-delete-apply", refused);
            Require(!refused.Ok && refused.Outcomes.Count == 1 && refused.Outcomes[0].Status == OutcomeStatus.Refused
                && refused.Outcomes[0].Code == DiagnosticCodes.StaleTarget, "deleted target apply must refuse specifically StaleTarget");
            Require(refused.Journaled && Runtime.Journal.Read(refused.Entry.Id)?.Outcomes?.Single().Code == DiagnosticCodes.StaleTarget,
                "StaleTarget refusal was not retained in History");
            Require(!Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Any(e => e.AuthoringId == Text(state, "npcId")), "refused apply resurrected the deleted NPC");
            Cleanup(state);
        }

        private static void RenameAndHandoff(JObject state)
        {
            string original = Text(state, "prefabOriginalPath");
            string renamed = Path.GetDirectoryName(original)!.Replace('\\', '/') + "/R7B_RenamedNpc.prefab";
            Require(!File.Exists(renamed), "rename destination already exists");
            state["prefabRenamedPath"] = renamed;
            state["prepareProcess"] = state["process"];
            state["prepareStatus"] = "renaming";
            // Durable preimage and intended paths precede the filesystem mutation; recovery is explicit.
            Write(state, "handoff.json", state);
            string problem = AssetDatabase.RenameAsset(original, "R7B_RenamedNpc");
            Require(string.IsNullOrEmpty(problem), "literal prefab rename failed: " + problem);
            CheckRenamedPrefab(state);
            state["prepareStatus"] = "prepared";
            Event(state, "literal-prefab-renamed", new JObject { ["from"] = original, ["to"] = renamed, ["guid"] = state["prefabGuid"] });
            Write(state, "handoff.json", state);
        }

        private static void CheckRenamedPrefab(JObject state)
        {
            string renamed = Text(state, "prefabRenamedPath");
            Require(!File.Exists(Text(state, "prefabOriginalPath")) && File.Exists(renamed), "literal renamed prefab path did not survive reopen");
            Require(AssetDatabase.GUIDToAssetPath(Text(state, "prefabGuid")) == renamed, "prefab GUID changed or resolves to the old path");
            var definition = AssetDatabase.LoadAssetAtPath<EntityDefinition>(Text(state, "definitionPath"));
            Require(definition != null && definition.Prefab != null && AssetDatabase.GetAssetPath(definition.Prefab) == renamed,
                "NPC definition did not load the renamed prefab");
        }

        private static void RestorePrefab(JObject state)
        {
            string original = Text(state, "prefabOriginalPath");
            string current = AssetDatabase.GUIDToAssetPath(Text(state, "prefabGuid"));
            Require(!string.IsNullOrEmpty(current), "cannot recover prefab: GUID missing");
            if (current != original)
                Require(string.IsNullOrEmpty(AssetDatabase.MoveAsset(current, original)), "cannot restore prefab path");
            File.Copy(Path.Combine(Text(state, "output"), "prefab-before.bytes"), original, true);
            File.Copy(Path.Combine(Text(state, "output"), "prefab-before.meta"), original + ".meta", true);
            AssetDatabase.ImportAsset(original, ImportAssetOptions.ForceUpdate);
            Require(ContentStamp.Sha256Hex(File.ReadAllBytes(original)) == Text(state, "prefabBeforeHash")
                && ContentStamp.Sha256Hex(File.ReadAllBytes(original + ".meta")) == Text(state, "prefabMetaBeforeHash"), "prefab cleanup did not restore exact original bytes/meta");
            Event(state, "prefab-restored-exactly", new JObject { ["prefabHash"] = state["prefabBeforeHash"], ["metaHash"] = state["prefabMetaBeforeHash"] });
        }

        private static void Cleanup(JObject state)
        {
            if (Text(state, "mode") == "edit")
            {
                foreach (string key in new[] { "deleteId", "editId" })
                {
                    string id = Text(state, key);
                    if (id.Length == 0) continue;
                    var entry = Runtime.Journal.Read(id);
                    if (entry?.EffectiveState != ChangeSetState.Applied) continue;
                    var result = Runtime.History.Undo(id);
                    Require(result.Ok, "normal History undo failed for " + key + ": " + string.Join("; ", result.Diagnostics.Select(d => d.Code + ": " + d.Message)));
                    Write(state, key + "-undone.json", StudioJson.ToToken(Runtime.Journal.Read(id)!));
                }
                if (Text(state, "npcId").Length == 0) return;
                if (Text(state, "editId").Length > 0)
                {
                    var restored = Object.FindObjectsByType<AuthoredEntity>(FindObjectsInactive.Include, FindObjectsSortMode.None).Single(e => e.AuthoringId == Text(state, "npcId"));
                    Require(JToken.DeepEquals(JToken.FromObject(restored.Overrides.Entries), state["cosmeticOverrides"]), "normal History undo did not restore the original NPC costume");
                }
                EditorSceneManager.OpenScene(Village, OpenSceneMode.Single);
                Require(ContentStamp.Sha256Hex(File.ReadAllBytes(Village)) == Text(state, "sceneHash"), "acceptance changed original scene bytes");
                var npc = Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Single(e => e.AuthoringId == Text(state, "npcId"));
                Require(JToken.DeepEquals(JToken.FromObject(npc.Overrides.Entries), state["cosmeticOverrides"]), "cleanup did not restore the original NPC costume");
                Event(state, "normal-history-undo-restored-npc", new JObject { ["sceneHash"] = state["sceneHash"] });
            }
            else if (File.Exists(Path.Combine(Text(state, "output"), "prefab-before.bytes"))) RestorePrefab(state);
        }

        private static ChangeSet Override(AuthoringRef target, string tint) => StudioRuntime.Single(
            "R7 W-EDIT-04 queued NPC costume edit", IntentOrigin.Manual,
            new Operation("npc-tint", "entity.applyOverride", target, new JObject { ["field"] = "tint", ["value"] = tint }));

        private static JObject NpcState(GameBoot boot, TargetId target) => new JObject
        {
            ["alive"] = Read(boot, target, GameplaySlots.EntityOwner, GameplaySlots.Alive),
            ["visible"] = Read(boot, target, GameplaySlots.EntityOwner, GameplaySlots.Visible),
            ["variant"] = Read(boot, target, GameplaySlots.EntityOwner, GameplaySlots.Variant),
            ["scaleMilli"] = Read(boot, target, GameplaySlots.EntityOwner, GameplaySlots.ScaleMilli),
            ["mood"] = Read(boot, target, NpcSlots.Owner, NpcSlots.Mood),
            ["patrolIndex"] = Read(boot, target, NpcSlots.Owner, NpcSlots.PatrolIndex),
            ["posX"] = Read(boot, target, GameplaySlots.WorldOwner, GameplaySlots.PosX),
            ["posZ"] = Read(boot, target, GameplaySlots.WorldOwner, GameplaySlots.PosZ)
        };

        private static int Read(GameBoot boot, TargetId target, OwnerId owner, SlotId slot)
        {
            Require(boot.World!.Slots.TryRead(target, owner, slot, out int value), "missing committed NPC slot " + slot);
            return value;
        }

        private static void RecordApply(JObject state, string label, ApplyReport report)
        {
            Write(state, label + ".json", StudioJson.ToToken(report.Entry));
            Write(state, label + "-diagnostics.json", JToken.FromObject(report.Diagnostics));
            Event(state, label, new JObject { ["changeSetId"] = report.Entry.Id, ["state"] = report.State.ToString(), ["journaled"] = report.Journaled });
        }

        private static void Finish(JObject state, string status)
        {
            state["active"] = false;
            state["status"] = status;
            state["finishedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            State = state;
            Write(state, Text(state, "mode") + "-result.json", state);
            EditorApplication.update -= Pump;
            EditorApplication.Exit(status == "fail" ? 1 : 0);
        }

        private static void Event(JObject state, string name, JObject data)
        {
            data["event"] = name;
            data["utc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            ((JArray)state["events"]!).Add(data);
            Write(state, Text(state, "mode") + "-events.json", state["events"]!);
        }

        private static void Write(JObject state, string file, JToken data) => File.WriteAllText(Path.Combine(Text(state, "output"), file), data.ToString(Formatting.Indented) + "\n");
        private static string Text(JObject state, string key) => (string?)state[key] ?? string.Empty;
        private static void Require(bool condition, string message) => WorkflowPlayChecks.Require(condition, message);
        private static string Argument(string flag)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, flag);
            Require(index >= 0 && index + 1 < args.Length, "required argument: " + flag);
            return args[index + 1];
        }
    }

    public sealed class R7LifecycleState : ScriptableSingleton<R7LifecycleState>
    {
        [NonSerialized] public ProbeExecution? Probe;
        [NonSerialized] public JObject? Receipt;
    }
}
