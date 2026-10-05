// GameCore.Rules.Gameplay.Ui - player settings rules (catalog row 10 settings screen), pure and engine-free.
//
// Volumes are gameplay-visible settings: they live in the audio plugin's audio.volume* slots (permille) and are changed
// with audio.setVolume. Look sensitivity, resolution and fullscreen are machine preferences, never part of a save, so the
// UI persists them in PlayerPrefs; these rules clamp and step them so a slider, a gamepad and a stored value agree.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Gameplay.Ui
{
    /// <summary>One display resolution choice.</summary>
    public readonly struct ResolutionChoice : IEquatable<ResolutionChoice>
    {
        public ResolutionChoice(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public int Width { get; }

        public int Height { get; }

        public bool Equals(ResolutionChoice other) => Width == other.Width && Height == other.Height;

        public override bool Equals(object? obj) => obj is ResolutionChoice other && Equals(other);

        public override int GetHashCode() => (Width * 397) ^ Height;

        public override string ToString() => Width.ToString(CultureInfo.InvariantCulture) + " x " + Height.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Clamping and stepping of the settings screen's values.</summary>
    public static class SettingsRules
    {
        /// <summary>Look sensitivity range and step (multiplier of the default).</summary>
        public const float MinSensitivity = 0.1f;
        public const float MaxSensitivity = 5f;
        public const float SensitivityStep = 0.05f;
        public const float DefaultSensitivity = 1f;

        /// <summary>Volume range and step in permille.</summary>
        public const int MinVolume = 0;
        public const int MaxVolume = 1000;
        public const int VolumeStep = 50;
        public const int DefaultVolume = 800;

        /// <summary>Clamps a sensitivity into range and onto the step grid; NaN and infinities become the default.</summary>
        public static float ClampSensitivity(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return DefaultSensitivity;
            }

            float clamped = Math.Max(MinSensitivity, Math.Min(MaxSensitivity, value));
            float steps = (float)Math.Round(clamped / SensitivityStep, MidpointRounding.AwayFromZero);
            float snapped = steps * SensitivityStep;
            return Math.Max(MinSensitivity, Math.Min(MaxSensitivity, snapped));
        }

        /// <summary>Clamps a volume into 0..1000.</summary>
        public static int ClampVolume(int permille) => Math.Max(MinVolume, Math.Min(MaxVolume, permille));

        /// <summary>A volume one gamepad step up (+1) or down (-1), clamped.</summary>
        public static int StepVolume(int permille, int direction) => ClampVolume(permille + Math.Sign(direction) * VolumeStep);

        /// <summary>
        /// The resolution choices a screen offers: the distinct sizes of <paramref name="available"/> at least
        /// <paramref name="minWidth"/> wide, largest first; the current size is always included.
        /// </summary>
        public static IReadOnlyList<ResolutionChoice> Choices(IReadOnlyList<ResolutionChoice> available, ResolutionChoice current, int minWidth)
        {
            var seen = new HashSet<ResolutionChoice>();
            var list = new List<ResolutionChoice>();
            if (available != null)
            {
                for (int i = 0; i < available.Count; i++)
                {
                    ResolutionChoice choice = available[i];
                    if (choice.Width >= minWidth && choice.Height > 0 && seen.Add(choice))
                    {
                        list.Add(choice);
                    }
                }
            }

            if (current.Width > 0 && current.Height > 0 && seen.Add(current))
            {
                list.Add(current);
            }

            list.Sort((l, r) => l.Width != r.Width ? r.Width.CompareTo(l.Width) : r.Height.CompareTo(l.Height));
            return list;
        }

        /// <summary>The index of <paramref name="wanted"/> in <paramref name="choices"/>, or of the closest choice by area.</summary>
        public static int IndexOf(IReadOnlyList<ResolutionChoice> choices, ResolutionChoice wanted)
        {
            if (choices == null || choices.Count == 0)
            {
                return -1;
            }

            int best = 0;
            long bestDistance = long.MaxValue;
            long area = (long)wanted.Width * wanted.Height;
            for (int i = 0; i < choices.Count; i++)
            {
                if (choices[i].Equals(wanted))
                {
                    return i;
                }

                long distance = Math.Abs((long)choices[i].Width * choices[i].Height - area);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                }
            }

            return best;
        }
    }
}
