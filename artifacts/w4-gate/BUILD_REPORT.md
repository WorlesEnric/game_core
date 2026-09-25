# W4-GATE build and test report

## Status

**NotRun (pending orchestrator build host).** Every command this report would record is unrun. This worktree is on
macOS (`darwin 25.3.0`, arm64) and has no Unity, no .NET SDK, no C# compiler and no Mono, so no build, no test, no
import and no player run was attempted here. Nothing below is a result; the sections are the exact commands and the
exact acceptance criteria the build host must satisfy.

What the host *did* run is in §3. It is host-side static checking only.

## 1. Revision and intended host

- Branch `w4-gate`, worktree `/Users/yangcao/wkspace/gc-wt/w4-gate`. Base `d3dc961` (`main` + GC-012); three
  `--no-ff` merges of `origin/gc-013`, `origin/gc-014`, `origin/gc-015`.
- Target host (from the worker brief): Linux x86_64, .NET 8 SDK used with `LangVersion 9` / `netstandard2.1` for the
  pure assemblies, Unity 6000.0.75f1 with Linux IL2CPP support at
  `~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`.
- The IL2CPP player profile this gate must be built with (unchanged from GC-012's gate): StandaloneLinux64, x86_64,
  IL2CPP, Release, Burst enabled, High managed stripping, and `Assets/link.xml` still carrying **no**
  `preserve="all"` entry for a kernel or gameplay assembly.

## 2. Commands to run, and what each must report

Run from the repository root. The one command that runs all of them is
`UNITY=<editor> DOTNET=<dotnet> PROBE_RUNS=5 tools/run_w4_gate.sh`.

| # | Command | Required result |
| --- | --- | --- |
| 1 | `dotnet build dotnet/GameCore.sln -c Release` | 0 errors, 0 warnings. |
| 2 | `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w4-gate/trx` | every test passes, including the three new `GameCore.Planning.Tests` cases that make the manifest reset field falsifiable (`AManifestSupportedResetProducesAResettablePolicy`, `AManifestWithoutResetSupportRefusesAnExecutedReset`, `AManifestSupportedResetWithoutAReasonIsADeclarationError`) and the W0-gate contract test that now accepts both waves' documented additions. |
| 3 | `"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w4-gate/unity/resolve.log` | packages resolve; `packages-lock.json` unchanged; every new `.cs` and `.meta` imported without a duplicate-GUID or missing-reference error; all scripts compile. |
| 4 | `"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/w4-gate/unity/editmode-results.xml -logFile artifacts/w4-gate/unity/editmode.log` | 0 failures. Must include `GameCore.W4Gate.Tests` (3 cases) **and** the pre-existing GC-013, GC-012, GC-014, GC-015 and W1–W3 assemblies, since the merge touched shared kernel files. |
| 5 | `"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/w4-gate/unity/playmode-results.xml -logFile artifacts/w4-gate/unity/playmode.log` | 0 failures. |
| 6 | `"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -executeMethod GameCore.Validation.Editor.CardCatalogGenerator.GenerateCatalog -logFile artifacts/w4-gate/unity/card-codegen.log` | the production compiler regenerates the card catalog. |
| 7 | `UNITY="$UNITY" ARTIFACTS=artifacts/w4-gate/toolchain tools/unity/build_probe.sh` | probe catalog regenerated and the StandaloneLinux64 IL2CPP Release player built with High stripping. |
| 8 | `git diff --exit-code -- unity/.../Generated/ProbeCatalog.g.cs` and `-- unity/.../GeneratedCards/CardCatalog.g.cs` | both committed catalogs byte-identical to the fresh generation. |
| 9–18 | every `tools/unity/run_*_probe.sh` at `PROBE_RUNS=5`: `run_probe.sh both`, `run_world_probe.sh`, `run_w1_gate_probe.sh`, `run_w2_gate_probe.sh`, `run_narrative_probe.sh`, `run_cards_probe.sh`, `run_w3_gate_probe.sh`, `run_w4_profile_probe.sh`, `run_gc013_probe.sh`, `run_w4_gate_probe.sh` | five clean runs each. `run_probe.sh both` reports `Pass` 5/5 and the deliberate missing registration `ExpectedNegative` 5/5 (exit 3). |
| 19 | `python3 tools/validate_game_core_docs.py --self-test` | 9 fixtures pass. |
| 20 | `python3 tools/validate_game_core_docs.py` | all 14 documents pass. |

### 2.1 The gate's own acceptance criteria (step 18)

`artifacts/w4-gate/toolchain/probe-w4-gate.json` must report `"task": "W4-GATE"`, `"mode": "W4Gate"`, `"result":
"Pass"`, at least one `"status": "Pass"`, no `"status": "Fail"`, every observation name (17 × 2 catalogs × 2 families
plus the two digest steps), the two digest literals
(`d73e1a15e3d5f997b47087d02ea73ed809b73692b35900c3f2feeeff65cebaab` narrative,
`4a1bdb460ab366c5a0ffed77f09aaca881b4fdfea142195290ee5ad73283b018` cards), and these fragments of the step details:
`mode=Automatic->Conservative`, `mode=Conservative->Automatic`, `mismatches=0`, `reverseOrder=True`,
`lateCompletion=discarded`, `dormant=True`, `namedExactly=True`, `shapeHeld=True`,
`supportProviderIsSecond=True`, `policyHostScoped=True`.

`run_w4_gate_probe.sh` fails the gate on any missing one, on any unclean run, and on a crash exit (`>= 128`).
`probe_runs.sh` treats a signal death as a crash rather than a verdict and never lets a later clean run repair an
earlier dirty one.

### 2.2 The Editor-invocation timeout policy (steps 3–6)

Every Unity Editor invocation is wrapped in `timeout --signal=TERM --kill-after=60 "${UNITY_TIMEOUT}"` (default
3600 s). A timed-out invocation is retried **exactly once**, with the retry logged; a second timeout is fatal. Any
other non-zero exit is fatal immediately, because a compile or test error is not the known intermittent pre-dispatch
hang. Player probes are not additionally retried here: `probe_runs.sh` already runs each probe `PROBE_RUNS` times and
fails the gate if any single run is unclean.

## 3. What was actually run on the authoring host

| Command | Result |
| --- | --- |
| `python3 tools/check_game_core_csharp.py` | `checked 359 C# file(s)` / `ok` (brace/paren/bracket balance, forbidden-construct scan, the engine-free rule for the pure assemblies). |
| `python3 tools/validate_game_core_docs.py --self-test` | passed: 9 isolated positive/negative fixtures. |
| `python3 tools/validate_game_core_docs.py` | passed: 14 Markdown documents; local links, anchors, ids, traceability, task DAG and wave ordering. |
| `bash -n tools/run_w4_gate.sh tools/unity/run_w4_gate_probe.sh tools/run_w3_gate.sh` | all clean. |
| `.meta` GUID scan over the whole worktree | 568 metas, 568 unique GUIDs, 0 duplicates. |
| scripted `IW4GateFamily` conformance check | 30 interface members; 0 missing and 0 duplicated in `W4GateNarrativeHost.cs` and `W4GateCardsHost.cs`; neither part redefines an `IGc013Family` member. |
| digest recomputation from the exported observation table | both literals reproduced independently of the implementation, with the 15-name GC-013 table used as the control (its two known literals also reproduce). |

**None of these is a build, an import, a test, a probe or a player run.** `check_game_core_csharp.py` reads text; it
does not type-check.

## 4. Risk carried into the first build-host run

Ordered by likelihood, from the handoff's §6:

1. A `IW4GateFamily`/adapter member shape mismatch — caught by the compiler, not by the name check.
2. A step's detail string not matching a clause fragment the probe script greps for, when that step took an
   early-return path.
3. The gate's policy publication order (pass first, then the lane advances on the case's neutral edit) differing from
   GC-015's scenario (lane first, then resynchronize). Both are independent paths on purpose.
4. `InstallEntry.Manifest.ServiceDependencies` / `InstallEntry.Bindings` property names in the mount step's
   resolution check.
5. A family's `PolicyTargets` set containing a live row the revision's manifests do not declare — the executor decides
   on every live slot it is shown, so an undeclared row makes a pass refuse.

## 5. Non-claims

- **No status in `artifacts/gates/w4-generic-profile/inventory.{md,json}` was promoted.** The gate is `Authored,
  NotRun`; 23 rows carry a `w4Gate` note naming what the gate's scenario covers and stating that the status is
  unchanged because nothing has run.
- **The Wave 4 exit gate is not claimed.** It cannot be claimed until §2 passes.
- \*\*`artifacts/gc-012/BUILD_REPORT.md` remains the current executable evidence for the provisional generic execution
  freeze.** This gate reruns it (step 17) but does not re-evidence it.
