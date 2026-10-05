# R3-F — cross-package handoffs

Branch `codex/r3-f`, base `fcdd4d6`; Linux build host, this checkout only.

## R2 fixes / R3 follow-through

| Finding | Fix | Regression |
|---|---|---|
| D21 | Merge bounded minimal prompt-reference nodes and scene refs without changing selection; preserve truncation and omitted counts | `R3FHandoffTests.D21_RetainedRingPromptIncludesUnselectedWell`, `D21_PromptReferencesRespectCapsAndReportTruncation` |
| D5 UI | Show `StagedChangeSet.Inferences` in an informational section separate from blocking diagnostics | `R3FHandoffTests.D5_ScopeInferenceIsVisibleInformationAndDoesNotBlockApply` |
| D4/D5 companion | Catalog scope intersection, normalized raw/typed target scopes, informational inference evidence, GP-ENT-004 tint check | four tests in `studio/agent/tests/r3_f_candidate.rs`; shared `robe-verdicts.json`, `FactHandoffTests.D4_D5_RetainedRobeSharesCompanionVerdictFixture` |
| D10a | Candidate fact producer ordering, typed binding after successful production, precise forward-reference diagnostic | `FactHandoffTests.D10a_EarlierCandidateFactStagesAppliesAndReplaysThroughEngine`, `D10a_ForwardFactReferenceRefusesBeforeWritesWithWitness`, `D10a_FailedFactProducerNeverAppliesDependentCondition` |

## Verification

Code revision: `861a07cc4dbf44ec42fe59f33feae4d37f002bd1`. All builds and tests ran on this Linux host in this checkout, one Editor at a time through the unchanged host allocator. No sibling checkout, installed companion, or ETOS node was changed.

| Check | Result | Retained evidence |
|---|---|---|
| Corrected Unity baseline | 2 passed, 5 expected failures, 0 skipped | [before.xml](Documentation~/R3-F/before.xml) |
| Rust baseline | All four R3-F regressions failed | [rust-before.log](Documentation~/R3-F/rust-before.log) |
| Final required EditMode filter | **146 passed, 0 failed, 3 skipped**, 149 total; 41.471 seconds of tests | [editmode.xml](Documentation~/R3-F/editmode.xml) |
| Rust fmt / clippy | `cargo fmt --check` and `cargo clippy --all-targets -- -D warnings` passed | host invocation, final source unchanged since checks |
| Rust full suite | **124 passed, 0 failed, 7 ignored** | [cargo.log](Documentation~/R3-F/cargo.log) |
| Model dotnet suite | **113 passed, 0 failed, 0 skipped**, read from TRX | [model.trx](Documentation~/R3-F/model.trx) |
| UI host evidence contract | 1 passed | `python3 -m unittest discover -s Packages/com.gamecore.studio.ui/Tests/Host -p 'test_*.py'` |
| Metadata / C# policy | Pass: 42 packages, 91 package assemblies; 1,152 C# files | `python3 tools/check_package_metadata.py`; `python3 tools/check_game_core_csharp.py` |
| Whitespace | Pass | `git diff --check` |

Final XML breakdown: Core 83/83, UI 49 passed + 3 graphical skips, P2.1 3/3, R3-A 3/3, R3-F 8/8. The three new UI handoff tests also pass within the UI count. No test namespace matching `Hollowmere.R2_C` exists in this checkout; its requested filter alternative was retained unchanged.

Exact final invocation (the wrapper supplies `-batchmode -nographics` and `-testResults` from `--results`, as in `unity-compile.sh`):

```sh
UNITY=/tmp/r3-f-unity bash studio/tools/unity-batch.sh \
  --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" \
  --label r3-f-verified --results "$PWD/.unity-logs/r3-f-verified.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.(UI|Core).*|Hollowmere\.R3_[AF].*|Hollowmere\.P2_1.*|Hollowmere\.R2_C.*'
```

The temporary launcher sets only `UPM_CACHE_ROOT=/tmp/r3-f-upm` and `DOTNET_PROCESSOR_COUNT=4`, then execs the installed Unity 6000.0.75f1. The private UPM cache was provisioned and verified from the committed public `studio/stage/cache/upm-lock.json` using `upm_cache.provision`; no credentials, project assemblies, or sibling caches were copied. First cold compilation exposed fixture API mistakes, corrected before the retained baseline. The baseline wrapper retried the documented ILPP socket fault. The [first fixed run](Documentation~/R3-F/first-fixed.xml) had 144 passes, two fact-test failures, and three skips; the final run fixes the producer import visibility and checks BestEffort operation outcomes. The wrapper returns 1 for the final run's three skips despite Unity exit 0; this is explicitly partial graphical qualification, not an all-passed result.

