// GameCore.Gameplay.Contracts - stable diagnostic codes of the UI and audio groups (P1.5).
//
// Same rules as GameplayDiagnosticCodes: a code is a stable string, never renumbered or reused. Kept in its own file so
// the UI/audio packet adds codes without editing the P1.1 table.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Stable UI (GP-UI-nnn) and audio (GP-AUD-nnn) diagnostic codes.</summary>
    public static class PresentationDiagnosticCodes
    {
        // UI authoring
        public const string UiMissingDocument = "GP-UI-001";
        public const string UiUnknownElement = "GP-UI-002";
        public const string UiBadSource = "GP-UI-003";
        public const string UiUnknownScreen = "GP-UI-004";
        public const string UiDuplicateScreen = "GP-UI-005";
        public const string UiMessageKeyCollision = "GP-UI-006";
        public const string UiMissingTheme = "GP-UI-007";
        public const string UiPreviewUnavailable = "GP-UI-008";

        // UI runtime refusals (ui.open / ui.close / ui.command)
        public const string UiTransitionRefused = "GP-UI-010";
        public const string UiNothingToClose = "GP-UI-011";
        public const string UiCommandNotOffered = "GP-UI-012";
        public const string UiUnchanged = "GP-UI-013";
        public const string UiSaveUnavailable = "GP-UI-014";

        // Audio authoring
        public const string AudioMissingClip = "GP-AUD-001";
        public const string AudioDuplicateId = "GP-AUD-002";
        public const string AudioKeyCollision = "GP-AUD-003";
        public const string AudioUnknownRegion = "GP-AUD-004";
        public const string AudioMixerMissingParameter = "GP-AUD-005";

        // Audio runtime refusals
        public const string AudioUnknownState = "GP-AUD-010";
        public const string AudioUnchanged = "GP-AUD-011";
        public const string AudioUnknownChannel = "GP-AUD-012";
        public const string AudioVolumeOutOfRange = "GP-AUD-013";

        // Media generation (audio.generateVoice / audio.generateSfx through the etos companion)
        public const string MediaNotConfigured = "GP-AUD-020";
        public const string MediaRefused = "GP-AUD-021";

        public static IReadOnlyList<string> All { get; } = System.Array.AsReadOnly(new[]
        {
            UiMissingDocument, UiUnknownElement, UiBadSource, UiUnknownScreen, UiDuplicateScreen, UiMessageKeyCollision,
            UiMissingTheme, UiPreviewUnavailable,
            UiTransitionRefused, UiNothingToClose, UiCommandNotOffered, UiUnchanged, UiSaveUnavailable,
            AudioMissingClip, AudioDuplicateId, AudioKeyCollision, AudioUnknownRegion, AudioMixerMissingParameter,
            AudioUnknownState, AudioUnchanged, AudioUnknownChannel, AudioVolumeOutOfRange,
            MediaNotConfigured, MediaRefused,
        });
    }
}
