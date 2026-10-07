# R8-B — guide lever and generated texture

This is the packet's PACKET.md. Branch `omp/r8-b`, base `7f5cacb`. Exclusive rows: W-DOC-02 / SR-12.2 and W-ETOS-07 / SR-4.6. Installed companion and etosd are not modified or restarted. Paid ceiling: one image at operator USD 0.20, no describe or TTS, total at most USD 0.30.

## R2 fixes

| Finding | Decision | Test / evidence |
|---|---|---|
| R2-38 / SR-12.2 | Keep the new-lever requirement. Correct 09's stale pressure-plate recovery claim and identify the trusted-game prerequisite that prevents a distinct lever package from completing admission. No product source outside this packet is changed. | `Hollowmere.R8_B.LeverAdmissionProbe.SR_12_2_NewLeverSmokeIsBlockedBeforeWorldAccess`; graphical prerequisite driver under `Tests/R8_B/Lever`. This refusal probe is not a signed-stage or admission pass. |

## Requests to other packets

### Trusted new-mechanism integration (W-DOC-02)

**Owner file:** `games/hollowmere/Assets/Hollowmere/Authoring/Editor/HollowmereStudioAdmission.cs`, `HollowmereAdmittedSmoke.Entries` and `RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)`.

The private trusted registry has exactly one tuple: `Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke`, `Begin`, `com.hollowmere.mechanism.pressureplate`. `Poll` rejects an unknown type/method before accessing the world. Using the old type with a new package instead fails its package-ownership comparison. No creator registration API is exposed. This blocks **every distinct lever package**, independently of cache provisioning, authenticated signing, or candidate correctness.

**Required seam:** provide a trusted, reload-stable new-mechanism integration for `com.hollowmere.mechanism.lever` / `Hollowmere.Mechanism.Lever.LeverSmoke.Begin`, retaining the existing `RunAdmittedSmokeEntry` signature and exact package binding. It must attach the admitted mechanism to the restored game through the supported game/world extension path, advance only on normal game frames, and observe a real committed lever off → on → off transition plus preserved checkpoint state. Return Pending until those assertions finish, Failed on a missing package/entry/root or failed assertion, and Passed only on completion. Keep candidate initialization hooks forbidden; do not invoke an arbitrary candidate-named callback, spoof the plate identity, replace the restored root with the sandbox smoke world, or disable the trusted smoke. Normal admission undo must detach/remove the mechanism and restore the original catalog.

The game owner may generalize this as a reviewed trusted extension registry instead of a fixed lever entry; that design remains outside R8-B ownership. The consumer seam remains the existing admission smoke callback. This packet does not implement a product shim in its test assembly.

## Left open

- W-DOC-02 is still FAIL. A distinct new lever cannot complete the trusted game's fixed smoke dispatch. No unsigned CLI run, parsed probe record, renamed maintained sample, or passing refusal test is counted as the required authenticated Stage → creator Admit → restored working lever → undo exercise.
- The probe intentionally uses a parsed **untrusted** record solely to reach the registry lookup. It never calls Admit, attaches companion authority, imports candidate code or bypasses D1/D2. It is not a substitute for the signed path.
- Shared historical P0/P1/P2/P4.3 packet notes and `docs/studio/07-verification-matrix.md` are outside R8-B's exclusive file list. Their owners should consume this note's R2-fixes section; only the two commissioned ROWS.json entries and their evidence records are updated here.
