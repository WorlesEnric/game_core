# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **PASS**. One current USD0.20 green cloth image passes entity.setMaterialTexture in real Play. Actual Maren body changes olive to dark-green weave and normal Undo restores its appearance; behavior inputs and runtime profile remain unchanged.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe
```

## Current-run evidence

- [W-AI-01/p42j-robe/workflow/result.json](../W-AI-01/p42j-robe/workflow/result.json)
- [W-AI-01/p42j-robe/workflow/ledger-final.json](../W-AI-01/p42j-robe/workflow/ledger-final.json)
