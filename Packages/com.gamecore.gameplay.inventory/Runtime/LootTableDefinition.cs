// GameCore.Gameplay.Inventory - LootTableDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>A loot table.</summary>
    [Authorable(NarrativeKinds.LootTable, DisplayName = "Loot Table", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Weighted loot rolled deterministically from a seed.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Loot Table", fileName = "LootTable")]
    public sealed class LootTableDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Min = 1, Doc = "Rolls per drop.")]
        [SerializeField] private int rolls = 1;

        [AuthorField(Doc = "Entries.")]
        [SerializeField] private List<LootEntryDefinition> entries = new List<LootEntryDefinition>();

        public override string NarrativeKind => NarrativeKinds.LootTable;

        public int Rolls => rolls;

        public IReadOnlyList<LootEntryDefinition> Entries => entries;

        public void Configure(int count, IEnumerable<LootEntryDefinition> list)
        {
            rolls = Math.Max(1, count);
            entries = new List<LootEntryDefinition>(list);
        }
    }
}
