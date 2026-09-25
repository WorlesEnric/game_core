#!/usr/bin/env python3
"""Apply a new leading registration group to a committed generated catalog (GC-012, no-SDK host helper).

WHY THIS EXISTS
---------------
A generated catalog (`unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs` and
`.../GeneratedCards/CardCatalog.g.cs`) is produced by `GameCore.Content.Compiler.CatalogEmitter` through the Editor
bridge (`ProbeCatalogGenerator` / `CardCatalogGenerator`), and `tools/run_w3_gate.sh` refuses a build when a
committed `.g.cs` differs from a fresh generation. The authoring host for GC-012 has no .NET SDK and no Unity, so
the Editor bridge cannot run here.

This script is that missing step, limited to exactly the change GC-012 makes: **one new registration group that
sorts before every existing group** (both catalogs add `FamilyEntryRegistrations`, which sorts before
`HandlerRegistrations` / `PluginRegistrations` / `ReducerRegistrations` / `StaticPredicateRegistrations` /
`SystemFactoryRegistrations`). It rewrites the three sections the emitter emits in group order (keys, tables,
lookups), updates `RegistrationGroupCount` and `GroupCatalogRegistrations`, and recomputes both hash literals.

The generated text is byte-for-byte the emitter's own format, taken from `CatalogEmitter.cs` (`EmitKeys`,
`EmitTables`, `EmitLookups`, `EmitCatalogFactory`, `EmitCodeDirectives`). It is verified two ways:

* `tools/verify_generated_catalog.py <file>` independently recomputes `CatalogFileHash` (SHA-256 of the file
  prefix before the declaration) and `CatalogFingerprint` (canonical packing of every registration row) from the
  file's own text, so a mis-emitted block cannot pass unnoticed.
* If the Editor bridge later regenerates the file and the result differs, the bridge is authoritative and this
  script is the thing that is wrong; the diff is the finding.

usage: python3 tools/regen_catalog_group.py <catalog description json> <generated .cs>
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import sys

HASH_DECLARATION = "        public const string CatalogFileHash = "

KIND = {
    "PluginFactory": "0",
    "SystemFactory": "1",
    "Reducer": "2",
    "StaticPredicate": "3",
    "Serializer": "4",
    "Migration": "5",
    "ResourceFactory": "6",
    "LayoutApply": "7",
    "SchemaFactory": "8",
    "StatePolicy": "9",
    "Handler": "10",
}

GROUP_METHOD = re.compile(
    r"private static FactoryRegistration\[\] GroupCatalogRegistrations\(\)\n        \{\n(.*?)\n        \}",
    re.S,
)


def key_literal(stable_name: str) -> tuple[str, str]:
    digest = hashlib.sha256(stable_name.encode("utf-8")).digest()[:16]
    return (
        digest[:8].hex().upper(),
        digest[8:].hex().upper(),
    )


def ordered_entries(group: dict) -> list[dict]:
    """The group's entries in the emitter's canonical order: ascending derived key, then key version.

    `CatalogEmitter` sorts each table's entries by derived key, so a description's declaration order never reaches
    the generated file (P-008). A single-entry group is unaffected, which is why the fidelity check only catches
    this on a group with several entries.
    """
    return sorted(
        group["entries"],
        key=lambda entry: (
            int(key_literal(entry["stableName"])[0], 16),
            int(key_literal(entry["stableName"])[1], 16),
            entry["keyVersion"],
        ),
    )


def emit_keys(group: dict) -> str:
    text = []
    for entry in ordered_entries(group):
        high, low = key_literal(entry["stableName"])
        text.append(
            "        /// <summary>Generated key of %s ('%s').</summary>\n"
            "        public static readonly FactoryKey %s = new FactoryKey(new Id128(0x%sUL, 0x%sUL), %dU);\n\n"
            % (entry["stableName"], entry["stableName"], entry["keyName"], high, low, entry["keyVersion"])
        )
    return "".join(text)


def emit_tables(group: dict) -> str:
    interface = group["interfaceType"]
    table = group["tableName"]
    kind = group["kind"]
    text = [
        "        /// <summary>Generated registrations of %s, in canonical key order, each bound to a direct constructor reference.</summary>\n" % kind,
        "        public static readonly BoundRegistration<%s>[] %s =\n" % (interface, table),
        "        {\n",
    ]
    for entry in ordered_entries(group):
        text.append("            new BoundRegistration<%s>(\n" % interface)
        text.append("                %s,\n" % entry["keyName"])
        text.append('                "%s",\n' % entry["stableName"])
        text.append("                %s),\n" % entry["implementationExpression"])
    text.append("        };\n\n")

    text.append("        /// <summary>Generated keys of %s, in the same canonical order.</summary>\n" % table)
    text.append("        public static readonly FactoryKey[] %s =\n" % group["keysName"])
    text.append("        {\n")
    for entry in ordered_entries(group):
        text.append("            %s,\n" % entry["keyName"])
    text.append("        };\n\n")

    text.append("        /// <summary>\n")
    text.append("        /// Catalog registrations of %s, in the same canonical order as %s.\n" % (kind, table))
    text.append("        /// Every entry carries its own owner package and precompiled implementation identity, so the\n")
    text.append("        /// runtime catalog hashes exactly the declarations this file was generated from (P-009, P-028).\n")
    text.append("        /// </summary>\n")
    text.append("        public static readonly FactoryRegistration[] %sCatalogRegistrations =\n" % table)
    text.append("        {\n")
    for entry in ordered_entries(group):
        owner_high = entry["ownerPackageId"][:16].upper()
        owner_low = entry["ownerPackageId"][16:].upper()
        impl_high = entry["implementationId"][:16].upper()
        impl_low = entry["implementationId"][16:].upper()
        text.append("            new FactoryRegistration(\n")
        text.append("                %s,\n" % entry["keyName"])
        text.append("                FactoryKind.%s,\n" % kind)
        text.append("                new Id128(0x%sUL, 0x%sUL),\n" % (owner_high, owner_low))
        text.append("                new Id128(0x%sUL, 0x%sUL),\n" % (impl_high, impl_low))
        text.append("                %dU),\n" % entry["keyVersion"])
    text.append("        };\n\n")
    return "".join(text)


def emit_lookup(group: dict) -> str:
    table = group["tableName"]
    interface = group["interfaceType"]
    text = [
        "        /// <summary>\n",
        "        /// Resolves one generated registration by key. A key absent from this table returns false with a\n",
        "        /// null implementation; nothing is constructed by reflection or runtime type discovery (04 section 8).\n",
        "        /// </summary>\n",
        "        public static bool %s(FactoryKey key, out %s? implementation)\n" % (group["lookupMethodName"], interface),
        "        {\n",
        "            for (int i = 0; i < %s.Length; i++)\n" % table,
        "            {\n",
        "                if (%s[i].Key.Equals(key))\n" % table,
        "                {\n",
        "                    implementation = %s[i].Implementation;\n" % table,
        "                    return true;\n",
        "                }\n",
        "            }\n\n",
        "            implementation = null;\n",
        "            return false;\n",
        "        }\n\n",
    ]
    return "".join(text)


def first_group_key_anchor(text: str, groups: list[dict]) -> str:
    """The doc-comment line that opens the key declarations of every existing group, in order."""
    for group in groups:
        anchor = "        /// <summary>Generated key of " + group["entries"][0]["stableName"] + " ('"
        if anchor in text:
            return anchor
    raise SystemExit("no key-declaration anchor found")


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__)
        return 2

    description_path = pathlib.Path(sys.argv[1])
    generated_path = pathlib.Path(sys.argv[2])
    description = json.loads(description_path.read_text(encoding="utf-8"))
    text = generated_path.read_text(encoding="utf-8")

    method = GROUP_METHOD.search(text)
    if method is None:
        raise SystemExit("the generated file has no GroupCatalogRegistrations method")
    existing_names = re.findall(r"Array\.Copy\((\w+CatalogRegistrations), 0, all, offset, (\d+)\)", method.group(1))
    existing_tables = [name[: -len("CatalogRegistrations")] for name, _ in existing_names]

    # The anchors are the groups in the emitter's own order — ascending table name — because that is the order the
    # three sections are emitted in. A description's declaration order never reaches the generated file (P-008), so
    # anchoring on the description order would place a new group in the middle of a section.
    ordered = sorted(description["groups"], key=lambda g: g["tableName"])
    existing_groups = [g for g in ordered if g["tableName"] in existing_tables]
    new_groups = [g for g in ordered if g["tableName"] not in existing_tables]
    if len(existing_groups) != len(existing_tables):
        raise SystemExit("the description and the generated file disagree about the existing groups")
    if [g["tableName"] for g in existing_groups] != existing_tables:
        raise SystemExit(
            "the generated file's group order is not ascending table name, so this helper's anchors would be wrong"
        )

    if not new_groups:
        print("nothing to do: every declared group is already present in " + str(generated_path))
        return 0

    for group in new_groups:
        if ordered.index(group) != 0:
            raise SystemExit(
                "this helper only inserts a group that sorts before every existing group (the GC-012 case); "
                + group["tableName"] + " does not"
            )

    # ---- 1. keys: before the first existing group's first key declaration
    anchor = first_group_key_anchor(text, existing_groups)
    text = text.replace(anchor, "".join(emit_keys(g) for g in new_groups) + anchor, 1)

    # ---- 2. tables: before the first existing group's first table declaration
    anchor = "        /// <summary>Generated registrations of " + existing_tables[0].replace(
        "Registrations", ""
    )
    table_anchor = None
    for group in existing_groups:
        candidate = (
            "        /// <summary>Generated registrations of "
            + group["kind"]
            + ", in canonical key order, each bound to a direct constructor reference.</summary>\n"
            + "        public static readonly BoundRegistration<"
            + group["interfaceType"]
            + ">[] "
            + group["tableName"]
            + " =\n"
        )
        if candidate in text:
            table_anchor = candidate
            break
    if table_anchor is None:
        raise SystemExit("no table-declaration anchor found")
    text = text.replace(table_anchor, "".join(emit_tables(g) for g in new_groups) + table_anchor, 1)

    # ---- 3. lookups: before the first existing group's lookup method
    lookup_anchor = None
    for group in existing_groups:
        candidate = (
            "        /// <summary>\n"
            "        /// Resolves one generated registration by key. A key absent from this table returns false with a\n"
            "        /// null implementation; nothing is constructed by reflection or runtime type discovery (04 section 8).\n"
            "        /// </summary>\n"
            "        public static bool " + group["lookupMethodName"] + "(FactoryKey key, out " + group["interfaceType"] + "? implementation)\n"
        )
        if candidate in text:
            lookup_anchor = candidate
            break
    if lookup_anchor is None:
        raise SystemExit("no lookup-method anchor found")
    text = text.replace(lookup_anchor, "".join(emit_lookup(g) for g in new_groups) + lookup_anchor, 1)

    # ---- 4. GroupCatalogRegistrations: the new copies first, the total widened
    total = 0
    for group in description["groups"]:
        total += len(group["entries"])
    copies = []
    offset = 0
    for group in sorted(description["groups"], key=lambda g: g["tableName"]):
        if not group["entries"]:
            continue
        copies.append(
            "            Array.Copy(%sCatalogRegistrations, 0, all, offset, %d);\n"
            % (group["tableName"], len(group["entries"]))
        )
        copies.append("            offset += %d;\n" % len(group["entries"]))
        offset += len(group["entries"])

    method = GROUP_METHOD.search(text)
    assert method is not None
    body = "".join(copies)
    replacement = (
        "private static FactoryRegistration[] GroupCatalogRegistrations()\n        {\n"
        "            FactoryRegistration[] all = new FactoryRegistration[%d];\n"
        "            int offset = 0;\n" % total
        + body
        + "\n            return all;\n        }"
    )
    text = text[: method.start()] + replacement + text[method.end():]

    # ---- 5. RegistrationGroupCount
    text = re.sub(
        r"RegistrationGroupCount = \d+;",
        "RegistrationGroupCount = %d;" % len(description["groups"]),
        text,
        count=1,
    )

    # ---- 6. using directives the new group needs, in the description's declared order
    declared = description["code"]["usingDirectives"]
    declared_block = "".join("using %s;\n" % name for name in declared)
    existing_block = re.search(r"(?:using [\w\.]+;\n)+", text)
    if existing_block is None:
        raise SystemExit("the generated file has no using-directive block")
    text = text[: existing_block.start()] + "using System;\nusing System.Collections.Generic;\nusing GameCore.Contracts;\n" + declared_block + text[existing_block.end():]

    # ---- 7. fingerprint, recomputed exactly as CatalogFingerprint.Compute packs it
    factories = []
    for group in description["groups"]:
        kind = int(KIND[group["kind"]])
        for entry in group["entries"]:
            high, low = key_literal(entry["stableName"])
            key = (int(high, 16), int(low, 16), entry["keyVersion"])
            owner = (int(entry["ownerPackageId"][:16], 16), int(entry["ownerPackageId"][16:], 16))
            impl = (int(entry["implementationId"][:16], 16), int(entry["implementationId"][16:], 16))
            factories.append((key, kind, owner, impl, entry["keyVersion"]))

    schemas = []
    for schema in description["schemas"]:
        serializer_high, serializer_low = key_literal(schema["stableName"])
        schema_id = (int(schema["schemaId"][:16], 16), int(schema["schemaId"][16:], 16))
        schemas.append(
            (
                schema_id,
                schema["schemaVersion"],
                (int(schema["ownerPackageId"][:16], 16), int(schema["ownerPackageId"][16:], 16)),
                (int(serializer_high, 16), int(serializer_low, 16), schema["serializerKeyVersion"]),
                schema["required"],
            )
        )
        factories.append(
            (
                (int(serializer_high, 16), int(serializer_low, 16), schema["serializerKeyVersion"]),
                int(KIND["Serializer"]),
                (int(schema["ownerPackageId"][:16], 16), int(schema["ownerPackageId"][16:], 16)),
                schema_id,
                schema["schemaVersion"],
            )
        )

    features = sorted(
        (int(f[:16], 16), int(f[16:], 16)) for f in description["supportedFeatureIds"]
    )
    factories.sort(key=lambda entry: (entry[0][0], entry[0][1], entry[0][2]))
    schemas.sort(key=lambda entry: entry[0])

    buffer = bytearray()
    for key, kind, owner, impl, contract in factories:
        buffer += (
            key[0].to_bytes(8, "big")
            + key[1].to_bytes(8, "big")
            + key[2].to_bytes(4, "big")
            + kind.to_bytes(4, "big")
            + owner[0].to_bytes(8, "big")
            + owner[1].to_bytes(8, "big")
            + impl[0].to_bytes(8, "big")
            + impl[1].to_bytes(8, "big")
            + contract.to_bytes(4, "big")
        )
    for schema_id, schema_version, owner, key, required in schemas:
        buffer += (
            schema_id[0].to_bytes(8, "big")
            + schema_id[1].to_bytes(8, "big")
            + schema_version.to_bytes(4, "big")
            + (1 if required else 0).to_bytes(1, "big")
            + key[0].to_bytes(8, "big")
            + key[1].to_bytes(8, "big")
            + key[2].to_bytes(4, "big")
            + owner[0].to_bytes(8, "big")
            + owner[1].to_bytes(8, "big")
        )
    for high, low in features:
        buffer += high.to_bytes(8, "big") + low.to_bytes(8, "big")

    fingerprint = hashlib.sha256(bytes(buffer)).hexdigest()
    text = re.sub(
        r'CatalogFingerprint = "[0-9a-f]{64}";',
        'CatalogFingerprint = "%s";' % fingerprint,
        text,
        count=1,
    )

    # ---- 8. file hash covers everything before the declaration, so it is written last
    marker = text.find(HASH_DECLARATION)
    if marker < 0:
        raise SystemExit("the generated file has no CatalogFileHash declaration")
    file_hash = hashlib.sha256(text[:marker].encode("utf-8")).hexdigest()
    text = re.sub(
        r'CatalogFileHash = "[0-9a-f]{64}";',
        'CatalogFileHash = "%s";' % file_hash,
        text,
        count=1,
    )

    generated_path.write_text(text, encoding="utf-8")
    print(
        "wrote %s: %d groups, %d factories, fingerprint %s, file hash %s"
        % (generated_path, len(description["groups"]), len(factories), fingerprint, file_hash)
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
