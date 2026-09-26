#!/usr/bin/env python3
"""The document half of the Wave 7 clause "benchmark data and budget decisions are recorded".

What this checks, and why the claim is split in two:

The probe player cannot read the repository. So `W7GateScenario`'s budget step can only assert the
COMPILED rows of `tests/GameCore.Benchmarks/Runtime/PerformanceBudgets.cs` — the ids it carries and the
numbers the run produced. It cannot see whether a human wrote down, inside the repository, what was
decided about each row. This script checks that COMMITTED record:

  1. `artifacts/performance/BUDGET_DECISIONS.md` exists, and each row id PARSED OUT OF the C# file (never
     a second hardcoded list, so a row added to the code and not to the record fails) has its own
     `## <id>` section.
  2. every one of those sections carries a `- Decision:` line and an `- Evidence:` line, and every
     repository path named on an `- Evidence:` line exists under the root.
  3. the record states the TEST-023 timing qualification deferral by project-owner decision
     (`Deferred by project-owner decision`).
  4. `artifacts/performance/summary.md` exists and carries a verdict line plus the short correctness
     diagnostic's evidence: a workload id from `BenchmarkWorkload.cs` and a summarizer verdict token.
  5. while the record itself calls the qualification deferred, no budget section claims a
     full-duration `Measured (full ...)` value or an `- Decision: accept`.

Nothing here measures anything and nothing here edits an artifact: it reads the committed files.

Exit codes: 0 every check holds; 1 at least one check failed; 2 a file this tool must read is missing.

Usage:
    python3 tools/check_budget_record.py [--root .] [--json <path>]
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys

TASK = "W7-GATE"

RECORD_REL = "artifacts/performance/BUDGET_DECISIONS.md"
SUMMARY_REL = "artifacts/performance/summary.md"
BUDGETS_REL = "tests/GameCore.Benchmarks/Runtime/PerformanceBudgets.cs"
WORKLOADS_REL = "tests/GameCore.Benchmarks/Runtime/BenchmarkWorkload.cs"

# `public const string ExecutionP95 = "budget.execution-p95";` — the row ids, in the code's own words.
ROW_CONST = re.compile(r'public\s+const\s+string\s+\w+\s*=\s*"(budget\.[^"]+)"\s*;')
# `public const string IdleCommandWorld = "idle-command-world";` — the workload ids.
WORKLOAD_CONST = re.compile(r'public\s+const\s+string\s+\w+\s*=\s*"([^"]+)"\s*;')

SECTION = re.compile(r"^##\s+(\S+)\s*$")
DECISION_LINE = re.compile(r"^\s*-\s*Decision:")
TARGET_LINE = re.compile(r"^\s*-\s*Target \(08\):")
EVIDENCE_LINE = re.compile(r"^\s*-\s*Evidence:")
FULL_MEASURED = re.compile(r"Measured \(full")
ACCEPT_DECISION = re.compile(r"^\s*-\s*Decision:\s*accept\b")
BACKTICKED = re.compile(r"`([^`]+)`")
VERDICT_LINE = re.compile(r"^\s*Verdict:\s*(\S.*)$")

DEFERRAL = "Deferred by project-owner decision"
PATH_EXTENSIONS = (".md", ".json", ".csv", ".txt", ".cs")
SUMMARY_VERDICT_TOKENS = ("NotMeasured", "MissedTarget", "WithinTarget")


def parse_row_ids(text: str) -> list:
    """The budget row ids as the code declares them, in declaration order, without duplicates."""
    ids = []
    for value in ROW_CONST.findall(text):
        if value not in ids:
            ids.append(value)
    return ids


def parse_workload_ids(text: str) -> list:
    """The workload ids as the code declares them, in declaration order, without duplicates."""
    ids = []
    for value in WORKLOAD_CONST.findall(text):
        if value not in ids:
            ids.append(value)
    return ids


def contains_id(text: str, identifier: str) -> bool:
    """True when the text names the id as its own token, not as part of a longer token.

    A workload id appearing only inside a gate name (`idle-command-world-advances-zero-steps`) is not the
    summary naming the workload it measured.
    """
    return re.search(r"(?<![\w-])" + re.escape(identifier) + r"(?![\w-])", text) is not None


def split_sections(lines: list):
    """Returns [(heading, [(line_number, text), ...]), ...] for every `## <heading>` section."""
    sections = []
    heading = None
    body = []
    for number, line in enumerate(lines, start=1):
        match = SECTION.match(line)
        if match:
            if heading is not None:
                sections.append((heading, body))
            heading = match.group(1)
            body = []
            continue
        if heading is not None:
            body.append((number, line))
    if heading is not None:
        sections.append((heading, body))
    return sections


def looks_like_repository_path(token: str) -> bool:
    if not token or token.startswith("http://") or token.startswith("https://"):
        return False
    if "/" in token:
        return True
    return token.endswith(PATH_EXTENSIONS)


def evidence_paths(line: str) -> list:
    """Every repository path named on one `- Evidence:` line.

    The record writes paths in backticks; a bare token is only a fallback for a line that has none.
    """
    spans = BACKTICKED.findall(line)
    if not spans:
        spans = line.split()
    tokens = []
    for span in spans:
        token = span.strip().strip(",;'\"")
        if looks_like_repository_path(token) and token not in tokens:
            tokens.append(token)
    return tokens


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=".", help="repository root the paths are relative to (default: .)")
    parser.add_argument("--json", metavar="PATH", default=None, help="write the same facts here as JSON")
    args = parser.parse_args()

    root = os.path.abspath(args.root)
    record_path = os.path.join(root, RECORD_REL)
    summary_path = os.path.join(root, SUMMARY_REL)
    budgets_path = os.path.join(root, BUDGETS_REL)
    workloads_path = os.path.join(root, WORKLOADS_REL)

    required = [budgets_path, workloads_path, record_path, summary_path]
    missing = [path for path in required if not os.path.isfile(path)]
    if missing:
        for path in missing:
            print("MISSING  " + path)
        print()
        print("VERDICT: fail (missing prerequisite)")
        report = {
            "tool": "tools/check_budget_record.py",
            "task": TASK,
            "root": root,
            "checks": [],
            "problems": ["missing prerequisite: " + path for path in missing],
            "verdict": "fail",
        }
        write_json(args.json, report)
        return 2

    budgets_text = read_text(budgets_path)
    workloads_text = read_text(workloads_path)
    record_text = read_text(record_path)
    record_lines = record_text.splitlines()
    summary_text = read_text(summary_path)

    row_ids = parse_row_ids(budgets_text)
    workload_ids = parse_workload_ids(workloads_text)
    sections = split_sections(record_lines)
    budget_sections = [(heading, body) for heading, body in sections if heading.startswith("budget.")]

    checks = []
    problems = []

    def check(name: str, ok: bool, detail: str) -> None:
        checks.append({"name": name, "status": "pass" if ok else "fail", "detail": detail})
        print(("PASS  " if ok else "FAIL  ") + name + ": " + detail)
        if not ok:
            problems.append(name + ": " + detail)

    # 1. The ids come out of the code; the record carries one section per id.
    check(
        "row-ids",
        bool(row_ids),
        "parsed " + str(len(row_ids)) + " row id(s) from " + BUDGETS_REL + ": " + ", ".join(row_ids),
    )

    declared = set(row_ids)
    section_headings = [heading for heading, _ in budget_sections]
    missing_sections = [row_id for row_id in row_ids if row_id not in section_headings]
    unknown_sections = [heading for heading in section_headings if heading not in declared]
    section_detail = (
        str(len(section_headings)) + " '## budget.*' section(s) in " + RECORD_REL
        + "; every declared row id has one"
    )
    if missing_sections:
        section_detail = "no '## <id>' section for: " + ", ".join(missing_sections)
    if unknown_sections:
        section_detail += "; section(s) with no row in " + BUDGETS_REL + ": " + ", ".join(unknown_sections)
    check("record-sections", not missing_sections and not unknown_sections, section_detail)

    # 2. Decision + Evidence per section, and every Evidence path exists.
    field_problems = []
    evidence_checked = 0
    missing_paths = []
    for heading, body in budget_sections:
        has_decision = any(DECISION_LINE.match(line) for _, line in body)
        evidence_lines = [(number, line) for number, line in body if EVIDENCE_LINE.match(line)]
        if not has_decision:
            field_problems.append("'" + heading + "' has no '- Decision:' line")
        if not evidence_lines:
            field_problems.append("'" + heading + "' has no '- Evidence:' line")
        for number, line in evidence_lines:
            for token in evidence_paths(line):
                evidence_checked += 1
                if not os.path.exists(os.path.join(root, token)):
                    missing_paths.append(RECORD_REL + ":" + str(number) + " names '" + token + "'")

    field_detail = (
        str(len(budget_sections)) + " section(s): each has '- Decision:' and '- Evidence:'"
    )
    if field_problems:
        field_detail = "; ".join(field_problems)
    check("section-fields", not field_problems, field_detail)

    path_detail = (
        str(evidence_checked) + " Evidence path(s) checked under " + root + "; all exist"
    )
    if missing_paths:
        path_detail = str(len(missing_paths)) + " missing path(s): " + "; ".join(missing_paths)
    check("evidence-paths", not missing_paths, path_detail)

    # 3. The deferral statement, reported verbatim.
    deferral_lines = [
        (number, line.strip()) for number, line in enumerate(record_lines, start=1) if DEFERRAL in line
    ]
    deferral_sentence = deferral_lines[0][1] if deferral_lines else ""
    check(
        "deferral",
        bool(deferral_lines),
        (
            RECORD_REL + ":" + str(deferral_lines[0][0]) + ": " + deferral_sentence
            if deferral_lines
            else "no line contains '" + DEFERRAL + "'"
        ),
    )

    # 4. summary.md: a verdict line and the short correctness diagnostic's evidence.
    summary_problems = []
    verdict_match = None
    for line in summary_text.splitlines():
        match = VERDICT_LINE.match(line)
        if match:
            verdict_match = line.strip()
            break
    if verdict_match is None:
        summary_problems.append("no 'Verdict:' line")
    workload_hits = [workload_id for workload_id in workload_ids if contains_id(summary_text, workload_id)]
    if not workload_hits:
        summary_problems.append("no workload id from " + WORKLOADS_REL)
    token_hits = [token for token in SUMMARY_VERDICT_TOKENS if token in summary_text]
    if not token_hits:
        summary_problems.append("none of " + ", ".join(SUMMARY_VERDICT_TOKENS))
    diagnostic_lines = [
        line.strip() for line in summary_text.splitlines() if "diagnostic" in line.lower()
    ]
    if not diagnostic_lines:
        summary_problems.append("no line naming the short diagnostic")
    summary_detail = (
        (verdict_match if verdict_match is not None else "no 'Verdict:' line")
        + "; workload id(s) " + ", ".join(workload_hits)
        + "; verdict token(s) " + ", ".join(token_hits)
        + "; diagnostic: " + (diagnostic_lines[0] if diagnostic_lines else "<none>")
    )
    if summary_problems:
        summary_detail = "; ".join(summary_problems)
    check("summary", not summary_problems, summary_detail)

    # 5. While deferred, no budget section claims a full-duration measurement or an accepted revision.
    offenders = []
    if deferral_lines:
        for heading, body in budget_sections:
            if not any(TARGET_LINE.match(line) for _, line in body):
                continue
            for number, line in body:
                if FULL_MEASURED.search(line) or ACCEPT_DECISION.match(line):
                    offenders.append(RECORD_REL + ":" + str(number) + " ('" + heading + "'): " + line.strip())
    consistency_detail = (
        "the record is deferred, so no budget section claims 'Measured (full' or '- Decision: accept'"
    )
    if not deferral_lines:
        consistency_detail = "not applicable: the record is not deferred"
    if offenders:
        consistency_detail = "; ".join(offenders)
    check("deferred-consistency", not offenders, consistency_detail)

    # 6. The ledger of counts and the verbatim deferral sentence.
    checked_rows = [row_id for row_id in row_ids if row_id in section_headings]
    print()
    print(
        "ROWS: " + str(len(row_ids)) + " declared in " + BUDGETS_REL
        + ", " + str(len(checked_rows)) + " checked in " + RECORD_REL
    )
    print("DEFERRAL: " + (deferral_sentence if deferral_sentence else "<absent>"))
    print("VERDICT: " + ("pass" if not problems else "fail"))

    report = {
        "tool": "tools/check_budget_record.py",
        "task": TASK,
        "root": root,
        "checks": checks,
        "problems": problems,
        "row_ids": row_ids,
        "rows_declared": len(row_ids),
        "rows_checked": len(checked_rows),
        "deferral_sentence": deferral_sentence if deferral_sentence else None,
        "verdict": "pass" if not problems else "fail",
    }
    write_json(args.json, report)
    return 0 if not problems else 1


def read_text(path: str) -> str:
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def write_json(path, report: dict) -> None:
    if not path:
        return
    directory = os.path.dirname(os.path.abspath(path))
    if directory and not os.path.isdir(directory):
        os.makedirs(directory, exist_ok=True)
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2)
        handle.write("\n")


if __name__ == "__main__":
    sys.exit(main())
