// GameCore.Gameplay.Audio - MusicStateDefinition (P1.5, catalog row 11). Own file: Unity binds a ScriptableObject to the script named after it.
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
    /// <summary>One music state.</summary>
    [Authorable("audio.musicState", DisplayName = "Music State", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A music state: its id, the bank clip it loops, the crossfade into it and an optional stinger played on entry.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Music State", fileName = "MusicState")]
    public sealed class MusicStateDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Stable state id (e.g. music.explore); audio.musicState holds its key.")]
        [SerializeField] private string stateId = string.Empty;

        [AuthorRef(Category = AuthorRefCategories.AudioClip, Required = false, Doc = "The loop: a clip id of the audio bank (empty = silence while in this state).")]
        [SerializeField] private string clipId = string.Empty;

        [AuthorField(Unit = "ms", Min = 0, Max = 20000, Doc = "Crossfade into this state.")]
        [SerializeField] private int fadeMs = 2000;

        [AuthorRef(Category = AuthorRefCategories.AudioClip, Required = false, Doc = "Stinger played on entry: a clip id of the audio bank (empty = none).")]
        [SerializeField] private string stingerId = string.Empty;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public string StateId => stateId;

        public int Key => PresentationSlots.KeyOf(stateId);

        public string ClipId => clipId;

        public int FadeMs => fadeMs;

        public string StingerId => stingerId;

        public void Configure(string state, string clip, int fade, string stinger)
        {
            stateId = state ?? string.Empty;
            clipId = clip ?? string.Empty;
            fadeMs = Math.Max(0, fade);
            stingerId = stinger ?? string.Empty;
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
