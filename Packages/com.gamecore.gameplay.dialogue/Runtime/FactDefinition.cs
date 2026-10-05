// GameCore.Gameplay.Dialogue - FactDefinition (its own file: Unity resolves a ScriptableObject script by file name).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Logic;
using GameCore.Rules.Gameplay.Dialogue;
using UnityEngine;

namespace GameCore.Gameplay.Dialogue
{
    /// <summary>A named world fact.</summary>
    [Authorable(NarrativeKinds.Fact, DisplayName = "Fact", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A named int32 world fact (narrative.fact.<name>), set by dialogue, rules and quests and read by conditions.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Fact", fileName = "Fact")]
    public sealed class FactDefinition : NarrativeDefinitionAsset, IFactDefinition
    {
        [AuthorField(Doc = "Fact name: lowercase letters, digits and underscores (the slot is narrative.fact.<name>).")]
        [SerializeField] private string factName = string.Empty;

        [AuthorField(Doc = "Value before anything sets it.")]
        [SerializeField] private int initialValue;

        [AuthorField(Doc = "False = the fact returns to its initial value when a conversation ends (a conversation-local flag).")]
        [SerializeField] private bool persistent = true;

        public override string NarrativeKind => NarrativeKinds.Fact;

        public override string DefinitionName => factName.Length > 0 ? factName : name;

        public string FactName => factName.Length > 0 ? factName : name;

        public int InitialValue => initialValue;

        public bool Persistent => persistent;

        public void Configure(string fact, int initial, bool keep)
        {
            factName = fact ?? string.Empty;
            initialValue = initial;
            persistent = keep;
        }
    }
}
