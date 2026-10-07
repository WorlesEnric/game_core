# R8-B — guide lever and generated texture

This is the packet's PACKET.md. Branch `omp/r8-b`, base `7f5cacb`. Exclusive rows: W-DOC-02 / SR-12.2 and W-ETOS-07 / SR-4.6. Installed companion and etosd are not modified or restarted. Paid ceiling: one image at operator USD 0.20, no describe or TTS, total at most USD 0.30.

## R2 fixes

| Finding | Decision | Test / evidence |
|---|---|---|
| R2-38 / SR-12.2 | Keep the new-lever requirement. Correct 09's stale pressure-plate recovery claim and identify the trusted-game prerequisite that prevents a distinct lever package from completing admission. No product source outside this packet is changed. | `Hollowmere.R8_B.LeverAdmissionProbe.SR_12_2_NewLeverSmokeIsBlockedBeforeWorldAccess`; graphical prerequisite driver under `Tests/R8_B/Lever`. This refusal probe is not a signed-stage or admission pass. |
| R2-38 / SR-4.6 | Run the full live image workflow on an isolated real node and companion built from this checkout, rather than relabeling historical image/client tests. | `Hollowmere.R8_B.MediaQualification.Run`: exactly one real generated image, verified download/import, `entity.setMaterialTexture`, normal History undo/redo/final undo, altered-download and altered-import `artifact_digest_mismatch` refusals. [Evidence](../../../artifacts/studio/verification/W-ETOS-07/r8-b-media/README.md). |

## Verification

- W-ETOS-07 **PASS**: real scratch ETOS node, local companion build, `echo-images` / `gpt-image-2`, operator-bound USD **0.20**, exactly one image, zero describe/TTS/worker calls. Generated, imported and redone SHA-256: `36e071fa68dc08dad9cdd913d3e7dbf54c7a8f501a07007e39c29003c4fc7d50`. Charge/producer checkpoints remain unchanged after generation. Typed material binding toggles with normal history; final undo restores the original entity content/structural recipe. Changed bytes hash to `1c05030cf7bbd2be30571ae4d03b51c8309d742c2b7e4fecfc1bb24d543a1799`; download and media import refuse, with no tampered asset/meta and no extra journal entry. The target is an owned definition using the existing Maren prefab, not a claim to rerun W-AI-01's live healer presentation.
- W-DOC-02 remains **FAIL**, with one exact external product blocker below. Probe XML: **1 passed / 0 failed / 0 skipped / 0 inconclusive**. Actual nonbatch `:1` / RTX 4060 Ti receipt observes both new-entry and wrong-package refusals. No signed lever stage/admission/restored Play/undo is claimed. [Evidence and reproduction](../../../artifacts/studio/verification/W-DOC-02/r8-b/README.md).
- Graphical capture caveat: the retained probe PNG is obscured by the existing GNOME failed-session/notification surface. Visual inspection does not establish a readable probe panel or working lever. Window capture and XComposite attempts did not remove the obstruction; no desktop service was restarted, and the ineffective workaround was removed.
- Existing ETOS dotnet client suite: **69 passed / 0 failed / 6 skipped**. The six installed-node environment-gated cases remain unrun; no skipped case is counted as live evidence. TRX is retained with W-ETOS-07 evidence.
- Initial lever harness setup failed CS0012 because its asmdef omitted `GameCore.Unity.App`; the corrected reference passes. Earlier graphical attempts also had docked/decorator window-selection mistakes; their logs remain retained separately, not attributed to product failures.
- Final policy gates: `python3 tools/check_package_metadata.py` passes **42 packages / 91 package assemblies**, and `python3 tools/check_game_core_csharp.py` passes **1,253 C# files**. No package dependencies or production C# source changed.
- Locally built companion: `cargo fmt --check`, all-target `cargo clippy -- -D warnings`, and `cargo test` pass: **145 passed / 12 ignored**. The ignored real-stage fixtures are not promoted to acceptance. Output retained with the media evidence; no Rust source changed.
- ROWS.json: **56 PASS / 11 BLOCKED / 1 FAIL, 68 rows**. Only W-ETOS-07 and W-DOC-02 records change; all 66 other row objects and every scenario/requirement remain unchanged. W-DOC-02 stays FAIL; W-ETOS-07 moves BLOCKED → PASS only on the new complete workflow receipts. Shared matrix prose is intentionally unchanged pending its owner's integration.

## Cleanup

Final media undo removes the imported image and restores the owned target definition before owned fixture cleanup. Scratch services are stopped only by their owning runner; installed services and sibling clones are untouched. Unity-generated changes to GraphicsSettings/QualitySettings and the removed orphan R6_D lock meta were restored to the initially clean checkout; unrelated generated metas/settings/crash blobs were removed. R8_B's new folder/file metas are retained. Local execution outputs remain untracked; only explicit sanitized row evidence is committed.

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
