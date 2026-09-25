#!/usr/bin/env python3
"""Run the probe under gdb until SIGSEGV, retaining full native thread stacks."""
import argparse
from pathlib import Path
import subprocess
import tempfile

MODES = {
    "positive": [], "negative": ["-probeMissingRegistration"],
    "world": ["-probeWorldDispatch"], "w1": ["-probeW1Gate"],
    "w2": ["-probeW2Gate"], "narrative": ["-probeNarrative"],
    "cards": ["-probeCards"], "w3": ["-probeW3Gate"],
}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--player", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--mode", choices=MODES, default="world")
    parser.add_argument("--runs", type=int, default=100)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="gc-gdb-") as temporary:
        result = Path(temporary) / "result.json"
        log = Path(temporary) / "player.log"
        command = ["gdb", "-batch", "-quiet", "-ex", "set debuginfod enabled off",
                   "-ex", "set pagination off", "-ex", "set print thread-events off",
                   "-ex", "handle SIGPWR SIGXCPU SIG33 nostop noprint pass", "-ex", "run",
                   "-ex", "thread apply all bt 30", "--args", str(args.player.resolve()),
                   "-batchmode", "-nographics", "-logFile", str(log),
                   *MODES[args.mode], "-probeResult", str(result)]
        for index in range(1, args.runs + 1):
            result.unlink(missing_ok=True)
            completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                       timeout=300, text=True, errors="replace")
            text = completed.stdout
            if "Program received signal SIGSEGV" in text or "Program received signal SIGABRT" in text:
                (args.output / "backtrace.txt").write_text(text)
                if result.exists():
                    (args.output / "result.json").write_bytes(result.read_bytes())
                if log.exists():
                    (args.output / "player.log").write_bytes(log.read_bytes())
                (args.output / "attempts.txt").write_text(f"mode={args.mode} attempts={index}\n")
                print(f"Captured {args.mode} crash on attempt {index}", flush=True)
                return 0
            if index % 10 == 0:
                print(f"{args.mode}: {index} gdb runs, no SIGSEGV/SIGABRT", flush=True)
        (args.output / "attempts.txt").write_text(f"mode={args.mode} attempts={args.runs} no crash\n")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
