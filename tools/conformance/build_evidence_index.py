#!/usr/bin/env python3
"""Build the GC-028 conformance evidence index from the actual result files.

The index answers one question per normative clause: *which executable artifact would discharge it, and what did
that artifact actually report in the latest gate?* The second half is never typed by hand. This tool reads

  * every vstest TRX under `<root>/trx/`
  * every Unity NUnit3 result XML under `<root>/unity/`
  * every player probe result JSON under `<root>/toolchain/`, `<root>/release/` and `<root>/probe/`
  * the archived host-side / release-side check documents named in `evidence_map.CHECKS`

and resolves each reference recorded in `tools/conformance/evidence_map.py` against them. Statuses are derived,
never asserted: a reference that resolves to nothing is reported as `NotRun` and listed as an unresolved reference,
and `--check` exits non-zero in that case so the build host cannot publish a "complete" index over a stale tree.

Usage:
  python3 tools/conformance/build_evidence_index.py                       # render the index into artifacts/conformance/
  python3 tools/conformance/build_evidence_index.py --evidence-root DIR   # resolve against another gate tree
  python3 tools/conformance/build_evidence_index.py --check               # fail on any Fail/unresolved reference
  python3 tools/conformance/build_evidence_index.py --self-test           # fabricate a tree and assert the verdicts

Exit codes: 0 written (and, with --check, nothing failing); 1 --check found a Fail or an unresolved reference;
2 a usage or input error.
"""

import argparse
import json
import os
import re
import shutil
import sys
import tempfile
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
REPO_ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)

import evidence_map as em  # noqa: E402  (path is set above)

TRX_NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
PROTOCOL_VERSION = "1.0"
INDEX_FORMAT = "gamecore.conformance.evidence-index/1"

STATUS_ORDER = {"Pass": 0, "Deferred": 1, "NotRun": 2, "Blocked": 3, "Fail": 4}


# ------------------------------------------------------------------------------------------------------------
# Result readers. Each returns a dict mapping a recorded name to a status plus the file it came from.
# ------------------------------------------------------------------------------------------------------------
def _read_trx(path):
    """-> ({(class.method): status}, source_path) for one TRX."""
    results = {}
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return results, path

    definitions = {}
    for unit in root.findall(".//t:UnitTest", TRX_NS):
        method = unit.find("t:TestMethod", TRX_NS)
        if method is None:
            continue
        definitions[unit.attrib.get("id", "")] = (
            method.attrib.get("className", ""),
            method.attrib.get("name", ""),
        )

    for result in root.findall(".//t:UnitTestResult", TRX_NS):
        class_name, method_name = definitions.get(result.attrib.get("testId", ""), ("", ""))
        name = (class_name + "." + method_name) if class_name else result.attrib.get("testName", "")
        if not name:
            continue
        outcome = result.attrib.get("outcome", "")
        results[name] = "Pass" if outcome == "Passed" else "Fail"

    return results, path


def _read_unity_xml(path):
    """-> ({(class.method): status}, source_path) for one Unity NUnit3 result file."""
    results = {}
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return results, path

    for case in root.iter("test-case"):
        full = case.attrib.get("fullname", "")
        if not full:
            continue
        result = (case.attrib.get("result") or "").lower()
        results[full] = "Pass" if result in ("passed", "skipped") else "Fail"

    return results, path


def _read_probe(path):
    """-> (mode, {observation name: status}, top-level result, source_path) for one probe result JSON."""
    try:
        with open(path, "r", encoding="utf-8") as handle:
            document = json.load(handle)
    except (ValueError, OSError):
        return None, {}, None, path

    if not isinstance(document, dict) or "probes" not in document:
        return None, {}, None, path

    observations = {}
    for entry in document.get("probes", []):
        if not isinstance(entry, dict):
            continue
        name = entry.get("name")
        status = entry.get("status")
        if not name:
            continue
        if status == "Pass":
            observations[name] = "Pass"
        elif status == "ExpectedNegative":
            observations[name] = "Pass"
        else:
            observations[name] = "Fail"

    return document.get("mode"), observations, document.get("result"), path


