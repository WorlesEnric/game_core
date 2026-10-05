# P1.5 ui-audio

Catalog rows 10 (UI and player flow) and 11 (audio and atmosphere) of the gameplay plugin library, with the Hollowmere
UI and audio content.

Branch: `worktree-agent-ab2fb374b37e8085c` (base main 444e266). Host clone: `~/wkspace/gc-studio/p1.5`.

## What was built

| Package / path | Content |
|---|---|
| `Packages/com.gamecore.gameplay.contracts/Runtime/Presentation/` (engine-free, additive) | `PresentationServices` (the composition point, one per world plan, keyed by interface type, survives restore). Interfaces: `IPromptPresenter`, `IUiIntentSink` + `UiIntent`, `IFeedbackSink` (System.Numerics.Vector3), `IDialogueView`, `IJournalView`, `IInventoryView`, `IVoiceLinePlayer`, `IDialogueInput`, `IInventoryInput`, `IVolumeSettingsSink`, `IGameplayPauseQuery`, `IPlayerSettings`. View models `DialogueViewModel`, `JournalViewModel`/`JournalQuestEntry`, `InventoryViewModel`/`InventorySlotEntry`. `PresentationSlots` (owners `ui.owner`/`audio.owner`, slot ids, session targets, `KeyOf(id)`). `PresentationDiagnosticCodes` (GP-UI-001..014, GP-AUD-001..021). `MediaGeneration` (`IMediaGenerationGateway`, `NullMediaGenerationGateway`, request/result). |
| `Packages/com.gamecore.rules.gameplay/Runtime/{Ui,Audio}` | `ScreenFlowRules` (open/close/command table, return-to for sub-screens, offered commands, host actions), `UiIntentRules`, `SaveSlotNaming`, `SettingsRules` (sensitivity, volume steps, resolution choices). `VolumeRules` (permille, dB), `MusicStateRules`, `AmbienceRules`, `CrossfadeSchedule` (equal-power, retarget). |
| `Packages/com.gamecore.gameplay.ui` | Kernel: UI plugin with slots `ui.screen`, `ui.returnTo`, `ui.message` on a per-world UI session target; routes `ui.open`, `ui.close`, `ui.command`; event `ScreenChanged` (from, to, host action, argument); `[DisableAutoCreation] UiCommandSystem` + `UiModule` (refusal trace). Runtime: `UiRuntime`, `UiViewModels` (INotifyBindablePropertyChanged, `[CreateProperty]`), `BindingHost`, `CommandDispatcher`, `UiSettingsStore` (PlayerPrefs), `UiRoot` (UIDocument, PanelSettings, theme, layers), `UiInput` (Input System), `UiHostDriver`, `SceneReloadSessionActions`. Definitions `UiDocumentDefinition` [ui.document], `ThemeDefinition` [ui.theme], `ScreenFlowDefinition` [ui.flow]. Editor: `ui.bind`, `ui.setCommand`, `ui.addScreen`, `ui.setTheme`, `ui.previewScreen`, `UiValidator`, `UiPreviewSession`. |
| `Packages/com.gamecore.gameplay.audio` | Kernel: audio plugin with slots `audio.musicState`, `audio.ambienceZone`, `audio.volumeMaster/Music/Sfx/Voice` on a per-world audio session target; routes `audio.setMusicState{state, stinger}`, `audio.setAmbienceZone{region}`, `audio.setVolume{channel, permille}`, `audio.playSfx{id, x, y, z}`, `audio.playVoice{clip, speaker}`, `audio.stopVoice`; events MusicStateChanged, AmbienceChanged, VolumeChanged, SfxPlayed, VoicePlayed, VoiceStopped; `AudioCommandSystem` + `AudioModule`. Runtime: `AudioRuntime`, `MusicController`, `AmbienceZoneBinder`, `LoopCrossfader`, `SfxPool` (IFeedbackSink), `VoicePlayer` (IVoiceLinePlayer), `AudioMixerBinding`, `AudioEngineHost`, `AnimationEventRelay`. Definitions `AudioBankDefinition` [audio.bank], `MusicStateDefinition` [audio.musicState], `AmbienceDefinition` [audio.ambience], `AudioSetDefinition` [audio.set]. Editor: `audio.assignClip`, `audio.setAmbience`, `audio.setMusicState`, `audio.generateVoice`, `audio.generateSfx`, `AudioValidator`, `ProceduralAudioGenerator`, `MediaGateways`. |
| `Packages/com.gamecore.gameplay.world` (P1.1 files, minimal additive seam) | `WorldExtensions.cs` (new): `IGameplayWorldExtension`, `IGameplayWorldExtensionSource`, `GameplayExtensionTarget`. `WorldBuildOptions.Extensions` and `.Presentation`; `WorldBuilder.Build` adds each extension's plugin, system, routes, lanes, readers, recipes, seed and mount steps; `WorldBuilder.Attach` attaches the extensions before the presentation frame; `GameplayWorld.Presentation` and `.Extensions`. |
| `Packages/com.gamecore.gameplay.contracts/Runtime/GameplayCatalogNames.cs` (P1.1 file, append only) | Catalog names and static entries for the ui and audio plugins, systems, session appliers, layouts and schemas. |
| `games/hollowmere` | Manifest and lock add `com.gamecore.gameplay.ui`, `.audio`, `.save`. `Assets/Hollowmere/UI`: nine UXML screens (`Screens/`), one theme (`Theme/Hollowmere.uss`, `HollowmereTheme.tss`), runtime rig `Runtime/HollowmereUiAudio.cs` (asmdef `Hollowmere.UiAudio`), authoring utility `Editor/HollowmereUiAudioAuthoring.cs` (asmdef `Hollowmere.UiAudio.Editor`). `Assets/Hollowmere/Audio/HollowmereMixer.mixer` (Master + Music/Ambience/Sfx/Voice, exposed `MasterVolume`, `MusicVolume`, `AmbienceVolume`, `SfxVolume`, `VoiceVolume`). Generated on the host by the authoring utility and committed: `Audio/Generated/*.wav` + `HollowmereAudio.manifest.json`, `Audio/Definitions/*`, `UI/Definitions/*`, `UI/Theme/HollowmerePanelSettings.asset`, `UI/Theme/HollowmereTheme.asset`, `UI/Resources/Hollowmere/UiAudio.asset`. `Boot/UiAudioBootstrap.cs`. Tests under `Tests/P1_5`. |
| `dotnet/tests/GameCore.Rules.Gameplay.Tests/{Ui,Audio}` | `ScreenFlowRulesTests`, `AudioRulesTests`. |

