# P4.2c live lanes

Product baseline: final main `f7878292`. Run on myubuntu from this packet's clone.
`studio/tools/verify-all.sh p42c <lane>` uses the immutable installed companion and
`.evidence/live` (fresh main, authored fixture writes remain disposable).

- `baseline`, `hello`, `tts-proof`: retain zero-point ledger, authenticated hello,
  then exactly one <= USD 0.001 TTS proof. Never repeat `baseline` after spending.
- `text2`, `robe2`, `narrative`, `reopen`, `voice2`: unmodified P3.2 C# drivers;
  Unity wrapper retains allocator, watchdog, redactor and graphics through the
  trusted `unity-interactive-p42c.py` adapter. `voice2` uses PipeWire's virtual source
  and the original marker protocol, restoring the prior source afterward.
- `stage-submit --candidate <dir>`: app-origin panel Stage, retain binding/job id,
  close creator Editor before sandbox Unity starts. Watch through the read-only
  `watch-stage-p42c.py <stage-request.json>`. That watcher cannot authorize admission.
- `stage-review --candidate <dir> --input <stage-request.json>`: restore panel job
  binding, fetch/verify through companion, explicit Admit from running Hollowmere,
  capture/restore/smoke and History Undo. `--review-only` reserves admission for the
  warm run. `--negative` records the semantic-negative disabled-Admit result.
- `restart`: a copy of the existing L03 stream test replaces ForceDisconnect with
  the actual documented `etos agent restart gamecore-studio`; checks same task,
  one final outcome and exact cursor replay. Node death is still not exercised.
- `guides`: literal text Send and image Send with retained prompts/results, plus
  an unconfigured 3D refusal. This is an observed guide walkthrough, not a claim
  of novice end-to-end NPC/dialogue completion.

Each paid lane reserves its maximum media counts before execution. Reusing a lane
name refuses; exhausted counts, absent ledger or USD 10 ledger spend refuse.
Robe reserves three images and two TTS (including the now-connected catalog voice
probe). Voice reserves two TTS. Paid responses and task budgets are retained;
operator estimates are ceiling accounting, not provider invoices. Task micro_usd=0
is reported literally and never converted to invented provider billing.

`retain-p42c.py <attempt>` collects each actual task's CLI budget and encodes the
P3.2 frames at their actual two-second cadence; keyframes remain. It never opens
key files. Copy `P42cLive/` only into the disposable project's Assets directory.
This trusted acceptance harness is not candidate code. No unsigned verdict import,
`allowUnsafe` exception, host sandbox fallback, etosd restart or product edit occurs.
