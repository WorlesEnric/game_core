# R7-E validator parity — PACKET

Branch: `omp/r7-e`; base: `1317f74`. Scope: R7-C's three requests for W-PLUG-10, and only the commissioned core/UI/media paths, tests and row evidence. No paid operations, installed companion/etosd changes or sibling clones.

## R2 fixes

R2-38 / W-PLUG-10: preserve the gameplay validator's canonical code, message and subject across the actual context inspector, validator console and production ETOS candidate-import/staging path. Only the remote companion/worker is fake. The original failing R7-C XML remains in `artifacts/studio/verification/W-PLUG-10/r7-baseline/results.xml`.

- Inspector: `ValidatorDiagnostics.For(target, runtime)` retains `SubjectId` as `data.subject` and resolves its canonical unstamped/unscoped `Diagnostic.Where` reference. Both inspector dispatch callers and the parity test use the runtime-bearing signature.
- Engine: `StageCore` projects dependency-ordered built-in `set`/`assign` edits to detached definition copies, remaps references between affected copies, then invokes reflected `[AuthorValidator] Validate(T)` methods. Canonical findings enter the proposal-level diagnostic list rather than `StagedOperation.Add`, whose fixed-code normalization would rewrite gameplay codes and drop subject data. An invalid cumulative proposal refuses as a whole, including under BestEffort. Copies are released in `finally`; no live definition is written. History inverses retain `validate=false`. Validator discovery is runtime-owned, not static mutable state; package dependencies are unchanged.
- Generated voices: after graph attachment, `AttachGenerated` journals `audio.assignClip` for each imported voice into `HollowmereAudioBank`, keyed by `AudioClip.name`, group Voice, volume 1, loop/spatial false. The existing reflected-tool preimage inverse restores inserted and replaced entries through normal History.

| Finding / request | Regression |
|---|---|
| R2-38 / W-PLUG-10 inspector and candidate parity | `Hollowmere.R7_C.Validation.DefinitionDiagnosticParityTests.WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts` |
| Proposed-state refusal and no stage writes | `ChangeSetEngineTests.R7E_MissingPrefabCandidatePreservesCanonicalDiagnosticWithoutMutation`, `R7E_ProposedInvalidStateBlocksEveryContributorUnderBestEffort` |
| Valid repairs and cumulative proposals | `R7E_ProposedPrefabRepairStagesWithoutWritingAsset` (set/assign), `R7E_MultipleOperationsValidateFinalDependencyOrderedState`, `R7E_AssignCollectionValidatesAppendAndSubsequentIndexRepair` |
| History inverse may restore pre-repair state | `R7E_HistoryCanUndoARepairBackToInvalidDefinition` |
| R7-C generated voice request | `Hollowmere.R7_E.GeneratedVoiceEnrollmentTests.AttachGenerated_EnrollsVoiceForDialogue_AndHistoryUndoRestoresBankPreimage` (missing-entry and wrong-existing-entry cases) |

## Verification

Rules gameplay TRX: **309 passed / 0 failed / 0 skipped**, `artifacts/studio/verification/W-PLUG-10/r7-e-rules/r7-e-rules.trx`.

Final code checkpoint: `aadc1044` (validator seams `c38ed6df`, voice enrollment `aadc1044`). Final desktop EditMode XML: **181 passed / 0 failed / 0 skipped / 0 inconclusive**: core 104, UI 69, R7-C 6, R7-E 2. Wrapper exit 0, 482 seconds, one attempt. [Machine-checked receipt](../../../artifacts/studio/verification/W-PLUG-10/r7-e-result.json) retains all three identical diagnostics plus source/XML/TRX SHA-256 values. Metadata: 42 packages / 91 assemblies; C# policy: 1,251 files, both pass. No Rust paths changed or Rust suites run.

W-PLUG-10 is **PASS**. Only this row's disposition changes; regenerated aggregate totals are **50 PASS / 17 BLOCKED / 1 FAIL (68 rows)**. The other 67 row records and all scenario/requirement cells remain unchanged. Shared P1.6/P2.1 notes remain untouched because this micro-packet's exclusive documentation path is this note.

The reproducible `W-PLUG-10/run-r7-e.sh` uses `unity-batch.sh` with one host allocation and the existing R7-C batch graphics adapter on desktop `:1`. The requested `Core|Ui` filter is supplemented with actual `Edit|UI` package namespaces, so the existing core and UI suites are not silently omitted. Results come from XML, never stdout.

The voice cases were executed with only the new bank-enrollment step removed: **0 passed / 2 failed**. Missing enrollment fails name-based bank resolution; wrong existing enrollment retains the SFX clip instead of the generated Maren voice. XML: `r7-e-voice-before-executed/results.xml`. The first case also exposed an empty-scene-setup teardown error, fixed by not restoring an empty setup. Two preceding compile-only attempts exposed unsupported Unity NUnit `NonParallelizable` and missing test namespace/asmdef references; their logs remain retained and are not counted as executed product tests.

Initial integrated Xvfb XML: **176 passed / 5 failed / 0 skipped**. Four failures exposed `StagedOperation.Add` normalization of canonical gameplay findings; the proposal-level diagnostic path fixes that without editing excluded engine types. The fifth was the unchanged `P42_UI_02_GraphicalClampedOriginReportsTimeout`, which explicitly requires the desktop panel/window-manager clamp absent from Xvfb. The complete final run uses desktop `:1`; the test is not skipped or weakened.

## Requests to other packets

None for the commissioned R7-C requests.

## Left open

No commissioned request remains open. This is a fake-transport agent acceptance path, not a paid worker or installed companion qualification; no such operation was authorized or performed.

Projection scope: existing definition targets edited by built-in `set`/`assign`. Creation/deferred targets and custom tools retain their existing validation paths. Validators receive transient copies; validators requiring the validated object's persistent AssetDatabase path need a projection-aware adapter. No such adapter is introduced by this micro-packet.