## GameBoot hook (for the integrator)

One line in `games/hollowmere/Assets/Hollowmere/Boot/GameBoot.cs`, `Start()`:

```csharp
// before
WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, new WorldBuildOptions { Name = "Hollowmere" });
// after
WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, UiAudioBootstrap.Configure(new WorldBuildOptions { Name = "Hollowmere" }, gameObject));
```

`UiAudioBootstrap` loads `Resources/Hollowmere/UiAudio` as an `IGameplayWorldExtensionSource`, so `Hollowmere.Boot`
needs no new asmdef reference. When the game has a save service (checkpoint codecs, P3.1), connect it once after
the attach: `GetComponentInChildren<HollowmereUiAudio>().UseSaves(service, restored => { /* scene loader, views */ })`.
Without it the save and load screens show GP-UI-014.

P1.3 player input should gate gameplay actions on `world.Presentation.Get<IGameplayPauseQuery>()?.GameplayPaused`
and raise `UiIntent.Pause/Journal/Inventory` through `world.Presentation.Get<IUiIntentSink>()`. Its look input reads
`IPlayerSettings.LookSensitivity`. P1.4 calls `IDialogueView`, `IJournalView`, `IInventoryView` and
`IVoiceLinePlayer`, and registers `IDialogueInput` and `IInventoryInput`.

## API

### Composition

