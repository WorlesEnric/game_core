#!/usr/bin/env python3
"""Emit the catalog reachability manifest from every committed catalog description (GC-025; no-SDK host helper).

The manifest is the build's answer to "which generated roots does this player have to keep?" (04 section 8,
P-058): every registration group entry, every declared schema's generated serializer and every closed-generic root
statement of every committed catalog, listed once, with the derived key and the stable name each belongs to.

Two outputs, both committed:

  * `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogReachability.g.cs` — the same data as
    compile-time C#, so the qualification player compares its live generated tables against the manifest and reports
    the exact missing root instead of reaching code only in the Editor (the manifest is data; the exercise is the
    generated `<Class>Coverage` companion the compiler mirror emits beside each catalog);
  * `artifacts/baseline/catalog-reachability.json` — the machine-readable ledger for the build host, including each
    catalog's `CatalogFileHash` and `CatalogFingerprint`, so `tools/compare_registration_fingerprints.py` can
    compare two independent clean builds.

usage:
  python3 tools/emit_catalog_reachability.py            # writes both outputs
  python3 tools/emit_catalog_reachability.py --check    # fails when either output is stale
"""

from __future__ import annotations

import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import emit_generated_catalog as emitter  # noqa: E402  (sibling tool, imported for the validated model)

ROOT = pathlib.Path(__file__).resolve().parents[1]

C_SHARP_PATH = "unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/CatalogReachability.g.cs"
JSON_PATH = "artifacts/baseline/catalog-reachability.json"

# Every committed catalog of the qualification project, in the order the manifest lists them. `label` is the
# manifest's own short name; `namespace`/`className` name the generated type the player compares against.
CATALOGS = (
    {
        "label": "probe",
        "namespace": "GameCore.Validation.Generated",
        "className": "ProbeCatalog",
        "description": "unity/GameCore.Validation/Catalogs/ProbeCatalog.catalog.json",
        "catalog": "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs",
    },
    {
        "label": "cards",
        "namespace": "GameCore.Validation.GeneratedCards",
        "className": "CardCatalog",
        "description": "unity/GameCore.Validation/Catalogs/CardCatalog.catalog.json",
        "catalog": "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs",
    },
    {
        "label": "checkpoint",
        "namespace": "GameCore.Validation.GeneratedCheckpoint",
        "className": "CheckpointCatalog",
        "description": "unity/GameCore.Validation/Catalogs/CheckpointCatalog.catalog.json",
        "catalog": "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs",
    },
    {
        "label": "traversal",
        "namespace": "GameCore.Validation.GeneratedTraversal",
        "className": "TraversalCatalog",
        "description": "unity/GameCore.Validation/Catalogs/TraversalCatalog.catalog.json",
        "catalog": "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs",
    },
)

KIND_NAMES = tuple(emitter.FACTORY_KINDS)


def hex16(value: int) -> str:
    return "%016X" % value


def build_rows() -> list[dict]:
    """The validated model of every committed catalog, with its committed generated file's recorded hashes."""
    rows: list[dict] = []
    for entry in CATALOGS:
        description = json.loads((ROOT / entry["description"]).read_text(encoding="utf-8"))
        model = emitter.validate(description)
        catalog_text = (ROOT / entry["catalog"]).read_text(encoding="utf-8")
        file_hash = read_constant(catalog_text, "CatalogFileHash", entry["catalog"])
        fingerprint = read_constant(catalog_text, "CatalogFingerprint", entry["catalog"])
        rows.append(
            {
                "label": entry["label"],
                "namespace": entry["namespace"],
                "className": entry["className"],
                "coverageClassName": entry["className"] + "Coverage",
                "description": entry["description"],
                "catalog": entry["catalog"],
                "catalogFileHash": file_hash,
                "catalogFingerprint": fingerprint,
                "model": model,
            }
        )
    return rows


def read_constant(text: str, name: str, where: str) -> str:
    needle = 'public const string ' + name + ' = "'
    start = text.find(needle)
    if start < 0:
        raise SystemExit(where + " has no " + name + " declaration")
    start += len(needle)
    end = text.find('"', start)
    if end < 0:
        raise SystemExit(where + " has an unterminated " + name + " declaration")
    return text[start:end]


