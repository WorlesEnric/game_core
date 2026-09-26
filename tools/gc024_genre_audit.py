#!/usr/bin/env python3
"""GC-024 genre audit: the host-side twin of `GameCore.ReferenceConformance.AssemblyReferenceAudit`.

This tool runs the SAME rule the fixture assembly implements, from the source tree only, and writes
`artifacts/gc-024/genre-audit.json` in the same document format (`gamecore.genre-audit/1`). It exists for the same
reason `tools/w4_generic_profile_audit.py` exists beside `KernelAssemblyAudit`: the C# audit is the executable one
(it runs in the EditMode suite and in the IL2CPP player, where the loaded-assembly half is available) and this one is
the reproducible host-side run that produces the committed artifact without a toolchain. They are deliberately
independent implementations of one rule, so a defect in one is visible as a disagreement with the other, and the
EditMode suite asserts the artifact's counts against its own audit.

## The rule, verbatim from the normative sources

`docs/game-core/00-core-protocols.md` P-001:

    "The kernel MUST provide composition, identity, assembly, execution coordination, lifecycle, and observation
     contracts without requiring an actor, action, turn, combat, physics, animation, resource, or reward schema."

P-057:

    "Optional gameplay packages do not become kernel dependencies merely because fixtures use them."

`docs/game-core/04-unity-integration.md` s2 ("Assemblies and dependency direction"):

    "| `GameCore.Gameplay.<Name>` | Corresponding Rules assembly, Unity Runtime and explicitly required adapters |"

Mechanically: no kernel assembly definition and no kernel dotnet project may reference a `GameCore.Gameplay.*`,
`GameCore.Rules.*`, `GameCore.Validation*` or `GameCore.Generated*` assembly or project; every gameplay/rules assembly
definition must reference the kernel; no kernel source file may name a genre type; and a package that declares
`noEngineReferences: true` may not reference an engine assembly.

## Falsifiability

The document's `clean` field requires every count to be positive, so a run that read nothing cannot look clean. The
`walkedEntries` count is what the EditMode suite asserts is non-zero on its own audit.

Usage:
    python3 tools/gc024_genre_audit.py [--root DIR] [--out PATH] [--stdout]

Exit codes: 0 the audit is clean; 1 a violation, a missing package or a zero count; 2 invalid configuration.
"""

import argparse
import json
import re
import sys
from pathlib import Path


FORMAT_NAME = "gamecore.genre-audit/1"
TASK_ID = "GC-024"
DEFAULT_OUT = "artifacts/gc-024/genre-audit.json"

# 04 s2's kernel assemblies, including the fixture assemblies the gates run on and the Editor-only compiler.
KERNEL_NAMES = (
    "GameCore.Contracts",
    "GameCore.Composition",
    "GameCore.Derivation",
    "GameCore.Derivation.Fixtures",
    "GameCore.Planning",
    "GameCore.Unity.Runtime",
    "GameCore.Unity.Fixtures",
    "GameCore.Unity.Adapters",
    "GameCore.Unity.Adapters.Fixtures",
    "GameCore.Content.Compiler",
    "GameCore.Content.Compiler.Editor",
    "GameCore.Execution",
    "GameCore.Adapters",
)

KERNEL_PACKAGES = (
    "Packages/com.gamecore.contracts",
    "Packages/com.gamecore.composition",
    "Packages/com.gamecore.derivation",
    "Packages/com.gamecore.planning",
    "Packages/com.gamecore.unity.runtime",
    "Packages/com.gamecore.unity.adapters",
    "Packages/com.gamecore.content.compiler",
)

FAMILY_PACKAGES = (
    "Packages/com.gamecore.gameplay.cards",
    "Packages/com.gamecore.gameplay.narrative",
    "Packages/com.gamecore.gameplay.traversal",
    "Packages/com.gamecore.gameplay.integration",
    "Packages/com.gamecore.rules.cards",
    "Packages/com.gamecore.rules.narrative",
    "Packages/com.gamecore.rules.traversal",
)

FORBIDDEN_PREFIXES = ("GameCore.Gameplay.", "GameCore.Rules.", "GameCore.Validation", "GameCore.Generated")

