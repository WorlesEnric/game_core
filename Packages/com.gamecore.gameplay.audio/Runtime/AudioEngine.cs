// GameCore.Gameplay.Audio - the engine half of the audio runtime: AudioSources, the fade driver and the animation
// event relay (P1.5, catalog row 11).
//
// AudioEngineHost is a MonoBehaviour that exists in every mode (it ticks the crossfade schedules, which are logic); it
// creates AudioSources (two music loops, a stinger, two ambience loops, the voice and the sfx pool) only when a
// graphics device exists and AudioListener output is possible. Headless the players keep their logic state, so tests
// read music, ambience, sfx and voice counters without any AudioSource.
//
// AnimationEventRelay sits on an animated model and forwards animation events (Footstep, Feedback) to the world's
// IFeedbackSink, which the audio runtime registers. Its sink is assigned by whoever spawns the view; without one it
// resolves the scene's AudioEngineHost once.
#nullable enable
using System;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using UnityEngine;

namespace GameCore.Gameplay.Audio
{
    /// <summary>Hosts an audio runtime's sources and drives its fades.</summary>
    public sealed class AudioEngineHost : MonoBehaviour
    {
        public const int DefaultSfxVoices = 8;

        public AudioRuntime? Runtime { get; private set; }

        /// <summary>True when AudioSources were created (a graphics device exists).</summary>
        public bool HasSources { get; private set; }

        public int Ticks { get; private set; }

        /// <summary>Creates the host under <paramref name="parent"/>; sources only when not headless.</summary>
        public static AudioEngineHost Create(Transform? parent, AudioRuntime runtime, int sfxVoices = DefaultSfxVoices)
        {
            if (runtime == null)
            {
                throw new ArgumentNullException(nameof(runtime));
            }

            var host = new GameObject("GameCore Audio");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            AudioEngineHost engine = host.AddComponent<AudioEngineHost>();
            engine.Runtime = runtime;
            if (!BinderEnvironment.IsHeadless)
            {
                engine.CreateSources(Math.Max(1, sfxVoices));
            }

            return engine;
        }

        private void CreateSources(int sfxVoices)
        {
            AudioRuntime runtime = Runtime!;
            runtime.Music.AttachSources(Source("Music A", false), Source("Music B", false), Source("Stinger", false));
            runtime.Ambience.AttachSources(Source("Ambience A", false), Source("Ambience B", false));
            runtime.Voice.AttachSource(Source("Voice", false));
            for (int i = 0; i < sfxVoices; i++)
            {
                runtime.Sfx.AttachSource(Source("Sfx " + i, true));
            }

            HasSources = true;
        }

        private AudioSource Source(string label, bool detached)
        {
            var child = new GameObject(label);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = detached ? 1f : 0f;
            return source;
        }

        private void Update()
        {
            if (Runtime != null)
            {
                Ticks++;
                Runtime.Tick();
            }
        }
    }

}