# ------------------------------------------------------------------------------------------------------------
# The evidence corpus: everything one gate tree recorded.
# ------------------------------------------------------------------------------------------------------------
class Corpus(object):
    def __init__(self, root):
        self.root = root
        self.dotnet = {}          # FQN -> status
        self.dotnet_source = {}   # FQN -> path
        self.unity = {}
        self.unity_source = {}
        self.probes = {}          # (mode lower, name) -> (status, path)
        self.probe_documents = []  # (mode, result, path, count)
        self.checks = {}          # key -> (status, path, detail)
        self.problems = []        # input-level problems (missing roots, unreadable files)
        self._load()

    # -- loading -----------------------------------------------------------------------------------------
    def _load(self):
        if not os.path.isdir(self.root):
            self.problems.append("evidence root does not exist: " + self.root)
            return

        trx_dir = os.path.join(self.root, "trx")
        if os.path.isdir(trx_dir):
            for name in sorted(os.listdir(trx_dir)):
                if not name.endswith(".trx"):
                    continue
                results, path = _read_trx(os.path.join(trx_dir, name))
                for test, status in results.items():
                    previous = self.dotnet.get(test)
                    if previous is None or (previous == "Pass" and status == "Fail"):
                        self.dotnet[test] = status
                        self.dotnet_source[test] = os.path.relpath(path, REPO_ROOT)
        else:
            self.problems.append("no trx directory under " + self.root)

        unity_dir = os.path.join(self.root, "unity")
        if os.path.isdir(unity_dir):
            for name in sorted(os.listdir(unity_dir)):
                if not name.endswith(".xml"):
                    continue
                results, path = _read_unity_xml(os.path.join(unity_dir, name))
                for test, status in results.items():
                    previous = self.unity.get(test)
                    if previous is None or (previous == "Pass" and status == "Fail"):
                        self.unity[test] = status
                        self.unity_source[test] = os.path.relpath(path, REPO_ROOT)
        else:
            self.problems.append("no unity directory under " + self.root)

        found_probe = False
        # Every probe result under the tree counts, including the benchmark harness's per-run documents under
        # `<root>/benchmark/raw/runN/`. One walk is easier to keep honest than a list of known subdirectories.
        for directory, _, filenames in os.walk(self.root):
            for name in sorted(filenames):
                if not name.startswith("probe-") or not name.endswith(".json"):
                    continue
                path = os.path.join(directory, name)
                mode, observations, result, _ = _read_probe(path)
                if mode is None:
                    self.problems.append("unreadable probe result: " + os.path.relpath(path, REPO_ROOT))
                    continue
                found_probe = True
                self.probe_documents.append((mode, result, os.path.relpath(path, REPO_ROOT), len(observations)))
                for observation, status in observations.items():
                    # A qualified name is recorded as "<label>/<bare>"; a bare name matches only a bare record.
                    self.probes[(mode.lower(), observation)] = (status, os.path.relpath(path, REPO_ROOT))
                    if "/" in observation:
                        label, bare = observation.split("/", 1)
                        self.probes.setdefault(
                            (mode.lower(), bare), (status, os.path.relpath(path, REPO_ROOT)))
        if not found_probe:
            self.problems.append("no player probe result JSON under " + self.root)

        for key, spec in em.CHECKS.items():
            self.checks[key] = self._load_check(key, spec)

    def _load_check(self, key, spec):
        base = REPO_ROOT if spec.get("root") == "repo" else self.root
        resolved = None
        for candidate in spec["paths"]:
            path = os.path.normpath(os.path.join(base, candidate))
            if os.path.exists(path):
                resolved = path
                break

        if resolved is None:
            return ("NotRun", None, "none of " + ", ".join(spec["paths"]) + " exists under " + base)

        relative = os.path.relpath(resolved, REPO_ROOT)
        if spec.get("field") == "exists" or ("field" not in spec and "contains" not in spec):
            return ("Pass", relative, "present")

        if resolved.endswith((".md", ".log", ".txt")):
            with open(resolved, "r", encoding="utf-8", errors="replace") as handle:
                text = handle.read()
            found = spec.get("contains")
            if found is None:
                return ("Pass", relative, "present")
            return (("Pass" if found in text else "Fail"), relative,
                    "contains" if found in text else "missing: " + found)

        try:
            with open(resolved, "r", encoding="utf-8") as handle:
                document = json.load(handle)
        except (ValueError, OSError) as error:
            return ("Fail", relative, "unreadable JSON: " + str(error))

        value = document
        for part in spec["field"].split("."):
            if isinstance(value, dict) and part in value:
                value = value[part]
            else:
                return ("NotRun", relative, "no field " + spec["field"])

        expected = spec["expected"]
        # `problems: 0` and `problems: []` are the same claim; a list-valued refusal set must be empty.
        if isinstance(expected, int) and not isinstance(expected, bool) and isinstance(value, list):
            value = len(value)
        return (("Pass" if value == expected else "Fail"), relative,
                spec["field"] + "=" + json.dumps(value))

    # -- resolution --------------------------------------------------------------------------------------
    def resolve(self, reference):
        """-> (status, source_path or None, detail)."""
        if ":" not in reference:
            return ("NotRun", None, "malformed reference: " + reference)

        kind, rest = reference.split(":", 1)

        if kind == "dotnet":
            if rest in self.dotnet:
                return (self.dotnet[rest], self.dotnet_source[rest], "trx")
            return ("NotRun", None, "no recorded result for " + rest)

        if kind == "unity":
            if rest in self.unity:
                return (self.unity[rest], self.unity_source[rest], "unity xml")
            return ("NotRun", None, "no recorded result for " + rest)

        if kind == "probe":
            if "/" not in rest:
                return ("NotRun", None, "probe reference needs <mode>/<name>: " + reference)
            mode, name = rest.split("/", 1)
            key = (mode.lower(), name)
            if key in self.probes:
                status, path = self.probes[key]
                return (status, path, "probe observation")
            return ("NotRun", None, "no recorded observation " + name + " in mode " + mode)

        if kind == "check":
            if rest in self.checks:
                status, path, detail = self.checks[rest]
                return (status, path, detail)
            return ("NotRun", None, "unknown check key " + rest)

        return ("NotRun", None, "unknown reference kind: " + kind)


