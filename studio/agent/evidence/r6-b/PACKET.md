# R6-B — honest workflow drivers, dialogue entry validation, describe identity

Branch `codex/r6-b`, base `d508045`. No files under `docs/` are edited: P4.3-final owns
those paths. This note is the integrator's replacement for updates to P0.3, P0.5,
P1.6, P2.1–P2.4 and P4.3-docs-draft. No paid calls, installed companion changes,
node changes, price application, key-file reads, or sibling-clone operations.

## R2 fixes

| P4.2d request / review finding | Fix | Regression |
|---|---|---|
| #3 / R2-38 W-VOICE-01 | Take owns Start and explicit Stop tasks; playback requires provider ready **and** actual microphone samples/capture. Failed Start stops without toggling/retrying. Playback completion triggers Stop, final field read follows drain. Existing separate Send and destructive request/journal checks remain. | `R6_Request3_DryReadinessThenExplicitStopDrainsFinalBeforeCompletion`, `R6_Request3_FailedStartNeverPlaysOrTogglesIntoAnotherStart`, `R6_Request3_ActualVoiceTakeDoesNotPlayOnElapsedTimerAndDrains`, `R6_Request3_RealSessionDryTransportWaitsForSamplesAndStopFinalNeverSubmits` |
| #5 / R2-38 W-AI-06 | Final consistency now throws on unequal/missing byte hashes after retaining `final.json`. | `R6_Request5_RetainedReopenMismatchFailsExactByteContract`, `R6_Request5_NoBakeOrAuthoredFieldIsDropped` (3 cases), `R6_Request5_MissingFilesCannotPassAsEqual`, `R6_Request5_RealReopenFinalStepFailsRatherThanOnlyRecordingFalse` |
| #6 / R2-38 W-AI-02 | Candidate model validator projects `dialogue.graph` changes and refuses disconnected entry with ValidationFailed / GP-DLG-005 and `data.unreachable`; missing projection fails closed. Worker must re-link or ask, never discard nodes. | `R6_Request6_*` (7 shared dotnet/EditMode cases), `R6_Request6_ActualEngineStageRejectsUnmodifiedFerrymanBeforeAnyWrite`, `R6_Request6_ActualEngineAcceptsExplicitRelinkWithoutWriting`, worker retained-witness test |
| #4 / voice setup | Preserve failure; add phase, frame count and code diagnostics without audio/transcript/credentials. No paid retry or speculative VAD change. | `R6_Request4_RefusedAcknowledgmentIsBeforeCaptureAndNeverRetried` |
| #7 / describe tariff | Document exact Echo alias/endpoint; retain operator placeholder and refusal. No DashScope price can bind this different provider. | `test_R6_Request7_describe_alias_is_echo_and_cannot_inherit_dashscope_tariff`, existing operator-fill/refusal test |

## Reproduced witnesses and contracts

The ferryman fixture is an exact byte copy of
`artifacts/studio/verification/W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/candidate.json`.
`request6-odd-before.json` projects entry/nodes/edges from the unchanged shipped Odd
asset. The original candidate adds terminal node 8 and sets entry=8; the new rule
reports precisely `[0,1,2,3,4,5,6,7]`. The generic validator is necessary because the
built-in `set` tool does not invoke the dialogue operation's static validator.
Candidate mode only: journal undo/recovery is not newly constrained. Known graph
operations are projected in envelope order; generic set supports complete and nested
node/edge fields. Validation evaluates the proposed graph, allowing explicit relinking
in the same change set. Missing/unknown graph projection returns StaleContext.
No catalog/schema/diagnostic registry shape is changed. GP-DLG-002/005 are existing
plugin codes, but `StudioDiagnostics.Normalize` drops structured data for unregistered
codes. Therefore the seam emits registered `ValidationFailed`, a message prefixed
`[GP-DLG-005]` (or `[GP-DLG-002]` for bad entry), and `data.unreachable`. The real
engine regression verifies that the witness survives normalization. This matches the
existing visible plugin-code wrapper while fixing its otherwise lost data. Index projection remains advisory under 03 §3; actual engine Stage
coverage uses the real full Hollowmere index and unchanged candidate.

