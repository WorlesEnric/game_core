#!/usr/bin/env python3
"""Run the new-lever prerequisite probe, capture :1, and quit the owned Editor.

This is NOT a stage/admit walkthrough pass. The tested product boundary currently
refuses every new package's smoke entry; no candidate or signing credential is used.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile
import time

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[6]
PROJECT = REPO / "games/hollowmere"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    evidence = args.evidence.resolve()
    evidence.mkdir(parents=True, exist_ok=False)
    if (PROJECT / "UserSettings/GameCoreStudio.json").exists():
        raise RuntimeError("Existing ETOS settings are not read or overwritten")
    with tempfile.TemporaryDirectory(prefix="r8-b-unpaired-") as directory:
        env = os.environ.copy()
        env["UNITY"] = str(REPO / "games/hollowmere/Assets/Hollowmere/Tests/R6_E/graphical-unity.sh")
        env["GAMECORE_R6_E_PAIRING_PATH"] = str(Path(directory) / "missing.json")
        env["DISPLAY"] = ":1"
        report = evidence / "probe.json"
        command = ["bash", str(REPO / "studio/tools/unity-batch.sh"),
                   "--project", str(PROJECT), "--log-dir", str(evidence / "logs"),
                   "--label", "r8-b-lever", "--", "-force-glcore", "-executeMethod",
                   "Hollowmere.R8_B.LeverAdmissionProbe.Run", "-gcR8BReport", str(report)]
        with subprocess.Popen(command, cwd=REPO, env=env) as process:
            deadline = time.monotonic() + 1500
            while not report.exists():
                if process.poll() is not None or time.monotonic() >= deadline:
                    raise RuntimeError("Probe did not produce a report; retain launcher logs")
                time.sleep(1)
            result = json.loads(report.read_text())
            if result["isBatchMode"] or result["display"] != ":1":
                raise RuntimeError("Graphical prerequisite was not exercised")
            time.sleep(3)
            subprocess.run(["import", "-display", ":1", "-window", "root", str(evidence / "probe.png")], check=True)
            Path(str(report) + ".quit").touch()
            code = process.wait(timeout=120)
            if code != 0:
                raise RuntimeError("Owned Editor did not exit successfully")
    print("Prerequisite refusal captured; W-DOC-02 is not a pass")


if __name__ == "__main__":
    main()
