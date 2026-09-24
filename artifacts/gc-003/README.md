# GC-003 evidence

Status: **NotRun (pending orchestrator build host)** for every dotnet and Unity check. Only the Python checks
ran here, because this authoring host (macOS) has no .NET SDK, no Unity and no Mono.

## What each file is

| File | Produced by | Status |
|---|---|---|
| `HANDOFF.md` | this task | summary, files, exact build/test commands, coverage map, gaps |
| `artifact-hashes.json` | this task | SHA-256 of every artifact GC-003 owns, so the build host can prove it built the reviewed revision |
| `api-surface-diff.log` | executed here | non-comment diff of the frozen W0 seam sources against the production copies; every difference is a pure addition |
| `static-checks.log` | executed here | `python3 tools/check_game_core_csharp.py` (brace balance, forbidden-language-construct scan, engine-reference scan) |
| `validator-self-test.log` | executed here | `python3 tools/validate_game_core_docs.py --self-test` |
| `validator.log` | executed here | `python3 tools/validate_game_core_docs.py` |
| `host-*.log` | executed here | host tool inventory (compiler availability) |
| `verify-generated-catalog.log` | executed here | `python3 tools/verify_generated_catalog.py`: recomputes the committed generated catalog's file hash and catalog fingerprint from its own tables |
| `check-contract-surface-parity.log` | executed here | `python3 tools/check_contract_surface_parity.py`: every enum and enum value in the frozen snapshot exists in the production sources with the same numeric value |
| `review-round-1.md`, `review-round-2.md` | executed here | the defect lists the two independent review rounds produced, and how each item was closed |
| `dotnet-*.log`, `trx/` | build host | `dotnet build`/`dotnet test` output and TRX results |
| `codegen.log`, `build.log`, `player-*.log`, `toolchain/` | build host | Unity codegen, IL2CPP player build and both player probe runs |

## NotRun

Nothing under `artifacts/gc-003/` may claim that a C# compilation, a test run or a player run happened on this
host. The build host owns those files and their results; see `HANDOFF.md` §3 for the exact commands.
