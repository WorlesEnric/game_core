# P4.2f installed qualification

Product source: main `4ac7ba858b91e73e2d5de9dc6f02852c13feec56`; retained owned
`.evidence/live`. Do not use sibling clones, read key files, restart etosd, or
substitute the R6-D synthetic-auth scratch service for the installed companion.

## Setup

1. Build the local `studio/agent` with cargo fmt, all-target clippy, tests and
   `cargo build --release --locked`. Run `install-p42d.sh` with this packet's owned
   live clone; it checks main, registers projects and activates an immutable release.
2. Copy `P42cLive`, `P42bHarness`, `P42eLive` from this directory into that clone's
   Hollowmere Assets. Copy the owned P3_2 `Workflows.cs` harness when exercising
   the measured-context NPC lane. No product package changes are needed.
3. Run `live-p42f.py baseline` once, then `receipt-hello` and `regression`.
   Baseline/reservations must not be reset. `monitor-p42f.py` preserves process
   ancestry; stop only that owned monitor when qualification ends.
4. `provision-p42d.sh` provisions this project's exact owner/version cache.
   Never delete cold-attempt markers or widen budgets.

## Mechanism

For cold and warm, mint a new envelope ID with `instantiate-p42d.py`; package,
proposal and operations stay byte-identical. Use the retained P4.2f candidates
only to reproduce these exact historical jobs, not to pretend a new job was run.

```sh
python3 artifacts/studio/verification/TOOLS/live-p42f.py stage-submit --candidate CANDIDATE
python3 artifacts/studio/verification/TOOLS/watch-stage-p42c.py STAGE_REQUEST_JSON
GAMECORE_P42E_STAGE_JOB=JOB python3 artifacts/studio/verification/TOOLS/live-p42f.py receipt-stage
python3 artifacts/studio/verification/TOOLS/live-p42f.py stage-review --candidate CANDIDATE --input STAGE_REQUEST_JSON
```

The wrapper keeps the shared allocator, display `:1` and a unique `-upmLogFile`.
The creator harness explicitly calls Admit from graphical Play. It requires real
restore, 120-frame Pending→Passed smoke, the unchanged 90-second bound and normal
History undo. A timed-out graphical attempt gets exactly one retry, retaining both.
All four P4.2f positive graphical attempts roll back with `compile_timeout`.

Negative submit/review adds `--negative`; no passing signed verdict may enable
Admit. Separately run `studio/stage/analyzer/offline-check.sh NEW_WORK OWNED_CACHE`
for production Docker semantic refusal; a lexical-refused stage is not a semantic
stage pass. `retain-stage-p42f.py` collects XML counts and owned UPM durations.

## NPC

`activate-worker-p42f.py` uses the supported agent-upgrade seam to update the node's
stored worker definitions: binary restart alone does not do that. Its immutable
operator release contains the exact main binary/instructions; only designer
`budget = { usd = 0.50 }` is added to the manifest. No grants or model are changed.
Run with no Editor/stage active. It never reads or copies credentials.

```sh
python3 artifacts/studio/verification/TOOLS/activate-worker-p42f.py
python3 artifacts/studio/verification/TOOLS/live-p42f.py receipt-hello
python3 artifacts/studio/verification/TOOLS/live-p42f.py text2
python3 artifacts/studio/verification/TOOLS/live-p42f.py npc
```

The original `text2` asks for navigation facts but answers with generic intent;
P4.2f retains its `needs_clarification` outcome unchanged. The separate `npc` lane
measures actual loaded-scene `NavMesh.SamplePosition` and complete paths, retains
those facts, and supplies them as the creator's answer to the worker. It does not
repair any candidate. Apply, actual `WorkflowPlayChecks.Effect("W-AI-02", ...)`,
and R6-C `AppliedOr`/normal History undo remain production-backed paths. Each text
lane is one-use reserved; there is no automatic paid replay. Image/TTS/describe
lanes are unreachable through this adapter; 3D is refusal only.

## Reporting

`retain-ledger-p42f.py` retains packet task budgets and the immutable media delta.
The USD ceiling is enforced by the worker's node-accounting scope; zero reported
cost is not a verified reseller invoice. `report_p42f.py` consumes reviewed
`workflows/P4.2f/row-dispositions.json`, changes only the two row cells/records,
and regenerates ROWS/SUMMARY. `check_p42f.py` checks XML, identities, ledger and
credential-shaped values, then keeps strict host exclusivity as a separate gate.
One earlier transient child had empty arguments; raw samples are retained and no
unproven process classification is used to force that gate to pass.
