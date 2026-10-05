// GameCore.Gameplay.Audio - AmbienceDefinition (P1.5, catalog row 11). Own file: Unity binds a ScriptableObject to the script named after it.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using UnityEngine;
using UnityEngine.Audio;

namespace GameCore.Gameplay.Audio
{
    /// <summary>One region's ambience loop.</summary>
    [Authorable("audio.ambience", DisplayName = "Ambience", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A region's ambience: the region, the bank clip it loops, its volume and the crossfade on entering the region.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Ambience", fileName = "Ambience")]
    public sealed class AmbienceDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "world.region", Doc = "The region whose ambience this is.")]
        [SerializeField] private RegionDefinition? region;

        [AuthorField(Doc = "Bank clip id of the loop.")]
        [SerializeField] private string clipId = string.Empty;

        [AuthorField(Min = 0, Max = 1, Doc = "Loop volume (linear, before the mixer).")]
        [SerializeField] private float volume = 0.8f;

        [AuthorField(Unit = "ms", Min = 0, Max = 20000, Doc = "Crossfade when the region is entered.")]
        [SerializeField] private int fadeMs = 2500;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public RegionDefinition? Region => region;

        /// <summary>The region's authoring id (empty without a region).</summary>
        public string RegionId => region != null ? region.AuthoringId : string.Empty;

        public string ClipId => clipId;

        public float Volume => volume;

        public int FadeMs => fadeMs;

        public void Configure(RegionDefinition? ambienceRegion, string clip, float loopVolume, int fade)
        {
            region = ambienceRegion;
            clipId = clip ?? string.Empty;
            volume = Mathf.Clamp01(loopVolume);
            fadeMs = Math.Max(0, fade);
        }

        public bool EnsureAuthoringId()
        {
            string next = AuthoringIdField.Ensure(authoringId);
            bool changed = !string.Equals(next, authoringId, StringComparison.Ordinal);
            authoringId = next;
            return changed;
        }

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
