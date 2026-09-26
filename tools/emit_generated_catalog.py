#!/usr/bin/env python3
"""Emit a generated GameCore catalog from its description document (no-SDK host helper).

`GameCore.Content.Compiler.CatalogEmitter` is the authoritative emitter: the Editor bridge calls it on the
qualification build host, and the committed generated files must reproduce byte for byte from their descriptions.
This host has no .NET SDK and no Unity, so this script is a faithful mirror of the emitter's own template, used to
produce and re-check committed catalogs here. It is subordinate to the compiler, not a second emitter: `--self-check`
regenerates every committed catalog from its committed description and fails on a single byte of drift, and the
build host's codegen step regenerates the same files with the production emitter.

Mirrored from `CatalogEmitter.cs` (`Emit` for the catalog, `EmitCoverage` for its coverage companion),
`CatalogDescriptionReader.cs` (validation) and `GameCore.Contracts.CatalogFingerprint` (fingerprint scope), all read
from this repository:

  * section order, indentation, doc comments and literal formats of the emitted file;
  * canonical ordering (groups by ordinal table name, schemas by ascending schema id, fields by ascending id,
    entries by derived key then key version);
  * `CatalogFileHash` = SHA-256 over the UTF-8 file prefix that ends immediately before the hash declaration;
  * `CatalogFingerprint` = SHA-256 over the canonical tables exactly as `CatalogFingerprint.Compute` builds them.

usage:
  python3 tools/emit_generated_catalog.py <description.json> <output.g.cs>
  python3 tools/emit_generated_catalog.py --check <description.json> <committed.g.cs>
  python3 tools/emit_generated_catalog.py --self-check
"""

from __future__ import annotations

import hashlib
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]

HASH_DECLARATION = "        public const string CatalogFileHash = "
HASH_PLACEHOLDER = "@@CATALOG_FILE_HASH@@"

DESCRIPTION_FORMAT = "gamecore.catalog-description/1"

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

# `CatalogFactoryKinds.Names`, in `FactoryKind` declaration order, with the enum value each name carries.
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

# `CatalogEmitter.ClosedGenericRootMethodName` and the names the emitter reserves inside the generated class.
CLOSED_GENERIC_ROOT_METHOD = "RootClosedGenericInstantiations"
RESERVED_MEMBERS = (
    "GeneratedFileName", "DescriptionFormat", "ProtocolVersion", "HashAlgorithm",
    "CatalogFileHash", "CatalogFileHashScope", "CatalogFingerprint", "CatalogFingerprintScope",
    "SupportedFeatureIds", "SchemaRegistrations", "Serializers", "BuildCatalog",
    "GroupCatalogRegistrations", "BuildVerifiedCatalog", "FingerprintMatchesGeneratedCatalog",
    "HasClosedGenericRoots", "RegistrationGroupCount", "SchemaCount",
    CLOSED_GENERIC_ROOT_METHOD,
)

CATALOG_FILE_HASH_SCOPE = (
    "SHA-256 over the UTF-8 bytes, with LF line endings, of the file prefix that ends immediately "
    "before the eight-space-indented CatalogFileHash declaration; that prefix ends with the "
    "newline that terminates the previous line, and the declaration itself, its value and the "
    "rest of the file are excluded"
)

# `GameCore.Contracts.CatalogFingerprint.Scope`, copied verbatim.
CATALOG_FINGERPRINT_SCOPE = (
    "SHA-256 over, in this fixed order: (1) every registered factory key in canonical ascending order "
    "as 16-byte big-endian id, 4-byte big-endian key version, 4-byte big-endian factory kind, "
    "16-byte big-endian owner package id, 16-byte big-endian implementation id, 4-byte big-endian "
    "contract version; (2) every accepted schema in ascending schema-id order as 16-byte big-endian "
    "id, 4-byte big-endian schema version, one byte 1 when required and 0 when optional, 16-byte "
    "big-endian serializer key id, 4-byte big-endian serializer key version, 16-byte big-endian owner "
    "package id; (3) every supported feature id in ascending order as 16 bytes. Declaration order, "
    "registration timing, machine paths and timestamps are excluded (P-008, P-028, P-053)."
)

KNOWN_TOP_LEVEL = (
    "descriptionFormat", "protocolVersion", "namespace", "className", "fileName",
    "supportedFeatureIds", "schemas", "groups", "code",
)
KNOWN_SCHEMA = (
    "stableName", "valueTypeName", "serializerTypeName", "serializerKeyName", "schemaId",
    "schemaVersion", "serializerKeyVersion", "ownerPackageId", "required", "fields",
)
KNOWN_FIELD = ("id", "name", "wireType", "required")
KNOWN_GROUP = ("tableName", "keysName", "lookupMethodName", "interfaceType", "kind", "entries")
KNOWN_ENTRY = (
    "stableName", "keyName", "keyVersion", "ownerPackageId", "implementationId",
    "implementationExpression",
)
KNOWN_CODE = ("usingDirectives", "assemblyAttributes", "closedGenericRootStatements")

MAX_GROUPS = 256
MAX_ENTRIES_PER_GROUP = 65536
MAX_SCHEMAS = 4096
MAX_FIELDS_PER_SCHEMA = 4096
MAX_CODE_FRAGMENT = 512

IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
DOTTED_IDENTIFIER = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$")
TYPE_EXPRESSION = re.compile(r"^[A-Za-z_][A-Za-z0-9_.<>,?\[\] ]*$")
STABLE_NAME = re.compile(r"^[a-z0-9_\-]+(?:\.[a-z0-9_\-]+)*$")
IDENTITY_HEX = re.compile(r"^[0-9a-f]{32}$")

# EnvelopeFormat.ChecksumFieldId: declared field ids are positive and start above the checksum field.
CHECKSUM_FIELD_ID = 0


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
    out: list[str] = []
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
    if "A" <= field_name[0] <= "Z":
        return field_name[0].lower() + field_name[1:]
    return field_name


def identity_hex(text: str, where: str, allow_all_zero: bool = False) -> tuple[int, int]:
    """`CatalogEmitter.ParseIdentity` after `CatalogDescriptionReader.RequiredIdentity`."""
    if not isinstance(text, str) or not IDENTITY_HEX.match(text):
        raise DescriptionMismatch("%s: '%s' is not a canonical 32-character lowercase hexadecimal id" % (where, text))
    if not allow_all_zero and text == "0" * 32:
        raise DescriptionMismatch("%s: an all-zero id is not a usable identity" % where)
    return int(text[:16], 16), int(text[16:], 16)


def has_control_character(text: str) -> bool:
    for c in text:
        if c == "\t":
            continue
        if ord(c) < 0x20 or ord(c) == 0x7F:
            return True
    return False


def has_balanced_delimiters(text: str) -> bool:
    angles = 0
    parentheses = 0
    for c in text:
        if c == "<":
            angles += 1
        elif c == ">":
            angles -= 1
        elif c == "(":
            parentheses += 1
        elif c == ")":
            parentheses -= 1
        if angles < 0 or parentheses < 0:
            return False
    return angles == 0 and parentheses == 0


def is_ascii_identifier_start(c: str) -> bool:
    return ("a" <= c <= "z") or ("A" <= c <= "Z") or c == "_"


def is_ascii_digit(c: str) -> bool:
    return "0" <= c <= "9"


def has_invocation_or_member(text: str) -> bool:
    """`CatalogCodeFragments.HasInvocationOrMember`: ASCII identifier characters only, plus the member/invocation
    punctuation. A non-ASCII letter is an operator-class character to the C# rule, so it rejects there."""
    if not text or not is_ascii_identifier_start(text[0]):
        return False
    for c in text[1:]:
        if not (is_ascii_identifier_start(c) or is_ascii_digit(c) or c in ".<>(), ?[]"):
            return False
    return True