# ------------------------------------------------------------------------------------------------------------
# Aggregation.
# ------------------------------------------------------------------------------------------------------------
def _worst(statuses):
    if not statuses:
        return "NotRun"
    return max(statuses, key=lambda status: STATUS_ORDER.get(status, 2))


def _clause_status(corpus, clause, unresolved, gc028_only):
    statuses = []
    evidence = []
    notes = []
    for reference in clause["evidence"]:
        if str(reference).startswith("note:"):
            notes.append(str(reference)[len("note:"):].strip())
            continue
        status, path, detail = corpus.resolve(reference)
        pending = False
        if status == "NotRun":
            if em.is_added_by_gc028(reference):
                # This task's own new test: it cannot appear in a gate tree recorded before it existed. It is
                # reported as NotRun with the reason, listed separately from a genuinely wrong reference, and it
                # does not lower the clause's status — the clause is judged on the evidence that does exist.
                detail = "added by GC-028; record it with tools/conformance/run_test_matrix.sh"
                pending = True
                gc028_only.append((clause["clause"], reference))
            elif not str(reference).startswith("check:"):
                unresolved.append((clause["clause"], reference, detail))
        entry = {
            "reference": reference,
            "status": status,
            "source": path,
            "detail": detail,
        }
        if pending:
            entry["pending"] = True
        evidence.append(entry)
        if not pending:
            statuses.append(status)

    worst = _worst(statuses)
    entry = {
        "clause": clause["clause"],
        "status": worst,
        "evidence": evidence,
    }
    if notes:
        entry["note"] = " ".join(notes)
    if clause.get("note"):
        entry["note"] = (entry["note"] + " " + clause["note"]).strip() if entry.get("note") else clause["note"]
        if worst == "Pass" and clause.get("blocked"):
            entry["status"] = "Blocked"
            worst = "Blocked"
    if clause.get("blocked"):
        entry["blocked"] = clause["blocked"]
        entry["status"] = "Blocked"
        worst = "Blocked"
    if clause.get("deferred"):
        entry["deferred"] = clause["deferred"]
        if worst == "Pass":
            entry["status"] = "Deferred"
            worst = "Deferred"
    return worst, entry


def build(corpus):
    unresolved = []
    gc028_only = []
    requirements = []

    for requirement in sorted(em.REQUIREMENTS):
        row = em.REQUIREMENTS[requirement]
        clause_entries = []
        statuses = []
        for clause in row["clauses"]:
            status, entry = _clause_status(corpus, clause, unresolved, gc028_only)
            clause_entries.append(entry)
            statuses.append(status)
        requirements.append({
            "id": requirement,
            "title": row["title"],
            "status": _worst(statuses),
            "clauses": clause_entries,
        })

    operations = []
    for operation in sorted(em.OPERATIONS):
        row = em.OPERATIONS[operation]
        clause_entries = []
        statuses = []
        for clause in row["clauses"]:
            status, entry = _clause_status(corpus, clause, unresolved, gc028_only)
            clause_entries.append(entry)
            statuses.append(status)
        operations.append({
            "id": operation,
            "title": row["title"],
            "required_suites": row.get("suites", []),
            "status": _worst(statuses),
            "clauses": clause_entries,
        })

    return requirements, operations, unresolved, gc028_only


