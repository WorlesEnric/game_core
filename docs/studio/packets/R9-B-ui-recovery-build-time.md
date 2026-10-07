# R9-B — creator UI recovery and build timestamp

This is the packet's PACKET.md. Branch `omp/r9-b`.

## R2 fixes

| Finding | Fix | Regression |
|---|---|---|
| P4.2i creator UI recovery request / R2-13 | Recover a late-arriving session job binding even when the candidate entry already exists, including the Find-without-Add refresh path. Bind authorization to the candidate/package/proposal/inputs/project/source/catalog actually staged; recheck after asynchronous fetch. Recovery fetches/verifies the original job instead of submitting another stage. | `R9_B_AlreadyRecoveredCandidateRefreshesLateJobBindingWithoutRestaging`, `R9_B_RefreshWithoutAddRecoversLateBindingButRefusesChangedSource`, `R9_B_ChangedCandidateCannotUsePreviouslyVerifiedJob` in `R2UiRegressionTests`. |
| P4.2i build timestamp request | Unity 6000.0 creates `buildStartedAt` from UTC ticks with `DateTimeKind.Unspecified`. Mark those ticks UTC instead of applying the local-zone offset again. Preserve `startedRaw` (round-trip format) and `startedRawKind`. | `Hollowmere.R9_B.EditMode.Tests.BuildTimestampTests.R9B_BuildTimestamp_NonUtcHostPreservesUtcInstantAndRawUnityValue`. |

## Retained witnesses and reproduction

The requested literal `W-MECH-01/p4.2i/` directory does not exist; retained runs are named `p42i-*`. The generic lever failure is `W-DOC-02/p42i-lever-workflow/live-failure.txt` and its `p42i-lever-review-20261007T061812.947804Z` Editor log. `LeverWalkthrough.Poll` writes the session binding, then uses `Find(id) ?? Add(...)`; an existing recovered entry bypasses restoration. `RefreshStage` originally used only the entry's fields. The original log does not print expected/current request fields, so the original entry's null binding is an inference, not a retained observation. The regression explicitly observes an existing entry with no job before installing the exact binding, then reproduces the same refusal.

The timestamp witness is `W-GAME-01/p42i-player/player/build-report.json` (`2026-10-06T22:34:27Z`) against the wrapper log's UTC launch (`2026-10-07T06:33:40.466884Z`). The regression supplies the corresponding UTC build ticks under Asia/Shanghai and exercises the production report writer without building a player.

Baseline evidence: `artifacts/studio/verification/W-MECH-01/r9-b/before/results.xml`: **0 passed / 4 failed / 0 skipped**. Both late-binding paths fail with `stage_context_changed`; changed package still enables Admit; the report gives `22:34:27Z` instead of `06:34:27Z`. The earlier compile attempt records an unsupported NUnit attribute in the new fixture; corrected, not counted as defect reproduction.

## Verification

Product checkpoint: **`e201d52c6648c7bf687619a86a57f051eea39876`**. Evidence is under `W-MECH-01/r9-b/` and `W-GAME-05/r9-b/`. No player build or installed companion/node operation was used. The scratch runner reuses only R6-E's synthetic authentication/proxy fixture; the companion, stage execution, signature issuance and verification are real. Its signing state is private and external to the checkout.

- Fixed batch EditMode XML: **71 passed / 0 failed / 5 graphical-only skips**, including all four previously failing regressions. `after/results.xml` retains every disposition; this is not described as an all-green graphical suite.
- Final graphical EditMode XML: **76 passed / 0 failed / 0 skipped** (`graphical-tests/results.xml`), including the full UI suite, Hollowmere P2_1 and timestamp regression. This exercises all five cases skipped by the headless run.
- Actual attached candidate-panel Stage control submitted app-origin job **`stg_1a115dc009c39fc5de569ef`**. Signed Docker verdict: **175019 ms**, all seven mandatory steps pass, **36 EditMode / 2 PlayMode** XML cases pass. Authenticated verification succeeds; unauthenticated verdict fetch is **401**.
- Reopened graphical Editor: `signed-stage/binding-before-recovery.json` directly observes `entryJob=null`, `entryRequest=null`, and exact saved/current binding equality. The same entry recovers the original job. `panel-result.json` reports **CanAdmit=true**, **attached Admit button enabled**, zero diagnostics, `batchMode=false`; no new stage is submitted.
- The scratch observer initially compared the prepared pre-session catalog (`00ebe8…`) with the panel's initialized catalog (`8d5bcc…`). The actual signed stage passed; the observer stopped after it. Its correction uses the exact submitted binding and reconnects the same private installation/job without rerunning stage. The original failure and correction are retained in `observer-correction.json`; signature authenticity and freshness were not relaxed.
- First panel capture was inverted and clipped; it is retained as rejected visual evidence, separately from the passing behavior receipt. The corrected **1100×900** `signed-stage/panel-admit-enabled.png` was visually inspected: upright complete candidate panel, verified Docker pass, seven passing steps and enabled Admit. Actual attached Refresh verdict and Stage controls were activated; Admit itself was not activated.
- Package metadata: **42 packages / 92 assemblies**, four engine pins, six game pins, three lock sources. C# policy: **1274 files** pass. Companion fmt and all-target clippy with warnings denied pass. Default parallel companion tests hit the existing timing-sensitive `a_slow_task_open_is_reread_not_failed` (requested/opening rather than running); unchanged tests run serially: **153 passed / 12 ignored**. Both transcripts are retained; no companion product source changed.

