// GameCore.Gameplay.Audio - AudioSetDefinition (P1.5, catalog row 11). Own file: Unity binds a ScriptableObject to the script named after it.
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
    /// <summary>A game's audio content: the bank, the music states, the ambiences and the start state.</summary>
    [Authorable("audio.set", DisplayName = "Audio Set", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "The audio content of a game: bank, music states (with the start state), region ambiences and the mixer.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Audio Set", fileName = "AudioSet")]
    public sealed class AudioSetDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "audio.bank", Doc = "The clip bank.")]
        [SerializeField] private AudioBankDefinition? bank;

        [AuthorRef(Category = "audio.musicState", Doc = "Every music state.")]
        [SerializeField] private List<MusicStateDefinition> musicStates = new List<MusicStateDefinition>();

        [AuthorField(Doc = "The music state a new world starts in (empty = silence).")]
        [SerializeField] private string startMusicState = string.Empty;

        [AuthorRef(Category = "audio.ambience", Doc = "Every region ambience.")]
        [SerializeField] private List<AmbienceDefinition> ambiences = new List<AmbienceDefinition>();

        [AuthorField(Doc = "Feedback id -> bank clip id prefix for footsteps and other presentation-only cues (e.g. footstep -> sfx.footstep).")]
        [SerializeField] private string feedbackPrefix = "sfx.";

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public AudioBankDefinition? Bank => bank;

        public IReadOnlyList<MusicStateDefinition> MusicStates => musicStates;

        public string StartMusicState => startMusicState;

        public IReadOnlyList<AmbienceDefinition> Ambiences => ambiences;

        public string FeedbackPrefix => feedbackPrefix;

        public void Configure(AudioBankDefinition? clipBank, IReadOnlyList<MusicStateDefinition> states, string start, IReadOnlyList<AmbienceDefinition> regionAmbiences)
        {
            bank = clipBank;
            musicStates = new List<MusicStateDefinition>(states);
            startMusicState = start ?? string.Empty;
            ambiences = new List<AmbienceDefinition>(regionAmbiences);
        }

        public MusicStateDefinition? FindState(int key)
        {
            for (int i = 0; i < musicStates.Count; i++)
            {
                if (musicStates[i] != null && musicStates[i].Key == key)
                {
                    return musicStates[i];
                }
            }

            return null;
        }

        public MusicStateDefinition? FindState(string stateId)
        {
            for (int i = 0; i < musicStates.Count; i++)
            {
                if (musicStates[i] != null && string.Equals(musicStates[i].StateId, stateId, StringComparison.Ordinal))
                {
                    return musicStates[i];
                }
            }

            return null;
        }

        public AmbienceDefinition? FindAmbience(string regionId)
        {
            for (int i = 0; i < ambiences.Count; i++)
            {
                if (ambiences[i] != null && string.Equals(ambiences[i].RegionId, regionId, StringComparison.Ordinal))
                {
                    return ambiences[i];
                }
            }

            return null;
        }

        public bool AddAmbience(AmbienceDefinition ambience)
        {
            if (ambience == null || ambiences.Contains(ambience))
            {
                return false;
            }

            ambiences.Add(ambience);
            return true;
        }

        public bool AddMusicState(MusicStateDefinition state)
        {
            if (state == null || musicStates.Contains(state))
            {
                return false;
            }

            musicStates.Add(state);
            return true;
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
