#!/usr/bin/env python3
"""Does a marker-free release player carry only the shipping surface, and does the qualification player prove the
inspection can see those markers at all?

The Wave 6 exit gate says an integrated revision must pass "a marker-free release build + release-surface inspection
(no fault latches, no telemetry, no W6Gate-only hooks)". Three existing tools settle most of that, each from a
different operand:

  * `tools/check_release_fault_free.py` — the latch sources compiled in both configurations, plus (with the compiled
    half) that the release assembly contains no latch type;
  * `tools/check_release_telemetry_free.py` — that every counting call site is compiled away without the telemetry
    marker package;
  * `tools/check_player_fault_free.py` — the built release player's managed assemblies and generated C++, for the
    GC-017 latch markers.

This tool settles the union the Wave 6 gate needs and none of them covers alone: the *qualification-only markers of
all five waves* (GC-017's latches, GC-021's delivery seat, GC-022's lifecycle stress, GC-023's replay fixture, the
Wave 5 and Wave 6 gate fixtures) must be ABSENT from a release player, and the SAME inspection pointed at the
qualification player must FIND a named, expected subset. The second half is what keeps the first half falsifiable: a
scan that finds nothing because it is looking in the wrong place, or because the marker strings changed, fails here
instead of reporting a clean release. A third check closes the same loop from the other side: the one mode the release
clone deliberately keeps (the GC-020 traversal course) must be present in BOTH players, which proves this inspection
really reads mode flags out of a built player rather than finding nothing everywhere.

The tool inspects bytes rather than symbols on purpose. A managed metadata heap and the IL2CPP generated C++ are both
UTF-8, so a byte search finds a surviving type name, mode flag or step prefix exactly as a reader would, and it needs
no toolchain on the machine running the check.

Usage:
    python3 tools/check_release_gate_free.py \\
        --player <release player dir> --qualification <qualification player dir> [--json <path>]

Exit codes: 0 the release player is free of every marker and the qualification player proves the scan works;
1 a marker was found in the release player, or the qualification player did not show the expected ones;
2 a required directory is missing or contains nothing to inspect.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

# The union of the qualification-only markers, grouped by the task that owns them. Each entry is a distinctive string:
# a type name, a package name, a mode flag or a step prefix. Ordinary English words are deliberately absent, because a
# false positive would be worse than a weaker check.
#
# THE TELEMETRY SWITCH IS NOT A GROUP HERE, deliberately: `GAMECORE_TELEMETRY` is a compile-time symbol whose
# [Conditional] call sites leave no distinctive string behind, so the telemetry release shape is settled where it is
# observable - `tools/check_release_telemetry_free.py` compiles the real sources both ways and scans both assemblies -
# rather than by a player-surface scan that could only look convincing.
MARKERS = {
    "gc017-fault-latch": (
        "AssemblyFaultInjection",
        "FaultBoundaryText",
        "FaultInjectedException",
        "GAMECORE_FAULT_INJECTION",
        "first-live-write",
        "structural-playback",
    ),
    "gc021-delivery-seat": (
        "Gc021Scenario",
        "Gc021CrashHook",
        "gc021-redelivery-applies-the-mutation-once",
        "-probeGc021",
    ),
    "gc022-lifecycle-stress": (
        "LifecycleStressScenario",
        "ProbeLifecycleStress",
        "-probeLifecycleStress",
        "GC_LIFECYCLE_STRESS_CYCLES",
    ),
    "gc023-replay-fixture": (
        "ReplayParallelJobs",
        "ReplayScenario",
        "ProbeReplay",
        "-probeReplay",
    ),
    "w5-gate": (
        "W5GateScenario",
        "ProbeW5Gate",
        "-probeW5Gate",
    ),
    "w6-gate": (
        "W6GateScenario",
        "W6CompositionAudit",
        "W6FamilyTraversalHost",
        "ProbeW6Gate",
        "-probeW6Gate",
        "GC_W6_GATE_CYCLES",
    ),
}

# The markers the qualification player must show: one per group above, so every group's scan is proved to work.
QUALIFICATION_EXPECTED = {
    "gc017-fault-latch": "GAMECORE_FAULT_INJECTION",
    "gc021-delivery-seat": "-probeGc021",
    "gc022-lifecycle-stress": "-probeLifecycleStress",
    "gc023-replay-fixture": "-probeReplay",
    "w5-gate": "-probeW5Gate",
    "w6-gate": "-probeW6Gate",
}

# One mode the release clone deliberately KEEPS: the GC-020 traversal course, whose local physics scene and committed
# animation/audio output are the optional engine surface the Wave 6 gate is about. It is asserted present in BOTH
# players, which is the second half of the falsifiability argument: this inspection really reads mode flags out of a
# built player, so its silence about the markers above means something.
KEPT_MODE = "-probeTraversal"

# The production seams the release player should still carry: the gate removes qualification fixtures, never the
# shipping code paths they exercise. Reported, not asserted, because a stripped player may legitimately drop a type no
# surviving reference needs, and this tool's verdict is about the qualification markers.
REQUIRED_IN_RELEASE = {
    "durable-delivery-core": ("DurableOutbox", "DeliveryKey"),
    "traversal-course": ("TraversalKeys", "TraversalRegistration"),
    "optional-engine-stages": ("UnityPhysicsSceneBackend", "CommittedAudioStage", "CommittedAnimationStage"),
}

SCAN_LIMIT = 64 * 1024 * 1024


def scan_file(path: str) -> dict:
    size = os.path.getsize(path)
    if size > SCAN_LIMIT:
        return {"path": path, "bytes": size, "skipped": "larger than %d bytes" % SCAN_LIMIT, "hits": {}}
    with open(path, "rb") as handle:
        data = handle.read()
    hits: dict[str, int] = {}
    for group, markers in MARKERS.items():
        for marker in markers:
            count = data.count(marker.encode("utf-8"))
            if count:
                hits[group + ":" + marker] = count
    return {"path": path, "bytes": size, "hits": hits}


def collect(root: str) -> list:
    """Managed assemblies, generated C++ and IL2CPP string metadata under one player directory."""
    found = []
    for dirpath, _dirnames, filenames in os.walk(root):
        for name in filenames:
            if name.endswith(".dll") and os.sep + "Managed" + os.sep in dirpath + os.sep:
                found.append(os.path.join(dirpath, name))
            elif name.endswith(".cpp") or name.endswith(".h") or name == "global-metadata.dat":
                found.append(os.path.join(dirpath, name))
    return sorted(found)


def scan_all(root: str) -> dict:
    files = collect(root)
    scans = [scan_file(path) for path in files]
    groups: dict[str, int] = {}
    findings = []
    for scan in scans:
        for key, count in scan["hits"].items():
            group, marker = key.split(":", 1)
            groups[group] = groups.get(group, 0) + count
            findings.append({"file": os.path.relpath(scan["path"], root), "marker": key, "count": count})
    return {"files": len(files), "scans": scans, "groups": groups, "findings": findings}


def read_blobs(root: str) -> list:
    """Every inspected file's bytes under one root, so a caller can search for cross-cutting strings."""
    blobs = []
    for path in collect(root):
        if os.path.getsize(path) > SCAN_LIMIT:
            continue
        with open(path, "rb") as handle:
            blobs.append(handle.read())
    return blobs


