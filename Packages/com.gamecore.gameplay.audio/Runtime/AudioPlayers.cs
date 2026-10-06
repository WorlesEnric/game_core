// GameCore.Gameplay.Audio - the presentation players: music, ambience, sfx pool, voice, mixer (P1.5, catalog row 11).
//
// Each player has a logic half that runs everywhere (state, crossfade schedule, counters; tests read it headless) and
// an engine half (AudioSources on the audio root, AudioMixer parameters) that exists only when a graphics device exists
// and audio is enabled. Players read committed state handed to them by the AudioRuntime after each pump; they never
// write gameplay state. Fades advance in Tick(nowMs), driven by the AudioEngineDriver's Update.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Rules.Gameplay.Audio;
using UnityEngine;
using UnityEngine.Audio;

namespace GameCore.Gameplay.Audio
{
    /// <summary>Two looping sources crossfading between keyed loops (music states, ambience zones).</summary>
    public sealed class LoopCrossfader
    {
        private readonly string label;
        private readonly bool releaseAudioData;
        private AudioSource? a;
        private AudioSource? b;
        private int keyA;
        private int keyB;
        private CrossfadeSchedule schedule;

        public LoopCrossfader(string label, bool releaseAudioData)
        {
            this.label = label;
            this.releaseAudioData = releaseAudioData;
        }

        /// <summary>The key faded to last (0 = silence).</summary>
        public int CurrentKey => schedule.ToKey;

        public CrossfadeSchedule Schedule => schedule;

        public int Changes { get; private set; }

        public bool HasSources => a != null && b != null;

        public void AttachSources(AudioSource first, AudioSource second)
        {
            a = first;
            b = second;
        }

        /// <summary>Starts a crossfade to <paramref name="key"/> (with its clip and volume) at <paramref name="nowMs"/>.</summary>
        public void FadeTo(int key, AudioClip? clip, float volume, AudioMixerGroup? group, int fadeMs, long nowMs)
        {
            if (key == schedule.ToKey && Changes > 0)
            {
                return;
            }

            schedule = Changes == 0 ? new CrossfadeSchedule(0, key, nowMs, fadeMs) : schedule.Retarget(key, nowMs, fadeMs);
            Changes++;
            if (a == null || b == null)
            {
                return;
            }

            // The source holding the outgoing loop keeps playing; the other one takes the incoming loop.
            AudioSource incoming = keyA == schedule.FromKey && schedule.FromKey != 0 ? b : a;
            if (incoming == a)
            {
                keyA = key;
            }
            else
            {
                keyB = key;
            }

            incoming.Stop();
            incoming.clip = clip;
            incoming.loop = true;
            incoming.outputAudioMixerGroup = group;
            incoming.volume = 0f;
            Volume = volume;
            if (clip != null && key != 0)
            {
                if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
                incoming.Play();
            }
        }

        /// <summary>The loop volume of the incoming key (linear).</summary>
        public float Volume { get; private set; } = 1f;

        /// <summary>Advances the fade; returns the gains applied.</summary>
        public CrossfadeGains Tick(long nowMs)
        {
            CrossfadeGains gains = schedule.GainsAt(nowMs);
            if (a != null && b != null)
            {
                Apply(a, keyA, gains);
                Apply(b, keyB, gains);
            }

            return gains;
        }

        private void Apply(AudioSource source, int key, CrossfadeGains gains)
        {
            float gain = key == schedule.ToKey ? gains.Incoming : (key == schedule.FromKey ? gains.Outgoing : 0f);
            source.volume = gain * Volume;
            if (gains.Complete && key != schedule.ToKey && source.clip != null)
            {
                AudioClip released = source.clip;
                if (source.isPlaying) source.Stop();
                source.clip = null;
                if (releaseAudioData)
                {
                    // Clips can be shared with music, voices or another world. Even a paused source
                    // retains its lease. Scan only once when a fade releases a clip, never per frame.
                    AudioSource[] sources = Resources.FindObjectsOfTypeAll<AudioSource>();
                    foreach (AudioSource other in sources)
                        if (other.clip == released) return;
                    released.UnloadAudioData();
                }
            }
        }

        public override string ToString() => label + " " + schedule;
    }

    /// <summary>Music states with crossfades and stingers.</summary>
    public sealed class MusicController
    {
        private readonly LoopCrossfader loops = new LoopCrossfader("music", false);
        private AudioSource? stingerSource;

        public int CurrentState => loops.CurrentKey;

        public LoopCrossfader Loops => loops;

        public int Stingers { get; private set; }

        public int LastStinger { get; private set; }

        public void AttachSources(AudioSource first, AudioSource second, AudioSource stinger)
        {
            loops.AttachSources(first, second);
            stingerSource = stinger;
        }

