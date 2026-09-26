#!/usr/bin/env python3
"""Attribute every native allocation in a Unity leak report to a frame, and gate on the result (GC-022).

Unity's shutdown leak report is either a bare count ("Leak Detected : Persistent allocates 57 individual
allocations") or, with leak detection set to EnabledWithStackTrace, a count followed by callstacks
("Found N leak(s) from callstack:" and its frames). A bare count attributes nothing, and `artifacts/gc-016/
BUILD_REPORT.md` records exactly that: 57 allocations that nobody could attribute. This tool turns such a log into an
attribution document, and it refuses to turn "nothing parsed" into "nothing leaked".

What it does
------------

* Parses every ``Leak Detected`` header, every ``Found N leak(s) from callstack:`` group and every
  ``Allocation of N bytes at 0x...`` line, in any mixture, and scores one block per group (or per headerless group,
  or per header that has no group).
* Classifies each block: ``gamecore`` when any frame names a GameCore type/namespace (``GameCore.`` or the IL2CPP
  ``GameCore_`` mangling), ``unity-engine`` when any frame names ``UnityEngine``/``UnityEditor``/``Unity.`` or carries
  the ``[Unity]`` module marker, ``third-party`` otherwise. A block with no frame that carries a *symbol* is
  ``unattributed``: an address alone, a truncated stack or a stack-trace-disabled report never becomes a clean class.
* Emits JSON with, per class, the block/allocation counts, the stated bytes and the distinct normalised frame
  signatures (top 5 frames) with their counts, plus the unattributed count, the header-versus-group count mismatch and
  the source log paths.
* Fails (exit 1) when a GameCore frame owns an allocation, because that is always a defect. Fails (exit 2) when an
  allocation is unattributed or when a signature has no declared bound in ``--policy``: an allocation with no bound is
  a defect, exactly like a GameCore one. Exits 0 only when every allocation is Unity-engine/third-party and every
  distinct signature is named by a bound in the policy file.
* ``--baseline`` reports which signatures are new relative to an earlier attribution JSON (or a plain signature list).

Resource policy vocabulary
--------------------------

A signature is bounded by a ``policy`` line of the form::

    bound: <normalised frame signature> | capacity=<live allocations> | bytes<=<ceiling> | released-by=<what frees it>

The signature matches exactly, or as a prefix of a full stack signature (so one allocation site bounds the stacks that
start at it). ``capacity``, ``bytes<=`` and ``released-by`` are all required: a signature declared without a bound is
a policy error, and a policy error leaves the signature unbounded.

``--self-test`` runs the in-repo fixtures under ``artifacts/gc-022/leak/fixtures`` through this same CLI as a
subprocess and checks the classifications, the exit codes and the deliberate non-fabrication rules.

Usage
-----

    python3 tools/attribute_native_leaks.py --log <player-or-editor.log> [--log ...] \\
        --out artifacts/gc-022/leak/native-leak-attribution.json \\
        --policy artifacts/gc-022/leak/policy.md [--baseline <earlier.json>]
    python3 tools/attribute_native_leaks.py --self-test
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
TOOL = Path(__file__).resolve()
FIXTURES = ROOT / "artifacts/gc-022/leak/fixtures"

# How many frames of a stack name its signature. Five is enough to name an allocation site plus its immediate owners
# while keeping two different stacks distinguishable.
TOP_FRAMES = 5

CLASSES = ("gamecore", "unity-engine", "third-party")

# `Leak Detected : Persistent allocates 57 individual allocations. To find out more please enable ...`
HEADER = re.compile(
    r"Leak Detected\s*:\s*(?P<allocator>[A-Za-z][\w\s\-]*?)\s+allocates\s+(?P<count>\d+)\s+individual allocation",
    re.IGNORECASE,
)
# `Found 3 leak(s) from callstack:` (the `(s)` is Unity's; tolerate a plain `leak` too).
GROUP = re.compile(r"Found\s+(?P<count>\d+)\s+leak(?:s|\(s\))?\s+from callstack\s*:", re.IGNORECASE)
# `Allocation of 4096 bytes at 0x0000012abc0300` (an optional symbol may follow the address).
ALLOCATION = re.compile(
    r"Allocation of\s+(?P<bytes>\d+)\s+bytes?\s+at\s+(?P<address>0x[0-9a-fA-F]+)(?P<symbol>.*)$"
)
# A frame line as Unity prints it with stack traces armed: its index, then the body — ` #0  (Mono JIT Code)
# [File.cs:12] GameCore.Type:Method (args)`, ` #4 UnsafeUtility::Malloc(...)`, ` #5 ???`. The index introduces the
# frame; the body is taken verbatim and classified by the owning-module and symbol rules below.
FRAME_INDEX = re.compile(r"^\s*#(?P<index>\d+)[\).:]?\s+(?P<body>.+?)\s*$")
# The body of such a frame: the owning module in parentheses, then the source location Unity resolved (brackets)
# and/or the symbol; either half may be absent, and `(wrapper ...)`, generics and `T&` refs are symbol characters.
MODULE_SYMBOL = re.compile(r"^\((?P<module>[^)]*)\)\s*(?P<symbol>.*?)\s*$")
# A frame introduced by an address, optionally carrying the module that owns the code and the resolved symbol.
FRAME_ADDRESS = re.compile(
    r"^\s*(?:#?\d+[\).:]?\s*)?(?P<address>0x[0-9a-fA-F]+)\s*(?:\((?P<module>[^)]*)\))?\s*(?P<symbol>.*?)\s*$"
)
# A frame without an address: `Namespace.Type:Method(args)`, `Namespace.Type.Method(args)`, `Namespace.Type.Method`.
FRAME_SYMBOL = re.compile(
    r"^\s*(?P<symbol>[A-Za-z_][\w`<>]*(?:(?:::)[\w`<>]+)*(?:\.[A-Za-z_][\w`<>]*)*"
    r"(?:\([^()]*\))?)\s*$"
)
# GameCore ownership: the managed namespace and the IL2CPP mangling (`GameCore_Unity_Runtime_..._Dispose`).
GAMECORE = re.compile(r"(?:^|[^A-Za-z0-9])GameCore(?:[._]|$)")
# Unity ownership: the engine namespaces and the symbols Unity ships inside its own binary.
UNITY = re.compile(r"\bUnityEngine\b|\bUnityEditor\b|\bUnity\.|\[Unity\]$")
POLICY_BOUND = re.compile(r"^\s*bound\s*:\s*(?P<body>.+?)\s*$", re.IGNORECASE)
ADDRESS = re.compile(r"0x[0-9a-fA-F]+")
OFFSET = re.compile(r"\+0x[0-9a-fA-F]+\s*$")


@dataclass
class Entry:
    """One scored block: a callstack group, or a header that carried no group."""

    allocator: str
    count: int
    hint: str = ""
    frames: list[str] = field(default_factory=list)
    unresolved: int = 0
    bytes_stated: int = 0
    bytes_seen: bool = False

    def classify(self) -> str:
        if not self.frames:
            return "unattributed"
        for frame in self.frames:
            if GAMECORE.search(frame):
                return "gamecore"
        for frame in self.frames:
            if UNITY.search(frame):
                return "unity-engine"
        return "third-party"

    def signature(self) -> str:
        return " <- ".join(self.frames[:TOP_FRAMES])


def normalise_frame(symbol: str, module: str) -> str:
    """Normalises a frame so two runs of the same call site produce the same signature."""
    text = OFFSET.sub("", symbol.strip())
    text = ADDRESS.sub("0xADDR", text)
    text = re.sub(r"\s+", " ", text).strip()
    if module:
        return f"{text} [{module}]" if text else f"[{module}]"
    return text


def parse_text(text: str) -> tuple[list[Entry], dict]:
    """Parses one leak report into scored blocks plus the parse facts.

    A header with groups is scored once per group, because a group *is* one leaked callstack and its own count. A
    header with no group is scored once, with the header's own count and no frames, so it lands in `unattributed`
    instead of vanishing. `Allocation of N bytes at 0x...` lines attach to the group they sit in (or to the header
    when they precede every group), and a frame that carries neither a symbol nor a module — an address alone, a
    `???` index frame — is counted as unresolved rather than as an attributed frame. Indexed frames
    (`#0  (Mono JIT Code) [File.cs:12] Symbol`) keep their module and their resolved symbol; a module with no
    symbol still names the owning code and attributes the frame.
    """
    headers: list[dict] = []
    leak_headers = 0
    header_allocation_total = 0
    headerless = 0
    current_header: dict | None = None
    current_entry: Entry | None = None
    in_stack = False

    def open_header(allocator: str, count: int | None, hint: str) -> dict:
        header = {"allocator": allocator, "count": count, "hint": hint, "groups": [], "own": None}
        headers.append(header)
        return header

    for raw in text.splitlines():
        line = raw.rstrip()

        match = HEADER.search(line)
        if match:
            leak_headers += 1
            count = int(match.group("count"))
            header_allocation_total += count
            hint = "stack-trace-disabled" if "to find out more" in line.lower() else ""
            current_header = open_header(match.group("allocator").strip(), count, hint)
            current_entry = None
            in_stack = False
            continue

        match = GROUP.search(line)
        if match:
            if current_header is None:
                headerless += 1
                current_header = open_header("<no Leak Detected header>", None, "")
            current_entry = Entry(
                allocator=current_header["allocator"],
                count=int(match.group("count")),
                hint=current_header["hint"],
            )
            current_header["groups"].append(current_entry)
            in_stack = True
            continue

        match = ALLOCATION.search(line)
        if match:
            if current_header is None:
                headerless += 1
                current_header = open_header("<no Leak Detected header>", None, "")
            target = current_entry
            if target is None:
                if current_header["own"] is None:
                    current_header["own"] = Entry(
                        allocator=current_header["allocator"], count=0, hint=current_header["hint"]
                    )
                target = current_header["own"]
            target.bytes_stated += int(match.group("bytes"))
            target.bytes_seen = True
            symbol = normalise_frame(match.group("symbol"), "")
            if symbol:
                target.frames.append(symbol)
            current_entry = target
            in_stack = True
            continue

        if in_stack:
            match = FRAME_INDEX.match(line)
            if match:
                body = match.group("body")
                if body == "???":
                    current_entry.unresolved += 1
                else:
                    module_match = MODULE_SYMBOL.search(body)
                    if module_match:
                        symbol = normalise_frame(module_match.group("symbol"), module_match.group("module"))
                    else:
                        symbol = normalise_frame(body, "")
                    if symbol:
                        current_entry.frames.append(symbol)
                    else:
                        current_entry.unresolved += 1
                continue

            match = FRAME_ADDRESS.match(line)
            if match:
                module = match.group("module") or ""
                symbol = normalise_frame(match.group("symbol"), module)
                if symbol:
                    current_entry.frames.append(symbol)
                else:
                    current_entry.unresolved += 1
                continue

            match = FRAME_SYMBOL.match(line)
            if match:
                symbol = normalise_frame(match.group("symbol"), "")
                if symbol:
                    current_entry.frames.append(symbol)
                    continue

            in_stack = False

    scored: list[Entry] = []
    count_mismatches: list[str] = []
    grouped = 0
    for header in headers:
        groups = header["groups"]
        own = header["own"]
        header_count = header["count"]
        if groups:
            scored.extend(groups)
            grouped += len(groups)
            if own is not None:
                scored.append(own)
            total = sum(group.count for group in groups)
            if header_count is not None and total != header_count:
                count_mismatches.append(
                    f"header claims {header_count} allocation(s) but its parsed block(s) total {total}"
                )
        elif own is not None:
            own.count = header_count or 0
            scored.append(own)
        else:
            scored.append(Entry(allocator=header["allocator"], count=header_count or 0, hint=header["hint"]))

    facts = {
        "leakHeaders": leak_headers,
        "headerAllocations": header_allocation_total,
        "headerlessBlocks": headerless,
        "countMismatch": count_mismatches,
        "groupedBlocks": grouped,
    }
    return scored, facts



def classify_blocks(entries: list[Entry]) -> dict:
    classes: dict[str, dict] = {
        name: {"blocks": 0, "allocations": 0, "bytes": 0, "bytesStated": False, "signatures": []}
        for name in CLASSES
    }
    unattributed = {"blocks": 0, "allocations": 0, "bytes": 0, "bytesStated": False, "hints": []}
    signature_counts: dict[str, dict[str, dict[str, int]]] = {name: {} for name in CLASSES}

    for entry in entries:
        name = entry.classify()
        if name == "unattributed":
            unattributed["blocks"] += 1
            unattributed["allocations"] += entry.count
            unattributed["bytesStated"] = unattributed["bytesStated"] or entry.bytes_seen
            unattributed["bytes"] += entry.bytes_stated
            if entry.hint and entry.hint not in unattributed["hints"]:
                unattributed["hints"].append(entry.hint)
            continue
        bucket = classes[name]
        bucket["blocks"] += 1
        bucket["allocations"] += entry.count
        bucket["bytesStated"] = bucket["bytesStated"] or entry.bytes_seen
        bucket["bytes"] += entry.bytes_stated
        signature = entry.signature()
        record = signature_counts[name].setdefault(signature, {"count": 0, "blocks": 0})
        record["count"] += entry.count
        record["blocks"] += 1

    for name in CLASSES:
        bucket = classes[name]
        signatures = [
            {"signature": signature, "allocations": record["count"], "blocks": record["blocks"]}
            for signature, record in signature_counts[name].items()
        ]
        signatures.sort(key=lambda item: (-item["allocations"], item["signature"]))
        bucket["signatures"] = signatures
        if not bucket["bytesStated"]:
            bucket["bytes"] = None
        del bucket["bytesStated"]
    if not unattributed["bytesStated"]:
        unattributed["bytes"] = None
    del unattributed["bytesStated"]

    return {"classes": classes, "unattributed": unattributed}


def load_baseline(path: Path) -> tuple[list[str], str]:
    """Loads an earlier attribution JSON or a plain signature list."""
    text = path.read_text(encoding="utf-8")
    try:
        payload = json.loads(text)
    except json.JSONDecodeError:
        payload = None

    if isinstance(payload, dict) and "classes" in payload:
        signatures: list[str] = []
        for bucket in payload["classes"].values():
            for item in bucket.get("signatures", []):
                signatures.append(str(item.get("signature", "")))
        signatures.extend(str(item) for item in payload.get("unattributedSignatures", []))
        return [signature for signature in signatures if signature], "json"

    signatures = [
        line.strip()
        for line in text.splitlines()
        if line.strip() and not line.strip().startswith("#")
    ]
    return signatures, "signatures"


def parse_policy(path: Path) -> tuple[list[dict], list[str], list[dict]]:
    """Parses the `bound:` declarations of a resource policy into bounds, problems and examples.

    A declaration whose signature or field values are `<placeholders>` is a format example, not a bound: it is
    reported under `examples` and can never cover a real signature.
    """
    bounds: list[dict] = []
    problems: list[str] = []
    examples: list[dict] = []
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        match = POLICY_BOUND.match(line)
        if not match:
            continue
        fields = [part.strip() for part in match.group("body").split("|")]
        signature = fields[0].strip().strip("`")
        bound = {"signature": normalise_frame(signature, ""), "capacity": "", "bytes": "", "releasedBy": ""}
        for field_text in fields[1:]:
            if "=" not in field_text:
                problems.append(f"{path}:{number}: bound field is not key=value: {field_text!r}")
                continue
            key, value = (part.strip() for part in field_text.split("=", 1))
            key_lower = key.lower()
            if key_lower == "capacity":
                bound["capacity"] = value
            elif key_lower in ("bytes<", "bytes<=", "bytes", "byte ceiling"):
                bound["bytes"] = value
            elif key_lower in ("released-by", "releasedby", "released_by"):
                bound["releasedBy"] = value
            else:
                problems.append(f"{path}:{number}: unknown bound field {key!r}")
        if not bound["signature"]:
            problems.append(f"{path}:{number}: bound line has no signature")
            continue

        if _is_placeholder_line(bound):
            bound["line"] = number
            examples.append(bound)
            continue

        missing = [
            name for name in ("capacity", "bytes", "releasedBy") if not bound[name]
        ]
        if missing:
            problems.append(
                f"{path}:{number}: bound for {bound['signature']!r} is incomplete, missing {', '.join(missing)}"
            )
        bounds.append(bound)
    return bounds, problems, examples


def _is_placeholder_line(bound: dict) -> bool:
    """True when a declaration still carries `<...>` placeholders, i.e. it documents the format, not a bound."""
    values = (bound["signature"], bound["capacity"], bound["bytes"], bound["releasedBy"])
    for value in values:
        if "<" in value and ">" in value:
            return True
    return False


def policy_coverage(signatures: list[str], bounds: list[dict]) -> tuple[list[str], list[dict]]:
    """Returns the uncovered signatures and the bounds that matched at least one signature."""
    uncovered: list[str] = []
    matched: list[dict] = []
    for signature in signatures:
        hit = None
        for bound in bounds:
            if not bound["capacity"] or not bound["bytes"] or not bound["releasedBy"]:
                continue
            if signature == bound["signature"] or signature.startswith(bound["signature"]):
                hit = bound
                break
        if hit is None:
            uncovered.append(signature)
        elif hit not in matched:
            matched.append(hit)
    return uncovered, matched


def build_report(log_paths: list[Path], baseline_path: Path | None, policy_path: Path | None) -> dict:
    entries: list[Entry] = []
    facts = {
        "leakHeaders": 0,
        "headerAllocations": 0,
        "headerlessBlocks": 0,
        "countMismatch": [],
        "groupedBlocks": 0,
    }
    for path in log_paths:
        text = path.read_text(encoding="utf-8", errors="replace")
        parsed, parsed_facts = parse_text(text)
        entries.extend(parsed)
        facts["leakHeaders"] += parsed_facts["leakHeaders"]
        facts["headerAllocations"] += parsed_facts["headerAllocations"]
        facts["headerlessBlocks"] += parsed_facts["headerlessBlocks"]
        facts["groupedBlocks"] += parsed_facts["groupedBlocks"]
        facts["countMismatch"].extend(
            [f"{path}: {problem}" for problem in parsed_facts["countMismatch"]]
        )

    scored = classify_blocks(entries)
    classes = scored["classes"]
    unattributed = scored["unattributed"]

    all_signatures: list[str] = []
    for name in CLASSES:
        all_signatures.extend(item["signature"] for item in classes[name]["signatures"])
    all_signatures.sort()

    policy: dict = {"path": None, "bounds": [], "problems": [], "uncovered": [], "matched": [], "examples": []}
    if policy_path is not None:
        bounds, problems, examples = parse_policy(policy_path)
        uncovered, matched = policy_coverage(all_signatures, bounds)
        policy = {
            "path": str(policy_path),
            "bounds": bounds,
            "problems": problems,
            "uncovered": uncovered,
            "matched": matched,
            "examples": examples,
        }

    baseline: dict | None = None
    if baseline_path is not None:
        signatures, kind = load_baseline(baseline_path)
        not_in_baseline = [
            signature
            for signature in all_signatures
            if not any(
                signature == entry or signature.startswith(entry) for entry in signatures
            )
        ]
        baseline = {
            "path": str(baseline_path),
            "format": kind,
            "signatures": len(signatures),
            "notInBaseline": not_in_baseline,
        }

    gamecore_allocations = classes["gamecore"]["allocations"]
    unattributed_blocks = unattributed["blocks"]
    covered = bool(all_signatures) and not policy["uncovered"]
    if policy_path is None:
        covered = not all_signatures

    if gamecore_allocations:
        verdict = "defect"
        exit_code = 1
    elif unattributed_blocks:
        verdict = "unattributed"
        exit_code = 2
    elif all_signatures and not covered:
        verdict = "unbounded"
        exit_code = 2
    else:
        verdict = "clean"
        exit_code = 0

    notes: list[str] = []
    if policy["problems"]:
        notes.extend(f"policy: {problem}" for problem in policy["problems"])
    if unattributed["blocks"]:
        notes.append(
            f"{unattributed['blocks']} block(s) / {unattributed['allocations']} allocation(s) carry no parseable "
            "frame: they are reported as unattributed and never as zero"
        )
    if unattributed["hints"]:
        notes.append("unattributed hint(s): " + ", ".join(unattributed["hints"]))
    if facts["headerlessBlocks"]:
        notes.append(f"{facts['headerlessBlocks']} callstack group(s) had no Leak Detected header")
    if facts["countMismatch"]:
        notes.extend(facts["countMismatch"])

    report = {
        "tool": "tools/attribute_native_leaks.py",
        "task": "GC-022",
        "logs": [str(path) for path in log_paths],
        "leakHeaders": facts["leakHeaders"],
        "headerAllocations": facts["headerAllocations"],
        "blocks": len(entries),
        "unattributed": unattributed_blocks,
        "totals": {
            "allocations": sum(bucket["allocations"] for bucket in classes.values()) + unattributed["allocations"],
            "blocks": len(entries),
        },
        "classes": classes,
        "unattributedDetail": unattributed,
        "policy": policy,
        "baseline": baseline,
        "verdict": verdict,
        "exitCode": exit_code,
        "notes": notes,
    }
    return report


def summarise(report: dict) -> str:
    lines = [
        "native leak attribution: "
        f"logs={len(report['logs'])} headers={report['leakHeaders']} blocks={report['blocks']} "
        f"allocations={report['totals']['allocations']}",
    ]
    for name in CLASSES:
        bucket = report["classes"][name]
        bytes_text = "unstated" if bucket["bytes"] is None else str(bucket["bytes"])
        lines.append(
            f"  {name:<12}: {bucket['blocks']} block(s), {bucket['allocations']} allocation(s), "
            f"bytes={bytes_text}, {len(bucket['signatures'])} signature(s)"
        )
    unattributed = report["unattributedDetail"]
    bytes_text = "unstated" if unattributed["bytes"] is None else str(unattributed["bytes"])
    hints = (" [" + ", ".join(unattributed["hints"]) + "]") if unattributed["hints"] else ""
    lines.append(
        f"  unattributed: {unattributed['blocks']} block(s), {unattributed['allocations']} allocation(s), "
        f"bytes={bytes_text}{hints}"
    )
    for name in CLASSES:
        for item in report["classes"][name]["signatures"]:
            lines.append(f"    {name}: x{item['allocations']} {item['signature']}")
    if report["policy"]["path"]:
        lines.append(
            f"policy: {report['policy']['path']} ({len(report['policy']['bounds'])} bound(s), "
            f"{len(report['policy']['examples'])} format example(s), "
            f"{len(report['policy']['uncovered'])} uncovered signature(s), "
            f"{len(report['policy']['problems'])} problem(s))"
        )
    else:
        lines.append("policy: <none>")
    if report["baseline"] is not None:
        lines.append(
            f"baseline: {report['baseline']['path']} "
            f"({len(report['baseline']['notInBaseline'])} signature(s) not in it)"
        )
    for note in report["notes"]:
        lines.append(f"note: {note}")
    lines.append(f"verdict: {report['verdict']} (exit {report['exitCode']})")
    return "\n".join(lines)


def run_self_test() -> int:
    checks: list[tuple[str, bool, str]] = []

    def check(name: str, condition: bool, detail: str = "") -> None:
        checks.append((name, bool(condition), detail))

    def fixture(name: str) -> Path:
        return FIXTURES / name

    def run(args: list[str]) -> tuple[int, dict | None, str]:
        with tempfile.TemporaryDirectory() as directory:
            out = Path(directory) / "attribution.json"
            proc = subprocess.run(
                [sys.executable, str(TOOL), *args, "--out", str(out)],
                capture_output=True,
                text=True,
            )
            payload = json.loads(out.read_text(encoding="utf-8")) if out.exists() else None
            return proc.returncode, payload, proc.stdout + proc.stderr

    ok_policy = fixture("policy-ok.md")
    missing_policy = fixture("policy-missing.md")
    malformed_policy = fixture("policy-malformed.md")

    # 1. A GameCore frame is a defect, and a policy bound cannot authorize it.
    rc, data, output = run(["--log", str(fixture("gamecore-stack.log"))])
    check(
        "gamecore stack exits 1 and names a GameCore block",
        rc == 1 and data is not None and data["classes"]["gamecore"]["allocations"] == 1
        and data["verdict"] == "defect",
        output,
    )
    rc, data, output = run(["--log", str(fixture("gamecore-stack.log")), "--policy", str(ok_policy)])
    check(
        "a GameCore allocation stays exit 1 even when the policy covers its signature",
        rc == 1 and data is not None and data["classes"]["gamecore"]["allocations"] == 1,
        output,
    )

    # 2. An all-Unity stack is unity-engine: unbounded without a policy, clean with one.
    rc, data, output = run(["--log", str(fixture("unity-only-stack.log"))])
    check(
        "unity-only stack without a policy exits 2 as unbounded",
        rc == 2 and data is not None and data["classes"]["unity-engine"]["allocations"] == 3
        and data["verdict"] == "unbounded",
        output,
    )
    rc, data, output = run(["--log", str(fixture("unity-only-stack.log")), "--policy", str(ok_policy)])
    check(
        "unity-only stack with a complete policy exits 0",
        rc == 0 and data is not None and data["verdict"] == "clean"
        and not data["policy"]["uncovered"]
        and data["classes"]["unity-engine"]["bytes"] is None
        and data["classes"]["unity-engine"]["signatures"][0]["allocations"] == 3,
        output,
    )

    # 3. The near miss: GameCore in a comment and in the header prose, never in a frame.
    rc, data, output = run(["--log", str(fixture("near-miss-comment.log")), "--policy", str(ok_policy)])
    check(
        "GameCore only in a comment/header does not classify as gamecore",
        rc == 0 and data is not None and data["classes"]["gamecore"]["allocations"] == 0
        and data["classes"]["unity-engine"]["allocations"] == 1
        and data["unattributed"] == 0,
        output,
    )

    # 4. A truncated stack and an address-only stack are unattributed, never a clean class.
    rc, data, output = run(["--log", str(fixture("truncated-stack.log"))])
    check(
        "a truncated stack is unattributed with its allocation count",
        rc == 2 and data is not None and data["unattributed"] == 1
        and data["unattributedDetail"]["allocations"] == 4
        and all(data["classes"][name]["allocations"] == 0 for name in CLASSES),
        output,
    )
    rc, data, output = run(["--log", str(fixture("address-only-stack.log"))])
    check(
        "an address-only stack is unattributed, not third-party",
        rc == 2 and data is not None and data["unattributed"] == 1
        and data["classes"]["third-party"]["allocations"] == 0,
        output,
    )

    # 4b. The real Editor format: indexed `#N (Module) [File.cs:line] Symbol` frames, `???` holes and a bare
    # `UnsafeUtility::Malloc(...)` — every frame attributes, a GameCore frame wins over a later Unity frame, and a
    # module-only frame still names its owner. This is the shape that used to parse as 49 unattributed blocks.
    rc, data, output = run(["--log", str(fixture("mono-indexed-stack.log"))])
    check(
        "indexed mono frames attribute instead of falling unattributed",
        rc == 1 and data is not None
        and data["classes"]["gamecore"]["allocations"] == 2
        and data["classes"]["gamecore"]["blocks"] == 1
        and data["classes"]["unity-engine"]["allocations"] == 1
        and data["unattributed"] == 0
        and data["totals"]["allocations"] == 3,
        output,
    )
    gamecore_signature = data["classes"]["gamecore"]["signatures"][0]["signature"]
    frames = gamecore_signature.split(" <- ")
    check(
        "an indexed frame keeps its resolved location, symbol and module",
        frames[0] == "[OwnershipSchedulePipeline.cs:336] GameCore.Unity.Runtime.Integration."
        "OwnershipSchedulePipeline:Build (System.Collections.Generic.IReadOnlyList`1<GameCore.Contracts.PluginManifest>,"
        "GameCore.Unity.Runtime.Time.IScheduleDispatchKindResolver,GameCore.Planning.Ownership.ISlotMigrationRegistry) "
        "[Mono JIT Code]"
        and "???" not in gamecore_signature
        and frames[-1] == "UnsafeUtility::Malloc(long, int, NativeCollection::Allocator, ScriptingExceptionPtr*)",
        gamecore_signature,
    )

    # 5. The 57-allocation shape: a bare count with the enable hint is unattributed, never zero.
    rc, data, output = run(["--log", str(fixture("summary-only.log"))])
    check(
        "a stack-trace-disabled report reports 57 unattributed allocations",
        rc == 2 and data is not None and data["unattributed"] == 1
        and data["unattributedDetail"]["allocations"] == 57
        and "stack-trace-disabled" in data["unattributedDetail"]["hints"]
        and data["totals"]["allocations"] == 57,
        output,
    )

    # 6. An empty log is all zeros, and no number is invented.
    rc, data, output = run(["--log", str(fixture("empty.log"))])
    check(
        "an empty log reports blocks=0 and unattributed=0 with exit 0",
        rc == 0 and data is not None and data["blocks"] == 0 and data["unattributed"] == 0
        and data["totals"]["allocations"] == 0
        and data["classes"]["gamecore"]["bytes"] is None,
        output,
    )

    # 7. A mixed report: per-class counts, stated bytes, signature counts and a baseline diff.
    rc, data, output = run(
        [
            "--log",
            str(fixture("mixed.log")),
            "--policy",
            str(ok_policy),
            "--baseline",
            str(fixture("baseline-unity.txt")),
        ]
    )
    checks_ok = (
        rc == 1
        and data is not None
        and data["classes"]["gamecore"]["allocations"] == 2
        and data["classes"]["gamecore"]["bytes"] == 128
        and data["classes"]["unity-engine"]["allocations"] == 3
        and data["classes"]["unity-engine"]["bytes"] == 4096
        and len(data["classes"]["gamecore"]["signatures"]) == 1
        and data["classes"]["gamecore"]["signatures"][0]["allocations"] == 2
        and data["baseline"]["notInBaseline"]
    )
    check("a mixed report counts per class, states bytes and diffs a baseline", checks_ok, output)

    # 8. A header whose count disagrees with its groups is surfaced, and the groups are what is counted.
    rc, data, output = run(["--log", str(fixture("count-mismatch.log"))])
    check(
        "a header/group count mismatch is noted and the groups are counted",
        rc == 2 and data is not None and data["headerAllocations"] == 5
        and data["totals"]["allocations"] == 2
        and any("total 2" in note for note in data["notes"]),
        output,
    )

    # 9. Policy coverage is enforced: a missing bound and a malformed bound both leave a signature unbounded.
    rc, data, output = run(["--log", str(fixture("third-party-stack.log")), "--policy", str(missing_policy)])
    check(
        "a signature the policy does not name exits 2",
        rc == 2 and data is not None and data["policy"]["uncovered"]
        and data["classes"]["third-party"]["allocations"] == 2,
        output,
    )
    rc, data, output = run(["--log", str(fixture("third-party-stack.log")), "--policy", str(malformed_policy)])
    check(
        "a bound missing its released-by clause is a policy problem, not a bound",
        rc == 2 and data is not None and data["policy"]["problems"]
        and data["policy"]["uncovered"],
        output,
    )

    # 10. A baseline given as an earlier attribution JSON is accepted too.
    with tempfile.TemporaryDirectory() as directory:
        baseline_json = Path(directory) / "baseline.json"
        rc_first, data_first, output = run(
            ["--log", str(fixture("unity-only-stack.log")), "--policy", str(ok_policy)]
        )
        wrote = subprocess.run(
            [
                sys.executable,
                str(TOOL),
                "--log",
                str(fixture("unity-only-stack.log")),
                "--policy",
                str(ok_policy),
                "--out",
                str(baseline_json),
            ],
            capture_output=True,
            text=True,
        )
        check(
            "an attribution JSON can be written and reused",
            rc_first == 0 and wrote.returncode == 0 and baseline_json.exists() and data_first is not None,
            output + wrote.stdout + wrote.stderr,
        )
        rc, data, output = run(
            [
                "--log",
                str(fixture("mixed.log")),
                "--policy",
                str(ok_policy),
                "--baseline",
                str(baseline_json),
            ]
        )
        check(
            "a JSON baseline diffs against the run's signatures",
            data is not None and data["baseline"]["format"] == "json"
            and any("GameCore" in signature for signature in data["baseline"]["notInBaseline"]),
            output,
        )

    # 11. A missing log is an error, not a clean pass.
    rc, data, output = run(["--log", str(fixture("does-not-exist.log"))])
    check(
        "a missing log file exits non-zero with a message",
        rc != 0 and data is None and "not found" in output,
        output,
    )

    failures = [entry for entry in checks if not entry[1]]
    for name, passed, detail in checks:
        print(f"  {'PASS' if passed else 'FAIL'} {name}")
        if not passed and detail:
            print("      " + detail.strip().replace("\n", "\n      "))
    print(f"self-test: {len(checks) - len(failures)}/{len(checks)} check(s) passed")
    return 1 if failures else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Attribute Unity native leak allocations to frames and gate on the resource policy (GC-022).",
    )
    parser.add_argument("--log", action="append", default=[], metavar="PATH", help="Unity log to parse (repeatable)")
    parser.add_argument("--out", metavar="PATH", help="write the attribution JSON here")
    parser.add_argument("--baseline", metavar="PATH", help="earlier attribution JSON or signature list")
    parser.add_argument("--policy", metavar="PATH", help="resource policy naming each signature's bound")
    parser.add_argument("--self-test", action="store_true", help="run the in-repo fixtures and exit")
    args = parser.parse_args(argv)

    if args.self_test:
        return run_self_test()

    if not args.log:
        print("no --log given: nothing can be attributed, and an unread log is not a clean run", file=sys.stderr)
        return 2

    log_paths: list[Path] = []
    for raw in args.log:
        path = Path(raw)
        if not path.is_file():
            print(f"log not found: {path}", file=sys.stderr)
            return 2
        log_paths.append(path)

    baseline_path = Path(args.baseline) if args.baseline else None
    if baseline_path is not None and not baseline_path.is_file():
        print(f"baseline not found: {baseline_path}", file=sys.stderr)
        return 2

    policy_path = Path(args.policy) if args.policy else None
    if policy_path is not None and not policy_path.is_file():
        print(f"policy not found: {policy_path}", file=sys.stderr)
        return 2

    report = build_report(log_paths, baseline_path, policy_path)
    print(summarise(report))

    if args.out:
        out_path = Path(args.out)
        if out_path.parent and str(out_path.parent):
            out_path.parent.mkdir(parents=True, exist_ok=True)
        out_path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(f"attribution: {out_path}")

    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())