def describe_code_fragment_rejection(fragment: str, allow_statement_attributes: bool) -> str | None:
    """`CatalogCodeFragments.DescribeRejection`, verbatim in behaviour."""
    if len(fragment) == 0:
        return "the fragment is empty"
    if len(fragment) > MAX_CODE_FRAGMENT:
        return "the fragment exceeds %d characters; split it into several fragments" % MAX_CODE_FRAGMENT
    if has_control_character(fragment):
        return "the fragment contains a control character"
    if "//" in fragment or "/*" in fragment or "*/" in fragment:
        return "the fragment cannot contain a comment"
    if ";" in fragment or "{" in fragment or "}" in fragment:
        return "the fragment cannot contain a statement terminator or a brace block"
    if not has_balanced_delimiters(fragment):
        return "the fragment has unbalanced '<', '>' or '(', ')' delimiters"
    if not has_invocation_or_member(fragment):
        return "the fragment must be a dotted expression or an invocation expression"
    if not allow_statement_attributes and "(" not in fragment:
        return "a statement fragment must be an expression, for example 'MyRoots.Track(default(My.Job<V>))'"
    return None


# ------------------------------------------------------------------------------------------------------------
# Description validation, mirroring `CatalogDescriptionReader` for the rules a well-formed description can hit.
# ------------------------------------------------------------------------------------------------------------


def _require(mapping: dict, key: str, where: str, kind):
    if key not in mapping or mapping[key] is None:
        raise DescriptionMismatch("%s is required" % where)
    value = mapping[key]
    if not isinstance(value, kind):
        raise DescriptionMismatch("%s must be %s" % (where, getattr(kind, "__name__", "the declared type")))
    return value


def _require_identifier(mapping: dict, key: str, where: str, allow_dotted: bool = False) -> str:
    text = _require(mapping, key, where, str)
    pattern = DOTTED_IDENTIFIER if allow_dotted else IDENTIFIER
    if not pattern.match(text):
        raise DescriptionMismatch("%s: '%s' is not a valid C# identifier" % (where, text))
    return text


def _require_uint32(mapping: dict, key: str, where: str) -> int:
    """`RequiredUInt32`: an unsigned 32-bit decimal integer, and a positive version (0 is not accepted)."""
    value = _require(mapping, key, where, int)
    if isinstance(value, bool) or value < 0 or value > 0xFFFFFFFF:
        raise DescriptionMismatch("%s must be an unsigned 32-bit integer" % where)
    if value == 0:
        raise DescriptionMismatch(
            "%s must be a positive version; version 0 is not an accepted declaration" % where)
    return value


def _require_int32(mapping: dict, key: str, where: str) -> int:
    value = _require(mapping, key, where, int)
    if isinstance(value, bool) or value < -0x80000000 or value > 0x7FFFFFFF:
        raise DescriptionMismatch("%s must be a signed 32-bit integer" % where)
    return value


def _require_bool(mapping: dict, key: str, where: str) -> bool:
    return _require(mapping, key, where, bool)


def _require_stable_name(mapping: dict, where: str) -> str:
    """`StableNameKeyDerivation.IsCanonicalStableName`: 1..200 characters of `a-z 0-9 _ - .`, no leading or
    trailing '.', no empty segment."""
    text = _require(mapping, "stableName", where + ".stableName", str)
    if not is_canonical_stable_name(text):
        raise DescriptionMismatch(
            "%s.stableName: '%s' is not a canonical stable name; only a-z 0-9 _ - . are accepted and the name "
            "cannot start or end with '.'" % (where, text))
    return text


def is_canonical_stable_name(text: str) -> bool:
    if not text or len(text) > 200 or not STABLE_NAME.match(text):
        return False
    return text[0] != "." and text[-1] != "." and ".." not in text


def _report_unknown(mapping: dict, where: str, known: tuple) -> None:
    for key in mapping:
        if key not in known:
            raise DescriptionMismatch("%s: unknown member '%s'" % (where, key))


def validate(description: dict) -> dict:
    """Reads the description into the canonical model the emitter would see, rejecting anything else."""
    if not isinstance(description, dict):
        raise DescriptionMismatch("the description document must be a JSON object")

    _report_unknown(description, "", KNOWN_TOP_LEVEL)
    if description.get("descriptionFormat") != DESCRIPTION_FORMAT:
        raise DescriptionMismatch(
            "descriptionFormat must be '%s'" % DESCRIPTION_FORMAT)

    protocol = _require(description, "protocolVersion", "protocolVersion", str)
    match = re.match(r"^(\d+)\.(\d+)$", protocol)
    if match is None or (int(match.group(1)), int(match.group(2))) != (1, 0):
        raise DescriptionMismatch("protocolVersion must be 1.0")

    generated_namespace = _require_identifier(description, "namespace", "namespace", allow_dotted=True)
    class_name = _require_identifier(description, "className", "className")
    file_name = _require(description, "fileName", "fileName", str)
    # `ValidateFileName`: a generated file name is a plain '.cs' name, not a path (an empty name is accepted there).
    if file_name and (not file_name.endswith(".cs")
                      or "/" in file_name or "\\" in file_name or ":" in file_name
                      or any(ord(c) < 0x20 for c in file_name)):
        raise DescriptionMismatch("fileName must be a plain '.cs' file name without a path separator")

    features: list[tuple[int, int]] = []
    raw_features = description.get("supportedFeatureIds")
    if raw_features is not None:
        if not isinstance(raw_features, list):
            raise DescriptionMismatch("supportedFeatureIds must be an array")
        for i, feature in enumerate(raw_features):
            features.append(identity_hex(feature, "supportedFeatureIds[%d]" % i))

    # Canonical identity order (P-008): the reader sorts the declared feature ids before the emitter sees them, so
    # the emitted array and the catalog fingerprint depend on the declared set and not on the document's order.
    features.sort()

    schemas: list[dict] = []
    raw_schemas = description.get("schemas")
    if raw_schemas is not None:
        if not isinstance(raw_schemas, list):
            raise DescriptionMismatch("schemas must be an array")
        if len(raw_schemas) > MAX_SCHEMAS:
            raise DescriptionMismatch("schemas declares more than %d schemas" % MAX_SCHEMAS)
        for i, item in enumerate(raw_schemas):
            schemas.append(validate_schema(item, "schemas[%d]" % i))

    groups: list[dict] = []
    raw_groups = description.get("groups")
    if raw_groups is not None:
        if not isinstance(raw_groups, list):
            raise DescriptionMismatch("groups must be an array")
        if len(raw_groups) > MAX_GROUPS:
            raise DescriptionMismatch("groups declares more than %d registration groups" % MAX_GROUPS)
        for i, item in enumerate(raw_groups):
            groups.append(validate_group(item, "groups[%d]" % i))

    code = validate_code(description)

    # Cross-checks that a repeated declaration would otherwise reach the emitted file as a duplicate member.
    schema_ids: dict[str, int] = {}
    for i, schema in enumerate(schemas):
        if schema["schemaIdHex"] in schema_ids:
            raise DescriptionMismatch(
                "schemas[%d].schemaId: schema identity %s is declared by more than one schema"
                % (i, schema["schemaIdHex"]))
        schema_ids[schema["schemaIdHex"]] = i

    owners: dict[str, str] = {}
    for i, schema in enumerate(schemas):
        if schema["stableName"] in owners:
            raise DescriptionMismatch(
                "schemas[%d].stableName: stable name '%s' is already used by %s"
                % (i, schema["stableName"], owners[schema["stableName"]]))
        owners[schema["stableName"]] = "schemas[%d].stableName" % i
    for g, group in enumerate(groups):
        for e, entry in enumerate(group["entries"]):
            if entry["stableName"] in owners:
                raise DescriptionMismatch(
                    "groups[%d].entries[%d].stableName: stable name '%s' is already used by %s"
                    % (g, e, entry["stableName"], owners[entry["stableName"]]))
            owners[entry["stableName"]] = "groups[%d].entries[%d].stableName" % (g, e)

    seen_members = {class_name}
    for name in RESERVED_MEMBERS:
        if name in seen_members:
            raise DescriptionMismatch("the generated class name '%s' is already a generated member name" % class_name)
        seen_members.add(name)
    for i, schema in enumerate(schemas):
        for member in (schema["valueTypeName"], schema["serializerTypeName"], schema["serializerKeyName"]):
            if member in seen_members:
                raise DescriptionMismatch("schemas[%d]: generated member name '%s' is already declared" % (i, member))
            seen_members.add(member)
    for g, group in enumerate(groups):
        for member in (group["tableName"], group["tableName"] + "CatalogRegistrations",
                       group["keysName"], group["lookupMethodName"]):
            if member in seen_members:
                raise DescriptionMismatch("groups[%d]: generated member name '%s' is already declared" % (g, member))
            seen_members.add(member)
        for entry in group["entries"]:
            if entry["keyName"] in seen_members:
                raise DescriptionMismatch(
                    "groups[%d]: generated key constant '%s' is already declared" % (g, entry["keyName"]))
            seen_members.add(entry["keyName"])

    # Canonical emission order: groups by ordinal table name, schemas by ascending schema identity. Declaration
    # order never reaches the generated file, so two descriptions with the same declarations produce byte-identical
    # output (P-008).
    groups.sort(key=lambda group: group["tableName"])
    schemas.sort(key=lambda schema: (schema["schemaId"][0], schema["schemaId"][1]))

    return {
        "descriptionFormat": description["descriptionFormat"],
        "protocol": protocol,
        "namespace": generated_namespace,
        "className": class_name,
        "fileName": file_name,
        "features": features,
        "schemas": schemas,
        "groups": groups,
        "code": code,
    }


