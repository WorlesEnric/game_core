#!/usr/bin/env python3
"""Compare the generated registration fingerprints of two independent clean builds (GC-025).

P-058 and P-060 require the shipping build to record its generated registration coverage, and GC-025's concrete
work names one more thing: "Compare generated registration fingerprints across builds". A fingerprint that differs
between two clean builds of the same revision means generation is not reproducible — a different declaration order,
a stale committed catalog, a description edited without regenerating, or two build shapes compiled from different
sources.

The tool reads each build's *catalog ledger* rather than its IL2CPP output: every committed generated catalog
carries `CatalogFileHash` (over its own source prefix) and `CatalogFingerprint` (over the declarations the runtime
catalog builds), and the committed reachability manifest carries the same two values per catalog. A build reaches
this tool as either

  * a Unity project directory, whose `Assets/**/*.g.cs` generated catalogs are discovered and read (the shape
    `tools/unity/build_probe.sh` leaves behind), or
  * a build-evidence directory containing `catalog-ledger.json`, the file `tools/build_baseline_player.sh` writes
    after a build.

Both sides are reduced to one value per catalog plus a single combined build fingerprint, so a comparison is one
equality and a mismatch names the exact catalog and field that differs. `--manifest` adds the committed manifest as
a third participant, which is how a build is checked against the revision's own recorded expectations.

usage:
  python3 tools/compare_registration_fingerprints.py --a <dir> --b <dir> [--manifest <json>] [--json <path>]
  python3 tools/compare_registration_fingerprints.py --ledger <project> --ledger-out <dir>
  python3 tools/compare_registration_fingerprints.py --self-test

exit codes: 0 every participant agrees; 1 a mismatch; 2 usage error.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import re
import shutil
import sys
import tempfile

ROOT = pathlib.Path(__file__).resolve().parents[1]

DEFAULT_MANIFEST = "artifacts/baseline/catalog-reachability.json"
LEDGER_NAME = "catalog-ledger.json"

FILE_HASH = re.compile(r'public const string CatalogFileHash = "([0-9a-f]{64})"')
FINGERPRINT = re.compile(r'public const string CatalogFingerprint = "([0-9a-f]{64})"')
CLASS_NAME = re.compile(r"public static class (\w+)\s*$", re.MULTILINE)
GROUP_COUNT = re.compile(r"public const int RegistrationGroupCount = (\d+);")
SCHEMA_COUNT = re.compile(r"public const int SchemaCount = (\d+);")
COVERAGE_CLASS = re.compile(r"public static class (\w+)Coverage\s*$", re.MULTILINE)
COVERAGE_REGISTRATIONS = re.compile(r"public const int RegistrationCount = (\d+);")


class Participant:
    """One build's registration fingerprints, keyed by catalog class name."""

    def __init__(self, label, source, catalogs):
        self.label = label
        self.source = source
        self.catalogs = catalogs

    def combined(self):
        """SHA-256 over the sorted `name:fileHash:fingerprint` triples: one value for the whole build."""
        text = "".join(
            "%s:%s:%s\n" % (name, entry["catalogFileHash"], entry["catalogFingerprint"])
            for name, entry in sorted(self.catalogs.items())
        )
        return hashlib.sha256(text.encode("utf-8")).hexdigest()


def from_ledger(label, path):
    document = json.loads((path / LEDGER_NAME).read_text(encoding="utf-8"))
    catalogs = {}
    for entry in document.get("catalogs", []):
        catalogs[entry["className"]] = {
            "catalogFileHash": entry["catalogFileHash"],
            "catalogFingerprint": entry["catalogFingerprint"],
            "registrationGroupCount": entry.get("registrationGroupCount"),
            "schemaCount": entry.get("schemaCount"),
            "registrationCount": entry.get("registrationCount"),
            "coverageClassName": entry.get("coverageClassName"),
        }
    return Participant(label, str(path / LEDGER_NAME), catalogs)


