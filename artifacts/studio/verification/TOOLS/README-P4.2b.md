# P4.2b lanes

Run `studio/tools/verify-all.sh <lane>` on the Linux host. Every attempt retains its own
row folder; skipped/missing XML is never promoted to a workflow pass.

| Lane | Scope |
|---|---|
| `live-preflight` | Read-only P3 guard, Editors/recorders, immutable release id and media-charge ledger availability. No credentials read. |
| `release-build` | Fresh final-main clone plus the declared tariff; fmt/clippy/tests and release build. Never changes the installed companion. |
| `release-binary` | Resume only the release build after the recorded build-script interruption, without repeating the passed Rust suite. |
| `live-install` | Guarded activation via the existing secret-free installer helper and `--apply-prices`. Never runs the full installer or restarts etosd. |
| `final-live` | Guarded P3.2 text2/robe2/narrative/reopen replay, existing stage and virtual microphone drivers. Refuses before calls while the required installation/display prerequisites fail. Individual workflow outcomes still require the P3.2 JSON/PNG review; driver exit alone is not acceptance. |
| `final-views-tests` | Final-main P2.3/R2-E EditMode suite. P4.2 screenshots remain explicitly historical supplemental evidence. |
| `final-timing` | Two 20-apply single/Marsh datasets and 20 kernel prepares, plus selection setup. Uses a scratch clone and packet-owned harness. |
| `final-selection` | Only the two complete 100-pick/500-candidate marquee datasets after correcting the initial unsaved-scene harness. Do not repeat the completed apply/compose datasets. |
| `final-guides` | Literal pressure-plate regeneration/check/stage commands from 09 in a fresh final-main clone. |
| `guide-stage` | Supply the already-built final-main binary and retry 09's stage command with a private stage root; retain the cache prerequisite refusal. |
| `guide-open` | 08's Boot scene and Open Studio menu on a private Xvfb display; one allocated Editor, no live gateway. Text/image Send remains prerequisite blocked. Library is seeded from this packet's own tested tree; no authoring data/settings are copied. |
| `node-equivalence` | Non-destructive network-namespace prerequisite only; never stops etosd. |
| `final-checks` | Required metadata/C# checks and existing installer/runner suites. Two installer tests assume a placeholder shipping template and remain owner-requested failures. |
| `tariff-tests` | Failing-before/passing-after owner tariff and fail-closed placeholder regression. The before failure is expected evidence. |
| `final-report` | Exactly two complete timing datasets, six P2.3-defined view rows, ROWS/SUMMARY/matrix regeneration. |

`P42bHarness/` and `P42bGuide/` are trusted acceptance harnesses, copied only into scratch
clones. They are never agent candidates and never change shipping package code. They
use C# 9, nullable annotations, Editor-only test assemblies and per-session state.
