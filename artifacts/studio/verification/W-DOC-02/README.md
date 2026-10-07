# W-DOC-02: Developer adds a lever interactable from the plugin guide

Verdict: **FAIL**. Current literal export/client-submit reaches a signed passing lever stage, but creator admission exceeds the unchanged 90000 ms limit while compilation is pending. A separate recovery Editor rolls it back with compile_timeout; no live lever/smoke/undo acceptance is claimed. The package is removed and no pending admission remains.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/lever-literal-p42j.py
```

## Current-run evidence

- [W-DOC-02/p42j-stage-lever-recovery-20261007T133830.438524Z/outcome.json](../W-DOC-02/p42j-stage-lever-recovery-20261007T133830.438524Z/outcome.json)
- [W-DOC-02/p42j-lever-literal/live-failure.txt](../W-DOC-02/p42j-lever-literal/live-failure.txt)
- [W-DOC-02/p42j-lever-literal/live-progress.json](../W-DOC-02/p42j-lever-literal/live-progress.json)
- [W-DOC-02/p42j-lever-literal/signed-verdict.json](../W-DOC-02/p42j-lever-literal/signed-verdict.json)
