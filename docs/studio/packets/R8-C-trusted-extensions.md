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

**W-DOC-02 PASS** on source `09430b3093b8b97cd4cb878f59d38ce7a8d37743`. [Complete evidence](../../../artifacts/studio/verification/W-DOC-02/r8-c/README.md): real private-node signed seven-step Docker Stage **76.172 s**, creator Admit **40.990 s**, authenticated reload, restored nine coins, equal save roundtrips, **120 normal smoke frames** with committed lever **0/1/0**, visible runtime control on/off, readable **1280×739 Play** and **960×720 world** captures on `:1` / RTX 4060 Ti, normal History Undo **22.911 s**. Package removed, original catalog restored, zero paid/worker/voice activity.

- Rules dotnet: 309 passed, 0 failed, 0 skipped (retained TRX).
- Locally built companion: fmt/clippy pass; cargo test 145 passed, 12 ignored. Ignored stage/node fixtures are not acceptance.
- Initial policy gates: 42 packages / 91 package assemblies and 1,266 C# files pass.
- Initial Unity compile caught the driver reference `GameCore.Gameplay.UI`; corrected to the actual `GameCore.Gameplay.Ui`.
- First executed requested EditMode suite: 110 total, 106 passed, 4 failed, zero skipped/inconclusive. Both existing admission-in-Play cases and the new registry test passed. Full XML retained as `editmode-initial-failures.xml`.
- Composed-registry rerun: 110 total, 107 passed, 3 failed, zero skipped/inconclusive. `AuthorAllIsIdempotent` now passes after the initial run applied its missing voice-bank journal step. The same three R5-A projection cases remain failed; no assertions were changed.
- Candidate attempts A–D failed honestly before admission: missing command-reader import (semantic SG012), missing Derivation/import dependencies, Unity Entities' required Collections reference (DC0061, recovered from retained Bee diagnostics), then two real PlayMode failures from the uninitialized slot. The mechanism recipe description now uses `.description.json`, distinguishing it from the registered game's tracked `.catalog.json` world snapshot.
- Candidate E passes all seven Docker stage steps after explicit new-slot initialization: six Rules, 16 EditMode and two PlayMode tests. Its first graphical admission driver failed before Admit because it retained only four wire fields rather than the complete `StageCandidateRequest`; the corrected driver exports `BuildStageRequest` directly.
- Stage submission is now outside Unity through a small production-client dotnet executable after the source Editor releases its lease. This prevents holding a source Editor while the companion's sandbox preflight starts. Final qualification uses this serialized path, not the earlier attempts.
- Final required EditMode XML: **107 passed / 3 failed / 0 skipped / 0 inconclusive, 110 total**. The exact same three R5-A projection cases remain failed. All registry/admission cases and P3.1 idempotency pass.
- Final graphics-enabled batch PlayMode suite on `:1`: **10 passed / 0 failed / 0 skipped / 0 inconclusive**. The first headless run was 9/10 because the native Maren voice assertion explicitly requires graphics; the retained rerun uses R7-C's existing graphics adapter, not a weakened assertion.
- Final literal candidate: **6 Rules + 16 sandbox EditMode + 2 sandbox PlayMode**, all passed. Its retained package SHA-256 is `96fc3e24ccdb6bcb84b45d7f09f78c20d21680a1d81208a7119947dbee5dbd15`.
- Final policy gates: **42 packages / 91 package assemblies / 1,267 C# files**, pass.
- The initial graphical G run completed admission, interactions and undo but had a clipped Game View; H correctly failed a readable-size guard and was recovered through product `StageCommandLine.Undo`. Final J uses one standalone free-aspect Game View; controls are asserted attached, enabled and onscreen, and its On/Off captures were visually inspected. The immediate resize-transition `admitted-play.png` is not used as visual proof.

## Requests to other packets

The R5-A dialogue/history regressions fail at ordinary candidate staging before admission: `HistoryReopenTests.R5_02_OddWitness_UndoesAfterEditorReopenAndDomainReload`, `UndoRegressionTests.R5_02_OddWitness_FinalPostimageUndoesAfterRuntimeReopen`, and `R5_02_FinalPostimageStillRefusesLaterExternalEdit`. Diagnostics include GP-DLG-003 edges 3→8 and 8→4 outside the projected graph, dangling node 3→8, and GP-DLG-005 unreachable node 4. Request to the core edit owner: `Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs`, candidate Stage projection must retain earlier compose/add-node output when validating a subsequent whole-graph set in the same candidate. No contract change, validation bypass or test rewrite is requested. This is outside R8-C ownership; the unchanged failure is retained rather than suppressed.

The initial P3.1 idempotency case applied the missing `media.voices-bank-28` step in this fresh checkout. Its journal/content preparation and the final rerun are tracked separately from the lever acceptance. Shared historical notes outside the exclusive set remain unchanged; this note supplies their integration mapping.

## Left open

The three unchanged R5-A dialogue/history projection regressions require the core owner's fix above. They are retained as failures, not skipped or rewritten. This packet qualifies W-DOC-02 only, not every Studio row or an all-green project. Admitted-package player distribution/IL2CPP stripping is not exercised.

## Cleanup

Final normal Undo removed the admitted lever and restored the original catalog; the scratch runner stopped only its owned node/companion. Unity-generated GraphicsSettings/QualitySettings, the P3.1 memory receipt, and the orphan R6-D lock meta were restored to their pre-run tracked contents; generated unrelated folder metas/settings and Python bytecode caches were removed. Fresh creator journals/admission state remain locally, untracked and unreset; relevant journal copies are retained with W-DOC-02 evidence. The `.omp` launcher state is untouched and not committed. No throwaway candidate code was copied into live imports, no failed verdict was promoted, and no cold-grace marker was removed.

Only W-DOC-02's ROWS object, the matching 07 cell and counts are promoted. The other 67 ROWS objects retain their exact prior contents; totals are **57 PASS / 11 BLOCKED / 0 FAIL**. Shared historical packet notes are intentionally unchanged because they are outside R8-C's exclusive paths.
