// GameCore.Studio fixtures - an authored component identified only by a serialized authoringId field (duck typing:
// no IAuthoredObject, no AuthoringId property).
#nullable enable
using GameCore.Studio.Model;
using UnityEngine;

namespace GameCore.Studio.Fixtures
{
    [Authorable("fixture.duck", DisplayName = "Fixture Duck")]
    public sealed class FixtureDuckEntity : MonoBehaviour
    {
        [SerializeField]
        private string authoringId = string.Empty;

        [AuthorField(Unit = "m/s", Min = 0, Max = 20, Doc = "Walking speed.")]
        public float speed = 1f;

        internal string Id => authoringId;
    }
}
