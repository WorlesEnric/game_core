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

## Left open

- D10a end-to-end same-change-set resolution is owned by R3-A; this packet cannot edit Studio core's resolver, model or engine under the exclusive path rule. A typed fact remains representable; this is not a claim that the unchanged engine can stage forward asset references.
- Live-node qualification is excluded by the brief. Fake-provider success establishes registration/lookup only.

## Verification

Pending host Unity regressions and requested suites. Rules: 309 passed, zero failed/skipped (TRX under dotnet/tests/GameCore.Rules.Gameplay.Tests/TestResults/r3-d-rules.trx).