        /// <summary>Follows the committed audio.musicState; <paramref name="stinger"/> is the stinger key of the change event (0 = none).</summary>
        public void OnState(int state, int stinger, AudioSetDefinition? set, AudioMixerGroup? group, AudioMixerGroup? stingerGroup, long nowMs)
        {
            MusicStateDefinition? definition = set != null ? set.FindState(state) : null;
            AudioBankEntry? entry = null;
            if (definition != null && set!.Bank != null && definition.ClipId.Length > 0)
            {
                set.Bank.TryGet(definition.ClipId, out entry);
            }

            loops.FadeTo(state, entry?.Clip, entry != null ? entry.Volume : 1f, group, definition != null ? definition.FadeMs : 1500, nowMs);
            if (stinger != 0)
            {
                Stingers++;
                LastStinger = stinger;
                AudioBankEntry? sting = null;
                if (set != null && set.Bank != null && set.Bank.TryGet(stinger, out sting) && sting != null && sting.Clip != null && stingerSource != null)
                {
                    stingerSource.outputAudioMixerGroup = stingerGroup;
                    stingerSource.PlayOneShot(sting.Clip, sting.Volume);
                }
            }
        }

        public CrossfadeGains Tick(long nowMs) => loops.Tick(nowMs);
    }

    /// <summary>Region ambience loops: crossfades when the committed audio.ambienceZone changes (after RegionEntered).</summary>
    public sealed class AmbienceZoneBinder
    {
        private readonly LoopCrossfader loops = new LoopCrossfader("ambience", true);

        public int CurrentZone => loops.CurrentKey;

        public LoopCrossfader Loops => loops;

        /// <summary>The region authoring id of the current zone (empty for none).</summary>
        public string CurrentRegionId { get; private set; } = string.Empty;

        public void AttachSources(AudioSource first, AudioSource second) => loops.AttachSources(first, second);

        public void OnZone(int zone, string regionId, AudioSetDefinition? set, AudioMixerGroup? group, long nowMs)
        {
            AmbienceDefinition? ambience = set != null && regionId.Length > 0 ? set.FindAmbience(regionId) : null;
            AudioBankEntry? entry = null;
            if (ambience != null && set!.Bank != null)
            {
                set.Bank.TryGet(ambience.ClipId, out entry);
            }

            CurrentRegionId = ambience != null ? regionId : string.Empty;
            float volume = (ambience != null ? ambience.Volume : 1f) * (entry != null ? entry.Volume : 1f);
            loops.FadeTo(ambience != null ? zone : 0, entry?.Clip, volume, group, ambience != null ? ambience.FadeMs : 1500, nowMs);
        }

        public CrossfadeGains Tick(long nowMs) => loops.Tick(nowMs);
    }

    /// <summary>
    /// Pooled 3D AudioSources for one-shots; also the presentation-only feedback sink (interaction cues, UI clicks) and
    /// footstep sink of P1.3's contracts.
    /// </summary>
    public sealed class SfxPool : IFeedbackSink, IFootstepSink
    {
        /// <summary>The prefix of an interaction cue for a refused interaction ("refused:&lt;code&gt;").</summary>
        public const string RefusedCuePrefix = "refused:";

        /// <summary>The feedback id every refused interaction plays.</summary>
        public const string RefusedFeedback = "refused";

        private readonly List<AudioSource> sources = new List<AudioSource>();
        private readonly Func<AudioSetDefinition?> set;
        private readonly Func<AudioGroup, AudioMixerGroup?> groups;
        private int next;

        public SfxPool(Func<AudioSetDefinition?> set, Func<AudioGroup, AudioMixerGroup?> groups)
        {
            this.set = set ?? throw new ArgumentNullException(nameof(set));
            this.groups = groups ?? throw new ArgumentNullException(nameof(groups));
        }

        public int Played { get; private set; }

        public int Missing { get; private set; }

        public string LastId { get; private set; } = string.Empty;

        public int Capacity => sources.Count;

        public void AttachSource(AudioSource source) => sources.Add(source ?? throw new ArgumentNullException(nameof(source)));

        /// <summary>The feedback id a footstep plays (resolved through the set's feedback prefix like every feedback id).</summary>
        public string FootstepId { get; set; } = "footstep";

        /// <summary>
        /// IFeedbackSink: plays the cue's id (or its prefixed bank id) at the cue's point (millimetres); every
        /// "refused:&lt;code&gt;" cue plays <see cref="RefusedFeedback"/>.
        /// </summary>
        public void OnFeedback(FeedbackCue cue)
        {
            string id = cue.Cue ?? string.Empty;
            if (id.StartsWith(RefusedCuePrefix, StringComparison.Ordinal))
            {
                id = RefusedFeedback;
            }

            if (id.Length == 0)
            {
                return;
            }

            PlayId(id, cue.X / 1000f, cue.Y / 1000f, cue.Z / 1000f, true);
        }

