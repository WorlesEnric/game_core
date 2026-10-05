# STAGE-TMP2

Branch `codex/stage-tmp2`, based on main `880d26cf`. Linux build host, scratch
service roots only. No installed companion/etosd, Packages, games, worker/provider
operations or credentials were changed. The service fixture supplies synthetic
node authentication and asserts zero operation calls. All real Editors use the
existing host allocator, sequentially (one packet reservation at a time).

Read first: the entire STAGE-TMP and STAGE-INT packets, P2.4 STAGE-INT/STAGE-TMP,
and P0.5 R2-F/R2-F2. `R2-G-host-stage-tooling.md` is absent in this baseline;
its merged instructions are in `studio/stage/PACKET.md`,
`studio/stage/evidence/R2-G-host/README.md` and P2.4's R2-G-host section.

## Implemented

1. `run_child_controlled` passes engine stdout/stderr through the existing
   incremental `copy_redacted` before both the bounded durable sink and flushed
   stdout. The outer 600-second silence watchdog now observes actual engine
   records while it runs. No synthetic heartbeat is emitted. The 64 KiB record
   and 8 MiB per-stream bounds are unchanged.
2. The trusted launcher directory (outside candidate mounts) allocates a monotonic
   attempt number. Container names combine the job-slot hash and attempt; retained
   `unity-stream-aN.log` files never reuse an earlier attempt's filename, including
   across slot reuse. The compatibility `unity-stream.log` is only a latest-result
   summary for the licensing probe. TERM/INT/HUP set cancellation, kill the child
   group and remove that attempt's container before the wrapper returns. Cleanup
   also covers cancellation racing cache/licensing preparation.
3. The versioned cache now retains `analysis-context`, verified against the
   committed Unity metadata manifest. New and reused slots receive fresh copies
   from that source, independently of `project/Library`. Every reference is hashed
   again before analysis, and the slot context is mounted read-only through both
   aliases. Old operator seeds migrate only after all pinned hashes match. Missing
   bytes, modified bytes and ancestor links fail closed with distinct diagnostics;
   the existing `trusted Unity metadata missing` message identifies a missing
   pinned manifest. Warm Library publication never replaces this analysis source.
4. `BEE_BUILD_THREADS=4` bounds Bee's native compiler/ILPP scheduling in addition
   to the existing `DOTNET_PROCESSOR_COUNT=4`. Read-only root, no network, UID/GID,
   cap-drop, no-new-privileges and PID limit 512 remain unchanged. Compiler logs
   use the documented `RoslynCommandLineLogFile` directory mode in a private
   16 MiB tmpfs at `/run/gcs`; the evidence observer redacts before host writes.
   Shared compilation stays enabled, and no host writable path was added.

Regressions: `stage_tmp2_streams_before_exit_and_retains_interrupted_attempts`
uses the actual CLI and observes redacted output before engine exit; signals it
and verifies both retained attempts. The real Docker
`stage_tmp2_timeout_removes_attempt_container_before_next_attempt` forces attempt
1 through the outer timeout, inspects Docker to prove removal, then requires
attempt 2 success without changing attempt 1's bytes. This is an explicit next
invocation: the merged R3-E allocator still retries only its documented ILPP
startup fault, never a generic timeout. That policy was not loosened. Rust metadata and three
Python context tests cover hashes, links, missing manifest and actual make-slot
reuse after deleting Library and analysis inputs. Launcher assertions require
both scheduler limits and existing confinement flags.

Two baseline integration defects were also fixed within scope: the existing Rust
2024 let-chain was rewritten for this branch's Rust 2021 edition; the obsolete
Python blanket denial of `GAMECORE_TEST` now follows R3-E/D25 while still requiring
startup-hook, arbitrary Unity-variable, credential and proxy exclusion.

## Compiler diagnosis

The first diagnostic cold run retained under `before-bee-limit/` failed at Bee's
`posix_spawn` with `Resource temporarily unavailable`. The complete, exact line
is in `diagnostics/before-bee-limit.txt`. The repeat reached the unchanged 512-PID
ceiling: `diagnostics/pid-counters.jsonl` records `pids.events: max 10` and kernel
`pids.peak: 518` (the configured limit remained 512). In-container inspection can
itself fail at the ceiling, so the counter observer reads this packet's cgroup
from the host without consuming a container PID.

