// GameCore.Gameplay.Contracts.Narrative - data-only view models the narrative presenters push to the UI (P1.5).
//
// Presenters build these from committed slots after each pump and push them to the views only when something changed.
// They are immutable plain data: P1.5 binds them to UI Toolkit documents; Studio and tests read them headless. Asset
// references (portraits, icons, voice clips) are carried as untyped objects plus a string reference, so this assembly
// stays engine-free.
#nullable enable
using System.Collections.Generic;
using System.Text;

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>One option of a dialogue choice.</summary>
    public sealed class DialogueChoiceView
    {
        public DialogueChoiceView(int index, string text, bool available, string unavailableReason)
        {
            Index = index;
            Text = text ?? string.Empty;
            Available = available;
            UnavailableReason = unavailableReason ?? string.Empty;
        }

        /// <summary>The index dialogue.choose takes.</summary>
        public int Index { get; }

        public string Text { get; }

        /// <summary>False when the option's condition fails (the UI may hide or grey it).</summary>
        public bool Available { get; }

        public string UnavailableReason { get; }
    }

    /// <summary>The dialogue panel: the current line or choice of the active conversation.</summary>
    public sealed class DialogueViewModel
    {
        public DialogueViewModel(
            bool active,
            int graphKey,
            string graphName,
            int node,
            string kind,
            string speaker,
            string text,
            object? portrait,
            string portraitRef,
            bool canAdvance,
            IReadOnlyList<DialogueChoiceView> choices,
            int serial)
        {
            Active = active;
            GraphKey = graphKey;
            GraphName = graphName ?? string.Empty;
            Node = node;
            Kind = kind ?? string.Empty;
            Speaker = speaker ?? string.Empty;
            Text = text ?? string.Empty;
            Portrait = portrait;
            PortraitRef = portraitRef ?? string.Empty;
            CanAdvance = canAdvance;
            Choices = choices ?? System.Array.Empty<DialogueChoiceView>();
            Serial = serial;
        }

        public static DialogueViewModel Inactive { get; } = new DialogueViewModel(
            false, 0, string.Empty, -1, string.Empty, string.Empty, string.Empty, null, string.Empty, false, null!, 0);

        public bool Active { get; }

        public int GraphKey { get; }

        public string GraphName { get; }

        public int Node { get; }

        /// <summary><c>line</c> or <c>choice</c> (empty when inactive).</summary>
        public string Kind { get; }

        public string Speaker { get; }

        public string Text { get; }

        /// <summary>The portrait asset (a Sprite or Texture2D in Unity), or null.</summary>
        public object? Portrait { get; }

        public string PortraitRef { get; }

        /// <summary>True on a line: dialogue.advance moves on.</summary>
        public bool CanAdvance { get; }

        public IReadOnlyList<DialogueChoiceView> Choices { get; }

        /// <summary>dialogue.serial when the model was built: changes whenever a new node is shown.</summary>
        public int Serial { get; }

        public override string ToString()
        {
            if (!Active)
            {
                return "dialogue(inactive)";
            }

            var builder = new StringBuilder("dialogue(" + GraphName + "#" + Node + " " + Kind + " " + Speaker + ": " + Text);
            for (int i = 0; i < Choices.Count; i++)
            {
                builder.Append(" [").Append(Choices[i].Index).Append(Choices[i].Available ? "] " : "x] ").Append(Choices[i].Text);
            }

            return builder.Append(')').ToString();
        }
    }

    /// <summary>One objective line of a journal entry.</summary>
    public sealed class ObjectiveView
    {
        public ObjectiveView(int index, string text, int count, int required, bool done, int branch)
        {
            Index = index;
            Text = text ?? string.Empty;
            Count = count;
            Required = required;
            Done = done;
            Branch = branch;
        }

        public int Index { get; }

        public string Text { get; }

        public int Count { get; }

        public int Required { get; }

        public bool Done { get; }

        /// <summary>0 for an objective every path needs; b &gt; 0 for an objective of branch b.</summary>
        public int Branch { get; }
    }

    /// <summary>One quest as the journal shows it.</summary>
    public sealed class QuestView
    {
        public QuestView(
            int questKey,
            string title,
            int status,
            int stage,
            string stageTitle,
            string stageDescription,
            int branch,
            IReadOnlyList<ObjectiveView> objectives)
        {
            QuestKey = questKey;
            Title = title ?? string.Empty;
            Status = status;
            Stage = stage;
            StageTitle = stageTitle ?? string.Empty;
            StageDescription = stageDescription ?? string.Empty;
            Branch = branch;
            Objectives = objectives ?? System.Array.Empty<ObjectiveView>();
        }

        public int QuestKey { get; }

        public string Title { get; }

        /// <summary>QuestIds.Inactive/Active/Completed/Failed.</summary>
        public int Status { get; }

        public int Stage { get; }

        public string StageTitle { get; }

        public string StageDescription { get; }

        public int Branch { get; }

        /// <summary>The current stage's objectives.</summary>
        public IReadOnlyList<ObjectiveView> Objectives { get; }
    }

    /// <summary>The journal: every started quest.</summary>
    public sealed class JournalViewModel
    {
        public JournalViewModel(IReadOnlyList<QuestView> quests, int revision)
        {
            Quests = quests ?? System.Array.Empty<QuestView>();
            Revision = revision;
        }

        public IReadOnlyList<QuestView> Quests { get; }

        /// <summary>Increases whenever the presenter pushed a changed model.</summary>
        public int Revision { get; }
    }

    /// <summary>One occupied slot of an inventory.</summary>
    public sealed class InventorySlotView
    {
        public InventorySlotView(int slot, int itemKey, string itemName, int count, int unitWeight, int unitPrice, object? icon, string iconRef)
        {
            Slot = slot;
            ItemKey = itemKey;
            ItemName = itemName ?? string.Empty;
            Count = count;
            UnitWeight = unitWeight;
            UnitPrice = unitPrice;
            Icon = icon;
            IconRef = iconRef ?? string.Empty;
        }

        public int Slot { get; }

        public int ItemKey { get; }

        public string ItemName { get; }

        public int Count { get; }

        /// <summary>Grams per item.</summary>
        public int UnitWeight { get; }

        /// <summary>Price per item (a vendor's buy price in a vendor view, the item's base price otherwise).</summary>
        public int UnitPrice { get; }

        /// <summary>The icon asset (a Sprite or Texture2D in Unity), or null.</summary>
        public object? Icon { get; }

        public string IconRef { get; }
    }

    /// <summary>An inventory (the player's, or a vendor's stock).</summary>
    public sealed class InventoryViewModel
    {
        public InventoryViewModel(
            int inventoryKey,
            string title,
            IReadOnlyList<InventorySlotView> slots,
            int slotCapacity,
            int currency,
            int totalWeight,
            int maxWeight,
            int revision)
        {
            InventoryKey = inventoryKey;
            Title = title ?? string.Empty;
            Slots = slots ?? System.Array.Empty<InventorySlotView>();
            SlotCapacity = slotCapacity;
            Currency = currency;
            TotalWeight = totalWeight;
            MaxWeight = maxWeight;
            Revision = revision;
        }

        public int InventoryKey { get; }

        public string Title { get; }

        public IReadOnlyList<InventorySlotView> Slots { get; }

        public int SlotCapacity { get; }

        public int Currency { get; }

        public int TotalWeight { get; }

        /// <summary>0 = no weight limit.</summary>
        public int MaxWeight { get; }

        public int Revision { get; }

        /// <summary>Count of one item across all slots.</summary>
        public int CountOf(int itemKey)
        {
            int total = 0;
            for (int i = 0; i < Slots.Count; i++)
            {
                if (Slots[i].ItemKey == itemKey)
                {
                    total += Slots[i].Count;
                }
            }

            return total;
        }
    }

    /// <summary>Renders the dialogue panel (P1.5).</summary>
    public interface IDialogueView
    {
        void Show(DialogueViewModel model);
    }

    /// <summary>Renders the journal (P1.5).</summary>
    public interface IJournalView
    {
        void Show(JournalViewModel model);
    }

    /// <summary>Renders an inventory (P1.5).</summary>
    public interface IInventoryView
    {
        void Show(InventoryViewModel model);
    }

    /// <summary>The headless dialogue view: keeps the last model and counts pushes.</summary>
    public sealed class NullDialogueView : IDialogueView
    {
        public DialogueViewModel Last { get; private set; } = DialogueViewModel.Inactive;

        public int Pushes { get; private set; }

        public void Show(DialogueViewModel model)
        {
            Last = model ?? DialogueViewModel.Inactive;
            Pushes++;
        }
    }

    /// <summary>The headless journal view.</summary>
    public sealed class NullJournalView : IJournalView
    {
        public JournalViewModel? Last { get; private set; }

        public int Pushes { get; private set; }

        public void Show(JournalViewModel model)
        {
            Last = model;
            Pushes++;
        }
    }

    /// <summary>The headless inventory view.</summary>
    public sealed class NullInventoryView : IInventoryView
    {
        public InventoryViewModel? Last { get; private set; }

        public int Pushes { get; private set; }

        public void Show(InventoryViewModel model)
        {
            Last = model;
            Pushes++;
        }
    }
}
