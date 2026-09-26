# Clean-checkout build and run

This page is the reproduction procedure: what a fresh clone needs, and what `tools/reproduce.sh` does with it.

## 1. Prerequisites

The **only** qualified toolchain is the one in [profile.md](profile.md). In particular:

| Requirement | Why |
| --- | --- |
| Unity **6000.0.75f1** Editor, installed on **Linux x86_64**, with the Linux IL2CPP build-support module | The one qualified Editor and player target. `tools/reproduce.sh` refuses any other Editor path. |
| .NET **8** SDK | Builds and tests the Unity-free assemblies (`netstandard2.1`, LangVersion 9, nullable, warnings-as-errors) and the NUnit test projects. |
| `python3` | The host-side checkers, the failure-code generator, the docs validator and the probe-result validation. |
| GNU `timeout` (coreutils) | Every step is bounded by a watchdog, and the Unity-hang policy depends on it. |
| `git` | The revision is recorded and the catalog byte-identity check is a `git diff`. |

Note the language-level constraint: **C# 9 / .NET Standard 2.1**. Unity's profile has no `IsExternalInit`, so
`record`, `init` accessors, file-scoped namespaces, global usings and `record struct` are unavailable. Nothing
in this repository uses them.

## 2. The one command

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
  tools/reproduce.sh
```

`UNITY` is required and must name the pinned Editor. Everything else has a default.

### Environment variables

| Variable | Default | Meaning |
| --- | --- | --- |
| `UNITY` | *(required)* | Absolute path to the pinned Editor executable. Rejected unless the path contains `6000.0.75f1`. |
| `DOTNET` | `dotnet` on `PATH` | The .NET 8 SDK executable. |
| `PYTHON` | `python3` on `PATH` | The python3 executable. |
| `UNITY_PROJECT` | `<repo>/unity/GameCore.Validation` | The qualification project. |
| `ARTIFACTS` | `<repo>/artifacts/reproducibility` | Transcript and evidence directory. |
| `PROBE_RUNS` | `2` | Repetitions of each family probe. **The script rejects anything but 1 or 2**, because two is the project owner's cap on repeated runs. |
| `UNITY_TIMEOUT` | `1800` | Seconds one Editor invocation may take, before the single retry. |
| `STEP_TIMEOUT` | `1800` | Seconds one host-side step may take. |
| `PLAYER_TIMEOUT` | `600` | Seconds one family-probe sequence may take. |
| `RELEASE` | `1` | Build and probe the marker-free release player. `0` skips it **and says so** — it will not imply a marker-free claim it did not test. |
| `RELEASE_PROJECT` | `<repo>/unity/GameCore.ReleaseCheck` | The disposable clone path. |
| `GAMECORE_OFFLINE` | `0` | **Opt-out only, never a default.** `1` adds `-p:NuGetAudit=false` to *this script's* `dotnet restore/build/test` — and to nothing else — after you have established the NuGet advisory feed is unreachable on the host (§2.1). **The audit is on by default.** Anything other than `0`/`1` is refused. |

### 2.1 `GAMECORE_OFFLINE=1`: the one documented way to run without the NuGet audit

`dotnet restore` runs NuGet's vulnerability audit by default, and this repository builds with
`TreatWarningsAsErrors`, so an audit that cannot reach the advisory feed fails the restore with `NU1900`
("audit did not complete") rather than degrading silently. **That is the designed failure.** The audit is on
by default and stays on for every run that did not explicitly opt out.

Before using the flag, **establish that the feed really is unreachable** — a slow proxy, a blocked
`api.nuget.org`/`api.vulnerabilitydata.microsoft.com`, an air-gapped host — and not, for example, a stale
package cache or a proxy that only needs credentials. Once you have:

```sh
GAMECORE_OFFLINE=1 UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
  tools/reproduce.sh
