# GC-003 evidence

Status: **Pass on the Linux build host**. Release solution build: zero errors/warnings; all 126 dotnet tests
passed. The IL2CPP player passed eight positive probes and the negative mode exited 3 as required.
Real compiler catalog generation was byte-identical across runs and matched the committed catalog.
See [BUILD_REPORT.md](BUILD_REPORT.md) for exact commands, fixes, counts, retained warnings and scope limits.

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

## Evidence provenance

`HANDOFF.md`, `host-tools.log`, the review logs and `artifact-hashes.json` preserve the original authoring-host
record. Their `NotRun` statements describe that earlier host, not the completed Linux runs.
`BUILD_REPORT.md`, `commands.json`, `suite-results.json`, `catalog-reproducibility.json`, `trx/` and `toolchain/`
record the executed build-host results. Initial failure logs are retained separately from final passing logs.
