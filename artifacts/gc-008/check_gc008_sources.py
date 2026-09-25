#!/usr/bin/env python3
"""Host-side static checks for the GC-008 sources.

This machine has no C# compiler, so this runs the repository's own checker
(`tools/check_game_core_csharp.py`) over exactly the files this task added, using the same rules it applies to the
other packages: brace/paren/bracket balance, forbidden C# 10+ constructs, `#nullable enable`, TODO/FIXME markers,
missing-return heuristics and the engine-free boundary. It is a smoke check, not a compiler substitute.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path("/Users/yangcao/wkspace/gc-wt/gc-008")
sys.path.insert(0, str(ROOT / "tools"))

import check_game_core_csharp as checker  # noqa: E402

TARGETS = [
    "Packages/com.gamecore.planning/Runtime/Plans",
    "Packages/com.gamecore.planning/Tests/Plans",
    "Packages/com.gamecore.unity.runtime/Runtime/Assembly",
    "Packages/com.gamecore.unity.runtime/Tests/Assembly",
]

ENGINE_FREE = [
    ROOT / "Packages/com.gamecore.planning/Runtime",
]

problems: list[str] = []
files: list[Path] = []
for target in TARGETS:
    base = ROOT / target
    files.extend(sorted(path for path in base.rglob("*.cs")))

for path in files:
    text = path.read_text(encoding="utf-8")
    rel = path.relative_to(ROOT)
    checker.check_balance(rel, text, problems)
    checker.check_missing_return(rel, checker.strip_code(text), problems)

    stripped = checker.strip_code(text)
    for name, pattern in checker.FORBIDDEN.items():
        if pattern.search(stripped) or pattern.search(text):
            problems.append(f"{rel}: contains forbidden construct: {name}")

    if "TODO" in stripped or "FIXME" in stripped:
        problems.append(f"{rel}: contains TODO/FIXME")

    if not text.startswith("#nullable enable") and "#nullable" not in text:
        problems.append(f"{rel}: missing '#nullable enable'")

    if "UnityEngine" in stripped:
        problems.append(f"{rel}: UnityEngine reference; the engine-free assemblies must not use it")

    for base in ENGINE_FREE:
        try:
            rel.relative_to(base.relative_to(ROOT))
        except ValueError:
            continue
        if checker.ENGINE_TYPES.search(stripped):
            problems.append(f"{rel}: engine type reference in an engine-free assembly")

print(f"checked {len(files)} GC-008 C# file(s)")
if problems:
    print(f"{len(problems)} problem(s):")
    for problem in problems:
        print("  - " + problem)
    sys.exit(1)

print("ok")
