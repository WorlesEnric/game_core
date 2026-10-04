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

SADR-014 (docs/studio/02-architecture.md section 8) extends the rules for the Studio and the games under
`games/` without loosening them for the kernel:

  7. `ENGINE_ALLOWLIST` names the additional engine/project assemblies a package may reference (Input System,
     AI Navigation, URP, Unity.Transforms, TextMeshPro/uGUI). Each maps to the package that ships it, and the
     version is the one pinned by the games projects (`games/*/Packages/manifest.json`, else the version their
     committed lock resolved). A kernel engine package (`ENGINE_ASSEMBLIES`) keeps the qualification pin, and a
     games manifest that pins one differently is a problem;
  8. built-in engine modules (`BUILTIN_MODULES`, e.g. `UnityEngine.UIElementsModule`) need no package
     dependency;
  9. `precompiledReferences` other than the project-provided test runner are accepted only for packages named
     `com.gamecore.studio.*` or `com.gamecore.gameplay.*`, only for DLLs in `PRECOMPILED_ASSEMBLIES`, and they
     require the DLL's package (e.g. `Newtonsoft.Json.dll` -> `com.unity.nuget.newtonsoft-json`);
 10. a package must be locked in at least one of `unity/GameCore.Validation/Packages/packages-lock.json` and
     `games/*/Packages/packages-lock.json`, and every lock that contains it must carry its exact
     `com.gamecore.*` dependency map;
 11. a kernel package may not depend on a gameplay or Studio package (`com.gamecore.gameplay.*`,
     `com.gamecore.studio.*`, `com.gamecore.rules.gameplay`);
 12. an assembly defined in a games project's own `Assets/` is, like the qualification project's, never a
     legal package reference.

Usage:
    python3 tools/check_package_metadata.py [--json <path>] [--sync-lock] [--self-test]

`--sync-lock` rewrites the `com.gamecore.*` dependency maps of
`unity/GameCore.Validation/Packages/packages-lock.json` (and of every `games/*/Packages/packages-lock.json`
that locks the package) from the manifests. Unity regenerates that file
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

# SADR-014: further engine/project assemblies a package may reference, mapped to the package that ships them.
# The version is pinned by the games projects (games/*/Packages/manifest.json, falling back to the version their
# committed lock resolved), except for a package already in ENGINE_ASSEMBLIES (Unity.Transforms ships in
# com.unity.entities), which keeps the qualification manifest's pin.
ENGINE_ALLOWLIST = {
    "Unity.InputSystem": "com.unity.inputsystem",
    "Unity.AI.Navigation": "com.unity.ai.navigation",
    "Unity.RenderPipelines.Universal.Runtime": "com.unity.render-pipelines.universal",
    "Unity.RenderPipelines.Core.Runtime": "com.unity.render-pipelines.core",
    "Unity.TextMeshPro": "com.unity.ugui",
    "UnityEngine.UI": "com.unity.ugui",
    "Unity.Transforms": "com.unity.entities",
    "Unity.Entities.Hybrid": "com.unity.entities",
}

# Built-in engine modules: always present in a Unity project, never a package dependency.
BUILTIN_MODULES = {
    "UnityEngine.UIElementsModule",
    "UnityEngine.UIModule",
    "UnityEngine.AudioModule",
    "UnityEngine.AnimationModule",
    "UnityEngine.AIModule",
    "UnityEngine.PhysicsModule",
    "UnityEngine.ImageConversionModule",
    "UnityEngine.JSONSerializeModule",
    "UnityEngine.UnityWebRequestModule",
    "UnityEngine.ScreenCaptureModule",
    "UnityEngine.VideoModule",
}

# Precompiled DLLs a Studio or gameplay package may name in `precompiledReferences`, mapped to their package.
PRECOMPILED_ASSEMBLIES = {
    "Newtonsoft.Json.dll": "com.unity.nuget.newtonsoft-json",
}
# Only packages with one of these name prefixes may carry precompiled references beyond the test runner.
PRECOMPILED_PREFIXES = ("com.gamecore.studio.", "com.gamecore.gameplay.")

