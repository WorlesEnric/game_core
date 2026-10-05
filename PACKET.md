# P2.1 studio-ui

The creator-facing editor surface of GameCore Studio (docs/studio/02-architecture.md boundary A): the Studio
Viewport, the prompt bar, the task tray, the candidate strip and panel, the context panel, the history panel, the
viewport move gizmo, Studio settings and the first-run guide. Every edit goes through the P1.6 `ChangeSetEngine`;
agent work arrives through `IStudioAgentGateway` (implemented by P2.2).

Branch: `worktree-agent-a595188d74c6b5b39`. Host clone: `~/wkspace/gc-studio/p2.1`.

## What was built

| Path | Content |
|---|---|
| `Packages/com.gamecore.studio.core/Runtime/Authoring/Agent/` (additive) | `AgentGatewayContracts.cs`: `IStudioAgentGateway : IAgentGateway` (Status, Events, Voice, Submit, Cancel, ListRequests, FetchCandidate, FetchArtifact, RejectCandidate), `ProviderStatus`/`ProviderAvailability`/`ProviderNames`/`GatewayConnection`, `AgentRequest` (+`ToJson`), `AgentRequestMode`, `AgentRequestState` (+`AgentRequestStates.Wire/Parse/IsOpen`, P0.5 wire names), `RequestHandle`, `AgentRequestInfo`, `AgentCandidate`, `AgentAttachment`, `VoiceTranscript`, `IVoiceSession`, `AgentEvent`/`AgentEventKind`, `AgentGatewayException`. `NullStudioAgentGateway`: NotConfigured everywhere, never a candidate, transcript or artifact. |
| `Packages/com.gamecore.studio.ui` (asmdef `GameCore.Studio.UI.Editor`, Editor-only) | `Core/` context (`StudioUiContext`, `StudioUiSession` ScriptableSingleton, `MainThreadQueue`, `StudioStyles`, `StudioAgentGateways`, menu ids); `Selection/` (`SelectionModel` with the loop-safe Unity mirror, `StudioSelection` ScriptableSingleton persistence); `Viewport/` (`StudioViewportWindow`, `ViewportRenderer`, `ViewportPicker`, `PumpMonitor`, `PlayInputRouting`, `SelectTimings`, `OverlapPopup`, `ViewportMode`); `Prompt/` (`PromptBar`, `AgentRequestBuilder`); `Tasks/` (`TaskLedger`, `TaskRow`, `StudioTaskStore`, `MemoryTaskRowStore`, `TaskTrayView`); `Candidates/` (`CandidateCoordinator`, `CandidateEntry`, `CandidateRequirements`, `CandidateCompare`, `CandidatePanelView`, `CandidateStripView`, `EditorAudioPlayer`); `Context/` (`ContextPanelView`, `ContextTools`, `ValidatorDiagnostics`); `History/HistoryPanelView`; `Gizmo/` (`ViewportMoveGizmo`, `StudioSelectMoveTool` EditorTool); `Settings/` (`StudioUiSettings`, `StudioSettingsProvider`, `IStudioSettingsSection`); `Windows/` (Context/Tasks/Candidates/History windows, `StudioMenu`, `FirstRunWizardWindow`); `Resources/GameCoreStudio/StudioStyles.uss`, `FirstRun.uxml`. |
| `Packages/com.gamecore.studio.ui/Tests/Editor` | `UiTestBed`, `TestAgentGateway` (EditMode only; replays `Fixtures/*.json`), `ScriptedVoiceSession`; tests `SelectionAndPickingTests`, `PromptAndTaskTests`, `CandidateHistoryGizmoTests`, `ContextPanelTests`, `ViewportWindowTests`. |
| `games/hollowmere/Assets/Hollowmere/Tests/P2_1` | `EditMode/HollowmereStudioUiTests` (real Thornwick Village: pick, request, context tools, settings); `Editor/StudioUiEvidence` (interactive evidence step machine). |
| `games/hollowmere/Packages/manifest.json` + lock | `com.gamecore.studio.ui` added and testable. |
| `studio/tools/evidence-p2.1.sh` | Runs the evidence entry in an interactive Editor on the host display, shrinks the PNGs, writes `artifacts/studio/evidence/P2.1/README.md`. |

## Verification

See the section "Host runs" at the end (commands, counts, durations, evidence).

## API for P2.2 / P2.3 / P3.x

