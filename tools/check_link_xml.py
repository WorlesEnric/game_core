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
  * type-level `preserve="all"` only under `GameCore.Unity.Adapters`, and only for the six PlayerLoop entry points
    that native code invokes through an `updateDelegate`/`RuntimeInitializeOnLoadMethod`;
  * no `preserve` of any kind for a kernel, rules, gameplay, compiler or runtime assembly — the assemblies generated
    registration is supposed to keep alive.

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

# The one assembly permitted to carry type-level preserves, with the exact type set it may preserve.
PLAYER_LOOP_ASSEMBLY = "GameCore.Unity.Adapters"
PERMITTED_TYPE_PRESERVES = (
    "GameCore.Unity.Adapters.GameCoreApplicationBootstrap",
    "GameCore.Unity.Adapters.GameCorePlayerLoopInstaller",
    "GameCore.Unity.Adapters.GameCoreApplicationPump",
    "GameCore.Unity.Adapters.GameCoreApplicationReset",
    "GameCore.Unity.Adapters.GameCoreApplicationComposition",
    "GameCore.Unity.Adapters.GameCorePumpLoop",
)

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

        if is_forbidden(name):
            problems.append(
                name + " is a kernel/rules/gameplay assembly and must not be preserved or rooted by link.xml; "
                "its reachability must come from the generated catalogs (04 section 8)")

        if preserve is not None:
            if preserve != "all":
                problems.append(name + ' carries preserve="' + preserve + '"; only preserve="all" is used here')
            if name not in PERMITTED_ASSEMBLY_PRESERVES:
                problems.append(
                    name + ' carries an assembly-level preserve="' + preserve + '", which is not one of the '
                    "permitted assembly-level preserves " + ", ".join(PERMITTED_ASSEMBLY_PRESERVES))
        elif name not in PERMITTED_ASSEMBLY_PRESERVES and name != PLAYER_LOOP_ASSEMBLY:
            problems.append(
                name + " is rooted without a preserve attribute; a root is only expected for the fixture and "
                "PlayerLoop assemblies")

        for element in assembly.findall("type"):
            type_name = element.get("fullname") or element.get("name")
            if not type_name:
                problems.append("a <type> element of " + name + " has no fullname")
                continue

            type_preserve = element.get("preserve")
            if type_preserve != "all":
                problems.append(type_name + ' carries preserve="' + str(type_preserve) + '"; only preserve="all" is used here')
            if name != PLAYER_LOOP_ASSEMBLY:
                problems.append(
                    "type " + type_name + " is preserved under " + name + ", which is not the one assembly "
                    "permitted to carry type-level preserves (" + PLAYER_LOOP_ASSEMBLY + ")")
            elif type_name not in PERMITTED_TYPE_PRESERVES:
                problems.append(
                    type_name + " is preserved but is not one of the PlayerLoop entry points native code invokes: "
                    + ", ".join(PERMITTED_TYPE_PRESERVES))

    # The permitted set must also be present: a file that dropped the PlayerLoop preserves would strip the application
    # pump in a player, which is the failure this allow-list exists to prevent.
    present = {entry["fullname"] for entry in facts["assemblies"]}
    for permitted in PERMITTED_ASSEMBLY_PRESERVES:
        if permitted not in present:
            problems.append("the permitted assembly-level preserve for " + permitted + " is missing")
    if PLAYER_LOOP_ASSEMBLY not in present:
        problems.append("the permitted " + PLAYER_LOOP_ASSEMBLY + " roots are missing")
    else:
        for entry in facts["assemblies"]:
            if entry["fullname"] == PLAYER_LOOP_ASSEMBLY:
                missing = [t for t in PERMITTED_TYPE_PRESERVES if t not in entry["types"]]
                if missing:
                    problems.append(
                        PLAYER_LOOP_ASSEMBLY + " does not preserve " + ", ".join(missing))

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
                    "task": "GC-025",
                    "status": "Fail" if problems else "Pass",
                    "path": str(path),
                    "sha256": facts.get("sha256"),
                    "assemblies": facts.get("assemblies", []),
                    "permittedAssemblyPreserves": list(PERMITTED_ASSEMBLY_PRESERVES),
                    "permittedTypePreserves": list(PERMITTED_TYPE_PRESERVES),
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
