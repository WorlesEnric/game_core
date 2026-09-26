# GC-027 compile-risk audit — findings and fixes

**What this is.** A read-only declaration audit of the new GC-027 C# files, run on the authoring host (no Unity, no
.NET SDK, so no compiler). Every `X.Y` access, call, constructor and `using` in the new files was cross-checked
against the declaration it must match, and every finding was reported with the disagreeing declaration's own
`file:line`. Twelve findings came back from that audit and a thirteenth from a second, mechanical pass over the
fixture suites' `using` sets; **all thirteen are fixed and each re-verified against the declaration**. Item 13 was
legal-but-inconsistent rather than an error, and is labelled that way rather than counted as a compile failure. The
fixes are in the commit `GC-027: fix the compile errors an independent declaration audit found`.

**One correction to that audit's own verdict, recorded because it was wrong in the safe direction**: it stated the
change set "does not compile" outright. Reading each finding's own symptom: **ten** are unconditional errors in every
configuration that compiles the file (`CS0103` for the two missing `WorldRecovery` imports, `CS0246` for the
`Gc027Scenario`/`Gc027SourceWorld`/`Gc027NarrativeHost`/`Gc027CardsHost` imports, `CS0200` for the get-only
assignment, `CS1061` for the member that does not exist, and the `FrozenPayload`/`byte[]` mismatch); **one** (the
`Faults` import in `WorldRecovery.cs`) is an error only where `GAMECORE_FAULT_INJECTION` is defined, which is the
Unity validation project and not the shipping build; and **one** (the manifest registration) is an omission rather
than a compile error — the package simply would never have run. The distinction does not change the work, because all
twelve were fixed, but an evidence file should not overstate the failure it found.

**What this is not.** It is not a compile. It cannot see a delegate conversion, an overload resolution, a generic
inference or an ambiguity the way a compiler can; §3 lists what remains only a compiler's to confirm.

## 1. Findings, and how each was fixed

