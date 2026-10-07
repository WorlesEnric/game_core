# R8-A — dialogue publication and running-stage cancellation

This is the packet's PACKET.md. Branch `omp/r8-a`.

## Scope

Owned verification rows: W-ETOS-04 (SR-2.4/4.3), W-REC-03 (SR-9.1). Installed services and paid operations are out of bounds; acceptance uses scratch infrastructure and batch Editors.

## R2 fixes

| Finding / row | Fix | Regression |
|---|---|---|
| SR-2.4/4.3 / W-ETOS-04 | Session-owned index publisher; project-open snapshot, rebuild/apply/undo deltas; durable owner-scoped graph expansion and worker query guidance. | `w_etos_04_graph_fields_publish_discoverable_owned_dialogue_nodes`, `w_etos_04_graph_revisions_clear_removed_fields_shrink_undo_and_delete`, `w_etos_04_project_open_after_restart_removes_stale_graphs_only_for_owner`; Unity `IndexPublicationTests`. |
| SR-9.1 / W-REC-03 | Authenticated owner-scoped cancel route; durable cancelling/cancelled transitions, child-group and Docker teardown, slot release and no verdict; client and creator Stage panel. | `w_rec_03_cancel_is_authenticated_owner_scoped_and_durable`, cancellation/completion ledger races, real child teardown and restart regressions; `R8StageCancellationTests`. |

## Verification

- Baseline `7f5cacb8464b040bab2a870ecc7f0e19f475984c`, isolated source copy with unchanged new regression files: dialogue publication **0 passed / 3 failed** (missing graph/node rows); authenticated cancellation **0 passed / 1 failed** (404 missing route instead of 200). Logs retained under the owned row evidence directories.
- Integrated Rust: `cargo fmt --check`, `cargo clippy --all-targets -- -D warnings`, `cargo test` and companion build pass: **153 passed / 12 ignored**. The initial full run exposed a cache-refusal regression; fixed controlled cache logging to avoid creating an unvalidated slot, then reran the complete gates.
- Existing dotnet ETOS client: **69 passed / 6 environment-gated skipped**. Standalone scratch driver builds with zero warnings/errors.
- First Unity compile found a missing acceptance-harness assembly reference and ambiguous `AuthorScope`; fixed before rerunning. No acceptance is claimed from that failed launch.
- Corrected batch EditMode run: result XML **199 passed / 0 failed / 0 skipped / 0 inconclusive**, including both lifecycle publication and both cancellation-acknowledgment regressions. XML is retained under each owned row's `r8-a/` directory.
- Product checkpoint `fccf3f8c` committed and pushed before scratch qualification. Exact versioned stage cache `22421f6df7cfc2cc396043796f1a4d9a966cbcd1e8c4a3287f60eb3816fa1c22` verifies successfully.
- Real scratch ETOS node, installed fresh companion and actual Docker worker: **W-ETOS-04 PASS**, selected Bram returns exactly **8 nodes**, unfiltered owner query **53 nodes**, node indices/text checked against the real asset. Worker tool-call trace and authenticated stdout retained; zero seeded rows, zero external provider calls. Local deterministic model fixture chooses the shell tool only; query execution and responses are genuine.
- **W-REC-03 PASS**: native region load observed unfinished at progress 0.9, cancellation settles all regions unloaded (one load, one unload, no failures). Real Docker stage `stg_1a114232ad123b9e94dfc3a` reaches semantic-analyzer dotnet execution with slot lock held. Actual attached Stage Cancel button produces durable `cancelled`, removes every job-labelled container, releases the held slot lock, leaves no issued verdict, and keeps Admit disabled. Authenticated verdict fetch returns 404; scratch-companion restart preserves state. Foreign app/project cancellations return 404; unauthenticated proxy request returns 401.
- Concurrency synchronization is explicit: pause only the exact owned running dotnet container after its Unity probe completes, then launch the second batch Editor and cancel. No two packet Editors overlap; no substitute stage process. The stage slot is held and released; no Unity allocator reservation is held during the paused dotnet phase, and none remains afterward.
- Existing UI suite XML: **64 passed / 0 failed / 5 skipped**. All five are graphical-only window/input/render tests; the batch wrapper correctly labels this partial rather than PASS. Actual Stage button behavior is independently exercised by scratch acceptance. No graphical appearance claim.
- First scratch attempt already returned real 8/53 rows but its observer confused the JSONL provenance header with data. Corrected to use the CLI structured rows/complete/count fields; reran fresh scratch-02 without weakening graph or owner assertions.
- Full public evidence, compressed logs and SHA-256 manifest: `games/hollowmere/Assets/Hollowmere/Tests/R8_A/Evidence~/scratch-02/`. Per-row copies: `artifacts/studio/verification/{W-ETOS-04,W-REC-03}/r8-a/`. Matrix changes are limited to these two rows and totals: **57 PASS / 10 BLOCKED / 1 FAIL**.
- Final policy gates pass: **42 packages / 92 package assemblies / 1,256 C# files**. Retention verification checks all 48 public evidence manifest entries against decompressed SHA-256; no credential-shaped value found. Owned temporary scratch nodes and copied caches, baseline source copy, local evidence staging and helper build outputs were removed after retention; Unity-generated out-of-scope meta changes were restored to their pre-run state.

## Reproduce

```sh
CARGO_TARGET_DIR=/tmp/r8-a-cargo-target cargo build --manifest-path studio/agent/Cargo.toml --bin gamecore-studio
dotnet build studio/agent/tools/r8-a/Client/Client.csproj
/tmp/r8-a-cargo-target/debug/gamecore-studio stage cache-path --repo "$PWD" --source-project "$PWD/games/hollowmere" --root "$HOME/.cache/gamecore-studio/stage"
studio/stage/provision-cache.sh <exact-cache-path> --verify
python3 games/hollowmere/Assets/Hollowmere/Tests/R8_A/run.py run \
  --companion /tmp/r8-a-cargo-target/debug/gamecore-studio \
  --cache <exact-cache-path> --evidence "$PWD/.evidence/r8-a/<fresh-name>"
```

Run the orchestrator under the process supervisor. It creates a private `/tmp/r8a-*` node root,
starts/stops only its owned node and companion, uses a scratch paired app through the actual
proxy, and executes both sequential batch Editors through `unity-batch.sh`. Existing project
ETOS settings are refused rather than read or overwritten. The trusted client's credential
resolver consumes the pairing path; no key contents are inspected or printed. The worker
controller uses only a local deterministic tool selector, not a paid provider.

## Requests to other packets

None currently. The baseline has no `studio.ui/Editor/Stage` directory or standalone Stage panel. This packet adds its Stage window in that owned directory rather than editing the existing Candidates panel outside its exclusive paths. It consumes the existing candidate coordinator and admission service.

## Left open

Both assigned rows are proven PASS. Graphical-only UI regressions remain unrun under this packet's batch-only mandate; their five XML skips are explicit, not acceptance passes.

Restart-only recovery limitation: if a recorded process-group leader has vanished while live
members remain, Linux exposes no group creation identity proving that its PGID was not reused.
The service refuses to signal that unverifiable group and retains `cancelling` with no verdict,
rather than falsely acknowledging teardown. Exact job-labelled Docker cleanup still runs.
Normal running cancellation and recovery with matching live/zombie leader identities terminate
their owned groups. No authority or cleanup acknowledgment is granted on this refusal.
