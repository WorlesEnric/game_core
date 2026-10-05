// Hollowmere - the game's UI and audio rig (P1.5): content asset, extension source and host component.
//
// HollowmereUiAudioContent (Resources/Hollowmere/UiAudio.asset) names the screen flow and the audio set. GameBoot reaches
// it through UiAudioBootstrap.Configure without referencing this assembly: Configure loads the asset as an
// IGameplayWorldExtensionSource and calls Contribute, which builds the rig on the boot object and adds the UI and audio
// extensions to the world build options. The rig (HollowmereUiAudio) holds the two runtimes for the life of the boot
// object: the UI root and the audio sources exist only when a graphics device exists; the runtimes, the host-action
// driver and the fade driver exist in every mode, so headless tests drive the same objects.
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

    /// <summary>The live UI and audio runtimes of one boot.</summary>
    public sealed class HollowmereUiAudio : MonoBehaviour
    {
        private UiRuntime? ui;
        private AudioRuntime? sound;

        public UiRuntime Ui => ui ?? throw new InvalidOperationException("the rig is not built");

        public AudioRuntime Audio => sound ?? throw new InvalidOperationException("the rig is not built");

        public HollowmereUiAudioContent? Content { get; private set; }

        /// <summary>The UI Toolkit root; null headless.</summary>
        public UiRoot? Root { get; private set; }

        public AudioEngineHost? Engine { get; private set; }

        public UiHostDriver? Driver { get; private set; }

        /// <summary>
        /// Builds the rig. <paramref name="startScreen"/> overrides the flow's start screen (null: a pending new-game
        /// request, else the flow's start screen).
        /// </summary>
        public static HollowmereUiAudio Create(HollowmereUiAudioContent content, Transform? parent, UiScreen? startScreen)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var host = new GameObject("Hollowmere UI and Audio");
            if (parent != null)
            {
                host.transform.SetParent(parent, false);
            }

            HollowmereUiAudio rig = host.AddComponent<HollowmereUiAudio>();
            rig.Build(content, startScreen);
            return rig;
        }

        /// <summary>Adds the UI and audio extensions to a world build.</summary>
        public void AddTo(WorldBuildOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            options.Extensions.Add(Ui.Extension);
            options.Extensions.Add(Audio.Extension);
        }

        /// <summary>Connects a save service (the game's checkpoint codecs) to the save and load screens.</summary>
        public void UseSaves(SaveService service, Action<GameplayWorld>? prepareRestoredWorld) => Ui.UseSaves(service, prepareRestoredWorld);

        private void Build(HollowmereUiAudioContent content, UiScreen? startScreen)
        {
            Content = content;
            ScreenFlowDefinition? flow = content.Flow;
            var settings = new UiSettingsStore();
            UiScreen fallback = flow != null ? flow.StartScreen : UiScreen.Menu;
            var options = new UiRuntimeOptions
            {
                StartScreen = startScreen ?? UiSettingsStore.ConsumeStartScreen(fallback),
                GameTitle = flow != null ? flow.GameTitle : "Hollowmere",
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
