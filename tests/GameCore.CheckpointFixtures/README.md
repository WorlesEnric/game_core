# GC-018 versioned checkpoint fixtures

Committed data for two behaviours of the versioned checkpoint format:

- `Data/migration-paths.json` — the **schema-version transition table** for the checkpoint container document:
  which registered directed steps exist, which source/target version pair they are queried with, and what
  `CheckpointMigrationRegistry.Plan` must answer (P-054).
- `Data/queue-policy.json` — the **queued-command policy matrix** for a capture: what
  `CheckpointQueuePolicy.RejectQueued` and `CheckpointQueuePolicy.IncludeQueued` do with 0, 1 and 3 offered
  but unexecuted external commands (P-053, `06-contracts-and-data-model.md` s7).

Both files are read by
`dotnet/tests/GameCore.Contracts.Tests/CheckpointVersionedFixtureTests.cs`, which derives real identities from
the stable names below (`StableNameKeyDerivation.Derive`) and drives the production migration planner and the
production checkpoint header record with this data. The tests are data-driven on purpose: extending the
transition table or the policy matrix is a JSON edit, not a C# edit.

## Files

| File | Subject |
| --- | --- |
| `Data/migration-paths.json` | `MigrationPlanOutcome` transition table of one schema's directed step graph |
| `Data/queue-policy.json` | `CheckpointQueuePolicy` disposition arithmetic and the header counts a capture writes |

## Canonical JSON form

Both files are committed in canonical form, and `CheckpointVersionedFixtureTests` reads them with
`System.Text.Json` default options (`JsonDocument.Parse`), so anything the reader would reject is a defect:

- UTF-8, LF line endings, one trailing newline, no trailing whitespace.
- 2-space indentation, one property per line, no comments, no trailing commas.
- Property names appear in the order documented below and are never duplicated.
- Every string is a canonical stable name (`a-z`, `0-9`, `.`, `_`, `-`, no leading/trailing/repeated dot) or a
  `PascalCase` member name of the enum it is compared with; the test asserts
  `StableNameKeyDerivation.IsCanonicalStableName` for all of them.
- Numbers are unsigned integers where they name a schema version or a record count.

## `migration-paths.json`

```json
{
  "format": "gamecore.checkpoint-fixtures/migration-paths/1",
  "schemaStableName": "gamecore.checkpoint.schema.document",
  "cases": [ /* case objects, see below */ ]
}
```

| Property | Type | Meaning |
| --- | --- | --- |
| `format` | string | Fixture format id; must equal `gamecore.checkpoint-fixtures/migration-paths/1` |
| `schemaStableName` | string | Stable name of the subject schema. Must be `gamecore.checkpoint.schema.document`, i.e. `CheckpointFormat.DocumentSchemaStableName`: the test derives the schema id and requires it to equal `CheckpointFormat.DocumentSchema.Id` |
| `cases` | array | Transition cases; non-empty, at least one per documented outcome (see Coverage) |

### Case object

Property order is exactly the order in the table; `toSchemaStableName` appears between `testIds` and `steps` when
present.

| Property | Type | Meaning |
| --- | --- | --- |
| `id` | string | Stable case id, unique in the file |
| `title` | string | One sentence stating the behaviour the case pins |
| `requirementIds` | array of string | Non-empty; protocol requirements the case exercises (P-054) |
| `testIds` | array of string | Non-empty; traceability test ids that own the case (`TEST-001`, `TEST-017`, `TEST-020`, `TEST-022`) |
| `toSchemaStableName` | string, optional | Destination schema stable name. Absent means the destination is the subject schema. Present (and different from the subject) makes the request cross-schema, which `Plan` refuses before it looks at versions — even when `fromVersion == toVersion` |
| `steps` | array of step objects | The registered directed steps. Empty means nothing is registered for the schema |
| `fromVersion` | uint | Version the document declares |
| `toVersion` | uint | Version the catalog declares |
| `expectedOutcome` | string | `Current`, `Unique`, `Ambiguous`, `Unreachable` or `UnknownSchema` |
| `expectedCode` | string | Expected `MigrationPlan.Code`: `None`, `OwnershipConflict`, `MigrationRequired` or `UnsupportedVersion` |
| `expectedPathCount` | int | Expected `MigrationPlan.PathCount`: `1` for `Unique`, the exact chain count for `Ambiguous` (2 in the committed case), `0` for every other outcome |
| `expectedStepFromVersions` | array of uint | The ordered chain's `steps[i].From.Version`; empty for every outcome other than `Unique` |
| `expectedDetailContains` | string | Substring of `MigrationPlan.Detail`. It pins which refusal branch produced the outcome, so two branches that share an outcome cannot be confused (P-052: the detail is the actionable reason) |

