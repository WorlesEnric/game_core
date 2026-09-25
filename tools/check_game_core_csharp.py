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
    # GC-006: the derivation package and its fixtures are engine-free too, so the same host-side checks apply.
    "Packages/com.gamecore.derivation",
    # GC-010: the narrative rules package is engine-free like the derivation package (no UnityEngine anywhere in
    # its Runtime), so it belongs in TARGETS and in `engine_free` below.
    "Packages/com.gamecore.rules.narrative",
    # GC-010: the narrative gameplay package declares real Unity components, so it is checked for balance and
    # forbidden constructs but is deliberately NOT in `engine_free` below (it references Unity.Entities).
    "Packages/com.gamecore.gameplay.narrative",
    # W2 gate: the planning package and the Unity runtime package hold the Wave 2 modules and the integration glue,
    # so the balance and forbidden-construct checks cover them as well. They are not in `engine_free`: both
    # legitimately reference Unity types.
    "Packages/com.gamecore.planning",
    "Packages/com.gamecore.unity.runtime",
    # GC-011: the card rules package is engine-free (it holds no Unity type at all), so it joins the engine-free
    # set; the card gameplay package holds the settlement systems and the integration glue, so it is only covered
    # by the balance and forbidden-construct checks.
    # GC-014: the composition package holds the Unity-free lifecycle ledgers, the teardown sequencer and the
    # quarantine registry, so the same host-side balance/forbidden-construct checks cover them (it is engine-free
    # and therefore also belongs in the `engine_free` tuple below).
    "Packages/com.gamecore.composition",
    "Packages/com.gamecore.rules.cards",
    "Packages/com.gamecore.gameplay.cards",
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
            # Keep the newline: line-oriented checks (missing returns, member scans) depend on line structure.
            out.append("\n")
            continue
        if c == "/" and i + 1 < n and text[i + 1] == "*":
            i += 2
            while i + 1 < n and not (text[i] == "*" and text[i + 1] == "/"):
                if text[i] == "\n":
                    out.append("\n")
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


MODIFIERS = {
    "public", "private", "protected", "internal", "static", "virtual", "override", "sealed", "abstract",
    "async", "new", "extern", "unsafe", "partial", "readonly", "const", "event", "delegate",
}

# Return types that never need a `return` statement.
VOID_LIKE = {"void"}

# Tokens that appear in a declaration-like line but never name a method.
NOT_A_METHOD = {
    "if", "while", "for", "foreach", "switch", "catch", "using", "lock", "fixed", "return", "this", "base",
    "do", "else", "try", "get", "set", "add", "remove", "when",
}

IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
RETURN_TYPE = re.compile(r"^[A-Za-z_][A-Za-z0-9_\.]*(?:\?)?(?:<[^ ]*>)?(?:\[\])*$")

METHOD_TAIL = re.compile(r"\(([^()]*)\)\s*(?:where[^{]*)?$")


def check_missing_return(path: Path, code: str, problems: list[str]) -> None:
    """Flags a non-void method whose block body contains no `return` (C# error CS0161).

    Conservative: it only inspects declarations of the form `<modifiers> <type> <Name>(...)` followed by a line
    containing only `{`, so constructors, expression-bodied members, properties and lambdas are not matched.
    """
    lines = code.split("\n")
    for index, line in enumerate(lines):
        stripped = line.strip()
        if not stripped.endswith(")") or not METHOD_TAIL.search(stripped):
            continue

        head = stripped[: stripped.rindex("(")].strip()
        tokens = head.replace("(", " ").split()
        if len(tokens) < 2:
            continue

        name = tokens[-1]
        return_type = tokens[-2]
        if not IDENTIFIER.match(name) or name in NOT_A_METHOD or name in MODIFIERS:
            continue
        if not RETURN_TYPE.match(return_type) or return_type in MODIFIERS or return_type in VOID_LIKE:
            continue

        # The body must open on the next non-blank line, otherwise this is not a block-bodied method.
        body_start = None
        for probe in range(index + 1, min(index + 4, len(lines))):
            if lines[probe].strip() == "":
                continue
            if lines[probe].strip() == "{":
                body_start = probe
            break

        if body_start is None:
            continue

        depth = 0
        body: list[str] = []
        for probe in range(body_start, len(lines)):
            depth += lines[probe].count("{") - lines[probe].count("}")
            body.append(lines[probe])
            if depth <= 0 and probe > body_start:
                break

        joined = "\n".join(body)
        if not re.search(r"\breturn\b", joined) and "throw " not in joined:
            problems.append(
                f"{path}:{index + 1}: non-void method '{name}' has no return statement (CS0161)"
            )


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
        files.extend(sorted(
            path for path in base.rglob("*.cs")
            if not (
                path.is_relative_to(ROOT / "dotnet")
                and {"bin", "obj"}.intersection(path.relative_to(base).parts)
            )
        ))

    if not files:
        print("no C# files found; nothing checked")
        return 1

    engine_free = (
        ROOT / "Packages/com.gamecore.contracts",
        ROOT / "Packages/com.gamecore.content.compiler/Runtime",
        ROOT / "Packages/com.gamecore.derivation",
        # GC-014: the composition package (lifecycle ledgers, teardown sequencer, quarantine registry, service
        # resolution) references no Unity type, so it is engine-free exactly like the contracts package.
        ROOT / "Packages/com.gamecore.composition/Runtime",
        ROOT / "Packages/com.gamecore.rules.narrative",
        ROOT / "Packages/com.gamecore.rules.cards",
        # GC-016: the observation storage of the Unity runtime package (bounded retention, snapshot leases,
        # resynchronization, delayed-consumer delivery, the committed-boundary lease) is engine-free on purpose: it
        # is compiled by dotnet/src/GameCore.Execution and must stay free of every Unity type.
        ROOT / "Packages/com.gamecore.unity.runtime/Runtime/Observation",
        ROOT / "dotnet/src",
    )

    for path in files:
        text = path.read_text(encoding="utf-8")
        rel = path.relative_to(ROOT)
        check_balance(rel, text, problems)

        check_missing_return(rel, strip_code(text), problems)

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
