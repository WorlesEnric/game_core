# W-DOC-02: Developer adds a lever interactable from the plugin guide

Verdict: **PASS — R10-A**, product `2e17a01ec02dcea3b4762b4536605120c985681b`, locally built scratch companion, graphical `:1`. Literal guide candidate export/client-submit produces an authenticated signed Docker pass in **88.035 s**. Actual creator Admit → compile/reload → restored Play/smoke takes **47.375 s**, below the unchanged 90 s limit. The live lever control commits **off → on → off**; world captures show the corresponding red/green handle poses. Normal History Undo completes in **34.288 s**, restoring the original catalog and removing the package/pending state.

The original stall was forced registry resolution: four `packages.unity.com` connection resets and a 161248 ms UPM request. Non-forcing local resolution now registers the new lever package in **18892 ms**, without those registry errors; the genuine compile/reload and authentication gates remain mandatory.

Current evidence: [result](r10-a/final/lever/result.json), [live lever state](r10-a/final/lever/interactive-lever.json), [authenticated admission](r10-a/final/lever/live-admit.json), [normal Undo](r10-a/final/lever/live-undo.json), [UPM log](r10-a/final/lever/config-upm.log), [visual review](r10-a/final/lever/visual-review.json), [packet](../../../../docs/studio/packets/R10-A-admission-freshness.md). Zero paid operations. No installed-service qualification or upgrade is claimed.

Reproduce all three cases: `python3 games/hollowmere/Assets/Hollowmere/Tests/R10_A/run.py /tmp/r10-a-new --label new --companion "$PWD/studio/agent/target/debug/gamecore-studio"`; build that companion and the Release literal-submit client first. `--lane lever` independently exercises the literal lever against a fresh cold scratch root.

## Historical P4.2j failure

The then-current literal export/client-submit passed signed staging but exceeded 90000 ms during admission compile; separate recovery rolled back with `compile_timeout`. Historical product `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release `0.1.0-fba3604e99ceadd1`; reported 2026-10-07T16:04:44.851949+00:00. Its original failure records below remain unchanged.

### Historical reproduction

```sh
python3 artifacts/studio/verification/TOOLS/lever-literal-p42j.py
```

### Historical evidence

- [W-DOC-02/p42j-stage-lever-recovery-20261007T133830.438524Z/outcome.json](../W-DOC-02/p42j-stage-lever-recovery-20261007T133830.438524Z/outcome.json)
- [W-DOC-02/p42j-lever-literal/live-failure.txt](../W-DOC-02/p42j-lever-literal/live-failure.txt)
- [W-DOC-02/p42j-lever-literal/live-progress.json](../W-DOC-02/p42j-lever-literal/live-progress.json)
- [W-DOC-02/p42j-lever-literal/signed-verdict.json](../W-DOC-02/p42j-lever-literal/signed-verdict.json)
