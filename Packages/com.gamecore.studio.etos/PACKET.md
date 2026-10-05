# R3-B: Studio ETOS client and UI

Branch `codex/r3-b`, base `ed760892`, Linux build host `myubuntu`.
Only the packet's exclusive source/test paths and R2-C/R2-D notes are edited.
This package-local PACKET.md satisfies the packet record without editing the shared root.

## Retained reproductions

- D19 / D22: `artifacts/studio/workflows/P3.2/runs/voice2-20261005T112002Z/voice/move-transcript.json`
  records an empty final, one `listening...` display revision, and capture released after 75,524 ms.
  Its `move.wav` supplies the regression's exact PCM input after the existing WAV conversion.
  The fake produces the expected sentence and deliberately delays the final after stop.
- D13: retained `text2-20261005T061054Z/timeline.jsonl` candidate/done for
  `cs_01M45B93ZY07E94WSV7YZGHJPD` has `updatedAt=1791181173997`,
  `recvMs=1791181184086`: 10,089 ms. The fake replays the request-state shape with a current
  timestamp and a progress burst; it does not pretend to reproduce the live host's scheduling.
- D16: P3.2 findings table documents the missing selector; hello fixtures advertise
  `gc-designer` / `gc-mechanic`, and the integration regression adds a third advertised worker.
- D12: P3.2 `relayout` steps document window-manager placement overriding OpenStudio.
  The regression simulates that late placement, then checks one deferred D6 relayout.

## Regression mapping

- D19 / W-VOICE-01: `D19_D22_TwoTakesDrainDelayedFinalsAndDisplayPartialsWithoutSubmitting`.
- D22: the same Editor test plus `D22_StopDrainsDelayedFinalAfterAllAudioFrames` and
  `D22_StopTimeoutPreservesPreciseReasonInsteadOfClientClosed`.
- D13: `D13_EventBurstReachesTrayWithinOneSecond`,
  `D13_ReadAheadIsBoundedAndNeverAcknowledgesQueuedWork`,
  `D13_BufferedFailureReplaysWithoutAcknowledgingLaterEvents`, `D13_DisconnectedStreamPollsAsFallback`.
- D16: `D16_PromptListsAdvertisedWorkersAndPreservesSelection`,
  `D16_HelloWorkersReachPromptAndExactWorkerReachesCompanion`.
- D12: `D12_DeferredPlacementRunsOnceWithoutOpeningGraphics` (executed);
  `D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce` (graphical, skipped headless).

## Verification

Implementation: `828cd44a` (source bytes unchanged between final tests and that checkpoint).
Committed [summary and hashes](Tests/Evidence/R3_B/summary.json),
[Unity final XML](Tests/Evidence/R3_B/unity-final.xml), and
[dotnet final TRX](Tests/Evidence/R3_B/dotnet-final.trx).

| Run | Result |
|---|---|
| Dotnet baseline R3 | 2 failed, 1 passed: D13 queued count 1 rather than 16; D22 closed event says `socket closed` rather than `stop timed out`. Delayed-final drain already passed. |
| Unity baseline | 0 passed, 4 failed: D19 second take not capturing; D16 missing selector; D13 p95 1,645 ms; initial D12 graphical test stopped on no-device logs before its placement assertion. |
| Unity intermediate | 78 passed, 3 failed, 7 skipped: the new headless D12 test deliberately ran with the original `StudioMenu.cs` and failed for missing deferred placement; two D16 tests exposed detached UI Toolkit controls not emitting change callbacks. The shared `PromptBar.SelectedWorker` controller now drives both the dropdown callback and headless tests. |
| Unity final | **81 passed, 0 failed, 7 skipped, 0 inconclusive (88 total)**; 36.466 s tests, 209 s wrapper, Editor exit 0. The wrapper returns 1 / PARTIAL because skips are not acceptance passes. |
| Dotnet final | **67 passed, 0 failed, 6 skipped (73 total)**; all live tests explicitly gated off with `GAMECORE_ETOS_LIVE=0`. |
| Static / host | Metadata: 42 packages / 91 package assemblies, pass. C#: 1,144 files, pass. UI host evidence-contract test: 1 passed (seven assertions). `git diff --check`: pass. |

The seven Unity skips are four paid/live-node cases and three graphics cases: the two existing
R2 input/render tests and the new physical D12 window test. Exact names/reasons are in the summary.
All six runnable R3 Editor tests passed. No skipped test is reported as exercised.

D13 measured 20 transitions through the actual tray state chip with a progress burst and 50 ms
main-thread pump cadence: baseline p95 **1,645 ms**; first fixed measurement p95 **106 ms**, max
**117 ms**; final p95 **110 ms**, max **117 ms**, against the unchanged ≤1,000 ms budget.
These are the two fixed-code measurements; no loaded-host live latency qualification is claimed.

