# W-PLUG-10: Definition validator parity (inspector, validator, agent)

Verdict: **FAIL**. R7-C now exercises all three real fronts; parity is a product defect, not merely an absent driver.

Source checkpoint `7ad4a809`; Linux Unity 6000.0.75f1 graphics-enabled batch Editor. No paid operation or installed companion is involved: only the remote transport/worker is fake.

## Evidence

- [Executed XML and all three observed diagnostics](r7-baseline/results.xml), case `Hollowmere.R7_C.Validation.DefinitionDiagnosticParityTests.WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts`: **Failed**.
- [Shared Editor log](../W-PLUG-11/r7-baseline-tests/r7-edit-baseline-20261007T045248-3963472-a1.log).

One real, indexed `EntityDefinition` has a valid identity and a missing required prefab. The actual `ContextPanelView` inspector renders `GP-ENT-006` and the same message as `ValidatorConsole`, but its structured diagnostic omits `where`. The console includes the definition identity and subject. An actual `EtosAgentGateway` request receives a fake-transport candidate changing that same definition's scale. Production candidate staging returns **accepted=True, diagnostics=[]**. No candidate apply occurs.

The failed assertion is `agent candidate validation must report the same invalid definition: expected Length 1, actual 0`. XML output independently retains the inspector location loss; the test does not fill missing locations or inject a validator.

## Requests to other packets

Exact signatures and behavior are recorded in [R7-C PACKET](../../../../docs/studio/packets/R7-C-plugin-rows.md#requests-to-other-packets). Fix `ValidatorDiagnostics.For` in Studio UI and generic definition validation in `ChangeSetEngine.StageCore` in Studio core. Both files are outside R7-C's exclusive paths. Gameplay cannot replace built-in set/assign and has no generic validation service registration seam.

## Reproduce

Graphics-enabled batch `unity-batch.sh`, `-runTests -testPlatform EditMode -testFilter DefinitionDiagnosticParityTests`, with the R7-C batch graphics adapter and an isolated display. Preserve the failing XML until the production seams are implemented; do not reinterpret this case as a passing negative test.