        /// <summary>IFootstepSink: plays <see cref="FootstepId"/> at the footstep's point (millimetres).</summary>
        public void OnFootstep(FootstepEvent footstep) =>
            PlayId(FootstepId, footstep.X / 1000f, footstep.Y / 1000f, footstep.Z / 1000f, true);

        /// <summary>Plays a bank clip id at a point in metres; <paramref name="tryPrefix"/> also tries the set's feedback prefix.</summary>
        public bool PlayId(string id, float x, float y, float z, bool tryPrefix)
        {
            AudioSetDefinition? current = set();
            AudioBankEntry? entry = null;
            bool found = current != null && current.Bank != null
                && (current.Bank.TryGet(id, out entry) || (tryPrefix && current.Bank.TryGet(current.FeedbackPrefix + id, out entry)));
            return PlayEntry(found ? entry : null, id, x, y, z);
        }

        /// <summary>Plays a bank clip by key at a point in millimetres (audio.playSfx events).</summary>
        public bool PlayKey(int key, int xMm, int yMm, int zMm)
        {
            AudioSetDefinition? current = set();
            AudioBankEntry? entry = null;
            bool found = current != null && current.Bank != null && current.Bank.TryGet(key, out entry);
            return PlayEntry(found ? entry : null, "#" + key, xMm / 1000f, yMm / 1000f, zMm / 1000f);
        }

        private bool PlayEntry(AudioBankEntry? entry, string id, float x, float y, float z)
        {
            if (entry == null)
            {
                Missing++;
                return false;
            }

            Played++;
            LastId = entry.Id;
            if (sources.Count == 0 || entry.Clip == null)
            {
                return true;
            }

            AudioSource source = sources[next];
            next = (next + 1) % sources.Count;
            source.transform.position = new UnityEngine.Vector3(x, y, z);
            source.spatialBlend = entry.Spatial ? 1f : 0f;
            source.minDistance = entry.MinDistance;
            source.maxDistance = entry.MaxDistance;
            source.outputAudioMixerGroup = groups(entry.Group);
            source.clip = entry.Clip;
            source.volume = entry.Volume;
            source.loop = false;
            source.Play();
            return true;
        }
    }

    /// <summary>
    /// Dialogue voice lines (P1.4's IVoiceLinePlayer): Play/Stop submit audio.playVoice / audio.stopVoice; the committed
    /// events start and stop the clip, so a voice line is ordered with the step that showed the line. A request's clip is
    /// the bank entry holding its AudioClip (or whose id is its ClipRef); a clip that is not in the bank is remembered by
    /// the key of its ClipRef and played directly. A line with neither clip nor ref plays nothing.
    /// </summary>
    public sealed class VoicePlayer : IVoiceLinePlayer
    {
        /// <summary>Direct (non-bank) clips remembered at most.</summary>
        public const int MaxDirectClips = 32;

        private readonly Dictionary<int, AudioClip> directClips = new Dictionary<int, AudioClip>();
        private readonly Func<AudioCommandIssuer?> issuer;
        private readonly Func<AudioSetDefinition?> set;
        private AudioSource? source;
        private long endsAtMs;

        public VoicePlayer(Func<AudioCommandIssuer?> issuer, Func<AudioSetDefinition?> set)
        {
            this.issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
            this.set = set ?? throw new ArgumentNullException(nameof(set));
        }

        public int Requested { get; private set; }

        public int Started { get; private set; }

        public int Stopped { get; private set; }

        /// <summary>The key of the clip of the last committed voice line (0 = none).</summary>
        public int CurrentClip { get; private set; }

        public int CurrentSpeaker { get; private set; }

        /// <summary>True while a line plays (its source, or its clip length when there is no source).</summary>
        public bool IsPlaying(long nowMs) => CurrentClip != 0 && (source != null ? source.isPlaying : nowMs < endsAtMs);

        public void AttachSource(AudioSource voice) => source = voice;

        /// <summary>Lines received without a playable clip (no AudioClip and no ClipRef).</summary>
        public int Silent { get; private set; }

        /// <summary>The last stop reason (IVoiceLinePlayer.Stop).</summary>
        public string LastStopReason { get; private set; } = string.Empty;

