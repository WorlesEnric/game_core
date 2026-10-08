# W-AI-01: Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores

Verdict: **PASS**. One real green cloth image passed through entity.setMaterialTexture; the real Hollowmere view used it in the body material slot, behaviour inputs and runtime profile stayed equal, and normal History undo restored the authored binding and rendered material. No tint substitute or direct binding call. Visual review: Actual generated image is green woven cloth. The complete real Maren body changes from olive to dark green weave and returns to olive after normal History Undo. Distant magenta geometry remains; no aesthetics improvement claimed.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
export GC_STUDIO_DISK_RESERVE_GIB=28
python3 artifacts/studio/verification/TOOLS/P42hMedia/ledger.py --out artifacts/studio/verification/W-AI-01/p42l-robe/workflow &
observer=$!
python3 artifacts/studio/verification/TOOLS/live-p42l.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe --workflow-out artifacts/studio/verification/W-AI-01/p42l-robe/workflow
wait "$observer"
# One-use reservation: do not replay an already attempted run. P4.2l packet retains the robe pre-generation failure and exact baseline-restored continuation.
```

## Current-run evidence

- [W-AI-01/p42l-robe-baseline-restored/workflow/result.json](../W-AI-01/p42l-robe-baseline-restored/workflow/result.json)
- [W-AI-01/p42l-robe-baseline-restored/workflow/ledger-final.json](../W-AI-01/p42l-robe-baseline-restored/workflow/ledger-final.json)
- [W-AI-01/p42l-robe-baseline-restored/workflow/result.json](../W-AI-01/p42l-robe-baseline-restored/workflow/result.json)
