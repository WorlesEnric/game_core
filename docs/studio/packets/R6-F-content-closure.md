# R6-F content closure and worker releases

This is the packet's PACKET.md. Branch `omp/r6-f`; baseline main `7009f82`.
Exclusive ownership is the R6-F packet's path list. No installed companion or etosd operation, paid operation, credential-file read, or graphical Editor was performed.

## R2 fixes

### P4.2f request 2: narrative closure

The contract remains P1.4's explicit per-world `GameplayContentSet.definitions` list, not asset discovery at runtime. A created graph must be enrolled in the owning world's set in the same durable change set. Graph references alone do not establish membership.

The retained candidate is read byte-for-byte from `artifacts/studio/verification/W-AI-02/p42f-npc-20261006T174113.356793Z/workflow/ferryman2/candidate.json`, SHA-256 `fb19405db6643d7a0243826aee8ce33ff92a049af23cf990c82abba700045d70`, ID `cs_01M49500E9KFTHBKGBCXWW3VZC`. Its graph operation is generic `create`, not a dialogue-specific tool. No worker output is repaired or replaced.


`dialogue.createGraph(name, path, fields)` in the trusted Studio gameplay Editor assembly is the reachable enrolled creator. Its prepared plan captures the content-set definitions before the first write, restores the list before deleting the new graph, and retains graph identity/path and owning set for replay. `DialogueContentClosure` resolves the active scene's unique authored region to its unique owning world and unique content set; it refuses ambiguity instead of guessing from directories. Membership follows the bake's transitive narrative references.

`DialogueClosureTools.ValidateCandidate(StudioRuntime, ChangeSet)` refuses NPC bindings outside that closure with `InvalidArgs` and `npc_dialogue_not_enrolled`, including graph and set witnesses. It accepts existing closure members or dependency-ordered `dialogue.createGraph` producers. The public graph tool invokes it during staging. Generic NPC-only changes still need the core hook below. Worker instructions remain unchanged: no new worker output is needed once generic-create dispatch is integrated.

### P4.2f request 3: stored worker definitions

Changed immutable releases now invoke supported `etos agent upgrade --link <release-directory>`. Configuration-only reapplication retains restart behavior. Rollback also upgrades to the retained directory, restoring the old stored instructions rather than only the binary symlink. Failed registration rollback invalidates the no-op marker and reports required operator reconciliation. Same-release repeats verify checksums and make no node calls.

Upstream `etos-studio/docs/operator.md` (agent lifecycle), `crates/etcli/src/agent.rs` (Upgrade arguments), and `crates/etnode/src/agents/mod.rs` (worker activation and upgrade) establish the supported command. Upgrade preserves worker image/network but replaces instructions/model/budget. `etos --json worker list` returns the manifest-shaped budget; runtime budget accounting separately uses micro-dollars.

Before registration the installer refuses a missing existing worker or a budget differing from the unchanged deployment manifest. It never silently removes or raises an operator ceiling. Manifest-authority changes still refuse. Operator reconciliation: retain the approved worker budget in an operator-owned deployment manifest and register that package with `etos agent upgrade --link <approved-package-directory>`; retry the installer with the identical manifest. This is an explicit operator action, not an action taken by this packet.

Regression: `test_r6_f_worker_change_updates_registration_preserves_policy_and_noop` fails against baseline `release` because stored instructions remain old; passes after the fix. Additional regressions cover health rollback, partial activation, rollback failure, missing workers, budget drift and manifest authority.

## Requests to other packets

`Packages/com.gamecore.studio.core/Editor/Engine/PreparedCreation.cs`, `Plan(EditContext)`: extend the existing fixed first-party `NpcCreationAdapter` dispatch to `tool == "create" && context.StringArg("type") == "dialogue.graph"`. Generic graph creation currently bypasses the gameplay adapter. R6-F cannot edit this file. Registry replacement is not an alternative: `ToolRegistry.Register` refuses built-in shadowing and `Find` prioritizes built-ins.

`Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs`, `StageCore`: invoke fixed trusted `GameCore.Studio.Gameplay.DialogueClosureTools.ValidateCandidate(StudioRuntime runtime, ChangeSet changeSet)` from `GameCore.Studio.Gameplay.Editor` and merge its `IReadOnlyList<Diagnostic>` before any writes, including deferred NPC operations. No generic candidate-validation extension exists in runtime options or the service registry. When the generic creation dispatch lands, extend `DialogueClosureTools.ValidateCandidate`'s producer predicate to accept `create` with `args.type == "dialogue.graph"` as well as `dialogue.createGraph`; accepting it now would falsely promise enrollment that core does not perform.

## Verification

- Rules dotnet suite: 309 passed, 0 failed, 0 skipped; includes dialogue and NPC. Command: `dotnet test dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj --logger trx --results-directory /tmp/r6-f-dotnet`.
- Installer and worker pytest: 20 passed, 2 subtests passed. Installer 14 cases; worker 6 cases. Python dependencies installed only in `/tmp/r6-f-pytest`.
- Installer baseline experiment: unchanged new regression against baseline `release` fails once at stored worker instructions; no node accessed.
- Final Unity XML: **19 passed / 1 failed / 3 skipped**, 23 cases. Existing P3_2: 17 pass, 3 opt-in live-node skips. R6_F: 2 pass, 1 integration failure. Executed with `studio/tools/unity-batch.sh --project <clone>/games/hollowmere --log-dir <clone>/.unity-logs --label r6-f-final --results <clone>/.unity-logs/r6-f-final.xml -- -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R6_F.*|Hollowmere\.P3_2.*'`. Only one owned batch Editor ran at a time; no graphical slot used.
- `R6_F_Request2_GraphCreationEnrollsAndHistoryRestoresClosure`: actual enrolled graph creation → bake → compiled catalog → real Play → `DialogueRunner.Start(..., out reason)` returns true → exact bell line presented → conversation ends → exit/domain reload → new Studio runtime, Unity Undo cleared → journal undo restores exact content-set bytes and deletes graph. This uses the public enrolled creator, not the retained six-op candidate.
- `R6_F_Request2_ValidatorRejectsExistingUnenrolledGraph`: direct safety-net validator returns `npc_dialogue_not_enrolled` and leaves membership unchanged. Generic candidate pipeline hookup remains outside ownership.
- `R6_F_Request2_UnchangedCandidateStartsPresentsBellEndsAndDurableUndoRestores20`: unchanged bytes stage/apply, 20→21; membership is false. After bake/catalog recompilation, actual Play returns **`GP-DLG-014: no baked dialogue graph`**. Normal durable journal undo then succeeds, restores 20 and exact content-set bytes, and deletes the graph. The test asserts success only after recording refusal and completing undo, so it remains **FAIL**, not acceptance. This confirms the missing closure rather than merely inferring it from the old generic refusal.
- Initial compile exposed two test-harness API mistakes, fixed before execution. First executed XML failed at missing closure; the next reached a stale generated-catalog boot mismatch because the harness had not awaited compilation. Final harness uses Unity Test Framework `RecompileScripts(false)` after baking and restoring fixtures; final failure is the real missing graph above, not catalog mismatch. Intermediate XMLs remain retained.
- Final Python receipt: **22 passed, 2 subtests passed** (installer 14, worker 6, Studio gameplay boundary 2). Rules: **309 passed**. Metadata: **42 packages / 91 assemblies**; C# policy: **1,231 files**, pass.

Evidence is retained under [`games/hollowmere/Assets/Hollowmere/Tests/R6_F/Evidence~`](../../../games/hollowmere/Assets/Hollowmere/Tests/R6_F/Evidence~/): final/intermediate Unity XML, rules TRX, Python JUnit XML, and baseline installer failure. Fixture backups restore only test-touched content after normal history assertions; restoration is not substituted for undo. Runner-created unrelated metadata/cache files were removed, and the tracked metadata Unity removed was restored byte-for-byte. No authored fixture or generated bake difference is delivered.

## Left open

- Unchanged retained-candidate integration requires the outside-owned core dispatch and validation hooks above. Successful isolated gameplay operation tests must not be represented as unchanged-candidate acceptance.
- The required unchanged-candidate Play acceptance is **not closed**; final XML deliberately retains its failure. Do not promote W-AI-02 or call this packet fully green. Integrate both named core hooks and the producer predicate before rerunning the retained test unchanged.
- Live installed worker qualification is prohibited by this packet's no-service-change rule. Installer evidence uses a stateful fake CLI matching the supported upstream operations.
