# Catalog generation and byte-identity

Game Core's registration catalog is **generated, committed, and proven byte-identical on every build**. This
page is the operator procedure for regenerating it and for proving the committed bytes are what the pinned
Editor produces.

**Status: `NotRun (pending orchestrator build host)`.** This host has no Unity. The commands are the ones
`tools/run_w7_gate.sh` and `tools/unity/build_probe.sh` execute.

## 1. Why byte-identity is the test

The generated catalogs are the closed registration surface that IL2CPP and High managed stripping are
allowed to keep. If generation is not deterministic, a build can strip a type the previous build kept, and
the failure surfaces as a missing registration at runtime — or worse, as a silently different catalog.

So the rule is not "the catalog compiles". It is: **regenerating from the same description produces exactly
the committed bytes**. `git diff --exit-code` is the assertion.

## 2. The four committed catalogs

| Catalog | Description document | Generated tree | Codegen entry point |
| --- | --- | --- | --- |
| `ProbeCatalog` (narrative + fixture registrations) | `unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json` | `Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` | `GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog` |
| `CardCatalog` | `Catalogs/CardCatalog.catalog.json` | `Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs` | `GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog` |
| `CheckpointCatalog` | `Catalogs/CheckpointCatalog.catalog.json` | `Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs` | `GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog` |
| `TraversalCatalog` | `Catalogs/TraversalCatalog.catalog.json` | `Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs` | `GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog` |

A fifth step bakes the catalog-coverage authoring document
(`Catalogs/CatalogCoverageAuthoring.json`) in place:
`GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake`.

Each generated tree also carries a `*Coverage.g.cs` file (for example
`Generated/ProbeCatalogCoverage.g.cs`), and the bake step writes
`Generated/CatalogCoverageBaked.g.cs` from `Catalogs/CatalogCoverageAuthoring.json`. Those files are inside
the byte-identity diff below, so they are held to the same determinism rule. The bake validates the authored
document against the traversal package's declared vocabulary before writing anything, and writes
deterministically (fixed order, LF endings, no timestamps, no machine paths).

The committed description documents and generated `.g.cs` files both live in the repository. The generated
assemblies are `GameCore.Validation.Generated`, `.GeneratedCards`, `.GeneratedCheckpoint`,
`.GeneratedTraversal`.

## 3. Regenerating (the exact commands)

Each step is one Editor invocation: `-batchmode -nographics -quit -projectPath <project> -executeMethod <method>`.
Run from the repository root, with `UNITY` set to the pinned Editor.

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
PROJECT=unity/GameCore.Validation
GEN=unity/GameCore.Validation/Assets/GameCore.Validation

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile artifacts/reproducibility/codegen-probe.log

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog \
  -logFile artifacts/reproducibility/codegen-cards.log

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog \
  -logFile artifacts/reproducibility/codegen-checkpoint.log

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.TraversalCatalogGenerator.GenerateCatalog \
  -logFile artifacts/reproducibility/codegen-traversal.log

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake \
  -logFile artifacts/reproducibility/codegen-bake.log
```

## 4. Proving byte-identity

```sh
git diff --exit-code -- \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint \
  unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal
```

A non-zero exit means the committed catalogs are **stale** or generation is **non-deterministic**. Those are
different problems and the diff tells you which:

- A diff in every run, with different content each time → **non-determinism**. Treat as a defect in
  generation (an unordered enumeration reaching the emitter is the usual cause). Do not commit the output.
- A diff that is stable across runs → the committed bytes are **stale** relative to a changed description.
  Commit the regenerated output as part of the change that altered the description.

`tools/reproduce.sh` runs this step and fails the whole run on a diff, so a clean checkout cannot report
success with stale catalogs.

## 5. Verifying a generated file without Unity

`tools/verify_generated_catalog.py` recomputes a generated file's `CatalogFileHash` and
`CatalogFingerprint` from the file itself, so host-side checks can assert catalog integrity with no Editor:

```sh
python3 tools/verify_generated_catalog.py \
  unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
# … likewise GeneratedCards/CardCatalog.g.cs, GeneratedCheckpoint/CheckpointCatalog.g.cs,
#          GeneratedTraversal/TraversalCatalog.g.cs
```

Two hashes, two meanings:

- `CatalogFileHash` covers the generated file prefix.
- `CatalogFingerprint` covers the declarations the runtime catalog actually builds (P-028).

Both are recorded per catalog in [`artifacts/baseline/ENVIRONMENT.md`](../../artifacts/baseline/ENVIRONMENT.md),
and `tools/compare_registration_fingerprints.py` compares two builds' ledgers — that is how a build is proven
to have produced the same registration surface as the baseline, rather than merely produced *a* catalog.

## 6. Related host-side checks (no Unity required)

```sh
python3 tools/emit_generated_catalog.py --self-check        # the emitter's own mirror
python3 tools/emit_catalog_reachability.py --check          # committed reachability manifest
python3 tools/emit_baked_catalog_coverage.py --check        # the baked coverage artifact
python3 tools/compare_registration_fingerprints.py --self-test
```

These are the halves of the codegen contract that can be falsified without an Editor, and `tools/reproduce.sh`
runs them.
