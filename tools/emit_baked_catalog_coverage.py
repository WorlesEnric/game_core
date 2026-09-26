#!/usr/bin/env python3
"""Bake the catalog-coverage authoring document into its committed artifact (GC-025; no-SDK host helper).

`GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake` is the producing step: it runs in the pinned Editor,
validates the authoring document against the traversal package's declared vocabulary and writes
`Assets/GameCore.Validation/Generated/CatalogCoverageBaked.g.cs`. This host has no Unity, so this script is the
faithful mirror of that baker's template, used to produce and re-check the committed artifact here; the build host's
bake step regenerates the same bytes and the gate diffs them (exactly the arrangement `tools/emit_generated_catalog.py`
uses for the catalogs).

What the artifact is, and why it is a C# file rather than a data file: 04 section 6 makes baking an Editor workflow
and requires the player to instantiate baked entities or invoke precompiled factories rather than invoking bakers at
runtime. A generated source read at compile time is that shape without a runtime asset-load path: the player names
the baked definition directly, and a missing bake is a compile error rather than a runtime null.

usage:
  python3 tools/emit_baked_catalog_coverage.py            # writes the artifact
  python3 tools/emit_baked_catalog_coverage.py --check    # fails when the artifact is stale
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]

AUTHORING_PATH = "unity/GameCore.Validation/Catalogs/CatalogCoverageAuthoring.json"
ARTIFACT_PATH = "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/CatalogCoverageBaked.g.cs"

# The path the artifact itself records: project-relative, because the baker resolves it from the Unity project
# root. Kept separate from `AUTHORING_PATH` so the recorded string cannot drift from the path the bake reads.
AUTHORING_RECORDED_PATH = "Catalogs/CatalogCoverageAuthoring.json"

DESCRIPTION_FORMAT = "gamecore.catalog-coverage-authoring/1"
ARTIFACT_FORMAT = "gamecore.catalog-coverage-baked/1"
BAKED_BY = "GameCore.Validation.Editor.BakeCatalogCoverageAuthoring.Bake"

# The roles this artifact bakes, in the order the authored document must declare them. One role: the traversal
# runner, the one target whose base layout a checkpoint-volume recipe cannot stand in for.
ROLES = ("runner",)

KNOWN_TOP_LEVEL = ("descriptionFormat", "protocolVersion", "bakedBy", "targets")
KNOWN_TARGET = (
    "role",
    "recipeStableName",
    "recipeSchemaStableName",
    "recipeSchemaVersion",
    "recipeRevision",
    "applierStableName",
    "applierKeyVersion",
    "baseLayoutSchemaStableNames",
    "descriptorTagStableNames",
    "initialPositionMilli",
    "initialVelocityMilli",
)
KNOWN_VECTOR = ("x", "y", "z")


class AuthoringMismatch(Exception):
    """The authoring document cannot produce the artifact this baker is contracted to emit."""


def stable_name(text, where):
    if not isinstance(text, str) or not text or not all(
        part and all(c.islower() or c.isdigit() or c in "_-" for c in part) for part in text.split(".")
    ):
        raise AuthoringMismatch(where + ": '" + str(text) + "' is not a dotted lower-case stable name")
    return text


def vector(document, where):
    if not isinstance(document, dict):
        raise AuthoringMismatch(where + " must be an object")
    for key in document:
        if key not in KNOWN_VECTOR:
            raise AuthoringMismatch(where + ": unknown member '" + key + "'")
    values = []
    for axis in KNOWN_VECTOR:
        value = document.get(axis)
        if not isinstance(value, int) or isinstance(value, bool):
            raise AuthoringMismatch(where + "." + axis + " must be an integer")
        values.append(value)
    return tuple(values)


def validate(document):
    if not isinstance(document, dict):
        raise AuthoringMismatch("the authoring document must be a JSON object")
    for key in document:
        if key not in KNOWN_TOP_LEVEL:
            raise AuthoringMismatch("unknown member '" + key + "'")
    if document.get("descriptionFormat") != DESCRIPTION_FORMAT:
        raise AuthoringMismatch("descriptionFormat must be '" + DESCRIPTION_FORMAT + "'")
    if document.get("protocolVersion") != "1.0":
        raise AuthoringMismatch("protocolVersion must be 1.0")
    if not isinstance(document.get("bakedBy"), str) or not document["bakedBy"]:
        raise AuthoringMismatch("bakedBy must name the Editor entry point that bakes this document")

    targets = document.get("targets")
    if not isinstance(targets, list) or not targets:
        raise AuthoringMismatch("targets must be a non-empty array")

    by_role = {}
    for i, target in enumerate(targets):
        where = "targets[" + str(i) + "]"
        if not isinstance(target, dict):
            raise AuthoringMismatch(where + " must be an object")
        for key in target:
            if key not in KNOWN_TARGET:
                raise AuthoringMismatch(where + ": unknown member '" + key + "'")
        role = target.get("role")
        if role not in ROLES:
            raise AuthoringMismatch(where + ".role must be one of " + ", ".join(ROLES))
        if role in by_role:
            raise AuthoringMismatch(where + ".role: '" + role + "' is declared twice")
        layout = target.get("baseLayoutSchemaStableNames")
        if not isinstance(layout, list) or not layout:
            raise AuthoringMismatch(where + ".baseLayoutSchemaStableNames must be a non-empty array")
        tags = target.get("descriptorTagStableNames")
        if not isinstance(tags, list):
            raise AuthoringMismatch(where + ".descriptorTagStableNames must be an array")
        for name in ("recipeSchemaVersion", "recipeRevision", "applierKeyVersion"):
            value = target.get(name)
            if not isinstance(value, int) or isinstance(value, bool) or value < 1 or value > 0xFFFFFFFF:
                raise AuthoringMismatch(where + "." + name + " must be a positive 32-bit integer")
        by_role[role] = {
            "role": role,
            "recipeStableName": stable_name(
                target.get("recipeStableName"), where + ".recipeStableName"),
            "recipeSchemaStableName": stable_name(
                target.get("recipeSchemaStableName"), where + ".recipeSchemaStableName"),
            "recipeSchemaVersion": target["recipeSchemaVersion"],
            "recipeRevision": target["recipeRevision"],
            "applierStableName": stable_name(target.get("applierStableName"), where + ".applierStableName"),
            "applierKeyVersion": target["applierKeyVersion"],
            "baseLayoutSchemaStableNames": [
                stable_name(name, where + ".baseLayoutSchemaStableNames[" + str(j) + "]")
                for j, name in enumerate(layout)
            ],
            "descriptorTagStableNames": [
                stable_name(name, where + ".descriptorTagStableNames[" + str(j) + "]")
                for j, name in enumerate(tags)
            ],
            "initialPositionMilli": vector(target.get("initialPositionMilli"), where + ".initialPositionMilli"),
            "initialVelocityMilli": vector(target.get("initialVelocityMilli"), where + ".initialVelocityMilli"),
        }

    for role in ROLES:
        if role not in by_role:
            raise AuthoringMismatch("the document declares no '" + role + "' target")
    return by_role


def emit(authoring_text, roles):
    runner = roles["runner"]
    authoring_hash = hashlib.sha256(authoring_text.encode("utf-8")).hexdigest()
    out = []
    out.append("// <auto-generated />\n")
    out.append("// Generated by GameCore.Validation.Editor.BakeCatalogCoverageAuthoring; do not edit by hand.\n")
    out.append("//\n")
    out.append("// Baked artifact of " + AUTHORING_RECORDED_PATH + " (04 section 6: baking is an Editor workflow, and the player\n")
    out.append("// instantiates baked definitions rather than invoking a baker). Every identity below is a stable name:\n")
    out.append("// the player derives the 128-bit ids with the production rule at the use site, so this artifact carries no\n")
    out.append("// CLR assembly name, no engine handle and no precomputed identity that could drift from the vocabulary\n")
    out.append("// the runtime recipe is declared with (P-004). Regenerating from the same authoring document must reproduce\n")
    out.append("// this file byte for byte.\n")
    out.append("#nullable enable\n")
    out.append("\n")
    out.append("namespace GameCore.Validation.Generated\n")
    out.append("{\n")
    out.append("    /// <summary>\n")
    out.append("    /// Editor-baked target definitions the catalog-coverage probe instantiates alongside the runtime recipe\n")
    out.append("    /// catalog, so TEST-020's bake/runtime recipe parity is a comparison of two materialized targets\n")
    out.append("    /// rather than of two descriptions.\n")
    out.append("    /// </summary>\n")
    out.append("    public static class CatalogCoverageBaked\n")
    out.append("    {\n")
    out.append("        /// <summary>Format id of this baked artifact.</summary>\n")
    out.append("        public const string Format = \"" + ARTIFACT_FORMAT + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Repository-relative authoring document this artifact was baked from.</summary>\n")
    out.append("        public const string AuthoringDocument = \"" + AUTHORING_RECORDED_PATH + "\";\n")
    out.append("\n")
    out.append("        /// <summary>SHA-256 over the UTF-8 bytes of the authoring document, recorded by the bake.</summary>\n")
    out.append("        public const string AuthoringHash = \"" + authoring_hash + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Editor entry point that produces this artifact.</summary>\n")
    out.append("        public const string BakedBy = \"" + BAKED_BY + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Stable role name of the one baked target.</summary>\n")
    out.append("        public const string RunnerRole = \"" + runner["role"] + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Stable recipe name of the baked runner target (P-004); the player applies the\n")
    out.append("        /// package's own recipe rule to it, so a definition identity cannot be baked as a literal.</summary>\n")
    out.append("        public const string RunnerRecipeStableName = \""
               + runner["recipeStableName"] + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Stable schema name the baked runner recipe declares; its `SchemaRef` version is below.</summary>\n")
    out.append("        public const string RunnerRecipeSchemaStableName = \""
               + runner["recipeSchemaStableName"] + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Schema version the baked runner recipe declares.</summary>\n")
    out.append("        public const uint RunnerRecipeSchemaVersion = " + str(runner["recipeSchemaVersion"]) + "U;\n")
    out.append("\n")
    out.append("        /// <summary>Immutable definition revision the baked runner recipe carries (05 section 2).</summary>\n")
    out.append("        public const uint RunnerRecipeRevision = " + str(runner["recipeRevision"]) + "U;\n")
    out.append("\n")
    out.append("        /// <summary>Stable registration name of the base-layout applier the baked recipe resolves.</summary>\n")
    out.append("        public const string RunnerApplierStableName = \"" + runner["applierStableName"] + "\";\n")
    out.append("\n")
    out.append("        /// <summary>Key version the baked recipe's applier registration carries.</summary>\n")
    out.append("        public const uint RunnerApplierKeyVersion = " + str(runner["applierKeyVersion"]) + "U;\n")
    out.append("\n")
    out.append("        /// <summary>Base-layout selector schemas the baked recipe installs, in declared order.</summary>\n")
    out.append("        public static readonly string[] RunnerBaseLayoutSchemaStableNames =\n")
    out.append("        {\n")
    for name in runner["baseLayoutSchemaStableNames"]:
        out.append("            \"" + name + "\",\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Immutable descriptor tags the baked recipe advertises, in declared order (P-015).</summary>\n")
    out.append("        public static readonly string[] RunnerDescriptorTagStableNames =\n")
    out.append("        {\n")
    for name in runner["descriptorTagStableNames"]:
        out.append("            \"" + name + "\",\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Position the baked recipe installs on a runner, in millimetres.</summary>\n")
    out.append("        public const int RunnerInitialPositionXMilli = " + str(runner["initialPositionMilli"][0]) + ";\n")
    out.append("        public const int RunnerInitialPositionYMilli = " + str(runner["initialPositionMilli"][1]) + ";\n")
    out.append("        public const int RunnerInitialPositionZMilli = " + str(runner["initialPositionMilli"][2]) + ";\n")
    out.append("\n")
    out.append("        /// <summary>Velocity the baked recipe installs on a runner, in thousandths of a metre per second.</summary>\n")
    out.append("        public const int RunnerInitialVelocityXMilli = " + str(runner["initialVelocityMilli"][0]) + ";\n")
    out.append("        public const int RunnerInitialVelocityYMilli = " + str(runner["initialVelocityMilli"][1]) + ";\n")
    out.append("        public const int RunnerInitialVelocityZMilli = " + str(runner["initialVelocityMilli"][2]) + ";\n")
    out.append("\n")
    out.append("        /// <summary>Number of baked targets this artifact declares.</summary>\n")
    out.append("        public const int BakedTargetCount = " + str(len(ROLES)) + ";\n")
    out.append("    }\n")
    out.append("}\n")
    return "".join(out)


def compute():
    authoring_path = ROOT / AUTHORING_PATH
    authoring_text = authoring_path.read_text(encoding="utf-8")
    roles = validate(json.loads(authoring_text))
    return emit(authoring_text, roles)


def main() -> int:
    check = "--check" in sys.argv[1:]
    artifact = compute()
    artifact_path = ROOT / ARTIFACT_PATH

    if check:
        if not artifact_path.exists():
            print("MISSING: " + str(artifact_path))
            return 1
        if artifact_path.read_text(encoding="utf-8") != artifact:
            print("STALE: " + str(artifact_path) + " differs from the authoring document's baked output")
            return 1
        print("baked catalog-coverage artifact reproduces from " + AUTHORING_PATH)
        return 0

    artifact_path.parent.mkdir(parents=True, exist_ok=True)
    artifact_path.write_text(artifact, encoding="utf-8")
    print("wrote " + ARTIFACT_PATH + " (" + str(len(artifact.encode("utf-8"))) + " bytes, authoringHash "
          + hashlib.sha256((ROOT / AUTHORING_PATH).read_text(encoding="utf-8").encode("utf-8")).hexdigest() + ")")
    return 0


if __name__ == "__main__":
    sys.exit(main())
