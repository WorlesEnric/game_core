# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **PASS**. P4.2h second real USD0.20 image is green woven cloth, imported through current media and assigned by explicit engine entity.setMaterialTexture. Actual GameBoot/New Game Maren body uses that texture via production property block; visible olive-to-forest-green cloth change and normal History restoration are captured. Behaviour/NPC/dialogue/village source hashes, runtime profile/route/schedule and structural recipe remain unchanged. Existing stylized capsule geometry and background graphics are not claimed improved.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe --workflow-out artifacts/studio/verification/W-AI-01/p42h-robe/workflow
```

## Retained evidence

- [W-AI-01/p42c-robe2-20261006T072832.434641Z/README.md](../W-AI-01/p42c-robe2-20261006T072832.434641Z/README.md)
- [W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/robe/apply-report.json](../W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/robe/apply-report.json)
- [W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/icon/assign.json](../W-AI-01/p42c-robe2-20261006T072832.434641Z/workflow/icon/assign.json)
- [W-AI-01/p42h-robe/workflow/result.json](../W-AI-01/p42h-robe/workflow/result.json)
- [W-AI-01/p42h-robe/workflow/visual-review.json](../W-AI-01/p42h-robe/workflow/visual-review.json)
- [W-AI-01/p42h-robe/workflow/play-applied.json](../W-AI-01/p42h-robe/workflow/play-applied.json)
- [W-AI-01/p42h-robe/workflow/play-undone.json](../W-AI-01/p42h-robe/workflow/play-undone.json)
- [W-AI-01/p42h-robe/workflow/ledger-final.json](../W-AI-01/p42h-robe/workflow/ledger-final.json)
