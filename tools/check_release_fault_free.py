#!/usr/bin/env python3
"""Are the GC-017 fault latches really absent from a shipping build?

The acceptance defect this tool closes: the qualification build carried the latches, and a
release-style build still contained latch metadata, empty reach methods, the latch type and a
per-world latch allocation. "Disabled reaches have no effect" is not the requirement; **no latch
code and no latch cost in release** is.

Three independent checks, each of which can fail on its own:

  1. **Compiled assembly, both configurations.** `dotnet/src/GameCore.Faults.ReleaseCheck` compiles
     `Packages/com.gamecore.unity.runtime/Runtime/Faults/**` — the whole latch, which is engine-free
     precisely so this is possible. `-c Release` (symbol undefined) must produce an assembly with no
     latch type and none of the eight boundary name literals; `-c Qualification` (symbol defined)
     must produce an assembly that has them. The second half is what makes the first half mean
     something: a checker that inspected an empty file would pass the first half too.

  2. **Source, both configurations, for the files Unity alone can compile.** `AssemblyPublisher`,
     `UnityExecutionDriver`, `WorldHost` and `StagedResourceGate` cannot be compiled here (they use
     `Unity.Entities`), so their release guarantee is checked at the source level with a real `#if`
     evaluator: with the symbol undefined, no latch type name may survive anywhere in the runtime
     package's compiled text; with it defined, each file that owns a boundary must still reach it.
     Comments are stripped first, so a doc comment may name a latch type; code may not.

  3. **The switch itself.** The qualification symbol must be produced by the asmdef `versionDefines`
     entry on the marker package `com.gamecore.fault-qualification`, and that marker package must be
     referenced by the validation project's manifest only. A shipping project that never references
     the marker cannot define the symbol, so its compilation of the runtime assembly takes the empty
     branch — which is what check 1 demonstrates concretely for the latch subset.

Evidence: `--json <path>` writes the machine-readable result. Exit 0 only when every check passes.

Usage:
    python3 tools/check_release_fault_free.py [--json artifacts/faults/release-surface.json]
                                              [--no-build] [--dotnet PATH]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# The whole latch lives in this folder and is compiled by dotnet in both configurations.
LATCH_SOURCES = "Packages/com.gamecore.unity.runtime/Runtime/Faults"
RUNTIME_SOURCES = "Packages/com.gamecore.unity.runtime/Runtime"
RELEASE_CHECK_PROJECT = "dotnet/src/GameCore.Faults.ReleaseCheck/GameCore.Faults.ReleaseCheck.csproj"

SYMBOL = "GAMECORE_FAULT_INJECTION"
MARKER_PACKAGE = "com.gamecore.fault-qualification"
QUALIFICATION_ASMDEF = "Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef"
ASMDEFS = [
    "Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef",
    "Packages/com.gamecore.unity.runtime/Tests/Assembly/GameCore.Unity.Assembly.Tests.asmdef",
    "Packages/com.gamecore.unity.runtime/Tests/Faults/GameCore.Unity.Faults.Tests.asmdef",
    "Packages/com.gamecore.unity.runtime/Tests/Recovery/GameCore.Unity.Recovery.Tests.asmdef",
    "unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/GameCore.Validation.ProbeHost.asmdef",
    "unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Faults/GameCore.Faults.Tests.asmdef",
]

# Every type the latch declares. A release build must contain none of these names anywhere.
LATCH_TYPES = (
    "FaultBoundaryText",
    "FaultBoundary",
    "AssemblyFaultInjection",
    "FaultTrace",
    "FaultRecord",
    "FaultReach",
    "FaultCompilation",
    "FaultInjectedException",
)

# Latch *members* that vanish with the types. The pattern catches the per-world latch reference without
# matching `PostwriteFaultCount` or an ordinary word such as "defaults".
LATCH_MEMBERS = ("InjectionRefusalCount", "InjectionReleaseRefusalCount")
LATCH_MEMBER_PATTERN = re.compile(r"\bFaults\b")

# The latch namespace. It is declared in both configurations (that is what keeps the assembly's `using`
# directives valid), so it is removed from the text before looking for the per-world latch *member*, which
# is what the guard actually removes.
LATCH_NAMESPACE = "GameCore.Unity.Runtime.Faults"

# Latch *members* that vanish with the types. The pattern catches the per-world latch reference without
# matching the namespace, `PostwriteFaultCount`, or an ordinary word such as "defaults".
LATCH_MEMBERS = ("InjectionRefusalCount", "InjectionReleaseRefusalCount")
LATCH_MEMBER_PATTERN = re.compile(r"\bFaults\b")


def latch_member_leak(text: str) -> bool:
    """True when the compiled text references the latch member rather than only its namespace."""
    return bool(LATCH_MEMBER_PATTERN.search(text.replace(LATCH_NAMESPACE, "")))


# The boundary name table `FaultBoundaryText.Names`, verbatim. Two of them ("migration", "cleanup") are
# ordinary words, so the assembly check uses the distinctive ones; the source check uses all of them
# only inside the file that must be empty in release.
BOUNDARY_NAMES = (
    "validation",
    "acquisition",
    "fence",
    "migration",
    "first-live-write",
    "structural-playback",
    "gate-installation",
    "cleanup",
)
DISTINCTIVE_NAMES = tuple(n for n in BOUNDARY_NAMES if "-" in n)

# Files that own a boundary and must therefore lose every reach call in release. Each entry is
# (path, at-least-one identifier that must be present in the qualification configuration).
BOUNDARY_OWNERS = (
    ("Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs", "FaultReach"),
    ("Packages/com.gamecore.unity.runtime/Runtime/Execution/UnityExecutionDriver.cs", "FaultReach"),
    ("Packages/com.gamecore.unity.runtime/Runtime/Integration/StagedResourceGate.cs", "FaultReach"),
    ("Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs", "AssemblyFaultInjection"),
)


class Failure(Exception):
    """One check failed; the message is the whole diagnostic."""


# --------------------------------------------------------------------------------------------
# A faithful-enough C# preprocessor for the single-symbol pattern this repository uses.
# --------------------------------------------------------------------------------------------

def strip_comments(text: str) -> str:
    """Remove // and /* */ comments, keeping string and char literals intact.

    Comments are stripped so that a doc comment may explain the latch (and name its types) without
    the source check mistaking prose for code. String literals are kept, so a boundary name literal
    that survived outside a guard is still found.
    """
    out = []
    i = 0
    n = len(text)
    while i < n:
        ch = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            i = n if j < 0 else j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if ch == "@" and text.startswith('@"', i):
            out.append(text[i:i + 2])
            i += 2
            while i < n:
                if text.startswith('""', i):
                    out.append('""')
                    i += 2
                    continue
                out.append(text[i])
                if text[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if ch in ('"', "'"):
            quote = ch
            out.append(ch)
            i += 1
            while i < n:
                if text[i] == "\\":
                    out.append(text[i:i + 2])
                    i += 2
                    continue
                out.append(text[i])
                done = text[i] == quote
                i += 1
                if done:
                    break
            continue
        out.append(ch)
        i += 1
    return "".join(out)


def evaluate(text: str, defined: set[str], where: str) -> str:
    """Return the text a compiler would see for `defined`, or raise on an unsupported directive.

    Only `#if SYMBOL`, `#if !SYMBOL`, `#else` and `#endif` appear in the guarded sources, so
    anything else is refused loudly rather than mis-evaluated: a checker that silently mis-strips a
    condition would report a release build it never actually examined.
    """
    kept = []
    stack = []          # (outer_live, this_branch_taken, in_else)
    live = True
    for number, line in enumerate(text.split("\n"), start=1):
        stripped = line.strip()
        if stripped.startswith("#if"):
            expression = stripped[3:].strip()
            if expression.startswith("!"):
                value = expression[1:].strip() not in defined
            elif re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", expression):
                value = expression in defined
            else:
                raise Failure(
                    "%s:%d: unsupported preprocessor expression `%s`; this checker understands one "
                    "symbol at a time and refuses to guess." % (where, number, expression))
            stack.append((live, value, False))
            live = live and value
            continue
        if stripped.startswith("#else"):
            if not stack or stack[-1][2]:
                raise Failure("%s:%d: `#else` without a matching `#if`." % (where, number))
            outer, taken, _ = stack[-1]
            stack[-1] = (outer, taken, True)
            live = outer and not taken
            continue
        if stripped.startswith("#elif"):
            raise Failure(
                "%s:%d: `#elif` is not used by the guarded sources and this checker does not "
                "evaluate it; extend the checker before adding one." % (where, number))
        if stripped.startswith("#endif"):
            if not stack:
                raise Failure("%s:%d: `#endif` without a matching `#if`." % (where, number))
            outer, _, _ = stack.pop()
            live = outer
            continue
        if live:
            kept.append(line)
    if stack:
        raise Failure("%s: %d unterminated `#if` block(s)." % (where, len(stack)))
    return "\n".join(kept)


# --------------------------------------------------------------------------------------------

def read(path: str) -> str:
    with open(os.path.join(REPO_ROOT, path), "r", encoding="utf-8") as handle:
        return handle.read()


def latch_sources() -> list[str]:
    root = os.path.join(REPO_ROOT, LATCH_SOURCES)
    found = []
    for base, _dirs, files in os.walk(root):
        for name in files:
            if name.endswith(".cs"):
                found.append(os.path.relpath(os.path.join(base, name), REPO_ROOT))
    return sorted(found)


def runtime_sources() -> list[str]:
    root = os.path.join(REPO_ROOT, RUNTIME_SOURCES)
    found = []
    for base, _dirs, files in os.walk(root):
        for name in files:
            if name.endswith(".cs"):
                found.append(os.path.relpath(os.path.join(base, name), REPO_ROOT))
    return sorted(found)


def assert_balanced(text: str, where: str) -> None:
    """A guard that splits a construct would leave one configuration unbalanced, which the compiler would
    report as an error somewhere else entirely. Checking it here names the real cause."""
    for opener, closer, label in (("{", "}", "braces"), ("(", ")", "parentheses"), ("[", "]", "brackets")):
        depth = 0
        for ch in text:
            if ch == opener:
                depth += 1
            elif ch == closer:
                depth -= 1
        if depth != 0:
            raise Failure(
                "%s: the compiled text of this configuration is unbalanced by %d %s, so a guard splits a "
                "construct." % (where, depth, label))


def check_source_configurations() -> dict:
    """Check 2: the release text of every runtime source is free of the latch; the qualification text
    still reaches every boundary that owns one."""
    findings = []
    for path in runtime_sources():
        text = strip_comments(read(path))
        release = evaluate(text, set(), path)
        qualify_text = evaluate(text, {SYMBOL}, path)
        assert_balanced(release, "release " + path)
        assert_balanced(qualify_text, "qualification " + path)
        leaked = sorted(name for name in LATCH_TYPES + LATCH_MEMBERS if name in release)
        if latch_member_leak(release):
            leaked.append("Faults")
        if leaked:
            findings.append("release %s still references %s" % (path, ", ".join(leaked)))
        qualify = evaluate(text, {SYMBOL}, path)
        if path.endswith("FaultBoundaries.cs") or path.endswith("AssemblyFaultInjection.cs"):
            if SYMBOL not in text:
                findings.append("%s does not guard itself on %s" % (path, SYMBOL))
        if path.endswith(("FaultBoundaries.cs", "AssemblyFaultInjection.cs")):
            if release.strip().replace("namespace GameCore.Unity.Runtime.Faults", "").replace(
                    "namespace GameCore.Unity.Runtime", "").strip(" {}\n") != "":
                findings.append(
                    "release %s is not empty apart from its namespace declaration; a release compilation of the "
                    "latch must contribute nothing at all." % path)
        if path.endswith("FaultBoundaries.cs"):
            for name in BOUNDARY_NAMES:
                if ('"%s"' % name) not in qualify_text:
                    findings.append("the boundary name table lost the literal %r" % name)
    owned = {}
    for path, required in BOUNDARY_OWNERS:
        text = strip_comments(read(path))
        release = evaluate(text, set(), path)
        qualify = evaluate(text, {SYMBOL}, path)
        leaked = sorted(name for name in LATCH_TYPES + LATCH_MEMBERS if name in release)
        if latch_member_leak(release):
            leaked.append("Faults")
        if leaked:
            findings.append("release %s still references %s" % (path, ", ".join(leaked)))
        if required not in qualify:
            findings.append("qualification %s no longer references %s; the boundary is gone" % (path, required))
        if "#if" in text and SYMBOL not in text:
            findings.append("%s has preprocessor conditions that do not name %s" % (path, SYMBOL))
        owned[path] = {
            "releaseLatchReferences": 0,
            "qualificationLatchReferences": sum(qualify.count(n) for n in LATCH_TYPES),
        }
    if findings:
        raise Failure("source configuration check failed:\n  - " + "\n  - ".join(findings))
    return {"latchSources": len(latch_sources()), "runtimeSources": len(runtime_sources()), "boundaryOwners": owned}


def check_conditional_sites() -> dict:
    """The apply path's latch call must disappear *at compile time*, not merely do nothing.

    `NoteFirstLiveWrite` is the one latch call the apply loop makes, once per effective write. It is
    masked with `[Conditional(SYMBOL)]`, so a compilation without the symbol drops every call site —
    argument evaluation included — and leaves an unreferenced empty stub the linker discards. Without
    that attribute the release build would keep a call to an empty method per write, which is exactly
    the residual cost the first audit found. A future refactor that drops the attribute must fail
    here rather than quietly reintroduce the cost.
    """
    path = "Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs"
    text = strip_comments(read(path))
    release = evaluate(text, set(), path)
    marker = '[System.Diagnostics.Conditional("%s")]' % SYMBOL
    if "NoteFirstLiveWrite(" not in release:
        raise Failure(
            "%s no longer calls NoteFirstLiveWrite from the unconditioned apply path. If the latch call "
            "was moved inside a guard that is correct but this check can no longer see it; update the "
            "check together with the move." % path)
    if marker not in text:
        raise Failure(
            "the apply path's latch call is not masked by %s, so a release build would keep one call per "
            "effective write to an empty method." % marker)
    if "private void NoteFirstLiveWrite(" not in text:
        raise Failure("NoteFirstLiveWrite must be a void method: the Conditional attribute is only "
                      "honoured on a void method (and is a compile error otherwise).")
    guarded_body = "Faults.MaybeFailAfterFirstLiveWrite();" in evaluate(text, {SYMBOL}, path)
    release_body = "MaybeFailAfterFirstLiveWrite" in release
    if not guarded_body or release_body:
        raise Failure(
            "the latch call itself must exist only in the qualification configuration "
            "(guarded=%s, leakedIntoRelease=%s)." % (guarded_body, release_body))
    return {"path": path, "callSites": release.count("NoteFirstLiveWrite("), "mask": marker}


def check_switch() -> dict:
    """Check 3: the symbol comes from the qualification marker package, and only the validation
    project references it."""
    asmdef = json.loads(read(QUALIFICATION_ASMDEF))
    entries = [e for e in asmdef.get("versionDefines", []) if e.get("define") == SYMBOL]
    if len(entries) != 1:
        raise Failure(
            "%s must declare exactly one versionDefines entry defining %s; found %d."
            % (QUALIFICATION_ASMDEF, SYMBOL, len(entries)))
    if entries[0]["name"] != MARKER_PACKAGE:
        raise Failure(
            "the qualification symbol is driven by %r, not by the marker package %r. A symbol tied to "
            "a package that ships transitively (the test framework does) cannot be absent from a "
            "shipping build." % (entries[0]["name"], MARKER_PACKAGE))
    manifest = json.loads(read("unity/GameCore.Validation/Packages/manifest.json"))
    dependency = manifest["dependencies"].get(MARKER_PACKAGE)
    if dependency is None:
        raise Failure("the validation project's manifest does not reference %s." % MARKER_PACKAGE)
    others = []
    for base, dirs, files in os.walk(REPO_ROOT):
        if "/.git" in base:
            continue
        dirs[:] = [d for d in dirs if d not in (".git", "Library", "obj", "bin", "artifacts", "Temp")]
        for name in files:
            if name != "manifest.json":
                continue
            path = os.path.relpath(os.path.join(base, name), REPO_ROOT)
            if path == "unity/GameCore.Validation/Packages/manifest.json":
                continue
            if MARKER_PACKAGE in open(os.path.join(base, name), encoding="utf-8").read():
                others.append(path)
    return {
        "asmdef": QUALIFICATION_ASMDEF,
        "versionDefine": entries[0],
        "validationDependency": dependency,
        "otherManifestsReferencingMarker": others,
    }


def check_asmdefs() -> dict:
    """No assembly that a shipping project would compile may name a latch type.

    Two kinds of assembly do name them, and each is excluded from a shipping build by a different
    mechanism:

      * an assembly inside a shipped package (`Packages/**`) must be a test assembly, i.e. carry the
        `UNITY_INCLUDE_TESTS` define constraint, so a player or a shipping consumer never compiles it;
      * an assembly inside the validation project itself (`unity/GameCore.Validation/**`) is a
        qualification artefact by construction: the project is the one that references the marker
        package that defines the symbol (checked in `check_switch`), and it is not a shipping project.

    Anything else naming a latch type would be reachable from a shipping compilation, which is the
    defect this check exists for.
    """
    result = {}
    for path in ASMDEFS:
        if path == QUALIFICATION_ASMDEF:
            continue
        data = json.loads(read(path))
        constraints = data.get("defineConstraints", [])
        if path.startswith("Packages/"):
            if "UNITY_INCLUDE_TESTS" not in constraints:
                raise Failure(
                    "%s names latch types but is not constrained to test builds "
                    "(defineConstraints=%r): a shipping player would compile it." % (path, constraints))
            result[path] = {"kind": "package-test", "defineConstraints": constraints}
        else:
            result[path] = {"kind": "validation-project", "defineConstraints": constraints}
    return result


def build(project: str, configuration: str, dotnet: str) -> str:
    command = [dotnet, "build", project, "-c", configuration, "--nologo", "-v", "quiet"]
    completed = subprocess.run(command, cwd=REPO_ROOT, capture_output=True, text=True)
    if completed.returncode != 0:
        raise Failure(
            "`%s` failed for configuration %s:\n%s\n%s"
            % (" ".join(command), configuration, completed.stdout.strip(), completed.stderr.strip()))
    if configuration == "Release":
        output = os.path.join(os.path.dirname(project), "bin", "Release", "netstandard2.1")
    else:
        output = os.path.join(os.path.dirname(project), "bin", configuration, "netstandard2.1")
    dll = os.path.join(output, "GameCore.Faults.ReleaseCheck.dll")
    if not os.path.isfile(os.path.join(REPO_ROOT, dll)):
        raise Failure("the %s build produced no assembly at %s." % (configuration, dll))
    return dll


def contains(haystack: bytes, needle: str) -> bool:
    return needle.encode("utf-8") in haystack


def check_assemblies(dotnet: str) -> dict:
    """Check 1: the compiled release assembly has no latch at all; the qualification assembly has the
    whole latch. Both halves are asserted, so neither can pass by accident."""
    release_dll = build(RELEASE_CHECK_PROJECT, "Release", dotnet)
    qualify_dll = build(RELEASE_CHECK_PROJECT, "Qualification", dotnet)
    with open(os.path.join(REPO_ROOT, release_dll), "rb") as handle:
        release = handle.read()
    with open(os.path.join(REPO_ROOT, qualify_dll), "rb") as handle:
        qualify = handle.read()

    problems = []
    if contains(release, "Fault"):
        problems.append("the release assembly contains the text `Fault`: a latch type or member survived.")
    for name in LATCH_TYPES:
        if contains(release, name):
            problems.append("the release assembly contains the latch type name %s." % name)
    for name in DISTINCTIVE_NAMES:
        if contains(release, name):
            problems.append("the release assembly contains the boundary name literal %r." % name)
    if contains(release, "boundary="):
        problems.append("the release assembly contains the trace line prefix `boundary=`.")
    if contains(release, SYMBOL):
        problems.append("the release assembly contains the qualification symbol string `%s`." % SYMBOL)

    for name in LATCH_TYPES:
        if not contains(qualify, name):
            problems.append("the qualification assembly is missing the latch type name %s, so the "
                            "release check above could pass by inspecting nothing." % name)
    for name in DISTINCTIVE_NAMES:
        if not contains(qualify, name):
            problems.append("the qualification assembly is missing the boundary name literal %r." % name)

    if problems:
        raise Failure("compiled-assembly check failed:\n  - " + "\n  - ".join(problems))
    return {
        "releaseAssembly": release_dll,
        "releaseAssemblyBytes": len(release),
        "qualificationAssembly": qualify_dll,
        "qualificationAssemblyBytes": len(qualify),
        "releaseTypeCount": 0,
        "latchTypesChecked": list(LATCH_TYPES),
        "boundaryLiteralsChecked": list(DISTINCTIVE_NAMES),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--json", default=None, help="write the result to this path")
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET", "dotnet"))
    parser.add_argument("--no-build", action="store_true",
                        help="skip the compiled-assembly check (source and switch checks only)")
    arguments = parser.parse_args()

    report = {"task": "GC-017", "check": "release-surface", "status": "Fail", "failures": []}
    failure_code = 0
    try:
        report["switch"] = check_switch()
        report["asmdefs"] = check_asmdefs()
        report["source"] = check_source_configurations()
        report["conditionalSites"] = check_conditional_sites()
        if arguments.no_build:
            report["assemblies"] = {"skipped": "--no-build"}
        else:
            if shutil.which(arguments.dotnet) is None:
                raise Failure(
                    "no `%s` on PATH. The compiled-assembly half of this check needs the .NET SDK; "
                    "run it on the build host, or pass --no-build for the source half only."
                    % arguments.dotnet)
            report["assemblies"] = check_assemblies(arguments.dotnet)
        report["status"] = "Pass"
    except Failure as failure:
        report["failures"].append(str(failure))
        failure_code = 1
    except Exception as unexpected:  # noqa: BLE001 - a checker must report, not traceback
        report["failures"].append("%s: %s" % (type(unexpected).__name__, unexpected))
        failure_code = 1

    if arguments.json:
        with open(arguments.json, "w", encoding="utf-8") as handle:
            json.dump(report, handle, indent=2, sort_keys=True)
            handle.write("\n")

    if report["status"] == "Pass":
        print("GC-017 release surface: PASS")
        if "skipped" in report["assemblies"]:
            print("  [not run] the compiled latch assembly in both configurations (--no-build)")
        else:
            print("  the release compilation of the latch sources defines no type and carries no boundary "
                  "literal (%d bytes); the qualification one carries them all (%d bytes)"
                  % (report["assemblies"]["releaseAssemblyBytes"],
                     report["assemblies"]["qualificationAssemblyBytes"]))
        print("  no runtime source keeps a latch reference once the qualification symbol is undefined")
        print("  the apply path's mask by %s is intact (%d call site(s))"
              % (report["conditionalSites"]["mask"], report["conditionalSites"]["callSites"]))
        print("  the symbol is driven by %s, referenced only by the validation manifest" % MARKER_PACKAGE)
        return 0

    print("GC-017 release surface: FAIL", file=sys.stderr)
    for failure in report["failures"]:
        print("  " + failure, file=sys.stderr)
    return failure_code


if __name__ == "__main__":
    sys.exit(main())