| # | File | Symptom | Fix |
|---|---|---|---|
| 1 | `Packages/.../Runtime/Recovery/WorldRecovery.cs` | `GameCoreThreading` unresolved (`CS0103`) — declared in `GameCore.Execution` (`Runtime/Pure/Execution/PublicationBoundary.cs`), a namespace the file did not import | added `using GameCore.Execution;` |
| 2 | same | `IRestoreTargetBuilder`, `RestoreOutcome`, `CheckpointRestoreExecutor` unresolved (`CS0246`) — `GameCore.Unity.Runtime.Persistence` | added `using GameCore.Unity.Runtime.Persistence;` |
| 3 | same | `FaultReach`, `FaultBoundary`, `FaultInjectedException` unresolved inside the fault guard — `GameCore.Unity.Runtime.Faults` | added the using **inside** `#if GAMECORE_FAULT_INJECTION`, the same shape `CheckpointRestoreExecutor.cs` uses: a shipping compilation has no such namespace to import |
| 4 | `.../Runtime/Gc027Scenario.cs` | `PipelineDescriptorReport` unresolved — `GameCore.Unity.Runtime.Integration` | added the using |
| 5 | same | `OwnershipStageDescriptor.WorldDefinition` does not exist — the descriptor declares layouts, stages and buffers only (`Runtime/Plans/OwnershipStageDescriptor.cs`) | the definition now comes from the run's own `source.Request.Definition`, which is where `WorldRecoveryRequest` gets it too |
| 6 | same | `hook.CrashAt = string.Empty` on a get-only property (`CS0200`) | `Gc027DeliveryCrashHook.CrashAt` is now settable, documented as the way an observation disarms the hook before driving a redelivery against the same hook |
| 7 | `.../Runtime/Gc027SourceWorld.cs` | `TargetView` returned `LiveTargetIndex.PlannerTargets()` (`IReadOnlyList<TargetDefinition>`) where `DerivationModeSwitchValidator` wants `Func<IReadOnlyList<DerivationTarget>>`; the two types are unrelated | it now builds the derivation view exactly as `Gc018FamilyRestoreBuilder.TargetView` does (`targets.BuildDerivationTargets()`, empty when the view refuses) |
| 8 | same | `IDerivationValueSource`, `DerivationTarget` (`GameCore.Derivation`) and `CheckpointPublication*` (`GameCore.Unity.Runtime.Recovery`) unresolved | added both usings |
| 9 | `.../Runtime/Gc027NarrativeHost.cs` | `NarrativeKeys`, `NarrativePayloadCodec`, `CheckpointSerializerBindings`, `SpawnRecipeCatalog`, `ProbeCatalog`, `W1GateKeys` unresolved | added the five usings |
| 10 | `.../Runtime/Gc027CardsHost.cs` | `DeliveryPayload` returned the codec's `FrozenPayload` where the obligation carrier is `byte[]`; `FrozenPayload` has no conversion | the bytes are copied out once into an array (`FrozenPayload.Length` / `.Bytes`), so nothing is aliased |
| 11 | same | `CheckpointSerializerBindings`, `SpawnRecipeCatalog`, `CardCatalog` unresolved | added the three usings |
| 12 | `tests/GameCore.Recovery/package.json` + `unity/GameCore.Validation/Packages/{manifest,packages-lock}.json` | the new package was absent from the only Unity project manifest, so its EditMode half would never have resolved or run, and the `GAMECORE_FAULT_INJECTION` versionDefine the latch-name assertion needs would never have been defined | registered in the manifest's `dependencies` (beside `com.gamecore.replay`) and `testables`, and in the lock; the release-clone tools now strip it like the other fixture packages |
| 13 | `tests/GameCore.Recovery/Tests/RecoveryMatrixFixtureTests.cs` | found by a **second, mechanical pass** over the fixture suites (type name → declaring namespace → the file's `using` set): the suite named ten types from `GameCore.Recovery.Fixtures` without importing it. C#'s lexical namespace lookup makes this *legal* — the file's namespace is `GameCore.Recovery.Fixtures.Tests`, a child of the types' namespace — so it was not an error, but the sibling suite states the import explicitly and this one now does too | added `using GameCore.Recovery.Fixtures;` for consistency with `RecoveryFixtureDataTests.cs` |

## 2. Independently re-verified after the fixes

| Check | Result |
|---|---|
| `python3 tools/check_game_core_csharp.py` | `checked 555 C# file(s)` → `ok` (braces/parens balanced in every configuration, no forbidden construct) |
| `python3 tools/check_release_fault_free.py --no-build` | `PASS` — including the source guard evaluation for the newly imported `Faults` namespace |
| `python3 tools/unity/prepare_gc017_release_project.py` then `python3 tools/check_release_clone.py` | clone prepared, `VERDICT: clone is clean`: no removed-type reference, no dangling asmdef reference, constructor 14 = 14, 58 files balanced, `Recovery` absent from every wiring point, thirteen kept modes wired, no qualification/replay/recovery dependency, `testables` empty (clone then deleted) |
| every `ARG_NEEDLES` entry matches exactly once | `needles 42, mismatches 0` |
| `bash -n tools/unity/run_recovery_probe.sh` | exit 0 |
| `ast.parse` over the three edited Python tools | exit 0 |
| the two frozen digest literals recomputed from the frozen name table | reproduce exactly; the same algorithm reproduces GC-018's two published literals, validating the method |

## 3. What only a compiler can confirm

Stated so a reviewer knows the boundary of this audit's claim:

* **method-group and delegate conversions** — `CheckpointCodecAdapter`'s serializer binding in the fixtures' code
  path, `Func<WorldId, OperationId>` passed to `Gc027RestoreBuilder`, `Func<WorldId, DurableOutbox?>`,
  `RecoveryTranscript.Add`'s optional parameter, and the `in NarrativeChoice` parameter of
  `NarrativePayloadCodec.EncodeChoice` (a temporary argument is legal for an `in` parameter since C# 7.2, but only a
  compiler settles it);
* **generic inference** — `Array.AsReadOnly(new[] { … })` over `RecoveryFaultPoint` with an inner
  `Array.AsReadOnly(new[] { … })` for the boundary names, and `List<KeyValuePair<string, RecoveryJsonNode>>` in the
  JSON reader's object construction;
* **overload resolution** — `RecoveryTranscript.Add` versus `AddFault`, and `MemoryCheckpointStore.TryPublish`'s
  `out` parameter names;
* **namespace ambiguity** — `Gc027Scenario.cs` imports `GameCore.Unity.Runtime.Faults` and
  `GameCore.Validation.ProbeHost` at once; a name declared in both would be ambiguous, and only a compiler enumerates
  every candidate. The audit found none;
* **nullability diagnostics** — the project has `Nullable enable` with `TreatWarningsAsErrors` for the plain-dotnet
  projects; Unity does not treat warnings as errors, but a genuine `CS86xx` would still be a warning there and an
  error in the dotnet build. The audit reported no possible-null dereference, and the `!` suppressions are all on
  values the surrounding code has just checked (`staging`, `targets`, `seeder`, `plan.Outbox`, `source`).

## 4. The audit's own residual risk statement

The three most likely first-build failures, in the auditor's order: (a) the delegate/method-group conversions listed
in §3, which no reading can settle; (b) a nullability diagnostic promoted to an error in the plain-dotnet build
(Unity tolerates it); (c) a name collision between the two imported namespaces in `Gc027Scenario.cs`. All three fail
loudly and locally at the first build, and none of them is a semantic defect in the recovery design.
