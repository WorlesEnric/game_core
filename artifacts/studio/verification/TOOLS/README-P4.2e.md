# P4.2e final R6 rows

Product source is main `d140f748` in this packet's retained `.evidence/live` clone.
Never use a sibling checkout. Build `studio/agent` here with fmt, clippy, tests and
`cargo build --release --locked`, then use `install-p42d.sh` (it resolves its own
checkout, verifies main, registers the local projects, and activates the immutable
release via `etos agent restart gamecore-studio`). No etosd restart.

The owner has declared describe USD 0.01/call as an **operator estimate**, not an
Echo invoice. The requested `install.sh --apply-prices --only describe` currently
refuses because main's CLI lists only `tts`. `apply-describe-p42e.py` calls the
existing installer `apply_prices(root, only='describe')` seam; it changes only the
selected provider and matching companion price. The packet records the CLI request.

Copy `P42cLive`, `P42bHarness` and `P42eLive` into the scratch project's Assets before
launch. `python3 artifacts/studio/verification/TOOLS/live-p42e.py LANE` supports:

- `baseline` once; `receipt-hello` verifies the operator tariff and 3D refusal.
- `regression` uses the real headless Editor for the 40 R6/driver cases.
- `describe` reserves one image and one describe; no automatic replay.
- `stage-submit --candidate DIR`, then `watch-stage-p42c.py STAGE_REQUEST_JSON`.
- `receipt-stage` with `GAMECORE_P42E_STAGE_JOB=...`, then
  `stage-review --candidate DIR --input STAGE_REQUEST_JSON`.
- Negative `stage-submit` / `stage-review` additionally use `--negative`.
- `text2`, `narrative`, `reopen`, `voice2` use the unchanged R6-B production drivers.

`instantiate-p42d.py SOURCE NEW_DEST` changes only envelope identity, preserving
sample package/proposal/operations. `provision-p42d.sh` provisions this project's
exact owner/version cache without resetting cold markers or budgets. Run
`studio/stage/analyzer/offline-check.sh NEW_WORK CACHE` for actual semantic-negative
Docker refusal. Candidate lexical refusal and Roslyn refusal remain separate facts.

The stage panel harness explicitly invokes creator Admit, observes the real compiler,
records restored inventory and 120-frame Pending→Passed smoke, then performs normal
history undo. Acceptance requires the 90-second bound; cleanup runs even if that
bound fails. All Editors use `unity-batch.sh`; the submit adapter holds the allocator
barrier until the creator Editor exits, leaving at most one packet-owned Editor.

Caps: **2 images / 3 TTS / 2 describe / USD 3**, no paid 3D. Describe's one call follows
the packet's explicit operator-price authorization. Reservations are immutable;
provider failure does not authorize replay. `retain-p42e.py` records media ledger
charges, task budgets and voice telemetry. Voice has no invoice/USD ledger field.

`restore-fixture-p42e.py LABEL` retains a diff and restores only this scratch clone's
tracked authored fixtures with no Editor active. Never restore between narrative
save and reopen. `report_p42e.py` consumes reviewed `row-dispositions.json`, regenerates
ROWS/SUMMARY and matrix status/evidence cells, preserving untouched-row wording.
