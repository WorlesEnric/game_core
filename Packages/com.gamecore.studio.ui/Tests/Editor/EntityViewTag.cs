// GameCore.Studio.UI.Tests - a stand-in for a gameplay view tag (same type and property names as the gameplay entity
// view tag), used to check that picks on runtime views map back to their authored objects.
#nullable enable
using UnityEngine;

namespace GameCore.Studio.UI.Tests
{
    public sealed class EntityViewTag : MonoBehaviour
    {
        public string AuthoringId { get; set; } = string.Empty;
    }
}
