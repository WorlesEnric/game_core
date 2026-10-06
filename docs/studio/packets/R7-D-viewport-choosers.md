# R7-D viewport choosers

Branch `omp/r7-d`; product checkpoint `61debfd5`, final tested source `b2717d03`, based on `8446c5a0` after the R7-A/R7-B merge. Exclusive product owner: `com.gamecore.studio.ui`; acceptance extends R7-B's existing picking driver. No paid operations, credential reads, installed-service changes, or sibling-clone changes.

## R2 fixes

| Finding / row | Fix | Regression / acceptance |
|---|---|---|
| R2-38 / W-UI-02 | Marquee ambiguity uses engine renderer projections and occlusion hits, not core marquee's unset occlusion flags. The resulting chooser retains candidate inclusion toggles and an explicit Apply. Filtering retains the original rectangle and applies Replace/Add/Toggle against the selection before the marquee. Non-overlapping marquees remain immediate. | `R7_D_W_UI_02_MarqueeChooser_OnlyAmbiguityOpensAndFilteringPreservesOriginalOperation`; actual-geometry ambiguity regression; `ScenariosR7Picking.RunFence` requires attached chooser interaction and exactly Maren/Odd/Pip without fence geometry. |
| R2-38 / W-UI-03 | Explicit Logical, Part, Prefab and Instance scope choices. Prefab resolves the originating nearest prefab root asset, never the mesh child or entity definition. Instance scope preserves logical identity and clears parts. Scope participates in selection change detection. Unavailable prefab choice is disabled and is a no-op if invoked directly. | Five `R7_D_W_UI_*` picker regressions cover identity, parts, scope replacement, unavailable prefab and geometry; `ScenariosR7Picking.RunLantern` activates attached real buttons for all four choices and checks exact refs/scopes. |
| R2-30 / 1280×720 | The popup scrolls and clamps to its parent; choice rows wrap. The acceptance viewport runs at the minimum 1280×720 window, with image at least 640×360. | Real graphical chooser captures and attached controls; no synthetic image. |

All selections use the existing `SelectionModel.Set` command; no authored-world mutation path was added. Existing bool-based picker and popup callers retain their logical/subpart semantics. Stable controls: `overlap-{i}`, `overlap-part-{i}`, `overlap-prefab-{i}`, `overlap-scope-{i}`, `overlap-include-{i}`, `overlap-apply`.

## Verification

Qualification command: `bash artifacts/studio/verification/W-UI-02/r7-d/run.sh`. It retains the shared `unity-batch.sh` allocator/redactor, removes only the batch/headless flags through the graphical adapter, uses display `:1`, and holds at most one Editor. The UI namespace is actually `GameCore.Studio.UI`; the filter accepts both `Ui` and `UI` rather than silently selecting zero UI tests.

Static check: package metadata passes (42 packages, 91 assemblies); C# policy passes (1,243 files).

- Final [EditMode XML](../../../artifacts/studio/verification/W-UI-02/r7-d/results.xml): **71 passed / 0 failed / 0 skipped**, including six R7-D regressions. UI 69/69; Hollowmere R7-B 2/2. No separate `Hollowmere.R7_D` or `Hollowmere.R2_38` namespace cases exist in this baseline; the requested alternatives remain in the filter. R7-D tests use the existing UI asmdef.
- `RunFence`: **PASS**, exit 0, 36 s; [chooser with fence unchecked and three NPCs checked](../../../artifacts/studio/verification/W-UI-02/r7-d-20261006T215026225Z/03-marquee-npc-choices-ui.png), exact selection/rectangle assertions and [receipt](../../../artifacts/studio/verification/W-UI-02/r7-d-20261006T215026225Z/receipt.json).
- `RunLantern`: **PASS**, exit 0, 42 s; [all four choices visible](../../../artifacts/studio/verification/W-UI-03/r7-d-20261006T215104970Z/02-lantern-chooser-ui.png), exact refs/scopes and [receipt](../../../artifacts/studio/verification/W-UI-03/r7-d-20261006T215104970Z/receipt.json).
- Both final UI capture sets are actual 1280×720 GUIView pixels, inspected after execution. Twenty final UI captures plus twenty viewport readbacks are retained. No synthetic screenshot or controller-only replacement.
- Initial suite: 69 passed, two test failures. Removed an incidental raw-prefab-path string assertion (real identity/root-resolution assertions remain), and targeted the actual button in the submit event. Initial driver assertions passed but old restored Studio panels obscured the composed capture; final isolated-process setup closes those restored panels first. All attempts are retained, not relabeled as final visual evidence.
- Before-fix evidence remains R7-B's BLOCKED driver receipts for each missing seam. New regression tests assert the missing behavior; no claim of executing new enum-dependent tests against the old API is made.

Exact new passing cases (all under `GameCore.Studio.UI.Tests`):

- `R7_D_W_UI_02_MarqueeAmbiguity_DistinguishesSeparatedAndOverlappingGeometry`
- `R7_D_W_UI_02_MarqueeChooser_OnlyAmbiguityOpensAndFilteringPreservesOriginalOperation`
- `R7_D_W_UI_03_AbsentPrefabChoice_PreservesSelectionPartsAndRectangle`
- `R7_D_W_UI_03_InstanceScope_PreservesIdentityClearsPartsAndReplacesPriorScope`
- `R7_D_W_UI_03_LogicalAndPartChoices_KeepOwnerAndOnlyExplicitPart`
- `R7_D_W_UI_03_PrefabChoice_SelectsOriginatingRootNotChildPartOrDefinition`

## Evidence integrity

The inherited `ROWS.json` had literal unresolved aggregate-count conflict markers, while its 68 row objects already combined R7-A and R7-B. Recounting those unchanged objects gives 41 PASS / 26 BLOCKED / 1 FAIL before R7-D. Its aggregate conflict is repaired by recomputation, not by choosing either stale side. SUMMARY is regenerated from row objects; unrelated row dispositions are not changed.

Final ROWS/SUMMARY: **43 PASS / 24 BLOCKED / 1 FAIL**, 68 rows. Only W-UI-02/03 are promoted. Their duplicate inherited matrix entries are consolidated; outside-owned W-UI-01 duplicates remain untouched.

Cleanup restores only tracked Unity-generated settings to their captured pre-run bytes, restores the deleted preexisting Cargo.lock metadata, and removes newly generated folder/settings metadata. R7-B's suite overwrote three historical evidence JSONs; this run's copies are retained under `W-UI-02/r7-d/r7-b-suite`, then the historical files are restored exactly. Own evidence has repository/home paths scrubbed and SHA256 manifests. No creator history or sibling cache is removed.

## Requests to other packets

Integrator: `docs/studio/07-verification-matrix.md` contains two inherited W-UI-01 rows (one FAIL, one PASS) after the R7-A/R7-B merge. That row is outside R7-D's cell ownership. Reconcile it with R7-A's retained PASS evidence; ROWS already contains the correct single PASS object. No product seam request remains.

## Left open

No W-UI-02/03 acceptance blocker remains. The absent namespace matches and outside-owned duplicate W-UI-01 matrix entries are explicitly recorded above; no unrelated acceptance or all-row same-revision pass is claimed.
