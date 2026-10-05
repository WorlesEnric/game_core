// GameCore.Studio fixtures - a dialogue-like definition: an [AuthorField(Type = "authoringId")] string naming a placed
// entity by id (as the gameplay dialogue, quest, inventory and logic definitions do) and a list of [Serializable] class
// elements with nested enum, object-reference and authoring-id members (set/undo on list members).
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Serializable]
    public sealed class FixtureLine
    {
        public string text = string.Empty;

        public FixtureMood mood = FixtureMood.Calm;

        public FixtureItemDefinition? item;

        [AuthorField(Type = "authoringId", Doc = "Speaker entity authoring id (optional).")]
        public string speakerEntityId = string.Empty;

        [SerializeField]
        private int weight = 1;

        public int Weight
        {
            get => weight;
            set => weight = value;
        }
    }

    [Authorable("fixture.dialogue", DisplayName = "Fixture Dialogue", Doc = "A dialogue graph (P1.6 test fixture).")]
    public sealed class FixtureDialogueDefinition : ScriptableObject, IAuthoredObject
    {
        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorField(Type = "authoringId", Doc = "Default speaker entity authoring id (optional).")]
        public string speakerEntityId = string.Empty;

        [AuthorField(Doc = "Lines in order.")]
        public List<FixtureLine> lines = new List<FixtureLine>();

        public string AuthoringId => authoringId;
    }
}
