#!/usr/bin/env python3
"""Audit local package metadata and asmdef references (interpreter-level; NOT a build).

GC-029 owns packaging metadata. Two things make a local package manifest wrong in a way nothing else
catches:

  * a **version**: a package that says `0.1.0` while a consumer requests `1.0.0` does not fail the C#
    compiler, it fails Unity's *resolver*, which is a step the repository only exercises on the build
    host. A clean checkout is the first place that shows up, so it has to be checkable without Unity;
  * a **missing or extra dependency**: an `asmdef` reference to an assembly that no package in the
    project provides resolves only because some unrelated package happens to pull the provider in, and
    a dependency nothing references makes every consumer install a package it does not need.

This tool derives the expected dependency set from the actual `asmdef` references instead of from a
hard-coded table, so it cannot drift from the sources it audits:

  1. every directory holding a `package.json` whose `name` starts with `com.gamecore.` is a package;
  2. every `.asmdef` under a package belongs to that package, and its `name` defines an assembly;
  3. a reference from assembly A to assembly B means A's package must declare B's package, and must
     declare nothing it does not reference (an exact set, not a superset);
  4. `versionDefines` entries are explicitly NOT references: they declare an *optional* dependency on a
     qualification marker package, and requiring one would force every shipping project to install the
     fault/telemetry switch that a release build is defined by omitting;
  5. the Editor-only test runners (`UnityEngine.TestRunner`, `UnityEditor.TestRunner`, `nunit.framework`)
     are provided by the project, never by a package;
  6. an `asmdef` that references an assembly defined outside every package (in the Unity project's own
     `Assets/`) is a reverse dependency and a defect, because a reusable package may not depend on the
     project that embeds it.

Engine package versions are read from the qualification project's own manifest, so the pins live in
exactly one place.

Usage:
    python3 tools/check_package_metadata.py [--json <path>] [--sync-lock] [--self-test]

`--sync-lock` rewrites the `com.gamecore.*` dependency maps of
`unity/GameCore.Validation/Packages/packages-lock.json` from the manifests. Unity regenerates that file
on import; mirroring it here is what keeps a committed lock honest between imports, and it is a no-op
once the two agree.

Exit codes: 0 every invariant holds; 1 at least one problem; 2 a missing prerequisite.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent
LOCK = ROOT / "unity/GameCore.Validation/Packages/packages-lock.json"
MANIFEST = ROOT / "unity/GameCore.Validation/Packages/manifest.json"

# The pinned release version of every Game Core package on this revision. A single revision ships one
# version, so the expectation is a constant rather than an inference.
RELEASE_VERSION = "1.0.0"

# Assemblies Unity itself supplies, mapped to the package that ships them. A package referencing one of
# these must declare it. Anything else prefixed `Unity`/`UnityEngine`/`UnityEditor` is a project-provided
# test runner or an editor module and is not a package dependency.
ENGINE_ASSEMBLIES = {
    "Unity.Burst": "com.unity.burst",
    "Unity.Collections": "com.unity.collections",
    "Unity.Entities": "com.unity.entities",
    "Unity.Mathematics": "com.unity.mathematics",
}
PROJECT_PROVIDED = {
    "nunit.framework",
    "nunit.framework.dll",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner",
}

# The kernel: packages that must never gain a gameplay dependency (P-057, 04 section 2).
KERNEL_PACKAGES = {
    "com.gamecore.contracts",
    "com.gamecore.composition",
    "com.gamecore.derivation",
    "com.gamecore.planning",
    "com.gamecore.content.compiler",
    "com.gamecore.rules.cards",
    "com.gamecore.rules.narrative",
    "com.gamecore.rules.traversal",
}

PRUNED_DIRS = {".git", "Library", "Temp", "Logs", "Builds", "UserSettings", "bin", "obj", "obj~",
               "Artifacts~", "node_modules", ".vs", ".idea"}
ASMDEF_REFERENCE = re.compile(r'"references"\s*:\s*\[(.*?)\]', re.S)
ASMDEF_NAME = re.compile(r'"name"\s*:\s*"([^"]+)"')


def walk(root: Path):
    """Every file under root, pruning directories Unity generates and build output."""
    for base, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in PRUNED_DIRS)
        for name in sorted(files):
            yield Path(base) / name


def load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise SystemExit(f"check_package_metadata.py: cannot read {path}: {error}")


def discover_packages(root: Path):
    """Returns {package name: {'dir': Path, 'manifest': dict}} for every local Game Core package."""
    packages = {}
    for path in walk(root):
        if path.name != "package.json" or path.parent.name.endswith(".meta"):
            continue
        manifest = load_json(path)
        name = manifest.get("name")
        if not isinstance(name, str) or not name.startswith("com.gamecore."):
            continue
        packages[name] = {"dir": path.parent, "manifest": manifest, "path": path}
    return packages


def discover_asmdefs(packages):
    """Returns {assembly name: package name} for every assembly a package defines."""
    assemblies = {}
    for name, package in packages.items():
        for path in walk(package["dir"]):
            if path.suffix != ".asmdef":
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            match = ASMDEF_NAME.search(text)
            if not match:
                continue
            assemblies[match[1]] = name
    return assemblies


def project_assemblies():
    """Every assembly defined outside a package: the Unity project's own Assets trees."""
    assemblies = set()
    for project in sorted((ROOT / "unity").glob("*/Assets")):
        for path in walk(project):
            if path.suffix != ".asmdef":
                continue
            match = ASMDEF_NAME.search(path.read_text(encoding="utf-8", errors="replace"))
            if match:
                assemblies.add(match[1])
    return assemblies


