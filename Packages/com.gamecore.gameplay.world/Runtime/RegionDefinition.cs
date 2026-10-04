// GameCore.Gameplay.World - RegionDefinition (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>One region of a world.</summary>
    [Authorable("world.region", DisplayName = "Region", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A streamed region: one additive scene with its own composition scope.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Region Definition", fileName = "Region")]
    public sealed class RegionDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Display name.")]
        [SerializeField] private string displayName = string.Empty;

        [AuthorField(Type = "artifact", Doc = "Project-relative path of the region scene (Assets/.../X.unity).")]
        [SerializeField] private string scenePath = string.Empty;

        [SerializeField] private string contentStamp = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => string.IsNullOrEmpty(displayName) ? name : displayName;

        public string ContentStamp => contentStamp;

        public string DisplayName => DefinitionName;

        public string ScenePath => scenePath;

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

        public void Configure(string regionName, string path)
        {
            displayName = regionName ?? string.Empty;
            scenePath = path ?? string.Empty;
        }

        public void SetContentStamp(string stamp) => contentStamp = stamp ?? string.Empty;

        private void Reset() => EnsureAuthoringId();

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                EnsureAuthoringId();
            }
        }
    }
}