def validate_schema(item, where: str) -> dict:
    if not isinstance(item, dict):
        raise DescriptionMismatch("%s: a schema declaration must be an object" % where)
    _report_unknown(item, where, KNOWN_SCHEMA)
    stable_name = _require_stable_name(item, where)
    value_type = _require_identifier(item, "valueTypeName", where + ".valueTypeName")
    serializer_type = _require_identifier(item, "serializerTypeName", where + ".serializerTypeName")
    serializer_key = _require_identifier(item, "serializerKeyName", where + ".serializerKeyName")
    schema_id_hex = _require(item, "schemaId", where + ".schemaId", str)
    schema_id = identity_hex(schema_id_hex, where + ".schemaId")
    schema_version = _require_uint32(item, "schemaVersion", where + ".schemaVersion")
    serializer_key_version = _require_uint32(item, "serializerKeyVersion", where + ".serializerKeyVersion")
    owner_hex = _require(item, "ownerPackageId", where + ".ownerPackageId", str)
    owner = identity_hex(owner_hex, where + ".ownerPackageId", allow_all_zero=True)
    is_required = _require_bool(item, "required", where + ".required")

    fields: list[dict] = []
    raw_fields = item.get("fields")
    if raw_fields is not None:
        if not isinstance(raw_fields, list):
            raise DescriptionMismatch("%s.fields must be an array" % where)
        if len(raw_fields) > MAX_FIELDS_PER_SCHEMA:
            raise DescriptionMismatch("%s.fields declares more than %d fields" % (where, MAX_FIELDS_PER_SCHEMA))
        seen_ids: set[int] = set()
        seen_names: set[str] = set()
        for i, field in enumerate(raw_fields):
            field_path = "%s.fields[%d]" % (where, i)
            if not isinstance(field, dict):
                raise DescriptionMismatch("%s: a field declaration must be an object" % field_path)
            _report_unknown(field, field_path, KNOWN_FIELD)
            field_id = _require_int32(field, "id", field_path + ".id")
            if field_id <= CHECKSUM_FIELD_ID:
                raise DescriptionMismatch("%s.id: field id %d is reserved" % (field_path, field_id))
            name = _require_identifier(field, "name", field_path + ".name")
            wire_type = _require(field, "wireType", field_path + ".wireType", str)
            if wire_type not in WIRE_TYPES:
                raise DescriptionMismatch(
                    "%s.wireType: unsupported wire type '%s'; supported types are %s"
                    % (field_path, wire_type, ", ".join(WIRE_TYPES)))
            required = _require_bool(field, "required", field_path + ".required")
            if field_id in seen_ids:
                raise DescriptionMismatch("%s.id: field id %d is declared twice" % (field_path, field_id))
            seen_ids.add(field_id)
            if name in seen_names:
                raise DescriptionMismatch("%s.name: field name '%s' is declared twice" % (field_path, name))
            seen_names.add(name)
            fields.append({"id": field_id, "name": name, "wireType": wire_type, "required": required})

    # Canonical field order is ascending field id; declaration order in the document is irrelevant (P-008).
    fields.sort(key=lambda entry: entry["id"])
    return {
        "stableName": stable_name,
        "valueTypeName": value_type,
        "serializerTypeName": serializer_type,
        "serializerKeyName": serializer_key,
        "schemaIdHex": schema_id_hex,
        "schemaId": schema_id,
        "schemaVersion": schema_version,
        "serializerKeyVersion": serializer_key_version,
        "ownerPackageIdHex": owner_hex,
        "ownerPackageId": owner,
        "required": is_required,
        "fields": fields,
        "serializerKey": derive_key(stable_name),
    }


def validate_group(item, where: str) -> dict:
    if not isinstance(item, dict):
        raise DescriptionMismatch("%s: a registration group must be an object" % where)
    _report_unknown(item, where, KNOWN_GROUP)
    table_name = _require_identifier(item, "tableName", where + ".tableName")
    keys_name = _require_identifier(item, "keysName", where + ".keysName")
    lookup_name = _require_identifier(item, "lookupMethodName", where + ".lookupMethodName")
    interface_type = _require(item, "interfaceType", where + ".interfaceType", str)
    if not TYPE_EXPRESSION.match(interface_type):
        raise DescriptionMismatch("%s.interfaceType: '%s' is not a C# type expression" % (where, interface_type))
    kind = _require(item, "kind", where + ".kind", str)
    if kind not in FACTORY_KINDS:
        raise DescriptionMismatch(
            "%s.kind: unknown factory kind '%s'; accepted kinds are %s"
            % (where, kind, ", ".join(FACTORY_KINDS)))

    entries: list[dict] = []
    raw_entries = item.get("entries")
    if raw_entries is not None:
        if not isinstance(raw_entries, list):
            raise DescriptionMismatch("%s.entries must be an array" % where)
        if len(raw_entries) > MAX_ENTRIES_PER_GROUP:
            raise DescriptionMismatch("%s.entries declares more than %d registrations" % (where, MAX_ENTRIES_PER_GROUP))
        for i, entry in enumerate(raw_entries):
            entries.append(validate_entry(entry, "%s.entries[%d]" % (where, i)))

    entries.sort(key=lambda entry: (entry["key"], entry["keyVersion"]))
    return {
        "tableName": table_name,
        "keysName": keys_name,
        "lookupMethodName": lookup_name,
        "interfaceType": interface_type,
        "kind": kind,
        "kindValue": FACTORY_KINDS[kind],
        "entries": entries,
    }


