#!/usr/bin/env python3
"""Verify one GameCore Studio staging slot (P2.4; docs/studio/03 s8, studio/stage/README.md). Interpreter-level.

A staging slot (made by studio/stage/make-slot.py) is the only place agent-proposed code compiles before a creator
admits it, so its contents must be exactly what the slot manifest (stage.json) says and nothing else:

  1. the slot manifest exists and names its change set, its candidate package and the candidate's file list;
  2. project/Packages/manifest.json references only allowlisted packages (studio/stage/allowlist.json):
     com.gamecore.* packages by an absolute file: path to the source repository's Packages/<name> (never a
     com.gamecore.studio.* package or a qualification marker), com.unity.* packages listed in the allowlist at
     exactly the source project's pin; `testables` names the candidate and only the candidate;
  3. project/Packages/ holds only manifest.json, packages-lock.json and the candidate's directory;
  4. the candidate's directory holds exactly the declared outputs: the files of the package archive, each with
     its recorded sha256 (an extra file, a missing file or a changed byte is a problem; Unity writing a .meta into
     the candidate shows up here);
  5. project/Assets/ holds only the template's harness, the source's render-pipeline settings (Assets/Settings)
     and the stage inputs the candidate declared (each with its recorded sha256), plus the folder .meta files of
     their parent folders;
  6. the project root holds only Unity's own folders, the harness configuration and nothing else.

usage:
  python3 tools/check_stage_slot.py <slot-dir> [--json PATH]
  python3 tools/check_stage_slot.py --self-test

Exit codes: 0 the slot is clean; 1 at least one problem; 2 bad usage or a missing slot manifest.
"""

from __future__ import annotations

import hashlib
import json
import shutil
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / "studio" / "stage"
TEMPLATE_PROJECT = STAGE / "template" / "project"
ALLOWLIST = STAGE / "allowlist.json"
SLOT_SCHEMA = "gamecore.studio.stage-slot/1"
ROOT_ENTRIES = {"Assets", "Packages", "ProjectSettings", "StageHarness.json", "Library", "Temp", "Logs",
                "UserSettings", "obj", "MemoryCaptures"}


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def files_under(base: Path) -> list[Path]:
    return sorted(p for p in base.rglob("*") if p.is_file())


def check_manifest(project: Path, record: dict, allow: dict, problems: list[str]) -> None:
    manifest_path = project / "Packages" / "manifest.json"
    if not manifest_path.is_file():
        problems.append("project/Packages/manifest.json is missing")
        return
    manifest = load_json(manifest_path)
    deps = manifest.get("dependencies") or {}
    candidate = record["package"]["name"]
    denied_prefixes = tuple(allow.get("deniedGamecorePrefixes", []))
    denied = set(allow.get("deniedPackages", []))
    unity_allowed = set(allow.get("unityPackages", []))
    source = record.get("source") or {}
    repo = Path(source["repo"]) if source.get("repo") else None
    source_pins = {}
    source_manifest = Path(source.get("project", "")) / "Packages" / "manifest.json"
    if source.get("project") and source_manifest.is_file():
        source_pins = load_json(source_manifest).get("dependencies") or {}
    for name, version in sorted(deps.items()):
        if name == candidate:
            problems.append(f"manifest: the candidate {name} must be embedded, not a manifest dependency")
        elif name.startswith("com.gamecore."):
            if name in denied or name.startswith(denied_prefixes):
                problems.append(f"manifest: {name} may not enter a staging slot")
                continue
            if not isinstance(version, str) or not version.startswith("file:/"):
                problems.append(f"manifest: {name} must be an absolute file: path, not {version!r}")
                continue
            path = Path(version[len("file:"):])
            if path.name != name or not (path / "package.json").is_file():
                problems.append(f"manifest: {name} points at {path}, which is not that package")
            elif repo is not None and path.parent.resolve() != (repo / "Packages").resolve():
                problems.append(f"manifest: {name} is not taken from the source repository's Packages/ ({path})")
        elif name.startswith("com.unity."):
            if name not in unity_allowed:
                problems.append(f"manifest: {name} is not allowlisted (studio/stage/allowlist.json)")
            elif source_pins and source_pins.get(name) != version:
                problems.append(f"manifest: {name} is {version!r}, the source project pins {source_pins.get(name)!r}")
        else:
            problems.append(f"manifest: {name} is not an allowlisted package")
    if manifest.get("testables") != [candidate]:
        problems.append(f"manifest: testables must be exactly [{candidate!r}], not {manifest.get('testables')!r}")
    extra_keys = set(manifest) - {"dependencies", "testables"}
    if extra_keys:
        problems.append(f"manifest: unexpected members {sorted(extra_keys)} (scoped registries are not allowed)")