```

What `1` does, precisely:

- adds `-p:NuGetAudit=false` to this script's `dotnet restore`, `dotnet build` and `dotnet test` command
  lines — the property appears verbatim in the transcript's `-- command:` lines and in `steps.tsv`;
- writes `nuget_audit=off-for-this-run (GAMECORE_OFFLINE=1; …)` into `environment.txt` and prints it in the
  run banner and the closing summary;
- **nothing else.** It is not exported to child processes, it is not written into any `.csproj`,
  `Directory.Build.props`, `nuget.config` or `NuGet.config` in the repository, and it does not touch
  compiler warnings-as-errors or any test.

What it does **not** entitle you to claim: a run made with `GAMECORE_OFFLINE=1` has **no NuGet
vulnerability-audit result at all** — the audit status for that run is `NotRun`, exactly like any other
skipped step, and must be reported that way. The `check_release_*` host-side checkers and the Unity Editor
builds are unaffected by the flag and keep their own behaviour.

Re-running one of the three commands by hand (§6) with the same disposition means passing the property
yourself, e.g. `$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release -p:NuGetAudit=false`. Do not
`export NuGetAudit=false` into the shell instead: a process-local property on the command line is visible in
the command record, an exported variable is not.

## 3. What it does, in order

The transcript numbers every step sequentially, including one entry per individual host-side check, so the
numbers below name the **groups** rather than promising an exact index. `steps.tsv` is authoritative for a
given run.

| Group | Action | Fails when |
| --- | --- | --- |
| toolchain | Records the revision (+ dirty count), Editor path, `dotnet --version`, `python3 --version`, host, `nproc`, `probe_runs`, `nuget_audit` → `environment.txt` | Never (it is a record); the prerequisites in §1 are checked before it |
| package metadata | `check_package_metadata.py --self-test`, then the audit with `--json` | A rule of the auditor no longer holds, or a package version, name, description, `unity` field, or asmdef-derived dependency set disagrees |
| generated documents | `emit_failure_codes.py --check` | The committed failure-code table is stale relative to the sources |
| restore | `dotnet restore dotnet/GameCore.sln` — **with the NuGet audit on, unless `GAMECORE_OFFLINE=1`** (§2.1), in which case the command line carries `-p:NuGetAudit=false` and `environment.txt` says so | The NuGet graph cannot be restored — **including** `NU1900` when the audit cannot complete and the run did not opt out |
| build | `dotnet build … -c Release --no-restore` (same audit disposition as the restore) | A Unity-free assembly does not compile |
| test | `dotnet test … -c Release --no-build` (trx into `host/trx`; same audit disposition) | A test fails |
| host checks | C# shape, contract-surface parity, the four committed catalogs, the catalog-emitter mirror, the reachability manifest, the baked coverage artifact, the fingerprint self-test, the budget-decision record, the release-clone checker's self-test, `link.xml`, and `bash -n` over this script and the harnesses it calls | Any of them disagrees |
| unity-resolve | Resolves the qualification project's package graph | It does not resolve |
| package-lock audit | Re-runs the metadata audit against the regenerated lock | Resolution produced a lock that disagrees with the manifests |
| EditMode | Unity **EditMode**, every testable package, unfiltered | A test fails, or the Editor exits non-zero |
| PlayMode | Unity **PlayMode**, same set | same |
| test-document check | Asserts both result XML documents exist and are non-empty | The Editor run produced no verdict — that is `NotRun`, never Pass |
| codegen | The five codegen/bake Editor invocations | Generation fails |
| byte identity | `git diff --exit-code` over the four generated trees | The committed catalogs are **stale** or generation is **non-deterministic** |
| qualification player | Builds the IL2CPP player (`tools/unity/build_probe.sh`) | The build fails, or no executable appears |
| qualification probes | The three family probes ×`PROBE_RUNS` | Any probe run is dirty or crashes |
| release player | Prepares, checks and builds the marker-free clone, and asserts it really is latch-free and telemetry-free; skipped with a notice when `RELEASE=0` | The clone, its surface, or the build fails |
| release probes | The same three family probes ×`PROBE_RUNS` in the release player | Any probe run is dirty or crashes |
| docs | `validate_game_core_docs.py --self-test` then the full validator | A link, anchor, ID, traceability entry, DAG edge or wave ordering is wrong |

Every step is wrapped in `timeout` with a clear named failure. Editor invocations additionally get **one**
retry on a timeout, and a real compile/test error is never retried (see [editor-hang.md](editor-hang.md)).

## 4. Evidence it leaves behind

```
artifacts/reproducibility/
  transcript.log            the whole run, verbatim (stdout+stderr)
  steps.tsv                 step  name  rc  seconds  command
  environment.txt           revision, toolchain versions, host
  host/
    package-metadata.json   the package/asmdef audit report
    package-metadata-after-resolve.json
    failure-codes.json      the parsed code sets
    reproduce-budget-record.json  the ten budget rows and the owner deferral
    link-xml-qualification.json
    trx/                    the .NET test results
  unity/
    resolve.log  editmode.log  playmode.log
    editmode-results.xml  playmode-results.xml
    codegen-*.log
  toolchain/                the qualification player's build log and environment
  release/                  the clone surface, player surface, build log, release probes
  probe/                    the qualification family probe results and player logs
  validator-self-test.log  validator.log