def referenced_assemblies(path: Path):
    """The `references` array of one asmdef. Deliberately ignores `versionDefines` (rule 4 above)."""
    text = path.read_text(encoding="utf-8", errors="replace")
    match = ASMDEF_REFERENCE.search(text)
    if not match:
        return []
    return re.findall(r'"([^"]+)"', match[1])


def expected_dependencies(package_dir: Path, assemblies, project, package_name, engine_versions):
    """The exact gamecore/engine dependency set one package's asmdefs require."""
    expected = {}
    unknown = []
    for path in walk(package_dir):
        if path.suffix != ".asmdef":
            continue
        for reference in referenced_assemblies(path):
            if reference in PROJECT_PROVIDED:
                continue
            if reference in ENGINE_ASSEMBLIES:
                engine = ENGINE_ASSEMBLIES[reference]
                if engine not in engine_versions:
                    unknown.append((path, reference, "engine package version is not pinned in the manifest"))
                    continue
                expected[engine] = engine_versions[engine]
                continue
            if reference in assemblies:
                if assemblies[reference] != package_name:
                    expected[assemblies[reference]] = RELEASE_VERSION
                continue
            if reference in project:
                unknown.append((path, reference, "a package may not reference the embedding project's assembly"))
                continue
            unknown.append((path, reference, "no package or project assembly defines this reference"))
    return expected, unknown


def check_manifests(root, packages, assemblies, project, engine_versions):
    """Every manifest-level rule. Returns (problems, report)."""
    problems = []
    report = []
    for name in sorted(packages):
        package = packages[name]
        manifest = package["manifest"]
        directory = package["dir"]
        if (root / "Packages").resolve() in directory.resolve().parents:
            if directory.name != name:
                problems.append(f"{name}: directory name {directory.name!r} does not match the package name")
        if manifest.get("version") != RELEASE_VERSION:
            problems.append(f"{name}: version is {manifest.get('version')!r}, expected {RELEASE_VERSION!r}")
        for field in ("displayName", "description"):
            if not isinstance(manifest.get(field), str) or not manifest[field].strip():
                problems.append(f"{name}: {field} is missing or empty")
        if manifest.get("unity") != "6000.0":
            problems.append(f"{name}: unity is {manifest.get('unity')!r}, expected '6000.0'")

        expected, unknown = expected_dependencies(
            directory, assemblies, project, name, engine_versions)
        declared = manifest.get("dependencies", {})
        if not isinstance(declared, dict):
            problems.append(f"{name}: dependencies must be an object")
            declared = {}

        for path, reference, why in unknown:
            problems.append(f"{name}: {path.relative_to(root)}: {reference} -> {why}")

        for key in sorted(set(expected) - set(declared)):
            problems.append(f"{name}: asmdef references {key} but the manifest does not declare it")
        for key in sorted(set(declared) - set(expected)):
            problems.append(f"{name}: the manifest declares {key} but no asmdef references it")
        for key in sorted(set(declared) & set(expected)):
            if declared[key] != expected[key]:
                problems.append(
                    f"{name}: {key} is pinned {declared[key]!r}, expected {expected[key]!r}")

        if name in KERNEL_PACKAGES:
            for key in sorted(declared):
                if key.startswith("com.gamecore.gameplay."):
                    problems.append(f"{name}: kernel package depends on gameplay package {key}")

        report.append({
            "package": name,
            "version": manifest.get("version"),
            "directory": str(directory.relative_to(root)),
            "declared": dict(sorted(declared.items())),
            "expected": dict(sorted(expected.items())),
            "kernel": name in KERNEL_PACKAGES,
        })
    return problems, report


