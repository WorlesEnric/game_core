# P2.4 staging lane: evidence

Every run below was made on the Linux build host `myubuntu` (Unity 6000.0.75f1, .NET 8, Rust 1.97.1) in the packet
clone `~/wkspace/gc-studio/p2.4`. Nothing was compiled, built or tested on the Mac. All Unity Editors ran under the
host-wide lock (`studio/tools/unity-batch.sh` or `studio/tools/unity-compile.sh`). Logs were redacted by the runner
and then scanned for etos keys/tickets, `sk-` keys and bearer values; none were found.

## Contents

| Path | What it shows |
|------|---------------|
| `stage-real/` | `cargo test -- --ignored stage_real` (2 passed, 1418 s, including one cold warm-up). Contents: `stage-real.json` (the measured run), and `out/` of the measured slot `stage-real-plate` (verdict, catalog delta, smoke runs, NUnit XML, redacted step logs). |
| `w-mech-01/` | W-MECH-01 end to end (`studio/stage/w-mech-01.sh`): stage, verdict, admit into `games/hollowmere`, Hollowmere PlayMode, undo. Summary in `w-mech-01.json`. |
| `w-mech-01-run1-rollback/` | First live attempt. The re-bake was refused right after the domain reload, so the admission **rolled back for real** (journal `RollbackInterrupted`, package deleted, recompile). Led to the idle-settle fix. |
| `w-mech-01-run3-undo-stale-defines/` | Admit and PlayMode passed. The undo's recompile ran before the Package Manager had re-registered the package list (stale `versionDefines`), so the undo reported `UndoFailed`. Led to the "resolve, then compile" fix. |
| `editmode/hollowmere-p2_4-and-studio-editmode-final.xml` | Final EditMode run on the final code: the 8 P2.4 admission tests plus the 37 studio.core / P1.6 tests, 45/45 passed. |
| `rust/` | `cargo test` (all targets) and `cargo clippy --all-targets -D warnings` transcripts. |
| `checkers/unity-compile-validation.txt`, `editmode/unity-compile-editmode-final.txt` | `unity/GameCore.Validation` batchmode compile (which includes studio.core with `Editor/Stage`): PASS in 110 s. Plus the transcript of the final EditMode run. |
| `checkers/checkers.txt` | Five checks: `check_stage_slot.py --self-test` (14 cases), `check_game_core_csharp.py`, `check_package_metadata.py`, `make-candidate.py --check`, and `check_stage_slot.py` on the W-MECH-01 slot. |

## B-STAGE (measured once)

The measured stage of the pressure plate ran from a fresh slot with the warm Library copied in.

| Step | Status | ms |
|------|--------|---:|
| scan | pass | 12 |
| checkers | pass | 200 |
| dotnet | pass (15 tests) | 4 103 |
| unity-editmode | pass (27 tests: package tests + catalog probe) | 270 706 |
| playmode-smoke | pass (120 frames, 120 sanctioned pumps) | 35 142 |
| determinism | pass (slot hash `5aa1e545c15d…` both runs) | 0 |
| **total** | **pass** | **312 068 of 360 000** |

The cold warm-up stage, which imports the whole slot project once per host and seeds `_warm/Library`, took
745 s under its own 30-minute budget. The failing-test fixture ran under B-STAGE in 317 s and failed at
`unity-editmode` as intended. The forbidden fixture failed at `scan` in 20 ms with `process-start` and
`static-mutable`, and nothing ran after it. The host was shared with other packets' Editors during these runs
(load average 36 to 48).

## W-MECH-01

| Phase | Result |
|-------|--------|
| stage | pass, 73 032 ms. Verdict `sha256:b1ba6aed…` |
| admit | `Admitted`. Live catalog-set hash `1d45d1c9…11d9fb` == predicted. World `425508a9…c899` == verdict world. Mechanism `eabc05e0…3f21` == verdict mechanism. Admission 10.4 s of a 45 s Editor run (compile + domain reload + checkers + re-bake). |
| Hollowmere PlayMode | 1/1 passed: `[W-MECH-01] plate placed in Thornwick Village; pressed after 1 frame(s), released after 1 frame(s); frames=2 sanctionedPumps=2 violations=0 catalogSet=1d45d1c9…` |
| undo | `Undone`. Package removed. Live hash back to `425508a9…c899` (the value before the admission). |

`catalogSet` in the PlayMode log, `predicted` in the verdict and `live` after the admission are the same hash. The
staging slot computed `predicted`, and the admitted live Editor reproduced it.

## Reproduce

```
studio/tools/sync-to-host.sh p2.4
ssh myubuntu 'cd ~/wkspace/gc-studio/p2.4/studio/agent && ~/.cargo/bin/cargo test && ~/.cargo/bin/cargo test --test stage_real -- --ignored --test-threads=1 stage_real'
ssh myubuntu 'cd ~/wkspace/gc-studio/p2.4 && bash studio/stage/w-mech-01.sh /tmp/w-mech-01 --reset-journal'
studio/tools/unity-compile.sh p2.4 games/hollowmere --tests EditMode --filter '(Hollowmere\.P2_4|GameCore\.Studio)\..*'
```
