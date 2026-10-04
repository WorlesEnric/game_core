// GameCore.Rules.Gameplay.Ui - the screen flow state machine of the UI plugin (catalog row 10), pure and engine-free.
//
// The UI's authoritative state is three int32 slots on the UI session target: ui.screen, ui.returnTo and ui.message.
// Every ui.open / ui.close / ui.command is decided here, so the kernel system, the Studio preview and the dotnet tests
// apply one rule set:
//
//   * a transition table names which screen may open which (menu -> settings, pause -> save, hud -> journal, ...);
//   * settings, save and load are "sub-screens": opening one records the screen it came from in ui.returnTo, and a
//     close returns there; pause, journal and inventory close back to the HUD; the HUD, the main menu and the ending
//     have nothing to close;
//   * a command (new game, save slot n, load slot n, quit, restart, ...) is offered only on the screens that show its
//     button, and decides the next screen; a refusal is a stable code with the inputs it read;
//   * pause semantics: every screen except the HUD (and None before boot) pauses gameplay input; the world itself keeps
//     stepping so UI commands can commit (a command-driven world advances only on commands, so nothing else moves).
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Rules.Gameplay.Ui
{
    /// <summary>The value of the ui.screen slot.</summary>
    public enum UiScreen
    {
        None = 0,
        Hud = 1,
        Menu = 2,
        Pause = 3,
        Settings = 4,
        Save = 5,
        Load = 6,
        Ending = 7,
        Journal = 8,
        Inventory = 9,
    }

    /// <summary>The action of a ui.command (its first int32).</summary>
    public enum UiAction
    {
        None = 0,
        NewGame = 1,
        Continue = 2,
        SaveSlot = 3,
        LoadSlot = 4,
        DeleteSlot = 5,
        Quit = 6,
        Restart = 7,
        QuitToMenu = 8,
        Resume = 9,
        ApplySettings = 10,
        ShowMessage = 11,
        ClearMessage = 12,

        /// <summary>The host restored a save into this world: every screen goes to the HUD.</summary>
        Loaded = 13,
    }

    /// <summary>Why a UI transition was refused.</summary>
    public enum UiRefusal
    {
        None = 0,
        UnknownScreen = 1,
        TransitionNotAllowed = 2,
        NothingToClose = 3,
        CommandNotOffered = 4,
        Unchanged = 5,
        BadArgument = 6,
    }

    /// <summary>The three UI slots of one world.</summary>
    public readonly struct UiState
    {
        public UiState(int screen, int returnTo, int message)
        {
            Screen = screen;
            ReturnTo = returnTo;
            Message = message;
        }

        public int Screen { get; }

        public int ReturnTo { get; }

        public int Message { get; }

        public UiScreen ScreenValue => (UiScreen)Screen;

        public override string ToString() => "ui(" + (UiScreen)Screen + ", return " + (UiScreen)ReturnTo + ", message " + Message + ")";
    }

    /// <summary>The verdict of one UI command: the refusal, or the next state and the action the host performs.</summary>
    public readonly struct UiTransition
    {
        private UiTransition(UiRefusal refusal, UiState next, UiAction hostAction, int argument, string detail)
        {
            Refusal = refusal;
            Next = next;
            HostAction = hostAction;
            Argument = argument;
            Detail = detail ?? string.Empty;
        }

        public UiRefusal Refusal { get; }

        public bool Accepted => Refusal == UiRefusal.None;

        public UiState Next { get; }

        /// <summary>What the host does after the commit (save, load, quit, new game, restart); None for a pure screen change.</summary>
        public UiAction HostAction { get; }

        public int Argument { get; }

        /// <summary>The inputs a refusal read (explain trace).</summary>
        public string Detail { get; }

        public static UiTransition Accept(UiState next, UiAction hostAction, int argument) =>
            new UiTransition(UiRefusal.None, next, hostAction, argument, string.Empty);

        public static UiTransition Refuse(UiRefusal refusal, UiState current, string detail) =>
            new UiTransition(refusal, current, UiAction.None, 0, detail);

        public override string ToString() => Accepted ? "-> " + Next + (HostAction == UiAction.None ? string.Empty : " host " + HostAction) : Refusal + ": " + Detail;
    }

    /// <summary>The screen flow: allowed transitions, close targets, offered commands and pause semantics.</summary>
    public static class ScreenFlowRules
    {
        public const int ScreenCount = 10;

        private static readonly UiScreen[] None = Array.Empty<UiScreen>();

        /// <summary>True for a defined ui.screen value.</summary>
        public static bool IsKnown(int screen) => screen >= 0 && screen < ScreenCount;

        /// <summary>Settings, save and load return to the screen they were opened from.</summary>
        public static bool IsSubScreen(UiScreen screen) => screen == UiScreen.Settings || screen == UiScreen.Save || screen == UiScreen.Load;

        /// <summary>True when the screen pauses gameplay input (everything but the HUD).</summary>
        public static bool PausesGameplay(UiScreen screen) => screen != UiScreen.Hud;

        /// <summary>True when the screen is drawn above the HUD and takes focus (everything but None and the HUD).</summary>
        public static bool IsModal(UiScreen screen) => screen != UiScreen.Hud && screen != UiScreen.None;

        /// <summary>The screens <paramref name="from"/> may open with ui.open, in canonical order.</summary>
        public static IReadOnlyList<UiScreen> Opens(UiScreen from)
        {
            switch (from)
            {
                case UiScreen.None: return new[] { UiScreen.Hud, UiScreen.Menu };
                case UiScreen.Menu: return new[] { UiScreen.Settings, UiScreen.Load };
                case UiScreen.Hud: return new[] { UiScreen.Pause, UiScreen.Ending, UiScreen.Journal, UiScreen.Inventory };
                case UiScreen.Pause: return new[] { UiScreen.Settings, UiScreen.Save, UiScreen.Load };
                case UiScreen.Journal: return new[] { UiScreen.Ending, UiScreen.Inventory };
                case UiScreen.Inventory: return new[] { UiScreen.Ending, UiScreen.Journal };
                case UiScreen.Save: return new[] { UiScreen.Load };
                case UiScreen.Load: return new[] { UiScreen.Save };
                default: return None;
            }
        }

        public static bool MayOpen(UiScreen from, UiScreen to)
        {
            IReadOnlyList<UiScreen> allowed = Opens(from);
            for (int i = 0; i < allowed.Count; i++)
            {
                if (allowed[i] == to)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>ui.open{screen}.</summary>
        public static UiTransition Open(UiState current, int screen)
        {
            if (!IsKnown(screen) || !IsKnown(current.Screen))
            {
                return UiTransition.Refuse(UiRefusal.UnknownScreen, current, "screen " + screen + " from " + current.Screen);
            }

            var from = (UiScreen)current.Screen;
            var to = (UiScreen)screen;
            if (from == to)
            {
                return UiTransition.Refuse(UiRefusal.Unchanged, current, "already on " + to);
            }

            if (!MayOpen(from, to))
            {
                return UiTransition.Refuse(UiRefusal.TransitionNotAllowed, current, from + " cannot open " + to);
            }

            int returnTo = current.ReturnTo;
            if (IsSubScreen(to))
            {
                // A sub-screen opened from another sub-screen (save <-> load) keeps the original return target.
                returnTo = IsSubScreen(from) ? current.ReturnTo : (int)from;
            }

            return UiTransition.Accept(new UiState(screen, returnTo, current.Message), UiAction.None, 0);
        }

        /// <summary>Where a close of <paramref name="current"/> goes, or false when the screen has nothing to close.</summary>
        public static bool TryCloseTarget(UiState current, out UiScreen target)
        {
            var screen = (UiScreen)current.Screen;
            switch (screen)
            {
                case UiScreen.Settings:
                case UiScreen.Save:
                case UiScreen.Load:
                    target = IsKnown(current.ReturnTo) && (UiScreen)current.ReturnTo != UiScreen.None && !IsSubScreen((UiScreen)current.ReturnTo)
                        ? (UiScreen)current.ReturnTo
                        : UiScreen.Menu;
                    return true;
                case UiScreen.Pause:
                case UiScreen.Journal:
                case UiScreen.Inventory:
                    target = UiScreen.Hud;
                    return true;
                default:
                    target = screen;
                    return false;
            }
        }

        /// <summary>ui.close.</summary>
        public static UiTransition Close(UiState current)
        {
            if (!IsKnown(current.Screen))
            {
                return UiTransition.Refuse(UiRefusal.UnknownScreen, current, "screen " + current.Screen);
            }

            if (!TryCloseTarget(current, out UiScreen target))
            {
                return UiTransition.Refuse(UiRefusal.NothingToClose, current, (UiScreen)current.Screen + " has nothing to close");
            }

            int returnTo = IsSubScreen((UiScreen)current.Screen) ? (int)UiScreen.None : current.ReturnTo;
            return UiTransition.Accept(new UiState((int)target, returnTo, current.Message), UiAction.None, 0);
        }

        /// <summary>The screens that offer <paramref name="action"/>.</summary>
        public static bool IsOffered(UiAction action, UiScreen screen)
        {
            switch (action)
            {
                case UiAction.NewGame:
                case UiAction.Continue:
                    return screen == UiScreen.Menu;
                case UiAction.SaveSlot:
                    return screen == UiScreen.Save;
                case UiAction.LoadSlot:
                    return screen == UiScreen.Load;
                case UiAction.DeleteSlot:
                    return screen == UiScreen.Save || screen == UiScreen.Load;
                case UiAction.Quit:
                    return screen == UiScreen.Menu || screen == UiScreen.Pause || screen == UiScreen.Ending;
                case UiAction.Restart:
                    return screen == UiScreen.Ending || screen == UiScreen.Pause;
                case UiAction.QuitToMenu:
                    return screen == UiScreen.Pause || screen == UiScreen.Ending;
                case UiAction.Resume:
                    return screen == UiScreen.Pause;
                case UiAction.ApplySettings:
                    return screen == UiScreen.Settings;
                case UiAction.ShowMessage:
                case UiAction.ClearMessage:
                case UiAction.Loaded:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>ui.command{action, argument}.</summary>
        public static UiTransition Command(UiState current, int action, int argument)
        {
            if (!IsKnown(current.Screen))
            {
                return UiTransition.Refuse(UiRefusal.UnknownScreen, current, "screen " + current.Screen);
            }

            var screen = (UiScreen)current.Screen;
            var kind = (UiAction)action;
            if (action <= 0 || action > (int)UiAction.Loaded || !IsOffered(kind, screen))
            {
                return UiTransition.Refuse(UiRefusal.CommandNotOffered, current, kind + " is not offered on " + screen);
            }

            switch (kind)
            {
                case UiAction.NewGame:
                case UiAction.Continue:
                case UiAction.Restart:
                    return UiTransition.Accept(new UiState((int)UiScreen.Hud, (int)UiScreen.None, 0), kind, argument);
                case UiAction.Resume:
                    return UiTransition.Accept(new UiState((int)UiScreen.Hud, (int)UiScreen.None, current.Message), UiAction.None, 0);
                case UiAction.Loaded:
                    return UiTransition.Accept(new UiState((int)UiScreen.Hud, (int)UiScreen.None, argument), UiAction.None, argument);
                case UiAction.QuitToMenu:
                    return UiTransition.Accept(new UiState((int)UiScreen.Menu, (int)UiScreen.None, 0), kind, argument);
                case UiAction.Quit:
                    return UiTransition.Accept(new UiState((int)UiScreen.None, (int)UiScreen.None, 0), kind, argument);
                case UiAction.SaveSlot:
                case UiAction.LoadSlot:
                case UiAction.DeleteSlot:
                    if (argument < 1 || argument > SaveSlotNaming.MaxManualSlots)
                    {
                        return UiTransition.Refuse(UiRefusal.BadArgument, current, "slot " + argument + " is outside 1.." + SaveSlotNaming.MaxManualSlots);
                    }

                    return UiTransition.Accept(current, kind, argument);
                case UiAction.ShowMessage:
                    if (argument == current.Message)
                    {
                        return UiTransition.Refuse(UiRefusal.Unchanged, current, "message " + argument + " is already shown");
                    }

                    return UiTransition.Accept(new UiState(current.Screen, current.ReturnTo, argument), UiAction.None, argument);
                case UiAction.ClearMessage:
                    if (current.Message == 0)
                    {
                        return UiTransition.Refuse(UiRefusal.Unchanged, current, "no message is shown");
                    }

                    return UiTransition.Accept(new UiState(current.Screen, current.ReturnTo, 0), UiAction.None, 0);
                default:
                    return UiTransition.Accept(current, kind, argument);
            }
        }
    }

    /// <summary>How the UI turns the player's intents into UI commands on each screen.</summary>
    public static class UiIntentRules
    {
        /// <summary>Intent values (the order of GameCore.Gameplay.Contracts.UiIntent).</summary>
        public const int Pause = 0;
        public const int Journal = 1;
        public const int Inventory = 2;
        public const int Confirm = 3;
        public const int Cancel = 4;

        /// <summary>What an intent does on a screen.</summary>
        public enum Effect
        {
            Ignore = 0,
            Open = 1,
            Close = 2,
            /// <summary>Confirm on a screen with a focused button: the UI activates the focused control.</summary>
            Activate = 3,
        }

        /// <summary>The effect of <paramref name="intent"/> on <paramref name="screen"/>, and the screen an Open targets.</summary>
        public static Effect Map(UiScreen screen, int intent, out UiScreen open)
        {
            open = screen;
            switch (intent)
            {
                case Pause:
                    if (screen == UiScreen.Hud)
                    {
                        open = UiScreen.Pause;
                        return Effect.Open;
                    }

                    return screen == UiScreen.Pause || screen == UiScreen.Journal || screen == UiScreen.Inventory || ScreenFlowRules.IsSubScreen(screen)
                        ? Effect.Close
                        : Effect.Ignore;
                case Journal:
                    if (screen == UiScreen.Hud || screen == UiScreen.Inventory)
                    {
                        open = UiScreen.Journal;
                        return Effect.Open;
                    }

                    return screen == UiScreen.Journal ? Effect.Close : Effect.Ignore;
                case Inventory:
                    if (screen == UiScreen.Hud || screen == UiScreen.Journal)
                    {
                        open = UiScreen.Inventory;
                        return Effect.Open;
                    }

                    return screen == UiScreen.Inventory ? Effect.Close : Effect.Ignore;
                case Cancel:
                    return ScreenFlowRules.TryCloseTarget(new UiState((int)screen, (int)UiScreen.None, 0), out UiScreen _) ? Effect.Close : Effect.Ignore;
                case Confirm:
                    return ScreenFlowRules.IsModal(screen) ? Effect.Activate : Effect.Ignore;
                default:
                    return Effect.Ignore;
            }
        }
    }

    /// <summary>Manual save slot names (the save rules' `slot-N` form) and their bounds.</summary>
    public static class SaveSlotNaming
    {
        public const int MaxManualSlots = 9;

        public static string SlotName(int slot) => "slot-" + slot.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Parses `slot-N` (1..MaxManualSlots); false for any other name.</summary>
        public static bool TryParse(string? name, out int slot)
        {
            slot = 0;
            if (name == null || !name.StartsWith("slot-", StringComparison.Ordinal))
            {
                return false;
            }

            string digits = name.Substring(5);
            if (digits.Length == 0 || digits.Length > 2 || digits[0] == '0')
            {
                return false;
            }

            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] < '0' || digits[i] > '9')
                {
                    return false;
                }
            }

            slot = int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            return slot >= 1 && slot <= MaxManualSlots;
        }
    }
}
