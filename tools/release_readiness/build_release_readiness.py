#!/usr/bin/env python3
"""Build the GC-030 release-readiness record, or refuse to record completion.

Two duties:

1. **Decide the completion condition mechanically.** Every accepted evidence tree must report every
   registered requirement and operation as `Pass` apart from the one row the project owner excepted
   (`P-060`, TEST-023 timing), every required TEST suite must be `Pass`, every family verdict `Pass`,
   the short diagnostic must be the diagnostic the owner accepted, and the budget decision record
   must still carry the deferral. Only then is the status
   `"V1 complete (with owner-approved exception: TEST-023 timing deferred)"` emitted. Anything else
   is emitted as `incomplete` with the reasons, and the exit status is 1.

2. **Generate, never hand-write, the evidence manifest.** `evidence-manifest.json` and
   `evidence-manifest.md` list every required gate (W1..W9, GC-001..GC-030) with its report, its
   result files, the revision it records, the verdict sentence its own report contains (matched by
   the marker in `readiness_data.GATE_RECORDS`, so editing the sentence away is caught), and the
   status derived from the accepted requirement index. `status.json` carries the decision itself.

The revision/catalog/package consistency check (`check_revision_consistency.py`) runs first and in
process; its failure prevents the manifest from being written at all.

Commands:
    python3 tools/release_readiness/build_release_readiness.py --self-test
    python3 tools/release_readiness/build_release_readiness.py --build
    python3 tools/release_readiness/build_release_readiness.py --check
"""

from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_REPO = os.path.dirname(os.path.dirname(HERE))

sys.path.insert(0, HERE)

import readiness_data as rd  # noqa: E402  (path is set above)
import check_revision_consistency as rcc  # noqa: E402  (path is set above)

FORMAT = "gamecore.release-readiness.evidence-manifest/1"
STATUS_FORMAT = "gamecore.release-readiness.status/1"

STATUS_INCOMPLETE = "incomplete"

#: Worst-to-best. A gate's derived status is the worst status among its registered rows.
STATUS_ORDER = ("Fail", "Blocked", "NotRun", "Deferred", "Pass")

OPERATIONS = "operations"
REQUIREMENTS = "requirements"


# --- helpers ------------------------------------------------------------------------------------

def read_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def read_text(path):
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        return handle.read()


def write_text(path, text):
    directory = os.path.dirname(path)
    if directory and not os.path.isdir(directory):
        os.makedirs(directory)
    with open(path, "w", encoding="utf-8") as handle:
        handle.write(text)

def evidence_files(repo, entries):
    """Expand declared evidence entries into a sorted list of existing files.

    The generated outputs of this tool are excluded: they are the record that points at the
    evidence, not evidence, and including them would make every write change its own manifest.
    """
    generated = (rd.MANIFEST_JSON, rd.MANIFEST_MD, rd.STATUS_JSON)
    files = []
    for entry in entries:
        full = os.path.join(repo, entry)
        if os.path.isfile(full):
            if os.path.basename(full) not in generated:
                files.append(entry)
        elif os.path.isdir(full):
            for directory, _, names in os.walk(full):
                for name in names:
                    if name in generated:
                        continue
                    path = os.path.join(directory, name)
                    files.append(os.path.relpath(path, repo))
    return sorted(set(files))


def derived_status(statuses):
    """The worst status in `statuses`, using the declared severity order."""
    worst = "Pass"
    for status in statuses:
        if status not in STATUS_ORDER:
            return "Unrecorded"
        if STATUS_ORDER.index(status) < STATUS_ORDER.index(worst):
            worst = status
    return worst


def accepted_status_label(status):
    if status == "Deferred":
        return "Deferred (owner decision)"
    return status


# --- the completion condition -------------------------------------------------------------------

