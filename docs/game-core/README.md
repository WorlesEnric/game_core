# Game Core implementation design

This set specifies a genre-neutral game runtime implemented in **C# and Unity Entities**, with **Automatic capability propagation as the default**. Mounting a plugin changes eligible descendants, including future spawns, through a validated assembly plan. Gameplay packages own their rules and state; the kernel owns composition and execution correctness.

Status: complete design candidate, protocol **1.0**, researched 2026-09-25. This repository currently contains documentation, representative contract code, and a documentation validator. It does not contain the Game Core runtime or a validated Unity player. The baseline is an exact selected build target supported by official constraints; successful resolution/build/execution remains the first implementation gate. No team size, commercial platform list, or production load is assumed.

The supplied request first requested Chinese and finally required all documents in English. This set follows the final instruction while retaining API/type identifiers.

## Confirmed decisions

- Unity Entities is the first and only V1 ECS backend. Jobs/Burst serve suitable hot paths; IL2CPP and stripping are mandatory build gates.
- A Cordis-inspired managed composition layer handles scopes, services, configuration, resources, and lifecycle. An assembly bridge translates its changes into ECS data, bindings, and execution graphs.
- Automatic and Conservative share one runtime. Automatic requires no per-instance capability imports for compatible descendants; Conservative applies an explicit grant predicate.
- A capability contribution is separate from a managed resource lifetime and from a committed gameplay effect.
- Games select `CommandDriven` or `FixedStep`. The kernel supplies no actor, combat, physics, animation, or universal transaction phase.
- V1 is a complete implementation of the specified contracts, including live changes, failures, persistence, both modes, all three reference families, and player tests. “Minimal running slice” describes an early gate, not the final deliverable.
- Workbench UX, editor panels, AI/chat workflows, another ECS/backend, C++ core, and universal engine abstraction are out of scope.

## Reading order and ownership

| Document | Responsibility / authority |
|---|---|
| [00 Core protocols](00-core-protocols.md) | **Sole normative source** for semantics, P-001…P-060 requirements, and O-01…O-26 operations. Read first. |
| [01 Architecture](01-architecture.md) | Boundaries, reuse, rationale, changes from the old design. |
| [02 Composition and propagation](02-composition-and-propagation.md) | Algorithms/indexes implementing the protocol; Cordis relationship. |
| [03 Runtime and execution](03-runtime-and-execution.md) | Runtime representation, execution graph compiler, state/message paths. |
| [04 Unity integration](04-unity-integration.md) | Exact selected toolchain and Unity API mapping/build obligations. |
| [05 Contracts and data model](05-contracts-and-data-model.md) | C# shapes, catalog/serialization schema, API ownership. |
| [06 Lifecycle and recovery](06-lifecycle-and-recovery.md) | Operational sequences, safe teardown and recovery cases. |
| [07 Reference compositions](07-reference-compositions.md) | Card, narrative, action, and cross-family package policies. |
| [08 Validation and performance](08-validation-and-performance.md) | TEST-001…TEST-024 suites, fault injection, provisional metrics. |
| [09 Implementation guide](09-implementation-guide.md) | GC task contracts, ordered waves, dependencies, demonstrable gates. |
| [10 Decisions and open questions](10-decisions-and-open-questions.md) | ADRs, ranked unknowns with defaults/experiments/fallbacks, primary sources. |

“Ownership” here names document responsibility, not an assumed person/team. Protocol changes require an update to 00, affected implementation mappings, examples, and acceptance tests in one change. A Unity API detail belongs in 04, not a new rule in 03. Example packages may choose different semantics without editing the kernel. If implementation discovers a contradiction, correct the source requirement before modifying mappings; do not silently redefine a term locally.

The previous [single-file design](../Game_Core_Implementation_Design_v1.md) is retained as historical input only. Its Arch candidate, combat protocol, and conservative default are superseded. Nothing in that file overrides this set.

## Evidence and maintenance

The [traceability registry](traceability.json) is machine-readable indexing of requirements, tasks, waves, and tests; it does not introduce additional requirements. Run from repository root:

```sh
rtk proxy python3 tools/validate_game_core_docs.py --self-test
rtk proxy python3 tools/validate_game_core_docs.py
```

The validator checks local links/anchors, ID resolution/coverage, task metadata, dependency cycles, and wave ordering. It cannot prove the runtime conforms or that Unity APIs build. [Unity source evidence](references/unity-evidence.md) distinguishes official statements from experiments still needed. [Implementation gates](09-implementation-guide.md) define when each stronger claim becomes justified.

Start at wave 0: validate the editor/package/native toolchain, Unity World/Jobs/Burst/IL2CPP/stripping, and protocol fixtures. Do not build a general framework before the Automatic propagation vertical slice runs in a standalone player.
