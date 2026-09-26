# Local packages and the assembly contract

GC-029 owns packaging metadata. This page is the contract: what the packages are, what version they carry,
how their dependencies are derived, and which dependency directions are forbidden.

## 1. Version

Every local package is version **`1.0.0`** on this revision, matching protocol 1.0 (P-055). A single
revision ships a single version.

Each `Packages/com.gamecore.<name>/package.json` also carries an accurate `displayName`, a `description`, and
`"unity": "6000.0"`. The description states what the package actually contains.

## 2. The engine-free kernel

These packages form the kernel. **None of them may depend on a gameplay package**, directly or transitively
(P-057; 09's "no gameplay dependency from kernel").

| Package | Assembly | Engine references | Dependencies |
| --- | --- | --- | --- |
| `com.gamecore.contracts` | `GameCore.Contracts` | none (`noEngineReferences: true`) | none |
| `com.gamecore.composition` | `GameCore.Composition` | none | contracts |
| `com.gamecore.derivation` | `GameCore.Derivation` | none | contracts |
| `com.gamecore.planning` | `GameCore.Planning` | none | contracts |
| `com.gamecore.content.compiler` | `GameCore.Content.Compiler` (Editor-only) | none | contracts |
| `com.gamecore.rules.cards` | `GameCore.Rules.Cards` | none | contracts |
| `com.gamecore.rules.narrative` | `GameCore.Rules.Narrative` | none | contracts, derivation |
| `com.gamecore.rules.traversal` | `GameCore.Rules.Traversal` | none | contracts |

The dependency direction is strictly **gameplay → kernel**. `GameCore.Content.Compiler.Editor` is the only
assembly inside a kernel package that uses `UnityEditor`/`UnityEngine`, and it is an Editor-only assembly —
Editor code cannot leak into runtime assemblies (04 §2).

`com.gamecore.rules.narrative` declares `com.gamecore.derivation` because its **Editor-only test assembly**
references `GameCore.Derivation` and `GameCore.Derivation.Fixtures`. The runtime assembly itself still depends
only on contracts.

## 3. Engine-facing packages

| Package | Assembly | Engine dependencies |
| --- | --- | --- |
| `com.gamecore.unity.runtime` | `GameCore.Unity.Runtime` | `com.unity.burst` 1.8.28, `com.unity.collections` 2.6.6, `com.unity.entities` 1.4.6 |
| `com.gamecore.unity.adapters` | `GameCore.Unity.Adapters` | `com.unity.entities` 1.4.6 |
| `com.gamecore.gameplay.cards` | `GameCore.Gameplay.Cards` | collections, entities |
| `com.gamecore.gameplay.narrative` | `GameCore.Gameplay.Narrative` | burst, collections, entities |
| `com.gamecore.gameplay.traversal` | `GameCore.Gameplay.Traversal` | collections, entities |
| `com.gamecore.gameplay.integration` | `GameCore.Gameplay.Integration` | collections, entities |
| `com.gamecore.gameplay.rewards` | `GameCore.Gameplay.Rewards` | collections, entities |

`GameCore.Unity.Runtime`'s `Runtime/Pure` subtree is engine-free (namespace `GameCore.Execution`) and is
compiled by both Unity and `dotnet/src/GameCore.Execution`, which is what lets the kernel be tested with plain
dotnet.

## 4. Marker packages

Two packages exist to switch qualification code in or out. **They declare no assembly and no dependency** —
a shipping project omits them, and that omission is what compiles the code out.

| Package | Version | Effect when present |
| --- | --- | --- |
| `com.gamecore.fault-qualification` | `1.0.0` | Defines `GAMECORE_FAULT_INJECTION` via `versionDefines`; compiles deterministic fault latches into the runtime and player. |
| `com.gamecore.telemetry-qualification` | `1.0.0` | Defines `GAMECORE_TELEMETRY`; compiles GC-023's counting call sites. |

`versionDefines` pins these at exactly `1.0.0` in the asmdefs that use them. `tools/check_release_fault_free.py`
and `tools/check_release_telemetry_free.py` read those entries, so **any version change to either marker
package must keep the `versionDefines` expression equal**.

## 5. Qualification fixture packages

These live outside `Packages/` (under `tests/`) and are referenced by the validation project's manifest. They
are qualification evidence, not shipping surface, and the release clone removes them.

| Package | Assembly | Dependencies |
| --- | --- | --- |
| `com.gamecore.benchmarks` | `GameCore.Benchmarks` | contracts, derivation |
| `com.gamecore.recovery` | `GameCore.Recovery.Fixtures` | contracts, unity.runtime |
| `com.gamecore.replay` | `GameCore.Replay` | contracts, derivation, composition, planning |
| `com.gamecore.reference-conformance` | `GameCore.ReferenceConformance` | contracts, planning, rules.cards, rules.narrative, rules.traversal |

## 6. How dependencies are derived and audited

A package's `dependencies` map is **not** hand-maintained prose. It is the exact set of packages required by
the assemblies that package defines:

- an `asmdef` reference to another Game Core assembly means the declaring package must depend on the
  referenced package;
- an `asmdef` reference to `Unity.Entities`/`Unity.Burst`/`Unity.Collections`/`Unity.Mathematics` means the
  package must depend on that engine package, at the version the qualification manifest pins;
- `versionDefines` entries are **explicitly not** dependencies — they express an *optional* dependency on a
  marker package, and requiring one would force every shipping project to install the switch a release build
  is defined by omitting;
- the test runners (`UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `nunit.framework`) are provided by the
  project, never by a package;
- a package may **not** reference an assembly defined in the embedding Unity project's own `Assets/`, because
  that inverts the dependency direction.

`tools/check_package_metadata.py` enforces all of the above by reading the `asmdef` files, so the metadata
cannot drift from the sources it describes:

```sh
python3 tools/check_package_metadata.py            # audit
python3 tools/check_package_metadata.py --self-test # falsify the rules themselves (12 cases)
python3 tools/check_package_metadata.py --sync-lock # mirror the lock's dependency maps from the manifests
python3 tools/check_package_metadata.py --json artifacts/reproducibility/package-metadata.json
```

## 7. The lock file

`unity/GameCore.Validation/Packages/packages-lock.json` is Unity's resolved package graph. Unity regenerates
it on import, and `tools/unity/prepare_gc017_release_project.py` deletes it so the release clone resolves its
own marker-free graph.

Because the lock is regenerated, it can be stale between imports. `--sync-lock` mirrors each local package's
`com.gamecore.*` dependency map from its manifest, which keeps a committed lock honest and is a no-op once the
two agree. The lock's *version* field for a local package is its `file:` path, not a semver, so the manifests
are the single source of truth for versions.

`tools/check_release_clone.py` treats two lock conditions as defects in **either** state (before or after an
import): a qualification-only package name, and a `com.gamecore.*` entry the manifest does not declare.

## 8. Player bundle version is not a package version

`PlayerSettings.bundleVersion` is `0.1.0`. That is the **player's** version string and it is unrelated to the
package versions above. Nothing asserts it; see [profile.md §5](profile.md).
