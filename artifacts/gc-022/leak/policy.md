# GC-022 resource policy — native allocations observed at shutdown

Status: **Passed on the Linux build host.** Full-stack native leak detection (`UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2`) attributed the initial EditMode shutdown's 81 allocations in 49 callstack groups: all were GameCore-owned, zero unattributed. They came from eagerly allocating `NativeDependencyTable` slots even when no producer ever stored a fence. `NativeDependencyTable` now allocates only at the first `Store`, and the rerun of the full EditMode suite and five standalone lifecycle runs reported **zero** persistent allocations. See `editor-native-leak-attribution.json`, `attribution.json`, and `../toolchain/native-leak-attribution.json`. The archived GC-016 log remains 57 unattributed because it had no callstacks; it was not used to waive a current leak.

## Why this file exists

`artifacts/gc-016/BUILD_REPORT.md` records Unity's shutdown line verbatim:

```
Leak Detected : Persistent allocates 57 individual allocations. To find out more please enable
'Preferences > Jobs > Leak Detection Level > Enabled With Stack Trace' and reproduce the leak again.
```

Fifty-seven native allocations with no attribution. TEST-023 states the standard that makes a number like that
unacceptable: "Resource counts, retained event bytes, native allocations and quarantine are reported separately;
aggregate process memory alone is insufficient to attribute a leak" (P-007, P-048, P-052). A count that nobody can
attribute is not evidence of bounded memory; it is an unowned resource. This file is the declaration that turns the
count into a gate.

## The rule

In the Contract-D vocabulary, every resource ends in exactly one of three states: **retired** (released once, counted
in `RetiredCount`), **quarantined** (retained because unfinished work may still reach it, and released by an explicit
later release — `ReleaseQuarantine` / `ReleaseQuarantineFor`), or a **bounded cache** (a declared,
capacity-limited retained structure whose growth is bounded and whose bound is stated). Elapsed time never
authorizes release (P-048).

Applied to native allocations observed at shutdown, the rule is:

> Every native allocation observed at shutdown is either **driven to zero**, or **listed here with its exact frame
> signature and a declared bound** — a capacity, a byte ceiling, and what releases it — as a bounded cache or a
> quarantine. An allocation with no bound is a defect.

Two consequences follow, and both are gates rather than prose:

* A **GameCore-owned** allocation is *always* a defect. No bound can authorize it: a GameCore frame at the allocation
  site is a leak in code this repository owns, and `tools/attribute_native_leaks.py` exits 1 for it even when this
  file declares a bound that covers its signature.
* An allocation that is **not driven to zero and not declared here** is a defect too, whether it is Unity-engine,
  third-party, or unattributable. In particular an allocation whose stack Unity could not print is *unattributed*
  and keeps its allocation count: `unattributed=57` is a defect statement, never a zero.

## What counts as a declared bound (the machine-checkable format)

`tools/attribute_native_leaks.py --policy <this file>` reads lines of the form

```
bound: <normalised frame signature> | capacity=<live allocations> | bytes<=<byte ceiling> | released-by=<what releases it>
```

* `<normalised frame signature>` is the signature the tool prints for a stack: up to five frames, joined with ` <- `,
  each frame written as `Symbol [Module]` (`[Unity]`, `[Mono JIT Code]`, …) with addresses and `+0x` offsets removed.
  A declaration matches a signature **exactly**, or as a **prefix** of it — so one declaration bounds the stacks whose
  allocation site is that frame. Prefix matching is why a bound names an allocation site rather than every observed
  stack.
* **All three bound fields are required.** `capacity` is the number of live allocations, `bytes<=` is the byte
  ceiling, and `released-by` names the release path (for a quarantined resource, the explicit release that drains it;
  for a bounded cache, the owner teardown that frees it). A declaration missing any field is reported as a policy
  problem and bounds nothing.
* A declaration written with `<placeholders>` documents the format only: the tool reports it under
  `policy.examples` and it can never cover a signature.

The example above is a placeholder declaration for exactly that reason.

## Current declarations

**None required.** With full stack traces enabled, the accepted Editor EditMode/PlayMode and IL2CPP player lifecycle runs have no native leak header and the attributor reports zero allocations, zero unattributed blocks, `verdict=clean`. No allocation was reclassified as a cache or quarantine. If a later run reports any allocation, it remains a defect until owned and released or explicitly bounded under the policy below.

## Declaring a bound after the first stack-trace run

1. Run the probe with stack traces armed (see `README.md`; `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2`).
2. Read the distinct signatures from `native-leak-attribution.json` (`classes.*.signatures[].signature`, and
   `policy.uncovered` for the ones already reported unbounded).
3. For each signature that is *not* a GameCore defect, add one `bound:` line here with its capacity, byte ceiling and
   release path, and state *why* the bound holds (which owner's teardown frees it, or which explicit release drains
   it). If a bound cannot be stated, the allocation is a defect and the correct action is to fix it rather than widen
   this file.
4. Re-run the harness: it exits 0 only when every observed allocation is Unity-engine/third-party *and* every distinct
   signature is named by a complete declaration here.

## What is not acceptable

* A declaration whose justification is elapsed time ("it is released after N seconds/frames") — P-048 forbids
  reclamation justified only by elapsed time.
* A declaration without a capacity or without a byte ceiling: "bounded" with no number is not a bound.
* Bounds for a whole *class* (`unity-engine` as such): a bound names a frame signature, because only a specific
  allocation site can be argued to be a cache or a quarantine.
* Treating `unattributed` as zero, or as a pass. The attributor exits 2 for it and the harness fails the run.
* Widening this file to cover a GameCore frame. The GameCore exit (1) is not preventable by this policy.
