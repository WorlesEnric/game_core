# PACKET APP-1: restore adoption

Branch: `codex/app-1`, based on `85dfbcae`. Work and verification run in the Linux build-host clone `/home/worlesenric/wkspace/gc-studio/app-1` (hostname `worlesenric`, the myubuntu host). No sibling clone, installed service, credential file or paid ETOS operation is used.

## R2 fixes

APP-1 closes the root-ownership part of review reconciliation **O28**, which affects R2-14 admission readiness and R2-32 restored-world consumers. The historical P1.2 note explicitly left adoption open. Changes stay within APP-1's exclusive paths; earlier packet notes remain historical.

| Finding | Fix | Regression in `GameCore.Unity.App.Tests.RestoreAdoptionTests` |
|---|---|---|
| APP-1 / O28 | Transfer `Current` only when replacing that exact root; publish `ActiveRoot` before old-world stop callbacks | `APP1_RestoreAdoptsCurrentAndPreservesRunState` (running/paused, twice), `APP1_CurrentNeverNullDuringStartStopOrRootChanged` |
| APP-1 isolation | A secondary root's restore leaves an unrelated `Current`, or null, untouched | `APP1_NonCurrentRestoreLeavesCurrentUntouched` (two cases) |
| APP-1 refusal/scratch preservation | Adoption is confined to a successful active-root replacement | `APP1_RefusedRestoreAndScratchRoundTripKeepCurrent` |

The package test assembly is Editor-only, not auto-referenced, and uses explicit test-runner/NUnit references, as supplied by the supported projects' pinned Unity Test Framework 1.6.0. Its define constraints are empty so the tests remain discoverable without editing Hollowmere or validation manifest/testables. Adding a test define constraint hid the fixture in these local packages; an explicit required test prevents mistaking a filtered run for acceptance. A friend assembly lets the fixture compose a second root through the same internal composition seam; it does not expose a public setter. Codecs are a per-fixture record double; the P1.2 suite supplies real generated-codec persistence coverage.

## Lifecycle contract

`SaveService.Restore` is the only `ActiveRoot` replacement assignment in this package; the other assignment is construction. `SaveServiceAdmissionCapture.TryRestore` already delegates to this path, so resume from an admission checkpoint receives the same behavior. `TestRoundTrip` only builds/disposes a scratch root and must never adopt it. The restored staging world starts for O-21 validation while the original remains current; only a successful validated replacement transfers ownership. A11 `Register`/`ResetSessionStatics` and ordinary stop semantics remain intact; no new static mutable state is introduced.

## Verification

Runtime implementation checkpoint: `2ad63dd1` (pushed); final test assembly configuration: `51055573`. Pre-fix runtime was unchanged from `85dfbcae`; the final corrected fixture's red run is `app1-red5`.

| Run | XML/TRX disposition | Evidence |
|---|---|---|
| Pre-fix package EditMode | 3 passed, **3 failed**: both adoption states returned null; lifecycle samples contained null | `.unity-logs/app-1/red.xml` (53 s Editor wall, 0.355 s tests) |
| Fixed Hollowmere EditMode | **15 passed, 0 failed/skipped**: 6 APP-1 + 9 P1.7a | `.unity-logs/app-1/editmode.xml` (51 s Editor wall, 1.948 s tests) |
| Fixed Hollowmere PlayMode | **9 passed, 0 failed/skipped**: 6 P1.7a + 3 P1.7c persistence | `.unity-logs/app-1/playmode.xml` (84 s Editor wall, 3.149 s tests) |
| Validation application + P1.2 persistence | **27 passed, 0 failed/skipped**: 10 existing application + 17 persistence | `.unity-logs/app-1/persistence.xml` (153 s Editor wall, 1.595 s tests) |
| Final Hollowmere EditMode (required APP-1 case) | **15 passed, 0 failed/skipped**: 6 APP-1 + 9 P1.7a, final assembly configuration | `.unity-logs/app-1/final.xml` (74 s Editor wall, 1.503 s tests) |
| dotnet solution (20 test projects) | **1815 passed, 0 failed, 6 skipped**, total 1821 | `.unity-logs/app-1/dotnet/*.trx`, `.unity-logs/app-1/dotnet.txt` |
| C# checker | **PASS**, 1131 files | `.unity-logs/app-1/csharp.txt` |
| Package metadata checker | **FAIL**, only the two baseline gameplay.world omissions below; 41 packages, 90 assemblies | `.unity-logs/app-1/metadata.txt` |

The test-discovery experiment (`discovery-notrun.xml`) ran only the nine P1.7a tests and was correctly rejected because the required APP-1 case was absent; it is not acceptance evidence.

The six dotnet skips are the explicitly gated live ETOS client cases L01–L06; no live node scenario was run. The app's dotnet-linked save-header sources were unchanged by APP-1. Initial fixture bring-up had one compile error (missing `fixedStep` argument) and three setup failures (value source, declared schedule, lowercase stable codec name); these are retained in launcher logs and `fixture-*-failure.xml` and are not counted as defect reproduction.

