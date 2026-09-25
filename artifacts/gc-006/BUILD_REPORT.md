# GC-006 BUILD REPORT

## Host and toolchain

- Host: Linux `worlesenric`, kernel `7.0.0-31-generic`, `x86_64`.
- .NET SDK: `8.0.425`; MSBuild `17.11.48+02bf66295`; host runtime `8.0.31`; `DOTNET_ROOT=/home/worlesenric/.dotnet`.
- Unity Editor: `6000.0.75f1` at `/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`.
- Starting revision tested after reset: `32513763a694e909c66514ca4c16150d5d4b0a97`.

## Commands run

```sh
git fetch origin && git checkout gc-006 && git reset --hard origin/gc-006
$HOME/.dotnet/dotnet --info
/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -version -batchmode -nographics -quit -logFile /tmp/gc006-unity-version.log
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj -c Release --logger "console;verbosity=normal" --logger trx --results-directory artifacts/gc-006/trx/derivation
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-006/trx
/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testFilter GameCore.Derivation.Tests -testResults artifacts/gc-006/unity/derivation-editmode.xml -logFile artifacts/gc-006/unity/derivation-editmode.log
python3 tools/check_game_core_csharp.py
python3 tools/validate_game_core_docs.py
```

Unity was run twice before the final passing command:

1. With `-quit`, package resolution failed because local packages declared `"com.gamecore.contracts": "file:../com.gamecore.contracts"`, which Unity 6000 rejects inside package dependencies.
2. After switching local package dependencies to version `0.1.0`, package resolution still failed until the project manifest added `com.gamecore.contracts` as a root local dependency.

Final Unity result XML was produced under the Unity project path and copied to `artifacts/gc-006/unity/derivation-editmode.xml` for evidence.

## Results

| Check | Result | Counts | Evidence |
| --- | --- | --- | --- |
| `dotnet build dotnet/GameCore.sln -c Release` | Pass | 0 warnings, 0 errors | console output |
| `dotnet test dotnet/tests/GameCore.Derivation.Tests/GameCore.Derivation.Tests.csproj -c Release` | Pass | total 88, passed 88, failed 0, skipped 0 | `artifacts/gc-006/trx/derivation/_worlesenric_2026-09-25_09_26_07.trx` |
| `dotnet test dotnet/GameCore.sln -c Release` | Pass | total 335, passed 335, failed 0, skipped 0 | `artifacts/gc-006/trx/*.trx` |
| Unity EditMode filter `GameCore.Derivation.Tests` | Pass | total 88, passed 88, failed 0, skipped 0, inconclusive 0 | `artifacts/gc-006/unity/derivation-editmode.xml`, `artifacts/gc-006/unity/derivation-editmode.log` |
| `python3 tools/check_game_core_csharp.py` | Pass | checked 99 C# files | console output |
| `python3 tools/validate_game_core_docs.py` | Pass | 14 Markdown docs checked | console output |

Full solution TRX files include one project with `total=0` because it has no tests; all executable test projects passed.

## Fixes made

### Compile fixes

- Converted provider identity wrapper usage from `PluginInstanceId` to `ProviderInstallationId` where contribution identity, imports, opt-ins, overrides and candidate evidence require provider-installation identity.
  - Files: `DerivationEngine.cs`, `DerivationOracle.cs`, `DerivationPolicy.cs`, `FixtureBuilder.cs`, `DerivationFixtureSupport.cs`, `ProvenanceTests.cs`, `PrecedenceAndSupportTests.cs`.
  - Reason: contracts define `ContributionKey`, `CandidateDecision.Provider`, `CapabilityImport`, `TargetOptIn` and `ProviderSelectionOverride` around `ProviderInstallationId`; install records still use `PluginInstanceId`. The conversion preserves the same `Id128` value.
- Fixed explanation paging construction in `DerivationExplainReader.cs`.
  - Added missing page budget/count locals and passed the missing `ExplanationSource` constructor argument.
  - Reason: code did not compile and pages must carry matching/rejected totals.
- Fixed `DerivationExplanation.EffectiveCapabilities` type from `IReadOnlyList<Id128>` to `IReadOnlyList<CapabilityId>`.
  - Reason: `TargetAssembly.EffectiveCapabilities` is a typed capability list and explanation consumers need the same type.
