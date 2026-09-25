# GC-018 build report — checkpoint capture, restore and directed schema migration

**Result: `NotRun (pending orchestrator build host)`.** Nothing in this change set has been compiled, imported or
executed. This file records what was produced, what was checked on the authoring host, and the exact commands and
fixtures the Linux build host must run. It claims no passing test, no passing build, no import and no player run.

## 1. Scope

GC-018 per `docs/game-core/09-implementation-guide.md`: "recreate a complete compatible world using stable data and
an unexposed restore target". Normative surface P-004, P-005, P-032, P-049, P-053, P-054, P-055; 05 §6; 06 §7; suites
TEST-002, TEST-010, TEST-017, TEST-022. Operations O-20 (`CaptureCheckpoint`) and O-21 (`RestoreCheckpoint`).

## 2. Revisions and artifacts

| Item | Value |
| --- | --- |
| Branch | `gc-018` (worktree `/Users/yangcao/wkspace/gc-wt/gc-018`) |
| Fork point | `f28a4af` (Wave 4 integration gate merge) |
| Authoring host | macOS 25.3.0, arm64, Apple M2 — **no Unity, no .NET SDK, no C# compiler, no Mono** |
| Target build host | Linux x86_64 (Ubuntu 24.04), .NET 8 SDK (LangVersion 9 / netstandard2.1), Unity **6000.0.75f1** with Linux IL2CPP |
| Language profile | C# 9, .NET Standard 2.1, `#nullable enable` |
| New assembly surface | `GameCore.Contracts/Serialization/` additions, `GameCore.Unity.Runtime/Pure/Persistence/`, `GameCore.Unity.Runtime/Persistence/` |
| New generated catalog | `GeneratedCheckpoint/CheckpointCatalog.g.cs`, 273157 bytes, 12 schemas, 0 registration groups |
| New probe modes | `-probeGc018` (narrative and card families, both catalogs) |
| New fixture data | `tests/GameCore.CheckpointFixtures/` (migration-path table, queue-policy matrix) |

## 3. Evidence this run must produce (none of it exists yet)

| Artifact | Produced by |
| --- | --- |
| `artifacts/gc-018/trx/*.trx` | `dotnet test dotnet/GameCore.sln` |
| `artifacts/gc-018/unity/gc018-editmode.xml`, `.log` | Unity EditMode run, `-testFilter GameCore.Gc018.Tests` |
| `artifacts/gc-018/unity/envoy*`/`resolve.log`, `checkpoint-codegen.log`, `catalog-codegen.log` | Unity batchmode import and both catalog generators |
| `artifacts/gc-018/toolchain/build.log`, `codegen.log`, `environment.txt` | `tools/unity/build_probe.sh` |
| `artifacts/gc-018/toolchain/probe-gc018.json`, `player-gc018.log` (+ `.run2`..`.run5`) | `PROBE_RUNS=5 tools/unity/run_gc018_probe.sh` |
| `artifacts/gc-018/static-checks.log`, `validator.log` | `python3 tools/check_game_core_csharp.py`, `python3 tools/validate_game_core_docs.py` |

Gate for this task (09 §GC-018 DoD): "Actual Unity-world save/recreate/restore passes for both early genres,
including dormant slots, using the existing committed boundary and controlled fixture failures." That is exactly what
the 16 observations × 2 families × 2 catalogs assert, and it is unproven until the player probe passes.

## 4. What ran on the authoring host

| Check | Command | Result |
| --- | --- | --- |
| C# static smoke check | `python3 tools/check_game_core_csharp.py` | `checked 378 C# file(s)` → `ok` |
| Generated catalog verification | `python3 tools/verify_generated_catalog.py unity/.../GeneratedCheckpoint/CheckpointCatalog.g.cs` | `0 groups, 12 factories, 12 schemas, 1 features, 273157 bytes`; file hash and fingerprint recompute correctly |
| Existing catalogs unchanged | `python3 tools/verify_generated_catalog.py [probe] [cards]` + `git diff --exit-code` on both `.g.cs` | both still match their recorded hashes; no diff |
| Emitter idempotence | `python3 tools/emit_checkpoint_catalog.py` then `git diff --exit-code` | `unchanged`; no diff |
| Shell syntax | `bash -n tools/unity/run_gc018_probe.sh` | clean |
| `.meta` GUID uniqueness | worktree-wide scan | zero duplicates |
| Digest literals | independent recomputation from the observation-name table | both reproduced |
| Record/field parity | description-derived field order vs each record type's constructor order, all 12 schemas | 0 mismatches |

**None of the above is a build, an import, a unit test or a player run**, and none of it may be read as evidence that
GC-018 works.

## 5. Expected first failures, in order

1. A name or signature mismatch between the new Unity scenario and the frozen runtime types.
2. The generated serializers' `Serialize`/`TryDeserialize` method groups binding to `Func<TValue, byte[]>` and
   `CheckpointDeserialize<TValue>` in `CheckpointCodecAdapter` — only a compiler can confirm this.
3. The "different native entity indices" observation, if two world creations in one run happen to reuse entity
   indices. The scenario must assert the *recorded source* indices differ from the restored world's; a legitimate
   collision must be reported, not weakened away.
4. Clause fragments grepped by `run_gc018_probe.sh` that only a fully passing run emits.

## 6. Command sequences (not yet run)

See `/artifacts/gc-018/HANDOFF.md` §10 for the exact `dotnet build`/`dotnet test`, Unity batchmode, and player-probe
command lines, including the two catalog-generation steps and the EditMode filter.

## 7. Status vocabulary used in this report

`NotRun` = the check has not been executed on a toolchain that can execute it. This report uses only `NotRun` plus
the authoring-host checks of §4, which are labelled as checks, not results.
