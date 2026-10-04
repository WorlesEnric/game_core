// GameCore.Gameplay.Ui - CommandDispatcher: UI actions -> typed commands (P1.5, catalog row 10).
//
// The UI never writes state. A click, a slider change, a gamepad confirm or a binding-map command name arrives here and
// becomes exactly one of:
//   * a UI plugin command (ui.open{screen}, ui.close, ui.command{action, argument}) submitted to the world;
//   * a command of the owning gameplay package through its presentation input interface (IDialogueInput.Choose,
//     IInventoryInput.Use/Drop, IVolumeSettingsSink.SetVolume -> audio.setVolume);
//   * a machine preference write (look sensitivity, resolution, fullscreen) in the PlayerPrefs store.
//
// Command names (the binding map's command:<name>):
//   open.<screen> close newgame continue quit restart quitToMenu resume applySettings
//   save.<n> load.<n> delete.<n> slot.<n> (save or load by the current screen) select.<n> (highlight a slot/row)
//   choose.<i> choose.selected advance use drop item.<i>
//   volume.<channel> (value 0..1000) volumeStep.<channel> (value +1/-1) sensitivity (value) resolution (value = index)
//   fullscreen (value 0/1)
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Rules.Gameplay.Ui;

namespace GameCore.Gameplay.Ui
{
    /// <summary>The outcome of one dispatched UI command.</summary>
    public readonly struct UiDispatchResult
    {
        public UiDispatchResult(bool accepted, string detail)
        {
            Accepted = accepted;
            Detail = detail ?? string.Empty;
        }

        public bool Accepted { get; }

        public string Detail { get; }

        public override string ToString() => (Accepted ? "accepted" : "refused") + (Detail.Length > 0 ? ": " + Detail : string.Empty);
    }

    /// <summary>Turns UI commands into typed commands.</summary>
    public sealed class CommandDispatcher
    {
        private readonly UiRuntime runtime;
        private readonly List<string> history = new List<string>();

