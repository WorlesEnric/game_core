// GameCore.Studio fixtures - an item definition (ScriptableObject asset) implementing the mirror IAuthoredObject.
#nullable enable
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Authorable("fixture.item", DisplayName = "Fixture Item", Doc = "A carried item (P1.6 test fixture).")]
    public sealed class FixtureItemDefinition : ScriptableObject, IAuthoredObject
    {
        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorField(Unit = "kg", Min = 0, Max = 100, Doc = "Carried weight.")]
        public float weight = 1f;

        [AuthorField(Doc = "Name shown in the inventory.")]
        public string displayName = string.Empty;

        public string AuthoringId => authoringId;
    }
}
