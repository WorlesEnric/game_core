// GameCore.Gameplay.Ui - the screens' view models (P1.5, catalog row 10).
//
// Each screen binds to one view model through UI Toolkit runtime data binding: the model implements
// INotifyBindablePropertyChanged and marks its bound properties [CreateProperty], so a DataBinding on an element
// (dataSourcePath = the property name) updates when the model raises propertyChanged. The models are plain objects:
// they are filled from committed slots, committed events and the presentation interfaces by the UI runtime on the
// main thread after each pump, they never write state, and they work headless (no panel is needed to fill or test them).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using Unity.Properties;
using UnityEngine.UIElements;

namespace GameCore.Gameplay.Ui
{
    /// <summary>Base of every UI view model: change notification and a version counter.</summary>
    public abstract class UiViewModel : INotifyBindablePropertyChanged
    {
        public event EventHandler<BindablePropertyChangedEventArgs>? propertyChanged;

        /// <summary>Incremented on every property change (tests and the preview tool read it).</summary>
        public int Version { get; private set; }

        /// <summary>Names of the bindable properties (the binding map's vm: paths are checked against it).</summary>
        public abstract IReadOnlyList<string> PropertyNames { get; }

        protected bool Set<T>(ref T field, T value, string name)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            Notify(name);
            return true;
        }

        protected bool SetArray(ref string[] field, string[] value, string name)
        {
            value ??= Array.Empty<string>();
            if (field.Length == value.Length)
            {
                bool same = true;
                for (int i = 0; i < value.Length; i++)
                {
                    if (!string.Equals(field[i], value[i], StringComparison.Ordinal))
                    {
                        same = false;
                        break;
                    }
                }

                if (same)
                {
                    return false;
                }
            }

            field = (string[])value.Clone();
            Notify(name);
            return true;
        }

