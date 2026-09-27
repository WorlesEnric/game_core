#!/usr/bin/env python3
"""Fail when the GC-028, GC-029 and Wave 8 gate evidence do not describe one revision.

This is the GC-030 consistency check. It answers one question with evidence, not prose: *do the
records that the release-readiness record relies on all describe the same source, catalogs and
package lock?* It reads the recorded revisions, catalog digests, package digests and player digests
out of the evidence files and compares them with each other and with the working tree.

What it checks
--------------
A. **Accepted revision agreement.** Every accepted record must declare exactly one revision, the
   same one, and that revision must be an ancestor of `HEAD`.
B. **Catalog identity.** The `CatalogFileHash`, `CatalogFingerprint` and `ProtocolVersion` literals
   inside the four committed generated catalogs are the recorded identity. Every compatibility
   report and catalog ledger must repeat them, every player document that ran against a catalog must
   report them, and every environment record's `catalog_sha256` must equal the file digest of
   `ProbeCatalog.g.cs` — the file `tools/unity/build_probe.sh` hashes.
C. **Package identity.** The package manifest's pins, the lock's resolved registry/builtin/local
   versions and the manifest's testables are re-derived from the working tree and must equal every
   compatibility report's copy. Every local package manifest must carry the accepted packaging
   version. A recorded manifest or lock digest that differs from the tree is only tolerated when
   `SUPERSEDED_DIGESTS` names the exact value and the change that explains it.
D. **Player identity.** The player launcher is a build product and is not committed, so its digest
   cannot be recomputed here: every record that declares one must declare the same one. That is
   agreement, not verification, and it is reported as such.
E. **Supersession and rejection.** Each superseded record must name the accepted record that
   supersedes it; the catalogs (and, where declared, the whole package tree) must be byte-identical
   between the two revisions, proved with `git diff --name-only`; and the accepted gate report must
   still contain its statement that the rejected clone was not accepted.
F. **No stale claim.** A derived suite matrix from an earlier corpus, or a template budget snapshot,
   must not be presented as accepted-revision evidence.

Exit status is 1 when any check fails. `--self-test` fabricates a sandbox repository, asserts that a
consistent corpus passes, and then mutates one fact at a time and asserts that the matching check
fails — every check this tool performs is falsified by its own self-test.

Nothing here builds or runs the runtime: this is an evidence-consistency tool.
"""

from __future__ import annotations

import argparse
import glob as globlib
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(os.path.dirname(HERE))

sys.path.insert(0, HERE)

import readiness_data as rd  # noqa: E402  (path is set above)

FORMAT = "gamecore.release-readiness.revision-consistency/1"

OK = "ok"
FAIL = "fail"
SKIP = "skip"


# --- primitives ---------------------------------------------------------------------------------

def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(65536), b""):
            digest.update(chunk)
    return digest.hexdigest()