**Byte-consistency contract:** exact SHA-256 of each entire asset file named in the
saved session's `paths` (or the existing four narrative defaults), including Unity YAML,
`contentStamp`, `contentHash`, and any embedded bake fields. **Normalized fields: none.**
A missing file/baseline is a failure, even if both maps say `missing`. 03 §1 and §7 use
content stamps as preconditions; §6 requires journal inverses, and neither provision
authorizes removing bake fields from a byte-equality claim. We keep both hashes and
fail the retained reopen witness rather than reclassifying equal authored fields as
byte equality. `reopen-diff-analysis.json` says Odd/DrownedBell differ only in stamps;
the wider fixture diff also includes baked content and graphics settings. Those bytes
are retained, not dropped. An owner-approved future semantic comparison must be a
separate named result, not a replacement for this byte check.

## Voice acknowledgment diagnosis (#4)

Correlated evidence: `artifacts/studio/workflows/P4.2d/voice-correlation.json`,
`companion-voice.log`, `voice-ledger.json`, and their referenced client workflow logs.

- Self-test sessions `vs_1a110f724ef84903ef151c1` and `vs_1a110f7373ef226390757b5`
  carry 43/44 frames (202752/206848 bytes), nonzero RMS and one final each. The second
  final arrives after Stop's input commit; waiting for final before Stop is incorrect.
- At **11:30:07.012652Z**, companion log line 193 reports `voice session refused`,
  provider `studio-voice`, code `bad_response`. The client receives the specific
  `upstream session.updated was not acknowledged; reconnect required` error.
  This is the **node realtime adapter ↔ upstream provider setup acknowledgment**
  boundary, propagated by companion `client.realtime().open` (voice.rs:224–257).
  The companion sends Unity ready only after open succeeds (voice.rs:282).
  The vendored SDK sends config and awaits a matching ready/generation before exposing
  its sender (vendor/etos-sdk/src/realtime.rs:296–328). Unity already awaits that ready
  before starting its source (EtosVoiceSession.StartAsync). Thus no client PCM/Stop
  ordering change can repair this particular pre-ready acknowledgment refusal.
- Move playback ended **11:30:10Z**, before capture at **11:31:18.739230Z**. The old
  release Toggle opened that late session after failed Start. Destructive playback
  ended **11:31:33Z**, before the next capture at **11:32:41.682157Z**. Both late
  sessions have zero RMS, with all 70/144 frames (331776/691200 bytes) accounted for
  at client, companion receive and forward. The demonstrated speech-loss hop is
  fixture playback → microphone capture. The driver ordering fix addresses that hop.
- The retained receipts contain no raw upstream session.updated event, acknowledgment
  correlation identifier, provider close/error frame or adapter acknowledgment state.
  They cannot distinguish missing/late acknowledgment from adapter matching logic.
  No provider defect or VAD threshold cause is asserted. New diagnostics distinguish
  `awaiting_companion_ready` from `awaiting_source_samples`, with frames/code only.

## Describe price provenance (#7)

