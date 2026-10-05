# R3-C — companion and node templates

Branch `codex/r3-c`, base `ed760892`. Linux host only. No live ETOS requests, provider calls,
service restarts, installed config edits or credential-file reads were performed. Sandbox owner
`studio/agent/src/stage/sandbox.rs` is unchanged. Packet evidence stays in this owned directory.

## Fixes and regressions

| Finding | Fix | Regression |
|---|---|---|
| D14 | Capped media calls require a positive operator-owned provider price, otherwise 409 `budget_unpriced`, before any downstream op. Prices pin the provider and check count/character quantity; unknown model/params/reference overrides refuse. `over_budget` is precise. The key binds the pinned input. Node model/ops templates carry cited publisher prices; companion template enables the verified Mainland TTS character tariff. | `r3_d14_retained_budget_refuses_unpriced_before_provider_call`; `r3_d14_priced_budget_binds_quantity_and_provider`; `r3_d14_bailian_published_character_examples`; Python `test_r3_d14_templates_prices_and_apply_diff` |
| D15 | Copy binary, manifest and worker files into `<root>/agents/gamecore-studio/0.1.0-<content hash>/`, verify SHA256SUMS on reuse, atomically replace current symlink under an install lock. Restart only through the integrator-run installer; health failure restores the old link. Old releases are retained, never uninstalled before health. Source rebuilds cannot replace the installed bytes. | `test_r3_d15_copy_is_immutable_atomic_switch_and_rollback`; `install-before.py` reproduces the old symlink defect in a temporary directory |
| D23 | New signed `POST /v1/stage/app-candidate`, authenticated by both existing proxy authority and node verification of the app signing key. Uses the same schema/catalog/candidate/artifact validation and CAS as agent candidates. Atomic immutable app-owned retention opens no task. Existing stage service binds source/catalog/package/proposal plus candidate digest and trusted origin (`app`/`agent`) into the signed verdict. Failing verdicts carry origin too and never attest/admit. | `r3_d23_retained_sample_signed_app_stage_and_tamper_refusals`: exact retained sample ID, package/proposal bytes, 202 stage, bad signature/key/bytes/project, collision, foreign job read, no worker task, no passing verdict |
| D24 | Installer/pairing writes `state/config.toml`, retains unrelated config and registered projects, and registers the stable Unity project ID. Startup warning lists project IDs and registration command. Unknown project returns 409 `stage_project_unregistered` with actionable hint. | `r3_d24_unregistered_project_has_registration_hint`; `test_r3_d24_register_preserves_config_and_matches_client_identity` |
| D3b | Authenticated hello works before a project header exists and advertises `minimumClientContract: 2`, `minimumClientRevision: 4635746`. Missing project header on scoped routes gives 426 `client_upgrade_required` and update hint. New signed route advertised as `stage.app-candidate.signed/1`. | `r3_d3b_hello_negotiates_before_project_header`; existing proxy/token-rotation suite |

The retained D14 JSON is
`artifacts/studio/workflows/P3.2/runs/honesty-20261005T095503Z/budget/generate.json`:
`maxCostUsd=0.001`, `ok=true`, reported `cost_usd=0`. D23/D24 replay the
`mech-b-20261005T124205Z/mech/panel-stage.json` diagnostic and the exact candidate
`samples/mechanisms/pressure-plate/candidate` with ID `cs_01K6RW0MECH00000000000000A`.

`regression-before.txt` records all three initial failures before implementation (D14/D24/D3b).
`d23-route-before.txt` records the new regression failing with the route addition removed, restoring
the old API surface. `install-before.txt` records the old installed binary changing after a rebuild.
All new positive tests use fake node/root fixtures. The app-stage test intentionally uses an absent
sandbox image, so it proves authenticated retention/dispatch and precise failed staging, not Unity
compilation or admission. Existing mandatory verdict/tamper tests remain required.

## Requests to other packets

**R3-B, D3b:** in `Packages/com.gamecore.studio.etos/Client/CompanionClient.cs` hello DTO/parser and
client initialization, consume `minimumClientContract` (integer) and `minimumClientRevision` (string).
If unsupported, show exactly “Update the Studio packages: this companion requires client contract 2
(revision 4635746 or later).” Treat 426 `client_upgrade_required` equivalently. Do not turn missing
project authority into a generic HTTP 400. Hello still requires the app key through the node proxy,
but may be called without `X-GameCore-Project` for negotiation.

**R3-B, D23:** add a client method in the same file with the seam
`Task<StageJobInfo> StageAppCandidateAsync(string changeSetId, string projectId, string sourceRevision,
string catalogRevision, JObject changeSet, JObject toolCatalog, IReadOnlyList<byte[]> artifactBytes,
CancellationToken ct = default)` (or a strongly typed equivalent). Integrate it in
`Packages/com.gamecore.studio.etos/Editor/CompanionStageService.cs` for sample/exported/app-authored
candidates, leaving ledger-produced agent candidates on `StageAsync`. Wire format:

```
POST /api/v1/agents/gamecore-studio/http/v1/stage/app-candidate
Authorization: Bearer <paired app key>           # ordinary node authentication
X-GameCore-Project: <stable project SHA-256>
X-GameCore-Stage-Key: <same paired app key>       # transient, never log/persist
{
  "payloadBase64": "<base64 of exact UTF-8 JSON payload bytes>",
  "signature": "<lowercase hex HMAC-SHA256>"
}
```

