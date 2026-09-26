#!/usr/bin/env python3
"""Check that link.xml preserves no kernel or gameplay assembly (GC-025, 04 section 8 item 4).

04 section 8 allows preservation metadata for "entry points reached by Unity callbacks or native code" and, since
GC-012, requires generated registration to be the reachability root for kernel and gameplay assemblies: "Removing the
GC-001-era `preserve="all"` entries for the narrative gameplay and rules assemblies was the point of GC-012:
reachability must come from generated roots, and a stripped type would be fixed by correcting generation rather than
by widening this file."

That rule is only a comment until something checks it, which is what this tool does. It reads a Unity project's
`Assets/link.xml` and asserts the preservation set is exactly the permitted one:

  * assembly-level `preserve="all"` only for the two fixture assemblies whose whole purpose is to be linked but not
    referenced (`GameCore.Validation.Fixture`, `GameCore.Unity.Fixtures`);
  * type-level `preserve="all"` only for named entry points: the six PlayerLoop types native code invokes through an
    `updateDelegate`/`RuntimeInitializeOnLoadMethod`, and the ONE host-invoked production entry added by GC-027 —
    `GameCore.Unity.Runtime.Recovery.WorldRecovery`. 04 section 8 item 4 permits preservation for "entry points
    reached by Unity callbacks or native code"; O-22's composition is reached by the HOST application, and in the
    marker-free release clone no managed call site reaches it at all, so High managed stripping really dropped it
    (GC-027's build report records the before/after). The exception is one assembly and one exact type list, so a
    blanket preserve of a kernel assembly still fails: an element whose only content is permitted type-level
    preserves is a container, while `preserve="all"` on the assembly itself remains forbidden everywhere below;
  * no other `preserve` of any kind for a kernel, rules, gameplay, compiler or runtime assembly — the assemblies
    generated registration is supposed to keep alive.

A bare `<assembly fullname="X" />` root without `preserve` is legal but is still reported, because a root for a
kernel or gameplay assembly is a symptom worth seeing even though it does not itself preserve members.

usage:
  python3 tools/check_link_xml.py [--project unity/GameCore.Validation] [--json <path>]

exit codes: 0 the file is exactly the permitted preservation set; 1 a problem; 2 no link.xml found.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ElementTree

ROOT = pathlib.Path(__file__).resolve().parents[1]

# Assemblies permitted to carry assembly-level preserve="all": the two linked-but-unreferenced fixture assemblies.
PERMITTED_ASSEMBLY_PRESERVES = (
    "GameCore.Validation.Fixture",
    "GameCore.Unity.Fixtures",
)

# The assembly that carries the PlayerLoop entry points, and the exact type set it may preserve.
PLAYER_LOOP_ASSEMBLY = "GameCore.Unity.Adapters"
PLAYER_LOOP_TYPE_PRESERVES = (
    "GameCore.Unity.Adapters.GameCoreApplicationBootstrap",
    "GameCore.Unity.Adapters.GameCorePlayerLoopInstaller",
    "GameCore.Unity.Adapters.GameCoreApplicationPump",
    "GameCore.Unity.Adapters.GameCoreApplicationReset",
    "GameCore.Unity.Adapters.GameCoreApplicationComposition",
    "GameCore.Unity.Adapters.GameCorePumpLoop",
)

# The ONE kernel-assembly type-level preserve this repository permits, added by GC-027. 04 section 8 item 4 allows
# preservation for "entry points reached by Unity callbacks or native code"; O-22's composition is invoked by the HOST
# application, and in the marker-free release clone no managed call site reaches `WorldRecovery` at all, so High
# managed stripping dropped the production recovery API (GC-027's build report records the before/after). The
# exception is one assembly and one exact type list: `preserve="all"` on the assembly itself stays a failure below, so
# this is not a way to widen a kernel assembly's reachability.
HOST_ENTRY_ASSEMBLY = "GameCore.Unity.Runtime"
HOST_ENTRY_TYPE_PRESERVES = (
    "GameCore.Unity.Runtime.Recovery.WorldRecovery",
)

# The two assemblies permitted to carry type-level preserves, mapped to the exact types each may preserve.
PERMITTED_TYPE_PRESERVES = {
    PLAYER_LOOP_ASSEMBLY: PLAYER_LOOP_TYPE_PRESERVES,
    HOST_ENTRY_ASSEMBLY: HOST_ENTRY_TYPE_PRESERVES,
}

# Every type name any assembly may preserve, for reporting.
ALL_PERMITTED_TYPE_PRESERVES = PLAYER_LOOP_TYPE_PRESERVES + HOST_ENTRY_TYPE_PRESERVES

# Assemblies that must never be preserved: the kernel, the rules and gameplay families, the compiler and the runtime.
# A prefix match, so `GameCore.Rules.Narrative` and `GameCore.Gameplay.Cards.Fixtures` are covered too.
FORBIDDEN_PREFIXES = (
    "GameCore.Contracts",
    "GameCore.Composition",
    "GameCore.Derivation",
    "GameCore.Planning",
    "GameCore.Rules.",
    "GameCore.Gameplay.",
    "GameCore.Unity.Runtime",
    "GameCore.Content.Compiler",
)


def is_forbidden(assembly):
    return any(assembly == prefix or assembly.startswith(prefix) for prefix in FORBIDDEN_PREFIXES)


def check(path):
    """Returns (problems, facts) for one link.xml."""
    problems = []
    if not path.is_file():
        return ["no link.xml at " + str(path)], {"path": str(path), "assemblies": [], "sha256": None}

    text = path.read_text(encoding="utf-8")
    try:
        root = ElementTree.fromstring(text)
    except ElementTree.ParseError as error:
        return ["link.xml is not valid XML: " + str(error)], {"path": str(path), "assemblies": [], "sha256": None}

    facts = {
        "path": str(path),
        "sha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
        "assemblies": [],
    }

    for assembly in root.findall("assembly"):
        name = assembly.get("fullname") or assembly.get("name")
        if not name:
            problems.append("an <assembly> element has no fullname")
            continue

        preserve = assembly.get("preserve")
        types = [element.get("fullname") or element.get("name") for element in assembly.findall("type")]
        facts["assemblies"].append({"fullname": name, "preserve": preserve, "types": [t for t in types if t]})

        if is_forbidden(name) and preserve is not None:
            problems.append(
                name + " is a kernel/rules/gameplay assembly and must not carry an assembly-level preserve; "
                "its reachability must come from the generated catalogs (04 section 8)")

        if preserve is not None:
            if preserve != "all":
                problems.append(name + ' carries preserve="' + preserve + '"; only preserve="all" is used here')
            if name not in PERMITTED_ASSEMBLY_PRESERVES:
                problems.append(
                    name + ' carries an assembly-level preserve="' + preserve + '", which is not one of the '
                    "permitted assembly-level preserves " + ", ".join(PERMITTED_ASSEMBLY_PRESERVES))
        elif name not in PERMITTED_ASSEMBLY_PRESERVES and name not in PERMITTED_TYPE_PRESERVES:
            problems.append(
                name + " is rooted without a preserve attribute; a root is only expected for the fixture "
                "assemblies and for the two assemblies that carry permitted type-level preserves ("
                + ", ".join(sorted(PERMITTED_TYPE_PRESERVES)) + ")")

        permitted_types = PERMITTED_TYPE_PRESERVES.get(name)
        for element in assembly.findall("type"):
            type_name = element.get("fullname") or element.get("name")
            if not type_name:
                problems.append("a <type> element of " + name + " has no fullname")
                continue

            type_preserve = element.get("preserve")
            if type_preserve != "all":
                problems.append(type_name + ' carries preserve="' + str(type_preserve) + '"; only preserve="all" is used here')
            if permitted_types is None:
                problems.append(
                    "type " + type_name + " is preserved under " + name + ", which is not one of the assemblies "
                    "permitted to carry type-level preserves (" + ", ".join(sorted(PERMITTED_TYPE_PRESERVES)) + ")")
            elif type_name not in permitted_types:
                problems.append(
                    type_name + " is preserved but is not one of the " + name + " entry points this file permits: "
                    + ", ".join(permitted_types))

        # A kernel assembly that is forbidden for assembly-level preserve must still not be a bare root: an element
        # whose only content is permitted type-level preserves is a container, and anything else is reported.
        if is_forbidden(name) and preserve is None and not assembly.findall("type"):
            problems.append(
                name + " is a kernel/rules/gameplay assembly rooted with no preserve and no permitted type entry; "
                "its reachability must come from the generated catalogs (04 section 8)")

    # The permitted set must also be present: a file that dropped the PlayerLoop preserves would strip the application
    # pump in a player, and a file that dropped GC-027's host-entry preserve would strip the production recovery API
    # out of a marker-free release clone. Both are the failures this allow-list exists to prevent.
    present = {entry["fullname"] for entry in facts["assemblies"]}
    preserved = {entry["fullname"]: entry["preserve"] for entry in facts["assemblies"]}
    for permitted in PERMITTED_ASSEMBLY_PRESERVES:
        if permitted not in present:
            problems.append("the permitted assembly-level preserve for " + permitted + " is missing")
        elif preserved.get(permitted) != "all":
            problems.append(
                "the permitted assembly-level preserve for " + permitted
                + ' is present but does not carry preserve="all" (it carries ' + str(preserved.get(permitted)) + ")")
    for assembly_name in sorted(PERMITTED_TYPE_PRESERVES):
        required = PERMITTED_TYPE_PRESERVES[assembly_name]
        if assembly_name not in present:
            problems.append("the permitted " + assembly_name + " type-level preserves are missing")
            continue
        for entry in facts["assemblies"]:
            if entry["fullname"] == assembly_name:
                missing = [t for t in required if t not in entry["types"]]
                if missing:
                    problems.append(assembly_name + " does not preserve " + ", ".join(missing))

    return problems, facts


def main():
    parser = argparse.ArgumentParser(description="Check a Unity project's link.xml preservation set.")
    parser.add_argument("--project", default="unity/GameCore.Validation",
                        help="Unity project directory containing Assets/link.xml")
    parser.add_argument("--json", default=None, help="destination of the verdict document")
    args = parser.parse_args()

    project = pathlib.Path(args.project)
    if not project.is_absolute():
        project = ROOT / project
    path = project / "Assets/link.xml"

    problems, facts = check(path)

    for entry in facts.get("assemblies", []):
        print("%s: preserve=%s types=%d" % (entry["fullname"], entry["preserve"], len(entry["types"])))

    if problems:
        print("%d problem(s) in %s:" % (len(problems), path))
        for problem in problems:
            print("  - " + problem)
    else:
        print("%s preserves only the permitted fixture and PlayerLoop roots (sha256 %s)"
              % (path, facts["sha256"]))

    if args.json:
        destination = pathlib.Path(args.json)
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_text(
            json.dumps(
                {
                    "check": "link-xml-preservation",
                    "permittedAssemblyPreserves": list(PERMITTED_ASSEMBLY_PRESERVES),
                    "permittedTypePreserves": dict(PERMITTED_TYPE_PRESERVES),
                    "permittedTypePreserveCount": len(ALL_PERMITTED_TYPE_PRESERVES),
                    "sha256": facts.get("sha256"),
                    "assemblies": facts.get("assemblies", []),
                    "problems": problems,
                },
                indent=2,
                sort_keys=True,
            ) + "\n",
            encoding="utf-8")

    if facts.get("sha256") is None:
        return 2
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