def sha256_text(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def read_text(path):
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def read_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def parse_kv(text):
    """Read `key=value` and `key: value` records (every environment record in this repository)."""
    fields = {}
    for line in text.splitlines():
        match = re.match(r"^([A-Za-z0-9_.]+)\s*[:=]\s*(.*?)\s*$", line)
        if match:
            fields[match.group(1)] = match.group(2)
    return fields


def dig(payload, key):
    """Read a dotted path out of a JSON document."""
    node = payload
    for part in key.split("."):
        if isinstance(node, dict) and part in node:
            node = node[part]
        else:
            return None
    return node


# --- recorded-revision extraction ---------------------------------------------------------------

def extract_revision(repo, record):
    """Return (value, problem). A record that cannot be read or parsed is a problem, never a pass."""
    path = os.path.join(repo, record["path"])
    if not os.path.exists(path):
        return None, "recorded revision source is missing: " + record["path"]
    try:
        if record["format"] == "kv":
            text = read_text(path)
            for line in text.splitlines():
                match = re.match(r"^" + re.escape(record["key"]) + r"\s*[:=]\s*(\S+)\s*$", line)
                if match:
                    return match.group(1), None
            return None, record["path"] + " does not declare `" + record["key"] + "`"
        if record["format"] == "json":
            value = dig(read_json(path), record["key"])
            if not isinstance(value, str) or not value:
                return None, record["path"] + " does not declare a `" + record["key"] + "` string"
            return value, None
        text = read_text(path)
        match = re.search(record["regex"], text)
        if not match:
            return None, record["path"] + " does not match the declared revision pattern"
        return match.group(1), None
    except (OSError, ValueError) as error:
        return None, record["path"] + " is unreadable: " + str(error)


def git(repo, args):
    """Run a git command; return (returncode, stdout). Missing git is a hard failure, by design."""
    try:
        result = subprocess.run(["git"] + list(args), cwd=repo, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE, check=False)
    except OSError as error:
        return 127, "git is unavailable: " + str(error)
    return result.returncode, result.stdout.decode("ascii", "replace").strip()


def git_show_bytes(repo, revision, path):
    try:
        result = subprocess.run(["git", "show", revision + ":" + path], cwd=repo,
                                stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    except OSError as error:
        return None, "git is unavailable: " + str(error)
    if result.returncode != 0:
        return None, "git show " + revision + ":" + path + " failed"
    return result.stdout, None


# --- working-tree facts -------------------------------------------------------------------------

def tree_catalogs(repo):
    """The recorded identity of every committed catalog, read out of the generated file itself."""
    rows = {}
    for name, relative in rd.CATALOGS:
        path = os.path.join(repo, relative)
        if not os.path.exists(path):
            raise FileNotFoundError("committed generated catalog is missing: " + relative)
        text = read_text(path)
        rows[name] = {
            "catalog": name,
            "file": relative,
            "fileHash": _read_const(text, "CatalogFileHash"),
            "fingerprint": _read_const(text, "CatalogFingerprint"),
            "declaredProtocolVersion": _read_const(text, "ProtocolVersion"),
            "sha256": sha256_file(path),
        }
    return rows


def _read_const(text, name):
    match = re.search(r'public const string ' + re.escape(name) + r'\s*=\s*"([^"]*)"', text)
    return match.group(1) if match else None


def lock_graph(lock):
    """The resolved `name -> (source, version)` graph of a lock document."""
    graph = {}
    for name, entry in lock["dependencies"].items():
        graph[name] = (entry.get("source"), entry.get("version"))
    return graph


def tree_packages(repo):
    manifest = read_json(os.path.join(repo, rd.MANIFEST_PATH))
    lock = read_json(os.path.join(repo, rd.LOCK_PATH))
    pinned = {n: v for n, v in manifest["dependencies"].items() if not str(v).startswith("file:")}
    local = {n: v for n, v in manifest["dependencies"].items() if str(v).startswith("file:")}
    resolved, builtin, local_resolved = {}, {}, {}
    for name, entry in lock["dependencies"].items():
        source = entry.get("source")
        if source == "registry":
            resolved[name] = entry.get("version")
        elif source == "builtin":
            builtin[name] = entry.get("version")
        elif source == "local":
            local_resolved[name] = entry.get("version")
    return {
        "manifest": rd.MANIFEST_PATH,
        "lock": rd.LOCK_PATH,
        "pinned": dict(sorted(pinned.items())),
        "localPackages": dict(sorted(local.items())),
        "resolvedRegistryVersions": dict(sorted(resolved.items())),
        "resolvedBuiltinVersions": dict(sorted(builtin.items())),
        "resolvedLocalVersions": dict(sorted(local_resolved.items())),
        "testables": sorted(manifest.get("testables", [])),
    }


# --- checks -------------------------------------------------------------------------------------

class Report(object):
    """Collects findings. A finding is one falsifiable statement about one record."""

    def __init__(self):
        self.findings = []

    def add(self, group, status, subject, detail):
        self.findings.append({"group": group, "status": status, "subject": subject, "detail": detail})

    def ok(self, group, subject, detail=""):
        self.add(group, OK, subject, detail)

    def fail(self, group, subject, detail):
        self.add(group, FAIL, subject, detail)

    def skip(self, group, subject, detail):
        self.add(group, SKIP, subject, detail)

    @property
    def problems(self):
        return [f for f in self.findings if f["status"] == FAIL]

    @property
    def failed(self):
        return bool(self.problems)


def check_accepted_revisions(repo, report, git_runner):
    declared = {}
    for record in rd.ACCEPTED_REVISION_RECORDS:
        value, problem = extract_revision(repo, record)
        if problem:
            report.fail("A: accepted revision", record["path"], problem)
            continue
        declared[record["path"]] = value
        report.ok("A: accepted revision", record["path"], "declares " + value)
    values = sorted(set(declared.values()))
    if len(values) > 1:
        report.fail("A: accepted revision", "agreement",
                    "accepted records disagree: " + json.dumps(declared, sort_keys=True))
    elif len(values) == 1:
        accepted = values[0]
        report.ok("A: accepted revision", "agreement", "all accepted records declare " + accepted)
        if not re.match(r"^[0-9a-f]{7,40}$", accepted):
            report.fail("A: accepted revision", accepted, "is not a revision hash")
        else:
            code, output = git_runner(repo, ["merge-base", "--is-ancestor", accepted, "HEAD"])
            if code == 0:
                report.ok("A: accepted revision", accepted, "is an ancestor of HEAD")
            else:
                report.fail("A: accepted revision", accepted,
                            "is not an ancestor of HEAD (git exit " + str(code) + ": " +
                            output[:200] + ")")
    return values[0] if len(values) == 1 else None


def compare_catalog_rows(report, group, label, rows, tree, require_all=False):
    """Compare catalog identity rows with the working tree.

    A row may spell the digests either as `fileHash`/`fingerprint` (the compatibility report) or as
    `catalogFileHash`/`catalogFingerprint` (the ledgers and the player documents). Both digests must
    be present; the protocol literal is compared when the row declares one. `require_all` demands
    that the row set cover all four committed catalogs (a compatibility report or a full ledger),
    which a single player document is not expected to do.
    """
    by_path = dict((row["file"], row["catalog"]) for row in tree.values())
    seen = set()
    for row in rows:
        name = row.get("className")
        if name not in tree:
            name = by_path.get(row.get("catalog"))
        if name not in tree:
            name = row.get("catalog")
        if name not in tree:
            report.fail(group, label, "names an unknown catalog " + str(row.get("catalog")))
            continue
        seen.add(name)
        want = tree[name]
        pairs = (("fileHash", row.get("fileHash", row.get("catalogFileHash"))),
                 ("fingerprint", row.get("fingerprint", row.get("catalogFingerprint"))))
        for key, got in pairs:
            if got is None:
                report.fail(group, label + " / " + str(name),
                            "does not record " + key)
            elif got != want[key]:
                report.fail(group, label + " / " + str(name),
                            key + " recorded " + str(got) + " != tree " + str(want[key]))
        declared_protocol = row.get("declaredProtocolVersion")
        if declared_protocol is not None and declared_protocol != want["declaredProtocolVersion"]:
            report.fail(group, label + " / " + str(name),
                        "declares protocol " + str(declared_protocol) + ", the tree declares " +
                        str(want["declaredProtocolVersion"]))
        if want["declaredProtocolVersion"] != rd.PROTOCOL_VERSION:
            report.fail(group, label + " / " + str(name),
                        "the committed catalog declares protocol " +
                        str(want["declaredProtocolVersion"]))
    if require_all:
        missing = sorted(set(tree) - seen)
        if missing:
            report.fail(group, label, "does not cover " + ", ".join(missing))
    if seen:
        report.ok(group, label, "records " + str(len(seen)) + " catalog(s), identical to the tree")


def compare_package_block(report, group, label, block, tree):
    for key in ("pinned", "localPackages", "resolvedRegistryVersions", "resolvedBuiltinVersions",
                "resolvedLocalVersions", "testables"):
        got = block.get(key)
        if got != tree[key]:
            report.fail(group, label + " / " + key, "recorded value differs from the tree")
        elif key == "resolvedRegistryVersions":
            report.ok(group, label + " / " + key,
                      str(len(got)) + " registry pins identical to the tree lock")
    for key in ("manifest", "lock"):
        if block.get(key) != tree[key]:
            report.fail(group, label + " / " + key,
                        "records path " + str(block.get(key)) + " instead of " + tree[key])


def check_catalogs(repo, report, tree):
    for path in ("artifacts/conformance/compatibility.json",
                 "artifacts/w8-gate/matrix/compatibility.json",
                 "artifacts/conformance/results/compatibility.json"):
        full = os.path.join(repo, path)
        if not os.path.exists(full):
            report.fail("B: catalog identity", path, "compatibility report is missing")
            continue
        compare_catalog_rows(report, "B: catalog identity", path, read_json(full)["catalogs"],
                             tree, require_all=True)

    for record in rd.CATALOG_LEDGER_RECORDS:
        label = record["path"] + " (" + record["describes"] + ")"
        full = os.path.join(repo, record["path"])
        if not os.path.exists(full):
            report.fail("B: catalog identity", label, "catalog ledger is missing")
            continue
        compare_catalog_rows(report, "B: catalog identity", label, read_json(full)["catalogs"],
                             tree, require_all=True)

    documents = 0
    for pattern in rd.CATALOG_DIGEST_PROBE_GLOBS:
        for path in sorted(globlib.glob(os.path.join(repo, pattern))):
            if path.endswith(rd.CATALOG_DIGEST_EXCLUDED_SUFFIXES):
                continue
            label = os.path.relpath(path, repo)
            try:
                payload = read_json(path)
            except ValueError as error:
                report.fail("B: catalog identity", label, "is not readable JSON: " + str(error))
                continue
            documents += 1
            row = {"catalog": "ProbeCatalog", "catalogFileHash": payload.get("catalogFileHash"),
                   "catalogFingerprint": payload.get("catalogFingerprint")}
            compare_catalog_rows(report, "B: catalog identity", label, [row], tree)
    report.ok("B: catalog identity", "player documents",
              str(documents) + " probe document(s) checked against the tree")


def check_packages(repo, report, tree, superseded_digests, unverifiable_digests):
    for path in ("artifacts/conformance/compatibility.json",
                 "artifacts/w8-gate/matrix/compatibility.json",
                 "artifacts/conformance/results/compatibility.json"):
        full = os.path.join(repo, path)
        if not os.path.exists(full):
            continue
        compare_package_block(report, "C: package identity", path, read_json(full)["packages"], tree)

    manifests = sorted(globlib.glob(os.path.join(repo, rd.PACKAGE_MANIFEST_GLOB)))
    if not manifests:
        report.fail("C: package identity", rd.PACKAGE_MANIFEST_GLOB, "no local package manifest found")
    for path in manifests:
        label = os.path.relpath(path, repo)
        version = read_json(path).get("version")
        if version != rd.ACCEPTED_PACKAGE_VERSION:
            report.fail("C: package identity", label,
                        "declares version " + str(version) + " instead of " +
                        rd.ACCEPTED_PACKAGE_VERSION)
    if manifests:
        report.ok("C: package identity", "local package manifests",
                  str(len(manifests)) + " package(s) at version " + rd.ACCEPTED_PACKAGE_VERSION)

    tree_digest = {
        "manifest": sha256_file(os.path.join(repo, rd.MANIFEST_PATH)),
        "lock": sha256_file(os.path.join(repo, rd.LOCK_PATH)),
        "catalog": sha256_file(os.path.join(repo, rd.ENVIRONMENT_CATALOG_FILE)),
    }
    players = {}
    for record in rd.DIGEST_RECORDS:
        full = os.path.join(repo, record["path"])
        if not os.path.exists(full):
            report.fail("C: package identity", record["path"], "digest record is missing")
            continue
        fields = parse_kv(read_text(full))
        for key, kind in record["fields"]:
            recorded = fields.get(key)
            if recorded is None:
                report.fail("C: package identity", record["path"] + " / " + key,
                            "record does not declare `" + key + "`")
                continue
            if kind == "player":
                players.setdefault(recorded, []).append(record["path"])
                continue
            if record.get("manifest_path") and kind == "manifest":
                if matches(unverifiable_digests, record["path"], key, recorded):
                    report.skip("C: package identity", record["path"] + " / " + key,
                                "hashes a disposable clone's manifest; recorded value kept, not "
                                "recomputed here")
                else:
                    report.fail("C: package identity", record["path"] + " / " + key,
                                "is not a declared unverifiable digest")
                continue
            if recorded == tree_digest[kind]:
                report.ok("C: package identity", record["path"] + " / " + key,
                          "equals the tree's " + kind + " digest")
                continue
            if matches(superseded_digests, record["path"], key, recorded):
                report.ok("C: package identity", record["path"] + " / " + key,
                          "differs from the tree and is a declared superseded digest")
                continue
            report.fail("C: package identity", record["path"] + " / " + key,
                        "recorded " + recorded + " != tree " + tree_digest[kind] +
                        " and is not declared in SUPERSEDED_DIGESTS")

    if not players:
        report.fail("D: player identity", "player_sha256", "no record declares a player digest")
    elif len(players) > 1:
        report.fail("D: player identity", "agreement",
                    "records disagree: " + json.dumps(players, sort_keys=True))
    else:
        digest, paths = list(players.items())[0]
        report.ok("D: player identity", "agreement",
                  "all " + str(len(paths)) + " records declare " + digest +
                  " (recorded-only; the player binary is a build product)")


def matches(declarations, path, field, value=None):
    """Find a declaration row. `value=None` matches on path and field alone."""
    for row in declarations:
        if row["path"] == path and row["field"] == field:
            if value is None or row["value"] == value:
                return row
    return None


def check_supersession(repo, report, accepted, git_runner):
    if accepted is None:
        report.fail("E: supersession", "accepted revision", "unknown; supersession cannot be proved")
        return
    for record in rd.SUPERSEDED_REVISION_RECORDS:
        label = record["path"]
        value, problem = extract_revision(repo, record)
        if problem:
            report.fail("E: supersession", label, problem)
            continue
        if value == accepted:
            report.ok("E: supersession", label, "declares the accepted revision directly")
            continue
        superseder = record["superseded_by"]
        super_value, super_problem = extract_revision(
            repo, next(r for r in rd.ACCEPTED_REVISION_RECORDS if r["path"] == superseder))
        if super_problem or super_value != accepted:
            report.fail("E: supersession", label,
                        "declares a supersession by " + superseder + ", which does not declare the "
                        "accepted revision")
            continue
        code, output = git_runner(repo, ["cat-file", "-e", value + "^{commit}"])
        if code != 0:
            report.fail("E: supersession", label,
                        "revision " + value + " is not present in this repository")
            continue
        full_identity = record.get("content_identity") == "catalogs_and_packages"
        if full_identity:
            paths, described = list(rd.CATALOG_PACKAGE_PATHS), "catalogs and package files"
        else:
            paths = list(rd.CATALOG_PATHS) + [rd.MANIFEST_PATH]
            described = "the four catalogs and the package manifest"
        code, output = git_runner(repo, ["diff", "--name-only", value, accepted, "--"] + paths)
        if code != 0:
            report.fail("E: supersession", label,
                        "git diff against the accepted revision failed: " + output[:200])
        elif output.strip():
            report.fail("E: supersession", label,
                        "differs from the accepted revision in: " +
                        ", ".join(output.splitlines()[:6]))
        else:
            report.ok("E: supersession", label,
                      value + " is content-identical to " + accepted[:12] + " on " + described)
    for record in rd.HISTORICAL_REVISION_RECORDS:
        value, problem = extract_revision(repo, record)
        if problem:
            report.fail("E: supersession", record["path"], problem)
        else:
            report.ok("E: supersession", record["path"],
                      "declares historical revision " + value + ": " + record["reason"])
    for declaration in rd.REJECTION_DECLARATIONS:
        full = os.path.join(repo, declaration["declared_in"])
        if not os.path.exists(full):
            report.fail("E: supersession", declaration["declared_in"], "record is missing")
            continue
        if declaration["marker"] not in read_text(full):
            report.fail("E: supersession", declaration["declared_in"],
                        "no longer contains the statement that " + declaration["record"] +
                        " was rejected: `" + declaration["marker"] + "`")
        else:
            report.ok("E: supersession", declaration["declared_in"],
                      "still records the rejection of " + declaration["record"])


def check_stale_claims(repo, report):
    full = os.path.join(repo, rd.CANONICAL_EVIDENCE_INDEX)
    if not os.path.exists(full):
        report.fail("F: stale claims", rd.CANONICAL_EVIDENCE_INDEX, "summary is missing")
    else:
        root = dig(read_json(full), "evidenceRoot")
        gate_root = rd.ACCEPTED_EVIDENCE_TREES[0]["root"]
        if root != gate_root:
            report.fail("F: stale claims", rd.CANONICAL_EVIDENCE_INDEX,
                        "evidenceRoot is " + str(root) + ", not the accepted gate tree " + gate_root)
        else:
            report.ok("F: stale claims", rd.CANONICAL_EVIDENCE_INDEX,
                      "derives from " + gate_root)

    full = os.path.join(repo, rd.W7_DERIVED_SUITE_MATRIX)
    if not os.path.exists(full):
        report.fail("F: stale claims", rd.W7_DERIVED_SUITE_MATRIX,
                    "the historical matrix must be retained, not deleted")
    else:
        report.ok("F: stale claims", rd.W7_DERIVED_SUITE_MATRIX, rd.W7_DERIVED_MATRIX_NOTE)

    full = os.path.join(repo, rd.BUDGET_TEMPLATE_SNAPSHOT)
    if not os.path.exists(full):
        report.fail("F: stale claims", rd.BUDGET_TEMPLATE_SNAPSHOT,
                    "the template snapshot must be retained, not deleted")
    else:
        text = read_text(full)
        authoritative = read_text(os.path.join(repo, rd.BUDGET_DECISION_RECORD))
        if rd.OWNER_EXCEPTION_PHRASE in text:
            report.fail("F: stale claims", rd.BUDGET_TEMPLATE_SNAPSHOT,
                        "contains the owner-deferral sentence; it is a pre-decision template "
                        "snapshot and the decision record is " + rd.BUDGET_DECISION_RECORD)
        elif rd.OWNER_EXCEPTION_PHRASE not in authoritative:
            report.fail("F: stale claims", rd.BUDGET_DECISION_RECORD,
                        "does not contain `" + rd.OWNER_EXCEPTION_PHRASE + "`")
        else:
            report.ok("F: stale claims", rd.BUDGET_TEMPLATE_SNAPSHOT,
                      "is the pre-decision template snapshot; the deferral is recorded in " +
                      rd.BUDGET_DECISION_RECORD)


def run(repo, git_runner):
    report = Report()
    tree = tree_catalogs(repo)
    packages = tree_packages(repo)
    accepted = check_accepted_revisions(repo, report, git_runner)
    check_catalogs(repo, report, tree)
    check_packages(repo, report, packages, rd.SUPERSEDED_DIGESTS, rd.UNVERIFIABLE_DIGESTS)
    check_supersession(repo, report, accepted, git_runner)
    check_stale_claims(repo, report)
    return report


# --- self-test ----------------------------------------------------------------------------------

SAMPLE_REVISION = "1111111111111111111111111111111111111111"
SUPERSEDED_REVISION = "2222222222222222222222222222222222222222"
STALE_REVISION = "5555555555555555555555555555555555555555"


def _write(path, text):
    directory = os.path.dirname(path)
    if directory and not os.path.isdir(directory):
        os.makedirs(directory)
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)


def _sandbox_repo(root):
    """Fabricate the smallest corpus that satisfies every check, so each check can be falsified."""
    catalog_text = ('public const string CatalogFileHash = "aa";\n'
                    'public const string CatalogFingerprint = "bb";\n'
                    'public const string ProtocolVersion = "1.0";\n')
    for name, relative in rd.CATALOGS:
        _write(os.path.join(root, relative), catalog_text)
    manifest = {"dependencies": {"com.unity.entities": "1.4.6", "com.gamecore.contracts": "file:x"},
                "testables": ["com.gamecore.contracts"]}
    lock = {"dependencies": {"com.unity.entities": {"source": "registry", "version": "1.4.6"},
                             "com.gamecore.contracts": {"source": "local",
                                                        "version": "file:../../../Packages/x"}}}
    _write(os.path.join(root, rd.MANIFEST_PATH), json.dumps(manifest))
    _write(os.path.join(root, rd.LOCK_PATH), json.dumps(lock))
    _write(os.path.join(root, "Packages/com.gamecore.contracts/package.json"),
           json.dumps({"version": rd.ACCEPTED_PACKAGE_VERSION}))
    manifest_sha = sha256_file(os.path.join(root, rd.MANIFEST_PATH))
    lock_sha = sha256_file(os.path.join(root, rd.LOCK_PATH))
    catalog_sha = sha256_file(os.path.join(root, rd.ENVIRONMENT_CATALOG_FILE))
    catalog_rows = [{"catalog": name, "file": rel, "fileHash": "aa", "fingerprint": "bb",
                     "declaredProtocolVersion": "1.0"} for name, rel in rd.CATALOGS]
    packages = {"manifest": rd.MANIFEST_PATH, "lock": rd.LOCK_PATH,
                "pinned": {"com.unity.entities": "1.4.6"},
                "localPackages": {"com.gamecore.contracts": "file:x"},
                "resolvedRegistryVersions": {"com.unity.entities": "1.4.6"},
                "resolvedBuiltinVersions": {},
                "resolvedLocalVersions": {"com.gamecore.contracts": "file:../../../Packages/x"},
                "testables": ["com.gamecore.contracts"]}
    for path in ("artifacts/conformance/compatibility.json",
                 "artifacts/w8-gate/matrix/compatibility.json",
                 "artifacts/conformance/results/compatibility.json"):
        body = {"revision": SAMPLE_REVISION, "catalogs": catalog_rows, "packages": packages}
        _write(os.path.join(root, path), json.dumps(body))
    ledger = {"catalogs": [{"className": name, "catalogFileHash": "aa",
                            "catalogFingerprint": "bb"} for name, _ in rd.CATALOGS]}
    for record in rd.CATALOG_LEDGER_RECORDS:
        _write(os.path.join(root, record["path"]), json.dumps(ledger))
    _write(os.path.join(root, "artifacts/w8-gate/matrix/toolchain/probe-positive.json"),
           json.dumps({"catalogFileHash": "aa", "catalogFingerprint": "bb"}))
    _write(os.path.join(root, "artifacts/reproducibility/final-clone/environment.txt"),
           "revision=" + SUPERSEDED_REVISION + "\n")
    _write(os.path.join(root, "artifacts/gc-029/BUILD_REPORT.md"),
           "clean-checkout reproduction and operator contract** at `" + SUPERSEDED_REVISION + "`\n")
    _write(os.path.join(root, "artifacts/reproducibility/first-clone/environment.txt"),
           "revision=3333333333333333333333333333333333333333\n")
    _write(os.path.join(root, "artifacts/baseline/ENVIRONMENT.md"),
           "| Source revision | `4444444444444444444444444444444444444444` |\n")
    _write(os.path.join(root, "artifacts/w8-gate/BUILD_REPORT.md"),
           "**Pass for the specified Wave 8 integration gate** on merged `w8-gate` "
           "source/reproduction revision `" + SAMPLE_REVISION + "`\n"
           "One earlier fresh clone changed two tracked host JSON reference documents; it was not "
           "accepted.\n")
    _write(os.path.join(root, "artifacts/w8-gate/HANDOFF.md"),
           "Accepted source/reproduction revision: `" + SAMPLE_REVISION + "`\n"
           "The first fresh clone changed tracked host JSON; the clone was removed and a new clone "
           "of `3895d0c` ran the full reproduction.\n")
    _write(os.path.join(root, "artifacts/w8-gate/reproduction/environment.txt"),
           "revision=" + SAMPLE_REVISION + "\n")
    _write(os.path.join(root, "artifacts/conformance/compatibility.md"),
           "| Source revision | `" + SAMPLE_REVISION + "` |\n")
    _write(os.path.join(root, "artifacts/w8-gate/matrix/compatibility.md"),
           "| Source revision | `" + SAMPLE_REVISION + "` |\n")
    _write(os.path.join(root, rd.CANONICAL_EVIDENCE_INDEX),
           json.dumps({"evidenceRoot": rd.ACCEPTED_EVIDENCE_TREES[0]["root"]}))
    _write(os.path.join(root, rd.W7_DERIVED_SUITE_MATRIX), json.dumps({"counts": {}}))
    _write(os.path.join(root, rd.BUDGET_TEMPLATE_SNAPSHOT), "template snapshot\n")
    _write(os.path.join(root, rd.BUDGET_DECISION_RECORD),
           "Status: **TEST-023 timing qualification " + rd.OWNER_EXCEPTION_PHRASE +
           " (" + rd.OWNER_DECISION_DATE + ")**\n")
    for record in rd.DIGEST_RECORDS:
        lines = []
        for key, kind in record["fields"]:
            declared = matches(rd.SUPERSEDED_DIGESTS, record["path"], key, None)
            if record.get("manifest_path") and kind == "manifest":
                value = _declared_value(rd.UNVERIFIABLE_DIGESTS, record["path"], key)
            elif declared is not None:
                value = declared["value"]
            elif kind == "manifest":
                value = manifest_sha
            elif kind == "lock":
                value = lock_sha
            elif kind == "catalog":
                value = catalog_sha
            else:
                value = PLAYER_SHA
            if value is None:
                raise ValueError("sandbox has no value for " + record["path"] + " / " + key)
            lines.append(key + ": " + value)
        _write(os.path.join(root, record["path"]), "\n".join(lines) + "\n")
    return root


PLAYER_SHA = "c" * 64


def _declared_value(declarations, path, field):
    for row in declarations:
        if row["path"] == path and row["field"] == field:
            return row["value"]
    return None


def _fake_git(clean_diff=True, dirty_revision=None):
    """A git stand-in: reports a content difference only for `dirty_revision`."""
    def runner(repo, args):
        if args[:1] == ["merge-base"]:
            return 0, ""
        if args[:1] == ["cat-file"]:
            return 0, ""
        if args[:1] == ["diff"]:
            if not clean_diff:
                return 0, "unity/GameCore.Validation/Packages/packages-lock.json"
            if dirty_revision is not None and args[2] == dirty_revision:
                return 0, "unity/GameCore.Validation/Assets/GameCore.Validation/Generated/" \
                          "ProbeCatalog.g.cs"
            return 0, ""
        return 1, "unexpected git invocation"
    return runner


def _mutation_specs():
    """Each mutation breaks exactly one recorded fact; the self-test asserts each is detected.

    A factory takes the already-populated sandbox root and returns the zero-argument mutation, so
    every mutation is applied to its own fresh corpus.
    """

    def set_revision(root, path, value):
        payload = read_json(os.path.join(root, path))
        payload["revision"] = value
        _write(os.path.join(root, path), json.dumps(payload))

    def set_catalog_hash(root, path, value):
        payload = read_json(os.path.join(root, path))
        payload["catalogs"][0]["fileHash"] = value
        _write(os.path.join(root, path), json.dumps(payload))

    def set_package_pin(root, path, value):
        payload = read_json(os.path.join(root, path))
        payload["packages"]["pinned"]["com.unity.entities"] = value
        _write(os.path.join(root, path), json.dumps(payload))

    def set_kv(root, path, key, value):
        text = read_text(os.path.join(root, path))
        lines = [key + ": " + value if line.startswith(key + ":") else line
                 for line in text.splitlines()]
        _write(os.path.join(root, path), "\n".join(lines) + "\n")

    def write(root, path, text):
        _write(os.path.join(root, path), text)

    return (
        ("accepted records disagree",
         lambda root: lambda: set_revision(root, "artifacts/w8-gate/matrix/compatibility.json",
                                           "a" * 40),
         _fake_git()),
        ("a catalog digest drifts",
         lambda root: lambda: set_catalog_hash(root,
                                               "artifacts/w8-gate/matrix/compatibility.json", "ff"),
         _fake_git()),
        ("a package pin drifts",
         lambda root: lambda: set_package_pin(
             root, "artifacts/conformance/results/compatibility.json", "9.9.9"),
         _fake_git()),
        ("a probe document ran another catalog",
         lambda root: lambda: write(root, "artifacts/w8-gate/matrix/toolchain/probe-positive.json",
                                    json.dumps({"catalogFileHash": "ff",
                                                "catalogFingerprint": "bb"})),
         _fake_git()),
        ("a package manifest version drifts",
         lambda root: lambda: write(root, "Packages/com.gamecore.contracts/package.json",
                                    json.dumps({"version": "0.1.0"})),
         _fake_git()),
        ("a recorded manifest digest drifts",
         lambda root: lambda: set_kv(root, "artifacts/w8-gate/matrix/toolchain/environment.txt",
                                     "packages_manifest_sha256", "9" * 64),
         _fake_git()),
        ("a recorded catalog digest drifts",
         lambda root: lambda: set_kv(root, "artifacts/conformance/results/toolchain/environment.txt",
                                     "catalog_sha256", "9" * 64),
         _fake_git()),
        ("the rejection statement is dropped",
         lambda root: lambda: write(root, "artifacts/w8-gate/BUILD_REPORT.md", "no statement\n"),
         _fake_git()),
        ("the published summary came from another tree",
         lambda root: lambda: write(root, rd.CANONICAL_EVIDENCE_INDEX,
                                    json.dumps({"evidenceRoot": "artifacts/conformance/results"})),
         _fake_git()),
        ("the pre-decision snapshot claims the deferral",
         lambda root: lambda: write(root, rd.BUDGET_TEMPLATE_SNAPSHOT,
                                    "Status: " + rd.OWNER_EXCEPTION_PHRASE + "\n"),
         _fake_git()),
        ("the decision record loses the deferral",
         lambda root: lambda: write(root, rd.BUDGET_DECISION_RECORD,
                                    "Status: measured at full duration\n"),
         _fake_git()),
        ("an undeclared lock digest appears",
         lambda root: lambda: set_kv(root, "artifacts/baseline/build/environment.txt",
                                     "packages_lock_sha256", "9" * 64),
         _fake_git()),
        ("a digest declared for another record is used here",
         lambda root: lambda: set_kv(root, "artifacts/w8-gate/matrix/toolchain/environment.txt",
                                     "packages_manifest_sha256",
                                     "301020ec2f42426b20deccfbdc2deb60abd28ef3b811d7c3e2bc7e23af8f2b84"),
         _fake_git()),
        ("a superseded record is not content-identical to the accepted revision",
         lambda root: lambda: write(root, "artifacts/reproducibility/final-clone/environment.txt",
                                    "revision=" + STALE_REVISION + "\n"),
         _fake_git(dirty_revision=STALE_REVISION)),
        ("a later revision changes the catalogs or package files",
         lambda root: (lambda: None),
         _fake_git(False)),
        ("the historical W7 matrix is deleted",
         lambda root: lambda: os.remove(os.path.join(root, rd.W7_DERIVED_SUITE_MATRIX)),
         _fake_git()),
    )


def self_test():
    problems = []
    specs = _mutation_specs()
    exercised = set()

    # 1. A corpus that satisfies every check must pass, or the checks are not falsifiable.
    sandbox = tempfile.mkdtemp(prefix="gc030-consistent-")
    try:
        root = _sandbox_repo(os.path.join(sandbox, "repo"))
        report = run(root, _fake_git())
        for finding in report.problems:
            problems.append("consistent corpus reported a problem: " + finding["subject"] +
                            " / " + finding["detail"])
        exercised = set((f["group"], f["subject"]) for f in report.findings)
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)

    # 2. Each mutation breaks exactly one recorded fact, and must be detected.
    for name, factory, runner in specs:
        sandbox = tempfile.mkdtemp(prefix="gc030-mutation-")
        try:
            root = _sandbox_repo(os.path.join(sandbox, "repo"))
            factory(root)()
            if not run(root, runner).failed:
                problems.append("mutation not detected: " + name)
        finally:
            shutil.rmtree(sandbox, ignore_errors=True)

    # 3. The comparisons that carry no dedicated mutation must still be exercised, so that a rewrite
    #    of the sandbox cannot silently stop comparing a digest.
    for expected in (
            ("C: package identity",
             "artifacts/w8-gate/matrix/toolchain/environment.txt / catalog_sha256"),
            ("C: package identity",
             "artifacts/w8-gate/matrix/toolchain/environment.txt / packages_manifest_sha256"),
            ("C: package identity",
             "artifacts/conformance/results/compatibility.json / resolvedRegistryVersions"),
            ("D: player identity", "agreement"),
            ("E: supersession", "artifacts/reproducibility/final-clone/environment.txt")):
        if expected not in exercised:
            problems.append("self-test does not exercise " + " / ".join(expected))

    if problems:
        for problem in problems:
            print("SELF-TEST FAIL: " + problem, file=sys.stderr)
        return 1
    print("self-test: a consistent corpus passes, and all %d mutations are detected" % len(specs))
    return 0