def validate_entry(item, where: str) -> dict:
    if not isinstance(item, dict):
        raise DescriptionMismatch("%s: a registration must be an object" % where)
    _report_unknown(item, where, KNOWN_ENTRY)
    stable_name = _require_stable_name(item, where)
    key_name = _require_identifier(item, "keyName", where + ".keyName")
    key_version = _require_uint32(item, "keyVersion", where + ".keyVersion")
    owner_hex = _require(item, "ownerPackageId", where + ".ownerPackageId", str)
    owner = identity_hex(owner_hex, where + ".ownerPackageId", allow_all_zero=True)
    implementation_hex = _require(item, "implementationId", where + ".implementationId", str)
    implementation = identity_hex(implementation_hex, where + ".implementationId")
    expression = _require(item, "implementationExpression", where + ".implementationExpression", str)
    reason = describe_code_fragment_rejection(expression, allow_statement_attributes=False)
    if reason is not None:
        raise DescriptionMismatch("%s.implementationExpression: %s" % (where, reason))
    return {
        "stableName": stable_name,
        "keyName": key_name,
        "keyVersion": key_version,
        "ownerPackageIdHex": owner_hex,
        "ownerPackageId": owner,
        "implementationIdHex": implementation_hex,
        "implementationId": implementation,
        "implementationExpression": expression,
        "key": derive_key(stable_name),
    }


def validate_code(description: dict) -> dict:
    element = description.get("code")
    if element is None:
        return {"usingDirectives": [], "assemblyAttributes": [], "closedGenericRootStatements": []}
    if not isinstance(element, dict):
        raise DescriptionMismatch("code must be an object of code directives")
    _report_unknown(element, "code", KNOWN_CODE)

    using_directives: list[str] = []
    raw_using = element.get("usingDirectives")
    if raw_using is not None:
        if not isinstance(raw_using, list):
            raise DescriptionMismatch("code.usingDirectives must be an array")
        for i, text in enumerate(raw_using):
            if not isinstance(text, str) or not DOTTED_IDENTIFIER.match(text):
                raise DescriptionMismatch("code.usingDirectives[%d]: '%s' is not a dotted C# identifier" % (i, text))
            using_directives.append(text)

    def fragments(member: str, allow_attributes: bool) -> list[str]:
        raw = element.get(member)
        if raw is None:
            return []
        if not isinstance(raw, list):
            raise DescriptionMismatch("code.%s must be an array" % member)
        values: list[str] = []
        for i, text in enumerate(raw):
            if not isinstance(text, str):
                raise DescriptionMismatch("code.%s[%d] must be a string" % (member, i))
            reason = describe_code_fragment_rejection(text, allow_attributes)
            if reason is not None:
                raise DescriptionMismatch("code.%s[%d]: %s" % (member, i, reason))
            values.append(text)
        return values

    return {
        "usingDirectives": using_directives,
        "assemblyAttributes": fragments("assemblyAttributes", True),
        "closedGenericRootStatements": fragments("closedGenericRootStatements", False),
    }


# ------------------------------------------------------------------------------------------------------------
# Fingerprint, exactly as `CatalogEmitter.ComputeFingerprint` + `CatalogFingerprint.Compute` build it.
# ------------------------------------------------------------------------------------------------------------


def pack_factory(key, kind_value: int, owner, implementation, contract_version: int) -> bytes:
    high, low, version = key
    return (
        high.to_bytes(8, "big")
        + low.to_bytes(8, "big")
        + version.to_bytes(4, "big")
        + kind_value.to_bytes(4, "big")
        + owner[0].to_bytes(8, "big")
        + owner[1].to_bytes(8, "big")
        + implementation[0].to_bytes(8, "big")
        + implementation[1].to_bytes(8, "big")
        + contract_version.to_bytes(4, "big")
    )


def compute_fingerprint(model: dict) -> str:
    factories: list[tuple] = []
    for group in model["groups"]:
        for entry in group["entries"]:
            key = entry["key"] + (entry["keyVersion"],)
            factories.append((key, group["kindValue"], entry["ownerPackageId"],
                              entry["implementationId"], entry["keyVersion"]))
    for schema in model["schemas"]:
        key = schema["serializerKey"] + (schema["serializerKeyVersion"],)
        factories.append((key, FACTORY_KINDS["Serializer"], schema["ownerPackageId"],
                          schema["schemaId"], schema["schemaVersion"]))

    # `CatalogOrdering.SortFactories` sorts by registration key then key version (P-008).
    factories.sort(key=lambda entry: (entry[0][0], entry[0][1], entry[0][2]))

    buffer = bytearray()
    for key, kind_value, owner, implementation, contract_version in factories:
        buffer += pack_factory(key, kind_value, owner, implementation, contract_version)

    for schema in sorted(model["schemas"], key=lambda entry: (entry["schemaId"][0], entry["schemaId"][1])):
        buffer += schema["schemaId"][0].to_bytes(8, "big")
        buffer += schema["schemaId"][1].to_bytes(8, "big")
        buffer += schema["schemaVersion"].to_bytes(4, "big")
        buffer += (1 if schema["required"] else 0).to_bytes(1, "big")
        buffer += schema["serializerKey"][0].to_bytes(8, "big")
        buffer += schema["serializerKey"][1].to_bytes(8, "big")
        buffer += schema["serializerKeyVersion"].to_bytes(4, "big")
        buffer += schema["ownerPackageId"][0].to_bytes(8, "big")
        buffer += schema["ownerPackageId"][1].to_bytes(8, "big")

    for feature in sorted(model["features"]):
        buffer += feature[0].to_bytes(8, "big") + feature[1].to_bytes(8, "big")

    return hashlib.sha256(bytes(buffer)).hexdigest()


# ------------------------------------------------------------------------------------------------------------
# The emitter template, section by section, mirroring `CatalogEmitter.EmitBody` and its helpers.
# ------------------------------------------------------------------------------------------------------------


def emit_body(model: dict, fingerprint: str) -> str:
    out: list[str] = []
    out.append("// <auto-generated />\n")
    out.append("// Generated by GameCore.Content.Compiler.CatalogEmitter; do not edit by hand.\n")
    out.append("// Description format: " + model["descriptionFormat"] + "\n")
    out.append("// Protocol: " + model["protocol"] + "\n")
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
    for directive in model["code"]["usingDirectives"]:
        out.append("using " + directive + ";\n")
    out.append("\n")
    for attribute in model["code"]["assemblyAttributes"]:
        out.append("[assembly: " + attribute + "]\n")
    if model["code"]["assemblyAttributes"]:
        out.append("\n")
    out.append("namespace " + model["namespace"] + "\n")
    out.append("{\n")
    out.append("    /// <summary>\n")
    out.append("    /// Generated closed registration catalog. Every registration is a direct reference, so managed\n")
    out.append("    /// stripping cannot remove a registration the player resolves by key at runtime (04 section 8).\n")
    out.append("    /// </summary>\n")
    out.append("    public static class " + model["className"] + "\n")
    out.append("    {\n")
    emit_constants(out, model, fingerprint)
    emit_keys(out, model)
    emit_tables(out, model)
    emit_lookups(out, model)
    emit_schema_types(out, model)
    emit_catalog_factory(out, model)
    emit_code_directives(out, model)
    out.append("        /// <summary>Hash of the generated source that precedes this declaration.</summary>\n")
    out.append(HASH_DECLARATION + '"' + HASH_PLACEHOLDER + '";\n')
    out.append("    }\n")
    out.append("}\n")
    return "".join(out)


