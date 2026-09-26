# GC-022 — native leak detection and attribution

Status: **NotRun (pending orchestrator build host)**. No player and no Editor run has executed here: this host has no
Unity, no IL2CPP toolchain and no C# compiler. The one thing executed while writing this directory is
`python3 tools/attribute_native_leaks.py --self-test` (a pure-Python falsification of the attributor, 16/16 checks)
plus the attribution of the historical GC-016 Editor log recorded below. Neither is a build or a test of the runtime,
and **no leak has been attributed to a frame yet** — the stack-trace run that produces the attributions happens on the
build host.

This directory is the leak half of GC-022: the recipe that makes Unity name the leaked allocations, the policy that
decides which of them are acceptable, and the attributor that turns a log into that verdict. The lifecycle-stress
probe itself lives in `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeLifecycleStress.cs` and is
driven by `tools/unity/run_lifecycle_stress_probe.sh`.

## Files

| File | What it is |
|---|---|
| `README.md` | this run recipe |
| `policy.md` | the resource policy: the rule that makes the leak gate meaningful, and the machine-checkable bound format |
| `fixtures/` | the attributor's in-repo falsification inputs (stack fixtures, a near-miss, a truncated stack, an address-only stack, the 57-allocation summary, a mixed report, a count mismatch, an empty log, three policy fixtures, a baseline signature list) |
| `attribution-gc016-editor-baseline.json` | the starting point, produced here: the historical GC-016 Editor log attributed as `unattributed=1 block / 57 allocations` with the `stack-trace-disabled` hint and exit 2 |
| `player/` | where the harness writes the probe result JSON, the player logs and the attribution JSON (created by the script; empty here because no player run has executed on this host) |

## Enabling leak detection with full stack traces

Unity only prints `Found N leak(s) from callstack:` groups when `NativeLeakDetection.Mode` is
`EnabledWithStackTrace`. Both halves of the toolchain read one environment variable:

* **Editor** — the Collections package ships `Unity.Collections.Editor/CLILeakDetectionSwitcher.cs`, an
  `[InitializeOnLoadMethod]` that reads `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE` (`0` Disabled, `1` Enabled,
  `2` EnabledWithStackTrace) and assigns `Unity.Collections.NativeLeakDetection.Mode`. Every Unity Editor invocation
  in a GC-022 gate must therefore be wrapped with `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2` exported (a whole-gate
  `export`, not a per-command prefix, so no invocation is missed).
* **Player** — `Unity.Collections.NativeLeakDetection` is a public runtime API. The probe applies the same variable
  in-process (or `-probeNativeLeakDetection=<0|1|2>`), logs the resulting `Mode` and records it as the
  `lifecycle-stress-native-leak-detection` step. With no variable and no switch the default behaviour is unchanged:
  the probe only reads and reports the mode. The application is guarded so it cannot throw — a request it cannot
  honour is recorded as a failing step, because leak evidence produced without the requested mode does not attribute
  the allocations it lists.

`tools/unity/run_lifecycle_stress_probe.sh` exports `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE` (default `2`) so the
player inherits it, and skips the attribution step with an explicit NOT RUN message when it is `0` or `1`, since only
`2` produces callstacks.

## Run recipe

```sh
# 0. Build the qualification player (the lifecycle-stress mode is compiled into it like every other mode).
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 \
  UNITY_PROJECT="$PWD/unity/GameCore.Validation" \
  ARTIFACTS=artifacts/gc-022/leak/build \
  tools/unity/build_probe.sh

# 1. Run the stress in the player under leak detection with full stack traces.
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 \
  GC_LIFECYCLE_STRESS_CYCLES=1000 \
  PROBE_RUNS=5 \
  PROBE_PLAYER="$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  UNITY_PROJECT="$PWD/unity/GameCore.Validation" \
  ARTIFACTS=artifacts/gc-022/leak/player \
  tools/unity/run_lifecycle_stress_probe.sh
```

The script runs the probe `PROBE_RUNS` (default 5) times and fails if any run is not clean, then checks the result
JSON for `"task": "GC-022"`, `"mode": "LifecycleStress"`, `"result": "Pass"`, the absence of any `"status": "Fail"`,
all 12 frozen observation names for both families, both digest lines, the resolved cycle count and
`applied=True` on the leak-detection step. Finally it runs the attributor over every run's log.

