// GameCore.Gameplay.Contracts - the authoritative int32 slots of the UI and audio plugins (P1.5, catalog rows 10-11).
//
// Both plugins keep their state on one session target each, seeded under the world (root) scope at boot, so the state
// is captured and restored with every other slot (SADR-004). Other packages and Studio read these slots by id without
// a type dependency on the UI or audio packages.
//
//   UI plugin (owner gameplay.ui.owner), on the UI session target:
//     ui.screen       UiScreen 0..9 (none, hud, menu, pause, settings, save, load, ending, journal, inventory)
//     ui.returnTo     the screen a close of settings/save/load returns to
//     ui.message      stable key of the shown message (0 = none); the text comes from the UI message table
//   audio plugin (owner gameplay.audio.owner), on the audio session target:
//     audio.musicState     stable key of the music state (0 = silence)
//     audio.ambienceZone   region key of the ambience zone (0 = none)
//     audio.volumeMaster/Music/Sfx/Voice   permille 0..1000
#nullable enable
using System;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Owners, slot ids and session targets of the UI and audio plugins.</summary>
    public static class PresentationSlots
    {
        public const uint SchemaVersion = 1U;

        public static readonly OwnerId UiOwner = GameplayIds.Owner("ui.owner");

        public static readonly OwnerId AudioOwner = GameplayIds.Owner("audio.owner");

        public static readonly SlotId Screen = SlotNames.Of("ui", "screen");

        public static readonly SlotId ReturnTo = SlotNames.Of("ui", "returnTo");

        public static readonly SlotId Message = SlotNames.Of("ui", "message");

        public static readonly SlotId MusicState = SlotNames.Of("audio", "musicState");

        public static readonly SlotId AmbienceZone = SlotNames.Of("audio", "ambienceZone");

        public static readonly SlotId VolumeMaster = SlotNames.Of("audio", "volumeMaster");

        public static readonly SlotId VolumeMusic = SlotNames.Of("audio", "volumeMusic");

        public static readonly SlotId VolumeSfx = SlotNames.Of("audio", "volumeSfx");

        public static readonly SlotId VolumeVoice = SlotNames.Of("audio", "volumeVoice");

        /// <summary>The volume slot of a channel (0 master, 1 music, 2 sfx, 3 voice).</summary>
        public static bool TryVolumeSlot(int channel, out SlotId slot)
        {
            switch (channel)
            {
                case 0: slot = VolumeMaster; return true;
                case 1: slot = VolumeMusic; return true;
                case 2: slot = VolumeSfx; return true;
                case 3: slot = VolumeVoice; return true;
                default: slot = default(SlotId); return false;
            }
        }

        /// <summary>The UI session target of a world (world authoring id).</summary>
        public static TargetId UiSessionTarget(string worldId) => GameplayIds.Target("ui.session." + Require(worldId));

        /// <summary>The audio session target of a world (world authoring id).</summary>
        public static TargetId AudioSessionTarget(string worldId) => GameplayIds.Target("audio.session." + Require(worldId));

        /// <summary>
        /// The positive int31 key a slot or payload carries for a presentation id (a music state, a message, a clip, a
        /// speaker): FNV-1a over the UTF-8 bytes, top bit cleared, zero mapped to one. Deterministic and engine-free; the
        /// definitions refuse two ids with the same key (GP-AUD-003 / GP-UI-006).
        /// </summary>
        public static int KeyOf(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return 0;
            }

            unchecked
            {
                uint hash = 2166136261U;
                byte[] bytes = Encoding.UTF8.GetBytes(id);
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619U;
                }

                int value = (int)(hash & 0x7FFFFFFFU);
                return value == 0 ? 1 : value;
            }
        }

        private static string Require(string worldId)
        {
            if (string.IsNullOrEmpty(worldId))
            {
                throw new ArgumentException("A presentation session needs the world's authoring id.", nameof(worldId));
            }

            return worldId;
        }
    }
}
