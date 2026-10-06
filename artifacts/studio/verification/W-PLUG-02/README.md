# W-PLUG-02: Despawn/respawn keeps override; Animator bound

Verdict: **PASS**.

R7-C, source checkpoint `7ad4a809`, Linux Unity 6000.0.75f1, graphics-enabled **batch** Editor on isolated Xvfb `:97` (llvmpipe). No real-GPU performance claim.

## Evidence

- [Executed XML](r7-baseline/results.xml): `Hollowmere.R7_C.Animation.Tests.AnimatorRespawnTests.W_PLUG_02_AnimatorEvaluatesCommittedVariant_AndRespawnRetainsOverrides` **Passed**.
- [Shared initial Editor log](../W-PLUG-11/r7-baseline-tests/r7-edit-baseline-20261007T045248-3963472-a1.log). The combined run has two unrelated failing acceptance cases; this row's named case passes.
- Driver: `games/hollowmere/Assets/Hollowmere/Tests/R7_C/Animation/AnimatorRespawnTests.cs`.

The production `GameplayBoot`/`CreateViews` presentation binds committed variant and scale to a real AnimatorController. Controller transitions evaluate real animation clips that move a child transform. Variant 2 selects the override prefab and 1.2 scale; returning to variant 0 changes the evaluated state/pose. Despawn destroys the old view and Animator; respawn creates a different view whose override, scale, parameters and evaluated pose match the committed state. The test does not call Animator.Play or SetInteger.

Temporary controller/clips/prefabs are deleted during teardown. ImmediateSceneLoader isolates the presentation assertion; region streaming remains qualified separately in W-PLUG-01.

## Reproduce

Use `studio/tools/unity-batch.sh --project <absolute games/hollowmere> --log-dir <dir> --label animator --results <xml> -- -runTests -testPlatform EditMode -testFilter AnimatorRespawnTests -force-glcore`, with `DISPLAY` set and `UNITY=<absolute Tests/R7_C/batch-graphics.py>`. The adapter preserves batch mode, host allocation, redaction and deadlines.