        public CommandDispatcher(UiRuntime runtime)
        {
            this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public int Dispatched { get; private set; }

        public int Refused { get; private set; }

        /// <summary>The last 32 dispatched command names with their outcome.</summary>
        public IReadOnlyList<string> History => history;

        /// <summary>The binding-map callback shape (name, value) -> accepted.</summary>
        public bool Invoke(string command, float value) => Dispatch(command, value).Accepted;

        public UiDispatchResult Dispatch(string command, float value = 0f)
        {
            UiDispatchResult result = Route(command ?? string.Empty, value);
            Dispatched++;
            if (!result.Accepted)
            {
                Refused++;
            }

            if (history.Count == 32)
            {
                history.RemoveAt(0);
            }

            history.Add(command + " -> " + result);
            return result;
        }

        private UiDispatchResult Route(string command, float value)
        {
            UiCommandIssuer? issuer = runtime.Commands;
            if (issuer == null)
            {
                return new UiDispatchResult(false, "no world is attached");
            }

            string verb = command;
            string arg = string.Empty;
            int dot = command.IndexOf('.');
            if (dot > 0)
            {
                verb = command.Substring(0, dot);
                arg = command.Substring(dot + 1);
            }

            switch (verb)
            {
                case "open":
                    return TryScreen(arg, out UiScreen screen) ? Receipt(issuer.Open(screen)) : new UiDispatchResult(false, "unknown screen '" + arg + "'");
                case "close":
                    return Receipt(issuer.Close());
                case "newgame":
                    return Receipt(issuer.Command(UiAction.NewGame));
                case "continue":
                    return Receipt(issuer.Command(UiAction.Continue));
                case "quit":
                    return Receipt(issuer.Command(UiAction.Quit));
                case "restart":
                    return Receipt(issuer.Command(UiAction.Restart));
                case "quitToMenu":
                    return Receipt(issuer.Command(UiAction.QuitToMenu));
                case "resume":
                    return Receipt(issuer.Command(UiAction.Resume));
                case "applySettings":
                    return Receipt(issuer.Command(UiAction.ApplySettings));
                case "save":
                    return Slot(issuer, UiAction.SaveSlot, arg);
                case "load":
                    return Slot(issuer, UiAction.LoadSlot, arg);
                case "delete":
                    return Slot(issuer, UiAction.DeleteSlot, arg);
                case "slot":
                    return Slot(issuer, issuer.State().ScreenValue == UiScreen.Load ? UiAction.LoadSlot : UiAction.SaveSlot, arg);
                case "select":
                    return Select(arg);
                case "choose":
                    return Choose(arg);
                case "advance":
                    return Input<IDialogueInput>(input => input.Advance(), "advance");
                case "use":
                case "drop":
                case "item":
                    return Item(verb, arg);
                case "volume":
                    return Volume(arg, (int)Math.Round(value));
                case "volumeStep":
                    return VolumeStep(arg, Math.Sign(value));
                case "sensitivity":
                    runtime.Settings.SetLookSensitivity(value);
                    runtime.Models.Settings.Sensitivity = runtime.Settings.LookSensitivity;
                    return new UiDispatchResult(true, "sensitivity " + runtime.Settings.LookSensitivity.ToString("0.##", CultureInfo.InvariantCulture));
                case "resolution":
                    return Resolution(TryIndex(arg, out int choice) ? choice : (int)Math.Round(value));
                case "fullscreen":
                    runtime.Settings.SetFullscreen(value > 0.5f);
                    runtime.Models.Settings.Fullscreen = runtime.Settings.Fullscreen;
                    return new UiDispatchResult(true, "fullscreen " + runtime.Settings.Fullscreen);
                default:
                    return new UiDispatchResult(false, "unknown command '" + command + "'");
            }
        }

        private static UiDispatchResult Receipt(CommandAdmissionReceipt receipt) =>
            new UiDispatchResult(receipt.Admitted, receipt.Admitted ? "admitted" : "not admitted: " + receipt.Result.Kind + "/" + receipt.Result.Reason);

        private static bool TryScreen(string name, out UiScreen screen)
        {
            for (int i = 0; i < ScreenFlowRules.ScreenCount; i++)
            {
                if (string.Equals(((UiScreen)i).ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    screen = (UiScreen)i;
                    return true;
                }
            }

            screen = UiScreen.None;
            return false;
        }

        private static bool TryIndex(string arg, out int index) =>
            int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out index);

        private UiDispatchResult Slot(UiCommandIssuer issuer, UiAction action, string arg)
        {
            int slot = arg == "selected" ? runtime.Models.SaveLoad.Selected : (TryIndex(arg, out int parsed) ? parsed : -1);
            if (slot < 1)
            {
                return new UiDispatchResult(false, "no slot selected");
            }

            runtime.Models.SaveLoad.Selected = slot;
            return Receipt(issuer.Command(action, slot));
        }

        private UiDispatchResult Select(string arg)
        {
            if (!TryIndex(arg, out int index))
            {
                return new UiDispatchResult(false, "bad index '" + arg + "'");
            }

            switch (runtime.Models.Screen.Screen)
            {
                case (int)UiScreen.Journal:
                    runtime.Models.Journal.Select(index);
                    break;
                case (int)UiScreen.Inventory:
                    runtime.Models.Inventory.Select(index);
                    break;
                default:
                    runtime.Models.SaveLoad.Selected = index + 1;
                    break;
            }

            return new UiDispatchResult(true, "selected " + index);
        }

        private UiDispatchResult Choose(string arg)
        {
            DialoguePanelViewModel dialogue = runtime.Models.Dialogue;
            int index = arg == "selected" || arg.Length == 0 ? dialogue.Selected : (TryIndex(arg, out int parsed) ? parsed : -1);
            if (!dialogue.Visible || !dialogue.HasChoices)
            {
                return new UiDispatchResult(false, "no choice is offered");
            }

            if (!dialogue.IsChoiceEnabled(index))
            {
                return new UiDispatchResult(false, "choice " + index + " is not available");
            }

            return Input<IDialogueInput>(input => input.Choose(index), "choose " + index);
        }

        private UiDispatchResult Item(string verb, string arg)
        {
            InventoryScreenViewModel inventory = runtime.Models.Inventory;
            if (verb == "item")
            {
                if (!TryIndex(arg, out int cell))
                {
                    return new UiDispatchResult(false, "bad item index '" + arg + "'");
                }

                inventory.Select(cell);
                return new UiDispatchResult(true, "selected item " + cell);
            }

            InventorySlotEntry? entry = inventory.Find(inventory.Selected);
            if (entry == null)
            {
                return new UiDispatchResult(false, "no item selected");
            }

            int slot = entry.SlotIndex;
            return verb == "use"
                ? Input<IInventoryInput>(input => input.Use(slot), "use " + slot)
                : Input<IInventoryInput>(input => input.Drop(slot), "drop " + slot);
        }

        private UiDispatchResult Volume(string arg, int permille)
        {
            if (!TryIndex(arg, out int channel) || channel < 0 || channel > 3)
            {
                return new UiDispatchResult(false, "unknown volume channel '" + arg + "'");
            }

            int clamped = SettingsRules.ClampVolume(permille);
            runtime.Models.Settings.SetVolumeOf(channel, clamped);
            return Input<IVolumeSettingsSink>(sink => sink.SetVolume(channel, clamped), "volume " + channel + " = " + clamped);
        }

        private UiDispatchResult VolumeStep(string arg, int direction)
        {
            if (!TryIndex(arg, out int channel) || channel < 0 || channel > 3)
            {
                return new UiDispatchResult(false, "unknown volume channel '" + arg + "'");
            }

            return Volume(arg, SettingsRules.StepVolume(runtime.Models.Settings.VolumeOf(channel), direction));
        }

        private UiDispatchResult Resolution(int index)
        {
            IReadOnlyList<ResolutionChoice> choices = runtime.Settings.ResolutionChoices();
            if (index < 0 || index >= choices.Count)
            {
                return new UiDispatchResult(false, "no resolution " + index);
            }

            runtime.Settings.SetResolution(choices[index]);
            runtime.Models.Settings.ResolutionIndex = index;
            return new UiDispatchResult(true, "resolution " + choices[index]);
        }

        private UiDispatchResult Input<T>(Action<T> act, string what) where T : class
        {
            GameCore.Gameplay.World.GameplayWorld? world = runtime.World;
            T? input = world != null ? world.Presentation.Get<T>() : null;
            if (input == null)
            {
                return new UiDispatchResult(false, what + ": no " + typeof(T).Name + " is registered");
            }

            act(input);
            return new UiDispatchResult(true, what);
        }
    }
}
