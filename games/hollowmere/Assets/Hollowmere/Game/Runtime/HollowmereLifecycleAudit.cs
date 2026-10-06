// Read-only standalone acceptance observer. All gameplay is driven by the normal autoplay/UI paths.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;

namespace Hollowmere.Game
{
    public sealed class HollowmereLifecycleAudit : MonoBehaviour
    {
        private readonly string[] Stages =
        {
            "menu", "newgame", "paused", "saved", "quit-ready", "menu-relaunched", "loaded", "ending", "restarted",
        };

        [Serializable]
        public sealed class State
        {
            public string region = string.Empty;
            public Vector3 position;
            public int quest, stage, branch, outcome, lantern, clapper, coins;
            public int rumour, gate, shrine, bell, ending;

            public static State Read(HollowmereGame game)
            {
                HollowmereDirector d = game.Director!;
                return new State
                {
                    region = game.CurrentRegionId(), position = game.PlayerPosition(),
                    quest = d.QuestStatus(), stage = d.QuestStage(), branch = d.QuestBranch(), outcome = d.Outcome,
                    lantern = d.ItemCount("Lantern"), clapper = d.ItemCount("BellClapper"), coins = d.ItemCount("OldCoin"),
                    rumour = d.Fact("heard_rumour"), gate = d.Fact("gate_open"), shrine = d.Fact("shrine_lit"),
                    bell = d.Fact("bell_rung"), ending = d.Fact("ending_c"),
                };
            }

            public bool Matches(State other) => region == other.region && Vector3.Distance(position, other.position) <= 0.1f
                && quest == other.quest && stage == other.stage && branch == other.branch && outcome == other.outcome
                && lantern == other.lantern && clapper == other.clapper && coins == other.coins
                && rumour == other.rumour && gate == other.gate && shrine == other.shrine && bell == other.bell && ending == other.ending;

            public bool Fresh => rumour == 0 && gate == 0 && shrine == 0 && bell == 0 && ending == 0
                && outcome == 0 && clapper == 0 && lantern == 0 && quest != QuestIds.Completed && quest != QuestIds.Failed;
        }

        [Serializable]
        public sealed class Stage
        {
            public string name = string.Empty, utc = string.Empty, screen = string.Empty;
            public int frame, attaches, restores;
            public double seconds;
            public State state = new State();
        }

        [Serializable]
        public sealed class Report
        {
            public string row = "W-GAME-05", revision = string.Empty, status = "RUNNING", failure = string.Empty;
            public string saveSlotHash = string.Empty;
            public bool saveProcessQuitObserved, finalProcessQuitObserved;
            public State? saved;
            public string platform = Application.platform.ToString();
#if ENABLE_IL2CPP
            public string backend = "IL2CPP";
#else
            public string backend = "Mono";
#endif
            public List<Stage> stages = new List<Stage>();
        }

        private HollowmerePersistentSession session = null!;
        private Report report = null!;
        private string path = string.Empty;
        private string waiting = string.Empty;
        private int endingAttaches;
        private bool initialized;

        public static bool Check(HollowmereGame game, string stage, out string detail)
        {
            if (game.Session == null || string.IsNullOrEmpty(game.CommandLine.SaveDirectory))
            {
                detail = "lifecycle requires -autoplay and an isolated -saveDir";
                return false;
            }
            HollowmereLifecycleAudit? audit = game.Session.GetComponent<HollowmereLifecycleAudit>();
            if (audit == null) audit = game.Session.gameObject.AddComponent<HollowmereLifecycleAudit>();
            return audit.CheckStage(game, stage, out detail);
        }

