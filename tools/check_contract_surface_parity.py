#!/usr/bin/env python3
"""Host-side parity check between the frozen W0 API snapshot and the production contract sources (GC-003).

The real gate is `dotnet/tests/GameCore.Contracts.Tests`, which compares a compiled API listing against the
committed snapshot. On a host with no C# compiler this script performs the part that can be done from source
text: every enum and every enum value the snapshot declares must exist in the production sources with the same
numeric value, and every type header's kind and name must exist. Additions are reported, not failed, because the
GC-003 gate allows a strict superset (additions are listed in artifacts/gc-003/HANDOFF.md).

usage: python3 tools/check_contract_surface_parity.py [snapshot] [production source directory]
"""

from __future__ import annotations

import pathlib
import re
import sys

DEFAULT_SNAPSHOT = "tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt"
DEFAULT_SOURCES = "Packages/com.gamecore.contracts/Runtime"

TYPE_HEADER = re.compile(r"^type (\w+) ([\w\.]+)")
ENUM_VALUE = re.compile(r"^\s+enumvalue public (\w+) = (-?\d+)$")
ENUM_DECLARATION = re.compile(r"public enum (\w+)(?:\s*:[^\{]+)?\s*\{(.*?)\n    \}", re.S)
ENUM_MEMBER = re.compile(r"(\w+)\s*=\s*(-?\d+)")


def parse_snapshot(path: pathlib.Path) -> tuple[dict[str, str], dict[str, dict[str, int]], dict[str, int]]:
    """Returns (type name -> kind, enum name -> {value name -> number}, kind counts)."""
    kinds: dict[str, str] = {}
    enums: dict[str, dict[str, int]] = {}
    current_enum: str | None = None
    kind_counts: dict[str, int] = {}

    for raw in path.read_text(encoding="utf-8").split("\n"):
        line = raw.rstrip()
        if line.startswith("#") or not line:
            continue

        header = TYPE_HEADER.match(line)
        if header:
            kind, full_name = header.group(1), header.group(2)
            short = full_name.split(".")[-1]
            kinds[short] = kind
            kind_counts[kind] = kind_counts.get(kind, 0) + 1
            current_enum = short if kind == "enum" else None
            if current_enum is not None:
                enums[current_enum] = {}
            continue

        value = ENUM_VALUE.match(line)
        if value and current_enum is not None:
            enums[current_enum][value.group(1)] = int(value.group(2))

    return kinds, enums, kind_counts


def parse_sources(directory: pathlib.Path) -> dict[str, dict[str, int]]:
    """Returns enum name -> {value name -> number} for every enum declared in the production sources."""
    enums: dict[str, dict[str, int]] = {}
    for path in sorted(directory.rglob("*.cs")):
        text = path.read_text(encoding="utf-8")
        for name, body in ENUM_DECLARATION.findall(text):
            members = {}
            for member_name, member_value in ENUM_MEMBER.findall(body):
                members[member_name] = int(member_value)
            enums[name] = members

    return enums


def main() -> int:
    snapshot_path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SNAPSHOT)
    sources_path = pathlib.Path(sys.argv[2] if len(sys.argv) > 2 else DEFAULT_SOURCES)

    if not snapshot_path.exists():
        print("no snapshot at " + str(snapshot_path))
        return 2
    if not sources_path.is_dir():
        print("no production source directory at " + str(sources_path))
        return 2

    kinds, snapshot_enums, kind_counts = parse_snapshot(snapshot_path)
    source_enums = parse_sources(sources_path)
    problems: list[str] = []
    additions: list[str] = []

    for enum_name, values in sorted(snapshot_enums.items()):
        if enum_name not in source_enums:
            problems.append("enum " + enum_name + " is in the snapshot but not in the production sources")
            continue

        source_values = source_enums[enum_name]
        for value_name, value_number in sorted(values.items()):
            if value_name not in source_values:
                problems.append(
                    "enum " + enum_name + " lost value " + value_name + " = " + str(value_number)
                )
            elif source_values[value_name] != value_number:
                problems.append(
                    "enum "
                    + enum_name
                    + " value "
                    + value_name
                    + " is "
                    + str(source_values[value_name])
                    + " in production but "
                    + str(value_number)
                    + " in the snapshot"
                )

        for value_name, value_number in sorted(source_values.items()):
            if value_name not in values:
                additions.append(
                    "enum " + enum_name + " adds " + value_name + " = " + str(value_number)
                )

    print(
        str(snapshot_path)
        + ": "
        + str(len(kinds))
        + " types ("
        + ", ".join(k + " " + str(v) for k, v in sorted(kind_counts.items()))
        + "), "
        + str(len(snapshot_enums))
        + " enums checked against "
        + str(sources_path)
    )

    if additions:
        print(str(len(additions)) + " addition(s) beyond the frozen snapshot (allowed, must be listed in HANDOFF):")
        for addition in additions:
            print("  + " + addition)

    if problems:
        print(str(len(problems)) + " problem(s):")
        for problem in problems:
            print("  - " + problem)
        return 1

    print("every snapshotted enum and enum value exists in the production sources with the same numeric value")
    return 0


if __name__ == "__main__":
    sys.exit(main())
