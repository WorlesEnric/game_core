# GC-025 handoff — qualify the complete standalone IL2CPP and headless profile (Wave 7)

Branch `gc-025` (worktree `/Users/yangcao/wkspace/gc-wt/gc-025`), starting from `main` = `700c3a9` (Wave 6
integration gate).

**Status of every Unity/dotnet step this change set adds: `NotRun (pending orchestrator build host)`.** This host has
no Unity, no .NET SDK and no mono, so nothing here has been compiled, imported or executed in Unity. What *did* run
on this host is listed in §9 and is only host-side checkers, generators and shell parsing. None of it is a build, an
import, a Unity test or a player run.

## 1. Summary

GC-025's sentence is "Build the complete selected target with locked dependencies, Burst and High managed stripping.
Exercise every generated factory/serializer/closed generic root, bake/runtime recipe parity, inactive plugin late
mount, stop/restart and headless reference execution. Compare generated registration fingerprints across builds."

What this branch adds, in the order the gate proves it:

1. **A fourth committed generated catalog.** The traversal genre had only a hand-written generated-style table
   (`TraversalCatalogTable`), and every earlier gate reported `generatedCatalog=absent` for it. GC-025 emits
   `TraversalCatalog.g.cs` through the *production* emitter from a new committed description, and proves in the
   player that the generated catalog fingerprints identically to the hand-written table
   (`f66d97d489bc1d8b891b20bc1c16186e0843d2ca5760dcd010909feea6829459` for both).
2. **Generated coverage companions, from a production emitter.** For every committed catalog the content compiler
   now also emits a `<Class>Coverage.g.cs`, compiled into the *same assembly as the catalog*: it resolves every
   registration through the table's own generated lookup method, writes/validates/re-reads **every** declared schema
   through its generated serializer (13 checkpoint schemas included) and executes every closed-generic root
   statement. A registration or serializer UnityLinker dropped with High stripping therefore fails in the player,
   not only in the Editor. `CatalogEmitter.EmitCoverage` is the production emitter, `CatalogGenerator.GenerateFromText`
   writes the companion and `CatalogGenerator.VerifyCoverageFromFiles` re-emits it from the description and fails on
   a stale or hand-edited file; the four Editor bridges call that verification in `VerifyCatalog` (a shared `shared:`
   commit — see §4).
3. **A catalog reachability manifest** generated from all four descriptions, committed twice: as compile-time C#
   data (`Runtime/CatalogReachability.g.cs`) the player compares its live tables against, and as
   `artifacts/baseline/catalog-reachability.json` for the build host.
4. **`-probeCatalogCoverage`**, a new player mode whose 13 named observations cover the manifest, every mandatory
   root executed by key, the generated traversal catalog's parity, the inactive plugin late mount, editor-baked vs
   runtime-recipe parity, refused unknown/stale recipes, two independent world hosts under different session salts,
   and the headless run against the pure-rule canonical fixtures.
5. **The bake side of TEST-020.** A committed authoring document, an Editor bake step that validates it against the
   traversal package's own vocabulary and writes a compiled artifact, and a player-side materialization of the
   course's recipe catalog from that artifact — compared against the runtime-declared catalog on definition, base
   layout, descriptor tag, applier registration, recipe-catalog fingerprint, run digest and canonical numbers.
6. **Baseline build and comparison tooling**: `tools/build_baseline_player.sh` (both shapes, headless runs,
   fingerprint comparison, environment capture), `tools/compare_registration_fingerprints.py`,
   `tools/check_link_xml.py`, `tools/run_gc025_gate.sh`, `tools/unity/run_catalog_coverage_probe.sh`.
7. **The baseline record**: `artifacts/baseline/{README.md,ENVIRONMENT.md,headless-run-config.md,unsupported-targets.md}`
   — the ENVIRONMENT template for the build host, and every unsupported target (macOS, Windows, mobile, consoles,
   ARM64, WebGL, Mono, Editor-only execution) documented as explicitly unqualified.

## 2. Files created

### Catalogs and generation

| Path | Contents |
| --- | --- |
| `unity/GameCore.Validation/Catalogs/TraversalCatalog.catalog.json` | The fourth catalog description: the traversal course plugin factory, its five compiled system factories, the registered `traversal.reducer.vec3i-sum` and `traversal.predicate.always`, and the one configured schema `traversal.schema.course-config`. Same nine registrations and one schema the hand-written table declares, so the two fingerprints must agree. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs` (+ `.meta`) | The emitted catalog. Byte-identical to `CatalogEmitter`'s output; `tools/verify_generated_catalog.py` recomputes its file hash and fingerprint from its own tables. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalogCoverage.g.cs` (+ `.meta`) | The generated coverage companion (8 registrations, 1 schema). |
| `unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/GameCore.Validation.GeneratedTraversal.asmdef` (+ `GeneratedTraversal.meta`, `.asmdef.meta`) | Assembly for the traversal catalog; references Contracts, Rules.Traversal, Gameplay.Traversal only. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalogCoverage.g.cs`, `GeneratedCards/CardCatalogCoverage.g.cs`, `GeneratedCheckpoint/CheckpointCatalogCoverage.g.cs` (+ metas) | The other three coverage companions: 3, 8 and 0 registrations and 1, 1 and 13 schemas. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/TraversalCatalogGenerator.cs` (+ `.meta`) | The Editor bridge for the fourth catalog, in the same shape as `CardCatalogGenerator` (`-executeMethod …TraversalCatalogGenerator.GenerateCatalog`). |

