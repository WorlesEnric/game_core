// GameCore.Gameplay.Inventory - ItemDefinition (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Inventory;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Inventory
{
    /// <summary>An item.</summary>
    [Authorable(NarrativeKinds.Item, DisplayName = "Item", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "An item: stack size, weight, price, tags and what using it does.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Item", fileName = "Item")]
    public sealed class ItemDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "Display name.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Min = 1, Doc = "Most items in one inventory slot.")]
        [SerializeField] private int maxStack = 1;

        [AuthorField(Min = 0, Doc = "Weight of one item (grams).", Unit = "g")]
        [SerializeField] private int weight;

        [AuthorField(Min = 0, Doc = "Base price (vendors may override).")]
        [SerializeField] private int price;

        [AuthorField(Doc = "Free tags (key, quest, currency...).")]
        [SerializeField] private List<string> tags = new List<string>();

        [AuthorRef(Category = NarrativeKinds.ActionSet, Required = false, Doc = "What using the item does (run through the outbox).")]
        [SerializeField] private ActionSetDefinition? useActions;

        [AuthorRef(Category = "texture.sprite", Required = false, Doc = "Inventory icon (presentation only).")]
        [SerializeField] private Sprite? icon;

        public override string NarrativeKind => NarrativeKinds.Item;

        public string DisplayName => displayName.Length > 0 ? displayName : name;

        public int MaxStack => maxStack;

        public int Weight => weight;

        public int Price => price;

        public IReadOnlyList<string> Tags => tags;

        public ActionSetDefinition? UseActions => useActions;

        public Sprite? Icon => icon;

        public void Configure(string shownName, int stack, int grams, int basePrice, IEnumerable<string>? itemTags, ActionSetDefinition? use)
        {
            displayName = shownName ?? string.Empty;
            maxStack = Math.Max(1, stack);
            weight = Math.Max(0, grams);
            price = Math.Max(0, basePrice);
            tags = itemTags != null ? new List<string>(itemTags) : new List<string>();
            useActions = use;
        }
    }
}
