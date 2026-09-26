# GC-017 release surface — no latch code and no latch cost outside the qualification build

**Status: `NotRun (pending orchestrator build host)` for the two halves that need a toolchain**
(`--json artifacts/faults/release-surface.json` and the release-player inspection). The source-level half and the
falsifiability self-test below ran on the authoring host and are recorded verbatim in §5.

## 1. The requirement, and the defect it closes

The first Linux build demonstrated that a define-less player's generated code returns `false` from the reach
helpers — but the same generated code still contained the empty reach methods, the boundary metadata,
`AssemblyFaultInjection` **and a per-world latch allocation**. "Disabled reaches have no effect" is not the
requirement. The requirement is:

1. **No per-world latch object, array or dictionary** is allocated when the qualification symbol is absent.
2. **No fault boundary metadata** — the name table, the ids, anything derived from `boundaries.json` — is compiled
   into a release assembly.
3. **Reach calls compile to nothing**: the call is absent from the release assembly, not merely inert.

## 2. The mechanism

`GAMECORE_FAULT_INJECTION` is produced by exactly one place:

```json
"versionDefines": [
  { "name": "com.gamecore.fault-qualification", "expression": "1.0.0", "define": "GAMECORE_FAULT_INJECTION" }
]
```

in `Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef`. The package it is keyed on,
`com.gamecore.fault-qualification`, is an empty marker package referenced by **only** the validation project's
manifest (`unity/GameCore.Validation/Packages/manifest.json`), and by no other manifest in the repository. The
earlier revision keyed the define on `com.unity.test-framework`, which Unity packages bring in transitively, so the
symbol could not be absent from a project that merely resolved the Test Framework; the marker package removes that
transitivity by construction.

Every latch source and every latch call site is then inside `#if GAMECORE_FAULT_INJECTION`:

| Where | What the guard removes in a release compilation |
| --- | --- |
| `Runtime/Faults/FaultBoundaries.cs` | the boundary enum, the **name table** (`FaultBoundaryText.Names`), `FaultRecord`, `FaultTrace`, `FaultInjectedException`, `FaultCompilation`, `FaultReach` |
| `Runtime/Faults/AssemblyFaultInjection.cs` | the latch itself, its two armed-boundary arrays, its trace and its counters |
| `AssemblyPublisher` | the `Faults` property, the assignment that shares the world's latch, the validation/acquisition/fence/prewrite-reach helpers, the first-live-write reach, the gate-installation reach, the cleanup reach, the legacy `FailDuringMigration` switch |
| `UnityExecutionDriver` | the structural-playback reach |
| `StagedResourceGate` | the latch field, the 3-argument constructor, the two injected-refusal counters and both reach calls |
| `UnityWorldHost`, `IWorldExecutionContext` | the `Faults` member — **including its `new AssemblyFaultInjection()`**, so a shipping world allocates no latch |

The namespace declaration itself stays *outside* the guard in both fault files. A namespace declaration emits no
metadata, and keeping it means the assembly's `using GameCore.Unity.Runtime.Faults;` directives stay valid in the
release configuration instead of becoming a missing-namespace error.

The apply loop's own latch call is masked differently, because it is the one call that sits in unconditioned code:

```csharp
private void NoteFirstLiveWrite(int before, int after)      // called once per effective write
[System.Diagnostics.Conditional("GAMECORE_FAULT_INJECTION")]
```

`[Conditional]` makes the **call site vanish at compile time** — argument evaluation included — so a shipping build
contains no call at all, and the method is left as an unreferenced empty stub the linker drops. That is the property
the first audit's "empty fault-reach functions" finding was about, and it is now asserted by the checker (§4, check
2b) rather than assumed. The attribute argument is a literal because the symbol's own type is inside the guard and a
`[Conditional]` argument must be a compile-time constant.

## 3. Behaviour is unchanged where the symbol *is* defined

The qualification build is the one all 29 `boundaries.json` cases run in, and nothing about it changed:
`FaultCompilation.IsCompiledIn` is the constant `true` (the type exists only when the latch does, so a test's
assertion of it now states that the qualification symbol is active), and `FaultReach.Reach`/`Refuse` are their plain
implementations instead of being wrapped in a redundant inner `#if`/`#else`. The reach *sequence* is byte-for-byte
what the asserts already expect; the one refactor on the apply path collapses the seven identical legacy
first-live-write sites into the single `[Conditional]` helper with the same firing condition (`before == 0 &&
after > 0`, equivalent to the `writes == 1` form it replaced at the two transfer sites).

