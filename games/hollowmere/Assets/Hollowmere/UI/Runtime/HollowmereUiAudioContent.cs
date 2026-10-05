// Hollowmere - HollowmereUiAudioContent: the UI and audio content asset GameBoot loads through UiAudioBootstrap (P1.5).
#nullable enable
using System;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Save;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using UnityEngine;

namespace Hollowmere.UiAudio
{
    /// <summary>The Hollowmere UI and audio content: the screen flow and the audio set.</summary>
    [CreateAssetMenu(menuName = "Hollowmere/UI and Audio Content", fileName = "UiAudio")]
    public sealed class HollowmereUiAudioContent : ScriptableObject, IGameplayWorldExtensionSource
    {
        /// <summary>The Resources path GameBoot loads (Assets/Hollowmere/UI/Resources/Hollowmere/UiAudio.asset).</summary>
        public const string ResourcePath = "Hollowmere/UiAudio";

        [SerializeField] private ScreenFlowDefinition? flow;
        [SerializeField] private AudioSetDefinition? audio;

        public ScreenFlowDefinition? Flow => flow;

        public AudioSetDefinition? Audio => audio;

        public void Configure(ScreenFlowDefinition? screenFlow, AudioSetDefinition? audioSet)
        {
            flow = screenFlow;
            audio = audioSet;
        }

        /// <summary>Builds the rig under <paramref name="host"/> (a GameObject or Transform) and adds the extensions.</summary>
        public void Contribute(WorldBuildOptions options, object? host)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Transform? parent = host is GameObject go ? go.transform : host as Transform;
            HollowmereUiAudio rig = HollowmereUiAudio.Create(this, parent, null);
            rig.AddTo(options);
        }
    }
}
