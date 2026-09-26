# GC-017 fault traces — the `FaultRecord.ToLine()` line format

`artifacts/faults/` archives GC-017's deterministic fault evidence. This file is the normative description of the
line format those traces use, and of the exact path the player probe takes to put a trace line into an artifact.

Normative sources: `docs/game-core/08-validation-and-performance.md` **TEST-016** (the injection matrix, and its
row-2 requirement that a failure "includes operation ID and provenance"), **P-004** (stable identities),
**P-029**/**P-030**/**P-031** (prewrite refusal, one atomic publication, postwrite fail-stop), **P-047**/**P-048**
(in-flight lifetime, teardown and quarantine), **P-052** (stable diagnostics with identity and provenance).

Implementation: `Packages/com.gamecore.unity.runtime/Runtime/Faults/FaultBoundaries.cs`.

---

## 1. One record

```csharp
public readonly struct FaultRecord
{
    public readonly int Ordinal;
    public readonly FaultBoundary Boundary;
    public readonly OperationId Operation;
    public readonly ContentHash PlanHash;
    public readonly bool Injected;
    public readonly string Detail;

    public string ToLine();
    public override string ToString();   // == ToLine()
}
```

`ToLine()` is the canonical one-line form, and it is the only form `artifacts/faults/` archives:

```
boundary=<name> op=<id> plan=<hex> fired=<0|1> detail=<text>
```

| Field | Source | Shape |
| --- | --- | --- |
| `boundary=<name>` | `FaultBoundaryText.Of(Boundary)` | one of `validation`, `acquisition`, `fence`, `migration`, `first-live-write`, `structural-playback`, `gate-installation`, `cleanup`, `checkpoint-capture-copy`, `checkpoint-publication`, `restore-reference-repair`, `restore-apply`, `recovery-publication` — `FaultBoundaryText.Names`, in `FaultBoundary` order. The last five were appended by GC-027; the archived GC-017 probe results exercise only the original eight. |
| `op=<id>` | `OperationId.ToString()` | `OperationId(WorldId(<32-hex session>), <32-hex issuer>, <issuer sequence>)` — the operation that reached the boundary |
| `plan=<hex>` | `ContentHash.ToHex()` | exactly 64 lowercase hex characters (the 32-byte SHA-256 of the plan), **not** the `sha256:`-prefixed `ToString()` form |
| `fired=<0\|1>` | `Injected` | `1` when the reach was an armed injection, `0` when the boundary was merely reached on the production path — a trace is a sequence of reaches, not only of faults |
| `detail=<text>` | injector/caller | free text, single-line: `FaultRecord` stores `detail ?? string.Empty`, and every production call site passes a message with no newline |

Field order is fixed and the separators are single spaces; `=` appears literally in `fired=` and in the `detail=`
value, so parse left to right with the first ` op=` / ` plan=` / ` fired=` / ` detail=` markers rather than by
splitting on `=`.

A worked line (shape only — every identity below is a placeholder, not a recorded value):

```
boundary=validation op=OperationId(WorldId(<32-hex>), <32-hex>, 7) plan=<64-hex> fired=1 detail=injected validation fault: the plan is refused before any live write
```

## 2. The trace

```csharp
public sealed class FaultTrace
{
    public int Count { get; }                 // every reach, injected or not
    public int InjectedCount { get; }         // the subset with fired=1
    public IReadOnlyList<FaultRecord> Records { get; }
    public FaultRecord Add(FaultBoundary, OperationId, ContentHash, bool injected, string detail);
    public void Clear();
    public IReadOnlyList<FaultRecord> Of(FaultBoundary boundary);
    public string Describe();                 // LF-joined ToLine()s, "<empty>" when nothing was reached
}
```

* `Add` returns the record it appended. `Ordinal` is the append index (`records.Count` at the time of the call), so
  ordinals are dense, zero-based and equal to the order of `Records`.
* `Describe()` is the LF-joined `ToLine()` sequence in that order; a trace that reached nothing is exactly the
  string `<empty>`. Two worlds never share a trace, which is what keeps their sequences apart (P-004).
* `Clear()` drops the records and leaves the armed set alone: **arming belongs to the latch, the trace belongs to
  the evidence.**
* `Of(boundary)` filters by `FaultBoundary` and preserves order; `Count`/`InjectedCount` are over the whole trace.

## 3. The latch, and when a line can exist at all

One latch per world. `UnityWorldHost.Faults` is that instance; `AssemblyPublisher.Faults` returns the same object and
the staged-resource gate takes it as a constructor argument (`new StagedResourceGate(ceiling, category, world.Faults)`),
so an arm placed by a test is the boundary the real apply path reaches:

| Latch member | Meaning |
| --- | --- |
| `Arm(FaultBoundary)` / `Disarm(...)` / `DisarmAll()` / `IsArmed(...)` | the armed set (chained) |
| `ReachCountOf(FaultBoundary)` | reaches for one boundary — the `boundaryReaches=` number in the probe detail |
| `TryReach(boundary, operation, planHash, detail)` | records a reach and throws `FaultInjectedException` when armed |
| `TryRefuse(boundary, detail)` | records a reach and answers `true` when armed — the refusal-value shape |
| `Trace` | the `FaultTrace` above |
| `ReachCount` / `InjectedCount` / `ArmedBoundaryCount` | totals across the whole latch |
| `IsCompiledIn` | `FaultCompilation.IsCompiledIn` |

Both reach helpers are unconditional at the call site so the boundary stays visible in the code that owns it:

* `FaultReach.Reach(...)` raises `FaultInjectedException` — the publisher's refusal and fault paths, which classify
  the failure into their own `AssemblyPublicationReport`;
* `FaultReach.Refuse(...)` answers a boolean — `StagedResourceGate.TryAcquire`/`Release`, whose contract returns
  `DiagnosticCode.ResourceUnavailable` instead of throwing.

Without `GAMECORE_FAULT_INJECTION` both helpers return immediately (`false`), no record is appended, and
`FaultCompilation.IsCompiledIn` is `false`. The symbol is declared by `GameCore.Unity.Runtime.asmdef`'s
`versionDefines` entry on `com.unity.test-framework`, so every fault test and the probe's first observation assert
`IsCompiledIn` before anything else: a missing symbol must fail loudly rather than produce a plausible empty trace.

## 4. How the probe archives a line

The archive surface is `FaultScenarioStep.Detail`: one `string` per named observation, produced by the scenario the
EditMode assembly `GameCore.Faults.Tests` and the player probe share. `ProbeFaults` copies every step's `Detail`
**verbatim** into `probes[].detail` of the probe result, and `ProbeReport.ToJson()` escapes a line feed as `\n`
(and `"`, `\`, tab, and control characters) so one detail stays one JSON string and the result document stays
diffable and greppable.

Per fault-arming observation the scenario's detail carries **one verbatim `FaultRecord.ToLine()` of the injected
record**, prefixed so it can be found without parsing prose:

| Fragment in `probes[].detail` | Meaning |
| --- | --- |
| `traceRecord=boundary=<name> op=<id> plan=<hex> fired=1 detail=<text>` | the exact line `FaultRecord.ToLine()` produced for the injected reach — TEST-016 row 2's "operation ID and provenance", verbatim |
| `boundaryReaches=<n>` | `AssemblyFaultInjection.ReachCountOf(boundary)` at that observation |
| `injected=<n>` | `AssemblyFaultInjection.Trace.InjectedCount` at that observation |
| `armed=True` / `armed=False` | whether the observation asserted the boundary was armed before the reach |

There is deliberately no whole-`FaultTrace.Describe()` blob in a detail: the trace is a sequence across many
boundaries and many steps, the observation table is the sequence, and duplicating the whole trace into every step
would make the artifact quadratic. `Describe()` remains the in-process form the EditMode fixture prints when an
observation fails.

Worked shape of one archived detail (placeholders, LF shown escaped as it appears in the JSON):

```
gc017-validation-fault-rejects-and-keeps-the-old-assembly: Pass (... boundaryReaches=1; injected=1; armed=True; traceRecord=boundary=validation op=OperationId(WorldId(<32-hex>), <32-hex>, 7) plan=<64-hex> fired=1 detail=injected validation fault: ...; structuralWrites=0; crossedLiveWriteBoundary=False ...)
```

Every string that reaches the JSON goes through `ProbeReport`'s escaper, so `\n`-separated `Describe()` output, if a
detail ever carried it, would appear as a single line with literal `\n` sequences. The gate script asserts the
fragments above (`traceRecord=boundary=validation`, `fired=1`, `boundaryReaches=`, `injected=`) with plain `grep`,
so a run that recorded the observation names without reaching the boundaries cannot satisfy them.

## 5. Reading the artifacts

| Artifact | Contents |
| --- | --- |
| `toolchain/probe-gc017-faults.json` | the `-probeFaults` result: `probes[].name` is the qualified observation (`narrative/…`, `cards/…`, and `fixture:`-prefixed for the fixture-catalog run) and `probes[].detail` carries the fragments above |
| `toolchain/player-gc017-faults.log` | the player's own stdout/stderr for run 1; `runs 2..5` are retained as `.run<N>` siblings |
| `unity/editmode-results.xml` | the EditMode suite, including `GameCore.Faults.Tests` (both digest literals recomputed from `FaultScenario.QualifiedNames(label)`) and the package fault fixtures |
| `boundaries.json` | the TEST-016 matrix: each row mapped to its `FaultBoundary`, its case and its evidence artifact |
| `README.md` | the exact commands and what each artifact contains |

Reproduce with `tools/run_gc017_gate.sh` (whole gate) or `tools/unity/run_gc017_faults_probe.sh` (the player probe
alone). Every artifact listed above is produced by those commands; **nothing in this directory is a recorded run on
this revision** — see `README.md`.
