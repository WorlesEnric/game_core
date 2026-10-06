# R6-A — trusted world delta and bounded admission compilation

Branch `codex/r6-a`, base `d508045`, Linux build host. No files under `docs/` were changed; P4.3-final owns their integration. Source commits: `e4e38fb0` (world export), `fefd3083` (compile lifecycle and real integration harness), `06e6b06d` (cached-verdict refusal and snapshot regressions).

Raw `.log`, `.xml` and `.txt` evidence is retained losslessly as `.gz` (the uncompressed names below identify those records). Use `gzip -dc <name>.gz` to recover the exact bytes; `raw-sha256.json` records their original digests. XML counts were read from the actual Unity-generated result files.

## Witnesses and reproduction

P4.2d's `artifacts/studio/workflows/P4.2d/admission-pending-snapshot.json` has `phase=compile` with no compile deadline/domain identity. Its signed stage delta omitted world/predicted; its live baseline was `d82aed18…`. The original harness treated missing WorldDefinition as an informational summary and the original Rust settlement still passed.

`baseline-red.log` runs the missing-world assertion against the actual `d508045` verdict module, retained as `baseline-verdict.rs.txt`: **1 expected failure**. The same assertion passes on the fixed implementation. The 817-second retained live witness is the pre-fix compile failure; it was not recreated by deliberately hanging another Editor.

## R2 fixes

| Finding | Fix | Regression / proof |
|---|---|---|
| P4.2d #1; R2-09/14 | Export the registered project's single Git-tracked baked world catalog description as data outside Assets. Bind its SHA-256 to change set/source revision. Mount the copied snapshot read-only at both sandbox aliases; never mount the live project. Recompile the description using the trusted content compiler. Require complete world/predicted and canonical catalog-set composition in Rust settlement, authenticated issued-record retrieval, and Unity VerdictCheck. Signed covered artifacts/step facts retain the source digest. | Rust `r6_a_01_missing_world_or_predicted_never_passes`, `r6_a_01_snapshot_is_bound_to_request_revision_and_bytes`; extended `r2_09_signed_verdict_transport_rejects_tampering_and_partial_jobs` rejects even installation-signed incomplete old records. Python four `test_R6_A_01_*` tests cover trusted enumeration, missing/ambiguous descriptions, source links and reused-destination links. Harness `WritesTheCatalogDelta`, `R6_A_01_MissingSourceWorldRefuses`, `R6_A_01_ChangedSourceWorldRefuses`, `R6_A_01_TrustedSourceWorldRecompilesDeterministically`. Unity `R6_A_01_P42dMissingDeltaRefusedBeforeInstall` (2 cases). |
| P4.2d #2; R2-14 | Observe compilation before Resolve. Registration callbacks only set a flag; a subsequent update refreshes once, including when batch registration needs that refresh to progress. Persist action, UTC deadline, domain identity and outcome before requesting compilation. Never silently reissue an existing attempt. Wait for a new loaded domain before rebake/catalog verification; retain explicit timeout records and deterministic terminal removal failure. Ignore callbacks from superseded actions/phases. | `R6_A_02_ExpiredDurableCompileDoesNotReenterAndRetainsTimeout` (new and legacy 817-second records; also verifies unbound recovery returns the same timeout). `r6_a_02_real_stage_play_admit_smoke_undo` uses the real companion, Docker stage, production authenticated C# client, real compiler/catalog/checker, Play capture/restore, tri-state smoke and compiled undo. |

Admission compilation retains a **90-second** persisted limit. Removal has a separate **180-second** persisted limit; the specification's 90-second B-STAGE admission-to-Play gate is still asserted by the real test. A blocked native refresh cannot run a managed timer; the original UTC deadline survives and is enforced when control/recovery returns. Expired records never acquire a new install budget.

The public Resolve behavior was checked against the [Unity 6000.0 reference bindings](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.0/Modules/PackageManager/Editor/Managed/PackageManager.bindings.cs); the final compiler uses that public API.

The exported data is the source world's baked catalog description, not a candidate constant, copied script, raw Unity asset or writable live-project mount. The live re-bake still has to equal the signed world fingerprint, and the observed mechanism catalog must build its declared fingerprint and compose to the signed prediction. An absent/ambiguous source description or stale live re-bake refuses admission.

## Real path qualification

The scratch companion is a real built binary, with a private ledger, CAS and installation signing key under `~/.cache/gamecore-studio/r6-a/service/`. A synthetic node supplies test authentication; candidate bytes are seeded from the unchanged pressure-plate sample without a worker/provider call. Unity uses the production CompanionClient/CompanionStageService fetch and verify routes. No fake compiler, catalog, checker, modified verdict or host-confinement exemption is used.

Two complete Docker stages passed within the unchanged 360-second warm gate: **179352 ms** and **268453 ms**, each with **15 Rules + 36 EditMode + 2 PlayMode** passes, empty semantic findings and equal deterministic smoke hashes. The final job is `stg_1a111629346057c8960aab6`. Subsequent compiler diagnostics reused this exact issued job through authenticated fetch/verify; they did not manufacture or re-sign a verdict.

Final real lifecycle: **PASS**, `live-rust.log`, `live-admit.json`, `live-undo.json` and the final `live-logs/` transcript. Admission took **48485 ms** including restored Play and smoke. Coins were changed from 2 to 9 before capture and verified as 9 after restore. Smoke observed **Pending → Passed**, **120 game frames**. The package was then removed, compiled, reloaded and its absence verified.

