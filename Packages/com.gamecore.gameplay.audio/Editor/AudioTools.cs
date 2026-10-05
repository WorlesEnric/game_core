// GameCore.Gameplay.Audio.Editor - audio authoring operations (P1.5, catalog row 11; Studio 03 s4/s5, 04 s5).
//
//   audio.assignClip     put an imported AudioClip into a bank under an id (group, volume, loop, 3D)
//   audio.setAmbience    give a region its ambience loop (creates the AmbienceDefinition and adds it to the set)
//   audio.setMusicState  add or edit a music state of a set (loop clip, crossfade, stinger), optionally the start state
//   audio.generateVoice  request a spoken line through the media gateway (agent tool)
//   audio.generateSfx    request a sound effect (agent tool; no gateway operation exists yet)
// The generate tools never call a provider: voice goes through P1.4's IMediaGenerationGateway.RequestVoiceLine, whose
// default (NotConfiguredMediaGateway) answers NotConfigured until P2.2 supplies a gateway; a Requested line arrives
// later as a candidate clip, which audio.assignClip puts into the bank. The gateway contract has no sound-effect request,
// so audio.generateSfx answers NotConfigured (GP-AUD-020). Nothing changes on disk in either case. 05 calls their tier
// "Agent"; 03's ToolTier has no such member, so they are Compose tools with Requires = "agent.media".
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace GameCore.Gameplay.Audio.Editor
{
    /// <summary>The audio.* authoring operations.</summary>
    public static class AudioTools
    {
        public const string AgentMedia = "agent.media";

        [AuthorOperation("audio.assignClip", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Live, Validator = typeof(AudioValidator),
            Doc = "Puts an imported AudioClip into an audio bank under a stable id, with its mixer group, volume, loop and 3D settings.")]
        public static AudioBankEntry AssignClip(
            AudioBankDefinition bank,
            [AuthorArg(Doc = "Stable clip id (e.g. sfx.footstep, ambience.marsh).")] string clipId,
            [AuthorArg(Category = "asset.audioClip", Doc = "The imported clip.")] AudioClip clip,
            [AuthorArg(Required = false, Doc = "Mixer group.")] AudioGroup group = AudioGroup.Sfx,
            [AuthorArg(Required = false, Min = 0, Max = 1, Doc = "Linear volume before the mixer.")] float volume = 1f,
            [AuthorArg(Required = false, Doc = "Loop (ambience and music beds).")] bool loop = false,
            [AuthorArg(Required = false, Doc = "Positional (3D) playback.")] bool spatial = false)
        {
            if (bank == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": a bank is required");
            }

            if (string.IsNullOrEmpty(clipId) || PresentationSlots.KeyOf(clipId) == 0)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioDuplicateId + ": a clip id is required");
            }

            if (clip == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": clip '" + clipId + "' needs an AudioClip");
            }

            int key = PresentationSlots.KeyOf(clipId);
            for (int i = 0; i < bank.Entries.Count; i++)
            {
                if (bank.Entries[i].Key == key && !string.Equals(bank.Entries[i].Id, clipId, StringComparison.Ordinal))
                {
                    throw new ArgumentException(PresentationDiagnosticCodes.AudioKeyCollision + ": '" + clipId + "' collides with '" + bank.Entries[i].Id + "'");
                }
            }

            Undo.RecordObject(bank, "audio.assignClip");
            bank.EnsureAuthoringId();
            AudioBankEntry entry = bank.Assign(clipId, clip, group, Mathf.Clamp01(volume), loop, spatial);
            EditorUtility.SetDirty(bank);
            return entry;
        }

        [AuthorOperation("audio.setAmbience", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild, Validator = typeof(AudioValidator),
            Doc = "Gives a region its ambience loop: creates or edits the region's AmbienceDefinition and adds it to the audio set.")]
        public static AmbienceDefinition SetAmbience(
            AudioSetDefinition set,
            [AuthorArg(Category = "world.region", Doc = "The region.")] RegionDefinition region,
            [AuthorArg(Doc = "Bank clip id of the loop.")] string clipId,
            [AuthorArg(Required = false, Min = 0, Max = 1, Doc = "Loop volume.")] float volume = 0.8f,
            [AuthorArg(Required = false, Unit = "ms", Min = 0, Max = 20000, Doc = "Crossfade on entering the region.")] int fadeMs = 2500,
            [AuthorArg(Required = false, Doc = "Asset path of a new definition; defaults next to the set.")] string assetPath = "")
        {
            if (set == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": an audio set is required");
            }

            if (region == null || string.IsNullOrEmpty(region.AuthoringId))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioUnknownRegion + ": a region with an authoring id is required");
            }

            RequireBankClip(set, clipId);
            AmbienceDefinition? ambience = set.FindAmbience(region.AuthoringId);
            if (ambience == null)
            {
                ambience = ScriptableObject.CreateInstance<AmbienceDefinition>();
                ambience.EnsureAuthoringId();
                ambience.Configure(region, clipId, volume, fadeMs);
                string path = string.IsNullOrEmpty(assetPath) ? SiblingPath(set, "Ambience_" + Sanitize(region.name) + ".asset") : assetPath;
                AssetDatabase.CreateAsset(ambience, path);
                Undo.RegisterCreatedObjectUndo(ambience, "audio.setAmbience");
                Undo.RecordObject(set, "audio.setAmbience");
                set.AddAmbience(ambience);
                EditorUtility.SetDirty(set);
            }
            else
            {
                Undo.RecordObject(ambience, "audio.setAmbience");
                ambience.Configure(region, clipId, volume, fadeMs);
                EditorUtility.SetDirty(ambience);
            }

            return ambience;
        }

        [AuthorOperation("audio.setMusicState", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild, Validator = typeof(AudioValidator),
            Doc = "Adds or edits a music state of an audio set: its loop clip, crossfade and stinger; optionally makes it the start state.")]
        public static MusicStateDefinition SetMusicState(
            AudioSetDefinition set,
            [AuthorArg(Doc = "Stable state id (e.g. music.explore).")] string stateId,
            [AuthorArg(Required = false, Doc = "Bank clip id of the loop (empty = silence).")] string clipId = "",
            [AuthorArg(Required = false, Unit = "ms", Min = 0, Max = 20000, Doc = "Crossfade into the state.")] int fadeMs = 2000,
            [AuthorArg(Required = false, Doc = "Bank clip id of a stinger played on entry.")] string stingerId = "",
            [AuthorArg(Required = false, Doc = "Make it the state a new world starts in.")] bool start = false,
            [AuthorArg(Required = false, Doc = "Asset path of a new definition; defaults next to the set.")] string assetPath = "")
        {
            if (set == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": an audio set is required");
            }

            if (string.IsNullOrEmpty(stateId) || PresentationSlots.KeyOf(stateId) == 0)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioUnknownState + ": a state id is required");
            }

            int key = PresentationSlots.KeyOf(stateId);
            MusicStateDefinition? collision = set.FindState(key);
            if (collision != null && !string.Equals(collision.StateId, stateId, StringComparison.Ordinal))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioKeyCollision + ": '" + stateId + "' collides with '" + collision.StateId + "'");
            }

            if (clipId.Length > 0)
            {
                RequireBankClip(set, clipId);
            }

            if (stingerId.Length > 0)
            {
                RequireBankClip(set, stingerId);
            }

            MusicStateDefinition? state = set.FindState(stateId);
            if (state == null)
            {
                state = ScriptableObject.CreateInstance<MusicStateDefinition>();
                state.EnsureAuthoringId();
                state.Configure(stateId, clipId, fadeMs, stingerId);
                string path = string.IsNullOrEmpty(assetPath) ? SiblingPath(set, "MusicState_" + Sanitize(stateId) + ".asset") : assetPath;
                AssetDatabase.CreateAsset(state, path);
                Undo.RegisterCreatedObjectUndo(state, "audio.setMusicState");
                Undo.RecordObject(set, "audio.setMusicState");
                set.AddMusicState(state);
            }
            else
            {
                Undo.RecordObject(state, "audio.setMusicState");
                state.Configure(stateId, clipId, fadeMs, stingerId);
                EditorUtility.SetDirty(state);
                Undo.RecordObject(set, "audio.setMusicState");
            }

            if (start)
            {
                set.Configure(set.Bank, set.MusicStates, stateId, set.Ambiences);
            }

            EditorUtility.SetDirty(set);
            return state;
        }

        [AuthorOperation("audio.generateVoice", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Live, Requires = AgentMedia,
            Doc = "Requests a spoken line through the media gateway (etos op tts); the clip arrives later as a candidate for audio.assignClip. NotConfigured (GP-AUD-020) until a gateway exists.")]
        public static MediaGenerationResult GenerateVoice(
            AudioBankDefinition bank,
            [AuthorArg(Doc = "Bank clip id the line is meant for (e.g. voice.warden.greeting).")] string clipId,
            [AuthorArg(Doc = "The line to speak.")] string text,
            [AuthorArg(Required = false, Doc = "Voice preset.")] string voice = "",
            [AuthorArg(Required = false, Doc = "Speaker id (provenance).")] string speakerId = "")
        {
            return GenerateVoice(bank, clipId, text, voice, speakerId, MediaGateways.Resolve());
        }

        /// <summary>The same request through an explicit gateway.</summary>
        public static MediaGenerationResult GenerateVoice(AudioBankDefinition bank, string clipId, string text, string voice, string speakerId, IMediaGenerationGateway gateway)
        {
            RequireClipId(bank, clipId);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.MediaRefused + ": a line of text is required");
            }

            MediaGenerationResult result = (gateway ?? new NotConfiguredMediaGateway()).RequestVoiceLine(
                new VoiceGenerationRequest(clipId, 0, speakerId ?? string.Empty, text, voice ?? string.Empty));
            return result.Status == MediaGenerationStatus.NotConfigured
                ? new MediaGenerationResult(MediaGenerationStatus.NotConfigured, result.RequestId, PresentationDiagnosticCodes.MediaNotConfigured + ": " + result.Detail)
                : result;
        }

        [AuthorOperation("audio.generateSfx", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Live, Requires = AgentMedia,
            Doc = "Requests a sound effect; the media gateway contract has no sound-effect operation yet, so this answers NotConfigured (GP-AUD-020) and changes nothing.")]
        public static MediaGenerationResult GenerateSfx(
            AudioBankDefinition bank,
            [AuthorArg(Doc = "Bank clip id (e.g. sfx.door.creak).")] string clipId,
            [AuthorArg(Doc = "What the sound is.")] string description,
            [AuthorArg(Required = false, Unit = "ms", Min = 0, Max = 30000, Doc = "Length (0 = the gateway decides).")] int durationMs = 0)
        {
            RequireClipId(bank, clipId);
            if (string.IsNullOrWhiteSpace(description))
            {
                throw new ArgumentException(PresentationDiagnosticCodes.MediaRefused + ": a description is required");
            }

            return new MediaGenerationResult(
                MediaGenerationStatus.NotConfigured,
                string.Empty,
                PresentationDiagnosticCodes.MediaNotConfigured + ": the media gateway contract (IMediaGenerationGateway) has no sound-effect request; "
                    + "generate the clip elsewhere and assign it with audio.assignClip");
        }

        private static void RequireClipId(AudioBankDefinition bank, string clipId)
        {
            if (bank == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": a bank is required");
            }

            if (string.IsNullOrEmpty(clipId) || PresentationSlots.KeyOf(clipId) == 0)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioDuplicateId + ": a clip id is required");
            }
        }

        private static void RequireBankClip(AudioSetDefinition set, string clipId)
        {
            if (string.IsNullOrEmpty(clipId) || set.Bank == null || !set.Bank.TryGet(clipId, out AudioBankEntry? entry) || entry == null)
            {
                throw new ArgumentException(PresentationDiagnosticCodes.AudioMissingClip + ": the bank holds no clip '" + clipId + "'");
            }
        }

        private static string SiblingPath(UnityEngine.Object neighbour, string file)
        {
            string path = AssetDatabase.GetAssetPath(neighbour);
            string folder = string.IsNullOrEmpty(path) ? "Assets" : (Path.GetDirectoryName(path) ?? "Assets").Replace('\\', '/');
            return AssetDatabase.GenerateUniqueAssetPath(folder + "/" + file);
        }

        private static string Sanitize(string value)
        {
            var chars = new List<char>(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                chars.Add(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' ? c : '_');
            }

            return new string(chars.ToArray());
        }
    }

    /// <summary>
    /// Finds the media gateway: the first concrete IMediaGenerationGateway (ordinal by full type name) with a public
    /// parameterless constructor among the loaded editor types, other than P1.4's NotConfiguredMediaGateway (P2.2 adds
    /// one); otherwise NotConfiguredMediaGateway. Discovery only: nothing is cached or registered.
    /// </summary>
    public static class MediaGateways
    {
        public static IMediaGenerationGateway Resolve()
        {
            Type? chosen = null;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<IMediaGenerationGateway>())
            {
                if (type.IsAbstract || type.IsInterface || type == typeof(NotConfiguredMediaGateway) || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                if (chosen == null || string.CompareOrdinal(type.FullName, chosen.FullName) < 0)
                {
                    chosen = type;
                }
            }

            return chosen != null && Activator.CreateInstance(chosen) is IMediaGenerationGateway gateway ? gateway : new NotConfiguredMediaGateway();
        }
    }
}