Fact binding does not mutate during Stage. It adds the producer dependency to an immutable change-set copy, rejects forward/ambiguous/wrong-kind refs before writes, and retains the normal category/unknown/cycle checks. Apply imports successful producer output before binding the consumer; failed producers skip consumers under BestEffort. Newly created fact assets get a deletion inverse; updates of existing assets never get that inverse. The engine test covers candidate identity through Apply, journal Undo and Redo.

## Requests to other packets

None: the four assigned handoffs are implemented within the exclusive paths. The companion uses declared catalog target types; actual indexed type checks remain with Unity, as in the existing bounded prevalidation contract.

## Left open

- Three selected Unity cases explicitly require a graphical Editor and skip under the required `-nographics` invocation: `R2_29_KeyDownUpOnPromptAndControlsNeverEnterViewportHandlers`, `D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce`, and `Render_TargetMatchesTheViewportArea`. Event delivery, physical window placement, and viewport rendering therefore remain unqualified here.
- Seven existing Rust integration tests are ignored by default: Docker isolation, two real-node tests (including agent restart), Docker stage-int, offline licensing, and two real-Unity staging cases. These require provisioned integration services/images/entitlement or forbidden node/service operations; they were not enabled by this candidate-validation packet.
- No paid/live ETOS operation, companion installation, or service restart was performed. Retained candidate replay does not claim a fresh provider workflow.

## R4 — R4-B Studio UI graphical failures

Branch `codex/r4-b`, base `c79a3832`. Exclusive changes: Studio UI package, the one
P3.1 memory assertion, and the R2-C packet note. Tests run only in this checkout
on the Linux host. No provider operation, companion/node restart, sibling
checkout edit, or credential-file inspection is part of this packet.

### Retained reproduction

Both `artifacts/studio/verification/GRAPHICAL/graphics-required-tests-20261005T172641.170099Z/results.xml`
and `...191202.057821Z/results.xml` contain the identical two failures (2 pass,
2 fail): R2-29's control callback observed zero key-downs; D12 still observed
`(100,100,800,700)` instead of `(20,40,1048,772)`. The GRAPHICAL directories
contain logs/XML, not PNGs. The adjacent W-UI-01 capture's `10-prompt-bar.png`
shows separate prompt/image controls but cannot establish event routing.

### R2 fixes / R4 regressions

- P42-UI-01 / R2-29: the first new graphical run isolates the rig fault:
  prompt root trickle-down sees one key-down/up, its bubbling counter stays zero,
  and image counters stay zero. The old counter measured propagation after the
  text control had consumed the event. The corrected rig waits for focus, checks
  non-navigation W down/up separately from Tab (which can change focus), checks
  text insertion and synchronous release inside FocusOut. Viewport product code
  is byte-identical to the retained failure; see `key-routing-source-blobs.txt`.
- P42-UI-02 / D12: bounded placement runs across `EditorApplication.update`,
  requiring three matching observations over 0.5 seconds. The five-second clock
  starts at the first update (GTK Show can block); retries are spaced by 250 ms,
  with a separate 120-write cap. Requested bounds never drift in response to
  clamping. All rectangles use integral native pixels and the last right-hand
  tile retains the remaining height. Completion/timeout unsubscribes, preserving
  later creator moves. Tests: `P42_UI_02_LatePlacementIsRetriedUntilStable`,
  `P42_UI_02_ClampedReadbackRetriesTheRequestedRectWithoutDrifting`,
  `P42_UI_02_StableObservationsDoNotSpendRetryBudget`,
  `P42_UI_02_LayoutUsesIntegralNativeWindowRects`,
  `P42_UI_02_TimeoutDiagnosesAndUnsubscribes`.