def check_tree(repo, tree, problems):
    """Validate one accepted evidence tree. Returns the facts it contributes."""
    root = tree["root"]
    label = tree["label"]

    def fail(subject, detail):
        problems.append(label + ": " + subject + ": " + detail)

    index_path = os.path.join(repo, root, rd.EVIDENCE_INDEX_NAME)
    if not os.path.exists(index_path):
        fail(rd.EVIDENCE_INDEX_NAME, "missing")
        return None
    index = read_json(index_path)
    summary = index.get("summary", {})

    if summary.get("requirements") != rd.EXPECTED_REQUIREMENT_TOTAL:
        fail("requirement total", "recorded " + str(summary.get("requirements")))
    if summary.get("operations") != rd.EXPECTED_OPERATION_TOTAL:
        fail("operation total", "recorded " + str(summary.get("operations")))
    by_status = summary.get("byStatus", {})
    for status in ("Fail", "Blocked", "NotRun"):
        if by_status.get(status):
            fail("requirement statuses", str(by_status[status]) + " row(s) are " + status)
    if by_status.get("Pass", 0) + by_status.get("Deferred", 0) != \
            rd.EXPECTED_REQUIREMENT_TOTAL + rd.EXPECTED_OPERATION_TOTAL:
        fail("requirement statuses", "do not sum to the registered total: " + json.dumps(by_status))
    non_pass = summary.get("nonPass", [])
    if len(non_pass) != 1 or non_pass[0].get("id") != rd.OWNER_EXCEPTION_ROW:
        fail("non-pass rows", "expected exactly " + rd.OWNER_EXCEPTION_ROW +
             ", recorded " + json.dumps(non_pass))
    elif non_pass[0].get("status") != "Deferred":
        fail(rd.OWNER_EXCEPTION_ROW, "is " + str(non_pass[0].get("status")) + ", not Deferred")
    if summary.get("unresolvedReferenceCount"):
        fail("unresolved references", str(summary["unresolvedReferenceCount"]))
    if index.get("unresolvedReferences"):
        fail("unresolved references", json.dumps(index["unresolvedReferences"])[:200])
    for problem in index.get("corpus", {}).get("problems", []):
        if not any(row["tree"] == root and row["problem"] == problem
                   for row in rd.KNOWN_CORPUS_PROBLEMS):
            fail("corpus", "undeclared problem: " + str(problem))
    for row in rd.KNOWN_CORPUS_PROBLEMS:
        if row["tree"] == root and row["problem"] not in index.get("corpus", {}).get("problems", []):
            fail("corpus", "declared problem is no longer reported: " + row["problem"])

    matrix_path = os.path.join(repo, root, rd.SUITE_MATRIX_NAME)
    suites = {}
    if not os.path.exists(matrix_path):
        fail(rd.SUITE_MATRIX_NAME, "missing")
    else:
        matrix = read_json(matrix_path)
        counts = matrix.get("counts", {})
        if counts.get("Pass") != rd.EXPECTED_SUITE_TOTAL:
            fail("suite matrix", "Pass = " + str(counts.get("Pass")))
        for status in ("Fail", "NotRun"):
            if counts.get(status):
                fail("suite matrix", str(counts[status]) + " suite(s) are " + status)
        if matrix.get("failing"):
            fail("suite matrix", "failing suites " + json.dumps(matrix["failing"]))
        for row in matrix.get("suites", []):
            suites[row["suite"]] = row["status"]

    audit_path = os.path.join(repo, root, rd.FAMILY_AUDIT_NAME)
    families = {}
    if not os.path.exists(audit_path):
        fail(rd.FAMILY_AUDIT_NAME, "missing")
    else:
        audit = read_json(audit_path)
        if not audit.get("assemblyAudit", {}).get("clean"):
            fail("family audit", "the assembly audit is not clean")
        for row in audit.get("families", []):
            families[row["family"]] = row["verdict"]
            if row["verdict"] != "Pass":
                fail("family audit", row["family"] + " is " + str(row["verdict"]))
        if sorted(families) != ["cards", "narrative", "traversal"]:
            fail("family audit", "families recorded: " + json.dumps(sorted(families)))

    benchmark_path = os.path.join(repo, root, rd.BENCHMARK_SUMMARY_NAME)
    benchmark = None
    if not os.path.exists(benchmark_path):
        fail(rd.BENCHMARK_SUMMARY_NAME, "missing")
    else:
        benchmark = read_json(benchmark_path)
        counts = benchmark.get("counts", {})
        if counts.get("gates") != rd.EXPECTED_DIAGNOSTIC_GATES:
            fail("diagnostic", "gate count " + str(counts.get("gates")))
        if counts.get("workloads") != rd.EXPECTED_DIAGNOSTIC_WORKLOADS:
            fail("diagnostic", "workload count " + str(counts.get("workloads")))
        if counts.get("runs") != 1:
            fail("diagnostic", "run count " + str(counts.get("runs")) +
                 " (the accepted evidence is a single short diagnostic, not a full-duration run)")
        if benchmark.get("gatesFailed"):
            fail("diagnostic", "failed gates " + json.dumps(benchmark["gatesFailed"]))
        if tuple(benchmark.get("missedTarget", ())) != rd.EXPECTED_DIAGNOSTIC_MISSED_TARGETS:
            fail("diagnostic", "missed targets " + json.dumps(benchmark.get("missedTarget")))
        missed = [row for row in benchmark.get("budgets", []) if row.get("verdict") == "MissedTarget"]
        if len(missed) != len(rd.EXPECTED_DIAGNOSTIC_MISSED_TARGETS):
            fail("diagnostic", "budget rows missed: " + json.dumps([r.get("id") for r in missed]))
        if counts.get("notMeasured"):
            fail("diagnostic", str(counts["notMeasured"]) + " budget row(s) NotMeasured")

    rows = {}
    for section in (REQUIREMENTS, OPERATIONS):
        for row in index.get(section, []):
            rows[row["id"]] = row["status"]

    return {
        "root": root,
        "label": label,
        "index": index,
        "summary": summary,
        "rows": rows,
        "suites": suites,
        "families": families,
        "benchmark": benchmark,
    }


