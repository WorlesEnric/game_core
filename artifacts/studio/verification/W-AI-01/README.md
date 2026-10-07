# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **PASS**. One real green cloth image passed through entity.setMaterialTexture; the real Hollowmere view used it in the body material slot, behaviour inputs and runtime profile stayed equal, and normal History undo restored the authored binding and rendered material. No tint substitute or direct binding call. Visual review: Generated pixels show green woven cloth. Actual Maren body is visibly olive before, dark green with coarse weave after application, and olive again after normal Undo. All three viewport captures frame the complete body; Studio captures identify the workflow. Capsule geometry and distant magenta geometry are unchanged, not improved. Initial attempts stopped before generation because the ledger observer was absent; both are retained, and the same unused reservations were subsequently exercised with observers.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42k.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe
```

## Current-run evidence

- [W-AI-01/p42k-robe/workflow/result.json](../W-AI-01/p42k-robe/workflow/result.json)
- [W-AI-01/p42k-robe/workflow/ledger-final.json](../W-AI-01/p42k-robe/workflow/ledger-final.json)
- [W-AI-01/p42k-robe-20261007T190127.927059Z/workflow/result.json](../W-AI-01/p42k-robe-20261007T190127.927059Z/workflow/result.json)
