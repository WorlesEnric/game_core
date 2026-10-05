#!/usr/bin/env python3
"""Build the W-MECH-01 staging-lane candidates of the pressure plate mechanism (stdlib only).

Writes three candidates next to this script, each a change set plus its content-addressed artifacts:

  candidate/               the real package                          -> expected verdict: pass
  candidate-forbidden/     + overlays/forbidden   (Process.Start, static mutable field) -> refused by the static scan
  candidate-failing-test/  + overlays/failing-test (a deliberately failing EditMode test) -> failing test verdict

  <candidate>/change-set.json          gamecore.studio.changeset/1, one mechanism.propose operation, state Candidate
  <candidate>/artifacts/package.tgz    deterministic UPM tarball: package.json at the top level, sorted entries,
                                       mtime 0, uid/gid 0, empty owner names, 0644 files / 0755 directories, gzip mtime 0
  <candidate>/artifacts/proposal.json  gamecore.studio.proposal/1: what the package adds and how to verify it

Every change set is validated against docs/studio/schemas/change-set.schema.json (the JSON Schema subset the schema
uses) and every package file and folder must carry its .meta. The rules-test counts come from rules-tests.json, which
records the host's `dotnet test` run of Tests/Rules.

usage:
  python3 samples/mechanisms/pressure-plate/make-candidate.py          # (re)write the three candidates
  python3 samples/mechanisms/pressure-plate/make-candidate.py --check  # fail when a committed candidate is stale

Byte-for-byte reproduction assumes the same zlib (gzip level 9); the tar stream itself is canonical.
"""

from __future__ import annotations

import gzip
import hashlib
import io
import json
import pathlib
import re
import sys
import tarfile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PACKAGE = HERE / "package"
OVERLAYS = HERE / "overlays"
SCHEMA = ROOT / "docs" / "studio" / "schemas" / "change-set.schema.json"
RULES_TESTS = HERE / "rules-tests.json"

DESCRIPTION = (
    "Pressure plate mechanism: a plate target with plate.pressed / plate.weight slots, pressed while the actors standing "
    "on it reach its threshold, refusing to go above its maximum weight or below zero; plate.press{actor} commits "
    "PlatePressed / PlateReleased."
)
INTENT = "Add a pressure plate mechanism to Hollowmere: a plate that is pressed while enough actors stand on it."

CANDIDATES = [
    # (directory, change-set id, overlay or None)
    ("candidate", "cs_01K6RW0MECH00000000000000A", None),
    ("candidate-forbidden", "cs_01K6RW0MECH00000000000000B", "forbidden"),
    ("candidate-failing-test", "cs_01K6RW0MECH00000000000000C", "failing-test"),
]

SKIPPED_NAMES = {".DS_Store"}


# ------------------------------------------------------------------------------------------------------------
# Package tarball
# ------------------------------------------------------------------------------------------------------------


def collect(base: pathlib.Path) -> dict[str, pathlib.Path]:
    """Every file and directory under base, keyed by its posix path relative to base."""
    entries: dict[str, pathlib.Path] = {}
    for path in sorted(base.rglob("*")):
        if path.name in SKIPPED_NAMES or "__pycache__" in path.parts:
            continue
        entries[path.relative_to(base).as_posix()] = path
    return entries


def package_entries(overlay: str | None) -> dict[str, pathlib.Path]:
    entries = collect(PACKAGE)
    if overlay is not None:
        for rel, path in collect(OVERLAYS / overlay).items():
            if path.is_file() and rel in entries:
                raise SystemExit("overlay " + overlay + " would replace package file " + rel)
            entries.setdefault(rel, path)
    return entries


def check_metas(entries: dict[str, pathlib.Path], label: str) -> None:
    """Every file and folder (except package-root metas themselves) ships a .meta; no .meta is orphaned."""
    names = set(entries)
    problems = []
    for rel in sorted(names):
        if rel.endswith(".meta"):
            if rel[: -len(".meta")] not in names:
                problems.append("orphan meta " + rel)
            continue
        if rel + ".meta" not in names:
            problems.append("missing meta for " + rel)
    if "package.json" not in names:
        problems.append("package.json is not at the package root")
    if problems:
        raise SystemExit(label + ": " + "; ".join(problems))