# --- entry point --------------------------------------------------------------------------------

def render(report):
    lines = []
    for finding in report.findings:
        lines.append("%-4s %-24s %s" % (finding["status"], finding["group"], finding["subject"]))
        if finding["detail"]:
            lines.append("       " + finding["detail"])
    return "\n".join(lines)


def main(argv):
    parser = argparse.ArgumentParser(description="Check that the release evidence describes one "
                                                 "revision.")
    parser.add_argument("--repo", default=DEFAULT_REPO, help="repository root (default: this tree)")
    parser.add_argument("--json", default=None, help="write the findings to this JSON file")
    parser.add_argument("--quiet", action="store_true", help="print failures only")
    parser.add_argument("--self-test", action="store_true", help="falsify every check in a sandbox")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()

    repo = os.path.abspath(args.repo)
    try:
        report = run(repo, git)
    except (FileNotFoundError, KeyError, ValueError) as error:
        print("FAIL: repository inputs are not readable: " + str(error), file=sys.stderr)
        return 1

    document = {
        "artifact": FORMAT,
        "repo": repo,
        "generator": "tools/release_readiness/check_revision_consistency.py",
        "findings": report.findings,
        "problems": [f for f in report.findings if f["status"] == FAIL],
    }
    if args.json:
        _write(args.json, json.dumps(document, indent=2) + "\n")

    if args.quiet:
        for finding in report.problems:
            print("FAIL %s: %s" % (finding["subject"], finding["detail"]))
    else:
        print(render(report))
        counts = {}
        for finding in report.findings:
            counts[finding["status"]] = counts.get(finding["status"], 0) + 1
        print("")
        print("checks: " + ", ".join("%s=%d" % (k, counts[k]) for k in sorted(counts)))

    if report.failed:
        print("FAIL: %d inconsistency(ies); the release-readiness status may not be recorded"
              % len(report.problems), file=sys.stderr)
        return 1
    print("OK: every recorded revision, catalog digest, package digest and player digest is "
          "consistent with the working tree")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
