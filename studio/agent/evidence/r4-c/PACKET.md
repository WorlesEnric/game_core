# R4-C: companion, tariff templates and indexed target scopes

Branch `codex/r4-c`, base `c79a383`. Linux build host only. Exclusive implementation paths are
`studio/agent`, `studio/etos`, core Model (scope inference only), and Hollowmere `Tests/R4_C`.
No credential files were inspected and no installed service was stopped or restarted.

## R2 fixes / R4 regressions

| Finding | Fix | Regression |
|---|---|---|
| P42-OPS-01 | Fresh free provider status precedes tariff/ceiling checks. Unknown selected providers refuse `not_configured`; only configured providers participate in pricing. | `p42_ops_01_retained_unconfigured_3d_precedes_budget`; `ops-before.log` reproduces the retained 409 `budget_unpriced` failure. |
| Tariff policy | Published URL or operator declaration required; positive price, existing quantity checks, hot tariff reload, explicit hello/error provenance and durable idempotent charge rows. Missing/invalid/removed tariffs fail closed. | `r4_tariff_provenance_missing_placeholder_and_invalid_refuse`, `r4_tariffs_operator_cap_pair_ledger_and_hot_reload`, existing R3-D14 cap/quantity tests. |
| Tariff installation | Derive companion aliases/model/provenance from the same ops provider block. Refuse Echo placeholders before writes. TTS-only apply supported without touching other providers. | `test_r4_placeholder_refuses_before_any_write`, `test_r4_operator_declaration_and_provider_binding`, `test_r3_d14_templates_prices_and_apply_diff`. |
| P42-LIVE-01 | Resolve target type from request-owned index slice using core identity precedence. Intersect tool/type scopes with an indexed concrete scope when present; infer only a singleton, retain `ScopeInferred`, otherwise re-ask once. Core uses the same restriction for generic tools; typed tools retain their declared scope contract. | Rust `p42_live_01_retained_generic_move_infers_indexed_instance`, `p42_live_01_missing_or_ambiguous_index_refuses_and_reasks`, `p42_live_01_published_candidate_uses_request_index_slice`, `p42_live_01_ambiguous_scope_reasks_exactly_once`; Unity `Hollowmere.R4_C.RetainedCandidateTests.P42_LIVE_01_RetainedGenericMoveInfersIndexedInstance`. `scope-before.log` proves the original missing inference. |

## Retained evidence and diagnosis

The unchanged candidate envelope is
`artifacts/studio/verification/W-EDIT-07/live-catalog-candidate-mode-20261005T194931.456257Z/real-candidate.json`:
`cs_01M46RHDNPQ3ZH1V5A0864010R`, task `t37c5b3de59d3b13c7592770e`, generic tool **move** (not a fabricated
`entity.move` replacement). `tests/fixtures/r4_c/context.json` contains that request's original catalog
and context slice recovered read-only from the installed companion ledger. Both Rust and Unity read
these same files. The well resolves to `entity.instance`; that type allows Instance and Prefab, while
the indexed reference identifies this well as Instance. Prior companion code never read the index;
prior core normalization did not narrow the two scopes with the indexed reference. Neither a tool
nor an index scope can widen the permitted intersection. Explicit disallowed scopes still refuse.

The direct retained 3D refusal is W-AI-07's
`installed-3d-refusal-tts-tamper-20261005T192945.989942Z/fixtures/error-generate-3d.json`.
The previous companion always checked its independent `ops_prices` before status. Node prices and
`models.toml` alone cannot satisfy this preflight. Echo was deliberately missing from companion
prices because the old node template's OpenAI output-price estimate was not a verified Echo total.
R4 removes that apparent price, supplies an operator placeholder, and requires an explicit declaration.

TTS uses the correct `bailian-tts` provider alias on disk. The retained W-AI-07 exchange actually
records TTS HTTP 200, so the premise that every TTS call refused is not supported by that receipt.
The live daemon start was 2026-10-05 23:38:43 CST; ops/config mtime was 2026-10-06 02:55:16 CST.
The daemon thus predates the file update, and the pinned service constructs prices at startup.
The old companion similarly loads config at startup. No reload route exists in that installed version.
The source is now explicit and companion tariff reads reload independently of the process.

Pinned etops rejects additional fields in `CostConfig` and `ProviderConfig`. The `# @studio` comment
extension keeps source/note/URL in ops.toml, bound to the containing provider and its exact per-unit
value, and is parsed strictly by the installer. It is not passed as provider request options. No
node parser or validator was weakened. Alibaba's Mainland price was rechecked at
https://www.alibabacloud.com/help/en/model-studio/model-pricing on 2026-10-06; Echo's endpoint did not
supply a retrievable price page, so no Echo published-price assertion is made.

## Verification

Final source validation (`4eba8a13`; documentation/evidence and whitespace-only metadata follow-up):

- Rust: **137 passed, 0 failed, 11 ignored**. `cargo-tests.log`; `fmt-verified.log` and
  `clippy-verified.log` pass. Ignored live/Docker/Unity suites remain ignored, including the new
  guarded real TTS test. The fake-node image cap pair and published TTS ledger test both pass.
- .NET Model: **113 passed, 0 failed, 0 skipped**, parsed from
  `dotnet-final/r4-c-model.trx`. The first run caught over-broad typed-tool inference (112/1);
  it is retained under `dotnet/`. The correction limits indexed placement inference to generic
  tools. The existing typed-tool test was not modified.