# Dependencies a kernel package may never take (P-057, extended to the Studio by SADR-014).
FORBIDDEN_FOR_KERNEL_PREFIXES = ("com.gamecore.gameplay.", "com.gamecore.studio.")
FORBIDDEN_FOR_KERNEL = {"com.gamecore.rules.gameplay"}

# Unity projects beside the qualification project (SADR-016). Each is a further lock source, and its manifest
# pins the allowlisted engine packages.
GAMES_GLOB = "games/*/Packages"

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

# `.claude` holds Claude Code git worktrees (`.claude/worktrees/<name>`): full checkouts of OTHER branches whose
# packages are not this tree's packages. `target` is Cargo output (studio/agent).
PRUNED_DIRS = {".git", "Library", "Temp", "Logs", "Builds", "UserSettings", "bin", "obj", "obj~",
               "Artifacts~", "node_modules", ".vs", ".idea", ".claude", "target"}
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


def project_assemblies(root: Path = ROOT):
    """Every assembly defined outside a package: the Unity projects' own Assets trees (qualification and games)."""
    assemblies = set()
    for project in sorted(root.glob("unity/*/Assets")) + sorted(root.glob("games/*/Assets")):
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


ASMDEF_PRECOMPILED = re.compile(r'"precompiledReferences"\s*:\s*\[(.*?)\]', re.S)


def precompiled_references(path: Path):
    """The `precompiledReferences` array of one asmdef (DLL file names)."""
    text = path.read_text(encoding="utf-8", errors="replace")
    match = ASMDEF_PRECOMPILED.search(text)
    if not match:
        return []
    return re.findall(r'"([^"]+)"', match[1])


def expected_dependencies(package_dir: Path, assemblies, project, package_name, engine_versions,
                          allowlist_versions=None):
    """The exact gamecore/engine dependency set one package's asmdefs require."""
    allowlist_versions = allowlist_versions or {}
    expected = {}
    unknown = []
    for path in walk(package_dir):
        if path.suffix != ".asmdef":
            continue
        for dll in precompiled_references(path):
            if dll in PROJECT_PROVIDED:
                continue
            if not package_name.startswith(PRECOMPILED_PREFIXES):
                unknown.append((path, dll, "precompiledReferences are accepted only for "
                                           "com.gamecore.studio.* and com.gamecore.gameplay.* packages"))
                continue
            if dll not in PRECOMPILED_ASSEMBLIES:
                unknown.append((path, dll, "precompiled assembly is not in the allowlist"))
                continue
            owner = PRECOMPILED_ASSEMBLIES[dll]
            if owner not in allowlist_versions:
                unknown.append((path, dll, f"{owner} is not pinned by any games/*/Packages manifest or lock"))
                continue
            expected[owner] = allowlist_versions[owner]
        for reference in referenced_assemblies(path):
            if reference in PROJECT_PROVIDED or reference in BUILTIN_MODULES:
                continue
            if reference in ENGINE_ASSEMBLIES:
                engine = ENGINE_ASSEMBLIES[reference]
                if engine not in engine_versions:
                    unknown.append((path, reference, "engine package version is not pinned in the manifest"))
                    continue
                expected[engine] = engine_versions[engine]
                continue
            if reference in ENGINE_ALLOWLIST:
                engine = ENGINE_ALLOWLIST[reference]
                version = engine_versions.get(engine, allowlist_versions.get(engine))
                if version is None:
                    unknown.append((path, reference, f"{engine} is not pinned by the qualification manifest or "
                                                     "any games/*/Packages manifest or lock"))
                    continue
                expected[engine] = version
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


