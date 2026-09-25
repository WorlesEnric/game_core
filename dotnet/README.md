# Plain-dotnet build and test (Unity-free code only)

This directory builds and tests the Unity-free sources of the Game Core V1 implementation with a plain
.NET SDK. It is the second half of the W0 gate: the documentation validator plus these pure fixtures and
the API snapshot. No Unity Editor, Burst or IL2CPP claim is made here — that evidence belongs to the
Unity qualification project and the player gates.

## Projects

| Project | Kind | Target | Sources |
|---|---|---|---|
| `src/GameCore.ReferenceSeams` | library | netstandard2.1 | `tests/GameCore.ReferenceSeams/**/*.cs` |
| `src/GameCore.ProtocolFixtures` | library | netstandard2.1 | `tests/GameCore.ProtocolFixtures/**/*.cs` |
| `src/GameCore.Contracts` | library | netstandard2.1 | `Packages/com.gamecore.contracts/Runtime/**/*.cs` |
| `src/GameCore.Content.Compiler` | library | netstandard2.1 | `Packages/com.gamecore.content.compiler/Runtime/**/*.cs` |
| `src/GameCore.ProtocolFixtures.Production` | library | netstandard2.1 | the same fixture sources, compiled against `GameCore.Contracts` |
| `tools/GameCore.ApiSnapshot` | console app | net8.0 | `tools/GameCore.ApiSnapshot/**/*.cs` |
| `tests/GameCore.ReferenceSeams.Tests` | NUnit 3 | net8.0 | API snapshot freeze test |
| `tests/GameCore.ProtocolFixtures.Tests` | NUnit 3 | net8.0 | fixture execution tests against the reference seam |
| `tests/GameCore.ProtocolFixtures.Production.Tests` | NUnit 3 | net8.0 | the same fixture suite against production `GameCore.Contracts` |
| `tests/GameCore.Contracts.Tests` | NUnit 3 | net8.0 | contract behaviour and the API-compatibility gate |
| `tests/GameCore.Content.Compiler.Tests` | NUnit 3 | net8.0 | catalog description rejection, emission reproducibility and the committed probe catalog |
| `src/GameCore.Composition` | library | netstandard2.1 | `Packages/com.gamecore.composition/Runtime/**/*.cs` |
| `tests/GameCore.Composition.Tests` | NUnit 3 | net8.0 | the composition package's own `Tests/**` sources |
| `src/GameCore.Execution` | library | netstandard2.1 | `Packages/com.gamecore.unity.runtime/Runtime/Pure/**/*.cs` |
| `tests/GameCore.Execution.Tests` | NUnit 3 | net8.0 | engine-free execution core tests |
| `src/GameCore.Faults.ReleaseCheck` | library (not in the solution) | netstandard2.1 | `Packages/com.gamecore.unity.runtime/Runtime/Faults/**/*.cs`, compiled twice by `tools/check_release_fault_free.py`: `-c Release` must contain no type and no boundary literal, `-c Qualification` must contain all of them (GC-017) |
| `src/GameCore.Derivation` | library | netstandard2.1 | `Packages/com.gamecore.derivation/Runtime/**/*.cs` |
| `tests/GameCore.Derivation.Tests` | NUnit 3 | net8.0 | the derivation package's own `Tests/**` sources |
| `src/GameCore.Planning` | library | netstandard2.1 | `Packages/com.gamecore.planning/Runtime/**/*.cs` |
| `tests/GameCore.Planning.Tests` | NUnit 3 | net8.0 | the planning package's own `Tests/**` sources |
| `src/GameCore.Rules.Narrative` | library | netstandard2.1 | `Packages/com.gamecore.rules.narrative/Runtime/**/*.cs` |
| `tests/GameCore.Rules.Narrative.Tests` | NUnit 3 | net8.0 | the narrative rules package's own `Tests/**` sources |
| `src/GameCore.Rules.Cards` | library | netstandard2.1 | `Packages/com.gamecore.rules.cards/Runtime/**/*.cs` |
| `tests/GameCore.Rules.Cards.Tests` | NUnit 3 | net8.0 | the card-rules package's own `Tests/**` sources |

The two fixture suites share one test source (`tests/GameCore.ProtocolFixtures.Tests/ProtocolFixtureTests.cs`).
They write separate evidence documents, `artifacts/protocol-fixtures/results.json` and
`artifacts/protocol-fixtures/results-production-contracts.json`, so neither run overwrites the other.

`tests/GameCore.Rules.Narrative.Tests` compiles the derivation fixtures' narrative vocabulary
(`Packages/com.gamecore.derivation/Fixtures/**/*.cs`) directly, exactly as `tests/GameCore.Derivation.Tests` does,
so the identity-agreement test can compare the narrative package's stable names and derived identities against
`GameCore.Derivation.Fixtures.NarrativeComposition` without a Unity assembly reference.

The W1 gate substituted production `GameCore.Contracts` for the frozen W0 reference seam in every production
consumer: `GameCore.Composition`, `GameCore.Execution` and their test projects reference
`src/GameCore.Contracts` (the two fixture suites keep their seam builds, so the seam is still compiled and still
compared against the API snapshot). `tests/GameCore.Contracts.Tests` deliberately does **not** reference
`GameCore.ReferenceSeams`: both assemblies declare the same types in the same namespace, so the frozen surface is
compared as a committed text snapshot via `GameCore.ApiSnapshot.ApiSurfaceComparer` instead of by referencing two
copies of the same types. No assembly in this solution references both contract assemblies.

