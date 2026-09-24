# GameCore.ProtocolFixtures — independent pure oracle (GC-002)

Test-only, Unity-free, independent protocol oracle. It references **only** `GameCore.ReferenceSeams` and
`System.Text.Json`; it imports no engine and no gameplay assembly, and an executed case
(`assembly-independence.json`) asserts that boundary rather than merely claiming it.

## What is here

| Path | Contents |
|---|---|
| `Oracle/CanonicalOrder.cs` | Canonical big-endian id bytes, hex form, two independent comparison paths and order-independent sorting |
| `Oracle/IdentityOracle.cs` | Dereference validation (world, slot, generation, liveness, category, activation epoch), world-incarnation separation, collision detection |
| `Oracle/CounterOracle.cs` | Counter/revision/epoch/step advancement, overflow rejection, publication boundary invariants |
| `Oracle/VersionOracle.cs` | Protocol version interpretation and required-feature gate (P-055) |
| `Oracle/EnvelopeOracle.cs` | Envelope probes: checksum verification/corruption, per-wire-type round trip, canonical NaN, required-feature gate, and hand-assembled documents for reader-side limit checks |
| `Fixtures/FixtureModel.cs` | Case/expectation/result model |
| `Fixtures/FixtureLoader.cs` | Strict loader for canonical case JSON |
| `Fixtures/FixtureRunner.cs` | Executes one case per kind and compares the observed verdict with the case expectation |
| `Fixtures/ResultDocument.cs` | Writes/reads `artifacts/protocol-fixtures/results.json` |
| `Data/cases/*.json` | The canonical fixture data (valid **and** invalid cases with expected outcomes): identity bytes, ordering, handles, counters/publication, version support, assembly independence, canonical hex parsing, diagnostic-code literals and the serialization envelope |
| `Data/result-schema.json` | Case-file schema, result-document shape, per-kind parameter tables, outcome vocabulary and source-of-truth rules |
| `RepoLayout.cs` | Repository-root discovery so fixture data and evidence always come from the committed tree |

## Independence rules

1. `docs/game-core/00-core-protocols.md` is the only normative source. Every case names the requirement ids
   it exercises; the expected outcome is derived from that requirement, not from the seam.
2. The oracle re-implements the semantics instead of delegating to the seam (`CanonicalOrder` does not call
   `Id128Codec`, `CounterOracle` does not call `Counter.TryIncrement`, `VersionOracle` is the only version
   interpreter). A seam defect is therefore observable as a disagreement — and the W0 tests cross-check both
   paths.
3. A case's `expected.code` must be a required diagnostic literal from 00 §9 or a detector code documented in
   `Data/result-schema.json` (`x-kinds`). A typo fails the suite instead of passing silently.
   The `envelopeCodec` kind is the one deliberate exception to rule 2: it drives the seam's own envelope codec,
   because the envelope *is* a codec and the fixtures exist to pin down its declared read/write rules. The
   byte-level documents the writer would refuse are hand-assembled so the reader's limit checks are reachable.
4. Fixture data is never edited to make a failing oracle pass. A mismatch means the fixture or the
   implementation is wrong, and the normative source is corrected first.

## Case format (one case)

```json
{
  "caseId": "handle-stale-generation-rejected",
  "title": "destroying and recreating storage invalidates the old generation",
  "requirementIds": ["P-005"],
  "testIds": ["TEST-002"],
  "kind": "handleValidation",
  "expected": { "outcome": "invalid", "code": "StaleGeneration" },
  "parameters": { }
}
```

`outcome` is `valid` (the input satisfies the protocol) or `invalid` (the oracle must refuse it, with the
declared `code`). Kind-specific parameters, the full list of refusal codes and the schema are in
`Data/result-schema.json`.

## Result format

The test project writes `artifacts/protocol-fixtures/results.json` through `ResultDocument`:

```json
{
  "schemaVersion": 1,
  "status": "Executed",
  "generatedBy": "dotnet/tests/GameCore.ProtocolFixtures.Tests",
  "cases": [
    {
      "caseId": "handle-stale-generation-rejected",
      "requirementIds": ["P-005"],
      "testId": "TEST-002",
      "outcome": "Pass",
      "detail": "expected invalid(StaleGeneration); observed StaleGeneration (dereference refused)"
    }
  ],
  "summary": { "caseCount": 1, "pass": 1, "fail": 0, "notRun": 0, "blocked": 0 }
}
```

* `Pass` — the oracle agreed with the case expectation.
* `Fail` — the oracle disagreed, including a missing or different rejection code.
* `NotRun` — the case was not executed on the reporting host; it is not passing evidence.
* `Blocked` — the case could not be evaluated because its data is malformed. A delivered run must contain
  neither `NotRun` nor `Blocked`; the suite asserts this.

## Evidence rule

The Linux build host executed all 58 cases in Round 2; `artifacts/protocol-fixtures/results.json` records
the real outcomes. See `artifacts/gc-002/BUILD_REPORT.md`, Round 2, for commands, suite results and scope limits.
The result document is produced only by an actual run, never by copying the example above.
