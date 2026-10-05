// GameCore.Gameplay.Audio - AudioBankDefinition (P1.5, catalog row 11). Own file: Unity binds a ScriptableObject to the script named after it.
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
    /// <summary>Clips by id.</summary>
    [Authorable("audio.bank", DisplayName = "Audio Bank", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Live,
        Doc = "Clips by id (imported AudioClips) with their mixer group, volume, loop and 3D settings.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Audio Bank", fileName = "AudioBank")]
    public sealed class AudioBankDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "The bank's clips.")]
        [SerializeField] private List<AudioBankEntry> entries = new List<AudioBankEntry>();

        [AuthorRef(Category = "asset.audioMixer", Required = false, Doc = "The mixer whose groups and exposed volume parameters the bank plays through.")]
        [SerializeField] private AudioMixer? mixer;

        public string AuthoringId => authoringId;

        public string DefinitionName => name;

        public string ContentStamp => string.Empty;

        public IReadOnlyList<AudioBankEntry> Entries => entries;

        public AudioMixer? Mixer => mixer;

        public bool TryGet(string id, out AudioBankEntry? entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Id, id, StringComparison.Ordinal))
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        public bool TryGet(int key, out AudioBankEntry? entry)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Key == key)
                {
                    entry = entries[i];
                    return true;
                }
            }

            entry = null;
            return false;
        }

        /// <summary>The id of the first entry holding <paramref name="clip"/>, or empty.</summary>
        public string IdOf(AudioClip clip)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (clip != null && entries[i].Clip == clip)
                {
                    return entries[i].Id;
                }
            }

            return string.Empty;
        }

        /// <summary>Adds or replaces the clip of an id (keeps an existing entry's settings); returns the entry.</summary>
        public AudioBankEntry Assign(string id, AudioClip? clip, AudioGroup group, float volume, bool loop, bool spatial)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (string.Equals(entries[i].Id, id, StringComparison.Ordinal))
                {
                    entries[i] = new AudioBankEntry(id, clip, group, volume, loop, spatial);
                    return entries[i];
                }
            }

            var entry = new AudioBankEntry(id, clip, group, volume, loop, spatial);
            entries.Add(entry);
            return entry;
        }

        public void SetMixer(AudioMixer? value) => mixer = value;

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
