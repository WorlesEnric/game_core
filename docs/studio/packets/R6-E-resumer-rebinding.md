# R6-E — authenticated admission recovery rebinding

This is the packet's PACKET.md. Branch `omp/r6-e`; baseline `7009f82`. Product fix checkpoint `1df93155`; Play-context graphical harness checkpoint `891ebc85`.

## Finding and decision

P4.2f request 1 / R2-14: durable admission recovery calls `RefreshPendingVerdicts()` without establishing its authenticated service. ETOS startup is independently scheduled through `delayCall` and `EditorApplication.update`, and batch startup is normally disabled. `EnsureStarted()` previously treated any existing gateway as sufficient, without checking the current runtime or recreated admission options.

The retained P4.2f cold log shows startup before entering Play, another startup after the Play reload, then no session-start record between package reload and the repeated authenticated-refresh waits. This is evidence of the missing recovery prerequisite, not evidence of a slow native compiler.

Decision: recovery establishes the optional authenticated ETOS session synchronously before its first asynchronous verdict fetch. Core resolves the ETOS entry point without a reverse assembly dependency. `EnsureStarted(StudioRuntime)` starts against that exact runtime, replaces a stale-runtime session, and restores a missing stage binding when the gateway is still valid. The automatic session no longer launches a competing fire-and-forget verdict refresh; the resumer owns recovery refresh ordering. No deadline, verdict authority, mandatory step, compiler, or admission transition is relaxed.

## R2 fixes

| Finding | Fix | Regression |
|---|---|---|
| P4.2f request 1 / R2-14 | Bind the authenticated session before pending verdict refresh, independently of bootstrap/update order; preserve current-runtime ownership. | `Hollowmere.R6_E.Tests.AdmissionRebindingTests.R6_E_PendingReloadRefreshStartsAuthenticatedSessionBeforeFirstPump`; retained real graphical driver `Hollowmere.R6_E.RealAdmission.Run`. |

## Verification

- Baseline product sources plus the new regression: **0 passed / 1 failed**, with `InvalidOperationException: stage_service_unavailable` (`Evidence~/edit-before.xml.gz`). The same test passes after the fix, including a second refresh after recreating admission options while the authenticated gateway survives.
- First expanded EditMode run: **180 passed / 1 failed / 0 skipped**. The sole failure is CORE-PICK's explicit idle-host precondition, which counted two Editors. This is retained in `Evidence~/edit-concurrent.xml.gz`, not reported as a green suite.
- Final expanded EditMode run under the existing CORE-PICK solo adapter: **181 passed / 0 failed / 0 skipped** (`Evidence~/edit-final.xml.gz`). Executed filter: `GameCore\.Studio\.(Core|Etos).*|Hollowmere\.R6_[ADE].*|Hollowmere\.P2_4.*|Hollowmere\.R2_B.*|.*P31AdmissionInPlayMode.*`. Includes all 88 Core cases, seven ETOS cases, the admission trust/lifecycle cases and actual P3.1 enter-Play capture/restore coverage.
- Final existing Hollowmere PlayMode suite: **8 passed / 0 failed / 0 skipped** (`Evidence~/play-final.xml.gz`), using `Hollowmere\..*FullQuestHeadless.*|Hollowmere\.P2_4.*|Hollowmere\.R6_[ADE].*`. The version-gated P2.4 installed-package test is absent after proven Undo, not silently counted as run; real admitted-package Play coverage is the graphical 120-frame smoke and each isolated stage's two PlayMode XML cases.
- Existing ETOS dotnet client suite: **69 passed / 0 failed / 6 skipped**. The six skipped tests are environment-gated live installed-node cases, not exercised under this packet's no-installed-service rule.
- Companion: `cargo fmt --check`, all-target clippy with `-D warnings`, and `cargo test` pass: **145 passed / 12 ignored**. Local companion and standalone scratch runner build; standalone fmt/clippy pass.
- First fresh real Docker stage at `5ea5a9d7`: **171731 ms**, **36 EditMode + 2 PlayMode** passes, all seven mandatory steps pass, authenticated signature verification succeeds, unauthenticated verdict fetch returns **401**.
- Second fresh real Docker stage at `891ebc85`: **203234 ms**, **36 EditMode + 2 PlayMode** passes, all seven mandatory steps pass.
- Real unaided graphical lifecycle at `891ebc85`: job `stg_1a11284dc6a31d517f1f376`; **Admitted in 45073 ms wall / 44767 ms product timer**, **Undone in 15937 ms wall**. Display `:1`, RTX 4060 Ti, OpenGLCore, `isBatchMode=false`; resumed Play restores nine OldCoins and executes the production **120-frame Pending → Passed** smoke. The service binding targets the current runtime in the resumed domain, with no manually installed service, manual Resume, or post-reload Fetch in the driver. Catalog equals the signed prediction; Undo removes the package, clears pending state and restores the before catalog. Evidence: `Evidence~/real-02`.
- Scratch authentication audit: **0 provider calls / 0 worker tasks**, **9 verdict fetches / 8 verifier calls** (includes the deliberately unauthenticated fetch). The companion is a fresh local build; its installation/signing state is outside the checkout and Unity project. Only the synthetic authentication/proxy fixture is reused; stage, Docker confinement, signature issuance and verification use the real companion.
- Final policy gates pass: **42 packages / 91 package assemblies / 1230 C# files**, exact dependencies and C# policy unchanged.
- The successful graphical transcript contains the product call chain `AdmissionResumer.RefreshPendingVerdicts → UnityAdmissionServices.EnsureStageService → EtosStudioSession.EnsureStarted → StartCore` after reload, and no authenticated-refresh wait. This confirms recovery itself established the missing binding rather than receiving a harness injection.