Payload:
`{app:"gamecore-unity", request:{changeSetId,projectId,sourceRevision,catalogRevision,steps?},
changeSet:<unchanged candidate>, toolCatalog:<catalog at catalogRevision>,
files:[{bytesBase64:<actual artifact bytes>},...]}`.
Signature input is UTF-8 `gamecore.stage.app-candidate/1\n` followed by the decoded payload bytes;
key is the raw UTF-8 paired app key. No JSON canonicalization is needed in the client: sign exactly
the bytes base64-encoded. No null members. Existing body-size limit applies. Only mechanism.propose
operations are accepted on this path; files are checked against the candidate manifest. Source paths
and verdicts are never accepted. The companion independently asks the trusted node to authenticate
the transient signing key, compares its app with proxy authority, then discards the key. It is not a
new pairing credential and no key file is read by this implementation. Same ID/bytes are retained
idempotently; altered bytes/origin under that ID produce 409. Follow the returned job with existing
status/signed-verdict/verify routes; partial/failing jobs still cannot admit.

**Integrator, D14/D24/D15:** review and run `studio/etos/install.sh --apply-prices` to print diffs and
atomically apply the model, op and companion price tables. This step never restarts services. It
preserves the companion's unrelated keys and stage registrations. To register a paired project alone:
`studio/etos/install.sh --register-project <stable-sha256> <absolute-Unity-project-path>`.
Full install `studio/etos/install.sh --skip-images --project <absolute-Unity-project-path>` computes
the same ID as `EtosProjectContext.LoadProjectId` (uses the non-secret Project.json cache when valid,
otherwise SHA256(lowercase productGUID + newline + full project path)), writes config, builds and copies
a release, atomically switches current and checks node-reported ready state. A changed config triggers
reload even if the binary version is unchanged. The integrator owns actual node/companion reloads.

## Left open

- Echo's reseller pricing could not be verified: searches returned no public tariff and both
  `https://echo-coding.com` and `https://api.echo-coding.com` were inaccessible to web retrieval.
  Model templates use the **model publishers'** standard-context list prices, with a comment that
  Echo markup is unverified. These must be reconciled by the integrator before deploying to Echo.
- GPT Image 2 publishes token pricing and an **output-only estimate** ($0.006 low 1024-square);
  the latter excludes prompt/reference tokens. ETOS's `per_unit` cannot express a true total fixed
  price for all sizes. No invented total price is installed in companion `ops_prices`: capped image
  and describe calls intentionally remain `budget_unpriced` until a verified bounded tariff exists.
  Three-dimensional generation has no configured provider or published tariff in this deployment.
- ETOS's flat model SLA cannot represent publisher long-context/cache-write tiers. Standard rates
  are cited per entry, not claimed as full billing reconciliation. ETOS TTS `check_budget` currently
  counts Unicode scalars, while Alibaba counts Han twice. The companion now applies Alibaba's rule;
  upstream `crates/etops/src/service.rs::LongOp::check_budget` and usage accounting need the same
  provider-aware count for workers calling ETOS directly. That upstream repository is outside this
  packet's exclusive paths; no live worker/node budget was qualified.
- Full installer changes to manifest grants/process authority deliberately refuse instead of silently
  applying a different registered manifest. A legacy real `current` directory also refuses before
  switching (the retained D15 installation is a symlink and is covered). Both need an explicit
  integrator migration; the new helper never deletes installed data to work around either case.
- No live deployment, paid-node verification, Unity Editor run or creator Admit was performed. The
  client/UI implementation of the signed route and version message is assigned to R3-B above.

## Verification

Final commands and counts are recorded below after the last run. Rust edition is now **2021**;
existing let chains were converted without changing guards, and one SQLite iterator lifetime was
made explicit. The vendored upstream SDK keeps its own edition. Sandbox.rs has no edits.

Final verification on myubuntu (2026-10-05), after the Rust 2021 conversion and candidate recovery fix:

- `cd studio/agent && ~/.cargo/bin/cargo fmt --check && ~/.cargo/bin/cargo clippy --all-targets -- -D warnings && ~/.cargo/bin/cargo test`: **120 passed, 0 failed, 7 ignored** (90 unit, 26 fake-companion, 4 stage-lane). Ignored cases require live/Unity/host-specific qualification and were not enabled. `fmt.txt`, `clippy.txt`, `cargo-tests.txt` retain the results.
- `PYTHONDONTWRITEBYTECODE=1 python3 -m unittest discover -s studio/etos/tests -v`: **3 passed**; `install-tests.txt`. `bash -n studio/etos/install.sh`: pass.
- `python3 tools/check_package_metadata.py`: **42 packages, 91 assemblies**, pass; `package-metadata.txt`.
- `python3 tools/check_game_core_csharp.py`: **1,141 files**, pass; `csharp.txt`.
- `git diff --check`: pass. `src/stage/sandbox.rs`: unchanged from the packet base.

Additional upstream request (outside this repository's exclusive paths):
`crates/etops/src/service.rs::LongOp::check_budget(&self, cost: &CostConfig)` needs provider-aware
billable-character quantity (Han = 2 for Alibaba), reused by usage accounting. It must also refuse
missing tariffs rather than defaulting a capped direct-worker op to zero. The companion protects
its own media route now; the templates do not change that upstream implementation.
