// GameCore.Studio fixtures - a scene entity referencing an NPC definition, with a capability.
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Authorable("fixture.entity", DisplayName = "Fixture Entity", Doc = "A placed character (P1.6 test fixture).")]
    public sealed class FixtureAuthoredEntity : MonoBehaviour, IAuthoredObject
    {
        private static readonly string[] Provided = { "fixture.talks" };

        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorRef(Required = false, Doc = "The character definition.")]
        public FixtureNpcDefinition? definition;

        [AuthorField(Min = 0, Max = 10, Doc = "Level.")]
        public int level = 1;

        public string AuthoringId => authoringId;

        public IEnumerable<string> Capabilities => Provided;
    }
}
