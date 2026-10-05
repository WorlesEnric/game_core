# R2-F companion packet

Branch: `codex/r2-f`. All builds and tests run on myubuntu. No ETOS paid operation, installed companion restart, or sibling checkout change is part of this packet.

## Implemented contracts

- Every HTTP/WS route requires the existing authenticated proxy app plus `X-GameCore-Project: <64 hex>`. Internally the owner is the unambiguous JSON tuple `[app, project]`. Old unscoped rows are deliberately not exposed to project-scoped clients. Same-id submissions by another owner return 404. Candidate manifests and explicit grants associate artifacts with owners. Media and voice events retain their owner, and filtered replay advances the server cursor over other owners' events.
- `POST /v1/stage` takes `{changeSetId, projectId, sourceRevision, catalogRevision, action?:"stage", steps?:string[], slot?:string}`. `projectId` must equal the header. `[stage.projects]` maps project IDs to operator-selected absolute Unity project paths. Client `sourceProject` and legacy `packageRef` are refused. Source HEAD and the candidate request's catalog revision must match; source revision is rechecked after slot preparation. Discard takes `{changeSetId, projectId, action:"discard"}`. Jobs and slot roots belong to the authenticated owner.
- `GET /v1/stage/{job}/verdict` returns only an issued, complete, passing signed record. `POST /v1/stage/{job}/verify` takes that entire record and returns `{verified:bool}`. Both require the same app/project authority and return 404 for another owner's job. Fields added to the existing verdict are `jobId`, `app`, `projectId`, `sourceRevision`, `catalogRevision`, `packageDigest`, `proposalDigest`, `confinement`, `coldCache`, and `signature`. HMAC-SHA256 covers canonical JSON of the entire record except `signature`, including steps and `verdictRef`. The unsigned CAS artifact is evidence, never admission authority. The key is created once by atomic hard-link publication with mode 0600 in companion state, never a repository or Unity project.
- `[stage] confinement = "docker"` is the default; `"host"` is an explicit operator opt-in, recorded in all resulting verdicts. `docker_image` defaults to `gamecore-stage:6000.0.75f1-v1`; it must be provisioned locally, and the service never implicitly pulls images. Docker has no network, a read-only root, no capabilities, no privilege escalation, and no live project/host HOME/daemon socket/provider environment. Only the slot and versioned cache are writable. Unity, licence directories, and trusted package sources are read-only. Launch specifications and executable wrappers live outside candidate-writable mounts. A missing/unlicensed sandbox produces `stage_failed` with `reason:"sandbox_unavailable"`; no verdict is issued and there is no host fallback.
- Configured staging is probed at service startup and before each job. Probes and actual Unity work use the repository `unity-batch.sh` allocator with at most one held reservation. Engine output uses `-logFile -`; the Rust wrapper redacts before durable writes. The launcher removes its own daemon-owned container after completion or timeout.
- The seven steps remain scan/checkers/dotnet/unity-editmode/playmode-smoke/determinism/budget. The dotnet step always runs the trusted semantic analyzer first, including packages without a Rules half. Missing/malformed/failing analyzer output fails the step and prevents Unity. Cache identity includes Unity version and the kernel/gameplay package manifests; warm budget remains 360 seconds and cold runs record `coldCache:true`. Cold grace (1800 seconds) is consumed once per cache version; later attempts use the warm budget even if the first cold attempt failed.
- Synchronous ledger, CAS, hash, fsync and GC operations in service requests/followers run in cancellable `spawn_blocking` service lanes. Network/timer executor threads remain free; futures waiting on networking can be cancelled. This retains the existing synchronous ledger API for operator and fixture code. No synchronous guard crosses an await.

## Findings and regression tests

| Finding | Change | Regression |
|---|---|---|
| R2-09 | Installation-bound HMAC, complete mandatory steps, authenticated fetch/verify | `r2_09_hmac_rfc4231_and_installation_binding`; `r2_09_not_applicable_never_bypasses_a_mandatory_step`; `r2_09_signed_verdict_transport_rejects_tampering_and_partial_jobs` |
| R2-11 | Default Docker boundary; no lexical path/argument/unsafe-reason exemptions; mandatory semantic step | `r2_11_docker_command_has_no_live_project_or_host_home`; `r2_11_textual_paths_and_editor_hooks_never_bypass_prefilter`; `r2_11_unsafe_reason_never_grants_authority`; `r2_11_cache_versions_and_one_cold_budget`; `r2_11_docker_isolation_blocks_host_files_environment_and_network`; `r2_11_missing_semantic_analyzer_fails_dotnet_without_executing_candidate` |
| R2-13 | Candidate-only DTO preserves proposal/stageInputs; trusted project map | `lane_requests_are_strict`; `r2_25_all_routes_isolate_apps_and_projects_and_replay_cursors`; existing stage-lane materialization tests |
| R2-16 | Permanent advisory-lock inode outside slot, PID/random token diagnostics, GC/discard hold lock | `r2_16_unparsable_locked_inode_survives_acquisition_and_gc`; `one_stage_per_slot`; `gc_removes_old_unlocked_slots_only` |
| R2-18 | Exact child environment allowlist; fixed service executables | `provider_keys_and_tokens_never_reach_a_stage_child`; `a_child_gets_the_allowlisted_env_and_is_killed_at_its_deadline` |
| R2-19 | Shared prefix/JSON redactor and streaming writer; retired external tar route | `r2_19_stream_and_json_secrets_are_redacted_before_write`; `r2_19_child_log_is_redacted_before_process_exit`; `r2_19_legacy_stage_extractor_is_retired`; child timeout test |
| R2-25 | App/project authority across routes, events, CAS, stage, catalogs and index | `r2_25_all_routes_isolate_apps_and_projects_and_replay_cursors` |
| R2-26 | Rehash CAS reads and duplicate puts; verified atomic repair | `r2_26_corruption_is_refused_and_verified_put_repairs` |
| R2-27 | Blocking-pool service lanes with cancellation | `r2_27_slow_storage_does_not_stall_executor`; existing cancel/restart fake-node tests |
| R2-28 | Required sequence from zero; rejected frames never consume sequence or reach provider | existing fake-node voice streaming test now injects missing/gapped/invalid input before valid sequence zero |