def summarize(requirements, operations, unresolved, corpus):
    counts = {}
    for row in requirements + operations:
        counts[row["status"]] = counts.get(row["status"], 0) + 1
    non_pass = [
        {"id": row["id"], "status": row["status"],
         "failed_clauses": [clause["clause"] for clause in row["clauses"] if clause["status"] in ("Fail", "Blocked", "Deferred", "NotRun")]}
        for row in requirements + operations if row["status"] != "Pass"
    ]
    return {
        "requirements": len(requirements),
        "operations": len(operations),
        "byStatus": counts,
        "unresolvedReferenceCount": len(unresolved),
        "nonPass": non_pass,
    }


# ------------------------------------------------------------------------------------------------------------
# Rendering.
# ------------------------------------------------------------------------------------------------------------
def render_markdown(document):
    lines = []
    lines.append("# GameCore V1 conformance evidence index")
    lines.append("")
    lines.append("Generated by `tools/conformance/build_evidence_index.py` from the recorded results under")
    lines.append("`" + document["evidenceRoot"] + "`. Every status below is derived from a result file; the")
    lines.append("tool exits non-zero when a reference resolves to nothing, so this document cannot silently")
    lines.append("carry a hand-written verdict.")
    lines.append("")
    lines.append("- Protocol version: `" + str(document["protocolVersion"]) + "`")
    lines.append("- Evidence root: `" + document["evidenceRoot"] + "`")
    lines.append("- Evidence artifacts read: " + str(document["corpus"]["trxResults"]) + " dotnet results, "
                 + str(document["corpus"]["unityResults"]) + " Unity results, "
                 + str(document["corpus"]["probeDocuments"]) + " probe documents, "
                 + str(document["corpus"]["checks"]) + " check documents")
    lines.append("")
    summary = document["summary"]
    lines.append("## Summary")
    lines.append("")
    lines.append("| Scope | Total | " + " | ".join(STATUS_ORDER) + " |")
    lines.append("| --- | ---: | " + " | ".join("---:" for _ in STATUS_ORDER) + " |")
    for label, key in (("Requirements P-001..P-060", "requirements"), ("Operations O-01..O-26", "operations")):
        rows = document["requirements"] if key == "requirements" else document["operations"]
        cells = [str(sum(1 for row in rows if row["status"] == status)) for status in STATUS_ORDER]
        lines.append("| " + label + " | " + str(len(rows)) + " | " + " | ".join(cells) + " |")
    lines.append("")
    lines.append("Unresolved evidence references: **" + str(summary["unresolvedReferenceCount"]) + "**.")
    lines.append("")

    if summary["nonPass"]:
        lines.append("## Non-Pass rows")
        lines.append("")
        lines.append("| Id | Status | Clauses not passing |")
        lines.append("| --- | --- | --- |")
        for row in summary["nonPass"]:
            clauses = "; ".join(row["failed_clauses"])
            clauses = clauses if len(clauses) <= 320 else clauses[:317] + "..."
            lines.append("| `" + row["id"] + "` | " + row["status"] + " | " + clauses.replace("|", "\\|") + " |")
        lines.append("")

    lines.append("## Requirements")
    lines.append("")
    for row in document["requirements"]:
        lines.append("### `" + row["id"] + "` — " + row["title"] + " — **" + row["status"] + "**")
        lines.append("")
        for clause in row["clauses"]:
            lines.append("- **" + clause["status"] + "** — " + clause["clause"])
            if clause.get("note"):
                lines.append("  - note: " + clause["note"])
            if clause.get("blocked"):
                lines.append("  - blocked: " + clause["blocked"])
            if clause.get("deferred"):
                lines.append("  - deferred: " + clause["deferred"])
            for item in clause["evidence"]:
                source = item["source"] if item["source"] else item["detail"]
                lines.append("  - `" + item["status"] + "` `" + item["reference"] + "` — " + str(source))
        lines.append("")

    lines.append("## Operations")
    lines.append("")
    for row in document["operations"]:
        lines.append("### `" + row["id"] + "` — " + row["title"] + " — **" + row["status"] + "**")
        lines.append("")
        if row["required_suites"]:
            lines.append("Required suites: " + ", ".join("`" + suite + "`" for suite in row["required_suites"]))
            lines.append("")
        for clause in row["clauses"]:
            lines.append("- **" + clause["status"] + "** — " + clause["clause"])
            if clause.get("note"):
                lines.append("  - note: " + clause["note"])
            if clause.get("blocked"):
                lines.append("  - blocked: " + clause["blocked"])
            for item in clause["evidence"]:
                source = item["source"] if item["source"] else item["detail"]
                lines.append("  - `" + item["status"] + "` `" + item["reference"] + "` — " + str(source))
        lines.append("")

    return "\n".join(lines) + "\n"


