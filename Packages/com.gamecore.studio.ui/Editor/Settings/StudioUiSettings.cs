// GameCore.Studio.UI - project settings of the Studio UI (ProjectSettings/GameCoreStudioUI.asset): preview ghost
// material, picking options and the viewport's rendering cadence. Per-user conveniences (first-run wizard shown) live in
// EditorPrefs instead.
#nullable enable
using GameCore.Studio.Authoring;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>Studio UI project settings.</summary>
    [FilePath("ProjectSettings/GameCoreStudioUI.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class StudioUiSettings : ScriptableSingleton<StudioUiSettings>
    {
        /// <summary>EditorPrefs key: the first-run wizard was dismissed with "don't show again".</summary>
        public const string FirstRunPrefKey = "GameCore.Studio.FirstRunDone";

        [SerializeField]
        private Material? ghostMaterial;

        [SerializeField]
        private bool includeUi = true;

        [SerializeField]
        private float overlapDepthFraction = 0.005f;

        [SerializeField]
        private float groundHeight;

        [SerializeField]
        private int overlapRadiusPixels = 4;

        [SerializeField]
        private bool fullContainmentDefault;

        [SerializeField]
        private int editModeRenderHz = 15;

        /// <summary>Material for preview ghosts; null keeps the engine's translucent ghost material.</summary>
        public Material? GhostMaterial
        {
            get => ghostMaterial;
            set
            {
                ghostMaterial = value;
                Persist();
            }
        }

        public bool IncludeUi
        {
            get => includeUi;
            set
            {
                includeUi = value;
                Persist();
            }
        }

        /// <summary>Relative depth band of an overlap group (03 s2 default 0.5 %).</summary>
        public float OverlapDepthFraction
        {
            get => overlapDepthFraction;
            set
            {
                overlapDepthFraction = Mathf.Clamp(value, 0f, 0.2f);
                Persist();
            }
        }

        public float GroundHeight
        {
            get => groundHeight;
            set
            {
                groundHeight = value;
                Persist();
            }
        }

        /// <summary>Click samples within this many pixels feed the overlap list (P2.1: 4 px).</summary>
        public int OverlapRadiusPixels
        {
            get => overlapRadiusPixels;
            set
            {
                overlapRadiusPixels = Mathf.Clamp(value, 0, 16);
                Persist();
            }
        }

        /// <summary>Marquee requires full containment by default.</summary>
        public bool FullContainmentDefault
        {
            get => fullContainmentDefault;
            set
            {
                fullContainmentDefault = value;
                Persist();
            }
        }

        /// <summary>How often the viewport re-renders the free camera in Edit mode when nothing changed.</summary>
        public int EditModeRenderHz
        {
            get => editModeRenderHz;
            set
            {
                editModeRenderHz = Mathf.Clamp(value, 1, 60);
                Persist();
            }
        }

        /// <summary>Picking options built from these settings.</summary>
        public PickOptions CreatePickOptions()
        {
            return new PickOptions
            {
                IncludeUi = includeUi,
                OverlapDepthFraction = overlapDepthFraction,
                GroundHeight = groundHeight,
            };
        }

        public static bool FirstRunDone
        {
            get => EditorPrefs.GetBool(FirstRunPrefKey, false);
            set => EditorPrefs.SetBool(FirstRunPrefKey, value);
        }

        private void Persist() => Save(true);
    }
}
