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
