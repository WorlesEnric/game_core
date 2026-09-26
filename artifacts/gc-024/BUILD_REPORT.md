# GC-024 build and test report

## Host and commands

Linux x86_64, Ubuntu 24.04, kernel 7.0.0-31-generic; Intel i7-12700KF. .NET SDK 8.0.425, runtime 8.0.31; Unity 6000.0.75f1, StandaloneLinux64 IL2CPP, High managed stripping; gcc 13.3.0, clang 18.1.3. All commands below ran in `gc-wt/gc-024` with `DOTNET_ROOT=$HOME/.dotnet`, `$HOME/.dotnet` on `PATH`, and `DOTNET_CLI_TELEMETRY_OPTOUT=1`. Initial sync: `git fetch origin && git checkout gc-024 && git reset --hard origin/gc-024` (starting revision `0b2f2e2`). Unity/player launches used `timeout 1800` for suites/builds or `timeout 600` for focused tests/probes; the harness also checks `pgrep -f "probeBenchmark"` before every launch, sleeping 60 seconds while a benchmark runs. The final gate recorded zero seconds of benchmark wait for its launches (`toolchain/unity/host-sharing.log`, `toolchain/toolchain/host-sharing.log`). Early manual guarded runs waited for GC-026 before the owner narrowed the host-sharing rule; no overlapping timed benchmark was launched.

Owner changed the repetition cap to **two** during the run. Final qualification and final-revision release family probes used `PROBE_RUNS=2`. An earlier release clone completed **5/5** per family before that update; those results are retained separately and were not rerun merely to reduce the count.

Exact final gate:

```sh
DOTNET=$HOME/.dotnet/dotnet UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  PROBE_RUNS=2 ARTIFACTS=$PWD/artifacts/gc-024/toolchain tools/run_conformance.sh
```

Final result: **PASS**, including `dotnet build dotnet/GameCore.sln -c Release --nologo`, `dotnet test dotnet/GameCore.sln -c Release --nologo --no-build`, host genre audit, Unity package resolve, all EditMode/PlayMode tests, catalog generation, IL2CPP player build, every configured probe harness, release-surface checks and documentation validator. The script uses `ARTIFACTS/toolchain` for player evidence; hence `artifacts/gc-024/toolchain/toolchain/` contains the gate's probe JSON and logs. The gate's `trx/gc024-trx.trx` filename is reused by concurrent test projects and therefore is **not** an aggregate test record; actual per-project counts were captured in the terminal runs with `LogFilePrefix=gc024-final` and are listed below. No Unity run hung for ten minutes after logging began; no gdb attach was needed.

