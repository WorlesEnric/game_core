# R12-A — broad Hollowmere suite isolation and prerequisites

## Scope and retained baseline

Branch `omp/r12-a`. Product packages and validators are unchanged. No GP-*, NPC, stale-context, verdict, full-byte or exact-identity assertion is relaxed. The retained Ferryman candidate remains unchanged. No worker tasks, paid calls, etosd operations, pairing changes or key-file inspection are performed.

Evidence root: [`artifacts/studio/verification/UNITY-HOLLOWMERE/r12-a-20261008T024809Z/`](../../../artifacts/studio/verification/UNITY-HOLLOWMERE/r12-a-20261008T024809Z/).

The retained P4.2l XML contains **590 passed / 17 failed / 21 skipped EditMode**, and **24 passed / 1 failed PlayMode**. Concrete case counts differ from the packet's group headings: A has seven EditMode cases plus one PlayMode case, B has three, C has seven (counting both boolean instances): eighteen failures total. Both retained XML files were inspected, not just the hypothesis list.

## Per-case disposition

Names below omit their unchanged namespace prefixes. Every change is to a test or acceptance fixture, not product code.

| Case | Group | Root cause | Fix | Ownership |
|---|---|---|---|---|
| `AnimatorRespawnTests.W_PLUG_02_AnimatorEvaluatesCommittedVariant_AndRespawnRetainsOverrides` | A | Null graphics device cannot evaluate graphical acceptance. | SetUp requires a graphics-enabled Editor; original body/assertions unchanged. | Test |
| `NativeMediaAcceptance.W_PLUG_01_TravelReleasesRegionNativeMediaWithinFivePercentPeak` | A | Null graphics device provides no native playing audio sources. | SetUp gates only the travel case on graphics; both headless lease/fade tests still run. Original body/output behavior unchanged. | Test |
| `TimingTests.R2_38_B_APPLY_20MarshAllNpcEdits` | A | Missing evidence directory was discovered only after edits. | SetUp requires explicit `GAMECORE_P42_EVIDENCE`; all twenty edits and original budget assertions retained. | Test |
| `TimingTests.R2_38_B_APPLY_20RealSingleTargetEdits` | A | Same late missing-evidence exception. | Same SetUp gate; twenty real edits and original budget retained. | Test |
| `TimingTests.R2_38_B_COMPOSE_20PreparesInReferenceWorld` | A | Same late missing-evidence exception. | Same SetUp gate; twenty real-world preparations/cancellations retained. | Test |
| `TimingTests.R2_38_B_SELECT_100PicksAnd500CandidateMarquee` | A | Same late missing-evidence exception. | Same SetUp gate; 500 candidates, 100 picks/marquees and budgets retained. | Test |
| `NoviceGuideTests.R2_38_Guide_NpcAndDialogueContextToolsUndo` | A | Opening Studio without graphics queues an error; test saves the applied NPC/line before yielding, and aborts before undo. No teardown existed. | SetUp requires graphics and explicit evidence directory. Failure-safe teardown restores exact asset/scene and metadata preimages after all original assertions, including on aborted yields. | Test |
| `FullQuestHeadless.R7C_WPLUG11_ActualMarenLinePlaysItsNativeVoiceClip` | A | Actual native voice playback requires graphics. | Method-specific SetUp gate; other PlayMode cases and voice assertions unchanged. | Test |
| `RetainedCandidateTests.R11_A_RetainedFerrymanCandidateEnrollsOnceAppliesAndUndoes` | B | Earlier Views fixture persisted an enrollment pointing to its deleted graph. Later save normalized the already-dangling reference. | Fix the leaking Views fixture's existing exact-byte teardown; no edits to this regression or candidate. | Test isolation |
| `ContentClosureTests.R6_F_Request2_GraphCreationEnrollsAndHistoryRestoresClosure` | B | Same contaminated starting content bytes. | Same predecessor teardown fix; original full-byte assertion unchanged. | Test isolation |
| `ContentClosureTests.R6_F_Request2_UnchangedCandidateStartsPresentsBellEndsAndDurableUndoRestores20` | B | Same pre-existing dangling closure member. | Same predecessor teardown fix; original closure, roster and full-byte assertions unchanged. | Test isolation |
| `NarrativeRegistrationTests.EveryNarrativeType_CreatesAndRegistersOnTheContentSet_ThroughTheEngine` | C | Uppercase fallback fact name violates GP-LOG-011; empty vendor/loot/world-item/quest/graph defaults are not valid content. Generic graph creation also requires a unique owning world/content set. | Lowercase fixture names; minimally valid typed fields; isolated saved region scene, world and content set. Every type still creates and registers through the engine, with the original registration assertions. | Test |
| `ReadOnlyAndWorldBindingTests.WorldTools_ConnectAddPortalAndSetSpawnPoint_ApplyThroughTheEngine` | C | Regions lacked scenes (GP-WLD-001). | Save real temporary A/B scenes, keep B closed and A active, reacquire assets after single-scene unload. Preserve portal and open/closed spawn assertions. | Test |
| `ToolJournalTests.NpcTools_SetDialogueAndAppearance_AreJournaled` | C | Active scene did not resolve one region/world owning the scratch graph. | Bind scratch content to isolated world and saved active AuthoredRegion scene. | Test |
| `FactHandoffTests.D10a_EarlierCandidateFactStagesAppliesAndReplaysThroughEngine` | C | Scratch content had no world (GP-LOG-001). | Create and bind a scratch WorldDefinition; add its existing assembly reference. | Test |
| `FactHandoffTests.D10a_FailedFactProducerNeverAppliesDependentCondition` | C | Missing-world staging refusal masked the intended invalid-name invocation failure. | The shared world fix restores the original intended `Failed` outcome; retain the original exact `Failed`, dependent `Skipped`, and empty-condition assertions. Changing it to `Refused` was experimentally incorrect once the world was valid. | Test |
| `CandidateTests.Request5_RetainedUnpreviewedCandidateBadgeSurvivesReload(False)` | C | P4.2c signed verdict predates mandatory world/predicted catalog delta. | Replay an unmodified later production-generated P4.2e signed verdict and its exact matching submitted candidate/artifacts through real StageAdmission/VerdictCheck. | Test |
| `CandidateTests.Request5_RetainedUnpreviewedCandidateBadgeSurvivesReload(True)` | C | Same stale retained verdict fixture. | Same fixture refresh; refresh-failure and disabled-Admit assertions unchanged. | Test |
| `P31AuthoringTests.AuthorAllIsIdempotent` | Additional clean-clone prerequisite | Test called its first invocation the “second run,” assuming an already-populated authoring journal. Fresh clone applied `media.voices-bank-28`. | Establish the first `AuthorAll(null, 0)` invocation and assert success before snapshot/journal capture; original second-run no-write/no-journal assertions remain intact. | Test |

