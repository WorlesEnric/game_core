# `artifacts/baseline/` — the GC-025 baseline record

| File | Status | Contents |
| --- | --- | --- |
| `ENVIRONMENT.md` | **Template with `FILL` placeholders** | The exact-versions record P-060 requires. `tools/build_baseline_player.sh` writes the machine-readable facts to `<ARTIFACTS>/host/environment.txt`; this file is where they are transcribed into the archived baseline. |
| `catalog-reachability.json` | **Committed, generated** | The catalog reachability manifest: every generated registration, serializer and closed-generic root of every committed catalog, with its derived key, stable name and owning catalog, plus each catalog's `CatalogFileHash` and `CatalogFingerprint`. Produced by `tools/emit_catalog_reachability.py` from the committed descriptions; re-checked with `--check`. |
| `headless-run-config.md` | **Committed** | The single supported headless player invocation, its flags, its exit-code contract, its result/log locations and the observations the coverage mode asserts. |
| `unsupported-targets.md` | **Committed** | Every target that is explicitly *unqualified* (macOS, Windows, mobile, consoles, ARM64, WebGL, Mono, Editor-only execution) with the reason and what would qualify it. |
| `<ARTIFACTS>/build/**` | **Not committed** (gitignored) | Build output: per-shape `environment.txt`, `codegen-*.log`, `resolve.log`, `build.log`, `player_sha256`, `catalog-ledger.json`, plus `fingerprint-comparison.json` and the two probe results. |

`ARTIFACTS` defaults to `artifacts/baseline/build` for the build script and `artifacts/baseline/probe` for the probe
harness; both are ignored by git (`artifacts/**/raw/` plus the project-local `Builds/` and `Library/` directories).
The four committed files above are the ones a reviewer reads.

## Refreshing the committed files

```sh
python3 tools/emit_catalog_reachability.py            # writes the manifest and its C# companion
python3 tools/emit_catalog_reachability.py --check    # fails when either is stale
python3 tools/emit_baked_catalog_coverage.py --check  # baked coverage artifact
python3 tools/emit_generated_catalog.py --self-check  # every catalog + coverage companion from its description
```

All three are run by `tools/build_baseline_player.sh` before it touches the Editor, so a stale committed artifact
fails the baseline build instead of being rebuilt silently.
