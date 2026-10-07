#!/usr/bin/env python3
"""Runs inside a real ETOS worker. Every row comes from actual etos query output."""
import base64
import json
from pathlib import Path
import re
import subprocess
import time


def require(value, message):
    if not value:
        raise RuntimeError(message)


def quote(value):
    return "'" + value.replace("'", "''") + "'"


def run_query(name, sql):
    command = ["etos", "--json", "query", "--fresh", "--format", "jsonl", "--name", name, sql]
    process = subprocess.run(command, capture_output=True, text=True, timeout=60)
    receipt = {"command": command, "exitCode": process.returncode,
               "stdout": process.stdout, "stderr": process.stderr, "rows": []}
    if process.returncode:
        return receipt
    wire = json.loads(process.stdout)
    receipt["rows"] = query_rows(wire)
    # ETOS JSONL starts with an etos_query provenance header, not a data row.
    # Keep that file verbatim; the CLI's structured rows member is the data contract.
    path = Path(wire["file"]["path"])
    require(path.is_relative_to(Path("/outputs/queries")) and path.suffix == ".jsonl",
            "ETOS query did not expose its worker-side JSONL result")
    receipt["resultFile"] = str(path)
    receipt["resultText"] = path.read_text()
    return receipt


def query_rows(wire):
    require(isinstance(wire, dict) and isinstance(wire.get("rows"), list), "ETOS --json omitted structured rows")
    require(wire.get("complete") is True and not wire.get("partial") and not wire.get("cursor"),
            "ETOS returned an incomplete query")
    rows = wire["rows"]
    require(all(isinstance(row, dict) for row in rows), "ETOS rows were not objects")
    require(wire.get("total_rows") == len(rows), "ETOS structured rows were truncated")
    return rows


def main():
    request = Path("/inputs/request.md").read_text()
    owner = re.search(r"owner namespace is `([a-f0-9]{64})`", request)
    require(owner is not None, "Production worker request omitted discoverable owner namespace")
    selection = json.loads(Path("/inputs/selection.json").read_text())
    index = json.loads(Path("/inputs/index-slice.json").read_text())
    selected_guid = selection["targets"][0]["assetGuid"]
    npc = next(node for node in index["nodes"] if node["ref"].get("assetGuid") == selected_guid)
    dialogue = next(reference["to"] for reference in npc["refs"] if reference["field"] == "dialogue")
    spec = {"owner": owner[1], "graphAssetGuid": dialogue["assetGuid"], "graphAuthoringId": dialogue.get("authoringId")}
    receipt = {"source": "real worker etos query", "spec": spec, "attempts": [], "pass": False}
    deadline = time.monotonic() + 90
    try:
        while True:
            discovery = run_query("r8a-discovery", "SELECT graph FROM gc_definition WHERE owner = " + quote(spec["owner"])
                                  + " AND asset_guid = " + quote(spec["graphAssetGuid"]) + " AND removed=false")
            receipt["attempts"].append(discovery)
            live = [row for row in discovery["rows"] if row.get("removed") is not True and row.get("graph")]
            if live:
                require(len(live) == 1, "Graph discovery must be unique within authenticated project owner")
                break
            require(time.monotonic() < deadline, "Production publication never became query-visible")
            time.sleep(1)
        graph = live[0]["graph"]
        receipt["graph"] = graph
        selected = run_query("r8a-selected", "SELECT * FROM gc_dialogue_node WHERE owner = " + quote(spec["owner"])
                             + " AND graph = " + quote(graph) + " AND removed=false ORDER BY node_index")
        all_rows = run_query("r8a-all", "SELECT * FROM gc_dialogue_node WHERE owner = " + quote(spec["owner"]) + " AND removed=false")
        receipt.update(discovery=discovery, selected=selected, all=all_rows)
        require(selected["exitCode"] == all_rows["exitCode"] == 0, "Real selected/unfiltered query failed")
        require(selected["rows"] and all_rows["rows"], "Real selected/unfiltered query returned zero rows")
        require(all(row.get("owner") == spec["owner"] and row.get("graph") == graph for row in selected["rows"]), "Foreign graph rows leaked")
        receipt["pass"] = True
    except Exception as error:
        receipt["error"] = str(error)
    question = "R8A_QUERY_BEGIN" + json.dumps(receipt, ensure_ascii=False, separators=(",", ":")) + "R8A_QUERY_END Would you like dialogue changes?"
    Path("/outputs/clarification.json").write_text(json.dumps({"status": "needs-clarification", "question": question}, ensure_ascii=False))
    print(json.dumps(receipt, ensure_ascii=False), flush=True)
    return 0 if receipt["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