        protected void Notify(string name)
        {
            Version++;
            propertyChanged?.Invoke(this, new BindablePropertyChangedEventArgs(name));
        }
    }

    /// <summary>The flow state: current screen, pause, message.</summary>
    public sealed class ScreenViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(Screen), nameof(ScreenName), nameof(GameplayPaused), nameof(MessageText), nameof(MessageVisible) };
        private int screen;
        private string screenName = "None";
        private bool gameplayPaused = true;
        private string messageText = string.Empty;
        private bool messageVisible;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public int Screen { get => screen; set => Set(ref screen, value, nameof(Screen)); }

        [CreateProperty] public string ScreenName { get => screenName; set => Set(ref screenName, value ?? string.Empty, nameof(ScreenName)); }

        [CreateProperty] public bool GameplayPaused { get => gameplayPaused; set => Set(ref gameplayPaused, value, nameof(GameplayPaused)); }

        [CreateProperty] public string MessageText { get => messageText; set => Set(ref messageText, value ?? string.Empty, nameof(MessageText)); }

        [CreateProperty] public bool MessageVisible { get => messageVisible; set => Set(ref messageVisible, value, nameof(MessageVisible)); }
    }

    /// <summary>The HUD: stamina, objective line, region banner.</summary>
    public sealed class HudViewModel : UiViewModel
    {
        private static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        {
            nameof(Stamina), nameof(StaminaVisible), nameof(ObjectiveText), nameof(QuestStageTitle), nameof(ObjectiveVisible), nameof(RegionName), nameof(RegionBannerVisible),
        });

        private float stamina = 100f;
        private bool staminaVisible;
        private string objectiveText = string.Empty;
        private string questStageTitle = string.Empty;
        private bool objectiveVisible;
        private string regionName = string.Empty;
        private bool regionBannerVisible;

        public override IReadOnlyList<string> PropertyNames => Names;

        /// <summary>Stamina 0..100 (percent).</summary>
        [CreateProperty] public float Stamina { get => stamina; set => Set(ref stamina, value, nameof(Stamina)); }

        [CreateProperty] public bool StaminaVisible { get => staminaVisible; set => Set(ref staminaVisible, value, nameof(StaminaVisible)); }

        [CreateProperty] public string ObjectiveText { get => objectiveText; set => Set(ref objectiveText, value ?? string.Empty, nameof(ObjectiveText)); }

        /// <summary>Current stage title of the first active quest; empty when none is active.</summary>
        [CreateProperty] public string QuestStageTitle { get => questStageTitle; set => Set(ref questStageTitle, value ?? string.Empty, nameof(QuestStageTitle)); }

        [CreateProperty] public bool ObjectiveVisible { get => objectiveVisible; set => Set(ref objectiveVisible, value, nameof(ObjectiveVisible)); }

        [CreateProperty] public string RegionName { get => regionName; set => Set(ref regionName, value ?? string.Empty, nameof(RegionName)); }

        [CreateProperty] public bool RegionBannerVisible { get => regionBannerVisible; set => Set(ref regionBannerVisible, value, nameof(RegionBannerVisible)); }
    }

    /// <summary>The interaction prompt (IPromptPresenter).</summary>
    public sealed class PromptViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(Text), nameof(Kind), nameof(Visible), nameof(Enabled) };
        private string text = string.Empty;
        private string kind = string.Empty;
        private bool visible;
        private bool enabled = true;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public string Text { get => text; set => Set(ref text, value ?? string.Empty, nameof(Text)); }

        /// <summary>PromptRequest.Kind: "npc", "door", "gate", "examinable", "point" or "switch".</summary>
        [CreateProperty] public string Kind { get => kind; set => Set(ref kind, value ?? string.Empty, nameof(Kind)); }

        [CreateProperty] public bool Visible { get => visible; set => Set(ref visible, value, nameof(Visible)); }

        [CreateProperty] public bool Enabled { get => enabled; set => Set(ref enabled, value, nameof(Enabled)); }
    }

    /// <summary>The dialogue panel (IDialogueView): speaker, line, choices with a keyboard/gamepad selection.</summary>
    public sealed class DialoguePanelViewModel : UiViewModel
    {
        private static readonly string[] Names =
        {
            nameof(Visible), nameof(Speaker), nameof(Text), nameof(Choices), nameof(HasChoices), nameof(Selected), nameof(VoiceClip), nameof(Received),
        };

        private bool visible;
        private string speaker = string.Empty;
        private string text = string.Empty;
        private string[] choices = Array.Empty<string>();
        private int[] choiceDisabled = Array.Empty<int>();
        private int[] choiceIndices = Array.Empty<int>();
        private bool hasChoices;
        private int selected;
        private string voiceClip = string.Empty;
        private int received;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public bool Visible { get => visible; set => Set(ref visible, value, nameof(Visible)); }

        [CreateProperty] public string Speaker { get => speaker; set => Set(ref speaker, value ?? string.Empty, nameof(Speaker)); }

        [CreateProperty] public string Text { get => text; set => Set(ref text, value ?? string.Empty, nameof(Text)); }

        [CreateProperty] public string[] Choices { get => choices; set => SetArray(ref choices, value, nameof(Choices)); }

        [CreateProperty] public bool HasChoices { get => hasChoices; set => Set(ref hasChoices, value, nameof(HasChoices)); }

        /// <summary>The highlighted choice (keyboard/gamepad navigation); -1 when there are no choices.</summary>
        [CreateProperty] public int Selected { get => selected; set => Set(ref selected, value, nameof(Selected)); }

        [CreateProperty] public string VoiceClip { get => voiceClip; set => Set(ref voiceClip, value ?? string.Empty, nameof(VoiceClip)); }

        /// <summary>How many view models the panel received (tests).</summary>
        [CreateProperty] public int Received { get => received; private set => Set(ref received, value, nameof(Received)); }

        /// <summary>The node id of the last line (tracing).</summary>
        public int NodeId { get; private set; }

        public bool IsChoiceEnabled(int index) =>
            index >= 0 && index < choices.Length && (index >= choiceDisabled.Length || choiceDisabled[index] == 0);

        /// <summary>
        /// Applies P1.4's dialogue model (DialoguePresenter pushes one per committed node change): an inactive model hides
        /// the panel; a choice node lists its choices (unavailable ones greyed and skipped by navigation).
        /// </summary>
        public void Apply(DialogueViewModel vm)
        {
            if (vm == null)
            {
                throw new ArgumentNullException(nameof(vm));
            }

            Received = received + 1;
            if (!vm.Active)
            {
                Clear();
                return;
            }

            Speaker = vm.Speaker;
            Text = vm.Text;
            IReadOnlyList<DialogueChoiceView> offered = vm.Choices;
            var texts = new string[offered.Count];
            var disabled = new int[offered.Count];
            choiceIndices = new int[offered.Count];
            for (int i = 0; i < offered.Count; i++)
            {
                texts[i] = offered[i].Text;
                disabled[i] = offered[i].Available ? 0 : 1;
                choiceIndices[i] = offered[i].Index;
            }

            choiceDisabled = disabled;
            Choices = texts;
            HasChoices = choices.Length > 0;
            Selected = HasChoices ? FirstEnabled() : -1;
            VoiceClip = string.Empty;
            NodeId = vm.Node;
            Serial = vm.Serial;
            Visible = true;
        }

        /// <summary>The dialogue.serial of the last shown node.</summary>
        public int Serial { get; private set; }

        /// <summary>The option index (DialogueChoiceView.Index) of the choice shown at <paramref name="row"/>.</summary>
        public int OptionAt(int row) => row >= 0 && row < choiceIndices.Length ? choiceIndices[row] : row;

        /// <summary>Moves the selection by <paramref name="delta"/>, skipping disabled choices; returns the new index.</summary>
        public int Move(int delta)
        {
            if (!HasChoices || delta == 0)
            {
                return Selected;
            }

            int index = Selected;
            for (int i = 0; i < choices.Length; i++)
            {
                index = ((index + Math.Sign(delta)) % choices.Length + choices.Length) % choices.Length;
                if (IsChoiceEnabled(index))
                {
                    Selected = index;
                    break;
                }
            }

            return Selected;
        }

        public void Clear()
        {
            Visible = false;
            Choices = Array.Empty<string>();
            HasChoices = false;
            Selected = -1;
            VoiceClip = string.Empty;
        }

        private int FirstEnabled()
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (IsChoiceEnabled(i))
                {
                    return i;
                }
            }

            return 0;
        }
    }

    /// <summary>The journal screen (IJournalView): quest lines and the tracked quest's objectives.</summary>
    public sealed class JournalScreenViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(QuestLines), nameof(DetailLines), nameof(Selected), nameof(Empty) };
        private string[] questLines = Array.Empty<string>();
        private string[] detailLines = Array.Empty<string>();
        private int selected = -1;
        private bool empty = true;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public string[] QuestLines { get => questLines; set => SetArray(ref questLines, value, nameof(QuestLines)); }

        [CreateProperty] public string[] DetailLines { get => detailLines; set => SetArray(ref detailLines, value, nameof(DetailLines)); }

        [CreateProperty] public int Selected { get => selected; set => Set(ref selected, value, nameof(Selected)); }

        [CreateProperty] public bool Empty { get => empty; set => Set(ref empty, value, nameof(Empty)); }

        /// <summary>The last journal model received.</summary>
        public JournalViewModel Last { get; private set; } = new JournalViewModel(Array.Empty<QuestView>(), 0);

        /// <summary>The first active quest's current stage title, empty when none is active.</summary>
        public string TrackedStageTitle { get; private set; } = string.Empty;

        /// <summary>
        /// Applies P1.4's journal model; returns the HUD objective line of the tracked quest (the first active quest),
        /// empty when none is active.
        /// </summary>
        public string Apply(JournalViewModel vm)
        {
            if (vm == null)
            {
                throw new ArgumentNullException(nameof(vm));
            }

            Last = vm;
            IReadOnlyList<QuestView> quests = vm.Quests;
            var lines = new string[quests.Count];
            int tracked = -1;
            for (int i = 0; i < quests.Count; i++)
            {
                lines[i] = quests[i].Title + " - " + StatusText(quests[i].Status);
                if (tracked < 0 && quests[i].Status == QuestStatusActive)
                {
                    tracked = i;
                }
            }

            TrackedStageTitle = tracked >= 0 ? quests[tracked].StageTitle : string.Empty;
            QuestLines = lines;
            Empty = quests.Count == 0;
            int shown = tracked >= 0 ? tracked : (quests.Count > 0 ? 0 : -1);
            Selected = shown;
            DetailLines = shown >= 0 ? Details(quests[shown]) : Array.Empty<string>();
            return tracked >= 0 ? ObjectiveLine(quests[tracked]) : string.Empty;
        }

        /// <summary>QuestIds.Active (P1.4).</summary>
        public const int QuestStatusActive = 1;

        public void Select(int index)
        {
            IReadOnlyList<QuestView> quests = Last.Quests;
            if (index < 0 || index >= quests.Count)
            {
                return;
            }

            Selected = index;
            DetailLines = Details(quests[index]);
        }

        public static string StatusText(int status)
        {
            switch (status)
            {
                case 1: return "Active";
                case 2: return "Completed";
                case 3: return "Failed";
                default: return "Not started";
            }
        }

        /// <summary>The first unfinished objective of a quest's current stage, or its stage title.</summary>
        public static string ObjectiveLine(QuestView quest)
        {
            IReadOnlyList<ObjectiveView> objectives = quest.Objectives;
            for (int i = 0; i < objectives.Count; i++)
            {
                if (!objectives[i].Done)
                {
                    return objectives[i].Text + Counter(objectives[i]);
                }
            }

            return quest.StageTitle;
        }

        private static string[] Details(QuestView quest)
        {
            IReadOnlyList<ObjectiveView> objectives = quest.Objectives;
            var lines = new List<string> { quest.StageTitle };
            if (quest.StageDescription.Length > 0)
            {
                lines.Add(quest.StageDescription);
            }

            for (int i = 0; i < objectives.Count; i++)
            {
                lines.Add((objectives[i].Done ? "[x] " : "[ ] ") + objectives[i].Text + Counter(objectives[i]));
            }

            return lines.ToArray();
        }

        private static string Counter(ObjectiveView objective) =>
            objective.Required <= 1 ? string.Empty : " (" + objective.Count + "/" + objective.Required + ")";
    }

    /// <summary>The inventory screen (IInventoryView): a grid of slots, counts, the selected slot, currency.</summary>
    public sealed class InventoryScreenViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(SlotLabels), nameof(SlotCounts), nameof(Selected), nameof(SelectedName), nameof(Currency), nameof(SelectedUsable) };
        private string[] slotLabels = Array.Empty<string>();
        private string[] slotCounts = Array.Empty<string>();
        private int selected = -1;
        private string selectedName = string.Empty;
        private int currency;
        private bool selectedUsable;

        public override IReadOnlyList<string> PropertyNames => Names;

        /// <summary>One label per grid cell (capacity cells; empty cells are empty strings).</summary>
        [CreateProperty] public string[] SlotLabels { get => slotLabels; set => SetArray(ref slotLabels, value, nameof(SlotLabels)); }

        [CreateProperty] public string[] SlotCounts { get => slotCounts; set => SetArray(ref slotCounts, value, nameof(SlotCounts)); }

        [CreateProperty] public int Selected { get => selected; set => Set(ref selected, value, nameof(Selected)); }

        [CreateProperty] public string SelectedName { get => selectedName; set => Set(ref selectedName, value ?? string.Empty, nameof(SelectedName)); }

        [CreateProperty] public int Currency { get => currency; set => Set(ref currency, value, nameof(Currency)); }

        [CreateProperty] public bool SelectedUsable { get => selectedUsable; set => Set(ref selectedUsable, value, nameof(SelectedUsable)); }

        /// <summary>The last inventory model received (null before the first push).</summary>
        public InventoryViewModel? Last { get; private set; }

        /// <summary>Applies P1.4's inventory model: one grid cell per slot of the capacity; every occupied slot can be used.</summary>
        public void Apply(InventoryViewModel vm)
        {
            if (vm == null)
            {
                throw new ArgumentNullException(nameof(vm));
            }

            Last = vm;
            IReadOnlyList<InventorySlotView> slots = vm.Slots;
            int cells = Math.Max(vm.SlotCapacity, 0);
            for (int i = 0; i < slots.Count; i++)
            {
                cells = Math.Max(cells, slots[i].Slot + 1);
            }

            var labels = new string[cells];
            var counts = new string[cells];
            for (int i = 0; i < cells; i++)
            {
                labels[i] = string.Empty;
                counts[i] = string.Empty;
            }

            for (int i = 0; i < slots.Count; i++)
            {
                InventorySlotView slot = slots[i];
                if (slot.Slot < 0)
                {
                    continue;
                }

                labels[slot.Slot] = slot.ItemName;
                counts[slot.Slot] = slot.Count > 1 ? "x" + slot.Count : string.Empty;
            }

            SlotLabels = labels;
            SlotCounts = counts;
            Currency = vm.Currency;
            Select(selected >= 0 && selected < cells ? selected : (cells > 0 ? 0 : -1));
        }

        public void Select(int index)
        {
            Selected = index;
            InventorySlotView? entry = Find(index);
            SelectedName = entry != null ? entry.ItemName : string.Empty;
            SelectedUsable = entry != null;
        }

        /// <summary>The occupied slot at a grid index, or null.</summary>
        public InventorySlotView? Find(int index)
        {
            IReadOnlyList<InventorySlotView> slots = Last != null ? Last.Slots : Array.Empty<InventorySlotView>();
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Slot == index)
                {
                    return slots[i];
                }
            }

            return null;
        }
    }

    /// <summary>The main menu: whether Continue is offered.</summary>
    public sealed class MenuViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(ContinueAvailable), nameof(Title) };
        private bool continueAvailable;
        private string title = string.Empty;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public bool ContinueAvailable { get => continueAvailable; set => Set(ref continueAvailable, value, nameof(ContinueAvailable)); }

        [CreateProperty] public string Title { get => title; set => Set(ref title, value ?? string.Empty, nameof(Title)); }
    }

    /// <summary>The settings screen: four volumes (committed audio slots), look sensitivity, resolution, fullscreen.</summary>
    public sealed class SettingsViewModel : UiViewModel
    {
        private static readonly string[] Names =
        {
            nameof(MasterVolume), nameof(MusicVolume), nameof(SfxVolume), nameof(VoiceVolume), nameof(Sensitivity), nameof(Resolutions),
            nameof(ResolutionIndex), nameof(Fullscreen), nameof(AudioAvailable),
        };

        private int masterVolume = 800;
        private int musicVolume = 800;
        private int sfxVolume = 800;
        private int voiceVolume = 800;
        private float sensitivity = 1f;
        private string[] resolutions = Array.Empty<string>();
        private int resolutionIndex = -1;
        private bool fullscreen;
        private bool audioAvailable;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public int MasterVolume { get => masterVolume; set => Set(ref masterVolume, value, nameof(MasterVolume)); }

        [CreateProperty] public int MusicVolume { get => musicVolume; set => Set(ref musicVolume, value, nameof(MusicVolume)); }

        [CreateProperty] public int SfxVolume { get => sfxVolume; set => Set(ref sfxVolume, value, nameof(SfxVolume)); }

        [CreateProperty] public int VoiceVolume { get => voiceVolume; set => Set(ref voiceVolume, value, nameof(VoiceVolume)); }

        [CreateProperty] public float Sensitivity { get => sensitivity; set => Set(ref sensitivity, value, nameof(Sensitivity)); }

        [CreateProperty] public string[] Resolutions { get => resolutions; set => SetArray(ref resolutions, value, nameof(Resolutions)); }

        [CreateProperty] public int ResolutionIndex { get => resolutionIndex; set => Set(ref resolutionIndex, value, nameof(ResolutionIndex)); }

        [CreateProperty] public bool Fullscreen { get => fullscreen; set => Set(ref fullscreen, value, nameof(Fullscreen)); }

        /// <summary>False when no audio session is registered (the volume sliders are disabled).</summary>
        [CreateProperty] public bool AudioAvailable { get => audioAvailable; set => Set(ref audioAvailable, value, nameof(AudioAvailable)); }

        public int VolumeOf(int channel)
        {
            switch (channel)
            {
                case 0: return MasterVolume;
                case 1: return MusicVolume;
                case 2: return SfxVolume;
                default: return VoiceVolume;
            }
        }

        public void SetVolumeOf(int channel, int permille)
        {
            switch (channel)
            {
                case 0: MasterVolume = permille; break;
                case 1: MusicVolume = permille; break;
                case 2: SfxVolume = permille; break;
                case 3: VoiceVolume = permille; break;
            }
        }
    }

    /// <summary>One row of the save/load screen.</summary>
    public sealed class SaveSlotRow
    {
        public SaveSlotRow(int slot, string name, bool exists, string label)
        {
            Slot = slot;
            Name = name;
            Exists = exists;
            Label = label;
        }

        public int Slot { get; }

        public string Name { get; }

        public bool Exists { get; }

        public string Label { get; }

        public override string ToString() => Label;
    }

    /// <summary>The save/load screen: one row per manual slot, the last outcome (refusal code and hint).</summary>
    public sealed class SaveLoadViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(Mode), nameof(SlotLabels), nameof(Selected), nameof(Status), nameof(RefusalCode), nameof(Available) };
        private string mode = "Save";
        private string[] slotLabels = Array.Empty<string>();
        private int selected;
        private string status = string.Empty;
        private string refusalCode = string.Empty;
        private bool available;

        public override IReadOnlyList<string> PropertyNames => Names;

        /// <summary>"Save" or "Load".</summary>
        [CreateProperty] public string Mode { get => mode; set => Set(ref mode, value ?? string.Empty, nameof(Mode)); }

        [CreateProperty] public string[] SlotLabels { get => slotLabels; set => SetArray(ref slotLabels, value, nameof(SlotLabels)); }

        /// <summary>The highlighted slot number (1-based); 0 when none.</summary>
        [CreateProperty] public int Selected { get => selected; set => Set(ref selected, value, nameof(Selected)); }

        /// <summary>The outcome line of the last save/load/delete ("Saved to slot 1", "save.unsafe-state: ...").</summary>
        [CreateProperty] public string Status { get => status; set => Set(ref status, value ?? string.Empty, nameof(Status)); }

        /// <summary>The stable refusal code of the last refused operation, empty when it succeeded.</summary>
        [CreateProperty] public string RefusalCode { get => refusalCode; set => Set(ref refusalCode, value ?? string.Empty, nameof(RefusalCode)); }

        /// <summary>False when the game has no save service (the screen shows GP-UI-014).</summary>
        [CreateProperty] public bool Available { get => available; set => Set(ref available, value, nameof(Available)); }

        public IReadOnlyList<SaveSlotRow> Rows { get; private set; } = Array.Empty<SaveSlotRow>();

        public void SetRows(IReadOnlyList<SaveSlotRow> rows)
        {
            Rows = rows ?? Array.Empty<SaveSlotRow>();
            var labels = new string[Rows.Count];
            for (int i = 0; i < labels.Length; i++)
            {
                labels[i] = Rows[i].Label;
            }

            SlotLabels = labels;
        }
    }

    /// <summary>The ending screen.</summary>
    public sealed class EndingViewModel : UiViewModel
    {
        private static readonly string[] Names = { nameof(Title), nameof(Body) };
        private string title = "The End";
        private string body = string.Empty;

        public override IReadOnlyList<string> PropertyNames => Names;

        [CreateProperty] public string Title { get => title; set => Set(ref title, value ?? string.Empty, nameof(Title)); }

        [CreateProperty] public string Body { get => body; set => Set(ref body, value ?? string.Empty, nameof(Body)); }
    }

    /// <summary>Every view model of one UI runtime, by binding-map name.</summary>
    public sealed class UiViewModels
    {
        public ScreenViewModel Screen { get; } = new ScreenViewModel();

        public HudViewModel Hud { get; } = new HudViewModel();

        public PromptViewModel Prompt { get; } = new PromptViewModel();

        public DialoguePanelViewModel Dialogue { get; } = new DialoguePanelViewModel();

        public JournalScreenViewModel Journal { get; } = new JournalScreenViewModel();

        public InventoryScreenViewModel Inventory { get; } = new InventoryScreenViewModel();

        public MenuViewModel Menu { get; } = new MenuViewModel();

        public SettingsViewModel Settings { get; } = new SettingsViewModel();

        public SaveLoadViewModel SaveLoad { get; } = new SaveLoadViewModel();

        public EndingViewModel Ending { get; } = new EndingViewModel();

        /// <summary>The binding-map names, in canonical order.</summary>
        public static IReadOnlyList<string> ModelNames { get; } = Array.AsReadOnly(new[]
        {
            "screen", "hud", "prompt", "dialogue", "journal", "inventory", "menu", "settings", "saveload", "ending",
        });

        /// <summary>The model of a binding-map name ("hud", "dialogue", ...), or null.</summary>
        public UiViewModel? Find(string name)
        {
            switch (name)
            {
                case "screen": return Screen;
                case "hud": return Hud;
                case "prompt": return Prompt;
                case "dialogue": return Dialogue;
                case "journal": return Journal;
                case "inventory": return Inventory;
                case "menu": return Menu;
                case "settings": return Settings;
                case "saveload": return SaveLoad;
                case "ending": return Ending;
                default: return null;
            }
        }
    }
}