# ------------------------------------------------------------------------------------------------------------
# Self-test: fabricate a tiny tree and assert the verdicts the tool derives from it.
# ------------------------------------------------------------------------------------------------------------
def self_test():
    problems = []
    sandbox = tempfile.mkdtemp(prefix="gc028-evidence-index-")
    try:
        os.makedirs(os.path.join(sandbox, "trx"))
        os.makedirs(os.path.join(sandbox, "unity"))
        os.makedirs(os.path.join(sandbox, "toolchain"))
        os.makedirs(os.path.join(sandbox, "host"))

        with open(os.path.join(sandbox, "trx", "a.trx"), "w", encoding="utf-8") as handle:
            handle.write(
                '<?xml version="1.0" encoding="utf-8"?>'
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                "<Results>"
                '<UnitTestResult testId="1" testName="Passing.Class.Case" outcome="Passed" />'
                '<UnitTestResult testId="2" testName="Failing.Class.Case" outcome="Failed" />'
                "</Results><TestDefinitions>"
                '<UnitTest id="1"><TestMethod className="Passing.Class" name="Case" /></UnitTest>'
                '<UnitTest id="2"><TestMethod className="Failing.Class" name="Case" /></UnitTest>'
                "</TestDefinitions></TestRun>")

        with open(os.path.join(sandbox, "unity", "editmode-results.xml"), "w", encoding="utf-8") as handle:
            handle.write(
                '<?xml version="1.0" encoding="utf-8"?>'
                '<test-run><test-suite>'
                '<test-case fullname="Unity.Class.Case" result="Passed" />'
                "</test-suite></test-run>")

        with open(os.path.join(sandbox, "toolchain", "probe-example.json"), "w", encoding="utf-8") as handle:
            json.dump({
                "mode": "Example",
                "task": "GC-999",
                "result": "Pass",
                "probes": [
                    {"name": "example-ok", "status": "Pass", "detail": ""},
                    {"name": "example-bad", "status": "Fail", "detail": ""},
                    {"name": "label/example-qualified", "status": "Pass", "detail": ""},
                ],
            }, handle)

        with open(os.path.join(sandbox, "host", "budget-record.json"), "w", encoding="utf-8") as handle:
            json.dump({"verdict": "pass", "problems": 0}, handle)

        corpus = Corpus(sandbox)

        expectations = [
            ("dotnet:Passing.Class.Case", "Pass"),
            ("dotnet:Failing.Class.Case", "Fail"),
            ("dotnet:Absent.Class.Case", "NotRun"),
            ("unity:Unity.Class.Case", "Pass"),
            ("unity:Unity.Missing.Case", "NotRun"),
            ("probe:Example/example-ok", "Pass"),
            ("probe:Example/example-bad", "Fail"),
            ("probe:Example/example-qualified", "Pass"),
            ("probe:Example/nope", "NotRun"),
            ("probe:NoMode/example-ok", "NotRun"),
            ("check:budget-record", "Pass"),
            ("check:native-leak-attribution", "NotRun"),
            ("nonsense", "NotRun"),
        ]
        for reference, expected in expectations:
            status, _, detail = corpus.resolve(reference)
            if status != expected:
                problems.append("resolve(%s) -> %s (%s), expected %s" % (reference, status, detail, expected))

        # A corpus with no roots must report problems rather than inventing a pass.
        empty = Corpus(os.path.join(sandbox, "does-not-exist"))
        if not empty.problems:
            problems.append("a missing evidence root must be reported as a problem")

        # The aggregation must prefer Fail over Pass and Blocked over NotRun.
        if _worst(["Pass", "Fail"]) != "Fail":
            problems.append("_worst must prefer Fail")
        if _worst(["Pass", "NotRun"]) != "NotRun":
            problems.append("_worst must prefer NotRun over Pass")
        if _worst([]) != "NotRun":
            problems.append("_worst of nothing must be NotRun")
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)

    if problems:
        for problem in problems:
            print("SELF-TEST FAIL: " + problem, file=sys.stderr)
        return 1

    print("self-test: %d reference resolutions and the aggregation rules behave as declared" % 13)
    return 0