The baseline first Editor attempt timed out in package resolution. The wrapper retried once and
completed compilation/import and the regressions. Diagnostic stacks from this clone's Editor are
retained in `.unity-logs/r3-b-before-backtrace.log` and `r3-b-before-postcompile-backtrace.log`
(Mono waiting, then asset import respectively; not a proven common root cause). No other process
or sibling clone was changed. The intermediate run's ILPP startup briefly reported a missing socket,
then recovered and executed tests. The final run completed on its first attempt.

The prepared protocol sources first passed in a temporary host build. Copying them with preserved
mtimes caused the first checkout dotnet invocation to reuse old binaries; that failed result was
not accepted. `dotnet clean` followed by `dotnet test` rebuilt the actual sources. The subsequent
final run includes the additional buffered-failure/cursor regression and is retained above.
Six synthetic credential-shaped values in parameterized dotnet test names are masked in the retained
TRX; counts and finding test names are unchanged. Original TRX remains in the host TestResults folder.

Exact final commands (repository root; `--results` supplies wrapper-owned `-testResults`):

```bash
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" \
  --label r3-b-verified --results "$PWD/.unity-logs/r3-b-verified.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.(Etos|UI).*|Hollowmere\.P2_[12].*|Hollowmere\.R2_[CD].*|Hollowmere\.R3_B.*|GameCore\.Studio\.Hollowmere\.P2_2.*'
PATH="$HOME/.dotnet:$PATH" GAMECORE_ETOS_LIVE=0 dotnet test \
  dotnet/tests/GameCore.Studio.Etos.Client.Tests --logger 'trx;LogFileName=r3-b-after.trx'
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
python3 Packages/com.gamecore.studio.ui/Tests/Host/test_evidence_contract.py
```

The requested filter is preserved and extended with the legacy `GameCore.Studio.Hollowmere.P2_2`
namespace so its existing gateway/voice tests are not accidentally omitted. Unity held one shared
host slot at a time. Generated parent metadata `Tests/R2_D.meta` and `Tests/R3_B.meta` were removed after Editor exit to restore the literal exclusive directory scope.

## Requests to other packets

- Integration / game test-root owner, `games/hollowmere/Assets/Hollowmere/Tests/R3_B.meta`:
  retain a normal Unity folder-asset metadata file when integrating the new R3_B test directory.
  This sibling metadata path is outside the brief's literal `R3_B/**` boundary; Unity generated it
  for the runs, and this packet removes it after exit. No asset refers to the folder GUID.

- Companion / realtime owner, `studio/agent/src/voice.rs`,
  `VoiceBridge::run` / `Ok(ClientMsg::Stop)`: investigate whether
  `sender.command("close-1", Action::Close)` closes the upstream recognizer before its final
  user transcript. Preserve the existing wire contract (client audio then stop; server partial
  revisions, `final:true` only when the upstream user item has `done:true`, then closed).
  Qualify with upstream input-audio seq/id, speech boundaries, transcript revision/done, control
  acknowledgment and close ordering. If close does not flush recognition, add a bounded upstream
  finish/drain handshake before Close. Do not manufacture a final from a partial.
  The client keeps receiving after stop, waits up to 10 s, and never sends a prompt implicitly.

## Left open

- D22's live empty-transcript cause is **not isolated**. Neither retained voice run contains the
  actual `/v1/voice` or upstream realtime frame log; `timeline.jsonl` records request views,
  and `move-transcript.json` records UI observations only. Therefore no exact historical
  audio/partial/done/close sequence can honestly be retained or reconstructed. The source's immediate
  upstream Close is a hypothesis, not proof of a node defect. No paid/live-node rerun is authorized.
- Fake-companion latency and simulated late placement do not qualify loaded-host live latency or
  the physical `:1` window manager. No paid ops, installed companion changes, or credential-file reads.

## R4 — R4-A implementation record

Branch `codex/r4-a`, base `c79a3832`, Linux build host. The retained P4.2
W-MECH-01, W-ETOS-07 and W-VOICE-01 reports are the reproductions, including
404 `no owned resource`, 404 `no request`, and the 44-frame/0.199-peak final
“To lead every N P C in the village.” No provider/service configuration changes.

### R4 fixes

- P42-STAGE-01: exact UTF-8 app payload signed with domain-separated HMAC-SHA256;
  transient stage-key header only on app intake. Candidate, catalog and verified
  CAS bytes travel together. Authenticated ledger ownership selects the worker
  route; missing linked worker requests never downgrade to app origin. Open UI,
  preview and journal candidates are read through a runtime-scoped resolver.
  Pending stage jobs are polled before fetching the companion-issued verdict;
  signed record fields, including origin, remain unchanged.