- **D12 physical disposition:** the exact retained `(20,40,1048,772)` request is
  clamped by the `:1` native window system to `(20,69,1048,772)`. The native
  ContainerWindow readback stays at y=69 even for a compensating y=11 request;
  the desktop work area starts at y=32. This ruled out the attempted offset
  correction, which was removed. The literal request is retained in
  `P42_UI_02_GraphicalClampedOriginReportsTimeout`: it requires the exact
  requested rectangle in `layout_timeout`, termination, preserved x/size and a
  clamped y. The positive `D12_DeferredRelayoutSurvivesWindowManagerPlacementAndRunsOnce`
  uses a feasible area `(20,100,1600,900)`, forces four late placements, asserts
  exact `(20,100,1048,694)`, then exact creator `(60,140,900,700)` after native
  acknowledgement. Both pass on `:1`; the original impossible y=40 rectangle
  is **not** claimed to have stuck. Earlier failed runs remain in the evidence.
- P42-STARTUP-01: `P42_STARTUP_01_TrayShowsPreciseSessionProblem` covers unpaired
  and missing-key diagnostics and clearing stale status after connection.
  `P42_STARTUP_01_OpenUsesOptionalIdempotentSessionAndPreservesGateway` checks
  startup selection and no restart of an existing gateway;
  `P42_STARTUP_01_NullGatewayPreservesOptionalSessionProblem` checks the actual
  optional-package diagnostic bridge without reading credentials.
- P42-MEMORY-01: `MemoryBudgetTests.test_P42_MEMORY_01_ten_cycle_assertion_enforces_fifteen_percent`
  fails against the old 25% assertion. The legacy ten-cycle assertion must use
  the fixed 15% budget from 07; independent P4.2 snapshot evidence remains intact.

### Requests to other packets

- R4-A, `Packages/com.gamecore.studio.etos/Editor/EtosStudioSession.cs`: preserve
  `public static Diagnostic? Problem`, and provide idempotent
  `public static bool EnsureStarted()` (or make existing `Start()` idempotent)
  over `StudioServices.Runtime`. Startup must set the precise redacted
  not-paired/missing-key diagnostic on failure and register the gateway on
  success; automatic startup/reload must use the same operation. R4-B calls
  `EnsureStarted` when present, otherwise existing `Start` only if no gateway is
  registered. Optional reflection targets assembly `GameCore.Studio.Etos`;
  package/asmdef dependencies remain unchanged. UI does not read credentials.

### Left open

- The native minimum on this `:1` desktop prevents literal y=40 placement.
  The specified bounded timeout/diagnostic outcome is verified; no WM setting,
  reflection bypass, or relaxed rectangle equality is used to force a pass.
- Automatic paired startup across reloads belongs to R4-A. This branch verifies
  the UI startup and diagnostic seams against the base ETOS package and fakes;
  it does not claim the not-yet-merged R4-A automatic-start integration.


### Final R4 verification

Product code: `c1bb1f0c`. All tests ran in this clone on Linux through
`unity-batch.sh`; counts are from result XML. Evidence and commands:
[Documentation~/R4-B/README.md](Documentation~/R4-B/README.md).

| Check | Result |
|---|---|
| Original-code regressions | 0 passed, 3 expected failures; memory budget guard also fails at 25% |
| Final requested UI filter, headless | **65 passed, 0 failed, 4 graphical skips** |
| P3.1 hooks (same final invocation) | **2 passed, 0 failed**, including `TenPlayEditCycles` at the 15% ceiling |
| Final `:1` graphical cases and R4 components | **12 passed, 0 failed, 0 skipped**; covers all four headless skips |
| Host regressions | **2 passed** |
| Package metadata | Pass: 42 packages, 91 package assemblies, exact dependencies |
| C# policy | Pass: 1,199 files |
| Whitespace / exclusive paths | Pass |

Final combined headless XML: 71 total = 67 passed + 4 graphical skips. The
wrapper correctly returns partial/nonzero for those skips; it is not described
as an all-passing headless acceptance run. `memory-cycles.json` records ten
actual cycles: allocated growth -27.18%, reserved growth +2.05%. This qualifies
the corrected legacy assertion, not a new Memory Profiler snapshot campaign;
the independent P4.2 snapshots remain unchanged. The report's revision field
says `editor`; the product source provenance is the commit above.

Generated ProjectSettings, the old shared memory report, one test journal entry
and untracked test metas were cleaned/restored after copying the new evidence
into this owned package. The initial `.codex/` directory was left alone. No
sibling clone or installed service was changed.