### Bake (TEST-020's editor half)

| Path | Contents |
| --- | --- |
| `unity/GameCore.Validation/Catalogs/CatalogCoverageAuthoring.json` | The authored runner target: recipe/applier stable names, the selector schemas, the descriptor tag and the seeded position/velocity (0 mm, 1000 mm/s along x). |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/BakeCatalogCoverageAuthoring.cs` (+ `.meta`) | The bake step. It validates the authored document against the traversal vocabulary (`TraversalVocabulary.RunnerRecipe`, `TraversalVocabulary.AccelerationTarget`, `TraversalVocabulary.SeededVelocityMilli`), reads it with a small self-contained JSON reader (Editor-only, no dependency on the catalog description reader), and writes the artifact deterministically. `-executeMethod …BakeCatalogCoverageAuthoring.Bake`; `Verify()` re-checks a committed artifact. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/CatalogCoverageBaked.g.cs` (+ `.meta`) | The baked artifact: stable names and authored values only — every 128-bit identity is derived at the player's use site with the production rule, so the artifact carries no CLR name, no engine handle and no precomputed identity (P-004). |

### Player coverage

| Path | Contents |
| --- | --- |
| `Runtime/CatalogReachability.g.cs` (+ `.meta`) | The generated manifest as compiled data: per-catalog facts (class name, file hash, fingerprint, group/schema/registration/root counts) and one row per registration, serializer and closed-generic root with its catalogue label, kind, role, stable name and derived key. |
| `Runtime/CatalogCoverageScenario.cs` (+ `.meta`) | The sequence: 13 named observations, one digest over them, and every helper (`TryLiveFacts`, `TryResolve`, `CanonicalFragments`, `Fragment`, `RegistryUnwound` replacement, `TryTraversalCatalog`). No reflection, no `System.Type`, no Editor-only API. |
| `Runtime/CatalogCoverageRecipeSource.cs` (+ `.meta`) | `ICatalogCoverageRecipeSource` with two implementations: `RuntimeCatalogCoverageRecipeSource` (the fixture's own declared recipes) and `BakedCatalogCoverageRecipeSource` + `BakedRunnerApplier` (the same recipes materialized from the baked artifact, with a second `ISpawnApplier` implementation registered under the same generated key). |
| `Runtime/CatalogCoverageProbe.cs` (+ `.meta`) | The `-probeCatalogCoverage` mode: one probe step per observation plus the digest step, with the frozen literal. |

### Tests

| Path | Contents |
| --- | --- |
| `Tests/CatalogCoverage/GameCore.CatalogCoverage.Tests.asmdef` (+ `.meta`) | EditMode test assembly, `UNITY_INCLUDE_TESTS`-constrained, nunit precompiled reference. |
| `Tests/CatalogCoverage/CatalogCoverageIntegrationTests.cs` (+ `.meta`, folder `.meta`) | One `[Test]` per observation plus a table test that recomputes the digest from `CatalogCoverageScenario.ObservationNames`, and a test that the probe mode freezes the same literal. `[OneTimeSetUp]` carries `[Timeout(1800000)]` for the intermittent pre-dispatch hang. |

### Tooling

| Path | Contents |
| --- | --- |
| `tools/emit_generated_catalog.py` | No-SDK mirror of `CatalogEmitter` (validation, canonical ordering, the exact `Emit` template, the hash/fingerprint scopes, and `EmitCoverage`). `--self-check` reproduces all four committed catalogs *and* their coverage companions byte for byte, so it is the independent check of the production emitter's output on a host with no compiler. |
| `tools/emit_catalog_reachability.py` | Emits the manifest as C# and as JSON from the four descriptions; `--check` fails on a stale artifact. |
| `tools/emit_baked_catalog_coverage.py` | No-SDK mirror of the Editor baker's template and validation, so the committed baked artifact can be produced and re-checked here; `--check` fails on drift. |
| `tools/compare_registration_fingerprints.py` | Compares two builds (Unity project directory or `catalog-ledger.json`), plus the committed manifest as a third participant. `--ledger`/`--ledger-out` write a build's ledger; `--self-test` proves the comparison is falsifiable (identical pair passes, a one-byte change is reported *and named*). |
| `tools/check_link_xml.py` | Enforces "no link.xml preserve-all for kernel/gameplay assemblies": an allow-list of the two fixture assemblies and the six `GameCore.Unity.Adapters` PlayerLoop types, and a prefix denylist for kernel/rules/gameplay/compiler/runtime assemblies. |
| `tools/build_baseline_player.sh` | The final baseline build: host facts, host-side freshness checks, Editor codegen + `git diff` reproducibility, `link.xml` (both projects), the qualification build and coverage probe, the release clone check + build + coverage probe, the two ledgers and the fingerprint comparison. |
| `tools/run_gc025_gate.sh` | The gate: host checks → dotnet → resolve → EditMode → PlayMode → codegen reproducibility → qualification player → `-probeCatalogCoverage` → release shape + fingerprint comparison + release coverage probe → docs. Every Editor invocation `timeout`-wrapped with exactly one retry on `124`/`137`. |
| `tools/unity/run_catalog_coverage_probe.sh` | The probe harness on `probe_runs.sh`: 13 required steps, the digest literal, 32 acceptance-clause fragments, `PROBE_SHAPE=release` for the release-shape evidence names. |

### Evidence

| Path | Contents |
| --- | --- |
| `artifacts/baseline/catalog-reachability.json` | The committed manifest ledger. |
| `artifacts/baseline/ENVIRONMENT.md` | The exact-versions template (`FILL` placeholders) plus the four catalogs' recorded hashes/fingerprints. |
| `artifacts/baseline/headless-run-config.md` | The one supported headless invocation, its flags, its exit-code contract, the audio/crash-139 rule, the result paths and the observations the mode asserts. |
| `artifacts/baseline/unsupported-targets.md` | Every explicitly unqualified target with the reason and what would qualify it. |
| `artifacts/baseline/README.md` | What is committed, what is build output, and how to refresh the committed artifacts. |
| `artifacts/gc-025/HANDOFF.md` | This file. |

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `Runtime/ProbeArguments.cs` | Nine additive edit sites for the new mode: the flag const, the ctor parameter, the assignment, the property (+ doc comment), the `IsProbeInvocation` term, the parse local, the parse branch, the ctor call (now two lines). | One mode flag per invocation; the existing modes are untouched. |
| `Runtime/ProbeRunner.cs` | Two additive arms: the dispatch arm and the report identity (`Named("CatalogCoverage", "GC-025")`). | Same shape as every other mode. |
| `Runtime/GameCore.Validation.ProbeHost.asmdef` | Added the `GameCore.Validation.GeneratedTraversal` reference (sorted insertion). | The probe host now names the generated traversal catalog. |
| `Runtime/Gc020TraversalHost.cs` | **`shared:` (see §4).** `CourseFamily` gained two additive constructors: one taking an `ICatalogCoverageRecipeSource`, one taking a source and an explicit session salt; the field type widened `TraversalRunnerApplier` → `ITraversalRunnerApplier`; `SessionSalt` became a property rather than a constant expression; `RecipeSourceLabel` added. Existing callers use the 3-argument constructor and the 4-argument one exactly as before. | The parity comparison needs a second materialization of the same course, and the restart observation needs two provably different session identities. |
| `Packages/com.gamecore.gameplay.traversal/Fixtures/Runtime/TraversalCourseWorld.cs` | **Fixture-package change.** Added `ITraversalRunnerApplier : ISpawnApplier`; `TraversalRunnerApplier` implements it; `TraversalCourseRecipes.Runner/DisplayRunner/Catalog` take the interface instead of the sealed class (a widening, so every existing caller compiles unchanged). | A recipe factory must accept the second materialization's applier. Widening a parameter type is source-compatible; nothing else changed. |
| `tools/unity/prepare_gc017_release_project.py` | **`shared:`** the `ProbeArguments` constructor-call needle and its replacement now match the two-line call the new mode produces. | The clone's argument list must keep agreeing with the constructor signature; the script's exact-once needle design caught this immediately (§9). |
| `tools/check_release_clone.py` | **`shared:`** `KEPT_MODES` gained `('CatalogCoverage', 'catalogCoverage')`. | The clone deliberately keeps this mode (see §4), so the clone check must verify its wiring survives. |
| `tools/emit_catalog_reachability.py`, `artifacts/baseline/*` | Regenerated/refreshed by their own tools. | — |
| `Packages/com.gamecore.content.compiler/Runtime/Description/CatalogEmitter.cs` | **`shared:`** additive: the `CoverageFileSuffix` const, `EmitCoverage(CatalogDescription)` and its private helpers. No existing member changed. | The coverage companion needed a production emitter; without one the committed companions had no producer the build host could run, and their "generated" claim rested on a mirror alone. |
| `Packages/com.gamecore.content.compiler/Runtime/Description/CatalogGenerator.cs` | **`shared:`** additive except that `GenerateFromText` now writes one more file: `CatalogFileSuffix`, `CoveragePathFor`, companion writing, and `VerifyCoverageFromFiles`. `Verify` is unchanged (catalog only). | The Editor codegen step must write and verify the companion, or "the committed file is the Editor's own output" would be false for it. |
| `unity/…/Editor/{Probe,Card,Checkpoint,Traversal}CatalogGenerator.cs` | **`shared:`** each `VerifyCatalog` now also verifies the coverage companion. | A stale companion fails the Editor step rather than reaching a player. |

## 4. Contract changes and shared-file changes

**Contract changes (`GameCore.Contracts`, plan DTOs): none.** No `Packages/com.gamecore.contracts` file changed, no
plan DTO changed, and `tools/check_contract_surface_parity.py` still passes against the frozen W0 snapshot.

**Shared-file changes**, each minimal and additive, each in its own commit:

1. `Packages/com.gamecore.gameplay.traversal/Fixtures/Runtime/TraversalCourseWorld.cs` — a new interface and a
   parameter-type widening in a *fixture* package (the traversal fixture is test-only qualification surface; the
   gameplay and rules packages are untouched). No kernel file, no gameplay `Runtime/` file and no production
   assembly changed. Consumer count is one (`Gc020TraversalHost`). **No earlier gate needs rerunning for this**: the
   interface is additive and every existing call site keeps its exact static type resolution; the traversal
   behaviours are unchanged, which is what the GC-020/W6 gates assert and what this task's own
   `catalog-coverage-bake-runtime-parity` observation re-asserts by running the whole course twice.
2. `unity/…/Runtime/Gc020TraversalHost.cs` — additive constructors and one property. `Gc020TraversalHost.SessionSalt`
   is unchanged as a value, so GC-020's and the Wave 6 gate's run identities are byte-identical.
3. `tools/unity/prepare_gc017_release_project.py` and `tools/check_release_clone.py` — the release-shape tooling.
   GC-025 adds no new qualification marker package and no new stripped fixture; these two edits keep their
   exact-once needle and kept-mode invariants true for the new mode.
4. `Packages/com.gamecore.content.compiler/{CatalogEmitter,CatalogGenerator}.cs` and the four Editor bridges —
   **generic**, genre-free (no genre type or name reaches the compiler) and additive except that a generation now
   also writes the companion file. This is the build-time content compiler, not the runtime kernel: no
   `GameCore.Contracts`, `Composition`, `Derivation`, `Planning` or `Unity.Runtime` file changed, and no emitted
   *catalog* byte changed, so no earlier gate's frozen digest, file hash or fingerprint moves. GC-003's and GC-018's
   codegen steps now also write and verify one more file each, which is strictly more checking rather than different
   output; §9 records the byte-level proof that the four catalogs are untouched.

**No kernel semantic change.** Nothing in `GameCore.Contracts`, `Composition`, `Derivation`, `Planning` or
`Unity.Runtime` (the kernel pipeline and hosts) changed, so no earlier wave gate needs rerunning on this account.

## 5. Requirement / test coverage map

| Requirement / test | Where implemented | Where observed |
| --- | --- | --- |
| P-002 participants and authority | the sequence creates, drives and disposes its own worlds; two hosts under two salts | `catalog-coverage-world-stop-restart` |
| P-004 stable identities, fresh session | `BakedRunnerApplier.Key` derives from a stable name; the baked artifact stores stable names only; two salts give two sessions | `catalog-coverage-late-mount-inactive-plugin`, `catalog-coverage-bake-runtime-parity`, `catalog-coverage-world-stop-restart` |
| P-005 runtime handles | the two started hosts never share an identity | `catalog-coverage-world-stop-restart` (`session=`/`registryBeforeCreate=` fragments) |
| P-008 stable ordering and determinism | the digest is over the observation-name table; both run digests and canonical fragments must match | the digest step, `TheDigestIsExactlyTheFrozenLiteral`, `catalog-coverage-bake-runtime-parity` |
| P-009 manifest / registration | the reachability manifest is built from the descriptions and compared against the live tables | `catalog-reachability-manifest`, `catalog-coverage-registration-lookup` |
| P-015 eligibility / descriptors | the baked and runtime recipes must agree on the descriptor's schemas and tags | `catalog-coverage-bake-runtime-parity` |
| P-024 spawn recipes | recipe resolution, its `StalePlan` and `MissingDependency` refusals | `catalog-coverage-unknown-recipe-refused` |
| P-028 catalog/version compatibility | generated fingerprints (per catalog, per build, and generated-vs-hand-written for traversal) | `tools/verify_generated_catalog.py` ×4, `catalog-coverage-traversal-generated-catalog`, `tools/compare_registration_fingerprints.py` |
| P-035 lifecycle | create → step → stop → dispose, twice, with the registry returned to its pre-create size | `catalog-coverage-world-stop-restart` |
| P-054 serialization | every declared schema of every catalog round-trips through its generated serializer in the player | the four `catalog-coverage-*-catalog` observations (16 schemas total) |
| P-055 protocol evolution | every catalog's declared features are exercised by its generated serializers' header gate | as above (the probe catalog and the checkpoint catalog both declare one feature) |
| P-058 V1 implementation profile | IL2CPP + High stripping + Burst in both build shapes; generated registration/serializer/generic-root coverage in the player | `tools/build_baseline_player.sh`, `-probeCatalogCoverage` in both shapes |
| P-060 evidence and release status | environment capture, ledgers, fingerprint comparison, `artifacts/baseline/*` | `tools/build_baseline_player.sh`; `ENVIRONMENT.md` |
| TEST-001 toolchain, generated registration, IL2CPP | generated registrations, a closed generic handler, a query, a Burst job and a serialization round trip in the player; a linked-but-inactive plugin; an explicit negative | the four coverage companions, `catalog-coverage-late-mount-inactive-plugin`, `catalog-coverage-closed-generic-roots`, plus GC-001's existing probe modes |
| TEST-017 checkpoints and schema evolution | the checkpoint catalog's 13 serializers execute in the player | `catalog-coverage-checkpoint-catalog` |
| TEST-018 Unity worlds and headless | headless actual-Entities execution; a stopped world host leaves nothing registered | `catalog-coverage-world-stop-restart`, `catalog-coverage-headless-canonical` |
| TEST-020 baking, runtime recipes, precompiled plugins | equivalent targets through editor baking and the runtime recipe catalog; a precompiled plugin mounted in the player; unknown/incompatible recipe revisions refused | `catalog-coverage-bake-runtime-parity`, `catalog-coverage-late-mount-inactive-plugin`, `catalog-coverage-unknown-recipe-refused` |
| "Unsupported target platforms remain explicitly unqualified" | — | `artifacts/baseline/unsupported-targets.md` |

## 6. What the coverage probe asserts, exactly

`CatalogCoverageScenario.ObservationNames`, 13 observations, digest over the names
`77bc74196ec96b076bcc63b007cc0f57f5322121bad69f36c0e280d0576fbbb2`:

| # | Observation | Assertion |
| --- | --- | --- |
| 1 | `catalog-reachability-manifest` | every manifest catalog's live class name, `CatalogFileHash`, `CatalogFingerprint` (recorded *and* freshly computed), group count, schema count, registration count and root count match the committed manifest |
| 2–5 | `catalog-coverage-{probe,cards,checkpoint,traversal}-catalog` | the catalog builds, its coverage companion resolves every registration, round-trips every schema and executes every root, with counts equal to the manifest |
| 6 | `catalog-coverage-registration-lookup` | every manifest registration **and** serializer root resolves by its derived key through the live generated lookup |
| 7 | `catalog-coverage-closed-generic-roots` | every manifest root statement is a closed-generic root of the probe catalog and the generated root method executes |
| 8 | `catalog-coverage-traversal-generated-catalog` | the hand-written table's literals still derive from their stable names; the generated catalog builds; the generated fingerprint equals the hand-written table's; the plugin, five systems, reducer and predicate all resolve |
| 9 | `catalog-coverage-late-mount-inactive-plugin` | the fixture plugin was instantiated 0 times before the late mount, 1 after, its closed handler returns the expected value, and the card table and traversal course plugin factories resolve by key from their own generated catalogs |
| 10 | `catalog-coverage-bake-runtime-parity` | the baked and runtime materializations agree on definition, base layout, descriptor tag, applier key and recipe-catalog fingerprint, and both course runs pass with equal digests and equal canonical fragments |
| 11 | `catalog-coverage-unknown-recipe-refused` | an undeclared recipe is `MissingDependency`, the same recipe at revision+1 is `StalePlan`, and both counters are 1 |
| 12 | `catalog-coverage-world-stop-restart` | two course runs under two salts both pass, have different `session=` values, and report the same pre-create registry size with equal digests and canonical fragments |
| 13 | `catalog-coverage-headless-canonical` | the run passes, its digest equals the frozen traversal literal, and the canonical velocity/pose/provider/step/catch-up values are the pure vocabulary's, with the arithmetic (1000 + 2000·20/1000 = 1040, 1040 − 1000·20/1000 = 1020, pose 20 mm) re-derived independently |

## 7. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run on this host.

### 7.1 One command (the whole gate)

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
PROBE_RUNS=5 \
  tools/run_gc025_gate.sh
```

In order: host-side checkers (catalog verifier ×4, emitter self-check, manifest/bake freshness, fingerprint
self-test, `link.xml` ×2, C# checker, contract parity, gate sources, `bash -n`); `dotnet build`/`test`; Unity
resolve; EditMode; PlayMode; codegen for four catalogs plus the bake and `git diff --exit-code`; the qualification
player; `-probeCatalogCoverage`; the release clone check, build, coverage probe and fingerprint comparison; the
documentation validator.

### 7.2 The baseline build alone (both shapes plus the environment capture)

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
PROBE_RUNS=5 \
SHAPES=qualification,release \
  tools/build_baseline_player.sh
```

Write the confirmed values into a copy of `artifacts/baseline/ENVIRONMENT.md` from
`artifacts/baseline/build/host/environment.txt`, the two per-shape `environment.txt` files, the two
`catalog-ledger.json` files and `fingerprint-comparison.json`.

### 7.3 The coverage probe alone

```sh
PROBE_PLAYER=$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
UNITY_PROJECT=$PWD/unity/GameCore.Validation \
ARTIFACTS=$PWD/artifacts/gc-025/probe \
PROBE_RUNS=5 \
  tools/unity/run_catalog_coverage_probe.sh
```

### 7.4 The GC-025 EditMode suite alone

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.CatalogCoverage.Tests \
  -testResults artifacts/gc-025/unity/catalog-coverage-editmode.xml \
  -logFile artifacts/gc-025/unity/catalog-coverage-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10).