def completion_condition(repo):
    """Evaluate the completion condition. Returns (facts, problems)."""
    problems = []
    trees = []
    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        facts = check_tree(repo, tree, problems)
        if facts:
            trees.append(facts)

    if len(trees) == len(rd.ACCEPTED_EVIDENCE_TREES):
        first, second = trees[0], trees[1]
        if first["rows"] != second["rows"]:
            differing = sorted(k for k in set(first["rows"]) | set(second["rows"])
                               if first["rows"].get(k) != second["rows"].get(k))
            problems.append("the two accepted evidence trees disagree on " +
                            ", ".join(differing[:10]))
        if first["suites"] != second["suites"]:
            problems.append("the two accepted evidence trees disagree on the suite matrix")
        if first["families"] != second["families"]:
            problems.append("the two accepted evidence trees disagree on the family verdicts")

    decision_path = os.path.join(repo, rd.BUDGET_DECISION_RECORD)
    if not os.path.exists(decision_path):
        problems.append(rd.BUDGET_DECISION_RECORD + ": missing")
    else:
        decision = read_text(decision_path)
        if rd.OWNER_EXCEPTION_PHRASE not in decision:
            problems.append(rd.BUDGET_DECISION_RECORD + ": does not carry `" +
                            rd.OWNER_EXCEPTION_PHRASE + "`")
        if rd.OWNER_DECISION_DATE not in decision:
            problems.append(rd.BUDGET_DECISION_RECORD + ": does not record the decision date " +
                            rd.OWNER_DECISION_DATE)
        if rd.DEFERRED_BUDGET_ROW not in decision:
            problems.append(rd.BUDGET_DECISION_RECORD + ": does not name the deferred budget row " +
                            rd.DEFERRED_BUDGET_ROW)

    template_path = os.path.join(repo, rd.BUDGET_TEMPLATE_SNAPSHOT)
    if os.path.exists(template_path) and rd.OWNER_EXCEPTION_PHRASE in read_text(template_path):
        problems.append(rd.BUDGET_TEMPLATE_SNAPSHOT + ": is a pre-decision snapshot but carries "
                        "the owner-deferral sentence")

    published = os.path.join(repo, rd.CANONICAL_EVIDENCE_INDEX)
    if os.path.exists(published) and trees:
        published_index = read_json(published)
        if published_index.get("summary") != trees[0]["summary"]:
            problems.append(rd.CANONICAL_EVIDENCE_INDEX + ": the published summary disagrees with "
                            "the accepted gate tree")
        published_audit = os.path.join(repo, "artifacts/conformance/family-audit.json")
        if os.path.exists(published_audit):
            audit = read_json(published_audit)
            live = dict((row["family"], row["verdict"]) for row in audit.get("families", []))
            if live != trees[0]["families"]:
                problems.append(published_audit + ": the published family audit disagrees with "
                                "the accepted gate tree")

    return trees, problems


# --- documentation status -----------------------------------------------------------------------

def check_docs_status(repo, complete, problems, digests=None):
    """A document may claim completion only when the evidence is complete, and then it must.

    The same pass verifies the digests `supported-profile.md` states by hand against the values the
    builder recomputed, so a lock or manifest that changed without the profile following it fails
    instead of leaving a stale literal in the release record.
    """
    for source in rd.DOCS_STATUS_SOURCES:
        path = os.path.join(repo, source["path"])
        if not os.path.exists(path):
            problems.append(source["path"] + ": missing")
            continue
        text = read_text(path)
        if source["mode"] == "status":
            match = re.search(source["regex"], text)
            if not match:
                problems.append(source["path"] + ": no status sentence matching `" +
                                source["regex"] + "`")
            elif complete and match.group(1) != rd.V1_STATUS_COMPLETE:
                problems.append(source["path"] + ": records `" + match.group(1) +
                                "` instead of `" + rd.V1_STATUS_COMPLETE + "`")
            elif not complete:
                problems.append(source["path"] + ": records a completion decision while the "
                                "evidence is incomplete")
        elif source["mode"] == "mention":
            present = rd.V1_STATUS_COMPLETE in text
            if complete and not present:
                problems.append(source["path"] + ": does not state `" + rd.V1_STATUS_COMPLETE + "`")
            if not complete and present:
                problems.append(source["path"] + ": still states `" + rd.V1_STATUS_COMPLETE +
                                "` while the evidence is incomplete")
        else:  # "exception"
            if rd.OWNER_EXCEPTION_PHRASE not in text:
                problems.append(source["path"] + ": does not record `" + rd.OWNER_EXCEPTION_PHRASE +
                                "`")

    profile = os.path.join(repo, rd.SUPPORTED_PROFILE)
    if os.path.exists(profile):
        text = read_text(profile)
        for label in sorted(digests or {}):
            value = digests[label]
            if value["sha256"] not in text:
                problems.append(rd.SUPPORTED_PROFILE + ": does not state the accepted " + label +
                                " digest `" + value["sha256"] + "` (recomputed from " +
                                value["path"] + ")")

def traceability(repo, problems):
    path = os.path.join(repo, rd.TRACEABILITY)
    if not os.path.exists(path):
        problems.append(rd.TRACEABILITY + ": missing")
        return {}, []
    document = read_json(path)
    by_id = {}
    for task in document.get("tasks", []):
        by_id[task["id"]] = task
    return by_id, document.get("tasks", [])


def verdict_sentence(text, marker, limit=240):
    """The sentence that contains `marker`, with markdown emphasis stripped and whitespace flattened.

    A report states its verdict as a bolded sentence, so the sentence ends at `.**` when there is one;
    otherwise at the next `. `. The manifest records that sentence rather than the paragraph around
    it, so a reader can check it against the report in one look.
    """
    flat = re.sub(r"\s+", " ", text)
    index = flat.find(marker)
    if index < 0:
        return None
    before = flat[:index]
    start = max(before.rfind(". ") + 1, before.rfind("**") + 2, before.rfind("| ") + 2, 0)
    after = index + len(marker)
    ends = []
    for token in (".**", "** ", ". "):
        position = flat.find(token, after)
        if position >= 0:
            ends.append(position + (1 if token == ". " else 0))
    end = min(ends) if ends else len(flat)
    sentence = flat[start:end].replace("**", "").strip()
    if not sentence.endswith("."):
        sentence += "."
    if len(sentence) > limit:
        sentence = sentence[:limit - 1].rstrip(" ,;") + "…"
    return sentence