def engine_versions_from_manifest():
    """The engine pins, read from the qualification project's own manifest (one source of truth)."""
    dependencies = load_json(MANIFEST).get("dependencies", {})
    return {name: version for name, version in dependencies.items()
            if name in set(ENGINE_ASSEMBLIES.values()) and not str(version).startswith("file:")}


def lock_report(packages):
    """Compare the committed lock's com.gamecore.* dependency maps with the manifests."""
    if not LOCK.exists():
        return [], None
    lock = load_json(LOCK)
    corrections = {}
    problems = []
    for name in sorted(packages):
        entry = lock.get("dependencies", {}).get(name)
        if entry is None:
            problems.append(f"packages-lock.json: {name} is not locked")
            continue
        declared = packages[name]["manifest"].get("dependencies", {})
        expected = {key: value for key, value in declared.items()
                    if key.startswith("com.gamecore.")}
        actual = entry.get("dependencies", {})
        actual_gamecore = {key: value for key, value in actual.items()
                           if key.startswith("com.gamecore.")}
        if actual_gamecore != expected:
            corrections[name] = expected
            problems.append(
                f"packages-lock.json: {name} dependency map disagrees with its manifest "
                f"(locked {actual_gamecore}, manifest {expected})")
    return problems, corrections


def sync_lock(corrections):
    """Rewrite the lock's gamecore dependency maps, preserving key order and formatting."""
    if not corrections:
        return 0
    lock = load_json(LOCK)
    for name, expected in corrections.items():
        lock["dependencies"][name]["dependencies"] = expected
    LOCK.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8")
    print(f"-- packages-lock.json: rewrote the com.gamecore.* dependency map of {len(corrections)} package(s)")
    return 1


