// GameCore.Gameplay.Logic - ConditionSetDefinition (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Logic;
using UnityEngine;

namespace GameCore.Gameplay.Logic
{
    /// <summary>A reusable list of conditions.</summary>
    [Authorable(NarrativeKinds.ConditionSet, DisplayName = "Condition Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Conditions over facts, items, quests, regions and time, combined with all or any.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Condition Set", fileName = "ConditionSet")]
    public sealed class ConditionSetDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "All conditions must hold, or any one.")]
        [SerializeField] private ConditionMode mode = ConditionMode.All;

        [AuthorField(Doc = "The conditions, in evaluation order.")]
        [SerializeField] private List<ConditionEntry> conditions = new List<ConditionEntry>();

        public override string NarrativeKind => NarrativeKinds.ConditionSet;

        public ConditionMode Mode => mode;

        public IReadOnlyList<ConditionEntry> Conditions => conditions;

        public void Configure(ConditionMode combine, IEnumerable<ConditionEntry> entries)
        {
            mode = combine;
            conditions = new List<ConditionEntry>(entries);
        }

        public void Add(ConditionEntry entry) => conditions.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }
}