## Exact byte-leak diagnosis

The predecessor is **`GameCore.Studio.Views.Hollowmere.Tests.HollowmereEditingViewsTests.R9C_AddChoiceFromChoice_PreservesBothOptionsAndUndoBytes`**. It creates `Assets/P2_3ViewsTemp/ViewsTestGraph.asset` through production generic graph creation, which correctly enrolls the graph in the active world's `HollowmereContent.asset`. It undoes the choice edit, not graph creation. `HollowmereViewsFixture.TearDownRuntime` saves assets and deletes the temporary folder, but its byte-backup list omitted the content asset.

The two-test `leak-before.xml` reproduces the exact retained failure: **8418 expected / 8362 actual**, first difference **offset 8359, ASCII 49 (`1`) versus 48 (`0`)**. The original content is 8346 bytes. The leaked 72-byte YAML reference starts at offset 8346; offset 8359 is the first digit of **`fileID: 11400000`**, not a content stamp or roster counter. When the missing graph reference is next serialized it becomes the 16-byte `  - {fileID: 0}\n`, explaining the exact 56-byte reduction. `preimage-HollowmereContent.asset`, `leaked-HollowmereContent.asset`, and `byte-leak-diagnosis.json` retain the bytes and diagnosis.

The fix adds `Rules/HollowmereContent.asset` to **the existing** `HollowmereViewsFixture.BackedUp` list. Its established teardown restores complete preimage bytes and force-imports them. No normalization, special-case undo or backup restoration is inserted before the downstream assertions. This is **test isolation**, not a product journaling defect. The separate guide leak is repaired with failure-safe exact-byte teardown for Maren's graph, Thornwick scene and content asset (plus metadata).