        private bool CheckStage(HollowmereGame game, string stage, out string detail)
        {
            detail = waiting = "waiting for lifecycle " + stage;
            if (Application.isEditor || Application.isBatchMode || game.Rig?.Root?.Document?.rootVisualElement.panel == null)
            {
                detail = waiting = "lifecycle requires a graphical standalone player with the production UI document";
                return false;
            }
            if (!initialized)
            {
                session = game.Session!;
                path = Path.Combine(game.CommandLine.SaveDirectory!, "lifecycle-report.json");
                report = File.Exists(path) ? JsonUtility.FromJson<Report>(File.ReadAllText(path)) : new Report { revision = HollowmereGame.Revision() };
                initialized = true;
                if (report.revision != HollowmereGame.Revision())
                    throw new InvalidOperationException("lifecycle report belongs to a different build");
                report.status = "RUNNING";
                Write();
            }
            if (report.stages.Count >= Stages.Length || stage != Stages[report.stages.Count])
            {
                detail = waiting = "out-of-order lifecycle stage " + stage;
                return false;
            }
            if (game.Director == null || game.Failure.Length != 0 || game.RestoreFailure.Length != 0) return false;
            State state = State.Read(game);
            UiScreen screen = game.Rig!.Ui.Screen;
            bool reached;
            switch (stage)
            {
                case "menu":
                    reached = screen == UiScreen.Menu && state.Fresh && !HollowmereConditions.Check(game, "slot 1", out _);
                    break;
                case "newgame":
                    reached = screen == UiScreen.Hud && state.Fresh;
                    break;
                case "paused":
                    reached = screen == UiScreen.Pause && state.quest == QuestIds.Active && state.rumour == 1
                        && state.gate == 1 && state.shrine == 1 && state.clapper == 1 && state.lantern == 1;
                    break;
                case "saved":
                    var pending = game.Rig.PendingSave;
                    reached = screen == UiScreen.Save && pending != null && pending.IsCompletedSuccessfully
                        && pending.Result.Succeeded && pending.Result.Slot == "slot-1"
                        && report.stages[2].state.Matches(state) && HollowmereConditions.Check(game, "slot 1", out _);
                    if (reached)
                    {
                        report.saved = state;
                        report.saveSlotHash = pending!.Result.SlotHash;
                    }
                    break;
                case "quit-ready":
                    reached = screen == UiScreen.Pause && report.saved != null && report.saved.Matches(state);
                    break;
                case "menu-relaunched":
                    reached = screen == UiScreen.Menu && report.saveProcessQuitObserved && session.Attaches == 1
                        && state.Fresh && SameInitialQuest(state) && HollowmereConditions.Check(game, "slot 1", out _);
                    break;
                case "loaded":
                    reached = screen == UiScreen.Hud && game.Restores == 1 && report.saved != null && report.saved.Matches(state);
                    break;
                case "ending":
                    reached = screen == UiScreen.Ending && game.Director.EndingShown && state.quest == QuestIds.Completed
                        && state.branch == 3 && state.outcome == 3 && state.ending == 1 && state.bell == 1;
                    if (reached) endingAttaches = session.Attaches;
                    break;
                case "restarted":
                    reached = screen == UiScreen.Hud && session.Attaches > endingAttaches && state.Fresh
                        && !game.Director.EndingShown && game.Restores == 0 && SameInitialQuest(state)
                        && state.region == report.stages[0].state.region && HollowmereConditions.Check(game, "slot 1", out _);
                    break;
                default:
                    reached = false;
                    break;
            }
            detail = waiting = stage + " screen=" + screen + " state=" + JsonUtility.ToJson(state)
                + " attaches=" + session.Attaches + " restores=" + game.Restores;
            if (!reached) return false;
            report.stages.Add(new Stage
            {
                name = stage, utc = DateTime.UtcNow.ToString("O"), frame = Time.frameCount,
                seconds = Time.realtimeSinceStartupAsDouble, screen = screen.ToString(),
                attaches = session.Attaches, restores = game.Restores, state = state,
            });
            Write();
            session.Mark("lifecycle:" + stage);
            Debug.Log("[R7-C lifecycle] PASS stage=" + stage + " " + detail);
            return true;
        }

        private bool SameInitialQuest(State state) => state.quest == report.stages[0].state.quest
            && state.stage == report.stages[0].state.stage && state.branch == report.stages[0].state.branch;

        private void OnApplicationQuit()
        {
            if (!initialized) return;
            bool uiQuit = session.Game != null && session.Game.Rig != null && session.Game.Rig.Ui.Screen == UiScreen.None
                && session.Autoplay != null && !session.Autoplay.Failed;
            if (report.stages.Count == 5 && uiQuit)
            {
                report.saveProcessQuitObserved = true;
                report.status = "AWAITING_RELAUNCH";
            }
            else if (report.stages.Count == Stages.Length && uiQuit)
            {
                report.finalProcessQuitObserved = true;
                report.status = "PASS";
            }
            else
            {
                report.status = "FAIL";
                report.failure = session.Autoplay?.Failed == true ? session.Autoplay.FailureText : waiting + "; production UI quit not observed";
            }
            Write();
            Debug.Log("[R7-C lifecycle] " + report.status + " " + report.failure);
        }

        private void Write()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonUtility.ToJson(report, true) + "\n");
        }
    }
}
