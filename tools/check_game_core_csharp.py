#!/usr/bin/env python3
"""Host-side static checks for the GameCore C# sources (GC-003).

This machine has no C# compiler, so this script performs the checks a compiler would fail on immediately:
brace/paren/bracket balance outside strings and comments, forbidden C# 10+ constructs, leftover draft markers,
and namespace/using sanity. It is a smoke check, not a compiler substitute; the build host is the real gate.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

TARGETS = [
    "Packages/com.gamecore.contracts",
    "Packages/com.gamecore.content.compiler",
    "dotnet/src/GameCore.Contracts",
    "dotnet/src/GameCore.Content.Compiler",
    "dotnet/tests/GameCore.Contracts.Tests",
    "dotnet/tests/GameCore.Content.Compiler.Tests",
    "dotnet/tests/GameCore.ProtocolFixtures.Production.Tests",
    "dotnet/src/GameCore.ProtocolFixtures.Production",
    "unity/GameCore.Validation/Assets",
    "dotnet/tools/GameCore.ApiSnapshot",
]

FORBIDDEN = {
    "file-scoped namespace": re.compile(r"^\s*namespace\s+[\w\.]+\s*;", re.M),
    "global using": re.compile(r"^\s*global\s+using\b", re.M),
    "record declaration": re.compile(r"\brecord\s+(?:class|struct)\s+\w|\brecord\s+\w+\s*\(", re.M),
    "init accessor": re.compile(r"\{\s*get;\s*init;", re.M),
    "required member": re.compile(r"\brequired\s+[\w<>\[\]\?]+\s+\w+\s*\{", re.M),
    "static abstract member": re.compile(r"\bstatic\s+abstract\b", re.M),
    "raw string literal": re.compile(r'"""', re.M),
    "list pattern": re.compile(r"\[\.\.\s", re.M),
}

# Unity type names that must never appear in the engine-free assemblies.
ENGINE_TYPES = re.compile(r"\bUnityEngine\b|\bUnity\.Entities\b|\bUnityEngine\.|Unity\.Burst|\bGameObject\b|\bJobHandle\b", re.M)


def strip_code(text: str) -> str:
    """Removes comments and string/char literal contents so brace counting is meaningful."""
    out = []
    i = 0
    n = len(text)
    while i < n:
        c = text[i]
        if c == "/" and i + 1 < n and text[i + 1] == "/":
            while i < n and text[i] != "\n":
                i += 1
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                i += 1
            i += 2
            continue
        if c == "@" and i + 1 < n and text[i + 1] == '"':
            i += 2
            while i < n:
                if text[i] == '"':
                    if i + 1 < n and text[i + 1] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                i += 1
            continue
        if c == '"':
            i += 1
            while i < n and text[i] != '"':
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        if c == "'":
            i += 1
            while i < n and text[i] != "'":
                i += 2 if text[i] == "\\" else 1
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def check_balance(path: Path, text: str, problems: list[str]) -> None:
    code = strip_code(text)
    pairs = {")": "(", "]": "[", "}": "{"}
    stack: list[tuple[str, int]] = []
    line = 1
    for ch in code:
        if ch == "\n":
            line += 1
        elif ch in "([{":
            stack.append((ch, line))
        elif ch in ")]}":
            if not stack or stack[-1][0] != pairs[ch]:
                problems.append(f"{path}: unbalanced '{ch}' at line {line}")
                return
            stack.pop()
    if stack:
        opener, opener_line = stack[-1]
        problems.append(f"{path}: unbalanced '{opener}' opened at line {opener_line}")


def main() -> int:
    problems: list[str] = []
    files: list[Path] = []
    for target in TARGETS:
        base = ROOT / target
        if not base.exists():
            continue
        files.extend(sorted(base.rglob("*.cs")))

    if not files:
        print("no C# files found; nothing checked")
        return 1

    engine_free = (
        ROOT / "Packages/com.gamecore.contracts",
        ROOT / "Packages/com.gamecore.content.compiler/Runtime",
        ROOT / "dotnet/src",
    )

    for path in files:
        text = path.read_text(encoding="utf-8")
        rel = path.relative_to(ROOT)
        check_balance(rel, text, problems)

        for name, pattern in FORBIDDEN.items():
            if pattern.search(strip_code(text)) or pattern.search(text):
                problems.append(f"{rel}: contains forbidden construct: {name}")

        stripped = strip_code(text)
        if "TODO" in stripped or "FIXME" in stripped:
            problems.append(f"{rel}: contains TODO/FIXME")

        if not text.startswith("#nullable enable") and "#nullable" not in text:
            problems.append(f"{rel}: missing '#nullable enable'")

        if "UnityEngine" in stripped and "com.gamecore." not in str(rel):
            try:
                rel.relative_to("unity")
            except ValueError:
                problems.append(f"{rel}: UnityEngine reference outside the Unity project")

        for base in engine_free:
            try:
                rel.relative_to(base.relative_to(ROOT))
            except ValueError:
                continue
            if ENGINE_TYPES.search(stripped):
                problems.append(f"{rel}: engine type reference in an engine-free assembly")

    print(f"checked {len(files)} C# file(s)")
    if problems:
        print(f"{len(problems)} problem(s):")
        for problem in problems:
            print("  - " + problem)
        return 1

    print("ok")
    return 0


if __name__ == "__main__":
    sys.exit(main())