# ------------------------------------------------------------------------------------------------------------
# The C# manifest.
# ------------------------------------------------------------------------------------------------------------


def emit_csharp(rows: list[dict]) -> str:
    out: list[str] = []
    out.append("// <auto-generated />\n")
    out.append("// Generated by tools/emit_catalog_reachability.py; do not edit by hand.\n")
    out.append("//\n")
    out.append("// Catalog reachability manifest (GC-025, 04 section 8, P-058). Every registration group entry, every\n")
    out.append("// declared schema's generated serializer and every closed-generic root statement of every committed\n")
    out.append("// catalog, with the derived key and stable name each belongs to. `-probeCatalogCoverage` compares its live\n")
    out.append("// generated tables against this list and reports the exact missing root, so a root that only the Editor\n")
    out.append("// keeps alive fails the player instead of passing it silently.\n")
    out.append("#nullable enable\n")
    out.append("using System;\n")
    out.append("\n")
    out.append("namespace GameCore.Validation.ProbeHost\n")
    out.append("{\n")
    out.append("    /// <summary>Generative category of one manifest root; the same names <c>FactoryKind</c> uses (P-009).</summary>\n")
    out.append("    public enum CatalogReachabilityKind\n")
    out.append("    {\n")
    for i, name in enumerate(KIND_NAMES):
        out.append("        /// <summary>Generated registration kind <c>" + name + "</c>.</summary>\n")
        out.append("        " + name + " = " + str(i) + ",\n")
    out.append("\n")
    out.append("        /// <summary>A closed-generic root statement: not a <c>FactoryKind</c>, the manifest's own category.</summary>\n")
    out.append("        ClosedGenericRoot = " + str(len(KIND_NAMES)) + ",\n")
    out.append("    }\n")
    out.append("\n")
    out.append("    /// <summary>Why one manifest root is mandatory: a registration, a schema's serializer, or a root statement.</summary>\n")
    out.append("    public enum CatalogReachabilityRole\n")
    out.append("    {\n")
    out.append("        /// <summary>A generated registration table entry resolved through the table's lookup method.</summary>\n")
    out.append("        Registration = 0,\n")
    out.append("\n")
    out.append("        /// <summary>A declared schema's generated serializer, resolved from the serializer table.</summary>\n")
    out.append("        Serializer = 1,\n")
    out.append("\n")
    out.append("        /// <summary>A generated closed-generic root statement executed through the root method.</summary>\n")
    out.append("        ClosedGenericRoot = 2,\n")
    out.append("    }\n")
    out.append("\n")
    out.append("    /// <summary>One mandatory reachability root of one catalog.</summary>\n")
    out.append("    public readonly struct CatalogReachabilityRoot\n")
    out.append("    {\n")
    out.append("        /// <summary>Builds one manifest row.</summary>\n")
    out.append("        public CatalogReachabilityRoot(\n")
    out.append("            string catalog,\n")
    out.append("            string group,\n")
    out.append("            CatalogReachabilityKind kind,\n")
    out.append("            CatalogReachabilityRole role,\n")
    out.append("            string stableName,\n")
    out.append("            string keyName,\n")
    out.append("            ulong keyHigh,\n")
    out.append("            ulong keyLow,\n")
    out.append("            uint keyVersion)\n")
    out.append("        {\n")
    out.append("            Catalog = catalog;\n")
    out.append("            Group = group;\n")
    out.append("            Kind = kind;\n")
    out.append("            Role = role;\n")
    out.append("            StableName = stableName;\n")
    out.append("            KeyName = keyName;\n")
    out.append("            KeyHigh = keyHigh;\n")
    out.append("            KeyLow = keyLow;\n")
    out.append("            KeyVersion = keyVersion;\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>Manifest label of the catalog this root belongs to.</summary>\n")
    out.append("        public string Catalog { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Generated registration table, or the schema-registration table for a serializer root.</summary>\n")
    out.append("        public string Group { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Generated registration kind; <c>ClosedGenericRoot</c> for a root statement.</summary>\n")
    out.append("        public CatalogReachabilityKind Kind { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Why this root is mandatory.</summary>\n")
    out.append("        public CatalogReachabilityRole Role { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Stable name the entry's key is derived from (P-004).</summary>\n")
    out.append("        public string StableName { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Name of the generated key constant in the catalog class.</summary>\n")
    out.append("        public string KeyName { get; }\n")
    out.append("\n")
    out.append("        /// <summary>High word of the derived 128-bit registration key.</summary>\n")
    out.append("        public ulong KeyHigh { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Low word of the derived 128-bit registration key.</summary>\n")
    out.append("        public ulong KeyLow { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Key version the registration carries.</summary>\n")
    out.append("        public uint KeyVersion { get; }\n")
    out.append("\n")

    out.append("    }\n")
    out.append("\n")
    out.append("    /// <summary>One catalog's reachability summary.</summary>\n")
    out.append("    public readonly struct CatalogReachabilityCatalog\n")
    out.append("    {\n")
    out.append("        /// <summary>Builds one catalog summary.</summary>\n")
    out.append("        public CatalogReachabilityCatalog(\n")
    out.append("            string label,\n")
    out.append("            string className,\n")
    out.append("            string coverageClassName,\n")
    out.append("            string catalogFileHash,\n")
    out.append("            string catalogFingerprint,\n")
    out.append("            int registrationGroupCount,\n")
    out.append("            int schemaCount,\n")
    out.append("            int registrationCount,\n")
    out.append("            int closedGenericRootCount)\n")
    out.append("        {\n")
    out.append("            Label = label;\n")
    out.append("            ClassName = className;\n")
    out.append("            CoverageClassName = coverageClassName;\n")
    out.append("            CatalogFileHash = catalogFileHash;\n")
    out.append("            CatalogFingerprint = catalogFingerprint;\n")
    out.append("            RegistrationGroupCount = registrationGroupCount;\n")
    out.append("            SchemaCount = schemaCount;\n")
    out.append("            RegistrationCount = registrationCount;\n")
    out.append("            ClosedGenericRootCount = closedGenericRootCount;\n")
    out.append("        }\n")
    out.append("\n")
    out.append("        /// <summary>Manifest label, unique within this manifest.</summary>\n")
    out.append("        public string Label { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Generated catalog class name, for diagnostics.</summary>\n")
    out.append("        public string ClassName { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Generated coverage companion class name, for diagnostics.</summary>\n")
    out.append("        public string CoverageClassName { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Recorded file-prefix hash of the committed generated catalog.</summary>\n")
    out.append("        public string CatalogFileHash { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Recorded canonical fingerprint of the committed generated catalog (P-028).</summary>\n")
    out.append("        public string CatalogFingerprint { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Registration groups the catalog declares.</summary>\n")
    out.append("        public int RegistrationGroupCount { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Schemas the catalog declares.</summary>\n")
    out.append("        public int SchemaCount { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Registration entries across every group.</summary>\n")
    out.append("        public int RegistrationCount { get; }\n")
    out.append("\n")
    out.append("        /// <summary>Closed-generic root statements the catalog emits.</summary>\n")
    out.append("        public int ClosedGenericRootCount { get; }\n")
    out.append("    }\n")
    out.append("\n")
    out.append("    /// <summary>\n")
    out.append("    /// The committed catalog reachability manifest. Every row is mandatory: the player resolves it through\n")
    out.append("    /// the generated lookup method and reports the missing key when a build dropped it.\n")
    out.append("    /// </summary>\n")
    out.append("    public static class CatalogReachability\n")
    out.append("    {\n")
    out.append("        /// <summary>Manifest format, so a reader can reject a shape it does not know.</summary>\n")
    out.append("        public const string Format = \"gamecore.catalog-reachability/1\";\n")
    out.append("\n")
    out.append("        /// <summary>Every committed catalog this manifest covers, in declaration order.</summary>\n")
    out.append("        public static readonly CatalogReachabilityCatalog[] Catalogs =\n")
    out.append("        {\n")
    for row in rows:
        model = row["model"]
        out.append("            new CatalogReachabilityCatalog(\n")
        out.append('                "' + row["label"] + '",\n')
        out.append('                "' + row["className"] + '",\n')
        out.append('                "' + row["coverageClassName"] + '",\n')
        out.append('                "' + row["catalogFileHash"] + '",\n')
        out.append('                "' + row["catalogFingerprint"] + '",\n')
        out.append("                " + str(len(model["groups"])) + ",\n")
        out.append("                " + str(len(model["schemas"])) + ",\n")
        out.append("                " + str(sum(len(group["entries"]) for group in model["groups"])) + ",\n")
        out.append("                " + str(len(model["code"]["closedGenericRootStatements"])) + "),\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Every mandatory registration root of every covered catalog.</summary>\n")
    out.append("        public static readonly CatalogReachabilityRoot[] Registrations =\n")
    out.append("        {\n")
    for row in rows:
        for group in row["model"]["groups"]:
            for entry in group["entries"]:
                out.append("            new CatalogReachabilityRoot(\n")
                out.append('                "' + row["label"] + '",\n')
                out.append('                "' + group["tableName"] + '",\n')
                out.append("                CatalogReachabilityKind." + group["kind"] + ",\n")
                out.append("                CatalogReachabilityRole.Registration,\n")
                out.append('                "' + entry["stableName"] + '",\n')
                out.append('                "' + entry["keyName"] + '",\n')
                out.append("                0x" + hex16(entry["key"][0]) + "UL,\n")
                out.append("                0x" + hex16(entry["key"][1]) + "UL,\n")
                out.append("                " + str(entry["keyVersion"]) + "U),\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Every mandatory serializer root contributed by a declared schema.</summary>\n")
    out.append("        public static readonly CatalogReachabilityRoot[] Serializers =\n")
    out.append("        {\n")
    for row in rows:
        for schema in row["model"]["schemas"]:
            out.append("            new CatalogReachabilityRoot(\n")
            out.append('                "' + row["label"] + '",\n')
            out.append('                "SchemaRegistrations",\n')
            out.append("                CatalogReachabilityKind.Serializer,\n")
            out.append("                CatalogReachabilityRole.Serializer,\n")
            out.append('                "' + schema["stableName"] + '",\n')
            out.append('                "' + schema["serializerKeyName"] + '",\n')
            out.append("                0x" + hex16(schema["serializerKey"][0]) + "UL,\n")
            out.append("                0x" + hex16(schema["serializerKey"][1]) + "UL,\n")
            out.append("                " + str(schema["serializerKeyVersion"]) + "U),\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Every closed-generic root statement, in catalog and declaration order.</summary>\n")
    out.append("        public static readonly CatalogReachabilityRoot[] ClosedGenericRoots =\n")
    out.append("        {\n")
    for row in rows:
        for statement in row["model"]["code"]["closedGenericRootStatements"]:
            out.append("            new CatalogReachabilityRoot(\n")
            out.append('                "' + row["label"] + '",\n')
            out.append('                "RootClosedGenericInstantiations",\n')
            out.append("                CatalogReachabilityKind.ClosedGenericRoot,\n")
            out.append("                CatalogReachabilityRole.ClosedGenericRoot,\n")
            out.append('                "' + emitter.escape_string_literal(statement) + '",\n')
            out.append('                string.Empty,\n')
            out.append("                0UL,\n")
            out.append("                0UL,\n")
            out.append("                0U),\n")
    out.append("        };\n")
    out.append("\n")
    out.append("        /// <summary>Rows of one catalog label; an unknown label yields the count zero with no rows.</summary>\n")
    out.append("        public static int CountOf(string catalog, CatalogReachabilityRoot[] rows)\n")
    out.append("        {\n")
    out.append("            int count = 0;\n")
    out.append("            for (int i = 0; i < rows.Length; i++)\n")
    out.append("            {\n")
    out.append("                if (string.Equals(rows[i].Catalog, catalog, StringComparison.Ordinal))\n")
    out.append("                {\n")
    out.append("                    count++;\n")
    out.append("                }\n")
    out.append("            }\n")
    out.append("\n")
    out.append("            return count;\n")
    out.append("        }\n")
    out.append("    }\n")
    out.append("}\n")
    return "".join(out)