## 4. The check

```sh
python3 tools/check_release_fault_free.py                       # all three halves
python3 tools/check_release_fault_free.py --no-build            # source and switch halves only
python3 tools/check_release_fault_free.py --json artifacts/faults/release-surface.json
```

Three independent halves, each of which can fail on its own:

1. **Compiled assembly, both configurations.** `dotnet/src/GameCore.Faults.ReleaseCheck` compiles
   `Runtime/Faults/**` — the whole latch, engine-free on purpose. `-c Release` must produce an assembly containing
   neither the text `Fault` nor any boundary literal nor the trace prefix `boundary=`; `-c Qualification` must
   contain every latch type name and every distinctive boundary literal. **Both** directions are asserted, so the
   check cannot pass by inspecting nothing.
2. **Source, both configurations**, for the four files that only Unity can compile (`AssemblyPublisher`,
   `UnityExecutionDriver`, `StagedResourceGate`, `WorldHost`): a real `#if` evaluator strips the qualified regions
   and the release text must contain no latch type name anywhere in the runtime package. The evaluator refuses any
   directive form it does not implement (`#elif`) instead of mis-stripping, and it strips comments first — prose may
   name a latch type, code may not.
   * **2b. The apply-path mask.** `NoteFirstLiveWrite` must still be called from unconditioned code, must still
   carry `[Conditional("GAMECORE_FAULT_INJECTION")]`, must be `void`, and its latch call must exist only in the
   qualification configuration. This is the assertion that "reach calls compile to nothing" on the apply path.
3. **The switch.** The symbol must be driven by the marker package, that package must be in the validation manifest,
   and no other manifest in the repository may reference it. Plus: every assembly that names a latch type is either a
   package test (`UNITY_INCLUDE_TESTS`) or part of the validation project itself.

And for the built artifact:

```sh
python3 tools/check_player_fault_free.py --player <release-player-dir> [--il2cpp <generated-cpp-dir>]
```

It byte-greps the player's `*_Data/Managed/*.dll` and IL2CPP generated C++ for latch type names, boundary literals
and the `boundary=` trace prefix, prints how many files it inspected, and **fails if it inspected nothing** — a
vacuous pass is a failure. The operand must be a release-configuration player (one built from a project whose
manifest omits the marker package); a qualification player legitimately contains the latches and the tool says so.
`tools/run_gc017_gate.sh` runs this half when `RELEASE_PLAYER` is set and otherwise prints an explicit
`NOT RUN (RELEASE_PLAYER unset)` note, so the gate never implies a built release player was inspected when none was
supplied.

## 5. What ran on the authoring host

```sh
python3 tools/check_release_fault_free.py --no-build
# PASS: no runtime source keeps a latch reference once the qualification symbol is undefined
#       the apply path's mask by [System.Diagnostics.Conditional("GAMECORE_FAULT_INJECTION")] is intact (8 call sites)
#       the symbol is driven by com.gamecore.fault-qualification, referenced only by the validation manifest
# other manifests referencing the marker: none
# boundary owners: releaseLatchReferences=0 in all four, qualificationLatchReferences 17/3/1/1
```

**Falsifiability.** Five defect classes were injected into the real sources one at a time; the check failed on every
one of them and returned to PASS when the file was restored byte-for-byte (`git status` clean after each):

| Injected defect | Result |
| --- | --- |
| an unguarded `Faults` reference in `AssemblyPublisher.Publish` | FAIL — "still references Faults" |
| an unguarded `InjectionReleaseRefusalCount` in `StagedResourceGate.TryAcquire` | FAIL — "still references InjectionReleaseRefusalCount" |
| an unguarded `FaultReach.Refuse(Faults, FaultBoundary.Fence, …)` in `Publish` | FAIL — "still references FaultBoundary, FaultReach, Faults" |
| the `[Conditional]` attribute deleted from `NoteFirstLiveWrite` | FAIL — "the apply path's latch call is not masked by …" |
| the guard deleted from `FaultBoundaries.cs` | FAIL — "release …/FaultBoundaries.cs still references …" |

The player tool was self-tested the same way against a synthetic player directory: an assembly containing
`AssemblyFaultInjection` fails with the marker and its count named, an assembly containing nothing passes, and a
directory with no assembly at all fails as "inspected nothing" rather than passing vacuously.

Nothing above is a build or a test of the Unity assemblies. The compiled-assembly half needs the .NET SDK and the
release-player half needs a release-configuration player; both are for the build host.
