// Hollowmere.P3_2.Workflows - the workflows of docs/studio/07 W-AI-01..06, W-VOICE-01 and B-AGENT-UX as step lists
// (GCS_P32_WORKFLOW selects one). Each list is rebuilt identically after a domain reload; state lives in P32State.
//   smoke      layout + gateway + one viewport pick (no model call)
//   text       W-AI-02 + typed edit on a selection + clarification round trip
//   robe       W-AI-01 (healer's green robe) + asset generation (image with max_cost_usd -> asset.import -> assign;
//              dialogue.generateVoice probe; TTS line through the media gateway)
//   narrative  W-AI-03 (conditional line for Odd), W-AI-04 (HUD objective label binding), W-AI-05 (two oil flasks);
//              applied and saved for W-AI-06
//   reopen     W-AI-06 after an Editor restart: journal + asset consistency, undo/redo of the narrative edits
//   voice      W-VOICE-01 (spoken destructive command not sent) + voice prompt -> edit lifecycle
//   batch      marquee multi-target edit, one target made stale, AllOrNothing refusal then BestEffort with rebase
//   honesty    inspect/explain (local tools, describe op, worker question), cancel mid-run, max_cost_usd 0.001 image
//   mech-a     gc-mechanic proposal -> panel Stage -> candidate exported for the CLI stage
//   mech-b     recorded verdict -> Admit (compile + reload) -> Play -> undo
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using GameCore.Unity.App;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hollowmere.P3_2.Workflows
{
    public static class Workflows
    {
        private static readonly Dictionary<string, IReadOnlyList<Step>> Cache = new Dictionary<string, IReadOnlyList<Step>>();

        private static P32State St => P32State.instance;

        public static IReadOnlyList<Step> For(string name)
        {
            if (!Cache.TryGetValue(name, out IReadOnlyList<Step>? steps))
            {
                steps = Build(name);
                Cache[name] = steps;
            }

            return steps;
        }

        private static IReadOnlyList<Step> Build(string name)
        {
            switch (name)
            {
                case "smoke": return Smoke();
                case "text": return Text();
                case "robe": return Robe();
                case "narrative": return Narrative();
                case "reopen": return Reopen();
                case "voice": return Voice();
                case "batch": return Batch();
                case "honesty": return Honesty();
                case "mech-a": return MechA();
                case "mech-b": return MechB();
                default: throw new ArgumentException("unknown workflow " + name);
            }
        }

        private const string Generated = "Assets/Hollowmere/Generated/P3_2";
        private const string MarenBehaviour = "Assets/Hollowmere/Npcs/Definitions/MarenBehaviour.asset";
        private const string MarenNpc = "Assets/Hollowmere/Npcs/Definitions/Maren.asset";
        private const string MarenEntity = "Assets/Hollowmere/Npcs/Definitions/MarenEntity.asset";
        private const string OddGraph = "Assets/Hollowmere/Dialogue/Graphs/Odd.asset";
        private const string MarenGraph = "Assets/Hollowmere/Dialogue/Graphs/Maren.asset";
        private const string HudDoc = "Assets/Hollowmere/UI/Definitions/Doc_Hud.asset";
        private const string Quest = "Assets/Hollowmere/Quests/DrownedBell.asset";
        private const string LanternItem = "Assets/Hollowmere/Items/Lantern.asset";
        private const string GateRule = "Assets/Hollowmere/Rules/GateKeyOpensGate.asset";
        private const string Shared = "Library/P3_2";

        private static string PositionAnswer(string question)
        {
            return "Use the world positions in scene-context.json (metres; +Z is north, +X is east, +Y is up). " +
                   "Keep everything that was not asked for unchanged. If several readings remain, choose the simplest one that matches the request.";
        }

        // ------------------------------------------------------------------------------------------------- smoke

        private static IReadOnlyList<Step> Smoke()
        {
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Select("Maren"),
                S.Note("roster", () => Roster("smoke", "village")),
            };
        }

        // -------------------------------------------------------------------------------------------------- text

        private static IReadOnlyList<Step> Text()
        {
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Note("roster before", () => Roster("move-patrol", "before")),

                // Clarification round trip: an ambiguous pronoun over three selected NPCs.
                S.Select("Maren", "Pip", "Odd"),
                S.Send("clarify", "Make one of them patrol around the well."),
                S.Await("clarify", q => "Maren. Centre a small loop of three or four points on the Village Well (its position is in the index slice; " +
                                        "if it is not, use a 3 m radius around (0, 0, -1.9)), +Z north, metres."),
                S.Do("review clarify", () => ReviewOrSkip("clarify")),
                S.Reject("clarify", "P3.2: clarification round trip recorded; reviewed, not applied."),

                // Typed edit on a selection: move + patrol, applied, journaled, undone.
                S.Select("Maren", "Village Well"),
                S.Send("move-patrol", "Move Maren two metres north and make her patrol around the well."),
                S.Await("move-patrol", PositionAnswer),
                S.Do("preview move-patrol", () => Guard("move-patrol", () => S.Preview("move-patrol").Run())),
                S.Do("hashes before move-patrol", () => Guard("move-patrol", () => HashStep("move-patrol", "before", MarenBehaviour, MarenNpc, MarenEntity))),
                S.Do("apply move-patrol", () => Guard("move-patrol", () => S.Apply("move-patrol").Run())),
                S.Do("after move-patrol", () => Guard("move-patrol", () =>
                {
                    Roster("move-patrol", "applied");
                    DescribeTargets("move-patrol", "applied");
                    return HashStep("move-patrol", "applied", MarenBehaviour, MarenNpc, MarenEntity);
                })),
                S.Do("undo move-patrol", () => Guard("move-patrol", () => S.Undo("move-patrol").Run())),
                S.Do("after undo move-patrol", () => Guard("move-patrol", () =>
                {
                    Roster("move-patrol", "undone");
                    return HashStep("move-patrol", "undone", MarenBehaviour, MarenNpc, MarenEntity);
                })),

                // W-AI-02: point at a location, add a ferryman NPC who talks about the bell.
                S.Note("clear selection", () => S.Context.Selection.Clear()),
                S.PointAt("Village Well", new Vector3(4f, 0f, 3f)),
                S.Note("roster before ferryman", () => Roster("ferryman", "before")),
                S.Send("ferryman", "Add a ferryman NPC here who talks about the bell."),
                S.Await("ferryman", q => "Create a new NPC called Ferryman Bram at the selected location, with his own short dialogue about the Drowned Bell, " +
                                         "a small patrol (two or three points within 3 m of the location) and navigation like the other village NPCs. " +
                                         "Reuse the existing NPC definitions as templates where the catalog needs one.", 1200),
                S.Do("preview ferryman", () => Guard("ferryman", () => S.Preview("ferryman").Run())),
                S.Do("apply ferryman", () => Guard("ferryman", () => S.Apply("ferryman").Run())),
                S.Do("after ferryman", () => Guard("ferryman", () =>
                {
                    Roster("ferryman", "applied");
                    DescribeTargets("ferryman", "applied");
                    return true;
                })),
                S.Do("undo ferryman", () => Guard("ferryman", () => AppliedOr("ferryman", () => S.Undo("ferryman").Run()))),
                S.Note("roster after ferryman undo", () => Roster("ferryman", "undone")),
                S.Do("reject leftovers", () =>
                {
                    RejectIfOpen("move-patrol");
                    RejectIfOpen("ferryman");
                    WorkflowRunner.Recording(false);
                    return true;
                }),
            };
        }

        // -------------------------------------------------------------------------------------------------- robe

        private static IReadOnlyList<Step> Robe()
        {
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Select("Maren"),
                S.Do("hashes before robe", () => HashStep("robe", "before", MarenBehaviour, MarenNpc, MarenEntity)),
                S.Note("roster before robe", () => Roster("robe", "before")),
                S.Note("viewport before robe", () => ViewportPng("robe", "before")),
                S.Send("robe", "Give her a green robe."),
                S.Await("robe", q => "She is Maren, the village healer. Give her robe a green look: a generated green cloth texture if the catalog can apply one, " +
                                     "otherwise a green tint of her view. Do not change her behaviour, dialogue or position.", 1200),
                S.Do("preview robe", () => Guard("robe", () => S.Preview("robe").Run())),
                S.Do("apply robe", () => Guard("robe", () => S.Apply("robe").Run())),
                S.Do("after robe", () => Guard("robe", () =>
                {
                    ViewportPng("robe", "applied");
                    Roster("robe", "applied");
                    DescribeTargets("robe", "applied");
                    return HashStep("robe", "applied", MarenBehaviour, MarenNpc, MarenEntity);
                })),
                S.Do("undo robe", () => Guard("robe", () => AppliedOr("robe", () => S.Undo("robe").Run()))),
                S.Do("after undo robe", () => Guard("robe", () =>
                {
                    ViewportPng("robe", "undone");
                    return HashStep("robe", "undone", MarenBehaviour, MarenNpc, MarenEntity);
                })),
                S.Note("reject robe leftovers", () => RejectIfOpen("robe")),

                // Asset generation through the Studio media path (asset.generate -> generate.image with a ceiling),
                // verified import (asset.import, journaled with prompt + digest), assigned with `assign`.
                S.SelectAsset(LanternItem),
                S.Do("generate lantern icon", () => GenerateImage("icon", "A small inventory icon of an old brass marsh lantern with a warm flame, hand-painted game UI style, centred, plain dark background.", Generated + "/lantern_icon.png", 0.25)),
                S.Do("assign lantern icon", () => AssignIcon("icon", LanternItem, Generated + "/lantern_icon.png")),
                S.Do("undo assign", () => Guard("icon-assign", () => S.Undo("icon-assign").Run())),
                S.Do("undo import", () => Guard("icon", () => S.Undo("icon").Run())),

                // Voice line: dialogue.generateVoice first (the tool the catalog offers), then the media gateway (tts).
                S.SelectAsset(MarenGraph),
                S.Do("dialogue.generateVoice probe", () => ProbeGenerateVoice("voice-line", MarenGraph)),
                S.Do("tts voice line", () => GenerateSpeech("voice-line", Generated + "/maren_line.wav")),
                S.Do("undo voice import", () => Guard("voice-line", () => S.Undo("voice-line").Run())),
                S.Note("stop recording", () => WorkflowRunner.Recording(false)),
            };
        }

        // --------------------------------------------------------------------------------------------- narrative

        private static IReadOnlyList<Step> Narrative()
        {
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Do("hashes before narrative", () => HashStep("narrative", "before", OddGraph, HudDoc, Quest, LanternItem)),
                S.Note("previews before", () =>
                {
                    DialoguePreview("odd-line", "before", OddGraph, "");
                    QuestSimulate("quest", "before");
                    DescribeAsset("hud", "before", HudDoc);
                }),

                // W-AI-03
                S.Select("Odd"),
                S.Send("odd-line", "Add a line Odd only says after the shrine is lit."),
                S.Await("odd-line", q => "There is no shrine in the game yet. Add a new fact shrine_lit (0 at the start) and one new line in Odd's dialogue graph that is only " +
                                         "available when shrine_lit is 1, for example: \"So the old shrine burns again. The marsh feels less hungry tonight.\" Change nothing else.", 1200),
                S.Do("preview odd-line", () => Guard("odd-line", () => S.Preview("odd-line").Run())),
                S.Do("apply odd-line", () => Guard("odd-line", () => S.Apply("odd-line").Run())),
                S.Do("verify odd-line", () => Guard("odd-line", () =>
                {
                    AssetDatabase.SaveAssets();
                    DialoguePreview("odd-line", "applied-unlit", OddGraph, "");
                    foreach (string fact in FactsMentioned("odd-line", "shrine"))
                    {
                        DialoguePreview("odd-line", "applied-" + fact, OddGraph, fact + "=1");
                    }

                    return true;
                })),

                // W-AI-04
                S.SelectAsset(HudDoc),
                S.Send("hud", "Change the HUD objective label and bind it to the current quest stage name."),
                S.Await("hud", q => "Use the HUD document's objective label element (the one that shows the current objective). Give it the label text 'Current stage' " +
                                    "and bind its text to the active quest's current stage title (the stage name of The Drowned Bell). Change nothing else.", 1200),
                S.Do("preview hud", () => Guard("hud", () => S.Preview("hud").Run())),
                S.Do("apply hud", () => Guard("hud", () => S.Apply("hud").Run())),
                S.Do("verify hud", () => Guard("hud", () =>
                {
                    AssetDatabase.SaveAssets();
                    DescribeAsset("hud", "applied", HudDoc);
                    return true;
                })),

                // W-AI-05
                S.SelectAsset(Quest, LanternItem),
                S.Send("quest", "Change the lantern quest to require two oil flasks."),
                S.Await("quest", q => "The quest is The Drowned Bell (the lantern is needed to cross the marsh). There is no oil flask item yet: add an Oil Flask item " +
                                      "and an objective in stage 1 ('The marsh gate') to collect 2 oil flasks. Keep the other objectives as they are.", 1200),
                S.Do("preview quest", () => Guard("quest", () => S.Preview("quest").Run())),
                S.Do("apply quest", () => Guard("quest", () => S.Apply("quest").Run())),
                S.Do("verify quest", () => Guard("quest", () =>
                {
                    AssetDatabase.SaveAssets();
                    QuestSimulate("quest", "applied");
                    return true;
                })),

                // Persist for W-AI-06 (reopen run).
                S.Do("save project", () =>
                {
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.SaveOpenScenes();
                    JObject applied = new JObject();
                    foreach (string tag in new[] { "odd-line", "hud", "quest" })
                    {
                        CandidateEntry? entry = S.EntryOf(tag);
                        applied[tag] = new JObject
                        {
                            ["changeSetId"] = S.IdOf(tag),
                            ["state"] = entry == null ? "none" : S.Context.Runtime.Journal.Read(entry.Id)?.EffectiveState.ToString(),
                            ["applied"] = entry != null && entry.Stage == CandidateStage.Applied,
                        };
                    }

                    JObject hashes = S.Hashes(OddGraph, HudDoc, Quest, LanternItem);
                    JObject doc = new JObject { ["applied"] = applied, ["before"] = St.Get("hash.narrative.before")?.DeepClone(), ["after"] = hashes, ["savedUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
                    Directory.CreateDirectory(Path.Combine(WorkflowRunner.ProjectRoot, Shared));
                    File.WriteAllText(Path.Combine(WorkflowRunner.ProjectRoot, Shared, "narrative.json"), doc.ToString());
                    WorkflowRunner.Json("narrative/saved.json", doc);
                    WorkflowRunner.Shot("narrative-saved", "Project saved with the applied narrative edits (W-AI-03/04/05) for the W-AI-06 reopen run.", doc);
                    WorkflowRunner.Recording(false);
                    return true;
                }),
            };
        }

        // ------------------------------------------------------------------------------------------------ reopen

        private static IReadOnlyList<Step> Reopen()
        {
            string[] tags = { "quest", "hud", "odd-line" };
            List<Step> steps = new List<Step>
            {
                S.OpenScene(S.VillageScene),
                S.Do("verify after reopen", () =>
                {
                    string file = Path.Combine(WorkflowRunner.ProjectRoot, Shared, "narrative.json");
                    if (!File.Exists(file))
                    {
                        throw new InvalidOperationException("No " + Shared + "/narrative.json: run the narrative workflow first.");
                    }

                    JObject saved = JObject.Parse(File.ReadAllText(file));
                    St.Set("saved", saved);
                    JObject check = new JObject { ["hashesNow"] = S.Hashes(OddGraph, HudDoc, Quest, LanternItem), ["journal"] = new JObject() };
                    foreach (string tag in tags)
                    {
                        string id = (string?)saved["applied"]?[tag]?["changeSetId"] ?? string.Empty;
                        JObject r = new JObject { ["prompt"] = tag, ["ids"] = new JArray(id), ["id"] = id };
                        St.SetReq(tag, r);
                        ((JObject)check["journal"]!)[tag] = id.Length == 0 ? "none" : S.Context.Runtime.Journal.Read(id)?.EffectiveState.ToString() ?? "missing";
                    }

                    check["hashesMatchSaved"] = JToken.DeepEquals(check["hashesNow"], saved["after"]);
                    WorkflowRunner.Json("reopen/after-reopen.json", check);
                    StudioHistoryWindow.Open();
                    WorkflowRunner.Shot("reopen", "Editor restarted: History lists the narrative entries; journal " + check["journal"]!.ToString(Newtonsoft.Json.Formatting.None) + "; asset hashes match the saved state: " + check["hashesMatchSaved"] + ".", check);
                    if (!(bool)check["hashesMatchSaved"]!)
                    {
                        WorkflowRunner.MarkFailed("asset hashes after reopen differ from the saved state");
                    }

                    return true;
                }),
            };
            foreach (string tag in tags)
            {
                steps.Add(S.Do("undo " + tag, () => AppliedEntry(tag) ? S.Undo(tag).Run() : Skip(tag, "undo")));
                steps.Add(S.Do("hash after undo " + tag, () => ConsistencyCheck("undo-" + tag)));
            }

            foreach (string tag in tags.Reverse())
            {
                steps.Add(S.Do("redo " + tag, () => JournalState(tag) == ChangeSetState.Undone ? S.Redo(tag).Run() : Skip(tag, "redo")));
                steps.Add(S.Do("hash after redo " + tag, () => ConsistencyCheck("redo-" + tag)));
            }

            foreach (string tag in tags)
            {
                steps.Add(S.Do("final undo " + tag, () => JournalState(tag) == ChangeSetState.Applied ? S.Undo(tag).Run() : Skip(tag, "final undo")));
            }

            steps.Add(S.Do("final consistency", () =>
            {
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveOpenScenes();
                JObject saved = (JObject)St.Get("saved")!;
                JObject now = S.Hashes(OddGraph, HudDoc, Quest, LanternItem);
                JObject result = new JObject { ["before"] = saved["before"]?.DeepClone(), ["now"] = now, ["backToBefore"] = JToken.DeepEquals(saved["before"], now) };
                WorkflowRunner.Json("reopen/final.json", result);
                WorkflowRunner.Shot("reopen-final", "After undo -> redo -> undo of every narrative edit: assets back to the pre-edit hashes: " + result["backToBefore"] + ".", result);
                return true;
            }));
            return steps;
        }

        private static bool AppliedEntry(string tag) => JournalState(tag) == ChangeSetState.Applied;

        private static ChangeSetState? JournalState(string tag)
        {
            string id = S.IdOf(tag);
            return id.Length == 0 ? null : S.Context.Runtime.Journal.Read(id)?.EffectiveState;
        }

        private static bool Skip(string tag, string what)
        {
            WorkflowRunner.Log(tag + "-skip", what + " skipped: journal state " + (JournalState(tag)?.ToString() ?? "none"), null);
            return true;
        }

        private static bool ConsistencyCheck(string label)
        {
            AssetDatabase.SaveAssets();
            JObject now = S.Hashes(OddGraph, HudDoc, Quest, LanternItem);
            WorkflowRunner.Json("reopen/hashes-" + label + ".json", now);
            return true;
        }

        // ------------------------------------------------------------------------------------------------- voice

        private static IReadOnlyList<Step> Voice()
        {
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Select("Village Well"),
                S.Do("speak prompts (tts)", () => SpeakPrompts()),
                S.Do("counts before", () =>
                {
                    St.Set("countsBefore", Counts());
                    return true;
                }),
                S.Do("voice destructive", () => VoiceTake("destructive", "destructive.wav")),
                S.Wait("settle after destructive", 10),
                S.Do("nothing happened", () =>
                {
                    JObject before = (JObject)St.Get("countsBefore")!;
                    JObject after = Counts();
                    PromptBar prompt = S.Prompt;
                    JObject result = new JObject { ["before"] = before, ["after"] = after, ["unchanged"] = JToken.DeepEquals(before, after), ["fieldText"] = prompt.Text };
                    WorkflowRunner.Json("voice/destructive-check.json", result);
                    WorkflowRunner.Shot("voice-not-sent", "W-VOICE-01: the spoken destructive command is only text in the prompt field; requests/journal before " + before.ToString(Newtonsoft.Json.Formatting.None) + " after " + after.ToString(Newtonsoft.Json.Formatting.None) + "; nothing was sent or applied.", result);
                    prompt.Text = string.Empty;
                    return true;
                }),
                S.Do("voice move", () => VoiceTake("move", "move.wav")),
                S.Do("send transcript", () => SendCurrent("voice-move")),
                S.Await("voice-move", PositionAnswer),
                S.Do("preview voice-move", () => Guard("voice-move", () => S.Preview("voice-move").Run())),
                S.Do("apply voice-move", () => Guard("voice-move", () => S.Apply("voice-move").Run())),
                S.Do("undo voice-move", () => Guard("voice-move", () => AppliedOr("voice-move", () => S.Undo("voice-move").Run()))),
                S.Note("reject leftovers", () =>
                {
                    RejectIfOpen("voice-move");
                    WorkflowRunner.Recording(false);
                }),
            };
        }

        private static JObject Counts()
        {
            StudioUiContext context = S.Context;
            return new JObject
            {
                ["trayRows"] = context.Tasks.Rows.Count,
                ["gatewayRequests"] = context.Gateway.Requests.Count,
                ["journalEntries"] = context.Runtime.Journal.List().Count,
            };
        }

        private static bool SpeakPrompts()
        {
            string key = "speak";
            if (St.Get(key) == null)
            {
                St.Set(key, "pending");
                GameCore.Studio.Authoring.Agent.IAgentGateway gateway = S.Context.Gateway;
                Task<OpResult> a = gateway.GenerateAsync(new OpRequest("tts", new JObject { ["text"] = "Delete every NPC in the village." }, 0.05), CancellationToken.None);
                Task<OpResult> b = gateway.GenerateAsync(new OpRequest("tts", new JObject { ["text"] = "Move the well one metre to the east." }, 0.05), CancellationToken.None);
                Pending["speak"] = Task.WhenAll(a, b).ContinueWith(done =>
                {
                    Directory.CreateDirectory(Path.Combine(WorkflowRunner.OutputDir, "voice"));
                    JObject info = new JObject();
                    foreach ((string name, Task<OpResult> task) in new[] { ("destructive.wav", a), ("move.wav", b) })
                    {
                        OpResult result = task.Result;
                        if (result.Bytes != null)
                        {
                            File.WriteAllBytes(Path.Combine(WorkflowRunner.OutputDir, "voice", name), result.Bytes);
                        }

                        info[name] = new JObject { ["ok"] = result.Succeeded, ["sha256"] = result.Sha256, ["provider"] = result.Provider, ["bytes"] = result.Bytes?.Length, ["refusal"] = result.Refusal == null ? null : StudioJson.ToToken(result.Refusal) };
                    }

                    return (object)info;
                });
                return false;
            }

            if (!Pending.TryGetValue("speak", out Task<object>? pending) || !pending.IsCompleted)
            {
                return false;
            }

            JObject spoken = pending.IsFaulted ? new JObject { ["error"] = pending.Exception?.GetBaseException().Message } : (JObject)pending.Result;
            WorkflowRunner.Json("voice/spoken-prompts.json", spoken);
            WorkflowRunner.Log("voice-prompts", "Spoken prompts generated with the tts op (played into the PipeWire virtual microphone by the driver): " + spoken.ToString(Newtonsoft.Json.Formatting.None), null);
            return true;
        }

        private static readonly Dictionary<string, Task<object>> Pending = new Dictionary<string, Task<object>>();

        /// <summary>
        /// Push-to-talk: presses the mic (voice session starts on the default microphone = the driver's virtual source),
        /// asks the driver to play <paramref name="wav"/> into it, records every transcript revision shown in the prompt bar,
        /// and releases once the final transcript is in the field (or after 60 s).
        /// </summary>
        private static bool VoiceTake(string label, string wav)
        {
            PromptBar prompt = S.Prompt;
            string key = "voice." + label;
            JObject v = St.Get(key) as JObject ?? new JObject();
            string phase = (string?)v["phase"] ?? "start";
            string dir = Path.Combine(WorkflowRunner.OutputDir, "voice");
            Directory.CreateDirectory(dir);
            switch (phase)
            {
                case "start":
                    prompt.Text = string.Empty;
                    prompt.ToggleVoice();
                    v["phase"] = "listening";
                    v["pressedMs"] = WorkflowRunner.NowMs;
                    v["pressedEditor"] = EditorApplication.timeSinceStartup;
                    St.Set(key, v);
                    StartWatch(label);
                    WorkflowRunner.Recording(true);
                    WorkflowRunner.Shot("voice-" + label + "-pressed", "Mic pressed (push-to-talk); the voice session opens on the default microphone.", null);
                    return false;
                case "listening":
                    if (EditorApplication.timeSinceStartup - (double)v["pressedEditor"]! < 3)
                    {
                        return false;
                    }

                    File.WriteAllText(Path.Combine(dir, "play-" + label), wav + "\n");
                    v["phase"] = "playing";
                    v["playRequestedMs"] = WorkflowRunner.NowMs;
                    St.Set(key, v);
                    return false;
                case "playing":
                    bool played = File.Exists(Path.Combine(dir, "played-" + label));
                    bool final = prompt.Text.Trim().Length > 0;
                    double waited = EditorApplication.timeSinceStartup - (double)v["pressedEditor"]!;
                    if (!(played && final) && waited < 75)
                    {
                        return false;
                    }

                    prompt.ToggleVoice();
                    v["phase"] = "done";
                    v["releasedMs"] = WorkflowRunner.NowMs;
                    v["final"] = prompt.Text;
                    v["played"] = played;
                    St.Set(key, v);
                    StopWatch(label);
                    JObject revisions = St.Get("watch." + label) as JObject ?? new JObject();
                    JObject result = new JObject { ["label"] = label, ["wav"] = wav, ["final"] = prompt.Text, ["played"] = played, ["pressedMs"] = v["pressedMs"], ["releasedMs"] = v["releasedMs"], ["revisions"] = revisions["revisions"]?.DeepClone() ?? new JArray(), ["voiceActiveAfterRelease"] = prompt.VoiceActive };
                    WorkflowRunner.Json("voice/" + label + "-transcript.json", result);
                    WorkflowRunner.Shot("voice-" + label + "-final", "Mic released: final transcript \"" + prompt.Text + "\" placed in the field (" + ((JArray)result["revisions"]!).Count + " partial revision(s) were shown while listening); nothing is sent until Send.", result);
                    if (prompt.Text.Trim().Length == 0)
                    {
                        WorkflowRunner.MarkFailed("voice " + label + ": no final transcript");
                    }

                    return true;
                default:
                    return true;
            }
        }

        private static Action? _watch;

        private static void StartWatch(string label)
        {
            StopWatch(label);
            string last = string.Empty;
            JArray revisions = new JArray();
            _watch = () =>
            {
                PromptBar? prompt = Resources.FindObjectsOfTypeAll<StudioViewportWindow>().FirstOrDefault(w => w != null)?.Prompt;
                if (prompt == null)
                {
                    return;
                }

                string partial = prompt.PartialTranscript;
                if (partial != last)
                {
                    last = partial;
                    revisions.Add(new JObject { ["ms"] = WorkflowRunner.NowMs, ["shown"] = partial, ["field"] = prompt.Text });
                    St.Set("watch." + label, new JObject { ["revisions"] = revisions });
                    if (revisions.Count % 3 == 1)
                    {
                        WorkflowRunner.Frame(null);
                    }
                }
            };
            WorkflowRunner.Watch += _watch;
        }

        private static void StopWatch(string label)
        {
            if (_watch != null)
            {
                WorkflowRunner.Watch -= _watch;
                _watch = null;
            }
        }

        /// <summary>Presses Send on what is in the prompt field (the confirmed voice transcript).</summary>
        private static bool SendCurrent(string tag)
        {
            string text = S.Prompt.Text.Trim();
            if (text.Length == 0)
            {
                throw new InvalidOperationException("The prompt field is empty (no transcript to send).");
            }

            return S.Send(tag, text).Run();
        }

        // ------------------------------------------------------------------------------------------------- batch

        private static IReadOnlyList<Step> Batch()
        {
            string[] crates = { "Crate 2", "Crate 3", "Market Crate" };
            return new[]
            {
                S.OpenScene(S.VillageScene),
                S.WaitGateway(),
                S.Do("marquee crates", () => Marquee(crates)),
                S.Note("roster before", () => Roster("ring", "before")),
                S.Send("ring", "Arrange these crates evenly in a ring of radius 3 metres around the Village Well."),
                S.Await("ring", q => "The three selected crates; the ring is centred on the Village Well (position in scene-context.json or the index slice), radius 3 m, " +
                                     "evenly spaced, same height as now. Use one move per crate.", 1200),
                S.Do("preview ring", () => Guard("ring", () => S.Preview("ring").Run())),
                S.Do("make one target stale", () => Guard("ring", () => MakeStale("ring"))),
                S.Do("apply AllOrNothing", () => Guard("ring", () => S.Apply("ring", ApplyPolicy.AllOrNothing, "apply-all-or-nothing").Run())),
                S.Do("rebase + BestEffort", () => Guard("ring", () => RebaseAndApply("ring"))),
                S.Note("roster applied", () => Roster("ring", "applied")),
                S.Do("undo ring", () => Guard("ring", () => AppliedOr("ring", () => S.Undo("ring").Run()))),
                S.Do("undo manual move", () =>
                {
                    string manual = St.Str("staleManualId");
                    if (manual.Length == 0)
                    {
                        return true;
                    }

                    St.SetReq("manual", new JObject { ["ids"] = new JArray(manual), ["id"] = manual });
                    return S.Undo("manual").Run();
                }),
                S.Note("roster undone", () =>
                {
                    Roster("ring", "undone");
                    RejectIfOpen("ring");
                    WorkflowRunner.Recording(false);
                }),
            };
        }

        private static bool Marquee(string[] names)
        {
            StudioViewportWindow viewport = S.Viewport();
            viewport.SetMode(ViewportMode.Select);
            List<GameObject> targets = names.Select(S.Require).ToList();
            S.FrameOn(targets, 1.2f);
            Rect union = Rect.zero;
            bool first = true;
            foreach (GameObject target in targets)
            {
                Rect? rect = viewport.ScreenRectOf(target);
                if (rect == null)
                {
                    continue;
                }

                union = first ? rect.Value : Rect.MinMaxRect(Mathf.Min(union.xMin, rect.Value.xMin), Mathf.Min(union.yMin, rect.Value.yMin), Mathf.Max(union.xMax, rect.Value.xMax), Mathf.Max(union.yMax, rect.Value.yMax));
                first = false;
            }

            if (first)
            {
                throw new InvalidOperationException("No crate is on screen.");
            }

            Rect marquee = new Rect(union.xMin - 6f, union.yMin - 6f, union.width + 12f, union.height + 12f);
            PickResult picked = viewport.MarqueeSelect(marquee, SelectionOp.Replace, true);
            List<string> extras = new List<string>();
            StudioUiContext context = viewport.Context;
            foreach (UnityEngine.Object selected in context.Selection.ResolveObjects())
            {
                GameObject? go = selected is Component c ? c.gameObject : selected as GameObject;
                if (go != null && !targets.Any(t => go.transform.IsChildOf(t.transform) || t.transform.IsChildOf(go.transform)))
                {
                    extras.Add(go.name);
                    AuthoringRef? reference = context.Selection.RefOf(selected);
                    if (reference != null)
                    {
                        context.Selection.Set(new[] { reference }, SelectionOp.Toggle);
                    }
                }
            }

            foreach (GameObject target in targets)
            {
                AuthoringRef? reference = context.Selection.RefOf(target);
                if (reference != null && !context.Selection.Targets.Any(t => t.AuthoringId == reference.AuthoringId))
                {
                    context.Selection.Set(new[] { reference }, SelectionOp.Add);
                    extras.Add("+" + target.name);
                }
            }

            string badges = string.Join(", ", context.Selection.Describe().Select(b => b.Label));
            WorkflowRunner.Shot("marquee", "Marquee (full containment) over the crates: " + picked.Targets().Count + " picked in " + picked.Timings.TotalMs.ToString("0.0", CultureInfo.InvariantCulture) + " ms; Ctrl-toggled " + (extras.Count == 0 ? "nothing" : string.Join(", ", extras)) + "; selection: " + badges + ".", new JObject { ["picked"] = picked.Targets().Count, ["adjusted"] = new JArray(extras.ToArray()), ["selection"] = badges });
            return true;
        }

        /// <summary>Moves the first target of the candidate by hand (a manual move change set) so its stamp is stale.</summary>
        private static bool MakeStale(string tag)
        {
            CandidateEntry entry = S.EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag);
            StudioUiContext context = S.Context;
            Operation? op = entry.ChangeSet.Operations.FirstOrDefault(o => o.Target != null && o.Target.Kind != AuthoringKind.Location);
            if (op == null)
            {
                throw new InvalidOperationException("The candidate has no targeted operation.");
            }

            UnityEngine.Object? resolved = context.Runtime.Resolver.Find(op.Target!);
            GameObject go = (resolved as Component)?.gameObject ?? resolved as GameObject ?? throw new InvalidOperationException("The target of " + op.OpId + " does not resolve.");
            Vector3 from = go.transform.position;
            ChangeSet manual = MoveChangeSets.Build(context.Runtime, go, from + new Vector3(0.5f, 0f, 0.5f)) ?? throw new InvalidOperationException("No manual move for " + go.name);
            ApplyReport report = context.Runtime.Engine.Apply(manual);
            St.Set("staleOp", op.OpId);
            St.Set("staleManualId", manual.Id);
            JObject data = new JObject { ["object"] = go.name, ["op"] = op.OpId, ["from"] = S.Vec(from), ["to"] = S.Vec(go.transform.position), ["manualChangeSet"] = manual.Id, ["manualState"] = report.State.ToString() };
            WorkflowRunner.Json(tag + "/stale.json", data);
            WorkflowRunner.Shot(tag + "-stale", "Before applying, " + go.name + " (op " + op.OpId + ") was moved by hand (manual move change set " + manual.Id + ", " + report.State + "); the candidate's stamp for it is now stale.", data);
            return true;
        }

        private static bool RebaseAndApply(string tag)
        {
            CandidateEntry entry = S.EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag);
            StudioUiContext context = S.Context;
            if (!entry.IsOpen)
            {
                WorkflowRunner.Log(tag + "-rebase", "The candidate is " + entry.Stage + " after the AllOrNothing apply; no rebase.", null);
                return true;
            }

            string stale = St.Str("staleOp");
            StagedChangeSet rebased = context.Candidates.Rebase(entry, stale.Length > 0 ? new[] { stale } : null);
            WorkflowRunner.Json(tag + "/rebased.json", new JObject { ["ok"] = rebased.Ok, ["rebased"] = stale, ["diagnostics"] = new JArray(rebased.AllDiagnostics.Select(d => StudioJson.ToToken(d)).ToArray()) });
            WorkflowRunner.Shot(tag + "-rebased", "Rebase of " + stale + " onto the current state: " + (rebased.Ok ? "ok" : "diagnostics remain") + ".", null);
            return S.Apply(tag, ApplyPolicy.BestEffort, "apply-best-effort").Run();
        }

        // ----------------------------------------------------------------------------------------------- honesty

        private static IReadOnlyList<Step> Honesty()
        {
            return new[]
            {
                S.OpenScene(S.MarshScene),
                S.WaitGateway(),
                S.Select("Causeway Gate"),
                S.Do("local explain", () => LocalExplain()),
                S.Send("explain", "Why is the Causeway Gate locked, and what would break if the lantern item were deleted? Explain only; do not change anything."),
                S.Await("explain", null, 900),
                S.Note("reject explain", () => RejectIfOpen("explain")),

                // Cancel mid-run from the tray.
                S.Select("Hale"),
                S.Send("cancel", "Give Hale a short patrol along the causeway, three points about two metres apart."),
                S.Do("cancel when running", () => CancelWhenRunning("cancel")),
                S.Await("cancel", null, 300),
                S.Do("cancel outcome", () =>
                {
                    JObject r = St.Req("cancel");
                    long cancelAt = (long?)r["cancelAtMs"] ?? 0;
                    long? cancelled = (long?)r["stateAt"]?[S.IdOf("cancel") + ":cancelled"];
                    r["cancelAckMs"] = cancelled.HasValue && cancelAt > 0 ? cancelled - cancelAt : null;
                    St.SetReq("cancel", r);
                    S.Timings("cancel");
                    WorkflowRunner.Shot("cancel-outcome", "Cancel from the tray: " + r["result"] + "; acknowledged in " + (r["cancelAckMs"]?.ToString() ?? "n/a") + " ms; candidate: " + (S.EntryOf("cancel") != null) + ".", null);
                    return true;
                }),

                // A media op with a 0.001 USD ceiling (expected: budget refusal).
                S.Do("image with max_cost 0.001", () => GenerateImage("budget", "A tiny test swatch, flat green square.", Generated + "/budget_probe.png", 0.001)),
                S.Do("describe generated image", () => DescribeLast("budget")),
                S.Do("undo budget import", () => Guard("budget", () => AppliedOr("budget", () => S.Undo("budget").Run()))),
                S.Note("stop", () => WorkflowRunner.Recording(false)),
            };
        }

        private static bool LocalExplain()
        {
            StudioUiContext context = S.Context;
            StudioRuntime runtime = context.Runtime;
            GameObject gate = S.Require("Causeway Gate");
            AuthoringRef gateRef = context.Selection.RefOf(gate) ?? throw new InvalidOperationException("no ref for the gate");
            UnityEngine.Object lantern = AssetDatabase.LoadMainAssetAtPath(LanternItem);
            AuthoringRef lanternRef = context.Selection.RefOf(lantern) ?? throw new InvalidOperationException("no ref for the lantern");
            JObject result = new JObject();
            foreach ((string name, string tool, AuthoringRef target, JObject args) in new[]
            {
                ("gate-describe", BuiltInToolIds.InspectDescribe, gateRef, new JObject()),
                ("gate-explain", BuiltInToolIds.InspectExplain, gateRef, new JObject { ["question"] = "Why is the Causeway Gate locked?" }),
                ("lantern-impact", BuiltInToolIds.QueryImpact, lanternRef, new JObject()),
                ("lantern-references", BuiltInToolIds.QueryReferences, lanternRef, new JObject()),
            })
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                OperationResult output = runtime.Registry.Invoke(tool, target, args);
                watch.Stop();
                result[name] = new JObject { ["tool"] = tool, ["status"] = output.Status.ToString(), ["code"] = output.Code, ["detail"] = output.Detail, ["ms"] = Math.Round(watch.Elapsed.TotalMilliseconds, 1), ["output"] = output.Output?.DeepClone() };
            }

            try
            {
                object? explained = S.InvokeTool("logic.explain", AssetDatabase.LoadMainAssetAtPath(GateRule));
                result["rule-explain"] = new JObject { ["tool"] = "logic.explain", ["rule"] = GateRule, ["output"] = explained?.ToString() };
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                result["rule-explain"] = new JObject { ["tool"] = "logic.explain", ["error"] = (error.InnerException ?? error).Message };
            }

            WorkflowRunner.Json("explain/local-tools.json", result);
            StudioContextWindow.Open();
            WorkflowRunner.Shot("explain-local", "Local, model-free answers (SADR-003): inspect.describe/inspect.explain on the Causeway Gate, query.impact/query.references on the Lantern item, logic.explain on GateKeyOpensGate.", new JObject { ["summary"] = Summarize(result) });
            return true;
        }

        private static string Summarize(JObject result)
        {
            return string.Join("; ", result.Properties().Select(p => p.Name + "=" + ((string?)p.Value["status"] ?? (p.Value["error"] != null ? "error" : "ok"))));
        }

        private static bool CancelWhenRunning(string tag)
        {
            StudioUiContext context = S.Context;
            LiveRequests.Collect(tag);
            string id = LiveRequests.Ids(St.Req(tag)).Last();
            TaskRow? row = context.Tasks.Find(id);
            if (row == null || row.requestId.Length == 0 || (row.State != AgentRequestState.Running && St.Waits++ < 240))
            {
                return false;
            }

            StudioTasksWindow.Open();
            TaskTrayView tray = EditorWindow.GetWindow<StudioTasksWindow>().View ?? throw new InvalidOperationException("The task tray is not open.");
            tray.Select(id);
            WorkflowRunner.Shot("cancel-running", "Task tray: " + id + " is " + row.StateLabel + " (etos " + row.etosStatus + "); pressing Cancel.", null);
            JObject r = St.Req(tag);
            r["cancelAtMs"] = WorkflowRunner.NowMs;
            r["id"] = id;
            St.SetReq(tag, r);
            _ = tray.Cancel(row);
            return true;
        }

        // ------------------------------------------------------------------------------------------- mechanisms

        private static IReadOnlyList<Step> MechA()
        {
            return new[]
            {
                S.OpenScene(S.MarshScene),
                S.WaitGateway(),
                S.Select("Causeway Gate"),
                S.Send("mech", "Add a pressure plate mechanism that opens the marsh gate (the Causeway Gate) while an item sits on the plate.", "mechanism"),
                S.Await("mech", q => "A floor plate placed next to the Causeway Gate in Blackmere Marsh; the gate is open exactly while at least one item rests on the plate, " +
                                     "and closes when it is removed. Keep the state in int32 slots and include Rules tests.", 2400),
                S.Do("preview mech", () => Guard("mech", () => S.Preview("mech").Run())),
                S.Do("panel stage", () => Guard("mech", () => PanelStage("mech"))),
                S.Do("export candidate", () => Guard("mech", () => ExportCandidate("mech"))),
                S.Note("stop", () => WorkflowRunner.Recording(false)),
            };
        }

        private static bool PanelStage(string tag)
        {
            CandidateEntry entry = S.EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag);
            StudioUiContext context = S.Context;
            string key = "stage." + tag;
            if (St.Get(key) == null)
            {
                St.Set(key, WorkflowRunner.NowMs);
                StudioCandidatesWindow.Open(entry.Id);
                WorkflowRunner.Shot(tag + "-stage-requested", "Candidate panel: Stage pressed for " + entry.Id + " (" + entry.Summary + "); badges " + string.Join(", ", entry.Badges) + ".", null);
                Pending[key] = context.Candidates.RequestStage(entry).ContinueWith(done => (object)(done.IsFaulted ? new Diagnostic("internal", done.Exception!.GetBaseException().Message) : done.Result!)!);
                return false;
            }

            if (Pending.TryGetValue(key, out Task<object>? pending) && !pending.IsCompleted)
            {
                return false;
            }

            Diagnostic? problem = pending?.Result as Diagnostic;
            StageState state = context.Candidates.StageStateOf(entry);
            JObject r = St.Req(tag);
            r["stageWallMs"] = WorkflowRunner.NowMs - (long)St.Get(key)!;
            St.SetReq(tag, r);
            JObject data = new JObject
            {
                ["problem"] = problem == null ? null : StudioJson.ToToken(problem),
                ["label"] = state.Label,
                ["verdict"] = state.Verdict == null ? null : StudioJson.ToToken(state.Verdict),
                ["wallMs"] = r["stageWallMs"],
                ["canRequestStage"] = context.Candidates.CanRequestStage,
            };
            WorkflowRunner.Json(tag + "/panel-stage.json", data);
            WorkflowRunner.Shot(tag + "-stage-result", "Panel Stage through the companion: " + state.Label + (problem != null ? " (" + problem.Code + ": " + problem.Message + ")" : string.Empty) + ".", data);
            return true;
        }

        /// <summary>Writes the candidate as a stage-lane candidate directory (change-set.json + artifacts/).</summary>
        private static bool ExportCandidate(string tag)
        {
            CandidateEntry entry = S.EntryOf(tag) ?? throw new InvalidOperationException("No candidate for " + tag);
            StudioRuntime runtime = S.Context.Runtime;
            string dir = Path.Combine(WorkflowRunner.OutputDir, tag, "candidate");
            Directory.CreateDirectory(Path.Combine(dir, "artifacts"));
            File.WriteAllText(Path.Combine(dir, "change-set.json"), StudioJson.Serialize(entry.ChangeSet));
            JArray written = new JArray();
            foreach (ArtifactRef artifact in entry.ChangeSet.Artifacts ?? (IReadOnlyList<ArtifactRef>)Array.Empty<ArtifactRef>())
            {
                byte[] bytes = runtime.Artifacts.Read(artifact.Sha256);
                string name = string.IsNullOrEmpty(artifact.Name) ? artifact.Sha256 : artifact.Name!;
                File.WriteAllBytes(Path.Combine(dir, "artifacts", name), bytes);
                written.Add(new JObject { ["name"] = name, ["sha256"] = artifact.Sha256, ["bytes"] = bytes.Length });
            }

            JObject doc = new JObject { ["changeSetId"] = entry.Id, ["requestId"] = entry.RequestId, ["dir"] = dir, ["artifacts"] = written, ["catalogRevision"] = entry.ToolCatalogRevision };
            Directory.CreateDirectory(Path.Combine(WorkflowRunner.ProjectRoot, Shared));
            File.WriteAllText(Path.Combine(WorkflowRunner.ProjectRoot, Shared, "mech.json"), doc.ToString());
            WorkflowRunner.Json(tag + "/exported.json", doc);
            WorkflowRunner.Log(tag + "-exported", "Candidate exported for the CLI stage lane: " + doc.ToString(Newtonsoft.Json.Formatting.None), null);
            return true;
        }

        private static IReadOnlyList<Step> MechB()
        {
            return new[]
            {
                S.OpenScene(S.MarshScene),
                S.Do("find candidate", () => FindMechCandidate()),
                S.Do("record verdict", () => RecordVerdict()),
                S.Do("admit", () => Admit()),
                S.Do("wait admission", () => WaitScenario("admission", 900)),
                S.Do("play check", () => PlayCheck()),
                S.Do("exit play", () =>
                {
                    if (EditorApplication.isPlaying)
                    {
                        EditorApplication.ExitPlaymode();
                        return false;
                    }

                    return true;
                }),
                S.Do("undo admission", () => UndoAdmission()),
                S.Do("wait undo", () => WaitScenario("undo", 900)),
                S.Note("stop", () => WorkflowRunner.Recording(false)),
            };
        }

        private static JObject MechDoc()
        {
            string env = Environment.GetEnvironmentVariable("GCS_P32_MECH") ?? Path.Combine(WorkflowRunner.ProjectRoot, Shared, "mech.json");
            return JObject.Parse(File.ReadAllText(env));
        }

        private static bool FindMechCandidate()
        {
            JObject doc = MechDoc();
            string id = (string)doc["changeSetId"]!;
            St.SetReq("mech", new JObject { ["ids"] = new JArray(id), ["id"] = id, ["submittedMs"] = WorkflowRunner.NowMs, ["submittedEditor"] = EditorApplication.timeSinceStartup });
            St.Set("mechDoc", doc);
            StudioUiContext context = S.Context;
            CandidateEntry? entry = context.Candidates.Find(id);
            if (entry == null && St.Get("retained") == null)
            {
                // A fresh Editor: the candidate comes back from the change-set directory exported by mech-a (the same
                // bytes the companion delivered; retained again and verified by digest).
                string dir = (string?)doc["dir"] ?? throw new InvalidOperationException("mech.json has no dir");
                string? overrideDir = Environment.GetEnvironmentVariable("GCS_P32_MECH_CANDIDATE");
                ChangeSet changeSet = StageAdmission.Of(context.Runtime).RetainCandidate(overrideDir ?? dir);
                entry = context.Candidates.Add((string?)doc["requestId"] ?? id, changeSet, (string?)doc["catalogRevision"]);
                St.Set("retained", true);
                if (changeSet.Id != id)
                {
                    St.SetReq("mech", new JObject { ["ids"] = new JArray(changeSet.Id), ["id"] = changeSet.Id, ["submittedMs"] = WorkflowRunner.NowMs, ["submittedEditor"] = EditorApplication.timeSinceStartup });
                }
            }

            entry = S.EntryOf("mech");
            if (entry == null)
            {
                return St.Waits++ > 120 ? throw new InvalidOperationException("The mechanism candidate did not come back.") : false;
            }

            StudioCandidatesWindow.Open(entry.Id);
            WorkflowRunner.Shot("mech-candidate", "Mechanism candidate " + entry.Id + " in the panel (" + entry.Summary + "); stage state " + context.Candidates.StageStateOf(entry).Label + ".", null);
            return true;
        }

        private static bool RecordVerdict()
        {
            CandidateEntry entry = S.EntryOf("mech") ?? throw new InvalidOperationException("no mechanism candidate");
            string path = Environment.GetEnvironmentVariable("GCS_P32_VERDICT") ?? throw new InvalidOperationException("GCS_P32_VERDICT is not set");
            StudioUiContext context = S.Context;
            StageVerdict verdict = context.Candidates.RecordVerdictFile(entry, path);
            StageState state = context.Candidates.StageStateOf(entry);
            JObject data = new JObject { ["verdictFile"] = Path.GetFileName(path), ["changeSetId"] = verdict.ChangeSetId, ["pass"] = state.VerdictPassed, ["label"] = state.Label, ["problems"] = new JArray(entry.Problems.Select(d => StudioJson.ToToken(d)).ToArray()) };
            WorkflowRunner.Json("mech/verdict-recorded.json", data);
            StudioCandidatesWindow.Open(entry.Id);
            WorkflowRunner.Recording(true);
            WorkflowRunner.Shot("mech-verdict", "Candidate panel after Record verdict: " + state.Label + "; Admit is " + (state.VerdictPassed ? "enabled" : "disabled") + ".", data);
            return true;
        }

        private static bool Admit()
        {
            CandidateEntry entry = S.EntryOf("mech") ?? throw new InvalidOperationException("no mechanism candidate");
            StudioUiContext context = S.Context;
            if (!context.Candidates.StageStateOf(entry).VerdictPassed)
            {
                WorkflowRunner.MarkFailed("no passing verdict: Admit stays disabled");
                SessionState.SetInt("GameCore.Studio.P32.Step", Workflows.For(WorkflowRunner.Workflow).Count - 1);
                return false;
            }

            St.Set("admitStartMs", WorkflowRunner.NowMs);
            AdmissionResult result = context.Candidates.Admit(entry, false);
            WorkflowRunner.Json("mech/admit-result.json", result.ToJson());
            WorkflowRunner.Shot("mech-admit", "Admit pressed: " + result.Outcome + " (" + result.Detail + "); the package is written and the Editor recompiles.", result.ToJson());
            return true;
        }

        private static bool WaitScenario(string which, double timeoutSeconds)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return false;
            }

            string id = S.IdOf("mech");
            StageState state = CandidateStaging.StateOf(S.Context.Runtime, id);
            ValidationScenario? scenario = which == "admission" ? state.Admission : state.Undo;
            bool done = scenario != null && scenario.Status != ScenarioStatus.Pending;
            if (!done && St.Waits++ < timeoutSeconds)
            {
                return false;
            }

            long started = (long?)St.Get(which == "admission" ? "admitStartMs" : "undoStartMs") ?? 0;
            JObject r = St.Req("mech");
            r[which == "admission" ? "admitMs" : "undoMs"] = WorkflowRunner.NowMs - started;
            St.SetReq("mech", r);
            JObject data = new JObject { ["scenario"] = scenario == null ? null : StudioJson.ToToken(scenario), ["wallMs"] = WorkflowRunner.NowMs - started, ["label"] = state.Label };
            WorkflowRunner.Json("mech/" + which + ".json", data);
            S.CopyJournal("mech", id, which);
            WorkflowRunner.Shot("mech-" + which, "stage." + which + ": " + (scenario?.Status.ToString() ?? "none") + " after " + data["wallMs"] + " ms (" + scenario?.Detail + ").", data);
            return true;
        }

        private static bool PlayCheck()
        {
            string key = "play";
            if (St.Get(key) == null)
            {
                St.Set(key, "entering");
                EditorSceneManager.SaveOpenScenes();
                EditorSceneManager.OpenScene(S.BootScene, OpenSceneMode.Single);
                EditorApplication.EnterPlaymode();
                return false;
            }

            if (!EditorApplication.isPlaying)
            {
                return false;
            }

            GameApplicationRoot? root = GameApplication.Current;
            if ((root == null || root.State != GameApplicationState.Running) && St.Waits++ < 60)
            {
                return false;
            }

            JObject doc = (JObject?)St.Get("mechDoc") ?? new JObject();
            List<string> loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name).Where(n => n.IndexOf("Pressure", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Mechanism", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Plate", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            JObject data = new JObject { ["rootState"] = root?.State.ToString(), ["assemblies"] = new JArray(loaded.ToArray()), ["changeSetId"] = doc["changeSetId"] };
            WorkflowRunner.Json("mech/play.json", data);
            StudioViewportWindow viewport = S.Viewport();
            viewport.SetMode(ViewportMode.Play);
            WorkflowRunner.Shot("mech-play", "Play after the admission: application root " + (root?.State.ToString() ?? "absent") + "; mechanism assemblies loaded: " + (loaded.Count == 0 ? "none" : string.Join(", ", loaded)) + ".", data);
            return true;
        }

        private static bool UndoAdmission()
        {
            if (EditorApplication.isPlaying)
            {
                return false;
            }

            string id = S.IdOf("mech");
            StudioHistoryWindow.Open();
            HistoryPanelView? panel = EditorWindow.GetWindow<StudioHistoryWindow>().View;
            St.Set("undoStartMs", WorkflowRunner.NowMs);
            HistoryResult result = panel != null ? panel.Undo(id) : S.Context.Runtime.History.Undo(id);
            JObject data = new JObject { ["ok"] = result.Ok, ["state"] = result.State?.ToString(), ["admission"] = panel?.LastAdmission?.ToJson(), ["diagnostics"] = new JArray(result.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray()) };
            WorkflowRunner.Json("mech/undo-result.json", data);
            WorkflowRunner.Shot("mech-undo", "History Undo of the admission (routed to StageAdmission.Undo): " + (panel?.LastAdmission?.Outcome.ToString() ?? result.State?.ToString()) + ".", data);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ helpers

        /// <summary>Runs <paramref name="run"/> only when the tag has a candidate; otherwise records the gap and advances.</summary>
        private static bool Guard(string tag, Func<bool> run)
        {
            if (S.EntryOf(tag) == null)
            {
                string key = "guard." + tag + "." + WorkflowRunner.CurrentName;
                if (St.Get(key) == null)
                {
                    St.Set(key, true);
                    WorkflowRunner.Log(tag + "-skipped", WorkflowRunner.CurrentName + " skipped: no candidate (" + (St.Req(tag)["result"] ?? "no request") + ")", null);
                }

                return true;
            }

            return run();
        }

        private static bool AppliedOr(string tag, Func<bool> run)
        {
            CandidateEntry? entry = S.EntryOf(tag);
            if (entry == null || entry.Stage != CandidateStage.Applied)
            {
                WorkflowRunner.Log(tag + "-not-applied", "nothing to undo: candidate " + (entry?.Stage.ToString() ?? "absent"), null);
                return true;
            }

            return run();
        }

        private static bool ReviewOrSkip(string tag)
        {
            return Guard(tag, () => S.Preview(tag).Run());
        }

        private static void RejectIfOpen(string tag)
        {
            CandidateEntry? entry = S.EntryOf(tag);
            if (entry != null && entry.IsOpen)
            {
                S.Context.Candidates.Reject(entry, "P3.2: left open at the end of the run; rejected.");
                S.CopyJournal(tag, entry.Id, "rejected");
            }
        }

        private static bool HashStep(string tag, string label, params string[] paths)
        {
            return S.Snapshot(tag, label, paths).Run();
        }

        /// <summary>Every AuthoredEntity in the open scenes (name, position, authoring id).</summary>
        private static void Roster(string tag, string label)
        {
            JArray list = new JArray();
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (behaviour == null || behaviour.GetType().Name != "AuthoredEntity")
                {
                    continue;
                }

                SerializedProperty? id = new SerializedObject(behaviour).FindProperty("authoringId");
                list.Add(new JObject { ["name"] = behaviour.name, ["position"] = S.Vec(behaviour.transform.position), ["authoringId"] = id?.stringValue, ["scene"] = behaviour.gameObject.scene.name });
            }

            WorkflowRunner.Json(tag + "/roster-" + label + ".json", new JObject { ["count"] = list.Count, ["entities"] = list });
        }

        /// <summary>inspect.describe of every operation target (after state).</summary>
        private static void DescribeTargets(string tag, string label)
        {
            CandidateEntry? entry = S.EntryOf(tag);
            if (entry == null)
            {
                return;
            }

            StudioRuntime runtime = S.Context.Runtime;
            JObject result = new JObject();
            foreach (Operation op in entry.ChangeSet.Operations)
            {
                if (op.Target == null || op.Target.Kind == AuthoringKind.Location || result[op.Target.AuthoringId ?? op.OpId] != null)
                {
                    continue;
                }

                OperationResult output = runtime.Registry.Invoke(BuiltInToolIds.InspectDescribe, op.Target, new JObject { ["includeReferrers"] = false });
                result[op.Target.AuthoringId ?? op.OpId] = new JObject { ["op"] = op.OpId, ["tool"] = op.Tool, ["status"] = output.Status.ToString(), ["output"] = output.Output?.DeepClone(), ["detail"] = output.Detail };
            }

            WorkflowRunner.Json(tag + "/describe-" + label + ".json", result);
        }

        private static void DescribeAsset(string tag, string label, string path)
        {
            StudioUiContext context = S.Context;
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(path);
            AuthoringRef? reference = context.Selection.RefOf(asset);
            OperationResult output = context.Runtime.Registry.Invoke(BuiltInToolIds.InspectDescribe, reference, new JObject { ["includeReferrers"] = false });
            WorkflowRunner.Json(tag + "/describe-" + label + ".json", new JObject { ["asset"] = path, ["status"] = output.Status.ToString(), ["output"] = output.Output?.DeepClone(), ["detail"] = output.Detail });
        }

        private static void ViewportPng(string tag, string label)
        {
            StudioViewportWindow viewport = S.Viewport();
            viewport.RenderNow();
            byte[]? png = viewport.EncodeViewportPng();
            if (png != null)
            {
                string path = Path.Combine(WorkflowRunner.OutputDir, tag, "viewport-" + label + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, png);
            }
        }

        private static void DialoguePreview(string tag, string label, string graph, string facts)
        {
            JObject data = new JObject { ["graph"] = graph, ["facts"] = facts };
            try
            {
                data["output"] = S.InvokeTool("dialogue.preview", AssetDatabase.LoadMainAssetAtPath(graph), facts)?.ToString();
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                data["error"] = (error.InnerException ?? error).Message;
            }

            WorkflowRunner.Json(tag + "/dialogue-preview-" + WorkflowRunner.Slug(label) + ".json", data);
        }

        private static void QuestSimulate(string tag, string label)
        {
            JObject data = new JObject();
            foreach (string path in new[] { "start", "start;advance:1", "start;collect:oil_flask=1", "start;collect:oil_flask=2" })
            {
                try
                {
                    data[path] = S.InvokeTool("quest.simulate", AssetDatabase.LoadMainAssetAtPath(Quest), path)?.ToString();
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    data[path] = "error: " + (error.InnerException ?? error).Message;
                }
            }

            WorkflowRunner.Json(tag + "/quest-simulate-" + label + ".json", data);
        }

        /// <summary>snake_case identifiers in the candidate's operation args that contain <paramref name="word"/>.</summary>
        private static IEnumerable<string> FactsMentioned(string tag, string word)
        {
            CandidateEntry? entry = S.EntryOf(tag);
            if (entry == null)
            {
                return Array.Empty<string>();
            }

            HashSet<string> facts = new HashSet<string>();
            foreach (Operation op in entry.ChangeSet.Operations)
            {
                foreach (Match match in Regex.Matches(op.Args?.ToString() ?? string.Empty, "[a-z][a-z0-9]*(?:_[a-z0-9]+)+"))
                {
                    if (match.Value.Contains(word))
                    {
                        facts.Add(match.Value);
                    }
                }
            }

            if (facts.Count == 0)
            {
                facts.Add("shrine_lit");
            }

            return facts;
        }

        // ----------------------------------------------------------------------------------------------- media

        private static bool GenerateImage(string tag, string prompt, string assetPath, double maxCost)
        {
            string key = "gen." + tag;
            if (St.Get(key) == null)
            {
                St.Set(key, WorkflowRunner.NowMs);
                StudioUiContext context = S.Context;
                EtosMediaGenerator media = new EtosMediaGenerator(context.Gateway, context.Runtime, EtosStudioSession.Gateway?.Queue ?? throw new InvalidOperationException("no etos gateway"));
                WorkflowRunner.Recording(true);
                WorkflowRunner.Write(tag + "/prompt.txt", prompt + "\n");
                Pending[key] = media.GenerateImageAsync(prompt, assetPath, 256, maxCost, null).ContinueWith(done => (object)done);
                WorkflowRunner.Log(tag + "-generate", "asset.generate (generate.image) with max_cost_usd " + maxCost.ToString(CultureInfo.InvariantCulture) + " -> " + assetPath, null);
                return false;
            }

            if (!Pending.TryGetValue(key, out Task<object>? pending) || !pending.IsCompleted)
            {
                return false;
            }

            Task<MediaImport> task = (Task<MediaImport>)pending.Result;
            long started = (long)St.Get(key)!;
            JObject data = new JObject { ["prompt"] = prompt, ["assetPath"] = assetPath, ["maxCostUsd"] = maxCost, ["ms"] = WorkflowRunner.NowMs - started };
            if (task.IsFaulted)
            {
                data["error"] = task.Exception?.GetBaseException().Message;
            }
            else
            {
                MediaImport import = task.Result;
                data["ok"] = import.Ok;
                data["provider"] = import.Result.Provider;
                data["providerSha256"] = import.Result.Sha256;
                data["providerBytes"] = import.Result.Bytes?.LongLength;
                data["opState"] = import.Result.State?.DeepClone();
                data["refusal"] = import.Result.Refusal == null ? null : StudioJson.ToToken(import.Result.Refusal);
                data["importedSha256"] = import.Artifact?.Sha256;
                data["fileSha256"] = S.Sha256Of(assetPath);
                data["journal"] = import.Report?.Entry.Id;
                data["journalState"] = import.Report?.State.ToString();
                data["problem"] = import.Problem == null ? null : StudioJson.ToToken(import.Problem);
                Texture2D? texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
                data["texture"] = texture == null ? null : texture.width + "x" + texture.height;
                if (import.Report != null)
                {
                    St.SetReq(tag, new JObject { ["ids"] = new JArray(import.Report.Entry.Id), ["id"] = import.Report.Entry.Id });
                    S.CopyJournal(tag, import.Report.Entry.Id, "import");
                }

                if (import.Result.Sha256 != null)
                {
                    St.Set("lastImageSha", import.Result.Sha256);
                }
            }

            WorkflowRunner.Json(tag + "/generate.json", data);
            StudioHistoryWindow.Open();
            WorkflowRunner.Shot(tag + "-generated", "asset.generate (image, max_cost_usd " + maxCost.ToString(CultureInfo.InvariantCulture) + "): " + (data["ok"]?.ToString() ?? "error") + (data["refusal"] != null && data["refusal"]!.Type != JTokenType.Null ? " refused " + data["refusal"]!["code"] : string.Empty) + "; provider sha256 " + data["providerSha256"] + "; imported " + assetPath + " (" + data["texture"] + "), journal " + data["journal"] + ".", data);
            return true;
        }

        private static bool AssignIcon(string tag, string itemPath, string texturePath)
        {
            StudioUiContext context = S.Context;
            if (!File.Exists(Path.Combine(WorkflowRunner.ProjectRoot, texturePath)))
            {
                WorkflowRunner.Log(tag + "-assign-skipped", "no imported texture at " + texturePath, null);
                return true;
            }

            UnityEngine.Object item = AssetDatabase.LoadMainAssetAtPath(itemPath);
            AuthoringRef target = context.Runtime.Resolver.BuildRef(item, null, true) ?? throw new InvalidOperationException("no ref for " + itemPath);
            Sprite? sprite = AssetDatabase.LoadAllAssetsAtPath(texturePath).OfType<Sprite>().FirstOrDefault();
            JObject data = new JObject { ["texture"] = texturePath, ["importedAsSprite"] = sprite != null, ["textureType"] = (AssetImporter.GetAtPath(texturePath) as TextureImporter)?.textureType.ToString() };
            Operation assign = new Operation("op1", "assign", target, new JObject { ["field"] = "icon", ["value"] = texturePath });
            ChangeSet changeSet = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent("Assign the generated lantern icon to the Lantern item", IntentOrigin.Manual), new[] { assign });
            ApplyReport report = context.Runtime.Engine.Apply(changeSet);
            data["state"] = report.State.ToString();
            data["outcomes"] = new JArray(report.Outcomes.Select(o => StudioJson.ToToken(o)).ToArray());
            data["diagnostics"] = new JArray(report.Diagnostics.Select(d => StudioJson.ToToken(d)).ToArray());
            data["applyMs"] = Math.Round(report.Milliseconds, 1);
            St.SetReq(tag + "-assign", new JObject { ["ids"] = new JArray(changeSet.Id), ["id"] = changeSet.Id });
            WorkflowRunner.Json(tag + "/assign.json", data);
            S.CopyJournal(tag, changeSet.Id, "assign");
            StudioHistoryWindow.Open();
            EditorWindow.GetWindow<StudioHistoryWindow>().View?.Select(changeSet.Id);
            WorkflowRunner.Shot(tag + "-assigned", "`assign` Lantern.icon = " + texturePath + ": " + report.State + " (sprite import: " + (sprite != null) + ").", data);
            return true;
        }

        private static bool ProbeGenerateVoice(string tag, string graphPath)
        {
            UnityEngine.Object graph = AssetDatabase.LoadMainAssetAtPath(graphPath);
            JArray attempts = new JArray();
            for (int node = 0; node < 8; node++)
            {
                try
                {
                    object? result = S.InvokeTool("dialogue.generateVoice", graph, node, string.Empty);
                    attempts.Add(new JObject { ["node"] = node, ["result"] = result == null ? null : JToken.FromObject(result) });
                    break;
                }
                catch (Exception error) when (!(error is OutOfMemoryException))
                {
                    attempts.Add(new JObject { ["node"] = node, ["error"] = (error.InnerException ?? error).Message });
                }
            }

            WorkflowRunner.Json(tag + "/dialogue-generateVoice.json", new JObject { ["graph"] = graphPath, ["attempts"] = attempts });
            WorkflowRunner.Log(tag + "-generateVoice", "dialogue.generateVoice (catalog tool): " + attempts.Last.ToString(Newtonsoft.Json.Formatting.None), null);
            return true;
        }

        private static bool GenerateSpeech(string tag, string assetPath)
        {
            string key = "tts." + tag;
            if (St.Get(key) == null)
            {
                St.Set(key, WorkflowRunner.NowMs);
                StudioUiContext context = S.Context;
                EtosMediaGenerator media = new EtosMediaGenerator(context.Gateway, context.Runtime, EtosStudioSession.Gateway?.Queue ?? throw new InvalidOperationException("no etos gateway"));
                Pending[key] = media.GenerateSpeechAsync("The bell has been silent for three winters. Find its clapper, and Thornwick will sleep again.", assetPath, null, 0.10, null).ContinueWith(done => (object)done);
                return false;
            }

            if (!Pending.TryGetValue(key, out Task<object>? pending) || !pending.IsCompleted)
            {
                return false;
            }

            Task<MediaImport> task = (Task<MediaImport>)pending.Result;
            JObject data = new JObject { ["assetPath"] = assetPath, ["ms"] = WorkflowRunner.NowMs - (long)St.Get(key)! };
            if (task.IsFaulted)
            {
                data["error"] = task.Exception?.GetBaseException().Message;
            }
            else
            {
                MediaImport import = task.Result;
                data["ok"] = import.Ok;
                data["provider"] = import.Result.Provider;
                data["sha256"] = import.Artifact?.Sha256;
                data["mediaType"] = import.Artifact?.MediaType;
                data["fileSha256"] = S.Sha256Of(assetPath);
                data["journal"] = import.Report?.Entry.Id;
                data["refusal"] = import.Result.Refusal == null ? null : StudioJson.ToToken(import.Result.Refusal);
                data["clip"] = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath) is AudioClip clip ? Math.Round(clip.length, 2) + " s" : null;
                if (import.Report != null)
                {
                    St.SetReq(tag, new JObject { ["ids"] = new JArray(import.Report.Entry.Id), ["id"] = import.Report.Entry.Id });
                    S.CopyJournal(tag, import.Report.Entry.Id, "import");
                }
            }

            WorkflowRunner.Json(tag + "/tts.json", data);
            WorkflowRunner.Shot(tag + "-tts", "TTS voice line through the media gateway (tts op, max_cost_usd 0.10): " + (data["ok"]?.ToString() ?? "error") + "; " + assetPath + " " + data["clip"] + ", journal " + data["journal"] + ".", data);
            return true;
        }

        private static bool DescribeLast(string tag)
        {
            string sha = St.Str("lastImageSha");
            string key = "describe." + tag;
            if (sha.Length == 0)
            {
                WorkflowRunner.Log(tag + "-describe-skipped", "no generated image to describe", null);
                return true;
            }

            if (St.Get(key) == null)
            {
                St.Set(key, WorkflowRunner.NowMs);
                StudioUiContext context = S.Context;
                EtosMediaGenerator media = new EtosMediaGenerator(context.Gateway, context.Runtime, EtosStudioSession.Gateway?.Queue ?? throw new InvalidOperationException("no etos gateway"));
                Pending[key] = media.DescribeAsync(sha, "Describe this image in one sentence.").ContinueWith(done => (object)done);
                return false;
            }

            if (!Pending.TryGetValue(key, out Task<object>? pending) || !pending.IsCompleted)
            {
                return false;
            }

            Task<OpResult> task = (Task<OpResult>)pending.Result;
            JObject data = new JObject { ["sha256"] = sha, ["ms"] = WorkflowRunner.NowMs - (long)St.Get(key)! };
            if (task.IsFaulted)
            {
                data["error"] = task.Exception?.GetBaseException().Message;
            }
            else
            {
                data["ok"] = task.Result.Succeeded;
                data["text"] = task.Result.Text;
                data["provider"] = task.Result.Provider;
                data["refusal"] = task.Result.Refusal == null ? null : StudioJson.ToToken(task.Result.Refusal);
            }

            WorkflowRunner.Json(tag + "/describe.json", data);
            WorkflowRunner.Log(tag + "-describe", "describe op: " + data.ToString(Newtonsoft.Json.Formatting.None), null);
            return true;
        }
    }
}
