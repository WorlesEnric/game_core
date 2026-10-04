// GameCore.Gameplay.Audio - AudioRuntime: the audio presentation runtime of one game (P1.5, catalog row 11).
//
// One runtime per game, across restores. It owns the audio world extension and the players, and it registers the
// presentation services the other gameplay packages call: IVoiceLinePlayer (VoicePlayer), IFeedbackSink (SfxPool) and
// IVolumeSettingsSink (the settings screen). After each pump its presenter binder (headless-safe: it reads committed
// state only and touches engine objects only through players that have sources) does three things:
//   * follows the committed audio slots: music state, ambience zone and volumes (mixer parameters);
//   * plays the committed one-shot events: stingers (MusicStateChanged), sfx (SfxPlayed), voice (VoicePlayed/Stopped);
//   * directs ambience: a RegionEntered of the focus traveller into a region with an ambience queues
//     audio.setAmbienceZone, submitted by the runtime's input source before the next step (never inside a pump).
// The engine half (AudioSources, AudioMixer) is created by AudioEngineHost only when a graphics device exists.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Audio;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Gameplay.Audio
{
    /// <summary>Options of an audio runtime.</summary>
    public sealed class AudioRuntimeOptions
    {
        /// <summary>Initial volumes (master, music, sfx, voice) in permille for a fresh world (the player's mirrored settings).</summary>
        public int[] Volumes { get; set; } = { 800, 800, 800, 800 };

        /// <summary>Region ambience follows the focus traveller's RegionEntered events.</summary>
        public bool DirectAmbience { get; set; } = true;
    }

    /// <summary>The audio presentation runtime of one game.</summary>
    public sealed class AudioRuntime : IVolumeSettingsSink, IGameplayInputSource
    {
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly List<CommittedEvent> events = new List<CommittedEvent>();
        private readonly Queue<int> zoneRequests = new Queue<int>();
        private readonly int[] volumes = { -1, -1, -1, -1 };
        private readonly AudioPresenterBinder binder;
        private readonly AudioRuntimeOptions options;
        private EventCursor cursor;
        private int committedMusic = -1;
        private int committedZone = -1;

        public AudioRuntime(AudioSetDefinition? set, AudioRuntimeOptions? options)
        {
            Set = set;
            this.options = options ?? new AudioRuntimeOptions();
            Extension = new AudioWorldExtension(SettingsOf(set, this.options.Volumes));
            Extension.Attached += OnAttached;
            Mixer = new AudioMixerBinding(set != null && set.Bank != null ? set.Bank.Mixer : null);
            Music = new MusicController();
            Ambience = new AmbienceZoneBinder();
            Sfx = new SfxPool(() => Set, Mixer.Group);
            Voice = new VoicePlayer(() => Commands, () => Set);
            binder = new AudioPresenterBinder(this);
        }

        public AudioSetDefinition? Set { get; }

        public AudioWorldExtension Extension { get; }

        public AudioMixerBinding Mixer { get; }

        public MusicController Music { get; }

        public AmbienceZoneBinder Ambience { get; }

        public SfxPool Sfx { get; }

        public VoicePlayer Voice { get; }

        public GameplayWorld? World { get; private set; }

        public AudioCommandIssuer? Commands { get; private set; }

        /// <summary>Raised when a volume change commits, with (channel, permille): the game mirrors it to the player's settings.</summary>
        public event Action<int, int>? VolumeCommitted;

        public int Attaches { get; private set; }

        public int Presents { get; private set; }

        /// <summary>audio.setAmbienceZone commands submitted by the ambience director.</summary>
        public int ZoneRequests { get; private set; }

        public int PendingZoneRequests => zoneRequests.Count;

        public int MusicChanges { get; private set; }

        public int AmbienceChanges { get; private set; }

        /// <summary>Milliseconds since the runtime started (the fade clock).</summary>
        public long NowMs => clock.ElapsedMilliseconds;

        /// <summary>The committed music state key (0 = silence).</summary>
        public int MusicState => committedMusic < 0 ? 0 : committedMusic;

        /// <summary>The committed ambience zone (a region key, 0 = none).</summary>
        public int AmbienceZone => committedZone < 0 ? 0 : committedZone;

        /// <summary>The audio settings an extension built from <paramref name="set"/> needs.</summary>
        public static AudioWorldSettings SettingsOf(AudioSetDefinition? set, int[]? initialVolumes)
        {
            var settings = new AudioWorldSettings();
            if (set != null)
            {
                for (int i = 0; i < set.MusicStates.Count; i++)
                {
                    MusicStateDefinition state = set.MusicStates[i];
                    if (state != null && state.StateId.Length > 0 && !settings.MusicStates.Contains(state.Key))
                    {
                        settings.MusicStates.Add(state.Key);
                    }
                }

                for (int i = 0; i < set.Ambiences.Count; i++)
                {
                    AmbienceDefinition ambience = set.Ambiences[i];
                    if (ambience != null && ambience.RegionId.Length > 0 && !settings.AmbienceRegions.Contains(ambience.RegionId))
                    {
                        settings.AmbienceRegions.Add(ambience.RegionId);
                    }
                }

                settings.StartMusicState = PresentationSlots.KeyOf(set.StartMusicState);
            }

            if (initialVolumes != null)
            {
                for (int i = 0; i < settings.Volumes.Length && i < initialVolumes.Length; i++)
                {
                    settings.Volumes[i] = initialVolumes[i];
                }
            }

            return settings;
        }

        // ------------------------------------------------------------------ IVolumeSettingsSink

        public void SetVolume(int channel, int permille)
        {
            Commands?.SetVolume(channel, permille);
        }

        public bool TryGetVolume(int channel, out int permille)
        {
            if (channel >= 0 && channel < volumes.Length && volumes[channel] >= 0)
            {
                permille = volumes[channel];
                return true;
            }

            permille = 0;
            return false;
        }

        // ------------------------------------------------------------------ IGameplayInputSource

        /// <summary>Submits the queued ambience zone requests (the world collects input before each step).</summary>
        public int Collect(GameplayWorld world)
        {
            AudioCommandIssuer? issuer = Commands;
            int submitted = 0;
            while (zoneRequests.Count > 0)
            {
                int zone = zoneRequests.Dequeue();
                if (issuer != null && issuer.World == world && issuer.SetAmbienceZone(zone).Admitted)
                {
                    submitted++;
                    ZoneRequests++;
                }
            }

            return submitted;
        }

        /// <summary>Advances the music and ambience crossfades (the engine host calls it every frame).</summary>
        public void Tick()
        {
            long now = NowMs;
            Music.Tick(now);
            Ambience.Tick(now);
        }

        // ------------------------------------------------------------------ attach and presentation

        private void OnAttached(GameplayWorld world, AudioModule module)
        {
            World = world;
            Commands = new AudioCommandIssuer(world);
            cursor = new EventCursor(world.Root.World, EventSequence.Zero);
            zoneRequests.Clear();
            committedMusic = -1;
            committedZone = -1;
            Attaches++;
            PresentationServices services = world.Presentation;
            services.Register<IVolumeSettingsSink>(this);
            services.Register<IVoiceLinePlayer>(Voice);
            services.Register<IFeedbackSink>(Sfx);
            world.AddBinder(binder);
            world.AddInput(this);
            Present();
        }

        internal int Present()
        {
            GameplayWorld? world = World;
            AudioCommandIssuer? issuer = Commands;
            if (world == null || issuer == null)
            {
                return 0;
            }

            Presents++;
            long now = NowMs;
            ReadEvents(world, now);
            int music = issuer.Read(PresentationSlots.MusicState, MusicStateRules.Silence);
            if (music != committedMusic)
            {
                // A seeded or restored state (no change event this frame): follow it without a stinger.
                committedMusic = music;
                if (Music.CurrentState != music || Music.Loops.Changes == 0)
                {
                    MusicChanges++;
                    Music.OnState(music, 0, Set, Mixer.Group(AudioGroup.Music), Mixer.Group(AudioGroup.Sfx), now);
                }
            }

            int zone = issuer.Read(PresentationSlots.AmbienceZone, AmbienceRules.NoZone);
            if (zone != committedZone)
            {
                committedZone = zone;
                if (Ambience.CurrentZone != zone || Ambience.Loops.Changes == 0)
                {
                    AmbienceChanges++;
                    Ambience.OnZone(zone, RegionIdOf(world, zone), Set, Mixer.Group(AudioGroup.Ambience), now);
                }
            }

            for (int channel = 0; channel < volumes.Length; channel++)
            {
                PresentationSlots.TryVolumeSlot(channel, out SlotId slot);
                volumes[channel] = issuer.Read(slot, volumes[channel] < 0 ? 0 : volumes[channel]);
            }

            Mixer.Apply(volumes);
            return 1;
        }

        private void ReadEvents(GameplayWorld world, long now)
        {
            WorldMessagePlane? plane = world.Root.Host.Messages;
            if (plane == null)
            {
                return;
            }

            events.Clear();
            CommittedEventPage page = plane.ReadEvents(cursor, 256);
            cursor = page.NextCursor;
            events.AddRange(page.Events);
            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committed = events[i];
                if (AudioEvent.TryDecode(committed, out AudioEvent audio))
                {
                    OnAudioEvent(world, audio, now);
                }
                else if (options.DirectAmbience && committed.Schema.Equals(WorldDeclarations.RegionEnteredEvent)
                    && WorldEvent.TryDecode(committed.Payload, out WorldEvent entered) && entered.Target.Equals(world.Focus))
                {
                    int current = committedZone < 0 ? AmbienceRules.NoZone : committedZone;
                    AudioModule? module = Extension.Module;
                    int next = AmbienceRules.ZoneForRegion(current, entered.B, module != null ? module.Zones : Array.Empty<int>());
                    if (next != current)
                    {
                        zoneRequests.Enqueue(next);
                    }
                }
            }
        }

        private void OnAudioEvent(GameplayWorld world, AudioEvent audio, long now)
        {
            if (audio.Schema.Equals(AudioDeclarations.MusicStateChangedEvent))
            {
                committedMusic = audio.B;
                MusicChanges++;
                Music.OnState(audio.B, audio.C, Set, Mixer.Group(AudioGroup.Music), Mixer.Group(AudioGroup.Sfx), now);
            }
            else if (audio.Schema.Equals(AudioDeclarations.AmbienceChangedEvent))
            {
                committedZone = audio.B;
                AmbienceChanges++;
                Ambience.OnZone(audio.B, RegionIdOf(world, audio.B), Set, Mixer.Group(AudioGroup.Ambience), now);
            }
            else if (audio.Schema.Equals(AudioDeclarations.VolumeChangedEvent))
            {
                if (audio.A >= 0 && audio.A < volumes.Length)
                {
                    volumes[audio.A] = audio.B;
                }

                VolumeCommitted?.Invoke(audio.A, audio.B);
            }
            else if (audio.Schema.Equals(AudioDeclarations.SfxPlayedEvent))
            {
                Sfx.PlayKey(audio.A, audio.B, audio.C, audio.D);
            }
            else if (audio.Schema.Equals(AudioDeclarations.VoicePlayedEvent))
            {
                Voice.OnPlayed(audio.A, audio.B, Mixer.Group(AudioGroup.Voice), now);
            }
            else if (audio.Schema.Equals(AudioDeclarations.VoiceStoppedEvent))
            {
                Voice.OnStopped();
            }
        }

        private static string RegionIdOf(GameplayWorld world, int zone)
        {
            if (zone == AmbienceRules.NoZone)
            {
                return string.Empty;
            }

            ManifestRegion? region = world.Manifest.FindRegionByKey(zone);
            return region != null ? region.authoringId : string.Empty;
        }

        /// <summary>The runtime's logic binder: active headless.</summary>
        private sealed class AudioPresenterBinder : IPresentationBinder
        {
            private readonly AudioRuntime runtime;

            public AudioPresenterBinder(AudioRuntime runtime)
            {
                this.runtime = runtime;
            }

            public string BinderName => "gameplay.audio.presenter";

            public bool IsActive => true;

            public int Present(ICommittedSlotReader slots) => runtime.Present();
        }
    }
}
