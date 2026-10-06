# R7-A: three FAIL rows

Branch `omp/r7-a`, baseline `f4b9d00`. Source checkpoints: `20512d83` cache provisioning; `1a462f88` runtime NPC selection, deterministic reopen contract and drivers. All qualification ran on the Linux build host, graphical slot `:1`, one packet-owned Editor at a time through `studio/tools/unity-batch.sh`. No installed companion/etosd restart, explicit pairing or paid request.

## Results

| Row | Disposition | Evidence |
|---|---|---|
| W-UI-01 | PASS | Ordinary Open Studio automatically connects; HUD walking commits 7.636 m in 3.040 s; Select shows Maren and MarenEntity in the actual definition card. [Proof](../../../artifacts/studio/verification/W-UI-01/r7-a/README.md) |
| W-AI-06 | PASS | Preparation PID 4005226, separate reopen PID 4007734; saved hashes match; normal undo/redo/final undo plus production bake restores every byte of Odd, DrownedBell, HUD and Lantern. [Proof](../../../artifacts/studio/verification/W-AI-06/r7-a/README.md) |
| W-DOC-02 | FAIL retained; commissioned fresh-cache defect fixed | New private-root provisioning and seven-step Docker sample stage pass in 169.481 s, 36 EditMode + 2 PlayMode passes. A new lever and authenticated Admit are not proven by an unsigned pressure-plate CLI stage. [Proof and boundary](../../../artifacts/studio/verification/W-DOC-02/r7-a/README.md) |

Matrix: **37 PASS / 30 BLOCKED / 1 FAIL**, 68 rows. Only these three row objects/READMEs and their 07 status/evidence cells changed; SUMMARY regenerated from ROWS. Other rows and historical headline retain their original attribution.

## R2 fixes

### W-UI-01 / R2-29

Automatic gateway startup and keyboard-event isolation already exist in the baseline (R4 fixes), but lacked current ordinary-open qualification. The new no-spend `Hollowmere.R7_A.OpenPlay.Run` uses normal `StudioMenu.OpenStudio`; it neither pairs nor calls a gateway-start method directly. It reuses existing real-game walking and window-capture helpers while skipping their paid steps.

The real run found a further product defect: `RuntimeViewMapper.Map` ignored the NPC's explicit view tag whenever generic picking found an authored ancestor. Runtime NPCs are children of authored GameBoot, so clicking Maren selected GameBoot. The mapper now gives the runtime tag precedence, retaining UI/ground exclusions and unresolved-tag fallback.

Finding → fix → test:

- W-UI-01 NPC card → runtime-tag precedence → `R7_A_RuntimeNpcTagOverridesAuthoredBootstrapAncestor`: fails before, passes after. Actual pre-fix and fixed graphical runs retained.
- R2-29 → existing image-only ownership verified without bypass → `R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers`, `R2_29_OnlyImageFocusOwnsGameInputAndLossResetsHeldKeys`.
- Automatic startup → installed pairing discovered at ordinary open → `P42_STARTUP_01_AutomaticSessionBindsHostPairingAfterReload` plus actual connected capture.
- UI suite maintenance → stale signed verdict fixture now includes mandatory world/catalog type/fingerprint/predicted digest → existing `R2_13_*` tests; no production trust rule changed.

### W-AI-06

`contentStamp` is already deterministic SHA-256 bake metadata. Play's production bake updates it; authored-field inverses do not fabricate a bake result. The defect was comparing a baked baseline with stale post-undo metadata. One paragraph in `03-authoring-contracts.md` defines the saved-byte boundary: production bake after final History transition, save, compare all bytes including contentStamp.

Only Reopen's step list changes: call the same `Entry.Bake` used by Play, fail on bake refusal, retain its receipt, save and keep exact equality. No stamp field is dropped, filtered, restored from fixture bytes or made time-dependent. No dialogue/runtime/Journal algorithm change was needed.

`R7_A_ReopenBakeRestoresAllBytesAfterRetainedNarrativeUndoRedoUndo` demonstrates the stale-metadata mismatch, runs the actual fixed Reopen bake step, checks complete original bytes and checks repeat-bake idempotence. Fixture cleanup is after assertions. Separate-process acceptance replays unchanged retained P4.2e candidate bytes through normal engine/precondition validation, saves, closes, reopens, undoes/redoes/final-undoes, bakes and verifies `backToBefore=true`. No new generation.

