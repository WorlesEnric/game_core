# R10-A — admission freshness and bounded local compilation

This is the packet's PACKET.md. Branch `omp/r10-a`. **W-MECH-01 PASS; W-DOC-02 PASS.** Both rows are qualified at product `2e17a01ec02dcea3b4762b4536605120c985681b` against a locally built scratch companion on display `:1`. Only these two ROWS.json objects and aggregate counts change: **60 PASS / 6 BLOCKED / 2 FAIL**. The other 66 row records, their scenario/requirement text and prior provenance remain unchanged; this is not a new all-row same-revision acceptance or installed-release upgrade.

## Retained witnesses

P4.2j request #1: both signed pressure-plate stages used world `6c13778e6adf58964f55654c0b352352e4fba6cdd0024655690c49d536734bea`, while admission's current authored-world computation returned `d82aed185b6d2e4f415e1e8d45f8d70a4e2f6779be825786ba03db3b3b447c18`. The service copied the tracked baked description; the Editor recomputed authored state. A tracked file is not a freshness guarantee after worker edits and normal Undo.

P4.2j request #2: `W-DOC-02/p42j-lever-literal/last-pending.json` records `phase=compile`, `compileOutcome=pending`. The corresponding UPM log records `project:resolve-packages` from 13:34:03.240Z to 13:36:44.488Z (161248 ms), with four `ECONNRESET` failures contacting `packages.unity.com`. The Editor records 161447.365 ms in `InvokePackagesCallback` and 171.157 s in the synchronous asset refresh. The authenticated resumer had already reached `CompilePending`: this was not a missing stage-service binding. [Exact diagnosis](../../../artifacts/studio/verification/W-DOC-02/r10-a/retained-diagnosis.json).

## Decisions