Additional exact commands exercised:

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release --nologo
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --nologo --logger 'trx;LogFilePrefix=gc024-final' --results-directory artifacts/gc-024/trx
python3 tools/gc024_genre_audit.py
python3 tools/verify_generated_catalog.py
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS=$PWD/artifacts/gc-024/toolchain tools/unity/build_probe.sh
PROBE_RUNS=2 ARTIFACTS=$PWD/artifacts/gc-024/toolchain tools/unity/run_conformance_probe.sh
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release tools/unity/build_probe.sh
PROBE_RUNS=5 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release tools/unity/run_narrative_probe.sh
PROBE_RUNS=5 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release tools/unity/run_cards_probe.sh
PROBE_RUNS=5 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release tools/unity/run_traversal_probe.sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release-final tools/unity/build_probe.sh
PROBE_RUNS=2 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release-final tools/unity/run_narrative_probe.sh
PROBE_RUNS=2 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release-final tools/unity/run_cards_probe.sh
PROBE_RUNS=2 UNITY_PROJECT=$PWD/unity/GameCore.ReleaseCheck ARTIFACTS=$PWD/artifacts/gc-024/release-final tools/unity/run_traversal_probe.sh
```

The release-clone check passed **before** building the disposable clone (149 source/config files, no dangling assembly references, no qualification dependency/testables/lock). After Unity built that clone it generated `Library/PackageCache` and `Packages/packages-lock.json`; rerunning the source-only checker against the built tree reported 27 false-positive problems in third-party cached source and the generated lock. This post-build checker outcome is **Fail**, not hidden. The *actual release player build* and 15 family probe launches passed, and `GameCore.ReferenceConformance` is absent from its `GameCoreProbe_Data/ScriptingAssemblies.json` while present in the qualification player's equivalent. To rerun the source-only clone check, remove the disposable clone and regenerate it; do not interpret its post-build package-cache scan as a build failure.

## Results

| Gate | Pass | Fail | Skipped/NotRun | Evidence |
|---|---:|---:|---:|---|
| Whole .NET solution, 15 test projects | 1,143 | 0 | 0 | `artifacts/gc-024/trx/gc024-final_*.trx` |
| Unity EditMode, every testable | 1,109 | 0 | 0 | `toolchain/unity/editmode-results.xml` |
| Unity PlayMode, every testable | 53 | 0 | 0 | `toolchain/unity/playmode-results.xml` |
| GC-024 focused EditMode | 8 | 0 | 0 | `conformance-editmode6.xml` (also covered by full suite) |
| Qualification player probes, 21 JSON modes | 42 launches | 0 | 0 | `toolchain/toolchain/probe-*.json` and `.run2` |
| Final-revision release-clone narrative/cards/traversal | 6 launches (2 each) | 0 | 0 | `release-final/probe-{narrative,cards,traversal}.json` and `.run2`; earlier release clone also passed 15/15 before the cap |
| Host genre audit | clean | 0 violations | 0 | `genre-audit.json` |
| Generated catalog verification | pass | 0 | 0 | `toolchain/toolchain/codegen.log` and gate output |
| Release clone source check before build | pass | 0 | 0 | command output; actual build and player data verified separately |
| Release clone source check after build | 0 | 27 checker problems | 0 | scanner included generated `Library/PackageCache` and new lock; see above |

The conformance player's `coverage` step: **four tables, 72 rows compared, 2,026 normalized facts, `allPassed=true`**. Per-table verdicts: cards 27/27 rows (174 observations, 798 facts), narrative 16/16 (114 observations, 366 facts), traversal 20/20 (110 observations, 452 facts), combined narrative+cards 9/9 (102 observations, 410 facts). The four normalized player traces are committed under `artifacts/gc-024/traces/` and matched byte-for-byte against the final two player runs. Trace digests: cards `a952a132fae2a163218ecf7cf55670ae6ee6fc956b391055e51e70899a7ddb0f`; narrative `eac21cf949dbb4b90c175e119c309dad68ca9c9119ee25a31d02e13d80a5f8eb`; traversal `28171e16b9b61284bc76e1e2ae2e96525be6ce6d7cfa198ce2829ef7f0c53987`; cross: see `toolchain/toolchain/trace-digest-cross.txt`. All 07:276 rows (pending refusal, drain/dormant unmount, transfer, scoring unmount) and three invalid-graph diagnostics are named Pass steps in `toolchain/toolchain/probe-conformance.json`. The combined `reward-settle`/`reward-redelivery` observations show one destination mutation with retry preserving it. The loaded-assembly audit and host audit both passed; the player reports which tree half it could inspect.

All 21 qualification JSON modes have both first-run and `.run2` evidence. One is an intentional `ExpectedNegative` missing-registration probe; it passed its expected-negative contract, not a positive launch. The other 20 modes report `Pass` on both runs. Native lifecycle/W6 probe leak attribution reported zero blocks. Final-revision release family result JSONs each report `Pass` on both launches; the prior release-clone artifacts additionally record five passing launches per family.

## Repairs and rationale

- The pure fixture did not compile: removed a duplicate `Pre` name; declared the opted-in field and setup operation; implemented the audit document's existing JSON escape convention. Fixed rewards `FieldOwnership` to take the required `SchemaRef`.
- The oracle discarded accumulated failures, the trace counted composite row keys against bare IDs, and preserved-value observations could not be recorded as canonical absence. Corrected these without weakening assertions. Repaired missing field declarations, per-table row lookup (identical row IDs occur in different families), operation inventory and projection phase/step arithmetic. The sole test-construction fix uses each row's declared outcome rather than claiming a refused row published.
- Unity compiler fixes: namespace imports, real `ConformanceStep` script type instead of observation type, `CardId` typing, replay argument local, narrative world preparation imports and root setup, and cross-world `RewardsInstallation.Bridge` API usage. Release clone generation now matches the repaired argument comments.
- A `NoTargetChange` edit had consumed the publication number reserved for spawn, causing `StalePlan`; spawn now stages a neutral publication and lets the real spawn consume it. Card readers now resolve actual opted-in/practice targets and published supports; card and narrative exclusions use the supported scope form; card mode switch validates prospective derivation before admission so an exclusive conflict preserves the old assembly.
- Narrative base-layout initialization overwrote Mara's already-seeded conversation node, causing the choice to be rejected and no permit fact to commit. Preserve that owned state across initialization; declare gate east's complete opt-in for this conformance world; apply the narrative package's close transition on chapter unmount. Traversal's first frame captures the fixed-step origin and commits no step, so the fixture primes that frame before integration; EditMode avoids the PlayMode-only local physics scene while still running the fixed-step ECS stage.
- Combined-world descriptor lacked registered narrative/rewards migrations and did not declare the rewards outbox slot in its compiled ownership surface. Added the package's slot-only ownership manifest and supplied the installation's real migration instance. Read acknowledgement from the durable outbox rather than an unrelated dispatch counter. No generic `kernel:` change was needed; kernel validation was correctly refusing undeclared policy/state.
- Corrected four *fixture transcription* mismatches with `07` as authority, never relaxed a check: nested +5→+3 awards 15→13 (`07:51,289`); Festival +2→none provider changes Festival→none (`07:101`); a just-mounted ChapterTwo changes sibling sailor none→2 before the reparent row (`07:139,175`); persistent exclusions mean Mara/runner A stay absent through later suspend/resume in their original stages, so traversal suspension was moved to a fresh world to test actual 2000→none→2000 (`P-016`, `07:205,245`). The exclusive-conflict preconditions also now assert the actual Automatic→Conservative setup and the denied un-opted-in seat (`07:108`). Four 07:276 rows remain real; no test was skipped, ignored or deleted.
- The conformance probe harness had text grep expectations for `True` and `loaded=kernel=` though the actual JSON has `true` and `loaded=kernelAssemblies=`, and its cross-line digest regex found none. It now extracts each named digest via `jq` and compares both runs and committed traces. The GC-026 sharing guard follows the owner's latest `probeBenchmark` rule and records waits; the probe harness caps future repetitions at two.

## Residual notes

No failing .NET, Unity test, qualification probe, genre audit, or release-family probe remains on the final revision. The post-build source-only release-clone checker must be run on a freshly generated, *unbuilt* clone; its scan of generated third-party cache after build is a tooling limitation, not evidence of a failing release player. The final revision's release player was rebuilt from a fresh clone and its narrative/cards/traversal family probes each passed 2/2. Its `GameCoreProbe_Data/ScriptingAssemblies.json` does not contain `GameCore.ReferenceConformance`, whereas the final qualification player does. No unrun platform is claimed qualified.