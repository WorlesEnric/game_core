# P4.2g installed qualification

Product baseline: `55091b74be95eb6e47fe0e33c01ce2d8f2528779` (main after R6-E/R6-F/R6-G). Acceptance changes are separate. Use an owned `.evidence/live` clone at that baseline; do not use sibling clones or inspect credentials. All Editors run serially through `unity-batch.sh` and its host allocator. Graphical runs use display `:1`.

## Setup

1. Run companion `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test`, and `cargo build --release --locked` in `studio/agent`; retain each command's result.
2. Copy `P42cLive`, `P42bHarness`, `P42eLive`, and `P42gLive` from this directory into the owned clone's Hollowmere Assets. Copy this packet's four modified acceptance sources: `Tests/P3_2/Editor/WorkflowPlayChecks.cs`, `WorkflowRunner.cs`, `Workflows.cs`, and `Tests/R6_B/Editor/RealPathTests.cs`, to their corresponding owned paths. Product packages remain baseline.
3. With no Editor/recorder running, invoke `activate-worker-p42g.py`. It uses the R6-F installer with the operator-approved USD 0.50 designer manifest, verifies exact binary/worker digests, registers owned projects, and checks repeat-no-op. `--approve-designer-budget-manifest` is only needed if the installed manifest lacks the existing node ceiling; the P4.2g deployment did not need reconciliation.
4. Start `monitor-p42g.py` before any Editor. It is a long-running owned monitor; stop only that process at the end. Run `live-p42g.py baseline` once, then `receipt-hello` and `regression`. Never reset the ledger baseline/reservations.
5. `provision-p42d.sh` provisions the owned project's exact versioned cache. Never remove cold-attempt markers or widen budgets.

## Mechanism

Mint fresh envelope identities with `instantiate-p42d.py` from `samples/mechanisms/pressure-plate/candidate` for cold and warm. Package/proposal/operations remain byte-identical. Use the retained packet candidates only to reproduce historical jobs, not as new-run evidence.

```sh
python3 artifacts/studio/verification/TOOLS/live-p42g.py stage-submit --candidate CANDIDATE
python3 artifacts/studio/verification/TOOLS/watch-stage-p42c.py EXACT_STAGE_REQUEST_JSON
GAMECORE_P42E_STAGE_JOB=JOB python3 artifacts/studio/verification/TOOLS/live-p42g.py receipt-stage
python3 artifacts/studio/verification/TOOLS/live-p42g.py stage-review --candidate CANDIDATE --input EXACT_STAGE_REQUEST_JSON
```

Discover the exact `stage-request.json` emitted by submit; do not reconstruct its timestamp. The submit barrier releases the creator Editor before sandbox Unity allocates. The graphical harness explicitly invokes creator Admit from Play, observes production reload recovery without injecting an authenticated service, proves captured coins/120-frame tri-state smoke, and invokes normal History Undo. Retain each owned `upm.log`. A timeout gets exactly one retry; P4.2g's completed admissions did not time out.

Negative submit/review use a fresh `candidate-forbidden` envelope and `--negative`. No issued passing verdict may enable Admit. Separately run `studio/stage/analyzer/offline-check.sh NEW_WORK EXACT_CACHE` for actual production Docker Roslyn refusal of `negative-semantic`; lexical stage refusal is not semantic-stage success.

The first completed cold review used the historical P42e harness. Its local/UTC `DateTime` subtraction produced a negative displayed timer. Original evidence is unchanged; `timing-audit.json` derives elapsed time from the durable Unix-UTC `startedMs` and retained completion timestamp. P42g's harness parses stored times back to UTC; warm uses that corrected timer. An earlier cold setup launch had the wrong request filename and refused before Admit; retained separately.

## NPC and regression

```sh
python3 artifacts/studio/verification/TOOLS/live-p42g.py regression-final
python3 artifacts/studio/verification/TOOLS/live-p42g.py npc
```

The NPC lane checks node-served instructions and the USD 0.50 budget before its one-use reservation. It runs the existing `p42f-npc` workflow: actual NavMesh measurements become the creator's clarification answer, never candidate repairs. It applies fresh worker output, invokes `WorkflowPlayChecks.Effect("W-AI-02", ...)` in graphical Play, records closure membership/dialogue startup/bell lines/end, and chooses undo from R6-C `AppliedOr`'s durable journal. Image/TTS/describe lanes are unreachable; 3D is refusal only. No automatic text replay.

The first NPC lane may request a second clarification about the template's actual graphical view. Retain that no-candidate outcome. `live-p42g.py npc-view` enters graphical Play through a reload-safe Editor update driver and observes resident Pip's active view, enabled NavMeshAgent and on-NavMesh state, verifying its prefab is exactly the prefab referenced by OddEntity. The separately reserved `npc-confirmed` lane adds this measured context. `npc-creation` explicitly requests a new Ferryman Elian rather than repurposing existing Bram and explains supported new-asset path references. No candidate bytes are repaired.

Final complete proof uses `GAMECORE_P42G_NPC_VIEW=EXACT_RECEIPT_JSON python3 artifacts/studio/verification/TOOLS/live-p42g.py npc-evidence-final`, after establishing a clean owned 20-entity fixture. Its `PlayReceipt` persists actual observations across coroutine ticks, then normal journal undo is explicitly saved before closing. Do not repeat the earlier unsaved-fixture mistake: the first detailed-receipt attempt opened an old unsaved undo and failed bake; that failure remains retained. Each text lane is one-use reserved and the same USD 0.50 worker ceiling remains enforced. Run `regression-final` after final proof; its XML is 45/45.

## Reporting

Run `retain-stage-p42g.py` and `retain-ledger-p42g.py`. Build reviewed `P4.2g/row-dispositions.json` from actual receipts: each of the two row objects has `status`, `note`, `command`, `patterns`, and explicit `checks` (XML counts or JSON-pointer equality assertions). `report_p42g.py` changes only those row records/cells and regenerates SUMMARY/ROWS; `check_p42g.py` validates citations, source-bound signed receipts and zero-media USD 0.50 accounting. Host exclusivity is a separate strict gate: unknown process samples are retained, not reclassified to force PASS.

No upstream provider invoice is available. Node-reported zero cost is not proof of free model execution.
