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

Verification is being recorded under `W-MECH-01/r9-b/` and `W-GAME-05/r9-b/`. No player build or installed companion/node operation is used. The scratch runner reuses only R6-E's synthetic authentication/proxy fixture; the companion, stage execution, signature issuance and verification are real. It creates private external installation state, and the actual candidate-panel Stage control submits the app-origin candidate. The creator Editor exits before sandbox Editors launch; review reopens after stage completion.

## Requests to other packets

Shared notes `P2.1-studio-ui.md` and the verification matrix are outside R9-B's exclusive documentation paths. Their owners should link this finding → fix → test table. No shared protocol or source change is required. No matrix status promotion is claimed.

## Left open

Final verification receipts and signed-stage outcome are recorded below when available. Historical P4.2i row counts remain **57 PASS / 6 BLOCKED / 5 FAIL**; this packet does not requalify unrelated rows.