### 7.5 Two independent clean builds compared by hand

```sh
python3 tools/compare_registration_fingerprints.py --ledger <build-a-project> --ledger-out /tmp/a
python3 tools/compare_registration_fingerprints.py --ledger <build-b-project> --ledger-out /tmp/b
python3 tools/compare_registration_fingerprints.py --a /tmp/a --b /tmp/b --json /tmp/verdict.json
```

### 7.6 The two shape-specific checks that used to be GC-017's runbook

```sh
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --clone unity/GameCore.ReleaseCheck --json artifacts/gc-025/release/clone.json
python3 tools/check_link_xml.py --project unity/GameCore.ReleaseCheck --json artifacts/gc-025/release/link-xml.json
```

## 8. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed.** The highest-risk item is `CatalogCoverageScenario.cs` (~1 180 lines of
   hand-written Unity-free-plus-Unity code calling 40-odd external members). Every external call site and every
   asserted detail fragment was audited against its declaration (§9), but only a compiler settles it. Second is the
   generated coverage companions' `read.Field == literal` comparisons, which depend on each schema's field wire
   types.
2. **`docs/game-core` is ambiguous about "bake" and this reading is recorded rather than assumed.** 04 §6 says
   baking "converts referenced assets/prefabs into entity prefabs, immutable blobs, and typed recipe metadata" and
   that the player "instantiates baked entities or invokes precompiled factories". The repository has **no**
   `Baker<T>`, no subscene and no `GameObject` authoring asset anywhere (a deliberate design: registration is data
   plus direct typed appliers). GC-025 therefore bakes **the authored target definition** into a committed artifact
   and materializes the same `SpawnRecipe` from it, rather than inventing a parallel GameObject/subscene pipeline
   that no other package uses. The parity claim is correspondingly exact — both materializations are compared on
   definition, base layout, descriptor tag, applier registration, recipe-catalog fingerprint, run digest and
   canonical numbers — but it is a *definition-level* bake, not a `GameObject`-prefab bake. If the orchestrator
   wants a real prefab/subscene bake, that is new content-pipeline work, not a change to this comparison.