def check_packages_dir(project: Path, record: dict, allow: dict, problems: list[str]) -> None:
    packages = project / "Packages"
    candidate = record["package"]["name"]
    allowed_files = set(allow.get("allowedPackageRootFiles", []))
    for entry in sorted(packages.iterdir()) if packages.is_dir() else []:
        if entry.is_dir() and entry.name == candidate:
            continue
        if entry.is_file() and entry.name in allowed_files:
            continue
        problems.append(f"Packages/{entry.name}: not the candidate, the manifest or the lock")
    package_dir = packages / candidate
    if not package_dir.is_dir():
        problems.append(f"Packages/{candidate}: the candidate package is missing")
        return
    declared = {f["path"]: f["sha256"] for f in record.get("files", [])}
    present = {p.relative_to(package_dir).as_posix(): p for p in files_under(package_dir)}
    for rel in sorted(set(present) - set(declared)):
        problems.append(f"Packages/{candidate}/{rel}: not a declared output of the candidate")
    for rel in sorted(set(declared) - set(present)):
        problems.append(f"Packages/{candidate}/{rel}: a declared output is missing")
    for rel in sorted(set(declared) & set(present)):
        if sha256_file(present[rel]) != declared[rel]:
            problems.append(f"Packages/{candidate}/{rel}: changed since it was staged (sha256 differs)")


def template_assets() -> set[str]:
    base = TEMPLATE_PROJECT
    return {p.relative_to(base).as_posix() for p in files_under(base / "Assets")} if (base / "Assets").is_dir() else set()


def check_assets(project: Path, record: dict, problems: list[str]) -> None:
    assets = project / "Assets"
    if not assets.is_dir():
        problems.append("project/Assets is missing")
        return
    allowed = template_assets()
    inputs = {f["path"]: f["sha256"] for f in record.get("inputFiles", [])}
    ancestors = set()
    for rel in list(inputs) + list(record.get("stageInputs", [])):
        parts = rel.split("/")
        for i in range(2, len(parts)):
            ancestors.add("/".join(parts[:i]) + ".meta")
    for path in files_under(assets):
        rel = path.relative_to(project).as_posix()
        if rel in allowed:
            continue
        if rel.startswith("Assets/Settings/") or rel == "Assets/Settings.meta":
            continue
        if rel in inputs:
            if sha256_file(path) != inputs[rel]:
                problems.append(f"{rel}: a stage input changed in the slot")
            continue
        if rel in ancestors:
            continue
        problems.append(f"{rel}: neither the harness, the render settings nor a declared stage input")


def check_root(project: Path, problems: list[str]) -> None:
    for entry in sorted(project.iterdir()):
        if entry.name not in ROOT_ENTRIES and not entry.name.endswith((".csproj", ".sln")):
            problems.append(f"project/{entry.name}: unexpected entry at the project root")


def check_slot(slot: Path) -> list[str]:
    record_path = slot / "stage.json"
    if not record_path.is_file():
        raise FileNotFoundError(f"{record_path} does not exist")
    record = load_json(record_path)
    problems: list[str] = []
    if record.get("schema") != SLOT_SCHEMA:
        problems.append(f"stage.json: schema is {record.get('schema')!r}, expected {SLOT_SCHEMA!r}")
    for key in ("slotId", "changeSetId", "package", "files"):
        if key not in record:
            problems.append(f"stage.json: {key} is missing")
    if problems:
        return problems
    allow = load_json(ALLOWLIST)
    project = slot / "project"
    check_manifest(project, record, allow, problems)
    check_packages_dir(project, record, allow, problems)
    check_assets(project, record, problems)
    check_root(project, problems)
    return problems


