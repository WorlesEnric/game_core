#nullable enable
using System;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Save;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using UnityEngine;

namespace Saltmarsh.UiAudio
{
    [CreateAssetMenu(menuName = "Saltmarsh/UI and Audio Content", fileName = "UiAudio")]
    public sealed class SaltmarshUiAudioContent : ScriptableObject, IGameplayWorldExtensionSource
    {
        public const string ResourcePath = "Saltmarsh/UiAudio";

        [SerializeField] private ScreenFlowDefinition? flow;
        [SerializeField] private AudioSetDefinition? audio;

        public ScreenFlowDefinition? Flow => flow;

        public AudioSetDefinition? Audio => audio;

        public void Configure(ScreenFlowDefinition? screenFlow, AudioSetDefinition? audioSet)
        {
            flow = screenFlow;
            audio = audioSet;
        }

        public void Contribute(WorldBuildOptions options, object? host)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Transform? parent = host is GameObject go ? go.transform : host as Transform;
            SaltmarshUiAudio rig = SaltmarshUiAudio.Create(this, parent, null);
            rig.AddTo(options);
        }
    }
}