`models.toml.tmpl` declares alias `describe` = `echo/gpt-5.6-sol`, endpoint
`https://api.echo-coding.com/v1`; `ops.toml.tmpl` names the same model. It is not
DashScope. The provider homepage [Echo](https://api.echo-coding.com) could not be
retrieved by the browsing tool on 2026-10-06; no reseller list price was verified.
Per the packet's conditional instruction the entry stays `operator`, unit `call`,
`per_unit=0.0 # SET_BY_OPERATOR`, with its explicit basis placeholder. Publisher
per-token rates and DashScope tariffs are not total Echo per-call estimates. The
installer test proves it refuses this placeholder and accepts an explicitly filled
operator estimate offline. Nothing was applied live.

## Verification

Commands run on myubuntu, using the host-wide Unity allocator, one owned Editor at a time:

```sh
# Required packet suites (additional client/core regression runs are retained separately).
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/studio/agent/evidence/r6-b/unity" --label r6-b-verified --results "$PWD/studio/agent/evidence/r6-b/unity/verified.xml" -- -runTests -testPlatform EditMode -testFilter 'Hollowmere\.P3_2\.Headless\.DriverDryTests.*|Hollowmere\.P1_4.*|Hollowmere\.R6_B.*|GameCore\.Studio\.Hollowmere\.P2_2\.Tests\.EtosGatewayTests.*|Hollowmere\.R5_B\.VoiceTests.*'
dotnet test dotnet/tests/GameCore.Studio.Model.Tests
dotnet test dotnet/tests/GameCore.Rules.Gameplay.Tests
dotnet test dotnet/tests/GameCore.Studio.Etos.Client.Tests
dotnet test studio/agent/evidence/r6-b/dotnet/R6.B.Tests.csproj
/tmp/r6-b-worker-tests/bin/python -m pytest studio/etos/agent/workers/tests -q
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
```

`--results` supplies Unity's `-testResults` internally, as in `unity-compile.sh`;
passing both is rejected as a reserved argument (initial setup rejection, no Editor).
The Python environment is isolated under `/tmp`, with pytest/jsonschema installed.
Rust sources are unchanged; no Rust build or installed-binary operation is needed.
XML/TRX case counts and hashes are reproducible with `python3 .../r6-b/summarize.py`
and retained in `results.json`. Final restored-source `verified.xml`: **59 passed / 0 failed / 0 skipped**:
P3_2 dry **16**, P1_4 EditMode **7**, R6_B **20**, existing ETOS gateway **15**,
existing R5_B voice **1**. Dotnet: model **113**, gameplay rules (including dialogue)
**309**, R6_B dialogue **7**, client **69 passed / 6 live tests skipped**; aggregate
**498 passed / 0 failed / 6 skipped**. Worker tests **5 passed**. Metadata passes
(**42 packages / 91 assemblies**); C# policy passes (**1225 files**); diff whitespace
and credential-shape audits pass. No paid/live test is relabeled as a dry pass. The last source change after those
tests only corrects the VoiceTake XML comment (gateway start, not a pointer click).

Negative controls use source from `d508045`, never candidate repair:
- Original model sources compiled in an isolated ignored `before/model` directory:
  **5 failed / 2 passed** of the same seven regressions. The unchanged ferryman witness
  receives no reachability refusal. Final model: **7 passed**.
- Original gc-designer instructions: **1 failed / 4 passed** worker tests; current:
  **5 passed**. Describe's already-correct operator behavior is not counted as a new fix.
- Original Reopen/VoiceTake bodies and original EtosVoiceSession were temporarily
  restored only in this clone for the focused baseline run, then restored byte-for-byte.
  The newly added RequireByteConsistency helper signature/body stayed present solely
  so the test assembly could compile; the original final-consistency step never calls
  it. Original Reopen fails `R6_Request5_RealReopenFinalStep...` because it returns
  without throwing; original client fails `R6_Request4_RefusedAcknowledgment...`
  because setup phase/frame/code diagnostics are absent.
- Initial baseline voice test had an unshown EditorWindow.Close cleanup exception
  masking its assertion; this is retained as a harness failure, not proof of the fix.
  The test now destroys that unshown window directly. `voice-before.xml` shows the
  original VoiceTake failing the exact readiness assertion: after the elapsed timer,
  its playback marker exists while capture is not ready. The same test passes in
  restored-source `verified.xml`.

Initial `dry.xml` (38 passed / 1 failed) and `stage.xml` (0 passed / 1 failed)
exposed the normalizer dropping plugin diagnostic data. After emitting the registered
wrapper, `final.xml` has **145 passed / 1 failed**, with all packet cases passing.
The sole broader failure is the unchanged CORE-PICK performance test's explicit
idle-host precondition: it counted two Editors (another packet was running). No
threshold was changed, no sibling process was stopped, and this is not reported as a
performance pass. Required packet suites pass on restored final source (`verified.xml`). The single
CORE-PICK case is additionally rerun through its existing `solo-unity.py` adapter;
`core-pick.xml` is **1 passed / 0 failed**, 21 Editor frames, median **1.9138 ms**
against the unchanged 50 ms gate. Initial contention refusal remains retained.
Across `final.xml`, `verified.xml`, and this focused rerun, all **147 unique Unity
tests** have a latest passing result (88 core + 59 packet/client).

## Requests to other packets

- P4.3-final: relocate this note into the relevant documentation packet sections; retain
  the strict byte contract and the distinction between dry regression and paid acceptance.
- UI owner, `Packages/com.gamecore.studio.ui/Editor/Prompt/PromptBar.cs`: provide public
  `Task StartVoiceAsync()` and `Task StopVoiceAsync()` plus capture readiness (after
  provider ready and first samples), and keep the active button state through drain.
  Current `ToggleVoice(): void` / private `StopVoice(): void` expose no awaitable
  lifecycle. R6-B's workflow owns the same gateway session and uses the public
  `PromptBar.OnTranscript(TranscriptUpdate)` handler, preserving explicit Send, without
  changing out-of-scope UI files. This proves session ordering, not pointer-event UX.
- Core diagnostic owner, `Packages/com.gamecore.studio.core/Editor/Core/StudioDiagnostics.cs`,
  `Normalize(Diagnostic)`: preserve/redact `diagnostic.Data` when wrapping an unregistered
  plugin code, and consider explicit registered plugin-code support. R6-B uses the
  registered ValidationFailed wrapper now so candidate-time unreachable witnesses survive.
- Companion owner, `studio/agent/src/voice.rs`,
  `VoiceBridge::run(self: Arc<Self>, socket: WebSocket, app: String)`: include the
  generated session ID and negotiation phase in the refused-open log; today line 193
  of the witness has provider/code but no session ID. Do not log config or credentials.
- ETOS provider owner (outside this clone), Bailian realtime session.update
  acknowledgment handling: add redacted setup correlation/expected-vs-received event
  diagnostics and an offline delayed/missing/mismatched session.updated test. The
  current receipts do not identify which acknowledgment condition failed. Preserve
  fail-closed setup and do not automatically retry paid input. Exact upstream file/
  signature is unknowable from this checkout: it vendors only the SDK, not node
  provider adapter sources. This is an observability request, not an asserted fix.
- History/bake owner, `Packages/com.gamecore.studio.core/Editor/Journal/HistoryService.cs`,
  `HistoryResult Undo(string? changeSetId = null, bool force = false)`, and
  `Packages/com.gamecore.gameplay.logic/Editor/NarrativeBake.cs`,
  `Write(NarrativeBakePlan plan, ICollection<string> changedFiles)`:
  investigate exact stamp preimages for Odd/DrownedBell from
  `fixture-after-reopen.diff` and the final hash maps. No authority exists in 03 to
  normalize those fields away; R6-B now reports failure until actual byte restoration
  or an explicitly revised contract is provided.

## Left open

- Provider acknowledgment root cause: no upstream acknowledgment frames/state in the
  retained evidence; no paid retry authorized by this packet.
- Full paid W-VOICE-01, W-AI-02 Play/nav and W-AI-06 byte restoration acceptance are not
  claimed by offline validation/dry transport tests. Reopen's retained mismatch remains
  a real failure. No gameplay/history/bake assets outside exclusive paths are changed.
- Describe live tariff/call: model is Echo and no operator amount/basis was supplied.
  The required operator placeholder remains fail-closed; packet explicitly forbids live apply.