`GameCore.Execution` compiles the engine-free execution core that also lives inside the Unity assembly
`GameCore.Unity.Runtime` (namespace `GameCore.Execution`): the temporal accumulator, the guarded dispatch plan,
the world/job resource ledger, the step publication boundary and the deterministic id sequence. It references
no `UnityEngine` or `Unity.*` type, which is why the same sources build under the plain SDK.

## Commands

Run from the repository root:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

The narrative rules package (GC-010) and its test project are part of that solution, so the two commands above
build and run them. While iterating on that package alone:

```sh
dotnet build dotnet/src/GameCore.Rules.Narrative/GameCore.Rules.Narrative.csproj -c Release
dotnet test dotnet/tests/GameCore.Rules.Narrative.Tests/GameCore.Rules.Narrative.Tests.csproj -c Release --logger trx
```

Everything a task's evidence needs is in the build and test output; no formatter, linter or Unity step is
part of these commands. GC-003 additionally provides a host-side static check of the C# sources (brace balance
and forbidden-language-construct scan) because this repository's authoring host has no C# compiler:

```sh
python3 tools/check_game_core_csharp.py
```

The GC-003 task gate — static checks, documentation validator, build, tests and, when `UNITY` is set, Unity
codegen plus the IL2CPP probe — is one command:

```sh
DOTNET=/usr/bin/dotnet PYTHON=/usr/bin/python3 tools/run_gc003_checks.sh
```

The W1 wave gate (this solution's build and tests, the package EditMode and PlayMode suites, the W1Gate EditMode
integration assembly, the IL2CPP build and every player probe) is one command:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet tools/run_w1_gate.sh
```

The W2 wave gate (this solution's build and tests, the package EditMode and PlayMode suites, the W1 and W2 gate
EditMode assemblies, the IL2CPP build and every player probe repeated `PROBE_RUNS` times) is one command:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet tools/run_w2_gate.sh
```

The W3 wave gate (two genuinely different running compositions on one kernel) adds the GC-010 narrative slice, the
GC-011 card slice and the W3 gate that runs both in one process; it runs this solution's build and tests, the whole
EditMode and PlayMode suites, the IL2CPP build and every player probe repeated `PROBE_RUNS` times:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet tools/run_w3_gate.sh
```

The Wave 4 provisional generic-execution profile gate (GC-012) adds the generated inactive family entries, the
canonical comparison of both families against their own fixture runs, the P-017/P-019 multi-supporter slot in a live
world, the host-side generic-profile audit and the kernel-separation clauses. It runs this solution's build and
tests, the host-side C# checks and audit, the whole EditMode and PlayMode suites, the IL2CPP build with High managed
stripping and every player probe repeated `PROBE_RUNS` times:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet tools/run_w4_profile_gate.sh
```

The generic-profile audit alone needs no Unity and no SDK; it reads the source tree and writes JSON:

```sh
python3 tools/w4_generic_profile_audit.py --out artifacts/gates/w4-generic-profile/generic-profile-audit.json
```

A generated catalog is normally produced by the content compiler through the Editor bridge. On a host with no SDK
and no Unity, a single new leading registration group can be applied to a committed catalog with
`tools/regen_catalog_group.py <description.json> <generated.cs>`, and the result verified independently with
`tools/verify_generated_catalog.py <generated.cs>` (which recomputes both the file hash and the catalog
fingerprint from the file's own text). The Editor bridge regenerates and is authoritative: the gate refuses a build
when a committed catalog differs from a fresh generation.

## Regenerating the committed probe catalog (GC-003)

`unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` is generated from
`unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json` by the production content compiler; the compiler
is the only owner of that file. Regenerate it with the pinned Editor and commit the result:

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/unity/build_probe.sh
```

`CommittedProbeCatalogTests` fails when the committed file differs from a fresh generation of its description,
and names the command above in the failure message.

## Regenerating the committed API snapshot

The snapshot is committed at `tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt` and compared by
`tests/GameCore.ReferenceSeams.Tests`. Regenerate it only for an intentional seam change, then rerun the
tests so the comparison and the file agree:

```sh
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.ReferenceSeams/bin/Release/netstandard2.1/GameCore.ReferenceSeams.dll \
  --output tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt \
  --namespace GameCore.Contracts
```

The committed snapshot was regenerated on the Linux build host after the Round 2 Wave 1 seam additions
(`CompositionHost.cs`, `WorldHost.cs`, `CatalogContract.cs` and the envelope/counter changes).
The compiled snapshot comparison passes; see `artifacts/gc-002/BUILD_REPORT.md`, Round 2, for evidence.
The test fails explicitly if a placeholder is present and prints a line diff when the listing differs.
After an intentional change, run the command above and rerun the tests. Any public
seam change additionally reopens the W0 interface gate before dependent modules compile
(`docs/game-core/09-implementation-guide.md`, GC-002).

## Notes

- Language level is C# 9 with nullable enabled, matching Unity's profile. `record`, `init`, file-scoped
  namespaces, raw string literals and global usings are avoided everywhere.
- `Directory.Build.props` sets `TreatWarningsAsErrors`; a warning is a build failure on purpose.
- `dotnet/src/GameCore.ProtocolFixtures` references `System.Text.Json` (netstandard2.1) for fixture JSON.
  A hand-written parser is deliberately not used.
- Fixture JSON is read from the repository tree, not a build output copy, so the committed fixtures are the
  single source of truth. The result document is written to `artifacts/protocol-fixtures/results.json`.