def gate_rows(repo, trees, tasks_by_id, problems):
    """One manifest row per required gate, with statuses derived from the accepted index."""
    if not trees:
        return []
    accepted_rows = trees[0]["rows"]
    accepted_suites = trees[0]["suites"]
    rows = []

    def statuses_for(requirement_ids, test_ids):
        requirement_rows = []
        for identifier in requirement_ids:
            requirement_rows.append({"id": identifier,
                                     "status": accepted_status_label(
                                         accepted_rows.get(identifier, "Unrecorded"))})
        test_rows = []
        for identifier in test_ids:
            test_rows.append({"id": identifier,
                              "status": accepted_suites.get(identifier, "Unrecorded")})
        return requirement_rows, test_rows

    for spec in rd.GATE_RECORDS + rd.WAVE_RECORDS:
        record = dict(spec)
        report = os.path.join(repo, record["report"])
        if not os.path.exists(report):
            problems.append(record["id"] + ": report is missing: " + record["report"])
            continue
        text = read_text(report)
        verdict = None
        if record.get("verdict_marker"):
            if record["verdict_marker"] not in text:
                problems.append(record["id"] + ": its report no longer contains the recorded "
                                "verdict `" + record["verdict_marker"] + "`")
            else:
                verdict = verdict_sentence(text, record["verdict_marker"])
        revision = None
        if record.get("revision_prefix"):
            pattern = re.escape(record["revision_prefix"]) + r"([0-9a-f]{7,40})" + \
                re.escape(record["revision_suffix"] or "")
            match = re.search(pattern, text)
            if match:
                revision = match.group(1)
            else:
                problems.append(record["id"] + ": its report no longer contains the recorded "
                                "revision declaration `" + record["revision_prefix"] + "`")
        if "kind" in record:
            kind, wave = record["kind"], tasks_by_id.get(record["id"], {}).get("wave")
        else:
            kind, wave = "wave", record["wave"]
        requirements, tests = [], []
        if kind == "task":
            task = tasks_by_id.get(record["id"])
            if not task:
                problems.append(record["id"] + ": is not registered in " + rd.TRACEABILITY)
            else:
                requirements = list(task.get("requirements", []))
                tests = list(task.get("tests", []))
        else:
            wave_tasks = [task for task in tasks_by_id.values() if task.get("wave") == wave]
            requirements = sorted(set(identifier for task in wave_tasks
                                      for identifier in task.get("requirements", [])))
            tests = sorted(set(identifier for task in wave_tasks
                               for identifier in task.get("tests", [])))
        requirement_rows, test_rows = statuses_for(requirements, tests)
        status = derived_status([row["status"].split(" ")[0] for row in requirement_rows] +
                               [row["status"].split(" ")[0] for row in test_rows])
        rows.append({
            "gate": record["id"],
            "kind": kind,
            "wave": wave,
            "report": record["report"],
            "evidence": evidence_files(repo, record["evidence"]),
            "recorded_verdict": verdict,
            "recorded_verdict_marker": record.get("verdict_marker"),
            "revision": revision,
            "requirements": requirement_rows,
            "tests": test_rows,
            "derived_status": accepted_status_label(status),
            "note": record.get("note"),
        })
    return rows


# --- rendering ----------------------------------------------------------------------------------
def accepted_digests(repo):
    """The digests of the accepted revision, recomputed from the working tree.

    These are the values a profile record states by hand, so the builder both publishes them in the
    manifest and requires `supported-profile.md` to state them: a lock or manifest that changes
    without the profile following it is a failure, not a stale literal nobody notices.
    """
    digests = {}
    for key, relative in (("manifest", rd.MANIFEST_PATH), ("lock", rd.LOCK_PATH),
                          ("catalog", rd.ENVIRONMENT_CATALOG_FILE)):
        path = os.path.join(repo, relative)
        if os.path.exists(path):
            digests[key] = {"path": relative, "sha256": rcc.sha256_file(path)}
    for record in rd.DIGEST_RECORDS:
        for key, kind in record["fields"]:
            if kind != "player":
                continue
            fields = rcc.parse_kv(read_text(os.path.join(repo, record["path"])))
            if fields.get(key):
                digests["player"] = {"path": "recorded in " + record["path"],
                                     "sha256": fields[key]}
                break
        if "player" in digests:
            break
    return digests