def required_present(root: str) -> dict:
    joined = b"\n".join(read_blobs(root))
    present = {}
    for seam, names in REQUIRED_IN_RELEASE.items():
        present[seam] = {name: joined.count(name.encode("utf-8")) for name in names}
    return present


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--player", required=True, metavar="DIR", help="the marker-free release player directory")
    parser.add_argument("--qualification", metavar="DIR", help="the qualification player directory (proves the scan)")
    parser.add_argument("--json", metavar="PATH", help="write the report here as well")
    args = parser.parse_args()

    if not os.path.isdir(args.player):
        print("check_release_gate_free: no such release player directory: %s" % args.player, file=sys.stderr)
        return 2

    release = scan_all(args.player)
    if release["files"] == 0:
        print("check_release_gate_free: nothing to inspect under %s" % args.player, file=sys.stderr)
        return 2

    problems = []
    for group in sorted(release["groups"]):
        problems.append(
            "the release player carries %d hit(s) of %s" % (release["groups"][group], group))

    release_seams = required_present(args.player)
    missing_seams = []
    for seam, names in sorted(release_seams.items()):
        for name, count in sorted(names.items()):
            if count == 0:
                missing_seams.append("%s (%s)" % (seam, name))

    qualification = None
    qualification_checks = {}
    if args.qualification:
        if not os.path.isdir(args.qualification):
            print(
                "check_release_gate_free: no such qualification player directory: %s" % args.qualification,
                file=sys.stderr)
            return 2
        qualification = scan_all(args.qualification)
        if qualification["files"] == 0:
            print(
                "check_release_gate_free: nothing to inspect under %s" % args.qualification, file=sys.stderr)
            return 2
        blobs = read_blobs(args.qualification)
        joined = b"\n".join(blobs)
        for group, marker in sorted(QUALIFICATION_EXPECTED.items()):
            found = joined.count(marker.encode("utf-8"))
            qualification_checks[group] = {"marker": marker, "count": found}
            if found == 0:
                problems.append(
                    "the qualification player does not show %s (%s), so this scan cannot prove the release "
                    "player is free of %s" % (marker, group, group))
        kept_in_qualification = joined.count(KEPT_MODE.encode("utf-8"))
        qualification_checks["kept-mode"] = {"marker": KEPT_MODE, "count": kept_in_qualification}
        if kept_in_qualification == 0:
            problems.append(
                "the qualification player does not carry %s, so this inspection cannot read mode flags at all"
                % KEPT_MODE)

    kept_release_count = b"\n".join(read_blobs(args.player)).count(KEPT_MODE.encode("utf-8"))
    if kept_release_count == 0:
        problems.append(
            "the release player does not carry %s, the one mode the clone deliberately keeps" % KEPT_MODE)

    report = {
        "task": "W6-GATE",
        "check": "release-gate-surface",
        "release": {
            "root": os.path.abspath(args.player),
            "filesInspected": release["files"],
            "groups": release["groups"],
            "findings": release["findings"],
            "requiredSeams": release_seams,
            "missingSeams": missing_seams,
            "keptMode": {"marker": KEPT_MODE, "count": kept_release_count},
        },
        "qualification": None
        if qualification is None
        else {
            "root": os.path.abspath(args.qualification),
            "filesInspected": qualification["files"],
            "expectedMarkers": qualification_checks,
        },
        "markers": {group: list(markers) for group, markers in sorted(MARKERS.items())},
        "problems": problems,
        "status": "Pass" if not problems else "Fail",
    }

    rendered = json.dumps(report, indent=2, sort_keys=True)
    print(rendered)
    if args.json:
        parent = os.path.dirname(os.path.abspath(args.json))
        if parent and not os.path.isdir(parent):
            os.makedirs(parent, exist_ok=True)
        with open(args.json, "w", encoding="utf-8") as handle:
            handle.write(rendered + "\n")

    return 1 if problems else 0


if __name__ == "__main__":
    raise SystemExit(main())