def emit_constants(out: list[str], model: dict, fingerprint: str) -> None:
    out.append("        /// <summary>File name this catalog was generated into.</summary>\n")
    out.append('        public const string GeneratedFileName = "' + model["fileName"] + '";\n')
    out.append("\n")
    out.append("        /// <summary>Description format this catalog was generated from.</summary>\n")
    out.append('        public const string DescriptionFormat = "' + model["descriptionFormat"] + '";\n')
    out.append("\n")
    out.append("        /// <summary>Declared protocol version this catalog was generated for.</summary>\n")
    out.append('        public const string ProtocolVersion = "' + model["protocol"] + '";\n')
    out.append("\n")
    out.append("        /// <summary>Hash algorithm used by CatalogFileHash.</summary>\n")
    out.append('        public const string HashAlgorithm = "SHA-256";\n')
    out.append("\n")
    out.append("        /// <summary>Exact scope of CatalogFileHash, so the recorded value can be reproduced independently.</summary>\n")
    out.append('        public const string CatalogFileHashScope = "'
               + escape_string_literal(CATALOG_FILE_HASH_SCOPE) + '";\n')
    out.append("\n")
    out.append("        /// <summary>Scope of CatalogFingerprint, identical to GameCore.Contracts.CatalogFingerprint.Scope.</summary>\n")
    out.append('        public const string CatalogFingerprintScope = "'
               + escape_string_literal(CATALOG_FINGERPRINT_SCOPE) + '";\n')
    out.append("\n")
    out.append("        /// <summary>Canonical fingerprint of the registrations below (P-028, P-053).</summary>\n")
    out.append('        public const string CatalogFingerprint = "' + fingerprint + '";\n')
    out.append("\n")
    out.append("        /// <summary>Supported protocol feature ids, in canonical identity order (P-055).</summary>\n")
    out.append("        public static readonly Id128[] SupportedFeatureIds =\n")
    out.append("        {\n")
    for feature in model["features"]:
        out.append("            new Id128(0x" + hex16(feature[0]) + "UL, 0x" + hex16(feature[1]) + "UL),\n")
    out.append("        };\n")
    out.append("\n")


def emit_keys(out: list[str], model: dict) -> None:
    for group in model["groups"]:
        for entry in group["entries"]:
            escaped = escape_string_literal(entry["stableName"])
            out.append("        /// <summary>Generated key of " + escaped + " ('" + escaped + "').</summary>\n")
            out.append("        public static readonly FactoryKey " + entry["keyName"]
                       + " = new FactoryKey(new Id128(0x" + hex16(entry["key"][0]) + "UL, 0x" + hex16(entry["key"][1])
                       + "UL), " + str(entry["keyVersion"]) + "U);\n")
            out.append("\n")
    for schema in model["schemas"]:
        out.append("        /// <summary>Generated serializer key of schema " + schema["schemaIdHex"]
                   + " ('" + escape_string_literal(schema["stableName"]) + "').</summary>\n")
        out.append("        public static readonly FactoryKey " + schema["serializerKeyName"]
                   + " = new FactoryKey(new Id128(0x" + hex16(schema["serializerKey"][0]) + "UL, 0x"
                   + hex16(schema["serializerKey"][1]) + "UL), " + str(schema["serializerKeyVersion"]) + "U);\n")
        out.append("\n")


def emit_tables(out: list[str], model: dict) -> None:
    for group in model["groups"]:
        out.append("        /// <summary>Generated registrations of " + group["kind"]
                   + ", in canonical key order, each bound to a direct constructor reference.</summary>\n")
        out.append("        public static readonly BoundRegistration<" + group["interfaceType"] + ">[] "
                   + group["tableName"] + " =\n")
        out.append("        {\n")
        for entry in group["entries"]:
            out.append("            new BoundRegistration<" + group["interfaceType"] + ">(\n")
            out.append("                " + entry["keyName"] + ",\n")
            out.append('                "' + escape_string_literal(entry["stableName"]) + '",\n')
            out.append("                " + entry["implementationExpression"] + "),\n")
        out.append("        };\n")
        out.append("\n")
        out.append("        /// <summary>Generated keys of " + group["tableName"]
                   + ", in the same canonical order.</summary>\n")
        out.append("        public static readonly FactoryKey[] " + group["keysName"] + " =\n")
        out.append("        {\n")
        for entry in group["entries"]:
            out.append("            " + entry["keyName"] + ",\n")
        out.append("        };\n")
        out.append("\n")
        out.append("        /// <summary>\n")
        out.append("        /// Catalog registrations of " + group["kind"]
                   + ", in the same canonical order as " + group["tableName"] + ".\n")
        out.append("        /// Every entry carries its own owner package and precompiled implementation identity, so the\n")
        out.append("        /// runtime catalog hashes exactly the declarations this file was generated from (P-009, P-028).\n")
        out.append("        /// </summary>\n")
        out.append("        public static readonly FactoryRegistration[] " + group["tableName"]
                   + "CatalogRegistrations =\n")
        out.append("        {\n")
        for entry in group["entries"]:
            out.append("            new FactoryRegistration(\n")
            out.append("                " + entry["keyName"] + ",\n")
            out.append("                FactoryKind." + group["kind"] + ",\n")
            out.append("                new Id128(0x" + hex16(entry["ownerPackageId"][0]) + "UL, 0x"
                       + hex16(entry["ownerPackageId"][1]) + "UL),\n")
            out.append("                new Id128(0x" + hex16(entry["implementationId"][0]) + "UL, 0x"
                       + hex16(entry["implementationId"][1]) + "UL),\n")
            out.append("                " + str(entry["keyVersion"]) + "U),\n")
        out.append("        };\n")
        out.append("\n")

    out.append("        /// <summary>Validated schema registrations, in canonical schema-id order.</summary>\n")
    out.append("        public static readonly SchemaRegistration[] SchemaRegistrations =\n")
    out.append("        {\n")
    for schema in sorted(model["schemas"], key=lambda entry: (entry["schemaId"][0], entry["schemaId"][1])):
        out.append("            new SchemaRegistration(\n")
        out.append("                new SchemaRef(new SchemaId(new Id128(0x" + hex16(schema["schemaId"][0]) + "UL, 0x"
                   + hex16(schema["schemaId"][1]) + "UL)), " + str(schema["schemaVersion"]) + "U),\n")
        out.append("                new Id128(0x" + hex16(schema["ownerPackageId"][0]) + "UL, 0x"
                   + hex16(schema["ownerPackageId"][1]) + "UL),\n")
        out.append("                " + schema["serializerKeyName"] + ",\n")
        out.append("                " + ("true" if schema["required"] else "false") + "),\n")
    out.append("        };\n")
    out.append("\n")


def emit_lookups(out: list[str], model: dict) -> None:
    for group in model["groups"]:
        out.append("        /// <summary>\n")
        out.append("        /// Resolves one generated registration by key. A key absent from this table returns false with a\n")
        out.append("        /// null implementation; nothing is constructed by reflection or runtime type discovery (04 section 8).\n")
        out.append("        /// </summary>\n")
        out.append("        public static bool " + group["lookupMethodName"] + "(FactoryKey key, out "
                   + group["interfaceType"] + "? implementation)\n")
        out.append("        {\n")
        out.append("            for (int i = 0; i < " + group["tableName"] + ".Length; i++)\n")
        out.append("            {\n")
        out.append("                if (" + group["tableName"] + "[i].Key.Equals(key))\n")
        out.append("                {\n")
        out.append("                    implementation = " + group["tableName"] + "[i].Implementation;\n")
        out.append("                    return true;\n")
        out.append("                }\n")
        out.append("            }\n")
        out.append("\n")
        out.append("            implementation = null;\n")
        out.append("            return false;\n")
        out.append("        }\n")
        out.append("\n")


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
        out.append("            new " + schema["serializerTypeName"] + "(),\n")
    out.append("        };\n")
    out.append("\n")


