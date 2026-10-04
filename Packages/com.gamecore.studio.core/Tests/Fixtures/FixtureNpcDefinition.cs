// GameCore.Studio fixtures - an NPC definition with a value field, an enum field and a reference to an item.
#nullable enable
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
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

        public string AuthoringId => authoringId;
    }
}