```

`steps.tsv` is the machine-readable ledger: a run that fails still shows exactly which step failed, with what
exit code, after how long.

## 5. What this script deliberately does not do

- **It does not run the exhaustive probe matrix.** The 25 probe modes, the release-surface inspections and the
  conformance tables belong to the wave gates and to GC-028 (`tools/run_w7_gate.sh`,
  `tools/run_conformance.sh`). This script is the clean-checkout entry point: the shortest path from a fresh
  clone to a reproduced profile. The direct commands for the matrix, the evidence index and the compatibility
  report are in §8.
- **It does not run the full-duration TEST-023 timing catalogue.** That qualification is **Deferred by
  project-owner decision**; see [deferred-scope.md](deferred-scope.md). This script makes no timing claim.
- **It does not qualify any platform other than the declared profile.**

## 6. Re-running pieces

If one step fails, the failed command is in the transcript's `-- command:` line. Re-running a piece directly:

```sh
# the pure half (append -p:NuGetAudit=false only under the §2.1 conditions)
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test  dotnet/GameCore.sln -c Release --no-build

# the host-only checks (no Editor, no SDK needed for most of them)
python3 tools/check_package_metadata.py
python3 tools/check_package_metadata.py --self-test
python3 tools/emit_failure_codes.py --check
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py

# the qualification player and one family probe (the build harness bounds each Editor invocation)
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/reproducibility/toolchain \
  tools/unity/build_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/reproducibility/probe tools/unity/run_narrative_probe.sh
```

`tools/reproduce.sh` is the authority on the exact command lines; the transcript records each one verbatim.
For the three dotnet commands the offline disposition is part of that record: with `GAMECORE_OFFLINE=1` the
transcript shows `-p:NuGetAudit=false` on the restore/build/test lines, and without it the lines are bare —
the audit ran.

## 7. Troubleshooting

| Symptom | Cause | Action |
| --- | --- | --- |
| `UNITY does not name the pinned Editor` | A different Editor revision | Use 6000.0.75f1. A different Editor is a different, unqualified profile. |
| `PROBE_RUNS must be 1 or 2` | You passed a larger count | Two is the project owner's cap. |
| `GAMECORE_OFFLINE must be 0 or 1` | You passed something other than `0`/`1` | The offline opt-out is a single explicit value, not a mode name. Set it to `1` only under §2.1's conditions. |
| `dotnet-restore`/`dotnet-build` fails with `NU1900` | The NuGet audit could not complete: the advisory feed was unreachable from `dotnet` | That is the designed failure, not a defect to work around blindly. Establish the feed really is unreachable (§2.1); only then re-run with `GAMECORE_OFFLINE=1`, and report the audit as `NotRun`. |
| A step fails with exit `127` | A command was not found | Check `DOTNET`, `PYTHON` and `PATH`; the step names the cause. |
| A step fails with exit `124`/`137` | The step timeout elapsed | For Unity, the single retry already ran. Escalate to [editor-hang.md](editor-hang.md). |
| `catalog-byte-identity` fails | Stale or non-deterministic catalogs | See [catalog-generation.md §4](catalog-generation.md). |
| `release-project: … already exists` | A previous release build left its disposable clone | `rm -rf unity/GameCore.ReleaseCheck` and re-run. |
| `package-metadata-audit` fails | A manifest or asmdef edit | The report names the exact package, reference and expected value. See [packages.md](packages.md). |

## 8. The conformance matrix, evidence index and compatibility report (GC-028)

`tools/reproduce.sh` proves one clean checkout reproduces the declared profile. It does **not** produce the
per-suite conformance matrix, the requirement evidence index or the compatibility report; three other tools
do, and their commands are:

```sh
# the TEST-001..TEST-024 matrix: one pass that produces every result file, then the per-suite status
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
  tools/conformance/run_test_matrix.sh

