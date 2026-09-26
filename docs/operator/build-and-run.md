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
| `DOCS` | `1` | Run the documentation validator. |

## 3. What it does, in order

The transcript numbers every step sequentially, including one entry per individual host-side check, so the
numbers below name the **groups** rather than promising an exact index. `steps.tsv` is authoritative for a
given run.

| Group | Action | Fails when |
| --- | --- | --- |
| toolchain | Records the revision (+ dirty count), Editor path, `dotnet --version`, `python3 --version`, host, `nproc`, `probe_runs` → `environment.txt` | Never (it is a record); the prerequisites in §1 are checked before it |
| package metadata | `check_package_metadata.py --self-test`, then the audit with `--json` | A rule of the auditor no longer holds, or a package version, name, description, `unity` field, or asmdef-derived dependency set disagrees |
| generated documents | `emit_failure_codes.py --check` | The committed failure-code table is stale relative to the sources |
| restore | `dotnet restore dotnet/GameCore.sln` | The NuGet graph cannot be restored |
| build | `dotnet build … -c Release --no-restore` | A Unity-free assembly does not compile |
| test | `dotnet test … -c Release --no-build` (trx into `host/trx`) | A test fails |
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
    budget-record.json      the ten budget rows and the owner deferral
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
  clone to a reproduced profile.
- **It does not run the full-duration TEST-023 timing catalogue.** That qualification is **Deferred by
  project-owner decision**; see [deferred-scope.md](deferred-scope.md). This script makes no timing claim.
- **It does not qualify any platform other than the declared profile.**

## 6. Re-running pieces

If one step fails, the failed command is in the transcript's `-- command:` line. Re-running a piece directly:

```sh
# the pure half
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test  dotnet/GameCore.sln -c Release --no-build

# the host-only checks (no Editor, no SDK needed for most of them)
python3 tools/check_package_metadata.py
python3 tools/check_package_metadata.py --self-test
python3 tools/emit_failure_codes.py --check
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py

# the qualification player and one family probe
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/reproducibility/toolchain \
  tools/unity/build_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/reproducibility/probe tools/unity/run_narrative_probe.sh
```

`tools/reproduce.sh` is the authority on the exact command lines; the transcript records each one verbatim.

## 7. Troubleshooting

| Symptom | Cause | Action |
| --- | --- | --- |
| `UNITY does not name the pinned Editor` | A different Editor revision | Use 6000.0.75f1. A different Editor is a different, unqualified profile. |
| `PROBE_RUNS must be 1 or 2` | You passed a larger count | Two is the project owner's cap. |
| A step fails with exit `127` | A command was not found | Check `DOTNET`, `PYTHON` and `PATH`; the step names the cause. |
| A step fails with exit `124`/`137` | The step timeout elapsed | For Unity, the single retry already ran. Escalate to [editor-hang.md](editor-hang.md). |
| `catalog-byte-identity` fails | Stale or non-deterministic catalogs | See [catalog-generation.md §4](catalog-generation.md). |
| `release-project: … already exists` | A previous release build left its disposable clone | `rm -rf unity/GameCore.ReleaseCheck` and re-run. |
| `package-metadata-audit` fails | A manifest or asmdef edit | The report names the exact package, reference and expected value. See [packages.md](packages.md). |
