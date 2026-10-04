// GameCore.Gameplay.Contracts - plain view models handed to the presentation interfaces (P1.4 -> P1.5).
//
// Plain classes with string, int and array members only: the producer (dialogue, quest, inventory presenters) builds a
// new instance from committed state and hands it to the view; the view copies what it shows and never writes back.
// Missing values are empty strings, zero and empty arrays, never null.
#nullable enable
using System;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>One shown dialogue line with its offered choices.</summary>
    public sealed class DialogueViewModel
    {
        /// <summary>Authoring id (or stable name) of the speaking NPC.</summary>
        public string SpeakerId = string.Empty;

        /// <summary>Display name of the speaker.</summary>
        public string SpeakerName = string.Empty;

        /// <summary>The line text (subtitle).</summary>
        public string Text = string.Empty;

        /// <summary>The offered choices in order; empty for a line without choices (advance continues).</summary>
        public string[] Choices = Array.Empty<string>();

        /// <summary>1 when the choice at the same index is offered but its condition is not met (shown greyed).</summary>
        public int[] ChoiceDisabled = Array.Empty<int>();

        /// <summary>Audio bank clip id of the line's voice, or empty.</summary>
        public string VoiceClip = string.Empty;

        /// <summary>The graph node id of the line (for tracing); 0 when unknown.</summary>
        public int NodeId;
    }

    /// <summary>One quest as the journal lists it.</summary>
    public sealed class JournalQuestEntry
    {
        public string QuestId = string.Empty;

        public string Title = string.Empty;

        /// <summary>0 inactive, 1 active, 2 completed, 3 failed (the quest.status slot).</summary>
        public int Status;

        /// <summary>The current stage index.</summary>
        public int Stage;

        public string StageTitle = string.Empty;

        /// <summary>Objective texts of the current stage.</summary>
        public string[] Objectives = Array.Empty<string>();

        public int[] ObjectiveCounts = Array.Empty<int>();

        public int[] ObjectiveTargets = Array.Empty<int>();

        /// <summary>1 when the objective at the same index is done.</summary>
        public int[] ObjectiveDone = Array.Empty<int>();
    }

    /// <summary>The journal: every known quest; <see cref="TrackedQuest"/> drives the HUD objective line.</summary>
    public sealed class JournalViewModel
    {
        public JournalQuestEntry[] Quests = Array.Empty<JournalQuestEntry>();

        /// <summary>Index into <see cref="Quests"/> of the tracked quest, or -1.</summary>
        public int TrackedQuest = -1;
    }

    /// <summary>One inventory slot as the inventory grid shows it.</summary>
    public sealed class InventorySlotEntry
    {
        public int SlotIndex;

        public string ItemId = string.Empty;

        public string DisplayName = string.Empty;

        /// <summary>Asset reference (path or id) of the item icon, or empty.</summary>
        public string IconRef = string.Empty;

        public int Count;

        /// <summary>1 when the item has a use effect.</summary>
        public int Usable;
    }

    /// <summary>The inventory: occupied slots, capacity and currency.</summary>
    public sealed class InventoryViewModel
    {
        public InventorySlotEntry[] Slots = Array.Empty<InventorySlotEntry>();

        public int Capacity;

        public int Currency;
    }
}
