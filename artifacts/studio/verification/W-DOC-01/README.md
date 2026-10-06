# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **BLOCKED**. Fresh final-main guide: Boot scene and Open Studio menu pass (1/1, PNG on private :99/llvmpipe). Text-edit and image-generate/bind steps cannot start before the guarded R4 companion activation. No novice NPC/dialogue completion is claimed.

Report timestamp: 2026-10-06T04:56:25.184385+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh guide-open
studio/tools/verify-all.sh final-live
```

## Retained evidence

- [W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/README.md](../W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/README.md)
- [W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/walkthrough.json](../W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/walkthrough.json)
- [W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/open-studio.png](../W-DOC-01/p42b-fresh-guide-open-20261006T044416.491505Z/open-studio.png)
