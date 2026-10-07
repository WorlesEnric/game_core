# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **PASS — R8-B complete live workflow**, 2026-10-07. Source checkpoint `0adcdda20527b77f88c87a2277a50ab4f3dcd8cd`; retained driver source SHA-256 is in [verification.json](r8-b-media/verification.json). The freshly built local companion SHA-256 is `be149e63d37747154e3c26c707277545f96e0bd670522a7bc395f3ae5e0855e1`.

## Actual result

[Result](r8-b-media/result.json) and [Unity command/log receipt](r8-b-media/unity-command.log): the sole live media Editor ran on owned display `:1` through `unity-batch.sh`, exited **0**, and completed in **88 seconds**. No Unity overlapped Main's probe.

1. A new private **real etosd** and newly built local companion were configured with only Echo `gpt-image-2` image generation. Authenticated [hello](r8-b-media/hello.json) reported the exact **operator-declared USD 0.20/image** tariff; TTS and describe were unconfigured. Unauthenticated proxy access returned [401](r8-b-media/unauthenticated-proxy.json).
2. A durable create-once [dispatch reservation](r8-b-media/dispatch-reservation.json) preceded the only generation call. Gateway automatic replay was disabled; node agent budget was USD 0.20 and image maximum output count was one. [HTTP observations](r8-b-media/http-exchanges.jsonl) contain exactly **one generation POST**, a successful verified download, and one later download for the tamper check. No historical or synthetic image was supplied.
3. The real [generation response](r8-b-media/generate.json) reports success and quantity one. The PNG is **3,752,328 bytes, 1254×1254**. The provider returned that size despite the 1024×1024 request; no resizing was requested or performed. Provider, downloaded, imported, retained and redone bytes all hash to:

   ```text
   36e071fa68dc08dad9cdd913d3e7dbf54c7a8f501a07007e39c29003c4fc7d50
   ```

4. `EtosMediaGenerator.ImportOnMain` retained verified bytes and applied ordinary `asset.import`, journal `cs_01M4A1P9A8XKEFWC1VKJWJSSTD`. An explicit **typed `entity.setMaterialTexture`** operation applied those imported bytes to an owned entity definition referencing Hollowmere's existing Maren prefab, journal `cs_01M4A1PAKYM8WWWW2TJAKDSDR0`. No game definition, scene, shared material or prefab was edited. [Assignment request](r8-b-media/assignment-request.json), [Applied report](r8-b-media/assignment-report.json), and the retained journals record the real tool and target.
5. Normal `HistoryService.Undo` removed the binding, then the imported image. Normal redo restored the **identical PNG** and material binding, with unchanged structural recipe. [Redone observation](r8-b-media/observe-redone.json) proves both. Final normal undo restored original entity content and removed the imported file; [final observation](r8-b-media/observe-final-undone.json) records the retained original digest and absent binding/file.
6. A one-bit mutation of the actual downloaded bytes produced `1c05030cf7bbd2be30571ae4d03b51c8309d742c2b7e4fecfc1bb24d543a1799`. The production media importer returned [`artifact_digest_mismatch`](r8-b-media/tampered-import.json), with journal count **2 → 2**, no destination image and no `.meta`. A second authenticated download of the same real artifact was altered after writing its temporary file and before verification using the existing client tamper hook; the client independently returned the [same digest refusal](r8-b-media/download-refusal.json). Nothing was imported from either altered byte sequence.

The [fresh generated image](r8-b-media/generated.png) was visually inspected and shows green woven cloth. This is **not** a desktop screenshot, rendered-healer acceptance or a new W-AI-01 claim. Applied/redone PNG copies and their exact hashes are retained in [verification.json](r8-b-media/verification.json).

## Cost and authority

**1 image / 0 TTS / 0 describe / 0 worker requests. Total binding charge: USD 0.20**, within the USD 0.30 packet ceiling. The image POST took 22,208.99 ms. [Generated](r8-b-media/ledger-generated.json), [undone](r8-b-media/ledger-undone.json), [redone](r8-b-media/ledger-redone.json), and [final](r8-b-media/ledger-final.json) read-only companion checkpoints have identical charge and producer records. The [real node audit](r8-b-media/node-audit.json) independently records an agent budget of 200,000 micro-USD and usage of exactly 200,000 micro-USD, with zero node workers and tasks.

- Effect key: `gc-d42d1260797e6b2adfb0e74ec4b8f7a35f8fa17e`
- Actual etos reference: `ref_01m4a1p8r23gsx61f02ca0enrq`
- Synchronous media receipt has no `jobId`; no job/request/task identity is invented.
- This is an **operator tariff/budget receipt**, not a verified Echo reseller invoice.

## Reproduce safely

The retained scripts intentionally refuse an existing run/dispatch reservation. A new paid qualification requires a newly authorized run identity/root and new evidence destination; never delete a reservation to retry an uncertain generation.

```sh
CARGO_TARGET_DIR=/tmp/r8-b-cargo-target cargo build --offline \
  --manifest-path studio/agent/Cargo.toml --bin gamecore-studio
python3 games/hollowmere/Assets/Hollowmere/Tests/R8_B/Media/scratch.py prepare
# Start under the harness process supervisor; wait for R8_B_MEDIA_READY:
python3 games/hollowmere/Assets/Hollowmere/Tests/R8_B/Media/scratch.py serve
# Only after Main explicitly grants the serial Unity slot, in a separate process:
GAMECORE_R8_MEDIA_SLOT_GRANTED=1 \
  python3 games/hollowmere/Assets/Hollowmere/Tests/R8_B/Media/scratch.py launch
```

The launcher invokes `Hollowmere.R8_B.MediaQualification.Run` through `unity-batch.sh`; the retained graphical wrapper removes batch/nographics flags and uses only `:1`. It forwards the newly paired scratch credential **path** to the production resolver, preventing graphical bootstrap from using the installed node. Systemd's normal `EnvironmentFile` mechanism resolves the existing provider environment operationally; the harness never opens or prints credential file contents. Private node/agent/provider config and the exact cap are retained in [isolation.json](r8-b-media/isolation.json). No installed companion/etosd was restarted or used for a paid operation.

## Cleanup and additional proof

[Cleanup](r8-b-media/cleanup.json) records real final undo before removing the owned target fixture and private scratch installation. The initial supervisor stop received repeated signals that interrupted its finalizer; an explicit stop of **only the owned transient unit** completed shutdown. The retained supervisor now ignores subsequent signals during its finalizer. A no-paid-operation shutdown smoke encountered the existing-state repeat-agent-install refusal, then executed the corrected finalizer and wrote [service-stopped.json](r8-b-media/service-stopped.json). The unit is inactive and scratch port closed. Original launch readiness is preserved in `service-ready-initial.json`; no setup/shutdown attempt is counted as another media pass.

Main's separate current-source [client TRX](r8-b-media/client-tests.trx) has **69 passed, 0 failed, 6 environment-gated skipped**; these are component results, not substitutes for the live workflow above. No formatter, linter or project-wide test suite was run by this row owner. Main owns shared project/Unity-generated metadata cleanup.

**Product changes or outside-scope requests: none for W-ETOS-07.** Only the acceptance driver, owned scratch support and evidence changed. Historical P4.2e and earlier evidence remains in the unchanged historical subdirectories with its original revision and partial disposition; it was not relabeled to obtain this pass.