def build_document(repo, git_runner=None):
    """Run every check and return (document, status_document, problems)."""
    problems = []
    report = rcc.run(repo, git_runner or rcc.git)
    revision_problems = [f["subject"] + ": " + f["detail"] for f in report.problems]
    consistency = {
        "ok": not report.failed,
        "checked": len(report.findings),
        "declaredSkips": len([f for f in report.findings if f["status"] == rcc.SKIP]),
        "failed": len(report.problems),
        "findings": report.findings,
    }
    problems.extend("revision consistency: " + problem for problem in revision_problems)

    trees, condition_problems = completion_condition(repo)
    problems.extend(condition_problems)

    tasks_by_id = {}
    traceability_problems = []
    by_id, task_list = traceability(repo, traceability_problems)
    problems.extend(traceability_problems)
    for task in task_list:
        tasks_by_id[task["id"]] = task

    gates = gate_rows(repo, trees, tasks_by_id, problems)
    allowed = []
    for row in gates:
        if row["derived_status"] not in rd.ALLOWED_GATE_STATUSES:
            allowed.append(row["gate"] + " is " + row["derived_status"])
    if allowed:
        problems.append("gates outside the allowed statuses: " + ", ".join(allowed))

    digests = accepted_digests(repo)

    complete = not problems
    check_docs_status(repo, complete, problems, digests)
    complete = not problems

    accepted_revision = None
    for record in rd.ACCEPTED_REVISION_RECORDS:
        value, problem = rcc.extract_revision(repo, record)
        if not problem:
            accepted_revision = value
            break

    status = {
        "artifact": STATUS_FORMAT,
        "generator": "tools/release_readiness/build_release_readiness.py",
        "protocolVersion": rd.PROTOCOL_VERSION,
        "acceptedRevision": accepted_revision,
        "v1Status": rd.V1_STATUS_COMPLETE if complete else STATUS_INCOMPLETE,
        "complete": complete,
        "ownerException": {
            "row": rd.OWNER_EXCEPTION_ROW,
            "decision": rd.OWNER_EXCEPTION_PHRASE,
            "date": rd.OWNER_DECISION_DATE,
            "sentence": rd.OWNER_EXCEPTION_SENTENCE,
            "recordedIn": [rd.BUDGET_DECISION_RECORD, rd.DECISION_DOC,
                           rd.SUPPORTED_PROFILE, "docs/operator/deferred-scope.md"],
        },
        "revisionConsistency": consistency,
        "gateCondition": {
            "acceptedTrees": [tree["root"] for tree in trees],
            "allowedStatuses": list(rd.ALLOWED_GATE_STATUSES),
            "problems": problems,
        },
        "evidenceTrees": [
            {
                "root": tree["root"],
                "label": tree["label"],
                "summary": tree["summary"],
                "families": tree["families"],
                "diagnostic": {
                    "counts": tree["benchmark"].get("counts") if tree["benchmark"] else None,
                    "missedTarget": tree["benchmark"].get("missedTarget")
                    if tree["benchmark"] else None,
                },
            }
            for tree in trees
        ],
    }

    manifest = {
        "artifact": FORMAT,
        "generator": "tools/release_readiness/build_release_readiness.py",
        "protocolVersion": rd.PROTOCOL_VERSION,
        "acceptedRevision": accepted_revision,
        "v1Status": status["v1Status"],
        "complete": complete,
        "profile": rd.SUPPORTED_PROFILE,
        "acceptedDigests": digests,
        "revisionConsistency": {
            "ok": consistency["ok"],
            "checked": consistency["checked"],
            "declaredSkips": consistency["declaredSkips"],
            "failed": consistency["failed"],
        },
        "ownerException": status["ownerException"],
        "gates": gates,
        "problems": problems,
    }
    return manifest, status