def self_test():
    """Falsify the two rules that matter, on a synthetic package tree, with no repository involved."""
    import tempfile
    failures = 0

    def check(label, condition, detail=""):
        nonlocal failures
        if condition:
            print(f"   ok   {label}")
        else:
            failures += 1
            print(f"   FAIL {label} {detail}")

    with tempfile.TemporaryDirectory() as tmp:
        root = Path(tmp)
        # Real package names, so the kernel rule is exercised against its actual data set.
        kernel = root / "Packages/com.gamecore.kernel"
        user = root / "Packages/com.gamecore.rules.cards"
        gameplay = root / "Packages/com.gamecore.gameplay.cards"
        for directory in (kernel, user, gameplay):
            directory.mkdir(parents=True)

        def manifest(directory, name, dependencies=None, version="1.0.0"):
            (directory / "package.json").write_text(json.dumps({
                "name": name, "version": version, "displayName": name, "description": "d",
                "unity": "6000.0", "dependencies": dependencies or {}}), encoding="utf-8")

        def asmdef(directory, name, references):
            (directory / (name.split(".")[-1] + ".asmdef")).write_text(json.dumps({
                "name": name, "references": references}), encoding="utf-8")

        # A kernel package and a gameplay package, each defining one assembly.
        manifest(kernel, "com.gamecore.kernel")
        asmdef(kernel, "GameCore.Kernel", [])
        manifest(gameplay, "com.gamecore.gameplay.cards")
        asmdef(gameplay, "GameCore.Gameplay.Cards", [])

        def write_user(dependencies, references, version="1.0.0"):
            """The package under test is com.gamecore.rules.cards, a real kernel package name."""
            manifest(user, "com.gamecore.rules.cards", dependencies, version)
            asmdef(user, "GameCore.Rules.Cards", references)

        def analyse(project=None, engine=None):
            """Re-read the tree the way a real run does: a fixture is only a fixture once read back."""
            fresh = discover_packages(root)
            return check_manifests(root, fresh, discover_asmdefs(fresh),
                                   project or set(), engine or {})[0]

        # Every fixture exists before discovery, so this is the real discovery contract under test.
        write_user({}, ["GameCore.Kernel"])
        packages = discover_packages(root)
        assemblies = discover_asmdefs(packages)
        check("every package directory is discovered",
              set(packages) == {"com.gamecore.kernel", "com.gamecore.rules.cards",
                                "com.gamecore.gameplay.cards"},
              str(sorted(packages)))
        check("an asmdef defines its own package's assembly",
              assemblies.get("GameCore.Rules.Cards") == "com.gamecore.rules.cards", str(assemblies))

        problems = analyse()
        check("a referenced assembly with no declared dependency is reported",
              any("does not declare it" in p for p in problems), str(problems))

        write_user({"com.gamecore.kernel": "1.0.0"}, [])
        problems = analyse()
        check("a declared dependency nothing references is reported",
              any("no asmdef references it" in p for p in problems), str(problems))

        write_user({"com.gamecore.kernel": "0.1.0"}, ["GameCore.Kernel"])
        problems = analyse()
        check("a stale dependency version is reported",
              any("pinned '0.1.0'" in p for p in problems), str(problems))

        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"])
        problems = analyse()
        check("an exactly correct package produces no problem", problems == [], str(problems))

        write_user({"com.gamecore.gameplay.cards": "1.0.0"}, ["GameCore.Gameplay.Cards"])
        problems = analyse()
        check("a kernel package depending on a gameplay package is reported",
              any("kernel package depends on gameplay package" in p for p in problems), str(problems))

        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.ProjectOwned"])
        problems = analyse(project={"GameCore.ProjectOwned"})
        check("a reference to the embedding project's own assembly is reported",
              any("may not reference the embedding project" in p for p in problems), str(problems))

        write_user({"com.gamecore.engine": "2.0.0"}, ["Unity.Entities"])
        problems = analyse(engine={"com.unity.entities": "1.4.6"})
        check("an engine reference resolves to its pinned package",
              any("does not declare it" in p for p in problems), str(problems))

        write_user({"com.unity.entities": "1.4.6"}, ["Unity.Entities"])
        problems = analyse(engine={"com.unity.entities": "1.4.6"})
        check("a correct engine dependency produces no problem", problems == [], str(problems))

        write_user({}, ["UnityEngine.TestRunner"])
        problems = analyse()
        check("a project-provided test runner is not a package dependency", problems == [], str(problems))

        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"], version="0.1.0")
        problems = analyse()
        check("a package version other than the release version is reported",
              any("version is '0.1.0'" in p for p in problems), str(problems))

    if failures:
        print(f"check_package_metadata.py --self-test FAILED ({failures} case(s))")
        return 1
    print("check_package_metadata.py --self-test passed (12 cases).")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--json", metavar="PATH", help="write the report here as well")
    parser.add_argument("--sync-lock", action="store_true",
                        help="rewrite the lock's com.gamecore.* dependency maps from the manifests")
    parser.add_argument("--self-test", action="store_true",
                        help="falsify this tool's own rules on a synthetic package tree and exit")
    parser.add_argument("--root", default=str(ROOT), help="repository root (default: this file's parent)")
    args = parser.parse_args(argv[1:])

    if args.self_test:
        return self_test()

    root = Path(args.root).resolve()
    packages = discover_packages(root)
    if not packages:
        print(f"check_package_metadata.py: no com.gamecore.* package found under {root}", file=sys.stderr)
        return 2
    assemblies = discover_asmdefs(packages)
    project = project_assemblies()
    engine = engine_versions_from_manifest()

    problems, report = check_manifests(root, packages, assemblies, project, engine)
    lock_problems, corrections = lock_report(packages)
    if args.sync_lock and corrections:
        sync_lock(corrections)
        lock_problems, corrections = lock_report(packages)
    problems += lock_problems

    if args.json:
        Path(args.json).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json).write_text(json.dumps({
            "packages": report,
            "assemblies": dict(sorted(assemblies.items())),
            "engine_pins": engine,
            "problems": problems,
        }, indent=2) + "\n", encoding="utf-8")

    print(f"packages inspected: {len(packages)}; assemblies defined by packages: {len(assemblies)}; "
          f"engine pins: {len(engine)}")
    print(f"release version expected: {RELEASE_VERSION}; kernel packages: {len(KERNEL_PACKAGES)}")
    if problems:
        print(f"check_package_metadata.py FAILED ({len(problems)} problem(s)):", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1
    print("package metadata and asmdef-derived dependencies agree.")
    print("No Unity resolve, no compilation and no player build was executed by this check.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
