// GameCore.Gameplay.Logic - GameplayContentSet (its own file: Unity resolves a ScriptableObject script by file name).
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
    /// <summary>The narrative content of one world: the definitions the bake learns (P1.4 compile hook).</summary>
    [Authorable(NarrativeKinds.ContentSet, DisplayName = "Gameplay Content Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Compile,
        Doc = "Lists the narrative definitions (facts, graphs, quests, items, rules...) of one world; the bake writes its content manifest.")]
    [CreateAssetMenu(menuName = "GameCore/Narrative/Content Set", fileName = "ContentSet")]
    public sealed class GameplayContentSet : ScriptableObject
    {
        [AuthorRef(Category = "world.world", Doc = "The world this content belongs to.")]
        [SerializeField] private WorldDefinition? world;

        [AuthorRef(Category = "narrative.definition", Doc = "Every narrative definition of the world.")]
        [SerializeField] private List<ScriptableObject> definitions = new List<ScriptableObject>();

        public WorldDefinition? World => world;

        public IReadOnlyList<ScriptableObject> Definitions => definitions;

        public void Configure(WorldDefinition? owner, IEnumerable<ScriptableObject> list)
        {
            world = owner;
            definitions = new List<ScriptableObject>(list);
        }

        /// <summary>Adds a definition once; true when added.</summary>
        public bool Add(ScriptableObject definition)
        {
            if (definition == null || definitions.Contains(definition))
            {
                return false;
            }

            definitions.Add(definition);
            return true;
        }
    }
}