3. **The release clone deliberately keeps the coverage mode.** Every other qualification-only probe is stripped
   from `unity/GameCore.ReleaseCheck`; this one is kept, because the shipping surface is exactly where "every
   mandatory catalog entry survives High stripping" has to be proved, and `tools/build_baseline_player.sh` runs it
   in both shapes. Consequently `tools/check_release_clone.py` gained a `KEPT_MODES` entry (a shared `shared:` edit)
   and `tools/check_release_gate_free.py` was left unchanged: its marker groups and its `-probeTraversal` kept-mode
   assertion are unaffected, and the release-shape coverage run is asserted by the probe harness itself
   (`"mode": "CatalogCoverage"`, `"task": "GC-025"`, `"result": "Pass"`). The new `Runtime/CatalogReachability.g.cs`,
   `CatalogCoverageScenario.cs`, `CatalogCoverageRecipeSource.cs` and `CatalogCoverageProbe.cs` therefore survive
   into the clone; none references a removed type, which is what the clone check verifies.
4. **`Gc020TraversalHost.CourseFamily` now has three constructors.** The 3-argument one is what every existing gate
   calls and its behaviour is unchanged (same session salt, same recipe source). The 4- and 5-argument ones are
   additive. Recorded because it is a shared validation fixture, not a task-local one.