def tarball(entries: dict[str, pathlib.Path]) -> bytes:
    raw = io.BytesIO()
    with tarfile.open(fileobj=raw, mode="w", format=tarfile.USTAR_FORMAT) as archive:
        for rel in sorted(entries):
            path = entries[rel]
            info = tarfile.TarInfo(rel)
            info.mtime = 0
            info.uid = 0
            info.gid = 0
            info.uname = ""
            info.gname = ""
            if path.is_dir():
                info.type = tarfile.DIRTYPE
                info.mode = 0o755
                archive.addfile(info)
            else:
                data = path.read_bytes()
                info.mode = 0o644
                info.size = len(data)
                archive.addfile(info, io.BytesIO(data))
    compressed = io.BytesIO()
    with gzip.GzipFile(filename="", mode="wb", fileobj=compressed, compresslevel=9, mtime=0) as stream:
        stream.write(raw.getvalue())
    return compressed.getvalue()


# ------------------------------------------------------------------------------------------------------------
# Proposal and change set
# ------------------------------------------------------------------------------------------------------------


def canonical(document: object) -> bytes:
    return (json.dumps(document, indent=2, ensure_ascii=False, sort_keys=True) + "\n").encode("utf-8")


def rules_tests() -> dict:
    if not RULES_TESTS.exists():
        raise SystemExit("rules-tests.json is missing: run the Tests/Rules suite with dotnet on the host first")
    record = json.loads(RULES_TESTS.read_text(encoding="utf-8"))
    for field in ("command", "passed", "failed"):
        if field not in record:
            raise SystemExit("rules-tests.json lacks " + field)
    if not isinstance(record["passed"], int) or record["passed"] <= 0 or record["failed"] != 0:
        raise SystemExit("rules-tests.json does not record a passing run")
    return record


def package_version() -> tuple[str, str]:
    manifest = json.loads((PACKAGE / "package.json").read_text(encoding="utf-8"))
    return manifest["name"], manifest["version"]


def proposal() -> dict:
    name, version = package_version()
    tests = rules_tests()
    return {
        "schema": "gamecore.studio.proposal/1",
        "package": name,
        "version": version,
        "adds": {
            "tools": ["mechanism.pressurePlate.add"],
            "definitions": ["pressureplate.definition"],
            "slots": ["plate.pressed", "plate.weight"],
            "routes": ["plate.press"],
            "events": ["PlatePressed", "PlateReleased", "PlateWeightChanged"],
        },
        "tests": {"command": tests["command"], "passed": tests["passed"], "failed": tests["failed"]},
        "smokeTest": {"type": "Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke", "method": "Begin", "steps": 120},
        "catalog": {"type": "Hollowmere.Mechanism.PressurePlate.Generated.PressurePlateCatalog"},
        "rules": {
            "assembly": "Hollowmere.Mechanism.PressurePlate.Rules",
            "directory": "Rules",
            "tests": "Tests/Rules",
        },
    }


def change_set(change_id: str, package: bytes, proposal_bytes: bytes) -> dict:
    package_hash = hashlib.sha256(package).hexdigest()
    proposal_hash = hashlib.sha256(proposal_bytes).hexdigest()
    return {
        "schema": "gamecore.studio.changeset/1",
        "id": change_id,
        "intent": {"text": INTENT, "origin": "agent"},
        "operations": [
            {
                "opId": "op1",
                "tool": "mechanism.propose",
                "args": {
                    "description": DESCRIPTION,
                    "package": {"artifact": "sha256:" + package_hash},
                    "proposal": {"artifact": "sha256:" + proposal_hash},
                    "stageInputs": ["Assets/Hollowmere/World", "Assets/Hollowmere/Regions"],
                },
                "applyRequirement": "Compile",
            }
        ],
        "requirements": {"max": "Compile", "worldRebuild": False, "compile": True, "build": False},
        "artifacts": [
            {
                "sha256": package_hash,
                "name": "package.tgz",
                "mediaType": "application/gzip",
                "bytes": len(package),
                "role": "package",
            },
            {
                "sha256": proposal_hash,
                "name": "proposal.json",
                "mediaType": "application/json",
                "bytes": len(proposal_bytes),
                "role": "proposal",
            },
        ],
        "state": "Candidate",
    }


# ------------------------------------------------------------------------------------------------------------
# JSON Schema subset validator (the keywords change-set.schema.json uses)
# ------------------------------------------------------------------------------------------------------------


TYPES = {
    "object": lambda v: isinstance(v, dict),
    "array": lambda v: isinstance(v, list),
    "string": lambda v: isinstance(v, str),
    "integer": lambda v: isinstance(v, int) and not isinstance(v, bool),
    "number": lambda v: isinstance(v, (int, float)) and not isinstance(v, bool),
    "boolean": lambda v: isinstance(v, bool),
}