def emit_value_type(out: list[str], schema: dict) -> None:
    out.append("        /// <summary>\n")
    out.append("        /// Generated value of schema " + schema["schemaIdHex"] + " ('"
               + escape_string_literal(schema["stableName"]) + "').\n")
    out.append("        /// Fields are declared in ascending field-id order, which is also the wire order (05 s6).\n")
    out.append("        /// </summary>\n")
    out.append("        public readonly struct " + schema["valueTypeName"] + "\n")
    out.append("        {\n")
    for field in schema["fields"]:
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        out.append("            /// <summary>Field id " + str(field["id"]) + ", wire type " + field["wireType"]
                   + (", required." if field["required"] else ", optional.") + "</summary>\n")
        out.append("            public readonly " + csharp + ("?" if nullable else "") + " " + field["name"] + ";\n")
    out.append("\n")
    out.append("            public " + schema["valueTypeName"] + "(")
    for i, field in enumerate(schema["fields"]):
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        if i != 0:
            out.append(", ")
        out.append(csharp + ("?" if nullable else "") + " " + parameter_name(field["name"]))
    out.append(")\n")
    out.append("            {\n")
    for field in schema["fields"]:
        out.append("                " + field["name"] + " = " + parameter_name(field["name"]) + ";\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            /// <summary>Diagnostic form; never used as an identity (P-004).</summary>\n")
    out.append("            public override string ToString()\n")
    out.append("            {\n")
    out.append('                return "' + escape_string_literal(schema["valueTypeName"]) + "(")
    for i, field in enumerate(schema["fields"]):
        if i != 0:
            out.append(", ")
        out.append(escape_string_literal(field["name"] + "=") + '" + ')
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        if field["wireType"] == "Bytes":
            out.append("(" + field["name"] + " ?? Array.Empty<byte>()).Length.ToString()")
        elif nullable:
            out.append("(" + field["name"] + ' ?? "<null>")')
        else:
            out.append(field["name"])
        out.append(' + "')
    out.append(')";\n')
    out.append("            }\n")
    out.append("        }\n")
    out.append("\n")


def emit_serializer(out: list[str], schema: dict) -> None:
    out.append("        /// <summary>\n")
    out.append("        /// Generated serializer of schema " + schema["schemaIdHex"] + " version "
               + str(schema["schemaVersion"]) + ". Canonical envelope format only: no reflection,\n")
    out.append("        /// no dynamic type construction (05 s6, P-054).\n")
    out.append("        /// </summary>\n")
    out.append("        public sealed class " + schema["serializerTypeName"] + " : GeneratedSerializerBase\n")
    out.append("        {\n")
    out.append("            private static readonly GeneratedFieldSlot[] DeclaredFieldSlots =\n")
    out.append("            {\n")
    for field in schema["fields"]:
        _, _, _, _ = WIRE_TYPES[field["wireType"]]
        out.append("                new GeneratedFieldSlot(" + str(field["id"]) + ", WireType." + field["wireType"]
                   + ", " + ("true" if field["required"] else "false") + "),\n")
    out.append("            };\n")
    out.append("\n")
    out.append("            public " + schema["serializerTypeName"] + "()\n")
    out.append("                : base(\n")
    out.append("                    " + schema["serializerKeyName"] + ",\n")
    out.append("                    new SchemaRef(new SchemaId(new Id128(0x" + hex16(schema["schemaId"][0]) + "UL, 0x"
               + hex16(schema["schemaId"][1]) + "UL)), " + str(schema["schemaVersion"]) + "U),\n")
    out.append("                    SupportedFeatureIds)\n")
    out.append("            {\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            /// <summary>Declared fields in ascending field-id order.</summary>\n")
    out.append("            protected override IReadOnlyList<GeneratedFieldSlot> DeclaredFields => DeclaredFieldSlots;\n")
    out.append("\n")
    out.append("            /// <summary>Writes one value as a canonical envelope document with a trailing checksum.</summary>\n")
    out.append("            public byte[] Serialize(" + schema["valueTypeName"] + " value)\n")
    out.append("            {\n")
    out.append("                EnvelopeWriter writer = CreateWriter();\n")
    for field in schema["fields"]:
        _, _, write_method, _ = WIRE_TYPES[field["wireType"]]
        out.append("                writer." + write_method + "(" + str(field["id"]) + ", value." + field["name"] + ");\n")
    out.append("                writer.WriteChecksum();\n")
    out.append("                return writer.ToArray();\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            /// <summary>\n")
    out.append("            /// Validates one document against this schema and decodes its declared fields. A false result\n")
    out.append("            /// reports the exact envelope error and leaves the value at its default.\n")
    out.append("            /// </summary>\n")
    out.append("            public bool TryDeserialize(byte[] document, out " + schema["valueTypeName"]
               + " value, out EnvelopeError error)\n")
    out.append("            {\n")
    out.append("                value = default(" + schema["valueTypeName"] + ");\n")
    out.append("                GeneratedFieldBuffer buffer = new GeneratedFieldBuffer();\n")
    out.append("                if (!TryReadDeclaredFields(document, buffer, out error))\n")
    out.append("                {\n")
    out.append("                    return false;\n")
    out.append("                }\n")
    out.append("\n")
    out.append("                EnvelopeReader reader = new EnvelopeReader(document);\n")
    for i, field in enumerate(schema["fields"]):
        csharp, nullable, _, _ = WIRE_TYPES[field["wireType"]]
        out.append("                " + csharp + ("?" if nullable else "") + " value" + str(i)
                   + " = default(" + csharp + ("?" if nullable else "") + ");\n")
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
    for i, field in enumerate(schema["fields"]):
        emit_field_case(out, field, i)
    out.append("                        default:\n")
    out.append("                            break;\n")
    out.append("                    }\n")
    out.append("                }\n")
    out.append("\n")
    out.append("                // A declared field the document omitted keeps its default, and every declared field\n")
    out.append("                // is assigned exactly once, so the generated value type stays immutable (05 s6, P-054).\n")
    out.append("                value = new " + schema["valueTypeName"] + "(")
    for i in range(len(schema["fields"])):
        if i != 0:
            out.append(", ")
        out.append("value" + str(i))
    out.append(");\n")
    out.append("                error = EnvelopeError.None;\n")
    out.append("                return true;\n")
    out.append("            }\n")
    out.append("        }\n")
    out.append("\n")


def emit_field_case(out: list[str], field: dict, index: int) -> None:
    csharp, nullable, _, read_method = WIRE_TYPES[field["wireType"]]
    field_var = "field" + str(index)
    value_var = field_var + "Value"
    bits_var = field_var + "Bits"
    indent = "                        "
    out.append(indent + "case " + str(field["id"]) + ":\n")
    out.append(indent + "{\n")
    out.append(indent + "    if (!reader.TryReadField(out EnvelopeField " + field_var + "))\n")
    out.append(indent + "    {\n")
    out.append(indent + "        error = reader.LastError;\n")
    out.append(indent + "        return false;\n")
    out.append(indent + "    }\n")
    out.append("\n")
    if field["wireType"] == "Float32":
        out.append(indent + "    uint " + bits_var + ";\n")
    elif field["wireType"] == "Float64":
        out.append(indent + "    ulong " + bits_var + ";\n")
    out.append(indent + "    " + csharp + ("?" if nullable else "") + " " + value_var + ";\n")
    out.append(indent + "    if (!reader." + read_method + "(" + field_var + ", out " + value_var)
    if field["wireType"] in ("Float32", "Float64"):
        out.append(", out " + bits_var)
    out.append("))\n")
    out.append(indent + "    {\n")
    out.append(indent + "        error = reader.LastError;\n")
    out.append(indent + "        return false;\n")
    out.append(indent + "    }\n")
    out.append("\n")
    out.append(indent + "    value" + str(index) + " = " + value_var + ";\n")
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
    if not model["groups"]:
        out.append("            return Array.Empty<FactoryRegistration>();\n")
    else:
        total = sum(len(group["entries"]) for group in model["groups"])
        out.append("            FactoryRegistration[] all = new FactoryRegistration[" + str(total) + "];\n")
        out.append("            int offset = 0;\n")
        for group in model["groups"]:
            count = len(group["entries"])
            if count == 0:
                continue
            out.append("            Array.Copy(" + group["tableName"] + "CatalogRegistrations, 0, all, offset, "
                       + str(count) + ");\n")
            out.append("            offset += " + str(count) + ";\n")
        out.append("            return all;\n")
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
    if model["code"]["closedGenericRootStatements"]:
        out.append("        /// <summary>\n")
        out.append("        /// Roots every closed generic instantiation this catalog depends on, so the instantiation exists in an\n")
        out.append("        /// AOT/IL2CPP player even when no other reachable code calls it (04 section 8 item 3).\n")
        out.append("        /// </summary>\n")
        out.append("        public static void " + CLOSED_GENERIC_ROOT_METHOD + "()\n")
        out.append("        {\n")
        for statement in model["code"]["closedGenericRootStatements"]:
            out.append("            " + statement + ";\n")
        out.append("        }\n")
        out.append("\n")
        out.append("        /// <summary>True because this catalog emits closed generic roots.</summary>\n")
        out.append("        public const bool HasClosedGenericRoots = true;\n")
        out.append("\n")
    else:
        out.append("        /// <summary>True because this catalog emits closed generic roots.</summary>\n")
        out.append("        public const bool HasClosedGenericRoots = false;\n")
        out.append("\n")


