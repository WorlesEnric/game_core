# R3-E host tooling and worker output

Branch `codex/r3-e`, base `ed760892`. Only R3-E paths are changed. No live node,
paid model call, installed companion change, or secret file access is needed.

## R3 fixes

| Finding | Fix | Regression |
|---|---|---|
| D25 | `run-redacted.py` forwards the documented non-secret `GAMECORE_*` namespace. XML skip reasons yield `RESULT ...: SKIPPED (env-gated): <names>`, exit 1; original XML stays intact and no passing verdict is synthesized. Failed tests/compiler errors retain FAIL precedence. | `test_D25_environment_prefix_excludes_credentials`, `test_D25_retained_skips_name_gate`, `test_D25_failures_take_precedence_over_skips` |
| D17 | Bash-3-safe optional array expansion in both SSH and host dispatch. Current-invocation Bee JSON/JSONL and Editor CS diagnostics are redacted and printed beside the result; stale Bee logs are ignored. | `test_D17_empty_arrays_are_bash3_safe`, `test_D17_ssh_forwarding_preserves_optional_arrays`, `test_D17_bee_compile_errors_are_redacted_and_visible` |
| D18 | Only `Can't find file /tmp/ilpp.sock-<id>` retries, at most once, with the first attempt log retained and named. Persistent symptoms fail even with exit 0; `--attempts 1` disables retry. Generic timeouts and other failures never retry. | `test_D18_retained_ilpp_retries_once`, `test_D18_persistent_ilpp_never_passes`, `test_D18_other_failures_never_retry`, `test_D18_retry_can_be_disabled` |
| W-AI-03 mechanic output (brief identifier) | Exact request intent, local full JSON Schema self-check and narrowly scoped intent post-processing, packaged into an offline worker image layer. | `studio/etos/workers/tests/test_mechanic.py` (see worker packet) |

### Environment contract

The operator runner retains its explicit UI/process variables and additionally accepts
names matching `GAMECORE_[A-Z0-9_]+`. It rejects names containing, case-insensitively,
`KEY`, `TOKEN`, `SECRET`, `PASSWORD`, `PASSWD`, `CREDENTIAL`, `AUTH`, `PROXY`, or `COOKIE`.
Examples: `GAMECORE_ETOS_LIVE=1`, `GAMECORE_ETOS_AUTOSTART=0` and non-secret project/stage
identifiers survive; `GAMECORE_ETOS_KEY_FILE`, all provider credential variables,
HTTP(S)/ALL/NO proxy variables, startup hooks and arbitrary Unity variables do not.
Never put credentials in this application-config namespace. No environment values
are included in the runner verdict. Authentication remains the existing settings/
companion route, not inherited provider secrets. Service execution still requires
the companion's env-cleared sandbox; this operator wrapper is not that sandbox.

### Retained startup evidence and policy

- `artifacts/studio/workflows/P3.2/runs/persist-20261005T080347Z`: an interactive
  startup produced no log for 900 seconds and was killed. Root cause is unknown.
  Watchdogs remain; this symptom alone does **not** authorize an automatic retry.
- `runs/mech-b-20261005T120918Z/editor-excerpt.log`: the exact IL post-processor
  `Can't find file /tmp/ilpp.sock-...` exception preceded a missing ETOS gateway.
  This is the sole automatic retry signature for `unity-batch.sh` in R3.
- This packet's policy supersedes the generic timeout retry advice in the older
  `docs/operator/editor-hang.md` for this runner only. It does not claim to fix
  Unity's native startup fault or authorize extra runs of interactive workflows.

## Verification

The initial host regression replay on `ed76089` failed 8/10 cases. The initial
worker harness failed 8/10 (missing self-check and prompt contract); the retained
intent defect independently produces exactly three JSON Schema errors. Final
counts are recorded below after validation. Fake-Editor policy tests isolate the
allocator; `unity-batch-lock.sh` remains the production allocator regression.

## Requests to other packets

- Stage test owner: `studio/stage/tests/test_host.py`,
  `HostTests.test_R2_18_EnvironmentIsEnumerated`: replace the obsolete assertion
  that `GAMECORE_TEST` is absent with `GAMECORE_TEST == "sentinel"`; continue
  asserting `DOTNET_STARTUP_HOOKS` and `UNITY_ATTACK` are absent, and add credential/
  proxy exclusions. The current test explicitly contradicts D25's new contract.
  This file is outside R3-E's exclusive set, so it was not edited.
- R3-C: `studio/etos/agent/workers/gc-mechanic.md` must consume the complete prompt
  at `studio/etos/workers/gc-mechanic.md` (copy it into the existing manifest path,
  or change `agent.toml` instructions to a supported packaged equivalent). This
  is the active prompt location on main; the brief's `studio/etos/workers/` did
  not exist. Run `check-output.py --inputs /inputs --outputs /outputs --repair-intent`
  before the worker answers, with nonzero exit blocking success. The allowed
  host image build now installs that checker/schema under `/opt/gamecore-worker`.
- Documentation owner: reconcile `docs/operator/editor-hang.md` §4 with the above
  ILPP-only retry rule for the Studio runner; other launchers are unchanged.

## Left open

- Deployment of the active worker prompt depends on R3-C's out-of-scope manifest/
  prompt update. Installed services and production image tags are not modified by
  verification. This packet supplies the reviewable prompt, checker and build seam.
- The legacy environment unit test fails its obsolete blanket-deny expectation;
  its exact required update is above. Other existing stage tests stay green.
- Retained mech-a has request, diagnostics and task summaries, but no rejected
  candidate/package bytes; mech-b has panel/verdict JSON, not worker outcome JSON.
  The offline harness reconstructs the proven bad intent envelope and does not
  claim model generation, package 12/12 reproduction, stage, admit or Play proof.
