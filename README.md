# GameCore

A genre-neutral game runtime kernel in **C# 9 / .NET Standard 2.1** on **Unity Entities**, with a Unity-free
contract and rules core so the kernel is testable in plain dotnet.

- **Protocol:** 1.0
- **Qualified target:** StandaloneLinux64 x86_64, IL2CPP, Release, High managed stripping, Burst enabled,
  headless. **No other platform is qualified.**
- **Status:** implementation complete against the protocol's required V1 scope, with one owner-deferred
  performance qualification recorded as deferred. See [scope](#scope) below.

## Reproduce from a clean checkout

One entry script takes a fresh clone to a reproduced profile. It checks the toolchain, audits the package
metadata, restores and verifies packages, proves catalog byte-identity, builds and tests the dotnet solution,
runs the Unity EditMode and PlayMode suites, builds the qualification **and** marker-free release players, runs
the three family probes twice in both, and validates the documentation.

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
  tools/reproduce.sh
```

Prerequisites: Unity **6000.0.75f1** (Linux x86_64, with the Linux IL2CPP module), .NET **8** SDK, `python3`,
and GNU `timeout`. `UNITY` is required; everything else has a default. Every step is bounded by a timeout and
fails with a named message. The whole run is transcribed to `artifacts/reproducibility/transcript.log`, with a
machine-readable step ledger in `steps.tsv`.

Full procedure, environment variables and troubleshooting:
[`docs/operator/build-and-run.md`](docs/operator/build-and-run.md).

## Layout

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.*/` | Local Unity packages: the kernel (`contracts`, `composition`, `derivation`, `planning`, `content.compiler`, `rules.*`), the Unity host (`unity.runtime`, `unity.adapters`), the gameplay packages, and the two qualification marker packages. |
| `unity/GameCore.Validation/` | The qualification project: the probe host, the generated catalogs, and the EditMode/PlayMode suites. |
| `dotnet/` | SDK-style projects that build and test the Unity-free assemblies (`dotnet/GameCore.sln`). |
| `tests/` | Pure fixtures and fixture data (benchmarks, recovery, replay, reference conformance, reference seams). |
| `tools/` | Build, gate and check scripts, and the host-side Python checkers. |
| `docs/game-core/` | The normative design set. [`00-core-protocols.md`](docs/game-core/00-core-protocols.md) is the sole normative source. |
| `docs/operator/` | **The operator contract**: profile, build, headless control surface, catalogs, failure codes, recovery, unload, and deferred scope. |
| `artifacts/` | Versioned evidence, gate reports and handoff notes. |

## Operator documentation

Start at [`docs/operator/README.md`](docs/operator/README.md). The pages that matter most:

| Page | Why |
| --- | --- |
| [`profile.md`](docs/operator/profile.md) | The exact qualified profile, and every explicitly unqualified target. Read before making any portability claim. |
| [`failure-codes.md`](docs/operator/failure-codes.md) | **Generated from source**, so it cannot drift: every protocol operation code with its meaning and operator action. |
| [`headless.md`](docs/operator/headless.md) | Production entry points, headless startup/shutdown, and the complete player command-line surface. |
| [`checkpoint-and-recovery.md`](docs/operator/checkpoint-and-recovery.md) | Capture, restore, and how to recover a **faulted** world (which is never resumed). |
| [`unload-and-leaks.md`](docs/operator/unload-and-leaks.md) | Teardown ordering, and how to read the resource/job ledgers for a real leak. |
| [`deferred-scope.md`](docs/operator/deferred-scope.md) | Required V1 vs deferred work, and the owner-deferred performance timing qualification. |

## Developer checks (no Unity required)

```sh
python3 tools/validate_game_core_docs.py --self-test   # the docs validator's own fixtures
python3 tools/validate_game_core_docs.py               # links, anchors, IDs, traceability, DAG, waves
python3 tools/check_package_metadata.py --self-test    # the package/asmdef rules, falsified
python3 tools/check_package_metadata.py                # audit every local package manifest
python3 tools/emit_failure_codes.py --check            # the failure-code table is not stale
python3 tools/check_game_core_csharp.py                # C# shape across the repository
```

## Scope

**Required V1** includes both propagation modes, automatic existing/future descendants, all composition
policies, finite derivation and indexes, state ownership, both temporal models, plugin stages, all lifecycle
transitions, stale-work rejection, safe unload/fail-stop, checkpoint/schema migration, consistent observation,
the three reference families and their combination, generated precompiled mounting, IL2CPP/stripping, and
measured bounded operation.

**Deferred by explicit scope decision** (each adds a large independent mechanism outside those requirements;
no empty mandatory interfaces are created for them): another engine/ECS backend, universal query abstraction,
arbitrary executable-code download or hot replacement, untrusted plugin sandboxing, cross-world or distributed
transactions, generic speculative execution/undo, historical rollback/netcode lockstep, exact cross-engine
physics/animation parity, and workbench/editor/AI UX.

**Deferred by project-owner decision (2026-09-26):** the full-duration TEST-023 **timing** qualification
(five independent 120-second runs, p95/p99 catalogues). TEST-023's **correctness** gates were passed in a short
diagnostic. The measured diagnostic numbers and the open whole-world prepare-cost issue (~0.9–1.1 s per
whole-world change at 10,000 targets against a 100 ms target) are recorded in
[`artifacts/performance/BUDGET_DECISIONS.md`](artifacts/performance/BUDGET_DECISIONS.md). **The deferred timing
rows are never reported as Pass.** Details: [`docs/operator/deferred-scope.md`](docs/operator/deferred-scope.md).

## Documentation authority

[`docs/game-core/00-core-protocols.md`](docs/game-core/00-core-protocols.md) is the sole normative source:
P-001…P-060 requirements and the O-01…O-26 operation catalogue. The other documents in `docs/game-core/` map
those semantics; they cannot change them. Where a mapping and 00 disagree, 00 wins.