## Requests to other packets

- R2-G, `studio/stage/make-slot.py`: retain the fixed `--slot-root/--slot/--source-project/--candidate/--warm-library` CLI consumed by `prepare_slot`. Complete R2-12's data-only stage input/template validation, link rejection and contained proposal rules/test paths before integrated admission qualification; this packet cannot modify that Python trust boundary.
- R2-H/D, `docs/studio/04-etos-integration.md` sections 2/5/7: reconcile the project header on all routes, owned-artifact-only describe input, owner-namespaced media idempotency and index keys/links, and redacted JSON `data.key` (the deterministic noncredential operation id still appears in the recovery hint). R2-F changes only section 6, as assigned.

- R2-G, `studio/stage/analyzer/`: provide the trusted Roslyn analyzer executable project with fixed CLI `dotnet run --project <analyzer> -- --root <slot> --rules <json> --out <findings.json>`. Exit zero only when findings are empty; output `{pass:true,findings:[]}` for success, `{pass:false,findings:[...]}` otherwise. Rules document: `{schema:"gamecore.stage.semantic-rules/1",package:string,forbidEditorHooks:true,forbidUnsafe:true,forbidProcess:true,forbidNetwork:true,fileRoots:["Assets/<package>","persistentDataPath"]}`. Scan all executable inputs semantically, permit only declared AuthorOperation/AuthorValidator/catalog extension points, and normalize filesystem authority. The runner copies the trusted analyzer into the slot so its own build output is sandbox-local. Missing analyzer is a tested failure, never a pass. Pre-provision analyzer dependencies in the image/versioned cache because Docker networking is disabled.
- R2-G, `studio/tools/unity-batch.sh`: preserve `UNITY=<wrapper>` support and the final engine argument list. This packet's wrapper strips the raw `-logFile` path, invokes Unity with `-logFile -`, and streams redacted output into the requested log. Your allocator must provide the D3 host-wide atomic reservation contract; Rust does not duplicate it.
- R2-B/D/C: consume the exact stage request and signed-record transport contracts above. Fetch the signed record from `/verdict`; verify through `/verify`; enforce mandatory steps and binding to the current proposal/source/catalog/project; expose host-confinement warning; keep admit/remove internal to explicit creator actions. Do not accept `verdictRef` artifact bytes as authority. The R2-B contract was not present in this checkout, so this is the published reconciliation seam.
- R2-D: send `X-GameCore-Project` on every HTTP request and ticketed WebSocket, derived from productGUID plus canonical project path and persisted in UserSettings. A missing project header is 400, not an implicit global project.
- Integrator: `studio/agent/Cargo.toml` declares edition 2024/rust-version 1.97.1 outside R2-F's exclusive paths. A Rust 2021 conversion requires that manifest change plus conversion of existing 2024 let-chain syntax across the crate. This packet does not change an unowned manifest or claim Rust 2021 qualification.

## Left open

- Semantic analyzer implementation and end-to-end accepted Stage → Admit remain dependent on R2-G and R2-B/D integration; their files are exclusively owned by those packets. No passing admission verdict is claimed from the fake fixtures.
- Unity licensing in Docker failed with exit 198 and “No valid Unity Editor license found” despite the actual entitlement directory being mounted read-only. Default Docker remains refusing. No real host-mode Unity qualification was attempted. Final gate totals are recorded in `r2-f-sandbox-probe.txt` and the validation evidence. No paid node qualification is run. A failed Docker probe leaves default confinement refusing; host mode is available only by explicit operator configuration, never selected here.
- The repository's ignored real-node and real-Unity acceptance suites require their documented external prerequisites. They are not counted as passes.
- `PACKET.md` is placed under `studio/agent/evidence/` to honor the exclusive-path rule; the companion packet note links here.


## Validation

The source at `03246274ae89fe26b6366b9bba8184ff51f4c1d3` passes fmt, clippy with warnings denied, 78 unit tests,
21 fake-node tests and four stage-lane tests. The explicitly executed Docker integration
fixture also passes: 104 executed passes total. Four external node/real-stage fixtures
remain ignored. Package metadata and all 1,076 C# files pass their checkers. Exact commands
and output are in `r2-f-validation.txt`. The real licence probe is a recorded refusal
(exit 198), not passing Unity qualification.