def check_manifests(root, packages, assemblies, project, engine_versions, allowlist_versions=None):
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
            directory, assemblies, project, name, engine_versions, allowlist_versions)
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
                elif key.startswith(FORBIDDEN_FOR_KERNEL_PREFIXES) or key in FORBIDDEN_FOR_KERNEL:
                    problems.append(f"{name}: kernel package depends on gameplay/Studio package {key}")

        report.append({
            "package": name,
            "version": manifest.get("version"),
            "directory": str(directory.relative_to(root)),
            "declared": dict(sorted(declared.items())),
            "expected": dict(sorted(expected.items())),
            "kernel": name in KERNEL_PACKAGES,
        })
    return problems, report


def engine_versions_from_manifest(root: Path = ROOT):
    """The engine pins, read from the qualification project's own manifest (one source of truth)."""
    dependencies = load_json(root / MANIFEST.relative_to(ROOT)).get("dependencies", {})
    return {name: version for name, version in dependencies.items()
            if name in set(ENGINE_ASSEMBLIES.values()) and not str(version).startswith("file:")}


def games_package_dirs(root: Path):
    """`games/<project>/Packages` directories that hold a manifest (SADR-016 projects)."""
    return sorted(path for path in root.glob(GAMES_GLOB) if (path / "manifest.json").is_file())


def allowlist_versions_from_games(root: Path, engine_versions):
    """Pins of the allowlisted engine packages and precompiled-DLL packages, from the games projects.

    A package's version is the one its games manifest declares, else the version that game's committed lock
    resolved (a transitive package such as com.unity.render-pipelines.core). Two games that disagree are a
    problem, and so is a games manifest that pins a kernel engine package (ENGINE_ASSEMBLIES) differently from
    the qualification manifest. Returns (pins, problems).
    """
    kernel_engine = set(ENGINE_ASSEMBLIES.values())
    wanted = (set(ENGINE_ALLOWLIST.values()) | set(PRECOMPILED_ASSEMBLIES.values())) - kernel_engine
    pins, sources, problems = {}, {}, []
    for packages_dir in games_package_dirs(root):
        label = str(packages_dir.relative_to(root))
        manifest = load_json(packages_dir / "manifest.json").get("dependencies", {})
        lock_path = packages_dir / "packages-lock.json"
        lock = load_json(lock_path).get("dependencies", {}) if lock_path.is_file() else {}
        for name in sorted(kernel_engine):
            version = manifest.get(name)
            if version is not None and name in engine_versions and version != engine_versions[name]:
                problems.append(f"{label}/manifest.json: {name} is pinned {version!r}, but the qualification "
                                f"manifest pins {engine_versions[name]!r}")
        for name in sorted(wanted):
            version = manifest.get(name)
            if version is None and isinstance(lock.get(name), dict):
                version = lock[name].get("version")
            if version is None or str(version).startswith("file:"):
                continue
            if name in pins and pins[name] != version:
                problems.append(f"{label}: {name} resolves to {version!r}, but {sources[name]} pins {pins[name]!r}")
                continue
            pins.setdefault(name, version)
            sources.setdefault(name, label)
    return pins, problems


def lock_paths(root: Path = ROOT):
    """Every committed lock a package may be locked in: the qualification project's, then each game's."""
    paths = []
    validation = root / LOCK.relative_to(ROOT)
    if validation.exists():
        paths.append(validation)
    paths.extend(path / "packages-lock.json" for path in games_package_dirs(root)
                 if (path / "packages-lock.json").is_file())
    return paths


def lock_label(root: Path, path: Path):
    """The qualification lock keeps its historical short name in messages; a game's lock is named by path."""
    return "packages-lock.json" if path == root / LOCK.relative_to(ROOT) else str(path.relative_to(root))


