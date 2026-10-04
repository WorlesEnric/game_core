// GameCore.Studio fixtures - a region root (provides world.region, so authored children get contains edges from it).
#nullable enable
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Authorable("fixture.region", DisplayName = "Fixture Region")]
    public sealed class FixtureRegion : MonoBehaviour, IAuthoredObject
    {
        private static readonly string[] Provided = { "world.region" };

        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorField(Doc = "Region name.")]
        public string regionName = "harbor";

        public string AuthoringId => authoringId;

        public IEnumerable<string> Capabilities => Provided;
    }
}
