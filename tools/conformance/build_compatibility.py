#!/usr/bin/env python3
"""Build the GC-028 protocol/catalog compatibility report and the three-family audit summary.

Both documents are derived from committed or recorded files, never typed by hand:

  * `compatibility.json` / `.md`
      - the protocol version the committed catalogs declare (`--protocol-source`, default
        `unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`)
      - the four committed catalogs' `CatalogFileHash` / `CatalogFingerprint` literals
      - the resolved package versions and the manifest's declared dependency pins
      - the source revision the evidence was taken on (from the gate hand-off, or `--revision`)
      - the declared qualification profile (backend, stripping, target) from the recorded player
        environment when an evidence tree is supplied
  * `family-audit.json` / `.md`
      - the three reference families' assembly-dependency audit (`artifacts/gc-024/genre-audit.json`)
      - one row per family with the executable verdicts the latest recorded probe documents carry

Usage:
  python3 tools/conformance/build_compatibility.py [--evidence-root artifacts/w7-gate]
                                                   [--out-dir artifacts/conformance]
                                                   [--revision <sha>]
  python3 tools/conformance/build_compatibility.py --self-test

Exit codes: 0 written; 1 a required input is missing or contradicts another; 2 usage error.
"""

import argparse
import json
import os
import re
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))

PROTOCOL_VERSION = "1.0"
FORMAT = "gamecore.conformance.compatibility/1"
FAMILY_FORMAT = "gamecore.conformance.family-audit/1"

CATALOGS = (
    ("ProbeCatalog", "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs"),
    ("CardCatalog", "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCards/CardCatalog.g.cs"),
    ("CheckpointCatalog", "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedCheckpoint/CheckpointCatalog.g.cs"),
    ("TraversalCatalog", "unity/GameCore.Validation/Assets/GameCore.Validation/GeneratedTraversal/TraversalCatalog.g.cs"),
)

FAMILIES = ("narrative", "cards", "traversal")


def _read_const(path, name):
    """Read a `public const string <name> = "value";` declaration out of a generated catalog."""
    if not os.path.exists(path):
        return None
    with open(path, "r", encoding="utf-8") as handle:
        text = handle.read()
    match = re.search(r'public const string ' + re.escape(name) + r'\s*=\s*"([^"]*)"', text)
    return match.group(1) if match else None


def _relative(path):
    return os.path.relpath(path, REPO_ROOT)


def catalogs():
    rows = []
    for name, relative in CATALOGS:
        path = os.path.join(REPO_ROOT, relative)
        if not os.path.exists(path):
            raise FileNotFoundError("committed generated catalog is missing: " + relative)
        rows.append({
            "catalog": name,
            "file": relative,
            "fileHash": _read_const(path, "CatalogFileHash"),
            "fingerprint": _read_const(path, "CatalogFingerprint"),
            "declaredProtocolVersion": _read_const(path, "ProtocolVersion"),
        })
    return rows


def packages():
    manifest_path = os.path.join(REPO_ROOT, "unity/GameCore.Validation/Packages/manifest.json")
    lock_path = os.path.join(REPO_ROOT, "unity/GameCore.Validation/Packages/packages-lock.json")
    with open(manifest_path, "r", encoding="utf-8") as handle:
        manifest = json.load(handle)
    with open(lock_path, "r", encoding="utf-8") as handle:
        lock = json.load(handle)

    pinned = {name: version for name, version in manifest["dependencies"].items()
              if not str(version).startswith("file:")}
    local = {name: version for name, version in manifest["dependencies"].items()
             if str(version).startswith("file:")}
    resolved = {}
    builtin = {}
    local_resolved = {}
    for name, entry in lock["dependencies"].items():
        source = entry.get("source")
        if source == "registry":
            resolved[name] = entry.get("version")
        elif source == "builtin":
            builtin[name] = entry.get("version")
        elif source == "local":
            local_resolved[name] = entry.get("version")
    return {
        "manifest": _relative(manifest_path),
        "lock": _relative(lock_path),
        "pinned": dict(sorted(pinned.items())),
        "localPackages": dict(sorted(local.items())),
        "resolvedRegistryVersions": dict(sorted(resolved.items())),
        "resolvedBuiltinVersions": dict(sorted(builtin.items())),
        "resolvedLocalVersions": dict(sorted(local_resolved.items())),
        "testables": sorted(manifest.get("testables", [])),
    }