- World before / after undo: `d82aed185b6d2e4f415e1e8d45f8d70a4e2f6779be825786ba03db3b3b447c18`.
- Signed predicted / observed admitted catalog: `361ae5328dc281f6fa7142fc551671baed8cbb7d0f2e14b7abbc858c8cd0772d`.
- Mechanism: `eabc05e00b2ad24fedbee8f283cd60e66782415e00865936919672ce1dab3f21`.
- Source description SHA-256: `c73d4c34a1a9b55d145d45aa5b3cbee53d8db9f46478ab084ac9ae778dd0b35d`.

The R6 live test is a Rust orchestrator plus a Unity executeMethod driver with durable progress across genuine script reloads. It does not synthesize NUnit XML. The existing P3.1 admission UnityTest also ran separately and passed; its historical compiler/catalog doubles are not the real-path evidence above.

### Retained failed attempts

- Initial suite failed compilation because the new driver omitted a Narrative assembly reference; corrected.
- `live-first`: stage passed, but the driver request fixture omitted sourceProject and stopped before admission; corrected.
- `live-second`: admission passed in **56381 ms**. Undo hit the initial 90-second removal timeout: native refresh took **94.331 s**, including **86.337 s** in InvokePackagesCallback and **4.054 s** compiling. The durable timeout is retained. No admission timing gate was widened.
- `live-third`: attempting refresh without explicit package resolution produced a stale catalog and removal compiler errors. This experiment was reverted.
- `live-fourth`: waiting only for registration failed to initiate timely batch refresh, reached the original admission deadline and rolled back. The driver later reached its overall timeout. This experiment was reverted.
- Initial broad XML: **171 pass / 11 fail**. Two new test expectations were corrected to assert the earlier fetch refusal. Seven polling failures during that changing-source run disappeared on the final unchanged-source run. CORE-PICK's shared-host precondition observed two Editors; the final run used its existing solo adapter and passed. The remaining historical-verdict failure is below.

These failed attempts remain failures; they are not counted as acceptance passes. Startup package-resolution stalls are separately visible in wrapper wall times, including the final 477-second Editor invocation; they occurred before the measured admission started.

## Verification

- Final Rust ordinary suite: **145 passed / 0 failed / 12 ignored**; fmt and all-target clippy clean. Opt-in real lifecycle: **1 passed**.
- Stage Python: **26 passed**. Gameplay boundary Python: **2 passed**.
- Roslyn analyzer dotnet suite: **50 passed**. Sample Rules: **15 passed per real stage**.
- Final required EditMode filter plus P3.1 admission: **183 passed / 1 failed / 0 skipped**, from `final-editmode.xml`. All four R6-A cases and all R2-B smoke/history cases pass. CORE-PICK and P3.1 admission pass.
- Final PlayMode: **8 passed / 0 failed / 0 skipped**, from `final-playmode.xml` (333-second wrapper wall time).
- Slot checker self-tests: **29 passed**.
- Package metadata and C# policy pass: **42 packages / 91 package assemblies; 1223 C# files**.

Commands: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test` in `studio/agent`; `dotnet test studio/stage/analyzer/Tests/StageAnalyzer.Tests.csproj`; `python3 -m unittest discover -s studio/stage/tests -v`; `python3 Packages/com.gamecore.studio.gameplay/Tests~/test_boundary.py`; both required repository policy checkers. Every Editor used `studio/tools/unity-batch.sh`; this packet held at most one Editor. The final broad EditMode run used the existing CORE-PICK solo adapter to satisfy its idle-host precondition.

For a fresh real run, provision the exact `stage cache-path` under `~/.cache/gamecore-studio/r6-a/stage/_warm/` with `studio/stage/cache.py`, then run `cargo test --test r6_a_real -- --ignored --nocapture`. The optional `R6_A_REUSE_STATE` / `R6_A_REUSE_JOB` pair can reuse only this test's scratch-state subtree; the complete issued record is still authenticated and verified. Retained old jobs bind the source revision at which they were staged; a new source revision requires a fresh stage.

## Requests to other packets

`games/hollowmere/Assets/Hollowmere/Tests/R5_A/Editor/P31AdmissionInPlayMode.cs`, method `R5_03_InstalledVerdict_CapturesInPlay_VerifiesCatalogBeforeInstall`: the historical installed-job test expects the known incomplete P4.2c delta to authorize admission. R6-A now correctly refuses it in `StageAdmission.FetchVerdict` with `verdict_failed`. Update the test to assert this refusal, or provide a fresh companion-issued job containing world/predicted. Do not bypass VerdictCheck or modify the retained signature. This test file is outside R6-A's exclusive paths.

P4.3-final: relocate this packet note's R2-fix and qualification sections into the relevant P0.5/P1.6/P2.4 notes. No documentation path was edited here.

## Left open

- The requested broad EditMode suite is **not all green**: its sole final failure is the incompatible historical R5-A expectation above. Ownership forbids changing that file; the security rule was not relaxed to pass it.
- A source project with no single tracked baked world catalog description is refused. Source authoring changes must use the existing bake workflow; missing/ambiguous/stale source data is not replaced with candidate fingerprints.
- Native Editor/UPM startup stalls remain a host behavior governed by the existing shared runner's watchdog/retry. The managed compile deadline is durable but cannot execute while native code blocks the main thread.
- This qualifies the unchanged pressure-plate sample's real stage/admit/restore/smoke/undo path, not paid worker generation, a graphical UI walkthrough, or every possible catalog-contributor extension.

Zero paid operations; no installed companion or etosd stop/restart. No key/provider/auth file was read or printed by the implementing tools. The scratch companion's private signing key remains outside the repository.
