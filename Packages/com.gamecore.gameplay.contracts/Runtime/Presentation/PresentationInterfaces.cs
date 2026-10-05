// GameCore.Gameplay.Contracts - presentation interfaces of the UI and audio (P1.5).
//
// The player-facing seams are P1.3's (PlayerContracts.cs: IPromptPresenter, UiIntent/IUiIntentSink, IFootstepSink;
// InteractionContracts.cs: IFeedbackSink) and the narrative views are P1.4's (Narrative/: IDialogueView, IJournalView,
// IInventoryView, IVoiceLinePlayer, INarrativeMessageSink); P1.5 implements them. What is declared here is the other
// direction and P1.5's own queries: the UI calls IDialogueInput, IInventoryInput and IVolumeSettingsSink with a
// player's choice, and their implementation (the game's boot, or the audio package) turns it into a typed command;
// IGameplayPauseQuery and IPlayerSettings are read by gameplay input and camera code. Every member is engine-free.
#nullable enable
namespace GameCore.Gameplay.Contracts
{
    /// <summary>The player's dialogue input (the game boot implements it over P1.4's DialogueRunner; the UI calls it).</summary>
    public interface IDialogueInput
    {
        /// <summary>Chooses the offered option <paramref name="option"/> (DialogueChoiceView.Index).</summary>
        void Choose(int option);

        /// <summary>Advances past a line without choices.</summary>
        void Advance();
    }

    /// <summary>The player's inventory input (the game boot implements it over P1.4's InventoryCommands; the UI calls it).</summary>
    public interface IInventoryInput
    {
        /// <summary>Uses (consumes one of) the item with stable key <paramref name="itemKey"/> (InventorySlotView.ItemKey).</summary>
        void Use(int itemKey);

        /// <summary>Drops one of the item with stable key <paramref name="itemKey"/>.</summary>
        void Drop(int itemKey);
    }

    /// <summary>Volume settings input (implemented by the audio package; the settings screen calls it).</summary>
    public interface IVolumeSettingsSink
    {
        /// <summary>Requests a channel volume in permille (0..1000); channel 0 master, 1 music, 2 sfx, 3 voice.</summary>
        void SetVolume(int channel, int permille);

        /// <summary>The committed volume of a channel in permille; false before the audio session exists.</summary>
        bool TryGetVolume(int channel, out int permille);
    }

    /// <summary>Whether a modal UI screen pauses gameplay input (P1.3's input source reads it each frame).</summary>
    public interface IGameplayPauseQuery
    {
        bool GameplayPaused { get; }
    }

    /// <summary>Non-gameplay player preferences the UI persists (camera code reads the sensitivity).</summary>
    public interface IPlayerSettings
    {
        /// <summary>Mouse/stick look sensitivity multiplier (1 = default).</summary>
        float LookSensitivity { get; }

        /// <summary>Raised after a preference changed and was persisted.</summary>
        event System.Action? Changed;
    }
}
