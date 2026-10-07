# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **PASS**. One fresh USD0.20 green cloth image is assigned through entity.setMaterialTexture in real Hollowmere Play. Actual olive-to-dark-green woven appearance and normal History restoration reviewed; behavior inputs/runtime profile stay unchanged.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe
```

## Current-run evidence

- [W-AI-01/p42i-robe/workflow/result.json](../W-AI-01/p42i-robe/workflow/result.json)
- [W-AI-01/p42i-robe/workflow/generate.json](../W-AI-01/p42i-robe/workflow/generate.json)
- [W-AI-01/p42i-robe/workflow/ledger-final.json](../W-AI-01/p42i-robe/workflow/ledger-final.json)
- [W-AI-01/p42i-robe/workflow/visual-review.json](../W-AI-01/p42i-robe/workflow/visual-review.json)
