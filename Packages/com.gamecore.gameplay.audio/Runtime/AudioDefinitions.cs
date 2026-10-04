// GameCore.Gameplay.Audio - authorable audio definitions (P1.5, catalog row 11; Studio 03 s4).
//
//   AudioBankDefinition   clips by id (imported AudioClips): mixer group, volume, loop, 3D settings
//   MusicStateDefinition  one music state: id, the bank clip it loops, crossfade, optional stinger
//   AmbienceDefinition    one region's ambience: the region, its loop clip, volume and crossfade
// Ids are stable strings; slots and payloads carry PresentationSlots.KeyOf(id). The validator refuses two ids with one
// key (GP-AUD-003).
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
    /// <summary>The mixer group a clip plays through.</summary>
    public enum AudioGroup
    {
        Music = 0,
        Ambience = 1,
        Sfx = 2,
        Voice = 3,
        Ui = 4,
    }

    /// <summary>One clip of an audio bank.</summary>
    [Serializable]
    public sealed class AudioBankEntry
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField] private AudioClip? clip;
        [SerializeField] private AudioGroup group = AudioGroup.Sfx;
        [SerializeField] private float volume = 1f;
        [SerializeField] private bool loop;
        [SerializeField] private bool spatial;
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 25f;

        public AudioBankEntry()
        {
        }

        public AudioBankEntry(string id, AudioClip? clip, AudioGroup group, float volume, bool loop, bool spatial)
        {
            this.id = id ?? string.Empty;
            this.clip = clip;
            this.group = group;
            this.volume = volume;
            this.loop = loop;
            this.spatial = spatial;
        }

        public string Id => id;

        public int Key => PresentationSlots.KeyOf(id);

        public AudioClip? Clip => clip;

        public AudioGroup Group => group;

        public float Volume => volume;

        public bool Loop => loop;

        public bool Spatial => spatial;

        public float MinDistance => minDistance;

        public float MaxDistance => maxDistance;

        public void SetClip(AudioClip? value) => clip = value;
    }

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

    /// <summary>One music state.</summary>
    [Authorable("audio.musicState", DisplayName = "Music State", Scope = AuthorScope.Definition, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "A music state: its id, the bank clip it loops, the crossfade into it and an optional stinger played on entry.")]
    [CreateAssetMenu(menuName = "GameCore/Gameplay/Music State", fileName = "MusicState")]
    public sealed class MusicStateDefinition : ScriptableObject, IDefinitionAsset
    {
        [SerializeField] private string authoringId = string.Empty;

        [AuthorField(Doc = "Stable state id (e.g. music.explore); audio.musicState holds its key.")]
        [SerializeField] private string stateId = string.Empty;

        [AuthorField(Doc = "Bank clip id of the loop (empty = silence while in this state).")]
        [SerializeField] private string clipId = string.Empty;

        [AuthorField(Unit = "ms", Min = 0, Max = 20000, Doc = "Crossfade into this state.")]
        [SerializeField] private int fadeMs = 2000;

        [AuthorField(Doc = "Bank clip id of a stinger played on entry (empty = none).")]
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