### Step object

Property order: `fromVersion`, `toVersion`, `keyStableName`.

| Property | Type | Meaning |
| --- | --- | --- |
| `fromVersion` | uint | Version the step reads |
| `toVersion` | uint | Version the step writes; must be greater than `fromVersion` (the graph is forward-only) |
| `keyStableName` | string | Canonical stable name of the step's registration key. The test derives its `FactoryKey` with `StableNameKeyDerivation.Derive` at key version 1; duplicate names inside one case are a registration defect |

### Coverage the test enforces

The committed cases must contain at least one `Current`, one `Unique`, one `Ambiguous`, one `Unreachable`, one
`UnknownSchema` and one cross-schema case, and exactly one case whose version gap exceeds
`CheckpointMigrationRegistry.MaxChainLength` (which must expect `Unreachable`, the hard bound of P-022/P-054).
Every case is also checked for the outcome/`IsRunnable` rule: only `Current` and `Unique` are runnable.

## `queue-policy.json`

```json
{
  "format": "gamecore.checkpoint-fixtures/queue-policy/1",
  "cases": [ /* case objects, see below */ ]
}
```

| Property | Type | Meaning |
| --- | --- | --- |
| `format` | string | Fixture format id; must equal `gamecore.checkpoint-fixtures/queue-policy/1` |
| `cases` | array | Policy cases; must cover both policies at 0, 1 and 3 offered commands |

### Case object

Property order is exactly the order in the table.

| Property | Type | Meaning |
| --- | --- | --- |
| `id` | string | Stable case id, unique in the file |
| `title` | string | One sentence stating the behaviour the case pins |
| `requirementIds` | array of string | Non-empty; protocol requirements the case exercises (P-053) |
| `testIds` | array of string | Non-empty; traceability test ids that own the case (`TEST-017`, `TEST-022`) |
| `offeredCommands` | int | Commands found queued and unexecuted at the committed boundary |
| `policy` | string | `RejectQueued` or `IncludeQueued`, naming a `CheckpointQueuePolicy` member |
| `expectedIncluded` | int | Commands written into the document as `Command` records: `offeredCommands` for `IncludeQueued`, `0` for `RejectQueued` |
| `expectedRejected` | int | Commands explicitly cancelled and counted: `offeredCommands` for `RejectQueued`, `0` for `IncludeQueued` |
| `expectedHeaderCommandCount` | int | The header's command count, i.e. the number of `Command` records framed in the document; equal to `expectedIncluded` |

The test asserts the policy rule, the accounting invariant `expectedIncluded + expectedRejected ==
offeredCommands` (the "never ambiguously omitted" rule of P-053), the header's policy field, its command count and
its rejected count, and the `CheckpointCounts` predicate the document reader applies. The end-to-end capture that
fills those header fields (`QueueDisposition`, `CheckpointCaptureRequest` in
`Packages/com.gamecore.unity.runtime/Runtime/Pure/Persistence/CheckpointCapture.cs`) lives in
`GameCore.Execution`, which the contract test project does not reference, so the capture path itself is proven by
the GC-018 Unity scenario and the execution-side capture tests.

## Running the tests that consume these fixtures

On a Linux build host with the .NET SDK installed:

```bash
dotnet test dotnet/tests/GameCore.Contracts.Tests/GameCore.Contracts.Tests.csproj \
  --filter FullyQualifiedName~CheckpointVersionedFixtureTests
```

Or as part of the whole solution, which is the gate command for this change set:

```bash
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-018/trx \
  --filter FullyQualifiedName~CheckpointVersionedFixtureTests
```

The fixture files are located through `GameCore.ProtocolFixtures.RepoLayout.FindRoot()` (the same locator every
other committed-data test uses: an upward search for `docs/game-core/traceability.json`, overridable with
`GAMECORE_REPO_ROOT`), so the tests must be started from a checkout of this repository, not from a copied build
output directory.
