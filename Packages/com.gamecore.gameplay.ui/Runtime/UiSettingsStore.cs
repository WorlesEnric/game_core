// GameCore.Gameplay.Ui - the non-gameplay player preferences the settings screen persists in PlayerPrefs (P1.5).
//
// Look sensitivity, resolution and fullscreen are machine preferences: never part of a save, never a slot. Volumes are
// gameplay-visible settings in the audio plugin's slots; the store only mirrors the last committed volumes so a new
// world starts with the player's levels (the audio extension seeds its slots from the mirror). One store per UI
// runtime; PlayerPrefs is the persistence, the store holds no static state. Applying a resolution touches the screen
// only when a graphics device exists.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Rules.Gameplay.Ui;
using UnityEngine;

namespace GameCore.Gameplay.Ui
{
    /// <summary>PlayerPrefs-backed player preferences (IPlayerSettings).</summary>
    public sealed class UiSettingsStore : IPlayerSettings
    {
        public const string Prefix = "GameCore.Ui.";
        public const string SensitivityKey = Prefix + "LookSensitivity";
        public const string ResolutionWidthKey = Prefix + "ResolutionWidth";
        public const string ResolutionHeightKey = Prefix + "ResolutionHeight";
        public const string FullscreenKey = Prefix + "Fullscreen";
        public const string VolumeKeyPrefix = Prefix + "Volume";
        public const string StartScreenOverrideKey = Prefix + "StartScreenOverride";

        public UiSettingsStore()
        {
            LookSensitivity = SettingsRules.ClampSensitivity(PlayerPrefs.GetFloat(SensitivityKey, SettingsRules.DefaultSensitivity));
            Fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) != 0;
            Resolution = new ResolutionChoice(PlayerPrefs.GetInt(ResolutionWidthKey, Screen.width), PlayerPrefs.GetInt(ResolutionHeightKey, Screen.height));
        }

        public event Action? Changed;

        public float LookSensitivity { get; private set; }

        public bool Fullscreen { get; private set; }

        public ResolutionChoice Resolution { get; private set; }

        /// <summary>True when screen changes are applied (a graphics device exists).</summary>
        public bool AppliesToScreen => !BinderEnvironment.IsHeadless;

        public int SaveCount { get; private set; }

        public void SetLookSensitivity(float value)
        {
            float clamped = SettingsRules.ClampSensitivity(value);
            if (Math.Abs(clamped - LookSensitivity) < 1e-6f)
            {
                return;
            }

            LookSensitivity = clamped;
            PlayerPrefs.SetFloat(SensitivityKey, clamped);
            Persist();
        }

        public void SetFullscreen(bool value)
        {
            if (value == Fullscreen)
            {
                return;
            }

            Fullscreen = value;
            PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0);
            if (AppliesToScreen)
            {
                Screen.fullScreen = value;
            }

            Persist();
        }

        public void SetResolution(ResolutionChoice choice)
        {
            if (choice.Width <= 0 || choice.Height <= 0 || choice.Equals(Resolution))
            {
                return;
            }

            Resolution = choice;
            PlayerPrefs.SetInt(ResolutionWidthKey, choice.Width);
            PlayerPrefs.SetInt(ResolutionHeightKey, choice.Height);
            if (AppliesToScreen)
            {
                Screen.SetResolution(choice.Width, choice.Height, Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed);
            }

            Persist();
        }

        /// <summary>The resolution choices of this machine (the current size when headless).</summary>
        public IReadOnlyList<ResolutionChoice> ResolutionChoices()
        {
            var available = new List<ResolutionChoice>();
            if (AppliesToScreen)
            {
                UnityEngine.Resolution[] modes = Screen.resolutions;
                for (int i = 0; i < modes.Length; i++)
                {
                    available.Add(new ResolutionChoice(modes[i].width, modes[i].height));
                }
            }

            return SettingsRules.Choices(available, Resolution, 640);
        }

        /// <summary>The mirrored volume of a channel (permille), or <paramref name="fallback"/>.</summary>
        public int MirroredVolume(int channel, int fallback) =>
            SettingsRules.ClampVolume(PlayerPrefs.GetInt(VolumeKeyPrefix + channel.ToString(System.Globalization.CultureInfo.InvariantCulture), fallback));

        /// <summary>Mirrors a committed volume so the next world starts with it.</summary>
        public void MirrorVolume(int channel, int permille)
        {
            string key = VolumeKeyPrefix + channel.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int clamped = SettingsRules.ClampVolume(permille);
            if (PlayerPrefs.GetInt(key, -1) == clamped)
            {
                return;
            }

            PlayerPrefs.SetInt(key, clamped);
            PlayerPrefs.Save();
        }

        /// <summary>Asks the next boot to start on <paramref name="screen"/> (new game / restart reload the boot scene).</summary>
        public static void RequestStartScreen(UiScreen screen)
        {
            PlayerPrefs.SetInt(StartScreenOverrideKey, (int)screen);
            PlayerPrefs.Save();
        }

        /// <summary>Takes a pending start screen request (and clears it), or returns <paramref name="fallback"/>.</summary>
        public static UiScreen ConsumeStartScreen(UiScreen fallback)
        {
            int value = PlayerPrefs.GetInt(StartScreenOverrideKey, -1);
            if (value < 0)
            {
                return fallback;
            }

            PlayerPrefs.DeleteKey(StartScreenOverrideKey);
            PlayerPrefs.Save();
            return ScreenFlowRules.IsKnown(value) ? (UiScreen)value : fallback;
        }

        private void Persist()
        {
            PlayerPrefs.Save();
            SaveCount++;
            Changed?.Invoke();
        }
    }
}