## Requests to other packets

Shared notes `P2.1-studio-ui.md` and the verification matrix are outside R9-B's exclusive documentation paths. Their owners should link this finding → fix → test table. No shared protocol or source change is required. No matrix status promotion is claimed.

## Left open

Historical P4.2i row counts remain **57 PASS / 6 BLOCKED / 5 FAIL**; no matrix statuses or row wording changed. The historical failed lever log lacks the original entry binding, so its exact guard branch remains [INFERENCE]; the new late-binding regression and real signed-stage witness establish the defect and fixed behavior directly. This packet proves Admit-enabled recovery, not a new live admission/player-build qualification.

## Reproduction commands

Build `studio/agent` and `games/hollowmere/Assets/Hollowmere/Tests/R9_B/Scratch/Cargo.toml` with `CARGO_TARGET_DIR=/tmp/r9-b-cargo-target`. Resolve and verify the exact cache using `gamecore-studio stage cache-path` and `studio/stage/provision-cache.sh <cache> --verify`.

Every creator Editor command uses `studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir <evidence> --label <label> -- -force-glcore ...`, with `UNITY=$PWD/games/hollowmere/Assets/Hollowmere/Tests/R6_E/graphical-unity.sh`, `DISPLAY=:1` and `GAMECORE_R6_E_PAIRING_PATH` pointing only to the newly created scratch pairing path. For preparation use an explicitly nonexistent `/tmp/r9-b-unpaired/not-paired.json` path, preventing installed credential lookup.

1. Creator context: `-executeMethod Hollowmere.R9_B.PanelRecovery.Run -gcR9BMode prepare -gcR9BConfig <context.json>`.
2. Start `/tmp/r9-b-cargo-target/debug/r9-b-scratch --companion /tmp/r9-b-cargo-target/debug/gamecore-studio --cache <cache> --context <context.json> --evidence <fresh W-MECH-01/r9-b directory> --state-root /tmp/r9-b-service` as a supervised service. Wait for `R9_B_READY`.
3. Read only the nonsecret `panel-config.json` pairing **path**, then run the same creator entry point with `-gcR9BMode submit -gcR9BConfig <panel-config.json>`. It clicks the attached Stage control and exits after retaining the submitted job binding. Do not open another creator Editor during sandbox execution.
4. Wait for `R9_B_STAGE_PASSED`, then run `-gcR9BMode review` with the same config/pairing. It creates the entry before restoring the binding, clicks attached Refresh verdict, verifies Admit-enabled and captures only the Studio window's pixels. Stop the owned scratch supervisor and remove its external state.
5. Regression suite: the same wrapper with `--results <xml>` and `-runTests -testPlatform EditMode -testFilter 'GameCore.Studio.UI.*|Hollowmere.R9_B.EditMode.*|Hollowmere.P2_1.*'`. Read XML counts, not wrapper summaries.

## Cleanup

The owned scratch supervisor exited normally and its private installation/signing state was removed without reading credential files. A no-network cleanup container removed container-owned cache remnants, mounting only `/tmp/r9-b-service`. Retained the fresh candidate journal/verdict index as evidence, then removed only that candidate's project-side records. Restored Unity-generated changes to GraphicsSettings, QualitySettings and unrelated metadata to their original bytes; removed generated unrelated settings/folder metadata and checker bytecode. Candidate/verdict CAS bytes remain retained; shared caches, sibling clones and installed services were not changed. Exact paths are in `cleanup.json`. Production sources remain the tested checkpoint; subsequent changes are evidence, fixture capture/reconnection and this note.