# the requirement/evidence index over a produced result tree
python3 tools/conformance/build_evidence_index.py \
  --evidence-root artifacts/conformance/results --out-dir artifacts/conformance/results --check

# the compatibility and three-family audit report over the same tree
python3 tools/conformance/build_compatibility.py \
  --evidence-root artifacts/conformance/results --out-dir artifacts/conformance/results
```

Useful subsets of the matrix runner (it takes the same `UNITY`/`DOTNET`/`PYTHON`/`PROBE_RUNS` environment as
everything else in this repository):

```sh
tools/conformance/run_test_matrix.sh --list          # the 24-suite table and exit
tools/conformance/run_test_matrix.sh --dry-run       # print every command, write no results
SUITE=TEST-013 tools/conformance/run_test_matrix.sh   # one suite only
STAGES=dotnet,host tools/conformance/run_test_matrix.sh   # one stage subset only
```

`STAGES` selects from `dotnet,unity,codegen,player,release,host,docs`; `RELEASE_BUILD=0` skips the
marker-free release half. `PROBE_RUNS` is capped at two, as everywhere else.

**Limits of these commands — read before quoting a result:**

- **A missing result file is `NotRun`, never `Pass`.** Every status is read out of a TRX, a Unity NUnit3 XML
  or a probe JSON the commands actually produced; nothing is reported as passing because a script said so.
- **The evidence index and compatibility report are derived, not independent evidence.** They are built *from*
  the result tree under `--evidence-root`; running `build_evidence_index.py`/`build_compatibility.py` against a
  stale or partial tree reports that tree. `build_evidence_index.py --check` exits non-zero when any row is
  `Fail` or any reference is unresolved — a green index over a tree that was never produced is impossible by
  construction, but an index over *someone else's* tree says nothing about your revision.
- **One profile only.** The matrix, the index and the compatibility report qualify exactly the profile in
  [profile.md](profile.md) (StandaloneLinux64 x86_64, IL2CPP, High stripping, headless). They are not
  portability evidence for any other target, and a compatibility "Pass" never rounds up to "supported
  everywhere". See [profile.md §3](profile.md#3-explicitly-unqualified).
- **TEST-023's timing row is not a timing claim.** The matrix's TEST-023 functional row can be `Pass` while
  the full-duration timing qualification stays **Deferred (owner decision)**; see
  [deferred-scope.md §3](deferred-scope.md#3-the-deferred-performance-qualification).
- **The NuGet audit disposition carries over.** These commands are not wrapped by `GAMECORE_OFFLINE`; they
  invoke `dotnet` directly, so the audit runs with the .NET SDK's own default. If you need the §2.1 opt-out
  for them too, pass `-p:NuGetAudit=false` explicitly on those command lines and record that you did.
- **`tools/run_conformance.sh` is a different thing**: the GC-024 *reference-conformance* gate (every 07
  before/after table in real worlds, plus the genre audit), not the matrix. `tools/run_w7_gate.sh` is the
  Wave 7 integrated gate. Neither is a substitute for the matrix runner above, and neither replaces
  `tools/reproduce.sh` as the clean-checkout entry point.

## 9. A live provider failed unexpectedly (P-012): safe deactivation vs fault-on-refusal

`ProviderFailed` is the code for one specific event: **an installation that was already `Active` failed
unexpectedly**, so its live activation can no longer hold execution authority and can never be kept active
(P-012; see the row in [failure-codes.md](failure-codes.md)). It is *not* the code for a candidate that
failed while preparing — that is `Preparing -> Failed`, a different fact with a different operator path.

P-012 gives that event exactly two outcomes, and which one you are in is readable from the report:

| Outcome | What happened | What you observe | Operator action |
| --- | --- | --- | --- |
| **Safe dependency-closure deactivation published** | The kernel published one ordinary composition edit: the failed installation is marked `Failed`, and every consumer that required it became `WaitingForDependencies` with its active contribution retracted — all in **one** assembly revision. | `ProviderFailureReport.Deactivated` is `true`, a publication token exists, the report's closure names the waiting consumers, and the code is `None`. | Inspect the failed installation and the waiting dependents. Restore or replace the provider with a **new operation** (mount/replace); waiting consumers resume automatically when a valid provider returns. Nothing needs unwinding: the world kept running on a reduced closure. |
| **Deactivation could not publish (fault on refusal)** | The deactivation's own publication was refused — a validator refused the change set, the plan was stale, or the publication boundary produced no result. **P-012 forbids keeping the provider active in this case.** | `Deactivated` is `false`, the token is null, and the refusal's own diagnostic code and detail name the reason; admission is closed and the world faults. | **Do not retry the failed provider in place and do not try to publish the deactivation by hand.** Follow the faulted-world procedure in [checkpoint-and-recovery.md §5](checkpoint-and-recovery.md#5-operator-procedure): the world is never resumed; recover into a **new** session from a trusted checkpoint or the initial definition, then fix the cause the refusal named. |

Two properties of this boundary are worth internalizing, because they are what "no invisible partial success"
reduces to here:

- **The report is exhaustive and exclusive.** Either the deactivation published (token present, code `None`)
  or it did not (token null, refusal code present) — never both, never neither. A report you cannot classify
  into one of the two rows above is a defect in the report, not a third outcome.
- **There is no timeout-based middle path.** No timeout, retry or partial publication exists on this path; a
  slow-but-refused deactivation is the second row, not a degraded first row.

The distinction from the neighbouring cases you are more likely to meet day-to-day:

| You observed | It is *not* `ProviderFailed`, it is | Where to go |
| --- | --- | --- |
| A consumer went `WaitingForDependencies` because a provider was **unmounted/lost by plan** | The ordinary P-012 closure outcome of a *deliberate* edit | This page's first row is only for the *unexpected* failure; for a deliberate unmount see [unload-and-leaks.md §3](unload-and-leaks.md#3-unloading-a-plugin-installation) |
| A candidate failed while preparing (`Preparing -> Failed`) | A failed *attempt*, not a failed live provider; the previously active activation is untouched | Re-submit with a new operation id; no deactivation is involved |
| `ApplyFault` after a live write | A post-write fail-stop (P-031) — the write path, not the provider-failure path | [checkpoint-and-recovery.md §5](checkpoint-and-recovery.md#5-operator-procedure) |

The kernel-side mechanics (which type marks the ledger entry, which resolver rule gives a failed activation
no bindings, and the fixtures that pin both halves) live in the composition package:
`ActivationLedger.FailActive`, `InstallationStateMachine` (`Active -> Failed` is the one edge P-012 adds to
the 06 §1 diagram), `CompositionEditApplier`, and `Packages/com.gamecore.composition/Tests/…/ProviderFailureTests.cs`.