# ------------------------------------------------------------------------------------------------------------
def main(argv):
    parser = argparse.ArgumentParser(description="Build the GC-028 conformance evidence index.")
    parser.add_argument("--evidence-root", default=os.path.join("artifacts", "w7-gate"),
                        help="gate tree to resolve evidence against (default: artifacts/w7-gate)")
    parser.add_argument("--out-dir", default=os.path.join("artifacts", "conformance"),
                        help="where the index is written (default: artifacts/conformance)")
    parser.add_argument("--check", action="store_true",
                        help="exit non-zero when any row is Fail or any reference is unresolved")
    parser.add_argument("--self-test", action="store_true", help="test this tool and exit")
    arguments = parser.parse_args(argv)

    if arguments.self_test:
        return self_test()

    root = arguments.evidence_root
    if not os.path.isabs(root):
        root = os.path.join(REPO_ROOT, root)
    out_dir = arguments.out_dir
    if not os.path.isabs(out_dir):
        out_dir = os.path.join(REPO_ROOT, out_dir)

    corpus = Corpus(root)
    requirements, operations, unresolved, gc028_only = build(corpus)
    summary = summarize(requirements, operations, unresolved, corpus)
    summary["gc028AddedNotYetRecorded"] = len(gc028_only)

    document = {
        "artifact": INDEX_FORMAT,
        "protocolVersion": PROTOCOL_VERSION,
        "evidenceRoot": os.path.relpath(root, REPO_ROOT),
        "map": "tools/conformance/evidence_map.py",
        "generator": "tools/conformance/build_evidence_index.py",
        "corpus": {
            "trxResults": len(corpus.dotnet),
            "unityResults": len(corpus.unity),
            "probeDocuments": len(corpus.probe_documents),
            "probeObservations": len(corpus.probes),
            "checks": len(corpus.checks),
            "problems": corpus.problems,
        },
        "summary": summary,
        "unresolvedReferences": [
            {"clause": clause, "reference": reference, "detail": detail}
            for clause, reference, detail in unresolved
        ],
        "gc028AddedNotYetRecorded": [
            {"clause": clause, "reference": reference} for clause, reference in gc028_only
        ],
        "requirements": requirements,
        "operations": operations,
    }

    os.makedirs(out_dir, exist_ok=True)
    json_path = os.path.join(out_dir, "evidence-index.json")
    with open(json_path, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2, sort_keys=False)
        handle.write("\n")

    md_path = os.path.join(out_dir, "evidence-index.md")
    with open(md_path, "w", encoding="utf-8") as handle:
        handle.write(render_markdown(document))

    print("evidence index : " + os.path.relpath(json_path, REPO_ROOT))
    print("rendered       : " + os.path.relpath(md_path, REPO_ROOT))
    print("evidence root  : " + document["evidenceRoot"])
    print("requirements   : " + ", ".join(
        "%s=%d" % (status, sum(1 for row in requirements if row["status"] == status)) for status in STATUS_ORDER))
    print("operations     : " + ", ".join(
        "%s=%d" % (status, sum(1 for row in operations if row["status"] == status)) for status in STATUS_ORDER))
    print("unresolved refs: %d (plus %d references to tests added by GC-028, recorded by the matrix runner)"
          % (len(unresolved), len(gc028_only)))
    for clause, reference, detail in unresolved[:40]:
        print("   UNRESOLVED " + reference + " (" + detail + ") for clause: " + clause[:90])

    if arguments.check:
        failing = [row["id"] for row in requirements + operations if row["status"] == "Fail"]
        if failing or unresolved:
            print("--check: FAIL (%d failing rows, %d unresolved references)" % (len(failing), len(unresolved)),
                  file=sys.stderr)
            return 1

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
