// GameCore.Gameplay.Contracts - presentation interfaces shared by the gameplay plugin library (P1.3/P1.4/P1.5).
//
// Declared additively by P1.5 under the names and shapes the plan fixes; P1.3 (player: prompt, intents, feedback) and
// P1.4 (dialogue, journal, inventory, voice) call them, P1.5 (UI Toolkit and audio) implements them, and the
// integrator reconciles same-named declarations. Every member is engine-free: positions are System.Numerics.Vector3
// in metres (this assembly has no engine reference), view models are plain classes of strings, ints and arrays.
//
// A presentation interface never writes authoritative state. The input interfaces (IDialogueInput, IInventoryInput,
// IVolumeSettingsSink) are the other direction: the UI calls them with a player's choice, and their implementation
// (the owning gameplay package) turns it into that package's typed command.
#nullable enable
using System.Numerics;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>The interaction prompt of the focused interactable (P1.3 InteractionFocus calls it).</summary>
    public interface IPromptPresenter
    {
        /// <summary>Shows <paramref name="text"/>; <paramref name="enabled"/> false shows it greyed (condition not met).</summary>
        void Show(string text, bool enabled);

        void Hide();
    }

    /// <summary>A UI intent the player raised (P1.3's PlayerInputAdapter: Pause, Journal, Inventory, Interact/Confirm, Cancel).</summary>
    public enum UiIntent
    {
        Pause = 0,
        Journal = 1,
        Inventory = 2,
        Confirm = 3,
        Cancel = 4,
    }

    /// <summary>Receives the player's UI intents (implemented by the UI package).</summary>
    public interface IUiIntentSink
    {
        void Raise(UiIntent intent);
    }

    /// <summary>Plays a presentation-only feedback (footstep, door creak, pickup) at a point in metres.</summary>
    public interface IFeedbackSink
    {
        void Play(string feedbackId, Vector3 at);
    }

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