        /// <summary>IVoiceLinePlayer (P1.4's VoiceLinePlayer binder): plays the request's clip, interrupting the current line.</summary>
        public void Play(VoiceLineRequest request)
        {
            if (request == null)
            {
                return;
            }

            AudioSetDefinition? current = set();
            AudioClip? clip = request.Clip as AudioClip;
            string id = string.Empty;
            if (clip != null && current != null && current.Bank != null)
            {
                id = current.Bank.IdOf(clip);
            }

            if (id.Length == 0 && request.ClipRef.Length > 0)
            {
                id = request.ClipRef;
                if (clip != null && (current == null || current.Bank == null || !current.Bank.TryGet(id, out AudioBankEntry? _)))
                {
                    if (directClips.Count >= MaxDirectClips)
                    {
                        directClips.Clear();
                    }

                    directClips[PresentationSlots.KeyOf(id)] = clip;
                }
            }

            if (id.Length == 0)
            {
                Silent++;
                return;
            }

            Play(id, request.Speaker);
        }

        /// <summary>Plays a bank clip id (or a remembered direct clip's ref) for a speaker through audio.playVoice.</summary>
        public void Play(string clipRef, string speakerId)
        {
            Requested++;
            issuer()?.PlayVoice(clipRef ?? string.Empty, speakerId ?? string.Empty);
        }

        /// <summary>IVoiceLinePlayer: stops the current line through audio.stopVoice.</summary>
        public void Stop(string reason)
        {
            LastStopReason = reason ?? string.Empty;
            issuer()?.StopVoice();
        }

        /// <summary>A committed VoicePlayed: interrupts the line in progress and starts the new one.</summary>
        public void OnPlayed(int clipKey, int speakerKey, AudioMixerGroup? group, long nowMs)
        {
            Started++;
            CurrentClip = clipKey;
            CurrentSpeaker = speakerKey;
            AudioSetDefinition? current = set();
            AudioBankEntry? entry = null;
            if (current != null && current.Bank != null)
            {
                current.Bank.TryGet(clipKey, out entry);
            }

            AudioClip? clip = entry != null ? entry.Clip : (directClips.TryGetValue(clipKey, out AudioClip? direct) ? direct : null);
            endsAtMs = nowMs + (clip != null ? (long)(clip.length * 1000f) : 0L);
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.outputAudioMixerGroup = group;
            source.clip = clip;
            source.volume = entry != null ? entry.Volume : 1f;
            if (source.clip != null)
            {
                source.Play();
            }
        }

        /// <summary>A committed VoiceStopped.</summary>
        public void OnStopped()
        {
            Stopped++;
            CurrentClip = 0;
            endsAtMs = 0;
            if (source != null)
            {
                source.Stop();
            }
        }
    }

    /// <summary>Applies the committed volumes to the mixer's exposed parameters.</summary>
    public sealed class AudioMixerBinding
    {
        /// <summary>Exposed parameter names by channel (master, music, sfx, voice); ambience follows sfx.</summary>
        public static readonly string[] Parameters = { "MasterVolume", "MusicVolume", "SfxVolume", "VoiceVolume" };

        public const string AmbienceParameter = "AmbienceVolume";

        private readonly int[] applied = { -1, -1, -1, -1 };

        public AudioMixerBinding(AudioMixer? mixer)
        {
            Mixer = mixer;
        }

        public AudioMixer? Mixer { get; }

        public int Applications { get; private set; }

        /// <summary>Last decibels per channel (also computed without a mixer).</summary>
        public float[] Decibels { get; } = { 0f, 0f, 0f, 0f };

        public AudioMixerGroup? Group(AudioGroup group)
        {
            if (Mixer == null)
            {
                return null;
            }

            string name = group == AudioGroup.Ui ? "Sfx" : group.ToString();
            AudioMixerGroup[] found = Mixer.FindMatchingGroups(name);
            for (int i = 0; i < found.Length; i++)
            {
                if (string.Equals(found[i].name, name, StringComparison.Ordinal))
                {
                    return found[i];
                }
            }

            return found.Length > 0 ? found[0] : null;
        }

        /// <summary>Applies changed volumes (permille per channel); returns how many parameters were written.</summary>
        public int Apply(int[] permille)
        {
            int written = 0;
            for (int channel = 0; channel < Parameters.Length && channel < permille.Length; channel++)
            {
                if (applied[channel] == permille[channel])
                {
                    continue;
                }

                applied[channel] = permille[channel];
                float db = VolumeRules.ToDecibels(permille[channel]);
                Decibels[channel] = db;
                written++;
                if (Mixer != null)
                {
                    Mixer.SetFloat(Parameters[channel], db);
                    if (channel == (int)AudioChannel.Sfx)
                    {
                        Mixer.SetFloat(AmbienceParameter, db);
                    }
                }
            }

            Applications += written;
            return written;
        }
    }
}