* **Gateway (P2.2).** Implement `GameCore.Studio.Authoring.Agent.IStudioAgentGateway` and register it as
  `StudioServices.Runtime.Services.AgentGateway` (the existing `IAgentGateway` slot). The UI resolves it on every tick
  (`StudioAgentGateways.Resolve`) and subscribes to `Events` when it changes, so registering on load is enough.
  * `Submit(AgentRequest)` -> `RequestHandle` (state Queued with request/task ids, or `RequestHandle.Refused` with the
    node's code; transport exceptions become Refused rows, never a fabricated success). `AgentRequest.ToJson()` is the
    04 s2 EditRequest body (changeSetId, intent, selection snapshot, contextSlice + truncated/bytes/omittedNodes,
    toolCatalogRevision, mode, attachments, parent, worker).
  * `Events`: `AgentEvent.RequestUpdated(info)` (task tray; state `candidate` makes the UI call `FetchCandidate`),
    `CandidateReady(requestId, changeSetId)`, `Voice(transcript)`, `StatusChanged(status)`. Emit from any thread; the
    UI marshals to the main thread.
  * `FetchCandidate(requestId)` -> `AgentCandidate(requestId, changeSet, toolCatalogRevision, artifacts, warnings,
    taskId)`; the UI then calls `FetchArtifact(sha256)` for every artifact the store lacks and verifies it with
    `ArtifactStore.Put` (a mismatch makes the candidate Invalid and imports nothing).
  * `RejectCandidate(changeSetId, reason)`, `Cancel(requestId)` (return the updated info when known),
    `ListRequests(after)` (re-fetch after a domain reload; rows are matched by changeSetId then requestId).
  * `Voice`: an `IVoiceSession` (Start/Stop, `Transcript` revisions with `IsFinal` on the done revision, `Level`,
    `Failed`). The prompt bar shows partial text, places only final text in the field and never submits by itself.
  * `Status`: `ProviderStatus` drives the provider chips and the prompt bar's disabled reason (NotConfigured /
    Disconnected "No node" / Refused with code / Connecting).
* **UI context.** `StudioUiSession.Context` (project) or `new StudioUiContext(runtime, gatewayProvider, selection,
  ledger, hookEditorUpdate)` (tests): `Selection`, `Tasks`, `Candidates`, `Requests`, `Submit`, `Handle(AgentEvent)`,
  `ReceiveCandidate`, `RecoverAsync`, `RequestPrompt(text)`.
* **Selection (P2.3 graph views, P3.x).** `SelectionModel.Set(refs, SelectionOp, regionRect?, parts?)`, `Clear`,
  `SetLocation`, `Capture(mode, frame?, worldSession?)` -> `SelectionSnapshot`, `Changed`, `Describe()` badges
  (residency, stale). Mirrors `UnityEditor.Selection` both ways without loops.
* **Settings sections.** Implement `GameCore.Studio.UI.IStudioSettingsSection` (Id, Title, Order, Build) in any Editor
  assembly; the settings page discovers it through TypeCache (P2.2 adds pairing/keys status there).
* **Panels as elements.** `CandidatePanelView`, `CandidateStripView`, `TaskTrayView`, `HistoryPanelView`,
  `ContextPanelView`, `PromptBar` are `VisualElement`s over a `StudioUiContext` and can be hosted elsewhere.

## Decisions

* The gateway interface is `IStudioAgentGateway : IAgentGateway` (P1.6 already owns the name `IAgentGateway`), so one
  registration slot serves both the engine's `asset.generate`/`mechanism.propose` calls and the UI.
* `FetchCandidate` returns an envelope (`AgentCandidate`) rather than a bare `ChangeSet`: the candidate needs the tool
  catalog revision it was planned against (StaleContext check) and its artifact list.
* `Refused` is the only state the UI creates locally (a submission the gateway did not accept); every other state comes
  from the companion's wire names.
* Open Studio tiles floating windows over the main window (Unity has no public split-dock API); windows the user
  already docked keep their place.
* Play-mode input routing switches the Input System's editor play-mode behaviour to "all device input goes to the
  game" while the viewport is focused in Play mode and restores it afterwards; there is no second input path.
* Step = Resume, then Pause after exactly one more sanctioned pump (the root has no step API).
* No static mutable state: caches are instance members; persistent UI state is in ScriptableSingletons
  (`StudioUiSession`, `StudioSelection`, `StudioTaskStore` in Library/GameCoreStudio, `StudioUiSettings` in
  ProjectSettings) and the first-run flag in EditorPrefs.

## Left open

* Microphone capture, the etos HTTP/WS client and artifact import are P2.2; graph views and tables are P2.3; the staging
  lane shows only the RequiresStageVerdict badge (P2.4).
