# R2-E — Studio views

Branch: `codex/r2-e`. Host: myubuntu. Base: R2-A `21d2bc03`; merged `origin/main` `4434e6a803a3ceb20465f0fef67745d77b94d8a9` in `42362cb0`.

R2-A's original report remains at `21d2bc03:PACKET.md`. This packet consumes its References, ReadOnly registry, and typed HistoryService seams. The only core edits are necessary merge conflict reconciliation: retain prepared recovery/inverses and RuntimeOnly metadata, retain main's post-import stamp rewitness tracking, and remove the automatically merged duplicate ReadOnly declaration (`f4401f12`). All implementation edits are inside the R2-E exclusive paths. No sibling checkout, credential file, ETOS service, installed companion or paid operation was accessed.

## R2 fixes

| Finding | Views implementation | Regression test |
|---|---|---|
| R2-15 | ViewEdits Undo/Redo continue directly through generic HistoryService, including admission dispatch. No view-specific lifecycle. | `R2_15_ViewsUndoAndRedoUseAdmissionHistoryHandler` |
| R2-30 | Floating view windows enforce 1280×720 on creation/reopen/focus; docked/embedded panels retain 640×360 under D6. Overall tiled Studio/viewport enforcement is R2-C. | `R2_30_StandaloneMinimumPersistsAfterReopenAndDockingUsesPanelMinimum` |
| R2-31 | Reuse visibility HashSet, node/edge lists, edge color buckets, grid buckets and stopwatches. Adjacency construction is time-sliced; large layouts defer their first slice and avoid an unused card refresh before framing. Painting consumes cached groups; RefreshEdgeStyles explicitly invalidates edge colors/highlights. | `R2_30_R2_31_TwoThousandNodesReuseBuffersWithinFrameBudget` (2,000 nodes, warm zoom allocation and regroup allocation assertions) |
| R2-32 | Resolve exact argument and return-contract types before invoking, reject ambiguous/incompatible overloads, use bool Admitted + enum Result.Kind and its reason; use bool Started for dialogue. Reflection failures become redacted diagnostics, owner is rescanned after destruction/replacement. | `R2_32_TravelUsesTypedReceiptAndExactOverload` (3 cases), `R2_32_MismatchedAndThrowingMembersRefuseWithDiagnostic`, `R2_32_DialogueUsesStartedAndRebindsRestoredWorld` (2 cases), `R2_32_RealPlayDialogueTravelAndDestroyedOwner` |
| R2-33 | ReadOnlyToolInvoker is a result adapter over Registry.Invoke only. Removed method discovery/argument-binding bypass and mutable pure-tool allowlist. P1.7b's pure metadata is included by the main merge. | `R2_33_ViewsRespectRegistryAuthorityWhenPureToolIsReclassified`; existing dialogue-preview/quest-simulate tests |
| R2-34 | World connect produces ONE AllOrNothing change set using world.connectRegions; removed two-change-set fallback and ApplySequence API. Explicit region AuthorArg for addPortal; setSpawnPoint always targets region definition. Additional operations use dependsOn in the same change set. | `R2_34_WorldAddPortalAndConnectAreSingleChangeSets`, `R2_34_SpawnTargetsDefinitionAndPortalCarriesExplicitRegion`, `R2_34_SecondOperationRefusalRollsBackConnectionInOneChangeSet` |
| R2-35 | Changes has an explicit asynchronous “Run repository dependency check” using python3 tools/check_package_metadata.py --json <unique temporary report>. Validates report/exit consistency; unavailable/fault/timeout never shows passed. Local graph hints are labeled informational. Raw child output goes through core RedactingTextWriter. | `R2_35_CheckerResultRetainsRulesOutsideLocalGraph`, `R2_35_ActualCheckerRunsAndReturnsItsJsonVerdict` |
| R2-36 | Deleted Views NestedReferenceContributor and its registration/rebuild helper. Context uses runtime.References and node Refs for labels (including classified edges). Opening a view does not change index revision or contributors. | `R2_36_ContextDoesNotInstallContributorOrRebuildIndex`, `R2_36_NodeRefsLabelClassifiedEdgesWithoutViewsContributor`; existing Hollowmere impact closure tests |

