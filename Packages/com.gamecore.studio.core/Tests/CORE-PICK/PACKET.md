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
The runner reserves one of the three host-wide slots through `unity-batch.sh`, then its
`UNITY=solo-unity.py` adapter takes the allocator mutex, drains already running Editors
without stopping them, and holds the mutex until its own Editor exits. This prevents new
leases during measurement while holding only one slot. Completed runners may briefly wait
to release their bookkeeping slots; no sibling source or process is changed. The wrapper's
3,000-second timeout includes the drain wait. Active Editor monitoring independently refuses
qualification if another Editor overlaps either run. The first run also executes the
21-update timing regression. An earlier unreserved wait was cancelled before any Editor or
sample (exit -15, zero XML cases, peak Editors zero); it is not a third dataset.

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
- First exclusive attempt: retained workload **1 passed**, bounded timing guard **1 failed**.
  Mono `Process.GetProcessesByName("Unity")` returned zero although the independent `/proc`
  monitor retained peak=1. The guard now enumerates `/proc` directly, matching the host allocator.
  The prequalification dataset (pick p95 0.8187 ms, marquee p95 0.2088 ms) and failed XML
  remain in `evidence/guard-fix-*`; they do not replace the final two datasets.
- Final source **`2d992c9f`**, exclusive run 1 XML: **2 passed / 0 failed / 0 skipped**;
  run 2 XML: **1 passed / 0 failed / 0 skipped**. Both independent process inventories
  retain **peak Editors = 1**. The first run includes the 21-update timing regression.
- Combined selected coverage: **88 core cases (83 existing + 5 new) and 8 UI selection
  cases passed**, plus the retained B-SELECT workload passed twice. Earlier guard refusals
  are retained above and are not relabeled as passing runs.

| Final measurement | Run 1 | Run 2 | Fixed gate |
|---|---:|---:|---:|
| 100 picks, p95 | 1.0232 ms | 1.8488 ms | 16 ms |
| 100 marquees / 500 distinct candidates, p95 | 0.2688 ms | 0.3098 ms | 50 ms |
| Marquee sample median | 0.21415 ms | 0.25660 ms | reported |
| Cold first marquee (included in samples) | 96.4086 ms | 169.0545 ms | reported, not a maximum gate |

The additional 21-update test retains all 500 identities on every update: median
**1.8216 ms**, maximum **232.9629 ms** (cold reference construction included), with
renderer sum **44.1212 ms** and reference resolution sum **317.5823 ms**. This proves
geometry refresh across Editor updates, not only repeated queries in one update.

Profiling in the retained harness separates renderer/projection from reference work.
Run 1's cold query spends **2.2041 ms** in rendering/bounds and **94.2037 ms** in reference
resolution; warmed medians are **0.0713 / 0.14305 ms** respectively. The implementation
retains full stamped refs rather than dropping stamps or replacing distinct identities.

Committed receipts: [run 1 XML](evidence/run1-results.xml), [run 2 XML](evidence/run2-results.xml),
[run 1 samples](evidence/run1-selection.json), [run 2 samples](evidence/run2-selection.json),
[run 1 profile](evidence/run1-profile.json), [run 2 profile](evidence/run2-profile.json),
[revision/harness/host receipt](evidence/final-receipt.json), and
[original XML/data hashes](evidence/final-original-hashes.json). Home paths and transcript trailing whitespace are normalized
in committed copies; originals and redacted Editor logs remain under `.evidence/core-pick-verified/`.

Final policy gates: metadata **42 packages / 91 assemblies passed**; C# policy
**1,209 files passed**; installer recheck **5 passed / 0 failed**; `git diff --check` passed.
Transcripts are [metadata](evidence/final-metadata.txt), [C#](evidence/final-csharp.txt)
and [installer](evidence/final-installer.txt). No test budget or identity assertion was relaxed.

## Requests to other packets

- Verification tooling owner: `artifacts/studio/verification/TOOLS/timing-p42b.sh`
  hard-pins `1752ca8a`; support `timing-p42b.sh OUT ATTEMPT [FILTER] [REVISION=1752ca8a]`,
  resolve/check out that exact revision before installing `P42bHarness`, and retain it in
  the receipt. `live_acceptance.py`'s `final-selection` branch should forward an explicitly
  supplied `GAMECORE_SELECTION_REVISION` as argument 4. Keep the existing default for
  historical reproduction.
  CORE-PICK runs the same B-SELECT timing workload with the scoped runner above, because
  invoking the frozen `final-selection` lane would measure old product code. The current
  `verify-all.sh ui` entry invokes the graphical capture/live gateway, not a separate
  500-candidate timing-only branch; no graphical/live ETOS action is required here.

- Verification report owner: update `artifacts/studio/verification/ROWS.json`'s
  `rows[row="W-UI-05"]` to `status="PASS"`, `baseline="2d992c9f"` and the final metrics/evidence
  above; regenerate `SUMMARY.md` and the W-UI-05/B-SELECT summary links from these receipts.
  Those paths are outside CORE-PICK. Relative to the retained aggregate, the row delta is
  **29→30 PASS, 7→6 FAIL, 32 BLOCKED unchanged**; no other row is requalified here.

## Left open

- The legacy ROWS/SUMMARY artifacts still contain the historical W-UI-05 failure because
  they are outside the exclusive paths; their exact update is requested above.
- Cold reference construction and content invalidation still invoke the trusted resolver
  synchronously. The benchmark includes the cold first sample; qualification is the
  matrix's p95 over 100, plus a separate median across 21 Editor updates, not a maximum
  cold-latency claim. Exact cold samples are retained rather than discarded.
- Rust and dotnet source paths are unchanged; no Rust build, paid operation, key-file read,
  etosd/installed-companion stop or restart is part of this packet.
