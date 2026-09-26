#!/usr/bin/env python3
"""Derive the TEST-001..TEST-024 per-suite status matrix from the result files a matrix run produced.

Called by `tools/conformance/run_test_matrix.sh`, which passes its suite table as `<id>|<dotnet>|<unity>|<probes>`
rows. The dotnet half is a project path plus a NUnit filter, the Unity half an assembly name, and the probe half a
list of `tools/unity/run_*_probe.sh` harnesses. Every status here is READ out of a result file:

  * dotnet  -> the vstest TRX files under `<ARTIFACTS>/trx/`, filtered by the suite's own `FullyQualifiedName~X`
               substring, so a filtered run is reproducible from the one unfiltered sweep
  * unity   -> the NUnit3 result XML under `<ARTIFACTS>/unity/`, selected by assembly name
  * probes  -> the probe result JSON under `<ARTIFACTS>/toolchain/` and `<ARTIFACTS>/release/`, matched by the
               mode the harness's own flag selects

A suite with no matching result at all is `NotRun`, never `Pass`. A suite whose declared commands produced a
failure is `Fail`. Exit code 1 when any requested suite is `Fail`.

Usage: summarize_suite_matrix.py '<id>|<dotnet>|<unity>|<probes>' ...
Environment: ARTIFACTS (required), REPO_ROOT (default: two levels up), SUITE (optional single suite),
             PROBE_RUNS (informational, recorded in the document).
"""

import json
import os
import re
import sys
import xml.etree.ElementTree as ET

STATUS_ORDER = {"Pass": 0, "NotRun": 2, "Fail": 4}


def worst(statuses):
    if not statuses:
        return "NotRun"
    return max(statuses, key=lambda s: STATUS_ORDER.get(s, 2))


def read_trx_results(trx_dir):
    """-> {(className.method): 'Pass'|'Fail'} plus the files read."""
    results = {}
    files = []
    if not os.path.isdir(trx_dir):
        return results, files
    namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    for name in sorted(os.listdir(trx_dir)):
        if not name.endswith(".trx"):
            continue
        path = os.path.join(trx_dir, name)
        files.append(name)
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        definitions = {}
        for unit in root.findall(".//t:UnitTest", namespace):
            method = unit.find("t:TestMethod", namespace)
            if method is None:
                continue
            definitions[unit.attrib.get("id", "")] = (
                method.attrib.get("className", ""),
                method.attrib.get("name", ""),
            )
        for result in root.findall(".//t:UnitTestResult", namespace):
            class_name, method_name = definitions.get(result.attrib.get("testId", ""), ("", ""))
            full = (class_name + "." + method_name) if class_name else result.attrib.get("testName", "")
            if not full:
                continue
            outcome = "Pass" if result.attrib.get("outcome") == "Passed" else "Fail"
            if results.get(full) == "Fail":
                continue
            results[full] = outcome
    return results, files


def read_unity_assemblies(unity_dir):
    """-> {assembly name: (pass, fail)} plus the files read."""
    assemblies = {}
    files = []
    if not os.path.isdir(unity_dir):
        return assemblies, files
    for name in sorted(os.listdir(unity_dir)):
        if not name.endswith(".xml"):
            continue
        path = os.path.join(unity_dir, name)
        files.append(name)
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError:
            continue
        for suite in root.iter("test-suite"):
            if suite.attrib.get("type") != "Assembly":
                continue
            assembly = suite.attrib.get("name", "")
            passed = int(suite.attrib.get("passed") or 0)
            failed = int(suite.attrib.get("failed") or 0)
            previous = assemblies.get(assembly, (0, 0))
            # Unity records an assembly as `<Name>.dll`; the suite table names the assembly.
            assembly = assembly[:-4] if assembly.endswith(".dll") else assembly
            previous = assemblies.get(assembly, (0, 0))
            assemblies[assembly] = (previous[0] + passed, previous[1] + failed)
    return assemblies, files


def probe_mode_of(repo_root, entry):
    """The probe mode a suite-table entry selects.

    Entry form: `<harness>[:<Mode>[:<args>]]`. An explicit mode wins; otherwise the harness's own `-probe<Mode>`
    flag is read out of the harness script.
    """
    harness, _, rest = entry.partition(":")
    explicit = rest.partition(":")[0]
    if explicit:
        return harness, explicit
    path = os.path.join(repo_root, "tools/unity", harness)
    if not os.path.exists(path):
        return harness, None
    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        text = handle.read()
    match = re.search(r"-probe([A-Za-z][A-Za-z0-9]*)", text)
    return harness, (match.group(1) if match else None)


def read_probes(artifacts):
    """-> {(mode lower): [(result, failing steps, path)]}."""
    documents = {}
    for directory, _, filenames in os.walk(artifacts):
        for name in sorted(filenames):
            if not name.startswith("probe-") or not name.endswith(".json"):
                continue
            path = os.path.join(directory, name)
            if name.endswith(".trace.json"):
                continue
            try:
                with open(path, "r", encoding="utf-8") as handle:
                    document = json.load(handle)
            except (ValueError, OSError):
                continue
            if not isinstance(document, dict) or "probes" not in document:
                continue
            mode = str(document.get("mode") or "").lower()
            failing = [entry.get("name") for entry in document["probes"]
                       if entry.get("status") not in ("Pass", "ExpectedNegative")]
            documents.setdefault(mode, []).append(
                (document.get("result"), failing, os.path.relpath(path, artifacts)))
    return documents