Evidence directory: [`R6_E/Evidence~`](../../../games/hollowmere/Assets/Hollowmere/Tests/R6_E/Evidence~). Gzip preserves original signed records and XML; manifests record SHA-256 of decompressed bytes. The passing graphical screenshot was visually inspected. Retained failed setup attempts are separate from acceptance counts.

## Reproduction

Build the companion with `CARGO_TARGET_DIR=/tmp/r6-e-cargo-target cargo build --bin gamecore-studio` in `studio/agent`. Build the standalone runner with the same target directory and `cargo build --offline --manifest-path games/hollowmere/Assets/Hollowmere/Tests/R6_E/Scratch/Cargo.toml`. After the source checkpoint:

```sh
python3 games/hollowmere/Assets/Hollowmere/Tests/R6_E/run-live.py prepare .evidence/r6-e/context.json
/tmp/r6-e-cargo-target/debug/r6-e-scratch \
  --companion /tmp/r6-e-cargo-target/debug/gamecore-studio \
  --cache <exact-versioned-cache> --context .evidence/r6-e/context.json \
  --evidence .evidence/r6-e/fresh-run --state-root /tmp/r6-e-service
# Wait for R6_E_READY, then use a separate shell while the owned scratch service remains alive:
python3 games/hollowmere/Assets/Hollowmere/Tests/R6_E/run-live.py graphical \
  .evidence/r6-e/fresh-run/graphical/live-config.json
python3 games/hollowmere/Assets/Hollowmere/Tests/R6_E/run-live.py retain \
  .evidence/r6-e/fresh-run/graphical/live-config.json \
  games/hollowmere/Assets/Hollowmere/Tests/R6_E/Evidence~/fresh-run
```

Use `gamecore-studio stage cache-path` for the exact cache identity and `studio/stage/provision-cache.sh <cache> --verify` before use. The runner verifies and privately copies that cache. All Editors use `unity-batch.sh`; stage finishes before the graphical Editor starts. Stop only the owned scratch service after Undo. The wrapper forwards only a nonsecret pairing path, then sets the ordinary credential-resolver environment variable immediately before Unity exec; no credential contents are inspected by the harness. Existing project ETOS settings, pending admissions or an installed pressure-plate package cause refusal rather than overwriting creator state.

### Retained setup failures

The first Unity attempt failed to compile the new harness because its SecretRedactor namespace import was missing; corrected. The first test fixture used a non-digest catalog string and was rejected by the client before recovery; corrected and retained as `setup-fixture-error.xml.gz`. Neither is counted as the product regression.

The first graphical attempt and its diagnostic retry stopped **before Admit**: preparation exported the Edit-mode catalog (`39de…334ba`) while the actual Play-mode registry was `f139…64ea8`. Runtime, node, project and source identity matched. Preparation now enters the actual Boot scene and waits for ready Play before exporting the trusted catalog. No candidate code/proposal/artifact or product validation was changed. Both failures and their signed stage are retained under `Evidence~/context-edit` and `Evidence~/context-diagnostic`.

## Requests to other packets

Shared historical packet notes, verification rows and matrix statuses are outside this packet's exclusive paths. Documentation owners should append this finding → fix → test result to the R2-fixes sections of `P0.5-companion.md`, `P1.6-studio-core-unity.md`, `P2.2-studio-etos-client.md`, and `P2.4-staging-lane.md`; these files were intentionally unchanged. The verification owner must consume this packet's final evidence before changing W-MECH-01 or B-STAGE status. No cross-packet code change is required.

## Left open

- No installed companion or etosd operation is authorized. The passed graphical qualification uses a newly built local companion and private external installation state; it does not establish an installed-service upgrade or creator-click workflow.
- One complete successful graphical lifecycle is qualified here. The earlier two graphical attempts refused before Admit on their harness context mismatch. The matrix's two-successful-run B-STAGE row is therefore not claimed closed by this packet, despite both measured stage durations and the successful admission being within budget.

## Cleanup

Both private scratch supervisors exited normally. Their external installations were removed without inspecting key contents. Real Undo was proved before removing the owned test journal/completed-state records; no pending admission or installed pressure-plate package remains. Unity-generated changes to existing project settings and unrelated metadata were restored to the initially clean baseline, and generated unrelated metadata was removed. No installed service, sibling clone, shared cache, kernel source, or other packet's code was modified.
