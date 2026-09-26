# GameCore.ReferenceConformance — GC-024's reference-conformance fixtures

Unity-free fixtures that hold **every before/after table of
[`docs/game-core/07-reference-compositions.md`](../../docs/game-core/07-reference-compositions.md)** as data, the
ordered script each table is executed through in a real world, the canonical normalized trace a run records, the
oracle that compares a trace against its table, and the build-time assembly-reference audit.

Assembly `GameCore.ReferenceConformance` (`Runtime/`), Editor test assembly
`GameCore.ReferenceConformance.Tests` (`Tests/`). Both are `noEngineReferences: true`: nothing in this package
touches `UnityEngine` or `Unity.*`, so the same sources compile and run in plain dotnet
(`dotnet/src/GameCore.ReferenceConformance`, `dotnet/tests/GameCore.ReferenceConformance.Tests`), in Unity EditMode,
and on the build host.

## What lives here

| File | Contents |
|---|---|
| `Runtime/ConformanceTable.cs` | `ConformanceExpectation` (Require / Unchanged / Absent / Preserved), `ConformanceRow`, `ConformanceTable`. The shape a 07 table row is transcribed into. |
| `Runtime/ReferenceTables.cs` | The four tables, row by row, each carrying its 07 anchor: cards (`07` s2.4 + the prose rows at `07:51`, `07:108`, `07:110`), narrative (s3.3), traversal (s4.3 + `07:247`), cross-family (s5). |
| `Runtime/ConformanceFields.cs` | The closed canonical field vocabulary every table is observed through (`seat-a.bonus`, `quest-ledger.bridge-permit`, `runner-a.velocity`, `outbox.open`, …). A field a run cannot read is a reported failure, not a silent skip. |
| `Runtime/ConformanceTrace.cs` | The canonical trace document (`gamecore.reference-conformance-trace/1`): fixed header, canonically ordered facts, SHA-256 digest, and a reader that refuses a tampered or malformed document. |
| `Runtime/ConformanceScript.cs` | `ConformanceStep`/`ConformanceStage`/`ConformanceScript` and the stable operation-key vocabulary a genre's family maps to its own declared payloads. |
| `Runtime/ReferenceScripts.cs` | The execution script of each table: one stage per fresh world, with precondition steps that establish each row's `Before` state. |
| `Runtime/ConformanceOracle.cs` | The field-by-field comparison, and `ConformanceVerdict` with its digest. |
| `Runtime/ReferenceProjections.cs` | Every 07 number recomputed **from the rules package that owns it** (`CardSetRules`, `TraversalMotionRules`, `NarrativeFacts`/`NarrativeGateRules`/`NarrativeDialogueRules`, `NarrativeChapters`), so a transcription typo or a moved rule number fails in a pure dotnet run. |
| `Runtime/AssemblyReferenceAudit.cs` | The build-time half of the genre audit: every `.asmdef` and every `dotnet` project classified, every kernel→family reference reported with the clause that forbids it, every kernel source scanned for genre tokens. |
| `Runtime/GenreAuditDocument.cs` | The deterministic writer/reader of `artifacts/gc-024/genre-audit.json`. |
| `Tests/ReferenceConformanceTests.cs` | The pure assertions: fixture self-consistency, trace round-trip and tamper refusal, oracle falsifiability, projection agreement, audit classification. |

## What this package is NOT

It is **not** the conformance run. Executing the tables in actual Unity entities worlds is
`unity/GameCore.Validation`'s `ConformanceScenario` plus the `-probeConformance` IL2CPP player mode; that half needs
a world, a control lane and compiled schedules, and this package has no Unity reference by design (P-001, P-057).
Neither half substitutes for the other.