Selection: added reflection-free `StudioSelectionAdapter` implementing IStudioSelectionBridge through strongly typed core-ref delegates. It forwards Current/Select/Focus/Changed and detaches on disposal (`SelectionSoftBindingPropagatesFocusChangesAndDetaches`). Final production binding is requested below. Static array-backed public lists in this package are now read-only wrappers.

## Requests to other packets

- **R2-C / integrator — selection binding:** in `Packages/com.gamecore.studio.ui/Editor/Core/StudioUiSession.cs`, bind the singleton-owned `SelectionModel` after `StudioSelection.instance.Bind(selection)`. Required bridge contract: `new StudioSelectionAdapter(() => selection.Targets, refs => selection.Set(refs, SelectionOp.Replace), focus, handler => selection.Changed += handler, handler => selection.Changed -= handler)`, passed to `StudioViewsSession.BindSelection`. Retain and dispose the adapter when the context is replaced. `focus(AuthoringRef)` must frame the Studio viewport (the existing `StudioViewportWindow.FrameSelection()` is private; expose a creator-facing `public void FrameSelection()` or equivalent typed action). A direct views→UI reference has no present assembly cycle, but requires adding `com.gamecore.studio.ui` to the views manifest AND updating both qualification/Hollowmere lock dependency maps outside R2-E's exclusive paths. Prefer an integrator-owned binding assembly with refs to both, or coordinate the direct reference plus lock updates. No hidden package dependency or reflective singleton lookup was added.
- **R2-G / R2-A — world inverse seam:** `Packages/com.gamecore.gameplay.world/Editor/WorldTools.cs` `world.connectRegions` must retain a durable inverse for the created PortalDefinition asset and both RegionDefinition.neighbours mutations, in addition to WorldDefinition.portals. Current reflected generic inverse captures only the target world's fields; touching the returned portal is not a delete inverse. Publish an engine-native result with prepared preimages before asset creation (`EditContext.PrepareInverse` / `OperationResult.WithAssetLevelInverse`) through the gameplay/core adapter. The views now call this one tool once; no independent fallback changes survive in Views. Scene-side addPortal/setSpawnPoint inverses likewise belong to the world tool adapter.
- **R2-H / integrator (inherited R2-A):** regenerate `docs/studio/schemas/tool-catalog.schema.json` for ReadOnly/RuntimeOnly and update `dotnet/tests/GameCore.Studio.Model.Tests/JsonRoundTripTests.cs` `DiagnosticCodeRegistryIsComplete` with MediaTypeForbidden, MediaPathForbidden, MediaImporterInvalid, ArtifactSourceForbidden. Neither path belongs to R2-E.
- **R2-C (D6):** ensure overall `StudioMenu` layout ≥1280×720 and viewport ≥640×360 after tiling/reopen. Individual embedded view panels remain ≥640×360; standalone view windows now enforce the full surface minimum.

## Verification

- `python3 tools/check_package_metadata.py --json .unity-logs/r2-e-metadata.json`: passed, 41 packages, 89 assemblies, exact dependencies unchanged.
- `python3 tools/check_game_core_csharp.py`: passed, 1,094 C# files; `git diff --check` passed. Metadata checker self-tests: 31/31 passed.
- `dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj`: 102 passed, 2 failed of 104, 0 skipped. The two failures are the inherited schema/diagnostic registry requests above. Initial build caught and fixed the merge's duplicate ReadOnly declaration.
- First Unity invocation: compilation stopped before tests, due to that duplicate and calling core's instance Redact method as static. Both corrected. No test pass claimed for that attempt.
- First measured views run: `.unity-logs/r2-e-verified.xml`, **40 passed, 1 failed, 0 skipped/inconclusive**, 41 total. Package suite 25 passed/1 failed; Hollowmere suite 15 passed. XML duration 44.056 s; Editor 702 s including cold import. SHA256 `400eed07d12b02a80071b0b037026c9aa1ad4dcd112c3f5833a5e0123c8c5597`.
  - Only failure: initial 2,000-node SetGraph 25.867 ms exceeded 16 ms. Layout slice max 8.82 ms; refresh max 0.67 ms; warm zoom and edge regrouping both **0 bytes/frame**.
  - Real Play Mode bridge/reboot test passed (26.979 s); actual Python checker invocation passed (0.843 s).
  - Corrected synchronous setup by moving layout adjacency construction into slices, deferring the large graph's first slice, and avoiding a duplicate card bind before FrameAll. Test now measures the entire layout frame including final grid rebuild and refresh, not just the iterator slice.