The player command line the script issues (`probe_run_n` in `tools/unity/probe_runs.sh`), one process per run:

```
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 GC_LIFECYCLE_STRESS_CYCLES=1000 \
timeout --signal=TERM --kill-after=10 600 \
  unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
    -batchmode -nographics -logFile artifacts/gc-022/leak/player/player-lifecycle-stress.log \
    -probeLifecycleStress -probeResult artifacts/gc-022/leak/player/probe-lifecycle-stress.json
```

`-quit` is never passed (the probe exits through `Application.Quit` with the code that encodes its result) and audio
is never re-enabled (crash-139).

## Attributing the allocations (the leak gate)

```sh
python3 tools/attribute_native_leaks.py \
  --log artifacts/gc-022/leak/player/player-lifecycle-stress.log \
  --log artifacts/gc-022/leak/player/player-lifecycle-stress.log.run2 \
  --out artifacts/gc-022/leak/native-leak-attribution.json \
  --policy artifacts/gc-022/leak/policy.md \
  --baseline artifacts/gc-022/leak/attribution-gc016-editor-baseline.json
```

Exit codes: `0` every allocation is Unity-engine/third-party and every distinct signature is bounded by
`policy.md`; `1` a GameCore frame owns an allocation (always a defect); `2` an allocation is unattributed, a signature
has no declared bound, or the policy itself is incomplete. `--baseline` is informational — it reports signatures that
are new relative to an earlier attribution, and never authorizes one.

## The 57-allocation plan

The GC-016 Editor log cannot name those 57 allocations, and this directory records that fact rather than papering
over it:

```sh
# Reproduces the starting point (exit 2, unattributed=1 block, allocations=57, hint stack-trace-disabled).
python3 tools/attribute_native_leaks.py \
  --log artifacts/gc-016/unity/editmode.log \
  --out artifacts/gc-022/leak/attribution-gc016-editor-baseline.json
```

Attribution on the build host, in order:

```sh
# 1. Editor half: the same EditMode fixture that leaked, with the mode armed before Unity starts.
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 \
  timeout --signal=TERM --kill-after=60 1800 \
    "${UNITY}" -batchmode -nographics \
      -projectPath "$PWD/unity/GameCore.Validation" \
      -runTests -testPlatform EditMode \
      -testResults "$PWD/artifacts/gc-022/leak/unity/editmode-results.xml" \
      -logFile "$PWD/artifacts/gc-022/leak/unity/editmode.log"
#    (no -quit; the gate wraps this in the shared timeout and retries once on 124/137 only)

# 2. Attribute the Editor log and the player logs separately; TEST-023 requires them reported separately.
python3 tools/attribute_native_leaks.py \
  --log artifacts/gc-022/leak/unity/editmode.log \
  --policy artifacts/gc-022/leak/policy.md \
  --baseline artifacts/gc-022/leak/attribution-gc016-editor-baseline.json \
  --out artifacts/gc-022/leak/editor-native-leak-attribution.json

# 3. Read every uncovered signature and decide, per signature, which of the three Contract-D outcomes it is:
#    driven to zero (fix), bounded cache (declare capacity + byte ceiling + release path in policy.md),
#    or quarantine (declare the explicit release that drains it).
python3 -c "import json;r=json.load(open('artifacts/gc-022/leak/editor-native-leak-attribution.json'));print('\n'.join(r['policy']['uncovered'] or ['<none>']))"

# 4. Re-run until the gate is clean, and confirm the allocation count itself fell: the goal is zero GameCore
#    allocations plus a stated bound for every remaining one, never a smaller unattributed count.
```

## Falsifiability

`python3 tools/attribute_native_leaks.py --self-test` runs the fixtures through the same CLI as a subprocess and
asserts, among others: a GameCore frame exits 1 even when the policy covers it; a stack whose frames are all Unity is
`unity-engine`; `GameCore` appearing only in a comment or in the `Leak Detected` header prose does **not** classify a
block as GameCore; a truncated stack and an address-only stack are `unattributed` with their allocation counts intact;
the 57-allocation summary is `unattributed=1 / allocations=57` and never zero; an empty log is `blocks=0,
unattributed=0, allocations=0`; a header whose count disagrees with its groups is surfaced and the groups are what is
counted; a missing bound and an incomplete bound both leave a signature unbounded; a missing log file is an error, not
a clean pass.
