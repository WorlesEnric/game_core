#!/usr/bin/env python3
"""Regenerate the lever catalog, source-content hash and deterministic Unity metas; stdlib only.

Only writes inside Lever~/package, which Unity does not import. --check writes nothing.
The sandbox and reviewed game registry compose this exact generated catalog; no Editor contributor discovery.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import sys
import uuid

sys.dont_write_bytecode = True

HERE = Path(__file__).resolve().parent
ROOT = next(p for p in HERE.parents if (p / "tools/emit_generated_catalog.py").is_file())
PACKAGE = HERE / "package"
PREFIX = "gameplay.hollowmere.lever."
OWNER = PREFIX + "package"
GENERATED = PACKAGE / "Runtime/Generated/LeverCatalog.g.cs"
CONTENT = PACKAGE / "Runtime/Generated/LeverPackageContent.g.cs"


def load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def hex_id(name: str) -> str:
    return hashlib.sha256(name.encode()).hexdigest()[:32]


def description() -> dict:
    schemas = []
    for schema, serializer, stem in [("schema.config", "serializer.config", "LeverConfig"),
                                     ("domain.lever", "serializer.domain-lever", "LeverDomain")]:
        schemas.append({"stableName": PREFIX + serializer, "valueTypeName": stem + "Value",
                        "serializerTypeName": stem + "Serializer", "serializerKeyName": stem + "SerializerKey",
                        "schemaId": hex_id(PREFIX + schema), "schemaVersion": 1, "serializerKeyVersion": 1,
                        "ownerPackageId": hex_id(OWNER), "required": True,
                        "fields": [{"id": 1, "name": "SchemaVersion", "wireType": "UInt32", "required": True}]})
    groups = []
    entries = [("PluginFactory", "plugin", "LeverPluginKey"),
               ("SystemFactory", "system.command", "LeverCommandSystemKey"),
               ("LayoutApply", "applier", "LeverApplierKey"), ("LayoutApply", "layout.lever", "LeverLayoutKey")]
    for kind, table, keys, lookup in [("PluginFactory", "PluginRegistrations", "PluginKeys", "TryGetPlugin"),
                                       ("SystemFactory", "SystemRegistrations", "SystemKeys", "TryGetSystem"),
                                       ("LayoutApply", "LayoutRegistrations", "LayoutKeys", "TryGetLayout")]:
        groups.append({"tableName": table, "keysName": keys, "lookupMethodName": lookup,
                       "interfaceType": "GameCore.Gameplay.Contracts.IGameplayCatalogEntry", "kind": kind,
                       "entries": [{"stableName": PREFIX + suffix, "keyName": key, "keyVersion": 1,
                                    "ownerPackageId": hex_id(OWNER),
                                    "implementationId": hex_id("gameplay.impl.hollowmere.lever." + suffix),
                                    "implementationExpression": "new GameplayCatalogEntry(" + key + ")"}
                                   for group, suffix, key in entries if group == kind]})
    return {"descriptionFormat": "gamecore.catalog-description/1", "protocolVersion": "1.0",
            "namespace": "Hollowmere.Mechanism.Lever.Generated", "className": "LeverCatalog",
            "fileName": GENERATED.name, "supportedFeatureIds": [], "schemas": schemas, "groups": groups,
            "code": {"usingDirectives": ["GameCore.Gameplay.Contracts"]}}


def outputs() -> dict[Path, bytes]:
    emitter = load("lever_catalog_emitter", ROOT / "tools/emit_generated_catalog.py")
    doc = description()
    catalog, _, coverage = emitter.emit(emitter.validate(doc))
    # Same no-static-mutable-table adaptation as the maintained mechanism generator.
    catalog = re.sub(r"static readonly ([^\n=]+\[\]) (\w+) =\n(\s*)\{",
                     r"static \1 \2 => new \1\n\3{", catalog)
    marker = catalog.index(emitter.HASH_DECLARATION)
    digest = emitter.sha256_hex(catalog[:marker])
    catalog = re.sub(r'(public const string CatalogFileHash = ")[a-f0-9]+', r'\g<1>' + digest, catalog)
    result = {PACKAGE / "Catalog/LeverCatalog.catalog.json": (json.dumps(doc, indent=2) + "\n").encode(),
              GENERATED: catalog.encode(), emitter.coverage_path_for(GENERATED): coverage.encode()}
    all_paths = set(PACKAGE.rglob("*")) | set(result) | {CONTENT}
    for path in tuple(all_paths):
        all_paths.update(p for p in path.parents if p != PACKAGE and PACKAGE in p.parents)
    for path in sorted(all_paths):
        if path.suffix == ".meta" or "__pycache__" in path.parts:
            continue
        meta = path.with_name(path.name + ".meta")
        if not meta.exists():
            guid = uuid.uuid5(uuid.NAMESPACE_URL, "com.hollowmere.mechanism.lever/" + path.relative_to(PACKAGE).as_posix()).hex
            is_dir = path.is_dir() or any(path in p.parents for p in all_paths)
            result[meta] = ("fileFormatVersion: 2\nguid: " + guid + "\n" + ("folderAsset: yes\n" if is_dir else "")).encode()
    files = {p: p.read_bytes() for p in PACKAGE.rglob("*") if p.is_file() and p != CONTENT and "__pycache__" not in p.parts}
    files.update(result)
    sha = hashlib.sha256()
    for path, data in sorted(files.items()):
        sha.update(path.relative_to(PACKAGE).as_posix().encode() + b"\0")
        sha.update(len(data).to_bytes(8, "big"))
        sha.update(data)
    content = sha.digest()
    result[CONTENT] = ("// Generated by make-catalog.py: SHA-256 of path, byte count and bytes of every other package file.\n"
                       "#nullable enable\nusing GameCore.Contracts;\nnamespace Hollowmere.Mechanism.Lever.Generated\n{\n"
                       "    public static class LeverPackageContent\n    {\n"
                       "        public const string Hash = \"" + content.hex() + "\";\n"
                       "        public static readonly ContentHash Value = new ContentHash(new byte[] { "
                       + ", ".join("0x" + format(b, "02x") for b in content) + " });\n    }\n}\n").encode()
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    for path, data in outputs().items():
        if args.check:
            if not path.is_file() or path.read_bytes() != data:
                print("stale: " + str(path), file=sys.stderr)
                return 1
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(data)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