```csharp
var options = new WorldBuildOptions { Name = "Game" };
options.Extensions.Add(ui.Extension);      // UiRuntime.Extension
options.Extensions.Add(audio.Extension);   // AudioRuntime.Extension
WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog, fingerprint, options);
// world.Presentation (= plan.Presentation) holds the services after attach:
world.Presentation.Get<IUiIntentSink>()?.Raise(UiIntent.Pause);
```

Every runtime re-registers its services on every attach (also after a restore), so a consumer always resolves through
`world.Presentation` and never caches across a restore.

### UI

* `UiRuntime(UiRuntimeOptions)`: `Extension`, `Models`, `Dispatcher`, `Settings`, `World`, `Commands`
  (`UiCommandIssuer`: `Open(screen)`, `Close()`, `Command(action, arg)`, `State()`), `Screen`, `GameplayPaused`,
  `UseSaves(SaveService, Action<GameplayWorld>?)`, `RunHostActions()`, `Raise(UiIntent)`, `Navigate(dx, dy)`,
  `SetEnding(title, body)`, events `WorldReplaced`, `HostActionDone`, `ActivateRequested`.
* Binding sources: `vm:<model>.<Property>` (models `screen`, `hud`, `prompt`, `dialogue`, `journal`, `inventory`, `menu`,
  `settings`, `saveload`, `ending`), `slot:<owner>/<domain>.<member>@<session|audio|focus|authoringId>`,
  `event:<region-entered|message|screen>`, `command:<name>`. Properties `text`, `value`, `visible`, `enabled`,
  `selected` (format `offset:<n>`), `items` (format `command:<prefix>`; a DropdownField gets choices), `clicked`,
  `changed`. Formats: `{0}` patterns and `percent:<max>`.
* Commands (`CommandDispatcher.Dispatch(name, value)`): `open.<screen>`, `close`, `newgame`, `continue`, `quit`,
  `restart`, `quitToMenu`, `resume`, `applySettings`, `save.<n>`, `load.<n>`, `delete.<n>`, `slot.<n|selected>`,
  `select.<i>`, `choose.<i|selected>`, `advance`, `use`, `drop`, `item.<i>`, `volume.<channel>`,
  `volumeStep.<channel>`, `sensitivity`, `resolution[.<i>]`, `fullscreen`.
* Message ids committed as `ui.message`: `ui.saved`, `ui.loaded`, `ui.deleted`, `ui.no-save`, `ui.save-unavailable`,
  and every `save.*` refusal id; the flow's message table gives their text.

### Audio

* `AudioRuntime(AudioSetDefinition?, AudioRuntimeOptions?)`: `Extension`, `Music`, `Ambience`, `Sfx`, `Voice`,
  `Mixer`, `Commands` (`AudioCommandIssuer`: `SetMusicState(state, stinger)`, `SetAmbienceZone(regionKey)`,
  `SetVolume(channel, permille)`, `PlaySfx(id, x, y, z)`, `PlayVoice(clip, speaker)`, `StopVoice()`, `Read(slot)`),
  `MusicState`, `AmbienceZone`, `Tick()`, event `VolumeCommitted(channel, permille)`.
* Keys: every id (state, clip, speaker) travels as `PresentationSlots.KeyOf(id)`; the ambience zone is the region key.
* Feedback ids resolve in the bank directly or with the set's prefix (`footstep` -> `sfx.footstep`, `ui.click` ->
  `sfx.ui.click`).

## Decisions where 05 was silent

1. **Two kernel plugins through a world extension seam.** UI and audio state are kernel slots on one session target
   per world (in the world scope), so they are saved, restored and replayed like gameplay state. P1.1's
   `WorldBuilder` has no plugin list, so I added the smallest additive seam: `WorldBuildOptions.Extensions`
   (`IGameplayWorldExtension`). Nothing changes for a build without extensions.
2. **`PresentationServices` is owned by the build plan**, not static. The plan survives a restore, so the
   registrations do too; runtimes re-register on attach.
3. **`ui.returnTo` slot.** 05 lists `ui.screen` and `ui.message`; sub-screens (settings, save, load) need to know where
   to return (pause or menu), so a third slot holds it.