Pipe/socket checks came first: Roslyn successfully connected and completed
requests through its short `/tmp/NvYX...` pipe. Runtime paths are job-private.
The actual Unity/Bee/Csc environments retain `DOTNET_PROCESSOR_COUNT=4` and the
short TMPDIR; DOTNET_CLI_HOME is unset and the existing HOME remains a private
licensing copy. There is no logged CLI-home permission failure. Both originally
named Csc targets completed with exit 0 in the first diagnostic run:
`diagnostics/before-bee-csc-completions.json`. Therefore the historical STAGE-TMP
wait is not proven to have one particular cause; the reproduced blocker here is
Bee fan-out exhausting the PID boundary. It is addressed by bounding Bee itself,
not by increasing the boundary or disabling shared compilation.

Roslyn's diagnostic environment and per-process directory logging are documented
in its [compiler-server guide](https://github.com/dotnet/roslyn/blob/main/docs/compilers/Compiler%20Server.md)
and [logger implementation](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Shared/CompilerServerLogger.cs).
The installed Unity Bee binary exposes `BEE_BUILD_THREADS`; live process/cgroup
observations qualify its use here.

## Acceptance and counts

**Default Docker acceptance: PASS.** The scratch companion produced both complete
signed passing StageVerdicts; authenticated fetch and verify returned success.
The acceptance binary used implementation `6e8156f2`. The follow-up cleanup race
hardening (repeat removal while cancellation races preparation) is covered by the
final real-Docker timeout regression; it does not change a successful engine run.
The final fixture copies the CLI and its minimal verified compiler cache into
scratch storage before timing the fake engine. A prior repeat measured busy host
disk binary/cache loading instead of engine execution; that setup defect was
fixed without changing the 10-second forced timeout or production watchdogs.

| Run | durationMs | budgetMs | coldCache | Enforced allowance | Candidate XML |
|---|---:|---:|---|---:|---|
| Cold | 417486 | 360000 | true | 1800000 ms (existing one-time grace) | EditMode 33/33; PlayMode 2/2 |
| Warm | 265617 | 360000 | false | 360000 ms | EditMode 33/33; PlayMode 2/2 |

Both runs also pass 15 Rules tests, the semantic scan with empty findings,
integrity/catalog/determinism checks and all seven verdict steps. XML has zero
failures/skips. The cold duration exceeds the standard budget by 57,486 ms but is
inside the one-time cold allowance; warm is 94,383 ms below its standard budget.
`cold/` and `warm/` retain signed/redacted verdicts, authenticated verification,
XML, engine tails, slot-check results and service jobs. `stage-timings.json`
separately records verdict and full service-job duration (including publication).

Final cgroup observations: candidate cold EditMode peaked at **277 PIDs**, and
all six Unity invocations had **zero PID-limit denials**. The configured limit
remained 512. `diagnostics/pid-summary.json` records each invocation; the exact
successful Csc and ILPP lines for both named targets are in
`diagnostics/final-bee-targets.txt`. No remaining Docker stage blocker is observed.

After both pressure runs, the production Docker analyzer refused the non-installable
`negative-semantic` sample with exit **3**, **21 findings**, all **SG001–SG010**.
The positive pressure sample has an empty findings list. Negative code was never
installed into Unity. Findings and status are retained at the packet root.

| Check | Final count/result |
|---|---|
| `cargo fmt --check`; `cargo clippy --all-targets -- -D warnings` | See clean gate transcripts |
| Ordinary `cargo test` | 125 passed, 0 failed, 10 ignored |
| Explicit Docker isolation, licensing/cleanup and attempt-timeout tests | 3/3 passed |
| Full signed Docker cold/warm service fixture | 1/1 passed, containing both accepted runs |
| Unique executed Rust tests | **129 passed**; six unrelated/live/legacy ignored cases unrun |
| Docker analyzer suite | **50/50 passed** |
| Stage Python tests | **19/19 passed** |
| Slot checker self-tests | **29/29 passed** |
| Checker on both accepted slot snapshots | **2/2 passed** |
| Candidate Rules | **15/15 each run** |
| Candidate Unity | **33 EditMode + 2 PlayMode each run**, 70 total passes |

The initial diagnostic service fixture failed as expected before bounding Bee;
its two refusals are retained under `before-bee-limit/` and are not included among
passing acceptance results. No skipped test is counted as a pass. Reproduction is
in `reproduce.sh`; source revision, counters and counts are in `summary.json`.