# The genre tokens a kernel source must not name: the four domain concepts P-001 keeps out of the kernel, plus the
# families' own type names. Additive with the reference audit, because a reference can be dropped while a `using` or a
# type name remains.
FORBIDDEN_TOKENS = (
    "GameCore.Gameplay.",
    "GameCore.Rules.",
    "GameCore.Validation.",
    "using GameCore.Gameplay",
    "using GameCore.Rules",
    "CardTableModule",
    "CardTableKeys",
    "CardSeatState",
    "CardHandRow",
    "CardCommittedRow",
    "NarrativeModule",
    "NarrativeKeys",
    "NarrativeTargetMarker",
    "QuestLedger",
    "TraversalModule",
    "TraversalKeys",
    "TraversalPose",
    "TraversalVelocity",
    "RunnerRecipe",
    "CheckpointVolume",
)

SKIP_DIRS = {"bin", "obj", ".git", "Library", "Temp", "Logs"}


def relative(root: Path, path: Path) -> str:
    return path.relative_to(root).as_posix()


def sorted_files(directory: Path, pattern: str):
    if not directory.is_dir():
        return []
    found = []
    for path in directory.rglob(pattern):
        if any(part in SKIP_DIRS for part in path.parts):
            continue
        found.append(path)
    return sorted(found, key=lambda p: p.as_posix())


def json_array(text: str, key: str):
    """Reads one flat JSON string array; the asmdef format is fixed and flat, so a scanner is exact enough."""
    match = re.search(r'"' + re.escape(key) + r'"\s*:\s*\[(.*?)\]', text, re.S)
    if not match:
        return []
    return re.findall(r'"([^"]*)"', match.group(1))


def json_string(text: str, key: str):
    match = re.search(r'"' + re.escape(key) + r'"\s*:\s*"([^"]*)"', text)
    return match.group(1) if match else None


def is_forbidden(name: str) -> bool:
    return any(name.startswith(prefix) for prefix in FORBIDDEN_PREFIXES)


def is_kernel(name: str) -> bool:
    return name in KERNEL_NAMES


def package_root(root: Path, path: Path) -> str:
    parts = relative(root, path).split("/")
    if parts[0] == "Packages" and len(parts) > 1:
        return parts[0] + "/" + parts[1]
    return "/".join(parts[:2]) if len(parts) >= 2 else parts[0]



def stripped_source(text: str) -> str:
    """Removes comment and string/char-literal contents while preserving line structure.

    The token rule is about a *reference* a kernel source makes, so a mention inside a doc comment (several kernel
    files describe a generated qualification assembly by name) or inside a literal is not a finding. Keeping the
    newlines means the line numbers a finding reports still point at the real source line.
    """
    out = []
    i = 0
    length = len(text)
    while i < length:
        char = text[i]
        if char == "/" and i + 1 < length and text[i + 1] == "/":
            while i < length and text[i] != "\n":
                i += 1
            continue
        if char == "/" and i + 1 < length and text[i + 1] == "*":
            i += 2
            while i + 1 < length and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            i += 2
            continue
        if char == "@" and i + 1 < length and text[i + 1] == '"':
            i += 2
            while i < length:
                if text[i] == '"':
                    if i + 1 < length and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            continue
        if char == '"':
            i += 1
            while i < length:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == '"':
                    i += 1
                    break
                if text[i] == "\n":
                    out.append("\n")
                i += 1
            continue
        if char == "'":
            i += 1
            while i < length:
                if text[i] == "\\":
                    i += 2
                    continue
                if text[i] == "'":
                    i += 1
                    break
                i += 1
            continue
        out.append(char)
        i += 1
    return "".join(out)