# ------------------------------------------------------------------------------------------------------------
# The JSON ledger.
# ------------------------------------------------------------------------------------------------------------


def emit_json(rows: list[dict]) -> str:
    document = {
        "format": "gamecore.catalog-reachability/1",
        "task": "GC-025",
        "catalogs": [
            {
                "label": row["label"],
                "namespace": row["namespace"],
                "className": row["className"],
                "coverageClassName": row["coverageClassName"],
                "description": row["description"],
                "catalog": row["catalog"],
                "catalogFileHash": row["catalogFileHash"],
                "catalogFingerprint": row["catalogFingerprint"],
                "registrationGroupCount": len(row["model"]["groups"]),
                "registrationCount": sum(len(group["entries"]) for group in row["model"]["groups"]),
                "schemaCount": len(row["model"]["schemas"]),
                "closedGenericRootCount": len(row["model"]["code"]["closedGenericRootStatements"]),
                "groups": [
                    {
                        "tableName": group["tableName"],
                        "kind": group["kind"],
                        "lookupMethodName": group["lookupMethodName"],
                        "interfaceType": group["interfaceType"],
                        "entries": [
                            {
                                "stableName": entry["stableName"],
                                "keyName": entry["keyName"],
                                "key": hex16(entry["key"][0]) + hex16(entry["key"][1]),
                                "keyVersion": entry["keyVersion"],
                                "ownerPackageId": entry["ownerPackageIdHex"],
                                "implementationId": entry["implementationIdHex"],
                            }
                            for entry in group["entries"]
                        ],
                    }
                    for group in row["model"]["groups"]
                ],
                "serializers": [
                    {
                        "stableName": schema["stableName"],
                        "schemaId": schema["schemaIdHex"],
                        "schemaVersion": schema["schemaVersion"],
                        "serializerKeyName": schema["serializerKeyName"],
                        "key": hex16(schema["serializerKey"][0]) + hex16(schema["serializerKey"][1]),
                        "keyVersion": schema["serializerKeyVersion"],
                        "required": schema["required"],
                    }
                    for schema in row["model"]["schemas"]
                ],
                "closedGenericRoots": list(row["model"]["code"]["closedGenericRootStatements"]),
            }
            for row in rows
        ],
    }
    return json.dumps(document, indent=2) + "\n"


