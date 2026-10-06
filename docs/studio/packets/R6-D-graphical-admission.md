# R6-D — graphical admission qualification

This is the packet's PACKET.md. Branch `omp/r6-d`; starting revision `12a50176`. Retained harness revision `edcb34d8`. All execution occurred on the Linux build host. No installed companion or etosd changes, paid operations, sibling-clone access, or credential-file inspection.

## Finding and decision

P4.2e / R2-14 / W-MECH-01 remains **open as a compiler defect**. Three new graphical executions of the **unchanged production compiler** succeeded; they do not invalidate the two historical graphical failures. No speculative compiler-ordering change or timeout increase is shipped.

The historical warm failure log `artifacts/studio/verification/W-MECH-01/p42e-stage-review-20261006T145348.148583Z/logs/p42e-stage-review-20261006T225348-2759573-a1.log` reports:

- line 795: Package Manager resolved packages in **146.08 seconds**;
- lines 1251–1261: refresh **153.922 seconds**, of which `InvokePackagesCallback` consumed **146107.623 ms**;
- subsequent durable recovery rolls back with `compile_timeout` and verifies removal.

This localizes the historical delay to native package resolution, rather than establishing a lost managed `delayCall`, a missed domain reload, or compilation started during capture. `Client.Resolve()` returned to the caller before native refresh; the transcript records `Waiting for compilation` before the long resolve. Unity's public binding forces resolution (`Resolve_Internal(true)`). The historical run has no retained per-process UPM request/network diagnostic log, so the reason that resolution took 146 seconds cannot be recovered from its Editor transcript.

In the first new graphical run, the owned UPM log records the admission resolve request at 16:21:09.761Z and success at 16:21:31.810Z (**22049 ms**). Admission, resumed Play, restored nine OldCoins, and 120-frame `Pending → Passed` smoke finish in **48437 ms**; undo finishes in **21974 ms**. The second graphical run finishes admission in **28157 ms**, undo in **24768 ms**. Both use OpenGLCore on **NVIDIA GeForce RTX 4060 Ti**, display `:1`, `isBatchMode=false`; the admission witness records `isPlaying=true`.

## R2 fixes / retained regression

| Finding | Delivered change | Test / disposition |
|---|---|---|
| R2-14 / P4.2e graphical compile timeout | Retained real-GPU executeMethod regression with durable JSON progress, authenticated signed verdict fetch/verify, real compiler/catalog/checkers/capture, explicit 90-second admission assertion and independently bounded undo. Added per-process UPM logs to future runs. Production lifecycle unchanged. | `Hollowmere.R6_D.RealAdmission.Run`, launched by `R6_D/run-live.py graphical`; three passes. This is qualification coverage, **not a demonstrated fail-before/pass-after compiler fix**. |
| R6-A batch preservation | Same fresh signed job, original unmodified R6-A executeMethod driver, separately allocated Editor and evidence. | `Hollowmere.R6_A.RealAdmission.Run`; results recorded below. |

## Reproduction

All new sources are under `games/hollowmere/Assets/Hollowmere/Tests/R6_D/`:

- `Live/RealAdmission.cs`: `ScriptableSingleton` driver survives genuine script/domain/Play reloads; no test-runner compilation locks, compiler doubles, injected verdict bytes, or candidate changes.
- `Scratch/`: Rust 2021 standalone runner shares the existing synthetic **authentication-only** node fixture. A real companion, private ledger/CAS/signing key, and real no-network Docker stage produce a fresh issued record bound to HEAD. Installation state is created under `~/.cache/gamecore-studio/r6-d/service/node-*`, outside both the repository and Unity project.
- `graphical-unity.sh`: strips only the batch/nographics flags and selects `:1`; allocation and supervision remain `studio/tools/unity-batch.sh`.
- `run-live.py`: launches graphical or original batch driver and refuses reused progress. A successful process exit is insufficient: it checks admit/undo outcomes, catalog equality, 120-frame tri-state smoke and admission timing witnesses.

Build the normal companion with `cargo build --bin gamecore-studio` in `studio/agent`. Build the standalone runner:

```sh
CARGO_TARGET_DIR="$PWD/studio/agent/target" cargo build --offline \
  --manifest-path games/hollowmere/Assets/Hollowmere/Tests/R6_D/Scratch/Cargo.toml
```

Provision/verify the exact pinned cache using the existing `studio/stage/provision-cache.sh`; `gamecore-studio stage cache-path` identifies its version. This run used the operator cache `~/.cache/gamecore-studio/r6-a/stage/_warm/22421f6df7cfc2cc396043796f1a4d9a966cbcd1e8c4a3287f60eb3816fa1c22`. The runner verifies it and makes a private copy, not a shared mutable stage slot.

Supervise `studio/agent/target/debug/r6-d-scratch` with arguments:

