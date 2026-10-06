# R7-A W-UI-01

**Graphical workflow PASS.** Source: `1a462f88` (the committed source bytes exercised by the final run). Linux Unity 6000.0.75f1, NVIDIA RTX 4060 Ti, display `:1`; one packet-owned Editor through the shared allocator. No pairing command, worker submission, paid media call or installed-service restart.

## Product fix

`RuntimeViewMapper.Map` previously returned early for any candidate with an authoring ID. In the actual game, NPC views are children of authored `GameBoot`, so the generic picker returned that ancestor and the runtime tag was ignored. The mapper now gives the explicit view tag precedence, retaining UI/ground exclusions and fallback for unresolved tags.

`R7_A_RuntimeNpcTagOverridesAuthoredBootstrapAncestor` failed before the fix (Maren was not selected) and passes afterwards. The first actual workflow connected and walked, but its card showed GameBoot; its failed log remains under `attempts/`.

Automatic startup and R2-29 event-delivery fixes already exist in this baseline. They were not replaced with an explicit pairing workaround. Graphical tests prove key down/up reaches the image only, prompt typing still works and focus loss resets game input. Actual Open Studio reports `EtosAgentGateway`, node reachable and agent ready in [automatic-gateway.json](open-play/automatic-gateway.json).

## Actual surface proof

- [Ordinary open](open-play/01-open.png): Studio companion Connected without an explicit pairing/start call from the driver.
- [Walking](open-play/02-walk.png): HUD active, routing active, **7.636 m** committed `world.posX/posZ` movement over **3.040 s**. [Measurement](open-play/evidence-log.jsonl).
- [NPC definition card](open-play/03-npc-definition.png): Select picks Maren and the real card shows **MarenEntity (Entity Definition)**.
- [Final receipt](open-play/result.json): driver succeeds; wrapper exits 0 in 78 s.

The driver reuses the existing game walking/picking/capture helpers, skipping their paid-request steps. It does not infer selection success from a hit: it requires one selected authored object and the visible Maren/definition card. Captures are composed from the actual Editor windows.

## Reproduce

```sh
bash artifacts/studio/verification/W-UI-01/r7-a/run.sh
```

.NET client regression TRX: 69 passed, 0 failed, 6 opt-in live skips. This is separate from graphical acceptance. Earlier full UI attempts retain the three stale signed-catalog fixture failures; fixtures now supply the mandatory world, mechanism catalog type/fingerprint and predicted digest without changing production verification.

Final graphical regression [XML](ui.xml): **71 passed / 0 failed / 0 skipped**, including the full UI suite, automatic-startup client regressions and strict-byte regression. Unity exits 0 in 140 s. A separate retained aggregate incorrectly included a headless-only refusal test in graphical mode; the headless suite subsequently passes 17/17 in its required mode, separately from this graphical pass.
