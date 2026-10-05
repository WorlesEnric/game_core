#!/usr/bin/env python3
"""Stage step (b): the repository's checkers, applied to one staging slot (P2.4). Interpreter-level, not a build.

The repository checkers audit fixed repository paths (`tools/check_game_core_csharp.py` has a TARGETS list,
`tools/check_package_metadata.py` discovers `com.gamecore.*` packages), so a staged mechanism package that lives in a
slot under ~/.cache would never be seen by them. This script imports both checkers unchanged and runs THEIR rule
functions on the slot's candidate package, plus `tools/check_stage_slot.py` on the slot:

  * C# (check_game_core_csharp.py): brace balance, missing returns, the forbidden C# 10+ constructs, TODO/FIXME,
    `#nullable enable`, the Studio/gameplay Editor-scope rule (a mechanism is a gameplay package, so UnityEditor is
    legal only in Editor code), and the engine-free rule for every assembly the package marks
    `noEngineReferences: true`;
  * package metadata (check_package_metadata.py): the exact dependency set its asmdefs require (the same
    `expected_dependencies` derivation, against the source repository's packages, engine pins and the games
    allowlist), declared == expected with exact versions, displayName/description present, `unity: 6000.0`;
  * the slot itself (check_stage_slot.py): allowlisted manifest, candidate = declared outputs, inputs unchanged.

No rule is relaxed. A mechanism package is not a `com.gamecore.*` package, so the release-version rule (1.0.0) and
the lock-source rule do not apply to it: it is never locked in a committed project lock before admission.

usage: slot-checks.py --slot <slot-dir> [--repo <repo root>]
       slot-checks.py --package-dir <dir> --package <name> [--repo <repo root>]
The second form runs the same C# and metadata rules on an installed package (Studio's admission re-runs the checkers
on the live project after the package is copied in; there is no slot then, so check_stage_slot.py is skipped).
The last stdout line is JSON `{ok, problems, csharpFiles, asmdefs, slotProblems}`; exit 1 when a problem exists.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    if spec is None or spec.loader is None:
        raise SystemExit(f"slot-checks.py: cannot load {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def csharp_checks(ccs, package_dir: Path, package_name: str, problems: list[str]) -> int:
    files = sorted(p for p in package_dir.rglob("*.cs") if p.is_file())
    engine_free_dirs = []
    for asmdef in sorted(package_dir.rglob("*.asmdef")):
        try:
            document = json.loads(asmdef.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, ValueError) as error:
            problems.append(f"{asmdef.relative_to(package_dir)}: unreadable asmdef: {error}")
            continue
        if document.get("noEngineReferences") is True:
            engine_free_dirs.append(asmdef.parent)
    for path in files:
        rel = Path(package_name) / path.relative_to(package_dir)
        # The gameplay Editor-scope rule keys on the repository path prefix of a gameplay package; a mechanism is
        # a gameplay package, so its files are presented under that prefix (the rule itself is unchanged).
        scoped = Path("Packages/com.gamecore.gameplay.mechanism") / path.relative_to(package_dir)
        text = path.read_text(encoding="utf-8")
        local: list[str] = []
        ccs.check_balance(rel, text, local)
        stripped = ccs.strip_code(text)
        ccs.check_missing_return(rel, stripped, local)
        for name, pattern in ccs.FORBIDDEN.items():
            if pattern.search(stripped) or pattern.search(text):
                local.append(f"{rel}: contains forbidden construct: {name}")
        if "TODO" in stripped or "FIXME" in stripped:
            local.append(f"{rel}: contains TODO/FIXME")
        if not text.startswith("#nullable enable") and "#nullable" not in text:
            local.append(f"{rel}: missing '#nullable enable'")
        scope: list[str] = []
        ccs.check_editor_scope(path, scoped, stripped, scope)
        local.extend(item.replace(str(scoped), str(rel)) for item in scope)
        for base in engine_free_dirs:
            if base in path.parents and ccs.ENGINE_TYPES.search(stripped):
                local.append(f"{rel}: engine type reference in an engine-free assembly ({base.name})")
        problems.extend(local)
    return len(files)


def metadata_checks(cpm, repo: Path, package_dir: Path, package_name: str, problems: list[str]) -> int:
    manifest = json.loads((package_dir / "package.json").read_text(encoding="utf-8"))
    # Every com.gamecore.* package lives in the repository's Packages/ (P0.2 layout); walking only that directory keeps
    # the check off the large artifacts/ tree. The discovery function itself is the checker's own.
    packages = cpm.discover_packages(repo / "Packages")
    assemblies = cpm.discover_asmdefs(packages)
    own = cpm.discover_asmdefs({package_name: {"dir": package_dir}})
    for assembly, owner in own.items():
        if assembly in assemblies:
            problems.append(f"{package_name}: assembly {assembly} is already defined by {assemblies[assembly]}")
        assemblies[assembly] = owner
    project = cpm.project_assemblies(repo)
    engine = cpm.engine_versions_from_manifest(repo)
    allowlist, _ = cpm.allowlist_versions_from_games(repo, engine)
    expected, unknown = cpm.expected_dependencies(package_dir, assemblies, project, package_name, engine, allowlist)
    declared = manifest.get("dependencies") or {}
    if not isinstance(declared, dict):
        problems.append(f"{package_name}: dependencies must be an object")
        declared = {}
    for path, reference, why in unknown:
        problems.append(f"{package_name}: {path.relative_to(package_dir)}: {reference} -> {why}")
    for key in sorted(set(expected) - set(declared)):
        problems.append(f"{package_name}: asmdef references {key} but the manifest does not declare it")
    for key in sorted(set(declared) - set(expected)):
        problems.append(f"{package_name}: the manifest declares {key} but no asmdef references it")
    for key in sorted(set(declared) & set(expected)):
        if declared[key] != expected[key]:
            problems.append(f"{package_name}: {key} is pinned {declared[key]!r}, expected {expected[key]!r}")
    for field in ("displayName", "description", "version"):
        if not isinstance(manifest.get(field), str) or not manifest[field].strip():
            problems.append(f"{package_name}: {field} is missing or empty")
    if manifest.get("unity") != "6000.0":
        problems.append(f"{package_name}: unity is {manifest.get('unity')!r}, expected '6000.0'")
    return len(own)


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--slot")
    parser.add_argument("--package-dir")
    parser.add_argument("--package")
    parser.add_argument("--repo")
    args = parser.parse_args(argv[1:])
    if bool(args.slot) == bool(args.package_dir) or (args.package_dir and not args.package):
        parser.error("use --slot DIR, or --package-dir DIR with --package NAME")
    problems: list[str] = []
    slot_problems: list[str] = []
    if args.slot:
        slot = Path(args.slot).expanduser().resolve()
        record = json.loads((slot / "stage.json").read_text(encoding="utf-8"))
        repo = Path(args.repo or (record.get("source") or {}).get("repo") or HERE.parents[1]).resolve()
        package_name = record["package"]["name"]
        package_dir = slot / "project" / "Packages" / package_name
    else:
        repo = Path(args.repo or HERE.parents[1]).resolve()
        package_name = args.package
        package_dir = Path(args.package_dir).expanduser().resolve()
    tools = repo / "tools"
    ccs = load_module("check_game_core_csharp", tools / "check_game_core_csharp.py")
    cpm = load_module("check_package_metadata", tools / "check_package_metadata.py")
    if args.slot:
        css = load_module("check_stage_slot", tools / "check_stage_slot.py")
        slot_problems = css.check_slot(slot)
        problems.extend(f"slot: {p}" for p in slot_problems)
    csharp_files = csharp_checks(ccs, package_dir, package_name, problems)
    asmdefs = metadata_checks(cpm, repo, package_dir, package_name, problems)
    for problem in problems:
        print(f"  - {problem}")
    print(f"slot-checks: {csharp_files} C# file(s), {asmdefs} asmdef(s), {len(problems)} problem(s)")
    print(json.dumps({"ok": not problems, "problems": problems, "csharpFiles": csharp_files, "asmdefs": asmdefs,
                      "slotProblems": len(slot_problems)}, sort_keys=True))
    return 0 if not problems else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