KNOWN = {
    "$defs", "$id", "$schema", "$ref", "title", "type", "enum", "const", "pattern", "required", "properties",
    "additionalProperties", "items", "minLength", "minimum", "minItems", "maxItems",
}


def validate(value: object, schema: dict, root: dict, where: str, errors: list[str]) -> None:
    unknown = set(schema) - KNOWN
    if unknown:
        errors.append(where + ": validator does not support " + ", ".join(sorted(unknown)))
    if "$ref" in schema:
        ref = schema["$ref"]
        if not ref.startswith("#/$defs/"):
            errors.append(where + ": unsupported $ref " + ref)
            return
        validate(value, root["$defs"][ref[len("#/$defs/"):]], root, where, errors)
        return
    if value is None:
        errors.append(where + ": null is not allowed")
        return
    kind = schema.get("type")
    if kind is not None and not TYPES[kind](value):
        errors.append(where + ": expected " + kind)
        return
    if "const" in schema and value != schema["const"]:
        errors.append(where + ": expected " + repr(schema["const"]))
    if "enum" in schema and value not in schema["enum"]:
        errors.append(where + ": " + repr(value) + " not in " + repr(schema["enum"]))
    if isinstance(value, str):
        if "pattern" in schema and re.search(schema["pattern"], value) is None:
            errors.append(where + ": " + repr(value) + " does not match " + schema["pattern"])
        if "minLength" in schema and len(value) < schema["minLength"]:
            errors.append(where + ": shorter than " + str(schema["minLength"]))
    if TYPES["number"](value) and "minimum" in schema and value < schema["minimum"]:
        errors.append(where + ": below " + str(schema["minimum"]))
    if isinstance(value, list):
        if "minItems" in schema and len(value) < schema["minItems"]:
            errors.append(where + ": fewer than " + str(schema["minItems"]) + " items")
        if "maxItems" in schema and len(value) > schema["maxItems"]:
            errors.append(where + ": more than " + str(schema["maxItems"]) + " items")
        if "items" in schema:
            for index, item in enumerate(value):
                validate(item, schema["items"], root, where + "[" + str(index) + "]", errors)
    if isinstance(value, dict):
        for field in schema.get("required", []):
            if field not in value:
                errors.append(where + ": missing " + field)
        properties = schema.get("properties", {})
        for field, item in value.items():
            if field in properties:
                validate(item, properties[field], root, where + "." + field, errors)
            elif schema.get("additionalProperties") is False:
                errors.append(where + ": unexpected property " + field)


# ------------------------------------------------------------------------------------------------------------
# Driver
# ------------------------------------------------------------------------------------------------------------


def build() -> dict[pathlib.Path, bytes]:
    schema = json.loads(SCHEMA.read_text(encoding="utf-8"))
    proposal_bytes = canonical(proposal())
    outputs: dict[pathlib.Path, bytes] = {}
    for directory, change_id, overlay in CANDIDATES:
        entries = package_entries(overlay)
        check_metas(entries, directory)
        package = tarball(entries)
        document = change_set(change_id, package, proposal_bytes)
        errors: list[str] = []
        validate(document, schema, schema, "$", errors)
        if errors:
            raise SystemExit(directory + "/change-set.json does not validate:\n  " + "\n  ".join(errors))
        target = HERE / directory
        outputs[target / "change-set.json"] = canonical(document)
        outputs[target / "artifacts" / "package.tgz"] = package
        outputs[target / "artifacts" / "proposal.json"] = proposal_bytes
    return outputs


def describe(outputs: dict[pathlib.Path, bytes]) -> None:
    for directory, change_id, _ in CANDIDATES:
        target = HERE / directory
        package = outputs[target / "artifacts" / "package.tgz"]
        proposal_bytes = outputs[target / "artifacts" / "proposal.json"]
        print(directory + "  " + change_id)
        print("  package.tgz   sha256 " + hashlib.sha256(package).hexdigest() + "  " + str(len(package)) + " bytes")
        print("  proposal.json sha256 " + hashlib.sha256(proposal_bytes).hexdigest() + "  " + str(len(proposal_bytes)) + " bytes")


def main() -> int:
    check = "--check" in sys.argv[1:]
    outputs = build()
    if check:
        stale = []
        for path, data in sorted(outputs.items()):
            if not path.exists() or path.read_bytes() != data:
                stale.append(str(path.relative_to(ROOT)))
        if stale:
            print("stale candidate file(s):")
            for path in stale:
                print("  " + path)
            return 1
        describe(outputs)
        print("candidates are up to date")
        return 0
    for path, data in sorted(outputs.items()):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
    describe(outputs)
    return 0


if __name__ == "__main__":
    sys.exit(main())