def from_project(label, path):
    """Reads every generated catalog source under a Unity project's Assets tree."""
    catalogs = {}
    coverage = {}
    for generated in sorted(path.glob("Assets/**/*.g.cs")):
        text = generated.read_text(encoding="utf-8")
        fingerprint = FINGERPRINT.search(text)
        file_hash = FILE_HASH.search(text)
        if fingerprint is None or file_hash is None:
            continue
        name = CLASS_NAME.search(text)
        if name is None:
            raise SystemExit("no catalog class name in " + str(generated))
        class_name = name.group(1)
        group_count = GROUP_COUNT.search(text)
        schema_count = SCHEMA_COUNT.search(text)
        catalogs[class_name] = {
            "catalogFileHash": file_hash.group(1),
            "catalogFingerprint": fingerprint.group(1),
            "registrationGroupCount": int(group_count.group(1)) if group_count else None,
            "schemaCount": int(schema_count.group(1)) if schema_count else None,
            "registrationCount": None,
            "coverageClassName": None,
            "source": str(generated),
        }
        for companion in sorted(generated.parent.glob("*.g.cs")):
            if companion == generated:
                continue
            companion_text = companion.read_text(encoding="utf-8")
            companion_name = COVERAGE_CLASS.search(companion_text)
            if companion_name is None or companion_name.group(1) != class_name:
                continue
            count = COVERAGE_REGISTRATIONS.search(companion_text)
            if count is not None:
                catalogs[class_name]["registrationCount"] = int(count.group(1))
            catalogs[class_name]["coverageClassName"] = companion_name.group(1) + "Coverage"
            catalogs[class_name]["coverageSource"] = str(companion)

    if not catalogs:
        raise SystemExit("no generated catalog found under " + str(path))
    return Participant(label, str(path), catalogs)


def from_manifest(label, path):
    path = pathlib.Path(path)
    document = json.loads(path.read_text(encoding="utf-8"))
    catalogs = {}
    for entry in document.get("catalogs", []):
        catalogs[entry["className"]] = {
            "catalogFileHash": entry["catalogFileHash"],
            "catalogFingerprint": entry["catalogFingerprint"],
            "registrationGroupCount": entry.get("registrationGroupCount"),
            "schemaCount": entry.get("schemaCount"),
            "registrationCount": entry.get("registrationCount"),
            "coverageClassName": entry.get("coverageClassName"),
        }
    return Participant(label, str(path), catalogs)


def read_participant(label, path):
    path = pathlib.Path(path)
    if (path / LEDGER_NAME).is_file():
        return from_ledger(label, path)
    if path.is_dir() and (path / "Assets").is_dir():
        return from_project(label, path)
    if path.is_file():
        return from_manifest(label, path)
    raise SystemExit("cannot read a build or manifest from " + str(path))


def compare(participants):
    """Compares every participant against the first; returns the problem list."""
    problems = []
    reference = participants[0]
    for other in participants[1:]:
        only_reference = sorted(set(reference.catalogs) - set(other.catalogs))
        only_other = sorted(set(other.catalogs) - set(reference.catalogs))
        for name in only_reference:
            problems.append("%s has catalog %s that %s does not" % (reference.label, name, other.label))
        for name in only_other:
            problems.append("%s has catalog %s that %s does not" % (other.label, name, reference.label))
        for name in sorted(set(reference.catalogs) & set(other.catalogs)):
            left = reference.catalogs[name]
            right = other.catalogs[name]
            for field in ("catalogFileHash", "catalogFingerprint", "registrationGroupCount", "schemaCount",
                          "registrationCount"):
                if left.get(field) is None or right.get(field) is None:
                    continue
                if left[field] != right[field]:
                    problems.append(
                        "%s: %s differs between %s and %s (%s vs %s)"
                        % (name, field, reference.label, other.label, left[field], right[field]))
        if reference.combined() != other.combined():
            problems.append("the combined build fingerprint differs between %s and %s" % (reference.label, other.label))
    return problems


def describe(participant):
    return {
        "label": participant.label,
        "source": participant.source,
        "combined": participant.combined(),
        "catalogs": {
            name: {
                "catalogFileHash": entry["catalogFileHash"],
                "catalogFingerprint": entry["catalogFingerprint"],
                "registrationGroupCount": entry.get("registrationGroupCount"),
                "schemaCount": entry.get("schemaCount"),
                "registrationCount": entry.get("registrationCount"),
            }
            for name, entry in sorted(participant.catalogs.items())
        },
    }


