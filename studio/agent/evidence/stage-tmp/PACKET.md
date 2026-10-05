# STAGE-TMP

Branch `codex/stage-tmp`, based on `fd038534`, Linux build host.

The default Docker launcher now uses `TMPDIR=XDG_RUNTIME_DIR=/tmp/gcs/<8 hex>`.
That mode-0700 directory lives in the job's private `/tmp` bind, never host `/tmp`.
`XDG_CACHE_HOME` and `UPM_CACHE_ROOT` use short children backed by the disposable
licensing HOME. Each Unity invocation copies the manifest-verified offline UPM
cache into that private HOME; changes cannot propagate to another invocation.
The private HOME is deleted on return, and the job owns the remaining runtime files.

Unity is launched as `/u/Unity`, with the project mounted at `/w/p`, result paths
under `/w/s/out`, and NuGet at `/c/nuget`. Host aliases remain available for the
trusted analyzer's absolute metadata paths and the slot's absolute package pins.
The host allocator still sees and owns the original host project/results paths.
The short project and runtime mounts reject symlink escapes. Unity argument paths
above 90 bytes fail closed; the regression uses an owner/slot path longer than 108
bytes and checks every launcher-provided engine/environment path against 90 bytes.
A real Docker regression also binds a Bee-sized Unix socket under the short runtime.

Read-only root, cap-drop=ALL, no-new-privileges, network none, fixed UID/GID,
512 PID limit, private licensing copy, host allocation, and pre-launch verification
of pinned NuGet archives/expanded inputs and offline UPM records remain enabled.
No installed companion, etosd, Packages or games files are changed. Authentication
comes only from the existing fake-node test fixture; no worker/provider operations.

## Reproduction

See `reproduce.sh`. The new ignored `tests/stage_tmp.rs` runs the real companion
against a scratch node under `/tmp/gamecore-stage-tmp-service`, with a
private binary and owner cache. It retains cold and subsequent pressure runs,
requires authenticated verdict fetch/verify for passing stages, and requires 404
for any refused authority. The standard budget remains 360,000 ms; the existing
one-time cold import allowance remains separately represented by `coldCache`.
All Unity launches are sequential through the existing host-wide allocator.

`negative-semantic` is explicitly a scan-only, non-installable fixture. The existing
`studio/stage/analyzer/offline-check.sh` runs its refusal through the production
Docker launcher, after 50 analyzer tests and an empty-findings pressure scan.
`negative-findings.json` retains all ten expected SG001–SG010 IDs.

## Final acceptance: BLOCKED

Launcher implementation: `8d27c8e5`. Both final pressure requests used that source
revision, the production CLI, default Docker and synthetic scratch-node authentication.
The mandatory Unity probe **passes**. The separate R2-F2 Docker licensing/cleanup test
also passes. `docker-path-proof.json` is a live inspection of the candidate Editor:
maximum launcher-provided path **27 bytes**, project `/w/p`, executable `/u/Unity`,
network none, read-only root, cap-drop ALL, no-new-privileges and PID limit 512.

