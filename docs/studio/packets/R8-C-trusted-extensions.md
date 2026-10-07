# R8-C — reviewed trusted extensions and literal lever walkthrough

This is the packet's PACKET.md. Branch `omp/r8-c`. Scope: game-owned authoring registry, the Boot extension seam, excluded lever authoring sources, R8-B lever driver, plugin guide, one registry decision, and W-DOC-02 evidence. Installed companion and installed etosd are never modified or restarted. No paid operations are authorized.

## R2 fixes

| Finding | Fix | Regression / acceptance |
|---|---|---|
| R2-38 / SR-12.2 / R8-B trusted integration request | Replace the single private smoke tuple with immutable game-owned reviewed declarations, binding package, smoke identity and extension assembly/type. Candidate packages cannot register entries. | `Hollowmere.R8_B.LeverAdmissionProbe.R2_38_LeverRegistrySurvivesRecreationAndRefusesForeignDispatch`; literal walkthrough results recorded below after execution. |
| R2-14 / SR-12.2 | Compose the lever using the normal world extension path; attach restored sessions without reseeding; exercise committed off/on/off changes on normal game frames while preserving the creator's checkpoint. | `Hollowmere.R8_B.LeverWalkthrough`, extended R8-B driver; signed service verdict, actual creator coordinator Admit, restored nine-coin witness, lever states, normal History undo. |

## Authority and authoring

Candidate sources live under `Assets/Hollowmere/Mechanisms/Lever~/`, excluded from Unity imports. The guide generator writes a distinct `com.hollowmere.mechanism.lever` candidate outside the project. Only full authenticated companion staging followed by explicit creator Admit installs it under the project's Packages directory. The trusted registry is reviewed game code, not candidate metadata or a registration operation. `RunAdmittedSmokeEntry(StageVerdict, string, string, int)` retains its existing signature.

The walkthrough uses a private node with no models, workers or operation providers, a companion built from this checkout, a separately provisioned owner/version cache, and the host Unity allocator. It never reads credential file contents; etos pairs the scratch app and the production client resolves that pairing. No installed service or sibling clone is used.

## Verification

Execution results pending. W-DOC-02 remains FAIL until all seven signed service steps, creator Admit, real restored Play with the committed lever transitions, graphical capture and normal undo are observed. Probe passes and unsigned diagnostic stages do not qualify the row.

- Rules dotnet: 309 passed, 0 failed, 0 skipped (retained TRX).
- Locally built companion: fmt/clippy pass; cargo test 145 passed, 12 ignored. Ignored stage/node fixtures are not acceptance.
- Initial policy gates: 42 packages / 91 package assemblies and 1,266 C# files pass.
- Initial Unity compile caught the driver reference `GameCore.Gameplay.UI`; corrected to the actual `GameCore.Gameplay.Ui`.
- First executed requested EditMode suite: 110 total, 106 passed, 4 failed, zero skipped/inconclusive. Both existing admission-in-Play cases and the new registry test passed. Full XML retained as `editmode-initial-failures.xml`.
- Composed-registry rerun: 110 total, 107 passed, 3 failed, zero skipped/inconclusive. `AuthorAllIsIdempotent` now passes after the initial run applied its missing voice-bank journal step. The same three R5-A projection cases remain failed; no assertions were changed.
- Candidate attempts A–D failed honestly before admission: missing command-reader import (semantic SG012), missing Derivation/import dependencies, Unity Entities' required Collections reference (DC0061, recovered from retained Bee diagnostics), then two real PlayMode failures from the uninitialized slot. The mechanism recipe description now uses `.description.json`, distinguishing it from the registered game's tracked `.catalog.json` world snapshot.
- Candidate E passes all seven Docker stage steps after explicit new-slot initialization: six Rules, 16 EditMode and two PlayMode tests. Its first graphical admission driver failed before Admit because it retained only four wire fields rather than the complete `StageCandidateRequest`; the corrected driver exports `BuildStageRequest` directly.
- Stage submission is now outside Unity through a small production-client dotnet executable after the source Editor releases its lease. This prevents holding a source Editor while the companion's sandbox preflight starts. Final qualification uses this serialized path, not the earlier attempts.

## Requests to other packets

The R5-A dialogue/history regressions fail at ordinary candidate staging before admission: `HistoryReopenTests.R5_02_OddWitness_UndoesAfterEditorReopenAndDomainReload`, `UndoRegressionTests.R5_02_OddWitness_FinalPostimageUndoesAfterRuntimeReopen`, and `R5_02_FinalPostimageStillRefusesLaterExternalEdit`. Diagnostics include GP-DLG-003 edges 3→8 and 8→4 outside the projected graph, dangling node 3→8, and GP-DLG-005 unreachable node 4. Request to the core edit owner: `Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs`, candidate Stage projection must retain earlier compose/add-node output when validating a subsequent whole-graph set in the same candidate. No contract change, validation bypass or test rewrite is requested. This is outside R8-C ownership; the unchanged failure is retained rather than suppressed.

The initial P3.1 idempotency case applied the missing `media.voices-bank-28` step in this fresh checkout. Its journal/content preparation and the final rerun are tracked separately from the lever acceptance. Shared historical notes outside the exclusive set remain unchanged; this note supplies their integration mapping.

## Left open

No completed acceptance claim yet. Runtime verification and evidence recording are in progress.
