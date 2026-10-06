# W-HOST-01: `install.sh` fresh, then no-op; `verify.sh` with real image/describe/tts/realtime calls

Verdict: **BLOCKED**. P4.2e activates and checksum-verifies immutable main-built release 0.1.0-cac2f82c59be070b; the repeat installer is a no-op and authenticated hello reports the owner describe tariff. A full fresh node install remains unrun under the no-etosd-restart rule. Initial host inventory is idle, but the monitor later sees a brief second Unity PID during NPC asset import; its exited process cannot be classified retrospectively, so strict host exclusivity is not claimed.

Report timestamp: 2026-10-06T15:29:44.385103+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
See TOOLS/README-P4.2e.md for immutable installation and receipt-hello.
```

## Retained evidence

- [INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json](../INSTALL-P4.2e/p42e-receipt-hello-20261006T143912.780456Z/result.json)
- [../workflows/P4.2e/install.log](../../workflows/P4.2e/install.log)
- [../workflows/P4.2e/installed-runtime.json](../../workflows/P4.2e/installed-runtime.json)
- [../workflows/P4.2e/editor-exclusivity-open.json](../../workflows/P4.2e/editor-exclusivity-open.json)

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