def main() -> int:
    check = "--check" in sys.argv[1:]
    rows = build_rows()
    csharp = emit_csharp(rows)
    ledger = emit_json(rows)

    csharp_path = ROOT / C_SHARP_PATH
    json_path = ROOT / JSON_PATH

    if check:
        problems = 0
        for path, text in ((csharp_path, csharp), (json_path, ledger)):
            if not path.exists():
                print("MISSING: " + str(path))
                problems += 1
            elif path.read_text(encoding="utf-8") != text:
                print("STALE: " + str(path) + " differs from the manifest these descriptions imply")
                problems += 1
        if problems:
            print("%d reachability manifest file(s) need regenerating" % problems)
            return 1
        print("catalog reachability manifest reproduces from the committed descriptions: "
              + str(len(rows)) + " catalog(s)")
        return 0

    csharp_path.parent.mkdir(parents=True, exist_ok=True)
    json_path.parent.mkdir(parents=True, exist_ok=True)
    csharp_path.write_text(csharp, encoding="utf-8")
    json_path.write_text(ledger, encoding="utf-8")
    registrations = sum(sum(len(group["entries"]) for group in row["model"]["groups"]) for row in rows)
    serializers = sum(len(row["model"]["schemas"]) for row in rows)
    roots = sum(len(row["model"]["code"]["closedGenericRootStatements"]) for row in rows)
    print("wrote %s and %s: %d catalog(s), %d registration(s), %d serializer(s), %d closed-generic root(s)"
          % (C_SHARP_PATH, JSON_PATH, len(rows), registrations, serializers, roots))
    return 0


if __name__ == "__main__":
    sys.exit(main())
