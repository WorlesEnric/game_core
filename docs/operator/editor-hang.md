# Known Unity Editor hang: symptoms, diagnosis, retry

The pinned Unity Editor has an **intermittent, unresolved pre-dispatch hang** in batchmode. It is a property
of this Editor on this host, not of Game Core source: the same revision succeeds on a retry with no code
change.

**Treating a hang as a pass would be the failure mode this page exists to prevent.** A hung invocation
produces no test results and no verdict. It is `NotRun`/`Blocked` for that attempt, never a pass.

## 1. Symptoms

- The Editor process starts and then **stops making log progress** for an extended period. A 10-minute
  silence with no new log line is the practical detection threshold used throughout this repository.
- It happens **before dispatch**: the command was accepted but the batchmode entry point never ran.
- Recorded stalls include:
  - an unbounded focused EditMode run hung for **2h49m** immediately after test start with no progress, and
    had to be killed externally (`artifacts/gc-014/BUILD_REPORT.md`);
  - a resolve stalled after `Unloading 75 Unused Serialized files`, with no log progress for 10 minutes
    (`artifacts/gc-025/BUILD_REPORT.md`);
  - a first PlayMode invocation stopped logging before dispatch for ten minutes
    (`artifacts/gc-020/BUILD_REPORT.md`).
- A **benchmark** run of the same shape stalls for a different reason (genuine long computation), so check
  whether progress labels are advancing before concluding "hang". `artifacts/gc-026/BUILD_REPORT.md` records
  a real first-run no-log-progress over 10 minutes that was cost, not a hang.

## 2. What it is not

- Not a Game Core fault: no managed world, job, PlayerLoop callback or message lane is implicated.
- Not reproducible on demand, so it cannot be fixed by a test. Every Unity invocation in this repository
  keeps its watchdog because of it.
- Not something to "wait out" with a longer timeout. A longer timeout turns a fast failure into a slow one.

## 3. Diagnosis: capture a backtrace

`gdb -p PID` is **denied by the host's ptrace policy**:

```
ptrace: Inappropriate ioctl for device
```

`sudo -n gdb` **does** attach and was the route that produced the only captured stacks
(`artifacts/gc-025/BUILD_REPORT.md`):

```sh
sudo -n gdb -p 647610 -batch -ex 'thread apply all bt'
```

The captured result on that occasion:

| Thread | Waiting in |
| --- | --- |
| Main | `GarbageCollectSharedAssets` → `UnloadUnusedAssetsOperation::IntegrateMainThread` → `EditorSceneManager::FinishNewScene` |
| Workers | the dynamic loader **TLS mutex** |

That is consistent with a loader/GC interaction during scene setup, not with anything Game Core does. It is
the best available evidence; the hang remains **unresolved and not source-fixed**.

If `sudo -n` is unavailable in your environment, there is no supported way to get the stack — record that the
backtrace could not be captured rather than guessing at a cause.

### For a **player** crash (a different problem)

A player that reports `Pass` and then dies in native teardown is a different failure with its own tooling:
`tools/unity/capture_crash_139.py` runs the player under `gdb -batch` until SIGSEGV/SIGABRT and retains full
native thread stacks. That is how crash-139 was isolated to Unity's FMOD/PulseAudio mixer thread. See
[§5](#5-related-the-audio-crash-crash-139).

## 4. Retry policy

The repository's policy, implemented in every gate script, is **timeout, then retry exactly once**:

```sh
timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}" "$@" || rc=$?
# rc 124 (timeout) or 137 (killed): retry once.
# any other non-zero exit: a real compile/test error — fail immediately, never retry.
```

| Attempt | Outcome | Result |
| --- | --- | --- |
| 1 | exit `0` | Pass. |
| 1 | exit `124`/`137` | Log the timeout, retry **once**. |
| 2 | exit `0` | Pass, with the first attempt's timeout recorded. |
| 2 | exit `124`/`137` | **Fail**: "timed out twice; this is not the known intermittent pre-dispatch hang". |
| any | other non-zero | **Fail immediately.** A real compile or test error is never retried. |

Defaults: `UNITY_TIMEOUT=1800` seconds for a full Editor invocation, `--kill-after=60` to escalate from TERM
to KILL. Player probes use a 600-second per-run watchdog inside `tools/unity/probe_runs.sh`, and the probe
sequence is retried once on a timeout — never on a probe verdict.

The exact messages, so you can recognize them in a log:

```
   FAIL <step>: Unity Editor timed out after 1800s (exit 124, attempt 1/2)
-- <step>: retrying once after a timeout
   FAIL <step>: Unity Editor timed out twice; this is not the known intermittent pre-dispatch hang
```

### Why "twice" is a failure rather than a waiver

One timeout is consistent with the known hang. Two consecutive timeouts on the same command is a *different*
signal — a genuine deadlock, a resource problem, or a broken command line. Waiving it by retrying until it
passes would convert a real defect into a flaky green.

### Never let a hang pass silently

A watchdog kill is recorded as a timeout, with the log retained, and is **never** recorded as a pass. If a
run's results XML is missing, the suite is `NotRun` for that attempt regardless of how the process exited.

## 5. Related: the audio crash (crash-139)

The headless validation player previously reported `Pass` and then died with SIGSEGV at address `0x20`. It was
reproduced and isolated to Unity's **FMOD/PulseAudio mixer thread** — not to managed code, a GameCore job, the
PlayerLoop, or a message lane.

The fix is a project setting, not a code change: Unity's audio device is disabled in
`unity/GameCore.Validation/ProjectSettings/AudioManager.asset` (`m_DisableAudio: 1`). After the change,
**2,400/2,400** four-way concurrent probe launches exited with their expected codes and JSON verdicts and zero
crashes (`artifacts/crash-139/BUILD_REPORT.md`).

Two cautions, both from that report:

- This is evidence for the fix in this project, **not** a general Unity engine patch.
- **Do not propagate this setting to an audio-enabled product** without diagnosing its audio backend. A
  product that needs audio must have its audio path qualified separately.
