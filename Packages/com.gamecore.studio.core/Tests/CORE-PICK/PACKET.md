# CORE-PICK

Branch: `codex/core-pick`. Baseline: `c9866292`. Host: Linux myubuntu.
This is the packet's PACKET.md, located inside the exclusive core test paths.

## R2 fixes

| Finding / request | Fix | Regression |
|---|---|---|
| R2-38 / P4.2b B-SELECT | Cache renderer projection/owner aggregation per Editor update; retain stamped references per content revision; reuse owner buffers and immutable candidates | `R2_38_B_SELECT_100PicksAnd500CandidateMarquee` (retained P42b harness); `R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms`; `R2_38_CORE_PICK_ReusesStampedRefsAndImmutableCandidates` |
| R2-38 correctness | Preserve frustum/intersection/full-owner containment, reversed rectangles and distance ordering | `R2_38_CORE_PICK_SeededMarqueeMatchesNaiveBothContainmentModes`; `R2_38_CORE_PICK_CameraViewportAndExplicitInvalidation` |
| R2-38 cache freshness | Singleton owns Editor update/content notifications; Undo, object changes, hierarchy/project changes, scene open/close/save and Play transitions invalidate; Play references expire every update | `R2_38_CORE_PICK_EditorChangesInvalidateGeometryIdentityAndStamp` |
| P4.2b installer request | Replace obsolete assumption that the shipping tariff is unconfigured with an explicit placeholder fixture; keep the immutable-release and tariff contracts | `test_r4_placeholder_refuses_before_any_write`; `test_r4_operator_declaration_and_provider_binding` |

`SemanticIndexService` has no renderer/bounds spatial index to reuse. Picking remains
engine-data driven. The cache is owned by each service; the Editor event counters live
in `ScriptableSingleton<PickingRevision>` in the existing Editor-only Authoring asmdef.
There is no event subscription retaining individual viewport services. Returned result
lists own their storage; later queries cannot mutate earlier results.

`PickingService.InvalidateMarqueeCache()` is the synchronous edit seam: code that mutates
a scene and immediately queries again before Editor notifications must call it. Normal
Editor changes invalidate automatically. Camera matrix, culling mask and viewport changes
are checked synchronously. Stale validation still recomputes stamps through the resolver.

## Reproduction

Run from the packet root after committing the source:

```sh
python3 Packages/com.gamecore.studio.core/Tests/CORE-PICK/run-benchmark.py \
  --out "$PWD/.evidence/core-pick-final" \
  --library "$PWD/.evidence/core-pick-clone/games/hollowmere/Library"
PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s studio/etos/tests -v
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
```

Omit `--library` for a fresh Library. It may name only this packet's own Library.
The runner creates a new disposable clone pinned to HEAD, installs P42bHarness into that
clone's Assets, and performs exactly two 100-pick/100-marquee runs over 500 distinct objects.
It adds only renderer/reference phase arrays to the disposable harness copy; original and
instrumented hashes are in the receipt. Service Stopwatch totals, sample counts, target
assertions and fixed 16/50 ms p95 budgets are unchanged. Every result is read from XML.
The host-wide allocator is invoked with `GC_STUDIO_UNITY_SLOTS=1`; the runner also monitors
active Editors and refuses qualification if another Editor overlaps either run.

The broad EditMode command uses the same batch arguments as `unity-compile.sh`:

```sh
GAMECORE_ETOS_AUTOSTART=0 GC_STUDIO_UNITY_SLOTS=1 bash studio/tools/unity-batch.sh \
  --project "$PWD/.evidence/core-pick-clone/games/hollowmere" \
  --log-dir "$PWD/.evidence/core-pick-tests/logs" --label core-pick-tests \
  --results "$PWD/.evidence/core-pick-tests/results.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore.Studio.Edit.Tests|GameCore.Studio.UI.Tests.SelectionAndPickingTests'
```

## Evidence and counts

- Baseline retained harness XML: **0 passed / 1 failed**; all 500 identities resolved.
  Pick p95 **3.0734 ms**, marquee p95 **266.152 ms**, unchanged 16/50 ms budgets.
- Installer before: **3 passed / 2 failed**; after: **5 passed / 0 failed**.
  Both formerly failing assertions were reproduced against the committed shipping tariff.
- Changed-tree broad EditMode XML: **95 passed / 1 failed / 0 skipped**. The sole
  failure is the new bounded timing test refusing two active Editors, as intended.
  Unity's class-level filter selection included it despite the attempted regex exclusion.
  All existing selected tests and all four new correctness/cache tests passed. The final
  runner explicitly repeats the timing test in run 1 under exclusive Editor monitoring.
- Final two datasets and exclusive timing results are recorded below after execution.

## Requests to other packets

- Verification tooling owner: `artifacts/studio/verification/TOOLS/timing-p42b.sh`
  hard-pins `1752ca8a`; add an explicit source-revision argument and clone that revision
  before installing `P42bHarness`. Keep the existing default for historical reproduction.
  CORE-PICK runs the same B-SELECT timing workload with the scoped runner above, because
  invoking the frozen `final-selection` lane would measure old product code. The current
  `verify-all.sh ui` entry invokes the graphical capture/live gateway, not a separate
  500-candidate timing-only branch; no graphical/live ETOS action is required here.

## Left open

- The changed-tree Unity run and final two datasets are waiting for the host to have no
  other active Editor. No sibling clone or service is touched to obtain a slot.
- Cold reference construction and content invalidation still invoke the trusted resolver
  synchronously. The benchmark includes the cold first sample; qualification is the
  matrix's p95 over 100, plus a separate median across 21 Editor updates, not a maximum
  cold-latency claim. Exact cold samples are retained rather than discarded.
- Rust and dotnet source paths are unchanged; no Rust build, paid operation, key-file read,
  etosd/installed-companion stop or restart is part of this packet.
