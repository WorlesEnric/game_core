// GameCore.Gameplay.Logic - ActionSetDefinition (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>A reusable list of actions.</summary>
    [Authorable(NarrativeKinds.ActionSet, DisplayName = "Action Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "Actions run in order: set facts, grant items, advance quests, change interactables, play cues, travel, show messages.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Action Set", fileName = "ActionSet")]
    public sealed class ActionSetDefinition : NarrativeDefinitionAsset
    {
        [AuthorField(Doc = "The actions, in order.")]
        [SerializeField] private List<ActionEntry> actions = new List<ActionEntry>();

        public override string NarrativeKind => NarrativeKinds.ActionSet;

        public IReadOnlyList<ActionEntry> Actions => actions;

        public void Configure(IEnumerable<ActionEntry> entries) => actions = new List<ActionEntry>(entries);

        public void Add(ActionEntry entry) => actions.Add(entry ?? throw new ArgumentNullException(nameof(entry)));
    }
}
