# R2-G-host evidence

Host: myubuntu, Linux, 2026-10-05. Branch `codex/r2-g-host`. Commands were run in this clone. No ETOS operations,
installed service changes, sibling clone edits, full stage run or live gameplay Unity test was performed.

| Command / check | Outcome |
|---|---|
| `studio/stage/analyzer/offline-check.sh /tmp/r2-g-analyzer-offline-04` | PASS: fresh offline restore, 42/43 analyzer tests, pressure-plate exit 0 with no findings, negative fixture exit 3 with every expected rule. Docker `--network none`; .NET 8.0.425 SDK, Roslyn 4.5.0. |
| `python3 tools/check_stage_slot.py --self-test` | PASS: 29 cases. |
| `python3 -m unittest discover -s studio/stage/tests -v` | PASS: 9 tests (redaction, XML, environment, cache, legacy refusal, process ownership). |
| `bash studio/tools/tests/unity-batch-lock.sh` | PASS: seven concurrent launch requests, peak three fake Editors in the final run (earlier runs peaked at one/two with other occupied slots); stale owner cleanup, lock-file retention, TERM delivery and no durable synthetic secrets. |
| `make-slot.py --slot-root /tmp/r2-g-slots --slot pressure --source-project games/hollowmere --candidate samples/mechanisms/pressure-plate/candidate --force` | PASS: 52 declared candidate files, nine trusted settings inputs; no live World/Regions stage inputs. No compiler or Unity launch. |
| `python3 tools/check_stage_slot.py /tmp/r2-g-slots/pressure` | PASS. |
| `python3 studio/stage/slot-checks.py --slot /tmp/r2-g-slots/pressure` | PASS: 14 C# files, six asmdefs, zero problems. |
| `python3 tools/check_package_metadata.py` | PASS: 41 packages, 88 assemblies, four engine pins, six allowlisted pins, two lock sources. |
| `python3 tools/check_game_core_csharp.py` | PASS: 1,076 C# files. Analyzer separately compiles with C# 9 and warnings as errors. |
| sample `make-catalog.py --check` and `make-candidate.py --check` | PASS: fresh-array catalog and three candidate archives reproduce. |
| `bash -n` on modified shell files; `git diff --check` | PASS. |
| `studio/stage/probe-sandbox.sh /tmp/r2-g-docker-probe-03` | Docker started; Unity licensing FAILED, Editor exit 198. Required service result: `stage_failed{sandbox_unavailable}`, no verdict. No host fallback. |

[Offline log](analyzer-offline.txt), [positive findings](pressure-findings.json),
[negative findings](negative-findings.json), [licensing excerpt](sandbox-licensing.txt).
The `stage_analyzer_error` lines in the offline log are expected invalid-input CLI tests; the test suite passes.
The workload-verification warning did not prevent the network-disabled restore or tests.

The first two exploratory probe attempts failed before licensing (scratch project/HOME setup); only the third probe
is licensing evidence. Its log capture already redacted before disk; the retained excerpt omits unrelated session and
machine identifiers. The pressure-plate metadata context uses trusted installed Unity DLLs plus Unity package DLLs
copied from the existing warm cache and current repository GameCore sources. R2-F must provide equivalent context
from its versioned cache in production. No candidate assembly was loaded or executed by the semantic scanner.
