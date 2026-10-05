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

}