The cold candidate passed scan (52 files), checkers (14 C# files / 6 asmdefs) and
semantic analysis plus **15/15 Rules tests**. Unity entered Bee/ILPP compilation;
`bee-progress.json` includes successful copying of `GameCore.Gameplay.World.Editor.dll`.
There were no observed compiler diagnostics or AF_UNIX path exceptions. Two Csc clients
remained waiting for the compiler server for `Hollowmere.Mechanism.PressurePlate.Generated.dll`
and `GameCore.Content.Compiler.dll`; the root cause of that wait is not established.

The next observable launcher blocker is at `src/stage/sandbox.rs:608–612`: the wrapper
waits for `sandbox.run` to finish before printing the engine stream. Consequently the
outer allocator's first attempt log stays empty. `studio/stage/run-redacted.py:73`
triggers its unchanged **600 s silence watchdog**. The interrupted wrapper does not
reach its normal container cleanup; `src/stage/sandbox.rs:495` removes the container
only after the whole allocator batch returns. The immediate second attempt therefore
collides with the first container. `src/stage/pipeline.rs:967` refuses missing XML.
Full lines are retained at `cold/engine-log-tails.txt:82,92,98`:

```text
Unity Editor timed out (limit 1500s, silence limit 600s; exit 124, attempt 1/2)
RESULT editmode: FAIL (unity exit 125, 604s, attempts 2, ...)
docker: Error response from daemon: Conflict. The container name "/gc-stage-251f522baf2123a3afd62fc9885375c3d8c9d41d15b6075836425d5496a99a62" is already in use by container "c5c7168be74ed954836ce74d14f86620520ddeec41816bca635e30a4f63cf909".
```

The original first-attempt engine stream is overwritten by the retry in the existing
wrapper. Its observed Bee progress and live Docker configuration were captured before
that happened; final allocator/engine tails are retained. No candidate EditMode or
PlayMode XML was produced, and no signed passing verdict was issued. Both authenticated
verdict fetches returned **404** (`cold/issuance.json`, `warm/issuance.json`). The retained
`verdict.json` files are **unsigned failing StageVerdicts**, not passing authority.

The repeat request also fails closed at `src/stage/semantic.rs:100`: the reused,
partially imported Library lacks five pinned metadata files, first
`ScriptAssemblies/Unity.Core.Editor.dll`. `studio/stage/make-slot.py:496` only seeds a
new Library; this reused slot does not recover the pinned analysis inputs.
`warm-missing-metadata.json` lists all five. The exact log is:

```text
trusted semantic context unavailable: trusted Unity metadata missing: No such file or directory (os error 2)
```

The follow-up work is continuous redacted engine streaming and attempt-scoped container
termination/cleanup, then diagnosis of any remaining Csc wait. A retry also needs a
manifest-verified analysis context independent of an incomplete mutable Unity Library.
No watchdog, log bound, metadata check, package exclusion or confinement policy was
relaxed to turn these failures into a pass.

| Attempt | durationMs | budgetMs | coldCache | Enforced allowance | Outcome |
|---|---:|---:|---|---:|---|
| Cold | 841857 | 360000 | true | 1800000 ms, existing one-time cold grace | EditMode failed; no XML |
| Repeat, intended warm | 12582 | 360000 | true | 360000 ms | Trusted metadata missing |

No completed cold import seeded a warm cache, so **no warm B-STAGE measurement exists**.
`stage-timings.json` preserves the actual flags and separates service durations from
verdict durations. Fixture copy/build/queue history is not reported as B-STAGE work.

## Checks and counts

- `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`: pass.
- Ordinary `cargo test`: **117 passed, 0 failed, 8 ignored**. Three new unit regressions
  cover path lengths, private cache lifetime and symlink escapes.
- Explicit ignored Docker isolation (including a real Unix socket bind) and offline
  licensing/cleanup: **2/2 passed**, making **119 executed Rust passes**.
- Explicit full Docker service acceptance: **1 blocked test case**, containing the
  two final pressure attempts above. Five other ignored live/legacy cases stay unrun.
- Docker analyzer suite **50/50**, stage Python **16/16**, slot-checker self-tests
  **29/29**. All **three** actual slot checks pass (independent prepared slot plus both
  final service slots). The independent pressure slot has 52 files / 9 inputs.
- Cold candidate Rules **15/15**; candidate Unity XML test counts unavailable (zero XMLs).
- Post-stage `negative-semantic` scan: production Docker exit **3**, **21 findings**,
  all **10 expected IDs SG001–SG010** (`negative-stage-status.json`).

## Earlier attempts and scratch handling

`attempt-1/` retains two allocator-only pressure refusals (211965 / 221105 ms) and a
licensing probe that acquired a reservation late and hit the existing outer deadline.
A still earlier host-disk cache copy was interrupted before any companion or Editor
started (`docker-stage-cache-copy-interrupted.txt`). The final service root is
`/tmp/gamecore-stage-tmp-service/node-04JtqZ`; the cache was privately copied on NVMe.
The test-only `STAGE_TMP_CACHE` option accepts an existing operator-provisioned cache
as the source of another private copy (or pinned NuGet provisioning for licensing).
`retry-cache-path.txt` records this run's source. Production requests cannot select it.

Only one packet-owned Unity reservation was held at a time. The final licensing probe
was queued after the full pressure harness exited. No other Editor/service was stopped,
no installed companion/etosd state was accessed, and the fake node observed zero ops.