- Keep signed source binding and live authored-world recomputation. Never substitute candidate-provided fingerprints or bless a mismatching catalog.
- Export a current, non-mutating world description using the same trusted computation as `Entry.Verify`; companion must validate the registered project's source inventory before using it. Explicit Stage may use the existing source-save tool; it does not run a generated-code rebake or introduce authored edits.
- Admission changes embedded packages, not requested registry versions. Unity's public `Client.Resolve()` calls `Resolve_Internal(true)`. Use the pinned Editor's non-forcing resolver so local package registration does not force a registry metadata refresh. Preserve the 90-second durable install deadline, actual compile/reload and catalog verification; fail closed if the pinned resolver is unavailable. [Unity 6000 binding](https://raw.githubusercontent.com/Unity-Technologies/UnityCsReference/6000.0/Modules/PackageManager/Editor/Managed/PackageManager.bindings.cs).

## R2 fixes

Product regressions and all three complete graphical creator lifecycles are verified below.

| Finding | Fix | Regression / proof |
|---|---|---|
| P4.2j #1 / R2-09, R2-14 | Compute the trusted current source description without rewriting bake outputs; verify source inventory in the companion and require generated-runtime coherence before export. | Nine `test_R10_A_W_MECH_01_*` Python regressions fail against the baseline exporter and pass after the fix. Full Python stage suite: 42 passed. Unity `R10_A_W_MECH_01_StaleDescriptionExportsCurrentWorldWithoutRebake` and `R10_A_W_MECH_01_StaleGeneratedRuntimeRefusesBeforeSnapshotPublication` pass. |
| Graphical freshness follow-up | Imported Unity objects can be dirty caches rather than editable source assets. Save/refusal detection follows native authored assets and scenes; the complete disk-source inventory still covers imported sources. | First graphical export refused a dirty loaded `.tss` after `project.save`; retained under `primary/cold`. `R10_A_W_MECH_01_ImportedCacheDirtinessDoesNotBlockSavedAuthoring` passes and still requires native authored dirtiness to be detected. |
| P4.2j #2 / R2-14 | Non-forcing local package resolution; unchanged 90-second durable deadline and real compile/reload. | Original retained 161248 ms UPM stall and `compile_timeout` are the before witness. Named real regression `R10_A_02` now completes literal lever creator admission in **47375 ms wall**; UPM registration takes **18892 ms**, without the original registry reset errors. The lever works in Play and normal History Undo completes in **34288 ms**. |

Verification already read from actual result files: admission EditMode XML **89 passed / 0 failed / 0 skipped**; analyzer TRX **50 passed / 0 failed**; Rust **153 passed / 12 ignored**, fmt and all-target clippy clean; package metadata **42 packages / 92 assemblies** and C# policy **1281 files** pass. Initial Rust fixture migration failures are retained separately; fixtures now provide source-bound receipts without weakening production validation.

## Complete creator qualification

| Case / named regression | Signed stage | Stage wall | Admit → restored Play/smoke wall | Normal Undo wall | Stage XML |
|---|---:|---:|---:|---:|---|
| `R10_A_01_cold` | 165852 ms | 167903.516 ms | 34751 ms | 36494 ms | 36 EditMode + 2 PlayMode passed |
| `R10_A_01_warm` | 82232 ms | 83776.246 ms | 30089 ms | 33832 ms | 36 EditMode + 2 PlayMode passed |
| `R10_A_02` literal lever | 88035 ms | 88747.306 ms | 47375 ms | 34288 ms | 16 EditMode + 2 PlayMode passed |

[Exact three-case qualification](../../../artifacts/studio/verification/W-MECH-01/r10-a/qualification.json), [cold](../../../artifacts/studio/verification/W-MECH-01/r10-a/final/cold/result.json), [warm](../../../artifacts/studio/verification/W-MECH-01/r10-a/final/warm/result.json), [lever](../../../artifacts/studio/verification/W-DOC-02/r10-a/final/lever/result.json).

Both pressure cases first apply a worker-format structural candidate through production Preview/Apply, prove early `bake_stale` for incompatible generated runtime, then normal journal Undo restores exact asset bytes and the original fingerprint. No generated bake file changes and no manual rebake occurs. This is a deterministic worker-format regression, not a paid worker-generation claim. Each passing stage has all seven mandatory steps, no forbidden findings, Docker confinement, a service signature, and equality with the exact record authenticated by the real creator panel. The enabled `candidate-admit` control receives the normal submit event with Capture and stop Play checked.

Admission automatically rebinds the authenticated service across actual domain reloads, restores nine OldCoins, executes 120 game frames with Pending → Passed smoke and checkpoint round-trip equality, and verifies the signed catalog set. Legacy pressure is an observer-only package: the actual restored root equals signed `world`, while its independently loaded catalog set equals signed `predicted`. The lever declares a runtime extension, so its actual composed root equals signed `predicted`; the real `lever-toggle` control commits off/on/off. Reviewed world PNGs show green/on and red/off handle poses. Normal History dispatch then removes each package, genuinely compiles/reloads, verifies the original catalog and clears pending state. Admission remains bounded by 90000 ms; removal by 180000 ms.

Reproduce: build the local companion and `dotnet build games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/Submit~/LeverSubmit.csproj -c Release`, then `python3 games/hollowmere/Assets/Hollowmere/Tests/R10_A/run.py /tmp/r10-a-new --label new --companion "$PWD/studio/agent/target/debug/gamecore-studio"`. Every source, stage and admission Editor runs through the shared host allocator, serialized to at most one Editor owned by this packet. Private scratch nodes have no workers/providers; each final ledger reports zero attempts/media charges/voice sessions. No installed service was stopped, restarted or modified; no key contents were read or printed.

## Retained qualification attempts

- `primary/cold`: pre-stage graphical export found imported theme-cache dirtiness; product correction and regression added. No signed stage or admission occurred.
- `verified/cold`: source export and signed cold stage passed. Stopped the owned runner before admission to correct an overstrict observer assumption: legacy pressure has no world-extension declaration, so its actual root must equal signed `catalogDelta.world`; the lever's composed root must equal signed `predicted`. Full observed catalog-set equality remains required for both. No package was installed and no admission was pending.
- `qualified/cold`: signed stage and explicit creator Admit passed, restoring nine coins in 39531 ms wall. The harness mistook `HistoryResult.Ok=false` during asynchronous removal for refusal. Normal History had already persisted action `undo`; a separate recovery Editor resumed it and returned `Undone` with the original catalog, removing the package and pending record. This run is retained as incomplete-row evidence, not a complete lifecycle PASS. The observer now requires durable Undo acceptance, then waits for actual `Undone`, absent package/pending state, matching catalog and the unchanged 180-second removal bound.

## Requests to other packets

`Packages/com.gamecore.gameplay.compile/Editor/Entry.cs`: expose a public, non-mutating `ComputeDescription(WorldDefinition world, BakePaths paths, out BakeResult? failure)` returning the exact `Compute(...).Description` or an empty string on failure, using the existing planning algorithm. R10-A cannot edit this exclusive gameplay path; its adapter must use the existing computation without duplicating it and fail closed if unavailable.

Shared-note owners: incorporate the final R10-A finding/fix/test results into the admission sections of P0.5, P1.6 and P2.4. Those shared notes are outside this packet's final exclusive documentation paths.

## Left open

No requested row remains open. Public non-mutating gameplay bake API availability remains the precisely scoped upstream request above; the current adapter reuses the existing planner and fails closed. Genuinely incompatible generated runtime still requires the named ordinary Bake workflow; it is refused early rather than silently admitted. The requested edit → Apply → Undo → Stage flow needs no manual rebake. Broad unrelated game suites, paid generation and installed-service upgrade are not claimed. The registry's remaining two FAIL and six BLOCKED rows retain their previous evidence and ownership.

## Cleanup and delivery

All four owned scratch supervisors exited; only their explicit private installation roots were removed, including Docker-created root-owned nested mount directories. Signed records, original failure/interrupt witnesses, actual XML, rendered PNGs, UPM logs, wall clocks and terminal journal/package-removal preimages remain under the two owned evidence directories. All 1722 initial non-secret project-source preimages match after restoration; generated unrelated Unity metadata/settings were removed. No admitted pressure/lever package or pending admission remains. [Source restoration](../../../artifacts/studio/verification/W-MECH-01/r10-a/cleanup/source-restoration.json), [scratch cleanup](../../../artifacts/studio/verification/W-MECH-01/r10-a/cleanup/scratch-cleanup.json).

Final post-cleanup policy gates: Python stage **42 passed**, slot-policy self-tests **29 passed**, exact package metadata and C# policy pass. Earlier Rust fixture-migration failures and the three non-final graphical attempts are retained, not counted as acceptance. Historical evidence is not rewritten.