- Unity: **84 passed, 0 failed, 0 skipped, 0 inconclusive**, parsed from
  `unity-retry/results.xml`. The required retained fixture
  `Hollowmere.R4_C.RetainedCandidateTests.P42_LIVE_01_RetainedGenericMoveInfersIndexedInstance`
  is present and Passed. The first run exited 1 after Package Manager IPC startup failure and
  produced no XML; its log remains in `unity/`. The fresh invocation took 701 seconds under
  the host-wide lock and completed successfully. No no-result run is counted as a pass.
- Installer: **5 passed**, `installer-final.log`; offline workers **10 passed**,
  `worker-tests-final.log` (host's pinned `~/.cache/gamecore-studio/p42-python` interpreter;
  the initial system Python lacks pytest and is recorded separately).
- Package metadata: **42 packages / 91 package assemblies**; C# policy: **1,199 files**, both pass.
  Shell syntax and `git diff --check` pass. No validator, cap, confinement or test was weakened.
- Live: **0 paid calls**. Both guarded TTS-only template apply and the one-call live probe
  refused because P3.1c remains active. `live-price-apply.log`, `live-tts-guard.log` and
  `live-preflight.json` retain that disposition. Restart/deployment and the real priced call
  remain explicitly open below.

Commands (run from this clone on Linux):

```sh
~/.cargo/bin/cargo fmt --manifest-path studio/agent/Cargo.toml --check
~/.cargo/bin/cargo clippy --manifest-path studio/agent/Cargo.toml --all-targets -- -D warnings
~/.cargo/bin/cargo test --manifest-path studio/agent/Cargo.toml
~/.dotnet/dotnet test dotnet/tests/GameCore.Studio.Model.Tests/GameCore.Studio.Model.Tests.csproj --no-restore --logger 'trx;LogFileName=r4-c-model.trx' --results-directory studio/agent/evidence/r4-c/dotnet-final
python3 -m unittest discover -s studio/etos/tests -v
~/.cache/gamecore-studio/p42-python/bin/python -m pytest -q studio/etos/workers/tests
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/studio/agent/evidence/r4-c/unity-retry" --label r4-c-retry --results "$PWD/studio/agent/evidence/r4-c/unity-retry/results.xml" -- -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R4_C.*|GameCore\.Studio\.Core.*'
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
bash -n studio/etos/install.sh studio/etos/verify-r4-tts.sh
```

The wrapper supplies `-testResults` from `--results`; passing it a second time is rejected before
an Editor starts. `counts.json` and `SHA256SUMS` provide the machine-readable receipt. The final credential-shape
scan redacted test-log matches; `redaction.json` retains counts and before/after hashes without values.
Display logs/results have trailing whitespace normalized; exact sanitized bytes remain in `.raw.gz`
companions, with hashes in `log-format.json`. Test outcomes and result attributes are unchanged.

## Requests to other packets

- Unity ETOS/UI owner: in `Packages/com.gamecore.studio.etos/Client/CompanionDtos.cs`, add
  `HelloInfo.Tariffs: IReadOnlyList<OpTariffInfo>` and `GenerateResult.Charge: MediaChargeInfo?`;
  `OpTariffInfo` carries `string Op` and `TariffInfo Tariff`; `TariffInfo.Kind` is the exact
  `published`/`operator` wire value and includes provider/model/unit/perUnitUsd/url/note;
  `MediaChargeInfo` carries `TariffInfo Tariff`, `double Quantity`, `double CostUsd`. Extend
  the corresponding `CompanionClient.cs` parsers. Display operator values as estimates, and
  read `EtosError.Data["tariff"]["kind"]` for over-budget diagnostics. Do not relabel estimates
  as provider invoices. These files are outside R4-C; the wire schema is documented in
  the integration document's “Tariffs and budgets” subsection.
- Node integrator: in pinned `crates/etops/src/config.rs`, add optional `CostConfig.source:
  Option<TariffSource>` (`Published | Operator`, lowercase serde values), `url: Option<String>`
  and `note: Option<String>`; validate the URL/declaration with the same fail-closed policy.
  In `crates/etops/src/service.rs::OpsService::dispatch("status")`, preserve `providers` and add
  `tariffs: [{op, provider, model, tariff: {kind, unit, perUnitUsd, url, note}}]` plus a loaded
  config revision, so callers can distinguish loaded state from disk state. Until that pin,
  preserve `# @studio` annotations and companion `media_charges`; do not interpret the node's
  token/output estimates as the companion's binding total tariff.
- Integrator: activate the reviewed R4 binary with the immutable release helper when the live-run
  guard is clear; the installed old process cannot expose the new fields without activation.
  Exact companion restart command (after the new release is selected):
  `ETOS_ROOT="$HOME/.local/share/etos-studio" "$HOME/.local/opt/etos/bin/etos" agent restart gamecore-studio`.
  If node numeric prices changed, the separate scheduled node command is
  `systemctl --user restart etosd.service`. Neither command was run by R4-C.

## Left open

- The specifically alleged TTS `budget_unpriced` refusal was not reproduced: retained W-AI-07
  contains TTS HTTP 200 and the live guard prevents a current probe. No unsupported TTS root-cause
  claim is made; file timestamps only prove that etosd predates the price-file update.

- Live apply and ONE real priced TTS proof: the existing live-run guard detected the active
  `gc-studio/p3.1c` run. No configuration change or paid call was made under that guard. Even after
  it clears, the installed pre-R4 companion lacks tariff provenance/reload and `media_charges`;
  restarting/replacing that installed service is expressly forbidden in this packet. The prepared
  `verify-r4-tts.sh` verifies the R4 hello before making its single <= $0.001 “Hi.” call and compares
  the returned charge with the installed ledger. This remains blocked until integrator activation.
- Echo operator value: no operator-declared number was supplied. The placeholder cannot be applied
  honestly; full-template apply refuses. `--apply-prices --only tts` is the supported partial apply.
