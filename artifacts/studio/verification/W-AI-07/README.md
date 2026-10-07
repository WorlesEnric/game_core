# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **PASS**. Current installed companion refuses the unconfigured 3D operation with not_configured before any paid generation; authenticated receipt and TRX pass.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py hello
```

## Current-run evidence

- [P4.2i/p42i-hello-20261007T041554.920808Z/3d-refusal.json](../P4.2i/p42i-hello-20261007T041554.920808Z/3d-refusal.json)
- [P4.2i/p42i-hello-20261007T041554.920808Z/result.json](../P4.2i/p42i-hello-20261007T041554.920808Z/result.json)