def audit(root: Path) -> dict:
    assemblies = []
    violations = []
    kernel_tokens = []
    missing = []
    assertions = 0
    asmdef_count = 0
    project_count = 0
    kernel_source_count = 0

    def read_asmdef(path: Path, classification: str):
        text = path.read_text(encoding="utf-8")
        name = json_string(text, "name") or path.stem
        return {
            "name": name,
            "path": relative(root, path),
            "kind": "asmdef",
            "classification": classification,
            "packageRoot": package_root(root, path),
            "noEngineReferences": '"noEngineReferences": true' in text,
            "references": json_array(text, "references"),
        }

    def read_project(path: Path, classification: str):
        text = path.read_text(encoding="utf-8")
        match = re.search(r"<AssemblyName>([^<]+)</AssemblyName>", text)
        name = match.group(1).strip() if match else path.stem
        refs = re.findall(r'<ProjectReference[^>]*Include="([^"]+)"', text)
        return {
            "name": name,
            "path": relative(root, path),
            "kind": "csproj",
            "classification": classification,
            "packageRoot": path.parent.relative_to(root).as_posix(),
            "noEngineReferences": False,
            "references": refs,
        }

    # 1. The kernel's own declarations: no family/generated/qualification reference, ever, and no engine reference
    #    from a declaration that forbids one.
    for package in KERNEL_PACKAGES:
        directory = root / package
        if not directory.is_dir():
            missing.append(package)
            continue
        for path in sorted_files(directory, "*.asmdef"):
            record = read_asmdef(path, "kernel")
            assemblies.append(record)
            asmdef_count += 1
            for reference in record["references"]:
                assertions += 1
                if is_forbidden(reference):
                    violations.append({
                        "assembly": record["name"], "path": record["path"], "reference": reference,
                        "rule": "P-001 / 04 s2: a kernel assembly never references a gameplay, rules,"
                                " qualification or generated assembly",
                    })
                elif record["noEngineReferences"] and reference.startswith("Unity"):
                    violations.append({
                        "assembly": record["name"], "path": record["path"], "reference": reference,
                        "rule": "01 s1: a declaration that sets noEngineReferences may not reference an engine"
                                " assembly",
                    })

    # 2. The families: each must reach the kernel, and the audit reads them so the direction is checked both ways.
    for package in FAMILY_PACKAGES:
        directory = root / package
        if not directory.is_dir():
            missing.append(package)
            continue
        for path in sorted_files(directory, "*.asmdef"):
            classification = "generated" if path.stem.startswith("GameCore.Generated") else "family"
            record = read_asmdef(path, classification)
            assemblies.append(record)
            asmdef_count += 1
            if classification != "family":
                continue
            on_kernel = False
            for reference in record["references"]:
                assertions += 1
                if is_kernel(reference):
                    on_kernel = True
            if not on_kernel:
                violations.append({
                    "assembly": record["name"], "path": record["path"], "reference": "<no kernel reference>",
                    "rule": "P-001 / 04 s2: a gameplay or rules assembly must reference the kernel it runs on",
                })

    # 3. Every other declaration in the tree, so a new assembly cannot enter unaudited.
    seen = {record["path"] for record in assemblies}
    for path in sorted_files(root, "*.asmdef"):
        if relative(root, path) in seen:
            continue
        classification = "generated" if path.stem.startswith("GameCore.Generated") else "qualification"
        assemblies.append(read_asmdef(path, classification))
        asmdef_count += 1

    # 4. The plain-dotnet half: the same boundary declared as a project path.
    #    A `dotnet/src` project is a KERNEL project only when its own assembly name is a kernel one: the directory
    #    also holds the plain-dotnet shells of the fixture suites (the replay, protocol, reference-seam and
    #    conformance fixtures), and those legitimately reference the rules packages they exercise (P-057's "neither
    #    half substitutes for the other" is why they exist at all).
    for path in sorted_files(root / "dotnet/src", "*.csproj"):
        record = read_project(path, "kernel" if is_kernel(path.stem) else "qualification")
        assemblies.append(record)
        project_count += 1
        if record["classification"] != "kernel":
            continue
        for reference in record["references"]:
            assertions += 1
            referenced = Path(reference.replace("\\", "/").rstrip("/")).stem
            if is_forbidden(referenced):
                violations.append({
                    "assembly": record["name"], "path": record["path"], "reference": reference,
                    "rule": "P-001 / 04 s2: a kernel dotnet project never references a gameplay, rules,"
                            " qualification or generated project",
                })
    for path in sorted_files(root / "dotnet/tests", "*.csproj"):
        assemblies.append(read_project(path, "qualification"))
        project_count += 1

    # 5. The kernel sources: a genre token in a kernel file is a finding even with no reference.
    for package in KERNEL_PACKAGES:
        directory = root / package
        if not directory.is_dir():
            continue
        for path in sorted_files(directory, "*.cs"):
            kernel_source_count += 1
            for line_number, line in enumerate(
                    stripped_source(path.read_text(encoding="utf-8")).splitlines(), start=1):
                for token in FORBIDDEN_TOKENS:
                    if token in line:
                        kernel_tokens.append({
                            "path": relative(root, path), "line": line_number, "token": token,
                        })

    assemblies.sort(key=lambda record: (record["name"], record["path"]))
    violations.sort(key=lambda item: (item["assembly"], item["reference"]))
    kernel_tokens.sort(key=lambda item: (item["path"], item["line"], item["token"]))
    missing.sort()

    kernel_assemblies = sorted({r["name"] for r in assemblies if r["classification"] == "kernel"})
    family_assemblies = sorted({r["name"] for r in assemblies if r["classification"] == "family"})
    families_on_kernel = sorted({
        record["name"]
        for record in assemblies
        if record["classification"] == "family"
        and any(is_kernel(reference) for reference in record["references"])
    })
    counts = {
        "kernelAssemblies": sum(1 for r in assemblies if r["classification"] == "kernel"),
        "familyAssemblies": sum(1 for r in assemblies if r["classification"] == "family"),
        "generatedAssemblies": sum(1 for r in assemblies if r["classification"] == "generated"),
        "qualificationAssemblies": sum(1 for r in assemblies if r["classification"] == "qualification"),
        "asmdefs": asmdef_count,
        "projectFiles": project_count,
        "kernelSourcesScanned": kernel_source_count,
        "referenceAssertions": assertions,
        "violations": len(violations),
        "kernelGenreTokens": len(kernel_tokens),
        "missingPackages": len(missing),
        "familiesOnKernel": len(families_on_kernel),
    }
    clean = (
        not violations
        and not kernel_tokens
        and not missing
        and asmdef_count > 0
        and project_count > 0
        and kernel_source_count > 0
        and assertions > 0
    )
    verdict = (
        f"kernel={counts['kernelAssemblies']}; family={counts['familyAssemblies']};"
        f" generated={counts['generatedAssemblies']}; qualification={counts['qualificationAssemblies']};"
        f" asmdefs={asmdef_count}; projectFiles={project_count}; kernelSources={kernel_source_count};"
        f" referenceAssertions={assertions}; violations={len(violations)};"
        f" kernelGenreTokens={len(kernel_tokens)}; missingPackages={len(missing)};"
        f" familiesOnKernel={len(families_on_kernel)}/{counts['familyAssemblies']};"
        f" clean={'true' if clean else 'false'}"
    )
    return {
        "format": FORMAT_NAME,
        "task": TASK_ID,
        "requirement": "P-001, P-057, P-059; 04 s2 (assembly dependency direction)",
        "rule": "no kernel assembly references a gameplay, rules, qualification or generated assembly, no dotnet"
                " kernel project references one, no kernel source names a genre type; and every gameplay/rules"
                " assembly references the kernel",
        "generator": "tools/gc024_genre_audit.py (the host-side twin of"
                     " GameCore.ReferenceConformance.AssemblyReferenceAudit)",
        "clean": clean,
        "counts": counts,
        "kernelAssemblies": kernel_assemblies,
        "familyAssemblies": family_assemblies,
        "familiesReferencingKernel": families_on_kernel,
        "assemblies": assemblies,
        "violations": violations,
        "kernelGenreTokens": kernel_tokens,
        "missingPackages": missing,
        "forbiddenKernelReferencePrefixes": list(FORBIDDEN_PREFIXES),
        "forbiddenKernelTokens": list(FORBIDDEN_TOKENS),
        "verdict": verdict,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=None, help="repository root (default: the tree above this script)")
    parser.add_argument("--out", default=DEFAULT_OUT, help=f"artifact path relative to the root ({DEFAULT_OUT})")
    parser.add_argument("--stdout", action="store_true", help="print the document instead of writing it")
    args = parser.parse_args()

    root = Path(args.root).resolve() if args.root else Path(__file__).resolve().parents[1]
    if not (root / "docs/game-core/traceability.json").is_file() or not (root / "dotnet/GameCore.sln").is_file():
        print(f"gc024_genre_audit.py: {root} is not a repository root", file=sys.stderr)
        return 2

    document = audit(root)
    text = json.dumps(document, indent=2, sort_keys=False) + "\n"
    if args.stdout:
        print(text, end="")
    else:
        out = root / args.out
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(text, encoding="utf-8")
        print(f"gc024 genre audit: {out}")
    print(document["verdict"])
    return 0 if document["clean"] else 1


if __name__ == "__main__":
    sys.exit(main())
