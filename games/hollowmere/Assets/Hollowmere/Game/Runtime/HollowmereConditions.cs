// Hollowmere - the condition vocabulary of autoplay `waituntil` lines and of the PlayMode tests (P3.1).
//
//   ready                         the world runs and the UI rig exists
//   screen <Name>                 the UI shows that screen (Hud, Menu, Pause, Save, Load, Ending, Journal, ...)
//   region <name...>              the player is in the region whose name contains the text (case-insensitive)
//   fact <name> [op] <value>      a fact compares (op: >= default, ==, !=, <=, <, >)
//   item <name> [op] <count>      the player's count of an item definition compares (>= default)
//   quest <inactive|active|completed|failed>
//   stage [op] <n>                the director quest's committed stage compares (>= default)
//   outcome <n>                   the ending outcome (1..n branch, -1 failed)
//   ending                        the ending screen was opened
//   dialogue <active|none>        a conversation shows / none shows
//   prompt <text...>              the interaction prompt shows and contains the text
//   near <x> <z> [radius]         the player is within radius (default 1.5 m) of (x, z)
//   restored                      a save was restored into this game
//   slot <name|n>                 the save slot exists (n: the manual slot slot-n)
//   lifecycle <stage>             graphical standalone lifecycle assertion; records evidence in the isolated save dir
#nullable enable
using System;
using System.Globalization;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using GameCore.Gameplay.Save;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Evaluates Hollowmere condition strings against the running game (see the file header).</summary>
    public static class HollowmereConditions
    {
        public static bool Check(HollowmereGame game, string condition, out string detail)
        {
            string[] words = (condition ?? string.Empty).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                detail = "empty condition";
                return false;
            }

            HollowmereDirector? director = game.Director;
            string verb = words[0].ToLowerInvariant();
            switch (verb)
            {
                case "lifecycle":
                    return HollowmereLifecycleAudit.Check(game, words.Length == 2 ? words[1] : string.Empty, out detail);
                case "ready":
                    detail = game.World != null ? game.World.Root.State.ToString() : "no world";
                    return game.World != null && game.Rig != null && game.World.Root.State == GameApplicationState.Running;
                case "screen":
                {
                    UiScreen screen = game.Rig != null ? game.Rig.Ui.Screen : UiScreen.None;
                    detail = "screen " + screen;
                    return words.Length > 1 && string.Equals(screen.ToString(), words[1], StringComparison.OrdinalIgnoreCase);
                }

                case "region":
                {
                    ManifestRegion? region = game.CurrentRegion();
                    string name = region != null ? region.name : string.Empty;
                    string wanted = string.Join(" ", words, 1, words.Length - 1);
                    detail = "region " + name;
                    return wanted.Length > 0 && name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                case "fact":
                case "item":
                {
                    if (words.Length < 2 || director == null)
                    {
                        detail = verb + ": needs a name and a director";
                        return false;
                    }

                    int value = verb == "fact" ? director.Fact(words[1]) : director.ItemCount(words[1]);
                    ParseComparison(words, 2, out string op, out int wanted);
                    detail = verb + " " + words[1] + "=" + value.ToString(CultureInfo.InvariantCulture);
                    return Compare(value, op, wanted);
                }

                case "quest":
                {
                    int status = director != null ? director.QuestStatus() : 0;
                    string name = status == QuestIds.Active ? "active" : status == QuestIds.Completed ? "completed" : status == QuestIds.Failed ? "failed" : "inactive";
                    detail = "quest " + name;
                    return words.Length > 1 && string.Equals(name, words[1], StringComparison.OrdinalIgnoreCase);
                }

                case "stage":
                {
                    int stage = director != null ? director.QuestStage() : 0;
                    ParseComparison(words, 1, out string op, out int wanted);
                    detail = "stage " + stage.ToString(CultureInfo.InvariantCulture);
                    return Compare(stage, op, wanted);
                }

                case "outcome":
                {
                    int outcome = director != null ? director.Outcome : 0;
                    detail = "outcome " + outcome.ToString(CultureInfo.InvariantCulture);
                    return words.Length > 1 && int.TryParse(words[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int wanted) && outcome == wanted;
                }

                case "ending":
                    detail = director != null ? "ending " + director.EndingShown + " " + director.EndingTitle : "no director";
                    return director != null && director.EndingShown;
                case "dialogue":
                {
                    bool active = game.Rig != null && game.Rig.Ui.Models.Dialogue.Visible;
                    detail = "dialogue " + (active ? "active" : "none");
                    return words.Length > 1 && (words[1].Equals("active", StringComparison.OrdinalIgnoreCase) ? active : !active);
                }

                case "prompt":
                {
                    bool visible = game.Rig != null && game.Rig.Ui.Models.Prompt.Visible;
                    string text = game.Rig != null ? game.Rig.Ui.Models.Prompt.Text : string.Empty;
                    string wanted = string.Join(" ", words, 1, words.Length - 1);
                    detail = "prompt " + (visible ? text : "(hidden)");
                    return visible && text.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                case "near":
                {
                    Vector3 at = game.PlayerPosition();
                    float x = words.Length > 1 ? ParseFloat(words[1]) : 0f;
                    float z = words.Length > 2 ? ParseFloat(words[2]) : 0f;
                    float radius = words.Length > 3 ? ParseFloat(words[3]) : 1.5f;
                    float distance = new Vector2(at.x - x, at.z - z).magnitude;
                    detail = "at " + HollowmereGame.F(at.x) + "," + HollowmereGame.F(at.z) + " distance " + HollowmereGame.F(distance);
                    return distance <= radius;
                }

                case "restored":
                    detail = "restores " + game.Restores.ToString(CultureInfo.InvariantCulture) + (game.RestoreFailure.Length > 0 ? " " + game.RestoreFailure : string.Empty);
                    return game.Restores > 0 && game.RestoreFailure.Length == 0;
                case "slot":
                {
                    ISaveSlotCatalog? catalog = game.Rig != null ? game.Rig.Ui.SlotCatalog : null;
                    catalog?.Refresh();
                    // "slot 1" is the manual slot "slot-1" (SaveSlotNaming); any other text is a slot name as is.
                    string slotName = words.Length > 1 && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int manual)
                        ? SaveSlotNaming.SlotName(manual)
                        : (words.Length > 1 ? words[1] : string.Empty);
                    bool exists = catalog != null && slotName.Length > 0 && catalog.TryGet(slotName, out SaveSlotHeader? header) && header != null;
                    detail = "slot " + (words.Length > 1 ? words[1] : "?") + (exists ? " exists" : " missing");
                    return exists;
                }

                default:
                    detail = "unknown condition verb '" + verb + "'";
                    return false;
            }
        }

        private static void ParseComparison(string[] words, int start, out string op, out int value)
        {
            op = ">=";
            value = 1;
            if (words.Length <= start)
            {
                return;
            }

            string first = words[start];
            if (first == ">=" || first == "==" || first == "!=" || first == "<=" || first == "<" || first == ">" || first == "=")
            {
                op = first == "=" ? "==" : first;
                if (words.Length > start + 1)
                {
                    int.TryParse(words[start + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
                }

                return;
            }

            int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool Compare(int value, string op, int wanted)
        {
            switch (op)
            {
                case "==":
                    return value == wanted;
                case "!=":
                    return value != wanted;
                case "<=":
                    return value <= wanted;
                case "<":
                    return value < wanted;
                case ">":
                    return value > wanted;
                default:
                    return value >= wanted;
            }
        }

        private static float ParseFloat(string text) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }
}