def emit_code_directives(out: list[str], model: dict) -> None:
    out.append("        /// <summary>Number of generated registration groups.</summary>\n")
    out.append("        public const int RegistrationGroupCount = " + str(len(model["groups"])) + ";\n")
    out.append("\n")
    out.append("        /// <summary>Number of declared schemas.</summary>\n")
    out.append("        public const int SchemaCount = " + str(len(model["schemas"])) + ";\n")
    out.append("\n")


# `CatalogWireTypes` mapped to a non-default literal, so a generated coverage exercise writes every declared field
# with a value whose round trip is observable (a default-only document could not distinguish "decoded" from
# "left at its default"). Kept in lockstep with WIRE_TYPES by construction.
COVERAGE_LITERALS = {
    "Bool": "true",
    "Bytes": "new byte[] { 1, 2, 3 }",
    "Float32": "1.5f",
    "Float64": "1.5",
    "Id128": "new Id128(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL)",
    "Int32": "7",
    "Int64": "8L",
    "UInt32": "9U",
    "UInt64": "10UL",
    "Utf8": '"gamecore"',
}


def coverage_field_comparison(field: dict, value: str) -> str:
    """One generated `&&`-clause comparing a decoded field to the literal the document was written from."""
    read = "read." + field["name"]
    wire = field["wireType"]
    if wire == "Bytes":
        return read + " != null && " + read + ".Length == 3 && " + read + "[1] == 2"
    if wire == "Utf8":
        return "string.Equals(" + read + ', "gamecore", StringComparison.Ordinal)'
    return read + " == " + value


def emit_coverage_body(model: dict) -> str:
    """The `<Class>Coverage` companion: every generated registration, serializer and closed-generic root executed.

    This is emitter output, not a hand-written fixture: it enumerates the same declarations the catalog file was
    emitted from, so a registration or serializer that a description declares but the player cannot execute shows up
    as a failed generated exercise rather than as a claim in a report. It is written into the catalog's own
    namespace so it can name the generated tables and types directly, with no reflection and no dynamic lookup.
    """
    class_name = model["className"]
    out: list[str] = []
    out.append("// <auto-generated />\n")
    out.append("// Generated by GameCore.Content.Compiler catalog tooling; do not edit by hand.\n")
    out.append("//\n")
    out.append("// Reachability exercise for " + class_name + ": it resolves every generated registration through the\n")
    out.append("// catalog's own generated lookup method, writes and re-reads every declared schema through its generated\n")
    out.append("// serializer, and calls the generated closed-generic root method when the catalog emits one. It is compiled\n")
    out.append("// into the same assembly as the catalog, so a stripped registration or serializer fails here in a player build\n")
    out.append("// instead of only in the Editor (04 section 8, GC-025).\n")
    out.append("#nullable enable\n")
    out.append("using System;\n")
    out.append("using GameCore.Contracts;\n")
    out.append("\n")
    out.append("namespace " + model["namespace"] + "\n")
    out.append("{\n")
    out.append("    /// <summary>\n")
    out.append("    /// Generated reachability exercise of <see cref=\"" + class_name + "\"/>. Every method returns the number of\n")
    out.append("    /// entries it exercised and reports the first failure in <c>failure</c> instead of throwing, so a player\n")
    out.append("    /// probe can record the exact missing entry.\n")
    out.append("    /// </summary>\n")
    out.append("    public static class " + class_name + "Coverage\n")
    out.append("    {\n")
    out.append("        /// <summary>Number of generated registration entries this exercise resolves by key.</summary>\n")
    out.append("        public const int RegistrationCount = " + str(sum(len(g["entries"]) for g in model["groups"])) + ";\n")
    out.append("\n")
    out.append("        /// <summary>\n")
    out.append("        /// Resolves every generated registration through the table's own generated lookup method and checks that a\n")
    out.append("        /// non-null implementation comes back under exactly the key the table declares. A key absent from the\n")
    out.append("        /// table must return false (P-009).\n")
    out.append("        /// </summary>\n")
    out.append("        public static int ExerciseRegistrations(out string failure)\n")
    out.append("        {\n")
    out.append("            failure = string.Empty;\n")
    out.append("            int exercised = 0;\n")
    for group in model["groups"]:
        if not group["entries"]:
            continue
        out.append("\n")
        out.append("            // " + group["tableName"] + " (" + group["kind"] + ", " + str(len(group["entries"]))
                   + " entr" + ("y" if len(group["entries"]) == 1 else "ies") + ")\n")
        out.append("            for (int i = 0; i < " + class_name + "." + group["tableName"] + ".Length; i++)\n")
        out.append("            {\n")
        out.append("                FactoryKey key = " + class_name + "." + group["tableName"] + "[i].Key;\n")
        out.append("                if (!" + class_name + "." + group["lookupMethodName"] + "(key, out "
                   + group["interfaceType"] + "? implementation) || implementation == null)\n")
        out.append("                {\n")
        out.append("                    failure = \"" + group["tableName"]
                   + ": the generated lookup returned no implementation for key \" + key.ToString();\n")
        out.append("                    return exercised;\n")
        out.append("                }\n")
        out.append("\n")
        out.append("                exercised++;\n")
        out.append("            }\n")
    out.append("\n")
    out.append("            return exercised;\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>\n")
    out.append("        /// Writes every declared schema through its generated serializer, validates the document, re-reads it\n")
    out.append("        /// and compares every declared field. A schema whose serializer did not survive stripping fails here\n")
    out.append("        /// (P-054, 05 section 6).\n")
    out.append("        /// </summary>\n")
    out.append("        public static int ExerciseSchemas(out string failure)\n")
    out.append("        {\n")
    out.append("            failure = string.Empty;\n")
    out.append("            int exercised = 0;\n")
    for schema in model["schemas"]:
        literal_arguments = ", ".join(COVERAGE_LITERALS[f["wireType"]] for f in schema["fields"])
        comparisons = [
            coverage_field_comparison(field, COVERAGE_LITERALS[field["wireType"]])
            for field in schema["fields"]
        ]
        comparison = " && ".join(comparisons) if comparisons else "true"
        out.append("\n")
        out.append("            // " + schema["stableName"] + " (schema " + schema["schemaIdHex"] + " version "
                   + str(schema["schemaVersion"]) + ")\n")
        out.append("            {\n")
        # The generated value type and serializer are NESTED inside the catalog class (the emitter writes them at
        # its own eight-space indentation), while this companion is a sibling top-level class: a simple name cannot
        # reach them (CS0246), so every reference is qualified with the catalog class, exactly as the repository's
        # hand-written consumers do (`new ProbeCatalog.ProbeRecordSerializer()`).
        out.append("                " + class_name + "." + schema["serializerTypeName"] + " serializer = new "
                   + class_name + "." + schema["serializerTypeName"] + "();\n")
        out.append("                byte[] written = serializer.Serialize(new " + class_name + "."
                   + schema["valueTypeName"] + "(" + literal_arguments + "));\n")
        out.append("                if (!serializer.TryValidate(written, out EnvelopeError validateError))\n")
        out.append("                {\n")
        out.append("                    failure = \"" + schema["stableName"]
                   + ": the generated document failed validation with \" + validateError.ToString();\n")
        out.append("                    return exercised;\n")
        out.append("                }\n")
        out.append("\n")
        out.append("                if (!serializer.TryDeserialize(written, out " + class_name + "."
                   + schema["valueTypeName"] + " read, out EnvelopeError readError))\n")
        out.append("                {\n")
        out.append("                    failure = \"" + schema["stableName"]
                   + ": the generated serializer failed to re-read its own document with \" + readError.ToString();\n")
        out.append("                    return exercised;\n")
        out.append("                }\n")
        out.append("\n")
        out.append("                if (!(" + comparison + "))\n")
        out.append("                {\n")
        out.append("                    failure = \"" + schema["stableName"]
                   + ": a declared field did not survive the generated round trip\";\n")
        out.append("                    return exercised;\n")
        out.append("                }\n")
        out.append("\n")
        out.append("                exercised++;\n")
        out.append("            }\n")
    out.append("\n")
    out.append("            return exercised;\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>Number of closed generic instantiations this catalog decides to root.</summary>\n")
    out.append("        public const int ClosedGenericRootCount = "
               + str(len(model["code"]["closedGenericRootStatements"])) + ";\n")
    out.append("\n")
    if model["code"]["closedGenericRootStatements"]:
        out.append("        /// <summary>\n")
        out.append("        /// Executes every generated closed-generic root statement, so an instantiation that only the catalog\n")
        out.append("        /// references is compiled into the player and really runs (04 section 8 item 3).\n")
        out.append("        /// </summary>\n")
        out.append("        public static void ExerciseClosedGenericRoots() => " + class_name + "."
                   + CLOSED_GENERIC_ROOT_METHOD + "();\n")
    else:
        out.append("        /// <summary>\n")
        out.append("        /// This catalog declares no closed generic instantiation, so there is nothing to execute here. The\n")
        out.append("        /// method still exists so every generated catalog has one exercise surface (04 section 8).\n")
        out.append("        /// </summary>\n")
        out.append("        public static void ExerciseClosedGenericRoots()\n")
        out.append("        {\n")
        out.append("        }\n")
    out.append("    }\n")
    out.append("}\n")
    return "".join(out)