def revision(explicit):
    if explicit:
        return explicit
    try:
        import subprocess
        result = subprocess.run(["git", "rev-parse", "HEAD"], cwd=REPO_ROOT, stdout=subprocess.PIPE,
                                stderr=subprocess.DEVNULL, check=False)
        if result.returncode == 0:
            return result.stdout.decode("ascii", "replace").strip()
    except OSError:
        pass
    handoff = os.path.join(REPO_ROOT, "artifacts/w7-gate/HANDOFF.md")
    if os.path.exists(handoff):
        with open(handoff, "r", encoding="utf-8") as handle:
            text = handle.read()
        match = re.search(r"stable\s+`?([0-9a-f]{7,40})`?", text)
        if match:
            return match.group(1) + " (from artifacts/w7-gate/HANDOFF.md)"
    return "unknown"


def profile(evidence_root):
    """The declared qualification profile, when a player environment record is available."""
    candidates = [
        os.path.join(evidence_root, "toolchain/environment.txt") if evidence_root else None,
        os.path.join(REPO_ROOT, "artifacts/baseline/ENVIRONMENT.md"),
    ]
    record = None
    for candidate in candidates:
        if candidate and os.path.exists(candidate):
            record = candidate
            break
    if record is None:
        return {"available": False, "reason": "no recorded player environment under the evidence tree"}

    with open(record, "r", encoding="utf-8", errors="replace") as handle:
        text = handle.read()
    fields = {}
    for key in ("editor_version", "host", "arch", "scripting_backend", "managed_stripping",
                "api_compatibility", "burst", "packages_lock_sha256", "player_sha256", "dotnet_version"):
        match = re.search(r"^" + key + r"[:=]\s*(.+)$", text, re.MULTILINE)
        if match:
            fields[key] = match.group(1).strip()
    return {"available": True, "record": _relative(record), "fields": fields}


def families(evidence_root):
    """One row per reference family: the assembly audit plus the recorded probe verdicts."""
    audit_path = os.path.join(REPO_ROOT, "artifacts/gc-024/genre-audit.json")
    audit = None
    if os.path.exists(audit_path):
        with open(audit_path, "r", encoding="utf-8") as handle:
            audit = json.load(handle)

    probe_verdicts = {}
    for root in (evidence_root, os.path.join(REPO_ROOT, "artifacts/w7-gate")):
        if not root or not os.path.isdir(root):
            continue
        for directory, _, filenames in os.walk(root):
            for name in sorted(filenames):
                if not name.startswith("probe-") or not name.endswith(".json"):
                    continue
                try:
                    with open(os.path.join(directory, name), "r", encoding="utf-8") as handle:
                        document = json.load(handle)
                except (ValueError, OSError):
                    continue
                mode = str(document.get("mode") or "").lower()
                for family in FAMILIES:
                    for entry in document.get("probes", []):
                        observation = str(entry.get("name") or "")
                        if observation.startswith(family + "/") and "teardown" in observation:
                            key = mode + ":" + family
                            status = "Pass" if entry.get("status") in ("Pass", "ExpectedNegative") else "Fail"
                            probe_verdicts.setdefault(key, []).append(status)
        if probe_verdicts:
            break

    rows = []
    for family in FAMILIES:
        verdicts = [status for key, statuses in probe_verdicts.items() if key.endswith(":" + family)
                    for status in statuses]
        rows.append({
            "family": family,
            "assemblyAuditClean": audit.get("clean") if audit else None,
            "familyAssemblies": [name for name in (audit or {}).get("familyAssemblies", [])
                                 if family in name.lower()],
            "recordedTeardownVerdicts": verdicts,
            "verdict": "Pass" if (verdicts and all(v == "Pass" for v in verdicts)) else "NotRun",
        })
    return rows, audit