5. **`W6GateScenario` is deliberately *not* referenced by the new files.** An earlier draft of the restart
   observation reused `W6GateScenario.RunReloadRoute`; the clone check immediately failed the clone with
   `FAIL …/CatalogCoverageScenario.cs`, because `W6GateScenario` is one of the types the release shape removes.
   The route-based implementation was replaced with two `Gc020Scenario.Run` calls under two session salts, and the
   additive `RunReloadRoute` overload that draft needed was reverted, so `W6GateScenario.cs` is byte-identical to
   `main`. This is the single most useful thing the release-clone checker did on this task.
6. **The traversal catalog is emitted here, and the earlier "no generated traversal catalog" statements are now
   stale.** `Gc020TraversalHost.GeneratedCatalogPresent` is still `false` and its comments still say the emission
   "is GC-025's catalog-coverage work" (which is now done), and `Gc020Scenario`'s and `W6GateScenario`'s details
   still report `generatedCatalog=absent`, and `tools/unity/run_traversal_probe.sh` still requires that fragment.
   Those files are owned by GC-020 and the Wave 6 gate; changing them would change their frozen digests and their
   harness expectations. GC-025 therefore proves the generated catalog's existence and parity in **its own**
   observation, and records the staleness here rather than editing another task's frozen evidence. **Recommendation
   for the orchestrator:** a follow-up in the GC-020/W6-owned files can switch those runs to the generated catalog
   and update the fragment, which would then require those gates' digests to be recomputed. This task deliberately
   does not do that.