- Fixed `DerivationSnapshot` cycle walk dictionary indexing.
  - Reason: scope index is keyed by `Id128`; traversal must load `source[index[current.Value]].Parent`.
- Fixed composition-conflict witness construction in `CompositionKernel.cs`.
  - Reason: exclusive/incompatible conflict witness keys must be `Id128` evidence keys, not contribution-key objects.
- Fixed `TryCheckDeadline` to return `false` after constructing the budget rejection.
- Fixed local variable shadowing in `DerivationEngine` cross-capability incompatibility check.
- Fixed fixture/test compile defects: missing `AssemblyEpoch.Second` replacements, missing deterministic sequence fields, missing random-composition recipe names, and fixture deterministic key cast.

### Runtime/oracle correctness fixes

- Added an explicit scope-reach filter to `DerivationOracle`.
  - Reason: the oracle deliberately avoids indexes, but still must enforce rule reach (`LocalOnly`, `DescendantsOnly`, `SelfAndDescendants`) before policy evaluation. Without this, the oracle considered out-of-reach candidates and diverged from the indexed runtime.
  - Independence check: `DerivationOracle.Derive` still does not call `DerivationEngine.Derive`; it keeps its own stratum/target/install/rule traversal, grouping, assembly loop and reclassification. It shares only `DerivationPolicy`, `SlotComposer`, projection/comparison helpers and immutable data shapes.

### Test and fixture corrections

No tests were skipped, ignored or deleted. Expected values were changed only where fixture code contradicted the documented behavior or typed identity contracts.

- Mode/reach fixtures:
  - Fixed boundary fixtures to actually declare the tested capability boundary.
  - Moved an inner provider into a child scope so tests distinguish outside-provider blocking from inside-boundary contribution.
  - Put the sibling target inside provider reach so no-spillover assertions test boundary/exclusion semantics, not reach absence.
  - Declared the missing second-rule capability contract in rule-exclusion tests.
  - Declared the local-only contract and added the required target tag in the Conservative import fixture.
- Composition/precedence fixtures:
  - `AdditiveWithoutAReducerProducesTheCanonicalSetUnion` now builds a reducer-less additive slot; the reducer-folded additive path remains separately asserted.
  - Replace policy identity assertion now uses identity payloads instead of integer payloads.
  - Cross-capability incompatibility fixture now derives both effective capabilities and declares the incompatibility set; advertised descriptor capabilities alone are not effective derived slots.
  - Priority and identity-tie fixtures now put the higher priority / equal-depth candidates on the intended providers.
  - Support-retraction assertions now use `ProviderInstallationId` and account for replace-vs-additive delta semantics.
  - Reference card assertion now matches the documented REF-C04 behavior: nested festival remains after ancestor retraction.

### Unity package fixes

- Changed local package-internal dependencies on `com.gamecore.contracts` from `file:../com.gamecore.contracts` to version `0.1.0` in:
  - `Packages/com.gamecore.composition/package.json`
  - `Packages/com.gamecore.derivation/package.json`
  - `Packages/com.gamecore.planning/package.json`
- Added `com.gamecore.contracts` as a root local dependency in `unity/GameCore.Validation/Packages/manifest.json`.
- Committed regenerated `unity/GameCore.Validation/Packages/packages-lock.json`.
- Unity generated missing `.meta` files for the existing planning package; these are included because Unity import created them.

## Acceptance coverage notes

- Both mode predicates passed in dotnet and Unity EditMode (`ModeGrantTests`, `IsolationAndExclusionTests`).
- All five contribution policies passed (`CompositionPolicyTests`), including insertion-order permutation stability.
- Automatic derivation for existing/future compatible targets passed in reference and mode tests.
- Exclusive/incompatible conflicts, same-stratum reads and quota overflow reject whole proposals without partial closure (`CompositionPolicyTests`, `TerminationAndBudgetTests`).
- Provenance includes losing/shadowed candidates and excluded candidates (`ProvenanceTests`, policy tests).
- Runtime derivation equals the full-recompute oracle (`OracleAgreementTests`, 50 fixed seeds × 40 steps plus 50 random compositions and reference compositions).
- Randomized tests use fixed deterministic seeds and the repository LCG; no clock/System.Random/Guid source is used in those paths.

## Still failing / blocked

None observed in the required GC-006 checks. No PlayMode suite was requested or run for this task.