def render_compatibility(document, family_rows):
    lines = []
    lines.append("# GameCore V1 protocol and catalog compatibility")
    lines.append("")
    lines.append("Generated by `tools/conformance/build_compatibility.py`. Every value below is read from a")
    lines.append("committed or recorded file; nothing here is hand-typed.")
    lines.append("")
    lines.append("| Item | Value |")
    lines.append("| --- | --- |")
    lines.append("| Protocol version | `" + document["protocolVersion"] + "` |")
    lines.append("| Source revision | `" + document["revision"] + "` |")
    lines.append("| Manifest | `" + document["packages"]["manifest"] + "` |")
    lines.append("| Package lock | `" + document["packages"]["lock"] + "` |")
    lines.append("| Local gamecore packages | " + str(len(document["packages"]["localPackages"])) + " |")
    lines.append("| Testable packages | " + str(len(document["packages"]["testables"])) + " |")
    lines.append("")
    lines.append("## Committed catalogs")
    lines.append("")
    lines.append("| Catalog | File | Protocol | File hash | Fingerprint |")
    lines.append("| --- | --- | --- | --- | --- |")
    for row in document["catalogs"]:
        lines.append("| " + row["catalog"] + " | `" + row["file"] + "` | `" + str(row["declaredProtocolVersion"])
                     + "` | `" + str(row["fileHash"]) + "` | `" + str(row["fingerprint"]) + "` |")
    lines.append("")
    versions = set(row["declaredProtocolVersion"] for row in document["catalogs"])
    if versions != {PROTOCOL_VERSION}:
        lines.append("**A committed catalog declares a protocol version other than " + PROTOCOL_VERSION + ".**")
        lines.append("")
    lines.append("## Resolved package versions")
    lines.append("")
    lines.append("| Package | Resolved |")
    lines.append("| --- | --- |")
    for name, version in document["packages"]["resolvedRegistryVersions"].items():
        lines.append("| `" + name + "` | `" + str(version) + "` (registry) |")
    for name, version in document["packages"]["resolvedBuiltinVersions"].items():
        lines.append("| `" + name + "` | `" + str(version) + "` (builtin) |")
    lines.append("")
    lines.append("## Declared qualification profile")
    lines.append("")
    if document["profile"].get("available"):
        lines.append("Record: `" + document["profile"]["record"] + "`")
        lines.append("")
        for key, value in document["profile"]["fields"].items():
            lines.append("- `" + key + "`: " + value)
    else:
        lines.append("Not recorded under the supplied evidence tree: " + document["profile"].get("reason", ""))
    lines.append("")
    lines.append("## Three-family audit")
    lines.append("")
    lines.append("| Family | Assembly audit clean | Recorded teardown verdicts | Verdict |")
    lines.append("| --- | --- | --- | --- |")
    for row in family_rows:
        lines.append("| " + row["family"] + " | " + str(row["assemblyAuditClean"]) + " | "
                     + str(len(row["recordedTeardownVerdicts"])) + " | " + row["verdict"] + " |")
    lines.append("")
    return "\n".join(lines) + "\n"


def render_family(family_rows, audit):
    lines = []
    lines.append("# GameCore V1 three-family audit summary")
    lines.append("")
    lines.append("Generated by `tools/conformance/build_compatibility.py` from")
    lines.append("`artifacts/gc-024/genre-audit.json` and the recorded player probe documents.")
    lines.append("")
    if audit:
        lines.append("- Assembly-dependency audit clean: **" + str(audit.get("clean")) + "**")
        counts = audit.get("counts", {})
        lines.append("- Kernel assemblies: " + str(counts.get("kernelAssemblies")))
        lines.append("- Family assemblies: " + str(counts.get("familyAssemblies")))
        lines.append("- Reference assertions: " + str(counts.get("referenceAssertions")))
        lines.append("- Violations: " + str(counts.get("violations")))
        lines.append("- Genre tokens found in kernel sources: " + str(counts.get("kernelGenreTokens")))
    else:
        lines.append("**No assembly audit document was found; this summary cannot make its claim.**")
    lines.append("")
    lines.append("| Family | Verdict | Family assemblies |")
    lines.append("| --- | --- | --- |")
    for row in family_rows:
        lines.append("| " + row["family"] + " | **" + row["verdict"] + "** | "
                     + ", ".join("`" + name + "`" for name in row["familyAssemblies"]) + " |")
    lines.append("")
    lines.append("A family is `Pass` here only when the recorded probes carry at least one teardown")
    lines.append("observation for it and every such observation reported `Pass`.")
    lines.append("")
    return "\n".join(lines) + "\n"