```text
--companion <repo>/studio/agent/target/debug/gamecore-studio
--cache <exact-versioned-cache>
--evidence <repo>/.evidence/r6-d/<fresh-run>
```

Wait for `R6_D_READY` (the isolated stage has finished and released its Editor), then sequentially run:

```sh
python3 games/hollowmere/Assets/Hollowmere/Tests/R6_D/run-live.py graphical \
  .evidence/r6-d/<fresh-run>/graphical/live-config.json
python3 games/hollowmere/Assets/Hollowmere/Tests/R6_D/run-live.py batch \
  .evidence/r6-d/<fresh-run>/batch/live-config.json
```

Stop only the scratch service after both Editors exit. Every stage/live/suite Editor uses the host allocator; this packet holds at most one Editor. Synthetic test authentication does not establish installed-node UI integration.

## Verification

- Requested exact EditMode filter: **92 passed / 0 failed / 0 skipped**.
- Final expanded EditMode filter (requested filter plus P2.4, R2-B and `P31AdmissionInPlayMode`): **172 passed / 0 failed / 0 skipped**. The admission test really enters Play; its existing compiler double is not confused with the executeMethod real-compiler evidence.
- Existing R6-A-selected Hollowmere `FullQuestHeadless` PlayMode suite: **8 passed / 0 failed / 0 skipped**.
- First two fresh Docker stages: **155863 / 161881 ms**, each **36 EditMode + 2 PlayMode** XML passes; all seven mandatory steps pass and the companion issues and verifies the signed record.
- Original R6-A real batch driver: **43063 / 43576 ms** admission, successful undo both times.
- Companion `cargo fmt --check`, all-target clippy with `-D warnings`, and `cargo test`: **145 passed / 12 ignored**. The ignored cases are existing opt-in integrations; the new scratch runs are reported separately.
- Standalone scratch runner: build, fmt and all-target clippy clean. Both repository policy checkers pass: **42 packages / 91 package assemblies / 1228 C# files**.

Retained XML, admission/undo/GPU witnesses, signed verdicts, service verification receipts and redacted Editor/UPM transcripts are under [`R6_D/Evidence~`](../../../games/hollowmere/Assets/Hollowmere/Tests/R6_D/Evidence~). Files are gzip-compressed; `manifest.json` records SHA-256 of the decompressed bytes. Signed records remain byte-identical, not redacted/re-signed. They are evidence only: the live driver fetched and verified them over the service transport. No signing key or service database is retained there.

Retained setup failures: the first cold compile overlapped creation of the test asmdef and failed on missing test-driver assembly references; after the asmdef existed, 92/92 passed. Standalone rustfmt initially traversed the shared path module with a different style edition; its collateral formatting was restored, and the standalone crate now uses the companion's `style_edition=2024` while retaining Rust 2021. The subsequent companion fmt/clippy/tests pass.

Authority-placement correction: the first two scratch-service trials created private state inside untracked `.evidence`. That did not meet D2's requirement that signing state be outside the checkout. Both stopped service directories were moved wholesale to `~/.cache/gamecore-studio/r6-d/service/` without opening keys. Commit `6d2bdc3f` corrects future creation to an external temporary installation directory. The final qualification below reruns the corrected service; no key was committed or copied into retained evidence.

Final corrected-state qualification at **6d2bdc3f**: job `stg_1a11216f4502e356a61a246`, signed Docker stage **169755 ms**, **36/36 EditMode + 2/2 PlayMode**. Graphical admission wall time **33876 ms** (production timer 33844 ms), undo **23869 ms**. Original R6-A batch admission **42741 ms**, successful undo. Final graphical witnesses again show RTX 4060 Ti / `:1` / non-batch / restored Play / nine coins / 120 Pending→Passed frames. Provider calls remain zero. Early `scratch-node.json` verdict traffic counters are not evidence: they used a path prefix stripped by the shared fixture's logger and therefore read zero. Those unused counters were removed; authority proof is the successful production FetchVerdict/verify and retained signed record, not a fixture count.

## Requests to other packets

None implemented across exclusive ownership. Shared historical packet notes and the verification matrix remain unchanged; this packet's note is the only authorized documentation path.

## Left open

- **Root cause and production fix for the P4.2e 146-second native resolver delay.** All three fresh graphical trials pass unchanged production code. The historical per-process UPM trace needed to distinguish registry/network/server work from native scheduling is absent. These passes cannot prove that the intermittent failure is fixed. The retained driver now preserves an owned UPM log for the next failure, without inspecting global logs from other sessions.
- Consequently no new regression is claimed to fail on the pre-fix compiler and pass after a compiler change. No production source was changed merely to manufacture that claim.
- The creator UI/installed-service path is not rerun: the packet explicitly requires a local signed-stage service and prohibits installed service changes. New evidence qualifies actual graphical compilation/capture/restore/smoke/undo through production admission APIs, not creator clicking or paid candidate generation.
