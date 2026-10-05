#!/usr/bin/env python3
"""Retain P3.1 statistics unchanged; add route, profile and manual-save witnesses."""
import csv
import hashlib
import json
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
EVIDENCE = Path(__file__).resolve().parent


def write(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def analyze(run):
    subprocess.run(
        ["python3", str(ROOT / "games/hollowmere/Tools/frame_stats.py"), str(run)],
        check=True, stdout=subprocess.DEVNULL,
    )
    stats = json.loads((run / "frame-stats.json").read_text())
    text = (run / "frame-log.csv").read_text()
    rows = list(csv.DictReader(line for line in text.splitlines() if not line.startswith("#")))
    log = (run / "player.log").read_text()
    requested_marks = re.findall(r"^mark (.+)$", (ROOT / "games/hollowmere/Autoplay/playthrough.txt").read_text(), re.M)
    observed_marks = {marker for row in rows for marker in row["marker"].split("|")}
    missing = sorted(set(requested_marks) - observed_marks)
    command = re.search(r"\[autoplay\] \d+ ui save\.1 frame=(\d+)", log)
    saved = next((int(row["frame"]) for row in rows if "saved" in row["marker"].split("|")), None)
    capture = re.search(r"\[P3\.1b\] save capture slot-1 mainThreadMs=([\d.]+)", log)
    save = None
    if command and saved is not None:
        frame = int(command[1])
        window = [row for row in rows if frame - 2 <= int(row["frame"]) <= saved + 3]
        worst = max(window, key=lambda row: float(row["dt_ms"]))
        save = {
            "saveCommandFrame": frame, "savedMarkerFrame": saved,
            "windowRule": "two frames before save.1 through three frames after saved, inclusive",
            "windowFrames": len(window), "maxDtMs": float(worst["dt_ms"]),
            "worstFrame": int(worst["frame"]),
            "over100": sum(float(row["dt_ms"]) > 100 for row in window),
            "captureMainThreadMs": float(capture[1]) if capture else None,
            "rows": window,
        }
    write(run / "manual-save.json", save)
    renderer = re.search(r"^Renderer:\s*(.+)$", log, re.M)
    screen = re.search(r"^# screen (.+)$", text, re.M)
    exit_code = int((run / "exit-code.txt").read_text())
    profile = bool(renderer and "GeForce RTX 4060 Ti" in renderer[1] and screen and screen[1].startswith("1920x1080 "))
    complete = exit_code == 0 and not missing and bool(re.search(r"\[autoplay\] quit 0 at frame \d+", log))
    transition = next((t for t in stats["transitions"] if "region:Blackmere Marsh->Drowned Belfry" in t["marker"]), None)
    # Verify that the inherited load/restore marker rule adds no extra exclusions on this route.
    extra_windows = [t for t in stats["transitions"] if "region:" not in t["marker"]]
    result = {
        "test": "P31c_GPU_TwoGraphicalPlaythroughs", "exit": exit_code,
        "complete": complete, "missingRouteMarkers": missing,
        "renderer": renderer[1] if renderer else None,
        "screen": screen[1] if screen else None, "requiredProfile": profile,
        "p95Ms": stats["dtMs"]["p95"],
        "over100OutsideTransitions": stats["framesOver100msOutsideTransitionsCount"],
        "marshToBelfry": transition,
        "manualSaveWorstFrameMs": save["maxDtMs"] if save else None,
        "steadySeconds": stats["steadySeconds"],
        "nonRegionTransitionWindows": extra_windows,
        "checks": stats["checks"],
        "verdict": "PASS" if profile and complete and not extra_windows and stats["steadySeconds"] >= 600 and stats["verdict"] == "pass" else "FAIL",
    }
    write(run / "run-result.json", result)
    return result


if __name__ == "__main__":
    results = [analyze(EVIDENCE / "measurement" / f"run{run}") for run in (1, 2)]
    write(EVIDENCE / "result.json", {
        "sourceRevision": (EVIDENCE / "player/revision.txt").read_text().strip(),
        "runs": results,
        "verdict": "PASS" if any(run["verdict"] == "PASS" for run in results) else "FAIL",
        "passRule": "07 B-FRAME unchanged; two total attempts, the second is the only allowed repeat",
    })
    files = sorted(p for p in EVIDENCE.rglob("*") if p.is_file() and "player" not in p.relative_to(EVIDENCE).parts and p.name != "sha256-manifest.txt")
    (EVIDENCE / "sha256-manifest.txt").write_text("".join(
        f"{hashlib.sha256(p.read_bytes()).hexdigest()}  {p.relative_to(EVIDENCE)}\n" for p in files
    ))