7. **Five full GC-020 course runs in one probe invocation** (two for parity, two for the restart, one for the
   canonical comparison), each creating and disposing a real world with a local `PhysicsScene`. The per-run player
   timeout is 600 s in `probe_runs.sh`; the sequence adds one to two orders of magnitude less work than the GC-022
   stress mode that runs in the same player, so no timeout change was made. If it does time out on the build host,
   raise the harness's `PROBE_RUNS`-independent timeout in `probe_runs.sh` rather than trimming observations.
8. **The manifest's "every mandatory entry" is a set over the four catalogs**, and the coverage companions iterate
   the *live* tables while the manifest rows are matched by key. A registration present in the live table but
   missing from the manifest is caught by the fingerprint/count comparison (observation 1), not by a per-row name
   comparison; a registration missing from the live table is caught by the lookup (observation 6) and by the
   companion's own count. Recorded because it is a deliberate division of labour rather than an omission.
9. **`artifacts/baseline/ENVIRONMENT.md` still contains `FILL` placeholders.** That is by design — it is the
   template the build host fills — and a `FILL` that survives into an archived report means the baseline was not
   recorded. The four `CatalogFileHash`/`CatalogFingerprint` literals in it *are* committed values and were verified
   against the generated files.
10. **Inventory rows (proposed, not promoted).** P-058 and P-060 move from Partial toward Implemented on the
    strength of `artifacts/baseline/` and the coverage probe **once the gate has run**; TEST-001 and TEST-020's
    player clauses move the same way. The orchestrator promotes rows it has run.