- P42-MEDIA-01: local voice request IDs remain display/file identifiers only.
  Direct media omits changeSetId; an explicitly supplied owned ID retains the
  existing server checks and all configured cost ceilings.
- P42-STARTUP-01: the automatic watcher binds when Open Studio creates the runtime
  and when reload replaces it; explicit Stop disables that watcher. Pairing resolves the host override/documented key
  location independently of a stale project key-file preference. Missing paths
  produce `not_configured` with the path; a newly available pairing is retried.
- P42-VOICE-01: provider readiness precedes source start; source sample readiness
  precedes IsCapturing/StartAsync completion. The first read is queued intact and
  release drains the final partial frame. Section 04 §5 specifies 24-kHz mono
  PCM16, without a gain/normalisation requirement; no speculative gain is applied.

### Requests to other packets

- UI/core owners: preserve the optional read-only resolver seam
  `StudioUiSession.ContextIfCreated.Runtime`, `.Candidates.Find(string id)` and
  `CandidateEntry.ChangeSet`. It is accessed by fixed public names to keep ETOS
  usable without a UI package dependency; only an identical runtime is accepted.
  A future shared `Func<string, ChangeSet?>` registration should replace this
  optional discovery if these public members change. The service constructor
  already accepts that typed resolver plus catalog/CAS delegates.

- **R4 UI owner**, `Packages/com.gamecore.studio.ui/Editor/Prompt/PromptBar.cs`,
  `private void StartVoice()`: the current implementation writes `listening...`
  before observing `IVoiceSession.StartAsync()`. Show a preparing/connecting state
  until that task completes, then update the active take on the Editor thread.
  Do not update a stopped/replaced take or overwrite an arrived transcript. The
  existing `Task StartAsync()` seam now completes only after source sample readiness;
  no interface change or artificial transcript event is needed. The retained voice
  harness already waits on `EtosVoiceSession.IsCapturing`; ordinary prompt UX needs
  this corresponding external-path change to avoid inviting speech too early.

### Left open

- Real recognition of “Delete” remains an external qualification: this packet
  forbids paid operations and real key-file reads. The opt-in virtual microphone
  test requires an already authenticated session, explicit realtime authorization,
  named existing PipeWire source/sink, and pw-play. It honestly skips otherwise.
  A fake transcript is never counted as speech recognition evidence.
- The retained startup report has no key-resolution or initialization-order trace,
  so its exact historical precedence/timing cause cannot be established without
  an installed-session reproduction. The host resolver and runtime-creation watcher
  are verified with fixture credentials; installed pairing remains unqualified.
- No installed companion restart, sandbox stage, live Admit, or real domain reload
  with a paid in-flight task is performed. Offline route/lifecycle tests do not
  claim those acceptance rows.

### Verification

Final Linux host results, implementation `1dd5b491`:

- .NET client: **69 passed, 0 failed, 6 NotExecuted**, 75 total, counted from TRX
  result records (not stdout). The six require real ETOS opt-in.
- Requested Hollowmere EditMode filter: **39 passed, 0 failed, 5 skipped**, 44 total,
  read from XML. All seven new offline R4-A cases pass. Four existing P2.2 live
  cases and the virtual-microphone case skip honestly. Editor exit 0; wrapper
  correctly returns PARTIAL/NotRun for skips. Test time 10.898 s, Editor run 45 s.
- `check_package_metadata.py`: pass, 42 packages / 91 package assemblies.
  `check_game_core_csharp.py`: pass, 1,200 files. `git diff --check`: pass.
- Before fix: the .NET app-intake test fails on the missing API; the three Editor
  tests fail on the transmitted local ID, missing automatic resolver, and capture
  becoming ready before the first source samples. The first Editor attempt timed
  out after 1,501 seconds with no XML. The unchanged warm attempt produced all
  three expected failures in XML (420-second run), with a recovered ILPP startup
  error also retained. None of these attempts is erased or counted as a pass.
- An auxiliary C# 9 compile against this clone's Unity references caught the queue
  overload error before the final Editor run. Its initial broad reference set had
  bridge/private-library conflicts; the corrected source and test compile had zero
  errors (one reference-version warning). This compile is not EditMode evidence.
- The final Editor waited for the shared allocator behind another packet's standalone
  verification. No foreign process, reservation, installed service or sibling clone
  was changed. Only one Editor reservation was held by this packet at a time.

Receipts and hashes: [Tests/Evidence/R4_A/summary.json](Tests/Evidence/R4_A/summary.json).
Exact final commands: [Tests/Evidence/R4_A/commands.txt](Tests/Evidence/R4_A/commands.txt).
Before/after XML/TRX and compressed startup/final logs are retained beside them.
Text receipts replace the home directory with `~`; raw XML hashes are also recorded.
The requested EditMode regex was used verbatim. No package dependency, validator,
confinement mode, budget, or existing test assertion was weakened.
