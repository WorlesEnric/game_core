# P1.5 ui-audio

Catalog rows 10 (UI and player flow) and 11 (audio and atmosphere) of the gameplay plugin library, with the Hollowmere
UI and audio content.

Branch: `worktree-agent-ab2fb374b37e8085c` (base main 444e266; main ad28bb3 with P1.3 merged in 8ff3ca6, main d04ce65 with
P1.4 merged in 9a5d8f5). Host clone: `~/wkspace/gc-studio/p1.5`. Since P1.3 and P1.4 finished, `Boot/**` is this packet's.

## What was built

| Package / path | Content |
|---|---|
| `Packages/com.gamecore.gameplay.contracts/Runtime/Presentation/` (engine-free, additive) | `PresentationServices` (the composition point, one per world plan, keyed by interface type, survives restore). Interfaces: `IDialogueInput`, `IInventoryInput` (by item key), `IVolumeSettingsSink`, `IGameplayPauseQuery`, `IPlayerSettings`. P1.3's `IPromptPresenter`, `UiIntent`/`IUiIntentSink`, `IFootstepSink`, `IFeedbackSink` and P1.4's `IDialogueView`, `IJournalView`, `IInventoryView`, `IVoiceLinePlayer`, `INarrativeMessageSink`, `IMediaGenerationGateway` (with their view models) are implemented, not redeclared. `PresentationSlots` (owners `ui.owner`/`audio.owner`, slot ids, session targets, `KeyOf(id)`). `PresentationDiagnosticCodes` (GP-UI-001..014, GP-AUD-001..021). |
| `Packages/com.gamecore.rules.gameplay/Runtime/{Ui,Audio}` | `ScreenFlowRules` (open/close/command table, return-to for sub-screens, offered commands, host actions), `UiIntentRules`, `SaveSlotNaming`, `SettingsRules` (sensitivity, volume steps, resolution choices). `VolumeRules` (permille, dB), `MusicStateRules`, `AmbienceRules`, `CrossfadeSchedule` (equal-power, retarget). |
| `Packages/com.gamecore.gameplay.ui` | Kernel: UI plugin with slots `ui.screen`, `ui.returnTo`, `ui.message` on a per-world UI session target; routes `ui.open`, `ui.close`, `ui.command`; event `ScreenChanged` (from, to, host action, argument); `[DisableAutoCreation] UiCommandSystem` + `UiModule` (refusal trace). Runtime: `UiRuntime`, `UiViewModels` (INotifyBindablePropertyChanged, `[CreateProperty]`), `BindingHost`, `CommandDispatcher`, `UiSettingsStore` (PlayerPrefs), `UiRoot` (UIDocument, PanelSettings, theme, layers), `UiInput` (Input System), `UiHostDriver`, `SceneReloadSessionActions`. Definitions `UiDocumentDefinition` [ui.document], `ThemeDefinition` [ui.theme], `ScreenFlowDefinition` [ui.flow]. Editor: `ui.bind`, `ui.setCommand`, `ui.addScreen`, `ui.setTheme`, `ui.previewScreen`, `UiValidator`, `UiPreviewSession`, `UiCatalogContributor`. |
| `Packages/com.gamecore.gameplay.audio` | Kernel: audio plugin with slots `audio.musicState`, `audio.ambienceZone`, `audio.volumeMaster/Music/Sfx/Voice` on a per-world audio session target; routes `audio.setMusicState{state, stinger}`, `audio.setAmbienceZone{region}`, `audio.setVolume{channel, permille}`, `audio.playSfx{id, x, y, z}`, `audio.playVoice{clip, speaker}`, `audio.stopVoice`; events MusicStateChanged, AmbienceChanged, VolumeChanged, SfxPlayed, VoicePlayed, VoiceStopped; `AudioCommandSystem` + `AudioModule`. Runtime: `AudioRuntime`, `MusicController`, `AmbienceZoneBinder`, `LoopCrossfader`, `SfxPool` (P1.3's `IFeedbackSink` and `IFootstepSink`), `VoicePlayer` (IVoiceLinePlayer), `AudioMixerBinding`, `AudioEngineHost`, `AnimationEventRelay`. Definitions `AudioBankDefinition` [audio.bank], `MusicStateDefinition` [audio.musicState], `AmbienceDefinition` [audio.ambience], `AudioSetDefinition` [audio.set]. Editor: `audio.assignClip`, `audio.setAmbience`, `audio.setMusicState`, `audio.generateVoice`, `audio.generateSfx`, `AudioValidator`, `ProceduralAudioGenerator`, `MediaGateways`, `AudioCatalogContributor`. |
| `Packages/com.gamecore.gameplay.world` (on top of P1.3's seam, additive) | The UI and audio extensions implement P1.3's `IGameplayWorldExtension`. `WorldExtensions.cs` (new) adds `IGameplayWorldTargets` (an extension's spawn recipes and world-scope session targets; `WorldBuilder.Build` adds the recipes and the seed steps), `GameplayExtensionTarget` and `IGameplayWorldExtensionSource`. `WorldBuildOptions.Presentation` / `WorldBuildPlan.Presentation`, `GameplayWorld.Presentation` and `.Extensions`. |
| Catalog registrations | `UiDeclarations` / `AudioDeclarations` `CatalogSchemas` + `CatalogEntries`, contributed through P1.3's `IGameplayCatalogContributor` (`UiCatalogContributor`, `AudioCatalogContributor`); `GameplayCatalogNames.cs` is untouched. |
| `games/hollowmere` | Manifest and lock add `com.gamecore.gameplay.ui`, `.audio`, `.save`. `Assets/Hollowmere/UI`: nine UXML screens (`Screens/`), one theme (`Theme/Hollowmere.uss`, `HollowmereTheme.tss`), runtime rig `Runtime/HollowmereUiAudio.cs` (asmdef `Hollowmere.UiAudio`), authoring utility `Editor/HollowmereUiAudioAuthoring.cs` (asmdef `Hollowmere.UiAudio.Editor`). `Assets/Hollowmere/Audio/HollowmereMixer.mixer` (Master + Music/Ambience/Sfx/Voice, exposed `MasterVolume`, `MusicVolume`, `AmbienceVolume`, `SfxVolume`, `VoiceVolume`). Generated on the host by the authoring utility and committed: `Audio/Generated/*.wav` + `HollowmereAudio.manifest.json`, `Audio/Definitions/*`, `UI/Definitions/*`, `UI/Theme/HollowmerePanelSettings.asset`, `UI/Theme/HollowmereTheme.asset`, `UI/Resources/Hollowmere/UiAudio.asset`. `Boot/GameBoot.cs` (boots the narrative world with UI and audio), `Boot/UiAudioBootstrap.cs`, `Boot/Boot.unity` (GameBoot references the content manifest). Tests under `Tests/P1_5`. |
| `dotnet/tests/GameCore.Rules.Gameplay.Tests/{Ui,Audio}` | `ScreenFlowRulesTests`, `AudioRulesTests`. |

## GameBoot (the real boot)

`games/hollowmere/Assets/Hollowmere/Boot/GameBoot.cs` `Start()`:

1. `build` = `WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 }` with P1.3's
   player, npc and interaction extensions;
2. `UiAudioBootstrap.Configure(build, gameObject)`: loads `Resources/Hollowmere/UiAudio`, adds the ui and audio
   extensions and builds the rig under GameBoot (UiRoot and AudioSources only with a graphics device);
3. `UiAudioBootstrap.AssignViews(rig, modules)`: P1.4's `modules.Dialogue.View`, `modules.Quest.View`,
   `modules.Inventory.View` = UiRuntime, `modules.Dialogue.VoicePlayer` = the audio VoicePlayer;
4. `HollowmereNarrative.Boot(manifest, content, modules, new GameApplicationBootOptions(), build, start: false)`
   (`NarrativeComposer` copies the whole plan, so the extensions and `Presentation` carry through; verified by
   `BootWiring`);
5. views, then `PlayerSession`/`NpcSession`/`InteractionSession.Install` as before;
6. `HollowmereNarrative.Wire(game, interactionExtension, Interactions, Npcs)`;
7. `UiAudioBootstrap.Wire(world, Player, Interactions, game, modules)`: `Focus.Prompts`, `Input.UiIntents`,
   `Locomotion.Footsteps`, `Dispatcher.Feedback`, the pause gate on `Input.Source`, `game.Runtime.UseFeedback` and
   `UseMessages`, and the UI's `IDialogueInput` (DialogueRunner) and `IInventoryInput` (InventoryCommands) - 9 seams;
8. `game.Root.Start()`. `OnDestroy` calls `NarrativeWorld.Shutdown()`.

The root starts after the sessions are installed and wired (step 8), as P1.3's GameBoot did, rather than with
`start: true`. The UI starts on the main menu, which pauses gameplay input (movement, look, run, jump and interact are
dropped; Pause, Journal and Inventory still reach the UI); New Game shows the HUD.

Saving: Hollowmere still has no checkpoint codecs, so GameBoot does not call `UseSaves` and the save and load screens
answer GP-UI-014. When P3.1 adds codecs: `UiAudioBootstrap.RigOf(gameObject).UseSaves(service, restored => ...)`; a
restore must then also re-attach the narrative modules and the P1.3 sessions to the restored root (the UI's restore
path only re-attaches the world plan).

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
  `UseSaves(SaveService, Action<GameplayWorld>?)`, `RunHostActions()`, `Raise(UiIntent)` (P1.3's Pause/Journal/Inventory),
  `Show(PromptRequest)`/`Hide()` (P1.3), `Show(DialogueViewModel)`, `Show(JournalViewModel)` (HUD objective = first
  active quest's first open objective), `Show(InventoryViewModel)`, `Show(NarrativeMessage)` (P1.4; a message stays on
  the message line for 4 s unless a committed `ui.message` is shown), `Confirm()`, `Cancel()` (UI navigation),
  `Navigate(dx, dy)`,
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
* `VoicePlayer.Play(VoiceLineRequest)` (P1.4): the bank entry holding the request's AudioClip, else its `ClipRef` as a
  bank id (a non-bank AudioClip is remembered under that key and played directly); a line with neither plays nothing
  (`Silent`). `Stop(reason)` commits `audio.stopVoice`.
* Feedback ids resolve in the bank directly or with the set's prefix (`footstep` -> `sfx.footstep`, `ui.click` ->
  `sfx.ui.click`).

## Decisions where 05 was silent

1. **Two kernel plugins through a world extension seam.** UI and audio state are kernel slots on one session target
   per world (in the world scope), so they are saved, restored and replayed like gameplay state. After the
   merge they use P1.3's `IGameplayWorldExtension`; the only P1.5 addition is `IGameplayWorldTargets` for the session
   targets (P1.3's plugins live on baked entities). Nothing changes for a build without extensions.
2. **`PresentationServices` is owned by the build plan**, not static. The plan survives a restore, so the
   registrations do too; runtimes re-register on attach.
3. **`ui.returnTo` slot.** 05 lists `ui.screen` and `ui.message`; sub-screens (settings, save, load) need to know where
   to return (pause or menu), so a third slot holds it.
4. **One event, `ScreenChanged`, carries the host action** (new game, continue, save/load/delete slot n, restart,
   quit). The host runs it between frames (`UiHostDriver.LateUpdate`), never inside the pump, then commits the outcome
   as `ui.message`. A successful load commits `ui.command{Loaded}`, which lands on the HUD from any screen.
5. **Pause does not pause the root.** UI commands must commit while paused, so `ui.screen != Hud` is exposed as
   `IGameplayPauseQuery.GameplayPaused`; `UiAudioBootstrap` gates P1.3's intent source on it.
6. **Volumes are slots** (saved with the game) and are mirrored to PlayerPrefs on every commit, so a new game starts
   with the player's volumes. Sensitivity, resolution and fullscreen are machine preferences (PlayerPrefs only).
7. **Ambience direction is a presentation-side director**: the audio runtime sees the focus traveller's RegionEntered,
   picks the zone with `AmbienceRules.ZoneForRegion` (a region without ambience keeps the previous one) and submits
   `audio.setAmbienceZone` through its `IGameplayInputSource` before the next step.
8. **No uGUI EventSystem.** UI Toolkit's own focus navigation handles buttons. `UiInput` adds Input System actions
   created in code: navigate (arrows, WASD, d-pad, left stick) for lists and dialogue choices, submit for dialogue,
   cancel (Backspace / gamepad east). Escape stays P1.3's Pause action. P1.3's `UiIntent` has only Pause, Journal and
   Inventory, so UI submit/cancel are `UiRuntime.Confirm()`/`Cancel()`, not intents.
9. **Headless**: `UiRoot` and the AudioSources are created only with a graphics device. The runtimes, their binders
   (which touch no engine object), the host-action driver and the fade driver exist in every mode.
10. **Media generation tier.** 05 says tier "Agent"; 03's `ToolTier` has no such member, so `audio.generateVoice` and
    `audio.generateSfx` are `Compose` tools with `Requires = "agent.media"`. Voice goes through P1.4's
    `IMediaGenerationGateway.RequestVoiceLine`; `MediaGateways.Resolve()` picks the first concrete gateway type other
    than `NotConfiguredMediaGateway` (P2.2 adds one; nothing is cached), and NotConfigured answers carry GP-AUD-020. A
    Requested line arrives later as a candidate for `audio.assignClip`. The contract has no sound-effect request, so
    `audio.generateSfx` always answers NotConfigured (GP-AUD-020).
11. **Hand-written mixer.** The AudioMixer YAML is written by a script with deterministic GUIDs. The authoring utility
    references it from the bank, and the validator checks every exposed parameter (GP-AUD-005).
12. **Interaction cues.** `SfxPool.OnFeedback` plays the cue id through the set's prefix; every `refused:<code>` cue
    plays the `refused` id (Hollowmere's bank has none yet, so it counts as missing). Positions are millimetres.
13. **Item input by key.** P1.4's inventory view gives item keys, so `IInventoryInput.Use/Drop` take the item key;
    the boot adapter maps it to the item's authoring id through the content manifest and submits consume/drop of one.

## Verification

No compile, build or test ran on the Mac. Mac-side checks pass: `tools/check_package_metadata.py`,
`tools/check_game_core_csharp.py`, `tools/validate_game_core_docs.py` and `tools/check_contract_surface_parity.py`.
Host runs went through `studio/tools/sync-to-host.sh p1.5`, `unity-compile.sh` and `dotnet-test.sh`, holding one
Unity instance at a time. Results come from the NUnit XML and TRX files.

| Run (host, commit) | Result |
|---|---|
| `dotnet-test.sh p1.5 dotnet/tests/GameCore.Rules.Gameplay.Tests` (c25e032) | PASS: 145/145. P1.5 adds `Ui/ScreenFlowRulesTests` (11 tests, 55 attribute lines including the TestCases) and `Audio/AudioRulesTests` (8 tests, 20 attribute lines). |
| EditMode run A: P1_1 `AuthoringBakeTests` + P1_5 `UiAudioContentTests` (6295b57) | 10 passed, 1 skipped (the preview). The catalog was re-baked for the ui/audio entries. The content was authored: 11 clips, 10 documents, 92 bindings, 0 diagnostics. The outputs were fetched back and committed in 82c70fe. |
| EditMode run B: `Hollowmere\.P1_5\.` and `Hollowmere\.P1_1\.` (4602cc0) | 28 passed, 0 failed, 1 skipped. All 16 P1.1 tests pass on the re-baked catalog. The skip is `PreviewScreenCapturesARenderTextureOrSkipsHeadless`, reason "GP-UI-008: no graphics device (-nographics); run the preview in a graphical Editor". `unity-compile.sh` labels an Ignored skip as FAIL, but no test failed. Logged lines: `P1.5-FLOW changes=10 accepted=10 refused=2 hostActions=1` and `P1.5-AUDIO accepted=7 refused=4`. After the run the host tree was clean, so re-authoring is byte-identical. |
| PlayMode: `UiFlowHeadless` + P1.1 `ThreeRegionLoop` (4602cc0) | PASS 2/2 (headless). The `[P1.5-UIFLOW]` lines are in the table below. |
| After the P1.3 merge: dotnet (8ff3ca6) | PASS 198/198. |
| After the P1.3 merge: EditMode A (re-bake + author) and `Hollowmere\.P1_[135]\.` (38b9ec1) | 11 passed + 1 skipped; then 45 passed, 0 failed, 1 skipped. Host tree clean after authoring. |
| After the P1.3 merge: PlayMode with the hook applied only on the host (uncommitted, reverted) | `BootWiring` passed; P1.3's `PlayerWalkAndInteract` failed only at its `NullPromptPresenter` cast (fixed after the P1.4 merge, see below). |
| After the P1.4 merge: dotnet (9a5d8f5) | PASS 259/259. |
| After the P1.4 merge: EditMode A (re-bake + author, f4414ec) | 11 passed, 1 skipped; catalog re-baked (ui/audio contributors next to P1.3's and P1.4's), committed f0f0f36; the P1.4 content manifest and the lock came back unchanged. |
| After the P1.4 merge: EditMode, every test of the project (f0f0f36) | 90 total: 89 passed, 0 failed, 1 skipped (the preview, GP-UI-008). Host tree clean. |
| After the P1.4 merge: PlayMode, every test of the project (0b59386) | PASS 5/5: P1.1 `ThreeRegionLoop`, P1.3 `PlayerWalkAndInteract`, P1.4 `DrownedBellHeadless`, P1.5 `UiFlowHeadless` and `BootWiring`, all on the real GameBoot. `P1.5-BOOT seams=9 extensions=player,npc,interaction,ui,audio screen=Hud firstLine="Traveller! The Drowned Bell has been silent since the flood took the old belfry." dialoguePushes=3`. |

`[P1.5-UIFLOW]` timings:

| Step | Time | Frames |
|---|---|---|
| boot to menu | 64 ms | 0 |
| menu to HUD | 5 ms | 1 |
| travel to marsh, ambience zone committed | 3 ms | 2 |
| save slot-1 | 66 ms | 2 |
| load slot-1 to HUD on the restored root | 56 ms | 2 |
| total | 205 ms | |

Totals for the run: screenChanges=10, hostActions=2, presents=75, audioPresents=75, zoneRequests=1. The pump count
was 30 over 30 frames, both before and after the restore, with no violation.

What the tests cover:

* **Binding maps.** Every binding of every one of the 10 documents names exactly one element in its UXML. Every
  property and source is valid, and every button has a command.
* **Asset integrity.** Every content asset is bound to its script.
* **Validator and tools.** They refuse an unknown element (GP-UI-002) and a bad source (GP-UI-003) and leave the asset
  unchanged.
* **Procedural audio.** The clips render deterministically and are byte-equal to the committed `.wav` files, with the
  SHA-256 recorded in the manifest. Loops have no click at the seam, and every clip imports as an AudioClip.
* **Audio content.** The mixer exposes every parameter. There are three ambiences, three music states, the explore
  start state and the bell stinger.
* **Media generation.** The generate tools answer NotConfigured (GP-AUD-020) and write nothing.
* **Screen flow.** It runs through the committed slots: menu, new game, HUD, pause, settings, pause, HUD, journal,
  inventory, HUD. The two refusals (NothingToClose, TransitionNotAllowed) are traced.
* **Saving without a service.** The save screen shows GP-UI-014 and commits it as `ui.message`.
* **Boot (`BootWiring`, PlayMode on Boot.unity).** The real GameBoot composes player, npc, interaction, ui and audio
  with the narrative modules; all nine seams are wired; the main menu gates movement and New Game opens the HUD; a
  real conversation with Maren reaches the dialogue panel through P1.4's LineShown -> DialoguePresenter ->
  IDialogueView, and the UI's advance goes back through `IDialogueInput`; a P1.3 footstep plays the footstep clip.
* **Dialogue.** The dialogue view receives P1.4's view model (EditMode, direct; the real path is in `BootWiring`). Navigation skips disabled choices, and Confirm chooses
  through `IDialogueInput`.
* **Audio kernel.** It handles music state with a stinger, refusals (unknown state, unchanged, unknown channel, out of
  range), volume into slot, mixer dB, the PlayerPrefs mirror and the settings model, sfx found and missing, voice
  play and stop, and feedback ids through the `sfx.` prefix.
* **Ambience.** It follows RegionEntered (village, marsh, belfry), and the HUD banner names the region.
* **PlayMode, `UiFlowHeadless`.** It goes menu, new game, HUD; pause and resume; pause, save, slot-1 listed, load,
  restore, HUD. On the restored root the UI and audio re-attach and the ambience zone is restored. It also covers the
  dialogue view model, ambience on RegionEntered, and one pump per frame.

## Files outside my exclusive paths (integrator)

* `Packages/com.gamecore.gameplay.world/Runtime/WorldBuilder.cs` (P1.3's version plus `IGameplayWorldTargets` recipes and
  seed steps and `Presentation`), `GameplayWorld.cs` (`Presentation`, `Extensions`), new `WorldExtensions.cs`.
* The re-baked Hollowmere catalog outputs (`World/Generated/HollowmereCatalog.g.cs`, `HollowmereCatalogCoverage.g.cs`,
  `World/Catalog/*`, `World/Hollowmere.manifest.asset`). Other packets that contribute catalog entries need one re-bake
  after the merge.
* `dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj` (two Compile globs next to P1.3's).
* `games/hollowmere/Packages/manifest.json` and `packages-lock.json` (three packages added; ui and audio depend on
  `com.gamecore.gameplay.compile` for the catalog contributors).
* `games/hollowmere/Assets/Hollowmere/Tests/P1_3/PlayMode/PlayerWalkAndInteract.cs`: one line. The test sets its own
  `NullPromptPresenter` after boot, because GameBoot now wires the UI as the prompt presenter.
* `Boot/GameBoot.cs`, `Boot/Boot.unity` and `Boot/Hollowmere.Boot.asmdef` (now this packet's).

## Open

* Real voice/sfx generation waits for the P2.2 gateway (etos op `tts`); the tools answer NotConfigured until then.
* Hollowmere has no checkpoint codecs; the PlayMode test uses a verbatim copy of the validation project's generated
  checkpoint catalog under `Tests/P1_5/Checkpoint`. Production saves need the game's codecs (P3.1), the `UseSaves` call
  in GameBoot, and a restore path that re-attaches the narrative modules and P1.3 sessions.
* HUD stamina is bound to `vm:hud.Stamina`, which nothing fills yet. With P1.3 on main the binding is
  `ui.bind stamina-bar value slot:player.owner/player.stamina@<player authoring id>` with `percent:<staminaMax>`.
* `ui.previewScreen` capture is skipped on the headless host (reason GP-UI-008); it needs a graphical Editor.
* If `Boot.unity` is ever regenerated by `HollowmereWorldAuthoring` (only when it is missing), GameBoot's `content` field
  must be set again (`ConfigureNarrative`), or GameBoot refuses with a clear message.
