// GameCore.Gameplay.Contracts.Narrative - what every narrative definition asset exposes to the bake and the runtime.
//
// The narrative packages reference each other's definitions through ScriptableObject fields with [AuthorRef]
// categories, not through types (dialogue, quest and inventory depend only on logic). The bake and the model builder
// recognise a definition by these interfaces: its kind (the [Authorable] type id), authoring id, content stamp and, for
// facts, the fact name that derives the fact's key and slot.
#nullable enable

namespace GameCore.Gameplay.Contracts.Narrative
{
    /// <summary>The [Authorable] type ids of the narrative definitions.</summary>
    public static class NarrativeKinds
    {
        public const string Fact = "narrative.fact";
        public const string ConditionSet = "logic.conditionSet";
        public const string ActionSet = "logic.actionSet";
        public const string Rule = "logic.rule";
        public const string Graph = "dialogue.graph";
        public const string Quest = "quest.quest";
        public const string Item = "inventory.item";
        public const string Inventory = "inventory.inventory";
        public const string Vendor = "inventory.vendor";
        public const string LootTable = "inventory.lootTable";
        public const string WorldItem = "inventory.worldItem";
        public const string ContentSet = "logic.contentSet";

        /// <summary>
        /// The capability every narrative definition provides (NarrativeDefinitionAsset.Capabilities): the [AuthorRef]
        /// category of a reference that accepts any narrative definition (GameplayContentSet.definitions). Not a type id.
        /// </summary>
        public const string Definition = "narrative.definition";

        /// <summary>Every definition kind, in bake order (facts first: other definitions key their conditions by them).</summary>
        public static System.Collections.Generic.IReadOnlyList<string> All { get; } = System.Array.AsReadOnly(new[]
        {
            Fact, ConditionSet, ActionSet, Rule, Item, Inventory, Vendor, LootTable, WorldItem, Quest, Graph,
        });
    }

    /// <summary>A narrative definition asset (every P1.4 definition implements it).</summary>
    public interface INarrativeDefinition : IDefinitionAsset
    {
        /// <summary>One of <see cref="NarrativeKinds"/>.</summary>
        string NarrativeKind { get; }

        /// <summary>Mints the authoring id when missing; true when it changed.</summary>
        bool EnsureAuthoringId();

        /// <summary>Written by the bake.</summary>
        void SetContentStamp(string stamp);
    }

    /// <summary>A fact definition: the bake declares slot narrative.fact.&lt;FactName&gt; for it.</summary>
    public interface IFactDefinition : INarrativeDefinition
    {
        string FactName { get; }

        int InitialValue { get; }

        /// <summary>False = reset to the initial value when a conversation ends.</summary>
        bool Persistent { get; }
    }
}