- Final measured views run on code `5b6db664` (second/final performance measurement): **41/41 passed, 0 failed/skipped/inconclusive**, Unity exit 0, Editor 246 s. Package suite 26/26; Hollowmere suite 15/15. XML: `.unity-logs/r2-e-final.xml`, result duration 8.546 s; SHA256 `f02ac29d9a04d4bd79293c736f60440f962b509f24a670765898a5bbd80f8ed5`. Full log `.unity-logs/r2-e-final-20261005T152316-a1.log`.
  - 2,000 nodes: index graph 5.1 ms, neighbourhood 1.6 ms, initial graph setup **5.6 ms**, layout 2 slices (max **8.37 ms**), whole layout frame including rebuild/refresh max **8.76 ms**, warm refresh max **0.22 ms**.
  - Warm zoom **0 managed bytes/frame** (30 frames); edge regroup **0 managed bytes/frame** (30 regroupings). Compact graph 0 cards; framed root 36 cards (cap 300).
  - Exactly two performance measurements; the first failure is retained above. Subsequent edits are documentation only. Tests restored all tracked content; final worktree has only packet notes pending and the pre-existing untracked `.codex/` launcher directory.

Host command: `bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs" --label r2-e-final --results "$PWD/.unity-logs/r2-e-final.xml" -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\.Views.*'`. Each invocation held only one of the three host-wide slots. No graphical capture or staging/paid operation was used.

## Left open

- Actual P2.1 singleton/viewport binding needs the exact cross-packet integration and package-lock ownership changes described above. The default bridge still uses UnityEditor.Selection; this packet does not claim an integrated viewport focus action.
- World tool durable asset/scene inverse completeness is R2-G/core ownership, described above; Views no longer split the user action across journal entries.
- The two inherited model schema/registry failures cannot be corrected inside R2-E's exclusive paths.
- D3 confinement/licence qualification belongs to the stage service packets. This packet launches only the requested test Editor under unity-batch's host lock, and issues no staging verdict; no Docker/host staging claim is made.
- Canvas measurements are headless Editor CPU/managed-allocation measurements, not graphical GPU frame or playthrough qualification. Graphical evidence was explicitly excluded for this packet.

## R2-E-merge

Merged `origin/main` `de2d9593287542cba8d097f0b0061b1dc3628f53` into `codex/r2-e` on the Linux build host. The two conflicting core files (`Editor/Tools/ReflectedTool.cs` and `Runtime/Authoring/AuthoringMetadata.cs`) take main's content exactly; the entire `Packages/com.gamecore.studio.core` tree is identical to main. Views continue through the existing public seams. No core change was needed or requested for this merge.

The packet-note rename conflict is resolved by preserving main's complete R2-A/core/integration report and the complete R2-E views report in `docs/studio/packets/R2-A-core-edit-recovery.md`. The branch's views report is also retained here. Earlier verification and open-item statements are historical; the merged-main verification below supersedes their test results.

