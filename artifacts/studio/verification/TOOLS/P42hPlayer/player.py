#!/usr/bin/env python3
"""Packet adapter: production builder + P3.1d's four locked routes + R7-C capture profile.

Build: python3 artifacts/studio/verification/TOOLS/P42hPlayer/player.py build --output "$PWD/.evidence/live/P42hPlayer"
Run:   python3 artifacts/studio/verification/TOOLS/P42hPlayer/player.py run --output "$PWD/.evidence/live/P42hPlayer"
The build output must be fresh. Run only once per output. No service operations.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import signal
import subprocess
import sys
import time
from window import prepare

ROOT = Path(__file__).resolve().parents[5]
SELF = Path(__file__).resolve()
TOOLS = ROOT / "games/hollowmere/Tools"


def dump(path, value):
    path.write_text(json.dumps(value, indent=2) + "\n")


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def manifest(out):
    with (out / "SHA256SUMS").open("w") as stream:
        for path in sorted(out.rglob("*")):
            if path.is_file() and path.name != "SHA256SUMS":
                stream.write(digest(path) + "  " + str(path.relative_to(out)) + "\n")


def logged(command, log, env=None):
    dump(log.with_suffix(".command.json"), command)
    with log.open("w") as stream:
        return subprocess.run(command, cwd=ROOT, env=env, stdin=subprocess.DEVNULL,
                              stdout=stream, stderr=subprocess.STDOUT).returncode


def stop(child):
    if child is not None and child.poll() is None:
        child.send_signal(signal.SIGINT)
        try:
            child.wait(timeout=30)
        except subprocess.TimeoutExpired:
            child.kill()
            child.wait()


def capture(arguments):
    # Invoked by the unchanged P3.1d runner in place of its executable; all player
    # arguments reach the real binary unchanged. This is NOT an unrecorded P3.1d run.
    target = Path(arguments[arguments.index("-frameLog") + 1]).parent
    player = os.environ["P42H_REAL_PLAYER"]
    command = ["ffmpeg", "-hide_banner", "-nostdin", "-loglevel", "warning", "-n",
               "-f", "x11grab", "-draw_mouse", "0", "-framerate", "30", "-video_size", "1920x1080",
               "-thread_queue_size", "1024", "-i", ":1.0", "-f", "pulse", "-thread_queue_size", "1024",
               "-i", os.environ.get("P42H_PULSE_SOURCE", "@DEFAULT_MONITOR@"),
               "-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p",
               "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart", str(target / "playthrough.mp4")]
    receipt = {"status": "FAIL", "captureCommand": command, "playerCommand": [player, *arguments]}
    recorder = child = None
    def interrupted(signum, frame):
        raise RuntimeError("capture interrupted by signal " + str(signum))
    for sig in (signal.SIGTERM, signal.SIGHUP, signal.SIGINT):
        signal.signal(sig, interrupted)
    try:
        child = subprocess.Popen([player, *arguments], stdin=subprocess.DEVNULL)
        receipt["playerPid"] = child.pid
        log_path = Path(arguments[arguments.index("-logFile") + 1])
        readiness_deadline = time.monotonic() + 30
        while not log_path.exists() or "frame pacing" not in log_path.read_text(errors="replace"):
            if child.poll() is not None or time.monotonic() >= readiness_deadline:
                raise RuntimeError("Player did not reach frame-pacing readiness before capture")
            time.sleep(0.1)
        window = prepare(child.pid)
        receipt["ownedWindowId"] = hex(window)
        receipt["captureTarget"] = "Exact _NET_WM_PID-matched player window, moved to 0,0 and raised; not desktop pixels"
        command[command.index("-f"):command.index("-f")] = ["-window_id", hex(window)]
        dump(target / "capture-start.json", receipt)
        with (target / "ffmpeg.log").open("w") as stream:
            recorder = subprocess.Popen(command, stdin=subprocess.DEVNULL, stdout=stream, stderr=subprocess.STDOUT)
            time.sleep(2)
            if recorder.poll() is not None:
                raise RuntimeError("owned-window recorder failed; see ffmpeg.log")
            capture_started = time.monotonic()
            while child.poll() is None:
                if recorder.poll() is not None:
                    raise RuntimeError("recorder stopped while player was active")
                time.sleep(0.5)
                if time.monotonic() - capture_started >= 15 and not (target / "window-proof.png").exists():
                    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-y", "-f", "x11grab", "-window_id", hex(window), "-video_size", "1920x1080", "-i", ":1.0", "-frames:v", "1", str(target / "window-proof.png")], check=True, timeout=10, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            receipt["playerExit"] = child.returncode
            stop(recorder)
            receipt["recorderExit"] = recorder.returncode
        if child.returncode != 0 or recorder.returncode not in (0, 255):
            raise RuntimeError("player or capture failed")
        rc = logged(["ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json",
                     str(target / "playthrough.mp4")], target / "recording-metadata.json")
        if rc != 0:
            raise RuntimeError("ffprobe failed")
        probe = json.loads((target / "recording-metadata.json").read_text())
        streams = probe["streams"]
        receipt["checks"] = {
            "video1080p": any(s.get("codec_type") == "video" and s.get("width") == 1920 and s.get("height") == 1080 for s in streams),
            "monitorAudio": any(s.get("codec_type") == "audio" for s in streams),
            "recordingAtLeast600Seconds": float(probe["format"]["duration"]) >= 600,
        }
        receipt["status"] = "PASS" if all(receipt["checks"].values()) else "FAIL"
    except (Exception, KeyboardInterrupt) as error:
        receipt["failure"] = str(error)
    finally:
        stop(child)
        stop(recorder)
        dump(target / "capture-result.json", receipt)
    return 0 if receipt["status"] == "PASS" else 1


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("build", "run"))
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    out = args.output.resolve()
    if args.action == "build":
        out.mkdir(parents=True, exist_ok=False)
    elif not out.is_dir():
        parser.error("build the fresh packet output first")
    elif (out / "measurement").exists():
        parser.error("measurement already exists; retain it and use a fresh build/output")
    result = {"row": "W-GAME-01", "status": "BLOCKED", "requestIds": [], "taskIds": [],
              "ownerDefinition": "OPEN: VSync measurement definition remains owner-owned; neither profile replaces the other",
              "captureOverhead": "All four P3.1d routes are recorded using R7-C ffmpeg settings; the runner's historical 'No video/screen capture' provenance line does not describe these wrapped runs.",
              "profiles": {"vsync0": {"status": "BLOCKED"}, "vsync1": {"status": "BLOCKED"}}}
    try:
        harness_revision = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
        revision = harness_revision if args.action == "build" else json.loads((out / "player/build-report.json").read_text())["revision"]
        if args.action == "run":
            subprocess.run(["git", "diff", "--quiet", revision, harness_revision, "--", "Packages", "games", "studio/agent", "studio/stage"], cwd=ROOT, check=True)
        result["revision"] = revision
        result["harnessRevision"] = harness_revision
        env = dict(os.environ, GC_STUDIO_UNITY_SLOTS="1")
        if args.action == "build":
            sources = out / "sources"
            sources.mkdir()
            inputs = [SELF, TOOLS / "measure_frames_p31d.sh", TOOLS / "qualify_p31d.py",
                      TOOLS / "frame_stats.py", ROOT / "games/hollowmere/Autoplay/playthrough.txt",
                      ROOT / "studio/tools/unity-batch.sh", ROOT / "studio/tools/unity-slot.sh",
                      ROOT / "games/hollowmere/Assets/Hollowmere/Build/Editor/Build.cs"]
            for path in inputs:
                destination = sources / path.relative_to(ROOT)
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(path, destination)
        if args.action == "build":
            command = ["bash", str(ROOT / "studio/tools/unity-batch.sh"), "--project", str(ROOT / "games/hollowmere"),
                       "--log-dir", str(out / "build-logs"), "--label", "p42h-player", "--timeout", "3600", "--attempts", "1", "--",
                       "-executeMethod", "Hollowmere.Build.BuildLinuxPlayer", "-buildOutput", str(out / "player"), "-buildRevision", revision]
            rc = logged(command, out / "build-transcript.txt", env)
            result["buildExit"] = rc
            if rc != 0:
                raise RuntimeError("production player build failed; retain build-logs and build-transcript.txt")
            dump(out / "build-source.json", {"revision": revision, "project": str(ROOT / "games/hollowmere")})
            result["observation"] = "Build completed; graphical qualification not run"
        else:
            report = json.loads((out / "player/build-report.json").read_text())
            if report.get("revision") != revision or report.get("result") != "Succeeded" or report.get("scriptingBackend") != "IL2CPP" or report.get("platform") != "StandaloneLinux64" or report.get("development"):
                raise RuntimeError("build receipt is not current-revision release Linux IL2CPP")
            for executable in ("ffmpeg", "ffprobe", "xdpyinfo", "glxinfo", "flock", "timeout"):
                if not shutil.which(executable):
                    raise RuntimeError("missing prerequisite: " + executable)
            wrapper = out / "capture-launcher"
            wrapper.mkdir()
            shutil.copy2(out / "player/revision.txt", wrapper / "revision.txt")
            launcher = wrapper / "Hollowmere.x86_64"
            launcher.write_text("#!/bin/sh\nexec " + shlex.join([sys.executable, str(SELF), "capture"]) + ' "$@"\n')
            launcher.chmod(0o755)
            env.update(PROBE_RUNS="2", PROFILE="0", EVIDENCE_DIR=str(out), PLAYER_DIR=str(wrapper),
                       P42H_REAL_PLAYER=str(out / "player/Hollowmere.x86_64"))
            rc = logged(["bash", "-x", str(TOOLS / "measure_frames_p31d.sh")], out / "measurement-transcript.txt", env)
            result["runnerExit"] = rc
            folders = [out / "measurement" / f"vsync{v}" / f"run{r}" for v in (0, 1) for r in (1, 2)]
            if not all((p / "frame-stats.json").is_file() for p in folders):
                raise RuntimeError("runner did not produce four measurements; see measurement/BLOCKED.txt and measurement-transcript.txt")
            if logged([sys.executable, str(TOOLS / "qualify_p31d.py"), str(out / "measurement")], out / "qualification-transcript.txt") != 0:
                raise RuntimeError("production qualification failed; see qualification-transcript.txt")
            qualified = json.loads((out / "measurement/qualification.json").read_text())
            for profile, runs in qualified.items():
                for run in runs:
                    capture_result = json.loads((out / "measurement" / profile / f"run{run['run']}" / "capture-result.json").read_text())
                    run["recording"] = capture_result
                    run["checks"]["TenMinutePlaythrough"] = run["seconds"] >= 600
                    run["checks"]["Recording"] = capture_result["status"] == "PASS"
                    run["status"] = "PASS" if run["B_FRAME"] == "PASS" and all(run["checks"].values()) else "FAIL"
                result["profiles"][profile] = {"status": "PASS" if all(r["status"] == "PASS" for r in runs) else "FAIL", "runs": runs}
            result["observation"] = "Literal B-FRAME verdicts are separate; W-GAME-01 remains BLOCKED on open owner definition"
    except (Exception, KeyboardInterrupt) as error:
        result["failure"] = str(error) or type(error).__name__
    finally:
        # Preserve completed observations when another run blocks qualification.
        for vsync in (0, 1):
            profile = result["profiles"][f"vsync{vsync}"]
            if "runs" in profile:
                continue
            observations = []
            for run in (1, 2):
                folder = out / "measurement" / f"vsync{vsync}" / f"run{run}"
                observation = {"run": run, "status": "BLOCKED"}
                for filename, key in (("frame-stats.json", "frameStats"), ("capture-result.json", "recording")):
                    path = folder / filename
                    if path.is_file():
                        try:
                            observation[key] = json.loads(path.read_text())
                        except (OSError, ValueError) as error:
                            observation[key + "ReadError"] = str(error)
                observations.append(observation)
            profile["observations"] = observations
        dump(out / ("build-result.json" if args.action == "build" else "result.json"), result)
        manifest(out)
    print(json.dumps(result, indent=2))
    return 1 if "failure" in result or any(p["status"] == "FAIL" for p in result["profiles"].values()) else 0


if __name__ == "__main__":
    sys.exit(capture(sys.argv[2:]) if len(sys.argv) > 1 and sys.argv[1] == "capture" else main())
