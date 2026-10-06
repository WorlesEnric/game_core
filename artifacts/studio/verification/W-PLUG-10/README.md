# W-PLUG-10: Definition validator parity (inspector, validator, agent)

Verdict: **PASS**. R7-E closes R7-C's Studio seams. Code checkpoint `aadc1044` (validator implementation `c38ed6df`); Linux Unity 6000.0.75f1, graphics-enabled batch Editor on desktop `:1`, through the host-wide `unity-batch.sh` allocator. No paid operation or installed companion access; only remote transport/worker is fake.

## Exact acceptance

One real indexed `EntityDefinition` has a valid identity and no required prefab. The actual `ContextPanelView` generated inspector renders the validator diagnostic. `ValidatorConsole` runs against the same runtime/index. A real `EtosAgentGateway` request imports a fake-companion candidate changing that definition's scale, then enters production candidate staging. The candidate is refused; original scale/prefab remain unchanged.

All three structured diagnostics are identical:

- Code: `GP-ENT-006`.
- Message: `definition Temporary_c33ca313af80403280bc112b3d00f528 has no prefab`.
- `where.authoringId` and `data.subject`: `c72adddf-5a01-4d48-8508-4c758124ecd5`.
- Full canonical `where` also retains the same asset GUID, GlobalObjectId and path, without stamp/scope.

[Machine-checked observations and source/receipt hashes](r7-e-result.json) are extracted from the actual [final XML](r7-e-final/results.xml), not constructed from expected strings. Case: `Hollowmere.R7_C.Validation.DefinitionDiagnosticParityTests.WPlug10_SameInvalidDefinition_HasCodeMessageAndLocationParityAcrossAllFronts`.

## Verification

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core EditMode | 104 | 0 | 0 |
| UI EditMode | 69 | 0 | 0 |
| R7-C EditMode | 6 | 0 | 0 |
| R7-E generated-voice EditMode | 2 | 0 | 0 |
| Gameplay Rules dotnet | 309 | 0 | 0 |

Final Editor: **181/181**, exit 0, 482 seconds, one attempt. [Rules TRX](r7-e-rules/r7-e-rules.trx). [Static checks](r7-e-static.txt): 42 packages / 91 assemblies; 1,251 C# files, pass. Exact package dependencies remain unchanged.

Seven engine cases cover canonical refusal, proposed-state set/assign repair, dependency-ordered final state, cumulative collection edits, no staging writes/copy leaks, BestEffort refusal of invalid proposals, and normal History undo back to a pre-repair invalid definition. Two generated-voice cases prove graph attachment, name-based bank enrollment/replacement as Voice (volume 1, loop/spatial false), and durable normal History restoration after clearing Unity Undo. No media generation occurs.

## Retained failures

- [Original R7-C XML](r7-baseline/results.xml), checkpoint `7ad4a809`: inspector omitted `where`; candidate accepted the same invalid definition with no diagnostics. This remains failing historical evidence.
- [Executed pre-fix voice XML](r7-e-voice-before-executed/results.xml): with only the new bank-enrollment step removed, missing-entry and wrong-existing-entry cases both fail. One teardown also encountered an empty scene setup; fixed before final execution.
- `r7-e-voice-before` and `r7-e-voice-before-run`: compile-only fixture setup errors (Unity NUnit attribute, imports/assembly references), not executed test counts.
- [First integrated XML](r7-e-integrated/results.xml): 176 pass / 5 fail. Four exposed legacy operation-diagnostic normalization of GP codes; canonical findings now stay in proposal-level diagnostics. One unchanged window-clamping test requires the desktop window manager missing from Xvfb; it passes on `:1` in the complete final run.

## Reproduce

```bash
DISPLAY=:1 bash artifacts/studio/verification/W-PLUG-10/run-r7-e.sh r7-e-final
python3 artifacts/studio/verification/W-PLUG-10/verify-r7-e.py
```

The launcher supplements the packet's `Core|Ui` spelling with actual package namespaces `Edit|UI`. It requires the exact parity case to appear and pass. Each attempt retains its own directory; use a new label for a new run. The verifier checks every selected case and all three canonical observations. See [R7-E PACKET](../../../../docs/studio/packets/R7-E-validator-parity.md) for contracts and scoped limitations. Other row dispositions are unchanged.
