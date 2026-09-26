# GameCore.Recovery — GC-027's recovery qualification fixtures

Committed data and a pure reader for the recovery path GC-027 closes: the faulted checkpoint/outbox recovery of
`06-contracts-and-data-model.md` s7 and 00 P-049.

| Path | Contents |
| --- | --- |
| `Data/recovery-matrix.json` | The **permitted-outcome matrix**: one case per injection point, in `RecoveryFaultPoints.All` order, naming the mechanism, the production boundary names, the permitted observable result, the data-loss class and the requirement/test ids that own it (P-049, TEST-016). |
| `Data/checkpoint-store-versions.json` | The **store-envelope version cases**: which `major.minor` a stored checkpoint declares, whether `CheckpointStoreFormat.IsSupported` accepts it, and the exact code and detail a read of such an envelope reports (P-054, P-055). |
| `Runtime/RecoveryMatrixFixture.cs` | The fixture model, the strict reader (`TryRead`/`Read`, `TryReadFile`/`ReadFile`), the fixture vocabulary and `RecoveryFixturePaths`. Engine-free and Unity-free: it holds data and refusals, no assertions. |
| `Runtime/GameCore.Recovery.Fixtures.asmdef` | The engine-free fixture assembly (`noEngineReferences`, no references, not auto-referenced). |
| `Tests/` | The NUnit suite that drives the **production** recovery types with these documents: the checkpoint store's publish/read/refusal contract, the matrix and version cases, the retry bound and failure classification, the transcript, and the outbox consistency report. |

## Three consumers

| Consumer | How |
| --- | --- |
| Plain dotnet | `dotnet/src/GameCore.Recovery.Fixtures` (netstandard2.1) compiles `Runtime/**`; `dotnet/tests/GameCore.Recovery.Fixtures.Tests` compiles `Tests/**` and runs them with NUnit 3 against `GameCore.Execution` (where the production recovery types are compiled) and `GameCore.Contracts`. |
| Unity EditMode | This folder is a local Unity package (`package.json` + the two asmdefs), consumed the way `com.gamecore.replay` is: a `file:` dependency plus a `testables` entry in the consuming project's manifest (`unity/GameCore.Validation/Packages/manifest.json`). `Tests/` is Editor-only and constrained on `UNITY_INCLUDE_TESTS`; in Unity they compile against `GameCore.Unity.Runtime`, which is where `GameCore.Execution.Recovery` and `GameCore.Execution.Delivery` live for the engine build. |
| Player | **Nothing.** No probe or player assembly references this package: the documents are qualification data, and the assemblies that run in a stripped player are GC-027's fault-injection scenarios, which live in the validation project. |

The `file:` dependency and `testables` entry above are part of the consuming project's manifest, which this package's
change set does not modify, so the plain-dotnet project is the executable consumer until that wiring lands.

## The reader refuses, it never guesses

`RecoveryMatrixDocument.TryRead` and `StoreVersionDocument.TryRead` return `false` and one reason for every defect
they can see, and `Read` turns the same refusal into a `RecoveryFixtureFormatException`. A document is refused when

- its root is not a JSON object, or it is not valid JSON at all;
- its `format` id is not the literal this package owns (`gamecore.checkpoint-fixtures/recovery-matrix/1`,
  `gamecore.checkpoint-fixtures/store-versions/1`);
- its `cases` array is missing, empty, or holds a non-object;
- a case repeats an `id` already seen;
- a required property is missing, or has another JSON kind than the one documented below;
- `mechanism`, `permittedOutcome`, `dataLossClass` or `expectedCode` names something outside
  `RecoveryFixtureVocabulary`, or a `boundaryNames` entry names a boundary no production table declares;
- a `requirementIds`, `testIds` or `boundaryNames` array is empty, or one of its entries is empty text.

No property is ever filled with a default, and a refused read leaves no partially built document behind (P-054).

`RecoveryFixtureVocabulary` carries the member names as **text** because this assembly is compiled by Unity with
`"references": []` and therefore cannot see the production enums at all. It holds three boundary-name lists - the
thirteen `FaultBoundaryText.Names` values a latch may name, the one `store-read` name a `StoreRead` point names, and
the six `DeliveryBoundaries.All` values a hook may name - plus their union, which is the set the reader validates
`boundaryNames` against. The latch list is a copy rather than a reference on purpose: `FaultBoundaryText` lives in
`GameCore.Unity.Runtime.Faults`, which is compiled only when the fault-injection marker package is present
(`GAMECORE_FAULT_INJECTION`) and is absent from the plain-dotnet build entirely, so no single source is visible from
both halves. `RecoveryMatrixFixtureTests` asserts what each half can see: that the delivery list is exactly
`DeliveryBoundaries.All`, that every latch name a production recovery point declares is in the latch list, that the
union is the set of names `RecoveryFaultPoints.BoundaryNames()` plus `DeliveryBoundaries.All` carries, and - in a
`#if GAMECORE_FAULT_INJECTION` case that compiles where the type exists - that the latch list equals
`FaultBoundaryText.Names` exactly. A renamed or added production member therefore fails the suite instead of silently
outdating a fixture.

## Canonical JSON form

Both documents are committed in the canonical form `tests/GameCore.CheckpointFixtures/README.md` records for the
GC-018 fixtures, and `RecoveryFixtureDataTests` asserts it from the committed bytes:

- UTF-8 without a byte-order mark; LF line endings; exactly one trailing newline; no blank line.
- Two spaces per nesting level, one value per line, no trailing whitespace, no tab.
- No comments, no trailing commas, no duplicate property names.
- Numbers that name a format version or a record count are unsigned integers.

`recovery-matrix.json`:

```json
{
  "format": "gamecore.checkpoint-fixtures/recovery-matrix/1",
  "cases": [ /* case objects, see below */ ]
}
```

Case property order is exactly this order, and every property is required:

| Property | Type | Meaning |
| --- | --- | --- |
| `id` | string | The injection point's stable id; must equal a `RecoveryFaultPoints` id and unique in the file |
| `title` | string | One sentence stating the behaviour the case pins |
| `requirementIds` | array of string | Non-empty; protocol requirements the case exercises |
| `testIds` | array of string | Non-empty; traceability tests that own the case (`TEST-002`, `TEST-010`, `TEST-014`, `TEST-016`, `TEST-017`) |
| `mechanism` | string | `Latch`, `DeliveryHook` or `StoreRead` |
| `boundaryNames` | array of string | The `RecoveryFaultPoints` boundary names the point covers: one for a store read and for every latch point except `restore-postwrite-apply`, which covers two (`restore-apply` and `recovery-publication`, the two reaches of the postwrite boundary), and the `DeliveryBoundaries` pair either side of the delivery step for a hook. Each name must belong to its mechanism's list (see `RecoveryFixtureVocabulary`) |
| `permittedOutcome` | string | A `RecoveryPermittedOutcome` member name |
| `dataLossClass` | string | A `RecoveryDataLossClass` member name |
| `statement` | string | One sentence stating what must be observable after the fault fired |

The suite asserts the case ids are exactly `RecoveryFaultPoints.All`'s ids in the same order, that each case's
mechanism, boundary names, permitted outcome and data-loss class equal that table's, that every boundary name belongs
to the list its mechanism may declare, that exactly one latch point covers two names (the postwrite-apply boundary),
and that the concatenated boundary names equal `RecoveryFaultPoints.BoundaryNames()` position by position.

`checkpoint-store-versions.json`:

```json
{
  "format": "gamecore.checkpoint-fixtures/store-versions/1",
  "cases": [ /* case objects, see below */ ]
}
```

| Property | Type | Meaning |
| --- | --- | --- |
| `id` | string | Stable case id, unique in the file |
| `title` | string | One sentence stating the version interpretation the case pins |
| `requirementIds` | array of string | Non-empty; protocol requirements the case exercises (P-054, P-055) |
| `testIds` | array of string | Non-empty; traceability tests that own the case |
| `major` | int 0..255 | Format major the stored envelope declares |
| `minor` | int 0..255 | Format minor the stored envelope declares |
| `expectedSupported` | bool | What `CheckpointStoreFormat.IsSupported(major, minor)` must answer |
| `expectedCode` | string | A `DiagnosticCode` member name a read of such an envelope reports; `None` means readable |
| `expectedDetailContains` | string | Substring the refusal detail must carry, so two branches that report the same code cannot be confused (P-052). Empty for the supported case, which has no refusal detail |

The suite asserts every case's `expectedSupported` against `CheckpointStoreFormat.IsSupported`, that exactly one case
is the version this build implements (`1.0`), that a future minor (`1.1`, and the highest expressible minor), a future
major (`2.0`, `2.1`, the highest expressible major) and major zero are each covered, and that the cases name distinct
refusal branches. It then drives each case through a real envelope: the declared `major.minor` is written into an
envelope the production store framed itself, and the read must answer the case's own `expectedCode` and
`expectedDetailContains` — so a case is not a claim about `IsSupported` alone.

The other refusal branches the checkpoint envelope documents (a truncated envelope, a foreign magic, a length that
disagrees with the file, a document checksum and an envelope checksum that do not verify) are not version data and
therefore are not cases here: `CheckpointStoreTests` builds each of them from a real envelope and asserts the code and
the field the detail names.

## What is deliberately not asserted

No document hash, no transcript digest and no envelope byte string is committed as a literal. GC-027's acceptance is
that a verified checkpoint produces a new session with the delivery cursor intact while the faulted world never
resumes, and a remembered digest would assert only that the code did not change — and would need re-recording on
every unrelated revision. What is pinned is the *agreement*: the matrix against `RecoveryFaultPoints`, the vocabulary
against the production enums, the version cases against `CheckpointStoreFormat`, and the committed bytes against the
canonical form.

## Running the tests

Fixture files are located through `RecoveryFixturePaths` (an upward search for `docs/game-core/traceability.json`,
overridable with `GAMECORE_REPO_ROOT`), so the suite must be started from a checkout of this repository, not from a
copied build output directory.

```sh
dotnet test dotnet/tests/GameCore.Recovery.Fixtures.Tests/GameCore.Recovery.Fixtures.Tests.csproj \
  -c Release --logger "trx;LogFileName=gc-027-recovery.trx"
```

As part of the whole solution, filtered to this suite:

```sh
dotnet test dotnet/GameCore.sln -c Release --logger trx \
  --filter FullyQualifiedName~GameCore.Recovery.Fixtures.Tests
```

The same `Tests/**` files run as Unity EditMode tests once the consuming project lists `com.gamecore.recovery` in its
`dependencies` and `testables`; the Unity-side run is that project's own EditMode run, which belongs to the project
rather than to this package.