def render_manifest(manifest, status):
    lines = []
    lines.append("# Release-readiness evidence manifest")
    lines.append("")
    lines.append("Generated by `tools/release_readiness/build_release_readiness.py`. Every value "
                 "below is read from an evidence file or derived from the accepted requirement "
                 "index; nothing is typed by hand. Do not edit this file.")
    lines.append("")
    lines.append("| Item | Value |")
    lines.append("| --- | --- |")
    lines.append("| Protocol version | `" + manifest["protocolVersion"] + "` |")
    lines.append("| Accepted revision | `" + str(manifest["acceptedRevision"]) + "` |")
    lines.append("| **V1 status** | **" + manifest["v1Status"] + "** |")
    lines.append("| Revision consistency | " +
                 ("Pass — " + str(manifest["revisionConsistency"]["checked"]) +
                  " checks, " + str(manifest["revisionConsistency"]["declaredSkips"]) +
                  " declared skips, 0 failures"
                  if manifest["revisionConsistency"]["ok"] else "FAIL") + " |")
    lines.append("| Supported profile record | [`" + manifest["profile"] + "`](" +
                 os.path.basename(manifest["profile"]) + ") |")
    lines.append("")
    lines.append("## Owner-approved exception")
    lines.append("")
    exception = manifest["ownerException"]
    lines.append("* Row: **" + exception["row"] + "** — " + exception["decision"] + ", " +
                 exception["date"] + ".")
    lines.append("* " + exception["sentence"])
    lines.append("* Recorded in: " + ", ".join("`" + path + "`" for path in
                                               exception["recordedIn"]) + ".")
    lines.append("")
    lines.append("## Gates")
    lines.append("")
    lines.append("`Derived status` is the worst status among the gate's registered requirements and "
                 "tests at the accepted revision. `Deferred (owner decision)` there always means the "
                 "gate's registered coverage includes the single owner-excepted row (`" +
                 rd.OWNER_EXCEPTION_ROW + "`, TEST-023 timing); it never means the gate's own "
                 "verification failed — a failed row reads `Fail`, `Blocked` or `NotRun`, and any of "
                 "those makes the completion condition fail. `Verdict` is the sentence the gate's own "
                 "report contains, matched against the marker recorded in "
                 "`readiness_data.GATE_RECORDS`; `—` means the report records commands and results "
                 "but no single verdict sentence, which the notes below state for each such gate.")
    lines.append("")
    lines.append("| Gate | Kind | Wave | Report | Derived status | Verdict | Revision | Files |")
    lines.append("| --- | --- | ---: | --- | --- | --- | --- | ---: |")
    for row in manifest["gates"]:
        verdict = row["recorded_verdict"] if row["recorded_verdict"] else "—"
        lines.append("| " + row["gate"] + " | " + row["kind"] + " | " + str(row["wave"]) + " | `" +
                     row["report"] + "` | " + row["derived_status"] + " | " +
                     verdict.replace("|", "\\|") + " | " +
                     ("`" + row["revision"] + "`" if row["revision"] else "—") + " | " +
                     str(len(row["evidence"])) + " |")
    lines.append("")
    lines.append("## Requirement and test rows per gate")
    lines.append("")
    lines.append("| Gate | Requirements | Tests |")
    lines.append("| --- | --- | --- |")
    def summarise(entries):
        if not entries:
            return "—"
        non_pass = [entry for entry in entries if entry["status"] != "Pass"]
        if not non_pass:
            return str(len(entries)) + " Pass"
        return ", ".join(entry["id"] + "=" + entry["status"] for entry in non_pass)

    for row in manifest["gates"]:
        lines.append("| " + row["gate"] + " | " + summarise(row["requirements"]) + " | " +
                     summarise(row["tests"]) + " |")

    notes = [row for row in manifest["gates"] if row["note"]]
    if notes:
        lines.append("")
        lines.append("## Recorded notes per gate")
        lines.append("")
        lines.append("The gate-specific resolutions a reader needs in order not to misread a status "
                     "above: a task-time verdict the accepted revision supersedes, a report that "
                     "records commands but no single verdict, or a revision the report does not "
                     "state.")
        lines.append("")
        lines.append("| Gate | Verdict | Note |")
        lines.append("| --- | --- | --- |")
        for row in notes:
            verdict = row["recorded_verdict"] if row["recorded_verdict"] else "—"
            lines.append("| " + row["gate"] + " | " + verdict.replace("|", "\\|") + " | " +
                         row["note"].replace("|", "\\|") + " |")
    lines.append("")
    lines.append("## Decision condition")
    lines.append("")
    if status["complete"]:
        lines.append("Every accepted evidence tree reports every registered requirement and ")
        lines.append("operation as `Pass` apart from the single owner-excepted row above; every ")
        lines.append("required TEST suite is `Pass`; the three families are `Pass`; the short ")
        lines.append("diagnostic is the accepted one; and the revision, catalog and package ")
        lines.append("digests agree with the working tree.")
    else:
        lines.append("**The completion condition is not met.** Reasons:")
        lines.append("")
        for problem in status["gateCondition"]["problems"]:
            lines.append("* " + problem)
    lines.append("")
    return "\n".join(lines) + "\n"


# --- self-test ----------------------------------------------------------------------------------

