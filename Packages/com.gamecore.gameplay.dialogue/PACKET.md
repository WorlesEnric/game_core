# R3-D gameplay catalog gaps

Branch `codex/r3-d`, base `ed760892`. Host: Linux build host, this clone only.
No paid operations; retained P3.2 JSON and fake media provider only.

## Evidence and regression mapping

- D9: retained `artifacts/studio/workflows/P3.2/runs/robe2-20261005T070105Z/robe/candidate.json` selects Maren and MarenEntity but falls back to tint. `D9_RetainedRobeRequestHasTextureBindingTool` requires the missing operation.
- D10a: retained `narrative-20261005T072920Z/odd-line/outcome.json` explicitly cannot reference newly created `shrine_lit`. `D10a_RetainedShrineFactHasTypedConditionTool` requires the typed fact condition seam.
- D10b: retained same run's `hud/outcome.json` explicitly asks for a current-stage-title source. `D10b_RetainedHudRequestHasStageTitleSource` requires it.
- D7: `D7_PairedProviderIsFoundByPublicVoiceTool` invokes the public three-argument tool through a fake `ScriptableObject` session implementing `IMediaGenerationGatewayProvider`; no explicit gateway argument bypasses lookup.

## Requests to other packets

R3-A: In `Packages/com.gamecore.studio.core/Editor/{Engine/ChangeSetEngine.cs,Core/AuthoringRefResolver.cs,Tools/ReflectedTool.cs}`, add candidate-local dependency-ordered output resolution to `AuthoringRefResolver.Resolve(AuthoringRef)` / reflected argument binding: resolve a fact AuthoringRef by its candidate-assigned authoringId from an earlier successful `dialogue.setFact` result during stage/apply, reject unknown/forward/cyclic/wrong-type refs before writes, and preserve that identity for history replay; R3-D supplies optional `DialogueTools.SetFact(..., string authoringId = "")` for the candidate-assigned identity and `DialogueTools.SetFactCondition(ConditionSetDefinition condition, FactDefinition fact, CompareOp comparison = CompareOp.Equal, int value = 1)` with a typed fact argument.

Integration owner (the brief assigns no R3 owner for baked game assets): run `Hollowmere.GameplayAuthoring.HollowmereGameplayAuthoring.AuthorAndBake()` (`games/hollowmere/Assets/Hollowmere/Player/Editor/HollowmereGameplayAuthoring.cs`) and `Hollowmere.NarrativeEditor.HollowmereNarrativeAuthoring.AuthorAndBake()` (`games/hollowmere/Assets/Hollowmere/Rules/Editor/HollowmereNarrativeAuthoring.cs`) after wave integration; retain updated entity definition `contentStamp`/`materialTextures` fields plus `games/hollowmere/Assets/Hollowmere/World/{Hollowmere.manifest.asset,Catalog/Hollowmere.bake.json}` and reconcile the existing narrative migration outputs listed in `../com.gamecore.gameplay.entities/Documentation~/R3-D/rebake-paths.txt`; all 14 entity content stamps change from adding the authored binding list, while recipe revisions and catalog fingerprint stay unchanged.

## Left open

- D10a end-to-end same-change-set resolution is owned by R3-A; this packet cannot edit Studio core's resolver, model or engine under the exclusive path rule. A typed fact remains representable; this is not a claim that the unchanged engine can stage forward asset references.
- Persisted game rebake outputs belong outside this packet's exclusive paths. The 52 files written by the existing authoring/bake suites were restored before PlayMode; the exact integration request and retained path list above identify the required follow-up.
- The existing P1.5 graphics preview is skipped by its GP-UI-008 guard under the required `-nographics` invocation; it is not counted as passed.

## Verification

Production code revision tested: `4756ee79` (base `ed760892`). All checks ran on this Linux build host; one Editor at a time through the host-wide wrapper.

| Check | Result | Evidence |
|---|---|---|
| Before fixes, retained regressions | 1 passed (D7), 3 expected failures (D9/D10a/D10b) | `../com.gamecore.gameplay.entities/Documentation~/R3-D/before.xml` |
| Rules after changes | 309 passed, zero failed/skipped | `../com.gamecore.gameplay.entities/Documentation~/R3-D/rules.trx` |
| EditMode `Hollowmere\.P1_[345].*|Hollowmere\.R3_D.*` | 52 passed, zero failed, 1 graphics-only skip; R3-D 16/16 passed | `../com.gamecore.gameplay.entities/Documentation~/R3-D/editmode.xml` |
| P1.3/P1.4 PlayMode | 2 passed, zero failed/skipped, using committed Hollowmere assets | `../com.gamecore.gameplay.entities/Documentation~/R3-D/playmode.xml` |
| Package metadata | Pass: 42 packages, 91 assemblies | `python3 tools/check_package_metadata.py` |
| C# policy | Pass: 1,144 C# files | `python3 tools/check_game_core_csharp.py` |
| Schemas | Regenerated with the emitter; no diff | `python3 tools/studio/emit_studio_schemas.py` |
| Gameplay catalog sample | Generated from the actual registry, 6 relevant tools and object types | `../com.gamecore.gameplay.entities/Documentation~/r3-d-tool-catalog.json`; generator test `D9_D10_CatalogExportHasTypedReferencesAndSources` |

Two cold baseline Editor attempts timed out before dispatch with no XML. A copied shared host warm cache (66/66 pinned Unity metadata hashes verified; no sibling clone accessed) allowed the baseline run to finish. Managed stack diagnosis identified `LinuxStandalone.TargetExtension.InstallToolchainIfIl2CppPresent` waiting in `Thread.Sleep`; the subsequent warm run completed. Startup also emitted CoreBusinessMetrics SQLite I/O messages, unrelated to the test verdicts. The complete attempt logs/stacks remain in this clone's `.unity-logs/`. No timeout or skip is counted as a passing test.

### Additional regressions

- D9: `D9_ImportedTextureTypedChangeSetUndoRedoAndBakeImpact` imports PNG bytes through the retained-artifact engine path, applies a typed texture ref, then uses journal undo/redo and checks content versus recipe stamps. `D9_MaterialSlotPresentationDoesNotMutateSharedMaterialAndReappliesOnSpawn` checks slot isolation, retained tint, unchanged shared material and two fresh view instances. Five `D9_InvalidBindingRefusesWithoutMutation` cases cover invalid renderer/slot/property; `D9_ClearIsUndoableAndUnsavedTextureRefuses` covers clear/Undo and non-imported texture refusal.
- D10a: `D10a_TypedFactConditionAppliesThroughEngineAndUndoes` proves typed fact binding and journal history for an existing fact. `D10a_CandidateAssignedFactIdIsStableAndDuplicateIdsRefuse` proves the producer identity seam without claiming core candidate resolution.
- D10b: `D10b_StageTitleTracksFirstActiveQuestAndClearsOnCompletion` covers first-active selection, source validation, stage transition and completion/empty clearing.
- D7: dotnet `D7_PairedProviderLookupTracksCurrentGatewayWithoutCaching` and `D7_AmbiguousProvidersRefuseWithoutCallingEitherGateway`, plus the public Unity tool regression reading the retained `voice-line/dialogue-generateVoice.json` refusal.
