# R8-B new-lever prerequisite

W-DOC-02 remains **FAIL**, unchanged requirement: developer adds a new lever from the guide, signed Stage, explicit creator Admit, restored working Play world, undo.

Product base `7f5cacb8`; probe checkpoint `0adcdda2` with its corrected assembly reference/window-capture driver. The trusted game's `HollowmereAdmittedSmoke` accepts only the pressure-plate tuple. The new lever tuple returns Failed before world access; borrowing the sample's smoke entry with the new package returns its package-ownership refusal. The [exact external owner request](../../../../../docs/studio/packets/R8-B-guide-and-texture.md#requests-to-other-packets) identifies the existing callback signature and required trusted game integration. No product file outside R8-B ownership was patched.

## Executed

- `results.xml`: **1 Passed, 0 Failed, 0 Skipped, 0 Inconclusive**, `Hollowmere.R8_B.LeverAdmissionProbe.SR_12_2_NewLeverSmokeIsBlockedBeforeWorldAccess`. This asserts the two refusal boundaries and zero smoke registrations/steps. It is a passing *blocker reproduction*, not acceptance.
- `graphical-probe.json`: same two refusals in the actual non-batch Editor on `:1`, NVIDIA RTX 4060 Ti. This invokes the trusted dispatch function with a parsed **untrusted** record, never authenticates a verdict, calls Admit, imports code, or attaches a fake world.
- `graphical-probe.png`: retained actual display capture is **obscured by the existing GNOME failed-session/notification surface**. Visual inspection therefore does not establish a visible lever, restored world or even readable probe panel. Neither FFmpeg window capture nor an attempted XComposite pixmap capture resolved that desktop obstruction. No desktop service was restarted; the ineffective capture workaround was removed.
- `graphical.log.gz`: final graphical Editor exited normally, zero generation calls. Earlier capture attempts and setup logs are retained separately.

## Reproduction

```sh
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/.evidence/r8-b/lever-tests" --label lever-tests \
  --results "$PWD/.evidence/r8-b/lever-tests/results.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'Hollowmere.R8_B.LeverAdmissionProbe'
python3 games/hollowmere/Assets/Hollowmere/Tests/R8_B/Lever/run-probe.py \
  "$PWD/.evidence/r8-b/lever-fresh"
```

The graphical launcher requires a fresh output directory and absent project ETOS settings. It supplies a deliberately missing scratch pairing path, preventing ordinary startup from resolving installed credentials, and shuts down only its owned Editor after the capture. Inspect the capture rather than treating a PNG file as visual success.

## Retained setup failures

The first fresh import reached Unity's safe-mode prompt because the probe asmdef omitted `GameCore.Unity.App` (the constructor's SaveService type). The initial graphical log stopped before printing compiler diagnostics; a subsequent batch launch reported CS0012. Adding that reference produced the passing XML. The first two screenshot launchers could not uniquely identify the probe window (docked window, then both native and decorator title matches); the corrected native-window selector completed. These are harness setup failures, not additional product findings, and they do not replace the single new-mechanism integration blocker.

No new lever candidate, signed lever verdict, admission, restored lever behavior or undo is claimed. The maintained sample's historical signed success remains in W-MECH-01, not this row.