## Signed fixture provenance

The original P4.2c historical badge witness assertions remain. The replay fixture now uses:

- `artifacts/studio/verification/W-MECH-01/p42e-stage-review-20261006T144510.582506Z/panel-verdict.json`
- Matching candidate/artifacts: `artifacts/studio/workflows/P4.2e/candidate/`
- Production request: `artifacts/studio/verification/W-MECH-01/p42e-stage-submit-20261006T144109.661452Z/stage-request.json`
- Production delta: the same submission's `service/slot-out/catalog-delta.json`
- Job `stg_1a111aa260b24c9b227111d`; candidate `cs_01M48TG1WGST48ZH271H3A96FA`.

No signed document is synthesized or rewritten. Existing offline service replay does not claim a new cryptographic-service or live-admission qualification; production verdict freshness/delta checks remain enabled.

## Graphical prerequisites and reproduction

Timing and guide sources are retained under `artifacts/studio/verification/TOOLS/P42bHarness/` and `TOOLS/P42dLive/`. They are temporarily injected into the same project paths used by P4.2l, then removed after verification. No worker/paid harness is installed or invoked.

All Unity launches use `studio/tools/unity-batch.sh`, `GC_STUDIO_UNITY_SLOTS=1`, `GC_STUDIO_DISK_RESERVE_GIB=28`, and one attempt. Qualification launches explicitly set `GAMECORE_ETOS_AUTOSTART=0` (the initial offline focused diagnosis did not override that variable). Broad headless selection is `-runTests -testPlatform EditMode|PlayMode -testFilter '.*'`, exactly the ordinary selection in `verify-all.sh`. The wrapper intentionally exits 1 on explicitly ignored prerequisites; suite disposition is established from XML (zero failed, explicit ignored reasons), not by relabeling that exit code.

Graphical timing uses the retained `R7_C/batch-graphics.py` adapter, `DISPLAY=:1`, and explicit `GAMECORE_P42_EVIDENCE`. Guide uses the same interactive argument transformation as the retained row driver, but a temporary credential-free adapter strips only `-batchmode`/`-nographics`; autostart stays disabled. This avoids the existing row dispatcher's matrix writes and live autostart. Filters and unchanged test bodies are the row drivers' actual filters, not substitutes. `commands.json` records bindings.

W-PLUG-01/02 and native PlayMode voice changes are SetUp-only. Their permitted retained P4.2l graphical PASS XML paths and SHA-256 values are in `retained-graphics-qualification.json`; no new native graphical acceptance is claimed.

## Verification results

