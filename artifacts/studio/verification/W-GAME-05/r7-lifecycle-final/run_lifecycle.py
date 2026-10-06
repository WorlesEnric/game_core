#!/usr/bin/env python3
"""Run the real Linux player twice: menu/save/QUIT, then menu/load/ending/restart.

Build first: GC_STUDIO_UNITY_SLOTS=1 bash studio/tools/build_game_player.sh r7-c
Run/record from the repository root (no Editor, services or network changes):
  python3 games/hollowmere/Assets/Hollowmere/Tests/R7_C/PlayerFlow/run_lifecycle.py \
    --output /absolute/fresh/evidence/directory --record
Omit --record for the graphical driver alone, which is not recording evidence.
The output directory MUST NOT exist. DISPLAY defaults to :1. Recording requires
ffmpeg/ffprobe and the PulseAudio monitor; absent audio is a failure, never a silent fallback.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone


STAGES = ["menu", "newgame", "paused", "saved", "quit-ready", "menu-relaunched", "loaded", "ending", "restarted"]


def utc():
    return datetime.now(timezone.utc).isoformat()


def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(chunk)
    return result.hexdigest()


def main():
    here = Path(__file__).resolve().parent
    root = here.parents[6]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--player", type=Path, default=root / "build/HollowmereLinux/Hollowmere.x86_64")
    parser.add_argument("--record", action="store_true")
    parser.add_argument("--display", default=os.environ.get("DISPLAY", ":1"))
    parser.add_argument("--pulse-source", default="@DEFAULT_MONITOR@")
    parser.add_argument("--phase-timeout", type=int, default=900)
    args = parser.parse_args()
    player = args.player.resolve()
    out = args.output.resolve()
    if not player.is_file() or not os.access(player, os.X_OK):
        parser.error("build the executable Linux IL2CPP player first: " + str(player))
    if args.phase_timeout <= 0:
        parser.error("--phase-timeout must be positive")
    if args.record and (not shutil.which("ffmpeg") or not shutil.which("ffprobe")):
        parser.error("recording requires ffmpeg and ffprobe")
    out.mkdir(parents=True, exist_ok=False)
    saves = out / "saves"
    saves.mkdir()
    env = dict(os.environ, DISPLAY=args.display)
    summary = {
        "row": "W-GAME-05", "status": "RUNNING", "startedUtc": utc(), "recorded": args.record,
        "player": str(player), "executableSha256": sha256(player),
        "revision": (player.parent / "revision.txt").read_text().strip(),
        "servicesChanged": False, "phases": [], "failure": "",
    }
    recorder = None
    recorder_log = None
    child = None
    try:
        shutil.copy2(__file__, out / "run_lifecycle.py")
        for name in ("save-quit.autoplay", "load-ending-restart.autoplay"):
            shutil.copy2(here / name, out / name)
        if args.record:
            capture = [
                "ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "warning", "-n",
                "-f", "x11grab", "-draw_mouse", "0", "-framerate", "30", "-video_size", "1920x1080",
                "-thread_queue_size", "1024", "-i", args.display + ".0",
                "-f", "pulse", "-thread_queue_size", "1024", "-i", args.pulse_source,
                "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
                "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(out / "playthrough.mp4"),
            ]
            summary["recordCommand"] = capture
            recorder_log = (out / "ffmpeg.log").open("w")
            recorder = subprocess.Popen(capture, env=env, stdin=subprocess.DEVNULL, stdout=recorder_log, stderr=subprocess.STDOUT)
            time.sleep(2)
            if recorder.poll() is not None:
                raise RuntimeError("recorder exited before launch; see ffmpeg.log (audio is required)")
        for script, expected in (("save-quit.autoplay", "AWAITING_RELAUNCH"), ("load-ending-restart.autoplay", "PASS")):
            phase = script.removesuffix(".autoplay")
            command = [str(player), "-screen-fullscreen", "1", "-screen-width", "1920", "-screen-height", "1080",
                       "-autoplay", str(out / script), "-saveDir", str(saves),
                       "-frameLog", str(out / (phase + "-frames.csv")), "-logFile", str(out / (phase + "-player.log"))]
            receipt = {"name": phase, "command": command, "startedUtc": utc()}
            summary["phases"].append(receipt)
            with (out / (phase + "-stdout.txt")).open("w") as stdout:
                child = subprocess.Popen(command, cwd=player.parent, env=env, stdin=subprocess.DEVNULL,
                                         stdout=stdout, stderr=subprocess.STDOUT)
                receipt["pid"] = child.pid
                deadline = time.monotonic() + args.phase_timeout
                while child.poll() is None:
                    if recorder is not None and recorder.poll() is not None:
                        raise RuntimeError("recorder stopped during " + phase)
                    if time.monotonic() > deadline:
                        raise RuntimeError("player phase exceeded timeout: " + phase)
                    time.sleep(0.5)
                receipt["exitCode"] = child.returncode
                child = None
            receipt["finishedUtc"] = utc()
            if receipt["exitCode"] != 0:
                raise RuntimeError("player failed: " + phase + " exit=" + str(receipt["exitCode"]))
            report = json.loads((saves / "lifecycle-report.json").read_text())
            shutil.copy2(saves / "lifecycle-report.json", out / (phase + "-report.json"))
            if report.get("backend") != "IL2CPP" or report.get("platform") != "LinuxPlayer":
                raise RuntimeError("lifecycle evidence requires the Linux IL2CPP player, not another backend/platform")
            if report.get("revision") != summary["revision"]:
                raise RuntimeError("lifecycle receipt does not identify the launched build revision")
            if report["status"] != expected:
                raise RuntimeError("missing production lifecycle acceptance: " + json.dumps(report))
            names = [stage["name"] for stage in report["stages"]]
            if names != (STAGES[:5] if expected == "AWAITING_RELAUNCH" else STAGES):
                raise RuntimeError("lifecycle stages missing or out of order: " + str(names))
            if not report["saveProcessQuitObserved"] or (expected == "PASS" and not report["finalProcessQuitObserved"]):
                raise RuntimeError("production UI quit not observed")
            time.sleep(2)  # Recorder stays alive through the actual process boundary.
        if recorder is not None:
            recorder.send_signal(signal.SIGINT)
            capture_exit = recorder.wait(timeout=30)
            recorder = None
            summary["recorderExit"] = capture_exit
            if capture_exit not in (0, 255):  # ffmpeg reports 255 for an orderly SIGINT flush.
                raise RuntimeError("recorder failed with " + str(capture_exit))
            metadata = subprocess.run(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json",
                                       str(out / "playthrough.mp4")], check=True, capture_output=True, text=True)
            probe = json.loads(metadata.stdout)
            (out / "recording-metadata.json").write_text(json.dumps(probe, indent=2) + "\n")
            kinds = {stream["codec_type"] for stream in probe["streams"]}
            if not {"audio", "video"}.issubset(kinds):
                raise RuntimeError("recording must contain both actual monitor audio and video")
        summary["status"] = "PASS"
    except (Exception, KeyboardInterrupt) as error:
        summary["status"] = "FAIL"
        summary["failure"] = str(error) or type(error).__name__
    finally:
        if child is not None and child.poll() is None:
            child.terminate()
            try:
                child.wait(timeout=15)
            except subprocess.TimeoutExpired:
                child.kill()
                child.wait()
        if recorder is not None and recorder.poll() is None:
            recorder.send_signal(signal.SIGINT)
            try:
                recorder.wait(timeout=30)
            except subprocess.TimeoutExpired:
                recorder.kill()
                recorder.wait()
        if recorder_log is not None:
            recorder_log.close()
        summary["finishedUtc"] = utc()
        (out / "driver-summary.json").write_text(json.dumps(summary, indent=2) + "\n")
        with (out / "SHA256SUMS").open("w") as hashes:
            for path in sorted(out.rglob("*")):
                if path.is_file() and path.name != "SHA256SUMS":
                    hashes.write(sha256(path) + "  " + str(path.relative_to(out)) + "\n")
    print(json.dumps(summary, indent=2))
    return 0 if summary["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
