#!/usr/bin/env python3
"""Offline R9-A acceptance; one Editor at a time, no provider/service calls.

Run from this checkout: python3 artifacts/studio/verification/W-AI-06/r9-a/run.py
Requires pristine retained-candidate target assets and no journal for either retained ID.
Stops on the first failure; leaves failed receipts and assets for diagnosis, never repairs input.
An optional --lane selects regressions, npc, odd, or odd-reopen. odd-reopen also needs
--odd-output pointing to the successful odd run directory from an earlier invocation.
--resume-failed <prior owned attempt> is allowed only with --lane npc or odd;
it proves unchanged input/assets and a pre-apply failure without deleting history.
--revalidate-current-catalog explicitly re-reviews unchanged retained candidate bytes;
it is not a claim that the historical worker request is fresh against this catalog.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[5]
PROJECT = ROOT / "games/hollowmere"
ROWS = ROOT / "artifacts/studio/verification"
VIEW = "Hollowmere.R9_A.FinalDialogueWorkflowTests.Dialogue_AddLineConnectRename_OneFinalValidChangeSetAndUndoRestores"
ORPHAN = "Hollowmere.R6_B.DialogueCandidateTests.R6_Request6_RetainedFerrymanCandidateRefusesWithExactUnreachableWitness"
ENGINE_ORPHAN = "Hollowmere.R6_B.RealPathTests.R6_Request6_ActualEngineStageRejectsUnmodifiedFerrymanBeforeAnyWrite"
CLOSURE = "Hollowmere.R6_F.ContentClosureTests.R6_F_Request2_ValidatorRejectsExistingUnenrolledGraph"
NPC = ROWS / "W-AI-02/p42i-npc-20261007T053414.703761Z/workflow/ferryman2/candidate.json"
ODD = ROWS / "W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json"


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def launch(out, mode, *, results=None, required=(), resume_failed=None, revalidate=False):
    env = dict(os.environ)
    env.pop("GAMECORE_R9A_RESUME_FAILED", None)
    if resume_failed is not None:
        env["GAMECORE_R9A_RESUME_FAILED"] = str(resume_failed.resolve())
    env["GAMECORE_R9A_REVIEW_CURRENT_CATALOG"] = "1" if revalidate else "0"
    env.update(GAMECORE_ETOS_AUTOSTART="0", GAMECORE_R9A_OUTPUT=str(out),
               GAMECORE_R9A_MODE=mode, GCS_P32_OUT=str(out))
    args = ["bash", str(ROOT / "studio/tools/unity-batch.sh"), "--project", str(PROJECT),
            "--log-dir", str(out / "logs"), "--label", "r9-a-" + mode]
    if results:
        args += ["--results", str(results)]
        for name in required:
            args += ["--require-test", name]
        args += ["--", "-runTests", "-testPlatform", "EditMode", "-testFilter", ";".join(required)]
    else:
        env["UNITY"] = str(ROWS / "W-AI-02/r9-a/graphical-unity.py")
        args += ["--", "-executeMethod", "Hollowmere.R9_A.RetainedReplay.Run"]
    write(out / (mode + "-command.json"), {"argv": args, "mode": mode,
          "resumeFailed": str(resume_failed.resolve()) if resume_failed is not None else None,
          "reviewKind": "ExplicitRetainedReplay" if revalidate else "OriginalRequestContext"})
    with (out / (mode + "-launcher.log")).open("w") as log:
        run = subprocess.run(args, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT)
    if run.returncode:
        raise RuntimeError(f"{mode} failed ({run.returncode}); inspect {out}")
    if results:
        tree = ET.parse(results)
        cases = tree.findall(".//test-case")
        found = {case.get("fullname"): case.get("result") for case in cases}
        if not cases or any(value != "Passed" for value in found.values()) or any(found.get(name) != "Passed" for name in required):
            raise RuntimeError(f"Required XML cases did not all pass: {found}")
    else:
        result = out / ("reopen-result.json" if mode == "odd-reopen" else "result.json")
        if json.loads(result.read_text())["status"] != "pass":
            raise RuntimeError(f"Missing passing workflow receipt: {result}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--lane", choices=["all", "regressions", "npc", "odd", "odd-reopen"], default="all")
    parser.add_argument("--odd-output", type=Path)
    parser.add_argument("--resume-failed", type=Path)
    parser.add_argument("--revalidate-current-catalog", action="store_true")
    options = parser.parse_args()
    if options.resume_failed is not None and options.lane not in ("npc", "odd"):
        parser.error("--resume-failed requires --lane npc or odd")
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S.%fZ")
    outputs = {}
    for mode, row in [("regressions", "W-VIEW-02"), ("npc", "W-AI-02"), ("odd", "W-AI-03"), ("odd-reopen", "W-AI-06")]:
        if options.lane not in ("all", mode):
            continue
        if mode == "odd-reopen":
            out = options.odd_output or outputs.get("odd")
            if out is None:
                parser.error("--lane odd-reopen requires --odd-output")
            out = out.resolve()
            if (out / "reopen-result.json").exists():
                raise RuntimeError("Refusing to overwrite prior reopen proof")
        else:
            out = ROWS / row / ("r9-a-" + mode + "-" + stamp)
            out.mkdir(parents=True, exist_ok=False)
            write(out / "provenance.json", {
                "revision": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
                "utc": stamp, "lane": mode,
                "retained": {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in (NPC, ODD)},
            })
        outputs[mode] = out
        if mode == "regressions":
            launch(out, mode, results=out / "regressions.xml", required=(VIEW, ORPHAN, ENGINE_ORPHAN, CLOSURE))
        else:
            launch(out, mode, resume_failed=options.resume_failed, revalidate=options.revalidate_current_catalog)
        if mode == "odd-reopen":
            receipt = ROWS / row / ("r9-a-reopen-" + stamp)
            receipt.mkdir(parents=True, exist_ok=False)
            write(receipt / "result.json", {"status": "pass", "evidence": str(out.relative_to(ROOT)),
                  "reopen": json.loads((out / "reopen-result.json").read_text()),
                  "scope": "Odd-edit portion only; not a claim of complete W-AI-06 acceptance"})
        print(f"{mode}: verified receipts at {out}")


if __name__ == "__main__":
    main()
