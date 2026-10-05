// GameCore.Gameplay.Ui - UiRuntime: the UI's presentation runtime of one game (P1.5, catalog row 10).
//
// One runtime per game, across restores. It owns the UI world extension, the view models, the command dispatcher and
// the host action queue, and it implements the presentation interfaces the other gameplay packages call:
// IPromptPresenter, IDialogueView, IJournalView, IInventoryView, IUiIntentSink and IGameplayPauseQuery (registered in
// the world's PresentationServices on every attach). After each pump its presenter binder (headless-safe: it touches no
// engine object) reads the committed UI slots and the committed events since the last frame:
//   * ui.screen / ui.message -> the screen model (screen, pause, message text from the message table);
//   * ScreenChanged with a host action -> queued for the host (save, load, delete, continue, new game, restart, quit);
//   * RegionEntered of the focus traveller -> the HUD region banner.
// Host actions never run inside the pump: RunHostActions executes them between frames (UiHostDriver calls it from
// LateUpdate; EditMode tests call it directly). A save or load goes through the P1.2 SaveCommandHost; its outcome is
// shown on the save/load screen (status line and stable refusal code) and committed as ui.message. A successful
// restore replaces the root: the runtime shuts the old gameplay world down, attaches the plan to the restored root
// (seedSlots false), lets the game prepare the new world (scene loader, views) and raises WorldReplaced, then commits
// ui.command{Loaded} so the restored world shows the HUD.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Save;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Ui
{
    /// <summary>What the game does for new game, restart and quit (the boot code owns scenes and the application).</summary>
    public interface IUiSessionActions
    {
        /// <summary>Starts a fresh game after the current one was played; false when it cannot.</summary>
        bool NewGame(UiRuntime ui);

        /// <summary>Restarts from the beginning (ending screen, pause menu).</summary>
        bool Restart(UiRuntime ui);

        void Quit(UiRuntime ui);
    }

    /// <summary>Options of a UI runtime.</summary>
    public sealed class UiRuntimeOptions
    {
        public UiScreen StartScreen { get; set; } = UiScreen.Menu;

        public string GameTitle { get; set; } = string.Empty;

        public UiMessageTable Messages { get; set; } = new UiMessageTable(null);

        public int RegionBannerMs { get; set; } = 3000;

        /// <summary>Manual save slots the save/load screen lists (1..SaveSlotNaming.MaxManualSlots).</summary>
        public int SlotCount { get; set; } = 5;

        public IUiSessionActions? Session { get; set; }

        public UiSettingsStore? Settings { get; set; }
    }

    /// <summary>Well-known message ids the runtime commits as ui.message.</summary>
    public static class UiMessageIds
    {
        public const string Saved = "ui.saved";
        public const string Loaded = "ui.loaded";
        public const string Deleted = "ui.deleted";
        public const string NoSave = "ui.no-save";
        public const string SaveUnavailable = "ui.save-unavailable";
    }

    /// <summary>The UI presentation runtime of one game.</summary>
    public sealed class UiRuntime : IPromptPresenter, IDialogueView, IJournalView, IInventoryView, IUiIntentSink, IGameplayPauseQuery
    {
        private readonly UiRuntimeOptions options;
        private readonly Queue<ScreenChanged> hostActions = new Queue<ScreenChanged>();
        private readonly List<IPresentationBinder> viewBinders = new List<IPresentationBinder>();
        private readonly Dictionary<string, string> eventValues = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<CommittedEvent> events = new List<CommittedEvent>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly UiPresenterBinder binder;
        private EventCursor cursor;
        private long bannerUntilMs;
        private bool playedSinceBoot;
        private SaveService? saveService;
        private Action<GameplayWorld>? prepareRestored;

        public UiRuntime(UiRuntimeOptions? options)
        {
            this.options = options ?? new UiRuntimeOptions();
            Settings = this.options.Settings ?? new UiSettingsStore();
            Extension = new UiWorldExtension(this.options.StartScreen);
            Extension.Attached += OnAttached;
            Models = new UiViewModels();
            Models.Menu.Title = this.options.GameTitle;
            Dispatcher = new CommandDispatcher(this);
            binder = new UiPresenterBinder(this);
            Models.Settings.Sensitivity = Settings.LookSensitivity;
            Models.Settings.Fullscreen = Settings.Fullscreen;
        }

        public UiWorldExtension Extension { get; }

        public UiViewModels Models { get; }

        public CommandDispatcher Dispatcher { get; }

        public UiSettingsStore Settings { get; }

        public UiRuntimeOptions Options => options;

        public UiMessageTable Messages => options.Messages;

        /// <summary>The attached gameplay world (replaced after a restore); null before the first boot.</summary>
        public GameplayWorld? World { get; private set; }

        public UiCommandIssuer? Commands { get; private set; }

        /// <summary>The save command host (null until <see cref="UseSaves"/>).</summary>
        public SaveCommandHost? Saves { get; private set; }

        public ISaveSlotCatalog? SlotCatalog { get; private set; }

        /// <summary>Raised after a restore replaced the world, with (old world, restored world).</summary>
        public event Action<GameplayWorld, GameplayWorld>? WorldReplaced;

        /// <summary>Raised after every host action with its event and outcome text (tests, logs).</summary>
        public event Action<ScreenChanged, string>? HostActionDone;

        public int Attaches { get; private set; }

        public int Presents { get; private set; }

        public int HostActionsRun { get; private set; }

        public int ScreenChanges { get; private set; }

        /// <summary>The committed ui.screen of the attached world.</summary>
        public UiScreen Screen => (UiScreen)Models.Screen.Screen;

        public bool GameplayPaused => ScreenFlowRules.PausesGameplay(Screen);

        public int PendingHostActions => hostActions.Count;

        /// <summary>Adds an engine-side binder (the UI root's documents) to every world this runtime attaches to.</summary>
        public void AddViewBinder(IPresentationBinder viewBinder)
        {
            if (viewBinder == null)
            {
                throw new ArgumentNullException(nameof(viewBinder));
            }

            viewBinders.Add(viewBinder);
            World?.AddBinder(viewBinder);
        }

        /// <summary>
        /// Connects the game's save service: save/load/delete/continue use it, the slot list reads it, and a restore
        /// re-attaches the world. <paramref name="prepareRestoredWorld"/> configures a restored world before it shows
        /// (scene loader, views); it runs right after the attach.
        /// </summary>
        public void UseSaves(SaveService service, Action<GameplayWorld>? prepareRestoredWorld)
        {
            if (saveService != null)
            {
                saveService.RootChanged -= OnRootChanged;
            }

            saveService = service ?? throw new ArgumentNullException(nameof(service));
            prepareRestored = prepareRestoredWorld;
            Saves = new SaveCommandHost(service);
            SlotCatalog = new SaveServiceSlotCatalog(service);
            service.RootChanged += OnRootChanged;
            RefreshSaveRows();
        }

        /// <summary>The latest value of a committed event stream (binding sources event:region-entered, event:message).</summary>
        public string EventValue(string name) => eventValues.TryGetValue(name, out string? value) ? value : string.Empty;

        /// <summary>Reads a slot binding source against the attached world.</summary>
        public (bool found, int value) ReadSlotSource(UiBindingSource source)
        {
            GameplayWorld? world = World;
            if (world == null)
            {
                return (false, 0);
            }

            TargetId target;
            switch (source.Target)
            {
                case "session":
                    target = PresentationSlots.UiSessionTarget(world.Manifest.WorldId);
                    break;
                case "audio":
                    target = PresentationSlots.AudioSessionTarget(world.Manifest.WorldId);
                    break;
                case "focus":
                    target = world.Focus;
                    break;
                default:
                    if (!AuthoringIds.IsValid(source.Target))
                    {
                        return (false, 0);
                    }

                    target = AuthoringIds.TargetIdFor(source.Target);
                    break;
            }

            bool found = world.Slots.TryRead(target, UiSlotSources.OwnerOf(source), UiSlotSources.SlotOf(source), out int value);
            return (found, value);
        }

        // ------------------------------------------------------------------ presentation interfaces

        public void Show(string text, bool enabled)
        {
            Models.Prompt.Text = text;
            Models.Prompt.Enabled = enabled;
            Models.Prompt.Visible = !string.IsNullOrEmpty(text);
        }

        public void Hide()
        {
            Models.Prompt.Visible = false;
        }

        void IDialogueView.Show(DialogueViewModel vm) => Models.Dialogue.Apply(vm);

        void IDialogueView.Hide() => Models.Dialogue.Clear();

        public void Update(JournalViewModel vm)
        {
            string objective = Models.Journal.Apply(vm);
            Models.Hud.ObjectiveText = objective;
            Models.Hud.ObjectiveVisible = objective.Length > 0;
        }

        public void Update(InventoryViewModel vm) => Models.Inventory.Apply(vm);

        /// <summary>A player intent: opens/closes screens through ui.open/ui.close, or activates the dialogue choice.</summary>
        public void Raise(UiIntent intent)
        {
            UiCommandIssuer? issuer = Commands;
            if (issuer == null)
            {
                return;
            }

            UiScreen screen = issuer.State().ScreenValue;
            if (screen == UiScreen.Hud && Models.Dialogue.Visible)
            {
                if (intent == UiIntent.Confirm)
                {
                    Dispatcher.Dispatch(Models.Dialogue.HasChoices ? "choose.selected" : "advance");
                    return;
                }

                if (intent == UiIntent.Pause || intent == UiIntent.Cancel)
                {
                    // Pause stays available during a conversation; Cancel does not skip a line.
                    if (intent == UiIntent.Cancel)
                    {
                        return;
                    }
                }
            }

            UiIntentRules.Effect effect = UiIntentRules.Map(screen, (int)intent, out UiScreen open);
            switch (effect)
            {
                case UiIntentRules.Effect.Open:
                    issuer.Open(open);
                    break;
                case UiIntentRules.Effect.Close:
                    issuer.Close();
                    break;
                case UiIntentRules.Effect.Activate:
                    ActivateRequested?.Invoke();
                    break;
            }
        }

        /// <summary>Raised when a Confirm intent should activate the focused control (the UI root handles it).</summary>
        public event Action? ActivateRequested;

        /// <summary>Keyboard/gamepad navigation: moves the selection of the dialogue choices or the current screen's list.</summary>
        public void Navigate(int dx, int dy)
        {
            int delta = dy != 0 ? -Math.Sign(dy) : Math.Sign(dx);
            if (delta == 0)
            {
                return;
            }

            if (Screen == UiScreen.Hud && Models.Dialogue.Visible)
            {
                Models.Dialogue.Move(delta);
                return;
            }

            switch (Screen)
            {
                case UiScreen.Save:
                case UiScreen.Load:
                    int count = Math.Max(1, Models.SaveLoad.Rows.Count);
                    int next = Models.SaveLoad.Selected + delta;
                    Models.SaveLoad.Selected = next < 1 ? count : (next > count ? 1 : next);
                    break;
                case UiScreen.Journal:
                    Models.Journal.Select(Models.Journal.Selected + delta);
                    break;
                case UiScreen.Inventory:
                    int cells = Models.Inventory.SlotLabels.Length;
                    if (cells > 0)
                    {
                        int step = dy != 0 ? -Math.Sign(dy) * InventoryColumns : Math.Sign(dx);
                        int cell = ((Models.Inventory.Selected + step) % cells + cells) % cells;
                        Models.Inventory.Select(cell);
                    }

                    break;
            }
        }

        /// <summary>Columns of the inventory grid (vertical navigation steps by a row).</summary>
        public int InventoryColumns { get; set; } = 4;

        /// <summary>Sets the ending screen's text (P1.4's ending logic or the game calls it before opening the ending).</summary>
        public void SetEnding(string title, string body)
        {
            Models.Ending.Title = title;
            Models.Ending.Body = body;
        }

        // ------------------------------------------------------------------ host actions

        /// <summary>Runs the queued host actions (between frames, never inside the pump); returns how many ran.</summary>
        public int RunHostActions()
        {
            int ran = 0;
            while (hostActions.Count > 0)
            {
                ScreenChanged change = hostActions.Dequeue();
                string outcome = RunHostAction(change);
                ran++;
                HostActionsRun++;
                HostActionDone?.Invoke(change, outcome);
            }

            return ran;
        }

        private string RunHostAction(ScreenChanged change)
        {
            switch (change.HostAction)
            {
                case UiAction.SaveSlot:
                    return Save(SaveSlotNaming.SlotName(change.Argument));
                case UiAction.LoadSlot:
                    return Load(SaveSlotNaming.SlotName(change.Argument));
                case UiAction.DeleteSlot:
                    return Delete(SaveSlotNaming.SlotName(change.Argument));
                case UiAction.Continue:
                    return Continue();
                case UiAction.NewGame:
                    return options.Session != null && options.Session.NewGame(this) ? "new game: restarted" : "new game: no session actions";
                case UiAction.Restart:
                    return options.Session != null && options.Session.Restart(this) ? "restarted" : "restart: no session actions";
                case UiAction.Quit:
                    if (options.Session != null)
                    {
                        options.Session.Quit(this);
                        return "quit";
                    }

                    return "quit: no session actions";
                case UiAction.QuitToMenu:
                    return "back to the main menu";
                default:
                    return "no host action";
            }
        }

        private string Save(string slot)
        {
            SaveCommandHost? host = Saves;
            if (host == null)
            {
                return Unavailable();
            }

            SaveResult result = host.Handle(SaveCommand.Capture(slot));
            RefreshSaveRows();
            if (result.Refusal != null)
            {
                return Refused(result.Refusal.Code, result.Refusal.Hint, "save");
            }

            Models.SaveLoad.RefusalCode = string.Empty;
            Models.SaveLoad.Status = "Saved to " + slot;
            ShowMessage(UiMessageIds.Saved);
            return "saved " + slot;
        }

        private string Load(string slot)
        {
            SaveCommandHost? host = Saves;
            if (host == null)
            {
                return Unavailable();
            }

            SaveResult result = host.Handle(SaveCommand.Restore(slot));
            if (result.Refusal != null)
            {
                RefreshSaveRows();
                return Refused(result.Refusal.Code, result.Refusal.Hint, "load");
            }

            Models.SaveLoad.RefusalCode = string.Empty;
            Models.SaveLoad.Status = "Loaded " + slot;
            UiCommandIssuer? issuer = Commands;
            if (issuer != null)
            {
                issuer.Command(UiAction.Loaded, PresentationSlots.KeyOf(UiMessageIds.Loaded));
            }

            RefreshSaveRows();
            return "loaded " + slot;
        }

        private string Delete(string slot)
        {
            SaveCommandHost? host = Saves;
            if (host == null)
            {
                return Unavailable();
            }

            SaveResult result = host.Handle(SaveCommand.Delete(slot));
            RefreshSaveRows();
            if (result.Refusal != null)
            {
                return Refused(result.Refusal.Code, result.Refusal.Hint, "delete");
            }

            Models.SaveLoad.RefusalCode = string.Empty;
            Models.SaveLoad.Status = "Deleted " + slot;
            ShowMessage(UiMessageIds.Deleted);
            return "deleted " + slot;
        }

        private string Continue()
        {
            ISaveSlotCatalog? catalog = SlotCatalog;
            if (catalog == null)
            {
                return Unavailable();
            }

            catalog.Refresh();
            if (catalog.Slots.Count == 0)
            {
                ShowMessage(UiMessageIds.NoSave);
                return "continue: no save";
            }

            return Load(catalog.Slots[0].Slot);
        }

        private string Unavailable()
        {
            Models.SaveLoad.Available = false;
            Models.SaveLoad.RefusalCode = PresentationDiagnosticCodes.UiSaveUnavailable;
            Models.SaveLoad.Status = PresentationDiagnosticCodes.UiSaveUnavailable + ": this game has no save service";
            ShowMessage(UiMessageIds.SaveUnavailable);
            return PresentationDiagnosticCodes.UiSaveUnavailable;
        }

        private string Refused(SaveRefusalCode code, string hint, string what)
        {
            string id = SaveRefusal.IdOf(code);
            Models.SaveLoad.RefusalCode = id;
            Models.SaveLoad.Status = id + (hint.Length > 0 ? ": " + hint : string.Empty);
            ShowMessage(id);
            return what + " refused " + id;
        }

        private void ShowMessage(string id)
        {
            Commands?.Command(UiAction.ShowMessage, PresentationSlots.KeyOf(id));
        }

        /// <summary>Re-reads the slot catalog into the save/load rows.</summary>
        public void RefreshSaveRows()
        {
            ISaveSlotCatalog? catalog = SlotCatalog;
            Models.SaveLoad.Available = catalog != null;
            catalog?.Refresh();
            int count = Math.Max(1, Math.Min(options.SlotCount, SaveSlotNaming.MaxManualSlots));
            var rows = new List<SaveSlotRow>(count);
            for (int n = 1; n <= count; n++)
            {
                string name = SaveSlotNaming.SlotName(n);
                SaveSlotHeader? header = null;
                bool exists = catalog != null && catalog.TryGet(name, out header) && header != null;
                string label = exists
                    ? "Slot " + n + "  " + RegionName(header!.RegionId) + "  " + header.SavedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                      + "  step " + header.LogicalStep.ToString(CultureInfo.InvariantCulture)
                    : "Slot " + n + "  (empty)";
                rows.Add(new SaveSlotRow(n, name, exists, label));
            }

            Models.SaveLoad.SetRows(rows);
            if (Models.SaveLoad.Selected < 1 || Models.SaveLoad.Selected > count)
            {
                Models.SaveLoad.Selected = 1;
            }

            Models.Menu.ContinueAvailable = catalog != null && catalog.Slots.Count > 0;
        }

        // ------------------------------------------------------------------ attach and restore

        private void OnAttached(GameplayWorld world, UiModule module)
        {
            World = world;
            Commands = new UiCommandIssuer(world);
            cursor = new EventCursor(world.Root.World, EventSequence.Zero);
            Attaches++;
            playedSinceBoot = Attaches > 1 || Commands.State().ScreenValue == UiScreen.Hud;
            PresentationServices services = world.Presentation;
            services.Register<IPromptPresenter>(this);
            services.Register<IDialogueView>(this);
            services.Register<IJournalView>(this);
            services.Register<IInventoryView>(this);
            services.Register<IUiIntentSink>(this);
            services.Register<IGameplayPauseQuery>(this);
            services.Register<IPlayerSettings>(Settings);
            world.AddBinder(binder);
            for (int i = 0; i < viewBinders.Count; i++)
            {
                world.AddBinder(viewBinders[i]);
            }

            Present(world.Slots);
        }

        private void OnRootChanged(GameApplicationRoot previous, GameApplicationRoot restored)
        {
            GameplayWorld? old = World;
            if (old == null)
            {
                return;
            }

            old.Shutdown();
            GameplayWorld next = WorldBuilder.Attach(restored, old.Plan, false);
            prepareRestored?.Invoke(next);
            WorldReplaced?.Invoke(old, next);
        }

        // ------------------------------------------------------------------ per-frame presentation

        /// <summary>Reads committed UI state and events into the view models (called after each pump by the presenter binder).</summary>
        internal int Present(GameCore.Gameplay.Contracts.ICommittedSlotReader slots)
        {
            GameplayWorld? world = World;
            UiCommandIssuer? issuer = Commands;
            if (world == null || issuer == null)
            {
                return 0;
            }

            Presents++;
            UiState state = issuer.State();
            ScreenViewModel screen = Models.Screen;
            screen.Screen = state.Screen;
            screen.ScreenName = ((UiScreen)state.Screen).ToString();
            screen.GameplayPaused = ScreenFlowRules.PausesGameplay((UiScreen)state.Screen);
            string message = Messages.TextOf(state.Message);
            screen.MessageText = message;
            screen.MessageVisible = message.Length > 0;
            eventValues["message"] = message;
            eventValues["screen"] = screen.ScreenName;
            ReadEvents(world);
            Models.Hud.RegionBannerVisible = Models.Hud.RegionName.Length > 0 && clock.ElapsedMilliseconds < bannerUntilMs;
            RefreshVolumes(world);
            return 1;
        }

        private void ReadEvents(GameplayWorld world)
        {
            WorldMessagePlane? plane = world.Root.Host.Messages;
            if (plane == null)
            {
                return;
            }

            events.Clear();
            CommittedEventPage page = plane.ReadEvents(cursor, 256);
            cursor = page.NextCursor;
            events.AddRange(page.Events);
            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committed = events[i];
                if (ScreenChanged.TryDecode(committed, out ScreenChanged change))
                {
                    OnScreenChanged(change);
                }
                else if (committed.Schema.Equals(WorldDeclarations.RegionEnteredEvent) && WorldEvent.TryDecode(committed.Payload, out WorldEvent entered))
                {
                    OnRegionEntered(world, entered);
                }
            }
        }

        private void OnScreenChanged(ScreenChanged change)
        {
            ScreenChanges++;

            // A new game from a world that has not been played yet needs nothing: this world is the new game.
            bool fresh = change.HostAction == UiAction.NewGame && !playedSinceBoot;
            if (change.HostAction != UiAction.None && !fresh)
            {
                hostActions.Enqueue(change);
            }

            if (change.ToScreen == UiScreen.Hud)
            {
                playedSinceBoot = true;
            }

            if (change.ToScreen == UiScreen.Save || change.ToScreen == UiScreen.Load)
            {
                Models.SaveLoad.Mode = change.ToScreen == UiScreen.Save ? "Save" : "Load";
                if (change.FromScreen != change.ToScreen)
                {
                    Models.SaveLoad.Status = string.Empty;
                    Models.SaveLoad.RefusalCode = string.Empty;
                    RefreshSaveRows();
                }
            }
            else if (change.ToScreen == UiScreen.Menu)
            {
                RefreshSaveRows();
            }
            else if (change.ToScreen == UiScreen.Settings)
            {
                IReadOnlyList<ResolutionChoice> choices = Settings.ResolutionChoices();
                var labels = new string[choices.Count];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = choices[i].ToString();
                }

                Models.Settings.Resolutions = labels;
                Models.Settings.ResolutionIndex = SettingsRules.IndexOf(choices, Settings.Resolution);
                Models.Settings.Sensitivity = Settings.LookSensitivity;
                Models.Settings.Fullscreen = Settings.Fullscreen;
            }
        }

        private void OnRegionEntered(GameplayWorld world, WorldEvent entered)
        {
            if (!entered.Target.Equals(world.Focus))
            {
                return;
            }

            string name = RegionNameByKey(world, entered.B);
            Models.Hud.RegionName = name;
            eventValues["region-entered"] = name;
            bannerUntilMs = clock.ElapsedMilliseconds + Math.Max(0, options.RegionBannerMs);
        }

        private void RefreshVolumes(GameplayWorld world)
        {
            IVolumeSettingsSink? volumes = world.Presentation.Get<IVolumeSettingsSink>();
            Models.Settings.AudioAvailable = volumes != null;
            if (volumes == null)
            {
                return;
            }

            for (int channel = 0; channel < 4; channel++)
            {
                if (volumes.TryGetVolume(channel, out int permille))
                {
                    Models.Settings.SetVolumeOf(channel, permille);
                }
            }
        }

        private string RegionName(string regionId)
        {
            GameplayWorld? world = World;
            if (world == null || string.IsNullOrEmpty(regionId))
            {
                return regionId ?? string.Empty;
            }

            ManifestRegion? region = world.Manifest.FindRegion(regionId);
            return region != null ? region.name : regionId;
        }

        private static string RegionNameByKey(GameplayWorld world, int key)
        {
            IReadOnlyList<ManifestRegion> regions = world.Manifest.Regions;
            for (int i = 0; i < regions.Count; i++)
            {
                if (regions[i].key == key)
                {
                    return regions[i].name;
                }
            }

            return string.Empty;
        }

        /// <summary>The runtime's logic binder: active headless, touches no engine object.</summary>
        private sealed class UiPresenterBinder : IPresentationBinder
        {
            private readonly UiRuntime runtime;

            public UiPresenterBinder(UiRuntime runtime)
            {
                this.runtime = runtime;
            }

            public string BinderName => "gameplay.ui.presenter";

            public bool IsActive => true;

            public int Present(GameCore.Gameplay.Contracts.ICommittedSlotReader slots) => runtime.Present(slots);
        }
    }
}
