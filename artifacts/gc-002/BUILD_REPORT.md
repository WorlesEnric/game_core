# GC-002 Linux build report

**Current result: Round 2 passes — 31 NUnit tests and 58 protocol cases.** See [Round 2](#round-2) below.
The preceding Round 1 report is retained as historical evidence; its counts, hashes and commands apply only
to that earlier revision.

## Outcome

**Pass for the GC-002 pure-dotnet W0 checks.** The final run after committing the generated snapshot built all five projects with zero warnings/errors, passed all 13 NUnit tests, and executed all 40 protocol fixture cases: **40 Pass, 0 Fail, 0 NotRun, 0 Blocked**. No tests were skipped, ignored, deleted, or weakened. No fixture inputs or expected outcomes were changed.

Started from `origin/gc-002` at `89e3d19`. Implementation fixes are in `fe29413`; the generated API freeze is in `0416621`. Both were pushed to `origin/gc-002` before the final post-commit verification. This report and the supporting evidence are delivered in the subsequent evidence commit.

This report supersedes the historical **NotRun** state in `HANDOFF.md`. It establishes the test-only reference seam/oracle checks, not production runtime conformance or the combined GC-001/GC-002 W0 exit gate.

## Host and toolchain

- Run date: 2026-09-25; host: `worlesenric`, Linux x86_64, Ubuntu 24.04.4 LTS.
- Kernel: `7.0.0-31-generic`, `#31~24.04.1-Ubuntu SMP PREEMPT_DYNAMIC Mon Aug 10 09:38:02 UTC 2`.
- .NET SDK: **8.0.425**, commit `4a98f641c7`, installed at `/home/worlesenric/.dotnet`.
- MSBuild: **17.11.48+02bf66295**; .NET host/runtime: **8.0.31**; VSTest: **17.11.1 x64**.
- Python: **3.12.3**.
- Evaluated reference-seam settings: `TargetFramework=netstandard2.1`, `LangVersion=9.0`, `TreatWarningsAsErrors=true`, `Nullable=enable`, `EnableNETAnalyzers=false`.
- An explicit non-incremental reference-seam rebuild with those language/target/warning settings passed with **0 warnings, 0 errors**. Runtime inspection reported target `.NETStandard,Version=v2.1` and exactly one assembly reference: `netstandard`.
- NuGet restore succeeded. No Unity invocation or installation/license check was needed or performed for this pure-dotnet task.

Toolchain output is preserved in `host-dotnet.log`, `host-kernel.log`, `host-python.log`, and `reference-seam-properties.log` beside this report.

## Commands and execution history

All repository commands ran from `/home/worlesenric/wkspace/game_core`. The environment supplied to build processes was:

```sh
export DOTNET_ROOT=/home/worlesenric/.dotnet
export PATH=/home/worlesenric/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

Initial synchronization, as requested:

```sh
git fetch origin && git checkout gc-002 && git reset --hard origin/gc-002
```

Read `artifacts/gc-002/HANDOFF.md`, GC-002 in 09, the named TEST-002/021/022/024 sections in 08, and the shared surface tables in 05 before completing the API review. No normative protocol changes were needed.

The full runner was invoked six times, with separate retained logs rather than overwriting failure evidence:

```sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/initial tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/after-hash-fix tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/after-compiler-fixes tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/before-snapshot tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/final tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/post-commit tools/run_w0_checks.sh
```

Each invocation runs these exact steps, stopping on failure:

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

| Run / log directory | Validator self-test / validator | Build | NUnit tests |
|---|---|---|---|
| `initial/` | Pass: 9 fixtures / 14 Markdown documents | Fail: 16 unsigned hash arithmetic errors | NotRun: build stopped runner |
| `after-hash-fix/` | Pass: 9 / 14 | Fail: duplicate `Valid` member and invalid nullability reflection overload | NotRun: build stopped runner |
| `after-compiler-fixes/` | Pass: 9 / 14 | Fail: missing `RepoLayout.ResultDocumentPath` | NotRun: build stopped runner |
| `before-snapshot/` | Pass: 9 / 14 | Pass: 0 warnings/errors | Protocol 10 Pass; reference seam 2 Pass, 1 Fail (placeholder) |
| `final/` | Pass: 9 / 14 | Pass: 0 warnings/errors | Protocol 10 Pass; reference seam 3 Pass |
| `post-commit/` | Pass: 9 / 14 | Pass: 0 warnings/errors | Protocol 10 Pass; reference seam 3 Pass |

The `post-commit/` run is the final authoritative full-gate run. It occurred after `0416621` was committed and pushed, and after the strict rebuild and evidence README updates.

Snapshot generation, between `before-snapshot/` and `final/`:

```sh
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll \
  --output tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt \
  --namespace GameCore.Contracts
```

Strict build and host inspection commands, all exit 0:

```sh
dotnet msbuild dotnet/src/GameCore.ReferenceSeams/GameCore.ReferenceSeams.csproj -getProperty:TargetFramework,LangVersion,TreatWarningsAsErrors,Nullable,EnableNETAnalyzers
dotnet build dotnet/src/GameCore.ReferenceSeams/GameCore.ReferenceSeams.csproj -c Release -t:Rebuild -p:LangVersion=9.0 -p:TargetFramework=netstandard2.1 -p:TreatWarningsAsErrors=true --no-restore
dotnet --info
uname -a
python3 --version
```

A throwaway C# 9 console consumer exercised the changed hash implementations against the compiled seam. Its exact project and source are retained in `hash-smoke.log`; the temporary project was removed afterward:

```sh
dotnet run --project /tmp/gc002-hash-smoke-54yco6zd/HashSmoke.csproj -c Release
```

The first smoke invocation failed to compile because the execution bridge rewrote a source line beginning with `!` into `__omp_shell(...)` while creating the temporary source. That was a smoke-harness creation defect, not a repository defect. The malformed line was repaired and the same command rerun: **52/52 dictionary lookups passed**, covering equal independently constructed values at 0, 1, 2^32, and UInt64.MaxValue (with corresponding UInt32 casts) across all 13 corrected types. Both initial and successful smoke logs are retained. No runtime exceptions occurred in the successful run.

After the strict seam rebuild, regeneration to a second file and byte comparison both exited 0:

```sh
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll \
  --output /tmp/gc002-hash-smoke-54yco6zd/second.api.txt \
  --namespace GameCore.Contracts
cmp /home/worlesenric/wkspace/game_core/tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt /tmp/gc002-hash-smoke-54yco6zd/second.api.txt
```

## Every implementation fix

1. **Unsigned hash arithmetic — `tests/GameCore.ReferenceSeams/Identity/Handles.cs`.** Sixteen expressions in 13 value types added `uint`/`ulong` directly to an `int` accumulator, causing CS0034/CS0266. Each now adds the field's `GetHashCode()`, preserving the existing unchecked multiply-by-31 scheme and including both halves of UInt64 values rather than simply truncating them. Constructors, fields, equality, and protocol values were unchanged. The strict build and 52-lookup smoke verify the correction.
2. **Conflicting verdict members — `tests/GameCore.ProtocolFixtures/Fixtures/FixtureModel.cs`.** `OracleVerdict` declared both a static `Valid(...)` factory and a `Valid` property (CS0102). Renamed the property to `IsValid`, matching the oracle's existing boolean naming, and migrated all accesses in `FixtureRunner.cs` and `ProtocolFixtureTests.cs`. The factory names remain unchanged. This is fixture-harness API, outside the frozen `GameCore.Contracts` surface. The only test-source edit is the corresponding property access; its false expectation and rejection-code checks are unchanged.
3. **Return nullability reflection — `dotnet/tools/GameCore.ApiSnapshot/ApiSnapshotGenerator.cs`.** `NullabilityInfoContext.Create` has no `MethodInfo` overload (CS1503). It now receives `method.ReturnParameter`. Real CLI generation, in-process generation, deterministic regeneration, and the snapshot comparison all execute successfully.
4. **Missing result path — `tests/GameCore.ProtocolFixtures/RepoLayout.cs`.** Added the missing `ResultDocumentPath` property used by suite setup (CS0117), with the already documented value `artifacts/protocol-fixtures/results.json`. This follows the neighboring repository-relative path properties. The suite writes the real result document and verifies its path, shape, and summary.
5. **Pending snapshot — `tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt`.** Replaced the deliberate placeholder using the real compiled-assembly tool, not hand-authored expected output. Observed the snapshot test fail before generation and pass afterward, including after committing the generated file.
6. **Evidence status documentation.** Updated `dotnet/README.md`, both test-directory READMEs, and `artifacts/protocol-fixtures/README.md` to replace stale pending-generation/unexecuted claims with the actual run status and report location. The original handoff remains an unchanged historical record.

No design gap required a new protocol decision. No language-version relaxation, framework retargeting, warning suppression, expected-value change, or test deletion was used. No new permanent tests were necessary for these compiler fixes; the existing failing build, existing suites, real snapshot CLI, and retained throwaway consumer provide the evidence.

## Final test results

| Executed check | Pass | Fail | NotRun / skipped | Blocked |
|---|---:|---:|---:|---:|
| Documentation validator self-test fixtures | 9 | 0 | 0 | 0 |
| Documentation validator | 1 invocation covering 14 Markdown documents | 0 | 0 | 0 |
| `GameCore.ProtocolFixtures.Tests` NUnit tests | 10 | 0 | 0 | 0 |
| `GameCore.ReferenceSeams.Tests` NUnit tests | 3 | 0 | 0 | 0 |
| Protocol JSON cases (executed inside the suite, not additional NUnit tests) | 40 | 0 | 0 | 0 |
| Throwaway hash dictionary lookups | 52 | 0 | 0 | 0 |

The 13 final NUnit test results, read from the retained TRX files:

| Suite | Test | Outcome |
|---|---|---|
| ProtocolFixtures | `CaseDirectoryContainsFixtureFiles` | Pass |
| ProtocolFixtures | `CaseIdentifiersAreUniqueAcrossFiles` | Pass |
| ProtocolFixtures | `CounterOracleRefusesOverflowForEveryCounter` | Pass |
| ProtocolFixtures | `EveryCaseExecutesAndMatchesItsExpectation` | Pass |
| ProtocolFixtures | `EveryDocumentedKindAndRequirementIsExercised` | Pass |
| ProtocolFixtures | `FixtureLoaderRejectsMalformedCasesAndRunnerBlocksUnknownKinds` | Pass |
| ProtocolFixtures | `OracleDistinguishesValidFromInvalidOutcomes` | Pass |
| ProtocolFixtures | `ResultDocumentRoundTripsAndIsWrittenToTheDocumentedPath` | Pass |
| ProtocolFixtures | `SeamCodecAgreesWithOracleCanonicalBytes` | Pass |
| ProtocolFixtures | `ShuffledInsertionOrdersProduceIdenticalCanonicalOutput` | Pass |
| ReferenceSeams | `GeneratedListingIsDeterministicAndExcludesAssemblyName` | Pass |
| ReferenceSeams | `PendingHeaderIsTreatedAsFailureWithClearMessage` | Pass |
| ReferenceSeams | `SeamApiSnapshotMatchesCommittedFile` | Pass |

Per-case primary suite attribution in `artifacts/protocol-fixtures/results.json`:

| Primary test ID | W0 subset exercised | Pass | Fail / NotRun / Blocked |
|---|---|---:|---|
| TEST-002 | Identity bytes, world/generation/activation separation, counters and publication advancement | 27 | 0 / 0 / 0 |
| TEST-021 | Oracle and reference-seam engine/gameplay assembly independence | 2 | 0 / 0 / 0 |
| TEST-022 | Canonical stable-ID ordering | 3 | 0 / 0 / 0 |
| TEST-024 | Protocol major/minor/required-feature interpretation | 8 | 0 / 0 / 0 |

Every individual fixture outcome and its expected-versus-observed explanation is in the committed result JSON. The named validation suites have broader runtime acceptance outside these W0 subsets; those broader suites are **NotRun**, not implicitly passed by this report.

## API snapshot sanity review

- Generated from the actual `GameCore.ReferenceSeams.dll`: **169 types, 1,187 members, 1,363 lines, 114,263 bytes**.
- Type names are unique and ordinally sorted; every type is in `GameCore.Contracts`. No Unity, gameplay, or `GameCore.TestFixtures` references occur in the listing.
- Reviewed identity widths and category wrappers; generation/epoch/step fields; factory keys; manifest declaration collections; change-plan deltas; owner/slot/access and stage/buffer descriptors; host dispatch, observer, operation, explanation, lease and callback interfaces; serialization surface; nullable publication token and distinct operation outcomes against 05 and the documented handoff decisions.
- The existing four-argument `OperationResult` constructor and extended revision/epoch/diagnostic constructor are both present. Wrapper choices and the 128-bit `SlotId` decision remain as documented; no contract field was changed to make compilation pass.
- A second real generation after an explicit strict rebuild is byte-identical. The committed-snapshot test passes after the snapshot commit.
- Snapshot SHA-256: `7f7850441899ec268f577a90fb14bb1f6a4a7952caf8560767f03ca97e2604bc`.
- Result JSON SHA-256: `ea2f4dec66991f734620d7f3f9d68c3688716abfc2ade77a28849cc7c3537243`.

Review limits: this is the existing reflection listing format, not an exhaustive binary/source compatibility checker or proof of every DTO's semantics. It does not encode all C# modifiers, constant values, generic constraints, or nested nullability annotations. Enum headers include inherited .NET 8 interfaces such as `System.ISpanFormattable`; that is host reflection metadata, not a new reference in the netstandard2.1 seam. Regeneration was verified on the stated .NET 8 toolchain, not across other reflection runtimes. Public-surface changes still require deliberate W0 review; snapshot equality is not a substitute for that review.

## Evidence and remaining scope

Committed evidence includes the real API snapshot, `artifacts/protocol-fixtures/results.json`, all six gate-run log directories, before/after/post-commit TRX files, toolchain/property/rebuild logs, CLI-generation/byte-comparison logs, and smoke logs with their source. All evidence files are below 2 MB; none required trimming. Temporary smoke project and duplicate snapshot were removed. Build outputs and normal `TestResults` directories remain ignored; TRX copies are retained under this report's directory.

**No GC-002 pure-dotnet check remains failing or blocked.** The following were not run and are not claimed:

- Unity Editor/EditMode/PlayMode, IL2CPP, Burst, player probing, or Unity package-lock validation (GC-001/other task scope).
- Production ECS/world behavior, the full TEST-002/021/022/024 runtime acceptance, three reference compositions, 10,000-step replay, performance/benchmarks, and later-wave integration.
- Remote documentation URL checking or cross-platform compatibility.

The W0 oracle agrees with its 40 fixtures. This does not establish production runtime conformance or authorize skipping any later integration gate.

## Round 2

### Outcome and revision

**Pass for the updated GC-002 pure-dotnet W0 gate.** All five projects build with **0 warnings and 0 errors**.
The final post-commit run passes **31/31 NUnit tests** and **58/58 protocol JSON cases**:
**0 Fail, 0 skipped, 0 NotRun, 0 Blocked**. The JSON cases run inside the NUnit suite, not as 58 additional
NUnit tests. Both documentation checks pass.

Started from `origin/gc-002` at `9d29da6`, including HANDOFF section 7. Round 2 commits:

- `17ae69c`: compiler, implementation and test-harness fixes described below.
- `b626f45`: regenerated compiled W1-facing API snapshot.
- `7b433bc`: current 58-case `results.json`, replacing `results-40-case-pre-review.json`.
- The subsequent report/evidence commit contains this section, retained logs/TRX and refreshed evidence READMEs.

No test was deleted, skipped, ignored or weakened. No fixture input or expected outcome changed. One
NUnit expectation was corrected using explicit normative authority: shutdown cannot succeed while the
test's failed dispatch still owns an unfinished quarantined job. Details below.

### Host, constraints and scope

- Linux x86_64 / Ubuntu 24.04, host `worlesenric`; kernel `7.0.0-31-generic`.
- .NET SDK **8.0.425**, MSBuild **17.11.48+02bf66295**, .NET host/runtime **8.0.31**,
  VSTest **17.11.1 x64**; Python **3.12.3**.
- Both pure projects evaluate to `netstandard2.1`, C# **9.0**, nullable enabled,
  warnings-as-errors enabled, analyzers disabled. Neither target nor language level was relaxed.
- An explicit **whole-solution non-incremental rebuild** also passed with zero warnings/errors.
- The two executable assembly-independence fixture cases pass. No engine/gameplay/fixture-namespace
  references appear in the generated `GameCore.Contracts` API listing.
- The configured Unity executable exists (`Path.is_file()` checked
  `/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`). Unity was **NotRun**:
  HANDOFF section 3 assigns Unity/IL2CPP probing to GC-001, not this pure-dotnet task.
  The installed Editor's actual version and license were not independently exercised.

### Exact commands and run history

Repository synchronization used a non-destructive fast-forward rather than a hard reset:

```sh
git fetch origin && git checkout gc-002
git merge --ff-only origin/gc-002
```

Read HANDOFF first, then GC-002 in 09 and TEST-002/021/022/024 in 08. Relevant normative sections in
00 and 05 were consulted before behavioral repairs and the corrected shutdown expectation.

All build processes ran from the repository root with:

```sh
export DOTNET_ROOT=/home/worlesenric/.dotnet
export PATH=/home/worlesenric/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export DOTNET_CLI_TELEMETRY_OPTOUT=1
```

The full runner was invoked seven times, retaining separate logs:

```sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/initial tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/after-compiler-fixes tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/before-snapshot tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/compiled-tests tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/after-behavior-fixes tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/final tools/run_w0_checks.sh
ARTIFACTS=/home/worlesenric/wkspace/game_core/artifacts/gc-002/round-2/post-commit tools/run_w0_checks.sh
```

Each runner invokes, in order and stopping on failure:

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

| Round 2 log directory | Build | ReferenceSeams NUnit | ProtocolFixtures NUnit | Protocol JSON |
|---|---|---|---|---|
| `initial/` | Fail: 5 compiler errors | NotRun | NotRun | NotRun |
| `after-compiler-fixes/` | Fail: 7 missing-probe-member errors | NotRun | NotRun | NotRun |
| `before-snapshot/` | Fail: 1 test constructor type error | NotRun | NotRun | NotRun |
| `compiled-tests/` | Pass | 17 Pass, 4 Fail | 9 Pass, 1 Fail | 55 Pass, 3 Fail |
| `after-behavior-fixes/` | Pass | 20 Pass, 1 Fail (snapshot placeholder) | 9 Pass, 1 Fail | 57 Pass, 1 Blocked (empty candidate) |
| `final/` | Pass | 21 Pass | 10 Pass | 58 Pass |
| `post-commit/` | Pass | 21 Pass | 10 Pass | 58 Pass |

Every invocation passed the documentation self-test (9 isolated fixtures) and validator (14 Markdown
documents). All successful builds had zero warnings/errors. No NUnit test was skipped in any executed run.
`post-commit/` is the authoritative final gate, after the code, snapshot and fixture-result commits and
the evidence README edits. It produces the same case document and per-test outcomes as `final/`.

Additional executed commands, all exit 0:

```sh
dotnet build dotnet/GameCore.sln -c Release -t:Rebuild -p:LangVersion=9.0 -p:TreatWarningsAsErrors=true
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll --output tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt --namespace GameCore.Contracts
dotnet --info
uname -a
python3 --version
dotnet msbuild dotnet/src/GameCore.ReferenceSeams/GameCore.ReferenceSeams.csproj -getProperty:TargetFramework,LangVersion,TreatWarningsAsErrors,Nullable,EnableNETAnalyzers
dotnet msbuild dotnet/src/GameCore.ProtocolFixtures/GameCore.ProtocolFixtures.csproj -getProperty:TargetFramework,LangVersion,TreatWarningsAsErrors,Nullable,EnableNETAnalyzers
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll --output /tmp/gc002-round2-snapshot-1praxj7i/second.api.txt --namespace GameCore.Contracts
cmp tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt /tmp/gc002-round2-snapshot-1praxj7i/second.api.txt
```

The temporary second snapshot directory was removed after comparison. `round-2/commands.json` records
the additional command argument vectors, exit statuses and log paths. The toolchain inspection
`/home/worlesenric/.dotnet/dotnet --info` was also run once before the logged inspection above.

### Every Round 2 fix and justification

1. **`Identity/CanonicalHex.cs`: CS0675.** Widening the signed nibble directly to UInt64 triggered
   the sign-extension warning-as-error. Convert its validated 0–15 value to UInt32 first, then combine
   with the UInt64 accumulator. No suppression or parsing relaxation.
2. **`Identity/Handles.cs`: typed generation comparison.** `AsyncWorkToken.IsAllocated` and its constructor
   still compared the new `InstallationGeneration` struct against `ulong`. Compare `.Value` to zero,
   retaining the generation-zero reservation; no public signature changed.
3. **`Serialization/Envelope.cs`: header writer calls.** Two calls supplied a redundant buffer argument
   to the existing two-argument instance `WriteUInt32BigEndian` helper. Removed only that argument.
4. **`Oracle/EnvelopeOracle.cs`: missing probe result members.** Added the read-only `Accepted`, `Code`
   and `Detail` properties already assigned by `EnvelopeProbe`'s constructor and consumed by the runner,
   following the existing `OracleVerdict` pattern.
5. **`SeamContractTests.cs`: missing schema wrapper.** Wrapped the unknown-schema test's existing `Id128`
   in `SchemaId` to satisfy the typed `SchemaRef` constructor. Identity bits and `MissingDependency`
   expectation are unchanged.
6. **`Serialization/Envelope.cs`: float wire types rejected.** `IsKnownWireType` omitted `Float32` and
   `Float64` despite their writers/readers being implemented. Added both cases. Before the fix, the
   round-trip probe decoded only 5 of 14 fields and NaN decoding failed; both original fixtures now pass.
   This implements 05 section 6's explicit IEEE-754 encoding.
7. **`Oracle/CanonicalOrder.cs`: overly permissive independent parser.** `NumberStyles.HexNumber`
   accepted uppercase/whitespace, disagreeing with HANDOFF section 7E and the canonical-form fixture.
   Independently validate each character as `0–9` or `a–f`; parse spans with `AllowHexSpecifier`.
   No seam-codec dependency was introduced, and substring allocations were removed.
8. **`Fixtures/FixtureRunner.cs`: empty negative candidate blocked.** The generic non-empty string
   helper rejected the fixture's intentional empty input before either parser could inspect it.
   Added an opt-in `allowEmpty` argument used only for hex candidates. Required metadata and canonical
   strings remain non-empty; the unchanged empty-string candidate now reaches both parsers and rejects.
9. **`Stubs/InMemoryHost.cs`: matching retransmission rejected and sequence drifted.** `Record` returned
   false for both duplicates and conflicts; `Submit` consequently rejected a valid retry. Matching
   retries now return accepted without enqueueing, and receipts use the stored admission sequence,
   not the next global sequence. The original conflict result and ledger-row preservation remain intact.
   The existing test now also admits an intervening command, then verifies the retry keeps its original
   sequence and does not enqueue again (P-050 and the receipt's replay-order contract).
10. **`Stubs/ObservationStubs.cs`: capacity off-by-one.** The acquisition check refused even the single
    retained image at capacity 1. Equality is now allowed; an over-capacity retained fixture set still
    reports `SnapshotBackpressure`. The existing expiry/foreign-world/capacity test now passes unchanged.
    This is the existing retained-image-count stub policy, not a production snapshot pool.
11. **`SeamContractTests.cs`: incorrect successful-stop expectation.** The test explicitly leaves one
    unfinished quarantined job after dispatch failure, then incorrectly expected `Published` from Stop.
    [P-047](../../docs/game-core/00-core-protocols.md#p-047) requires executing jobs to finish before
    release; [P-048](../../docs/game-core/00-core-protocols.md#p-048) requires unfinished users to remain
    pinned and report `TeardownBlocked`; O-19 likewise requires a blocked/quarantined result.
    Changed that one expected outcome to `Rejected` and added assertions for `TeardownBlocked`,
    `Stopping`, and the outstanding/quarantined job remaining tracked. **The host Stop implementation
    was not changed.** The existing separate stalled-job test still verifies successful disposal after
    the job completes. This strengthens lifetime safety rather than weakening a failing assertion.
12. **Generated freeze and evidence.** Replaced the intentional snapshot placeholder using the real
    compiled-assembly CLI. Replaced the obsolete 40-case result artifact with the real 58-case run.
    Refreshed `dotnet/README.md`, both protocol evidence READMEs, and made the illustrative one-row
    result JSON's summary count one. HANDOFF remains a historical implementation-worker record.

Paths above are relative to `tests/GameCore.ReferenceSeams/`, `tests/GameCore.ProtocolFixtures/`, or
`dotnet/tests/GameCore.ReferenceSeams.Tests/` as indicated. No new protocol rule or design-gap workaround
was needed. No expected JSON values, language settings, framework targets or compiler warnings were relaxed.
Existing failing scenarios provide retained before/after regression evidence; no additional test method
or permanent smoke project was added.

### Final suite and per-test results

| Executed check | Pass | Fail | NotRun / skipped | Blocked |
|---|---:|---:|---:|---:|
| Documentation validator self-test fixtures | 9 | 0 | 0 | 0 |
| Documentation validator | 1 invocation / 14 documents | 0 | 0 | 0 |
| ReferenceSeams NUnit tests | 21 | 0 | 0 | 0 |
| ProtocolFixtures NUnit tests | 10 | 0 | 0 | 0 |
| Protocol JSON cases | 58 | 0 | 0 | 0 |

All 31 per-test names/outcomes are retained in `round-2/test-results.json` and the two
`round-2/post-commit/*.trx` files. ReferenceSeams' 21 include the 18 seam contract tests and 3 API
snapshot tests; ProtocolFixtures' 10 tests are the same named suite methods listed in the Round 1 table.

| Primary validation ID | Current W0 fixture cases passing | Fail / NotRun / Blocked |
|---|---:|---|
| TEST-002 | 28 | 0 / 0 / 0 |
| TEST-021 | 2 | 0 / 0 / 0 |
| TEST-022 | 4 | 0 / 0 / 0 |
| TEST-024 | 24 | 0 / 0 / 0 |

Each individual case's expected-versus-observed detail is in `artifacts/protocol-fixtures/results.json`.
The envelope probes deliberately exercise the seam codec; they are not an independent implementation
of that codec, as documented by the fixture package.

### API freeze, retained evidence and remaining limits

- Generated API: **221 types, 1,589 member lines, 1,817 total lines, 152,655 bytes**.
- Includes the intentionally expanded W1 surface; generated from the C# 9 / netstandard2.1 assembly.
  The existing placeholder-failure test, deterministic-generation test and committed-file comparison
  all pass, including after the snapshot commit. A second CLI generation is byte-identical.
- Snapshot SHA-256: `89a54bc49ad543c5765bdf29dbbc44160a2fad6b1d286da41e222362e7a6c6e7`.
- Result JSON SHA-256: `48a6f41f94a8d173f5d3ffaa75bd33a37f843cd364010808e3ba719b59e05f7d`.
- `round-2/` retains all seven gate-run log directories, failure/success TRX, failing case documents,
  toolchain/property/rebuild/generation logs, command metadata and per-test results. No evidence file
  exceeds 2 MB, so no trimming was necessary. The obsolete result filename is absent from the working
  tree; historical 40-case evidence remains in Git history.

**No applicable GC-002 pure-dotnet check remains failing or blocked.** Unity Editor/EditMode/PlayMode,
IL2CPP/Burst/player probing, package-lock validation, production ECS behavior, W1 real-module integration,
the full broader TEST-002/021/022/024 acceptance, reference compositions, 10,000-step replay, performance
and cross-platform runs are **NotRun**, not implied passes. No Unity package or package-lock change was
required by this task. The snapshot format limitations documented in Round 1 still apply.

This closes the requested build-host pass over the test seam/oracle; it does not claim production runtime
conformance or the combined GC-001/GC-002 W0 exit gate.
