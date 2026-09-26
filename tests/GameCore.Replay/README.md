# GameCore.Replay — replay, differential propagation and telemetry fixtures (GC-023)

The pure half of GC-023: canonical replay hashing with a documented exclusion list, the recorded 10,000-step
integer fixture and its generator, the worker-scheduling model, engine-observation replay separated from
native-physics comparison, differential propagation with a deterministic reducer, the fixed compact telemetry
collector and the raw benchmark trace format.

**Unity-free by design.** Nothing under `Runtime/` references `UnityEngine`/`Unity.*`, so the same sources compile
and run in three places:

| Consumer | How |
| --- | --- |
| Plain dotnet | `dotnet/src/GameCore.Replay` (netstandard2.1) compiles `Runtime/**`; `dotnet/tests/GameCore.Replay.Tests` compiles `Tests/**` and runs them with NUnit 3. |
| Unity EditMode | This folder is a local Unity package (`package.json` + `Runtime/GameCore.Replay.asmdef`), referenced by `unity/GameCore.Validation` and listed in its `testables`, so `Tests/` runs as EditMode tests. |
| IL2CPP player | `GameCore.Validation.ProbeHost`'s `-probeReplay` mode drives the same scenario in the stripped player (`tools/unity/run_replay_probe.sh`). |

## Layout

| Path | Contents |
| --- | --- |
| `Runtime/ReplayHashing.cs` | `ExcludedFields` (the fields a canonical hash may not read), `WorldOrdinalMap` (the comparison-only normalization), `ReplayStateHash` (state/decision/provenance/event/state-table canonical text and hashes). |
| `Runtime/IntegerFixtureGenerator.cs` | The fixture shape, the seeded step-script generator, the fixture composition builder, the integer rule stage, the worker schedule and the LCG. |
| `Runtime/ReplayRunner.cs` | The recorded-trace driver, the per-step result, the run's hashes and the comparison of two runs. |
| `Runtime/TelemetryCollector.cs` | The owner registry, bounded frame retention, the raw benchmark trace format (line-oriented text and a hand-rolled JSON document) and its reader. |
| `Runtime/ObservationReplay.cs` | Recorded engine observations, the integral rule replay, and `NativePhysicsComparison` as a separate result. |
| `Runtime/DifferentialPropagation.cs` | The seeded differential sweep against the real oracle and the deterministic reducer that produces `FailingSeedRecord`s. |
| `Tests/` | The suites: schema facts, fixture determinism, the 10,000-step worker sweep, telemetry/collector/trace behaviour, observation replay and the differential sweep with an injected divergence. |
| `Data/replay-record.json` | The recorded input trace (shape, seed, schedules) the probe and the suites both drive. |

## What is deliberately *not* asserted

No canonical hash is committed as a literal. TEST-022's acceptance for this task is that replays of the same
recorded admitted input agree with each other across worker counts and shuffled producer/completion orders; a
remembered hash would assert only that the code did not change, and would have to be re-recorded on every
unrelated revision. The runs' hashes are recorded in the probe artifact instead
(`artifacts/gc-023/toolchain/probe-replay.json`), and `Data/replay-record.json` carries the one literal that does
belong in the repository: the observation-table digest, which the build host records from its first passing run and
the harness then requires.

## The exclusion list

`ExcludedFields` names what a canonical replay hash must not read: timestamps, padding, diagnostic counters, native
layout, worker scheduling and enumeration order. `ReplayDeterminismTests.ADiagnosticCounterCannotChangeACorrectnessHash`
is the mechanical check that instrumentation cannot move a correctness hash, and `BenchmarkTraceFormatTests` is the
one that every counter the schema reports is visible in the cost trace instead.
