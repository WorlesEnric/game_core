#!/usr/bin/env python3
"""Write the pressure plate's catalog description and regenerate its generated catalog.

The description (gamecore.catalog-description/1) is derived from the stable names below, exactly as
`GameCore.Gameplay.Compile.CatalogDescriptionWriter` derives the gameplay catalog's, but under the mechanism's own
prefix `hollowmere.pressureplate.` so no registration can collide with the world catalog it is composed with. The
generated C# is produced by the repository's emitter mirror `tools/emit_generated_catalog.py` (interpreter only).

usage:
  python3 samples/mechanisms/pressure-plate/make-catalog.py          # write description + generated file
  python3 samples/mechanisms/pressure-plate/make-catalog.py --check  # fail when either file is stale
"""

from __future__ import annotations

import hashlib
import importlib.util
import re
import json
import pathlib
import subprocess
import sys

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[2]
PACKAGE = HERE / "package"
DESCRIPTION = PACKAGE / "Catalog" / "PressurePlateCatalog.catalog.json"
GENERATED = PACKAGE / "Runtime" / "Generated" / "PressurePlateCatalog.g.cs"
EMITTER = ROOT / "tools" / "emit_generated_catalog.py"

PREFIX = "hollowmere.pressureplate."
OWNER = PREFIX + "package"

SCHEMAS = [
    # (schema name, serializer name, type stem)
    (PREFIX + "schema.config", PREFIX + "serializer.config", "PressurePlateConfig"),
    (PREFIX + "domain.plate", PREFIX + "serializer.domain-plate", "PlateDomain"),
]

ENTRIES = [
    # (group kind, stable name suffix, key name)
    ("PluginFactory", "plugin", "PressurePlatePluginKey"),
    ("SystemFactory", "system.command", "PressurePlateCommandSystemKey"),
    ("LayoutApply", "applier", "PlateApplierKey"),
    ("LayoutApply", "layout.plate", "PlateLayoutKey"),
]

GROUPS = [
    ("PluginFactory", "PluginRegistrations", "PluginKeys", "TryGetPlugin"),
    ("SystemFactory", "SystemRegistrations", "SystemKeys", "TryGetSystem"),
    ("LayoutApply", "LayoutRegistrations", "LayoutKeys", "TryGetLayout"),
]


def hex_id(name: str) -> str:
    return hashlib.sha256(name.encode("utf-8")).hexdigest()[:32]


def description_text() -> str:
    schemas = []
    for schema, serializer, stem in SCHEMAS:
        schemas.append({
            "stableName": serializer,
            "valueTypeName": stem + "Value",
            "serializerTypeName": stem + "Serializer",
            "serializerKeyName": stem + "SerializerKey",
            "schemaId": hex_id(schema),
            "schemaVersion": 1,
            "serializerKeyVersion": 1,
            "ownerPackageId": hex_id(OWNER),
            "required": True,
            "fields": [{"id": 1, "name": "SchemaVersion", "wireType": "UInt32", "required": True}],
        })
    groups = []
    for kind, table, keys, lookup in GROUPS:
        entries = []
        for group, suffix, key in ENTRIES:
            if group != kind:
                continue
            entries.append({
                "stableName": PREFIX + suffix,
                "keyName": key,
                "keyVersion": 1,
                "ownerPackageId": hex_id(OWNER),
                "implementationId": hex_id(PREFIX + "impl." + suffix),
                "implementationExpression": "new GameplayCatalogEntry(" + key + ")",
            })
        groups.append({
            "tableName": table,
            "keysName": keys,
            "lookupMethodName": lookup,
            "interfaceType": "GameCore.Gameplay.Contracts.IGameplayCatalogEntry",
            "kind": kind,
            "entries": entries,
        })
    document = {
        "descriptionFormat": "gamecore.catalog-description/1",
        "protocolVersion": "1.0",
        "namespace": "Hollowmere.Mechanism.PressurePlate.Generated",
        "className": "PressurePlateCatalog",
        "fileName": "PressurePlateCatalog.g.cs",
        "supportedFeatureIds": [],
        "schemas": schemas,
        "groups": groups,
        "code": {"usingDirectives": ["GameCore.Gameplay.Contracts"]},
    }
    return json.dumps(document, indent=2, ensure_ascii=False) + "\n"


def main() -> int:
    check = "--check" in sys.argv[1:]
    text = description_text()
    spec = importlib.util.spec_from_file_location("catalog_emitter", EMITTER)
    emitter = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(emitter)
    catalog, _, coverage = emitter.emit(emitter.validate(json.loads(text)))
    # D1: callers receive fresh tables, never a shared mutable static array.
    catalog = re.sub(r"static readonly ([^\n=]+\[\]) (\w+) =\n(\s*)\{",
                     r"static \1 \2 => new \1\n\3{", catalog)
    marker = catalog.index(emitter.HASH_DECLARATION)
    digest = emitter.sha256_hex(catalog[:marker])
    catalog = re.sub(r'(public const string CatalogFileHash = ")[a-f0-9]+', r'\g<1>' + digest, catalog)
    outputs = {DESCRIPTION: text, GENERATED: catalog, emitter.coverage_path_for(GENERATED): coverage}
    if check:
        return int(any(not path.exists() or path.read_text() != content for path, content in outputs.items()))
    for path, content in outputs.items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content)
    return 0


if __name__ == "__main__":
    sys.exit(main())