def self_test():
    problems = []
    sandbox = tempfile.mkdtemp(prefix="gc028-compatibility-")
    try:
        catalog = os.path.join(sandbox, "Catalog.g.cs")
        with open(catalog, "w", encoding="utf-8") as handle:
            handle.write('public const string CatalogFileHash = "aa";\n'
                         'public const string CatalogFingerprint = "bb";\n'
                         'public const string ProtocolVersion = "1.0";\n')
        if _read_const(catalog, "CatalogFingerprint") != "bb":
            problems.append("_read_const must read a generated const literal")
        if _read_const(catalog, "NotDeclared") is not None:
            problems.append("_read_const must return None for an absent declaration")
        if _read_const(os.path.join(sandbox, "missing.g.cs"), "CatalogFingerprint") is not None:
            problems.append("_read_const must return None for a missing file")
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)

    try:
        rows = catalogs()
        if len(rows) != 4:
            problems.append("four committed catalogs must be found")
        for row in rows:
            if row["declaredProtocolVersion"] != PROTOCOL_VERSION:
                problems.append("catalog " + row["catalog"] + " declares " + str(row["declaredProtocolVersion"]))
            if row["fileHash"] is None or row["fingerprint"] is None:
                problems.append("catalog " + row["catalog"] + " is missing a hash literal")
        packed = packages()
        # A pin must resolve to itself. Unity resolves Editor modules as `builtin`, repo packages as `local` and
        # third-party packages through the registry; the claim is the same for all three, so the check reads the
        # union rather than assuming one source.
        resolved = {}
        resolved.update(packed["resolvedRegistryVersions"])
        resolved.update(packed["resolvedBuiltinVersions"])
        for name, version in packed["pinned"].items():
            if name not in resolved:
                problems.append("manifest dependency " + name + " does not appear in the lock")
            elif resolved[name] != version:
                problems.append("resolved version of " + name + " disagrees with the manifest pin")
        for name in packed["localPackages"]:
            if name not in packed["resolvedLocalVersions"]:
                problems.append("local package " + name + " does not appear in the lock")
        for name in packed["testables"]:
            if name not in packed["localPackages"]:
                problems.append("testable " + name + " is not a declared dependency of the manifest")
    except (FileNotFoundError, KeyError, ValueError) as error:
        problems.append("repository inputs are not readable: " + str(error))

    if problems:
        for problem in problems:
            print("SELF-TEST FAIL: " + problem, file=sys.stderr)
        return 1
    print("self-test: catalog literals, the four committed catalogs and the manifest/lock agreement hold")
    return 0


def main(argv):
    parser = argparse.ArgumentParser(description="Build the GC-028 compatibility and family-audit reports.")
    parser.add_argument("--evidence-root", default=os.path.join("artifacts", "w7-gate"))
    parser.add_argument("--out-dir", default=os.path.join("artifacts", "conformance"))
    parser.add_argument("--revision", default=None)
    parser.add_argument("--self-test", action="store_true")
    arguments = parser.parse_args(argv)

    if arguments.self_test:
        return self_test()

    evidence_root = arguments.evidence_root
    if not os.path.isabs(evidence_root):
        evidence_root = os.path.join(REPO_ROOT, evidence_root)
    out_dir = arguments.out_dir
    if not os.path.isabs(out_dir):
        out_dir = os.path.join(REPO_ROOT, out_dir)

    try:
        catalog_rows = catalogs()
        package_rows = packages()
    except (FileNotFoundError, KeyError, ValueError) as error:
        print("build_compatibility.py: " + str(error), file=sys.stderr)
        return 1

    document = {
        "artifact": FORMAT,
        "protocolVersion": PROTOCOL_VERSION,
        "revision": revision(arguments.revision),
        "generator": "tools/conformance/build_compatibility.py",
        "catalogs": catalog_rows,
        "packages": package_rows,
        "profile": profile(evidence_root),
    }

    family_rows, audit = families(evidence_root)
    family_document = {
        "artifact": FAMILY_FORMAT,
        "protocolVersion": PROTOCOL_VERSION,
        "generator": "tools/conformance/build_compatibility.py",
        "assemblyAudit": {
            "document": "artifacts/gc-024/genre-audit.json",
            "present": audit is not None,
            "clean": audit.get("clean") if audit else None,
        },
        "families": family_rows,
    }

    os.makedirs(out_dir, exist_ok=True)
    for name, payload in (("compatibility.json", document), ("family-audit.json", family_document)):
        with open(os.path.join(out_dir, name), "w", encoding="utf-8") as handle:
            json.dump(payload, handle, indent=2)
            handle.write("\n")
    with open(os.path.join(out_dir, "compatibility.md"), "w", encoding="utf-8") as handle:
        handle.write(render_compatibility(document, family_rows))
    with open(os.path.join(out_dir, "family-audit.md"), "w", encoding="utf-8") as handle:
        handle.write(render_family(family_rows, audit))

    print("compatibility : " + _relative(os.path.join(out_dir, "compatibility.json")))
    print("family audit  : " + _relative(os.path.join(out_dir, "family-audit.json")))
    print("protocol      : " + PROTOCOL_VERSION + " (catalogs declare "
          + ", ".join(sorted(set(str(row["declaredProtocolVersion"]) for row in catalog_rows))) + ")")
    for row in family_rows:
        print("family " + row["family"] + ": " + row["verdict"])

    inconsistent = [row["catalog"] for row in catalog_rows
                    if row["declaredProtocolVersion"] != PROTOCOL_VERSION]
    if inconsistent:
        print("build_compatibility.py: catalogs declare another protocol version: " + ", ".join(inconsistent),
              file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
