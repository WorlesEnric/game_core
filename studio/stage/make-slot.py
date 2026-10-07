#!/usr/bin/env python3
"""Create (or refresh) one GameCore Studio staging slot (P2.4, docs/studio/03 s8, 04 s6).

A slot is an isolated, minimal Unity project plus a dotnet workspace in which an agent-proposed mechanism package is
compiled and tested before it can affect the live editor. Layout of `<slot-root>/<slot-id>/`:

    stage.json                 the slot manifest (schema gamecore.studio.stage-slot/1): change set, package, the exact
                               file list of the candidate package (declared outputs), stage inputs, source project
    candidate/                 the candidate as staged: change-set.json and its artifacts (read-only copies)
    project/                   the Unity project
      Packages/manifest.json     the source project's kernel + gameplay packages pinned by absolute file: paths to the
                                 source repository's Packages/ (no com.gamecore.studio.*, no qualification markers),
                                 the source project's com.unity.* pins, and `testables: [<candidate>]`
      Packages/<candidate>/      the candidate package (embedded), extracted from package.tgz
      ProjectSettings/           copied from the source project (URP/Input/quality/...), EditorBuildSettings from the
                                 template (no scenes, no config objects)
      Assets/Settings/           the source project's render-pipeline assets (copied, when present)
      Assets/StageHarness/       the harness (template): catalog probe (EditMode) and smoke runner (PlayMode)
      Assets/<stage inputs>      ONLY what the candidate's `stageInputs` lists, copied read-only (Editor/ and Tests/
                                 folders are not copied)
      StageHarness.json          harness configuration (smoke entry, catalog type, out dir)
    dotnet/                    Rules.csproj + Rules.Tests.csproj for the Unity-free half (when the package has one)
    out/                       step logs and the verdict (written by the runner)

The candidate comes from a directory holding `change-set.json` and its artifacts (by artifact name or by sha256) as
`studio/etos/agent/workers/gc-mechanic.md` writes them, or (legacy `stage.sh` mode) from a bare package directory.
Every artifact is checked against its sha256; the archive is extracted safely (no absolute paths, no `..`, no links,
no devices). Re-running on an existing slot of the same change set and package keeps `project/Library` (incremental
Unity import); a different change set in the same slot is refused unless --force.

usage:
  make-slot.py --slot-root DIR --slot ID --source-project DIR --candidate DIR [--warm-library DIR] [--force]
  make-slot.py --slot-root DIR --slot ID --source-project DIR --package-dir DIR --change-set-id ID [...]

The last stdout line is a JSON summary `{ok, slot, slotDir, changeSetId, package, files, reused}`; problems exit 1
with `{ok: false, error}`. Exit 2 is bad usage.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import io
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tarfile
from pathlib import Path
from xml.sax.saxutils import escape
from path_policy import relative, contained, data_path, validate_meta
from redact import redact

HERE = Path(__file__).resolve().parent
TEMPLATE = HERE / "template"
ALLOWLIST = HERE / "allowlist.json"
SLOT_SCHEMA = "gamecore.studio.stage-slot/1"
SLOT_ID = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
CHANGE_SET_ID = re.compile(r"^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$")
PACKAGE_NAME = re.compile(r"^[a-z0-9][a-z0-9.-]{2,213}$")
SKIPPED_INPUT_DIRS_LOWER = {"editor", "plugins", "tests", "library", "temp", "logs", "obj", "bin"}
SKIPPED_INPUT_DIRS = {"Editor", "Tests", "Library", "Temp", "Logs", "obj", "bin"}
MAX_PACKAGE_BYTES = 64 * 1024 * 1024
MAX_PACKAGE_FILES = 4000


class SlotError(Exception):
    pass


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


def load_json(path: Path):
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise SlotError(f"cannot read {path}: {error}") from error


def write_json(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(value, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def tree_digest(root: Path) -> str:
    """sha256 over the sorted `path\\0sha256\\n` lines of every file under root (the template revision)."""
    lines = []
    for path in sorted(p for p in root.rglob("*") if p.is_file()):
        lines.append(f"{path.relative_to(root).as_posix()}\0{sha256_file(path)}\n")
    return sha256_bytes("".join(lines).encode("utf-8"))


def artifact_sha(value) -> str | None:
    """`{"artifact": "sha256:<hex>"}` or `"sha256:<hex>"` -> hex."""
    if isinstance(value, dict):
        value = value.get("artifact")
    if not isinstance(value, str):
        return None
    text = value[7:] if value.startswith("sha256:") else value
    return text if re.fullmatch(r"[0-9a-f]{64}", text) else None


def find_artifact(candidate: Path, entry: dict) -> Path:
    sha = entry["sha256"]
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-f]{64}", sha):
        raise SlotError("artifact_digest_invalid")
    if entry.get("name") is not None:
        relative(entry["name"])
        if "/" in entry["name"]:
            raise SlotError("artifact_name_invalid: expected basename")
    for name in filter(None, [entry.get("name"), sha, f"sha256-{sha}"]):
        for base in (candidate / "artifacts", candidate):
            path = contained(candidate, ("artifacts/" if base != candidate else "") + name)
            if path.is_file():
                if sha256_file(path) != sha:
                    raise SlotError(f"artifact {name} does not match its sha256 {sha}")
                return path
    raise SlotError(f"artifact {entry.get('name') or sha} ({sha}) is not in {candidate}")


def read_candidate(candidate: Path) -> dict:
    change_set = load_json(contained(candidate, "change-set.json"))
    cs_id = change_set.get("id")
    if not isinstance(cs_id, str) or not CHANGE_SET_ID.match(cs_id):
        raise SlotError("change-set.json has no valid change-set id")
    proposals = [op for op in change_set.get("operations", []) if op.get("tool") == "mechanism.propose"]
    if len(proposals) != 1:
        raise SlotError("a staged change set carries exactly one mechanism.propose operation")
    args = proposals[0].get("args") or {}
    package_sha = artifact_sha(args.get("package"))
    proposal_sha = artifact_sha(args.get("proposal"))
    if package_sha is None:
        raise SlotError("mechanism.propose has no 'package' artifact argument")
    entries = {a.get("sha256"): a for a in change_set.get("artifacts", []) if isinstance(a, dict)}
    if package_sha not in entries:
        raise SlotError("the package artifact is not listed in the change set's artifacts")
    package_path = find_artifact(candidate, entries[package_sha])
    proposal = None
    proposal_path = None
    if proposal_sha is not None:
        if proposal_sha not in entries:
            raise SlotError("the proposal artifact is not listed in the change set's artifacts")
        proposal_path = find_artifact(candidate, entries[proposal_sha])
        proposal = load_json(proposal_path)
    stage_inputs = args.get("stageInputs") or []
    if not isinstance(stage_inputs, list) or not all(isinstance(p, str) for p in stage_inputs):
        raise SlotError("stageInputs is a list of project-relative paths")
    return {
        "changeSet": change_set,
        "changeSetId": cs_id,
        "opId": proposals[0].get("opId"),
        "packageSha": package_sha,
        "packagePath": package_path,
        "proposalSha": proposal_sha,
        "proposalPath": proposal_path,
        "proposal": proposal,
        "stageInputs": stage_inputs,
    }


def safe_members(archive: tarfile.TarFile):
    total = 0
    count = 0
    for member in archive:
        name = member.name
        while name.startswith("./"):
            name = name[2:]
        if name in ("", "."):
            continue
        parts = name.split("/")
        if name.startswith("/") or ".." in parts or "\\" in name or any(p == "" for p in parts[:-1]):
            raise SlotError(f"unsafe path in the package archive: {member.name!r}")
        if not (member.isfile() or member.isdir()):
            raise SlotError(f"the package archive may hold only files and directories: {member.name!r}")
        count += 1
        total += member.size
        if count > MAX_PACKAGE_FILES or total > MAX_PACKAGE_BYTES:
            raise SlotError("the package archive is too large")
        yield name.rstrip("/"), member


def extract_package(archive_path: Path, target: Path) -> None:
    if archive_path.stat().st_size > MAX_PACKAGE_BYTES:
        raise SlotError("the compressed package archive is too large")
    data = archive_path.read_bytes()
    try:
        archive = tarfile.open(fileobj=io.BytesIO(data), mode="r:*")
    except tarfile.TarError as error:
        raise SlotError(f"the package artifact is not a tar/tar.gz archive: {error}") from error
    with archive:
        members = list(safe_members(archive))
        for name, member in members:
            destination = target / name
            if member.isdir():
                destination.mkdir(parents=True, exist_ok=True)
                continue
            destination.parent.mkdir(parents=True, exist_ok=True)
            source = archive.extractfile(member)
            if source is None:
                raise SlotError(f"cannot read {name} from the archive")
            destination.write_bytes(source.read())
            destination.chmod(0o644)


def package_files(package_dir: Path) -> list[dict]:
    files = []
    for path in sorted(p for p in package_dir.rglob("*") if p.is_file()):
        rel = path.relative_to(package_dir).as_posix()
        files.append({"path": rel, "sha256": sha256_file(path), "bytes": path.stat().st_size})
    return files


def make_writable(path: Path) -> None:
    if not path.exists():
        return
    for item in [path, *path.rglob("*")]:
        try:
            mode = item.lstat().st_mode
            if not stat.S_ISLNK(mode):
                item.chmod(mode | stat.S_IWUSR)
        except OSError:
            pass


def remove_tree(path: Path) -> None:
    if path.exists():
        make_writable(path)
        shutil.rmtree(path)


def copy_tree(source: Path, target: Path, skip_dirs: set[str]) -> list[Path]:
    copied = []
    if source.is_symlink() or any(p.is_symlink() for p in source.rglob("*")):
        raise SlotError("stage_path_link: copy source contains links")
    for root, dirs, files in os.walk(source):
        dirs[:] = sorted(d for d in dirs if d not in skip_dirs)
        rel_root = Path(root).relative_to(source)
        for name in sorted(files):
            if Path(name).stem in skip_dirs and name.endswith(".meta"):
                continue  # the .meta of a skipped folder
            src = Path(root) / name
            dst = target / rel_root / name
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
            copied.append(dst)
    return copied


def root_mismatch(detail: str) -> SlotError:
    return SlotError("stage_package_root_mismatch: " + detail +
                     "; register the intended checkout's project in stage.projects and pin local packages "
                     "to that checkout's Packages/<package-name> (not the companion tools checkout)")


def project_root(source_project: Path) -> Path:
    """Only the operator-selected source project determines the trusted checkout."""
    result = subprocess.run(["git", "-C", str(source_project), "rev-parse", "--show-toplevel"],
                            capture_output=True, text=True, timeout=10)
    if result.returncode or not result.stdout.strip():
        raise root_mismatch(f"{source_project} is not in a git checkout")
    root = Path(result.stdout.strip()).resolve()
    packages = root / "Packages"
    if packages.is_symlink() or not packages.is_dir():
        raise root_mismatch(f"{packages} must be a real package directory")
    return root


def build_manifest(source_project: Path, package: str, allow: dict) -> tuple[dict, dict]:
    trusted_packages = project_root(source_project) / "Packages"
    manifest = load_json(source_project / "Packages" / "manifest.json")
    deps = manifest.get("dependencies") or {}
    packages_dir = source_project / "Packages"
    denied_prefixes = tuple(allow.get("deniedGamecorePrefixes", []))
    denied = set(allow.get("deniedPackages", []))
    unity_allowed = set(allow.get("unityPackages", []))
    out: dict[str, str] = {}
    pins: dict[str, str] = {}
    for name, version in sorted(deps.items()):
        if name == package:
            continue
        if name.startswith("com.gamecore."):
            if name in denied or name.startswith(denied_prefixes):
                continue
            if not isinstance(version, str) or not version.startswith("file:"):
                raise SlotError(f"the source manifest pins {name} as {version!r}; slots need a file: package")
            resolved = (packages_dir / version[len("file:"):]).resolve()
            expected = trusted_packages / name
            if not PACKAGE_NAME.fullmatch(name) or expected.is_symlink() or resolved != expected:
                raise root_mismatch(f"{name} resolves to {resolved}; expected {expected}")
            if any(p.is_symlink() for p in expected.rglob("*")):
                raise root_mismatch(f"{name} contains a symlink")
            if not (resolved / "package.json").is_file():
                raise SlotError(f"{name} resolves to {resolved}, which has no package.json")
            out[name] = "file:" + resolved.as_posix()
            pins[name] = out[name]
        elif name.startswith("com.unity."):
            if name not in unity_allowed:
                raise SlotError(f"{name} is not in studio/stage/allowlist.json unityPackages")
            out[name] = version
            pins[name] = version
        else:
            raise SlotError(f"the source manifest names {name}, which no slot may carry")
    return {"dependencies": out, "testables": [package]}, pins


def copy_settings(source_project: Path, project: Path) -> list[dict]:
    settings = contained(source_project, "ProjectSettings")
    target = project / "ProjectSettings"
    target.mkdir(parents=True, exist_ok=True)
    for path in sorted(settings.iterdir()):
        contained(source_project, path.relative_to(source_project).as_posix())
        if path.is_file() and path.name != "EditorBuildSettings.asset":
            if path.suffix not in (".asset", ".txt", ".json"):
                raise SlotError("stage_settings_invalid: executable project settings")
            shutil.copyfile(path, target / path.name)
    # Settings are trusted source YAML data, never an executable-content exemption.
    render = contained(source_project, "Assets/Settings")
    return copy_inputs(source_project, project, ["Assets/Settings"], settings=True) if render.is_dir() else []


def copy_inputs(source_project: Path, project: Path, inputs: list[str], settings=False) -> list[dict]:
    recorded = {}
    for rel in inputs:
        relative(rel)
        if not rel.startswith("Assets/") or any(p.lower() in {"editor", "plugins"} for p in rel.split("/")):
            raise SlotError("stage_input_executable: forbidden input path")
        source = contained(source_project, rel)
        paths = sorted(source.rglob("*")) if source.is_dir() else [source]
        paths += [source_project / (rel + ".meta")] if (source_project / (rel + ".meta")).exists() else []
        for src in paths:
            name = src.relative_to(source_project).as_posix()
            contained(source_project, name)
            if src.is_dir():
                if any(p.lower() in SKIPPED_INPUT_DIRS_LOWER for p in src.parts):
                    raise SlotError("stage_input_executable: forbidden input directory")
                continue
            if not src.is_file():
                raise SlotError("stage_input_missing")
            if src.suffix == ".meta":
                original = src.with_suffix("")
                if not original.is_dir():
                    data_path(name[:-5], settings)
                validate_meta(src)
            else:
                data_path(name, settings)
                if settings and src.suffix == ".asset" and not src.read_bytes().startswith(b"%YAML"):
                    raise SlotError("stage_settings_invalid: expected Unity YAML")
            dst = contained(project, name)
            dst.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(src, dst)
            recorded[name] = {"path": name, "sha256": sha256_file(dst)}
            dst.chmod(0o444)
    return [recorded[k] for k in sorted(recorded)]


def write_dotnet(slot: Path, package_dir: Path, proposal: dict | None) -> dict | None:
    rules_info = (proposal or {}).get("rules") or {}
    rules_dir = contained(package_dir, rules_info.get("directory", "Rules"))
    tests_dir = contained(package_dir, rules_info.get("tests", "Tests/Rules"))
    if not rules_dir.is_dir() or not any(rules_dir.rglob("*.cs")):
        return None
    assembly = rules_info.get("assembly") or "StagedRules"
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_.]*", assembly):
        raise SlotError(f"proposal rules.assembly {assembly!r} is not an assembly name")
    dotnet = slot / "dotnet"
    remove_tree(dotnet)
    (dotnet / "Rules").mkdir(parents=True)
    template = TEMPLATE / "dotnet"
    for name in ("Directory.Build.props", "NuGet.Config"):
        shutil.copyfile(template / name, dotnet / name)
    rules = (template / "Rules.csproj.tmpl").read_text(encoding="utf-8")
    rules = rules.replace("@@ASSEMBLY@@", assembly).replace("@@RULES_DIR@@", escape(rules_dir.as_posix(), {'"': "&quot;"}))
    (dotnet / "Rules" / "Rules.csproj").write_text(rules, encoding="utf-8")
    has_tests = tests_dir.is_dir() and any(tests_dir.rglob("*.cs"))
    if has_tests:
        (dotnet / "Rules.Tests").mkdir(parents=True)
        tests = (template / "Rules.Tests.csproj.tmpl").read_text(encoding="utf-8")
        tests = tests.replace("@@ASSEMBLY@@", assembly).replace("@@TESTS_DIR@@", escape(tests_dir.as_posix(), {'"': "&quot;"}))
        (dotnet / "Rules.Tests" / "Rules.Tests.csproj").write_text(tests, encoding="utf-8")
    return {"assembly": assembly, "rules": rules_dir.relative_to(package_dir).as_posix(),
            "tests": tests_dir.relative_to(package_dir).as_posix() if has_tests else None}


def git_head(path: Path) -> str | None:
    try:
        out = subprocess.run(["git", "-C", str(path), "rev-parse", "HEAD"], capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.TimeoutExpired):
        return None
    return out.stdout.strip() if out.returncode == 0 else None


def make_slot(args) -> dict:
    if not SLOT_ID.match(args.slot):
        raise SlotError("a slot id is 1-64 characters of [a-z0-9._-] starting with a letter or digit")
    source_project = Path(args.source_project).resolve()
    if not (source_project / "ProjectSettings").is_dir() or not (source_project / "Packages" / "manifest.json").is_file():
        raise SlotError(f"{source_project} is not a Unity project")
    allow = load_json(ALLOWLIST)
    repo = project_root(source_project)
    expected_root = getattr(args, "package_root", None)
    if expected_root and Path(expected_root).absolute() != repo / "Packages":
        raise root_mismatch(f"sandbox mount {expected_root} differs from {repo / 'Packages'}")
    # Validate pins before reading candidate artifacts or creating a slot.
    build_manifest(source_project, "", allow)
    slot = Path(args.slot_root).expanduser().resolve() / args.slot
    if args.candidate:
        cand = read_candidate(Path(args.candidate).absolute())
    else:
        if not args.change_set_id or not CHANGE_SET_ID.match(args.change_set_id):
            raise SlotError("--package-dir needs --change-set-id cs_<ULID>")
        cand = {"changeSet": None, "changeSetId": args.change_set_id, "opId": None, "packageSha": None,
                "packagePath": None, "proposalSha": None, "proposalPath": None, "proposal": None, "stageInputs": []}
        bare = Path(args.package_dir).resolve() / "proposal.json"
        if bare.is_file():
            cand["proposal"] = load_json(bare)

    reused = False
    created_at = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    previous = slot / "stage.json"
    if previous.is_file():
        old = load_json(previous)
        same = old.get("changeSetId") == cand["changeSetId"] and old.get("package", {}).get("artifact") == cand["packageSha"]
        if not same and not args.force:
            raise SlotError(f"slot {args.slot} holds change set {old.get('changeSetId')}; use another slot or --force")
        reused = same
        if reused and isinstance(old.get("createdAt"), str):
            created_at = old["createdAt"]
    slot.mkdir(parents=True, exist_ok=True)
    project = slot / "project"
    library = project / "Library"
    if not reused:
        remove_tree(project)
    # Rebuild everything but Library/ (and Unity's own Temp/Logs, which a run recreates).
    for child in ("Assets", "Packages", "ProjectSettings", "UserSettings"):
        remove_tree(project / child)
    remove_tree(slot / "candidate")
    remove_tree(slot / "out")
    project.mkdir(parents=True, exist_ok=True)
    (slot / "out" / "logs").mkdir(parents=True)

    # The candidate, as staged (read-only copies).
    staged = slot / "candidate"
    (staged / "artifacts").mkdir(parents=True)
    if cand["changeSet"] is not None:
        write_json(staged / "change-set.json", cand["changeSet"])
        for key in ("packagePath", "proposalPath"):
            if cand[key] is not None:
                shutil.copyfile(cand[key], staged / "artifacts" / Path(cand[key]).name)

    trusted = load_json(HERE / "template-manifest.json")
    actual = {p.relative_to(TEMPLATE).as_posix(): sha256_file(p) for p in TEMPLATE.rglob("*") if p.is_file()}
    if actual != trusted:
        raise SlotError("stage_template_changed: trusted manifest mismatch")
    # Template (harness, EditorBuildSettings), then the source's settings.
    copy_tree(TEMPLATE / "project", project, set())
    settings_inputs = copy_settings(source_project, project)
    template_ebs = TEMPLATE / "project" / "ProjectSettings" / "EditorBuildSettings.asset"
    shutil.copyfile(template_ebs, project / "ProjectSettings" / "EditorBuildSettings.asset")

    # The candidate package (embedded).
    work = slot / ".extract"
    remove_tree(work)
    work.mkdir()
    if cand["packagePath"] is not None:
        extract_package(cand["packagePath"], work)
    else:
        copy_tree(Path(args.package_dir).absolute(), work, set())
    package_json = work / "package.json"
    if not package_json.is_file():
        raise SlotError("the package has no package.json at its root")
    package_meta = load_json(package_json)
    name = package_meta.get("name")
    if not isinstance(name, str) or not PACKAGE_NAME.match(name):
        raise SlotError("package.json names no valid package")
    if name.startswith("com.gamecore.") or name.startswith("com.unity."):
        raise SlotError(f"a mechanism package may not use the reserved name {name}")
    proposal = cand["proposal"]
    if proposal is not None and proposal.get("package") not in (None, name):
        raise SlotError(f"proposal.json names {proposal.get('package')} but the archive holds {name}")
    package_dir = project / "Packages" / name
    package_dir.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(work), str(package_dir))
    files = package_files(package_dir)
    if cand["packageSha"] is None:
        cand["packageSha"] = sha256_bytes("".join(f"{f['path']}\0{f['sha256']}\n" for f in files).encode("utf-8"))

    manifest, pins = build_manifest(source_project, name, allow)
    write_json(project / "Packages" / "manifest.json", manifest)

    inputs = copy_inputs(source_project, project, cand["stageInputs"])
    inputs += settings_inputs

    import world_snapshot
    snapshot = world_snapshot.export(source_project, slot / "world-snapshot", cand["changeSetId"],
                                     git_head(repo), repo / "Packages")
    if snapshot.get("error"):
        raise SlotError(f"{snapshot['error']}: expected one tracked source-world description, found {snapshot['count']}")

    smoke = (proposal or {}).get("smokeTest")
    if isinstance(smoke, str):
        smoke = None
    catalog_type = ((proposal or {}).get("catalog") or {}).get("type")
    out_dir = slot / "out"
    harness = {
        "package": name,
        "changeSetId": cand["changeSetId"],
        "catalogType": catalog_type or "",
        "smokeType": (smoke or {}).get("type", ""),
        "smokeMethod": (smoke or {}).get("method", "Begin"),
        "smokeSteps": int((smoke or {}).get("steps", 120)),
        "outDir": out_dir.as_posix(),
        "worldSnapshotPath": snapshot.get("path", ""),
        "worldSnapshotSha256": snapshot.get("sha256", ""),
    }
    write_json(project / "StageHarness.json", harness)

    dotnet = write_dotnet(slot, package_dir, proposal)

    if args.warm_library:
        import analysis_context
        warm = Path(args.warm_library).expanduser()
        context = warm.parent / 'analysis-context'
        # Migrate an old operator seed only after verifying every pinned byte.
        if not context.exists():
            analysis_context.seed(warm, context)
        analysis_context.seed(context, slot / 'analysis-context')

    if not reused and args.warm_library and not library.exists():
        warm = Path(args.warm_library).expanduser()
        if (warm / "ArtifactDB").exists() or warm.is_dir():
            subprocess.run(["cp", "-a", "--reflink=auto", str(warm), str(library)], check=False)

    record = {
        "schema": SLOT_SCHEMA,
        "slotId": args.slot,
        "changeSetId": cand["changeSetId"],
        "opId": cand["opId"],
        "package": {
            "name": name,
            "version": package_meta.get("version"),
            "artifact": cand["packageSha"],
            "proposal": cand["proposalSha"],
            "dir": package_dir.relative_to(slot).as_posix(),
        },
        "files": files,
        "stageInputs": cand["stageInputs"],
        "inputFiles": inputs,
        "manifest": pins,
        "harness": harness,
        "worldSnapshot": snapshot,
        "dotnet": dotnet,
        "allowUnsafe": None,
        "blobs": (proposal or {}).get("blobs") or [],
        "source": {
            "project": source_project.as_posix(),
            "repo": repo.as_posix(),
            "packageRoot": (repo / "Packages").as_posix(),
            "commit": git_head(repo),
        },
        "template": tree_digest(TEMPLATE),
        "createdAt": created_at,
    }
    write_json(slot / "stage.json", record)
    return {"ok": True, "slot": args.slot, "slotDir": slot.as_posix(), "changeSetId": cand["changeSetId"],
            "package": name, "files": len(files), "inputs": len(inputs), "reused": reused}


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--slot-root")
    parser.add_argument("--slot")
    parser.add_argument("--source-project", required=True)
    parser.add_argument("--package-root", help="trusted sandbox package mount; must match the source checkout")
    parser.add_argument("--describe-project", action="store_true", help="validate the registered checkout without a candidate")
    group = parser.add_mutually_exclusive_group()
    group.add_argument("--candidate", help="directory with change-set.json and its artifacts")
    group.add_argument("--package-dir", help="a bare package directory (legacy stage.sh mode)")
    parser.add_argument("--change-set-id", help="with --package-dir")
    parser.add_argument("--warm-library", help="a warm Library/ copied into a new slot (cp --reflink=auto)")
    parser.add_argument("--force", action="store_true", help="replace a slot that holds another change set")
    try:
        args = parser.parse_args(argv[1:])
    except SystemExit as exit_:
        return 2 if exit_.code else 0
    try:
        if args.describe_project:
            project = Path(args.source_project).resolve()
            root = project_root(project)
            manifest, _ = build_manifest(project, "", load_json(ALLOWLIST))
            print(json.dumps({"project": str(project), "repo": str(root),
                              "packageRoot": str(root / "Packages"), "commit": git_head(root),
                              "manifest": manifest}))
            return 0
        if not args.slot or not args.slot_root or not (args.candidate or args.package_dir):
            raise SlotError("--slot, --slot-root and --candidate or --package-dir are required")
        summary = make_slot(args)
    except (SlotError, ValueError, OSError, TypeError, KeyError) as error:
        print(redact(json.dumps({"ok": False, "error": str(error)})).strip())
        return 1
    print(json.dumps(summary, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
