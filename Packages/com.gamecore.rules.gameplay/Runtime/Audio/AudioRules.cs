// GameCore.Rules.Gameplay.Audio - the pure rules of the audio plugin (catalog row 11), engine-free.
//
//   * VolumeRules: permille settings -> linear gain -> mixer decibels, with a perceptual (squared) curve so the slider's
//     midpoint sounds like half loudness, a -80 dB floor for silence, and master x channel composition;
//   * MusicStateRules: which music states a command may select and whether a stinger plays;
//   * AmbienceRules: when a region change switches the ambience zone;
//   * CrossfadeSchedule: the equal-power gain curve of a crossfade between two loops, evaluated at any time, so the music
//     controller and the ambience binder fade the same way and a test can check any instant without a clock.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Rules.Gameplay.Audio
{
    /// <summary>A volume channel (the value of an audio.setVolume channel argument).</summary>
    public enum AudioChannel
    {
        Master = 0,
        Music = 1,
        Sfx = 2,
        Voice = 3,
    }

    /// <summary>Why an audio command was refused.</summary>
    public enum AudioRefusal
    {
        None = 0,
        UnknownState = 1,
        Unchanged = 2,
        UnknownChannel = 3,
        OutOfRange = 4,
        UnknownZone = 5,
    }

    /// <summary>Volume curve and mix math.</summary>
    public static class VolumeRules
    {
        public const int MaxPermille = 1000;

        /// <summary>The mixer attenuation used for silence (an exposed mixer volume parameter's practical floor).</summary>
        public const float SilenceDecibels = -80f;

        public const int ChannelCount = 4;

        public static bool IsChannel(int channel) => channel >= 0 && channel < ChannelCount;

        /// <summary>Validates an audio.setVolume request against the current committed value.</summary>
        public static AudioRefusal Check(int channel, int permille, int current)
        {
            if (!IsChannel(channel))
            {
                return AudioRefusal.UnknownChannel;
            }

            if (permille < 0 || permille > MaxPermille)
            {
                return AudioRefusal.OutOfRange;
            }

            return permille == current ? AudioRefusal.Unchanged : AudioRefusal.None;
        }

        /// <summary>Linear gain 0..1 of a permille setting along the perceptual curve (gain = (p/1000)^2).</summary>
        public static float ToLinear(int permille)
        {
            float p = Math.Max(0, Math.Min(MaxPermille, permille)) / (float)MaxPermille;
            return p * p;
        }

        /// <summary>Mixer decibels of a linear gain; gains at or below 1e-4 (-80 dB) are silence.</summary>
        public static float LinearToDecibels(float linear)
        {
            if (float.IsNaN(linear) || linear <= 0.0001f)
            {
                return SilenceDecibels;
            }

            float db = 20f * (float)Math.Log10(Math.Min(1f, linear));
            return Math.Max(SilenceDecibels, db);
        }

        /// <summary>Mixer decibels of a permille setting.</summary>
        public static float ToDecibels(int permille) => LinearToDecibels(ToLinear(permille));

        /// <summary>The effective linear gain of a channel under the master setting.</summary>
        public static float Effective(int masterPermille, int channelPermille) => ToLinear(masterPermille) * ToLinear(channelPermille);

        /// <summary>The permille a linear gain corresponds to on the perceptual curve (inverse of <see cref="ToLinear"/>).</summary>
        public static int FromLinear(float linear)
        {
            if (float.IsNaN(linear) || linear <= 0f)
            {
                return 0;
            }

            double p = Math.Sqrt(Math.Min(1f, linear)) * MaxPermille;
            return (int)Math.Round(p, MidpointRounding.AwayFromZero);
        }
    }

    /// <summary>Music state selection.</summary>
    public static class MusicStateRules
    {
        /// <summary>The silence state key (no music).</summary>
        public const int Silence = 0;

        /// <summary>Validates audio.setMusicState: the state must be silence or a known key, and differ from the current one.</summary>
        public static AudioRefusal Check(int current, int next, IReadOnlyCollection<int> known)
        {
            if (next != Silence && (known == null || !Contains(known, next)))
            {
                return AudioRefusal.UnknownState;
            }

            return next == current ? AudioRefusal.Unchanged : AudioRefusal.None;
        }

        /// <summary>
        /// Whether the change plays a stinger: the command asked for one (stinger key non-zero) and the new state is not
        /// silence. A stinger is a one-shot over the crossfade, never a state.
        /// </summary>
        public static bool PlaysStinger(int next, int stinger) => stinger != 0 && next != Silence;

        private static bool Contains(IReadOnlyCollection<int> known, int value)
        {
            foreach (int key in known)
            {
                if (key == value)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Ambience zone selection from region changes.</summary>
    public static class AmbienceRules
    {
        public const int NoZone = 0;

        /// <summary>
        /// Validates audio.setAmbienceZone: the zone must be NoZone or a region with an ambience, and differ from the
        /// current zone (re-entering the same region keeps the loop playing).
        /// </summary>
        public static AudioRefusal Check(int current, int next, IReadOnlyCollection<int> zones)
        {
            if (next != NoZone)
            {
                bool found = false;
                if (zones != null)
                {
                    foreach (int zone in zones)
                    {
                        if (zone == next)
                        {
                            found = true;
                            break;
                        }
                    }
                }

                if (!found)
                {
                    return AudioRefusal.UnknownZone;
                }
            }

            return next == current ? AudioRefusal.Unchanged : AudioRefusal.None;
        }

        /// <summary>
        /// The zone a traveller's region change selects: the destination region when it has an ambience, otherwise the
        /// current zone is kept (a region without ambience inherits the last one).
        /// </summary>
        public static int ZoneForRegion(int currentZone, int destinationRegion, IReadOnlyCollection<int> zones)
        {
            if (zones != null)
            {
                foreach (int zone in zones)
                {
                    if (zone == destinationRegion)
                    {
                        return destinationRegion;
                    }
                }
            }

            return currentZone;
        }
    }

    /// <summary>Gains of the outgoing and incoming loop at one instant of a crossfade.</summary>
    public readonly struct CrossfadeGains
    {
        public CrossfadeGains(float outgoing, float incoming, bool complete)
        {
            Outgoing = outgoing;
            Incoming = incoming;
            Complete = complete;
        }

        public float Outgoing { get; }

        public float Incoming { get; }

        public bool Complete { get; }

        public override string ToString() => "out " + Outgoing.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            + " in " + Incoming.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + (Complete ? " (complete)" : string.Empty);
    }

    /// <summary>
    /// One crossfade from a loop to another, started at <see cref="StartMs"/> and lasting <see cref="DurationMs"/>:
    /// equal-power curves (outgoing cos, incoming sin of the quarter turn) so the summed power stays constant. A zero or
    /// negative duration is a cut.
    /// </summary>
    public readonly struct CrossfadeSchedule
    {
        public CrossfadeSchedule(int fromKey, int toKey, long startMs, int durationMs)
        {
            FromKey = fromKey;
            ToKey = toKey;
            StartMs = startMs;
            DurationMs = Math.Max(0, durationMs);
        }

        public int FromKey { get; }

        public int ToKey { get; }

        public long StartMs { get; }

        public int DurationMs { get; }

        public long EndMs => StartMs + DurationMs;

        /// <summary>Progress 0..1 at <paramref name="nowMs"/>.</summary>
        public float Progress(long nowMs)
        {
            if (DurationMs <= 0 || nowMs >= EndMs)
            {
                return 1f;
            }

            if (nowMs <= StartMs)
            {
                return 0f;
            }

            return (float)((nowMs - StartMs) / (double)DurationMs);
        }

        public CrossfadeGains GainsAt(long nowMs)
        {
            float t = Progress(nowMs);
            double angle = t * Math.PI / 2.0;
            float outgoing = FromKey == 0 ? 0f : (float)Math.Cos(angle);
            float incoming = ToKey == 0 ? 0f : (float)Math.Sin(angle);
            if (t >= 1f)
            {
                outgoing = 0f;
                incoming = ToKey == 0 ? 0f : 1f;
            }

            return new CrossfadeGains(outgoing, incoming, t >= 1f);
        }

        /// <summary>
        /// A new crossfade toward <paramref name="toKey"/> starting at <paramref name="nowMs"/> while this one may still run:
        /// the outgoing loop of the new fade is whichever loop is louder now, so an interrupted fade never jumps.
        /// </summary>
        public CrossfadeSchedule Retarget(int toKey, long nowMs, int durationMs)
        {
            CrossfadeGains gains = GainsAt(nowMs);
            int from = gains.Incoming >= gains.Outgoing ? ToKey : FromKey;
            return new CrossfadeSchedule(from, toKey, nowMs, durationMs);
        }
    }
}
