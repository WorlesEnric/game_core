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
| `tools/GameCore.ApiSnapshot` | console app | net8.0 | `tools/GameCore.ApiSnapshot/**/*.cs` |
| `tests/GameCore.ReferenceSeams.Tests` | NUnit 3 | net8.0 | API snapshot freeze test |
| `tests/GameCore.ProtocolFixtures.Tests` | NUnit 3 | net8.0 | fixture execution tests |

`GameCore.ReferenceSeams` is **test-only**: it freezes the shared compile-time surface of
`docs/game-core/05-contracts-and-data-model.md` so Wave 1 peers can compile in parallel. GC-003 replaces it
with production `GameCore.Contracts` without a surface change. Sources live under `tests/` so the same files
serve the fixture oracle and the dotnet build.

## Commands

Run from the repository root:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

Everything a task's evidence needs is in the build and test output; no formatter, linter or Unity step is
part of these commands.

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

The committed file ships with the placeholder header
`# snapshot pending generation on build host`; the test fails with an explicit message while that placeholder
is present. Any public seam change additionally reopens the W0 interface gate before dependent modules
compile (`docs/game-core/09-implementation-guide.md`, GC-002).

## Notes

- Language level is C# 9 with nullable enabled, matching Unity's profile. `record`, `init`, file-scoped
  namespaces, raw string literals and global usings are avoided everywhere.
- `Directory.Build.props` sets `TreatWarningsAsErrors`; a warning is a build failure on purpose.
- `dotnet/src/GameCore.ProtocolFixtures` references `System.Text.Json` (netstandard2.1) for fixture JSON.
  A hand-written parser is deliberately not used.
- Fixture JSON is read from the repository tree, not a build output copy, so the committed fixtures are the
  single source of truth. The result document is written to `artifacts/protocol-fixtures/results.json`.