4. **One event, `ScreenChanged`, carries the host action** (new game, continue, save/load/delete slot n, restart,
   quit). The host runs it between frames (`UiHostDriver.LateUpdate`), never inside the pump, then commits the outcome
   as `ui.message`. A successful load commits `ui.command{Loaded}`, which lands on the HUD from any screen.
5. **Pause does not pause the root.** UI commands must commit while paused, so `ui.screen != Hud` is exposed as
   `IGameplayPauseQuery.GameplayPaused` and gameplay input gates on it.
6. **Volumes are slots** (saved with the game) and are mirrored to PlayerPrefs on every commit, so a new game starts
   with the player's volumes. Sensitivity, resolution and fullscreen are machine preferences (PlayerPrefs only).
7. **Ambience direction is a presentation-side director**: the audio runtime sees the focus traveller's RegionEntered,
   picks the zone with `AmbienceRules.ZoneForRegion` (a region without ambience keeps the previous one) and submits
   `audio.setAmbienceZone` through its `IGameplayInputSource` before the next step.
8. **No uGUI EventSystem.** UI Toolkit's own focus navigation handles buttons. `UiInput` adds Input System actions
   created in code: navigate (arrows, WASD, d-pad, left stick) for lists and dialogue choices, submit for dialogue,
   cancel (Backspace / gamepad east). Escape stays P1.3's Pause action.
9. **Headless**: `UiRoot` and the AudioSources are created only with a graphics device. The runtimes, their binders
   (which touch no engine object), the host-action driver and the fade driver exist in every mode.
10. **Media generation tier.** 05 says tier "Agent"; 03's `ToolTier` has no such member, so `audio.generateVoice` and
    `audio.generateSfx` are `Compose` tools with `Requires = "agent.media"`. `MediaGateways.Resolve()` finds the
    highest-priority `IMediaGenerationGateway` type with a parameterless constructor (P2.2 adds one; nothing is cached)
    and falls back to the null gateway, which answers NotConfigured (GP-AUD-020).
11. **Hand-written mixer.** The AudioMixer YAML is written by a script with deterministic GUIDs. The authoring utility
    references it from the bank, and the validator checks every exposed parameter (GP-AUD-005).

## Verification

See "Host runs" below. No compile, build or test ran on the Mac. Mac-side checks: `tools/check_package_metadata.py`,
`tools/check_game_core_csharp.py`, `tools/validate_game_core_docs.py`, `tools/check_contract_surface_parity.py` pass.

## Files outside my exclusive paths (integrator)

* `Packages/com.gamecore.gameplay.world/Runtime/WorldBuilder.cs`, `GameplayWorld.cs`, new `WorldExtensions.cs` (seam).
* `Packages/com.gamecore.gameplay.contracts/Runtime/GameplayCatalogNames.cs` (appended entries) and the re-baked
  Hollowmere catalog outputs (`World/Generated/HollowmereCatalog.g.cs`, `World/Catalog/*`,
  `World/Hollowmere.manifest.asset`). Other packets that append catalog entries need one re-bake after the merge.
* `dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj` (two Compile globs).
* `UiWorldExtension.cs`/`AudioWorldExtension.cs` pass `null` schema defaults (no `GameCore.Composition` reference).

## Open

* Real voice/sfx generation waits for the P2.2 gateway (etos op `tts`); the tools answer NotConfigured until then.
* Hollowmere has no checkpoint codecs; the PlayMode test uses a verbatim copy of the validation project's generated
  checkpoint catalog under `Tests/P1_5/Checkpoint`. Production saves need the game's codecs (P3.1) and the
  `UseSaves` call in GameBoot.
* HUD stamina is bound to `vm:hud.Stamina`, which nothing fills yet. P1.3 binds its stamina slot with `ui.bind`
  (`slot:<owner>/<domain>.<member>@focus`) or sets the model.
* `ui.previewScreen` capture is skipped on the headless host (reason GP-UI-008); it needs a graphical Editor.
* The LineShown event is P1.4's; the tests deliver the dialogue view model through `IDialogueView`, as P1.4's presenter
  will.