def lock_report(packages, root: Path = ROOT):
    """Compare the com.gamecore.* dependency maps of every lock that holds a package with its manifest.

    A package must be locked in at least one lock source, and every lock that holds it must agree. Returns
    (problems, corrections) where corrections maps a lock path to {package: expected map}.
    """
    paths = lock_paths(root)
    if not paths:
        return [], {}
    locks = {path: load_json(path) for path in paths}
    corrections = {}
    problems = []
    for name in sorted(packages):
        holders = [path for path in paths if name in locks[path].get("dependencies", {})]
        if not holders:
            where = "" if len(paths) == 1 else " in any of " + ", ".join(lock_label(root, p) for p in paths)
            problems.append(f"packages-lock.json: {name} is not locked{where}")
            continue
        declared = packages[name]["manifest"].get("dependencies", {})
        expected = {key: value for key, value in declared.items()
                    if key.startswith("com.gamecore.")}
        for path in holders:
            entry = locks[path]["dependencies"][name]
            actual = entry.get("dependencies", {})
            actual_gamecore = {key: value for key, value in actual.items()
                               if key.startswith("com.gamecore.")}
            if actual_gamecore != expected:
                corrections.setdefault(path, {})[name] = expected
                problems.append(
                    f"{lock_label(root, path)}: {name} dependency map disagrees with its manifest "
                    f"(locked {actual_gamecore}, manifest {expected})")
    return problems, corrections


def sync_lock(corrections, root: Path = ROOT):
    """Rewrite the locks' gamecore dependency maps, preserving key order and formatting."""
    if not corrections:
        return 0
    for path, names in corrections.items():
        lock = load_json(path)
        for name, expected in names.items():
            lock["dependencies"][name]["dependencies"] = expected
        path.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8")
        print(f"-- {lock_label(root, path)}: rewrote the com.gamecore.* dependency map of {len(names)} package(s)")
    return 1


