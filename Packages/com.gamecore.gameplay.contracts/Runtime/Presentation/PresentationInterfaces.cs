// GameCore.Gameplay.Contracts - presentation interfaces shared by the gameplay plugin library (P1.4/P1.5).
//
// P1.3's contracts own the player-facing seams (PlayerContracts.cs: IPromptPresenter, UiIntent/IUiIntentSink,
// IFootstepSink; InteractionContracts.cs: IFeedbackSink), which P1.5 implements. The view and voice interfaces below
// (IDialogueView, IJournalView, IInventoryView, IVoiceLinePlayer) are declared additively by P1.5 under the names the
// plan fixes, until P1.4's declarations land; the integrator reconciles same-named declarations. Every member is
// engine-free: view models are plain classes of strings, ints and arrays.
//
// A presentation interface never writes authoritative state. The input interfaces (IDialogueInput, IInventoryInput,
// IVolumeSettingsSink) are the other direction: the UI calls them with a player's choice, and their implementation
// (the owning gameplay package) turns it into that package's typed command.
#nullable enable
namespace GameCore.Gameplay.Contracts
{
    /// <summary>The dialogue panel (P1.4's DialoguePresenter calls it on LineShown/ChoiceOffered/DialogueEnded).</summary>
    public interface IDialogueView
    {
        void Show(DialogueViewModel vm);

        void Hide();
    }

    /// <summary>The journal (P1.4's JournalPresenter calls it whenever quest state changes).</summary>
    public interface IJournalView
    {
        void Update(JournalViewModel vm);
    }

    /// <summary>The inventory screen (P1.4's InventoryPresenter calls it whenever inventory slots change).</summary>
    public interface IInventoryView
    {
        void Update(InventoryViewModel vm);
    }

    /// <summary>Plays dialogue voice lines (implemented by the audio package).</summary>
    public interface IVoiceLinePlayer
    {
        /// <summary>Plays <paramref name="clipRef"/> (an audio bank clip id) for <paramref name="speakerId"/>, interrupting a line in progress.</summary>
        void Play(string clipRef, string speakerId);

        void Stop();
    }

    /// <summary>The player's dialogue input (implemented by the dialogue package; the UI calls it).</summary>
    public interface IDialogueInput
    {
        /// <summary>Chooses the offered choice at <paramref name="index"/> (0-based).</summary>
        void Choose(int index);

        /// <summary>Advances past a line without choices.</summary>
        void Advance();
    }

    /// <summary>The player's inventory input (implemented by the inventory package; the UI calls it).</summary>
    public interface IInventoryInput
    {
        void Use(int slotIndex);

        void Drop(int slotIndex);
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