### W-DOC-02 / O52

`install-state.py stage-cache` derives the exact cache path from the built companion and provisions/verifies the complete pinned offline closure without node configuration. Root-derived stage-tool mode refuses malformed/foreign/linked cache paths, incomplete seed inputs and corrupted bytes. The existing dependency-only low-level mode remains for sandbox probes. `guide-flow.sh` executes the literal sample regeneration/check sequence, provisioning and full Docker stage with unchanged budgets. Guide 09 documents every prerequisite and authentication boundary.

Tests: six `test_R7_A_*` cache cases and three `test_r7_a_cache_*` installer cases. Baseline cache code fails the new positive/integrity regressions; fixed suites pass. Real first preflight correctly refused one missing pinned public UPM record in the mutable default host cache. The successful fresh-root command explicitly supplies a verified operator public-UPM seed; no locks/pins/cold-grace marker were altered or copied.

## Verification

Final graphical regression XML: **71 passed / 0 failed / 0 skipped**, Unity exit 0, 140 s. This includes the complete UI suite, automatic-startup client regressions and strict-byte regression. [XML](../../../artifacts/studio/verification/W-UI-01/r7-a/ui.xml). The separate headless driver XML is **17/17**; no failing aggregate is substituted for either passing environment-specific suite.
- .NET ETOS client: **69 passed / 0 failed / 6 opt-in live skipped**, retained TRX.
- Stage Python: **32 passed**; installer: **17 passed**.
- Rust consumer gates: fmt/check and clippy clean; **145 passed / 12 external or opt-in ignored**.
- Dedicated strict-byte regression: **1 passed** in XML.
- Headless workflow driver suite: **17 passed** in XML. Its headless-refusal test was initially run in the graphical aggregate and correctly failed its mode assumption; that attempt remains retained, not relabeled.
- Package metadata: **42 packages / 91 package assemblies**, exact dependencies agree. C# policy: **1236 files**, pass.
- Real open/play driver: exit 0, 78 s. Separate preparation/reopen: exit 0, 43/79 s. Fresh Docker stage: 15 Rules tests, 36 EditMode and 2 PlayMode XML passes, 169481 ms.

The initial driver compile overlapped creation of its new test asmdef, then exposed redundant TestAssemblies reference injection; both were corrected. The owned safe-mode Editor was terminated after TERM did not exit. No sibling Editor or service was touched. Subsequent compile/test/actual workflows succeed; failed attempts are preserved.

Cleanup restored only Unity-generated ProjectSettings/metafile changes from this initially clean clone, removed generated out-of-scope folder metadata and Python caches, and retained the three final Undone journals in evidence before removing those packet-created project journal entries. No creator history was reset. The local narrative preparation marker was removed so a future fresh proof is not blocked by this run.

## Requests to other packets

The guide acceptance owner must supply the remaining literal lever and authenticated private-run exercise. Exact reusable seam: `artifacts/studio/verification/TOOLS/P42gLive/StageUi.cs` `Submit()`/`Review()` drives `RequestStage` → authenticated `RefreshStage`/`CanAdmit` → explicit `Candidates.Admit(entry,true)` → restored-world smoke → History undo. `artifacts/studio/verification/TOOLS/live-p42c.py` `unity(row,label,extra,workflow=None,results=None,environment=None)`, `project_id()` and `ledger()` currently hardcode installed-node authority and `.evidence/live`; add explicit scratch service/project/evidence inputs rather than changing installed configuration. Product APIs are `CompanionClient.StageAppCandidateAsync`, `FetchTrustedVerdictAsync(jobId)` and `VerifyVerdictAsync(jobId,signedRecord)`.

## Left open

- W-DOC-02's full unchanged row is not established: guide's maintained mechanism is a pressure plate, not a new lever; this private CLI stage issues no authenticated admission authority. R7-A's cache fix removes the reported product blocker but cannot substitute its unsigned diagnostic verdict for explicit creator Admit. The exact outside-owned acceptance harness seam is above; row stays FAIL.
- No paid/live-provider, node-death, installed companion restart, or unrelated blocked-row claim. Opt-in suite skips remain skips.