**Final regression totals:** 51 Unity tests passed (15 final Hollowmere EditMode + 9 PlayMode + 27 validation EditMode), zero failed/skipped. The earlier green EditMode rerun is not double-counted.

Raw host evidence is retained under `.unity-logs/app-1/` in this clone. Unity runs use `bash studio/tools/unity-batch.sh` with its host-wide allocator, one Editor held at a time, `-batchmode -nographics`, and XML-based dispositions. Shell scripts in this clone are not executable, so the `bash` prefix is required.

Exact commands (from the packet clone; `unity-batch.sh --results` supplies Unity's reserved `-testResults` argument):

```bash
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs/app-1" --label app1-editmode --results "$PWD/.unity-logs/app-1/editmode.xml" -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Unity\.App.*|Hollowmere\.P1_2.*|Hollowmere\.P1_7a.*|Hollowmere\.P3_1.*'
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs/app-1" --label app1-playmode --results "$PWD/.unity-logs/app-1/playmode.xml" -- -runTests -testPlatform PlayMode -testFilter 'GameCore\.Unity\.App.*|Hollowmere\.P1_2.*|Hollowmere\.P1_7a.*|Hollowmere\.P3_1.*|Hollowmere\.P1_7c.*HollowmereSaveRestore.*'
bash studio/tools/unity-batch.sh --project "$PWD/unity/GameCore.Validation" --log-dir "$PWD/.unity-logs/app-1" --label app1-persistence --results "$PWD/.unity-logs/app-1/persistence.xml" -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Persistence\.Tests.*|GameCore\.App\.Tests.*|GameCore\.Unity\.App.*'
bash studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" --log-dir "$PWD/.unity-logs/app-1" --label app1-final2 --results "$PWD/.unity-logs/app-1/final.xml" --require-test GameCore.Unity.App.Tests.RestoreAdoptionTests.APP1_CurrentNeverNullDuringStartStopOrRootChanged -- -runTests -testPlatform EditMode -testFilter 'GameCore\.Unity\.App.*|Hollowmere\.P1_2.*|Hollowmere\.P1_7a.*|Hollowmere\.P3_1.*'
PATH="$HOME/.dotnet:$PATH" dotnet test dotnet/GameCore.sln --logger trx --results-directory "$PWD/.unity-logs/app-1/dotnet"
python3 tools/check_package_metadata.py
python3 tools/check_game_core_csharp.py
git diff --check
```

Evidence SHA-256 (paths relative to `.unity-logs/app-1/`):

| File | SHA-256 |
|---|---|
| `red.xml` | `3394f8d1e890f1e9918854bb0bf0471f52ac13e7fc6dd3ae9ad127472f8238fc` |
| `editmode.xml` | `ee3b99c1557652fbbbcb9a22db1015d48ad8dd4a0195493a00cdd3aff4175ab6` |
| `playmode.xml` | `7be543de8a7adcb609484661bf4f80061221a91bd837e3d63f69e897503a78f2` |
| `persistence.xml` | `5c3daad78c22eb2fe017a3e8e275f54ab4603e3c68ac56cc0c269ae8a6f98a22` |
| `final.xml` | `0666537f7c4badcd19cbf8e422af5a452193ae3eaf61c6f11132504b370cffca` |

## Requests to other packets

- Gameplay package metadata owner: in `Packages/com.gamecore.gameplay.world/package.json`, declare `com.gamecore.studio.core: 1.0.0` and `com.unity.nuget.newtonsoft-json: 3.2.1` to match the existing Editor asmdef's Studio references and `Newtonsoft.Json.dll`. The metadata checker reports both missing dependencies on APP-1's unchanged baseline; these files are outside this packet's ownership.
- Hollowmere/P3.1 owner: the exact game-side file is not present in this baseline (no `AdmissionReady` implementation is found). In the P3.1-owned file defining `GameBoot.AdmissionReady`, after integrating APP-1, remove the interim `Current == null` acceptance branch from `GameBoot.AdmissionReady`; readiness can require `ReferenceEquals(World.Root, GameApplication.Current)` after restore. No game file is edited here.

## Left open

- The package metadata check cannot pass until the gameplay owner fixes the two baseline manifest omissions above; APP-1 has no authorization to edit that manifest.
- No P3.1 packet note or `Hollowmere.P1_2` / `Hollowmere.P3_1` test namespace exists in this checkout. The available P1.2 persistence suite is `GameCore.Persistence.Tests` in `unity/GameCore.Validation`; available Hollowmere lifecycle coverage is under `Hollowmere.P1_7a`.

The requested P3.1 interim `GameBoot.AdmissionReady` implementation is also absent from this baseline, so its exact game-side file and signature cannot be verified here; the removal request names the method from the packet brief. APP-1 does not claim a P3.1 admission end-to-end run.
