# GC-022 Play Mode reload matrix

**Status: `NotRun (pending orchestrator build host)`.** This host has no Unity and no .NET toolchain, so nothing in
this folder has been produced by a real run yet. This note names the command that produces it and the files it
leaves behind.

## What it proves

Four reload settings, each in its own Unity Editor process invocation, ten real Play Mode enter/exit cycles per
setting. At `EnteredPlayMode` every cycle must show exactly one registry world, exactly one application loop route,
no bootstrap fallback, a `Running` host with a **fresh `WorldId`** (the stale-static-state proof across a
reload-disabled boundary) and exactly one `GameCoreApplicationReset` for the session. At `EnteredEditMode` nothing
may survive: zero registry worlds, zero installed loop nodes, and — when domain reload was disabled, so the host
reference is still reachable — `Disposed`, no ECS storage, no outstanding job and no retained resource. A leaked
subscription or a surviving loop node fails the combination.

The combinations are selected by a command-line argument so an intermittent Editor hang in one of them cannot lose
the other three.

| Combination | `EditorSettings.enterPlayModeOptionsEnabled` | `enterPlayModeOptions` | Domain reload | Scene reload |
| --- | --- | --- | --- | --- |
| `reload-on-scene-on` | `false` | `None` | on | on |
| `reload-on-scene-off` | `true` | `DisableSceneReload` | on | off |
| `reload-off-scene-on` | `true` | `DisableDomainReload` | off | on |
| `reload-off-scene-off` | `true` | `DisableDomainReload \| DisableSceneReload` | off | off |

The Editor's previous settings are captured before anything is changed and restored on every exit path, so the
qualification project's own enter-play configuration is left unchanged.

## Command

The runner needs only `UNITY` (the pinned Unity 6000.0.75f1 Editor, executable). Everything else has a default.

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  tools/unity/run_lifecycle_playmode_matrix.sh
```

Optional environment: `UNITY_PROJECT` (default `<repo>/unity/GameCore.Validation`), `ARTIFACTS`
(default `<repo>/artifacts/gc-022/playmode-matrix`), `UNITY_TIMEOUT` (default `1800` seconds per invocation),
`MATRIX_COMBINATIONS` (default all four, space separated), `MATRIX_CYCLES` (default `10`).

The runner prints, and records in `commands.txt`, the exact command it executes for each combination. The same
invocation written out by hand:

```sh
"$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -executeMethod GameCore.Validation.Editor.LifecyclePlayModeMatrix.Run \
  -gc022Combination reload-on-scene-on \
  -gc022Cycles 10 \
  -gc022OutputDirectory "$PWD/artifacts/gc-022/playmode-matrix" \
  -logFile "$PWD/artifacts/gc-022/playmode-matrix/reload-on-scene-on.log"
```

Substitute the other three combination names for the other three invocations. The same combination can also be
selected with the `GC022_COMBINATION` environment variable (`GC022_CYCLES`, `GC022_OUTPUT_DIRECTORY` mirror the
other two arguments); the runner sets both paths, so a stripped argument cannot silently run the wrong combination.
`-quit` is never passed — the Editor exits itself through `EditorApplication.Exit` with a code that encodes its
result — and no audio flag is passed, because the project already disables the audio device
(`ProjectSettings/AudioManager.asset`, `m_DisableAudio: 1`).

Every invocation runs under `timeout --signal=TERM --kill-after=60 $UNITY_TIMEOUT`, and a combination is retried
exactly once, only on exit 124 or 137.

## Result codes

| Code | Meaning |
| --- | --- |
| 0 | Every requested combination `Pass`ed. |
| 1 | A combination failed an assertion, or its evidence is missing/invalid. |
| 2 | A combination was killed by the watchdog after its single retry (and nothing failed an assertion). |
| 64 | Usage or configuration error (missing `UNITY`, unknown combination, bad cycle count, missing tool). |

The Editor exits 0 on success and 1 on an assertion failure; the watchdog's 124/137 is mapped to the runner's 2.

## Files produced per run

Canonical names below replace `<c>` with the combination name.

| File | Content |
| --- | --- |
| `<c>.log` | The Unity Editor log of the authoritative attempt. |
| `<c>.jsonl` | One `cycle-begin` line per cycle, written *before* the session starts, and one `cycle-result` line per cycle, written after it ends with the assertion outcome. Appended and flushed line by line, so a killed run's last line names the combination and the cycle index it died at. |
| `<c>-summary.json` | The Editor's own summary: `result` (`Pass`, `Fail`), `cyclesCompleted`, `cyclesAttempted`, `cyclesRequested`, `combinationsCompleted`, `wallClockSecondsPerCombination`, plus `failureCount`/`detail`. |
| `<c>.timeout` | Marker written when the authoritative (last) attempt was killed by the watchdog. |
| `<c>.attempt1.log`, `<c>.attempt1.jsonl`, `<c>-summary.attempt1.json`, `<c>.attempt1.timeout` | The first attempt's evidence, preserved under explicit names when the watchdog killed it and the single retry ran. |
| `<c>-summary.editor.json` | An Editor summary that a runner verdict contradicted, moved aside rather than discarded. |
| `matrix-summary.json` | The runner's aggregate over all requested combinations: `result`, `exitCode`, `cyclesCompleted`, `cyclesAttempted`, `cyclesRequested`, `combinationsCompleted`, `wallClockSecondsPerCombination` (per combination), `watchdogKills`, and a per-combination array. |
| `commands.txt` | Every Unity command line the run executed, with the environment that accompanied it. |

## The Editor hang is recorded as a frequency, never as a pass

The unresolved intermittent pre-dispatch hang (`artifacts/gc-014/BUILD_REPORT.md` §Fixes item 5) is expected to have
its best chance of reproducing here. It is handled as data:

- A killed attempt writes a `*.timeout` marker (`<c>.timeout` for the authoritative attempt, `<c>.attempt1.timeout`
  for a killed first attempt), and the killed process's last `cycle-begin` line names the combination and the cycle
  it died at.
- If a killed invocation produced no `cycle-begin` line at all, the hang landed before the Editor reached the
  matrix (the pre-dispatch case); the Unity log and the marker are then the whole record, and the runner's
  `<c>-summary.json` (written because the Editor could not write its own) reports `"result": "Timeout"` with
  `"cyclesAttempted": 0`.
- A combination that only passed on the sanctioned retry is reported as `Pass` with the hang counted in
  `watchdogKills` and named in the table's `note` column — it is never reported as an unqualified clean pass.
- A combination still incomplete after its retry makes the runner exit 2 (`result: Timeout`), never 0.

The `result` in `matrix-summary.json` is `Pass` only when every requested combination completed all
`MATRIX_CYCLES` cycles with every assertion holding.