11. **No performance, budget or platform claim.** GC-026 owns the measured budgets, no non-Linux target was built,
    and nothing here asserts a timing.

## 9. What actually ran on this host

Host-side only; none of it is a build, an import, a Unity test or a player run.

```sh
python3 tools/emit_generated_catalog.py --self-check
#   ok: all four committed catalogs AND their four coverage companions reproduce byte for byte from their
#   descriptions (25 321 + 27 893 + 308 815 + 26 084 bytes)
python3 tools/verify_generated_catalog.py <each of the four generated catalogs>
#   3 groups/4 factories/1 schema; 5/9/1; 0/13/13; 4/9/1 — file hash and fingerprint both recompute
python3 tools/emit_catalog_reachability.py && python3 tools/emit_catalog_reachability.py --check
#   4 catalogs, 19 registrations, 16 serializers, 2 closed-generic roots; reproduces
python3 tools/emit_baked_catalog_coverage.py --check
#   the baked artifact reproduces from Catalogs/CatalogCoverageAuthoring.json
python3 tools/compare_registration_fingerprints.py --self-test
#   identical builds agree; a changed fingerprint is reported and named (2 problems): OK
python3 tools/compare_registration_fingerprints.py --a unity/GameCore.Validation --b unity/GameCore.Validation
#   all three participants (two project reads + the committed manifest) agree;
#   combined=f776f27d633346bc3030f9350949c61d4432cda449193e9d95c142cb175797f2
python3 tools/check_link_xml.py
#   PASS: 3 assemblies, 2 assembly-level preserves (both fixture assemblies), 6 PlayerLoop type preserves,
#   no kernel/gameplay assembly preserved
#   falsifiability self-test: injecting <assembly fullname="GameCore.Rules.Narrative" preserve="all" /> made it
#   FAIL with 2 named problems; restoring the file returned it to PASS
python3 tools/unity/prepare_gc017_release_project.py && python3 tools/check_release_clone.py --clone unity/GameCore.ReleaseCheck
#   VERDICT: clone is clean — including the new kept mode and the new files, after the two shared edits
python3 tools/check_game_core_csharp.py                 # checked 555 C# file(s); ok
python3 tools/check_contract_surface_parity.py          # frozen W0 surface parity
python3 tools/check_gate_sources.py --file <the 9 new/changed C# files>
#   balance ok; unresolved members: none; unreachable types: none; ambiguous names: none
python3 tools/validate_game_core_docs.py --self-test    # 9 isolated fixtures passed
python3 tools/validate_game_core_docs.py                # 14 documents, links, anchors, IDs, traceability, DAG
bash -n tools/run_gc025_gate.sh tools/build_baseline_player.sh tools/unity/run_catalog_coverage_probe.sh
#   clean
python3 tools/run_gc025_gate.sh  (UNITY=<stub>, DOCS=0)
#   every host-side step of the gate executed and passed; the Unity steps fail at the stub, as expected here
# .meta GUID uniqueness over the whole worktree: 835 metas, 835 unique, 0 duplicates
```

