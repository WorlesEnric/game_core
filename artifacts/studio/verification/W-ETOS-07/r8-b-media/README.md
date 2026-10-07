# R8-B — real generated texture and tamper workflow

**PASS**, W-ETOS-07 / SR-4.6. Product base `7f5cacb8`, execution checkpoint `0adcdda20527b77f88c87a2277a50ab4f3dcd8cd` plus the retained R8-B driver (`verification.json` binds its source SHA-256). The companion was built locally from this checkout. A private real etosd, private companion installation, authenticated paired app, exact operator image tariff and node USD 0.20 budget were used. Installed services were not touched.

## Results

- Graphical Unity executeMethod on `:1`, through the shared `unity-batch.sh` allocator: exit **0**, 88 seconds. No concurrent Editor from this packet.
- Exactly **one** `generate.image` POST, one `echo-images` / `gpt-image-2` result, operator charge **USD 0.20**. No describe, TTS or worker requests. Real node usage and budget are both 200000 micro-USD. These are operator/node accounting, not a provider invoice.
- Verified download, `EtosMediaGenerator.ImportOnMain`, then typed `entity.setMaterialTexture` through ChangeSetEngine. Ordinary History undo/redo/final undo restores import and material binding without generation. The owned target definition uses Maren's existing prefab; no game content asset is changed.
- Generated/applied/redone PNG SHA-256: `36e071fa68dc08dad9cdd913d3e7dbf54c7a8f501a07007e39c29003c4fc7d50`, 3,752,328 bytes. The provider returned 1254×1254 pixels; no resize occurred. Visual inspection shows green woven cloth. This is not a rendered-healer or desktop screenshot claim.
- Flipping one downloaded byte yields `1c05030cf7bbd2be30571ae4d03b51c8309d742c2b7e4fecfc1bb24d543a1799`. Client verification and media import both return **artifact_digest_mismatch**. Tampered asset/meta are absent and journal count stays at two.
- All post-generation charge/producer checkpoints remain unchanged. Final undo restores the original target's content and structural recipe before owned fixture cleanup.
- Separately, Main's existing client TRX: **69 passed, 0 failed, 6 environment-gated skips**. Test credential-shaped values were redacted; `client-tests-redaction.json` retains original SHA and unchanged counters.

## Reproduction

Use the retained `Tests/R8_B/Media/scratch.py` prepare/serve/launch flow documented in the [row README](../README.md). The current evidence directory and dispatch reservation are deliberately non-reusable. A new paid run needs separate operator authorization; this packet's one-image allowance is exhausted. Do not delete the reservation to retry generation. The operational systemd environment-file resolver supplies provider credentials without the harness opening or printing them.

## Evidence and cleanup

`result.json`, `verification.json`, `http-exchanges.jsonl`, `node-audit.json`, `ledger-*.json`, exact journal/observation records, original/retained PNGs and both tamper receipts cover the complete workflow. `SHA256SUMS` binds the retained files.

`cleanup.json` records final History undo, owned fixture removal, private installation removal, inactive scratch unit and closed port. Initial repeated supervisor stop signals interrupted the finalizer; explicit shutdown of only the owned scratch unit completed cleanup. The corrected finalizer ignores repeated signals and was exercised without a paid call. No installed companion, installed etosd, sibling clone, rule, tariff or acceptance wording was changed.