def self_test():
    """Falsifiability: an identical pair passes, a one-byte change in one catalog fails and is named."""
    root = pathlib.Path(tempfile.mkdtemp(prefix="gc025-fingerprints-"))
    try:
        ledger = {
            "catalogs": [
                {"className": "ProbeCatalog",
                 "catalogFileHash": "a" * 64, "catalogFingerprint": "b" * 64,
                 "registrationGroupCount": 3, "schemaCount": 1, "registrationCount": 3},
                {"className": "TraversalCatalog",
                 "catalogFileHash": "c" * 64, "catalogFingerprint": "d" * 64,
                 "registrationGroupCount": 4, "schemaCount": 1, "registrationCount": 8},
            ]
        }
        first = root / "first"
        second = root / "second"
        third = root / "third"
        for directory in (first, second, third):
            directory.mkdir()
        (first / LEDGER_NAME).write_text(json.dumps(ledger), encoding="utf-8")
        (second / LEDGER_NAME).write_text(json.dumps(ledger), encoding="utf-8")

        changed = json.loads(json.dumps(ledger))
        changed["catalogs"][1]["catalogFingerprint"] = "e" * 64
        (third / LEDGER_NAME).write_text(json.dumps(changed), encoding="utf-8")

        same = compare([from_ledger("first", first), from_ledger("second", second)])
        different = compare([from_ledger("first", first), from_ledger("third", third)])

        ok = (
            not same
            and len(different) == 2
            and any("TraversalCatalog" in problem and "catalogFingerprint" in problem for problem in different)
            and any("combined build fingerprint" in problem for problem in different)
        )
        print("self-test: identical builds agree (%d problem(s)); a changed fingerprint is reported and named (%d "
              "problem(s)): %s" % (len(same), len(different), "OK" if ok else "FAILED"))
        for problem in different:
            print("  - " + problem)
        return 0 if ok else 1
    finally:
        shutil.rmtree(root, ignore_errors=True)


def main():
    parser = argparse.ArgumentParser(description="Compare generated registration fingerprints of two builds.")
    parser.add_argument("--a", help="first build or manifest")
    parser.add_argument("--b", help="second build or manifest")
    parser.add_argument("--label-a", default="build-a")
    parser.add_argument("--label-b", default="build-b")
    parser.add_argument("--manifest", default=None,
                        help="committed reachability manifest to include as well (default: %s when it exists)"
                             % DEFAULT_MANIFEST)
    parser.add_argument("--json", default=None, help="destination of the verdict document")
    parser.add_argument("--ledger", default=None,
                        help="read this Unity project and write its catalog ledger, for a later comparison")
    parser.add_argument("--ledger-out", default=None, help="directory the ledger is written into")
    parser.add_argument("--self-test", action="store_true", help="prove the comparison is falsifiable and exit")
    arguments = parser.parse_args()

    if arguments.self_test:
        return self_test()

    if arguments.ledger:
        if not arguments.ledger_out:
            parser.print_help()
            return 2
        participant = read_participant(arguments.label_a, arguments.ledger)
        destination = pathlib.Path(arguments.ledger_out)
        destination.mkdir(parents=True, exist_ok=True)
        (destination / LEDGER_NAME).write_text(
            json.dumps(
                {
                    "format": "gamecore.catalog-ledger/1",
                    "task": "GC-025",
                    "source": participant.source,
                    "combined": participant.combined(),
                    "catalogs": [
                        dict(
                            {"className": name},
                            **{
                                key: value
                                for key, value in sorted(entry.items())
                                if key in ("catalogFileHash", "catalogFingerprint", "registrationGroupCount",
                                           "schemaCount", "registrationCount", "coverageClassName")
                            })
                        for name, entry in sorted(participant.catalogs.items())
                    ],
                },
                indent=2,
                sort_keys=False,
            ) + "\n",
            encoding="utf-8")
        print("wrote %s (%d catalog(s), combined=%s)"
              % (destination / LEDGER_NAME, len(participant.catalogs), participant.combined()))
        return 0

    if not arguments.a or not arguments.b:
        parser.print_help()
        return 2

    participants = [
        read_participant(arguments.label_a, arguments.a),
        read_participant(arguments.label_b, arguments.b),
    ]

    manifest_path = arguments.manifest
    if manifest_path is None:
        candidate = ROOT / DEFAULT_MANIFEST
        manifest_path = str(candidate) if candidate.is_file() else None
    if manifest_path:
        participants.append(from_manifest("committed-manifest", manifest_path))

    problems = compare(participants)

    for participant in participants:
        print("%s (%s): combined=%s, catalogs=%d"
              % (participant.label, participant.source, participant.combined(), len(participant.catalogs)))
        for name, entry in sorted(participant.catalogs.items()):
            print("  %s fileHash=%s fingerprint=%s" % (name, entry["catalogFileHash"], entry["catalogFingerprint"]))

    if problems:
        print("%d fingerprint difference(s):" % len(problems))
        for problem in problems:
            print("  - " + problem)
    else:
        print("every participant records the same generated registration fingerprints")

    if arguments.json:
        destination = pathlib.Path(arguments.json)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(
            json.dumps(
                {
                    "check": "registration-fingerprints",
                    "task": "GC-025",
                    "status": "Fail" if problems else "Pass",
                    "participants": [describe(participant) for participant in participants],
                    "problems": problems,
                },
                indent=2,
                sort_keys=True,
            ) + "\n",
            encoding="utf-8")

    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