The production `EmitCoverage` transcription was verified as follows: `git diff --stat` shows a pure addition to
`CatalogEmitter.cs` (323 insertions, 0 deletions; two additive hunks at lines 27 and 57 and one at 891), the ten
review findings the gate-source checker reports for that file are **identical in count and content to `HEAD`'s own
24** (all of them pre-existing false positives for its nested `GeneratedEntry.Entry`/`GeneratedSchema.SchemaId`
members, in code this change does not touch), and every generated catalog is byte-unchanged: `tools/verify_generated_catalog.py`
recomputes all four file hashes and fingerprints, `tools/emit_generated_catalog.py --self-check` still reports
"reproduces byte for byte", and `git status` shows no generated catalog as modified after the compiler commit.

Five further findings from the second (tooling) review were fixed, none of them compile-blocking:

* `tools/build_baseline_player.sh` no longer writes the fingerprint comparison as
  `run_step … || echo NOT RUN`, which turned a genuine mismatch (exit 1) into a passing step. Absence of one shape's
  ledger is now the only NOT RUN case, and a comparison that runs and disagrees fails the step. Proved with a
  synthetic mismatching pair: exit 1 and `"status": "Fail"`.
* `tools/run_gc025_gate.sh` now runs `bash -n` once per script, because `bash -n a b c` parses only its first
  operand, so two new scripts had escaped the gate's only syntax check.
* `tools/emit_generated_catalog.py`'s validation rules were aligned with the production reader where they had
  drifted: canonical stable names now allow `_` and enforce the 200-character cap (matching
  `StableNameKeyDerivation.IsCanonicalStableName`), version `0` is rejected on every version field (matching
  `RequiredUInt32`), code fragments accept ASCII identifier characters only (the C# rule is ASCII-only), `fileName`
  follows `ValidateFileName`, duplicate `schemaId`s are rejected, and declared feature ids are emitted in canonical
  identity order (the reader sorts them before the emitter sees them). Each rule was proved falsifiable against the
  traversal description; none of them changes a committed byte (`--self-check` still passes for all four catalogs and
  all four companions).
* The two documentation-only items (a `RELEASE_PLAYER=1` mention in this file's own history and the
  `environment.{txt,json}` wording) were corrected while updating §3.

Four defects were found and fixed by these checks and by the independent reviews rather than by a compiler:

1. `BakedCatalogCoverageRecipeSource` first declared its recipes in the order runner, display runner, volume, while
   `TraversalCourseRecipes.Catalog` declares runner, volume, display runner. A `SpawnRecipeCatalog` fingerprints its
   recipes in insertion order, so the two catalogs' fingerprints would have differed and
   `catalog-coverage-bake-runtime-parity` would have failed. Fixed in commit
   "GC-025: materialize the baked recipe catalog in the runtime catalog's own declaration order".
2. The four coverage companions named the catalog's nested value and serializer types unqualified while the
   companion is a sibling top-level class, which is CS0246 in all four assemblies (and, for the checkpoint catalog, a
   CS1503 once only the serializer were qualified, because its value-type names also exist as top-level contract
   records). Fixed in the emitter and regenerated.
3. The player-mode class `ProbeCatalogCoverage` shadowed the generated companion of the same name in the same
   compilation, because a namespace member always wins over a using-imported type: ten members would have failed
   with CS0117. The mode class was renamed `CatalogCoverageProbe`, which also left the generated companion reachable
   by its own name.
4. `BakeCatalogCoverageAuthoring` named `TraversalVocabulary` while the Editor assembly did not reference
   `GameCore.Rules.Traversal` (Unity assembly references are not transitive), and the baked runner definition was
   derived with `Definition(stableName)` while the runtime recipe derives `Definition(stableName + ".definition")`,
   so the parity observation could never have passed. Both fixed.
5. The release-clone strip script's constructor-call needle no longer matched after the new mode was added, and the
   script raised rather than silently half-editing; the first draft of the restart observation referenced
   `W6GateScenario`, which the clone removes, and the clone checker caught it. Both fixed in §4's shared edits.

## 10. Reviewer notes on contract/surface discipline

* `Packages/com.gamecore.contracts/**` — unchanged.
* `Packages/com.gamecore.{composition,derivation,planning}/**` — unchanged.
* `Packages/com.gamecore.unity.runtime/**` and `Packages/com.gamecore.unity.adapters/**` — unchanged.
* `Packages/com.gamecore.gameplay.{cards,narrative}/**`, `Packages/com.gamecore.rules.*` — unchanged.
* The only package change is the traversal *fixture* package described in §4 item 1.
* Every generated file is produced by a tool in this branch and re-checked by that tool before the gate builds
  anything, so a hand-edited committed catalog fails the gate in step 1 rather than being compiled.
