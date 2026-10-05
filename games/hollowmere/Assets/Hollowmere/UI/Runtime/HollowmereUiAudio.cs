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
using System.Globalization;
using System.Threading.Tasks;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Save;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hollowmere.UiAudio
{

    /// <summary>The live UI and audio runtimes of one boot.</summary>
    public sealed class HollowmereUiAudio : MonoBehaviour
    {
        private UiRuntime? ui;
        private AudioRuntime? sound;
        private SaveService? saves;
        private BindingHost? gameBindings;
        private VisualElement? boundLayer;
        public Task<SaveResult>? PendingSave { get; private set; }

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
        public void UseSaves(SaveService service, Action<GameplayWorld>? prepareRestoredWorld)
        {
            saves = service;
            Ui.UseSaves(service, prepareRestoredWorld);
            BindGameCommands();
        }

        /// <summary>The game's UI command entry: manual saves finish asynchronously at a committed boundary.</summary>
        public UiDispatchResult Dispatch(string command, float value = 0)
        {
            int dot = command.IndexOf('.');
            string verb = dot < 0 ? command : command.Substring(0, dot);
            if (PendingSave != null && !PendingSave.IsCompleted
                && (verb == "save" || verb == "slot" || verb == "load" || verb == "delete"
                    || verb == "continue" || verb == "newgame" || verb == "restart" || verb == "quit"))
                return new UiDispatchResult(false, "save still being written");
            if (verb != "save" && !(verb == "slot" && Ui.Screen == UiScreen.Save))
                return Ui.Dispatcher.Dispatch(command, value);
            if (Ui.Screen != UiScreen.Save || saves == null)
                return new UiDispatchResult(false, "open the save screen first");
            string argument = dot < 0 ? string.Empty : command.Substring(dot + 1);
            int slot = argument == "selected" ? Ui.Models.SaveLoad.Selected
                : int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
            if (slot < 1 || slot > Ui.Options.SlotCount)
                return new UiDispatchResult(false, "invalid save slot");
            Ui.Models.SaveLoad.Selected = slot;
            Ui.Models.SaveLoad.Status = "Saving…";
            Ui.Models.SaveLoad.RefusalCode = string.Empty;
            PendingSave = SaveAndConfirm("slot-" + slot.ToString(CultureInfo.InvariantCulture));
            return new UiDispatchResult(true, "save started");
        }

        private async Task<SaveResult> SaveAndConfirm(string slot)
        {
            var captureClock = System.Diagnostics.Stopwatch.StartNew();
            Task<SaveResult> write = saves!.CaptureAsync(slot);
            Debug.Log("[P3.1b] save capture " + slot + " mainThreadMs="
                + captureClock.Elapsed.TotalMilliseconds.ToString("F3", CultureInfo.InvariantCulture));
            SaveResult result = await write;
            if (this == null) return result;
            Ui.RefreshSaveRows();
            Ui.Models.SaveLoad.RefusalCode = result.Refusal?.CodeId ?? string.Empty;
            Ui.Models.SaveLoad.Status = result.Succeeded ? "Saved to " + slot : result.Refusal!.ToString();
            Ui.Commands?.Command(UiAction.ShowMessage,
                PresentationSlots.KeyOf(result.Succeeded ? UiMessageIds.Saved : result.Refusal!.CodeId));
            return result;
        }

        // UiRoot owns document layout and navigation. Its public binding surface lets the game supply its command
        // callback, so mouse, keyboard/controller confirmation and autoplay all use the same asynchronous save path.
        private void BindGameCommands()
        {
            VisualElement? root = Root?.Document?.rootVisualElement;
            if (root == null || root.childCount == 0 || ReferenceEquals(boundLayer, root[0])) return;
            Root!.Bindings?.Clear();
            gameBindings?.Clear();
            gameBindings = new BindingHost(Ui.Models, (name, value) => Dispatch(name, value).Accepted);
            foreach (UiDocumentDefinition definition in Root.Flow!.Documents)
            {
                VisualElement layer = root.Q<VisualElement>("layer-" + definition.name);
                if (layer != null && definition.Uxml != null)
                {
                    // BindingHost.Clear detaches model subscriptions, but its command closures belong to the old
                    // elements. Re-clone their contents so a click cannot also call the generic synchronous saver.
                    layer.Clear();
                    definition.Uxml.CloneTree(layer);
                    gameBindings.Bind(layer, definition.Bindings);
                    if (definition.Screen == Ui.Screen) layer.Q<Button>()?.Focus();
                }
            }
            boundLayer = root[0];
        }

        private void LateUpdate()
        {
            if (saves == null) return;
            BindGameCommands();
            gameBindings?.Refresh(Ui.ReadSlotSource, Ui.EventValue);
        }

        private void OnDestroy() => gameBindings?.Clear();

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
