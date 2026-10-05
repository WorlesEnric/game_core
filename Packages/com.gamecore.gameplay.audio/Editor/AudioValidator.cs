// GameCore.Gameplay.Audio.Editor - the audio validator with stable codes (P1.5, catalog row 11).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEditor;
using UnityEngine.Audio;

namespace GameCore.Gameplay.Audio.Editor
{
    /// <summary>Validates audio banks, music states, ambiences and audio sets.</summary>
    [AuthorValidator("audio.validator", Codes = new[]
    {
        PresentationDiagnosticCodes.AudioMissingClip,
        PresentationDiagnosticCodes.AudioDuplicateId,
        PresentationDiagnosticCodes.AudioKeyCollision,
        PresentationDiagnosticCodes.AudioUnknownRegion,
        PresentationDiagnosticCodes.AudioMixerMissingParameter,
    })]
    public static class AudioValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(AudioBankDefinition bank)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (bank == null)
            {
                return diagnostics;
            }

            string subject = Subject(bank.AuthoringId, bank.name);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var keys = new Dictionary<int, string>();
            for (int i = 0; i < bank.Entries.Count; i++)
            {
                AudioBankEntry entry = bank.Entries[i];
                if (entry.Clip == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioMissingClip, subject, "clip '" + entry.Id + "' has no AudioClip"));
                }

                if (!ids.Add(entry.Id))
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioDuplicateId, subject, "clip id '" + entry.Id + "' repeats"));
                }
                else if (keys.TryGetValue(entry.Key, out string? other))
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioKeyCollision, subject,
                        "clip ids '" + other + "' and '" + entry.Id + "' have one key"));
                }
                else
                {
                    keys.Add(entry.Key, entry.Id);
                }
            }

            if (bank.Mixer != null)
            {
                diagnostics.AddRange(ValidateMixer(bank.Mixer, subject));
            }

            return diagnostics;
        }

        /// <summary>Every exposed volume parameter the runtime writes must exist on the mixer.</summary>
        public static IReadOnlyList<GameplayDiagnostic> ValidateMixer(AudioMixer mixer, string subject)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (mixer == null)
            {
                return diagnostics;
            }

            var names = new List<string>(AudioMixerBinding.Parameters) { AudioMixerBinding.AmbienceParameter };
            for (int i = 0; i < names.Count; i++)
            {
                if (!mixer.GetFloat(names[i], out float _))
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioMixerMissingParameter, subject,
                        mixer.name + " exposes no parameter '" + names[i] + "'"));
                }
            }

            return diagnostics;
        }

        public static IReadOnlyList<GameplayDiagnostic> Validate(AudioSetDefinition set)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (set == null)
            {
                return diagnostics;
            }

            string subject = Subject(set.AuthoringId, set.name);
            if (set.Bank == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioMissingClip, subject, set.name + " has no bank"));
            }
            else
            {
                diagnostics.AddRange(Validate(set.Bank));
            }

            var states = new Dictionary<int, string>();
            for (int i = 0; i < set.MusicStates.Count; i++)
            {
                MusicStateDefinition state = set.MusicStates[i];
                if (state == null)
                {
                    continue;
                }

                if (states.TryGetValue(state.Key, out string? other))
                {
                    diagnostics.Add(new GameplayDiagnostic(other == state.StateId ? PresentationDiagnosticCodes.AudioDuplicateId : PresentationDiagnosticCodes.AudioKeyCollision,
                        subject, "music states '" + other + "' and '" + state.StateId + "' share a key"));
                }
                else
                {
                    states.Add(state.Key, state.StateId);
                }

                RequireClip(set, state.ClipId, "music state " + state.StateId, subject, diagnostics, false);
                RequireClip(set, state.StingerId, "stinger of " + state.StateId, subject, diagnostics, true);
            }

            if (set.StartMusicState.Length > 0 && set.FindState(set.StartMusicState) == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioDuplicateId, subject,
                    "start music state '" + set.StartMusicState + "' is not one of the set's states"));
            }

            var regions = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < set.Ambiences.Count; i++)
            {
                AmbienceDefinition ambience = set.Ambiences[i];
                if (ambience == null)
                {
                    continue;
                }

                if (ambience.Region == null || ambience.RegionId.Length == 0)
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioUnknownRegion, subject, ambience.name + " names no region"));
                }
                else if (!regions.Add(ambience.RegionId))
                {
                    diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioDuplicateId, subject, "two ambiences for region " + ambience.Region.name));
                }

                RequireClip(set, ambience.ClipId, ambience.name, subject, diagnostics, false);
            }

            return diagnostics;
        }

        private static void RequireClip(AudioSetDefinition set, string clipId, string what, string subject, List<GameplayDiagnostic> diagnostics, bool optional)
        {
            if (clipId.Length == 0)
            {
                return;
            }

            if (set.Bank == null || !set.Bank.TryGet(clipId, out AudioBankEntry? entry) || entry == null || entry.Clip == null)
            {
                diagnostics.Add(new GameplayDiagnostic(PresentationDiagnosticCodes.AudioMissingClip, subject,
                    what + " uses clip '" + clipId + "', which the bank does not hold" + (optional ? " (stinger)" : string.Empty)));
            }
        }

        private static string Subject(string authoringId, string name) => string.IsNullOrEmpty(authoringId) ? name : authoringId;

        internal static void Refuse(IReadOnlyList<GameplayDiagnostic> diagnostics)
        {
            if (diagnostics.Count > 0)
            {
                throw new ArgumentException(diagnostics[0].Code + ": " + diagnostics[0].Message);
            }
        }

        internal static bool IsAsset(UnityEngine.Object value) => value != null && AssetDatabase.Contains(value);
    }
}
