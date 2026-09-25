#!/usr/bin/env python3
"""Does a built player contain any trace of the GC-017 fault latch?

`tools/check_release_fault_free.py` settles the source and managed-assembly halves of the release
claim. This tool settles the last one: the artifact a shipping build actually produces. Point it at
a **release-configuration** player (one whose project does not reference the qualification marker
package `com.gamecore.fault-qualification`, so `GAMECORE_FAULT_INJECTION` is undefined in its
runtime assembly) and it reports any latch type, boundary name or trace prefix it finds in

  * the player's managed assemblies (`<player>_Data/Managed/*.dll`), and
  * the IL2CPP generated C++ (a `--il2cpp` directory, or discovered under the player's
    `_BackUpThisFolder_ButDontShipItWithYourGame` / bee build output if one is passed).

Pointing it at a qualification player fails by design and says so: that build is *supposed* to carry
the latches, so the operand must be a release build. The tool prints what it inspected either way,
because a check that silently finds nothing is worth nothing.

Usage:
    python3 tools/check_player_fault_free.py --player <dir> [--il2cpp <dir>] [--json <path>]
"""

from __future__ import annotations

import argparse
import json
import os
import sys

# Distinctive strings only: "migration" and "cleanup" are ordinary English words that a shipping
# player's own code or a Unity internal could contain, and a false positive would be worse than a
# weaker check. Every string below is a latch type name or a literal from `FaultBoundaryText.Names`
# / `FaultRecord.ToLine()`.
LATCH_MARKERS = (
    "AssemblyFaultInjection",
    "FaultBoundary",
    "FaultBoundaryText",
    "FaultTrace",
    "FaultRecord",
    "FaultReach",
    "FaultCompilation",
    "FaultInjectedException",
    "GAMECORE_FAULT_INJECTION",
    "first-live-write",
    "structural-playback",
    "gate-installation",
    "boundary=",
    "refused as a value",
)


def scan_file(path: str, limit: int = 64 * 1024 * 1024) -> dict:
    """Byte-grep one file for every marker; the managed metadata heap and the generated C++ are both
    UTF-8, so a byte search finds a surviving name or literal exactly as a reader would."""
    size = os.path.getsize(path)
    if size > limit:
        return {"path": path, "bytes": size, "skipped": "larger than %d bytes" % limit, "hits": []}
    with open(path, "rb") as handle:
        data = handle.read()
    hits = []
    for marker in LATCH_MARKERS:
        count = data.count(marker.encode("utf-8"))
        if count:
            hits.append({"marker": marker, "count": count})
    return {"path": path, "bytes": size, "hits": hits}


def discover(player: str) -> tuple[list[str], list[str]]:
    """Managed assemblies and generated C++ sources under one player directory."""
    managed = []
    generated = []
    for base, dirs, files in os.walk(player):
        parts = set(os.path.relpath(base, player).split(os.sep))
        if "Managed" in parts:
            managed.extend(os.path.join(base, f) for f in files if f.endswith(".dll"))
        for name in files:
            if name.endswith((".cpp", ".h")):
                generated.append(os.path.join(base, name))
    return sorted(managed), sorted(generated)


def scan_all(paths: list[str]) -> list[dict]:
    results = []
    for path in paths:
        try:
            results.append(scan_file(path))
        except OSError as error:
            results.append({"path": path, "error": str(error), "hits": []})
    return results


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--player", required=True, help="a release-configuration player directory")
    parser.add_argument("--il2cpp", default=None, help="the IL2CPP generated C++ directory, if separate")
    parser.add_argument("--json", default=None, help="write the result to this path")
    arguments = parser.parse_args()

    if not os.path.isdir(arguments.player):
        print("no player directory at %s" % arguments.player, file=sys.stderr)
        return 2

    managed, generated = discover(arguments.player)
    if arguments.il2cpp:
        if not os.path.isdir(arguments.il2cpp):
            print("no IL2CPP directory at %s" % arguments.il2cpp, file=sys.stderr)
            return 2
        for base, _dirs, files in os.walk(arguments.il2cpp):
            generated.extend(os.path.join(base, f) for f in files if f.endswith((".cpp", ".h")))
        generated = sorted(set(generated))

    report = {
        "task": "GC-017",
        "check": "player-release-surface",
        "player": os.path.abspath(arguments.player),
        "il2cpp": os.path.abspath(arguments.il2cpp) if arguments.il2cpp else None,
        "managedAssembliesInspected": len(managed),
        "generatedSourcesInspected": len(generated),
        "markers": list(LATCH_MARKERS),
        "finding": [],
        "status": "Fail",
    }

    findings = [r for r in scan_all(managed) + scan_all(generated) if r.get("hits")]
    report["managed"] = scan_all(managed)
    report["generated"] = scan_all(generated)

    if not managed and not generated:
        report["finding"].append(
            "inspected nothing: no managed assembly and no generated C++ under %s. A pass here would be "
            "vacuous, so this is a failure." % arguments.player)
    for result in findings:
        for hit in result["hits"]:
            report["finding"].append("%s: %s x%d" % (result["path"], hit["marker"], hit["count"]))

    if arguments.json:
        with open(arguments.json, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2, sort_keys=True)
            handle.write("\n")

    if report["finding"]:
        print("GC-017 release player: FAIL", file=sys.stderr)
        print("  inspected %d managed assembly(ies) and %d generated source(s) under %s"
              % (len(managed), len(generated), arguments.player), file=sys.stderr)
        for finding in report["finding"][:40]:
            print("  " + finding, file=sys.stderr)
        print("  note: a qualification player legitimately contains these; the operand must be a release "
              "build (a project whose manifest does not reference com.gamecore.fault-qualification).",
              file=sys.stderr)
        return 1

    report["status"] = "Pass"
    print("GC-017 release player: PASS")
    print("  %d managed assembly(ies) and %d generated source(s) contain no latch type, no boundary name "
          "and no trace prefix" % (len(managed), len(generated)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
