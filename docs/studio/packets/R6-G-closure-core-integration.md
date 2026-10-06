# R6-G closure core integration

This is the packet's PACKET.md. Branch `omp/r6-g`, baseline `99197193`; implementation checkpoint `b90623ac`. Ownership: core engine, engine tests, R6_F integration harness, and this note. No gameplay package, package manifest, or assembly definition changed. No installed companion, etosd, paid operation, credential file, sibling clone, or graphical Editor was touched.

## R2 fixes

| Finding / request | Fix | Regression |
|---|---|---|
| R6-F request 1 / P4.2f request 2: generic graph bypasses enrolled creator | `PreparedCreation.Plan` routes `create` with `type == dialogue.graph` through the existing fixed `NpcCreationAdapter, GameCore.Studio.Gameplay.Editor`. R6-F's prepared graph creator enrolls the graph and retains the content-set inverse before writing. A missing or nonconforming creation adapter, or an adapter returning no plan, refuses with `NotConfigured`; no generic fallback. | `R6_G_Request1_GenericDialogueUsesTrustedPreparation`; `R6_G_Request1_AbsentCreationAdapterRefusesPrecisely`; unchanged `R6_F_Request2_UnchangedCandidateStartsPresentsBellEndsAndDurableUndoRestores20`. |
| R6-F request 2: deferred NPC operations bypass closure validation | `ChangeSetEngine.StageCore` invokes the fixed first-party `DialogueClosureTools.ValidateCandidate(StudioRuntime, ChangeSet)` through assembly-qualified reflection and merges diagnostics before per-operation staging, including the deferred-operation early exit. Missing/incompatible validators produce `NotConfigured`; exceptions or null results produce `StageFailed`. Unrelated core operations do not require gameplay installation. | `R6_G_Request2_AbsentClosureAdapterRefusesDeferredNpc`; strengthened `R6_F_Request2_ValidatorRejectsExistingUnenrolledGraph` now also exercises Engine.Stage and Engine.Apply refusal, verifies 20 entities remain, and verifies the graph remains unenrolled. |

The existing gameplay validator recognizes only `dialogue.createGraph` producers. Core passes a validation-only projection of its fixed generic-dialogue dispatch: only those producer tool IDs become `dialogue.createGraph`, with identity, arguments, dependencies and preconditions preserved. Candidate operations, retained input bytes, execution and journal remain generic `create`. This keeps the gameplay producer predicate outside this packet's exclusive paths while supplying the validator the actual enrolled-creation semantics. No registry, candidate-provided type name, or gameplay assembly/package dependency was added (ADAPT-SPLIT).

The retained integration test was not weakened or replaced. R6-F's baseline XML already documents its failure at `GP-DLG-014: no baked dialogue graph`; the same unchanged-candidate test now passes. It stages/applies the original candidate bytes, creates entity 21, enrolls its graph, bakes, awaits `RecompileScripts(false)`, starts real Play dialogue, presents the bell line, ends, exits Play, then performs durable journal undo restoring 20 entities, removing the graph, and restoring exact content-set bytes. The new generic-preparation regression would return no plan before the dispatch change; the strengthened engine refusal assertion would accept the previously bypassed candidate before the StageCore hook.

## Verification

All runs used this Linux clone and held at most one batch Editor through `studio/tools/unity-batch.sh`. Results below come from XML/TRX, not the wrapper summary.

- Rules: **309 passed, 0 failed, 0 skipped**. Command: `~/.dotnet/dotnet test dotnet/tests/GameCore.Rules.Gameplay.Tests/GameCore.Rules.Gameplay.Tests.csproj --logger trx --results-directory /tmp/r6-g-dotnet`.
- Final requested Unity/Core/P3_2/R6_F run: **110 passed, 1 failed, 3 skipped, 0 inconclusive; 114 total**. All **9 ChangeSetEngineTests passed**, including all **3 R6-G regressions**. All **3 R6-F tests passed**. P3_2: **17 passed, 3 opt-in live-node skips**.
- The single final failure is `R2_38_CORE_PICK_500CandidatesMedianAcrossEditorFramesBelow50Ms`: its sole-Editor prerequisite expected 1 Editor and observed 2. Another packet owns the graphical Editor; this packet neither stops it nor weakens the benchmark. This is not an all-green core-suite receipt.
- Initial expanded run: **109 passed, 2 failed, 3 skipped**. One failure was the same sole-Editor prerequisite; the other was the new test using NUnit `Has.Count` against an array. Corrected to assert the `IReadOnlyList.Count` value; the regression passes in the final XML.
- `python3 tools/check_package_metadata.py`: pass, **42 packages / 91 assemblies**, no dependency changes.
- `python3 tools/check_game_core_csharp.py`: pass, **1,231 C# files**.

Final command (absolute project/log/result paths under this clone):

```sh
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.unity-logs" --label r6-g-verified \
  --results "$PWD/.unity-logs/r6-g-verified.xml" -- \
  -runTests -testPlatform EditMode \
  -testFilter 'GameCore\.Studio\.Core.*|GameCore\.Studio\.Edit\.Tests\.ChangeSetEngineTests.*|Hollowmere\.R6_F.*|Hollowmere\.P3_2.*'
```

The requested `GameCore.Studio.Core` pattern selects the core test assembly, including the unrelated picking benchmark. The added namespace pattern explicitly names the edited engine fixture; it does not exclude other core tests. The first run used the broader `GameCore.Studio.Edit.Tests.*` addition and selected the same 114 cases.

Receipts are retained in `games/hollowmere/Assets/Hollowmere/Tests/R6_F/Evidence~/r6-g-{initial,verified}.xml` and `r6-g-rules.trx`. Full local Unity logs are `.unity-logs/r6-g-final-20261007T024018-3513176-a1.log` and `.unity-logs/r6-g-verified-20261007T024512-3547299-a1.log`. No generated authored/baked fixture changes are delivered. Runner-created metadata is removed and its deleted tracked metadata restored byte-for-byte after the runs.

## Requests to other packets

None required for the two hooks or unchanged-candidate acceptance. The validation-only projection implements this side of the existing producer seam without editing `Packages/com.gamecore.studio.gameplay/Editor/DialogueClosureTools.cs`. Shared historical P0/P1/P2 notes are intentionally unchanged because this micro-packet's exclusive documentation path is this note.

## Left open

- The sole-Editor picking benchmark needs its existing solo-unity/idle-host qualification lane. Both XMLs retain the prerequisite failure (2 Editors instead of 1); another packet's graphical slot may not be stopped by R6-G. The requested broad suite therefore remains **110 passed / 1 failed / 3 skipped**, not green.
- The three opt-in P3_2 live-node scenarios were skipped as designed; no paid or installed-service qualification is claimed.
