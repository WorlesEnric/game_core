# Headless startup and shutdown

This page covers the runtime entry points, how a headless player is launched, the full player command-line
surface, and how a world is stopped.

**Status of every command on this page: `NotRun (pending orchestrator build host)`** unless it cites an
archived artifact. This host has no Unity and no .NET SDK.

## 1. Production entry points

The V1 application is wired by one bootstrap and driven by exactly one update path. Do not add a second one.

| Entry point | Type | What it does |
| --- | --- | --- |
| `ICustomBootstrap.Initialize` | `GameCore.Unity.Adapters.GameCoreApplicationBootstrap` | Creates the designated gameplay world from the application composition root, assigns `World.DefaultGameObjectInjectionWorld`, installs the single PlayerLoop node, and returns `true` to suppress Unity's default gameplay initialization. |
| PlayerLoop node | `GameCore.Unity.Adapters.GameCorePlayerLoopInstaller` | Idempotently inserts exactly one application-owned pump node (marker type `GameCorePumpLoop`) before `Update.ScriptRunBehaviourUpdate`. It edits the *current* loop recursively and never removes unrelated nodes. |
| Pump | `GameCore.Unity.Adapters.GameCoreApplicationPump` | Pumps registered worlds each frame, including paused/idle pumping. |
| Session reset | `GameCore.Unity.Adapters.GameCoreApplicationReset.ResetForNewSession` | `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`. Invalidates stale loop nodes, disposes surviving hosts and clears static state before the first scene load of every session, because domain-reload-disabled Play Mode does not reset statics. |

Three rules the implementation depends on, all from [04 §3](../game-core/04-unity-integration.md):

- **One update path.** Do **not** also call `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop`, call
  `World.Update()`, or attach `MonoBehaviour.Update`/`FixedUpdate` methods that drive the same world.
- **One world per host.** Each protocol world has one owning `UnityWorldHost` and one `Unity.Entities.World`.
  Multiple protocol worlds need separate hosts and worlds, never global singleton state.
- **No assembly scanning.** The bootstrap never discovers systems by scanning loaded assemblies.

### A composition defect does not produce a default world

If the composition root cannot create the world, the bootstrap falls back to an **infrastructure-only** world
and records the failure (`FallbackCount`, `LastCode`, `LastDetail`) rather than returning `false`. Returning
`false` would let Unity create a default world populated with every auto-created system, which is exactly the
second update path the design forbids. An operator seeing a non-zero `FallbackCount` is looking at a
composition defect, not a silent degradation to a working world.

## 2. Shutting down

| Action | Mechanism | Guarantee |
| --- | --- | --- |
| Stop one world | `IWorldHost.Stop(OperationId, string reason)` — operation **O-19 StopWorld** | C→B: closes all ingress, settles jobs, cancels queued commands, retracts the assembly and reverses dependency resources. Repeated stop joins the same attempt; there is no cancellation once stopping. Stuck users stay pinned and are reported (see [unload-and-leaks.md](unload-and-leaks.md)). |
| Pause/resume | `IWorldHost.SetRunState(OperationId, WorldLifecycleState)` — operation **O-26 SetWorldRunState** | Only `Running ↔ Paused`. A paused duration adds no simulation debt and no steps; commands may queue within capacity while paused. Same state returns `NoChange`. |
| Dispose the host | `UnityWorldHost.Dispose()` | Releases the native storage the host owns. A world becomes `Disposed` only when all its required resources are settled. |
| Process exit | `Application.Quit` via the probe runner (probes) or the engine (normal player) | The quit hook closes the loop route before engine teardown, so the pump cannot step a world whose storage is being released. |

World lifecycle states (`WorldLifecycleState`): `Created`, `Running`, `Paused`, `Stopping`, `Disposed`,
`Faulted`. A `Faulted` world never resumes; recovery builds a **new** world (see
[checkpoint-and-recovery.md](checkpoint-and-recovery.md)).

## 3. Launching a headless player

The player is headless by construction: `-batchmode -nographics`. Audio is disabled in the qualification
project (`AudioManager.asset` → `m_DisableAudio: 1`) because an FMOD/PulseAudio crash at exit was recorded
(crash-139); nothing re-enables it.

The exact launch line, as `tools/unity/probe_runs.sh` builds it:

```sh
timeout --signal=TERM --kill-after=10 600 "${PROBE_PLAYER}" \
  -batchmode \
  -nographics \
  -logFile "${log_file}" \
  ${extra_args[@]+"${extra_args[@]}"} \
  -probeResult "${result_file}"
```

Notes that matter when you write your own harness:

- **`-quit` is deliberately absent.** The probe exits itself through `Application.Quit(report.ExitCode)`, so
  the process exit code *is* the verdict.
- The default qualification player is
  `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64`.
- A run whose exit code is `>= 128` is a **crash, not a verdict**: 139 SIGSEGV, 134 SIGABRT, 135 SIGBUS,
  136 SIGFPE. Probe harnesses fail on a signal death even when a later run is clean.

### Probe exit codes

From `ProbeRunner`:

| Code | Meaning |
| ---: | --- |
| `0` | Every positive probe step passed. |
| `1` | Failure — a step failed, or `-probeResult` was missing. |
| `3` | A negative mode succeeded, i.e. the deliberately invalid case was correctly rejected. |

## 4. The player command-line surface

`-probeResult <path>` is **required** — it is where the structured JSON result is written. Without it the
runner reports a failure and exits `1`.

Exactly one mode flag is passed per run. The full set, from `ProbeArguments`:

| Flag | Purpose |
| --- | --- |
| `-probeResult` | Required. Destination of the structured JSON result. |
| `-probeMissingRegistration` | Negative mode: the deliberately omitted registration must fail with the missing stable ID (exit 3). |
| `-probeWorldDispatch` | GC-005 owned-world mode: guarded dispatch, fail-stop, idle command-driven steps, two worlds, fixed-step debt clock. |
| `-probeW1Gate` | Wave 1 gate: two owned worlds, one admitted operation as a guarded stage, a post-write throw stopping the next stage and publication. |
| `-probeW2Gate` | Wave 2 gate: provider derived and published, one bounded command committed, compiled schedule, future target fully assembled. |
| `-probeW3Gate` | Wave 3 gate: narrative and card compositions each in its own world on one kernel. |
| `-probeNarrative` | The narrative reference slice: chapter providers, derived bindings, one committed choice, chapter-two mount, spawned target. |
| `-probeCards` | The card-market reference slice: mounts, inherited modifier, atomic bounded command, duplicate-transfers-once, rejected settlement. |
| `-probeW4Profile` | Wave 4 provisional generic-execution profile gate. |
| `-probeGc013` | Subtree reparent preserving identity/state, both mode directions, Conservative future spawn, exclusive conflict refused. |
| `-probeW4Gate` | Integrated Wave 4 gate over both families and both catalogs. |
| `-probeFaults` | GC-017: the TEST-016 fault-boundary observations in a stripped player. |
| `-probeGc018` | Checkpoint round trip: capture with explicit queued-command disposition, refusals, restore into a fresh unexposed world. |
| `-probeGc019` | Adapters: stamped input, bounded async asset lease, committed-image presentation, adapter teardown. |
| `-probeW5Gate` | Wave 5 gate join: retained observation, deterministic faults, checkpoint restore, common adapters. |
| `-probeTraversal` | GC-020 fixed-step traversal course with its committed animation/audio output. |
| `-probeGc021` | Durable delivery: key derivation, persist-before-apply, exactly-once redelivery, outbox across unload. |
| `-probeRecovery` | GC-027 checkpoint and recovery: verified-before-use checkpoint, fault refusals, recovery at different native handles. |
| `-probeLifecycleStress` | GC-022 counted mount/unmount cycles, delayed completions, stalled jobs, throwing disposer, under leak detection. |
| `-probeReplay` | GC-023 10,000-step integer replay across worker counts under shuffled completion order. |
| `-probeW6Gate` | Wave 6 gate: traversal with counters and replay, durable reward across unload, three-genre loop. |
| `-probeCatalogCoverage` | GC-025 coverage: reachability manifest vs live catalogs, every generated registration executed, bake/runtime parity. |
| `-probeBenchmark` | GC-026 benchmark fixture and correctness gates (see [deferred-scope.md](deferred-scope.md)). |
| `-probeW7Gate` | Wave 7 exit gate on one merged revision. |
| `-probeRecoverySmoke` | Production `WorldRecovery.Recover` from a real file checkpoint, with no fault latches. **A shipping build keeps this mode.** |
| `-probeConformance` | GC-024: every 07 before/after table in a real world of its genre, plus the combined cross-family world. |

`-probeBenchmark` additionally takes value-bearing sub-arguments parsed by `ProbeBenchmark`, not by
`ProbeArguments`: `-probeBenchmarkWorkload=`, `-probeBenchmarkScopes=`, `-probeBenchmarkTargets=`,
`-probeBenchmarkLiveScopes=`, `-probeBenchmarkLiveTargets=`, `-probeBenchmarkWarmup=`,
`-probeBenchmarkDuration=`, `-probeBenchmarkRepetitions=`, `-probeBenchmarkSeed=`,
`-probeBenchmarkApplyTargets=`, `-probeBenchmarkLiveSpawnTargets=`, `-probeBenchmarkIdleFrames=`,
`-probeBenchmarkUnchangedSteps=`, `-probeBenchmarkRunOrdinal=`.

### Which modes a shipping (marker-free) build keeps

`tools/unity/prepare_gc017_release_project.py` builds a disposable marker-free clone. These modes are
**removed** from it, because they are qualification evidence rather than shipping behaviour:

`-probeFaults`, `-probeW5Gate`, `-probeGc021`, `-probeLifecycleStress`, `-probeReplay`, `-probeW6Gate`,
`-probeW7Gate`, `-probeRecovery`, `-probeConformance`, `-probeBenchmark`.

These are **kept** in both shapes: the GC-001 modes, `-probeWorldDispatch`, the three family probes
(`-probeNarrative`, `-probeCards`, `-probeTraversal`), `-probeGc018`, `-probeGc019`, `-probeCatalogCoverage`
and `-probeRecoverySmoke`.

## 5. Reading a probe result

Each run writes one JSON document containing the mode, a `result` (`Pass`/`Fail`), the engine facts the
player observed (`unityVersion`, `scriptingBackend`, `isIl2Cpp`, `managedStrippingLevel`,
`burstCompilerEnabled`, `catalogFingerprint`, …), and one entry per step with its own `status` and `detail`.

A harness should assert, not sample: strict JSON, the expected `result`, the expected exit code, no step with
`status: Fail`, and at least one step with `status: Pass`. `tools/unity/probe_runs.sh` is the shared helper
that does exactly this and repeats each probe `PROBE_RUNS` times (default **2**, which is the project owner's
cap; the helper rejects any other value). Run 1 writes the retained evidence; runs 2..N write `.<file>.run<N>`
so later runs add crash evidence without overwriting run 1.

## 6. Idle and paused behaviour an operator should expect

- A command-driven world with no work advances **zero** simulation steps and runs **zero** stages. Host
  presentation and pending control-plane operations may still run.
- A paused world retains simulation debt and adds none for the paused duration; on resume the host-time
  sample origin resets so paused elapsed time does not become a catch-up burst.
