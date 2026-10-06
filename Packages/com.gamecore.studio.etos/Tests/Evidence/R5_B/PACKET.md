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

- Before C# fixes: `unity-before.xml` has **1 passed / 3 failed / 1 skipped**. Both
  Request #5 cases fail on the exact retained “not staged” label; Request #6 fails on
  absent frame/release telemetry. Strict importer refusal already passes. The paid
  two-line self-test is skipped. Initial test harness compile errors (missing namespace,
  then an incorrect IDisposable assumption) were corrected before this baseline run.
- The complete cold baseline wrapper took 1,388 seconds under concurrent host I/O load;
  tests themselves took 0.813 seconds. No timeout was changed.
- Worker baseline: **3 failed** (missing shared enum contract and absent tariff refusal).
  Fixed new + existing worker tests: **13 passed**. UI host evidence check: **1 passed**.
- Rust retained-audio baseline: **1 failed, zero finals**. Fixed focused voice checks:
  **4 passed**. Full Rust: **138 passed / 0 failed / 11 ignored**, fmt/clippy clean.
  The first retained WAV parser attempt exposed its streaming length sentinel and was
  corrected to bound reads by actual EOF; the audio bytes were never modified.
- .NET client TRX: **69 passed / 0 failed / 6 live skipped**.
- Final package metadata and C# checks: pass (42 packages, 91 package assemblies,
  1,211 C# files). Exact commands: `commands.txt`. Retained receipts replace the host
  home prefix with `~` and trim log-line trailing whitespace; raw Unity XML remains in this clone's `.unity-logs/`.
- Full requested EditMode XML: **185 passed / 0 failed / 9 skipped / 0 inconclusive**
  (194 total), Unity exit 0, wrapper 212 seconds. All four offline R5-B cases pass.
  Four existing graphical cases, four existing live ETOS cases, and the new real-provider
  two-line voice case are skipped; they are not acceptance passes. No source changes followed
  this successful run. See `unity-final.xml`.


## Voice loss disposition

The original ledger is internally consistent: 600 frames / 2,880,000 bytes and 560 frames /
2,688,000 bytes, with zero finals. It establishes companion forwarding totals, not what the
provider recognized. Both original WAVs contain speech-level nonzero PCM (see
`audio-analysis.json`); the separately recorded virtual-source WAV also contains nonzero audio.
The move fixture peak/RMS is 0.43747/0.08714; destructive is 0.32224/0.07070. The long virtual
capture's lower aggregate RMS includes substantial silence and is not a VAD diagnosis.

A concrete companion defect is independently reproduced: Stop used to issue Close first.
The SDK contract defines Close as resource release; it is not an input commit. When VAD has
not finalized input, this discards the opportunity to transcribe it. Stop now commits pending
input, keeps receiving for up to eight seconds, preserves final sequencing, and only then closes.
The client receives closure before the SDK's additional cleanup deadline. No response_create
or automatic Studio request is introduced. This fixes the reproducible release defect; it is
not sufficient evidence to assign every historical zero-final session to that cause.


Source checkpoint: `43725155` (C#), `44b840aa` (voice bridge), `6f4fc462` (workers/tariff).
`summary.json` records counts and qualification limits; `SHA256SUMS` covers retained receipts.
Compressed Unity logs include both initial harness compile failures, the failing baseline and
the successful full suite. No test failure was suppressed or counted as a pass.