def main(argv):
    artifacts = os.environ.get("ARTIFACTS")
    if not artifacts:
        print("summarize_suite_matrix.py: ARTIFACTS is required", file=sys.stderr)
        return 2
    repo_root = os.environ.get("REPO_ROOT") or os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
    only = os.environ.get("SUITE") or ""
    probe_runs = os.environ.get("PROBE_RUNS") or "2"

    dotnet_results, trx_files = read_trx_results(os.path.join(artifacts, "trx"))
    unity_assemblies, unity_files = read_unity_assemblies(os.path.join(artifacts, "unity"))
    probes = read_probes(artifacts)

    rows = []
    failing_suites = []
    for row in argv:
        fields = row.split("|")
        if len(fields) != 4:
            print("summarize_suite_matrix.py: malformed suite row: " + row, file=sys.stderr)
            return 2
        suite_id, dotnet_field, unity_field, probe_field = fields
        if only and suite_id != only:
            continue

        project = ""
        name_filter = ""
        if dotnet_field:
            project, _, name_filter = dotnet_field.partition(":")

        # --- dotnet half
        if project:
            # A `*` filter means "the whole project": this repository names every test namespace after its
            # assembly, so the project directory name is an exact selector over the recorded FQNs.
            selector = name_filter
            if not selector or selector == "*":
                selector = os.path.basename(project) + "."
            elif "~" in selector:
                # `FullyQualifiedName~Value` selects the substring `Value`; only `~` is used in this table.
                selector = selector.rsplit("~", 1)[1]
            selected = [name for name in dotnet_results if selector in name]
            outcomes = [dotnet_results[name] for name in selected]
            dotnet_status = worst(outcomes) if outcomes else "NotRun"
            dotnet_detail = ("%d matching test(s)" % len(selected)) if selected else "no matching result"
        else:
            dotnet_status = "NotRun"
            dotnet_detail = "the suite table declares no dotnet command"

        # --- Unity half
        if unity_field:
            counts = unity_assemblies.get(unity_field)
            if counts is None:
                unity_status = "NotRun"
                unity_detail = "assembly " + unity_field + " has no recorded result"
            else:
                passed, failed = counts
                unity_status = "Pass" if failed == 0 and passed > 0 else "Fail"
                unity_detail = "%d passed, %d failed" % (passed, failed)
        else:
            unity_status = "NotRun"
            unity_detail = "the suite table declares no Unity assembly"

        # --- probe half
        probe_statuses = []
        probe_details = []
        harnesses = [entry for entry in probe_field.split(",") if entry]
        for entry in harnesses:
            harness, mode = probe_mode_of(repo_root, entry)
            if mode is None:
                probe_statuses.append("NotRun")
                probe_details.append(entry + ": no recorded mode")
                continue
            documents = probes.get(mode.lower())
            if not documents:
                probe_statuses.append("NotRun")
                probe_details.append(harness + " (" + mode + "): no result file")
                continue
            status = "Pass"
            for result, failing, path in documents:
                if result == "Pass" and not failing:
                    continue
                if result == "ExpectedNegative" and not failing:
                    continue
                status = "Fail"
                probe_details.append(harness + " (" + mode + "): " + str(result) + " " + str(failing) + " in " + path)
            if status == "Pass":
                probe_details.append(harness + " (" + mode + "): %d document(s) pass" % len(documents))
            probe_statuses.append(status)

        if not harnesses:
            probe_statuses = []
            probe_details = ["the suite table declares no player probe for this suite"]

        # A suite is judged on the evidence its own table row declares: an empty column is "this suite is not
        # hosted here", not a missing result. Run with the default table in this repository, every column is filled.
        declared = []
        if project:
            declared.append(dotnet_status)
        if unity_field:
            declared.append(unity_status)
        declared.extend(probe_statuses)
        status = worst(declared)
        if status == "Fail":
            failing_suites.append(suite_id)

        rows.append({
            "suite": suite_id,
            "status": status,
            "dotnet": {"status": dotnet_status, "project": project, "filter": name_filter, "detail": dotnet_detail},
            "unity": {"status": unity_status, "assembly": unity_field, "detail": unity_detail},
            "probes": {"status": worst(probe_statuses), "harnesses": harnesses, "detail": probe_details},
        })

    document = {
        "artifact": "gamecore.conformance.suite-matrix/1",
        "generator": "tools/conformance/summarize_suite_matrix.py",
        "probeRuns": probe_runs,
        "corpus": {
            "trxFiles": len(trx_files),
            "unityFiles": len(unity_files),
            "probeModes": sorted(probes),
        },
        "suites": rows,
        "counts": {},
        "failing": failing_suites,
    }
    for status in STATUS_ORDER:
        document["counts"][status] = sum(1 for row in rows if row["status"] == status)

    out = os.environ.get("SUITE_JSON") or os.path.join(artifacts, "suite-status.json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
        handle.write("\n")

    print("%-10s %-8s %-8s %-8s %-8s" % ("suite", "status", "dotnet", "unity", "probes"))
    for row in rows:
        print("%-10s %-8s %-8s %-8s %-8s" % (
            row["suite"], row["status"], row["dotnet"]["status"], row["unity"]["status"], row["probes"]["status"]))
    print("matrix: " + out)
    print("counts: " + ", ".join("%s=%d" % (k, v) for k, v in document["counts"].items()))

    return 1 if failing_suites else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
