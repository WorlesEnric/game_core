#!/usr/bin/env python3
"""Generate the GC-018 checkpoint catalog from its description document (no-SDK host helper).

WHY THIS EXISTS
---------------
A generated catalog is produced by `GameCore.Content.Compiler.CatalogEmitter` through an Editor bridge, and the
GC-018 checkpoint catalog is the same kind of generated content as `ProbeCatalog.g.cs` and `CardCatalog.g.cs`:

* Unity bridge: `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/CheckpointCatalogGenerator.cs`
  (menu `GameCore/Content/Generate Catalog` or
  `-executeMethod GameCore.Validation.Editor.CheckpointCatalogGenerator.GenerateCatalog`).
* Host mirror: this script, which reproduces `CatalogEmitter.Emit` for
  `unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json` so the committed
  `.../GeneratedCheckpoint/CheckpointCatalog.g.cs` can be produced and updated on a host with no .NET SDK and no
  Unity, where the Unity bridge cannot run.

This script is subordinate to the Editor bridge: if the bridge ever regenerates the file and the result differs,
the bridge is authoritative and this script is the thing that is wrong. The authoritative proof that this script's
output is correct is the build host's
`dotnet/tests/GameCore.Content.Compiler.Tests/CheckpointCatalogTests` regeneration comparison, which re-runs the
real compiler and asserts the committed bytes are identical.

The emitted text is byte-for-byte the emitter's own format, taken from `CatalogEmitter.cs` (`EmitBody`,
`EmitConstants`, `EmitKeys`, `EmitTables`, `EmitLookups`, `EmitSchemaTypes`, `EmitSerializer`, `EmitFieldCase`,
`EmitCatalogFactory`, `EmitCodeDirectives`) for this description only: 0 registration groups, 12 schemas and
scalar/bytes fields. Every 128-bit literal is derived from a stable name with SHA-256 (P-004); no literal is
hand-typed. `CatalogFileHash` is SHA-256 over the UTF-8 file prefix that ends immediately before its declaration,
and `CatalogFingerprint` is recomputed from the emitted tables and checked against a parse of the emitted text.

usage: python3 tools/emit_checkpoint_catalog.py [catalog description json] [generated .cs]
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import subprocess
import sys

HASH_DECLARATION = "        public const string CatalogFileHash = "
HASH_PLACEHOLDER = "@@CATALOG_FILE_HASH@@"

DEFAULT_DESCRIPTION = "unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json"
DEFAULT_OUTPUT = (
    "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs"
)

# `CatalogWireTypes.All`, in declaration order (name -> C# type, nullable reference, writer, reader).
WIRE_TYPES = {
    "Bool": ("bool", False, "WriteBoolField", "TryReadBool"),
    "Bytes": ("byte[]", True, "WriteBytesField", "TryReadBytes"),
    "Float32": ("float", False, "WriteFloat32Field", "TryReadFloat32"),
    "Float64": ("double", False, "WriteFloat64Field", "TryReadFloat64"),
    "Id128": ("Id128", False, "WriteId128Field", "TryReadId128"),
    "Int32": ("int", False, "WriteInt32Field", "TryReadInt32"),
    "Int64": ("long", False, "WriteInt64Field", "TryReadInt64"),
    "UInt32": ("uint", False, "WriteUInt32Field", "TryReadUInt32"),
    "UInt64": ("ulong", False, "WriteUInt64Field", "TryReadUInt64"),
    "Utf8": ("string", True, "WriteUtf8Field", "TryReadUtf8"),
}

FACTORY_KINDS = {
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

# `FactoryKind.Serializer`, the one registration kind a schema contributes when a catalog declares no groups.
SERIALIZER_KIND = FACTORY_KINDS["Serializer"]

CATALOG_FILE_HASH_SCOPE = (
    "SHA-256 over the UTF-8 bytes, with LF line endings, of the file prefix that ends immediately before the "
    "eight-space-indented CatalogFileHash declaration; that prefix ends with the newline that terminates the "
    "previous line, and the declaration itself, its value and the rest of the file are excluded"
)

# `GameCore.Contracts.CatalogFingerprint.Scope`, copied verbatim.
CATALOG_FINGERPRINT_SCOPE = (
    "SHA-256 over, in this fixed order: (1) every registered factory key in canonical ascending order as 16-byte "
    "big-endian id, 4-byte big-endian key version, 4-byte big-endian factory kind, 16-byte big-endian owner "
    "package id, 16-byte big-endian implementation id, 4-byte big-endian contract version; (2) every accepted "
    "schema in ascending schema-id order as 16-byte big-endian id, 4-byte big-endian schema version, one byte 1 "
    "when required and 0 when optional, 16-byte big-endian serializer key id, 4-byte big-endian serializer key "
    "version, 16-byte big-endian owner package id; (3) every supported feature id in ascending order as 16 bytes. "
    "Declaration order, registration timing, machine paths and timestamps are excluded (P-008, P-028, P-053)."
)

DESCRIPTION_FORMAT = "gamecore.catalog-description/1"
KNOWN_TOP_LEVEL = (
    "descriptionFormat",
    "protocolVersion",
    "namespace",
    "className",
    "fileName",
    "supportedFeatureIds",
    "schemas",
    "groups",
    "code",
)
KNOWN_SCHEMA = (
    "stableName",
    "valueTypeName",
    "serializerTypeName",
    "serializerKeyName",
    "schemaId",
    "schemaVersion",
    "serializerKeyVersion",
    "ownerPackageId",
    "required",
    "fields",
)
KNOWN_FIELD = ("id", "name", "wireType", "required")

IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
IDENTITY_HEX = re.compile(r"^[0-9a-f]{32}$")


class DescriptionMismatch(Exception):
    """The description cannot produce the catalog this script is contracted to emit."""


# ------------------------------------------------------------------------------------------------------------
# Derivation and low-level formatting, mirrored from the compiler.
# ------------------------------------------------------------------------------------------------------------


def derive_key(stable_name: str) -> tuple[int, int]:
    """`StableNameKeyDerivation.Derive`: first 16 SHA-256 bytes as two big-endian u64 words (P-004)."""
    digest = hashlib.sha256(stable_name.encode("utf-8")).digest()[:16]
    return int.from_bytes(digest[:8], "big"), int.from_bytes(digest[8:], "big")


def sha256_hex(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def hex16(value: int) -> str:
    """`CatalogEmitter.Hex`: uppercase 16-digit hex, as emitted into every Id128 literal."""
    return "%016X" % value


def escape_string_literal(text: str) -> str:
    """`CatalogEmitter.EscapeStringLiteral`."""
    out = []
    for c in text:
        if c == "\\":
            out.append("\\\\")
        elif c == '"':
            out.append('\\"')
        elif c == "\n":
            out.append("\\n")
        elif c == "\r":
            out.append("\\r")
        elif c == "\t":
            out.append("\\t")
        elif ord(c) < 0x20 or ord(c) > 0x7E:
            out.append("\\u%04x" % ord(c))
        else:
            out.append(c)
    return "".join(out)


def parameter_name(field_name: str) -> str:
    """`CatalogEmitter.ParameterName`: a lower-case first letter, so a description declares a name once."""
    if not field_name:
        return "value"
    first = field_name[0]
    if "A" <= first <= "Z":
        return chr(ord(first) + 32) + field_name[1:]
    return field_name


def identity_hex(text: str, where: str, allow_all_zero: bool = False) -> tuple[int, int]:
    """`CatalogEmitter.ParseIdentity`; an owner package id may be all-zero, a schema or feature id may not."""
    if not IDENTITY_HEX.match(text) or (not allow_all_zero and text == "0" * 32):
        raise DescriptionMismatch("%s: '%s' is not a usable 32-character lowercase identity" % (where, text))
    return int(text[:16], 16), int(text[16:], 16)


# ------------------------------------------------------------------------------------------------------------
# Description validation and canonical ordering.
# ------------------------------------------------------------------------------------------------------------


def validate(description: dict) -> dict:
    """Reads the description into the canonical model the emitter would see, rejecting anything else."""
    if not isinstance(description, dict):
        raise DescriptionMismatch("the document root must be an object")

    unknown = [key for key in description if key not in KNOWN_TOP_LEVEL]
    if unknown:
        raise DescriptionMismatch("unknown member(s) " + ", ".join(sorted(unknown)))

    if description.get("descriptionFormat") != DESCRIPTION_FORMAT:
        raise DescriptionMismatch(
            "descriptionFormat is %r but this mirror emits only %r" % (description.get("descriptionFormat"), DESCRIPTION_FORMAT)
        )

    protocol = description.get("protocolVersion")
    if protocol != "1.0":
        raise DescriptionMismatch("protocolVersion is %r but this mirror emits only '1.0'" % (protocol,))
    major, minor = protocol.split(".")

    generated_namespace = description.get("namespace")
    class_name = description.get("className")
    file_name = description.get("fileName")
    if not isinstance(generated_namespace, str) or not generated_namespace:
        raise DescriptionMismatch("namespace must be a non-empty string")
    if not isinstance(class_name, str) or not IDENTIFIER.match(class_name):
        raise DescriptionMismatch("className must be a C# identifier")
    if not isinstance(file_name, str) or not file_name.endswith(".cs"):
        raise DescriptionMismatch("fileName must be a bare file name ending in '.cs'")

    code = description.get("code")
    if code not in (None, {}):
        raise DescriptionMismatch(
            "this mirror emits a catalog with no code directives; the description declares a 'code' section"
        )

    features_raw = description.get("supportedFeatureIds")
    if not isinstance(features_raw, list) or not features_raw:
        raise DescriptionMismatch("supportedFeatureIds must be a non-empty array")
    features = []
    for i, text in enumerate(features_raw):
        features.append(identity_hex(text, "supportedFeatureIds[%d]" % i))
    features.sort()

    groups = description.get("groups")
    if groups is None:
        groups = []
    if not isinstance(groups, list):
        raise DescriptionMismatch("groups must be an array")
    if groups:
        raise DescriptionMismatch(
            "this mirror emits a catalog with zero registration groups; the description declares %d" % len(groups)
        )

    schemas_raw = description.get("schemas")
    if not isinstance(schemas_raw, list) or not schemas_raw:
        raise DescriptionMismatch("schemas must be a non-empty array")

    schemas = []
    seen_ids = set()
    seen_keys = set()
    for index, raw in enumerate(schemas_raw):
        where = "schemas[%d]" % index
        if not isinstance(raw, dict):
            raise DescriptionMismatch("%s: a schema declaration must be an object" % where)
        extra = [key for key in raw if key not in KNOWN_SCHEMA]
        if extra:
            raise DescriptionMismatch("%s: unknown member(s) %s" % (where, ", ".join(sorted(extra))))
        missing = [key for key in KNOWN_SCHEMA if key not in raw]
        if missing:
            raise DescriptionMismatch("%s: missing member(s) %s" % (where, ", ".join(missing)))

        for member in ("valueTypeName", "serializerTypeName", "serializerKeyName"):
            if not isinstance(raw[member], str) or not IDENTIFIER.match(raw[member]):
                raise DescriptionMismatch("%s.%s: '%s' is not a C# identifier" % (where, member, raw[member]))

        schema_id = identity_hex(raw["schemaId"], where + ".schemaId")
        owner = identity_hex(raw["ownerPackageId"], where + ".ownerPackageId", allow_all_zero=True)
        stable_name = raw["stableName"]
        if not isinstance(stable_name, str) or not stable_name:
            raise DescriptionMismatch("%s.stableName must be a non-empty string" % where)

        for member in ("schemaVersion", "serializerKeyVersion"):
            if not isinstance(raw[member], int) or raw[member] < 0:
                raise DescriptionMismatch("%s.%s must be a non-negative integer" % (where, member))
        if not isinstance(raw["required"], bool):
            raise DescriptionMismatch("%s.required must be a boolean" % where)

        if schema_id in seen_ids:
            raise DescriptionMismatch("%s: schema id %s is declared twice" % (where, raw["schemaId"]))
        seen_ids.add(schema_id)
        if stable_name in seen_keys:
            raise DescriptionMismatch("%s: stable name '%s' is declared twice" % (where, stable_name))
        seen_keys.add(stable_name)

        fields_raw = raw["fields"]
        if not isinstance(fields_raw, list) or not fields_raw:
            raise DescriptionMismatch("%s.fields must be a non-empty array" % where)
        fields = []
        seen_field_ids = set()
        seen_field_names = set()
        for field_index, field in enumerate(fields_raw):
            field_where = "%s.fields[%d]" % (where, field_index)
            if not isinstance(field, dict):
                raise DescriptionMismatch("%s: a field declaration must be an object" % field_where)
            extra = [key for key in field if key not in KNOWN_FIELD]
            if extra:
                raise DescriptionMismatch("%s: unknown member(s) %s" % (field_where, ", ".join(sorted(extra))))
            missing = [key for key in KNOWN_FIELD if key not in field]
            if missing:
                raise DescriptionMismatch("%s: missing member(s) %s" % (field_where, ", ".join(missing)))

            field_id = field["id"]
            if not isinstance(field_id, int) or field_id <= 0:
                raise DescriptionMismatch("%s.id: %r is not a positive field id" % (field_where, field_id))
            if field_id in seen_field_ids:
                raise DescriptionMismatch("%s.id: field id %d is declared twice" % (field_where, field_id))
            seen_field_ids.add(field_id)
            name = field["name"]
            if not isinstance(name, str) or not IDENTIFIER.match(name):
                raise DescriptionMismatch("%s.name: '%s' is not a C# identifier" % (field_where, name))
            if name in seen_field_names:
                raise DescriptionMismatch("%s.name: field name '%s' is declared twice" % (field_where, name))
            seen_field_names.add(name)
            wire = field["wireType"]
            if wire not in WIRE_TYPES:
                raise DescriptionMismatch(
                    "%s.wireType: unsupported wire type '%s'; supported types are %s"
                    % (field_where, wire, ", ".join(sorted(WIRE_TYPES)))
                )
            if not isinstance(field["required"], bool):
                raise DescriptionMismatch("%s.required must be a boolean" % field_where)
            fields.append({"id": field_id, "name": name, "wireType": wire, "required": field["required"]})

        if [f["id"] for f in fields] != sorted(f["id"] for f in fields):
            raise DescriptionMismatch("%s.fields: field ids must ascend; the description is not canonical" % where)

        schemas.append(
            {
                "stableName": stable_name,
                "valueTypeName": raw["valueTypeName"],
                "serializerTypeName": raw["serializerTypeName"],
                "serializerKeyName": raw["serializerKeyName"],
                "schemaIdHex": raw["schemaId"],
                "schemaId": schema_id,
                "schemaVersion": raw["schemaVersion"],
                "serializerKeyVersion": raw["serializerKeyVersion"],
                "ownerPackageIdHex": raw["ownerPackageId"],
                "ownerPackageId": owner,
                "required": raw["required"],
                "fields": fields,
            }
        )

    # Canonical emission order: schemas by ascending schema identity, never declaration order (P-008).
    schemas.sort(key=lambda schema: schema["schemaId"])

    return {
        "descriptionFormat": description["descriptionFormat"],
        "protocolMajor": major,
        "protocolMinor": minor,
        "namespace": generated_namespace,
        "className": class_name,
        "fileName": file_name,
        "features": features,
        "schemas": schemas,
        "groupCount": len(groups),
    }


# ------------------------------------------------------------------------------------------------------------
# Fingerprint, exactly as `CatalogEmitter.ComputeFingerprint` + `CatalogFingerprint.Compute` build it.
# ------------------------------------------------------------------------------------------------------------


def pack_factory(
    key: tuple[int, int, int],
    kind: int,
    owner: tuple[int, int],
    implementation: tuple[int, int],
    contract: int,
) -> bytes:
    return (
        key[0].to_bytes(8, "big")
        + key[1].to_bytes(8, "big")
        + key[2].to_bytes(4, "big")
        + kind.to_bytes(4, "big")
        + owner[0].to_bytes(8, "big")
        + owner[1].to_bytes(8, "big")
        + implementation[0].to_bytes(8, "big")
        + implementation[1].to_bytes(8, "big")
        + contract.to_bytes(4, "big")
    )


def compute_fingerprint(model: dict) -> str:
    """With zero groups the factory table holds only the one Serializer registration per schema."""
    factories = []
    schemas = []

    for schema in model["schemas"]:
        key = derive_key(schema["stableName"])
        serializer_key = (key[0], key[1], schema["serializerKeyVersion"])
        factories.append(
            (
                serializer_key,
                SERIALIZER_KIND,
                schema["ownerPackageId"],
                schema["schemaId"],
                schema["schemaVersion"],
            )
        )
        schemas.append(
            (
                schema["schemaId"],
                schema["schemaVersion"],
                schema["ownerPackageId"],
                serializer_key,
                schema["required"],
            )
        )

    factories.sort(key=lambda entry: (entry[0][0], entry[0][1], entry[0][2]))
    schemas.sort(key=lambda entry: entry[0])
    features = sorted(model["features"])

    buffer = bytearray()
    for key, kind, owner, implementation, contract in factories:
        buffer += pack_factory(key, kind, owner, implementation, contract)
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

    return sha256_hex_bytes(bytes(buffer))


def sha256_hex_bytes(payload: bytes) -> str:
    return hashlib.sha256(payload).hexdigest()


# ------------------------------------------------------------------------------------------------------------
# The emitter template, section by section, mirroring `CatalogEmitter.EmitBody` and its helpers.
# ------------------------------------------------------------------------------------------------------------


def emit_body(model: dict, fingerprint: str) -> str:
    out: list[str] = []

    out.append("// <auto-generated />\n")
    out.append("// Generated by GameCore.Content.Compiler.CatalogEmitter; do not edit by hand.\n")
    out.append("// Description format: %s\n" % model["descriptionFormat"])
    out.append("// Protocol: %s.%s\n" % (model["protocolMajor"], model["protocolMinor"]))
    out.append("// Deterministic output: fixed declaration order, canonical key order inside every table,\n")
    out.append("// no timestamps, no machine paths, no culture-sensitive formatting, LF line endings.\n")
    out.append("// Regenerating from the same validated description must reproduce this file byte for byte.\n")
    out.append("// Regenerate with the GameCore content compiler (menu GameCore/Content/Generate Catalog or\n")
    out.append("// -executeMethod GameCore.Content.Compiler.Editor.CatalogGeneratorMenu.GenerateFromCommandLine).\n")
    out.append("\n")
    out.append("#nullable enable\n")
    out.append("using System;\n")
    out.append("using System.Collections.Generic;\n")
    out.append("using GameCore.Contracts;\n")
    out.append("\n")  # no declared using directives
    # no assembly attributes
    out.append("namespace %s\n" % model["namespace"])
    out.append("{\n")
    out.append("    /// <summary>\n")
    out.append("    /// Generated closed registration catalog. Every registration is a direct reference, so managed\n")
    out.append("    /// stripping cannot remove a registration the player resolves by key at runtime (04 section 8).\n")
    out.append("    /// </summary>\n")
    out.append("    public static class %s\n" % model["className"])
    out.append("    {\n")

    emit_constants(out, model, fingerprint)
    emit_keys(out, model)
    emit_tables(out, model)
    emit_lookups(out, model)
    emit_schema_types(out, model)
    emit_catalog_factory(out, model)
    emit_code_directives(out, model)

    out.append("        /// <summary>Hash of the generated source that precedes this declaration.</summary>\n")
    out.append(HASH_DECLARATION + '"%s";\n' % HASH_PLACEHOLDER)
    out.append("    }\n")
    out.append("}\n")
    return "".join(out)


def emit_constants(out: list[str], model: dict, fingerprint: str) -> None:
    out.append("        /// <summary>File name this catalog was generated into.</summary>\n")
    out.append('        public const string GeneratedFileName = "%s";\n' % escape_string_literal(model["fileName"]))
    out.append("\n")
    out.append("        /// <summary>Description format this catalog was generated from.</summary>\n")
    out.append('        public const string DescriptionFormat = "%s";\n' % escape_string_literal(model["descriptionFormat"]))
    out.append("\n")
    out.append("        /// <summary>Declared protocol version this catalog was generated for.</summary>\n")
    out.append('        public const string ProtocolVersion = "%s.%s";\n' % (model["protocolMajor"], model["protocolMinor"]))
    out.append("\n")
    out.append("        /// <summary>Hash algorithm used by CatalogFileHash.</summary>\n")
    out.append('        public const string HashAlgorithm = "SHA-256";\n')
    out.append("\n")
    out.append("        /// <summary>Exact scope of CatalogFileHash, so the recorded value can be reproduced independently.</summary>\n")
    out.append('        public const string CatalogFileHashScope = "%s";\n' % escape_string_literal(CATALOG_FILE_HASH_SCOPE))
    out.append("\n")
    out.append("        /// <summary>Scope of CatalogFingerprint, identical to GameCore.Contracts.CatalogFingerprint.Scope.</summary>\n")
    out.append('        public const string CatalogFingerprintScope = "%s";\n' % escape_string_literal(CATALOG_FINGERPRINT_SCOPE))
    out.append("\n")
    out.append("        /// <summary>Canonical fingerprint of the registrations below (P-028, P-053).</summary>\n")
    out.append('        public const string CatalogFingerprint = "%s";\n' % fingerprint)
    out.append("\n")
    out.append("        /// <summary>Supported protocol feature ids, in canonical identity order (P-055).</summary>\n")
    out.append("        public static readonly Id128[] SupportedFeatureIds =\n")
    out.append("        {\n")
    for high, low in model["features"]:
        out.append("            new Id128(0x%sUL, 0x%sUL),\n" % (hex16(high), hex16(low)))
    out.append("        };\n")
    out.append("\n")


def emit_keys(out: list[str], model: dict) -> None:
    for schema in model["schemas"]:
        key = derive_key(schema["stableName"])
        out.append(
            "        /// <summary>Generated serializer key of schema %s ('%s').</summary>\n"
            % (schema["schemaIdHex"], escape_string_literal(schema["stableName"]))
        )
        out.append(
            "        public static readonly FactoryKey %s = new FactoryKey(new Id128(0x%sUL, 0x%sUL), %dU);\n"
            % (schema["serializerKeyName"], hex16(key[0]), hex16(key[1]), schema["serializerKeyVersion"])
        )
        out.append("\n")


def emit_tables(out: list[str], model: dict) -> None:
    # Zero registration groups, so no group tables and no group keys are emitted.
    out.append("        /// <summary>Validated schema registrations, in canonical schema-id order.</summary>\n")
    out.append("        public static readonly SchemaRegistration[] SchemaRegistrations =\n")
    out.append("        {\n")
    for schema in model["schemas"]:
        schema_id = schema["schemaId"]
        owner = schema["ownerPackageId"]
        out.append("            new SchemaRegistration(\n")
        out.append(
            "                new SchemaRef(new SchemaId(new Id128(0x%sUL, 0x%sUL)), %dU),\n"
            % (hex16(schema_id[0]), hex16(schema_id[1]), schema["schemaVersion"])
        )
        out.append("                new Id128(0x%sUL, 0x%sUL),\n" % (hex16(owner[0]), hex16(owner[1])))
        out.append("                %s,\n" % schema["serializerKeyName"])
        out.append("                %s),\n" % ("true" if schema["required"] else "false"))
    out.append("        };\n")
    out.append("\n")


def emit_lookups(out: list[str], model: dict) -> None:
    # One lookup method per registration group; a schemas-only catalog declares none.
    return


def emit_schema_types(out: list[str], model: dict) -> None:
    for schema in model["schemas"]:
        emit_value_type(out, schema)
        emit_serializer(out, schema)

    out.append("        /// <summary>\n")
    out.append("        /// Generated serializer instances, one per declared schema, in the same canonical order as\n")
    out.append("        /// SchemaRegistrations. Instances are constructed directly; no serializer is discovered at runtime.\n")
    out.append("        /// </summary>\n")
    out.append("        public static readonly ISchemaSerializer[] Serializers =\n")
    out.append("        {\n")
    for schema in model["schemas"]:
        out.append("            new %s(),\n" % schema["serializerTypeName"])
    out.append("        };\n")
    out.append("\n")


def emit_value_type(out: list[str], schema: dict) -> None:
    out.append("        /// <summary>\n")
    out.append(
        "        /// Generated value of schema %s ('%s').\n"
        % (schema["schemaIdHex"], escape_string_literal(schema["stableName"]))
    )
    out.append("        /// Fields are declared in ascending field-id order, which is also the wire order (05 s6).\n")
    out.append("        /// </summary>\n")
    out.append("        public readonly struct %s\n" % schema["valueTypeName"])
    out.append("        {\n")

    for field in schema["fields"]:
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        out.append(
            "            /// <summary>Field id %d, wire type %s, %s</summary>\n"
            % (field["id"], field["wireType"], "required." if field["required"] else "optional.")
        )
        out.append(
            "            public readonly %s%s %s;\n" % (csharp, "?" if nullable else "", field["name"])
        )

    out.append("\n")
    parameters = ", ".join(
        "%s%s %s" % (WIRE_TYPES[f["wireType"]][0], "?" if WIRE_TYPES[f["wireType"]][1] else "", parameter_name(f["name"]))
        for f in schema["fields"]
    )
    out.append("            public %s(%s)\n" % (schema["valueTypeName"], parameters))
    out.append("            {\n")
    for field in schema["fields"]:
        out.append("                %s = %s;\n" % (field["name"], parameter_name(field["name"])))
    out.append("            }\n")
    out.append("\n")

    out.append("            /// <summary>Diagnostic form; never used as an identity (P-004).</summary>\n")
    out.append("            public override string ToString()\n")
    out.append("            {\n")
    text = '                return "%s(' % escape_string_literal(schema["valueTypeName"])
    for index, field in enumerate(schema["fields"]):
        if index != 0:
            text += ", "
        text += escape_string_literal(field["name"] + "=")
        text += '" + '
        if WIRE_TYPES[field["wireType"]][1]:
            text += '(%s ?? "<null>")' % field["name"]
        else:
            text += field["name"]
        text += ' + "'
    text += ')";\n'
    out.append(text)
    out.append("            }\n")
    out.append("        }\n")
    out.append("\n")


def emit_serializer(out: list[str], schema: dict) -> None:
    schema_id = schema["schemaId"]

    out.append("        /// <summary>\n")
    out.append(
        "        /// Generated serializer of schema %s version %d. Canonical envelope format only: no reflection,\n"
        % (schema["schemaIdHex"], schema["schemaVersion"])
    )
    out.append("        /// no dynamic type construction (05 s6, P-054).\n")
    out.append("        /// </summary>\n")
    out.append("        public sealed class %s : GeneratedSerializerBase\n" % schema["serializerTypeName"])
    out.append("        {\n")
    out.append("            private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =\n")
    out.append("            {\n")
    for field in schema["fields"]:
        out.append(
            "                new GeneratedFieldSlot(%d, WireType.%s, %s),\n"
            % (field["id"], field["wireType"], "true" if field["required"] else "false")
        )
    out.append("            };\n")
    out.append("\n")

    out.append("            public %s()\n" % schema["serializerTypeName"])
    out.append("                : base(\n")
    out.append("                    %s,\n" % schema["serializerKeyName"])
    out.append(
        "                    new SchemaRef(new SchemaId(new Id128(0x%sUL, 0x%sUL)), %dU),\n"
        % (hex16(schema_id[0]), hex16(schema_id[1]), schema["schemaVersion"])
    )
    out.append("                    SupportedFeatureIds)\n")
    out.append("            {\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            /// <summary>Declared fields in ascending field-id order.</summary>\n")
    out.append("            protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;\n")
    out.append("\n")

    out.append("            /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>\n")
    out.append("            public byte[] Serialize(%s value)\n" % schema["valueTypeName"])
    out.append("            {\n")
    out.append("                EnvelopeWriter writer = CreateWriter();\n")
    for field in schema["fields"]:
        out.append(
            "                writer.%s(%d, value.%s);\n"
            % (WIRE_TYPES[field["wireType"]][2], field["id"], field["name"])
        )
    out.append("                writer.WriteChecksum();\n")
    out.append("                return writer.ToArray();\n")
    out.append("            }\n")
    out.append("\n")

    out.append("            /// <summary>\n")
    out.append("            /// Validates one document against this schema and decodes its declared fields. A false result\n")
    out.append("            /// reports the exact envelope error and leaves the value at its default.\n")
    out.append("            /// </summary>\n")
    out.append(
        "            public bool TryDeserialize(byte[] document, out %s value, out EnvelopeError error)\n"
        % schema["valueTypeName"]
    )
    out.append("            {\n")
    out.append("                value = default(%s);\n" % schema["valueTypeName"])
    out.append("                GeneratedFieldBuffer buffer = new GeneratedFieldBuffer();\n")
    out.append("                if (!TryReadDeclaredFields(document, buffer, out error))\n")
    out.append("                {\n")
    out.append("                    return false;\n")
    out.append("                }\n")
    out.append("\n")
    out.append("                EnvelopeReader reader = new EnvelopeReader(document);\n")
    for index, field in enumerate(schema["fields"]):
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        marker = "?" if nullable else ""
        out.append(
            "                %s%s value%d = default(%s%s);\n" % (csharp, marker, index, csharp, marker)
        )

    out.append("                for (int i = 0; i < buffer.Count; i++)\n")
    out.append("                {\n")
    out.append("                    EnvelopeField recorded = buffer.Field(i);\n")
    out.append("                    if (!reader.TrySeekTo(buffer.RecordOffset(i)))\n")
    out.append("                    {\n")
    out.append("                        error = reader.LastError;\n")
    out.append("                        return false;\n")
    out.append("                    }\n")
    out.append("\n")
    out.append("                    switch (recorded.FieldId)\n")
    out.append("                    {\n")
    for index, field in enumerate(schema["fields"]):
        emit_field_case(out, field, index)
    out.append("                        default:\n")
    out.append("                            break;\n")
    out.append("                    }\n")
    out.append("                }\n")
    out.append("\n")
    out.append("                // A declared field the document omitted keeps its default, and every declared field\n")
    out.append("                // is assigned exactly once, so the generated value type stays immutable (05 s6, P-054).\n")
    out.append(
        "                value = new %s(%s);\n"
        % (schema["valueTypeName"], ", ".join("value%d" % i for i in range(len(schema["fields"]))))
    )
    out.append("                error = EnvelopeError.None;\n")
    out.append("                return true;\n")
    out.append("            }\n")
    out.append("        }\n")
    out.append("\n")


def emit_field_case(out: list[str], field: dict, index: int) -> None:
    csharp, nullable, _, read_method = WIRE_TYPES[field["wireType"]]
    marker = "?" if nullable else ""
    field_var = "field%d" % index
    value_var = field_var + "Value"
    bits_var = field_var + "Bits"
    indent = "                        "

    out.append("%scase %d:\n" % (indent, field["id"]))
    out.append(indent + "{\n")
    out.append("%s    if (!reader.TryReadField(out EnvelopeField %s))\n" % (indent, field_var))
    out.append(indent + "    {\n")
    out.append(indent + "        error = reader.LastError;\n")
    out.append(indent + "        return false;\n")
    out.append(indent + "    }\n")
    out.append("\n")

    if field["wireType"] == "Float32":
        out.append("%s    uint %s;\n" % (indent, bits_var))
    elif field["wireType"] == "Float64":
        out.append("%s    ulong %s;\n" % (indent, bits_var))

    out.append("%s    %s%s %s;\n" % (indent, csharp, marker, value_var))
    call = "%s    if (!reader.%s(%s, out %s" % (indent, read_method, field_var, value_var)
    if field["wireType"] in ("Float32", "Float64"):
        call += ", out " + bits_var
    out.append(call + "))\n")
    out.append(indent + "    {\n")
    out.append(indent + "        error = reader.LastError;\n")
    out.append(indent + "        return false;\n")
    out.append(indent + "    }\n")
    out.append("\n")
    out.append("%s    value%d = %s;\n" % (indent, index, value_var))
    out.append(indent + "    break;\n")
    out.append(indent + "}\n")


def emit_catalog_factory(out: list[str], model: dict) -> None:
    out.append("        /// <summary>\n")
    out.append("        /// Validates every generated table under the production catalog rules and returns the immutable\n")
    out.append("        /// catalog, or the exact structured rejections (P-009). Nothing is exposed on rejection.\n")
    out.append("        /// </summary>\n")
    out.append("        public static CatalogBuildResult BuildCatalog()\n")
    out.append("        {\n")
    out.append("            FactoryRegistration[] groupRegistrations = GroupCatalogRegistrations();\n")
    out.append("            FactoryRegistration[] all = new FactoryRegistration[groupRegistrations.Length + SchemaRegistrations.Length];\n")
    out.append("            Array.Copy(groupRegistrations, 0, all, 0, groupRegistrations.Length);\n")
    out.append("\n")
    out.append("            for (int i = 0; i < SchemaRegistrations.Length; i++)\n")
    out.append("            {\n")
    out.append("                SchemaRegistration registration = SchemaRegistrations[i];\n")
    out.append("                all[groupRegistrations.Length + i] = new FactoryRegistration(\n")
    out.append("                    registration.Serializer,\n")
    out.append("                    FactoryKind.Serializer,\n")
    out.append("                    registration.OwnerPackageId,\n")
    out.append("                    registration.Schema.Id.Value,\n")
    out.append("                    registration.Schema.Version);\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            return ImmutableCatalog.Build(all, SchemaRegistrations, SupportedFeatureIds, Serializers);\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>Every group's catalog registrations concatenated in declaration order.</summary>\n")
    out.append("        private static FactoryRegistration[] GroupCatalogRegistrations()\n")
    out.append("        {\n")
    out.append("            return Array.Empty<FactoryRegistration>();\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>\n")
    out.append("        /// Builds the catalog and asserts that its fingerprint equals the value emitted into this file, so a\n")
    out.append("        /// stale generated file cannot describe a catalog whose declarations changed (P-028).\n")
    out.append("        /// </summary>\n")
    out.append("        public static CatalogBuildResult BuildVerifiedCatalog(out ContentHash observedFingerprint)\n")
    out.append("        {\n")
    out.append("            CatalogBuildResult result = BuildCatalog();\n")
    out.append("            observedFingerprint = result.Catalog == null ? ContentHash.Empty : result.Catalog.Fingerprint;\n")
    out.append("            return result;\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>\n")
    out.append("        /// Fingerprint the generated declarations must produce. A test compares this literal against a\n")
    out.append("        /// freshly built catalog so an unnoticed declaration change fails instead of being ignored.\n")
    out.append("        /// </summary>\n")
    out.append("        public static bool FingerprintMatchesGeneratedCatalog(out ContentHash observed)\n")
    out.append("        {\n")
    out.append("            CatalogBuildResult result = BuildVerifiedCatalog(out observed);\n")
    out.append("            return result.Catalog != null && string.Equals(observed.ToHex(), CatalogFingerprint, StringComparison.Ordinal);\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>True because this catalog emits closed generic roots.</summary>\n")
    out.append("        public const bool HasClosedGenericRoots = false;\n")
    out.append("\n")


def emit_code_directives(out: list[str], model: dict) -> None:
    out.append("        /// <summary>Number of generated registration groups.</summary>\n")
    out.append("        public const int RegistrationGroupCount = %d;\n" % model["groupCount"])
    out.append("\n")
    out.append("        /// <summary>Number of declared schemas.</summary>\n")
    out.append("        public const int SchemaCount = %d;\n" % len(model["schemas"]))
    out.append("\n")


def emit(model: dict) -> tuple[str, str]:
    """Returns the complete file text and the recorded `CatalogFileHash`."""
    fingerprint = compute_fingerprint(model)
    body = emit_body(model, fingerprint)
    marker = body.find(HASH_DECLARATION)
    if marker < 0:
        raise DescriptionMismatch("the emitter template lost its hash declaration line")
    file_hash = sha256_hex(body[:marker])
    return body.replace(HASH_PLACEHOLDER, file_hash), file_hash


# ------------------------------------------------------------------------------------------------------------
# Self-checks against the emitted text, so a mis-emitted block cannot pass unnoticed.
# ------------------------------------------------------------------------------------------------------------


def recompute_from_text(text: str) -> tuple[list[str], str, str]:
    """Re-derives the file hash and fingerprint from the generated text's own declarations."""
    marker = text.find(HASH_DECLARATION)
    if marker < 0:
        raise DescriptionMismatch("the generated text has no CatalogFileHash declaration")
    file_hash = sha256_hex(text[:marker])

    rows = re.findall(
        r"new SchemaRegistration\(\s*new SchemaRef\(new SchemaId\(new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\)\), (\d+)U\),"
        r"\s*new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\),\s*(\w+),\s*(true|false)\)",
        text,
        re.S,
    )
    factories = []
    schemas = []
    for sh, sl, version, oh, ol, key_name, required in rows:
        match = re.search(
            re.escape(key_name) + r" = new FactoryKey\(new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\), (\d+)U\)",
            text,
        )
        if match is None:
            raise DescriptionMismatch("the generated text has no FactoryKey named " + key_name)
        key = (int(match.group(1), 16), int(match.group(2), 16), int(match.group(3)))
        schema_id = (int(sh, 16), int(sl, 16))
        owner = (int(oh, 16), int(ol, 16))
        factories.append((key, SERIALIZER_KIND, owner, schema_id, int(version)))
        schemas.append((schema_id, int(version), owner, key, required == "true"))

    features = sorted(
        (int(h, 16), int(l, 16))
        for h, l in re.findall(
            r"new Id128\(0x([0-9A-F]{16})UL, 0x([0-9A-F]{16})UL\)",
            array_body(text, "SupportedFeatureIds"),
        )
    )

    factories.sort(key=lambda entry: (entry[0][0], entry[0][1], entry[0][2]))
    schemas.sort(key=lambda entry: entry[0])

    buffer = bytearray()
    for key, kind, owner, implementation, contract in factories:
        buffer += pack_factory(key, kind, owner, implementation, contract)
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

    return [key_name for _, _, _, _, _, key_name, _ in rows], file_hash, sha256_hex_bytes(bytes(buffer))