def self_test() -> int:
    failures = 0
    cases = 0

    def expect(label: str, condition: bool) -> None:
        nonlocal failures, cases
        cases += 1
        if not condition:
            failures += 1
            print(f"  FAILED: {label}")

    with tempfile.TemporaryDirectory() as tmp:
        repo = Path(tmp) / "repo"
        kernel = repo / "Packages" / "com.gamecore.contracts"
        studio = repo / "Packages" / "com.gamecore.studio.core"
        for package in (kernel, studio):
            package.mkdir(parents=True)
            (package / "package.json").write_text(json.dumps({"name": package.name}))
        (repo / "studio" / "tools").mkdir(parents=True)
        source = repo / "games" / "g"
        (source / "Packages").mkdir(parents=True)
        (source / "Packages" / "manifest.json").write_text(json.dumps({"dependencies": {
            "com.gamecore.contracts": "file:../../../Packages/com.gamecore.contracts", "com.unity.burst": "1.8.28"}}))

        def make(slot_name: str) -> Path:
            slot = Path(tmp) / slot_name
            project = slot / "project"
            pkg = project / "Packages" / "com.example.plate"
            pkg.mkdir(parents=True)
            (pkg / "package.json").write_text('{"name":"com.example.plate"}')
            (project / "ProjectSettings").mkdir()
            shutil.copytree(TEMPLATE_PROJECT / "Assets", project / "Assets")
            (project / "Assets" / "World").mkdir()
            (project / "Assets" / "World" / "W.asset").write_text("w")
            (project / "Packages" / "manifest.json").write_text(json.dumps({
                "dependencies": {"com.gamecore.contracts": "file:" + kernel.as_posix(), "com.unity.burst": "1.8.28"},
                "testables": ["com.example.plate"]}))
            record = {"schema": SLOT_SCHEMA, "slotId": slot_name, "changeSetId": "cs_01JAPP0000000000000000PLAT",
                      "package": {"name": "com.example.plate"},
                      "files": [{"path": "package.json", "sha256": sha256_file(pkg / "package.json")}],
                      "stageInputs": ["Assets/World/W.asset"],
                      "inputFiles": [{"path": "Assets/World/W.asset",
                                      "sha256": sha256_file(project / "Assets" / "World" / "W.asset")}],
                      "source": {"project": source.as_posix(), "repo": repo.as_posix()}}
            (slot / "stage.json").write_text(json.dumps(record))
            return slot

        clean = make("clean")
        expect("a clean slot passes", check_slot(clean) == [])

        slot = make("extra-output")
        (slot / "project" / "Packages" / "com.example.plate" / "Extra.cs").write_text("class X {}")
        expect("an undeclared file in the candidate is a problem", any("not a declared output" in p for p in check_slot(slot)))

        slot = make("meta-written")
        (slot / "project" / "Packages" / "com.example.plate" / "package.json.meta").write_text("guid: 1")
        expect("a .meta Unity wrote into the candidate is a problem", any("package.json.meta" in p for p in check_slot(slot)))

        slot = make("changed-output")
        (slot / "project" / "Packages" / "com.example.plate" / "package.json").write_text('{"name":"x"}')
        expect("a changed candidate byte is a problem", any("changed since" in p for p in check_slot(slot)))

        slot = make("studio-dependency")
        manifest = slot / "project" / "Packages" / "manifest.json"
        doc = load_json(manifest)
        doc["dependencies"]["com.gamecore.studio.core"] = "file:" + studio.as_posix()
        manifest.write_text(json.dumps(doc))
        expect("a Studio package in the slot is a problem", any("may not enter" in p for p in check_slot(slot)))

        slot = make("unlisted-unity")
        doc = load_json(slot / "project" / "Packages" / "manifest.json")
        doc["dependencies"]["com.unity.ads"] = "4.0.0"
        (slot / "project" / "Packages" / "manifest.json").write_text(json.dumps(doc))
        expect("a non-allowlisted Unity package is a problem", any("not allowlisted" in p for p in check_slot(slot)))

        slot = make("wrong-pin")
        doc = load_json(slot / "project" / "Packages" / "manifest.json")
        doc["dependencies"]["com.unity.burst"] = "1.8.99"
        (slot / "project" / "Packages" / "manifest.json").write_text(json.dumps(doc))
        expect("a Unity pin that differs from the source is a problem", any("source project pins" in p for p in check_slot(slot)))

        slot = make("registry")
        doc = load_json(slot / "project" / "Packages" / "manifest.json")
        doc["scopedRegistries"] = [{"url": "https://example.invalid"}]
        (slot / "project" / "Packages" / "manifest.json").write_text(json.dumps(doc))
        expect("a scoped registry is a problem", any("scoped registries" in p for p in check_slot(slot)))

        slot = make("relative-kernel")
        doc = load_json(slot / "project" / "Packages" / "manifest.json")
        doc["dependencies"]["com.gamecore.contracts"] = "file:../x"
        (slot / "project" / "Packages" / "manifest.json").write_text(json.dumps(doc))
        expect("a relative kernel path is a problem", any("absolute file:" in p for p in check_slot(slot)))

        slot = make("live-asset")
        (slot / "project" / "Assets" / "World" / "Secret.asset").write_text("s")
        expect("a live asset that is not a stage input is a problem", any("Secret.asset" in p for p in check_slot(slot)))

        slot = make("changed-input")
        target = slot / "project" / "Assets" / "World" / "W.asset"
        target.write_text("changed")
        expect("a changed stage input is a problem", any("stage input changed" in p for p in check_slot(slot)))

        slot = make("root-entry")
        (slot / "project" / "Hidden.dll").write_text("x")
        expect("an unexpected root entry is a problem", any("Hidden.dll" in p for p in check_slot(slot)))

        slot = make("extra-package-dir")
        (slot / "project" / "Packages" / "com.other.pkg").mkdir()
        expect("a second embedded package is a problem", any("com.other.pkg" in p for p in check_slot(slot)))

        slot = make("testables")
        doc = load_json(slot / "project" / "Packages" / "manifest.json")
        doc["testables"] = ["com.example.plate", "com.gamecore.contracts"]
        (slot / "project" / "Packages" / "manifest.json").write_text(json.dumps(doc))
        expect("testables beyond the candidate are a problem", any("testables" in p for p in check_slot(slot)))

    if failures:
        print(f"check_stage_slot.py --self-test FAILED ({failures} of {cases} case(s))")
        return 1
    print(f"check_stage_slot.py --self-test passed ({cases} cases).")
    return 0


def main(argv: list[str]) -> int:
    args = argv[1:]
    if args == ["--self-test"]:
        return self_test()
    json_out = None
    if "--json" in args:
        index = args.index("--json")
        if index + 1 >= len(args):
            print(__doc__, file=sys.stderr)
            return 2
        json_out = Path(args[index + 1])
        del args[index:index + 2]
    if len(args) != 1:
        print(__doc__, file=sys.stderr)
        return 2
    slot = Path(args[0]).expanduser().resolve()
    try:
        problems = check_slot(slot)
    except (FileNotFoundError, KeyError, ValueError) as error:
        print(f"check_stage_slot.py: {error}", file=sys.stderr)
        return 2
    if json_out is not None:
        json_out.parent.mkdir(parents=True, exist_ok=True)
        json_out.write_text(json.dumps({"slot": slot.as_posix(), "ok": not problems, "problems": problems},
                                       indent=2) + "\n", encoding="utf-8")
    if problems:
        print(f"check_stage_slot.py: {len(problems)} problem(s) in {slot}:")
        for problem in problems:
            print(f"  - {problem}")
        return 1
    print(f"check_stage_slot.py: slot {slot.name} is clean (manifest allowlisted, candidate = declared outputs).")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
