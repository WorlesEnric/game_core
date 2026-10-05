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

    public sealed class SaltmarshUiAudio : MonoBehaviour
    {
        private UiRuntime? ui;
        private AudioRuntime? sound;

        public UiRuntime Ui => ui ?? throw new InvalidOperationException("the rig is not built");

        public AudioRuntime Audio => sound ?? throw new InvalidOperationException("the rig is not built");

        public SaltmarshUiAudioContent? Content { get; private set; }

        public UiRoot? Root { get; private set; }

        public AudioEngineHost? Engine { get; private set; }

        public UiHostDriver? Driver { get; private set; }

        public static SaltmarshUiAudio Create(SaltmarshUiAudioContent content, Transform? parent, UiScreen? startScreen)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var host = new GameObject("Saltmarsh UI and Audio");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            SaltmarshUiAudio rig = host.AddComponent<SaltmarshUiAudio>();
            rig.Build(content, startScreen);
            return rig;
        }

        public void AddTo(WorldBuildOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Extensions.Add(Ui.Extension);
            options.Extensions.Add(Audio.Extension);
        }

        public void UseSaves(SaveService service, Action<GameplayWorld>? prepareRestoredWorld) => Ui.UseSaves(service, prepareRestoredWorld);

        private void Build(SaltmarshUiAudioContent content, UiScreen? startScreen)
        {
            Content = content;
            ScreenFlowDefinition? flow = content.Flow;
            var settings = new UiSettingsStore();
            UiScreen fallback = flow != null ? flow.StartScreen : UiScreen.Menu;
            var options = new UiRuntimeOptions
            {
                StartScreen = startScreen ?? UiSettingsStore.ConsumeStartScreen(fallback),
                GameTitle = flow != null ? flow.GameTitle : "Saltmarsh",
                Messages = flow != null ? flow.BuildMessageTable() : new UiMessageTable(null),
                RegionBannerMs = flow != null ? flow.RegionBannerMs : 3000,
                Session = new SceneReloadSessionActions(),
                Settings = settings,
            };
            ui = new UiRuntime(options);
            var volumes = new int[4];
            for (int channel = 0; channel < volumes.Length; channel++)
            {
                volumes[channel] = settings.MirroredVolume(channel, SettingsRules.DefaultVolume);
            }

            sound = new AudioRuntime(content.Audio, new AudioRuntimeOptions { Volumes = volumes });
            sound.VolumeCommitted += settings.MirrorVolume;
            Driver = gameObject.AddComponent<UiHostDriver>();
            Driver.Runtime = ui;
            Engine = AudioEngineHost.Create(transform, sound);
            if (flow != null)
            {
                Root = UiRoot.Create(transform, ui, flow);
            }
        }
    }
}
