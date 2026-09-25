#!/usr/bin/env python3
"""Run every existing probe mode repeatedly; retain failures, not redundant clean logs."""
import argparse
import concurrent.futures
import json
from pathlib import Path
import subprocess
import tempfile
import time

MODES = {
    "positive": ([], 0, "Pass"),
    "negative": (["-probeMissingRegistration"], 3, "ExpectedNegative"),
    "world": (["-probeWorldDispatch"], 0, "Pass"),
    "w1": (["-probeW1Gate"], 0, "Pass"),
    "w2": (["-probeW2Gate"], 0, "Pass"),
    "narrative": (["-probeNarrative"], 0, "Pass"),
    "cards": (["-probeCards"], 0, "Pass"),
    "w3": (["-probeW3Gate"], 0, "Pass"),
}


def run(item, player, root):
    mode, index = item
    args, expected_rc, expected_result = MODES[mode]
    with tempfile.TemporaryDirectory(prefix="gc-crash-") as temporary:
        result = Path(temporary) / "result.json"
        log = Path(temporary) / "player.log"
        command = [str(player), "-batchmode", "-nographics", "-logFile", str(log),
                   *args, "-probeResult", str(result)]
        try:
            process = subprocess.Popen(command, stdout=subprocess.DEVNULL,
                                       stderr=subprocess.PIPE)
            maps = ""
            while process.poll() is None:
                try:
                    current = Path(f"/proc/{process.pid}/maps").read_text()
                    if "UnityPlayer.so" in current and "GameAssembly.so" in current:
                        maps = current
                except OSError:
                    pass
            error = process.communicate(timeout=180)[1].decode("utf-8", errors="replace")
            rc = process.returncode
        except subprocess.TimeoutExpired as exc:
            rc = -999
            error = "timed out after 180 seconds: " + str(exc)
        try:
            data = json.loads(result.read_text())
            statuses = [step["status"] for step in data["probes"]]
            result_value = data["result"]
            valid = result_value == expected_result and "Fail" not in statuses and "Pass" in statuses
        except (OSError, ValueError, KeyError, TypeError) as exc:
            valid = False
            result_value = "invalid/missing: " + str(exc)
        clean = rc == expected_rc and valid
        if not clean:
            failed = root / "failures" / f"{mode}-{index:04d}"
            failed.mkdir(parents=True, exist_ok=True)
            if result.exists():
                (failed / "result.json").write_bytes(result.read_bytes())
            if log.exists():
                (failed / "player.log").write_bytes(log.read_bytes())
            (failed / "stderr.txt").write_text(error)
            if maps:
                (failed / "maps.txt").write_text(maps)
            (failed / "command.txt").write_text(" ".join(command) + "\n")
        return mode, index, rc, result_value, clean


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--runs", type=int, default=100)
    parser.add_argument("--workers", type=int, default=4)
    parser.add_argument("--modes", nargs="+", choices=MODES, default=list(MODES))
    opts = parser.parse_args()
    if opts.runs < 1 or opts.workers < 1:
        parser.error("runs and workers must be positive")
    opts.output.mkdir(parents=True, exist_ok=True)
    started = time.time()
    counts = {mode: {"pass": 0, "fail": 0, "crash": 0} for mode in opts.modes}
    items = [(mode, i) for mode in opts.modes for i in range(1, opts.runs + 1)]
    with concurrent.futures.ThreadPoolExecutor(max_workers=opts.workers) as pool:
        for mode, index, rc, result_value, clean in pool.map(
                lambda item: run(item, opts.player.resolve(), opts.output), items):
            count = counts[mode]
            count["pass" if clean else "fail"] += 1
            if rc == -11 or rc == 139:
                count["crash"] += 1
            if not clean:
                print(f"FAIL {mode} #{index}: rc={rc}, result={result_value}", flush=True)
            if (count["pass"] + count["fail"]) % 25 == 0:
                print(f"{mode}: {count}", flush=True)
    summary = {"player": str(opts.player.resolve()), "runs_per_mode": opts.runs,
               "workers": opts.workers, "elapsed_seconds": round(time.time() - started, 2),
               "counts": counts}
    (opts.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n")
    print(json.dumps(summary, indent=2), flush=True)
    return int(any(row["fail"] for row in counts.values()))


if __name__ == "__main__":
    raise SystemExit(main())