def coverage_path_for(catalog_path: pathlib.Path) -> pathlib.Path:
    """The companion coverage file of one generated catalog: `<Class>.g.cs` -> `<Class>Coverage.g.cs`."""
    return catalog_path.with_name(catalog_path.name[:-len(".g.cs")] + "Coverage.g.cs")


def emit(model: dict) -> tuple[str, str, str]:
    """Returns the catalog text, its recorded `CatalogFileHash`, and the companion coverage text."""
    fingerprint = compute_fingerprint(model)
    body = emit_body(model, fingerprint)
    marker = body.find(HASH_DECLARATION)
    if marker < 0:
        raise DescriptionMismatch("the emitter template lost its hash declaration line")
    file_hash = sha256_hex(body[:marker])
    return body.replace(HASH_PLACEHOLDER, file_hash), file_hash, emit_coverage_body(model)


# ------------------------------------------------------------------------------------------------------------


def main() -> int:
    arguments = [argument for argument in sys.argv[1:]]
    check = False
    self_check = False
    while arguments and arguments[0].startswith("--"):
        flag = arguments.pop(0)
        if flag == "--check":
            check = True
        elif flag == "--self-check":
            self_check = True
        else:
            print("unknown flag " + flag)
            return 2

    if self_check:
        return run_self_check()

    if len(arguments) != 2:
        print(__doc__)
        return 2

    description_path = pathlib.Path(arguments[0])
    output_path = pathlib.Path(arguments[1])
    if not description_path.is_absolute():
        description_path = ROOT / description_path
    if not output_path.is_absolute():
        output_path = ROOT / output_path

    try:
        description = json.loads(description_path.read_text(encoding="utf-8"))
        model = validate(description)
        text, file_hash, coverage = emit(model)
    except DescriptionMismatch as mismatch:
        print("description rejected: " + str(mismatch))
        return 1
    except json.JSONDecodeError as error:
        print("description is not valid JSON: " + str(error))
        return 1

    coverage_path = coverage_path_for(output_path)
    if check:
        if not output_path.exists():
            print("no committed catalog at " + str(output_path))
            return 1
        if not coverage_path.exists():
            print("no committed coverage file at " + str(coverage_path))
            return 1
        if output_path.read_text(encoding="utf-8") != text:
            print("DRIFT: " + str(output_path) + " differs from the description's emitted output")
            return 1
        if coverage_path.read_text(encoding="utf-8") != coverage:
            print("DRIFT: " + str(coverage_path) + " differs from the description's emitted coverage")
            return 1
        print("%s reproduces %s and %s byte for byte (%d bytes, hash %s)"
              % (description_path.name, output_path.name, coverage_path.name,
                 len(text.encode("utf-8")), file_hash))
        return 0

    output_path.write_text(text, encoding="utf-8")
    coverage_path.write_text(coverage, encoding="utf-8")
    print("wrote %s and %s (%d bytes, CatalogFileHash %s, %d groups, %d schemas, %d coverage entries)"
          % (output_path, coverage_path, len(text.encode("utf-8")), file_hash,
             len(model["groups"]), len(model["schemas"]),
             sum(len(group["entries"]) for group in model["groups"])))
    return 0


# Every committed catalog with its committed description, for `--self-check`.
COMMITTED_CATALOGS = (
    ("unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json",
     "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs"),
    ("unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs"),
    ("unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs"),
    ("unity/GameCore.Validation/Catalogs/TraversalCatalog.catalog.json",
     "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs"),
)


def run_self_check() -> int:
    """Regenerate every committed catalog and coverage file from its description and compare byte for byte."""
    failures = 0
    for description_relative, catalog_relative in COMMITTED_CATALOGS:
        description_path = ROOT / description_relative
        catalog_path = ROOT / catalog_relative
        coverage_path = coverage_path_for(catalog_path)
        if not description_path.exists() or not catalog_path.exists() or not coverage_path.exists():
            print("MISSING: %s, %s or %s" % (description_relative, catalog_relative, coverage_path))
            failures += 1
            continue
        model = validate(json.loads(description_path.read_text(encoding="utf-8")))
        text, file_hash, coverage = emit(model)
        if text != catalog_path.read_text(encoding="utf-8"):
            print("DRIFT: %s does not reproduce %s" % (description_relative, catalog_relative))
            failures += 1
            continue
        if coverage != coverage_path.read_text(encoding="utf-8"):
            print("DRIFT: %s does not reproduce %s" % (description_relative, coverage_path.name))
            failures += 1
            continue
        print("ok: %s -> %s + %s (%d bytes, %d groups, %d schemas, %d coverage entries, CatalogFileHash %s)"
              % (description_relative, catalog_path.name, coverage_path.name, len(text.encode("utf-8")),
                 len(model["groups"]), len(model["schemas"]),
                 sum(len(group["entries"]) for group in model["groups"]), file_hash))
    if failures:
        print("%d committed catalog(s) drifted from their description" % failures)
        return 1
    print("every committed catalog and coverage file reproduces byte for byte from its committed description")
    return 0


if __name__ == "__main__":
    sys.exit(main())
