#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Quest;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Quest;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Hollowmere.Boot;
using Hollowmere.Narrative;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Hollowmere.P3_2.Workflows
{
    /// <summary>Observations of the real boot and committed gameplay state. No candidate repairs.</summary>
    public static class WorkflowPlayChecks
    {
        public const string QuestPath = "Assets/Hollowmere/Quests/DrownedBell.asset";
        public const string OddPath = "Assets/Hollowmere/Dialogue/Graphs/Odd.asset";
        private static P32State State => P32State.instance;

        public static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException(detail);
        }

        public static string OilFlaskIdentity(StudioRuntime runtime)
        {
            runtime.Index.Rebuild();
            var nodes = runtime.Index.Snapshot().Nodes.Where(n => n.Type == "inventory.item" &&
                (n.Name == "OilFlask" || n.Name == "Oil Flask")).ToArray();
            Require(nodes.Length == 1 && !string.IsNullOrEmpty(nodes[0].Ref.AuthoringId),
                "OilFlask must resolve to exactly one indexed inventory.item identity");
            return nodes[0].Ref.AuthoringId!;
        }

        public static string CheckedToolOutput(Func<object?> invoke)
        {
            string output = invoke()?.ToString() ?? throw new InvalidOperationException("verification tool returned no result");
            Require(!output.Contains("GP-QST-004") && !output.Contains("refused", StringComparison.OrdinalIgnoreCase)
                && !output.Contains("error:", StringComparison.OrdinalIgnoreCase), output);
            return output;
        }

        public static string SimulateQuest(StudioRuntime runtime, string path)
        {
            var quest = AssetDatabase.LoadMainAssetAtPath(QuestPath);
            var target = runtime.Resolver.BuildRef(quest, GameCore.Studio.Model.AuthorScope.Definition, true);
            var result = runtime.Registry.Invoke("quest.simulate", target, new JObject { ["path"] = path });
            Require(result.Status == OutcomeStatus.Applied, result.Code + ": " + result.Detail);
            return CheckedToolOutput(() => result.Output);
        }

        public static void CaptureNpcBaseline()
        {
            State.Set("npc-before-ids", new JArray(UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None).Select(e => e.AuthoringId)));
        }

        public static void CaptureDialogueBaseline()
        {
            var graph = AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(OddPath);
            State.Set("odd-before-texts", new JArray(graph.Nodes.Select(n => n.text)));
        }

        public static Step Effect(string row, string tag) => new Step("Play effect " + row, () => Tick(row, tag));

        private static bool Tick(string row, string tag)
        {
            string key = "play-effect." + tag;
            var receipt = State.Get(key) as JObject;
            if (receipt == null)
            {
                receipt = new JObject { ["row"] = row, ["status"] = "pending", ["scene"] = SceneManager.GetActiveScene().path,
                    ["deadline"] = WorkflowRunner.NowMs + 300000, ["phase"] = "enter" };
                State.Set(key, receipt);
                State.PlayError = string.Empty;
                try
                {
                    var candidate = S.EntryOf(tag);
                    Require(candidate != null && candidate.Stage == CandidateStage.Applied, row + ": candidate was not applied");
                    receipt["changeSetId"] = candidate!.Id;
                    if (row == "W-AI-02")
                    {
                        var before = ((JArray?)State.Get("npc-before-ids") ?? throw new InvalidOperationException("missing NPC baseline")).Values<string>().ToArray();
                        var ids = UnityEngine.Object.FindObjectsByType<AuthoredEntity>(FindObjectsSortMode.None)
                            .Where(e => !before.Contains(e.AuthoringId)).ToArray();
                        Require(ids.Length == 1, "exactly one newly applied NPC must exist");
                        S.Context.Runtime.Index.Rebuild();
                        Require(S.Context.Runtime.Index.Snapshot().Nodes.Any(n => n.Ref.AuthoringId == ids[0].AuthoringId), "created NPC is absent from the index");
                        receipt["npcId"] = ids[0].AuthoringId;
                    }
                    if (row == "W-AI-03")
                    {
                        var before = ((JArray?)State.Get("odd-before-texts") ?? throw new InvalidOperationException("missing dialogue baseline")).Values<string>().ToArray();
                        var graph = AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(OddPath);
                        var added = graph.Nodes.Select(n => n.text).Where(t => !string.IsNullOrEmpty(t) && !before.Contains(t)).ToArray();
                        Require(graph.Nodes.Count > before.Length && added.Length > 0, "candidate added no identifiable dialogue line");
                        receipt["newLines"] = new JArray(added);
                    }
                    if (row == "W-AI-05") receipt["oilId"] = OilFlaskIdentity(S.Context.Runtime);
                    State.Set(key, receipt);
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.SaveOpenScenes();
                    var world = AssetDatabase.LoadAssetAtPath<WorldDefinition>("Assets/Hollowmere/World/Hollowmere.asset");
                    var bake = Entry.Bake(world, BakePaths.ConventionFor(AssetDatabase.GetAssetPath(world)), false);
                    Require(bake.Succeeded, "cannot observe applied content: " + bake);
                    receipt["phase"] = "baked";
                    State.Set(key, receipt);
                    AssetDatabase.Refresh();
                    return false;
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    receipt["status"] = "fail";
                    receipt["detail"] = error.GetBaseException().Message;
                    receipt["phase"] = "exit";
                }
            }
            try
            {
                if ((string?)receipt["phase"] == "baked")
                {
                    if (EditorApplication.isCompiling || EditorApplication.isUpdating) return false;
                    receipt["phase"] = "enter";
                    State.Set(key, receipt);
                    EditorSceneManager.OpenScene(S.BootScene, OpenSceneMode.Single);
                    EditorApplication.EnterPlaymode();
                    return false;
                }
                if ((string?)receipt["phase"] != "exit")
                {
                    Require(WorkflowRunner.NowMs < (long)receipt["deadline"]!, row + ": Play observation timed out");
                    if (!EditorApplication.isPlaying) return false;
                    if (State.PlayProbe == null)
                    {
                        State.PlayProbe = new ProbeExecution(Observe(row, receipt));
                        Application.logMessageReceived += State.ObservePlayLog;
                    }
                    Require(string.IsNullOrEmpty(State.PlayError), "Play exception: " + State.PlayError);
                    if (State.PlayProbe.MoveNext()) return false;
                    receipt["status"] = "pass";
                    receipt["phase"] = "exit";
                }
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                receipt["status"] = "fail";
                receipt["detail"] = error.GetBaseException().Message;
                receipt["phase"] = "exit";
            }
            Application.logMessageReceived -= State.ObservePlayLog;
            State.PlayProbe = null;
            State.Set(key, receipt);
            WorkflowRunner.Json(tag + "/play-effect.json", receipt);
            if ((string?)receipt["status"] != "pass") WorkflowRunner.MarkFailed(row + ": " + receipt["detail"]);
            if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return false; }
            string scene = (string?)receipt["scene"] ?? string.Empty;
            if (scene.Length > 0 && SceneManager.GetActiveScene().path != scene) EditorSceneManager.OpenScene(scene, OpenSceneMode.Single);
            return true;
        }

        private static IEnumerator Observe(string row, JObject receipt)
        {
            yield return Until(() => UnityEngine.Object.FindFirstObjectByType<GameBoot>()?.Narrative != null, "real Hollowmere boot");
            yield return StartGame();
            var boot = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
            Require(boot != null && boot.Narrative != null && boot.Modules != null, "Hollowmere boot unavailable");
            if (row == "W-AI-02") yield return Npc(boot!, (string)receipt["npcId"]!, receipt);
            else if (row == "W-AI-03") yield return Dialogue(boot!, ((JArray)receipt["newLines"]!).Values<string>().ToArray()!, receipt);
            else if (row == "W-AI-05") yield return Quest(boot!, (string)receipt["oilId"]!, receipt);
            else throw new InvalidOperationException("unknown Play row " + row);
        }

        public static IEnumerator StartGame()
        {
            var rig = UnityEngine.Object.FindFirstObjectByType<Hollowmere.UiAudio.HollowmereUiAudio>();
            Require(rig != null, "real game UI is missing");
            Require(rig!.Ui.Dispatcher.Dispatch("newgame").Accepted, "newgame refused");
            // HUD commits before host actions settle. Reacquire owners until the current boot is stable.
            int owner = 0;
            int stable = 0;
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (stable < 6)
            {
                var current = UnityEngine.Object.FindFirstObjectByType<GameBoot>();
                var currentRig = UnityEngine.Object.FindFirstObjectByType<Hollowmere.UiAudio.HollowmereUiAudio>();
                bool ready = current != null && current.Narrative != null && currentRig != null
                    && currentRig.Ui.Screen == GameCore.Rules.Gameplay.Ui.UiScreen.Hud;
                int id = ready ? current!.GetInstanceID() : 0;
                stable = ready && id == owner ? stable + 1 : 0;
                owner = id;
                Require(Time.realtimeSinceStartupAsDouble < deadline,
                    "newgame did not settle: boot=" + (current == null ? "absent" : current.Failure)
                    + "; screen=" + (currentRig == null ? "absent" : currentRig.Ui.Screen.ToString()));
                yield return null;
            }
        }

        public static IEnumerator Until(Func<bool> condition, string effect, double seconds = 30)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!condition())
            {
                Require(Time.realtimeSinceStartupAsDouble < deadline, "Play effect absent: " + effect);
                yield return null;
            }
        }

        public static IEnumerator Npc(GameBoot boot, string authoringId, JObject receipt)
        {
            Require(Application.isPlaying, "NPC observation requires Play");
            var npc = boot.NpcExtension!.Records.Single(n => n.AuthoringId == authoringId);
            Require(npc.Route.Count > 1 && !string.IsNullOrEmpty(npc.DialogueGraph), "applied NPC needs patrol and dialogue");
            var world = boot.World!;
            Require(world.Views != null && world.Views.IsActive, "NPC navigation evidence requires graphical Play; headless views are inactive");
            yield return Until(() => world.Views != null && world.Views.TryGetView(npc.Target, out _), "NPC view");
            world.Views!.TryGetView(npc.Target, out var view);
            yield return Until(() => view!.GetComponent<NavMeshAgent>() is NavMeshAgent agent && agent.enabled && agent.isOnNavMesh,
                "NPC navigation on the real NavMesh");
            var nav = view!.GetComponent<NavMeshAgent>();
            Vector3 initial = view.transform.position;
            yield return NpcMotion(boot, authoringId, receipt);
            yield return Until(() => Vector3.Distance(view.transform.position, initial) > 0.1f,
                "NPC patrol moves its NavMesh view");
            Require(nav.isOnNavMesh, "NPC left NavMesh during patrol");
            receipt["npcId"] = authoringId;
            receipt["motionObserved"] = true;
            receipt["onNavMesh"] = true;
            var lines = new List<string>();
            yield return Converse(boot, npc.AuthoringId, npc.DialogueGraph, lines, null);
            Require(lines.Any(line => line.Contains("bell", StringComparison.OrdinalIgnoreCase)), "the new NPC never talks about the bell");
            receipt["dialogueLines"] = new JArray(lines);
        }

        public static IEnumerator NpcMotion(GameBoot boot, string authoringId, JObject receipt)
        {
            Require(Application.isPlaying, "NPC motion observation requires Play");
            var npc = boot.NpcExtension!.Records.Single(n => n.AuthoringId == authoringId);
            var world = boot.World!;
            Require(NpcCommands.TryPosition(world.Slots, npc.Target, out int x, out int z), "NPC committed pose missing");
            yield return Until(() => NpcCommands.TryPosition(world.Slots, npc.Target, out int nx, out int nz)
                && Math.Abs(nx - x) + Math.Abs(nz - z) > 100, "NPC patrol changes committed pose");
            receipt["committedMotionObserved"] = true;
        }

        public static IEnumerator Dialogue(GameBoot boot, string[] added, JObject receipt)
        {
            Require(Application.isPlaying && added.Length > 0, "dialogue observation needs Play and the candidate's added line");
            var runner = boot.Modules!.Dialogue.Runner!;
            var seen = new List<string>();
            for (int lit = 0; lit <= 1; lit++)
            {
                int value = lit;
                Require(runner.SetFact("shrine_lit", value, 0), "shrine_lit command refused");
                yield return Until(() => boot.Narrative!.Runtime.Models.TryGetFactByName("shrine_lit", out var fact)
                    && fact != null && boot.Narrative.Runtime.State.Fact(fact.Key) == value, "committed shrine fact");
                seen.Clear();
                yield return Converse(boot, HollowmereNarrative.OddId, AssetDatabase.LoadAssetAtPath<DialogueGraphDefinition>(OddPath).AuthoringId, seen, "Not yet.");
                bool found = added.Any(t => seen.Contains(t));
                Require(found == (lit == 1), lit == 0 ? "new line leaked while shrine unlit" : "new line never displayed while shrine lit");
                receipt[lit == 0 ? "unlitLines" : "litLines"] = new JArray(seen);
            }
        }

        private static IEnumerator Converse(GameBoot boot, string npcId, string graphId, List<string> seen, string? exitChoice)
        {
            var runner = boot.Modules!.Dialogue.Runner!;
            Require(runner.Start(npcId, graphId), "dialogue start refused");
            yield return Until(() => boot.Modules.Dialogue.Presenter!.Last.Active, "conversation started");
            for (int step = 0; step < 64 && boot.Modules.Dialogue.Presenter!.Last.Active; step++)
            {
                yield return Until(() => !boot.Modules.Dialogue.Presenter!.Last.Active || boot.Modules.Dialogue.Presenter.Last.CanAdvance
                    || boot.Modules.Dialogue.Presenter.Last.Kind == "choice", "presented dialogue line or choice");
                var shown = boot.Modules.Dialogue.Presenter!.Last;
                if (!shown.Active) break;
                if (!string.IsNullOrEmpty(shown.Text)) seen.Add(shown.Text);
                int node = shown.Node;
                if (shown.Kind == "choice")
                {
                    var leave = shown.Choices.LastOrDefault(c => c.Available && (exitChoice == null || c.Text == exitChoice));
                    Require(leave != null && runner.Choose(leave.Index), "authored exit choice unavailable");
                }
                else
                {
                    yield return Until(() => boot.Modules.Dialogue.Presenter!.Last.CanAdvance, "dialogue can advance");
                    Require(runner.Advance(), "dialogue advance refused");
                }
                yield return Until(() => !boot.Modules.Dialogue.Presenter!.Last.Active || boot.Modules.Dialogue.Presenter.Last.Node != node, "next dialogue node");
            }
            Require(!boot.Modules.Dialogue.Presenter!.Last.Active, "conversation did not end");
        }

        public static IEnumerator Quest(GameBoot boot, string oilId, JObject receipt)
        {
            Require(Application.isPlaying, "quest consequence requires Play");
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(QuestPath);
            var oil = quest.Objectives.Where(o => o.kind == ObjectiveKind.Collect && o.target is ItemDefinition item && item.AuthoringId == oilId).ToArray();
            Require(oil.Length == 1 && oil[0].required == 2 && oil[0].stage == 1 && oil[0].branch == 0,
                "applied quest must require two indexed oil flasks in stage 1");
            var rt = boot.Narrative!.Runtime;
            Require(rt.Models.TryResolve(quest.AuthoringId, out int key), "quest missing from real runtime");
            TargetId target = rt.Index.TargetOf(NarrativeTargetKind.Quest, key);
            Require(rt.Submitter.Submit(QuestIds.StartRoute, target, QuestIds.StartCommand, NarrativeCommands.QuestStart(0)).Admitted, "quest start refused");
            Require(boot.Modules!.Dialogue.Runner!.SetFact("heard_rumour", 1, 0), "rumour setup refused");
            yield return Until(() => boot.Modules.Quest.TryRead(key, out var state) && state!.Stage == 1, "quest reaches oil stage");
            var inventory = boot.Modules.Inventory.Commands!;
            Require(inventory.Grant(HollowmereNarrative.Lantern, 1).Admitted, "lantern prerequisite refused");
            Require(inventory.Grant(oilId, 1).Admitted, "first oil flask refused");
            Require(rt.Models.TryResolve(oilId, out int itemKey), "indexed oil identity absent from runtime");
            yield return Until(() => rt.State.ItemCount(0, itemKey, rt.ActorKey) == 1, "first oil flask committed");
            // Allow the real quest tracker several pumps to consume inventory changes.
            for (int i = 0; i < 10; i++) yield return null;
            Require(boot.Modules.Quest.TryRead(key, out var one) && one!.Stage == 1, "one flask incorrectly advances the quest");
            Require(inventory.Grant(oilId, 1).Admitted, "second oil flask refused");
            yield return Until(() => boot.Modules.Quest.TryRead(key, out var state) && state!.Stage == 2, "second flask advances quest consequence");
            receipt["oilId"] = oilId;
            receipt["stageAfterOne"] = 1;
            receipt["stageAfterTwo"] = 2;
        }
    }

    /// <summary>Runs the same nested observation coroutines from the interactive step driver.</summary>
    public sealed class ProbeExecution
    {
        private readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        public ProbeExecution(IEnumerator probe) { stack.Push(probe); }
        public bool MoveNext()
        {
            while (stack.Count > 0)
            {
                var top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                if (top.Current is IEnumerator nested) { stack.Push(nested); continue; }
                return true;
            }
            return false;
        }
    }
}
