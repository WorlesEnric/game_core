#!/usr/bin/env python3
"""Launch either retained real driver through the shared Editor allocator."""
import argparse
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("graphical", "batch"))
    parser.add_argument("config", type=Path)
    args = parser.parse_args()
    config_path = args.config.resolve()
    config = json.loads(config_path.read_text())
    evidence = Path(config["evidence"])
    if (evidence / "live-progress.json").exists():
        parser.error("use fresh live evidence; prior progress must not be overwritten")
    repo = Path(__file__).resolve().parents[6]
    env = os.environ.copy()
    editor = env.get("R6_D_UNITY_EDITOR", str(Path.home() / "Unity/Hub/Editor/6000.0.75f1/Editor/Unity"))
    if args.mode == "graphical":
        env["UNITY"] = str(Path(__file__).with_name("graphical-unity.sh"))
        env["GAMECORE_R6_D_UNITY_EDITOR"] = editor
        env["DISPLAY"] = ":1"
        method, flag = "Hollowmere.R6_D.RealAdmission.Run", "-gcR6DConfig"
    else:
        env["UNITY"] = editor
        method, flag = "Hollowmere.R6_A.RealAdmission.Run", "-gcR6Config"
    command = [
        str(repo / "studio/tools/unity-batch.sh"),
        "--project", str(repo / "games/hollowmere"),
        "--log-dir", str(evidence / "live-logs"),
        "--label", "r6-d-" + args.mode,
        "--timeout", "600", "--attempts", "1", "--",
        "-executeMethod", method, flag, str(config_path),
        "-upmLogFile", str(evidence / "upm.log"),
    ]
    status = subprocess.call(command, cwd=repo, env=env)
    if status:
        raise SystemExit(status)
    admitted = json.loads((evidence / "live-admit.json").read_text())
    undone = json.loads((evidence / "live-undo.json").read_text())
    assert admitted["outcome"] == "Admitted" and undone["outcome"] == "Undone"
    assert admitted["milliseconds"] <= 90000
    assert admitted["live"] == admitted["predicted"]
    assert admitted["smokeSteps"] == 120
    assert admitted["smokeTransitions"] == ["Pending", "Passed"]
    assert undone["before"] == undone["live"] == admitted["before"]
    if args.mode == "graphical":
        assert admitted["admissionWallMs"] <= 90000
        assert admitted["graphics"]["isPlaying"] and not admitted["graphics"]["isBatchMode"]
    print("R6_D_PASS", args.mode, "admissionMs=", admitted["milliseconds"], flush=True)


if __name__ == "__main__":
    main()
