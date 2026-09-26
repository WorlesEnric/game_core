# GC-024 normalized conformance traces

One file per 07 before/after table, written by the run that observed it:

| File | Table | 07 anchor |
|---|---|---|
| `cards.txt` | card market | `07-reference-compositions.md` s2.4, plus the section's prose rows at `07:51`, `07:108`, `07:110` |
| `narrative.txt` | chapter quest | s3.3 (plus `07:181`'s dormant-gate policy) |
| `traversal.txt` | traversal challenge | s4.3 (plus `07:247`'s numeric sequence, REF-A01) |
| `cross.txt` | cross-family combination | s5 (`07:267`–`07:276`) |

**These files are outputs, not inputs.** Nothing here has been executed on the authoring host (no Unity, no .NET SDK),
so no trace is committed: a trace written without a run would be a fabricated observation, which is exactly what this
fixture exists to prevent. The files appear when one of the following writes them:

* `tools/unity/run_conformance_probe.sh` — the IL2CPP player mode `-probeConformance`, which writes each table's
  document to `<ARTIFACTS>/traces/<table>.txt` (the harness diffs them against the copies committed here), or
* the EditMode suite `GameCore.Conformance.Tests`, which holds each document in
  `ConformanceTableResult.Document` and asserts it round-trips through `ConformanceTrace.TryParse`.

## Format

`gamecore.reference-conformance-trace/1`:

```text
format=gamecore.reference-conformance-trace/1
label=<the genre label the run carried>
tables=<n>
rows=<n>
facts=<n>
<table>|<row>|<before|after>|<field>=<value>
...
digest=<sha256 over the canonical body>
```

The body is sorted by `(table, row, phase, field)` regardless of the order the run recorded its facts, every value is
a canonical token (`none`, a decimal integer, `(x,y,z)`, `true`/`false`, a stable catalog name, `{a,b}`), and the
digest is re-derived by the reader — so a tampered document is refused rather than read. Two runs that observe the
same facts write the same bytes; two runs that disagree write different digests, which is what
`tools/unity/run_conformance_probe.sh` compares across its `PROBE_RUNS` repetitions.

## What a trace is compared against

The **transcribed tables** in `tests/GameCore.ReferenceConformance/Runtime/ReferenceTables.cs` — one row per 07 row,
each carrying the 07 anchor it came from — through `ConformanceOracle.CompareScript`. A row the run never reached is a
failure, a field the run never read is a failure, and a 07 row whose column says the operation is refused must have
been refused. The row-by-row expectations are therefore in the repository as data; the trace is the evidence that a
real world met them.