def array_body(text: str, name: str) -> str:
    """The initializer body of `public static readonly ... name = { ... };` at eight-space indentation."""
    lines = text.split("\n")
    for index, line in enumerate(lines):
        if not line.startswith("        public static readonly ") or name + " =" not in line:
            continue
        if index + 1 >= len(lines) or lines[index + 1] != "        {":
            continue
        body = []
        for candidate in lines[index + 2:]:
            if candidate == "        };":
                return "\n".join(body)
            body.append(candidate)
    raise DescriptionMismatch("the generated text has no array table named " + name)


def string_constant(text: str, name: str) -> str | None:
    match = re.search(r'public const string ' + re.escape(name) + r' = "([^"]*)"', text)
    return match.group(1) if match else None


# ------------------------------------------------------------------------------------------------------------
# Entry point.
# ------------------------------------------------------------------------------------------------------------


def main() -> int:
    root = pathlib.Path(__file__).resolve().parents[1]
    description_path = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / DEFAULT_DESCRIPTION
    output_path = pathlib.Path(sys.argv[2]) if len(sys.argv) > 2 else root / DEFAULT_OUTPUT
    if not description_path.is_absolute():
        description_path = (pathlib.Path.cwd() / description_path).resolve()
    if not output_path.is_absolute():
        output_path = (pathlib.Path.cwd() / output_path).resolve()

    if not description_path.exists():
        print("no catalog description at " + str(description_path))
        return 2

    try:
        description = json.loads(description_path.read_text(encoding="utf-8"))
    except ValueError as error:
        print("the description is not valid JSON: " + str(error))
        return 2

    try:
        model = validate(description)
        code, file_hash = emit(model)
    except DescriptionMismatch as error:
        print("the description cannot be emitted: " + str(error))
        return 1

    fingerprint = string_constant(code, "CatalogFingerprint")
    if fingerprint is None:
        print("the emitted text records no CatalogFingerprint")
        return 1

    # 1. Self-check: recompute both hash literals from the emitted text's own declarations.
    try:
        key_names, recomputed_file_hash, recomputed_fingerprint = recompute_from_text(code)
    except DescriptionMismatch as error:
        print("the emitted text is inconsistent: " + str(error))
        return 1

    problems = []
    if recomputed_file_hash != file_hash:
        problems.append("CatalogFileHash: emitted %s, recomputed %s" % (file_hash, recomputed_file_hash))
    if recomputed_fingerprint != fingerprint:
        problems.append("CatalogFingerprint: emitted %s, recomputed %s" % (fingerprint, recomputed_fingerprint))
    if len(key_names) != len(model["schemas"]):
        problems.append(
            "emitted %d schema registration row(s) for %d declared schema(s)" % (len(key_names), len(model["schemas"]))
        )
    expected_keys = [s["serializerKeyName"] for s in model["schemas"]]
    if key_names != expected_keys:
        problems.append("SchemaRegistrations key order %s does not match the canonical schema order %s" % (key_names, expected_keys))
    declared_groups = re.search(r"RegistrationGroupCount = (\d+);", code)
    if declared_groups is None or int(declared_groups.group(1)) != model["groupCount"]:
        problems.append("RegistrationGroupCount does not match the %d declared group(s)" % model["groupCount"])
    declared_schemas = re.search(r"SchemaCount = (\d+);", code)
    if declared_schemas is None or int(declared_schemas.group(1)) != len(model["schemas"]):
        problems.append("SchemaCount does not match the %d declared schema(s)" % len(model["schemas"]))

    if problems:
        print("the description and the emitted catalog disagree:")
        for problem in problems:
            print("  - " + problem)
        return 1

    payload = code.encode("utf-8")
    existed = output_path.exists()
    unchanged = existed and output_path.read_bytes() == payload
    if not unchanged:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_bytes(payload)

    print(
        "%s %s; bytes=%d; catalogFingerprint=%s; catalogFileHash=%s"
        % ("unchanged" if unchanged else "regenerated" if existed else "created", output_path, len(payload), fingerprint, file_hash)
    )

    # 2. Independent gate: the committed verifier recomputes both values from the file's own text.
    verifier = root / "tools/verify_generated_catalog.py"
    if verifier.exists():
        completed = subprocess.run(
            [sys.executable, str(verifier), str(output_path)],
            capture_output=True,
            text=True,
        )
        sys.stdout.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        if completed.returncode != 0:
            print("independent verification failed with exit code %d" % completed.returncode)
            return completed.returncode

    return 0


if __name__ == "__main__":
    sys.exit(main())
