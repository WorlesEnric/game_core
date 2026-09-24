#!/usr/bin/env python3
"""Verify a generated GameCore catalog without a C# compiler (GC-003).

The committed probe catalog is produced by `GameCore.Content.Compiler.CatalogEmitter` (through the Editor
bridge). On a host with no .NET SDK, two properties of the generated file can still be checked exactly as the
player checks them:

1. `CatalogFileHash` covers the UTF-8 bytes of the file prefix that ends immediately before its declaration.
2. `CatalogFingerprint` equals `GameCore.Contracts.CatalogFingerprint.Compute` over the tables the generated
   `BuildCatalog()` builds: every registration table entry, one serializer registration per schema, the schema
   registrations and the declared features, all in canonical key/id order.

This is an independent recomputation from the file's own text; it does not call the compiler.

usage: python3 tools/verify_generated_catalog.py [path to generated .cs]   (default: the committed probe catalog)
"""

from __future__ import annotations

import hashlib
import pathlib
import re
import sys

DEFAULT_CATALOG = "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs"

HASH_DECLARATION = "        public const string CatalogFileHash = "

KIND = {
    "PluginFactory": 0,
    "SystemFactory": 1,
    "Reducer": 2,
    "StaticPredicate": 3,
    "Serializer": 4,
    "Migration": 5,
    "ResourceFactory": 6,
    "LayoutApply": 7,
    "SchemaFactory": 8,
    "StatePolicy": 9,
    "Handler": 10,
}


def block_of(text: str, name: str) -> str:
    match = re.search(re.escape(name) + r" =\n        \{\n(.*?)\n        \};", text, re.S)
    if match is None:
        raise SystemExit("generated file has no table named " + name)
    return match.group(1)


def key_of(text: str, name: str) -> tuple[int, int, int]:
    match = re.search(
        re.escape(name)
        + r" = new FactoryKey\(new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\), (\d+)U\)",
        text,
    )
    if match is None:
        raise SystemExit("generated file has no FactoryKey named " + name)
    return int(match.group(1), 16), int(match.group(2), 16), int(match.group(3))


def pack_factory(key: tuple[int, int, int], kind: int, owner: tuple[int, int], impl: tuple[int, int], contract: int) -> bytes:
    return (
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


def main() -> int:
    path = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else DEFAULT_CATALOG)
    if not path.exists():
        print("no generated catalog at " + str(path))
        return 2

    text = path.read_text(encoding="utf-8")
    problems = []

    # 1. file-prefix hash
    marker = text.find(HASH_DECLARATION)
    if marker < 0:
        problems.append("the file has no CatalogFileHash declaration")
    declared_hash = re.search(r'CatalogFileHash = "([0-9a-f]{64})"', text)
    if declared_hash is None:
        problems.append("CatalogFileHash is not a 64-character lowercase hex value")
    elif marker >= 0:
        computed_hash = hashlib.sha256(text[:marker].encode("utf-8")).hexdigest()
        if computed_hash != declared_hash.group(1):
            problems.append(
                "CatalogFileHash mismatch: recorded %s, recomputed %s" % (declared_hash.group(1), computed_hash)
            )

    # 2. fingerprint over the generated tables
    factories = []
    for table, kind_name in (
        ("PluginRegistrationsCatalogRegistrations", None),
        ("HandlerRegistrationsCatalogRegistrations", None),
    ):
        if table not in text:
            continue
        for key_name, entry_kind, oh, ol, ih, il, version in re.findall(
            r"new FactoryRegistration\(\s*(\w+),\s*FactoryKind\.(\w+),\s*new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\),"
            r"\s*new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\),\s*(\d+)U\)",
            block_of(text, table),
        ):
            if entry_kind not in KIND:
                problems.append("unknown FactoryKind." + entry_kind + " in " + table)
                continue
            factories.append(
                (
                    key_of(text, key_name),
                    KIND[entry_kind],
                    (int(oh, 16), int(ol, 16)),
                    (int(ih, 16), int(il, 16)),
                    int(version),
                )
            )

    schemas = []
    for sh, sl, sver, oh, ol, key_name, required in re.findall(
        r"new SchemaRegistration\(\s*new SchemaRef\(new SchemaId\(new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\)\), (\d+)U\),"
        r"\s*new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\),\s*(\w+),\s*(true|false)\)",
        block_of(text, "SchemaRegistrations"),
    ):
        schemas.append(
            (
                (int(sh, 16), int(sl, 16)),
                int(sver),
                (int(oh, 16), int(ol, 16)),
                key_of(text, key_name),
                required == "true",
            )
        )

    # BuildCatalog() appends one serializer registration per schema row, exactly as computed here.
    for schema_id, schema_version, owner, key, _ in schemas:
        factories.append((key, KIND["Serializer"], owner, schema_id, schema_version))

    factories.sort(key=lambda entry: (entry[0][0], entry[0][1], entry[0][2]))
    schemas.sort(key=lambda entry: entry[0])
    features = sorted(
        (int(h, 16), int(l, 16))
        for h, l in re.findall(
            r"new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\)", block_of(text, "SupportedFeatureIds")
        )
    )

    buffer = bytearray()
    for key, kind, owner, impl, contract in factories:
        buffer += pack_factory(key, kind, owner, impl, contract)
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

    computed_fingerprint = hashlib.sha256(bytes(buffer)).hexdigest()
    declared_fingerprint = re.search(r'CatalogFingerprint = "([0-9a-f]{64})"', text)
    if declared_fingerprint is None:
        problems.append("the file has no CatalogFingerprint declaration")
    elif declared_fingerprint.group(1) != computed_fingerprint:
        problems.append(
            "CatalogFingerprint mismatch: recorded %s, recomputed %s over %d factories, %d schemas, %d features"
            % (declared_fingerprint.group(1), computed_fingerprint, len(factories), len(schemas), len(features))
        )

    print(
        "%s: %d factories, %d schemas, %d features, %d bytes"
        % (path, len(factories), len(schemas), len(features), len(text.encode("utf-8")))
    )
    if problems:
        print("%d problem(s):" % len(problems))
        for problem in problems:
            print("  - " + problem)
        return 1

    print("file hash and catalog fingerprint both recompute correctly from the generated tables")
    return 0


if __name__ == "__main__":
    sys.exit(main())
