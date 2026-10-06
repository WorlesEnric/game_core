# R5-B — candidate badge, voice delivery, importer contract and describe tariff

Branch `codex/r5-b`, base `c9866292`, Linux build host. No service restarts,
credential-file reads, paid calls, live tariff apply, or sibling-clone changes.

## R2 fixes / R5 regressions

- Request #5: regression replays the original P4.2c `panel-verdict.json` and unchanged
  pressure-plate candidate with the production registry and StageAdmission/VerdictCheck.
  `Request5_RetainedUnpreviewedCandidateBadgeSurvivesReload` covers a missing Preview,
  durable badge state, fresh runtime authority loss, successful refresh and failed refresh.
  Its service replays a retained record; this is not a new authenticated live-stage run.
- Request #6: `request6_voice_stop_commits_before_close_when_vad_emits_nothing` runs
  the actual companion bridge/proxy/SDK against an offline node which releases resources
  on Close and transcribes on InputCommit. Replays the retained destructive WAV padded
  to the observed 600/560 frames; requires byte-exact delivery, one final per fresh take,
  and no response-generation commands. Baseline fails with zero finals.
- Request #8: `test_request8_retained_candidate_refuses_inspector_label` retains the
  original lantern receipt byte-for-byte. Worker enum schema refuses it; the independent
  valid example uses Sprite/Single. Both installed prompts embed shared rules.
  `Request8_OriginalCandidateStillRefusesAndPublishedEnumsMatchUnity` exercises the
  production MediaImportPolicy and compares every exported enum literal against Unity.
- Describe: `test_describe_tariff_placeholder_refuses_before_apply` calls the existing
  production tariff parser; an unpriced template refuses, a filled offline declaration
  binds echo-describe / echo/gpt-5.6-sol / call. No deployment.

## Requests to other packets

- Core catalog owner: `Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/AssetTools.cs`,
  `AssetImportTool` constructor's `Arg("importer", ValueTypes.Object, false, ...)`:
  replace the generic property-name documentation with the exact case-sensitive enum
  contract (textureType Sprite, spriteImportMode Single; no Inspector labels/numeric enums).
  The model currently has no nested-object ArgSpec schema; if adding that seam, export the
  strict enum settings without weakening MediaImportPolicy. R5-B's enum subset is in
  `studio/etos/agent/workers/importer-contract.json` and checked against production policy.
  Changing catalog text at the HTTP boundary would break its revision binding, so this
  packet deliberately does not rewrite a catalog after hashing it.

## Left open

- The historical voice loss is not fully isolated: P4.2c retained the frame/byte totals
  and zero revisions but no provider event stream or PCM from the actual Unity-to-companion
  socket. The fresh ready-gated failure rules out only simple session reuse/source-start
  explanations. It cannot prove provider VAD threshold as the cause.
- The binding no-paid-ops rule prevents the env-gated real provider self-test, and the
  no-restart rule prevents activation of this companion. Both fixture lines must transcribe
  on the installed real path before W-VOICE-01 is qualified. Offline simulated transcripts
  are never recognition evidence.
- No documented gain-normalisation rule exists in contract 04 §5 (also recorded by R4-A).
  Retained audio is nonzero; its maximum RMS indicator is not a VAD threshold probability.
  Do not infer that 0.15 should be compared with configured VAD probability 0.5. No speculative
  gain change is applied without provider-event evidence. Public interaction documentation:
  https://www.alibabacloud.com/help/en/model-studio/realtime (checked 2026-10-06).
- Echo describe public pricing could not be verified: https://api.echo-coding.com and
  https://echo-coding.com returned no retrievable tariff, and domain searches found none.
  The template therefore requires an operator's explicit total estimate and refuses until
  supplied. This does not make the currently installed hello priced; live apply is forbidden.

## Verification

In progress; final counts and retained before/after receipts are appended below.