def self_test():
    """Falsify the two rules that matter, on a synthetic package tree, with no repository involved."""
    import tempfile
    failures = 0
    cases = 0

    def check(label, condition, detail=""):
        nonlocal failures, cases
        cases += 1
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

        # A Claude Code worktree (another branch's checkout) and generated trees hold packages that are not
        # this tree's; discovery must not see them.
        for skipped in (".claude/worktrees/other/Packages/com.gamecore.unity.app", "Library/PackageCache/x",
                        "studio/agent/target/x", "web/node_modules/x"):
            (root / skipped).mkdir(parents=True)
            manifest(root / skipped, "com.gamecore.unity.app")

        # Every fixture exists before discovery, so this is the real discovery contract under test.
        write_user({}, ["GameCore.Kernel"])
        packages = discover_packages(root)
        check("packages under .claude/, Library/, target/ and node_modules/ are not discovered",
              "com.gamecore.unity.app" not in packages, str(sorted(packages)))
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

        # --- SADR-014: allowlisted engine assemblies, built-in modules, precompiled DLLs, kernel isolation.
        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"])
        studio = root / "Packages/com.gamecore.studio.core"
        studio.mkdir(parents=True)
        games_pins = {"com.unity.inputsystem": "1.19.0", "com.unity.nuget.newtonsoft-json": "3.2.1"}

        def write_studio(dependencies, references, precompiled=None):
            manifest(studio, "com.gamecore.studio.core", dependencies)
            document = {"name": "GameCore.Studio.Model", "references": references}
            if precompiled is not None:
                document["overrideReferences"] = True
                document["precompiledReferences"] = precompiled
            (studio / "Model.asmdef").write_text(json.dumps(document), encoding="utf-8")

        def analyse_sadr(engine=None, pins=None):
            fresh = discover_packages(root)
            return check_manifests(root, fresh, discover_asmdefs(fresh), set(), engine or {},
                                   games_pins if pins is None else pins)[0]

        write_studio({"com.unity.inputsystem": "1.19.0"}, ["Unity.InputSystem"])
        problems = analyse_sadr()
        check("an allowlisted engine assembly resolves to its games-pinned package", problems == [],
              str(problems))

        write_studio({}, ["Unity.InputSystem"])
        problems = analyse_sadr()
        check("an allowlisted engine assembly without its package dependency is reported",
              any("com.unity.inputsystem but the manifest does not declare it" in p for p in problems),
              str(problems))

        write_studio({"com.unity.inputsystem": "1.11.2"}, ["Unity.InputSystem"])
        problems = analyse_sadr()
        check("an allowlisted engine package pinned off the games manifest is reported",
              any("pinned '1.11.2', expected '1.19.0'" in p for p in problems), str(problems))

        write_studio({"com.unity.inputsystem": "1.19.0"}, ["Unity.InputSystem"])
        problems = analyse_sadr(pins={})
        check("an allowlisted engine package no games project pins is reported",
              any("is not pinned by the qualification manifest" in p for p in problems), str(problems))

        write_studio({"com.unity.entities": "1.4.6"}, ["Unity.Transforms"])
        problems = analyse_sadr(engine={"com.unity.entities": "1.4.6"})
        check("Unity.Transforms resolves to com.unity.entities at the qualification pin", problems == [],
              str(problems))

        write_studio({}, ["UnityEngine.UIElementsModule"])
        problems = analyse_sadr()
        check("a built-in engine module needs no package dependency", problems == [], str(problems))

        write_studio({"com.unity.nuget.newtonsoft-json": "3.2.1"}, [], precompiled=["Newtonsoft.Json.dll"])
        problems = analyse_sadr()
        check("a Studio package's allowlisted precompiled DLL resolves to its package", problems == [],
              str(problems))

        write_studio({}, [], precompiled=["Newtonsoft.Json.dll"])
        problems = analyse_sadr()
        check("a precompiled DLL without its package dependency is reported",
              any("com.unity.nuget.newtonsoft-json but the manifest does not declare it" in p
                  for p in problems), str(problems))

        write_studio({}, [], precompiled=["Evil.dll"])
        problems = analyse_sadr()
        check("a precompiled DLL outside the allowlist is reported",
              any("precompiled assembly is not in the allowlist" in p for p in problems), str(problems))
        write_studio({}, [])

        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"], )
        (user / "Cards.asmdef").write_text(json.dumps({
            "name": "GameCore.Rules.Cards", "references": ["GameCore.Kernel"], "overrideReferences": True,
            "precompiledReferences": ["Newtonsoft.Json.dll"]}), encoding="utf-8")
        problems = analyse_sadr()
        check("a precompiled DLL in a package outside studio/gameplay is reported",
              any("accepted only for com.gamecore.studio.*" in p for p in problems), str(problems))

        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"])
        (user / "Cards.asmdef").write_text(json.dumps({
            "name": "GameCore.Rules.Cards", "references": ["GameCore.Kernel"], "overrideReferences": True,
            "precompiledReferences": ["nunit.framework.dll"]}), encoding="utf-8")
        problems = analyse_sadr()
        check("the project-provided nunit DLL stays legal in every package", problems == [], str(problems))

        write_user({"com.gamecore.studio.core": "1.0.0"}, ["GameCore.Studio.Model"])
        problems = analyse_sadr()
        check("a kernel package depending on a Studio package is reported",
              any("kernel package depends on gameplay/Studio package com.gamecore.studio.core" in p
                  for p in problems), str(problems))
        write_user({"com.gamecore.kernel": "1.0.0"}, ["GameCore.Kernel"])

        # --- games manifests: allowlist pins, kernel engine pin agreement.
        game = root / "games/demo/Packages"
        game.mkdir(parents=True)
        (game / "manifest.json").write_text(json.dumps({"dependencies": {
            "com.unity.inputsystem": "1.19.0", "com.unity.entities": "1.4.5"}}), encoding="utf-8")
        (game / "packages-lock.json").write_text(json.dumps({"dependencies": {
            "com.unity.render-pipelines.core": {"version": "17.0.4", "depth": 1, "source": "builtin"}}}),
            encoding="utf-8")
        pins, pin_problems = allowlist_versions_from_games(root, {"com.unity.entities": "1.4.6"})
        check("a games manifest pins an allowlisted package; a transitive one comes from its lock",
              pins == {"com.unity.inputsystem": "1.19.0", "com.unity.render-pipelines.core": "17.0.4"},
              str(pins))
        check("a games manifest pinning a kernel engine package differently is reported",
              any("com.unity.entities is pinned '1.4.5'" in p for p in pin_problems), str(pin_problems))

        # --- lock sources: the qualification lock plus every games lock.
        validation = root / "unity/GameCore.Validation/Packages"
        validation.mkdir(parents=True)
        fresh = discover_packages(root)

        def lock_entry(dependencies):
            return {"version": "file:x", "depth": 0, "source": "local", "dependencies": dependencies}

        everything = {name: lock_entry({}) for name in fresh}
        everything["com.gamecore.rules.cards"] = lock_entry({"com.gamecore.kernel": "1.0.0"})
        without_studio = {k: v for k, v in everything.items() if k != "com.gamecore.studio.core"}
        (validation / "packages-lock.json").write_text(json.dumps({"dependencies": without_studio}),
                                                       encoding="utf-8")
        (game / "packages-lock.json").write_text(json.dumps({"dependencies": {
            "com.gamecore.studio.core": lock_entry({})}}), encoding="utf-8")
        problems, _ = lock_report(fresh, root)
        check("a package locked only in a games lock satisfies the lock rule", problems == [], str(problems))

        (game / "packages-lock.json").write_text(json.dumps({"dependencies": {}}), encoding="utf-8")
        problems, _ = lock_report(fresh, root)
        check("a package locked in no lock source is reported",
              any("com.gamecore.studio.core is not locked in any of" in p for p in problems), str(problems))

        stale = dict(everything)
        stale["com.gamecore.rules.cards"] = lock_entry({})
        (game / "packages-lock.json").write_text(json.dumps({"dependencies": stale}), encoding="utf-8")
        problems, corrections = lock_report(fresh, root)
        check("a games lock whose dependency map disagrees is reported against that lock",
              any(p.startswith("games/demo/Packages/packages-lock.json: com.gamecore.rules.cards")
                  for p in problems) and list(corrections) == [game / "packages-lock.json"],
              str(problems))
        sync_lock(corrections, root)
        problems, corrections = lock_report(fresh, root)
        check("--sync-lock repairs the games lock it reported", problems == [] and not corrections,
              str(problems))

    if failures:
        print(f"check_package_metadata.py --self-test FAILED ({failures} case(s))")
        return 1
    print(f"check_package_metadata.py --self-test passed ({cases} cases).")
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
    project = project_assemblies(root)
    engine = engine_versions_from_manifest(root)
    allowlist, pin_problems = allowlist_versions_from_games(root, engine)

    problems, report = check_manifests(root, packages, assemblies, project, engine, allowlist)
    problems += pin_problems
    lock_problems, corrections = lock_report(packages, root)
    if args.sync_lock and corrections:
        sync_lock(corrections, root)
        lock_problems, corrections = lock_report(packages, root)
    problems += lock_problems

    if args.json:
        Path(args.json).parent.mkdir(parents=True, exist_ok=True)
        Path(args.json).write_text(json.dumps({
            "packages": report,
            "assemblies": dict(sorted(assemblies.items())),
            "engine_pins": engine,
            "allowlist_pins": dict(sorted(allowlist.items())),
            "lock_sources": [str(path.relative_to(root)) for path in lock_paths(root)],
            "problems": problems,
        }, indent=2) + "\n", encoding="utf-8")

    print(f"packages inspected: {len(packages)}; assemblies defined by packages: {len(assemblies)}; "
          f"engine pins: {len(engine)}")
    print(f"release version expected: {RELEASE_VERSION}; kernel packages: {len(KERNEL_PACKAGES)}")
    print(f"allowlisted engine pins from games/*: {len(allowlist)}; lock sources: {len(lock_paths(root))}")
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