| Check | Before | After | Evidence under the root above |
|---|---|---|---|
| Full Hollowmere EditMode, same 628 cases | 590 passed / 17 failed / 21 skipped | **600 passed / 0 failed / 28 Ignored** | `final-editmode.xml`, `final-editmode-logs/` |
| Full Hollowmere PlayMode, same 25 cases | 24 passed / 1 failed | **24 passed / 0 failed / 1 Ignored** | `playmode.xml`, `playmode-logs/` |
| Exact predecessor/undo reproduction | 1 passed / 1 failed, exact 8418→8362 discrepancy | Both pass in focused and full final runs | `leak-before.xml`, `fixtures.xml`, `final-editmode.xml` |
| Focused fixture/idempotence/ordering verification | Initial diagnostic 28 passed / 2 failed | 14 passed / 0 failed | `diagnosis.xml`, `fixtures.xml` |
| Graphics `:1` timing driver | Missing evidence prerequisite fails headless | 4 passed / 0 failed; single p95 23.1748ms, Marsh p95 36.5299ms, prepare p95 4.3448ms | `timing.xml`, `timing/*.json` |
| Graphics `:1` novice guide | Null graphics abort before undo | 1 passed / 0 failed; original 20 scene entities and 13 lines restored by normal undo | `guide.xml`, `guide/guide.json`, `guide/guide-applied.png` |
| Studio core and Views package suites | Product unchanged | 116/116 and 26/26 passed as part of full EditMode; another 16/16 Hollowmere Views tests passed | `final-editmode.xml` |
| .NET solution Release | — | 1,832 passed / 0 failed / 6 environment-gated NotExecuted | `dotnet/*.trx`, `dotnet-summary.json` |
| Metadata policy | — | 42 packages / 92 assemblies, pass | `check_package_metadata-clean.log` |
| C# policy | — | 1,287 files with injection; 1,285 after cleanup, pass | `check_game_core_csharp.log`, `check_game_core_csharp-clean.log` |

`ignored-prerequisites.json` preserves every Ignored case's nonempty explicit reason. Headless A cases all report **SetUp** prerequisites, rather than failing after mutation. All original live/graphics/voice gates remain. Native graphical qualification is the permitted retained evidence, not a new run.

Intermediate failures are retained without relabeling: `editmode.xml` is the first broad repair run (**597 passed / 3 failed / 28 skipped**), exposing the valid vendor fixture, clean-clone idempotence setup, and the experimentally incorrect `Refused` expectation. `final-editmode.xml` is the final full run after those corrections. No narrowed run replaces it.

Both final XML roots are `Skipped:Ignored`, with zero failed test cases. Both wrapper processes exit **1**, unchanged. The wrapper labels EditMode `SKIPPED (env-gated)`, but PlayMode `FAIL`: `unity-diagnostics.py` extracts only `GAMECORE_*` names, so its display classifier does not recognize a graphics-only skip. This is an explicit reporting limitation, not a failed PlayMode test and not a reason to weaken the runner. Packet suite pass means zero failed cases with the commissioned prerequisite skips; it does not mean every case ran headless or the wrapper returned zero.

The guide capture was inspected: it shows the graphical Studio viewport surface, not readable NPC/dialogue-edit evidence. The unchanged test assertions and `guide.json` establish the actual operations and undo; no screenshot-content claim is made. Opening Studio performed its ordinary read-only provider-status discovery; no worker task or paid operation was requested.

## Host, cleanup and remaining prerequisites

Launches were serialized through the one-slot wrapper. The scoped monitor recorded 295 samples, zero foreign project-session samples, and minimum `/home` free space **33.619 GiB** (reserve 28 GiB). No pruning was needed. `host-summary.json` explicitly retains a monitor limitation: its import-worker filter used the wrong argument spelling, so one guide sample counted three raw Unity processes. The guide log records two AssetImportWorker startups; attributing the two extra sampled PIDs to those workers is an inference, not retained argv proof. Do not interpret the raw process maximum as a certified Editor count. Wrapper launch intervals never overlap.

`cleanup.json` lists only owned removals/restorations: injected acceptance copies and generated metadata, temporary launcher/monitor/preimage, run-created journals (retained under `run-residue/` first), regenerated historical receipts and Unity project settings restored to original tracked bytes. Authored fixture source changes remain. No product package, unrelated user file, compiler cache, other clone, `ROWS.json`, `SUMMARY.md`, 07 or 12 is changed. The two modified retained harness source checksums are refreshed in `TOOLS/SHA256SUMS`; the evidence root has its own `SHA256SUMS`.

Remaining prerequisites are the explicitly ignored live/graphical/voice cases listed in `ignored-prerequisites.json`. W-PLUG-01/02 and native voice retain the authorized prior graphical proof; timing and guide have new graphical proof. No original nonpassing case remains a test failure. This packet does not claim newly executed paid/live acceptance, altered matrix totals, or zero wrapper exit status.