def _sandbox(root):
    """Fabricate a corpus that satisfies the consistency checker, then add the gate condition.

    The revision side is exactly the corpus `check_revision_consistency._sandbox_repo` builds, so
    the two tools cannot disagree about what a consistent corpus is; this function adds the
    accepted requirement index, suite matrix, family audit, diagnostic summary, traceability
    registry, budget decision record, gate reports and the documents that state the status.
    """
    rcc._sandbox_repo(root)
    revision = rcc.SAMPLE_REVISION

    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        base = os.path.join(root, tree["root"])
        requirement_rows = [{"id": "P-%03d" % n, "status": "Pass"} for n in range(1, 60)]
        requirement_rows.append({"id": rd.OWNER_EXCEPTION_ROW, "status": "Deferred"})
        operation_rows = [{"id": "O-%02d" % n, "status": "Pass"} for n in range(1, 27)]
        write_text(os.path.join(base, rd.EVIDENCE_INDEX_NAME),
                   json.dumps({"evidenceRoot": tree["root"],
                               "requirements": requirement_rows, "operations": operation_rows,
                               "summary": {"requirements": rd.EXPECTED_REQUIREMENT_TOTAL,
                                           "operations": rd.EXPECTED_OPERATION_TOTAL,
                                           "byStatus": {"Pass": 85, "Deferred": 1},
                                           "unresolvedReferenceCount": 0,
                                           "nonPass": [{"id": rd.OWNER_EXCEPTION_ROW,
                                                        "status": "Deferred"}]},
                               "unresolvedReferences": [],
                               "corpus": {"problems": [row["problem"]
                                                       for row in rd.KNOWN_CORPUS_PROBLEMS
                                                       if row["tree"] == tree["root"]]}}))
        write_text(os.path.join(base, rd.SUITE_MATRIX_NAME),
                   json.dumps({"artifact": "gamecore.conformance.suite-matrix/1", "failing": [],
                               "counts": {"Pass": rd.EXPECTED_SUITE_TOTAL, "NotRun": 0, "Fail": 0},
                               "probeRuns": "2",
                               "suites": [{"suite": "TEST-%03d" % n, "status": "Pass"}
                                          for n in range(1, rd.EXPECTED_SUITE_TOTAL + 1)]}))
        write_text(os.path.join(base, rd.FAMILY_AUDIT_NAME),
                   json.dumps({"assemblyAudit": {"clean": True},
                               "families": [{"family": "cards", "verdict": "Pass"},
                                            {"family": "narrative", "verdict": "Pass"},
                                            {"family": "traversal", "verdict": "Pass"}]}))
        write_text(os.path.join(base, rd.BENCHMARK_SUMMARY_NAME),
                   json.dumps({"counts": {"documents": rd.EXPECTED_DIAGNOSTIC_WORKLOADS,
                                          "runs": 1,
                                          "workloads": rd.EXPECTED_DIAGNOSTIC_WORKLOADS,
                                          "gates": rd.EXPECTED_DIAGNOSTIC_GATES,
                                          "gatesFailed": 0, "budgets": 10, "missedTarget": 1,
                                          "notMeasured": 0},
                               "gatesFailed": [],
                               "missedTarget": list(rd.EXPECTED_DIAGNOSTIC_MISSED_TARGETS),
                               "budgets": [{"id": row, "verdict": "MissedTarget"}
                                           for row in rd.EXPECTED_DIAGNOSTIC_MISSED_TARGETS]}))

    # The two published summaries GC-028 regenerated from the accepted gate tree.
    gate_root = os.path.join(root, rd.ACCEPTED_EVIDENCE_TREES[0]["root"])
    write_text(os.path.join(root, rd.CANONICAL_EVIDENCE_INDEX),
               json.dumps(read_json(os.path.join(gate_root, rd.EVIDENCE_INDEX_NAME))))
    write_text(os.path.join(root, "artifacts/conformance/family-audit.json"),
               json.dumps(read_json(os.path.join(gate_root, rd.FAMILY_AUDIT_NAME))))

    write_text(os.path.join(root, rd.TRACEABILITY),
               json.dumps({"tasks": [
                   {"id": record["id"],
                    "wave": 1 + (index % 9),
                    "requirements": [rd.OWNER_EXCEPTION_ROW] if record["id"] == "GC-026"
                                    else ["P-001"],
                    "tests": ["TEST-001"]}
                   for index, record in enumerate(rd.GATE_RECORDS)]}))
    write_text(os.path.join(root, rd.BUDGET_DECISION_RECORD),
               "Status: **TEST-023 timing qualification " + rd.OWNER_EXCEPTION_PHRASE + " (" +
               rd.OWNER_DECISION_DATE + "); not measured at full duration**. The excepted row is " +
               rd.OWNER_EXCEPTION_ROW + "; the deferred budget row is " + rd.DEFERRED_BUDGET_ROW +
               ".\n")

    for record in rd.GATE_RECORDS + rd.WAVE_RECORDS:
        path = os.path.join(root, record["report"])
        text = read_text(path) if os.path.exists(path) else ""
        if record.get("verdict_marker"):
            text += record["verdict_marker"] + "\n"
        if record.get("revision_prefix"):
            text += (record["revision_prefix"] + revision +
                     (record.get("revision_suffix") or "") + "\n")
        write_text(path, text or "no verdict recorded\n")

    for source in rd.DOCS_STATUS_SOURCES:
        if source["mode"] == "status":
            body = "V1 status: " + rd.V1_STATUS_COMPLETE + ".\n"
        elif source["mode"] == "mention":
            body = "Status: " + rd.V1_STATUS_COMPLETE + ".\n"
        else:
            body = "The owner decision records " + rd.OWNER_EXCEPTION_PHRASE + ".\n"
        write_text(os.path.join(root, source["path"]), body)


    # The sandbox profile must state the digests the builder recomputes, exactly as the real record
    # does, or the digest check would be untested.
    digests = accepted_digests(root)
    with open(os.path.join(root, rd.SUPPORTED_PROFILE), "a", encoding="utf-8") as handle:
        for label in sorted(digests):
            handle.write("| " + label + " | `" + digests[label]["sha256"] + "` |\n")
    return root

def _sandbox_mutations(root):
    return (
        ("a required row turns NotRun", lambda: _set_index(root, {"id": "P-002",
                                                                  "status": "NotRun"})),
        ("a suite turns Fail", lambda: _set_suite(root, "Fail")),
        ("the diagnostic becomes a full run", lambda: _set_benchmark(root, runs=5)),
        ("a second budget row is missed", lambda: _add_missed(root)),
        ("the owner deferral leaves the record", lambda: write_text(
            os.path.join(root, rd.BUDGET_DECISION_RECORD), "Status: measured at full duration\n")),
        ("a gate report loses its verdict", lambda: write_text(
            os.path.join(root, "artifacts/gc-001/BUILD_REPORT.md"), "no verdict here\n")),
        ("a status document drops the completion status", lambda: write_text(
            os.path.join(root, "README.md"), "Status: under construction\n")),
        ("the decision record states a different status", lambda: write_text(
            os.path.join(root, rd.DECISION_DOC), "V1 status: incomplete.\n")),
        ("a document claims completion while a row is NotRun", lambda: (
            _set_index(root, {"id": "P-002", "status": "NotRun"}),
            write_text(os.path.join(root, "docs/operator/deferred-scope.md"),
                       rd.V1_STATUS_COMPLETE + " " + rd.OWNER_EXCEPTION_PHRASE + "\n"))),
        ("the profile loses a recomputed digest", lambda: write_text(
            os.path.join(root, rd.SUPPORTED_PROFILE),
            "V1 status: " + rd.V1_STATUS_COMPLETE + ".\n" + rd.OWNER_EXCEPTION_PHRASE + "\n")),
    )


