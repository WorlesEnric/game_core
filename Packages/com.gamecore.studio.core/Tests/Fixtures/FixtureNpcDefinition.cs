// GameCore.Studio fixtures - an NPC definition with a value field, an enum field and a reference to an item.
#nullable enable
using System.Collections.Generic;
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Authorable("fixture.npc", DisplayName = "Fixture NPC", Doc = "A talking character definition (P1.6 test fixture).")]
    public sealed class FixtureNpcDefinition : ScriptableObject, IAuthoredObject
    {
        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorField(Doc = "First line the character says.")]
        public string greeting = "Hello";

        [AuthorField(Doc = "Mood.")]
        public FixtureMood mood = FixtureMood.Calm;

        [AuthorRef(Required = false, Doc = "Item the character starts with.")]
        public FixtureItemDefinition? startingItem;

        [AuthorRef(Required = false)]
        public List<FixtureItemDefinition> items = new List<FixtureItemDefinition>();

        [AuthorRef(Required = false)]
        public List<FixtureItemDefinition> ManagedItems { get; set; } = new List<FixtureItemDefinition>();

        public string AuthoringId => authoringId;
    }
}
