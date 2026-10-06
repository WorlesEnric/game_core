# R6-C follow-ups

## Final verification

Requested EditMode filter: `Hollowmere\.R5_A.*|Hollowmere\.R6_A.*|Hollowmere\.R2_B.*|Hollowmere\.P3_2.*`.
Final XML: **112 passed / 0 failed / 3 skipped / 0 inconclusive**, 115 selected.
Breakdown: R5_A **20 pass**, R6_A **4 pass**, R2_B **71 pass**, P3_2 **17 pass / 3 skipped**.
Both changed Unity regressions pass. XML: `.unity-logs/games_hollowmere-editmode-20261007T001504-3004321.xml`;
SHA-256 `99e4d1f2a844a83632d63b8090b7149506dc8b2c479195846a6e0097c0482eff`.
Unity exited 0; the wrapper intentionally exits 1 / PARTIAL because the three paid live tests are skipped.
This is not an all-selected-tests PASS claim.

Installer pytest: **7 passed plus 2 subtests**. Worker pytest: **6 passed**. Combined **13 passed plus 2 subtests**, zero failures.
Command: `/tmp/r6-c-pytest/bin/python -m pytest studio/etos/tests studio/etos/agent/workers/tests -v --junitxml=/tmp/r6-c-python.xml`.
Both repository policy checkers pass. Temporary baseline fixtures were automatically deleted; generated Unity folder metas and Python caches were removed after the last run. Authored dialogue/quest fixtures restored themselves through existing test teardown. No out-of-scope authored changes remain.

This is the packet's PACKET.md. Branch `omp/r6-c`, base `fa16f858`. Scope: stale R5-A admission expectation, describe-only installer selection, worker tariff fixtures, durable workflow undo eligibility, and NPC authoring prerequisites. No retained P4.2e candidate or shipping USD 0.01 describe tariff is changed.

## R2 fixes

| Finding / owning note | Fix | Regression |
|---|---|---|
| R6-A “not all green”; R2-09/14; P0.5/P2.4 | Historical P4.2c `pass=true` is not admission authority. Read the existing job through the authenticated companion, establish that world/predicted are absent, require the signed-verdict route to refuse it, and verify no cached trust, pending admission, capture or installation results. | `R5_03_InstalledVerdict_CapturesInPlay_VerifiesCatalogBeforeInstall` retains its historical identifier but now asserts refusal, not successful capture. The current companion rejects at retrieval with HTTP 404 `not_found`, before Unity's `VerdictCheck`; the earlier R6-A witness rejected later with `verdict_failed`. |
| P4.2e describe-only CLI; P0.5/P2.2 | Add `describe` to `prices --only`; leave `apply_prices(root, only)` provider selection and pre-write placeholder checks unchanged. | `test_r6_c_describe_only_cli_refuses_placeholder_without_writes`; `test_r6_c_describe_only_cli_updates_provider_and_tariff_preserving_other_state`. Both exercise `install.sh --apply-prices --only describe` on temporary roots. |
| P4.2e stale tariff expectations; P2.2 | Both worker tests use explicit zero-price `SET_BY_OPERATOR` input and still check refusal plus a filled USD 0.03 estimate. The owner-declared shipping USD 0.01 price is not reverted. | `test_describe_tariff_placeholder_refuses_before_apply`; `test_R6_Request7_describe_alias_is_echo_and_cannot_inherit_dashscope_tariff`. |
| P4.2e post-Play undo; R2-03/15; P1.6/P2.1/P2.3 | `AppliedOr` reads the durable journal, not candidate UI validity. Its private dry helper executes the existing History step for Applied, preserves that step's completion result, and logs the actual journal state when skipping. Existing `S.History` still writes the real History result/diagnostics and marks refusals as failures. | `R6_C_AppliedJournalWithInvalidUiStillAttemptsHistoryUndo`: persisted Applied entry plus Invalid candidate UI reaches real History and becomes Undone. No graphics, provider calls or candidate repair. |
| P4.2e NPC prerequisites; P2.1/P2.2 | Worker contract requires patrol route, dialogue graph and reachable bell line, placement/roster/navigation context, ordered catalog operations or clarification. The independent ferryman excerpt uses Definition references and vector-array patrol points. | `test_R6_C_ferryman_prompt_answer_carries_patrol_and_dialogue_prerequisites`: validates sample operation/reference schemas, patrol vectors, shared NPC/graph references and dependency order. |

Shared historical packet notes remain unchanged because R6-C's exclusive documentation path is this note. The rows above supply the R2-fix cross-references for their integrator.

## Regression evidence

- The describe CLI regressions were run against a temporary copy restored to `choices=["tts"]`: filled selection failed with argparse exit 2, and both placeholder subcases failed because argparse prevented the required tariff refusal. Fixed installer/worker pytest: **13 passed, 2 subtests passed**. JUnit: `/tmp/r6-c-python.xml` (15 entries including subtests).
- The new NPC prompt regression rejects the baseline prompt because the patrol/dialogue prerequisite section is absent. The fixed prompt passes schema and prerequisite checks. This is an offline contract test, not generated-provider or graphical Play evidence.
- Original undo guard with the Applied/Invalid fixture: **0 passed / 1 failed**, `.unity-logs/games_hollowmere-editmode-20261007T000556-2964553.xml`, failure “durable Applied must reach History despite Invalid UI”. The guard never called History.
- First compile attempt exposed the dry test's missing UI assembly reference; corrected within the owned P3_2 test asmdef. An intermediate full run recorded **110 passed / 2 failed / 3 skipped**: viewport creation in the dry test required a graphics device, and the installed companion rejected incomplete signed records earlier than the old Unity-side assertion. The dry guard seam now avoids graphics; the authenticated regression asserts the current service boundary instead of accepting arbitrary transport errors.
- `python3 tools/check_package_metadata.py`: **42 packages / 91 package assemblies**, pass. `python3 tools/check_game_core_csharp.py`: **1227 files**, pass.

## Requests to other packets

None required for these changes. Existing P4.2e requests about graphical admission compilation, voice partials and byte-exact restoration remain with their owners; this packet does not modify those paths or acceptance rules.

## Left open

- No paid worker generation or graphical ferryman Play qualification was performed. W-AI-02 remains governed by its retained failed evidence; a better prompt is not a passing end-to-end workflow.
- Three P3_2 live tests require `GAMECORE_ETOS_LIVE=1` and can make paid requests. They are intentionally not enabled under this packet's no-paid-ops rule; skipped tests are not passes.
- The historical authenticated admission regression requires the installed companion's retained P4.2c job. It only reads that job/verdict route; it does not start a new stage or alter the installed service.
- Python's system environment lacked pytest/jsonschema. Tests used an isolated `/tmp/r6-c-pytest` virtual environment; no installed service environment was changed.

All Unity invocations use `studio/tools/unity-compile.sh`, which delegates to `unity-batch.sh` and its host-wide lock. At most one owned Editor ran at a time. No paid operations, service restarts, secret printing, sibling-clone changes, or relaxed security rules.
