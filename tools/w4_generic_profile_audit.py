#!/usr/bin/env python3
"""GC-012 provisional generic-execution profile: source-tree audit of the kernel/gameplay boundary.

This is the reproducible, host-runnable half of the GC-012 work item "Freeze the provisional generic
execution contract in a player" (`docs/game-core/09-implementation-guide.md:679-699`). It proves, from the
source tree only, that no kernel assembly depends on a genre-specific type, and it inventories the public
kernel types plus every genre-specific token occurrence in kernel sources. It executes no Unity or .NET code
and makes no executable-result claim; the audit verdict is drawn from this evidence by the GC-012 report.

## The dependency-direction rule this tool encodes

Quoted from the two normative documents (both read before this rule was written):

`docs/game-core/04-unity-integration.md:47-62` ("2. Assemblies and dependency direction"):

    | Assembly | Allowed references | Contents and execution boundary |
    | `GameCore.Contracts` | Supported BCL subset only; `noEngineReferences: true` | ... |
    | `GameCore.Composition` | Contracts, supported BCL | ... |
    | `GameCore.Derivation` | Contracts, supported BCL | ... |
    | `GameCore.Planning` | Contracts, Composition, Derivation | ... |
    | `GameCore.Rules.<Name>` | Contracts and small value types only | ... |
    | `GameCore.Unity.Runtime` | Contracts, Composition, Derivation, Planning, Unity Entities/Collections/Jobs/
      Burst/Mathematics | ... |
    | `GameCore.Unity.Adapters` | Runtime, UnityEngine | ... |
    | `GameCore.Content.Compiler` | Contracts, Planning, Unity Runtime, Unity Editor APIs; Editor-only | ... |
    | `GameCore.Gameplay.<Name>` | Corresponding Rules assembly, Unity Runtime and explicitly required
      adapters | ... |
    | `GameCore.Generated` | Known plugin and adapter assemblies | ... |

    `.asmdef` references must remain acyclic. Generated registration is the application composition root;
    Contracts and Composition cannot reference it. Editor code cannot leak into runtime assemblies.

`docs/game-core/01-architecture.md:26-38` (assembly/layer table, "1. Responsibility flow"):

    | Layer / assembly | Owns | Integration seam |
    | `GameCore.Contracts` | Stable IDs, versioned DTOs, operation/result shapes, wire schemas | No Unity
      types, reflection-driven factories, or ECS facade. |
    | `GameCore.Composition` | Scope/install graph, service resolver, lifecycle controller, resource
      ledger | Proposes immutable snapshots and dependency closures. |
    | `GameCore.Derivation` | Finite rule evaluation, contribution composition, provenance/indexes | Pure
      catalog + snapshot -> contribution delta. |
    | `GameCore.Planning` | Validated `ChangePlan`, semantic stage DAG, state dispositions | Uses layout
      keys, not generic entity access methods. |
    | `GameCore.Unity.Runtime` | Unity `World`, Entity mapping, ECS assembly apply, concrete systems/
      groups/jobs | Implements the single concrete execution path. |
    | `GameCore.Unity.Adapters` | PlayerLoop, input, assets, GameObject mapping, physics/animation/audio |
      ... |
    | `GameCore.Content.Compiler` | Manifest/schema validation, serializers, factories, closed generic
      roots | Build-time output consumed by the player. |
    | `GameCore.Rules.*` | Pure domain calculations where useful | Inputs/outputs use stable values; no ECS
      query wrappers. |
    | `GameCore.Gameplay.*` | Domain schemas, Unity systems, stage/owner/request policies | Cards,
      narrative, traversal and optional combinations. |

Rule as mechanically checked here: the kernel set
(`GameCore.Contracts`, `GameCore.Composition`, `GameCore.Derivation`, `GameCore.Planning`,
`GameCore.Unity.Runtime`, `GameCore.Unity.Adapters`, `GameCore.Content.Compiler`, including their test and
fixture assembly names) must not reference any `GameCore.Gameplay.*`, `GameCore.Rules.*`,
`GameCore.Validation*` or generated (`GameCore.Generated*`) assembly; and a kernel assembly that declares
`noEngineReferences: true` must not reference any assembly whose name starts with `Unity`.

The originating requirement is P-001 (`docs/game-core/00-core-protocols.md:10-11`, "Genre independence and
boundaries"): the kernel MUST NOT require an actor, action, turn, combat, physics, animation, resource, or
reward schema. The genre-token scan in section 4 is the source-level evidence for that requirement; the
scan never fails the run, it reports, because classifying a hit (comment vs. incidental identifier vs. real
schema) is a review act, not a mechanical one.

## Output

A deterministic JSON document on stdout (sorted keys, 2-space indent, LF), optionally also written with
`--out PATH` (the two are byte-identical). No timestamps, no absolute paths: every path is repo-relative
POSIX. Running the tool twice produces byte-identical output.

    python3 tools/w4_generic_profile_audit.py [REPO_ROOT] [--out PATH] [--pretty | --compact]
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence, Set, Tuple

SCHEMA_VERSION = 1
TOOL_PATH = "tools/w4_generic_profile_audit.py"
WORK_ITEM = "GC-012"
REQUIREMENT = "P-001"
NOT_RUN = "NotRun (pending orchestrator build host)"

# --- section 1: assembly discovery -------------------------------------------------------------

#: Directories searched for `*.asmdef`, repo-relative.
ASMDEF_ROOTS: Tuple[str, ...] = ("Packages", "unity/GameCore.Validation/Assets")

#: Assembly-name prefixes that make an assembly part of the kernel (`04-unity-integration.md:51-58`).
KERNEL_PREFIXES: Tuple[str, ...] = (
    "GameCore.Contracts",
    "GameCore.Composition",
    "GameCore.Derivation",
    "GameCore.Planning",
    "GameCore.Unity.Runtime",
    "GameCore.Unity.Adapters",
    "GameCore.Content.Compiler",
)

GAMEPLAY_PREFIXES: Tuple[str, ...] = ("GameCore.Gameplay.", "GameCore.Rules.")
VALIDATION_PREFIX = "GameCore.Validation"
FIXTURE_PREFIX = "GameCore.Unity.Fixtures"
GENERATED_PREFIXES: Tuple[str, ...] = ("GameCore.Generated", "GameCore.Validation.Generated")

#: Names that are not project assemblies but are legitimately referenced by project assemblies.
#: Anything outside this set that is not a discovered project assembly is reported as unresolved.
EXTERNAL_REFERENCE_PREFIXES: Tuple[str, ...] = (
    "Unity.",
    "UnityEngine",
    "UnityEditor",
    "nunit",
    "System",
    "Microsoft.",
    "Mono.",
    "netstandard",
    "mscorlib",
    "com.unity.",
    "Newtonsoft",
)

GUID_REFERENCE_RE = re.compile(r"^GUID:[0-9a-fA-F]{32}$")

# --- section 4: kernel type inventory and genre-token scan --------------------------------------

#: Kernel package directories (repo-relative) whose `Runtime`/`Editor`/`Tests` folders are inventoried.
KERNEL_PACKAGE_DIRS: Tuple[str, ...] = (
    "Packages/com.gamecore.contracts",
    "Packages/com.gamecore.composition",
    "Packages/com.gamecore.derivation",
    "Packages/com.gamecore.planning",
    "Packages/com.gamecore.unity.runtime",
    "Packages/com.gamecore.unity.adapters",
    "Packages/com.gamecore.content.compiler",
)

#: Top-level folders of a kernel package that belong to the generic profile itself. `Fixtures/` is
#: deliberately excluded: fixture assemblies are validation support, not shipped kernel surface.
KERNEL_SOURCE_FOLDERS: Tuple[str, ...] = ("Runtime", "Editor", "Tests")

TYPE_DECL_RE = re.compile(
    r"^\s*(?:public|internal)\s+(?:static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+)*"
    r"(class|struct|interface|enum)\s+(\w+)"
)

#: Genre-specific token names scanned for in kernel sources, matching P-001's forbidden schemas
#: (`docs/game-core/00-core-protocols.md:10-11`) and the hidden-assumption table
#: (`docs/game-core/07-reference-compositions.md:316-333`). Matched case-insensitively as whole
#: identifiers only, so `hand` never matches `Handle`/`handle`/`shader`.
GENRE_TOKENS: Tuple[str, ...] = (
    "actor",
    "action",
    "turn",
    "combat",
    "hit",
    "vitality",
    "quest",
    "inventory",
    "damage",
    "card",
    "deck",
    "hand",
    "narrative",
    "chapter",
    "physics",
    "animation",
    "reward",
    "transform",
    "gameobject",
    "monobehaviour",
    "prefab",
    "story",
    "villager",
    "seat",
    "gate",
)

GENRE_TOKEN_RES: Dict[str, "re.Pattern[str]"] = {
    token: re.compile(r"\b" + re.escape(token) + r"\b", re.IGNORECASE) for token in GENRE_TOKENS
}


def rel(root: Path, path: Path) -> str:
    """Repo-relative POSIX path; absolute paths never appear in the report."""
    return path.resolve().relative_to(root).as_posix()


def classify_assembly(name: str) -> str:
    """Classify an assembly name (section 2). Checked most-specific-first: fixture, kernel, gameplay,
    validation, other."""
    if name == FIXTURE_PREFIX:
        return "fixture"
    if any(name.startswith(prefix) for prefix in KERNEL_PREFIXES):
        return "kernel"
    if any(name.startswith(prefix) for prefix in GAMEPLAY_PREFIXES):
        return "gameplay"
    if name.startswith(VALIDATION_PREFIX):
        return "validation"
    return "other"


def is_generated(name: str) -> bool:
    return any(name.startswith(prefix) for prefix in GENERATED_PREFIXES)


def is_external_reference(name: str) -> bool:
    if name in {"mscorlib", "netstandard", "nunit.framework"}:
        return True
    return any(name.startswith(prefix) for prefix in EXTERNAL_REFERENCE_PREFIXES)


def assemble_asmdefs(root: Path) -> List[dict]:
    """Section 1: discover and describe every `*.asmdef`, with references resolved to assembly names."""
    paths: List[Path] = []
    for relative in ASMDEF_ROOTS:
        base = root / relative
        if base.is_dir():
            paths.extend(path for path in base.rglob("*.asmdef") if path.is_file())
    paths.sort(key=lambda path: path.as_posix())

    raw: List[dict] = []
    for path in paths:
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except (OSError, ValueError) as error:
            print("cannot read asmdef {}: {}".format(rel(root, path), error), file=sys.stderr)
            raise SystemExit(2)
        if not isinstance(data, dict):
            data = {}
        name = data.get("name")
        if not isinstance(name, str) or not name:
            name = path.stem
        references = data.get("references")
        platforms = data.get("includePlatforms")
        if not isinstance(platforms, list):
            platforms = []
        if not isinstance(references, list):
            references = []
        raw.append(
            {
                "path": rel(root, path),
                "name": name,
                "includePlatforms": sorted(
                    value for value in platforms if isinstance(value, str)
                ),
                "noEngineReferences": bool(data.get("noEngineReferences", False)),
                "references": references,
            }
        )

    project_names: Set[str] = {entry["name"] for entry in raw}

    assemblies: List[dict] = []
    for entry in raw:
        project: List[str] = []
        external: List[str] = []
        guid: List[str] = []
        unresolved: List[str] = []
        for reference in entry["references"]:
            if isinstance(reference, dict):
                value = reference.get("name") or reference.get("guid") or ""
                value = str(value)
                if not value.startswith("GUID:") and reference.get("guid"):
                    value = "GUID:" + str(reference["guid"])
            else:
                value = str(reference)
            if GUID_REFERENCE_RE.match(value) or value.startswith("GUID:"):
                guid.append(value)
            elif value in project_names:
                project.append(value)
            elif is_external_reference(value):
                external.append(value)
            else:
                unresolved.append(value)

        assemblies.append(
            {
                "name": entry["name"],
                "path": entry["path"],
                "packageRoot": entry["path"].rsplit("/", 1)[0] if "/" in entry["path"] else ".",
                "classification": classify_assembly(entry["name"]),
                "includePlatforms": entry["includePlatforms"],
                "noEngineReferences": entry["noEngineReferences"],
                "references": sorted(set(project)),
                "externalReferences": sorted(set(external)),
                "guidReferences": sorted(set(guid)),
                "unresolvedReferences": sorted(set(unresolved)),
            }
        )

    assemblies.sort(key=lambda entry: (entry["name"], entry["path"]))
    return assemblies


# --- section 3: forbidden edges ------------------------------------------------------------------


def forbidden_edges(assemblies: Sequence[dict]) -> Tuple[List[dict], List[dict], List[dict]]:
    """Every `kernel -> gameplay|rules|validation|generated` edge, the `kernel -> validation` subset, and
    every kernel assembly that declares `noEngineReferences: true` while referencing a `Unity*` assembly."""
    gameplay_targets: List[dict] = []
    validation_targets: List[dict] = []
    engine_violations: List[dict] = []

    for entry in assemblies:
        if entry["classification"] != "kernel":
            continue
        for target in entry["references"]:
            target_kind = classify_assembly(target)
            if target_kind in {"gameplay", "validation"} or is_generated(target):
                violation = {"from": entry["name"], "to": target, "fromPath": entry["path"]}
                gameplay_targets.append(violation)
                if target_kind == "validation":
                    validation_targets.append(dict(violation))
        if entry["noEngineReferences"]:
            for target in entry["externalReferences"]:
                if target.startswith("Unity"):
                    engine_violations.append(
                        {
                            "assembly": entry["name"],
                            "fromPath": entry["path"],
                            "reference": target,
                            "declaredNoEngineReferences": True,
                        }
                    )

    def key(violation: dict) -> Tuple[str, str, str]:
        return (violation["from"], violation["to"], violation["fromPath"])

    gameplay_targets.sort(key=key)
    validation_targets.sort(key=key)
    engine_violations.sort(key=lambda item: (item["assembly"], item["reference"], item["fromPath"]))
    return gameplay_targets, validation_targets, engine_violations


# --- section 4: kernel type inventory and genre-token scan ---------------------------------------


def kernel_sources(root: Path) -> List[Path]:
    """Every `*.cs` file under `Runtime`/`Editor`/`Tests` of the seven kernel package directories."""
    sources: List[Path] = []
    for package in KERNEL_PACKAGE_DIRS:
        for folder in KERNEL_SOURCE_FOLDERS:
            base = root / package / folder
            if base.is_dir():
                sources.extend(path for path in base.rglob("*.cs") if path.is_file())
    unique = {path.resolve(): path for path in sources}
    return [unique[key] for key in sorted(unique, key=lambda p: p.as_posix())]


def type_inventory(root: Path, sources: Sequence[Path]) -> List[dict]:
    inventory: List[dict] = []
    for path in sources:
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for number, line in enumerate(text.splitlines(), start=1):
            match = TYPE_DECL_RE.match(line)
            if match is not None:
                inventory.append(
                    {
                        "name": match.group(2),
                        "kind": match.group(1),
                        "file": rel(root, path),
                        "line": number,
                    }
                )
    inventory.sort(key=lambda entry: (entry["file"], entry["line"], entry["name"]))
    return inventory


def genre_token_scan(root: Path, sources: Sequence[Path]) -> dict:
    hits: List[dict] = []
    per_token: Dict[str, int] = {token: 0 for token in GENRE_TOKENS}
    for path in sources:
        relative = rel(root, path)
        text = path.read_text(encoding="utf-8-sig", errors="replace")
        for number, line in enumerate(text.splitlines(), start=1):
            for token in GENRE_TOKENS:
                if GENRE_TOKEN_RES[token].search(line):
                    hits.append(
                        {
                            "file": relative,
                            "line": number,
                            "token": token,
                            "lineText": line.rstrip("\r\n"),
                        }
                    )
                    per_token[token] += 1
    hits.sort(key=lambda hit: (hit["file"], hit["line"], hit["token"]))
    return {
        "tokens": list(GENRE_TOKENS),
        "tokenCount": len(GENRE_TOKENS),
        "hitCount": len(hits),
        "perTokenCounts": per_token,
        "hits": hits,
    }


# --- report --------------------------------------------------------------------------------------


def build_report(root: Path) -> dict:
    assemblies = assemble_asmdefs(root)
    kernel_to_gameplay, kernel_to_validation, engine_violations = forbidden_edges(assemblies)

    sources = kernel_sources(root)
    inventory = type_inventory(root, sources)
    scan = genre_token_scan(root, sources)

    classification_counts = {"kernel": 0, "gameplay": 0, "validation": 0, "fixture": 0, "other": 0}
    for entry in assemblies:
        classification_counts[entry["classification"]] += 1

    summary = {
        "assemblyCount": len(assemblies),
        "kernelAssemblyCount": classification_counts["kernel"],
        "gameplayAssemblyCount": classification_counts["gameplay"],
        "kernelToGameplayEdgeCount": len(kernel_to_gameplay),
        "kernelToValidationEdgeCount": len(kernel_to_validation),
        "kernelNoEngineReferenceViolationCount": len(engine_violations),
        "typeCount": len(inventory),
        "genreTokenHitCount": scan["hitCount"],
        "deterministic": True,
    }

    return {
        "schemaVersion": SCHEMA_VERSION,
        "tool": TOOL_PATH,
        "workItem": WORK_ITEM,
        "requirement": REQUIREMENT,
        "repoRoot": ".",
        "basis": {
            "rule": "kernel assemblies must not reference gameplay, rules, validation or generated assemblies; "
            "a kernel assembly declaring noEngineReferences must not reference Unity assemblies",
            "sources": [
                "docs/game-core/00-core-protocols.md:10-11",
                "docs/game-core/01-architecture.md:26-38",
                "docs/game-core/04-unity-integration.md:47-62",
                "docs/game-core/07-reference-compositions.md:316-333",
                "docs/game-core/09-implementation-guide.md:679-699",
            ],
            "scannedAsmdefRoots": list(ASMDEF_ROOTS),
            "scannedKernelPackages": list(KERNEL_PACKAGE_DIRS),
            "scannedKernelSourceFolders": list(KERNEL_SOURCE_FOLDERS),
            "kernelSourceFileCount": len(sources),
            "typeInventoryScope": "the specified declaration regex is applied to every line of every "
            "scanned kernel file and is not brace-depth filtered, so nested declarations are listed too",
        },
        "assemblies": assemblies,
        "classificationCounts": classification_counts,
        "forbiddenEdges": {
            "kernelToGameplay": kernel_to_gameplay,
            "kernelToValidation": kernel_to_validation,
        },
        "kernelNoEngineReferenceViolations": engine_violations,
        "typeInventory": inventory,
        "genreTokenScan": scan,
        "summary": summary,
        "execution": {
            "unityOrDotnetInvoked": False,
            "unityPlayerResult": NOT_RUN,
            "testResult": NOT_RUN,
        },
    }


def render(report: dict, pretty: bool) -> str:
    if pretty:
        return json.dumps(report, sort_keys=True, indent=2, ensure_ascii=False) + "\n"
    return json.dumps(report, sort_keys=True, separators=(",", ":"), ensure_ascii=False) + "\n"


def main(argv: Optional[Sequence[str]] = None) -> int:
    parser = argparse.ArgumentParser(
        description="Audit the GameCore kernel/gameplay assembly boundary and kernel genre tokens (GC-012).",
    )
    parser.add_argument(
        "repo_root",
        nargs="?",
        default=None,
        help="repository root (default: two directories above this script)",
    )
    parser.add_argument("--out", default=None, help="also write the JSON document to this path")
    style = parser.add_mutually_exclusive_group()
    style.add_argument("--pretty", dest="pretty", action="store_true", help="indented JSON (default)")
    style.add_argument("--compact", dest="pretty", action="store_false", help="single-line JSON")
    parser.set_defaults(pretty=True)
    args = parser.parse_args(argv)

    root = (
        Path(args.repo_root) if args.repo_root is not None else Path(__file__).resolve().parent.parent
    ).resolve()
    if not (root / "Packages").is_dir():
        print("not a GameCore repository root: {}".format(root), file=sys.stderr)
        return 2

    document = render(build_report(root), args.pretty)
    if args.out is not None:
        destination = Path(args.out)
        destination.parent.mkdir(parents=True, exist_ok=True)
        with destination.open("w", encoding="utf-8", newline="\n") as handle:
            handle.write(document)
    sys.stdout.write(document)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
