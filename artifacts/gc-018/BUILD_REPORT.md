# GC-018 build report — checkpoint capture, restore and directed schema migration

**Result: GREEN on the Linux build host.** Every executable check this change set adds was compiled, imported and
executed here. .NET solution 899/899; Unity EditMode 784/784, GC-018-filtered 3/3, PlayMode 6/6; all eleven probe
scripts × five runs in the High-stripping IL2CPP player, 55/55 clean. The GC-018 player probe reports **66/66**
observations, byte-identical across all five runs, in both families over both catalogs, with Conservative-mode
imports and exclusions covered.

This file replaces the authoring-host placeholder that said `NotRun (pending orchestrator build host)`.

## 1. Host and toolchain

| Item | Value |
| --- | --- |
| Host | Linux `worlesenric` 7.0.0-31-generic (Ubuntu 24.04), x86_64, 12th Gen Intel Core i7-12700KF, 32 GB |
| .NET SDK | `8.0.425` (`$HOME/.dotnet`, C# 9 / `netstandard2.1` libraries, `net8.0` tests) |
| Unity | `6000.0.75f1` (26349cd2a5c8), Linux Editor, StandaloneLinux64 IL2CPP |
| Managed stripping | `High` (set and re-read by `BuildProbe`; reported in the probe JSON) |
| Burst | enabled |
| Native toolchain | gcc 13.3.0, clang 18.1.3, GNU ld 2.42 |
| Worktree | `~/wkspace/gc-wt/gc-018`, branch `gc-018` |

Environment snapshot: `artifacts/gc-018/toolchain/environment.txt`.

## 2. Revision under test

Source revision when the runs were executed: `6658026` (`gc-018`), whose `Packages/`, `dotnet/`, `unity/` and
`tests/` trees are identical to the WIP commit `a87e22f`. The IL2CPP build was reproduced from a clean rebuild in
this session and produced a byte-identical player (`player_sha256 =
aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`) and catalog
(`catalog_sha256 = 5298bbc853b35424c7b7e8300b7c39afe6e27ec493027a22a47336a5cb567625`), confirming the prior
probe evidence was built from this source.

Verification commits appended after the WIP, none of which changed source:

`243c651` (dotnet) · `d3573f3` (Unity) · `1bb1296` (player probes) · `952c49d` (inventory promotion).

## 3. Exact commands run and results

### 3.1 .NET solution

```sh
export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --no-build --logger trx --results-directory artifacts/gc-018/trx
```

Build: **0 warnings, 0 errors**. Test: **899 executed / 899 passed / 0 failed / 0 skipped**, 11 projects.

| Project | Passed | Total |
| --- | ---: | ---: |
| GameCore.Contracts.Tests | 148 | 148 |
| GameCore.Execution.Tests | 99 | 99 |
| GameCore.Content.Compiler.Tests | 49 | 49 |
| GameCore.Composition.Tests | 144 | 144 |
| GameCore.Planning.Tests | 160 | 160 |
| GameCore.Derivation.Tests | 114 | 114 |
| GameCore.Rules.Cards.Tests | 26 | 26 |
| GameCore.Rules.Narrative.Tests | 118 | 118 |
| GameCore.ProtocolFixtures.Tests | 10 | 10 |
| GameCore.ProtocolFixtures.Production.Tests | 10 | 10 |
| GameCore.ReferenceSeams.Tests | 21 | 21 |
| **Total** | **899** | **899** |

Logs: `artifacts/gc-018/dotnet-build.log`, `artifacts/gc-018/dotnet-test.log`; raw TRX in `artifacts/gc-018/trx/`
(11 files; the earlier interrupted-run TRX remain in `trx-fresh/` and `trx-final/`).

The GC-018 pure suites all run here: `CheckpointDocumentTests`, `CheckpointIdentityTests`,
`CheckpointMigrationTests`, `CheckpointRecordTests`, `CheckpointVersionedFixtureTests` (Contracts),
`CheckpointCatalogTests` (Content.Compiler), `RngStreamTests`, `CheckpointCaptureTests`,
`CheckpointRestorePlanTests` (Execution).

### 3.2 Static and documentation checks

```sh
python3 tools/check_game_core_csharp.py
python3 tools/check_contract_surface_parity.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`checked 389 C# file(s)` → ok. Contract-surface parity: 221 frozen types; 5 additions beyond the frozen snapshot,
all previously documented GC-012/GC-015 enum values, no removals. Documentation validator: self-test 9/9 fixtures,
full run passes (14 documents). Logs: `artifacts/gc-018/static-checks.log`, `artifacts/gc-018/validator.log`.

### 3.3 Unity Editor

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-018/unity/resolve.log
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog     -logFile artifacts/gc-018/unity/catalog-codegen.log
"$UNITY" ... -executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog -logFile .../checkpoint-codegen.log
"$UNITY" ... -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog      -logFile .../cards-codegen.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults "$PWD/artifacts/gc-018/unity/editmode.xml" -logFile "$PWD/artifacts/gc-018/unity/editmode.log"
"$UNITY" ... -testPlatform EditMode -testFilter GameCore.Gc018.Tests -testResults .../gc018-editmode.xml ...
"$UNITY" ... -testPlatform PlayMode -testResults .../playmode.xml ...
```

| Check | Result |
| --- | --- |
| Import / resolve | exit 0; **no** `packages-lock.json`, `.meta` or generated-file drift |
| ProbeCatalog regeneration | byte-identical (`git diff` empty) |
| CardCatalog regeneration | byte-identical |
| CheckpointCatalog regeneration | byte-identical |
| Full EditMode | **784 passed / 0 failed / 0 skipped** (includes the GC-018 3) |
| GC-018 EditMode (`-testFilter GameCore.Gc018.Tests`) | **3 passed / 0 failed** |
| PlayMode | **6 passed / 0 failed** |

The three GC-018 EditMode tests are `GameCore.Gc018.Tests.Gc018IntegrationTests`:
`TheCheckpointObservationTableIsExactlyThePublishedSequence`, `TheNarrativeFamilyPassesEveryObservationOverBothCatalogs`,
`TheCardFamilyPassesEveryObservationOverBothCatalogs`. The lock file and all `.meta` files were already committed by
the change set; this run produced no change to them, which is itself the required evidence.

### 3.4 IL2CPP player and probes

```sh
UNITY="$UNITY" ARTIFACTS=artifacts/gc-018/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=<per-probe dir> tools/unity/run_<name>_probe.sh      # eleven scripts
```

Player: StandaloneLinux64 IL2CPP, `scriptingBackend=IL2CPP`, `isIl2Cpp=true`, `managedStrippingLevel=High`,
128-bit process. Built and then run headless (`-batchmode -nographics`, exit code encodes the result), each run
under the `timeout` watchdog in `probe_runs.sh`/`build_probe.sh`.

| Probe script | Mode | Steps/run | Runs clean |
| --- | --- | ---: | ---: |
| `run_probe.sh` (GC-001, positive + expected-negative) | `-probe` | 8 | 5 + 5 |
| `run_world_probe.sh` (GC-005) | `-probeWorldDispatch` | 7 | 5 |
| `run_w1_gate_probe.sh` (W1-GATE) | `-probeW1Gate` | 23 | 5 |
| `run_w2_gate_probe.sh` (W2-GATE) | `-probeW2Gate` | 24 | 5 |
| `run_narrative_probe.sh` (GC-010) | `-probeNarrative` | 24 | 5 |
| `run_cards_probe.sh` (GC-011) | `-probeCards` | 28 | 5 |
| `run_w3_gate_probe.sh` (W3-GATE) | `-probeW3Gate` | 11 | 5 |
| `run_w4_profile_probe.sh` (GC-012) | `-probeW4Profile` | 7 | 5 |
| `run_gc013_probe.sh` (GC-013) | `-probeGc013` | 62 | 5 |
| `run_w4_gate_probe.sh` (W4-GATE) | `-probeW4Gate` | 70 | 5 |
| `run_gc018_probe.sh` (GC-018) | `-probeGc018` | 66 | 5 |
| **Total** | | | **55/55 clean** |

Every run exited 0 with `"result": "Pass"` and zero failing steps. GC-018 result JSONs (`probe-gc018.json` and
`.run2..run5`) are **byte-identical** (SHA-256 `b3bd5f73…`). The same holds for every other probe except
`world/probe-world-dispatch.json`, whose only per-run difference is the freshly minted fixture world session id
inside `applicationWorld='GameCoreFixtureWorld:<id>'` — a unique world incarnation (P-004), not a defect; all five
runs still Pass. Raw evidence: `artifacts/gc-018/toolchain/` (`build.log`, `codegen.log`, `environment.txt`,
`probe-*.json[.runN]`, `player-*.log[.runN]`, `*.driver.log`).

**High-stripping survival.** The generated checkpoint serializers are bound as direct delegate references in
`CheckpointCodecAdapter`, so the GC-018 probe exercises all twelve generated value structs inside the stripped
IL2CPP player: capture (encode) and restore (decode) both run under `High` stripping, in both families and over
both the committed generated catalog and the hand-written fixture catalog.

## 4. Acceptance criteria

The brief's four clauses plus the four named suites, against the 16-observation GC-018 sequence (each observation
runs twice per family — generated catalog and `fixture:` catalog — for narrative and cards):

| Acceptance clause | Observation(s) | Observed evidence (narrative; cards identical in shape) |
| --- | --- | --- |
| Save → recreate → restore in both families with **different native entity indices** preserves canonical state | `gc018-restore-recreates-state-at-different-native-indices` | `differ=7`, `identitySetEqual=True`, `indexBlocksDisjoint=True`; source target indices `24,22,19,23,21,18,20` → restored `31..37`; slot handles `slot6/1→slot0/1` … |
| … including **dormant slots** | `gc018-restore-preserves-dormant-slots` | `slots=2->2; dormant=1->1; active=1->1; rowsEqual=True; liveStorage=True`; `active=False` preserved |
| … and **mode/import/exclusion** data | `gc018-restore-preserves-mode-imports-and-exclusions` | `mode=Conservative->Conservative`; `treeEqual=True`, `rowsEqual=True`, `imports=1->1`, `exclusions=1->1`, `replayedImports=1`, `replayedModes=1` |
| Unknown required **schema/content** rejects without a partial world | `gc018-unknown-required-schema-rejects-restore`, `gc018-corrupt-and-truncated-documents-reject` | planner `MigrationRejected/MigrationRequired`; `restored=False; builderBuilds=0; registry=2->2`; tampered/truncated → `UnsupportedVersion`, padded → `ResourceUnavailable`, `registry=2->2` |
| **Ambiguous migration** rejects without a partial world | `gc018-ambiguous-migration-rejects-restore` | `directPlan=Ambiguous(...,paths=2)`; `plannerRefusal=OwnershipConflict`; `builderBuilds=0; registry=2->2` |
| **Corrupt references** reject without a partial world | `gc018-corrupt-reference-rejects-restore` | `plannerRefusal=CorruptReference/MissingDependency`; diagnostic names the undeclared target; `builderBuilds=0; registry=2->2` |
| **Old callbacks cannot target the new session** | `gc018-old-callbacks-cannot-target-the-new-session`, `gc018-restore-happens-into-a-new-unexposed-world` | `oldHandleRefused=True (StaleHandle)`, `oldRequestKnownInRestored=False`, `resubmitAdmitted=False`; restore stages while `registryAfterStaging=2` and only exposes to `3`; `restoredSession≠sourceSession` |
| **Queued-command include/reject cutoff** | `gc018-queued-commands-are-dispositioned-not-omitted` | `rejectQueued(offered=1,included=0,rejected=1) accounted=True headerRejected=1`; `includeQueued(offered=1,included=1,rejected=0) headerCommands=1`; `cutoff=1` |

Suite mapping: **TEST-017** ← all 16 observations; **TEST-002** ← different-native-indices + old-callbacks +
unexposed-new-world; **TEST-010** ← dormant slots + mode/imports/exclusions + missing-migration rejection +
clocks/RNG/cursors; **TEST-022** ← byte-identical five-run determinism + different session ids + stale-handle
rejection. Both digests match the literals the scenario publishes:
narrative `f881469b2a2ba43e4c1bf7972913dd2c93f945a623fef770e00319409ece7ac0`,
cards `fe1aaae38982be120fe3c668742990504f984b54b053de3b2d7886ff899b5256`.

## 5. Fixes

**This worker made no source fix.** The tree compiled, imported and passed every suite and probe unchanged; there
was no compile error, test failure or genuine defect to fix. The only change this worker made beyond evidence is
the inventory promotion in §6.

The WIP commit `a87e22f` (the interrupted tester's uncommitted work, committed unverified) was reviewed in full and
**retained**, and is now covered by the passing runs above. Its source fixes, by area:

1. `CheckpointDocument` — sort each record kind's bucket by encoded bytes before framing, so canonical document
   order is independent of discovery order (P-008, TEST-022); relax the field-id check from strictly-ascending to
   non-decreasing, because one kind legitimately contributes many records under one field id; include the reader's
   error in the checksum-mismatch detail; `Header` setter narrowed to `private set`.
2. `CheckpointIdentityTable` — the selection's provider install is looked up as a `PluginInstanceId` wrapping
   `selection.Provider.Value` (the two id types are distinct; the old call compared a `PluginInstanceId` against a
   `PluginId`).
3. `CheckpointRecords` / `CheckpointMigrationPlan` — `HeaderRecordValue.ToString()` dropped invalid `(uint)` casts
   over `int` counts; `MigrationPlan.Plan`'s parameter/doc corrected to `SchemaId`.
4. `CatalogEmitter` — a `Bytes` wire type now reports its byte length in diagnostics instead of treating `byte[]`
   as a nullable reference and printing a string form.
5. `CheckpointCodecAdapter` — introduced `ICheckpointRecordSerializer<TValue>` and the two-type bridge
   `CheckpointRecordSerializer<TValue,TGenerated>` with two conversion delegates, because the generated serializer
   takes the catalog's own nested value struct while the codec seam carries the `GameCore.Contracts` record value.
   Serialize/deserialize still call the actual generated methods by direct reference (IL2CPP-safe).
6. `CheckpointRestoreExecutor` — reservation handling follows the returned `RestoreReservationKind`
   (Fresh / Retransmission / SessionInUse); added `TryProvePlannedState`, a two-direction census of the staged
   world's targets and state rows against the plan (no invented or dropped rows, dormant stays dormant, and for a
   direct plan the version/value match verbatim) before exposure.
7. `RestoreReservationLedger` — a duplicate request returns the recorded answer while pending and replays a
   **Published** session after publication, but a settled Rejected/Cancelled/Faulted attempt is never replayed and
   its session id is never handed out again; terminal outcomes are never rewritten (P-030, P-050, O-21).
8. `CaptureContext` — `TargetRegistry` type reference and the optional `PluginClockRegistry` needed for wake rows.
9. `Gc018Scenario`/`Gc018Family`/hosts and the generated checkpoint catalog/emitter/`migration-paths.json` —
   Conservative-mode import/exclusion coverage and consistency with the regenerated catalog (regeneration is
   byte-identical).

No test was weakened, skipped, deleted or `[Ignore]`d, and no expected value was changed.

## 6. Inventory promotion (build-host action delegated by the HANDOFF)

`artifacts/gates/w4-generic-profile/inventory.{json,md}`: the four rows GC-018's HANDOFF proposed were promoted to
`Implemented+Evidenced` now that the runs above pass — `P-053`, `P-054`, `O-20`, `O-21`. `O-22`, `P-004`, `P-005`,
`P-032` and `P-055` are unchanged, exactly as proposed. Counts move from 37/45/4 to 41/44/1; every stale
`Not yet`/`proposal only` statement in `inventory.md` was updated and its evidence mirrors `inventory.json`.

## 7. Still failing / not runnable (unchanged, by design)

- **`O-22 RecoverWorld` is not implemented.** Its procedure ("reuse Restore/Create") is now reachable, but the
  faulted-world → new-session composition belongs to GC-027 (with GC-017's fault latch). `O-22` stays `Not yet`.
- **GC-016 observation lease not consumed.** `ICommittedBoundaryReader` is the frozen seam and does not depend on
  it; when the bounded lease lands, `UnityCommittedBoundaryReader` takes it for the read duration.
- **`ContentRevisionCount` is recorded, not resolved.** Definition-revision resolution is GC-025's content-loading
  surface.
- **Included queued commands are not re-admitted into the restored plane.** The recorded set, cutoff and restored
  high-water are proven; re-admission is command ingress (GC-019).
- **TEST-002's small-width counter-exhaustion fixture is still absent**, so `P-004`/`P-005` stay `Partial`.
- **Physics bit-identical continuation and cross-engine asset portability are non-goals** (P-054, 06 §7).
- **Documentation validator does not check remote URLs** — stated by the tool itself.

Not a failure: Unity prints `[Licensing::Module] Error: Access token is unavailable; failed to update` on each
batch run; licensing is already activated locally and every run completed with exit 0.

## 8. Evidence index

| Path | Contents |
| --- | --- |
| `artifacts/gc-018/dotnet-build.log`, `dotnet-test.log`, `trx/` | .NET build and 899/899 test run |
| `artifacts/gc-018/static-checks.log`, `validator.log` | C# check, contract parity, docs validation |
| `artifacts/gc-018/unity/{resolve,catalog-codegen,checkpoint-codegen,cards-codegen}.log` | import and catalog regeneration |
| `artifacts/gc-018/unity/{editmode,gc018-editmode,playmode}.{xml,log}` | Unity test runs |
| `artifacts/gc-018/toolchain/{codegen,build}.log`, `environment.txt` | IL2CPP build + toolchain snapshot |
| `artifacts/gc-018/toolchain/probe-gc018.json[.run2..5]`, `player-gc018.log[.runN]` | the GC-018 probe (66/66 ×5) |
| `artifacts/gc-018/toolchain/**/probe-*.json[.runN]` | the other ten probe scripts ×5 |
| `artifacts/gc-018/toolchain/*.driver.log` | per-script probe driver output |
| `artifacts/gates/w4-generic-profile/inventory.{json,md}` | promoted required-surface rows |