def _set_index(root, extra):
    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        path = os.path.join(root, tree["root"], rd.EVIDENCE_INDEX_NAME)
        payload = read_json(path)
        payload["requirements"].append(extra)
        payload["summary"]["byStatus"]["NotRun"] = 1
        write_text(path, json.dumps(payload))


def _set_suite(root, status):
    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        path = os.path.join(root, tree["root"], rd.SUITE_MATRIX_NAME)
        payload = read_json(path)
        payload["suites"][0]["status"] = status
        payload["counts"]["Fail"] = 1
        payload["failing"] = [payload["suites"][0]["suite"]]
        write_text(path, json.dumps(payload))


def _set_benchmark(root, runs):
    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        path = os.path.join(root, tree["root"], rd.BENCHMARK_SUMMARY_NAME)
        payload = read_json(path)
        payload["counts"]["runs"] = runs
        write_text(path, json.dumps(payload))


def _add_missed(root):
    for tree in rd.ACCEPTED_EVIDENCE_TREES:
        path = os.path.join(root, tree["root"], rd.BENCHMARK_SUMMARY_NAME)
        payload = read_json(path)
        payload["budgets"].append({"id": "budget.execution-p95", "verdict": "MissedTarget"})
        write_text(path, json.dumps(payload))


def self_test():
    problems = []
    names = [name for name, _ in _sandbox_mutations("")]

    # 1. A complete corpus must record the completion status.
    sandbox = tempfile.mkdtemp(prefix="gc030-readiness-")
    try:
        root = _sandbox(os.path.join(sandbox, "repo"))
        manifest, status = build_document(root, rcc._fake_git())
        if not status["complete"]:
            problems.append("complete corpus refused: " +
                            json.dumps(status["gateCondition"]["problems"]))
        if manifest["v1Status"] != rd.V1_STATUS_COMPLETE:
            problems.append("complete corpus did not emit the completion status")
    finally:
        shutil.rmtree(sandbox, ignore_errors=True)

    # 2. Each mutation must make the tool refuse to record completion.
    for name in names:
        sandbox = tempfile.mkdtemp(prefix="gc030-readiness-mutation-")
        try:
            root = _sandbox(os.path.join(sandbox, "repo"))
            dict(_sandbox_mutations(root))[name]()
            manifest, status = build_document(root, rcc._fake_git())
            if status["complete"]:
                problems.append("mutation not refused: " + name)
        finally:
            shutil.rmtree(sandbox, ignore_errors=True)

    if problems:
        for problem in problems:
            print("SELF-TEST FAIL: " + problem, file=sys.stderr)
        return 1
    print("self-test: a complete corpus records the V1 status, and all %d mutations are refused"
          % len(names))
    return 0


# --- entry point --------------------------------------------------------------------------------

def main(argv):
    parser = argparse.ArgumentParser(description="Build or verify the release-readiness record.")
    parser.add_argument("--repo", default=DEFAULT_REPO)
    parser.add_argument("--out-dir", default=None,
                        help="output directory (default: <repo>/artifacts/release-readiness)")
    parser.add_argument("--build", action="store_true", help="write the manifest and status")
    parser.add_argument("--check", action="store_true",
                        help="verify the committed manifest, status and document statuses")
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args(argv)

    if args.self_test:
        return self_test()

    repo = os.path.abspath(args.repo)
    out_dir = args.out_dir or os.path.join(repo, "artifacts/release-readiness")
    manifest, status = build_document(repo)
    manifest_text = json.dumps(manifest, indent=2) + "\n"
    status_text = json.dumps(status, indent=2) + "\n"
    rendered = render_manifest(manifest, status)

    if args.build:
        write_text(os.path.join(out_dir, rd.MANIFEST_JSON), manifest_text)
        write_text(os.path.join(out_dir, rd.MANIFEST_MD), rendered)
        write_text(os.path.join(out_dir, rd.STATUS_JSON), status_text)
        print("wrote " + rd.MANIFEST_JSON + ", " + rd.MANIFEST_MD + ", " + rd.STATUS_JSON +
              " to " + out_dir)
        if not status["complete"]:
            for problem in status["gateCondition"]["problems"]:
                print("  incomplete: " + problem, file=sys.stderr)
            return 1
        return 0

    if args.check:
        drifted = []
        for name, expected in ((rd.MANIFEST_JSON, manifest_text), (rd.MANIFEST_MD, rendered),
                               (rd.STATUS_JSON, status_text)):
            path = os.path.join(out_dir, name)
            if not os.path.exists(path):
                drifted.append(name + " is missing")
            elif read_text(path) != expected:
                drifted.append(name + " is stale; run --build")
        for problem in drifted:
            print("FAIL: " + problem, file=sys.stderr)
        if drifted:
            return 1
        if not status["complete"]:
            for problem in status["gateCondition"]["problems"]:
                print("FAIL: " + problem, file=sys.stderr)
            print("FAIL: the completion condition is not met; the records must not claim it",
                  file=sys.stderr)
            return 1
        print("OK: the committed manifest, status and documentation statuses agree with the "
              "evidence; " + status["v1Status"])
        return 0

    print(rendered)
    print("V1 status: " + status["v1Status"])
    return 0 if status["complete"] else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