Verification commands:

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r2-e-merge \
  --results "$PWD/.unity-logs/r2-e-merge.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'GameCore\.Studio\..*|Hollowmere\..*'
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
git diff origin/main --check
```

- Package metadata: passed, 41 packages / 89 assemblies.
- C# static checks: passed, 1,101 files.
- Diff whitespace check against main: passed. The full staged merge diff reports only pre-existing main whitespace in five P1_7c `.meta` files and `studio/stage/run-redacted.py`; those unrelated files retain main's content.

- First full-suite XML: `.unity-logs/r2-e-merge-first.xml`, **325 passed, 1 failed, 5 skipped, 0 inconclusive** (331 cases; 226.807 seconds). Unity exited 2 after 1,440 seconds including compilation/import/shutdown; wrapper exited 1. SHA256: `d9cd5cf226e93e1b563a9986bf8769d84d1323f2fef0dd2cba8785269f5d5173`. Log: `.unity-logs/r2-e-merge-20261005T155621-924471-a1.log`.
  - Failure: `R2_35_ActualCheckerRunsAndReturnsItsJsonVerdict` exhausted the views checker's 30-second subprocess deadline during heavy host I/O. The standalone repository checker passed. `PackageMetadataCheck` now allows a bounded 120 seconds off the UI thread, preserving timeout refusal, redaction, exit/report validation and cleanup. No test assertion or core code changed.
  - The unchanged 2,000-node test passed: setup 5.6 ms; max layout slice 8.89 ms; max whole layout frame 9.23 ms; max refresh 0.22 ms; warm zoom and edge regrouping both 0 bytes/frame. The 16 ms limits remain unchanged; no median adaptation was necessary.
- Final full-suite command uses the same filter and project with `--label r2-e-merge-final --results "$PWD/.unity-logs/r2-e-merge-final.xml" --timeout 1800 --attempts 1`. Only one Editor is launched at a time by this task through the shared host slot allocator.

- Final full-suite XML: `.unity-logs/r2-e-merge-final.xml`, **326 passed, 0 failed, 5 skipped, 0 inconclusive**, 331 total; XML duration 91.796 seconds. Unity exited 0 after 485 seconds, one attempt. SHA256: `09ae71568723b565745fa22e34279490328f076e79f05a56f81f9c2206048572`. Log: `.unity-logs/r2-e-merge-final-20261005T163958-1007180-a1.log`.
  - All 41 views cases, all 28 P1_7b cases and all 60 R2_B cases passed. The actual repository-checker case passed in 0.187 seconds.
  - Exactly the expected skips: four `GameCore.Studio.Hollowmere.P2_2.Live.EtosLiveTests` cases (`B_NpcRequest_ReachesStaging`, `C_D_E_F_I_MediaOps`, `J_Voice_FromASpokenWav`, `J_Voice_FromTheEditorMicrophone`) require the opt-in live ETOS environment; `Hollowmere.P1_5.EditMode.Tests.UiAudioContentTests.PreviewScreenCapturesARenderTextureOrSkipsHeadless` requires a graphics device.
  - Main's strict wrapper reports `FAIL` / exit 1 and `PARTIAL/NotRun` because it rejects any skipped case. The parsed case-level XML meets this packet's explicit acceptance requirement; skipped tests are not counted as passed. The wrapper was not modified.
  - Unchanged 2,000-node regression passed again: setup **7.8 ms**, max layout slice **8.13 ms**, max whole layout frame **12.05 ms**, max refresh **0.33 ms**; warm zoom and edge regrouping **0 bytes/frame**, root framing 36 cards (cap 300). All original 16 ms and allocation assertions remain intact; no median adaptation or threshold relaxation was needed.
- Both requested Python checks passed again after the timeout fix (41 packages / 89 assemblies; 1,101 C# files). Generated Hollowmere asset mutations from the test fixtures were restored from the merged index after the Editor exited. The pre-existing untracked `.codex/` launcher files remain untouched. The core package remains byte-for-byte identical to main.
VIEWS-RENAME: Renamed the nested test type Result to CheckerResult and its type uses in R2ViewsRegressionTests.cs; Result properties and the checker are unchanged. Unity was not started.
VIEWS-RENAME: Ran python3 tools/check_gate_sources.py with the seven --file arguments from tools/run_w7_gate.sh and --json /tmp/views-rename-gate-sources.json: Result false positives cleared, but exit 1 remains for 13 unrelated World.Session entries in ProbeRecoverySmoke.cs/Gc027Scenario.cs; the W7GateScenario.cs-only check reports unresolved: none (exit 0). python3 tools/check_game_core_csharp.py passed (1,136 files).
